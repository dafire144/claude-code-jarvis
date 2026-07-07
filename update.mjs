// update.mjs — atualiza o J.A.R.V.I.S. no lugar, em um comando.
// Uso: node update.mjs   (rode de dentro da pasta da instalação)
// - Windows: encerra as telinhas (o .exe fica travado enquanto abertas).
// - Se a pasta é um repo git: git pull.
// - Senão: baixa o zip mais recente do GitHub, extrai e copia POR CIMA
//   (preserva o estado local: .cooldowns.json, .titles.json, hud-sessions, queue…).
import { existsSync, mkdirSync, writeFileSync, cpSync, rmSync, readFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";
import { tmpdir } from "os";
import { execSync, spawnSync } from "child_process";

const __dir = dirname(fileURLToPath(import.meta.url));
const isWin = process.platform === "win32";
const ZIP = "https://github.com/dafire144/claude-code-jarvis/archive/refs/heads/main.zip";

const say = (m) => console.log(`[J.A.R.V.I.S.] ${m}`);
say(`Atualizando a instalação em: ${__dir}`);

// 1) Windows: encerra as telinhas nativas (senão o .exe não pode ser sobrescrito)
if (isWin) {
  try {
    spawnSync("powershell", ["-NoProfile", "-Command", "Stop-Process -Name jarvis-hud-wf -Force -ErrorAction SilentlyContinue"], { stdio: "ignore" });
    say("Telinhas encerradas (reabrem sozinhas no próximo evento).");
  } catch { /* nenhuma aberta */ }
}

// 2) repo git? git pull. senão, zip + extração + cópia por cima.
if (existsSync(join(__dir, ".git"))) {
  say("Repositório git detectado — puxando as novidades…");
  try {
    execSync("git fetch origin", { cwd: __dir, stdio: "inherit" });
    execSync("git pull origin main", { cwd: __dir, stdio: "inherit" });
    say("Atualizado via git. ✅");
  } catch (e) {
    say(`git pull falhou: ${e.message}`);
    say("Resolva conflitos manualmente ou apague a pasta e reinstale.");
    process.exit(1);
  }
} else {
  say("Sem git — baixando o pacote mais recente…");
  const tmp = join(tmpdir(), `jarvis-update-${process.pid}`);
  const zipPath = join(tmp, "jarvis.zip");
  try {
    mkdirSync(tmp, { recursive: true });
    const res = await fetch(ZIP);
    if (!res.ok) throw new Error(`download falhou (HTTP ${res.status})`);
    writeFileSync(zipPath, Buffer.from(await res.arrayBuffer()));
    // tar é nativo no Windows 10+ (bsdtar) e no macOS — extrai o zip sem dependência
    execSync(`tar -xf "${zipPath}" -C "${tmp}"`, { stdio: "inherit" });
    const src = join(tmp, "claude-code-jarvis-main");
    if (!existsSync(src)) throw new Error("estrutura do zip inesperada");
    // copia por cima: sobrescreve os arquivos do projeto, preserva o estado local
    cpSync(src, __dir, { recursive: true, force: true });
    say("Atualizado via pacote. ✅");
  } catch (e) {
    say(`Falha ao atualizar pelo pacote: ${e.message}`);
    process.exit(1);
  } finally {
    try { rmSync(tmp, { recursive: true, force: true }); } catch { /* ok */ }
  }
}

let ver = "?";
try { ver = readFileSync(join(__dir, "VERSION"), "utf8").trim(); } catch { /* ok */ }
say(`Pronto. Versão agora: ${ver}. Reinicie o Claude Code para carregar as novidades.`);
