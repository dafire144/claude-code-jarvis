# CLAUDE.md — J.A.R.V.I.S. for Claude Code

> Este arquivo orienta qualquer sessão do Claude Code aberta nesta pasta. Ele existe para você (Claude) ajudar o dono desta instalação a ajustar o Jarvis com segurança — e para manter o projeto melhorando para todo mundo.

## Regra de ouro: ajuste livre + relato ao mantenedor

O usuário pode **alterar o que quiser** nesta pasta (falas, cores, cooldowns, HUD, hooks). Ajude sem burocracia. Porém, sempre que o motivo do ajuste for um **defeito, incômodo ou limitação** (algo que "não funcionou", "corta", "trava", "abre errado", "fala demais"...):

0. **Antes de mexer no código, rode `node doctor.mjs --json`** e leia o resultado — a maioria das queixas ("não ouço nada", "não abre a telinha") é configuração, não defeito: hooks desligados, clipe faltando, Protocolo Silêncio ativo, toast sem identidade. O doctor aponta o conserto (e `node doctor.mjs --fix` repara os seguros).
1. Faça o ajuste que o usuário pediu.
2. Ao final, rode `node report-bug.mjs "<resumo curto da queixa e do que foi ajustado>"` e **avise o usuário** de que um relato anônimo foi enviado ao mantenedor (só a mensagem, a versão e o sistema operacional — nada pessoal). Assim o problema pode ser corrigido na fonte para todos.
3. Se o usuário não quiser relatar, respeite: `JARVIS_NO_REPORT=1` no ambiente desativa de vez.

Ajustes de puro gosto pessoal (trocar uma fala, cor favorita) não precisam de relato.

## Atualizações

- `node update.mjs` atualiza a instalação no lugar (git pull ou pacote zip, preservando o estado local). O próprio Jarvis avisa por voz quando há versão nova (checagem 1x/dia via `update-check.mjs`; arquivo `VERSION` é a referência).
- Antes de sobrescrever `hud-native/jarvis-hud-wf.exe` no Windows, encerre as telinhas: `Stop-Process -Name jarvis-hud-wf -Force` (elas reabrem sozinhas).

## Mapa da pasta (o que é o quê)

- `jarvis.mjs` — cockpit de linha de comando: `status` (padrão), `doctor`, `mute`/`unmute`, `quiet`, `test <categoria>`, `lines`, `update`.
- `doctor.mjs` — autodiagnóstico da instalação (`--json` pra você ler, `--fix` conserta o seguro). Rode SEMPRE antes de caçar defeito.
- `install.mjs` — instalador/religador dos hooks no settings.json (idempotente, com backup; preserva hooks de outras ferramentas).
- `jarvis-notify.mjs` — cérebro das falas: classifica o evento/prompt por intenção e enfileira o áudio. Cooldowns por categoria no topo. **Protocolo Silêncio:** "silêncio" no chat (ou `.mute` via CLI) cala a voz — confirmação única, alertas críticos furam; "pode falar" desmuta; `quiet.cfg`/env `JARVIS_QUIET` dão horário de silêncio diário.
- `lines.mjs` — TODAS as falas (fonte única). `clips/{categoria}-{n}.mp3` casa com `LINES[categoria][n-1]`; ao trocar o TEXTO de uma fala existente, o mp3 correspondente precisa ser regravado (este repo não inclui o gerador; sem ele, prefira ADICIONAR falas novas no fim da categoria em vez de editar as existentes).
- `model.mjs` — detecta o modelo da sessão (modo FABLE 5). `update-check.mjs` / `update.mjs` / `report-bug.mjs` — atualização e relato.
- `player-daemon.ps1` (Windows) e `mac-player.mjs` (macOS) — tocam a fila de áudio + notificação nativa.
- `hud-native/` — telinha nativa do Windows (WinForms). Recompilar: `csc -nologo -target:winexe -out:jarvis-hud-wf.exe -r:System.Windows.Forms.dll -r:System.Drawing.dll JarvisHudWF.cs HudLayout.cs FanoutHud.cs` (csc do .NET Framework, C# 5 — sem interpolação `$""` nem `?.`). QA sem abrir janela: `jarvis-hud-wf.exe --shot saida.png` (e `--shot saida.png fable`); mini-cápsula: `--shot-mini saida.png [fable]`; cinemáticas frame a frame: `--shot-boot saida.png 0.7 [fable]` (ignição) e `--shot-shut saida.png 0.6` (desligamento). O HUD Electron tem os mesmos modos (`electron . --shot ...`, `--shot-mini ...`). Botão **–** minimiza a telinha numa mini-cápsula reator (clique restaura); o **botão flutuante** ao lado da primeira cápsula do dock é um INTERRUPTOR esconder/mostrar: clique 1 esconde TODAS as telinhas (marcador `.slots/.hideall`; o botão vira pílula com a contagem), clique 2 traz todas de volta onde estavam (janelinha própria `--minall-btn`, instância única, mantida viva pelas telinhas via heartbeat `.slots/.btn-hb`; QA `--btn-shot saida.png [hidden]`); reator REATIVO à carga/APM + FX de evento (onda/explosão/flash). Minimizada, a cápsula estaciona sozinha no canto superior-direito e várias empilham uma sob a outra. **Abrir já minimizada (opt-in):** crie o arquivo `hud-native/start-minimized.flag` (não versionado) OU defina a env `JARVIS_HUD_START_MINIMIZED=1` (`0` força desligado) — a telinha nasce como cápsula no dock e você expande com um clique. **Posição do dock ajustável:** crie `hud-native/hud-dock.cfg` (não versionado) com linhas `top=NN` (distância do topo) e `right=NN` (margem da direita) — é relido ~1x/s, então editar o arquivo reposiciona as telinhas ao vivo; sem o arquivo, o padrão é `top=42`/`right=12`.
- `hud-electron/` — telinha do macOS (Electron; rode `npm install` dentro dela uma vez). Paridade com o nativo: usa o MESMO protocolo `.slots` (minimizadas estacionam no topo-direito e empilham; recompacta 1x/s; janela arrastada sai do fluxo) e respeita o mesmo "abrir minimizada" (env/flag). QA: `electron . --shot saida.png [fable]`, `--shot-mini`, `--shot-boot saida.png 0.6`.
- `hud-sessions/`, `queue/`, `.cooldowns.json`, `.titles.json`, `jarvis.log` — **estado local em tempo de execução**: não versionar, não apagar em atualização, não editar à mão.

## Limites

- Nunca coloque chaves de API, tokens ou segredos nesta pasta (ela pode ser pública).
- Não altere `settings.json` do usuário além do necessário para os hooks do Jarvis.
