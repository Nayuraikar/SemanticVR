# SemanticVR: Phase 2 (AI perception + semantic importance) and Phase 3 (CVD profiles + baseline colour processing)

Project: *Semantic-Priority Stereo-Shared Recolouring for Colour-Vision-Deficient Users in Dynamic VR Environments Under Frame Budgets.*
Unity 6.3 LTS (6000.3.28f1), URP 17.3.0, macOS Apple Silicon M1. Target (not yet tested): Android + Google Cardboard.

**Status: first-half prototype, validated on one machine (M1 Mac) in the Unity Editor only.**
Nothing here has been run on an Android device or with Cardboard/XR. Nothing here is the PAGP optimiser.

---

## 1. What exists

| Area | Implemented | Not implemented (out of scope, no placeholders claim otherwise) |
|---|---|---|
| Perception | RF-DETR-Seg Nano on Unity Inference Engine; async, rate-limited, budgeted inference; boxes, classes, confidences, instance masks, unique IDs, instance-ID map | Custom dataset / fine-tuning, stereo (both eyes), Android optimisation |
| Semantics | COCO-91 → `SemanticCategory` map; manual `SemanticTag` fallback via a rendered category buffer; AI/manual disagreement exposed; conflict policy | AI-driven weights (by design never) |
| Importance | 0–5 weights, 4 profiles, runtime editing | Learned/adaptive importance |
| Colour | CVD simulation (Machado 2009), baseline daltonisation, 5 modes, shader, 3-view preview, pair read-out, JSON profile save/load, provider interface | PAGP optimisation, CIEDE2000 objective, Shapley, C++/OpenMP/CUDA/Metal, stereo-shared rendering, environment *adaptation* (hooks only) |

## 2. Architecture

```
Game camera ──► CameraRenderTextureSource (hidden mirror camera, mip-mapped RT)   ┐ ICameraFrameSource
Photo       ──► StaticTextureSource                                               ┘ (swap for a stereo source later)
        │
        ▼
ModelInputPreprocessor   gamma-encode → mip trilinear stretch to 312×312 → (v-mean)/std → NCHW tensor (GPU)
        │
        ▼
InferenceScheduler (interval, never overlapping) → IInferenceBackend / RfDetrInferenceBackend
        ScheduleIterable under a per-frame ms budget, async readback, CPU or GPUCompute worker
        │ outputs: dets[100,4] labels[100,91] masks[100,78,78]
        ▼
DetectionParser (sigmoid, COCO-91 names, threshold, ids; no NMS: DETR-style queries)
MaskProcessor   (MaskLayout correction, box crop, 78×78 logits, instance-ID map R8)
        │
        ▼
SemanticResolver ← CocoToSemanticMap (editable)  ← ManualSemanticBuffer (SemanticTag → category-ID render)
        │   → DetectedObject {AI category, manual category, final, source, disagreement, importance}
        ▼
SemanticImportanceConfig (user-only weights; runtime copy)             PerceptionDebugUI (IMGUI)

CvdProfile + CorrectionSettings (ScriptableObjects, user data)
        ▼
ColourMatrixPipeline ← IColourMatrixProvider (BaselineMatrixProvider now; PAGP optimiser later)
        ▼  validated 3×3 matrices (linear RGB)
ColourProcessor (ColourTransform.shader) → Original / Simulated / Corrected views → ColourPreviewUI (IMGUI)
```

Separation rules kept: perception never touches importance or colour; importance has no AI-facing setter; user profiles contain no algorithm; AI detections are `DetectedObject`s and never write `SemanticTag`.

Folders: `Assets/Scripts/{Perception,Semantics,Accessibility,ColourProcessing,UI}`, `Assets/Scripts/Editor/Perception` (setup, tests), `Assets/Resources/SemanticVR` (3 shaders), `Assets/Resources/Golden` (golden reference), `Assets/Settings/SemanticVR` (editable assets), `Assets/Textures/Test`.
No assembly definitions are used on purpose: Phase 1 scripts live in `Assembly-CSharp` and asmdefs cannot reference it.
The class `ModelLoader` requested in the brief is named `RfDetrModelLoader` to avoid clashing with `Unity.InferenceEngine.ModelLoader`.

## 3. Setup

1. **Package:** `com.unity.ai.inference` 2.6.1 is already added to `Packages/manifest.json` (the version Unity's 6.3 docs list).
2. **Model (not in Git, `*.onnx` is ignored):** `Assets/Models/rfdetr_seg_nano.onnx` must exist. If it is missing, download it from the model card below and check the SHA-256 before use:
   `shasum -a 256 model.onnx` must print `c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6`.
3. In Unity: **SemanticVR ▸ Setup Perception Demo Scene** (re-runnable). It creates the assets under `Assets/Settings/SemanticVR` and `Assets/Scenes/PerceptionDemo.unity`, a copy of `MainScene`; `MainScene` is never modified.
4. First import of the 123 MB model takes a while and logs ~56 harmless warnings (LayerNormalization `axis` attribute defaulted to -1; Resize `cubic_coeff_a` ignored).

## 4. Running the demo

Open `Assets/Scenes/PerceptionDemo.unity`, press Play. Wait for status `Ready` (start-up runs the golden validation; first GPU run measured 3–9 s here).

* Left panel: status, backend, **Game camera / Street photo** switch, toggles for **Detections / Masks / Info / Weights**, threshold and interval sliders, timings, per-detection list (class, confidence, box, AI category, manual category, final, weight, `DISAGREE`).
* Right-top: **importance editor**, profile selector (Road Safety / Navigation / General / Custom), sliders 0–5 (steps of 0.5), Reset profile, Copy to Custom.
* Right-bottom: **colour panel**: 9 profile types, severity, correction strength, 5 modes, "perceived" toggle, full-view toggle, Reset/Save/Load (JSON in `persistentDataPath`), ΔE76 read-out for troublesome colour pairs. Three preview thumbnails sit at the bottom centre: Original / CVD simulated / Corrected.
* Edits change runtime copies only; the saved assets are not modified by play-mode edits.

Automated runs (headless): set `SVR_AUTOTEST_OUT=<dir>` and run
`Unity -batchmode -projectPath <proj> -executeMethod SemanticVR.EditorTools.PerceptionBatchTest.Run` (see `tools/scripts` and §7 for the other variables).
Logic tests: `Unity -batchmode -quit -executeMethod SemanticVR.EditorTools.LogicSelfTests.Run` (exit code 1 on failure) or menu **SemanticVR ▸ Run Logic Self-Tests**.

## 5. Model provenance and licence

* Model: RF-DETR-Seg Nano, Roboflow (code and Seg Nano/Small weights Apache-2.0 per the [RF-DETR repo](https://github.com/roboflow/rf-detr)).
* File used: community FP32 ONNX, opset 17, from <https://huggingface.co/imagingforgood/rfdetr-seg-nano-coco-onnx>, itself a copy of AnnotateIt's conversion made with the official `rfdetr` exporter (tag 1.9.4), per the model card.
* SHA-256 `c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6` (**verified**), 122,833,543 bytes. Copies of the licence and provenance are in `Documentation/test_results/`.
* Not verified: the licences of the DINOv2 backbone and of the pre-training data (COCO annotations CC BY 4.0, images under their Flickr licences). Cite appropriately and check before any redistribution. The ONNX conversion by a third party is trusted only through the checksum and the golden test.
* YOLO11n-seg was rejected: AGPL-3.0 for code and weights.

## 6. The mask-layout defect and its handling

With `com.unity.ai.inference` 2.3.0, 2.4.1, 2.5.0 and 2.6.1 (CPU and GPUCompute alike) the `masks` output is numerically correct but its flat buffer is in `[x][query][y]` order, i.e. `index = (x*100 + q)*78 + y` (cause: very likely the decomposed `Einsum` in the segmentation head; not proven).
`dets` and `labels` are unaffected. The fix is `MaskLayout.UnityEinsumTransposed` in `MaskLayoutUtil`.

**Safety net:** at start-up the pipeline runs a fixed golden input (`Assets/Resources/Golden`, generated by `tools/scripts/make_golden.py` from ONNX Runtime) and compares `dets`, `labels` and the masks of the 16 top queries against ONNX Runtime. If anything deviates, or the layout changed, the pipeline goes to `Failed` with an explicit message (it says "LAYOUT CHANGED" if the other layout now matches). Masks are never used unvalidated. The negative control (a wrongly laid-out buffer is rejected) is part of the logic tests.
Measured: dets max abs 1.9e-4, labels 1.6e-3, masks 1e-3, sign agreement 100%.

## 7. Test methodology

| Test | How | Verifies |
|---|---|---|
| Logic self-tests (55) | `LogicSelfTests`, edit mode, no GPU | COCO table, parser vs ONNX Runtime script output on the golden data (19 detections at 0.3, class order, top box), mask code, mapping, importance, policy, scheduler, matrix validation, layout negative control, simulation rules |
| Golden validation | in the pipeline at every start | dets/labels/masks vs ONNX Runtime |
| Perception play-mode test | `PerceptionAutoTest` (headless play mode): Game camera (16:9, 1280 px capture) then street photo | detections, categories, manual agreement, timings, annotated PNGs |
| Preprocessing check | Unity input tensor vs Python preprocessing of the same saved frame (`tools/scripts/compare_preprocess.py`) | orientation, colour space, brightness (found and fixed linear-space averaging: channel means now equal) |
| Colour numerics | `ColourAutoTest` pushes 8 patches × 9 profile/mode cases through the real shader; `tools/scripts/verify_colour_math.py` recomputes everything in numpy from the published matrices | matrices (max diff 6.2e-8), shader output (max 0.53 of an 8-bit level), identity for Normal, grey for Achromatopsia |
| Performance | frame rate paced to 60 FPS (`Application.targetFrameRate`) in headless play mode; warm-up inference excluded; cold start reported separately | see §8 |
| Timeout/failure path | CPU backend with a 20 s init timeout | pipeline reports `Failed` with a clear message instead of hanging |

Env variables for the automated run: `SVR_AUTOTEST_OUT`, `_RUNS`, `_ASPECT`, `_WIDTH`, `_FPS`, `_DEVICE` (CPU), `_BUDGET`, `_MODES` (camera), `_VERBOSE`, `_INITTIMEOUT`, `_ONLY_COLOUR`.

## 8. Actual results (M1 Mac, Unity Editor, headless batch play mode; **not representative of Android**)

**Detections** (threshold 0.3; raw data in `Documentation/test_results/perception_report_gpu.json`; images `annotated_*.png`)

*Generated Unity intersection (16:9 camera):* 7 detections. 4 traffic lights (0.84, 0.79, 0.75, 0.49), all confirmed by the manual tags where measurable (100%); 3 vehicles found at 0.72, 0.43, 0.40, **but labelled "bus"** (they are low-poly cars; the AI and manual *categories* agree: Vehicle).
**Missed:** all pedestrians (0 of 5 visible capsule pedestrians), and no stop/traffic sign was found. In an earlier 4:3 capture no traffic light was found at all (ONNX Runtime on the same frame agrees), and in the earlier 16:9 ONNX Runtime test the foreground pedestrian was misread as "vase" (0.40). Small and distant objects are fragile at 312 px input.

*Real photograph (public domain, Oxford St bus queue):* 17 detections: buses 0.95/0.94/0.78/0.51/0.35/0.33/0.31, cars 0.85/0.40, persons 0.82/0.64/0.47/0.33, traffic lights 0.47/0.36/0.30, and one non-category "handbag" (0.33). Several lower-confidence bus entries are fragments of the same queue. Not annotated by hand, so **no precision/recall is claimed**.

**Domain gap:** the model is good on the photograph and unreliable on the primitive-mesh scene (pedestrians, car class). Hence the hybrid design: manual `SemanticTag` fallback, with disagreement shown.

**Timings, GPUCompute, step budget 1 ms, 60 FPS cadence** (`perception_report_gpu.json`)

| | Game camera | Photo |
|---|---|---|
| time to Ready (incl. golden run) | 3.0 s | 8.6 s |
| cold first inference (wall) | 377 ms | 464 ms |
| steady inference wall | 396 ms | 451 ms |
| main-thread inference cost / worst single step | 23.6 / 1.3 ms | 27.8 / 1.7 ms |
| capture · preprocess · readback | 5.4 · 0.28 · 1.1 ms | 0 · 0.57 · 1.2 ms |
| parse · masks · semantics | 0.15 · 1.2 · 0.11 ms | 0.13 · 2.7 · 0.14 ms |
| frame time median / p95 / max | 16.7 / 18.8 / 44.0 ms | 16.7 / 17.5 / 27.5 ms |

**Step-budget trade-off** (camera mode; raw files `tools/out/budget_*`; one numeric field in them is malformed by a since-fixed formatting bug):

| budget | p95 frame | max frame | inference latency |
|---|---|---|---|
| 4 ms | 80.6 ms | 152.5 ms | 223 ms |
| 1 ms (default) | 19.7 ms | 33.0 ms | 380 ms |
| 0.25 ms | 16.7 ms | 26.5 ms | 1364 ms |

The per-frame CPU cap is always respected, but GPU work is not preemptible; a larger budget caused visible hitches. In batch mode without any display, frame times do not include presentation.

**CPU backend:** passes the golden validation in edit mode (about 510 ms wall, 6 ms main-thread when warm, masks 1e-3, 100% sign agreement). **In batch play mode it did not finish validation within 15 minutes (twice, once with synchronous Burst compilation); cause not diagnosed.** The pipeline now fails after `initTimeoutSeconds` (default 120 s) with a message recommending GPUCompute. Treat CPU in play mode as **not working**.

**Colour** (`colour_numeric_report.json`, `colour_perf.json`): matrices and shader verified (above). Overhead with live camera capture plus 3 blits every frame: 2.5 ms CPU median (mostly the extra camera render), no frame-time change at 60 FPS (p95 16.7 ms); a three-view blit pass alone takes 0.34 ms at 480 px; building the matrices takes 5–14 µs. GPU memory/CPU memory were **not** measured (the only memory figure taken earlier was a 7.5 MB managed-heap delta).

**Baseline effect (perceived ΔE76, uncorrected → corrected, 100% severity, Balanced):**

| pair | deuteranopia | protanopia | tritanopia |
|---|---|---|---|
| Red/Green | 8 → 58 | 35 → 60 | 124 → 100 |
| Green/Brown | 10 → 33 | 24 → 34 | 72 → 55 |
| Orange/Green | 20 → 42 | 5 → 50 | 84 → 85 |
| Blue/Purple | 34 → 25 | 19 → 28 | 42 → 16 |

The baseline helps red/green confusions for protan/deutan, **hurts Blue/Purple for deuteranopia, and the tritan correction made most listed pairs worse** (its error-shift matrix is the developer's adaptation, see §9). The default pairs are red/green-oriented and do not test tritan confusions meaningfully.

## 9. CVD mathematical methods

All matrices act on **linear RGB** (`out = M·rgb`), shaders clamp to [0,1].

* **Simulation:** Machado, Oliveira & Fernandes, *A Physiologically-based Model for Simulation of Color Vision Deficiency*, IEEE TVCG 15(6), 2009. Severity-1.0 matrices for protanopia, deuteranopia and tritanopia were checked against the authors' table (<https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html>). *Approximation:* the authors tabulate 0.1 severity steps; this code interpolates linearly from the identity (the paper's severity 0) to the severity-1.0 matrix. The page did not state linear-vs-gamma RGB; linear RGB follows the paper's model and the usual implementations, not a checked statement. Anomalous types use the severity slider, "-opia" types are fixed at 100%.
* **Achromatopsia:** Rec.709 luminance (0.2126, 0.7152, 0.0722) on linear RGB, blended by severity. A simple monochromacy model, not physiological.
* **Baseline correction (daltonisation):** `C = I + k · E · (I − S)`, `S` = simulation matrix, `E` = error-shift matrix, `k = strength × mode multiplier` (max 2).
  * Protan/deutan `E = [0 0 0; 0.7 1 0; 0.7 0 1]`: Fidaner, Lin & Ozguven, *Analysis of Color Blindness* (2005).
  * Tritan `E = [1 0 0.7; 0 1 0.7; 0 0 0]` is **a developer adaptation, not from a paper, and measured above to be weak**. Achromatopsia has `E = 0` (no recoverable chroma).
  * Modes (heuristic presets, not optimised): Balanced ×1; Max distinguishability ×1.6; Natural ×0.5; High contrast ×1 with contrast 1.3 about linear mid-grey 0.214; Custom: user matrix and contrast.
* **Validation:** finite entries, |entry| ≤ 4, |det| ≥ 0.05 for corrections (simulation may be singular, e.g. achromatopsia). Invalid output yields identity matrices plus an error; it is never applied silently.
* **Metric:** CIE76 ΔE on D65 Lab for the on-screen pair read-out only (CIEDE2000 belongs to the later optimiser).
* **Plug-in point:** implement `IColourMatrixProvider` (the context also carries detected objects, instance-ID map, importance and environment hooks) and assign `ColourProcessor.Provider`.
* **Colour space:** project is Linear. The `_GammaProject` branch for Gamma projects exists but is **untested**.

## 10. Limitations and known issues

* Verified only on one M1 Mac in the Editor (headless). No Android, Cardboard, XR or mobile GPU test. No real-time claim is made: GPU inference takes ~0.4 s per frame here and is run at ≤ 2 Hz.
* Model size 123 MB FP32 (not quantised/optimised); memory use not measured.
* Detection quality on the low-poly scene is poor for pedestrians and car class; no accuracy metrics exist. Output resolution is coarse (312 px input, 78×78 masks).
* CPU backend: fails in batch play mode (undiagnosed). Editor-only first-time Burst compilation crashed once in a fresh-library batch import (Unity/Burst native crash) and passed on retry.
* `masks` layout correction depends on the importer's behaviour; guarded by the golden check, but a future package may change it.
* Manual-tag buffer needs `MeshRenderer`s with a `SemanticTag` ancestor and is refreshed only at start (`Rebuild()` after spawning). Small objects can get no manual label.
* The camera path adds an extra scene render at each inference and (while the colour panel is active) every frame.
* The colour UI is IMGUI (not for VR). Tritan correction is weak; severities between 0 and 100 are an interpolation.
* Unity's Search indexer logs an `ArgumentOutOfRangeException` at batch start-up (Unity-internal, harmless here).
* `Navigation` and `General` importance profiles are proposed values, not derived from studies.

## 11. Files (see also the Git section of the hand-over notes)

Created under `SemanticVR_Unity/Assets`: Scripts (Perception 19, Semantics 5, Accessibility 3, ColourProcessing 8, UI 3, Editor/Perception 4; 42 `.cs` files), 3 shaders, golden data (4 `.bytes`), 4 assets in `Settings/SemanticVR`, `Scenes/PerceptionDemo.unity`, `Textures/Test` (photo + attribution), `Models/rfdetr_seg_nano.onnx` (git-ignored). Modified: `Packages/manifest.json` (one line). Phase 1 files and `MainScene` are untouched.
Tools (outside Unity, not required to run the project): `tools/scripts/*` (golden generator, ONNX Runtime still-image test, comparison and verification scripts, sync script), `tools/model` (model + provenance, git-ignored), `tools/images`, `tools/out`.
