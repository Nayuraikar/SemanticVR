# Phase 1b: application-driven scenes (Cockpit, Medical)

Sample list: **01 Street, 02 Signals, 03 Cockpit, 04 Medical**. 03_Cockpit replaces 03_Forest as the third sample. The Forest
builder is kept (`SemanticVR > Build Legacy Forest Scene`) but is no longer built, synced or listed in `package.json`.

> **Inspired by, not exact copies of, aviation and medical colour conventions.** The scenes borrow the general idea (red = warning,
> amber = caution, green = normal/active, magenta/cyan on displays; red/yellow/cyan alarm priority; coloured gas cylinders and
> drug-label classes) to build realistic confusion cases for CVD research. They do not reproduce any specific standard
> (e.g. FAA/EASA annunciator rules, IEC 60601-1-8, ISO 32, ISO 26825, or any national cylinder colour code), and must not be used
> for training, certification or clinical purposes.

## Palette method

`python/scripts/phase1b_palette.py` computes CIEDE2000 between every colour pair of a scene group, for normal vision and for
Machado deutan / protan at severity 1. It uses `semanticvr_ref` (the same float64 reference the C#/HLSL parity tests use).
Colours are clamped to [0,1] after simulation, as on screen and in `SemanticGraph`. A pair is **confusable** when
ΔE00(normal) >= 10 and ΔE00(deutan) <= 15 (the `SemanticGraphGizmos` defaults). The colours in the script must match
`DefaultClassLibrary.cs`.

Run from `python/`: `python scripts/phase1b_palette.py`.

## Classes

| Scene | Class (id) | priority | tau | States |
|---|---|---|---|---|
| Cockpit | Warning (13) | 1.00 | 2 | Off, Red |
| | Caution (14) | 0.85 | 4 | Off, Amber |
| | Advisory (15) | 0.50 | 8 | Off, White, Blue |
| | Normal Status (16) | 0.35 | 10 | Off, Green |
| | Display Text (17) | 0.75 | 6 | Green, Magenta, Cyan, Amber, Red, White |
| | Runway Light (18) | 0.80 | 5 | Red, White, Off (PAPI, edge, wing-bar lights) |
| | Grass (5), Sky (6) | | | reused (memory colours) |
| Medical | Alarm High (19) | 1.00 | 2 | Off, Red |
| | Alarm Medium (20) | 0.85 | 4 | Off, Yellow |
| | Alarm Low (21) | 0.55 | 8 | Off, Cyan (added: the brief asks for cyan banners) |
| | Vital Trace (22) | 0.70 | 6 | Ecg green, Spo2 cyan, Bp red, Temp yellow |
| | Gas Cylinder (23) | 0.90 | 4 | Oxygen, Helium, Air, NitrousOxide, CO2, Vacuum |
| | Drug Label (24) | 0.90 | 4 | Yellow, Blue, Red, Orange, Violet, Green |
| | Status Light (25) | 0.90 | 4 | Off, Green, Amber, Red |
| | Tissue Tint (26) | 0.85 | 5 | Pale, Cyanosis, Wound (added: tint regions on the patient) |
| | Skin (12) | 0.30 | 2 | Light (existing memory-colour class, small tau, flagged) |

Warning / Alarm High have the highest priority and the tightest tau because their hue is the message.

## Intended confusable pairs (ΔE00, severity 1)

Cockpit:

| Pair | normal | deutan | protan | Where |
|---|---|---|---|---|
| Warning Red / Normal Green | 73.2 | **10.5** | 27.9 | annunciator rows 0 and 3, overhead lights |
| Caution Amber / Normal Green | 41.7 | **14.0** | 7.0 | annunciator rows 1 and 3 |
| Display green / amber | 43.3 | **9.7** | 9.6 | PFD FMA vs flag, ND mode vs traffic |
| Display magenta / cyan | 54.6 | **12.6** | 28.7 | selected bugs vs ILS scales |
| Advisory blue / Display magenta | 38.2 | **11.3** | 5.9 | mode strip vs PFD bugs |
| Display green / red | 78.5 | 16.7 | 35.6 | does *not* quite collapse |
| Warning Red / Caution Amber | 37.0 | 17.0 | 29.6 | does *not* quite collapse (kept as a control) |
| PAPI Red / White | 44.1 | 35.5 | 45.7 | stays distinguishable (large luminance gap): a control |

Medical:

| Pair | normal | deutan | protan | Where |
|---|---|---|---|---|
| Drug Blue / Violet (also N2O / Violet) | 19.0 | **3.0** | 2.4 | syringe tray |
| Gas Oxygen / Helium | 42.0 | **11.9** | 20.3 | adjacent cylinders |
| Drug Red / Green | 68.9 | **9.8** | 25.1 | syringe tray |
| Drug Orange / Green | 52.8 | **14.1** | 7.2 | syringe tray |
| Alarm/Trace Red / ECG Green | 78.5 | 16.7 | 35.6 | monitor (just misses; ECG vs Temp does collapse) |
| Trace ECG green / Temp yellow | 31.0 | **12.7** | 7.1 | monitor |
| Pump Green / Red | 73.2 | **10.5** | 27.9 | infusion pump states over time |
| Pump Green / Amber | 41.7 | **14.0** | 7.0 | infusion pump states over time |
| SpO2 cyan / Cyanosis tint | 26.6 | **7.0** | 8.5 | monitor vs fingertips (distance-limited) |
| Skin / Wound | 30.8 | 23.6 | 31.1 | stays distinguishable: a control |
| Alarm High Red / Medium Yellow | 52.9 | 23.7 | 38.5 | stays distinguishable: a control |

The Python table lists every pair; some collapsing pairs are far apart in the room (e.g. gas rack vs syringe tray) and so are not
graph edges (edge range is 1.2 m in Medical, 0.55 m in Cockpit).

## Measured confusable edges (PlayMode, deutan severity 1, ΔE00 sim <= 15, original >= 10)

| Scene | tags | edges | confusable | test minimum |
|---|---|---|---|---|
| 01_Street | 68 | 225 | 60 | 3 |
| 02_Signals | 15 | 23 | 4 | 3 |
| 03_Cockpit | 130 | 1031 | 125 | 50 |
| 04_Medical | 41 | 213 | 21 | 12 |

`SampleScenesTests.MinConfusableEdges` holds the minimums (set well below the measurements).

## Design notes

- **PFD / ND**: neutral textures (horizon, tapes, compass arc, range rings) on Quads; every colour-coded element (green active, magenta
  selected, cyan tuned, amber caution, red warning) is a separate tagged `Display Text` panel. A colour baked into a texture is
  invisible to a per-object palette, so only tagged elements can be corrected.
- **PAPI**: left bank static on-slope (WWRR); right bank cycles through every indication via phase-offset FSM targets.
- **Medical traces** are one ribbon mesh per trace (saved as an asset) so each trace is one tag.
- **Cyanosis**: lips and fingertips fade Pale ↔ Cyanosis on a 12 s FSM; Pale equals the skin albedo, so the tint appears from nothing.
- Text is shown as placeholder bars (no font rendering in the samples), as in 02_Signals.
- The ceiling renderers in the interiors do not cast shadows so the directional light (same as Phase0) reaches the panels.

## Not verified visually

See the report that accompanied this change; summary: stereo Game view, HUD layout, emissive look under URP post-processing,
the dynamic FSM appearance over time, and Android/Cardboard.
