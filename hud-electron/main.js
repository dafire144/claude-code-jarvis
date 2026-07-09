// J.A.R.V.I.S. HUD (Electron) — telinha por sessao, cross-platform (usada no macOS).
// Espelha a versao nativa do Windows: janela sem moldura, transparente, sempre-no-topo,
// uma por sessao, com IGNICAO cinematica na abertura e desligamento CRT no fim. Le
// hud-sessions/<sid>/ (meta/feed/progress/done/end/closed) escritos pelos hooks.
// LAYOUT multi-janela: mesmo protocolo .slots do nativo (HudLayout.cs) — as minimizadas
// estacionam no topo do canto direito e empilham uma sob a outra; as cheias ficam abaixo;
// recompacta 1x/s; janela arrastada sai do fluxo. "ABRIR MINIMIZADA" (opt-in) via env
// JARVIS_HUD_START_MINIMIZED=1 ou o arquivo start-minimized.flag.
// Modos de QA: --shot <png> [fable], --shot-mini <png> [fable], --shot-boot/-shut ...
const { app, BrowserWindow, ipcMain, screen } = require('electron');
const fs = require('fs');
const path = require('path');

const W = 380, H = 300, MINI_W = 182, MINI_H = 54, HB_STALE = 6000;
const ROOT = path.join(__dirname, '..', 'hud-sessions');
// coordenador de layout compartilhado — MESMO dir/protocolo do nativo (HudLayout.cs)
const SLOTS = path.join(__dirname, '..', 'hud-native', '.slots');
const GAP = 10, SLOT_STALE = 4000, SLOT_ORPHAN = 12000;
const FULLW = 380, COLGAP = 12, BOTTOMPAD = 6;   // dock SANFONA: passo de coluna + folga inferior (espelha HudLayout.cs)
// posicao do dock configuravel: le hud-native/hud-dock.cfg ("top=NN"/"right=NN"), default 42/12.
function loadDock() {
  let top = 42, margin = 12;
  try {
    for (const raw of fs.readFileSync(path.join(__dirname, '..', 'hud-native', 'hud-dock.cfg'), 'utf8').split(/\r?\n/)) {
      const line = raw.trim(); if (!line || line[0] === '#') continue;
      const eq = line.indexOf('='); if (eq <= 0) continue;
      const k = line.slice(0, eq).trim().toLowerCase(), n = parseInt(line.slice(eq + 1).trim(), 10);
      if (isNaN(n)) continue;
      if (k === 'top' || k === 'topgap') top = n;
      else if (k === 'right' || k === 'margin' || k === 'rightmargin') margin = n;
    }
  } catch (e) {}
  return { top: top, margin: margin };
}

// argumentos (depois do caminho do app)
const argv = process.argv.slice(app.isPackaged ? 1 : 2);
let mode = 'run', sid = 'global', shotOut = null, shotP = 0.3, shotFable = false, shotMini = false;
if (argv[0] === '--shot') { mode = 'shot'; shotOut = argv[1]; shotFable = argv[2] === 'fable'; }
else if (argv[0] === '--shot-mini') { mode = 'shot'; shotOut = argv[1]; shotMini = true; shotFable = argv[2] === 'fable'; }
else if (argv[0] === '--shot-shut') { mode = 'shotshut'; shotOut = argv[1]; shotP = parseFloat(argv[2] || '0.3'); }
else if (argv[0] === '--shot-boot') { mode = 'shotboot'; shotOut = argv[1]; shotP = parseFloat(argv[2] || '0.7'); }
else if (argv[0]) { sid = argv[0]; }

const cleanSid = String(sid).replace(/[^A-Za-z0-9_-]/g, '');
const sdir = path.join(ROOT, cleanSid);
const myPid = process.pid;
const bornMs = Date.now();          // "claim" desta janela (ordem de abertura)
let userMoved = false, miniState = false, lastPlaced = null;

function hbFresh() { try { return Date.now() - fs.statSync(path.join(sdir, 'hb')).mtimeMs < HB_STALE; } catch { return false; } }

// "abrir minimizada" (opt-in): env JARVIS_HUD_START_MINIMIZED (1/true/on; 0 forca desligado)
// OU o arquivo start-minimized.flag (compartilhado com o nativo em hud-native/, ou local).
function wantStartMinimized() {
  const v = (process.env.JARVIS_HUD_START_MINIMIZED || '').trim().toLowerCase();
  if (v === '1' || v === 'true' || v === 'yes' || v === 'on') return true;
  if (v === '0' || v === 'false' || v === 'no' || v === 'off') return false;
  try { if (fs.existsSync(path.join(__dirname, '..', 'hud-native', 'start-minimized.flag'))) return true; } catch (e) {}
  try { if (fs.existsSync(path.join(__dirname, 'start-minimized.flag'))) return true; } catch (e) {}
  return false;
}

// EMPACOTADOR PURO (espelha HudLayout.Pack do nativo): modelo SANFONA (ordem de abertura).
// Ordena por (claim, pid); empilha do topo pra baixo encostando na direita; quando a proxima
// nao cabe antes do fim da tela, comeca uma coluna NOVA a esquerda (passo = largura cheia).
// Determinista -> toda janela chega ao mesmo mapa -> zero vao/sobreposicao por desacordo.
function packLayout(wins, area, top, margin) {
  const items = wins.slice().sort((a, b) => (a.claim !== b.claim) ? (a.claim - b.claim) : (a.pid - b.pid));
  const rightEdge = area.x + area.width - margin;
  const topY = area.y + top;
  const bottom = area.y + area.height - BOTTOMPAD;
  const colPitch = FULLW + COLGAP;
  let col = 0, y = topY;
  const out = {};
  for (const s of items) {
    if (y !== topY && y + s.h > bottom) { col++; y = topY; }   // nao cabe -> proxima coluna (esquerda)
    let sx = rightEdge - col * colPitch - s.w;
    if (sx < area.x + 6) sx = area.x + 6;                       // ultima linha de defesa: nao sai da tela
    out[s.pid] = { x: Math.round(sx), y: Math.round(y) };
    y += s.h + GAP;
  }
  return out;
}
// le os slots vivos do disco (limpa orfaos, ignora mortos); inclui o meu com w/h atuais.
function liveWins(selfW, selfH) {
  const now = Date.now();
  const wins = [{ claim: bornMs, h: selfH, pid: myPid, w: selfW }];
  try {
    for (const f of fs.readdirSync(SLOTS)) {
      if (!f.endsWith('.slot')) continue;
      const opid = parseInt(f, 10);
      if (!opid || opid === myPid) continue;
      let parts;
      try { parts = fs.readFileSync(path.join(SLOTS, f), 'utf8').split('|'); } catch (e) { continue; }
      if (parts.length < 3) continue;
      const oclaim = parseInt(parts[0], 10), oh = parseInt(parts[1], 10), ohb = parseInt(parts[2], 10);
      if (isNaN(oclaim) || isNaN(oh) || isNaN(ohb)) continue;
      if (now - ohb > SLOT_ORPHAN) { try { fs.unlinkSync(path.join(SLOTS, f)); } catch (e) {} continue; }
      if (now - ohb > SLOT_STALE) continue;               // sem heartbeat: janela morta, nao ocupa espaco
      let ow = FULLW; if (parts.length >= 6) { const t = parseInt(parts[5], 10); if (!isNaN(t)) ow = t; }   // slot antigo -> assume cheia
      wins.push({ claim: oclaim, h: oh, pid: opid, w: ow });
    }
  } catch (e) {}
  return wins;
}
function place(mini) {
  try { fs.mkdirSync(SLOTS, { recursive: true }); } catch (e) {}
  const now = Date.now();
  const w = mini ? MINI_W : W, h = mini ? MINI_H : H;
  const dock = loadDock();
  const area = screen.getPrimaryDisplay().workArea;
  const map = packLayout(liveWins(w, h), area, dock.top, dock.margin);
  const pos = map[myPid] || { x: Math.round(area.x + area.width - w - dock.margin), y: Math.round(area.y + dock.top) };
  try { fs.writeFileSync(path.join(SLOTS, myPid + '.slot'), bornMs + '|' + h + '|' + now + '|' + pos.x + '|' + pos.y + '|' + w + '|' + (mini ? '1' : '0')); } catch (e) {}
  return { x: pos.x, y: pos.y, w: w, h: h };
}
function releaseSlot() { try { fs.unlinkSync(path.join(SLOTS, myPid + '.slot')); } catch (e) {} }
function applyBounds(win, x, y, w, h) {
  lastPlaced = { x: Math.round(x), y: Math.round(y) };
  try { win.setBounds({ x: lastPlaced.x, y: lastPlaced.y, width: w, height: h }); } catch (e) {}
}
function redock(win) {
  if (userMoved) return;
  const p = place(miniState);
  let b = null; try { b = win.getBounds(); } catch (e) {}
  if (!b || b.x !== p.x || b.y !== p.y || b.width !== p.w || b.height !== p.h) applyBounds(win, p.x, p.y, p.w, p.h);
}

app.disableHardwareAcceleration();                 // mais leve; evita problemas de GPU/transparencia
app.on('window-all-closed', () => app.quit());

app.whenReady().then(() => {
  if (mode === 'run' && hbFresh()) { app.quit(); return; }   // ja ha uma telinha viva p/ essa sessao

  const startMin = (mode === 'run') && wantStartMinimized();
  miniState = startMin;
  const openMini = shotMini || startMin;

  const win = new BrowserWindow({
    width: openMini ? MINI_W : W, height: openMini ? MINI_H : H,
    frame: false, transparent: true, resizable: false, hasShadow: false, movable: true,
    alwaysOnTop: true, skipTaskbar: true, focusable: false, show: false, fullscreenable: false,
    webPreferences: { nodeIntegration: true, contextIsolation: false, backgroundThrottling: false }
  });
  win.setAlwaysOnTop(true, 'screen-saver');
  try { win.setVisibleOnAllWorkspaces(true, { visibleOnFullScreen: true }); } catch (e) {}

  const q = 'sid=' + encodeURIComponent(cleanSid) + '&dir=' + encodeURIComponent(sdir) +
            '&mode=' + mode + '&p=' + shotP + (shotFable ? '&fable=1' : '') + (openMini ? '&mini=1' : '');
  win.loadFile(path.join(__dirname, 'index.html'), { search: q });

  if (mode === 'run') {
    const p0 = place(miniState);
    applyBounds(win, p0.x, p0.y, p0.w, p0.h);
    win.once('ready-to-show', () => { try { win.showInactive(); } catch (e) { win.show(); } });

    // usuario arrastou (o -webkit-app-region:drag move a janela pelo SO) -> sai do auto-layout
    win.on('move', () => {
      if (userMoved) return;
      try { const b = win.getBounds(); if (!lastPlaced || Math.abs(b.x - lastPlaced.x) > 3 || Math.abs(b.y - lastPlaced.y) > 3) { userMoved = true; releaseSlot(); } } catch (e) {}
    });
    // recompacta 1x/s (dock + empilhamento) e mantem o heartbeat do slot fresco
    const relayout = setInterval(() => { if (!userMoved) redock(win); }, 150);
    win.on('closed', () => { try { clearInterval(relayout); } catch (e) {} releaseSlot(); });

    ipcMain.on('hud-close', () => { try { win.close(); } catch (e) {} });
    ipcMain.on('hud-drag', (e, dx, dy) => { try { const pp = win.getPosition(); userMoved = true; releaseSlot(); win.setPosition(pp[0] + dx, pp[1] + dy); lastPlaced = { x: pp[0] + dx, y: pp[1] + dy }; } catch (er) {} });
    // minimizar/restaurar: MINIMIZAR rejunta ao auto-layout e estaciona no dock (mesmo se arrastada)
    ipcMain.on('hud-min', (e, mini) => {
      miniState = !!mini;
      if (mini) userMoved = false;
      if (!userMoved) redock(win);
      else {
        try { const b = win.getBounds(), right = b.x + b.width, top = b.y, w2 = miniState ? MINI_W : W, h2 = miniState ? MINI_H : H; win.setBounds({ x: right - w2, y: top, width: w2, height: h2 }); lastPlaced = { x: right - w2, y: top }; } catch (er) {}
      }
    });
  } else {
    win.webContents.once('did-finish-load', async () => {
      await new Promise(r => setTimeout(r, mode === 'shotshut' ? 300 : 550));
      try { const img = await win.webContents.capturePage(); fs.writeFileSync(shotOut, img.toPNG()); } catch (e) {}
      app.quit();
    });
  }
});
