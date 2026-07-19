// jarvis.mjs — o cockpit do J.A.R.V.I.S. (v1.5.0)
// Porta de entrada de linha de comando: estado geral, diagnóstico, silêncio,
// teste de voz e atualização, tudo num lugar só.
//
//   node jarvis.mjs                 -> painel de estado
//   node jarvis.mjs doctor [--fix]  -> autodiagnóstico (com veredito falado)
//   node jarvis.mjs mute [30m|2h|sempre] -> Protocolo Silêncio (padrão: 1h)
//   node jarvis.mjs unmute          -> voz de volta (com confirmação falada)
//   node jarvis.mjs quiet 22-07     -> horário de silêncio diário (quiet off desliga)
//   node jarvis.mjs test [categoria]-> toca uma fala da categoria (padrão: stop)
//   node jarvis.mjs lines [cat]     -> lista categorias/falas
//   node jarvis.mjs update          -> atualiza a instalação (update.mjs)
import { readdirSync, existsSync, readFileSync, writeFileSync, rmSync, statSync, mkdirSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { spawnSync, spawn } from "child_process";
import { LINES } from "./lines.mjs";

const __dir = dirname(fileURLToPath(import.meta.url));
const isWin = process.platform === "win32";
const MUTE_FILE = join(__dir, ".mute");
const QUIET_CFG = join(__dir, "quiet.cfg");
const say = (m) => console.log(`[J.A.R.V.I.S.] ${m}`);
const cmd = (process.argv[2] || "status").toLowerCase();
const arg = process.argv[3] || "";

// ---------- fala por fora dos hooks: enfileira o clipe e acorda o player ----------
function enqueue(cat) {
  let pool = [];
  try { pool = readdirSync(join(__dir, "clips")).filter((f) => new RegExp(`^${cat}-\\d+\\.mp3$`).test(f)); } catch { /* ok */ }
  if (!pool.length) return false;
  const pick = pool[Math.floor(Math.random() * pool.length)];
  const idx = Number((pick.match(/-(\d+)\.mp3$/) || [])[1]) - 1;
  const text = (LINES[cat] && LINES[cat][idx]) || "";
  const QUEUE = join(__dir, "queue");
  try { if (!existsSync(QUEUE)) mkdirSync(QUEUE, { recursive: true }); } catch { /* ok */ }
  const item = { file: join(__dir, "clips", pick), prefix: "", session: "cli", cat, ts: Date.now(), text, title: "CLI" };
  try { writeFileSync(join(QUEUE, `${Date.now()}-${process.pid}-${Math.floor(Math.random() * 1e6)}.json`), JSON.stringify(item)); } catch { return false; }
  if (process.platform === "darwin") {
    try { const c = spawn(process.execPath, [join(__dir, "mac-player.mjs"), __dir], { detached: true, stdio: "ignore" }); c.unref(); } catch { /* ok */ }
  } else if (isWin) {
    const daemonCmd = `"powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "${join(__dir, "player-daemon.ps1")}" -Dir "${__dir}"`;
    const wmi = [
      `$si = ([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
      `$si.ShowWindow = 0;`,
      `$r = ([wmiclass]'Win32_Process').Create('${daemonCmd.replace(/'/g, "''")}', $null, $si);`,
      `exit $r.ReturnValue`,
    ].join(" ");
    spawnSync("powershell", ["-NoProfile", "-Command", wmi], { timeout: 15000, stdio: "ignore" });
  }
  return text || pick;
}

function muteInfo() {
  try {
    const m = JSON.parse(readFileSync(MUTE_FILE, "utf8"));
    if (m.until === 0) return "ATIVO até segunda ordem";
    if (m.until > Date.now()) return `ATIVO por mais ${Math.ceil((m.until - Date.now()) / 60000)} min`;
  } catch { /* sem mute */ }
  return "";
}

// ---------- comandos ----------
if (cmd === "status") {
  let version = "?"; try { version = readFileSync(join(__dir, "VERSION"), "utf8").trim(); } catch { /* ok */ }
  let clips = 0; try { clips = readdirSync(join(__dir, "clips")).filter((f) => f.endsWith(".mp3")).length; } catch { /* ok */ }
  const cats = Object.keys(LINES).length;
  const total = Object.values(LINES).reduce((a, b) => a + b.length, 0);
  let queueN = 0; try { queueN = readdirSync(join(__dir, "queue")).filter((f) => f.endsWith(".json")).length; } catch { /* ok */ }
  let sessions = 0;
  try {
    for (const d of readdirSync(join(__dir, "hud-sessions"))) {
      const sd = join(__dir, "hud-sessions", d);
      if (existsSync(join(sd, "end")) || existsSync(join(sd, "closed"))) continue;
      let fresh = 0;
      for (const f of ["burst.txt", "feed.txt", "hb"]) { try { const m = statSync(join(sd, f)).mtimeMs; if (m > fresh) fresh = m; } catch { /* ok */ } }
      if (fresh && Date.now() - fresh < 10 * 60 * 1000) sessions++;
    }
  } catch { /* ok */ }
  let lastSpoken = "";
  try {
    const log = readFileSync(join(__dir, "jarvis.log"), "utf8").trim().split("\n");
    const l = [...log].reverse().find((x) => x.includes("enfileirado:"));
    if (l) lastSpoken = l.split("enfileirado:")[1].trim().split(" ")[0];
  } catch { /* ok */ }
  let updateNote = "";
  try {
    const st = JSON.parse(readFileSync(join(__dir, ".update-state.json"), "utf8"));
    if (st.announcedVersion && st.announcedVersion !== version) updateNote = `v${st.announcedVersion} disponível — node update.mjs`;
  } catch { /* ok */ }
  const mute = muteInfo();
  let quiet = ""; try { quiet = readFileSync(QUIET_CFG, "utf8").replace(/\s+/g, " ").trim(); } catch { /* ok */ }

  console.log(`\n  ◈ J.A.R.V.I.S.  v${version}  (${process.platform})`);
  console.log(`  ─────────────────────────────────────────────`);
  console.log(`  instalação     ${__dir}`);
  console.log(`  voz            ${mute ? `🔇 Protocolo Silêncio ${mute}` : "ativa"}${quiet ? `  ·  horário de silêncio: ${quiet}` : ""}`);
  console.log(`  biblioteca     ${clips} clipes · ${total} falas · ${cats} categorias`);
  console.log(`  sessões vivas  ${sessions}${queueN ? `  ·  fila de áudio: ${queueN}` : ""}`);
  if (lastSpoken) console.log(`  última fala    ${lastSpoken}`);
  if (updateNote) console.log(`  atualização    ${updateNote}`);
  console.log(`\n  comandos: doctor · mute [30m|2h|sempre] · unmute · quiet 22-07 · test [cat] · lines · update\n`);
} else if (cmd === "doctor") {
  const r = spawnSync(process.execPath, [join(__dir, "doctor.mjs"), ...process.argv.slice(3)], { stdio: "inherit" });
  if (!process.argv.includes("--json")) {
    try {
      const d = JSON.parse(readFileSync(join(__dir, ".last-doctor.json"), "utf8"));
      enqueue(d.errors === 0 && d.warns === 0 ? "diag_ok" : "diag_bad");   // veredito falado
    } catch { /* ok */ }
  }
  process.exit(r.status || 0);
} else if (cmd === "mute") {
  let until = Date.now() + 60 * 60 * 1000;   // padrão: 1 hora
  const m = arg.match(/^(\d+)\s*(h|m|min)?$/i);
  if (/^(sempre|forever|indefinido|0)$/i.test(arg)) until = 0;
  else if (m) until = Date.now() + Number(m[1]) * (/h/i.test(m[2] || "m") ? 3_600_000 : 60_000);
  writeFileSync(MUTE_FILE, JSON.stringify({ until, by: "cli", ts: Date.now() }));
  say(until === 0 ? "Protocolo Silêncio ativado até segunda ordem." : `Protocolo Silêncio ativado por ${Math.round((until - Date.now()) / 60000)} min.`);
  say("Alertas críticos (reservas) continuam falando. Voz de volta: node jarvis.mjs unmute");
} else if (cmd === "unmute") {
  const was = muteInfo();
  try { rmSync(MUTE_FILE); } catch { /* ok */ }
  say(was ? "Protocolo Silêncio encerrado." : "A voz já estava ativa.");
  if (was) enqueue("mute_off");
} else if (cmd === "quiet") {
  if (/^off$/i.test(arg)) { try { rmSync(QUIET_CFG); } catch { /* ok */ } say("Horário de silêncio desligado."); }
  else {
    const m = arg.match(/^(\d{1,2})-(\d{1,2})$/);
    if (!m) { say("Uso: node jarvis.mjs quiet 22-07   (silêncio das 22h às 7h)  |  quiet off"); process.exit(1); }
    writeFileSync(QUIET_CFG, `start=${m[1]}\nend=${m[2]}\n`);
    say(`Horário de silêncio: das ${m[1]}h às ${m[2]}h, todos os dias. (Alertas críticos continuam.)`);
  }
} else if (cmd === "test") {
  const cat = arg || "stop";
  if (!LINES[cat]) { say(`Categoria "${cat}" não existe. Veja: node jarvis.mjs lines`); process.exit(1); }
  if (muteInfo()) say("Protocolo Silêncio ativo — tocando mesmo assim (ordem direta é ordem).");
  const t = enqueue(cat);
  say(t ? `Tocando ${cat}: "${t}"` : `Sem clipes da categoria "${cat}" no disco.`);
} else if (cmd === "lines") {
  if (arg && LINES[arg]) {
    console.log(`\n  ${arg} (${LINES[arg].length} falas):\n`);
    LINES[arg].forEach((l, i) => console.log(`  ${String(i + 1).padStart(2)}. ${l}`));
    console.log("");
  } else {
    console.log("\n  categorias:\n");
    const names = Object.keys(LINES).filter((c) => !/^usage/.test(c));
    for (let i = 0; i < names.length; i += 4) console.log("  " + names.slice(i, i + 4).map((n) => `${n} (${LINES[n].length})`.padEnd(18)).join(" "));
    console.log(`\n  detalhe: node jarvis.mjs lines <categoria>\n`);
  }
} else if (cmd === "update") {
  const r = spawnSync(process.execPath, [join(__dir, "update.mjs")], { stdio: "inherit" });
  process.exit(r.status || 0);
} else if (cmd === "version" || cmd === "-v" || cmd === "--version") {
  try { console.log(readFileSync(join(__dir, "VERSION"), "utf8").trim()); } catch { console.log("?"); }
} else {
  say("Comandos: status (padrão) · doctor [--fix] · mute [30m|2h|sempre] · unmute · quiet 22-07|off · test [categoria] · lines [categoria] · update · version");
}
