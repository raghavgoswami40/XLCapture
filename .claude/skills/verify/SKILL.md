---
name: verify
description: Build XLCapture and run every dev check (editor and session functions, shape pick-up, shared sessions, editor opens, every dialog photographed, menus, capture window) with the user's settings backed up and restored, then review the pictures for clipped text or buttons. Use after any change to XLCapture's code, before telling the user it works, and before a release.
---

# verify

One command checks XLCapture end to end without touching the user's running copy or their settings.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\verify.ps1"
```

Run it from the SnapLite folder (the one holding `build.ps1`). It takes a few minutes; run it in the background if the shell has a time limit. Use `-SkipBuild` to reuse the build from the last run.

## What it does

1. Builds to `%LOCALAPPDATA%\SnapLite\verify\XLCapture.exe`, a side copy, so a running XLCapture never blocks it.
2. Backs up `%APPDATA%\SnapLite\settings.txt` and puts it back at the end, even if a check fails (the checks change the saved sessions folder).
3. Runs each check with a time limit, deletes the old log first (a stale log must never count as a pass) and reports PASS or FAIL with the first failing line.
4. Leaves pictures in `%LOCALAPPDATA%\SnapLite\verify\shots`.

Checks: `--editor-test` (session editor, cropping, steps, undo, export; needs the folder argument), `--pick-test`, `--open-editor`, `--share-test`, `--dialogs-shot`, `--menu-shot`, `--round-shot`, `--hub-shot`.

## After it runs

- Report each FAIL with its detail. Do not call the work done while any check fails, and do not edit a test to make it pass.
- **Look at the pictures.** Read every `dlg-*.png`, `menu-*.png`, `round-*.png` and `hub*.png` that the change could affect. The tests cannot see clipped text, a button too small for its label, overlapping controls or a dialog that opened off the screen. Say what you checked and what you did not.
- The dialogs run at this PC's display scale; a layout written in plain pixels instead of `Dpi.S(...)` shows up here as text bigger than its box.
- If you added a dialog or window, add it to `DialogsShot` in `src\DevChecks.cs` so it is photographed from now on.

## Do not

- Do not stop or restart the user's running XLCapture to run these checks; that is a separate step and only on their say-so.
- Do not run the checks with the real settings folder; the script handles the backup.
