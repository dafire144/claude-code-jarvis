// Player do Jarvis no macOS (Node): drena a fila de falas e toca 1 por vez com `afplay`,
// espelhando cada fala como notificacao nativa via `osascript`. E o equivalente do
// player-daemon.ps1 do Windows. Lancado destacado pelo jarvis-notify.mjs.
// Instancia unica via lockfile com heartbeat (evita duas falas ao mesmo tempo).
//
// ⚠️ Escrito com cuidado mas NAO testado num Mac de verdade (o autor esta no Windows).
//    Se algo nao tocar, cheque: `afplay` e `osascript` no PATH; permissao de Notificacoes.
import { readdirSync, readFileSync, writeFileSync, existsSync, statSync, unlinkSync, appendFileSync } from "fs";
import { join } from "path";
import { spawnSync } from "child_process";

const DIR = process.argv[2] || process.cwd();
const QUEUE = join(DIR, "queue");
const LOCK = join(QUEUE, ".mac-player.lock");
const BLIP = join(DIR, "assets", "robot-blip.wav");
const LAST_SESSION = join(DIR, ".last-session");
const LAST_END = join(DIR, ".last-sessionend");
const GAP_MS = 1000, GRACE_MS = 4000, MAX_AGE_MS = 60000;
const HOLD_START_MS = 6000, DEDUPE_MS = 10000;

const now = () => Date.now();
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function lockFresh() { try { return now() - statSync(LOCK).mtimeMs < 8000; } catch { return false; } }
if (lockFresh()) process.exit(0);                       // ja ha um player drenando -> sai
function beat() { try { writeFileSync(LOCK, String(process.pid)); } catch { /* ok */ } }
beat();

function afplay(file) { try { spawnSync("afplay", [file], { timeout: 20000, stdio: "ignore" }); } catch { /* ok */ } }
function notify(text, title) {
  if (!text) return;
  const clean = (s) => String(s || "").replace(/["\\]/g, "").replace(/[\r\n\t]/g, " ");
  const t = clean(text), tt = clean(title);
  const script = tt
    ? `display notification "${t}" with title "J.A.R.V.I.S." subtitle "${tt}"`
    : `display notification "${t}" with title "J.A.R.V.I.S."`;
  try { spawnSync("osascript", ["-e", script], { timeout: 8000, stdio: "ignore" }); } catch { /* ok */ }
}

let lastSession = "";
try { lastSession = readFileSync(LAST_SESSION, "utf8").replace(/﻿/g, "").trim(); } catch { /* 1a vez */ }
const lastCatPlay = {};

async function main() {
  let idle = 0;
  while (true) {
    beat();
    let items = [];
    try { items = readdirSync(QUEUE).filter((f) => f.endsWith(".json")).sort(); } catch { /* ok */ }
    if (!items.length) {
      if (idle >= GRACE_MS) break;                       // fila vazia -> sai apos a folga
      await sleep(500); idle += 500; continue;
    }
    idle = 0;
    const it = join(QUEUE, items[0]);
    let data = null;
    try { data = JSON.parse(readFileSync(it, "utf8")); } catch { /* ok */ }
    if (!data) { try { unlinkSync(it); } catch { /* ok */ } continue; }
    const cat = String(data.cat || "");

    // boas-vindas: segura 6s antes de tocar (cancela se o app estiver fechando)
    if (cat === "sessionstart" && data.ts && now() - Number(data.ts) < HOLD_START_MS) { await sleep(500); continue; }
    try { unlinkSync(it); } catch { /* ok */ }
    if (!data.file || !existsSync(data.file)) continue;
    if (data.ts && now() - Number(data.ts) > MAX_AGE_MS) continue;          // fala velha demais

    if (cat === "sessionstart") {                                          // fechamento do app -> cancela boas-vindas
      let lastEnd = 0; try { lastEnd = Number(String(readFileSync(LAST_END, "utf8")).replace(/[^0-9.]/g, "")); } catch { /* ok */ }
      if (lastEnd > Number(data.ts) - 2000) continue;
    }
    if ((cat === "sessionstart" || cat === "sessionend") && lastCatPlay[cat] && now() - lastCatPlay[cat] < DEDUPE_MS) continue;

    const sess = String(data.session || "");
    if (sess) { lastSession = sess; try { writeFileSync(LAST_SESSION, sess); } catch { /* ok */ } }
    if (cat) lastCatPlay[cat] = now();

    notify(data.text, data.title);                                         // notificacao nativa (espelha a voz)
    if (data.text && data.session) {                                       // feed p/ o HUD (Electron, fase 2)
      try {
        const sid = String(data.session).replace(/[^A-Za-z0-9_-]/g, "");
        const sdir = join(DIR, "hud-sessions", sid);
        if (existsSync(sdir)) appendFileSync(join(sdir, "feed.txt"), `${Number(data.ts)}\tJVS\t${String(data.text).replace(/[\t\r\n]/g, " ")}\n`);
      } catch { /* ok */ }
    }
    if (existsSync(BLIP)) afplay(BLIP);                                     // blip robotico (lead-in)
    if (data.prefix && existsSync(data.prefix)) afplay(data.prefix);
    afplay(data.file);                                                     // a fala
    await sleep(GAP_MS);
  }
  try { unlinkSync(LOCK); } catch { /* ok */ }
}
main();
