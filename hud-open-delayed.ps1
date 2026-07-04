# Abre a telinha do Jarvis DELAY segundos apos o prompt, SE a sessao ainda estiver ativa.
# Lancado (via WMI, destacado) pelo hud-native.mjs no UserPromptSubmit. Torna a abertura
# baseada em TEMPO desde o prompt (nao na cadencia de ferramentas) -> robusto p/ tarefas
# que ficam muito tempo "pensando" com poucas ferramentas. ASCII puro (PS 5.1 le .ps1 como ANSI).
param([string]$Dir, [string]$Exe, [string]$Sid, [int]$Delay = 30)
Start-Sleep -Seconds $Delay
if (-not (Test-Path $Dir)) { exit }                                                    # sessao foi limpa
if ((Test-Path (Join-Path $Dir 'done')) -or (Test-Path (Join-Path $Dir 'end')) -or (Test-Path (Join-Path $Dir 'closed'))) { exit }  # tarefa acabou / fechada a mao
$hb = Join-Path $Dir 'hb'
if (Test-Path $hb) { if ((((Get-Date) - (Get-Item $hb).LastWriteTime).TotalMilliseconds) -lt 6000) { exit } }  # telinha ja esta viva
$si = ([wmiclass]'Win32_ProcessStartup').CreateInstance(); $si.ShowWindow = 0          # abre oculto e sobrevivente
([wmiclass]'Win32_Process').Create("`"$Exe`" `"$Sid`"", $null, $si) | Out-Null
