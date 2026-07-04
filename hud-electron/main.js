// J.A.R.V.I.S. HUD (Electron) — telinha por sessao, cross-platform (usada no macOS).
// Espelha a versao nativa do Windows: janela sem moldura, transparente, sempre-no-topo,
// uma por sessao. Le hud-sessions/<sid>/ (meta/feed/progress/done/end/closed) escritos
// pelos hooks. Modos de QA: --shot <png> e --shot-shut <png> <p>.
const { app, BrowserWindow, ipcMain, screen } = require('electron');
const fs = require('fs');
const path = require('path');

const W = 380, H = 300, HB_STALE = 6000;
const ROOT = path.join(__dirname, '..', 'hud-sessions');

// argumentos (depois do caminho do app)
const argv = process.argv.slice(app.isPackaged ? 1 : 2);
let mode = 'run', sid = 'global', shotOut = null, shotP = 0.3;
if (argv[0] === '--shot') { mode = 'shot'; shotOut = argv[1]; }
else if (argv[0] === '--shot-shut') { mode = 'shotshut'; shotOut = argv[1]; shotP = parseFloat(argv[2] || '0.3'); }
else if (argv[0]) { sid = argv[0]; }

const cleanSid = String(sid).replace(/[^A-Za-z0-9_-]/g, '');
const sdir = path.join(ROOT, cleanSid);

function hbFresh() { try { return Date.now() - fs.statSync(path.join(sdir, 'hb')).mtimeMs < HB_STALE; } catch { return false; } }

app.disableHardwareAcceleration();                 // mais leve; evita problemas de GPU/transparencia
app.on('window-all-closed', () => app.quit());

app.whenReady().then(() => {
  if (mode === 'run' && hbFresh()) { app.quit(); return; }   // ja ha uma telinha viva p/ essa sessao

  const win = new BrowserWindow({
    width: W, height: H,
    frame: false, transparent: true, resizable: false, hasShadow: false, movable: true,
    alwaysOnTop: true, skipTaskbar: true, focusable: false, show: false, fullscreenable: false,
    webPreferences: { nodeIntegration: true, contextIsolation: false, backgroundThrottling: false }
  });
  win.setAlwaysOnTop(true, 'screen-saver');
  try { win.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); } catch (e) {}

  const q = 'sid=' + encodeURIComponent(cleanSid) + '&dir=' + encodeURIComponent(sdir) +
            '&mode=' + mode + '&p=' + shotP;
  win.loadFile(path.join(__dirname, 'index.html'), { search: q });

  if (mode === 'run') {
    positionWindow(win);
    win.once('ready-to-show', () => { try { win.showInactive(); } catch (e) { win.show(); } });
    ipcMain.on('hud-close', () => { try { win.close(); } catch (e) {} });
    ipcMain.on('hud-drag', (e, dx, dy) => { try { const p = win.getPosition(); win.setPosition(p[0] + dx, p[1] + dy); } catch (er) {} });
  } else {
    win.webContents.once('did-finish-load', async () => {
      await new Promise(r => setTimeout(r, mode === 'shotshut' ? 300 : 550));
      try { const img = await win.webContents.capturePage(); fs.writeFileSync(shotOut, img.toPNG()); } catch (e) {}
      app.quit();
    });
  }
});

// canto superior direito, empilhando por sessoes vivas (slot simples)
function positionWindow(win) {
  let slot = 0;
  try {
    const now = Date.now();
    for (const d of fs.readdirSync(ROOT)) {
      if (d === cleanSid) continue;
      try { if (now - fs.statSync(path.join(ROOT, d, 'hb')).mtimeMs < HB_STALE) slot++; } catch (e) {}
    }
  } catch (e) {}
  const area = screen.getPrimaryDisplay().workArea;
  const x = area.x + area.width - W - 12;
  const y = area.y + 12 + slot * (H + 10);
  try { win.setPosition(Math.round(x), Math.round(y)); } catch (e) {}
}
