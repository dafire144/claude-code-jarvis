# J.A.R.V.I.S. - tocador com FILA (queue). Uma unica instancia (mutex) drena a fila:
# 1 fala por vez, 1s de intervalo entre falas. Prefixo "De <sessao>" so quando a
# sessao que fala muda. Lancado (invisivel) pelo jarvis-notify.mjs via WMI.
param([string]$Dir)
$ErrorActionPreference = "SilentlyContinue"
Add-Type -AssemblyName presentationCore

# toast nativo do Windows (espelha a fala): dot-source define Show-JarvisToast
$ToastPs1 = Join-Path $Dir "toast.ps1"
if (Test-Path $ToastPs1) { try { . $ToastPs1 } catch {} }

# som robótico de notificação (blip lead-in antes da fala). Apagar o arquivo = desliga.
$BlipFile = Join-Path $Dir "assets\robot-blip.wav"

$QueueDir        = Join-Path $Dir "queue"
$LastSessionFile = Join-Path $Dir ".last-session"
$GapMs   = 1000    # intervalo entre uma fala e a proxima
$GraceMs = 4000    # apos esvaziar a fila, espera um pouco por novas antes de sair
$MaxAgeMs = 60000  # descarta falas enfileiradas ha mais de 60s (evita enxurrada atrasada)

# instancia unica: se ja existe um daemon drenando, este sai na hora
$mutex = New-Object System.Threading.Mutex($false, "JarvisPlayerMutex")
$owned = $false
try { $owned = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
if (-not $owned) { return }

$lastSession = ""
try { $lastSession = ((Get-Content -Raw $LastSessionFile) -replace "﻿", "").Trim() } catch {}

$LogFile = Join-Path $Dir "jarvis.log"
function Log([string]$m) { try { Add-Content -Path $LogFile -Value ((Get-Date).ToUniversalTime().ToString("o") + " daemon: " + $m) } catch {} }
function Now-Ms { return [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }

# Toca prefixo (opcional) + fala principal EMENDADOS (pre-carga paralela, sem gap interno).
function Play-Item([string]$prefixFile, [string]$mainFile) {
  # blip robotico (lead-in). WAV curto -> System.Media.SoundPlayer.PlaySync (confiavel e
  # sincrono). O WPF MediaPlayer NAO carregava o WAV em 250ms (NaturalDuration vazia) e o
  # blip nao tocava (bug 04/07). SoundPlayer bloqueia ate o blip terminar; a voz vem logo apos.
  if ($BlipFile -and (Test-Path $BlipFile)) {
    try { $sp = New-Object System.Media.SoundPlayer $BlipFile; $sp.PlaySync(); $sp.Dispose() } catch {}
  }
  $main = New-Object System.Windows.Media.MediaPlayer
  $main.Open([System.Uri]$mainFile)
  $pre = $null
  if ($prefixFile -and (Test-Path $prefixFile)) {
    $pre = New-Object System.Windows.Media.MediaPlayer
    $pre.Open([System.Uri]$prefixFile)
  }
  Start-Sleep -Milliseconds 600   # bufferiza os dois em paralelo
  if ($pre) {
    $d = 2.0; if ($pre.NaturalDuration.HasTimeSpan) { $d = $pre.NaturalDuration.TimeSpan.TotalSeconds }
    $pre.Play(); Start-Sleep -Milliseconds ([int]($d * 1000) + 80); $pre.Stop(); $pre.Close()
  }
  $d = 4.0; if ($main.NaturalDuration.HasTimeSpan) { $d = $main.NaturalDuration.TimeSpan.TotalSeconds }
  $main.Play(); Start-Sleep -Milliseconds ([int]($d * 1000) + 150); $main.Stop(); $main.Close()
}

$LastEndFile = Join-Path $Dir ".last-sessionend"
$HoldStartMs = 6000     # boas-vindas ficam "de molho" antes de tocar (cancela se o app estiver fechando)
$DedupeMs = 10000       # trava anti-corrida: mesma categoria (start/end) 1x por 10s
$lastCatPlay = @{}

try {
  $idle = 0
  while ($true) {
    $items = Get-ChildItem -Path $QueueDir -Filter *.json -ErrorAction SilentlyContinue | Sort-Object Name
    if (-not $items -or $items.Count -eq 0) {
      if ($idle -ge $GraceMs) { break }
      Start-Sleep -Milliseconds 500; $idle += 500; continue
    }
    $idle = 0
    $it = $items[0]
    $data = $null
    try { $data = Get-Content -Raw -Encoding UTF8 $it.FullName | ConvertFrom-Json } catch {}
    if ($null -eq $data) { Remove-Item $it.FullName -Force -ErrorAction SilentlyContinue; continue }
    $cat = [string]$data.cat

    # boas-vindas: segura na fila por 6s antes de tocar (sem remover o item)
    if ($cat -eq "sessionstart" -and $data.ts -and ((Now-Ms) - [double]$data.ts) -lt $HoldStartMs) {
      Start-Sleep -Milliseconds 500; continue
    }

    Remove-Item $it.FullName -Force -ErrorAction SilentlyContinue
    if (-not $data.file -or -not (Test-Path $data.file)) { continue }

    # descarta fala velha demais (backlog atrasado)
    if ($data.ts -and ((Now-Ms) - [double]$data.ts) -gt $MaxAgeMs) { continue }

    # boas-vindas: se um SessionEnd chegou junto/depois dela, o app estava FECHANDO -> cancela
    if ($cat -eq "sessionstart") {
      $lastEnd = 0
      try { $lastEnd = [double]((Get-Content -Raw $LastEndFile) -replace "[^0-9.]", "") } catch {}
      if ($lastEnd -gt ([double]$data.ts - 2000)) {
        Log "descarta sessionstart (assinatura de fechamento do app)"
        continue
      }
    }

    # trava anti-corrida: N sessões disparando juntas passam pelo cooldown do handler
    # antes da 1ª registrá-lo; aqui só a primeira da janela de 10s fala
    if (($cat -eq "sessionstart" -or $cat -eq "sessionend") -and $lastCatPlay.ContainsKey($cat)) {
      if (((Now-Ms) - $lastCatPlay[$cat]) -lt $DedupeMs) { Log ("descarta " + $cat + " (dedupe 10s)"); continue }
    }

    $sess = [string]$data.session
    $prefix = ""
    if ($data.prefix -and (Test-Path $data.prefix) -and $sess -ne "" -and $sess -ne $lastSession) {
      $prefix = [string]$data.prefix   # sessao mudou -> anuncia "De <sessao>"
    }
    if ($sess -ne "") {
      $lastSession = $sess
      Set-Content -Path $LastSessionFile -Value $sess -NoNewline -Encoding ASCII  # ASCII = sem BOM
    }

    $pfx = if ($prefix) { "com-prefixo" } else { "sem-prefixo" }
    Log ("toca " + (Split-Path $data.file -Leaf) + " (" + $pfx + ")")
    if ($cat -ne "") { $lastCatPlay[$cat] = Now-Ms }
    # toast do Windows com a MESMA frase, no instante da fala (só quando há texto)
    if ($data.text) { try { Show-JarvisToast -Text ([string]$data.text) -Title ([string]$data.title) } catch {} }
    # manda a fala pro FEED do HUD nativo DAQUELA sessão (append-only, formato do hook)
    if ($data.text -and $data.session) {
      try {
        $sidClean = ([string]$data.session) -replace '[^A-Za-z0-9_-]', ''
        $sdir = Join-Path $Dir ("hud-sessions\" + $sidClean)
        if (Test-Path $sdir) {
          $enc = New-Object System.Text.UTF8Encoding($false)
          $ln  = ([string]([long]$data.ts) + "`tJVS`t" + ((([string]$data.text)) -replace "[`t`r`n]", " ")) + "`n"
          [System.IO.File]::AppendAllText((Join-Path $sdir "feed.txt"), $ln, $enc)
        }
      } catch {}
    }
    Play-Item $prefix $data.file
    Start-Sleep -Milliseconds $GapMs
  }
} finally {
  try { $mutex.ReleaseMutex() } catch {}
  $mutex.Dispose()
}
