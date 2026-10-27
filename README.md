# XLCapture

> XLCapture was called SnapLite until now. The program is `XLCapture.exe` and every window, message and menu says XLCapture. Your settings, presets and library still live in the `SnapLite` folders under AppData, and sessions in your Pictures\SnapLite folder, so nothing you had is lost; the project folder keeps its name too.

A local screen-capture, recording and annotation app for Windows. Original code, no installs,
no network use, no accounts. Built with the C# compiler that ships with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1      # makes XLCapture.exe from src\*.cs
```

Run `XLCapture.exe`. It opens the capture window and keeps an icon in the system tray
(`XLCapture.exe --tray` starts it hidden). Captures are saved to `Pictures\XLCapture`.

## Two windows, laid out like Snagit

**Capture window**: All-in-One / Image / Video / Sessions tabs on the left; a card with two
headed columns (Capture: Selection, Effects and Share; Options: Preview in editor, Copy to clipboard,
Cursor and Time delay); the round Capture button (red Record on the Video and Sessions tabs) with its
hotkey shown as keys, and Reset tab under it; Presets, Save preset and "Open XLCapture Editor" along the bottom.
All-in-One: click a window or drag an area, then choose Image, Video, GIF, Scroll, Panorama or Text.
The pick screen is the same for All-in-One, Region, Scrolling window, Panorama, Video, GIF and Sessions: the window or pane under the pointer is highlighted (a browser shows just its page), a click takes it, a drag takes the area drawn, the wheel steps to a larger or smaller area, and F takes the whole monitor.
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
| Follow a procedure (beta, optional, on the Sessions tab): choose a Process Manager export (.xlsx or .csv) and an activity; a checklist panel shows its tasks and actions while you record, and each step carries its task and action | Tested on a real Process Manager export |
| `.clicktrail` files: read ClickTrail's files, save ones ClickTrail can open | Tested on 5 real ClickTrail sessions (load, re-save, reload identical) |
| Session view in the editor: rename, edit / delete / reorder steps, browse screens | Rendering tested |
| Edit a screen as a normal picture; export pictures; export a PDF guide | Built |
| Editor tray filter: All / Images / Videos / Sessions with counts | Rendering tested |
| Guide folder... exports `clicks.json` + `shot-NN.png` for the click-guide-builder skill (PowerPoint) | Folder written; deck not built here |

**Session editor** (the editing functions of ClickTrail's session editor): merge a screen with the previous one (within a task), start a new screenshot from a step (every step now keeps its own picture, as a JPEG, so the new screen shows what the screen looked like at that step), add a step by hand, drag a box to move it and its corner to resize it, drag a number, crop (drag, handles, Enter and Esc), paste a screenshot or drop an image file on it (and Use original), choose which picture a screen shows (taken at a step, or after typing), delete a step or a whole screenshot, undo (up to 40 steps, Ctrl+Z), box and number colours. Merge, split, crop, pasted pictures, manual steps, moved numbers and the chosen picture are saved in the session file and used by the exports (pictures, PDF, guide folder). Right-click a step for New screenshot from here, Merge with previous, Copy / Duplicate screenshot and Delete step. With rounded boxes, the selected box shows a round dot on its top edge near the top-right corner: drag it sideways to set how round that box's corners are (saved with the session; Reset box corners on the right-click menu goes back to automatic). The capture window steps aside while a session is recorded and comes back when the recording stops, if it was open when it began. Not copied from ClickTrail: zoom/pan and the automatic close-up.


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
