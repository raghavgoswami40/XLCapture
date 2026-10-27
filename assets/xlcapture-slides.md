---
name: xlcapture-slides
description: Build the PowerPoint slide pack for one activity (or several) from XLCapture "slide pack" folders (slides.json plus slide-NN.png and the Ventia template), using the bundled build_deck.py. Use when the user points at a folder that holds slides.json from XLCapture's Export > Slide pack for Claude, or asks to "build the slides", "make the slide pack" or "make the deck" for a recorded activity or session.
---
<!-- xlcapture-skill: 3 -->

# xlcapture-slides

XLCapture records an activity (a task-by-task procedure such as "Setup InEight Billings") and exports a **slide pack folder**. The deck is built by `build_deck.py`, which sits next to this file (`~/.claude/skills/xlcapture-slides/build_deck.py`). It does the layout deterministically, on the client's template, the way Ventia's completed decks are laid out. Your job is to run it with the right inputs, fill in the one thing it cannot see (the highlight on the process diagram), check the result and report.

## What the deck looks like

- Cover (process code and title, author), then for each activity: a divider slide (activity number such as `02`, activity title, process), a process diagram slide when the pack has `diagram.png` (the activity's node ringed in lime), then one slide per screenshot, then the closing slide (date, e-mail). The template's instruction slides are removed.
- A content slide has: process title, `<activity code> <activity title>`, the task line (`6.1 Open the project`, with "(continued)" on the second and later slides of a task), the screenshot, the highlight rectangles, and the numbered step texts (`1. Log into InEight Platform`, numbering restarts on every slide).
- **The screenshot is placed exactly where the completed decks put it**: scaled to fit a 16:9 frame (7956000 x 4475163 EMU) centred on the slide, top at 2168525 EMU, in the template's picture placeholder. The pictures in the pack carry no marks; the boxes are native rounded rectangles over the picture and every step text is a text box at the position it had in XLCapture's editor. Text that was placed **outside** the screenshot in the editor therefore sits outside the picture on the slide (right-aligned against the picture's left edge when it is on the left).

## The pack folder

| File | What it is |
|---|---|
| `slides.json` (schemaVersion 2) | process, activity, tasks, and each slide with its steps: `text`, `index`, `box` and `textBox` (`x, y, w, h` in the picture's pixels; `textBox.outside` is true when it lies outside the picture) |
| `slide-01.png`, ... | the clean screenshot for each slide (cropped as in the editor, no marks) |
| `template.pptx` | the client's template, copied from the export (absent when the activity is to be added to an existing deck) |
| `appendTo` (in `slides.json`) | the path of an existing deck this activity is to be added to, or null for a new deck |
| `diagram.png` | the process flow diagram Process Manager exported beside the procedure file (only if found) |
| `claude-prompt.txt` | the sentence the author pasted |

## Steps

1. **Read** `slides.json`. Check `schemaVersion` is 2 and every `image` exists. If it is missing or older, stop and tell the author to export the pack again from XLCapture.
2. **Python**: `python -c "import pptx, PIL"`. If it fails, `pip install python-pptx` (Pillow comes with it).
3. **Diagram highlight** (only when `slides.json` has `diagram`): open `diagram.png` and look at it. Find the node of this activity (the activity number from `activity.code` is the black number badge on the node; its title is written inside). Set `diagram.highlight` in `slides.json` to that node's **centre and size as fractions of the picture** (0,0 is top left, 1,1 bottom right), with a small margin round the node: `{"cx": 0.25, "cy": 0.55, "w": 0.12, "h": 0.55}`. Save `slides.json`. If you cannot tell which node it is, leave `highlight` null and say so in the report.
4. **Build**: if `slides.json` has `appendTo`, this activity is added to that existing deck (go to "Adding to an existing deck" below). Otherwise run
   ```
   python ~/.claude/skills/xlcapture-slides/build_deck.py "<pack folder>" --template "<pack folder>/template.pptx" --author "<author>" --date "<date as the author writes it, e.g. 24 October 2026>" --email "<author e-mail>"
   ```
   Give several pack folders in one command to build one deck for a whole process (one cover, each activity in turn, one closing slide). Ask the author for the author name and e-mail if you do not know them; if they do not want them, pass empty strings and say the cover and closing slide have blank fields. Use today's date for `--date` unless told otherwise. The deck is saved in the first pack folder as `<activity title> - slide pack.pptx` unless you pass `--out`.
   ### Adding to an existing deck
   When `appendTo` is set, run
   ```
   python ~/.claude/skills/xlcapture-slides/build_deck.py "<pack folder>" --append "<appendTo path>"
   ```
   It copies the look of the deck's own divider, diagram and content slides, puts the activity in its place by number (before the next higher activity, else before the closing slide), leaves the cover and closing slide alone, and saves a **new file** `<deck name> - updated.pptx` in the pack folder; the original deck is never changed. If the deck already has an activity with that number the builder stops: ask the author whether to replace it, and only then add `--replace`. Several pack folders can be added in one run. No author, date or e-mail is needed.
5. **Check**: open the deck with python-pptx and confirm: for a new deck the slide count equals cover + (divider [+ diagram] + the pack's slide count) per activity + closing (for an added activity: the old count + divider [+ diagram] + the pack's slide count, and the new slides sit in the right place), no slide still reads "Process Name", "Author", "Activity", "Task" or "Date", and no instruction slide is left. If PowerPoint or LibreOffice is available, export a few slides to images (PowerPoint: `Slide.Export`; LibreOffice: `soffice --headless --convert-to pdf`) and look at: a divider, the diagram slide, and two content slides (one with text outside the picture). Look for text running off the slide or overlapping other text; if a text box is too small for its text, widen it by editing the pack's numbers is **not** needed, just report it. If you cannot render, say the visual check was not done.
6. **Report**: the path of the deck, the slide counts, whether the diagram highlight was set, and anything left blank or guessed. Do not claim a visual match you did not look at.

## Rules

- Never change the step texts or the pictures, and never invent a step. The slide numbers, task lines and texts come from `slides.json`.
- Never write into the template file; `build_deck.py` only reads it.
- Work only inside the pack folder and this skill's folder. Do not open InEight or any other app, and do not contact any service.
- If the template cannot be opened, or has no cover, divider, content or closing example slide, stop and say which is missing.
