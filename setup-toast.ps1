# Instalador ÚNICO das notificações "J.A.R.V.I.S." do Windows (idempotente).
# 1) gera assets\jarvis.ico a partir do logo; 2) cria um atalho no Menu Iniciar com
# AppUserModelID "Jarvis.ClaudeCode"; 3) registra DisplayName+IconUri no registro.
# Depois disso, o daemon dispara toasts que aparecem como "J.A.R.V.I.S." com ícone.
# Rodar UMA vez: powershell -NoProfile -ExecutionPolicy Bypass -File setup-toast.ps1
$ErrorActionPreference = "Stop"
$Dir   = $PSScriptRoot
$AUMID = "Jarvis.ClaudeCode"
$Logo  = Join-Path $Dir "assets\jarvis-logo.png"
$Ico   = Join-Path $Dir "assets\jarvis.ico"

if (-not (Test-Path $Logo)) { throw "logo não encontrado: $Logo" }

# --- 1) PNG -> ICO (256px, PNG embutido; Vista+ suporta) ---
Add-Type -AssemblyName System.Drawing
function Build-Ico($pngPath, $icoPath, $size = 256) {
  $src = [System.Drawing.Image]::FromFile($pngPath)
  try {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $png = $ms.ToArray(); $ms.Dispose()
  } finally { $src.Dispose() }
  $fs = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $fs
  $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)   # ICONDIR: reserved, type=icon, count=1
  $dim = if ($size -ge 256) { 0 } else { $size }
  $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0)  # w,h,colors,reserved
  $bw.Write([UInt16]1); $bw.Write([UInt16]32)                        # planes, bpp
  $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)              # size, offset (6+16)
  $bw.Write($png); $bw.Flush()
  [System.IO.File]::WriteAllBytes($icoPath, $fs.ToArray())
  $bw.Dispose(); $fs.Dispose()
}
Build-Ico $Logo $Ico 256
Write-Host "ico gerado: $Ico"

# --- 2) atalho no Menu Iniciar com AppUserModelID (interop IShellLink+IPropertyStore) ---
if (-not ("Jarvis.Shortcut" -as [type])) {
Add-Type -Language CSharp @"
using System;
using System.Runtime.InteropServices;
namespace Jarvis {
  [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellLinkW {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder f, int c, IntPtr p, uint fl);
    void GetIDList(out IntPtr ppidl); void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder n, int c);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string n);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder d, int c);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string d);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder a, int c);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string a);
    void GetHotkey(out short w); void SetHotkey(short w);
    void GetShowCmd(out int s); void SetShowCmd(int s);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder ip, int c, out int i);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string ip, int i);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string r, uint res);
    void Resolve(IntPtr hwnd, uint fl);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string f);
  }
  [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPersistFile {
    void GetClassID(out Guid c); [PreserveSig] int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string f, uint m);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string f, [MarshalAs(UnmanagedType.Bool)] bool r);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string f);
  }
  [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IPropertyStore {
    void GetCount(out uint c); void GetAt(uint i, out PROPERTYKEY k);
    void GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
    void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
    void Commit();
  }
  [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
  [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt; public ushort r1; public ushort r2; public ushort r3; public IntPtr p; public int p2; }
  [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }
  public static class Shortcut {
    public static void Create(string lnk, string target, string args, string icon, string aumid, string desc) {
      var link = (IShellLinkW)new CShellLink();
      link.SetPath(target);
      if (!string.IsNullOrEmpty(args)) link.SetArguments(args);
      if (!string.IsNullOrEmpty(icon)) link.SetIconLocation(icon, 0);
      if (!string.IsNullOrEmpty(desc)) link.SetDescription(desc);
      var store = (IPropertyStore)link;
      var key = new PROPERTYKEY(); key.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); key.pid = 5;
      var pv = new PROPVARIANT(); pv.vt = 31; pv.p = Marshal.StringToCoTaskMemUni(aumid);
      store.SetValue(ref key, ref pv); store.Commit();
      Marshal.FreeCoTaskMem(pv.p);
      ((IPersistFile)link).Save(lnk, true);
    }
  }
}
"@
}

$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$lnk = Join-Path $startMenu "J.A.R.V.I.S..lnk"
[Jarvis.Shortcut]::Create($lnk, "$env:SystemRoot\explorer.exe", "`"$Dir`"", $Ico, $AUMID, "J.A.R.V.I.S. for Claude Code")
Write-Host "atalho criado: $lnk (AUMID=$AUMID)"

# --- 3) registro: DisplayName + ícone da identidade do toast ---
$reg = "HKCU:\Software\Classes\AppUserModelId\$AUMID"
New-Item -Path $reg -Force | Out-Null
New-ItemProperty -Path $reg -Name DisplayName -Value "J.A.R.V.I.S." -PropertyType String -Force | Out-Null
New-ItemProperty -Path $reg -Name IconUri -Value $Logo -PropertyType String -Force | Out-Null
Write-Host "registro OK: $reg"
Write-Host "`nPronto. Notificacoes 'J.A.R.V.I.S.' instaladas."
