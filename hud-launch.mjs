// Hook PreToolUse: quando um subagente/workflow/processo em 2º plano sobe,
// cria o arquivo de missão e abre a janela HUD (hud.ps1) estilo Jarvis.
import { readdirSync, writeFileSync, mkdirSync, existsSync, statSync, unlinkSync, appendFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { spawnSync } from "child_process";

const __dir = dirname(fileURLToPath(import.meta.url));
const HUD_DIR = join(__dir, "hud");
const EXE = join(__dir, "hud-native", "jarvis-hud-wf.exe");   // telinha NATIVA de fan-out (substitui o CMD hud.ps1, legado)
if (!existsSync(HUD_DIR)) mkdirSync(HUD_DIR, { recursive: true });

async function readStdin() {
  try {
    const chunks = [];
    for await (const c of process.stdin) chunks.push(c);
    return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
  } catch { return {}; }
}
const evt = await readStdin();
const tool = String(evt.tool_name || "");
const inp = evt.tool_input || {};

// decide se essa chamada merece um HUD
let proto = "", agent = "", task = "", autoCloseSec = 0;
if (tool === "Agent" || tool === "Task") {
  proto = inp.run_in_background ? "SUBAGENTE EM SEGUNDO PLANO" : "SUBAGENTE EM CAMPO";
  agent = String(inp.subagent_type || "general-purpose");
  task = String(inp.description || inp.prompt || "");
  if (inp.run_in_background) autoCloseSec = 90; // não temos sinal de fim; painel informativo
} else if (tool === "Workflow") {
  proto = "WORKFLOW MULTI-AGENTE";
  const script = String(inp.script || "");
  agent = String(inp.name || (script.match(/name:\s*'([^']+)'/) || [])[1] || "workflow");
  task = String((script.match(/description:\s*'([^']+)'/) || [])[1] || inp.name || "orquestracao de agentes");
} else if ((tool === "Bash" || tool === "PowerShell") && inp.run_in_background) {
  proto = "PROCESSO EM SEGUNDO PLANO";
  agent = tool.toLowerCase();
  task = String(inp.description || inp.command || "");
  autoCloseSec = 75;
} else {
  process.exit(0); // não é caso de HUD
}

task = task.replace(/\s+/g, " ").trim().slice(0, 240);

// faxina: arquivos de missão com mais de 24h
try {
  for (const f of readdirSync(HUD_DIR)) {
    const p = join(HUD_DIR, f);
    if (Date.now() - statSync(p).mtimeMs > 24 * 60 * 60 * 1000) unlinkSync(p);
  }
} catch { /* ok */ }

// arquivo de missão
const id = `${Date.now()}-${Math.floor(Math.random() * 1e6)}`;
const file = join(HUD_DIR, `${id}.json`);
const mission = {
  status: "running",
  start: Date.now(),
  proto, agent, task,
  model: String(inp.model || ""),
  autoCloseSec: autoCloseSec || undefined,
};
writeFileSync(file, JSON.stringify(mission));

// abre a telinha NATIVA de fan-out (jarvis-hud-wf.exe --fanout) via WMI oculto: nasce
// fora do job do hook, sem flash de console, e sobrevive ao fim do hook. (Antes abria o
// CMD hud.ps1 — legado, desconectado.) Se o exe sumir, segue sem janela.
if (!existsSync(EXE)) process.exit(0);
const cmd = `"${EXE}" --fanout "${file}"`;
try {
  const wmi = [
    `$si=([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
    `$si.ShowWindow=0;`,
    `$r=([wmiclass]'Win32_Process').Create('${cmd.replace(/'/g, "''")}',$null,$si);`,
    `exit $r.ReturnValue`,
  ].join(" ");
  const res = spawnSync("powershell", ["-NoProfile", "-Command", wmi], { timeout: 15000, stdio: "ignore" });
  appendFileSync(join(__dir, "jarvis.log"), `${new Date().toISOString()} fanout-hud aberto: ${proto} / ${agent} (wmi rc=${res.status})\n`);
} catch { /* sem janela = segue o baile */ }
process.exit(0);
