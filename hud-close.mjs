// Hook PostToolUse (Agent|Task|Workflow): marca a missão mais antiga em execução
// como concluída e anexa custo/tokens quando o resultado informa.
import { readdirSync, readFileSync, writeFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dir = dirname(fileURLToPath(import.meta.url));
const HUD_DIR = join(__dir, "hud");

async function readStdin() {
  try {
    const chunks = [];
    for await (const c of process.stdin) chunks.push(c);
    return JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}");
  } catch { return {}; }
}
const evt = await readStdin();
const tool = String(evt.tool_name || "");
if (!["Agent", "Task", "Workflow"].includes(tool)) process.exit(0);
// agente em segundo plano retorna na hora — o HUD dele tem vida própria (autoCloseSec)
if (evt.tool_input?.run_in_background) process.exit(0);

// procura a missão em execução mais antiga (FIFO) que espera sinal de fim
let candidates = [];
try {
  for (const f of readdirSync(HUD_DIR).filter((f) => f.endsWith(".json"))) {
    const p = join(HUD_DIR, f);
    try {
      const m = JSON.parse(readFileSync(p, "utf8"));
      if (m.status === "running" && !m.autoCloseSec) candidates.push({ p, m });
    } catch { /* arquivo em transição */ }
  }
} catch { process.exit(0); }
if (!candidates.length) process.exit(0);
candidates.sort((a, b) => a.m.start - b.m.start);
const { p, m } = candidates[0];

// extrai custo/tokens do resultado, se o harness informar
const raw = JSON.stringify(evt.tool_response || {});
const cost = (raw.match(/"total_?cost_?usd"\s*:\s*([\d.]+)/i) || [])[1];
const tokens = (raw.match(/"total_?tokens"\s*:\s*(\d+)/i) || [])[1];

m.status = "done";
m.doneAt = Date.now();
if (cost) m.cost_usd = parseFloat(cost);
if (tokens) m.tokens = parseInt(tokens, 10);
writeFileSync(p, JSON.stringify(m));
process.exit(0);
