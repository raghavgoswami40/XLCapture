# Builds XLCapture to a side folder (so a running XLCapture is never in the way) and runs every dev check, with the user's
# settings file backed up first and put back afterwards. Pictures of the dialogs and windows are left in <work>\shots.
#   powershell -ExecutionPolicy Bypass -File tools\verify.ps1            run everything
#   powershell -ExecutionPolicy Bypass -File tools\verify.ps1 -SkipBuild reuse the build from the last run
param([switch]$SkipBuild, [string]$Work = "")
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $Work) { $Work = Join-Path $env:LOCALAPPDATA "SnapLite\verify" }
New-Item -ItemType Directory -Force $Work | Out-Null
$exe = Join-Path $Work "XLCapture.exe"; $shots = Join-Path $Work "shots"; $data = Join-Path $Work "data"
New-Item -ItemType Directory -Force $shots, $data | Out-Null
$results = New-Object System.Collections.ArrayList
function Note($name, $ok, $detail) { [void]$results.Add([pscustomobject]@{ Check = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Detail = $detail }) }

if (-not $SkipBuild) {
  if (Test-Path $exe) { Remove-Item $exe -Force }                      # an old build must never stand in for a failed one
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build.ps1") -OutFile $exe -Dev 2>&1 | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host $_ }
  if (-not (Test-Path $exe)) { throw "The program did not build." }
}
Note "build" (Test-Path $exe) $exe

# run one check: wait for it, kill it if it hangs, and say whether it passed
function Run($name, $arguments, $log, [int]$seconds = 120, [string]$mustSay = "") {
  if ($log -and (Test-Path $log)) { Remove-Item $log -Force }                    # a stale log must never count as a pass
  $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
  if (-not $p.WaitForExit($seconds * 1000)) { try { $p.Kill() } catch {}; Note $name $false "no answer after $seconds s (killed)"; return }
  $text = if ($log -and (Test-Path $log)) { Get-Content $log -Raw } else { "" }
  if ($mustSay) { Note $name ($text -match [regex]::Escape($mustSay)) ($(if ($text -match 'FAIL') { (($text -split "`n") | Where-Object { $_ -match '^FAIL|THROWN|Unhandled' } | Select-Object -First 3) -join ' | ' } else { "exit $($p.ExitCode)" })) }
  else { Note $name ($p.ExitCode -eq 0 -and ($text -notmatch 'THROWN|Unhandled|NO FORM|NO METHOD')) ($(if ($text) { (($text -split "`n") | Where-Object { $_ -match 'THROWN|Unhandled|NO FORM|NO METHOD' } | Select-Object -First 3) -join ' | ' } else { "exit $($p.ExitCode)" })) }
}

$settings = Join-Path $env:APPDATA "SnapLite\settings.txt"; $backup = Join-Path $Work "settings.backup"
if (Test-Path $settings) { Copy-Item $settings $backup -Force }
try {
  $env:XLC_SHOW_STATUS = '1'                                                      # lets the status window be photographed
  Run "editor and session functions" @('--editor-test', "`"$Work\editor-test.log`"", "`"$data`"") "$Work\editor-test.log" 240 "ALL PASSED"
  $sess = @(Get-ChildItem $data -Filter *.clicktrail -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName)[0]
  Run "a crafted project file is refused" @('--security-test', "`"$Work\security.log`"", "`"$Work\security`"") "$Work\security.log" 120 "ALL PASSED"
  Run "checklist ticks and the recording area" @('--plan-test', "`"$Work\plan.log`"") "$Work\plan.log" 60 "ALL PASSED"
  Run "canvas picks up an existing shape" @('--pick-test', "`"$Work\pick.log`"") "$Work\pick.log" 60 "ALL PASSED"
  Run "editor opens without an exception" @('--open-editor', "`"$Work\open-editor.log`"") "$Work\open-editor.log" 60 "done"
  if ($sess -and (Test-Path $sess)) {
    Run "session editor photographed (box corner dot, step menu)" @('--round-shot', "`"$shots\round`"", "`"$sess`"", "`"$data`"") "" 90
    Run "shared session round trip, moved text" @('--share-test', "`"$Work\share.log`"", "`"$sess`"", "`"$Work\share`"") "$Work\share.log" 120 "ALL PASSED"
    $ventia = Join-Path $env:USERPROFILE "Downloads\Ventia - 1.4.1 Create Delivery Project - V0.0 - 202610231528.xlsx"
    $template = Join-Path $env:USERPROFILE "Downloads\Ventia Template 1.pptx"
    Run "procedure workbook and slide pack folder" @('--slides-test', "`"$Work\slides.log`"", "`"$sess`"", "`"$Work\slides`"", "`"$ventia`"", "`"$template`"") "$Work\slides.log" 120 "ALL PASSED"
    # the PowerPoint builder, on the pack the test just wrote (needs python-pptx and the Ventia template; says so when it cannot run)
    $pack = ((Get-Content "$Work\slides.log" -ErrorAction SilentlyContinue | Where-Object { $_ -like 'PACK *' } | Select-Object -First 1) -replace '^PACK ', '')
    $py = Join-Path $here "assets\xlcapture-build-deck.py"
    if ($pack -and (Test-Path $template) -and (Test-Path $py)) {
      $hasLib = $false; try { & python -c "import pptx, PIL" 2>$null; $hasLib = ($LASTEXITCODE -eq 0) } catch {}
      if ($hasLib) {
        $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'      # python's messages on stderr are not failures here
        $out = Join-Path $Work "deck.pptx"; if (Test-Path $out) { Remove-Item $out -Force }
        $said = & python $py $pack --template (Join-Path $pack "template.pptx") --author "Test" --date "1 January 2026" --email "test@example.com" --out $out 2>&1 | Out-String
        $count = 0; try { $count = [int](& python -c "from pptx import Presentation; import sys; print(len(Presentation(sys.argv[1]).slides))" $out) } catch {}
        $n = ((Get-Content (Join-Path $pack "slides.json") -Raw | ConvertFrom-Json).slides | Measure-Object).Count
        $hasDiagram = Test-Path (Join-Path $pack "diagram.png")
        $want = 3 + $n + $(if ($hasDiagram) { 1 } else { 0 })     # cover, divider, closing and the screenshots (+ the diagram)
        Note "PowerPoint deck builds on the template ($count slides, expected $want)" ((Test-Path $out) -and $count -eq $want) $said.Trim()
        # a second activity added to that deck: it must land in the deck's own order and leave the original alone
        $pack2 = Join-Path $Work "pack2"; if (Test-Path $pack2) { Remove-Item $pack2 -Recurse -Force }; Copy-Item $pack $pack2 -Recurse
        $j = Get-Content (Join-Path $pack2 "slides.json") -Raw | ConvertFrom-Json; $j.activity.code = "9"; $j.activity.title = "Added activity"; $j | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $pack2 "slides.json") -Encoding UTF8
        $out2 = Join-Path $Work "deck-added.pptx"; if (Test-Path $out2) { Remove-Item $out2 -Force }; $beforeHash = (Get-FileHash $out).Hash
        $said2 = & python $py $pack2 --append $out --out $out2 2>&1 | Out-String
        $count2 = 0; try { $count2 = [int](& python -c "from pptx import Presentation; import sys; print(len(Presentation(sys.argv[1]).slides))" $out2) } catch {}
        $order = ""; try { $order = (& python -c "from pptx import Presentation; import sys; p=Presentation(sys.argv[1]); print(' '.join(s.slide_layout.name[:3] for s in p.slides))" $out2) } catch {}
        $want2 = $count + $n + 1 + $(if ($hasDiagram) { 1 } else { 0 })
        Note "an activity is added to an existing deck ($count2 slides, expected $want2)" ((Test-Path $out2) -and $count2 -eq $want2 -and $order.Trim().EndsWith("Tha") -and ((Get-FileHash $out).Hash -eq $beforeHash)) $said2.Trim()
        $ErrorActionPreference = $eap
      } else { Note "PowerPoint deck builds on the template" $true "SKIPPED: python-pptx is not installed" }
    } else { Note "PowerPoint deck builds on the template" $true "SKIPPED: no pack, template or builder" }
    Run "Appearance works with the capture window open" @('--look-test', "`"$Work\look`"", "`"$sess`"", "p", "hub") "$Work\look.txt" 90 "ALL PASSED"
    Run "every dialog photographed" @('--dialogs-shot', "`"$shots\dlg`"", "`"$sess`"") "$shots\dlg.txt" 240
    Run "menus, look and drop-downs photographed" @('--menu-shot', "`"$shots\menu`"", "`"$sess`"") "$shots\menu.txt" 120
  } else { Note "session based checks" $false "the editor test left no session in $data" }
  Run "capture window is the same size on every monitor" @('--hub-monitors', "`"$shots\hubmon`"") "$shots\hubmon.txt" 90 "ALL PASSED"
  Run "capture from the capture window still covers the real desktop" @('--hub-capture', "`"$Work\hubcap.log`"") "$Work\hubcap.log" 60 "ALL PASSED"
  Run "status window photographed" @('--status-shot', "`"$shots\status`"") "" 60
  Run "capture window photographed" @('--hub-shot', "`"$shots\hub.png`"") "" 90
}
finally {
  $env:XLC_SHOW_STATUS = $null
  if (Test-Path $backup) { Copy-Item $backup $settings -Force }                   # the user's settings, exactly as they were
}
$results | Format-Table -AutoSize -Wrap | Out-String | Write-Host
$bad = @($results | Where-Object { $_.Result -eq 'FAIL' }).Count
"Pictures: $shots"
if ($bad -gt 0) { "$bad check(s) FAILED"; exit 1 } else { "All checks passed" }

