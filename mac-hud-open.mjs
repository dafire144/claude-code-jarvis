// Abre a telinha Electron `delay`s apos o prompt, SE a sessao ainda estiver ativa (macOS).
// Espelha o hud-open-delayed.ps1 do Windows: abertura baseada em TEMPO desde o prompt (nao na
// cadencia de ferramentas), robusto p/ tarefas que ficam muito "pensando". Lancado destacado
// (detached) pelo hud-native.mjs no UserPromptSubmit. No macOS o processo destacado sobrevive.
// Sessao RECEM-criada pode nao ter titulo no momento do prompt (o app grava async; corrida de
// 07/07): sem meta.txt, RE-SONDA o titulo (title-probe.mjs) a cada 30s, ate 4 vezes.
import { existsSync, statSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { spawn } from "child_process";
import { probeTitle } from "./title-probe.mjs";

const __dir = dirname(fileURLToPath(import.meta.url));
const [dir, sid, delayS] = process.argv.slice(2);
const delay = (Number(delayS) || 30) * 1000;
const RETRY = 30000, MAXTRIES = 4;
let tries = 0;

function attempt() {
  tries++;
  try {
    if (!dir || !existsSync(dir)) return;                                   // sessao limpa
    if (existsSync(join(dir, "done")) || existsSync(join(dir, "end")) || existsSync(join(dir, "closed"))) return;  // acabou / fechada
    try { if (Date.now() - statSync(join(dir, "hb")).mtimeMs < 6000) return; } catch (e) {}  // telinha ja viva
    if (!existsSync(join(dir, "meta.txt"))) {                               // titulo ainda nao resolvido: sonda
      const t = probeTitle(sid, dir);
      if (!t) { if (tries < MAXTRIES) setTimeout(attempt, RETRY); return; } // titulo pode nascer em instantes
    }
    const bin = join(__dir, "hud-electron", "node_modules", ".bin", "electron");
    if (!existsSync(bin)) return;                                           // precisa `npm install` em hud-electron/
    const c = spawn(bin, [join(__dir, "hud-electron"), sid], { detached: true, stdio: "ignore" });
    c.unref();
  } catch (e) { /* ok */ }
}
setTimeout(attempt, delay);
