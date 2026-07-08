// Hook do HUD nativo POR SESSÃO. Roda em UserPromptSubmit / PreToolUse(broad) / SessionEnd.
// ABRE a telinha só quando a TAREFA passa de 30s (corrida de atividade em burst.txt; gap >2min
// reinicia) e SÓ se a sessão tem título real (sem título = CLI/efêmera = não abre). Robusto:
// baseado em atividade, não depende do hook de UserPromptSubmit disparar em sessão já aberta.
// Mantém uma pasta por sessão em hud-sessions\<sid>\ com:
//   meta.txt  = linha1 título, linha2 startTs
//   feed.txt  = append-only "<tsMs>\t<TAG>\t<resumo>" (a janela mostra a cauda)
//   hb        = heartbeat da janela (mtime); end = sessão encerrada; closed = fechada à mão
// Garante 1 janela (jarvis-hud-wf.exe <sid>) viva por sessão (WMI oculto, dedupe por hb).
import { readFileSync, writeFileSync, appendFileSync, existsSync, mkdirSync, statSync, readdirSync, rmSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { spawnSync, spawn } from "child_process";
import { sessionModel } from "./model.mjs";

const __dir = dirname(fileURLToPath(import.meta.url));
const ROOT = join(__dir, "hud-sessions");
const EXE = join(__dir, "hud-native", "jarvis-hud-wf.exe");        // telinha nativa (Windows)
const ELECTRON_DIR = join(__dir, "hud-electron");                  // telinha cross-platform (macOS)
const HB_STALE = 6000;

async function readStdin() {
  try { const c = []; for await (const x of process.stdin) c.push(x); return JSON.parse(Buffer.concat(c).toString("utf8").replace(/^﻿/, "") || "{}"); }   // tolera BOM (pipes do PS 5.1)
  catch { return {}; }
}
const evt = await readStdin();
const now = Date.now();
const sid = String(evt.session_id || "");
if (!sid) process.exit(0);
const dir = join(ROOT, sid.replace(/[^A-Za-z0-9_-]/g, ""));  // só o caminho; pasta é criada quando (e se) houver título

const P = (n) => join(dir, n);
const fresh = (n) => { try { return now - statSync(P(n)).mtimeMs < HB_STALE; } catch { return false; } };

// ---- título da sessão (mesma fonte do jarvis-notify: arquivos do app desktop) ----
const TITLES = join(__dir, ".titles.json");
const SESSIONS_DIR = process.platform === "darwin"
  ? join(process.env.HOME || "", "Library", "Application Support", "Claude", "claude-code-sessions")
  : join(process.env.APPDATA || "", "Claude", "claude-code-sessions");
function findTitle(id) {
  if (!id) return "";
  let cache = {};
  try { cache = JSON.parse(readFileSync(TITLES, "utf8")); } catch { /* 1a vez */ }
  const hit = cache[id];
  // ⚠️ resultado VAZIO só vale 15s: sessão recém-criada ganha o título do app segundos
  // depois do 1º prompt (corrida de 07/07) — um vazio cacheado por 10min envenenava tudo.
  if (hit && Date.now() - hit.ts < (hit.title ? 10 * 60 * 1000 : 15 * 1000)) return hit.title;
  let title = "";
  const walk = (d, depth) => {
    if (title || depth > 3) return;
    let ents = []; try { ents = readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of ents) {
      if (title) return;
      const p = join(d, e.name);
      if (e.isDirectory()) walk(p, depth + 1);
      else if (e.name.endsWith(".json")) {
        try { const raw = readFileSync(p, "utf8"); if (raw.includes(`"cliSessionId":"${id}"`)) title = (raw.match(/"title"\s*:\s*"([^"]+)"/) || [])[1] || ""; } catch { /* em uso */ }
      }
    }
  };
  walk(SESSIONS_DIR, 0);
  cache[id] = { title, ts: Date.now() };
  try { writeFileSync(TITLES, JSON.stringify(cache)); } catch { /* ok */ }
  return title;
}

function writeMeta(title) {
  let startTs = 0;
  try { const l = readFileSync(P("meta.txt"), "utf8").split("\n"); if (l[1]) startTs = Number(l[1]) || 0; } catch { /* 1a vez */ }
  // meta nova: ancora o TEMPO DE OP. no início real da tarefa (burst), não no "agora" —
  // o título pode só resolver no 1º PreToolUse, minutos depois do prompt (corrida 07/07)
  if (!startTs) { try { startTs = Number(readFileSync(P("burst.txt"), "utf8").split("\t")[0]) || 0; } catch { /* sem burst */ } }
  if (!startTs) startTs = now;
  try { writeFileSync(P("meta.txt"), `${title}\n${startTs}`); } catch { /* ok */ }
}

// ---- resumo amigável da ação (reaproveitado do feed.mjs) ----
function describe(tool, inp) {
  const base = (p) => String(p || "").split(/[\\/]/).filter(Boolean).pop() || "";
  const cut = (s, n) => { s = String(s || "").replace(/\s+/g, " ").trim(); return s.length > n ? s.slice(0, n) + "…" : s; };
  switch (tool) {
    case "Write": case "Edit": case "NotebookEdit": return { tag: "EDIT", text: `Editando ${base(inp.file_path)}` };
    case "Read": return { tag: "READ", text: `Lendo ${base(inp.file_path)}` };
    case "Bash": case "PowerShell": {
      const c = String(inp.command || "");
      if (/netlify\s+deploy|--prod|vercel\s+(deploy|--prod)|firebase\s+deploy/i.test(c)) return { tag: "DPLY", text: `Deploy: ${cut(c, 40)}` };
      if (/git\s+(commit|push)/i.test(c)) return { tag: "GIT", text: `Git: ${cut(c, 42)}` };
      if (/npm\s+(test|run test)|vitest|jest|pytest|node .*test/i.test(c)) return { tag: "TEST", text: `Testando: ${cut(c, 36)}` };
      return { tag: "EXEC", text: `Executando: ${cut(c, 40)}` };
    }
    case "Grep": return { tag: "FIND", text: `Buscando "${cut(inp.pattern, 32)}"` };
    case "Glob": return { tag: "FIND", text: `Vasculhando ${cut(inp.pattern, 32)}` };
    case "WebSearch": return { tag: "WEB", text: `Pesquisando: ${cut(inp.query, 38)}` };
    case "WebFetch": { try { return { tag: "WEB", text: `Consultando ${new URL(inp.url).host}` }; } catch { return { tag: "WEB", text: "Consultando a web" }; } }
    case "Agent": case "Task": return { tag: "AGNT", text: `Delegando: ${cut(inp.description || inp.subagent_type || "agente", 38)}` };
    case "Workflow": return { tag: "AGNT", text: "Orquestrando agentes" };
    case "TodoWrite": case "TaskCreate": case "TaskUpdate": return { tag: "PLAN", text: "Organizando o plano" };
    default: {
      // tools MCP (mcp__servidor__acao): sessões que trabalham via conectores (Chrome,
      // Supabase, Netlify...) também alimentam o feed e o relógio de abertura da telinha
      const m = /^mcp__(.+?)__(.+)$/.exec(String(tool || ""));
      if (m) return { tag: "MCP", text: cut(`${m[1].replace(/[-_]/g, " ")}: ${m[2].replace(/[-_]/g, " ")}`, 46) };
      return { tag: "--", text: String(tool || "ação") };
    }
  }
}

function feed(tag, text) {
  try { appendFileSync(P("feed.txt"), `${now}\t${tag}\t${String(text).replace(/[\t\n\r]/g, " ")}\n`); } catch { /* ok */ }
}

// progresso da atividade = tarefas concluídas / total (barra 0→k da telinha)
function updateProgress(tool, inp) {
  try {
    let c = 0, t = 0;
    try { const p = readFileSync(P("progress.txt"), "utf8").split("\t"); c = Number(p[0]) || 0; t = Number(p[1]) || 0; } catch { /* 1a vez */ }
    if (tool === "TodoWrite" && Array.isArray(inp.todos)) {           // lista completa -> exato
      t = inp.todos.length;
      c = inp.todos.filter((x) => x && x.status === "completed").length;
    } else if (tool === "TaskCreate") {
      t += 1;                                                          // nova tarefa
    } else if (tool === "TaskUpdate") {
      const s = String(inp.status || "");
      if (s === "completed") c += 1;
      else if (s === "deleted") t = Math.max(0, t - 1);
    }
    if (c > t) c = t;
    writeFileSync(P("progress.txt"), `${c}\t${t}`);
  } catch { /* ok */ }
}

// ---- garante a janela viva (via WMI, oculto, sobrevive ao fim do hook) ----
function ensureHud() {
  if (existsSync(P("closed"))) return;      // fechada à mão: respeita
  if (fresh("hb")) return;                    // já viva
  if (process.platform === "darwin") {        // macOS: telinha Electron (spawn destacado sobrevive)
    const bin = join(ELECTRON_DIR, "node_modules", ".bin", "electron");
    if (!existsSync(bin)) return;             // Mac precisa ter rodado `npm install` em hud-electron/
    try { const c = spawn(bin, [ELECTRON_DIR, sid], { detached: true, stdio: "ignore" }); c.unref(); } catch { /* ok */ }
    return;
  }
  if (process.platform !== "win32") return;   // outras plataformas: sem telinha por ora
  const cmd = `"${EXE}" "${sid}"`;
  const wmi = [
    `$si=([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
    `$si.ShowWindow=0;`,
    `$r=([wmiclass]'Win32_Process').Create('${cmd.replace(/'/g, "''")}',$null,$si);`,
    `exit $r.ReturnValue`,
  ].join(" ");
  try { spawnSync("powershell", ["-NoProfile", "-Command", wmi], { timeout: 12000, stdio: "ignore" }); } catch { /* ok */ }
}

// Agenda a abertura da telinha `delaySec` s apos o prompt (via WMI, sobrevive ao fim do hook).
// Abertura baseada em TEMPO desde o prompt, NAO na cadencia de ferramentas -> nao depende de
// um PreToolUse disparar no momento certo (robusto p/ tarefas que ficam muito "pensando").
// O abridor re-sonda o TITULO (title-probe.mjs) a cada tentativa: sessao nova pode ainda nao
// ter titulo no momento do prompt (corrida 07/07) e ele nasce segundos depois.
function scheduleOpen(delaySec) {
  if (existsSync(P("closed"))) return;                          // fechada a mao: respeita
  if (process.platform === "darwin") {                          // macOS: agenda via node (detached sobrevive)
    try { const c = spawn(process.execPath, [join(__dir, "mac-hud-open.mjs"), dir, sid, String(delaySec)], { detached: true, stdio: "ignore" }); c.unref(); } catch { /* ok */ }
    return;
  }
  if (process.platform !== "win32") return;
  const script = join(__dir, "hud-open-delayed.ps1");
  const inner = `"powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "${script}" -Dir "${dir}" -Exe "${EXE}" -Sid "${sid}" -Delay ${delaySec} -Node "${process.execPath}" -Jarvis "${__dir}"`;
  const wmi = [
    `$si=([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
    `$si.ShowWindow=0;`,
    `$r=([wmiclass]'Win32_Process').Create('${inner.replace(/'/g, "''")}',$null,$si);`,
    `exit $r.ReturnValue`,
  ].join(" ");
  try { spawnSync("powershell", ["-NoProfile", "-Command", wmi], { timeout: 8000, stdio: "ignore" }); } catch { /* ok */ }
}

// limpa pastas de sessões ociosas há +24h (mantém hud-sessions enxuto)
function janitor() {
  try {
    for (const d of readdirSync(ROOT)) {
      const p = join(ROOT, d);
      let newest = 0;
      try { for (const f of readdirSync(p)) { const m = statSync(join(p, f)).mtimeMs; if (m > newest) newest = m; } } catch { continue; }
      if (newest && now - newest > 24 * 3600 * 1000) { try { rmSync(p, { recursive: true, force: true }); } catch { /* ok */ } }
    }
  } catch { /* ok */ }
}

// ---- roteia por evento ----
const ev = String(evt.hook_event_name || "");

// SessionEnd: encerra a telinha (só se a sessão já tinha pasta/telinha)
if (ev === "SessionEnd") {
  const reason = String(evt.reason || "");
  if (existsSync(dir) && ["clear", "logout", "prompt_input_exit"].includes(reason)) {
    try { writeFileSync(P("end"), String(now)); } catch { /* ok */ }
    feed("END", "Sessão encerrada.");
  }
  process.exit(0);
}

// Stop: Claude TERMINOU a resposta. Marca "done" -> a telinha fecha após breve carência
// (DONE_CLOSE no exe). Só se a telinha dessa sessão já existe; some quando vier novo
// prompt/ação (removido abaixo) -> nunca fecha no meio de uma tarefa longa.
if (ev === "Stop") {
  if (existsSync(dir)) { try { writeFileSync(P("done"), String(now)); } catch { /* ok */ } }
  process.exit(0);
}

// TÍTULO OBRIGATÓRIO (pedido do Davi): sessão sem título = CLI/efêmera → NUNCA abre telinha.
// ⚠️ Mas a regra vale no ABRIR, não no pipeline (lição de 07/07, sessão "Caio CRM"): no 1º
// prompt de uma sessão RECÉM-criada o app ainda não gravou o título → se o hook morresse
// aqui, perdia o burst (âncora do relógio) e o agendamento dos 30s, e a telinha só nascia
// minutos depois. Agora o pipeline roda SEMPRE; meta.txt só é escrita quando o título
// existe, o ensureHud exige título, e o abridor atrasado re-sonda via title-probe.mjs.
const title = findTitle(sid);
try { mkdirSync(dir, { recursive: true }); } catch { /* ok */ }
// mantém model.txt fresco pro exe/Electron (modo FABLE 5); statusline já cobre, isto é o fallback
try { sessionModel(__dir, sid, evt.transcript_path); } catch { /* modelo é opcional */ }

// UserPromptSubmit = o Davi mandou um prompt → marca o INÍCIO da tarefa (não abre ainda).
if (ev === "UserPromptSubmit") {
  janitor();
  try { if (existsSync(P("closed"))) rmSync(P("closed")); } catch { /* ok */ }
  try { if (existsSync(P("end"))) rmSync(P("end")); } catch { /* ok */ }
  try { if (existsSync(P("done"))) rmSync(P("done")); } catch { /* ok */ }  // novo prompt -> cancela o auto-fechar
  if (title) writeMeta(title);
  try { writeFileSync(P("burst.txt"), `${now}\t${now}`); } catch { /* ok */ }  // reinicia o relógio da tarefa
  const txt = String(evt.prompt || "").replace(/\s+/g, " ").trim();
  feed("REQ", txt ? txt.slice(0, 48) : "Novo pedido.");
  scheduleOpen(Number(process.env.JARVIS_HUD_DELAY || 30));   // abre em ~30s se a tarefa ainda estiver rodando
  process.exit(0);
}

// PreToolUse (matcher amplo): alimenta feed/progresso e ABRE a telinha só quando a tarefa passa de 30s.
if (title) writeMeta(title);
try { if (existsSync(P("done"))) rmSync(P("done")); } catch { /* ok */ }  // atividade nova -> cancela o auto-fechar
const tool = String(evt.tool_name || "");
const inp = evt.tool_input || {};
if (tool === "TaskCreate" || tool === "TaskUpdate" || tool === "TodoWrite") {
  updateProgress(tool, inp);                       // move a barra de progresso da atividade
  if (tool === "TaskCreate") feed("PLAN", `Nova tarefa: ${String(inp.subject || "").slice(0, 44)}`);
  else if (tool === "TaskUpdate" && String(inp.status) === "completed") feed("DONE", "Tarefa concluída.");
} else {
  const d = describe(tool, inp);
  feed(d.tag, d.text);
}
// regra dos 30s (Davi 04/07): abre quando a "corrida" de atividade atual já dura ≥30s.
// Baseado em atividade (não no prompt) → robusto mesmo se o hook de UserPromptSubmit não disparar.
// ⚠️ O RELÓGIO da tarefa é ancorado no prompt (UserPromptSubmit reinicia burst=now\tnow).
// O gap-reset abaixo é só um FALLBACK p/ o caso raro do prompt não disparar. Era 20s e
// ZERAVA a cada tool, porque o modelo "pensa" mais de 20s entre uma ferramenta e outra —
// aí os 30s nunca acumulavam e a telinha não abria em tarefa longa (bug 04/07). 2min cobre
// as pausas de raciocínio e ainda distingue tarefa nova (prompt novo já zera de qualquer jeito).
let bstart = now, blast = 0;
try { const b = readFileSync(P("burst.txt"), "utf8").split("\t"); bstart = Number(b[0]) || now; blast = Number(b[1]) || 0; }
catch {
  // burst sumiu? ancora no startTs do meta — o relógio da tarefa NUNCA recomeça do zero
  // só porque um arquivo se perdeu (o reinício era o que atrasava a telinha em 07/07)
  try { const l = readFileSync(P("meta.txt"), "utf8").split("\n"); const t0 = Number(l[1]) || 0; if (t0 > 0) { bstart = t0; blast = now; } } catch { /* 1a vez mesmo */ }
}
if (blast > 0 && now - blast > 900000) bstart = now;            // só zera após 15min parado (o scheduleOpen já cobre o caso principal)
try { writeFileSync(P("burst.txt"), `${bstart}\t${now}`); } catch { /* ok */ }
if (now - bstart >= 30000 && title) ensureHud();                 // tarefa passou de 30s (e tem título) → abre
process.exit(0);
