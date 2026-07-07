// report-bug.mjs — relato ANÔNIMO de bug/ajuste para o mantenedor do J.A.R.V.I.S.
// Uso: node report-bug.mjs "resumo curto da queixa e do ajuste feito"
// O que é enviado: a mensagem, a versão local, o sistema operacional e a data.
// NADA além disso (sem caminhos, sem nome de usuário, sem conteúdo de sessão).
// Para desativar de vez: defina a variável de ambiente JARVIS_NO_REPORT=1.
import { readFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dir = dirname(fileURLToPath(import.meta.url));
const ENDPOINT = "https://jarvis.ornasolucoes.com.br/";

const msg = (process.argv[2] || "").trim();
if (!msg) { console.log("uso: node report-bug.mjs \"resumo da queixa/ajuste\""); process.exit(1); }
if (process.env.JARVIS_NO_REPORT === "1") {
  console.log("[J.A.R.V.I.S.] Relato desativado por JARVIS_NO_REPORT=1. Nada foi enviado.");
  process.exit(0);
}

let versao = "?";
try { versao = readFileSync(join(__dir, "VERSION"), "utf8").trim(); } catch { /* ok */ }

const body = new URLSearchParams({
  "form-name": "jarvis-bug-report",
  queixa: msg.slice(0, 1200),
  versao,
  plataforma: `${process.platform} node${process.versions.node}`,
  data: new Date().toISOString(),
});

try {
  const ctrl = new AbortController();
  const to = setTimeout(() => ctrl.abort(), 10000);
  const res = await fetch(ENDPOINT, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: body.toString(),
    signal: ctrl.signal,
  });
  clearTimeout(to);
  if (res.ok) console.log("[J.A.R.V.I.S.] Relato anônimo enviado ao mantenedor. Obrigado por aprimorar meus sistemas.");
  else console.log(`[J.A.R.V.I.S.] O posto de escuta respondeu HTTP ${res.status}. O relato não foi registrado.`);
} catch {
  console.log("[J.A.R.V.I.S.] Sem conexão com o posto de escuta. O relato não foi enviado (tudo bem, siga o trabalho).");
}
