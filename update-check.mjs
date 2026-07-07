// update-check.mjs — anunciador de atualização do J.A.R.V.I.S.
// Roda 1x/dia (destacado, disparado pelos hooks pelo jarvis-notify). Compara o
// VERSION local com o do GitHub (raw). Se houver versão nova AINDA NÃO anunciada,
// dispara `jarvis-notify.mjs update` (reusa a fila/voz/toast/daemon existente).
// Offline / sem novidade / já anunciado = silêncio. Zero rede no caminho do hook
// (só aqui, num processo separado que não bloqueia a sessão).
import { readFileSync, writeFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { spawn } from "child_process";

const __dir = dirname(fileURLToPath(import.meta.url));
const STATE = join(__dir, ".update-state.json");
const VERSION_FILE = join(__dir, "VERSION");
const REMOTE = "https://raw.githubusercontent.com/dafire144/claude-code-jarvis/main/VERSION";
const DAY = 24 * 60 * 60 * 1000;
const force = process.argv.includes("--force");

function log(msg) {
  try { writeFileSync(join(__dir, "jarvis.log"), `${new Date().toISOString()} update-check: ${msg}\n`, { flag: "a" }); } catch { /* ok */ }
}

let state = {};
try { state = JSON.parse(readFileSync(STATE, "utf8")); } catch { /* primeira vez */ }

// throttle diário (o --force fura, p/ teste)
if (!force && Date.now() - (state.lastCheckTs || 0) < DAY) process.exit(0);
state.lastCheckTs = Date.now();
try { writeFileSync(STATE, JSON.stringify(state)); } catch { /* ok */ }

let local = "";
try { local = readFileSync(VERSION_FILE, "utf8").trim().split(/\s+/)[0]; } catch { /* sem VERSION local */ }

let remote = "";
try {
  const ctrl = new AbortController();
  const to = setTimeout(() => ctrl.abort(), 8000);
  const res = await fetch(REMOTE, { signal: ctrl.signal, headers: { "Cache-Control": "no-cache" } });
  clearTimeout(to);
  if (res.ok) remote = (await res.text()).trim().split(/\s+/)[0];
} catch { process.exit(0); }   // offline / rede indisponível = silêncio
if (!remote) process.exit(0);

// só anuncia versão nova que ainda não foi anunciada (não repete a cada dia)
if (remote !== local && remote !== state.announcedVersion) {
  try {
    state.announcedVersion = remote;
    writeFileSync(STATE, JSON.stringify(state));
    log(`nova versao ${remote} (local ${local || "?"}) -> anunciando`);
    const child = spawn(process.execPath, [join(__dir, "jarvis-notify.mjs"), "update"], { detached: true, stdio: "ignore", windowsHide: true });
    child.unref();
  } catch { /* ok */ }
} else {
  log(`sem novidade (local ${local || "?"} remoto ${remote})`);
}
// SEM process.exit() aqui: sair na marra logo após um spawn destacado derruba o node
// no Windows (assert do libuv, visto 07/07). O loop esvazia e o processo termina só.
