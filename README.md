# J.A.R.V.I.S. for Claude Code

> A butler-voiced assistant layer for [Claude Code](https://claude.com/claude-code): it **speaks** to you, mirrors every event as a **native notification**, and runs a live **reactor-core HUD** on your desktop — one window per session, with a rather cinematic power-down animation when it's done.

<p align="center">
  <img src="docs/hud.png" width="420" alt="Jarvis HUD — reactor core, telemetry feed and session metrics" />
  &nbsp;
  <img src="docs/shutdown.png" width="420" alt="Jarvis HUD cooling down / shutting off" />
</p>

It hooks into Claude Code's event system (no polling, no cost) and reacts with pre-recorded lines: *"Positivo, senhor. Iniciando o trabalho."* when you send a prompt, *"Tarefa concluída, senhor."* when it finishes, energetic lines when you tell it to floor it, a gentle nudge at 3 a.m., and so on — **321 lines across 35 categories**, chosen by intent.

> The voice is a **Brazilian-Portuguese butler** (think Iron Man's JARVIS, but he calls you *"senhor"*). Fully pre-recorded, so playback is instant and free.

---

## What it does

- **Speaks** — picks a line by what you're doing (prompt / question / done / error / deploy / git / test / research / late-night / greetings…), never repeating the same line twice in a row.
- **Native notifications** — every line is mirrored to the OS notification center (Windows toast / macOS Notification Center) with the same text.
- **Robotic blip** — a short synth lead-in before each line.
- **Live desktop HUD** *(Windows)* — a frameless, always-on-top mini-panel per session: animated arc-reactor core emanating light, uptime, actions, APM + trend, activity sparkline, and a color-coded telemetry feed of what Claude is doing.
- **Cinematic power-down** *(Windows)* — when a task ends the HUD **cools down** (amber → steel-blue, every element recoloring), **collapses like an old CRT**, and blinks out.
- **FABLE 5 overdrive mode** — when a session runs Anthropic's most powerful model (Claude Fable 5), the whole HUD goes into **overheat**: heated background, border pulsing between gold and ember, white-hot reactor core with 16 rays and 3 orbiting satellites, molten metrics, a shimmering **"✦ FABLE 5"** badge and a *"PLENA CARGA"* status — plus **24 dedicated voice lines**, delivered fully in character: Jarvis treats Fable 5 as his own **hidden full-power protocol**, unlocked only for top-priority work (*"Protocolo Fable 5 autorizado, senhor. Desviando toda a energia do reator para o senhor."*). Detection is automatic: the status line records the session model, with a transcript-sniffing fallback if you don't use it.
- **Status line** — an optional Claude Code status line with model, folder, branch, live cost and clock (it also powers the Fable 5 detection, and marks the model with a golden ✦ when Fable is running).

<p align="center">
  <img src="docs/hud-fable.png" width="420" alt="Jarvis HUD in FABLE 5 overdrive — overheated palette, white-gold reactor, FABLE 5 badge" />
</p>

## Platform support

| Feature | Windows | macOS |
|---|:---:|:---:|
| Butler voice | yes | yes |
| Native notifications | yes (WinRT toast) | yes (osascript) |
| Robotic blip | yes | yes |
| Live desktop HUD + power-down animation | yes (native) | yes (Electron) |
| FABLE 5 overdrive (HUD + voice) | yes | yes |
| Status line | yes | yes |

The desktop HUD ships two ways for the best of both worlds: a lightweight **native WinForms/GDI+** app on Windows, and a **cross-platform Electron** build on macOS — same reactor core, telemetry feed and cinematic power-down. Everyone gets the same experience.

## Requirements

- [Claude Code](https://claude.com/claude-code) and **Node.js** (any recent version).
- **Windows:** .NET Framework (ships with Windows) for the prebuilt HUD `.exe`; PowerShell (built-in) for audio + toasts.
- **macOS:** `afplay` and `osascript` (built in). For the desktop HUD, run `npm install` once inside `hud-electron/` (pulls Electron).

No API keys. No servers. No paid dependencies.

## Install

1. **Get the files** — clone or download this repo into your Claude config folder:
   ```bash
   git clone https://github.com/dafire144/claude-code-jarvis.git ~/.claude/jarvis
   ```
   *(Any folder works; `~/.claude/jarvis` is just tidy. On Windows that's `C:\Users\<you>\.claude\jarvis`.)*

2. **Wire the hooks** — open [`settings.example.json`](settings.example.json), copy the `"hooks"` block into your `~/.claude/settings.json` (merge if you already have hooks), and replace `__JARVIS_DIR__` with the **absolute path** to this folder (use forward slashes, even on Windows).

3. **Windows only — branded toasts:** run once for the "J.A.R.V.I.S." toast identity (Start-menu shortcut + arc-reactor icon):
   ```powershell
   powershell -ExecutionPolicy Bypass -File setup-toast.ps1
   ```

4. **Restart Claude Code.** Send a prompt — he should greet you.

To turn it off, remove the `hooks` block (or the lines you don't want). Every hook is independent.

## How it works

Claude Code fires **hooks** on events (prompt submitted, tool used, response finished, session ended…). Each hook runs a tiny Node script:

- **`jarvis-notify.mjs`** classifies the event/prompt by intent, picks a matching line from **`lines.mjs`**, drops it on a queue, and wakes a player. The player (PowerShell daemon on Windows, `mac-player.mjs` on macOS) drains the queue one line at a time and fires the notification in sync with the audio.
- **`hud-native.mjs`** tracks a per-session activity feed and launches/updates the HUD window (opens once a task has been running ~30s; closes ~20s after the task ends, with the power-down animation).

All state is local and ephemeral; nothing leaves your machine.

## Customizing the voice

The 339 `.mp3` clips in [`clips/`](clips/) are pre-generated (ElevenLabs). This public build **does not include the generator or any API key**, so the lines are fixed. Want your own voice or language? Edit [`lines.mjs`](lines.mjs) and regenerate the clips with your own ElevenLabs key — the mapping is `clips/{category}-{index}.mp3`. (A generator script is intentionally left out here to keep the repo key-free.)

## Roadmap

- **Linux HUD** — the Electron HUD should run on Linux too; needs testing.
- Optional English voice pack.
- One-command installer that wires the hooks for you.

## Notes

- Written and tested on **Windows**. The macOS voice/notification path is written carefully but **not yet verified on a real Mac** — please open an issue if something misbehaves.
- "J.A.R.V.I.S." is a nod to the Iron Man films; this is a personal, non-commercial fan project and is not affiliated with or endorsed by Marvel/Disney.

## License

[MIT](LICENSE) © Davi Lopes — built with [Claude Code](https://claude.com/claude-code).
