// doctor.mjs — autodiagnóstico do J.A.R.V.I.S. (v1.5.0)
// Varre a instalação inteira e reporta o que está saudável, o que está torto e
// COMO consertar. Pensado pra quem instalou e "não ouve nada": rode isto antes
// de abrir um issue — na maioria das vezes a causa aparece aqui.
//
// Uso: node doctor.mjs           -> relatório legível
//      node doctor.mjs --json    -> saída em JSON (pro Claude Code do usuário ler)
//      node doctor.mjs --fix     -> além de reportar, conserta o que é seguro
//                                   (estado corrompido, fila velha, toast do Windows)
// Código de saída = número de FALHAS (0 = instalação saudável).
import { readdirSync, existsSync, readFileSync, writeFileSync, rmSync, statSync, mkdirSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { homedir } from "os";
import { spawnSync } from "child_process";
import { LINES } from "./lines.mjs";

const __dir = dirname(fileURLToPath(import.meta.url));
const isWin = process.platform === "win32";
const isMac = process.platform === "darwin";
const JSON_MODE = process.argv.includes("--json");
const FIX = process.argv.includes("--fix");
const REMOTE_VERSION = "https://raw.githubusercontent.com/dafire144/claude-code-jarvis/main/VERSION";

const checks = [];   // { level: ok|warn|fail|info, name, msg, fix? }
const add = (level, name, msg, fix) => checks.push({ level, name, msg, ...(fix ? { fix } : {}) });

// ---------- 1) ambiente ----------
const nodeMajor = Number(process.versions.node.split(".")[0]);
if (nodeMajor >= 18) add("ok", "node", `Node.js ${process.versions.node}`);
else add("fail", "node", `Node.js ${process.versions.node} é antigo demais (preciso de fetch nativo)`, "instale Node 18 ou mais novo");

let version = "?";
try { version = readFileSync(join(__dir, "VERSION"), "utf8").trim(); add("ok", "versao", `instalação v${version} em ${__dir}`); }
catch { add("warn", "versao", "arquivo VERSION ausente", "rode node update.mjs pra restaurar"); }

// ---------- 2) settings.json + hooks ----------
const settingsPath = process.env.JARVIS_SETTINGS || join(homedir(), ".claude", "settings.json");
let settingsRaw = "", settings = null;
if (!existsSync(settingsPath)) {
  add("fail", "settings", `não encontrei ${settingsPath}`, "rode node install.mjs pra ligar os hooks");
} else {
  try { settingsRaw = readFileSync(settingsPath, "utf8"); settings = JSON.parse(settingsRaw); } catch (e) {
    add("fail", "settings", `settings.json não é JSON válido (${e.message})`, "corrija o arquivo à mão (há um backup .bak-jarvis* se o instalador rodou)");
  }
}
if (settings) {
  if (settingsRaw.includes("__JARVIS_DIR__")) {
    add("fail", "hooks", "o placeholder __JARVIS_DIR__ ainda está no settings.json (os hooks apontam pro nada)", "troque pelo caminho absoluto desta pasta, ou rode node install.mjs");
  }
  // todos os comandos de hook, achatados
  const cmds = [];
  const walk = (v) => {
    if (Array.isArray(v)) v.forEach(walk);
    else if (v && typeof v === "object") { if (typeof v.command === "string") cmds.push(v.command); Object.values(v).forEach(walk); }
  };
  walk(settings.hooks || {});
  const norm = (s) => String(s).replace(/\\/g, "/").toLowerCase();
  const dirNorm = norm(__dir);
  const mine = cmds.filter((c) => norm(c).includes(dirNorm));
  const jarvisish = cmds.filter((c) => /jarvis-notify\.mjs|hud-native\.mjs|hud-launch\.mjs/i.test(c));
  if (!mine.length && jarvisish.length) {
    add("warn", "hooks", "há hooks do Jarvis no settings.json, mas apontando pra OUTRA pasta (instalação duplicada?)", "confira o caminho nos comandos ou rode node install.mjs daqui");
  } else if (!mine.length) {
    add("fail", "hooks", "nenhum hook aponta pra esta instalação — o Jarvis está desligado", "rode node install.mjs (ou copie o bloco hooks de settings.example.json)");
  } else {
    // eventos essenciais ligados?
    const events = ["Stop", "Notification", "UserPromptSubmit", "PreToolUse", "SessionStart", "SessionEnd", "PreCompact", "SubagentStop"];
    const missing = events.filter((ev) => {
      const local = []; const w2 = (v) => { if (Array.isArray(v)) v.forEach(w2); else if (v && typeof v === "object") { if (typeof v.command === "string") local.push(v.command); Object.values(v).forEach(w2); } };
      w2((settings.hooks || {})[ev] || []);
      return !local.some((c) => norm(c).includes(dirNorm));
    });
    if (missing.length) add("warn", "hooks", `hooks ligados, mas sem os eventos: ${missing.join(", ")}`, "compare com settings.example.json (cada evento é independente)");
    else add("ok", "hooks", `hooks ligados nesta instalação (${mine.length} comandos, todos os eventos)`);
  }
  if (settings.statusLine && norm(JSON.stringify(settings.statusLine)).includes(dirNorm)) add("ok", "statusline", "status line do Jarvis configurada (detecção de modelo instantânea)");
  else add("info", "statusline", "status line não configurada (opcional; sem ela a detecção de modelo usa o transcript)");
}

// ---------- 3) clipes de voz vs lines.mjs ----------
let clipFiles = [];
try { clipFiles = readdirSync(join(__dir, "clips")).filter((f) => f.endsWith(".mp3")); } catch { /* sem pasta */ }
if (!clipFiles.length) {
  add("fail", "clipes", "pasta clips/ vazia ou ausente — sem áudio não há voz", "rode node update.mjs (os clipes vêm no pacote)");
} else {
  const missing = [];
  let total = 0;
  for (const [cat, arr] of Object.entries(LINES)) {
    for (let i = 0; i < arr.length; i++) { total++; if (!clipFiles.includes(`${cat}-${i + 1}.mp3`)) missing.push(`${cat}-${i + 1}`); }
  }
  const known = new Set();
  for (const [cat, arr] of Object.entries(LINES)) for (let i = 0; i < arr.length; i++) known.add(`${cat}-${i + 1}.mp3`);
  const orphans = clipFiles.filter((f) => /^[a-z_]+-\d+\.mp3$/.test(f) && !known.has(f));
  if (missing.length) add("warn", "clipes", `${missing.length} de ${total} falas sem clipe (ficam mudas): ${missing.slice(0, 8).join(", ")}${missing.length > 8 ? "…" : ""}`, "rode node update.mjs pra baixar os clipes que faltam");
  else add("ok", "clipes", `${clipFiles.length} clipes no disco cobrindo as ${total} falas de ${Object.keys(LINES).length} categorias`);
  if (orphans.length) add("info", "clipes", `${orphans.length} clipes órfãos (sem fala correspondente — inofensivo)`);
}

// ---------- 4) estado local (JSONs) ----------
for (const f of [".cooldowns.json", ".titles.json", ".last-line.json", ".update-state.json", ".mute"]) {
  const p = join(__dir, f);
  if (!existsSync(p)) continue;
  try { JSON.parse(readFileSync(p, "utf8")); } catch {
    if (FIX) { try { rmSync(p); add("ok", "estado", `${f} estava corrompido — apagado (se recria sozinho)`); } catch { add("warn", "estado", `${f} corrompido e não consegui apagar`, `apague ${f} à mão`); } }
    else add("warn", "estado", `${f} corrompido (o Jarvis ignora, mas convém limpar)`, `apague o arquivo (ou rode node doctor.mjs --fix)`);
  }
}

// ---------- 5) fila ----------
const QUEUE = join(__dir, "queue");
try {
  if (!existsSync(QUEUE)) mkdirSync(QUEUE, { recursive: true });   // nasce vazia numa instalação nova
  const probe = join(QUEUE, `.doctor-${process.pid}`);
  writeFileSync(probe, "1"); rmSync(probe);
  const stale = (existsSync(QUEUE) ? readdirSync(QUEUE) : []).filter((f) => {
    try { return f.endsWith(".json") && Date.now() - statSync(join(QUEUE, f)).mtimeMs > 5 * 60 * 1000; } catch { return false; }
  });
  if (stale.length) {
    if (FIX) { let n = 0; for (const f of stale) { try { rmSync(join(QUEUE, f)); n++; } catch { /* ok */ } } add("ok", "fila", `${n} itens velhos purgados da fila`); }
    else add("warn", "fila", `${stale.length} itens parados há >5min na fila (daemon não drenou?)`, "rode node doctor.mjs --fix pra purgar; se persistir, veja jarvis.log");
  } else add("ok", "fila", "fila de áudio gravável e sem acúmulo");
} catch { add("fail", "fila", "não consigo escrever na pasta queue/", "confira as permissões da pasta"); }

// ---------- 6) player / última fala ----------
try {
  const log = readFileSync(join(__dir, "jarvis.log"), "utf8");
  const lines = log.trim().split("\n");
  const lastQueue = [...lines].reverse().find((l) => l.includes("enfileirado:"));
  if (lastQueue) {
    const ts = Date.parse(lastQueue.slice(0, 24));
    const ago = ts ? Math.round((Date.now() - ts) / 60000) : -1;
    add("info", "voz", `última fala enfileirada ${ago >= 0 ? `há ${ago} min` : "(data ilegível)"} — ${lastQueue.split("enfileirado:")[1].trim().slice(0, 60)}`);
  } else add("info", "voz", "nenhuma fala registrada no jarvis.log ainda (instalação nova?)");
} catch { add("info", "voz", "sem jarvis.log ainda (nenhum hook disparou — reinicie o Claude Code após instalar)"); }

// ---------- 7) HUD ----------
if (isWin) {
  if (existsSync(join(__dir, "hud-native", "jarvis-hud-wf.exe"))) add("ok", "hud", "telinha nativa presente (hud-native/jarvis-hud-wf.exe)");
  else add("fail", "hud", "jarvis-hud-wf.exe ausente — a telinha não abre", "rode node update.mjs; ou recompile (comando no CLAUDE.md)");
  // toast com identidade instalada? (AUMID lido do próprio toast.ps1 — fonte da verdade)
  let aumid = "Jarvis.ClaudeCode";
  try { aumid = (readFileSync(join(__dir, "toast.ps1"), "utf8").match(/JarvisAumid\s*=\s*"([^"]+)"/) || [])[1] || aumid; } catch { /* usa o padrão */ }
  const reg = spawnSync("reg", ["query", `HKCU\\Software\\Classes\\AppUserModelId\\${aumid}`], { timeout: 5000 });
  if (reg.status === 0) add("ok", "toast", `identidade de notificação instalada (${aumid})`);
  else if (FIX) {
    const r = spawnSync("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", join(__dir, "setup-toast.ps1")], { timeout: 60000 });
    add(r.status === 0 ? "ok" : "warn", "toast", r.status === 0 ? "identidade de notificação instalada agora" : "setup-toast.ps1 falhou", r.status === 0 ? undefined : "rode powershell -ExecutionPolicy Bypass -File setup-toast.ps1");
  } else add("warn", "toast", "identidade de notificação não instalada (toasts saem sem marca/ícone)", "rode powershell -ExecutionPolicy Bypass -File setup-toast.ps1 (ou node doctor.mjs --fix)");
} else if (isMac) {
  if (existsSync(join(__dir, "hud-electron", "node_modules", ".bin", "electron"))) add("ok", "hud", "Electron instalado (telinha do Mac pronta)");
  else add("warn", "hud", "Electron não instalado — a telinha do Mac não abre", "rode npm install dentro de hud-electron/");
}

// ---------- 8) silêncio ----------
try {
  const m = JSON.parse(readFileSync(join(__dir, ".mute"), "utf8"));
  if (m.until === 0) add("info", "silencio", "PROTOCOLO SILÊNCIO ativo até segunda ordem (por isso não há voz!)");
  else if (m.until > Date.now()) add("info", "silencio", `PROTOCOLO SILÊNCIO ativo por mais ${Math.ceil((m.until - Date.now()) / 60000)} min (por isso não há voz!)`);
} catch { /* sem mute = normal */ }
if (process.env.JARVIS_QUIET || existsSync(join(__dir, "quiet.cfg"))) add("info", "silencio", `horário de silêncio configurado (${process.env.JARVIS_QUIET || "quiet.cfg"})`);

// ---------- 9) versão remota (rede, tolerante a offline) ----------
try {
  const ctrl = new AbortController();
  const to = setTimeout(() => ctrl.abort(), 4000);
  const res = await fetch(REMOTE_VERSION, { signal: ctrl.signal, headers: { "Cache-Control": "no-cache" } });
  clearTimeout(to);
  if (res.ok) {
    const remote = (await res.text()).trim().split(/\s+/)[0];
    // só avisa se o remoto é realmente MAIS NOVO (local adiantado = dev, tudo bem)
    const newer = (a, b) => { const pa = a.split(".").map(Number), pb = b.split(".").map(Number); for (let i = 0; i < 3; i++) { if ((pa[i] || 0) !== (pb[i] || 0)) return (pa[i] || 0) > (pb[i] || 0); } return false; };
    if (remote && newer(remote, version)) add("warn", "update", `versão ${remote} disponível (local: ${version})`, "rode node update.mjs");
    else add("ok", "update", `na versão mais recente (${version})`);
  }
} catch { add("info", "update", "sem rede pra checar versão (tudo bem, o Jarvis checa sozinho 1x/dia)"); }

// ---------- veredito ----------
const errors = checks.filter((c) => c.level === "fail").length;
const warns = checks.filter((c) => c.level === "warn").length;
try { writeFileSync(join(__dir, ".last-doctor.json"), JSON.stringify({ ts: Date.now(), errors, warns, version })); } catch { /* ok */ }

if (JSON_MODE) {
  console.log(JSON.stringify({ version, platform: process.platform, errors, warns, checks }, null, 2));
} else {
  const ICO = { ok: "  ✓", warn: "  !", fail: "  ✗", info: "  ·" };
  console.log(`\n[J.A.R.V.I.S.] Diagnóstico da instalação — v${version} (${process.platform})\n`);
  for (const c of checks) {
    console.log(`${ICO[c.level]} ${c.name.padEnd(10)} ${c.msg}`);
    if (c.fix && c.level !== "ok") console.log(`               ↳ conserto: ${c.fix}`);
  }
  console.log(`\n${errors === 0 && warns === 0 ? "Todos os sistemas em ordem, senhor." : `${errors} falha(s), ${warns} aviso(s).${errors || warns ? " Consertos sugeridos acima." : ""}`}\n`);
}
// SEM process.exit(): sair na marra depois de fetch/spawn derruba o node no Windows
// (assert do libuv em async.c, lição de 07/07). O loop esvazia e o processo termina só.
process.exitCode = errors;
