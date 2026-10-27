# XLCapture - design

## Subject
Capture-and-annotate tool for people who document software: how-to guides, procedures, bug reports,
reviews. Primary job: grab something, mark it up, send it, in under ten seconds. It is a tool people
work in all day (Operate mode): the interface should be calm, readable and predictable, and get out of
the way of the screenshot.

## Visual direction: "calm workbench"
A light, neutral surface for an office in daylight; ink type; one teal-green accent that marks the one
thing to do next and what is chosen. Red is kept for recording and for the marks people draw on
screenshots, so it always means "this is live" or "this is yours". The XLYOUR logo (charcoal and gold)
stays as the logo and is not used as interface colour.

| Role | Token | Hex |
|---|---|---|
| Main action, chosen tab icon, switches on, check marks, focus | Accent | #0B7A6A |
| Hover / pressed on the accent | AccentDark / AccentDeep | #086658 / #065247 |
| Selected rows, accent hover | AccentTint | #E3F2EE |
| Large selected fills with ink text on them | AccentSoft | #C6E8E0 |
| Recording, stop, errors, default annotation colour | Mark | #D62B2B |
| Text | Ink | #1C1F22 |
| Secondary text | Slate | #5E656C |
| Disabled text, switch track off | Mist | #8D949A |
| Edge of a control you press or type in | Line | #C9CDC9 |
| Dividers, card edges | Rule | #E2E4E1 |
| Window ground | Paper | #F3F4F2 |
| Panels | Sheet | #FAFAF9 |
| Quiet control under the pointer / pressed | Hover / Press | #ECEEEB / #E2E5E1 |

All tokens are in `src\Theme.cs`; nothing else hard-codes interface colours.

## Type
Segoe UI everywhere (the platform face of a native Windows tool). Segoe UI Semibold for headings,
buttons and the chosen tab; regular for everything else. Sentence case for labels ("Copy to clipboard",
not "Copy to Clipboard"), no colons after field labels.

## Components
- **Buttons**: one shape, a 6px corner. The main action is filled with the accent, white text. Other
  buttons are white with a 1px Line edge. Quiet buttons (toolbars, the bottom bar) have no edge until
  the pointer is on them. At most one filled button per window or dialog.
- **Drop-downs**: white box, 1px Line edge, a plain chevron; the edge darkens on hover and turns the
  accent with focus. The list that drops down is light, like the menus.
- **Menus**: white, 10px corners, a soft system shadow, a grey rounded hover, accent check marks.
- **Switches**: grey track off, accent track on, white knob either way.
- **Check boxes / radios**: grey ring off; accent fill with a white tick, or an accent dot, on.
- **Tabs (capture window)**: the chosen tab is a white sheet on the grey ground with its icon in the
  accent; the others are quiet until hovered.
- **Cards**: white, 12px corners, a 1px Rule edge, no shadow. Each column opens with a short heading.
- **Focus**: a solid 1.5px accent ring on every control (no dotted rectangles).
- **Hotkeys** are shown as keycaps (`KeyHint`).
- **Not built yet** features are not shown, rather than shown greyed out.

## Motion
Only feedback: hover and press colour changes, the switch knob sliding, menus fading in over ~75 ms.
Nothing animates on keyboard actions. The capture button shrinks slightly while pressed.

## Rules
Contrast >= 4.5:1 for text, >= 3:1 for the switch track, check marks and the focus ring (control edges
are lighter on purpose; every control also has its label beside it), a label or tooltip on every icon
button, an 8px spacing grid, layouts written with `Dpi.S(...)`.

## Feature matrix
See README.md for the current, honest status of every feature.
