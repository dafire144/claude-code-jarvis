// Status line "J.A.R.V.I.S." pro Claude Code.
// Recebe JSON via stdin e imprime UMA linha com ANSI colors.
// Custo em R$ (cotação USD-BRL ao vivo, cache 12h) + créditos (1 cr = US$0,01).
// Config no settings.json: "statusLine": { "type": "command", "command": "node .../statusline.mjs" }
import { execSync, spawn } from "child_process";
import { readFileSync, existsSync, writeFileSync, mkdirSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dir = dirname(fileURLToPath(import.meta.url));
const RATE_FILE = join(__dir, ".usdbrl.json");
const RATE_FALLBACK = 5.4;
const RATE_TTL = 12 * 60 * 60 * 1000; // 12h

// lê cotação do cache; se velha/ausente, dispara atualização em 2º plano (nunca bloqueia)
function getRate() {
  let rate = RATE_FALLBACK, ts = 0;
  try {
    const c = JSON.parse(readFileSync(RATE_FILE, "utf8"));
    if (c.rate > 0) { rate = c.rate; ts = c.ts || 0; }
  } catch { /* sem cache ainda */ }
  if (Date.now() - ts > RATE_TTL) {
    const code = `
      import { writeFileSync } from "fs";
      const r = await fetch("https://economia.awesomeapi.com.br/json/last/USD-BRL").then(x => x.json());
      const rate = parseFloat(r.USDBRL.bid);
      if (rate > 0) writeFileSync(${JSON.stringify(RATE_FILE)}, JSON.stringify({ rate, ts: Date.now() }));
    `;
    try {
      const child = spawn(process.execPath, ["--input-type=module", "-e", code], { detached: true, stdio: "ignore" });
      child.unref();
    } catch { /* offline = segue com fallback */ }
  }
  return rate;
}

const chunks = [];
for await (const c of process.stdin) chunks.push(c);
let d = {};
try { d = JSON.parse(Buffer.concat(chunks).toString("utf8") || "{}"); } catch { /* segue com defaults */ }

// paleta âmbar/dourado (estilo arco-reator)
const AMB = "\x1b[38;5;214m";  // âmbar (E8B24A)
const ARC = "\x1b[38;5;178m";  // dourado p/ custo
const OK  = "\x1b[38;5;108m";  // verde suave (online)
const TXT = "\x1b[38;5;250m";  // cinza claro
const DIM = "\x1b[38;5;240m";  // separadores
const B = "\x1b[1m", R = "\x1b[0m";

const model = d.model?.display_name || "Claude";

// grava o MODELO da sessão pro resto do Jarvis (HUD e falas do modo FABLE 5):
// hud-sessions/<sid>/model.txt = linha1 id, linha2 nome. Escreve a CADA render
// (mtime fresco = o model.mjs dos hooks nem precisa farejar o transcript).
const modelId = String(d.model?.id || "");
try {
  const sid = String(d.session_id || "").replace(/[^A-Za-z0-9_-]/g, "");
  if (sid && modelId) {
    const mdir = join(__dir, "hud-sessions", sid);
    if (!existsSync(mdir)) mkdirSync(mdir, { recursive: true });
    const mf = join(mdir, "model.txt");
    let old = "";
    try { old = (readFileSync(mf, "utf8").split("\n")[0] || "").trim(); } catch { /* 1ª vez */ }
    if (old && old !== modelId) writeFileSync(join(mdir, "model-prev"), old);   // marcador de TROCA (fala+transição)
    writeFileSync(mf, `${modelId}\n${model}`);
  }
} catch { /* statusline nunca quebra por isso */ }
const fable = /fable/i.test(modelId);   // Fable 5 = classe Mythos, ganha estrela dourada

const cwd = d.workspace?.current_dir || process.cwd();
const dir = cwd.split(/[\\/]/).filter(Boolean).pop() || cwd;

// branch git (rápido e silencioso; fora de repo = vazio)
let branch = "";
try {
  branch = execSync("git rev-parse --abbrev-ref HEAD", {
    cwd, stdio: ["ignore", "pipe", "ignore"], timeout: 1500,
  }).toString().trim();
} catch { /* não é repo */ }

// custo da sessão (quando o Claude informa)
const cost = d.cost?.total_cost_usd;

// hora local
const now = new Date();
const hh = String(now.getHours()).padStart(2, "0");
const mm = String(now.getMinutes()).padStart(2, "0");

const sep = `${DIM} │ ${R}`;
const GOLD = "\x1b[38;5;220m";  // ouro-branco Mythos (só pro Fable 5)
const parts = [
  `${AMB}${B}◈ J.A.R.V.I.S.${R} ${OK}online${R}`,
  fable ? `${GOLD}${B}✦ ${model}${R}` : `${TXT}${model}${R}`,
  `${AMB}${dir}${R}`,
];
if (branch) parts.push(`${TXT}⎇ ${branch}${R}`);
if (typeof cost === "number" && cost > 0) {
  const brl = (cost * getRate()).toFixed(2).replace(".", ",");
  parts.push(`${ARC}R$ ${brl}${R}`);
}
parts.push(`${DIM}${hh}:${mm}${R}`);

console.log(parts.join(sep));
