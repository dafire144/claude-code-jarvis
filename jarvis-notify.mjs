// Hook do Claude Code: toca uma fala "Jarvis" nos eventos da sessão.
// Recebe o JSON do hook via stdin. Sorteia um clipe da categoria e toca DESTACADO
// (não trava a sessão). Uso no settings.json:
//   node ~/.claude/jarvis/jarvis-notify.mjs <categoria>
// Categorias: stop, notify, prompt, fanout, subagent, sessionstart, compact,
//             credits, sessionend, code, terminal, search, files, deploy, git
import { readdirSync, existsSync, statSync, writeFileSync, readFileSync, appendFileSync, mkdirSync, rmSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { spawnSync, spawn } from "child_process";
import { LINES } from "./lines.mjs";
import { sessionModel, isFable } from "./model.mjs";

const __dir = dirname(fileURLToPath(import.meta.url));
const CLIPS = join(__dir, "clips");
const LOCK = join(__dir, ".last-play");
const COOLDOWNS_FILE = join(__dir, ".cooldowns.json");
const LASTLINE_FILE = join(__dir, ".last-line.json");   // anti-repetição: última fala por categoria
const LOG = join(__dir, "jarvis.log");
const LAST_END = join(__dir, ".last-sessionend");       // último SessionEnd (qualquer reason)
const LAST_ACTIVITY = join(__dir, ".last-activity");    // último evento de QUALQUER tipo
const VOICE_ID = "N2lVS1w4EtoT3dr4eOWO";                // Callum — robótica/raspada estilo filme (03/07)

// dicionário de pronúncia p/ o prefixo: palavras que o TTS lê errado -> grafia fonética PT.
// chave em minúsculas (casa a palavra inteira, sem acento/caixa). Fácil de estender.
// Ao ADICIONAR/mudar algo aqui, apagar clips\prefix\* pra os prefixos regenerarem.
const PRONUNCE = {
  seo: "Es I Ou", // casa "SEO" e "S.E.O" (pontos são removidos na comparação)
};

// caixa-preta: registra cada chamada pra diagnosticar hooks que não disparam
function logLine(msg) {
  try { appendFileSync(LOG, `${new Date().toISOString()} ${msg}\n`); } catch { /* ok */ }
}

// cooldown POR CATEGORIA (ms) — gatilhos frequentes falam de vez em quando,
// senão o Jarvis vira papagaio. Categorias fora da lista = sem cooldown próprio.
const COOLDOWN = {
  terminal: 90_000,
  code: 90_000,
  files: 120_000,
  search: 60_000,
  subagent: 30_000,
  qa: 20_000,        // agente de QA: ação deliberada, cooldown curto só p/ não duplicar
  qa_ultra: 20_000,
  git: 30_000,
  credits: 300_000, // alerta importante, mas não repetir a cada requisição
  sessionend: 120_000,
  sessionstart: 10_000, // regra do Davi: só o 1º evento de boas-vindas num intervalo de 10s
  fable: 240_000,       // modo FABLE 5: tempero ocasional, nunca papagaio
  fable_stop: 300_000,
  fable_boot: 120_000,  // várias sessões Fable abrindo juntas = só a 1ª anuncia
  fable_off: 120_000,   // recolhimento do protocolo (troca de modelo no meio da sessão)
  update: 72_000_000,   // aviso de atualização: no máx. 1x a cada 20h (cinto de segurança)
};

// categoria: 1º argumento, ou deriva do evento do hook (stdin)
let cat = (process.argv[2] || "").trim();

async function readStdin() {
  try {
    const chunks = [];
    for await (const c of process.stdin) chunks.push(c);
    return JSON.parse(Buffer.concat(chunks).toString("utf8").replace(/^﻿/, "") || "{}");   // tolera BOM (pipes do PS 5.1)
  } catch { return {}; }
}

const evt = await readStdin();
if (!cat) {
  const map = {
    Notification: "notify",
    UserPromptSubmit: "prompt",
    PreToolUse: "fanout",
    SubagentStop: "subagent",
    SessionStart: "sessionstart",
    SessionEnd: "sessionend",
    PreCompact: "compact",
  };
  cat = map[evt.hook_event_name] || "stop";
}

// registra atividade: lê o carimbo anterior ANTES de renovar (a saudação usa o anterior)
let lastActivity = 0;
try { lastActivity = Number(readFileSync(LAST_ACTIVITY, "utf8")) || 0; } catch { /* primeira vez */ }
try { writeFileSync(LAST_ACTIVITY, String(Date.now())); } catch { /* ok */ }

// SessionStart: boas-vindas SÓ numa abertura real do Claude. Etiqueta não basta
// (clicar numa sessão pode religar o processo dela com source "startup", e fechar o
// app recicla sessões idem). Regra comportamental: só saúda se o Jarvis estava em
// SILÊNCIO há 3+ min (abertura real = Davi estava fora). O daemon ainda segura a
// saudação 6s e cancela se chegar um SessionEnd (assinatura de fechamento).
if (cat === "sessionstart") {
  const source = String(evt.source || "");
  if (source !== "startup") {
    logLine(`silencio: SessionStart source=${source || "?"}`);
    process.exit(0);
  }
  if (Date.now() - lastActivity < 180_000) {
    logLine("silencio: SessionStart com atividade recente (nao e abertura do app)");
    process.exit(0);
  }
}

// --- consciência de horário (saudações por período + madrugada) ---
const HOUR = new Date().getHours();
function greetCat() {
  return HOUR >= 5 && HOUR < 12 ? "greet_am" : HOUR >= 12 && HOUR < 18 ? "greet_pm" : "greet_night";
}

// --- refinamentos por conteúdo do evento ---
// Bash/PowerShell: se o comando é deploy, git ou teste, fala especial
if (cat === "terminal") {
  const cmd = String(evt.tool_input?.command || "");
  if (/netlify\s+deploy|vercel\s+(deploy|--prod)|firebase\s+deploy|--prod/i.test(cmd)) cat = "deploy";
  else if (/git\s+(commit|push)/i.test(cmd)) cat = "git";
  else if (/\bnpm\s+(run\s+)?test\b|\byarn\s+test\b|\bpnpm\s+(run\s+)?test\b|\bvitest\b|\bjest\b|\bpytest\b|\bgo\s+test\b|\bcargo\s+test\b|\bmocha\b|\bphpunit\b|\brspec\b|\bnode\b[^|&]*\btest\b/i.test(cmd)) cat = "test";
}
// Notificação: se a mensagem fala de limite/créditos, alerta especial
if (cat === "notify") {
  const msg = String(evt.message || "");
  if (/usage limit|rate limit|running low|credit|out of (tokens|usage)|limit (reached|approaching)/i.test(msg)) {
    cat = "credits";
  }
}
// Agente de QA: Agent/Task/Workflow chega como "fanout". Distingo o QA SIMPLES (1 inspetor,
// subagente orna-qa) do QA PROFUNDO multi-agente (banca, workflow orna-qa-ultra) -> falas próprias.
if (cat === "fanout") {
  const tn = String(evt.tool_name || "");
  const ti = evt.tool_input || {};
  const hay = [ti.subagent_type, ti.name, ti.description, ti.script].map((x) => String(x || "")).join(" ").toLowerCase();
  if (/qa[-_ ]?ultra|orna-qa-ultra/.test(hay)) cat = "qa_ultra";
  else if (tn === "Workflow" && /\bqa\b|auditoria|orna-qa/.test(hay)) cat = "qa_ultra";
  else if (/orna-qa\b/.test(hay) || ((tn === "Agent" || tn === "Task") && /\bqa\b/.test(hay))) cat = "qa";
}
// Prompt: se o Davi ativa o ULTRACODE (menciona a palavra), força total do Jarvis.
if (cat === "prompt" && /\bultracode\b/i.test(String(evt.prompt || ""))) {
  cat = "ultracode";
}
// Prompt: se o Davi anuncia que vai dar clear, fala de encerramento/handoff
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\/clear|vou (dar|fazer) (um )?clear|limpar (a )?sess[aã]o|encerrar (a )?sess[aã]o/i.test(txt)) {
    cat = "clear";
  }
}
// Prompt: se o Davi se despede do Jarvis, fala de encerramento/desligamento
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\b(tchau|adeus|at[eé] (logo|mais|amanh[aã]|a pr[oó]xima|breve)|valeu por hoje|obrigad[oa] por hoje|por hoje (é|e|eh) s[oó]|encerrar por hoje|vou (fechar|encerrar) o claude|me despedir)\b/i.test(txt)) {
    cat = "sessionend";
  }
}
// Prompt: saudação ("bom dia/tarde/noite", "olá", "oi jarvis") -> saudação por período.
// Só quando a mensagem é ESSENCIALMENTE um cumprimento (o resto é curto), pra não
// engolir pedidos como "bom dia, refaça o HUD".
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  // (?=$|[\s,!.]) em vez de \b: \b ASCII não casa após vogal acentuada ("olá", "alô").
  const gm = txt.match(/^\s*(bom dia|boa tarde|boa noite|ol[aá]|oi,?\s*jarvis|hey,?\s*jarvis|e a[ií]|al[oô])(?=$|[\s,!.])[\s,!.]*/i);
  if (gm && txt.slice(gm[0].length).trim().length <= 10) {
    const g = gm[1].toLowerCase();
    cat = /bom dia/.test(g) ? "greet_am" : /boa tarde/.test(g) ? "greet_pm" : /boa noite/.test(g) ? "greet_night" : greetCat();
  }
}
// Prompt: se o Davi diz que algo NÃO funcionou / deu errado, o Jarvis vai investigar.
// (prioridade sobre pergunta: "não funcionou, por quê?" deve virar issue, não question)
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\b(n[aã]o funcion|n[aã]o deu certo|n[aã]o foi bem|n[aã]o abriu|n[aã]o (est[aá]|t[aá]) (funcion|indo|rodando)|parou de funcionar|deu (errad|ruim|pau|erro|problema)|deu um erro|apareceu (um )?erro|tem (um )?(erro|bug|problema)|falhou|quebrou|bugou|bugad[oa]|com bug|t[aá] com bug|t[aá] errad|est[aá] errad|coisa estranha|algo (de )?estranho|est[aá] estranho|n[aã]o rolou|n[aã]o deu)/i.test(txt)) {
    cat = "issue";
  }
}
// Prompt: o Davi pede pra ESPERAR/segurar (mensagem curta) -> o Jarvis aguarda a postos.
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  if (txt.length <= 40 && /^\s*(espera|espere|pera|per[aá][ií]|calma|segura|segure|aguarda|aguarde|um (momento|instante|segundo|minuto)|s[oó] um (momento|instante|segundo)|deixa eu (ver|pensar)|espera a[ií])\b/i.test(txt)) {
    cat = "wait";
  }
}
// Prompt: o Davi diz NÃO / cancela / deixa / esquece (curto) -> recua com elegância.
// (issue já foi checado antes, então "não funcionou" vira issue, não deny.)
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  if (txt.length <= 40 && /^\s*(n[aã]o(?!\s*(funcion|deu|abriu|est|t[aá]|rolou|foi|precisa|sei))|para(r)?\b|pare\b|cancela(r)?|deixa (pra l[aá]|isso|quieto|estar|pra depois)|esquece|esque[çc]a|desconsidera|melhor n[aã]o|abandona)\b/i.test(txt)) {
    cat = "deny";
  }
}
// Prompt: "pode" / "pode sim" / "pode ser" isolado (sem mais nada) = sinal verde, não
// pergunta. Só quando a frase é essencialmente isso (senão "pode me dizer X?" vira pergunta).
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  if (/^\s*pode( sim| ser)?\s*[.!]*$/i.test(txt)) cat = "affirm";
}
// Prompt: se é uma PERGUNTA, o Jarvis reage como consulta ("Vamos ver, senhor.")
// em vez da saudação de tarefa. Sinais: termina com "?" ou começa com interrogativo.
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  const isQuestion = /\?\s*$/.test(txt)
    || /^\s*(qual|quais|como|quando|onde|aonde|quem|quanto|quantos|quantas|por qu[eê]|o que|ser[aá]|[eé] poss[ií]vel|d[aá] (pra|para)|posso|pode|poderia|consegue|tem como|existe|h[aá] (como|algum)|devo|deveria|vale a pena|faz sentido|voc[eê] (acha|sabe|consegue|pode)|me (diz|explica|fala))\b/i.test(txt);
  if (isQuestion) cat = "question";
}
// Prompt: se o Davi ELOGIA (mensagem curta e positiva), o Jarvis agradece com classe.
// Gate de tamanho evita disparar em "gostei X, mas agora faça Y" (pedido disfarçado).
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  if (txt.length <= 42 && /\b(muito bom|ficou (muito )?bom|ficou (top|show|perfeito|[oó]timo|lindo|massa|sensacional|incr[ií]vel)|top demais|ficou top|adorei|amei|gostei|perfeito|excelente|maravilh|sensacional|show de bola|mandou (bem|muito bem)|arrasou|parab[eé]ns|que (legal|maneiro|massa)|ficou (muito )?legal)\b/i.test(txt)) {
    cat = "praise";
  }
}
// Prompt: se o Davi manda "meter marcha" (acelerar / ir com tudo), o Jarvis responde animado.
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\bmete(r)?\s*(a\s*)?marcha\b|\bmete\s*(o\s*p[eé]|ficha|o\s*loko)(?!\w)|\bmanda\s*(ver|bala|brasa|bronca)\b|\b(bota|p[oõ]e)\s*(pra|para)\s*(quebrar|rodar|ferver)\b|\bsolta\s*(os\s*cavalos|a\s*fera)\b|\bvai\s*(com\s*(tudo|for[çc]a|g[aá]s)|fundo|pra\s*cima)\b|\bvamo?s?\s*que\s*vamo?s?\b|\b(vambora|simbora|partiu|bora)\b|\bacelera(r|ndo)?\b|\bpisa\s*(fundo|no\s*acelerador)\b|\bp[eé]\s*na\s*t[aá]bua\b|\bafunda\s*o\s*(p[eé]|acelerador)(?!\w)|\b(detona|arrebenta|arrasa)(r|ndo)?\b|\bdesce\s*(a\s*lenha|o\s*sarrafo)\b|\b(turbina|nitro|turbo)\b|\bmodo\s*turbo\b|\bfor[çc]a\s*(total|m[aá]xima)\b|\bpot[eê]ncia\s*m[aá]xima\b|\bno\s*talo\b|\ba\s*plena\s*carga\b|\b(carga|energia)\s*total\b|\btoca\s*(ficha|o\s*barco|em\s*frente)\b|\bm[aã]os?\s*[aà]\s*obra\b|\bm[aã]o\s*na\s*massa\b|\barrega[çc]a(r|\s*as\s*mangas)?\b|\btaca(-?\s*le|\s*lhe)?\s*pau\b|\bengata\s*(a\s*)?(marcha|quinta|sexta)\b|\b(quinta|sexta|[uú]ltima)\s*marcha\b|\ba\s*todo\s*(vapor|g[aá]s)\b|\ba\s*(mil|milh[aã]o|jato)\b|\bsem\s*(freio|d[oó]|enrola[çc][aã]o)(?!\w)|\bpau\s*(na\s*m[aá]quina|pra\s*toda\s*obra)\b|\brasga(r)?\b|\bdecola(r)?\b|(?:^|\s)[eé]\s*pra\s*ontem\b|\bbota\s*(pilha|f[eé])(?!\w)|\bp[oõ]e\s*pilha\b|\bcom\s*(ra[çc]a|garra|sangue\s*no\s*olho)\b|\breta\s*final\b|\balavanca(r)?\b|\brapid[aã]o\b|\bvoando\b/i.test(txt)) {
    cat = "acelera";
  }
}
// Prompt: confirmação/sinal-verde CURTO ("isso", "pode", "manda") -> o Jarvis prossegue.
// (checado depois de acelera pra "manda ver"/"vai com tudo" continuarem sendo acelera.)
if (cat === "prompt") {
  const txt = String(evt.prompt || "").trim();
  if (txt.length <= 24 && !/\?\s*$/.test(txt) && /^\s*(sim|isso( mesmo)?|exato|exatamente|pode( sim| ser)?|claro|com certeza|ok|okay|okey|beleza|blz|certo|correto|correta|fechado|fechou|vai( l[aá])?|manda|fa[çc]a|isso a[ií]|perfeito|combinado|confirmo|confirmado|positivo)\b/i.test(txt)) {
    cat = "affirm";
  }
}
// Prompt: pedido de criação VISUAL (imagem/arte/logo/reel) -> tom criativo.
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\b(ger\w*|cri\w*|faz|fa[çc]a|fazer|desenh\w*|mont\w*|produz\w*|renderiz\w*|refaz\w*|refa[çc]\w*|edit\w*)\b[^?]{0,42}\b(imagem|imagens|arte|artes|logo|logotipo|reel|reels|v[ií]deo|thumbnail|banner|criativo|criativos|carross[eé]l|ilustra[çc][aã]o|capa|motion|est[aá]tico)\b/i.test(txt)) {
    cat = "design";
  }
}
// Prompt: pedido de PESQUISA A FUNDO / estudo aprofundado -> tom de investigação.
if (cat === "prompt") {
  const txt = String(evt.prompt || "");
  if (/\bdeep\s*research\b|\bfa[çc]a uma pesquisa\b|\b(pesquis\w*|investig\w*|estud\w*|levant\w*|analis\w*|mapei\w*|aprofund\w*)\b[^?]{0,30}\b(a fundo|em profundidade|profund|detalhad|minucios|aprofund|tudo sobre|o m[aá]ximo|com afinco)\b/i.test(txt)) {
    cat = "research";
  }
}
// Prompt: madrugada e o Davi ainda trabalha -> o Jarvis começa, com um cuidado gentil.
// Só às vezes (não vira papagaio) e só se o prompt não caiu em nenhuma intenção acima.
if (cat === "prompt" && HOUR >= 0 && HOUR < 5 && Math.random() < 0.35) {
  cat = "night";
}
// SessionEnd: despedida CONSERVADORA — só em sinal explícito (princípio Jarvis:
// melhor calar do que falar errado). reason=other (trocar/sair de sessão, reciclagem
// do app) = SEMPRE silêncio; a despedida do dia a dia é o Davi se despedir no chat.
// O carimbo .last-sessionend continua servindo pro daemon cancelar boas-vindas falsas.
if (evt.hook_event_name === "SessionEnd") {
  const reason = String(evt.reason || "");
  logLine(`sessionend reason=${reason || "?"}`);
  try { writeFileSync(LAST_END, String(Date.now())); } catch { /* ok */ }
  if (reason === "clear") cat = "clear";
  else if (reason === "logout" || reason === "prompt_input_exit") cat = "sessionend";
  else process.exit(0);
}

const now = Date.now();
logLine(`chamado: evt=${evt.hook_event_name || "?"} cat=${cat}${evt.source ? " src=" + evt.source : ""}`);

// --- cooldown por categoria ---
let cds = {};
try { cds = JSON.parse(readFileSync(COOLDOWNS_FILE, "utf8")); } catch { /* primeira vez */ }

// --- anunciador de atualização: dispara o update-check no máx. 1x/dia, num processo
// que SOBREVIVE ao hook. No Windows, filho de hook morre com o job (lição 03/07) ->
// nasce via WMI, oculto e fora do job; no Mac, spawn destacado basta. O check é quem
// faz rede/compare; aqui só um read barato do estado. Fora de teardown e sem recursão. ---
if (cat !== "update" && evt.hook_event_name !== "SessionEnd") {
  try {
    let ust = {};
    try { ust = JSON.parse(readFileSync(join(__dir, ".update-state.json"), "utf8")); } catch { /* 1a vez -> checa */ }
    if (Date.now() - (ust.lastCheckTs || 0) >= 24 * 60 * 60 * 1000) {
      const checker = join(__dir, "update-check.mjs");
      if (process.platform === "win32") {
        const cmdU = `"${process.execPath}" "${checker}"`;
        const wmiU = [
          `$si=([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
          `$si.ShowWindow=0;`,
          `$r=([wmiclass]'Win32_Process').Create('${cmdU.replace(/'/g, "''")}',$null,$si);`,
          `exit $r.ReturnValue`,
        ].join(" ");
        spawnSync("powershell", ["-NoProfile", "-Command", wmiU], { timeout: 8000, stdio: "ignore" });
      } else {
        const cU = spawn(process.execPath, [checker], { detached: true, stdio: "ignore" });
        cU.unref();
      }
    }
  } catch { /* o anunciador nunca atrapalha a fala */ }
}

// --- TROCA DE MODELO em sessão aberta: o model.mjs/statusline deixam o marcador
// model-prev quando o modelo muda. Entrando no Fable -> saudação do protocolo;
// saindo -> recolhimento (fable_off). O HUD faz a transição visual por conta própria. ---
if (evt.hook_event_name !== "SessionEnd") {
  try {
    const tdir = join(__dir, "hud-sessions", String(evt.session_id || "").replace(/[^A-Za-z0-9_-]/g, ""));
    const prevFile = join(tdir, "model-prev");
    if (existsSync(prevFile)) {
      const prev = readFileSync(prevFile, "utf8").trim();
      try { rmSync(prevFile); } catch { /* outro hook consumiu junto */ }
      const nowId = sessionModel(__dir, evt.session_id, evt.transcript_path);
      const okT = (c) => !COOLDOWN[c] || now - (cds[c] || 0) >= COOLDOWN[c];
      if (!isFable(prev) && isFable(nowId) && okT("fable_boot")) {
        logLine(`transicao: ${prev || "?"} -> ${nowId} (protocolo engajado)`);
        cat = "fable_boot";
      } else if (isFable(prev) && !isFable(nowId) && okT("fable_off")) {
        logLine(`transicao: ${prev} -> ${nowId || "?"} (protocolo recolhido)`);
        cat = "fable_off";
      }
    }
  } catch { /* transição nunca atrapalha a fala normal */ }
}

// --- MODO FABLE 5: o protocolo oculto de força total do Jarvis ganha voz própria ---
// O modelo da sessão vem de hud-sessions/<sid>/model.txt (statusline grava; sem
// statusline o model.mjs fareja a cauda do transcript). Sessão rodando Fable:
// 1) a PRIMEIRA fala vira a saudação épica fable_boot (1x por sessão, marcador
//    fable-hello gravado ANTES da troca: se falhar, fica na fala normal);
// 2) falas de trabalho trocam DE VEZ EM QUANDO pela versão Fable (chance baixa
//    + cooldown checado ANTES da troca, senão o portão geral engoliria a fala).
const FABLE_SWAP = {
  prompt: ["fable", 0.22], code: ["fable", 0.2], terminal: ["fable", 0.15],
  files: ["fable", 0.15], search: ["fable", 0.15], fanout: ["fable", 0.3],
  ultracode: ["fable", 0.5], acelera: ["fable", 0.25], stop: ["fable_stop", 0.3],
};
if (evt.hook_event_name !== "SessionEnd") {   // teardown segue rápido e intocado
  try {
    if (isFable(sessionModel(__dir, evt.session_id, evt.transcript_path))) {
      const fdir = join(__dir, "hud-sessions", String(evt.session_id || "").replace(/[^A-Za-z0-9_-]/g, ""));
      const swapOk = (c) => !COOLDOWN[c] || now - (cds[c] || 0) >= COOLDOWN[c];
      const bootCats = ["sessionstart", "prompt", "greet_am", "greet_pm", "greet_night", "ultracode", "acelera"];
      if (bootCats.includes(cat) && !existsSync(join(fdir, "fable-hello")) && swapOk("fable_boot")) {
        writeFileSync(join(fdir, "fable-hello"), "1");
        logLine(`fable: ${cat} -> fable_boot (saudacao Mythos da sessao)`);
        cat = "fable_boot";
      } else if (FABLE_SWAP[cat] && Math.random() < FABLE_SWAP[cat][1] && swapOk(FABLE_SWAP[cat][0])) {
        logLine(`fable: ${cat} -> ${FABLE_SWAP[cat][0]}`);
        cat = FABLE_SWAP[cat][0];
      }
    }
  } catch { /* modo Fable nunca derruba a fala normal */ }
}

if (COOLDOWN[cat] && now - (cds[cat] || 0) < COOLDOWN[cat]) { logLine(`silencio: cooldown de ${cat}`); process.exit(0); }

// escolhe um clipe da categoria
let pool = [];
try {
  // regex ancorado: casa exatamente "cat-<n>.mp3" (evita "git" pegar "github-*" etc.)
  const re = new RegExp("^" + cat.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "-\\d+\\.mp3$");
  pool = readdirSync(CLIPS).filter((f) => re.test(f));
} catch { /* pasta pode não existir */ }

if (!pool.length) process.exit(0); // sem clipe = silêncio (não atrapalha)

// anti-repetição: nunca toca a MESMA fala duas vezes seguidas na mesma categoria.
// Guarda a última escolhida por categoria em .last-line.json e a exclui do sorteio.
let lastLine = {};
try { lastLine = JSON.parse(readFileSync(LASTLINE_FILE, "utf8")); } catch { /* primeira vez */ }
let candidates = pool;
if (pool.length > 1 && lastLine[cat]) {
  const semRepetir = pool.filter((f) => f !== lastLine[cat]);
  if (semRepetir.length) candidates = semRepetir;
}
const pick = candidates[Math.floor(Math.random() * candidates.length)];
const file = join(CLIPS, pick);
if (!existsSync(file)) process.exit(0);
try { lastLine[cat] = pick; writeFileSync(LASTLINE_FILE, JSON.stringify(lastLine)); } catch { /* ok */ }

// reverse-map do clipe sorteado -> texto falado (pro toast do Windows espelhar a voz).
// clips/{cat}-{i}.mp3 casa com LINES[cat][i-1].
const idxMatch = pick.match(/-(\d+)\.mp3$/);
const lineIdx = idxMatch ? Number(idxMatch[1]) - 1 : -1;
const spokenText = (LINES[cat] && LINES[cat][lineIdx]) || "";

// ---------- prefixo "De <sessão>" ----------
// Descobre o título da sessão (armazenado pelo app desktop) e garante um clipe
// de prefixo gerado UMA vez por título (cache em clips/prefix/).
const PREFIX_DIR = join(CLIPS, "prefix");
const TITLES_CACHE = join(__dir, ".titles.json");
const SESSIONS_DIR = process.platform === "darwin"
  ? join(process.env.HOME || "", "Library", "Application Support", "Claude", "claude-code-sessions")
  : join(process.env.APPDATA || "", "Claude", "claude-code-sessions");

function findTitle(sid) {
  if (!sid) return "";
  // cache com TTL de 10 min (título pode ser renomeado); resultado VAZIO só vale 15s —
  // sessão recém-criada ganha o título segundos depois do 1º prompt (corrida de 07/07)
  let cache = {};
  try { cache = JSON.parse(readFileSync(TITLES_CACHE, "utf8")); } catch { /* primeira vez */ }
  const hit = cache[sid];
  if (hit && Date.now() - hit.ts < (hit.title ? 10 * 60 * 1000 : 15 * 1000)) return hit.title;
  // varre os arquivos de sessão do app desktop atrás do cliSessionId
  let title = "";
  const walk = (dir, depth) => {
    if (title || depth > 3) return;
    let entries = [];
    try { entries = readdirSync(dir, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      if (title) return;
      const p = join(dir, e.name);
      if (e.isDirectory()) walk(p, depth + 1);
      else if (e.name.endsWith(".json")) {
        try {
          const raw = readFileSync(p, "utf8");
          if (raw.includes(`"cliSessionId":"${sid}"`)) {
            title = (raw.match(/"title"\s*:\s*"([^"]+)"/) || [])[1] || "";
          }
        } catch { /* arquivo em uso */ }
      }
    }
  };
  walk(SESSIONS_DIR, 0);
  cache[sid] = { title, ts: Date.now() };
  try { writeFileSync(TITLES_CACHE, JSON.stringify(cache)); } catch { /* ok */ }
  return title;
}

// (A geração do prefixo de voz "De <sessão>" via ElevenLabs foi REMOVIDA desta versão
// pública — exigia chave de API. As notificações mostram o título da sessão como texto.)

// ⚠️ TEARDOWN (SessionEnd: /clear, logout, saída): o app DERRUBA o processo do hook em
// ~1-2s. NADA de rede (ElevenLabs no ensurePrefixClip) nem varredura de pastas (findTitle)
// ANTES de enfileirar — senão a fala se perde (bug 04/07: 2 de 5 /clear não tocaram). No
// teardown enfileira rápido; título só do cache (sem dir-walk), sem prefixo "De <sessão>".
const isTeardown = evt.hook_event_name === "SessionEnd";
let prefixClip = "";
let sessionTitle = "";
if (isTeardown) {
  try {
    const c = JSON.parse(readFileSync(TITLES_CACHE, "utf8"));
    sessionTitle = (c[evt.session_id] && c[evt.session_id].title) || "";
  } catch { /* sem cache = sem título no toast, tudo bem */ }
} else {
  try { sessionTitle = findTitle(evt.session_id) || ""; } catch { /* sem título */ }
}

// registra o cooldown da categoria
try { cds[cat] = now; writeFileSync(COOLDOWNS_FILE, JSON.stringify(cds)); } catch { /* ok */ }

// ENFILEIRA a fala: um item por disparo. O player-daemon (instância única) drena a
// fila tocando 1 por vez com 1s de intervalo — e decide o prefixo pela sessão.
const QUEUE = join(__dir, "queue");
try { if (!existsSync(QUEUE)) mkdirSync(QUEUE, { recursive: true }); } catch { /* ok */ }
const item = { file, prefix: prefixClip || "", session: String(evt.session_id || ""), cat, ts: now, text: spokenText, title: sessionTitle };
const qname = `${now}-${process.pid}-${Math.floor(Math.random() * 1e6)}.json`;
try { writeFileSync(join(QUEUE, qname), JSON.stringify(item)); } catch { /* ok */ }

// acorda o player pra drenar a fila. No Mac: mac-player.mjs (Node + afplay + osascript),
// destacado e sobrevivente. No Windows: player-daemon.ps1 via WMI (fora do job do hook).
if (process.platform === "darwin") {
  try {
    const child = spawn(process.execPath, [join(__dir, "mac-player.mjs"), __dir], { detached: true, stdio: "ignore" });
    child.unref();
  } catch { /* ok */ }
  logLine(`enfileirado: ${pick} (mac)`);
  process.exit(0);
}
const DAEMON = join(__dir, "player-daemon.ps1");
const daemonCmd = `"powershell.exe" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "${DAEMON}" -Dir "${__dir}"`;
const wmi = [
  `$si = ([wmiclass]'Win32_ProcessStartup').CreateInstance();`,
  `$si.ShowWindow = 0;`,
  `$r = ([wmiclass]'Win32_Process').Create('${daemonCmd}', $null, $si);`,
  `exit $r.ReturnValue`,
].join(" ");
const res = spawnSync("powershell", ["-NoProfile", "-Command", wmi], { timeout: 15000, stdio: "ignore" });
logLine(`enfileirado: ${pick}${prefixClip ? " (+prefixo)" : ""} (daemon rc=${res.status})`);
process.exit(0);
