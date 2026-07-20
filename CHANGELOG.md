# Changelog — J.A.R.V.I.S. for Claude Code

Every installed Jarvis announces new versions by voice (daily check). Update in place with `node update.mjs`.

## 1.5.3 — 2026-07-20 · Clicks behave like clicks

- Fixed a sneaky input bug that made capsule clicks unreliable: Windows fires a spurious zero-delta mouse-move right after mouse-down, which the HUD interpreted as a **0-pixel drag** — so clicking a capsule to expand it would sometimes silently release the window from the dock instead. Now only real movement counts as dragging.
- This was the "minimize-all button doesn't work" ghost: the button always delivered its broadcast, but erratic capsule clicks made the whole dock feel broken. Verified end-to-end: expand by click, one button click, everything folds back.

## 1.5.2 — 2026-07-20 · The minimize-all button becomes its own thing

- "Minimize all" is now a **dedicated floating button**: a small always-on-top reactor-styled disc that lives right beside the **first capsule of the dock** (to its right when there's room, to its left otherwise) and does exactly one thing — one click and every open panel folds into its mini capsule.
- It's always within reach even when every window is already minimized (the in-panel chevron only existed on expanded panels — useless if your windows start minimized). The panel button was removed; panels are back to **–** and **×**.
- Self-managing: the HUD windows keep it alive via a heartbeat (single instance, auto-respawn), it follows the dock as windows come and go, appears with the first window and retires a minute after the last one closes.

## 1.5.1 — 2026-07-20 · Minimize all, simply

- The double-chevron button now **minimizes every HUD window at once** — each panel folds into its live mini reactor capsule and stacks in the dock. One click to declutter, each capsule still one click from expanding.
- This **replaces** the 1.4.5 "collapse all" (hide everything + a restore pill): windows no longer vanish off-screen — you always see your sessions.
- Under the hood: a one-shot broadcast stamp (`.slots/.minall`); windows born later ignore old stamps. Layout self-test still at 48 scenarios, zero overlaps.

## 1.5.0 — 2026-07-19 · "The Attentive Butler"

- **Silence Protocol** — say *"silêncio"* in chat (or `node jarvis.mjs mute 2h`) and the voice goes quiet after a single in-character confirmation; *"pode falar"* brings it back. Supports durations (*"silêncio por 30 minutos"*, *"até segunda ordem"*) and daily **quiet hours** (`quiet 22-07`). Critical reserve alerts still speak.
- **Self-diagnosis** — new `doctor.mjs`: sweeps hooks, clips, local state, queue, HUD, toast identity and remote version; prints a fix hint per finding; `--fix` repairs the safe ones; `--json` for machine use.
- **CLI cockpit** — new `jarvis.mjs`: status panel + front door to `doctor`, `mute`/`unmute`, `quiet`, `test <category>`, `lines`, `update`, `version`.
- **One-command installer** — new `install.mjs`: wires the hooks into `~/.claude/settings.json` with a backup, idempotently, preserving your other hooks; sets up the Windows toast identity and the macOS Electron HUD. (Closes the roadmap item.)
- **16 new voice lines** in 4 categories (`mute_on`, `mute_off`, `diag_ok`, `diag_bad`) — the doctor speaks his verdict.
- Library now at **363 lines / 43 categories / 377 clips**.

## 1.4.x — 2026-07-09 → 07-10 · Dock hardening & window management

- **1.4.5** — "collapse all" button on every capsule: hides all windows behind a single reactor pill; click restores everything.
- **1.4.4** — every window (session + fan-out) can be born minimized (opt-in flag/env).
- **1.4.3** — fan-out (agent) windows minimize into mini-capsules too.
- **1.4.0–1.4.2** — deep-audit hardening of the dock: 60 ms reflow, atomic slot writes, staircase over-capacity, model-skin race fix, boot-freeze regression fix, extended self-tests (48 scenarios, zero overlap).

## 1.3.x — 2026-07-07 → 07-09 · The living reactor

- **1.3.9** — the QA agent got its own voice and HUD animations (simple inspection with a magnifying glass; deep audit with a radar-swept board).
- **1.3.8** — accordion dock: deterministic packing, column overflow, morphs that reserve their footprint (no overlap), `--layout-test` self-test.
- **1.3.5–1.3.7** — fast dock recompaction, live-configurable dock position (`hud-dock.cfg`), fan-out HUD at a smooth 30 fps.
- **1.3.2–1.3.4** — minimized capsules auto-dock top-right and stack; optional "start minimized"; full macOS (Electron) parity.
- **1.3.0** — the reactor core reacts to real session activity (spin/glow/electric discharges), cinematic event FX (shockwave on telemetry, golden burst on completion), and **minimize to a live mini reactor capsule**.

## 1.2.x — 2026-07-07 · Protocol Cinema

- Cinematic **CRT ignition** on open and refined power-down with glitch slices; holographic glass (scanlines, vignette, blueprint grid); instrument realism (status capsule with breathing LED, real model tag, VU peak-hold, sparkline halo); livelier reactor; physical jolt on model transitions.
- **1.2.1** — cached-background rendering: double the framerate at lower CPU; HUD always opens on the first prompt (title race fixed).

## 1.1.0 — 2026-07-07 · Self-updating

- Daily version check with an in-character announcement; `update.mjs` one-command update; anonymous opt-out bug reports; instant model detection; cinematic FABLE 5 ⇄ normal transition with dedicated lines.

## 1.0.0 — 2026-07-04 · First public release

- Butler voice (pre-recorded PT-BR lines chosen by intent), native notifications in sync with the audio, robotic blip, per-session desktop HUD with reactor core and telemetry feed, fan-out mission windows, auto-layout of all windows, FABLE 5 overdrive mode.
