// Abre a telinha Electron `delay`s apos o prompt, SE a sessao ainda estiver ativa (macOS).
// Espelha o hud-open-delayed.ps1 do Windows: abertura baseada em TEMPO desde o prompt (nao na
// cadencia de ferramentas), robusto p/ tarefas que ficam muito "pensando". Lancado destacado
// (detached) pelo hud-native.mjs no UserPromptSubmit. No macOS o processo destacado sobrevive.
import { existsSync, statSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { spawn } from "child_process";

const __dir = dirname(fileURLToPath(import.meta.url));
const [dir, sid, delayS] = process.argv.slice(2);
const delay = (Number(delayS) || 30) * 1000;

setTimeout(() => {
  try {
    if (!dir || !existsSync(dir)) return;                                   // sessao limpa
    if (existsSync(join(dir, "done")) || existsSync(join(dir, "end")) || existsSync(join(dir, "closed"))) return;  // acabou / fechada
    try { if (Date.now() - statSync(join(dir, "hb")).mtimeMs < 6000) return; } catch (e) {}  // telinha ja viva
    const bin = join(__dir, "hud-electron", "node_modules", ".bin", "electron");
    if (!existsSync(bin)) return;                                           // precisa `npm install` em hud-electron/
    const c = spawn(bin, [join(__dir, "hud-electron"), sid], { detached: true, stdio: "ignore" });
    c.unref();
  } catch (e) { /* ok */ }
}, delay);
