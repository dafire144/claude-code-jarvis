# Abre a telinha do Jarvis DELAY s apos o prompt, SE a sessao seguir ativa. Lancado (via
# WMI, destacado) pelo hud-native.mjs no UserPromptSubmit. Torna a abertura baseada em
# TEMPO desde o prompt (nao na cadencia de ferramentas) -> robusto p/ tarefas que ficam
# muito tempo "pensando" com poucas ferramentas.
# Sessao RECEM-criada pode ainda NAO ter titulo no momento do prompt (o app grava async;
# corrida de 07/07, sessao "Caio CRM"). Sem titulo nao se abre (regra: sem titulo =
# CLI/efemera), mas em vez de desistir o script RE-SONDA o titulo (title-probe.mjs) a
# cada RETRY s, ate MAXTRIES vezes — a sonda, achando, escreve o meta.txt e a telinha
# abre. ASCII puro (PS 5.1 le .ps1 como ANSI).
param([string]$Dir, [string]$Exe, [string]$Sid, [int]$Delay = 30, [string]$Node = 'node', [string]$Jarvis = '', [int]$Retry = 30, [int]$MaxTries = 4)
Start-Sleep -Seconds $Delay
for ($try = 1; $try -le $MaxTries; $try++) {
  if (-not (Test-Path $Dir)) { exit }                                                    # sessao foi limpa
  if ((Test-Path (Join-Path $Dir 'done')) -or (Test-Path (Join-Path $Dir 'end')) -or (Test-Path (Join-Path $Dir 'closed'))) { exit }  # tarefa acabou / fechada a mao
  $hb = Join-Path $Dir 'hb'
  if (Test-Path $hb) { if ((((Get-Date) - (Get-Item $hb).LastWriteTime).TotalMilliseconds) -lt 6000) { exit } }  # telinha ja esta viva
  $ok = Test-Path (Join-Path $Dir 'meta.txt')
  if (-not $ok -and $Jarvis -ne '') {                                                    # titulo ainda nao resolvido: sonda de novo
    try { & $Node (Join-Path $Jarvis 'title-probe.mjs') $Sid $Dir | Out-Null } catch { }
    $ok = Test-Path (Join-Path $Dir 'meta.txt')
  }
  if ($ok) {
    $si = ([wmiclass]'Win32_ProcessStartup').CreateInstance(); $si.ShowWindow = 0        # abre oculto e sobrevivente
    ([wmiclass]'Win32_Process').Create("`"$Exe`" `"$Sid`"", $null, $si) | Out-Null
    exit
  }
  Start-Sleep -Seconds $Retry                                                            # titulo pode nascer em instantes
}
