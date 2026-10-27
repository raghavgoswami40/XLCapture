# Builds XLCapture-Setup-<version>.exe in the dist folder: the program, the logo and the installer in one file.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$N = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$version = [regex]::Match((Get-Content (Join-Path $here "src\Brand.cs") -Raw), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "Could not find Brand.Version" }
$dist = Join-Path $here "dist"; New-Item -ItemType Directory -Force $dist | Out-Null
$work = Join-Path $env:LOCALAPPDATA "SnapLite\setup-build"; New-Item -ItemType Directory -Force $work | Out-Null
$app = Join-Path $work "XLCapture.exe"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build.ps1") -OutFile $app
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $app)) { throw "The program did not build" }
$ver = Join-Path $work "version.txt"; Set-Content $ver $version -Encoding ascii
$assets = Join-Path $here "assets"
$out = Join-Path $dist "XLCapture-Setup-$version.exe"
& "$N\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /out:$out "/win32icon:$assets\xlcapture.ico" `
  "/resource:$app,XLCapture.exe" "/resource:$ver,version.txt" "/resource:$assets\logo-full.png,logo-full.png" "/resource:$assets\xlcapture.ico,setup.ico" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $here "Setup\Setup.cs")
if ($LASTEXITCODE -ne 0) { throw "The installer did not build" }
$h = (Get-FileHash $out -Algorithm SHA256).Hash
Set-Content (Join-Path $dist "XLCapture-Setup-$version.sha256.txt") "$h  XLCapture-Setup-$version.exe" -Encoding ascii
"Built $out ($([int]((Get-Item $out).Length / 1KB)) KB)`nSHA-256 $h"
