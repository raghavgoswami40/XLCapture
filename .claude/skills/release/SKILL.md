---
name: release
description: Make an XLCapture release - run the checks, set the version, build the installer (XLCapture-Setup-<version>.exe), and write release notes and a checksum into dist\. Use only when the user asks to release, ship or publish a new version of XLCapture.
disable-model-invocation: true
---

# release

Builds the installer people will download. Nothing is published: the files are left in `dist\` for the user to upload (to the Odoo App Hub once it exists).

## Steps

1. **Pick the version** with the user if they did not say. Semantic version `MAJOR.MINOR.PATCH`: a fix is PATCH, something new people will notice is MINOR, a change that breaks saved sessions or settings is MAJOR. The current one is in `src\Brand.cs` (`Brand.Version`). It must not go down.
2. **Write the release notes** in plain words for the people who will install it: what is new or fixed, one short line each, no code names. Draw them from what changed since the last release; ask the user if you are unsure what was in it. Never put secrets, paths or personal data in them.
3. **Run it** from the SnapLite folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\release.ps1" -Version 1.1.0 -Notes "- Faster ...`n- Fixed ..."
```

   It runs `tools\verify.ps1` first (see the `verify` skill) and stops if anything fails. Then it sets `Brand.Version`, runs `build-setup.ps1`, and writes `dist\XLCapture-Setup-<version>.exe`, `.sha256.txt` and `release-notes-<version>.md`. If the build fails it puts the old version back.
4. **Look at the pictures** the checks left (see `verify`) before saying it is ready.
5. **Report**: the version, the files in `dist\`, the SHA-256, and anything that did not pass.

## Rules

- Do not use `-SkipChecks` unless the user asks.
- Do not upload or send the installer anywhere, and do not tell the user it is published. The App Hub does not exist yet; when it does, uploading is a separate step the user approves.
- The installer is not code-signed, so Windows SmartScreen warns on first run. Say so if the user is about to send it to others.
- Do not install it on the user's PC unless they ask; that replaces the copy they may be running.
