# Show-JarvisToast — dispara um toast nativo do Windows (WinRT) com a fala do Jarvis.
# Usa o AUMID "Jarvis.ClaudeCode" (registrado por setup-toast.ps1) p/ aparecer como J.A.R.V.I.S.
# - dot-source (. toast.ps1)  -> só define a função (o daemon usa assim)
# - powershell -File toast.ps1 -Text "..." -Title "..."  -> dispara um toast (teste)
param([string]$Text, [string]$Title)

$script:JarvisAumid = "Jarvis.ClaudeCode"
$script:JarvisLogo  = Join-Path $PSScriptRoot "assets\jarvis-logo.png"

function Show-JarvisToast {
  param([string]$Text, [string]$Title)
  if ([string]::IsNullOrWhiteSpace($Text)) { return }
  try {
    [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.UI.Notifications.ToastNotification,        Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument,                 Windows.Data.Xml.Dom,     ContentType = WindowsRuntime]

    $attr = if ([string]::IsNullOrWhiteSpace($Title)) { "J.A.R.V.I.S." } else { $Title }
    $logoXml = ""
    if (Test-Path $script:JarvisLogo) {
      $uri = ([System.Uri]$script:JarvisLogo).AbsoluteUri
      $logoXml = "<image placement='appLogoOverride' hint-crop='circle' src='$uri'/>"
    }
    $t = [System.Security.SecurityElement]::Escape($Text)
    $a = [System.Security.SecurityElement]::Escape($attr)
    $xml = @"
<toast scenario='default'>
  <visual>
    <binding template='ToastGeneric'>
      $logoXml
      <text>$t</text>
      <text placement='attribution'>$a</text>
    </binding>
  </visual>
  <audio silent='true'/>
</toast>
"@
    $doc = New-Object Windows.Data.Xml.Dom.XmlDocument
    $doc.LoadXml($xml)
    $toast = New-Object Windows.UI.Notifications.ToastNotification $doc
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($script:JarvisAumid).Show($toast)
  } catch { }
}

# execução direta (não dot-source) com -Text = teste
if ($Text -and $MyInvocation.InvocationName -ne '.') {
  Show-JarvisToast -Text $Text -Title $Title
}
