# Phase 2A: C1, light-transport-aware confusion prediction

Two objects are confusable according to their **lit** colour (albedo x irradiance + emission), not their albedo alone. The lit colour
is predicted from the lighting model with no framebuffer readback. The albedo-only score is kept as the baseline and every graph edge
carries both.

## Model (`Runtime/Confusion/LitColorPredictor.cs`)

Linear colour space throughout.

```
E(n)      = SH_L2(n)  +  L_main * max(0, n . l)          # irradiance, "albedo * E = outgoing radiance" convention
lit       = albedo_lin * E(n) + emission_lin
displayed = saturate(lit)                                # SDR target, no tone mapping
```

* **SH term.** `LightProbes.GetInterpolatedProbe` when the scene has baked probes, otherwise `RenderSettings.ambientProbe`. Evaluated
  with the 9 coefficients written out explicitly (`ShMath`). Unity folds every SH constant and the cosine convolution into the stored
  coefficients, so the basis is just `{1, y, z, x, xy, yz, 3z^2-1, xz, x^2-y^2}` with no extra weights. I did not assume this: the
  ordering, signs and scaling were measured against `SphericalHarmonicsL2.Evaluate` (probe script, then
  `LitColorPredictorTests`: 20 random coefficient sets x 50 directions, plus each coefficient alone, tolerance 1e-4).
* **Main light.** The realtime directional light (`RenderSettings.sun`, else the brightest active directional light; baked-only lights
  are skipped because they are in the probes) is added analytically: `colour_linear x intensity x max(0, n.l)`, honouring colour
  temperature and `GraphicsSettings.lightsUseLinearIntensity`.
* **Emission.** `albedo_lin x emissionIntensity` of the current `SemanticState`, exactly what `SemanticTag.EmissionColor` uploads after
  the GPU's gamma-to-linear step.
* **Unlit materials** (shader name contains "Unlit": screens, the sky dome) show their colour as-is, so lit = albedo.
* The maths (`ShL2`, `ShMath`, `Lambert`, `Irradiance`, `Lit`) is blittable structs and `Unity.Mathematics` only, so it is Burst-compatible,
  but **no Burst job is wired up yet**; the graph calls it from the main thread.

### Per-object normal (documented choice)

`SemanticTag.DominantNormal(viewer, toLight)`: take the object's three local axes scaled to world size; each axis has two opposite faces
of area `product of the other two sizes`. The dominant face is the one with the largest area (front of a thin panel, top of a disc, top
of a car body). Axes within 5% of the largest count as tied (spheres, cubes, poles), and among the tied faces the one facing the
**viewer** (`Camera.main`) wins; with no camera, the one facing the light; with neither, the most upward. Override with
`SemanticTag.useNormalOverride` + `normalOverride` (local space).

## Pair graph

`PairEdge` now has `DeltaEOriginal/DeltaESimulated` (albedo-only baseline, unchanged) and `LitDeltaEOriginal/LitDeltaESimulated`
(predicted lit colours through Machado -> Lab -> CIEDE2000, clamped before and after simulation as on screen). `OriginalFor(mode)`,
`SimulatedFor(mode)` and `IsConfusable(mode, ...)` select between them; `CvdDebugState.Scoring` is the overlay/HUD mode (default Lit).

## Lighting change detection (`LightingChangeDetector`)

Fingerprint = ambient SH-L2 coefficients + main-light colour x intensity + direction. `IrradianceFingerprint.Delta` is the RMS over 14
fixed normals (6 axes, 8 cube diagonals) of the Euclidean norm of the linear-RGB irradiance difference. The unit is "linear reflectance of
a white surface": the default threshold 0.03 is a 3% change in how a white surface is lit. The event fires when the delta from the last
committed baseline exceeds the threshold, then the baseline resets, so a slowly moving sun fires once the accumulated change crosses it.
No image statistics are involved. Measured: Day -> Night in 05_NightCity gives a delta of **1.129**. The pair graph rebuilds when
`LightingChangeDetector.Version` changes (the gizmos add a detector to the rig if none exists).

## Controls

* **K**: albedo-only / lit-colour scoring (overlay and HUD row "Scoring (K)").
* **N**: next lighting preset (only scenes with a `LightingPresetSwitcher`; HUD row "Lighting (N)").
* Menus: `SemanticVR > Build NightCity Scene`, `SemanticVR > Validate Lit Prediction`.

## 05_NightCity: design numbers (`python/scripts/phase2a_nightcity.py`)

A shopping street. Painted **Shop Sign** (class 27, no emission) and emissive **Neon Sign** (class 28, emission 1.5) panels hang on
gantries; four vehicles; Sky gets a `Night` state. Both presets use `AmbientMode.Flat`, so the ambient SH is exactly L0 and the Python maths is exact for
this scene. Design normal: a facade facing the viewer, n = (0,0,-1). Confusable = dE00(normal) >= 10 and dE00(deutan, severity 1) <= 15.

| Preset | Sun/moon colour, intensity, Euler | Flat ambient (sRGB) | E at n=(0,0,-1), linear RGB |
|---|---|---|---|
| Day | #FFF1DC, 1.25, (48,-32,0) | #8FA0B8 | (0.984, 0.975, 0.987) |
| Night | #7F9CFF, 0.12, (35,-32,0) | #6B4426 (sodium haze) | (0.165, 0.086, 0.103) |

(Point/spot street lamps are not modelled by the predictor, so Night uses a warm flat ambient instead of lamp lights; the lamp heads and lit
windows are decor shown only at night.)

dE00 as `normal / deutan`:

| Pair (scene tags) | Day | Night | Design target |
|---|---|---|---|
| Neon Red / Shop Green (`Sign_NeonRed_A`, `Sign_ShopGreen_A`) | 69.4 / **10.9** | 62.4 / 44.3 | confusable by **day only** |
| Shop Yellow / Neon Amber (B) | 13.9 / **10.1** | 52.3 / 50.3 | day only |
| Shop Green / Shop Pink (C) | 68.8 / 20.2 | 44.5 / **7.7** | confusable at **night only** |
| Shop Violet / Shop Teal (D) | 37.7 / 17.9 | 26.1 / **7.1** | night only |
| Shop Blue / Shop Teal (D) | 30.6 / 20.5 | 20.2 / **11.0** | night only |
| Shop Blue / Shop Violet (D) | 18.8 / **3.0** | 12.7 / **4.1** | both |
| Shop Red / Shop Green (E) | 68.6 / **9.7** | 41.8 / **7.9** | both |

The albedo-only score of every pair is identical in both presets, so the baseline cannot see any of these flips. The Unity-side lit scores
(`NightCityTests.Edges_FlipBetweenPresets_LitScoreOnly`) match this table to the printed precision, which is a second, independent
check of the C# predictor against the float64 Python reference (`python/tests/test_phase2a.py` pins the design pairs).

## Validation harness (`LitPredictionValidator`, PlayMode tests + `SemanticVR > Validate Lit Prediction`)

For each tagged object: pick a camera standing in front of its dominant face, raycast (temporary `MeshCollider`s on every renderer,
removed afterwards) at a small grid of points on the face (a decal such as a road-sign bar can cover the centre) until it hits the object
head-on, render a 6-degree view to a 32x32 sRGB 8-bit `RenderTexture` (**post-processing off, HDR off so no tone mapping, no AA, CVD pass
off**), read the four centre pixels, decode to linear and compare with the prediction at the same point, normal (interpolated mesh normal
at the hit) and viewer: CIEDE2000.

Skipped and counted separately: **Occluded** (no unobstructed head-on view), **Shadowed** (the point has a ray to the sun blocked by any
collider) and **Textured** materials (only the sky dome). Shadowed objects are still rendered so the size of the shadow miss is
reported; they are excluded from every other statistic. The calibration is 7 known **unlit** colours read back through the same
pipeline: **0.00 dE00** in every scene, so the readback itself is not a source of error.

**What the model does not predict:** realtime shadows (it has none: the main light is added unconditionally), specular highlights and
Fresnel/skybox reflections, additional point/spot lights, metallic surfaces, textures, ambient occlusion and bounce light. Probes and the
analytic Lambert term cannot see shadows or strong specular, by construction.

### Measured prediction error (PlayMode, dE00 of predicted lit vs rendered pixel)

Unshadowed, visible objects only. "Albedo only" is the same comparison using the base albedo as the prediction.

| Scene | sampled | occluded | shadowed | mean | p95 | max | albedo-only mean |
|---|---|---|---|---|---|---|---|
| 01_Street | 60 | 0 | 7 | 1.55 | 2.97 | 3.37 | 5.23 |
| 02_Signals | 8 | 0 | 6 | 0.88 | 2.97 | 2.97 | 11.74 |
| 03_Cockpit | 80 | 1 | 48 | 0.65 | 2.46 | 23.45 | 9.51 |
| 04_Medical | 1 | 1 | 38 | 0.62 | 0.62 | 0.62 | 14.18 |
| 05_NightCity / Day | 19 | 0 | 0 | 1.59 | 3.62 | 3.62 | 4.95 |
| 05_NightCity / Night | 19 | 0 | 0 | 3.93 | 6.45 | 6.45 | 28.21 |
| **All lit samples** | **187** | | | **1.38** | **4.92** | **23.45** | |

Per object type (all scenes pooled): Runway Light 0.08 mean (n 46), Rail Signal 0.18, Neon Sign 0.61, Normal Status 0.60, Advisory 0.50,
Skin 0.62, Grass 1.05, Caution 1.16, Foliage 1.50 (n 24), Hazard Light 1.56, Road Sign 1.95, Vehicle 3.00 (p95 6.45), Shop Sign 3.15
(p95 5.77), Display Text 11.84 (n 2, see below). Files: `results/quality/c1/lit_prediction_<scene>.csv` (+ `_summary.csv`).

Where the error comes from:

* **Night is worse than Day** (3.93 vs 1.59): everything is dark, so the (unmodelled) constant specular/skybox-reflection term the
  renderer adds is a large fraction of the colour. Rendered pixels are consistently slightly brighter than predicted.
* **Shadowed objects** (skipped) show the cost of having no shadow model: mean 10.4 in Street (n 7), 5.2 in Signals (n 6), 2.5 in Cockpit (n 48, max
  35.5), 4.1 in Medical (n 38, max 31.9). **04_Medical is an enclosed room**: its ceiling puts every object in the sun's shadow, so the
  predictor (which adds the sun regardless) is wrong there and the validation has almost nothing to compare; its headline experiment is not evaluable.
* **The single large outlier in Cockpit** (23.45) is `RouteBend`, a zero-thickness ribbon mesh with no meaningful face normal; the
  sample lands on its edge. Excluding the ribbon, the Cockpit max is 2.61.

### Headline experiment (deutan, severity 1, edge range = each scene's overlay range)

Per pair-graph edge, "confusable" (dE00 normal >= 10 and dE00 deutan <= 15) from (a) albedo-only, (b) lit-colour prediction as deployed
(dominant normal, no readback), (c) the rendered ground truth (the validation pixels of the two endpoints, same pipeline). Edges with an
occluded, shadowed or textured endpoint are excluded and counted. "Wrong" = verdict differs from (c).

| Scene | edges evaluated (skipped) | ground-truth confusable | (a) albedo-only wrong | (b) lit-colour wrong |
|---|---|---|---|---|
| 01_Street | 187 (38) | 45 | **32** (FP 18, FN 14) | **23** (FP 12, FN 11) |
| 02_Signals | 8 (15) | 1 | **1** | **0** |
| 03_Cockpit | 295 (736) | 34 | **10** (FP 10) | **10** (FP 10) |
| 04_Medical | 0 (213) | n/a | n/a | n/a |
| 05_NightCity / Day | 30 (0) | 9 | **1** | **0** |
| 05_NightCity / Night | 30 (0) | 16 | **12** (FP 3, FN 9) | **1** (FN 1) |

Mean |simulated dE00 error| vs ground truth over the same edges: Street 3.83 -> 1.89, Signals 4.92 -> 0.62, Cockpit 9.17 -> 1.49,
NightCity Day 3.02 -> 1.38, Night 16.76 -> 2.09 (albedo-only -> lit).

Honest reading: lit-colour prediction is clearly better where the lighting is coloured or strongly different from white (NightCity Night:
12 -> 1 wrong; Street 32 -> 23), and ties in Cockpit, where the 10 wrong edges are the same Caution-amber/Normal-green annunciator pairs
for both methods: the rendered dE00 is 9.85, just under the threshold of 10, while lit predicts 11.9 and albedo 15.4 (lit is closer but
the verdict still straddles the threshold). The 23 remaining Street errors are mostly borderline pairs of large objects (grass, foliage,
cars) where a single sample point and a single dominant normal cannot represent a whole object. Headline numbers depend on the viewer: the
PlayMode tests (rig camera) are the reference; the Edit-Mode menu has no rig cameras, so ties fall back to light-facing and its numbers
differ (e.g. Street 36/36).

CSVs: `results/quality/c1/headline_<scene>.csv` (per edge) and `headline_<scene>_summary.csv`.

## Files

Runtime: `Confusion/LitColorPredictor.cs`, `LightingChangeDetector.cs`, `LitPredictionValidator.cs`, `HeadlineExperiment.cs`,
`Core/LightingPresetSwitcher.cs`; changed `SemanticTag`, `SemanticGraph`, `SemanticGraphGizmos`, `HudOverlay`, `CvdDebugState`.
Editor: `Scene05NightCity.cs`, `LitPredictionMenu.cs`, `DefaultClassLibrary` (classes 27, 28; `Sky` gains a `Night` state).
Tests: `LitColorPredictorTests`, `LightingChangeDetectorTests`, `NightCityTests`, `LitPredictionValidationTests`; `python/tests/test_phase2a.py`.
