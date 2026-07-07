// model.mjs — descobre o MODELO da sessão (base do modo FABLE 5 e das transições).
// Ordem das fontes (v2, 07/07 — a v1 só farejava o transcript e atrasava até 5min):
//   1. hud-sessions/<sid>/model.txt fresco (<15s): statusline (CLI) grava a cada
//      render, então no CLI isso responde na hora.
//   2. Arquivo de sessão do APP DESKTOP (local_*.json): carrega o campo "model"
//      ESCOLHIDO — o /model reflete aqui imediatamente. O caminho do arquivo é
//      memorizado por sessão em .model-paths.json (a varredura cara roda 1x).
//   3. Fallback: cauda do transcript (~48KB, último "model":"..." de mensagem).
// Toda descoberta grava model.txt (mtime fresco) e, quando o modelo MUDA, deixa
// o marcador model-prev (id antigo) — é ele que dispara a fala e a transição.
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync, openSync, readSync, closeSync, readdirSync } from "fs";
import { join } from "path";

const TTL = 15 * 1000;   // cache curto: troca de modelo aparece no próximo evento de hook

const SESSIONS_DIR = process.platform === "darwin"
  ? join(process.env.HOME || "", "Library", "Application Support", "Claude", "claude-code-sessions")
  : join(process.env.APPDATA || "", "Claude", "claude-code-sessions");

// acha (e memoriza) o local_*.json da sessão no app desktop; lê o "model" dele
function desktopModel(baseDir, sid) {
  const CACHE = join(baseDir, ".model-paths.json");
  let cache = {};
  try { cache = JSON.parse(readFileSync(CACHE, "utf8")); } catch { /* primeira vez */ }
  let file = cache[sid];
  const readModelField = (p) => {
    const raw = readFileSync(p, "utf8");
    if (!raw.includes(`"cliSessionId":"${sid}"`)) return null;      // arquivo reciclado p/ outra sessão
    return (raw.match(/"model"\s*:\s*"([^"]+)"/) || [])[1] || "";
  };
  if (file) {
    try { const id = readModelField(file); if (id !== null) return id; } catch { /* sumiu -> re-varre */ }
  }
  // varredura (1x por sessão): mesma dos títulos, profundidade 3
  let found = "", foundPath = "";
  const walk = (d, depth) => {
    if (foundPath || depth > 3) return;
    let ents = []; try { ents = readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of ents) {
      if (foundPath) return;
      const p = join(d, e.name);
      if (e.isDirectory()) walk(p, depth + 1);
      else if (e.name.endsWith(".json")) {
        try { const id = readModelField(p); if (id !== null) { foundPath = p; found = id; } } catch { /* em uso */ }
      }
    }
  };
  walk(SESSIONS_DIR, 0);
  if (foundPath) {
    cache[sid] = foundPath;
    try { writeFileSync(CACHE, JSON.stringify(cache)); } catch { /* ok */ }
  }
  return found;
}

// fallback: cauda do transcript (últimos ~48KB) — vale o ÚLTIMO "model":"..."
function transcriptModel(transcriptPath) {
  try {
    const size = statSync(String(transcriptPath || "")).size;
    const want = Math.min(size, 48 * 1024);
    const fd = openSync(String(transcriptPath), "r");
    const buf = Buffer.alloc(want);
    readSync(fd, buf, 0, want, size - want);
    closeSync(fd);
    const all = [...buf.toString("utf8").matchAll(/"model"\s*:\s*"([a-z0-9.:_-]+)"/gi)];
    return all.length ? all[all.length - 1][1] : "";
  } catch { return ""; }
}

export function sessionModel(baseDir, sessionId, transcriptPath) {
  const sid = String(sessionId || "").replace(/[^A-Za-z0-9_-]/g, "");
  if (!sid) return "";
  const dir = join(baseDir, "hud-sessions", sid);
  const file = join(dir, "model.txt");
  let cached = "";
  try {
    cached = (readFileSync(file, "utf8").split("\n")[0] || "").trim();
    if (cached && Date.now() - statSync(file).mtimeMs < TTL) return cached;
  } catch { /* sem cache ainda */ }
  let id = "";
  try { id = desktopModel(baseDir, sid); } catch { /* CLI puro / sem app desktop */ }
  if (!id) id = transcriptModel(transcriptPath);
  if (!id) return cached;
  try {
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true });
    if (cached && id !== cached) writeFileSync(join(dir, "model-prev"), cached);   // marcador de TROCA
    writeFileSync(file, id + "\n");
  } catch { /* cache é conveniência, não requisito */ }
  return id;
}

export function isFable(modelId) {
  return /fable/i.test(String(modelId || ""));
}
