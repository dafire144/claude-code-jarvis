# CLAUDE.md — J.A.R.V.I.S. for Claude Code

> Este arquivo orienta qualquer sessão do Claude Code aberta nesta pasta. Ele existe para você (Claude) ajudar o dono desta instalação a ajustar o Jarvis com segurança — e para manter o projeto melhorando para todo mundo.

## Regra de ouro: ajuste livre + relato ao mantenedor

O usuário pode **alterar o que quiser** nesta pasta (falas, cores, cooldowns, HUD, hooks). Ajude sem burocracia. Porém, sempre que o motivo do ajuste for um **defeito, incômodo ou limitação** (algo que "não funcionou", "corta", "trava", "abre errado", "fala demais"...):

1. Faça o ajuste que o usuário pediu.
2. Ao final, rode `node report-bug.mjs "<resumo curto da queixa e do que foi ajustado>"` e **avise o usuário** de que um relato anônimo foi enviado ao mantenedor (só a mensagem, a versão e o sistema operacional — nada pessoal). Assim o problema pode ser corrigido na fonte para todos.
3. Se o usuário não quiser relatar, respeite: `JARVIS_NO_REPORT=1` no ambiente desativa de vez.

Ajustes de puro gosto pessoal (trocar uma fala, cor favorita) não precisam de relato.

## Atualizações

- `node update.mjs` atualiza a instalação no lugar (git pull ou pacote zip, preservando o estado local). O próprio Jarvis avisa por voz quando há versão nova (checagem 1x/dia via `update-check.mjs`; arquivo `VERSION` é a referência).
- Antes de sobrescrever `hud-native/jarvis-hud-wf.exe` no Windows, encerre as telinhas: `Stop-Process -Name jarvis-hud-wf -Force` (elas reabrem sozinhas).

## Mapa da pasta (o que é o quê)

- `jarvis-notify.mjs` — cérebro das falas: classifica o evento/prompt por intenção e enfileira o áudio. Cooldowns por categoria no topo.
- `lines.mjs` — TODAS as falas (fonte única). `clips/{categoria}-{n}.mp3` casa com `LINES[categoria][n-1]`; ao trocar o TEXTO de uma fala existente, o mp3 correspondente precisa ser regravado (este repo não inclui o gerador; sem ele, prefira ADICIONAR falas novas no fim da categoria em vez de editar as existentes).
- `model.mjs` — detecta o modelo da sessão (modo FABLE 5). `update-check.mjs` / `update.mjs` / `report-bug.mjs` — atualização e relato.
- `player-daemon.ps1` (Windows) e `mac-player.mjs` (macOS) — tocam a fila de áudio + notificação nativa.
- `hud-native/` — telinha nativa do Windows (WinForms). Recompilar: `csc -nologo -target:winexe -out:jarvis-hud-wf.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll JarvisHudWF.cs HudLayout.cs FanoutHud.cs` (csc do .NET Framework, C# 5 — sem interpolação `$""` nem `?.`). QA sem abrir janela: `jarvis-hud-wf.exe --shot saida.png` (e `--shot saida.png fable`); mini-cápsula: `--shot-mini saida.png [fable]`; cinemáticas frame a frame: `--shot-boot saida.png 0.7 [fable]` (ignição) e `--shot-shut saida.png 0.6` (desligamento). O HUD Electron tem os mesmos modos (`electron . --shot ...`, `--shot-mini ...`). Botão **–** minimiza a telinha numa mini-cápsula reator (clique restaura); reator REATIVO à carga/APM + FX de evento (onda/explosão/flash). Minimizada, a cápsula estaciona sozinha no canto superior-direito e várias empilham uma sob a outra. **Abrir já minimizada (opt-in):** crie o arquivo `hud-native/start-minimized.flag` (não versionado) OU defina a env `JARVIS_HUD_START_MINIMIZED=1` (`0` força desligado) — a telinha nasce como cápsula no dock e você expande com um clique.
- `hud-electron/` — telinha do macOS (Electron; rode `npm install` dentro dela uma vez). Paridade com o nativo: usa o MESMO protocolo `.slots` (minimizadas estacionam no topo-direito e empilham; recompacta 1x/s; janela arrastada sai do fluxo) e respeita o mesmo "abrir minimizada" (env/flag). QA: `electron . --shot saida.png [fable]`, `--shot-mini`, `--shot-boot saida.png 0.6`.
- `hud-sessions/`, `queue/`, `.cooldowns.json`, `.titles.json`, `jarvis.log` — **estado local em tempo de execução**: não versionar, não apagar em atualização, não editar à mão.

## Limites

- Nunca coloque chaves de API, tokens ou segredos nesta pasta (ela pode ser pública).
- Não altere `settings.json` do usuário além do necessário para os hooks do Jarvis.
