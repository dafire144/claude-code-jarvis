// Sonda de TITULO da sessao — usada nas RE-TENTATIVAS do abridor atrasado
// (hud-open-delayed.ps1 no Windows, mac-hud-open.mjs no macOS).
// Por que existe: sessao RECEM-criada pode nao ter titulo no momento do 1o prompt
// (o app desktop grava o titulo async, segundos depois — corrida vista em 07/07).
// A sonda re-resolve o titulo direto dos arquivos do app e, ao achar, escreve o
// meta.txt (titulo + startTs ancorado no inicio do burst = inicio real da tarefa).
// A partir do meta.txt existir, a telinha esta liberada pra abrir.
// CLI: node title-probe.mjs <session-id> <dir-da-sessao>   (exit 0 = titulo existe)
import { readFileSync, writeFileSync, readdirSync } from "fs";
import { join } from "path";

const SESSIONS_DIR = process.platform === "darwin"
  ? join(process.env.HOME || "", "Library", "Application Support", "Claude", "claude-code-sessions")
  : join(process.env.APPDATA || "", "Claude", "claude-code-sessions");

export function probeTitle(sid, dir) {
  if (!sid || !dir) return "";
  let title = "";
  const walk = (d, depth) => {
    if (title || depth > 3) return;
    let ents = []; try { ents = readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of ents) {
      if (title) return;
      const p = join(d, e.name);
      if (e.isDirectory()) walk(p, depth + 1);
      else if (e.name.endsWith(".json")) {
        try { const raw = readFileSync(p, "utf8"); if (raw.includes(`"cliSessionId":"${sid}"`)) title = (raw.match(/"title"\s*:\s*"([^"]+)"/) || [])[1] || ""; } catch { /* em uso */ }
      }
    }
  };
  walk(SESSIONS_DIR, 0);
  if (!title) return "";
  let startTs = 0;
  try { const l = readFileSync(join(dir, "meta.txt"), "utf8").split("\n"); if (l[1]) startTs = Number(l[1]) || 0; } catch { /* 1a vez */ }
  if (!startTs) { try { startTs = Number(readFileSync(join(dir, "burst.txt"), "utf8").split("\t")[0]) || 0; } catch { /* sem burst */ } }
  if (!startTs) startTs = Date.now();
  try { writeFileSync(join(dir, "meta.txt"), `${title}\n${startTs}`); } catch { /* ok */ }
  return title;
}

// modo CLI (chamado pelo hud-open-delayed.ps1 a cada re-tentativa)
if (process.argv[1] && process.argv[1].replace(/\\/g, "/").endsWith("title-probe.mjs")) {
  const t = probeTitle(String(process.argv[2] || ""), String(process.argv[3] || ""));
  if (t) { console.log(t); process.exit(0); }
  process.exit(1);
}
