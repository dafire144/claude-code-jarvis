// install.mjs — instalador de 1 comando do J.A.R.V.I.S. (v1.5.0)
// Liga os hooks desta pasta no seu ~/.claude/settings.json, com backup e sem
// destruir o que já existe lá. Rode DE DENTRO da pasta da instalação:
//
//   node install.mjs           -> instala (faz backup do settings.json antes)
//   node install.mjs --dry     -> só mostra o que faria, não escreve nada
//
// Idempotente: rodar de novo remove entradas antigas do Jarvis e recoloca as
// atuais (útil após mover a pasta ou atualizar). Hooks de OUTRAS ferramentas
// são preservados intocados.
import { existsSync, readFileSync, writeFileSync, mkdirSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { homedir } from "os";
import { spawnSync } from "child_process";

const __dir = dirname(fileURLToPath(import.meta.url));
const isWin = process.platform === "win32";
const DRY = process.argv.includes("--dry");
const sIdx = process.argv.indexOf("--settings");
const settingsPath = sIdx > -1 ? process.argv[sIdx + 1] : join(homedir(), ".claude", "settings.json");
const say = (m) => console.log(`[J.A.R.V.I.S.] ${m}`);

// 1) monta o bloco desta instalação a partir do settings.example.json
const dirSlash = __dir.replace(/\\/g, "/");   // barras normais, mesmo no Windows
const example = JSON.parse(readFileSync(join(__dir, "settings.example.json"), "utf8").replaceAll("__JARVIS_DIR__", dirSlash));

// 2) carrega (ou cria) o settings.json do usuário
let settings = {};
if (existsSync(settingsPath)) {
  try { settings = JSON.parse(readFileSync(settingsPath, "utf8")); }
  catch (e) { say(`ERRO: ${settingsPath} não é JSON válido (${e.message}). Corrija antes de instalar.`); process.exit(1); }
} else {
  say(`settings.json não existe ainda — será criado em ${settingsPath}`);
}

// 3) merge dos hooks: preserva tudo que não é do Jarvis; entradas antigas do
//    Jarvis (qualquer caminho) saem e as desta pasta entram — idempotente.
const isJarvisEntry = (entry) =>
  Array.isArray(entry?.hooks) && entry.hooks.some((h) => /jarvis-notify\.mjs|hud-(native|launch|close)\.mjs|jarvis[\\/]/i.test(String(h?.command || "")));

settings.hooks = settings.hooks || {};
let added = 0, replaced = 0;
for (const [event, entries] of Object.entries(example.hooks)) {
  const existing = Array.isArray(settings.hooks[event]) ? settings.hooks[event] : [];
  const kept = existing.filter((e) => !isJarvisEntry(e));
  replaced += existing.length - kept.length;
  settings.hooks[event] = [...kept, ...entries];
  added += entries.length;
}

// 4) status line (opcional): só entra se o usuário ainda não tem uma própria
let statusLineNote = "mantida a sua";
if (!settings.statusLine) { settings.statusLine = example.statusLine; statusLineNote = "instalada"; }
else if (JSON.stringify(settings.statusLine).includes("statusline.mjs")) { settings.statusLine = example.statusLine; statusLineNote = "atualizada"; }

say(`hooks: ${added} entradas do Jarvis em ${Object.keys(example.hooks).length} eventos${replaced ? ` (${replaced} entradas antigas do Jarvis substituídas)` : ""}`);
say(`status line: ${statusLineNote}`);

if (DRY) {
  say("--dry: nada foi escrito. O settings.json ficaria assim:");
  console.log(JSON.stringify(settings, null, 2));
  process.exit(0);
}

// 5) backup + escrita
try { mkdirSync(dirname(settingsPath), { recursive: true }); } catch { /* ok */ }
if (existsSync(settingsPath)) {
  const stamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
  const bak = `${settingsPath}.bak-jarvis-${stamp}`;
  writeFileSync(bak, readFileSync(settingsPath));
  say(`backup do settings.json: ${bak}`);
}
writeFileSync(settingsPath, JSON.stringify(settings, null, 2));
say(`hooks gravados em ${settingsPath} ✅`);

// 6) acabamento por plataforma
if (isWin) {
  say("instalando a identidade das notificações (toast)…");
  const r = spawnSync("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", join(__dir, "setup-toast.ps1")], { timeout: 90000, stdio: "ignore" });
  say(r.status === 0 ? "toast configurado ✅" : "setup-toast falhou (rode à mão: powershell -ExecutionPolicy Bypass -File setup-toast.ps1)");
} else if (process.platform === "darwin") {
  if (!existsSync(join(__dir, "hud-electron", "node_modules", ".bin", "electron"))) {
    say("instalando o Electron da telinha (npm install em hud-electron/)…");
    const r = spawnSync("npm", ["install"], { cwd: join(__dir, "hud-electron"), timeout: 300000, stdio: "inherit" });
    say(r.status === 0 ? "telinha do Mac pronta ✅" : "npm install falhou — rode à mão dentro de hud-electron/ quando puder (a voz funciona sem isso)");
  }
}

say("");
say("Pronto. REINICIE o Claude Code e mande um prompt — ele deve cumprimentá-lo.");
say("Conferência completa a qualquer momento: node doctor.mjs");
