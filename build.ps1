param([string]$OutFile = "", [switch]$Dev)       # -Dev adds the developer test switches (tools\verify.ps1 uses it); the shared program is built without
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$N = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$W = "C:\Windows\System32\WinMetadata"
$refs = @(
  "System.dll","System.Core.dll","System.Drawing.dll","System.Windows.Forms.dll","System.IO.Compression.dll",
  "System.IO.Compression.FileSystem.dll","System.Runtime.dll","System.Runtime.WindowsRuntime.dll","System.Runtime.InteropServices.WindowsRuntime.dll","System.Xml.dll","Microsoft.VisualBasic.dll","System.Web.Extensions.dll"
) | ForEach-Object { "/r:$_" }
foreach ($asm in 'UIAutomationClient','UIAutomationTypes','WindowsBase') {
  $p = Get-ChildItem "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\$asm" -Recurse -Filter "$asm.dll" | Select-Object -First 1
  $refs += "/r:$($p.FullName)"
}
$refs += (Get-ChildItem $W -Filter "Windows.*.winmd" | Where-Object { $_.Name -match '^Windows\.(Foundation|Globalization|Graphics|Media|Storage|Devices|Data)\.winmd$' } | ForEach-Object { "/r:$($_.FullName)" })
$src = Get-ChildItem (Join-Path $here "src") -Filter *.cs | ForEach-Object { $_.FullName }
$out = if ($OutFile) { $OutFile } else { Join-Path $here "XLCapture.exe" }
$assets = Join-Path $here "assets"
# the logos are built into the program: the short one as the icon, both as pictures
$res = @("/win32icon:$assets\xlcapture.ico", "/resource:$assets\xlcapture.ico,XLCapture.icon.ico", "/resource:$assets\logo-mark.png,XLCapture.logo-mark.png", "/resource:$assets\logo-full.png,XLCapture.logo-full.png", "/resource:$assets\xlcapture-slides.md,XLCapture.xlcapture-slides.md", "/resource:$assets\xlcapture-build-deck.py,XLCapture.xlcapture-build-deck.py")
$define = @(); if ($Dev) { $define = @("/define:DEVCHECKS") }
& "$N\csc.exe" /nologo /target:winexe /optimize+ /unsafe /codepage:65001 @define /out:$out @res @refs $src
if ($LASTEXITCODE -ne 0) { exit 1 }
"Built $out"
