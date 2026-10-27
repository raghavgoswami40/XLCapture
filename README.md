# XLCapture

> XLCapture was called SnapLite until now. The program is `XLCapture.exe` and every window, message and menu says XLCapture. Your settings, presets, library and the jobs folder still live in the `SnapLite` folders under AppData, and sessions in your Pictures\SnapLite folder, so nothing you had is lost; the project folder and the `snaplite-teach` skill keep their names too.

A local screen-capture, recording and annotation app for Windows. Original code, no installs,
no network use, no accounts. Built with the C# compiler that ships with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1      # makes XLCapture.exe from src\*.cs
```

Run `XLCapture.exe`. It opens the capture window and keeps an icon in the system tray
(`XLCapture.exe --tray` starts it hidden). Captures are saved to `Pictures\XLCapture`.

## Two windows, laid out like Snagit

**Capture window**: All-in-One / Image / Video / Sessions / ClickBot tabs on the left; a card with two
headed columns (Capture: Selection, Effects and Share; Options: Preview in editor, Copy to clipboard,
Cursor and Time delay); the round Capture button (red Record on the Video and Sessions tabs) with its
hotkey shown as keys, and Reset tab under it; Presets, Save preset and "Open XLCapture Editor" along the bottom.
All-in-One: click a window or drag an area, then choose Image, Video, GIF, Scroll, Panorama or Text.
Screen Draw is not built yet and is not shown; Camera and System audio are greyed out when no device is found.

**XLCapture Editor**: menu bar; a ribbon with Editor / Library / Capture / Create, the centred tool group
(Favorites, Arrow, Text, Callout, Shape, Stamp, Fill, Move, Selection, More, Undo, Redo) and Copy All /
Share; Quick Styles (with themes) and Tool Properties on the right, or Effects; a bottom bar with Hide
Recent, Tag, zoom, image size, Effects and Properties; and the recent-captures tray. One window: clicking
a tray item switches pictures, unsaved edits keep a red dot. Right-click a tool to add it to Favorites.

## Hotkeys (change them in Settings)

| Keys | Action |
|---|---|
| Ctrl+Shift+1 | Region |
| Ctrl+Shift+2 | Window |
| Ctrl+Shift+3 | Full screen |
| Ctrl+Shift+4 | Scrolling page |
| Ctrl+Shift+5 | UI element |
| Ctrl+Shift+6 | Freehand |
| Ctrl+Shift+7 | Grab text (OCR) |
| Ctrl+Shift+8 | Record video (press again to stop) |
| Ctrl+Shift+9 | Repeat last capture |
| Ctrl+Shift+R | Start / stop a session recording |

Panorama, Fixed size, Multiple areas and Record GIF have no default key; set one in Settings.

## Status against Snagit

Honest accounting. **Tested** = I ran it and checked the result. **Built** = the code is in and
compiles, but I did not exercise it by hand (mouse-driven flows in particular). **Not built** = absent.

### Capture
| Feature | Status |
|---|---|
| Full screen (all monitors), hotkey, tray mode | Tested end to end |
| Region, window, fixed size, multi-area, freehand, UI element (UI Automation) | Built |
| Scrolling page / panorama (auto-scroll + stitch) | Stitcher tested on a synthetic page (exact rebuild); live scrolling Built |
| Grab text (Windows OCR) | OCR tested; select-and-copy flow Built |
| Capture delay with countdown, include pointer, repeat last, profiles | Built |
| Configurable hotkeys, start with Windows (opt-in) | Built |
| Screen video to MP4 with microphone | Tested (3.3 s clip) |
| GIF recording | Tested |
| Pause/resume, click halo, trim video | Pause and halo Built; trim Built |
| System audio, webcam capture / overlay | Not built |
| Mobile capture (Snagit Connect), cloud sync | Not built (needs the vendor's services) |

### Editor
| Feature | Status |
|---|---|
| Arrow, line, shape (7 kinds), callout with aimable tail, text, 14 stamps, auto-numbered steps | Rendering tested |
| Pen, marker, highlighter; magnify; spotlight; blur / pixelate | Rendering tested |
| Select, move, resize, rotate, snap guides, multi-select, align, distribute, order, lock, duplicate | Built |
| Crop, cut-out band, fill (flood), eyedropper | Built; fill and ops tested |
| Raster selections: rectangle, ellipse, freehand, magic wand; copy, cut, fill, crop | Wand tested; rest Built |
| Smart Move (lift a region, fill the gap) | Built (simple edge-interpolation fill, not AI) |
| Resize, canvas size, rotate 90 / any angle, flip, trim borders, perspective | Tested |
| Border, drop shadow, torn / wave / sawtooth / fade edges | Tested |
| Brightness, contrast, saturation, hue, gamma, grayscale, sepia, invert, posterize, sharpen, blur, emboss, edges, replace colour | Tested (sizes); look checked in dialogs |
| Undo / redo, project file `.snapl` (keeps annotations editable) | Tested (round-trip identical) |
| Export PNG, JPEG, BMP, GIF, TIFF, PDF; copy; print | PDF structure and export Tested; print Built |
| Email (Outlook if present), FTP upload, open externally | Built |
| Library: thumbnails, search, favourites, tags, sort, rename, recycle-bin delete, combine pictures, multi-page PDF | Tested (rendering); actions Built |
| Grab text from the picture | Built |
| Simplify (AI turns a screenshot into a simplified mock-up) | Not built (needs a trained model) |
| Step-by-step guide templates, themes, Slack / Teams / Dropbox / Drive / YouTube sharing | Not built |
| Spell check, eraser tool | Not built |
| Snagit's own file format (`.snagx`) | Not supported |

### Sessions (modelled on ClickTrail)
Studied with REA from the ClickTrail desktop app, then rebuilt natively.

| Feature | Status |
|---|---|
| Sessions tab (4th tab): record every screen, one monitor, a region or one window | Built (live recording not exercised) |
| Each click becomes a numbered step; screenshot taken just before the click lands | Built |
| Element name and role from UI Automation; ClickTrail's box-size rules for the highlight | Built |
| Typing and scrolling folded into steps (toggles on the tab); new screen when the window changes | Built |
| Floating recording bar (or panel, on a second monitor) with step count, Switch window, Pause, Stop, **Start again** (throws away the steps so far; a continued session goes back to how it was saved) and **Cancel** (throws the recording away, nothing is saved); both ask first when there are steps. Hidden from its own screenshots | Built |
| `.clicktrail` files: read ClickTrail's files, save ones ClickTrail can open | Tested on 5 real ClickTrail sessions (load, re-save, reload identical) |
| Session view in the editor: rename, edit / delete / reorder steps, browse screens | Rendering tested |
| Edit a screen as a normal picture; export pictures; export a PDF guide | Built |
| Editor tray filter: All / Images / Videos / Sessions with counts | Rendering tested |

### Learn and replay (the click guide bot)
XLCapture can do a session itself instead of recording you. Claude (the `snaplite-teach` skill) performs a written procedure step by step, XLCapture records it as a session, and later XLCapture repeats that session by itself, fast and with no AI, using different values.

| Feature | Status |
|---|---|
| `XLCapture.exe --tray --control` opens a local named pipe (only your Windows user can use it); `XLCapture.exe --cmd <json>` talks to it | Tested (ping, windows, observe, session-start, click, type, session-end) |
| Driver: finds a control by name / id / role / class / place through UI Automation, acts through a UIA pattern, falls back to the real mouse | Tested on a WinForms window and on a web page in Edge |
| Browser pages and desktop apps share one path: Edge/Chrome expose page elements (name, role, DOM id as the id) through UI Automation; the address (no query string) is saved per step | Tested in Edge; Chrome not run |
| Every action is saved as a normal session step with the screenshot taken before it; session file gains `className`, `ancestorPath`, `urlBar`, `param`, `expect`, and a `procedure` header (old files load and save unchanged) | Tested (an existing ClickTrail file round-trips identical) |
| Inputs: a typed step carries only the NAME of its input (`param`), never the value; Make input... in the session view names one by hand | Tested (value not in the file) |
| Replay... button: asks for every input first (or a CSV, one run per row), then runs back to back; Pause/Break or the Stop button halts it | Tested (single run, 2-row CSV with a quoted comma, stop on a missing control) |
| Each replay records itself as a new session; if a control is gone it stops at that step and says which | Tested |
| Guide folder... exports `clicks.json` + `shot-NN.png` for the click-guide-builder skill (PowerPoint) | Folder written; deck not built here |

The **ClickBot** tab (5th tab, after Sessions) is the front end for replay: a list of saved sessions with an inputs badge, the inputs for the chosen one as plain text boxes (or a CSV for many runs), and the big round button as **Run**. A step ribbon fills green, amber or red as the bot works; the same ribbon shows on the floating bar while XLCapture is minimized. The capture hotkey never starts a run while this tab is open. Pictures checked on screen; the run itself was tested through the self-tests, not by clicking Run in this tab.

**Add a procedure** (button at the bottom of the ClickBot list): choose a Process Manager export (.xlsx or .csv, the one with Code, Title and Node Type columns), then the activity Claude should capture. Only activities that have tasks and actions are offered, one session is made per activity and the activity's name is the session's name. The next page lists every action task by task with what Claude will do with it (click, enter a value, choose an option, or leave it to a person); right-click to change one. The last page asks for every value and choice Claude needs, then starts Claude Code on a job file. The saved session is grouped by task (each task starts a new screen, the editor shows the task as the group heading, and the guide folder carries each task and the action text). Values are asked for again before every replay; an optional input left blank skips its step. Tested on a real Process Manager export (4 activities, 29 tasks, 92 actions classified).

**Session editor** (the editing functions of ClickTrail's session editor): merge a screen with the previous one (within a task), start a new screenshot from a step (every step now keeps its own picture, as a JPEG, so the new screen shows what the screen looked like at that step), add a step by hand, drag a box to move it and its corner to resize it, drag a number, crop (drag, handles, Enter and Esc), paste a screenshot or drop an image file on it (and Use original), choose which picture a screen shows (taken at a step, or after typing), delete a step or a whole screenshot, undo (up to 40 steps, Ctrl+Z), box and number colours. Merge, split, crop, pasted pictures, manual steps, moved numbers and the chosen picture are saved in the session file and used by the exports (pictures, PDF, guide folder). Right-click a step for New screenshot from here, Merge with previous, Copy / Duplicate screenshot and Delete step. With rounded boxes, the selected box shows a round dot on its top edge near the top-right corner: drag it sideways to set how round that box's corners are (saved with the session; Reset box corners on the right-click menu goes back to automatic). The capture window steps aside while a session is recorded and comes back when the recording stops, if it was open when it began. Not copied from ClickTrail: zoom/pan and the automatic close-up.

**Capture prompt**: pressing Record, and "Teach with Claude", first asks what to capture (one window from the list of open ones, the full screen or one screen, or a region for hand recordings) and asks you to confirm the screen is ready. A window capture crops every screenshot to that window.

**Every name in double quotes is a click**: ClickBot turns each action into its clicks, one recorded step each, so the screenshots show every click: `Select "Closed" in "Issue status"` is a click on "Issue status" and then a click on "Closed", `Expand "Schedule impact" in "Additional details"` is "Additional details" then "Schedule impact". The wizard's third column shows them. A name after "beside" or "next to" only says where to look and is not clicked.

Not tested end to end: a real learning run of Claude through InEight. The job file, the commands and the session grouping are tested; the Claude Code launch uses the copy of Claude Code that ships with the Claude desktop app and was not run here.

Limits: matching is by name, so a renamed control needs re-learning; a control with no name is matched only if its class and surroundings are unchanged. Do not type or move the mouse while a run is going. Keyboard-only actions (Enter, Tab, shortcuts) are not recorded yet. Start the app with `--control` to enable the pipe; it is off otherwise.

## Known limits

- Window capture crops the screen, so anything overlapping the window is included.
- Video is recorded at 15 fps through GDI, so very fast motion will look choppy.
- The recorder falls back to an `.avi` if Windows cannot encode MP4.
- Targets 64-bit Windows 10/11 with .NET Framework 4.8 (already installed).

## How it was designed

REA (the reverse-engineering toolkit) was used to look at the installed Snagit at a high level:
a native C++/MFC application using DXGI desktop duplication and GDI `BitBlt` capture, DWM window
bounds, global hotkeys, and OpenCV / ONNX / OCR components for its smart features. Its resource
strings gave a feature inventory. Only those general techniques informed this app. No Snagit code,
images, icons or file formats were copied; every line and every icon here is original. See `DESIGN.md`.
