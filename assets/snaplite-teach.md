---
name: snaplite-teach
description: Perform one activity of an InEight procedure (browser or desktop client) by driving XLCapture, and save what was done as a XLCapture session (the guide file) that ClickBot can later replay quickly with different data. Use when the user points at a job file from XLCapture's ClickBot tab ("Add a procedure..."), gives a procedure (activity > task > action) and asks to "teach", "learn", "perform", "run through" or "do the clicks for" it, or asks to replay a saved session with new values.
---
<!-- xlcapture-skill: 2 -->

# snaplite-teach

XLCapture has a local control channel (its program file is at `XLCAPTURE_EXE`: the path is written in the prompt you were given and in the job file as `xlcapture`; wherever this guide says XLCAPTURE_EXE, use that path). You use it to look at an app, click, choose and type in it, and record each action as a session step (screenshot taken just before the action, target details saved for replay). ClickBot, the fifth tab in XLCapture, replays the saved session by itself with new values.

Put the JSON in a file and call it, so quoting is never a problem:

```bash
"XLCAPTURE_EXE" --cmd @req.json --out reply.json
```

Read `reply.json` afterwards (`ok` is true or false; on false, `error` says why). If XLCapture is not running, the first command starts it. If it is running without its control channel the reply says so: ask the user to start it from ClickBot's "Add a procedure..." or restart it with `--control`.

## Before you start

- A learning run really clicks and types. Make sure the target is a **test or sandbox** InEight environment unless the user clearly says otherwise.
- The user must not touch the keyboard or mouse while you work.
- The user signs in themselves. Never ask for or type passwords.
- The job file holds the values for this run. Use them only in the fields they belong to. Never repeat them back in chat, and never put them in `stepText`.

## Teach from a job file (the normal way)

The user fills in ClickBot, which writes a job file under `%LOCALAPPDATA%\SnapLite\jobs\` and tells you its path. Read it. It has:

- `sessionName`: the activity's name. The saved session must carry this name.
- `system`: the app the activity is done in (for example `Change`, meaning InEight Change).
- `procedure`: pass this object, unchanged, to `session-start`. It stores the activity, its tasks and the names of its inputs in the session file.
- `capture`: what the session captures. `mode: "window"` means the window with that `process` and `title` (use it as the `window` of every command, and every screenshot is that window); `screen` or `monitor` means the monitor the action is on. The user has already confirmed the screen is ready.
- `tasks[]`, each with `task` (the label to save on every step, such as `3.3 Enter the issue details`) and `actions[]`. Each action has `code`, `text`, `kind`, a `plan`, and for inputs `param`, `field`, `type`, `optional`, `value`, `choices`.

**Every name in double quotes in an action is a click on the screen, and each click is its own recorded step, so the screenshots show every click.** The action's `plan` lists them in the order to do them. For `Select "Closed" in "Issue status"` the plan is: click `Issue status`, then click `Closed`. A plan step is one command:

| plan `do` | Command | Notes |
|---|---|---|
| `click` | `click` with `target.label` = the plan `target` | one step per click |
| `type` | `type` with `text` = the action's `value` and `param` | `target` is the field (typing into it is its click); an empty `target` means the field that just opened or has focus. `locator`, if present, only says where to look (for example the label beside the field) and is not clicked |
| `choose` | `choose` with `text` = the action's `value` and `param` | the option's visible name; it must be on screen, so scroll or open the list first. With a `locator` and no earlier click, it is a list item or tab of that kind (for example a "Resource Type" tab) |

Steps:

1. `{"cmd":"windows"}` lists open windows. Find the window named by `capture` (or the InEight window for `system`: the browser window showing InEight, or the desktop client). If it is not open or not signed in, stop and ask the user.
2. `{"cmd":"session-start","name":"<sessionName>","procedure":<the procedure object>}`.
3. Go through the tasks in order, and each task's actions in order. For every plan step, `observe` first (it returns the controls, the page address and a screenshot path: **read the screenshot**), then act. Every command you send for an action carries `"task":"<task label>"`, `"actionCode":"<code>"` and `"stepText":"<action text>"`, so the session is grouped by task and its text comes from the procedure.
   - If `optional` is true and `value` is empty, do nothing for that action.
   - `kind: "skip"`: do nothing and record nothing. It is for a person (reading, repeating).
   - A list or dropdown that is not yet open: the plan's `click` on the field opens it, then `observe` again before the `choose`. Yes/No radio buttons need no extra click beyond the plan. If `type` is `date`, the plan types the date into the field.
   - Give every command of one action the same task, actionCode and stepText.
4. After each action check `windowNow` and `warning` in the reply and the next screenshot. If the screen is not what the procedure expects, `observe` again and try the nearest sensible control. If you are stuck, stop and tell the user which action (its code and text).
5. `{"cmd":"session-end"}` saves the session. Check the reply: `saved` is the file, `steps` the count, `inputs` the inputs. Report to the user: the session name, how many steps, and any action you skipped or could not do. Do not list the values.

If you stop part-way, still send `session-end` so the steps so far are kept, and say the session is partial.

## Commands

All take a `window` of `{"process":"msedge","title":"..."}` where they act on a window.

| Command | What it does |
|---|---|
| `ping`, `windows`, `stop` | check the channel, list windows, stop a running replay |
| `observe` | controls (`index`, `label`, `role`, `id`, `className`, `path`, `bounds`), `window.urlBar`, `screenshot` PNG path. Indexes are valid only for the latest observe of that window |
| `session-start` | `name`, optional `procedure` object |
| `click` | `target`: `{"label","role","id","index"}`. Name the control by what it is, not only by index, because the description is saved for replay |
| `type` | `target`, `text`, `param` (the input's name; the text itself is never saved) |
| `choose` | `text`: the option's visible name (clicked with the real mouse so the app sees the selection), `param`. The option must be visible on screen |
| `scroll` | `direction`: up, down, left, right |
| `session-end` | `save` (default true) |
| `replay` | `path`, `values` object or `csv` path, `strict` |
| `export` | `path`, `folder`: writes `clicks.json` and `shot-NN.png` for the click-guide-builder skill |

Extra fields on `click`, `type`, `choose`: `task`, `actionCode`, `stepText`.

Limits: Enter, Tab and shortcuts are not recorded, so use the on-screen control that does the same thing. A choice can only be clicked if it is on screen, so scroll a long list first. A control with no name can be clicked by `index`, but replay can then only find it by class and surroundings: tell the user.

## Without a job file

If the user just describes a procedure, write the steps out yourself in the same shape (activity, tasks, actions), ask for any value an action needs, then follow the same loop. Give each value-taking action a short `param` name.

## Guide document

`{"cmd":"export","path":"<saved .clicktrail>","folder":"<parent folder>"}` writes a folder with `clicks.json` (each screenshot carries its `task`, each click its `stepText`) and the screenshots. Then use the **click-guide-builder** skill on that folder. Use each task as the slide heading and each `stepText` as the step.

## Replay with different data

ClickBot does this itself: choose the session, press Run, and it asks for the values. Through the channel:

```json
{"cmd":"replay","path":"<saved .clicktrail>","values":{"issue_name":"Culvert damage"}}
```

For several runs give a CSV (header row = input names): `{"cmd":"replay","path":"...","csv":"C:\\path\\rows.csv"}`. An optional input left out is skipped. The reply lists steps done, a log per run and `saved` (each replay records itself as a new session). If it stops, `error` names the step.
