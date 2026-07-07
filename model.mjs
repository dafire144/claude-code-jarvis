// model.mjs — descobre o MODELO da sessão (base do modo FABLE 5).
// Fonte primária: hud-sessions/<sid>/model.txt (o statusline grava a cada render,
// mantendo o mtime fresco). Fallback sem statusline: fareja a cauda do transcript
// da sessão (os hooks recebem transcript_path; as mensagens do assistente carregam
// "model") e grava o mesmo cache pro HUD ler. Custo: 1 stat + 1 read pequeno.
import { readFileSync, writeFileSync, statSync, existsSync, mkdirSync, openSync, readSync, closeSync } from "fs";
import { join } from "path";

const TTL = 5 * 60 * 1000;   // cache velho = re-fareja (cobre troca de modelo no meio da sessão)

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
  // cauda do transcript (últimos ~48KB): vale o ÚLTIMO "model":"..." que aparecer
  let id = "";
  try {
    const size = statSync(String(transcriptPath || "")).size;
    const want = Math.min(size, 48 * 1024);
    const fd = openSync(String(transcriptPath), "r");
    const buf = Buffer.alloc(want);
    readSync(fd, buf, 0, want, size - want);
    closeSync(fd);
    const all = [...buf.toString("utf8").matchAll(/"model"\s*:\s*"([a-z0-9.:_-]+)"/gi)];
    if (all.length) id = all[all.length - 1][1];
  } catch { /* sem transcript = fica no cache */ }
  if (!id) return cached;
  try {
    if (!existsSync(dir)) mkdirSync(dir, { recursive: true });
    writeFileSync(file, id + "\n");
  } catch { /* cache é conveniência, não requisito */ }
  return id;
}

export function isFable(modelId) {
  return /fable/i.test(String(modelId || ""));
}
