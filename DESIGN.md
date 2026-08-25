---
name: AutomateX — The Rack Elevation
description: The instance you own, drawn as the front face of the rack it runs on.
colors:
  rack-void: "#0E1113"
  panel-face: "#161A1D"
  panel-recess: "#1D2225"
  engrave: "#272E33"
  rail: "#39434A"
  rail-num: "#7E8B93"
  silkscreen: "#93A0A8"
  legend: "#C3CDD4"
  stencil: "#EBF0F3"
  lamp-clear: "#3DD68C"
  lamp-active: "#4DA3FF"
  lamp-hold: "#F2B233"
  lamp-fault: "#FF5C4D"
  lamp-dark: "#39434A"
typography:
  panel-head:
    fontFamily: "IBM Plex Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "24px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  unit-title:
    fontFamily: "IBM Plex Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "18px"
    fontWeight: 500
    lineHeight: 1.35
    letterSpacing: "normal"
  body:
    fontFamily: "IBM Plex Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.55
    letterSpacing: "normal"
  silkscreen:
    fontFamily: "IBM Plex Sans, ui-sans-serif, system-ui, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    lineHeight: 1.2
    letterSpacing: "0.1em"
  data:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  rail-num:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: "11px"
    fontWeight: 400
    lineHeight: 1
    letterSpacing: "0.08em"
rounded:
  none: "0px"
  control: "2px"
spacing:
  hair: "4px"
  tight: "8px"
  unit: "16px"
  bay: "24px"
  section: "40px"
components:
  unit:
    backgroundColor: "{colors.panel-face}"
    textColor: "{colors.stencil}"
    rounded: "{rounded.none}"
    padding: "16px 24px"
    height: "auto"
  button-primary:
    backgroundColor: "{colors.lamp-clear}"
    textColor: "{colors.rack-void}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-secondary:
    backgroundColor: "{colors.panel-face}"
    textColor: "{colors.stencil}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  button-disabled:
    backgroundColor: "{colors.panel-recess}"
    textColor: "{colors.silkscreen}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
  input:
    backgroundColor: "{colors.panel-recess}"
    textColor: "{colors.stencil}"
    rounded: "{rounded.control}"
    padding: "8px 12px"
  label-silkscreen:
    textColor: "{colors.silkscreen}"
    typography: "{typography.silkscreen}"
---

# Design System: AutomateX — The Rack Elevation

## Overview

The world is the **rack elevation drawing**: the U-numbered front-view schematic a
self-hoster makes of their own rack. Equipment occupies whole units down a numbered
rail, labels are printed onto the panel face, and each unit carries a status lamp in
the same place on every face, so the whole stack reads as one column of state.

This is chosen for a reason the product can defend: AutomateX is the automation engine
you run on hardware you own. The elevation drawing is the document of ownership. It is
also honest about the operating scene — a technically fluent operator checking, at odd
hours in a dim room, whether the things they wired up overnight actually ran.

**Dark is chosen from the use scene, not from category habit.** The rack lives in a
dim room and the operator checks it at night.

**The risk this direction was chosen against, and how it is answered.** Stacked labelled
units sit close to a list of cards, so this could have been a re-skin that changed
nothing. The answer is that the rail is structural, not decorative: units are bounded by
the rail and by hairline rules, never by a box. Removing the card is the change.

## Colors

### Primary

`lamp-clear` `#3DD68C` — the healthy lamp, and the accent on the AutomateX wordmark.
Green is not decorative here: it is the colour of a clear signal. The brand mark and
"this ran fine" being the same colour is the point.

### Secondary

There is no secondary accent. Colour is illumination; a surface that is not asserting
state carries no colour at all.

### Neutral

| Token | Hex | Role |
|---|---|---|
| `rack-void` | `#0E1113` | the space around equipment; page ground |
| `panel-face` | `#161A1D` | equipment front face; the unit band |
| `panel-recess` | `#1D2225` | recessed areas: inputs, code, disabled controls |
| `engrave` | `#272E33` | hairline rules; the engraved separations |
| `rail` | `#39434A` | the rack rail itself, and an unlit lamp |
| `rail-num` | `#7E8B93` | rail numerals — 5.00:1 |
| `silkscreen` | `#93A0A8` | printed labels, disabled control text — 6.53:1 |
| `legend` | `#C3CDD4` | secondary body text — 10.84:1 |
| `stencil` | `#EBF0F3` | primary text — 15.25:1 |

### Named Rules

1. **Colour is illumination.** Colour appears only on lamps and focus rings. Body text is
   always achromatic — never green, red, or amber prose.
2. **The palette is closed and every lamp has one reserved meaning.** `lamp-fault` red
   means a run failed. It may never be spent on a delete button, a destructive
   confirmation, or a decorative highlight.
3. **Foreign colour is never tinted.** Plugin and connection icons keep their own brand
   colour. The palette governs what we draw, not what we host.

### Lamp vocabulary

| Lamp | Hex | Means | On face |
|---|---|---|---|
| `lamp-clear` | `#3DD68C` | succeeded / healthy | 9.33:1 |
| `lamp-active` | `#4DA3FF` | running | 6.67:1 |
| `lamp-hold` | `#F2B233` | waiting, or awaiting approval | 9.33:1 |
| `lamp-fault` | `#FF5C4D` | failed | 5.74:1 |
| `lamp-dark` | `#39434A` | idle / never run — unlit | — |

Every lamp clears 4.5:1 on the panel face, so a lamp colour may also carry its own label.

## Typography

Two families, both from the IBM Plex superfamily — a face drawn for an engineering
identity, sharing one skeleton across sans and mono.

**Self-hosted, never CDN-linked.** PRODUCT.md principle 1 is that nothing may require a
hosted service or phone home; a Google Fonts link would break that on an air-gapped
homelab install.

### Hierarchy

| Role | Size | Weight | Notes |
|---|---|---|---|
| `panel-head` | 24px | 600 | page heading |
| `unit-title` | 18px | 500 | the equipment name |
| `body` | 14px | 400 | descriptions, prose |
| `data` | 13px mono | 400 | timings, IDs, cron, JSON — `tabular-nums` |
| `silkscreen` | 11px | 600 | uppercase, `0.1em` tracked, field labels |
| `rail-num` | 11px mono | 400 | rail numerals |

Steps run 11 → 14 → 18 → 24: ratios of 1.27, 1.29, 1.33, every step clearing 1.25. The
incumbent scale ran six sizes across a 1.7:1 total range, which is why it read flat.

**11px is the floor.** Nothing smaller ships. The incumbent 10px category tags are the
single largest defect in the audit.

### Named Rules

- **Mono is for data, never for costume.** IDs, timestamps, durations, cron expressions,
  JSON, and rail numerals. Never for headings, buttons, or nav.
- **Tabular numerals everywhere numbers align.** Execution tables, durations, counts.

## Layout

**The rail** is the organising structure of every list surface: a 40px column on the left
carrying a hairline vertical rule and mono unit numerals. Entities occupy **units** —
full-width bands seated against the rail, minimum height 56px, separated by a 1px
`engrave` rule.

**The lamp column.** Every unit's lamp sits at one fixed x-position immediately right of
the rail, so state reads as a single scannable column down the whole stack.

Body measure is capped at 72ch. Spacing runs 4 / 8 / 16 / 24 / 40, with more space above
a heading than below it.

## Elevation & Depth

The rack face is lit from above. Depth is physical, never a glow.

### Shadow Vocabulary

- **Seated** (units): no shadow. A 1px top highlight at `rgba(255,255,255,0.04)`.
- **Floating** (dropdowns, dialogs, popovers): `0 8px 24px -8px rgba(0,0,0,0.7)` — real
  offset, real blur.
- **Recessed** (inputs, code): `panel-recess` fill with a 1px `engrave` border, no shadow.

A zero-offset coloured halo is not part of this system.

## Shapes

The rack is square. `0px` on units, bands, and every full-width surface. `2px` on
controls and inputs only. Nothing in this system is pill-shaped or softly rounded.

## Components

### Buttons

Primary is a lit control: `lamp-clear` fill, `rack-void` label, 10.10:1. Secondary is a
panel control: `panel-face` fill, 1px `engrave` border, `stencil` label.

**Disabled is unlit and engraved, never faded.** `panel-recess` fill with `silkscreen`
label at **5.99:1**. Opacity-based dimming is banned: the incumbent `disabled:opacity-50`
measured 1.1–1.2:1 and is recorded in PRODUCT.md as a known AA failure.

### Chips

Status chips carry their lamp colour as a 1px border and an 8px lamp square, with the
label in `stencil`. The chip is never filled with the lamp colour.

### Cards / Containers

**There are no cards.** A unit is a band on the rail, bounded by rule and spacing.

**Enclosure is banned: a container may never sit inside another container.** The audit
found 38 nested-card instances; this rule is what removes them and what keeps them gone.

### Inputs / Fields

`panel-recess` fill, 1px `engrave` border, `stencil` text, `silkscreen` placeholder.
Focus is a 1px `lamp-active` ring with a 1px offset — not a 3px glow.

### Navigation

The rail runs in **two registers**, because identity, destinations and account are
three different kinds of thing and one row made them compete:

1. Wordmark and workspace selector on the left, account on the right — which
   instance, as whom.
2. Destinations on their own line, in `silkscreen` caps. The active destination is
   the only one in `stencil`, with a 2px `lamp-clear` underline — the only place the
   brand lamp appears in the chrome. No pills, no filled tabs, and no icons up here;
   icons belong in the touch menu where they aid scanning.

Six destinations fit at 768px in two registers where one row needed ~1050px.

### Readout strip

The dashboard's figures sit on one strip divided by `engrave` rules, labels in
`silkscreen` caps above `data`-step mono values with `tabular-nums`. Not four cards:
the strip reads as one instrument, and it avoids the hero-metric template (big
number, small label, supporting stat, accent) that this category ships by default.

### Signature component: the Unit

```
│ 04 │ ▪ │ HEARTBEAT                        │ CRON · * * * * *  │ 1m ago │
│    │   │ Dev seed: fetches GitHub zen…    │                   │        │
└ rail  lamp   unit-title + body               silkscreen + data
```

Rail numeral, lamp at fixed x, title and description, then right-aligned data in mono.
One invariant label grid — identity, state, timing — repeated without exception across
workflows, executions, connections, and plugins.

## Do's and Don'ts

### Do:

- Put colour only where something is asserting state.
- Let the rail and a hairline rule do the bounding.
- Use mono for data and measurement.
- Give disabled controls a real, readable colour.
- Keep 11px as the smallest type that ships.
- Theme the browser's own surfaces — selection, caret, scrollbar, focus ring — from this
  palette.

### Known and rejected

**`clipped-overflow-container` on the workflow canvas (8 findings).** The detector
flags `div.react-flow.dark` and the canvas well for clipping a positioned child.
Both are correct and both are structural: a pan/zoom canvas works by clipping a
transformed viewport. Verified that no graph node renders outside the frame (0 of
7 on the branching demo). Left visible rather than suppressed — `ignoreValues`
does not apply to this rule, and silencing the rule project-wide would also hide
a genuinely clipped dropdown or tooltip later.

### Don't:

- Nest a container inside a container.
- Colour body text.
- Spend `lamp-fault` red on anything but a failure.
- Fade a control with opacity to say it is unavailable.
- Tint a plugin's own icon.
- Reach for a pill, a soft shadow, or a glow.
