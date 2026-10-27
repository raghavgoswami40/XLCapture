# Makes a release: checks the program, sets the version, builds the installer, and writes the release notes and checksum beside it.
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Notes "What is new..."
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -NotesFile notes.md
# Nothing is published anywhere: the files are left in dist\ for you to upload (the App Hub, once it exists).
param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$Notes = "", [string]$NotesFile = "",
  [switch]$SkipChecks                                   # only for a rebuild of something that was just checked
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (MAJOR.MINOR.PATCH)." }
$brand = Join-Path $here "src\Brand.cs"
$utf8 = New-Object System.Text.UTF8Encoding $true
$current = [regex]::Match([IO.File]::ReadAllText($brand, $utf8), 'Version = "([^"]+)"').Groups[1].Value
function Cmp($a, $b) { return ([version]$a).CompareTo([version]$b) }
if ((Cmp $Version $current) -lt 0) { throw "Version $Version is older than the current $current." }
if ($NotesFile) { $Notes = Get-Content $NotesFile -Raw }
if (-not $Notes.Trim()) { throw "Give the release notes: -Notes ""..."" or -NotesFile file.md (what is new, in plain words)." }

if (-not $SkipChecks) {
  Write-Host "Running the checks first..."
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "tools\verify.ps1")
  if ($LASTEXITCODE -ne 0) { throw "The checks failed, so there is no release. Fix them, or look at the pictures the check left." }
}

# set the version in the one place it lives, and keep the old text in case the build fails
$text = [IO.File]::ReadAllText($brand, $utf8); $bak = $text
[IO.File]::WriteAllText($brand, ($text -replace 'Version = "[^"]+"', "Version = ""$Version"""), $utf8)
try { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build-setup.ps1"); if ($LASTEXITCODE -ne 0) { throw "The installer did not build." } }
catch { [IO.File]::WriteAllText($brand, $bak, $utf8); throw }

$dist = Join-Path $here "dist"; $setup = Join-Path $dist "XLCapture-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "The installer is missing: $setup" }
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
$md = @"
# XLCapture $Version

Released $(Get-Date -Format 'd MMMM yyyy')

## What is new
$($Notes.Trim())

## Install
Run ``XLCapture-Setup-$Version.exe``. It installs for you only, no administrator rights needed, and replaces an older version. Your captures, sessions and settings are kept.

SHA-256 of the installer: ``$hash``
"@
Set-Content (Join-Path $dist "release-notes-$Version.md") $md -Encoding UTF8
"Release $Version is ready in $dist"
Get-ChildItem $dist | Where-Object { $_.Name -like "*$Version*" } | Select-Object Name, @{ n = 'KB'; e = { [int]($_.Length / 1KB) } } | Format-Table -AutoSize | Out-String | Write-Host
