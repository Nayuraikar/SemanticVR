# SemanticVR

**Semantic-priority recolouring for colour-vision-deficient (CVD) users in dynamic VR environments, under frame budgets.**

SemanticVR is a Unity 6 research prototype. It looks at what a first-person camera sees, recognises the objects in view with an AI instance-segmentation model, lets the *user* decide how important each kind of object is, and then recolours the image for a chosen colour-vision deficiency, correcting harder where it matters most (traffic lights, vehicles, pedestrians) and less elsewhere.

> **Honest status (2026-11-19):** first-half prototype, developed and tested on **one machine (Apple M1 MacBook, macOS, Unity Editor)**. Nothing has been run on Windows, Android, Google Cardboard or any XR device. The PAGP optimiser and the CGVR component are **not implemented** (see [section 4](#4-cgvr-and-pagp)). Read [section 12](#12-test-status-pass--fail--not-tested) before relying on any claim.

> **Repository state note (remove once the Phase 2-4 commit is pushed):** when this README was written, the remote `main` held only the Phase 1 scaffold (traffic-intersection scene and generator). The AI perception, colour processing, walkable demo, tests and docs existed in the maintainer's working tree and were **staged but not yet committed**. A clone is complete only if `SemanticVR_Unity/Assets/Scenes/PerceptionDemo.unity` exists after cloning. If it does not, the Phase 2-4 commit has not been pushed yet.

---

## Table of contents

1. [What the project does](#1-what-the-project-does)
2. [Architecture and data flow](#2-architecture-and-data-flow)
3. [Technology stack](#3-technology-stack)
4. [CGVR and PAGP](#4-cgvr-and-pagp)
5. [Prerequisites](#5-prerequisites)
6. [Clone and Unity setup](#6-clone-and-unity-setup)
7. [AI model: download, placement, checksum](#7-ai-model-download-placement-checksum)
8. [Running the demo](#8-running-the-demo)
9. [Repository structure](#9-repository-structure)
10. [Testing](#10-testing)
11. [Python tools](#11-python-tools)
12. [Test status: PASS / FAIL / NOT TESTED](#12-test-status-pass--fail--not-tested)
13. [Known limitations](#13-known-limitations)
14. [Troubleshooting](#14-troubleshooting)
15. [Contributing](#15-contributing)
16. [What to know before modifying each part](#16-what-to-know-before-modifying-each-part)
17. [Model provenance and licences](#17-model-provenance-and-licences)

---

## 1. What the project does

**Problem.** Colour-vision deficiency (protan, deutan, tritan or complete achromatopsia) is commonly cited as affecting roughly 8% of men and 0.5% of women (general background, not a figure measured by this project). In dynamic virtual environments, colour is often the only cue separating safety-critical things (a red from a green traffic light, a vehicle from a road). Uniform "daltonisation" recolours everything equally and cannot spend a limited compute budget where it matters.

**Objective of the research** (title in the project documents): *Semantic-Priority Stereo-Shared Recolouring for Colour-Vision-Deficient Users in Dynamic VR Environments Under Frame Budgets.*

**What is implemented today (the "first half"):**

| Area | Implemented |
|---|---|
| Perception | RF-DETR-Seg Nano (COCO instance segmentation) on Unity Inference Engine. Asynchronous, rate-limited and per-frame-budgeted. Output: boxes, class labels, confidences, instance masks, unique IDs, an instance-ID map. |
| Semantics | COCO class to `SemanticCategory` map (editable). Manual `SemanticTag` fallback rendered into a category buffer. Disagreement between AI and tag is exposed. Conflict policy. |
| Importance | User-set weights 0-5 per category, four profiles (Road Safety, Navigation, General, Custom), editable at run time. **AI never writes a weight.** |
| Colour | CVD simulation (Machado et al. 2009), baseline daltonisation correction, five modes, one shader, Original / Simulated / Corrected views, side-by-side compare, JSON profile save/load, a provider interface for a future optimiser. |
| Demo | A walkable first-person traffic intersection with cycling lights, moving cars, walking pedestrians, a priority-aware assist layer and a settings UI. |
| Tests | 55 pure-logic tests, golden-reference validation of the model at every start, headless play-mode tests, a UI layout checker, independent colour-math verification. |

**Not implemented:** the PAGP optimiser, a CIEDE2000 objective, Shapley-based importance, native (C++/OpenMP/CUDA/Metal) code, stereo-shared rendering, environment adaptation (hooks only), any Android/Cardboard build.

**Key features**
- Live AI recognition on the camera view, with a startup self-check against ONNX Runtime so a broken model or mask layout is detected instead of used.
- User-controlled semantic importance; the pipeline reads it and never overwrites it.
- Per-pixel correction strength: `strength = bg + (1 - bg) * importance / 5`, with `bg = 1 - "Priority focus"`. Focus 0 equals uniform baseline correction.
- Hybrid recognition: AI first, manual `SemanticTag` fallback for classes the pretrained model cannot recognise (road, sidewalk, markings, buildings, vegetation, ground).
- Everything is editable live, with no restart.

---

## 2. Architecture and data flow

```
Game camera ──► CameraRenderTextureSource (hidden mirror camera, mip-mapped RT)   ┐ ICameraFrameSource
Photo       ──► StaticTextureSource                                               ┘
        │
        ▼
ModelInputPreprocessor   gamma-encode -> mip trilinear stretch to 312x312 -> (v-mean)/std -> NCHW tensor (GPU)
        │
        ▼
InferenceScheduler (interval, never overlapping) -> RfDetrInferenceBackend
        steps the model under a per-frame ms budget, async readback, GPUCompute (default) or CPU
        outputs: dets[100,4]  labels[100,91]  masks[100,78,78]
        ▼
DetectionParser (sigmoid, COCO-91 names, threshold, ids; DETR-style, no NMS)
MaskProcessor   (mask-layout correction, box crop, 78x78 logits, instance-ID map R8)
        │
        ▼
SemanticResolver <- CocoToSemanticMap (editable)  <- ManualSemanticBuffer (SemanticTag -> category-ID render)
        │   -> DetectedObject { AI category, manual category, final, source, disagreement, importance }
        ▼
SemanticImportanceConfig (user-only weights; runtime copy)

CvdProfile + CorrectionSettings (ScriptableObjects, user data)
        ▼
ColourMatrixPipeline <- IColourMatrixProvider (BaselineMatrixProvider now; an optimiser later)
        ▼  validated 3x3 matrices (linear RGB)
ColourProcessor (ColourTransform.shader) -> Original / Simulated / Corrected views
        ▲
SemanticAssistLayer: per-pixel priority weights = live SemanticTag buffer + faded AI instance map
ColourAssistView: shows Original / Simulated / Corrected / compare
```

**Rules the code keeps** (do not break them): perception never touches importance or colour; importance has no AI-facing setter; user profiles contain no algorithm; AI detections are `DetectedObject`s and never write a `SemanticTag`.

**AI versus fallback.** `SemanticResolver` combines two independent sources:
- **AI category:** from the model's COCO class through `CocoToSemanticMap`. Defaults: person -> Pedestrian; bicycle, car, motorcycle, bus, truck -> Vehicle; traffic light -> TrafficLight; stop sign -> TrafficSign.
- **Manual category:** from `SemanticTag` components in the scene, rendered into a category buffer.
- `DetectedObject.Source` is `AI`, `ManualTag` or `None`. The conflict policy defaults to `AiAboveConfidence` (threshold 0.6).
- Ground, Road, RoadMarking, Sidewalk, Building and Vegetation have **no COCO class**, so they are only ever known through `SemanticTag` fallback (`CocoToSemanticMap.UnsupportedByPretrainedModel`).

**Default importance weights** (Ground, Road, RoadMarking, Sidewalk, TrafficLight, TrafficSign, Vehicle, Pedestrian, Building, Vegetation):

| Profile | Weights |
|---|---|
| Road Safety (and Custom's starting point) | 1, 3, 4, 3, 5, 5, 4, 5, 1, 1 |
| Navigation (*proposed by the developer, not from studies*) | 1, 4, 5, 4, 4, 5, 3, 3, 3, 1 |
| General (*proposed by the developer, not from studies*) | 2, 3, 3, 3, 4, 4, 3, 4, 2, 2 |

**Colour maths.** All matrices act on linear RGB. Simulation: Machado, Oliveira and Fernandes, IEEE TVCG 15(6), 2009 (severity interpolated linearly from the identity). Correction: `C = I + k * E * (I - S)` after Fidaner, Lin and Ozguven (2005) for protan/deutan. The tritan error-shift matrix is **a developer adaptation and measured to be weak**. Details are in `Documentation/README_Phase2_Phase3.md` section 9.

---

## 3. Technology stack

| Layer | Technology | Version (from the repository) |
|---|---|---|
| Engine | Unity | **6000.3.28f1** (revision 8f082d2fe476), from `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | Universal RP | 17.3.0, Linear colour space |
| AI runtime | Unity Inference Engine (`com.unity.ai.inference`) | **2.6.1** (other versions 2.3.0, 2.4.1, 2.5.0 were tried; see mask-layout note in [section 13](#13-known-limitations)) |
| Input | Input System (`com.unity.inputsystem`) | 1.20.1; *Active Input Handling* = Input System Package (New) only |
| UI | UGUI (`com.unity.ugui`) | 2.0.0, UI built from code (`UiKit`) |
| Model | RF-DETR-Seg Nano, FP32 ONNX, opset 17, 312x312 input | see [section 17](#17-model-provenance-and-licences) |
| Language | C# | Unity 6 compiler, `LangVersion 9.0` in the generated project |
| Shaders | HLSL (3 shaders in `Resources/SemanticVR`) | CategoryFlat, ColourTransform, ModelPreprocess |
| Python tools (optional) | Python, numpy, onnxruntime, Pillow | tested with Python 3.12.6, numpy 2.5.5, onnxruntime 1.32.0, Pillow 13.0.0 (installed in the maintainer's `tools/venv`; there is **no** `requirements.txt`) |
| Other packages present | ai.navigation 2.0.16, test-framework 1.6.0, timeline 1.8.13, visualscripting 1.9.12, multiplayer.center 1.0.1, collab-proxy 2.14.0, ide.rider 3.1.0, ide.visualstudio 2.0.26 | `Packages/manifest.json` |

There are **no assembly definitions** on purpose: Phase 1 scripts live in `Assembly-CSharp` and an asmdef cannot reference it. The project therefore builds as `Assembly-CSharp` (runtime) and `Assembly-CSharp-Editor`.

Asset serialization is **Force Text** (`m_SerializationMode: 2`), so scenes and prefabs diff and merge as text.

---

## 4. CGVR and PAGP

The repository root contains folders named `CGVR/`, `PAGP/`, `Integration/`, `Models/` and `Datasets/`. **All five are empty placeholders** (Git does not track empty directories, so they will not exist after a clone).

**PAGP** is referenced by the code and documents as the *future colour-optimisation component*:
- `ColourContext.cs`: "importance-aware providers such as the future PAGP optimiser".
- `ColourMatrixPipeline.cs`: "the PAGP optimiser can replace [the baseline provider] later".
- `ColourProcessor.cs` and `BaselineCorrection.cs`: "Not the PAGP optimiser".
- `MaskProcessor.cs`: the instance-ID map is built "for later colour processing (PAGP)".
- `README_Phase2_Phase3.md` lists as not implemented: PAGP optimisation, CIEDE2000 objective, Shapley, C++/OpenMP/CUDA/Metal, stereo-shared rendering.

Responsibility, as far as the repository defines it: PAGP would implement `IColourMatrixProvider` and be assigned to `ColourProcessor.Provider`, receiving the detected objects, the instance-ID map, the importance weights and environment hooks through `ColourContext`. Today the provider is `BaselineMatrixProvider` ("Baseline daltonisation (not PAGP)"). The repository does **not** state what the acronym stands for.

**CGVR** appears **nowhere** in any source file or document, only as an empty folder name. This README does not guess its meaning or responsibilities.

> **Needs an owner decision before the next contributor starts:** what CGVR and PAGP stand for, which team member owns each, and whether `Integration/`, `Models/` and `Datasets/` are still planned. Replace this section once decided.

---

## 5. Prerequisites

| Requirement | Detail | Verified |
|---|---|---|
| Git | any recent version. No Git LFS is used (no `.gitattributes`). | n/a |
| Unity Hub + **Unity 6000.3.28f1** | Install exactly this editor version. If it is not in the Hub's list, use Unity's version archive (the Hub also accepts a `unityhub://6000.3.28f1/8f082d2fe476` link; that link format was **not** tried here). | Editor installed and used on macOS |
| Disk | about 120 MB for the model plus about 2.5 GB for Unity's generated `Library/` (measured on the maintainer's machine) | measured |
| Network | first open resolves packages from Unity's registry | needed |
| GPU | the default backend is **GPUCompute** (Metal on the tested Mac). The CPU backend does not work in play mode (see [limitations](#13-known-limitations)). | Apple M1 only |
| Python 3.12 (optional) | only for the tools in [section 11](#11-python-tools) | 3.12.6 on macOS |

**Platforms.**

| Platform | Status |
|---|---|
| macOS, Apple Silicon (M1) | developed and tested here |
| Windows 10/11 | **NOT TESTED.** Unity 6 supports it and nothing in the C# code is macOS-specific, but the GPU backend, shaders and the Editor menu flow have never been run on Windows. Steps below marked **(Windows, not run)** are standard Windows equivalents, not verified here. |
| Linux | not considered |
| Android / Google Cardboard / XR | **NOT TESTED.** `AndroidMinSdkVersion` is 25 in Project Settings and the XR/VR modules are in the manifest, but no Android or XR build has been attempted. Only `SampleScene` is in Build Settings. Do not assume any of it works on a device. |

---

## 6. Clone and Unity setup

1. **Clone**
   ```bash
   git clone https://github.com/Nayuraikar/SemanticVR.git
   cd SemanticVR
   ```
   Check that the demo scene came with the clone:
   ```bash
   ls SemanticVR_Unity/Assets/Scenes/        # expect MainScene, PerceptionDemo, SampleScene
   ```
   If `PerceptionDemo.unity` is missing, see the repository state note at the top.

2. **Install the model first** (it is not in Git): follow [section 7](#7-ai-model-download-placement-checksum). Doing this *before* opening Unity avoids a "missing model" first run.

3. **Open the project.** Unity Hub -> *Add* -> *Add project from disk* -> select the **`SemanticVR_Unity`** folder (not the repository root) -> open with Editor **6000.3.28f1**.

4. **Wait for the first import.** Unity resolves packages, generates `Library/`, compiles scripts and imports the 117 MiB model. This takes several minutes. Importing the model logs about 56 harmless warnings (LayerNormalization `axis` defaulted to -1; Resize `cubic_coeff_a` ignored). The Console should end with no errors.

5. **Open the demo scene:** *Project* window -> `Assets/Scenes/PerceptionDemo.unity` (double-click). The scene already references the model through the tracked `rfdetr_seg_nano.onnx.meta` GUID, so nothing needs to be dragged in as long as the file name is exact.

6. *(Only if the scene is missing or its wiring is lost)* menu **SemanticVR > Setup Perception Demo Scene** recreates the settings assets and the scene (a copy of `MainScene`; `MainScene` itself is never modified). Then **SemanticVR > Phase 4 > Build Walkable Demo (PerceptionDemo scene)** rebuilds the walkable world. Both are re-runnable. **Running either modifies `PerceptionDemo.unity`.** Do not run them casually in a shared scene.

> **If you ever see a scene that lacks the player or UI** (for example `ViewerRig` shows only a Transform), the open Editor is holding an **older in-memory copy** of the scene. Re-open it with *File > Open Scene* and choose **Don't Save** when asked about the stale copy. Do not press save on a stale scene; it overwrites the good file on disk.

---

## 7. AI model: download, placement, checksum

The ONNX file is **not in Git**: `*.onnx` is git-ignored (`.gitignore`) and it is 122,833,543 bytes. You must fetch it yourself. Never commit it.

| Item | Value |
|---|---|
| Model | RF-DETR-Seg Nano, FP32 ONNX (COCO instance segmentation) |
| Source | `https://huggingface.co/imagingforgood/rfdetr-seg-nano-coco-onnx/resolve/main/model.onnx` |
| Size | 122,833,543 bytes |
| SHA-256 | `c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6` |
| Licence | Apache-2.0 (see [section 17](#17-model-provenance-and-licences)) |

The URL was checked on 2026-11-19: it redirects to a CDN and reports `x-linked-size: 122833543`, matching the size above. The copy already on the maintainer's disk matches the SHA-256 above, both in `SemanticVR_Unity/Assets/Models/` and in `tools/model/`. A fresh 117 MiB download was not repeated for this README.

**macOS (tested commands)**
```bash
# from the repository root
curl -L -o SemanticVR_Unity/Assets/Models/rfdetr_seg_nano.onnx \
  "https://huggingface.co/imagingforgood/rfdetr-seg-nano-coco-onnx/resolve/main/model.onnx"
shasum -a 256 SemanticVR_Unity/Assets/Models/rfdetr_seg_nano.onnx
# must print: c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6
```

**Windows PowerShell (standard commands, not run on Windows)**
```powershell
curl.exe -L -o SemanticVR_Unity\Assets\Models\rfdetr_seg_nano.onnx "https://huggingface.co/imagingforgood/rfdetr-seg-nano-coco-onnx/resolve/main/model.onnx"
Get-FileHash -Algorithm SHA256 SemanticVR_Unity\Assets\Models\rfdetr_seg_nano.onnx
```
The hash must equal the value above (PowerShell prints it in upper case; compare case-insensitively).

**Rules**
- The file name must be **exactly** `rfdetr_seg_nano.onnx` and the folder `SemanticVR_Unity/Assets/Models/`. The Git-tracked `rfdetr_seg_nano.onnx.meta` carries the asset GUID the scene refers to; another name creates a different asset and the scene's *Model Asset* field goes empty.
- **If the checksum does not match, delete the file and download again.** Do not use a file that fails the check.
- The Python tools in [section 11](#11-python-tools) read the model from `tools/model/model.onnx` (a second, git-ignored location). Copy the same file there if you use them.
- At every start the pipeline also validates the model against a golden ONNX Runtime reference (`Assets/Resources/Golden`). If that fails the pipeline status is `Failed` with an explicit message and masks are never used.
- The model signature the loader expects: input `input` `[1,3,312,312]`; outputs `dets` `[1,100,4]`, `labels` `[1,100,91]`, `masks` `[1,100,78,78]`. This was re-confirmed on the local file with `tools/scripts/inspect_model.py`.
- Dependency risk: the original AnnotateIt repository was already unavailable at the time of the conversion record, and the current source is a third-party mirror. Consider keeping a team-controlled copy and recording its SHA-256.

---

## 8. Running the demo

1. Open `Assets/Scenes/PerceptionDemo.unity` and press **Play**.
2. Wait for the status pill (top left) to show **AI ready**. Start-up runs the golden validation; the first GPU run measured 3-9 s on the M1. `AI loading...` means wait; `AI unavailable` means check the Console (usually the model is missing or invalid).
3. Click inside the **Game view** so it has focus.

**Controls (keyboard and mouse)**

| Input | Action |
|---|---|
| W A S D (or arrow keys) | move |
| Mouse | look |
| Shift | run |
| **Tab** or **Esc** | free the cursor and open Settings (click the 3D world to resume walking) |
| 1 / 2 / 3 | Original / CVD simulated / Corrected |
| C | compare: Original, Simulated and Corrected side by side |
| F1 | developer debug text (**hidden by default**) |
| F2 | AI detection / mask overlay |

Speeds: walk 1.5 m/s, run 3.2 m/s, eye height 1.6 m.

**Settings panel.** A small **Settings** button sits top right; the panel opens with Tab/Esc or by clicking the button once the cursor is free. It has four sections, scrolls if the window is short, and closes with the **x** or **Done**:
- **Vision** (Colour Vision): Normal / Protan / Deutan / Tritan / Achroma, Partial or Complete form, severity slider (active only for partial types).
- **Correction**: Balanced / Max distinction / Natural / High contrast, strength slider.
- **Priority** (Semantic Priority): prioritise-important-objects switch, priority-focus slider, importance context (Road safety / Navigation / General).
- **Display** (Display and Comparison): Original / Simulated / Corrected, compare switch, Debug info button.
- **Reset to defaults** restores the saved assets.

Edits change **runtime copies only**; the saved assets under `Assets/Settings/SemanticVR` are not modified by play-mode edits.

> `DesktopPlayerInput` currently has a **temporary** `debugLog` option (on by default) that writes one `[DesktopPlayerInput]` status line per second to the Console (menu state, cursor lock, focus, keyboard availability). Untick it on the component, or remove the block, when it is no longer needed.

---

## 9. Repository structure

```
SemanticVR/
├── README.md                     this file
├── .gitignore                    Unity, IDE, Python, model weights (see "Hygiene" below)
├── Documentation/
│   ├── README_Phase2_Phase3.md   AI perception + semantics + CVD maths, methods, measurements
│   ├── README_Phase4_WalkableDemo.md   walkable demo architecture and controls
│   └── test_results/             recorded reports (JSON, annotated PNGs, logic-test log, model provenance + licence)
├── SemanticVR_Unity/             the Unity project (open THIS folder in Unity Hub)
│   ├── Assets/
│   │   ├── Scenes/               MainScene (Phase 1, never modified by tools), PerceptionDemo (the demo), SampleScene
│   │   ├── Scripts/
│   │   │   ├── Environment/      SemanticCategory, SemanticTag
│   │   │   ├── Perception/       pipeline, model loader, preprocessing, scheduler, RF-DETR backend, parser, masks, golden validator
│   │   │   ├── Semantics/        COCO map, resolver, importance config, manual tag buffer, DetectedObject
│   │   │   ├── Accessibility/    CvdType, CvdProfile, CorrectionSettings
│   │   │   ├── ColourProcessing/ simulation, baseline correction, matrix pipeline, processor, metrics, provider interface
│   │   │   ├── Assist/           SemanticAssistLayer, ColourAssistView
│   │   │   ├── Player/           IPlayerInput, DesktopPlayerInput, PlayerController
│   │   │   ├── World/            TrafficLightController, TrafficSimulation
│   │   │   ├── UI/               older IMGUI developer panels (PerceptionDebugUI, ColourPreviewUI, CategoryPalette)
│   │   │   ├── DemoUI/           the settings UI (DemoUiController, UiKit), DemoDebugOverlay, test drivers
│   │   │   └── Editor/           Environment/, Perception/, Demo/ (menu items, setup, batch entries, logic tests)
│   │   ├── Resources/SemanticVR/ 3 shaders;  Resources/Golden/ golden reference (4 .bytes)
│   │   ├── Settings/SemanticVR/  editable assets: CocoToSemanticMap, SemanticImportanceConfig, CvdProfile, CorrectionSettings
│   │   ├── Models/               put rfdetr_seg_nano.onnx here (git-ignored); its .meta is tracked
│   │   ├── Materials/, Prefabs/  environment art (Phase 1 and Phase 4 walkers)
│   │   ├── Textures/Test/        public-domain street photo + attribution
│   │   └── TutorialInfo/, Readme.asset, InputSystem_Actions.inputactions   Unity template leftovers
│   ├── Packages/                 manifest.json, packages-lock.json
│   └── ProjectSettings/
├── tools/                        helper scripts and local test infrastructure (see section 11)
│   ├── scripts/                  Python tools, shell scripts, two C# probe files
│   ├── model/                    model + provenance notes (the .onnx is git-ignored)
│   ├── images/, ref/             test images and ONNX Runtime reference binaries
│   └── (local only, git-ignored: out/, *.log, unity_dev/, backup_pre_phase4/, venv/)
└── CGVR/ PAGP/ Integration/ Models/ Datasets/   empty placeholders, not in Git (section 4)
```

**Generated, never commit:** `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, `*.csproj`, `*.sln`, `*.slnx` (all ignored), the `.onnx` model, `tools/venv/`.

**Ignore rules and what stays out of Git**
- The root `.gitignore` covers Unity-generated folders, IDE files, Python environments, model weights (`*.onnx`, `*.pt`, `*.pth`, `*.safetensors`, `*.engine`), build outputs, `*.log` and the local-only paths `tools/unity_dev/` (a multi-GB scratch Unity project), `tools/unity_infer_test/`, `tools/unity_capture_copy/`, `tools/out/`, `tools/backup_pre_phase4/`, `tools/*.log` and `tools/ref/unity_*`.
- The ignore patterns do **not** match `*.onnx.meta`, so the Unity importer file `rfdetr_seg_nano.onnx.meta` is tracked while the model itself is not.
- `tools/ref/{input,dets,labels,masks}.bin` (ONNX Runtime reference data), `tools/images/`, `tools/model/{LICENSE,PROVENANCE.md,README.md}`, `tools/scripts/`, `Documentation/test_results/` and `Assets/Resources/Golden/` are deliberately tracked.
- Still stage explicit paths rather than `git add .` (see [section 15](#15-contributing)): it is a habit that keeps stray local files out even where a rule is missing.

---

## 10. Testing

All commands below were read from the repository. The status of each is in [section 12](#12-test-status-pass--fail--not-tested).

### 10.1 Logic self-tests (no GPU, no inference)

- **Editor menu:** **SemanticVR > Run Logic Self-Tests**. Read the Console for `[TEST] SUMMARY passed=N failed=M`.
- **Command line** (from the header of `LogicSelfTests.cs`; exits with code 1 on failure; the project must **not** be open in another Unity):
  ```bash
  "/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity" \
    -batchmode -quit -projectPath "$PWD/SemanticVR_Unity" \
    -executeMethod SemanticVR.EditorTools.LogicSelfTests.Run -logFile "$PWD/logic_tests.log"
  ```
  **(Windows, not run)** use `"C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe"` (the Hub's usual install path; confirm yours) with the same arguments.
- Recorded result: 55 passed, 0 failed (`Documentation/test_results/logic_tests_log.txt`).

### 10.2 Headless play-mode tests

These open `PerceptionDemo`, enter play mode, drive it with scripted input, write reports and screenshots, and exit. They are **inert unless their environment variable is set**. Do **not** pass `-quit` (the test ends the run itself). Close any Editor that has the project open first; a batch run cannot share a project with an open Editor.

| What | Entry method | Environment variable | Output |
|---|---|---|---|
| Perception + colour numerics | `SemanticVR.EditorTools.PerceptionBatchTest.Run` | `SVR_AUTOTEST_OUT=<dir>` (optional `SVR_AUTOTEST_RUNS`, `_ASPECT`, `_WIDTH`, `_FPS`, `_DEVICE`, `_BUDGET`, `_MODES`, `_VERBOSE`, `_INITTIMEOUT`, `_ONLY_COLOUR`) | `perception_report*.json`, annotated PNGs, `colour_report.json` |
| Walkable demo end-to-end | `SemanticVR.EditorTools.DemoBatchTest.Run` | `SVR_DEMO_TEST_OUT=<dir>`; optional `SVR_DEMO_PHASE=A`, `B` or `C` | `demo_report*.json`, screenshots |
| UI layout across resolutions | `SemanticVR.EditorTools.DemoBatchTest.Run` | `SVR_UI_SHOTS=<dir>` | `ui_layout_report.txt` and PNGs at 10 resolutions |
| AI pipeline verification (AI-only mode) | `SemanticVR.EditorTools.DemoBatchTest.Run` | `SVR_AI_VERIFY=<dir>` | `ai_verification_report.txt`, frames, JSON |

Example (macOS):
```bash
OUT="$PWD/test_out/demo"; mkdir -p "$OUT"
SVR_DEMO_TEST_OUT="$OUT" "/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -projectPath "$PWD/SemanticVR_Unity" \
  -executeMethod SemanticVR.EditorTools.DemoBatchTest.Run -logFile "$PWD/demo_test.log"
```
Notes:
- `DemoBatchTest.Run` sets `ScriptCompilationDuringPlay` so a recompile is deferred, but **a domain reload during play can still end the test** (see [troubleshooting](#14-troubleshooting)). Running phases A, B and C separately with `SVR_DEMO_PHASE` was the maintainers' workaround.
- Only `DemoBatchTest.Run` and `PerceptionBatchTest.Run` open the scene and enter play mode; `DemoBatchTest.Run` calls `PerceptionBatchTest.Run`.
- These batch commands were **run on a scratch copy of the project** (`tools/unity_dev`, not part of the repository contract) by `tools/scripts/dev_batch.sh`, never from a clean clone of this repository. They have not been run against `SemanticVR_Unity` directly.
- `tools/scripts/dev_batch.sh`, `sync_to_main.sh`, `sync_phase4_to_main.sh` and `try_version.sh` hard-code the maintainer's macOS paths (`~/Projects/SemanticVR`, `/Users/<maintainer>/...`) and expect the scratch projects `tools/unity_dev` and `tools/unity_infer_test`, which are **not in the repository**. They will not work on another machine without editing, and a teammate does not need them.

### 10.3 Manual test of the demo (what a person should check)

After opening `PerceptionDemo` and pressing Play with focus in the Game view:
1. Status pill reaches **AI ready**.
2. W/A/S/D move, the mouse looks, Shift runs (this was **not** verified with physical input by the author; see [section 12](#12-test-status-pass--fail--not-tested)).
3. Press **Tab**: the cursor is freed and the Settings panel opens, fully inside the view. Press Tab/Esc again, or **Done**: walking resumes.
4. In **Vision**, pick Deutan and Partial, drag **Severity**: the image changes live.
5. In **Display**, choose Original / Simulated / Corrected and switch on Compare: three slices appear.
6. Resize the Game view (or change its resolution/aspect): the panel stays fully visible and scrolls when short.
7. F1 toggles debug text; it starts hidden.

---

## 11. Python tools

Optional. They are not needed to run the Unity demo. Create an environment with the three imports the scripts use (versions that were installed when the scripts were last run are in [section 3](#3-technology-stack)):

```bash
# macOS / Linux
python3 -m venv tools/venv
tools/venv/bin/pip install numpy onnxruntime pillow
```
```powershell
# Windows (not run)
py -3.12 -m venv tools\venv
tools\venv\Scripts\pip install numpy onnxruntime pillow
```

**All scripts must be run with `tools/` as the current directory** and expect the model at `tools/model/model.onnx` and test images in `tools/images/` (the scripts open `model/model.onnx` and `images/...` by relative path).

| Script | Command (from `tools/`) | Purpose | Verified for this README |
|---|---|---|---|
| `scripts/inspect_model.py` | `venv/bin/python scripts/inspect_model.py` | prints input/output names and shapes | **run**: prints `input [1,3,312,312]`, `dets [1,100,4]`, `labels [1,100,91]`, `masks [1,100,78,78]` |
| `scripts/verify_colour_math.py` | `venv/bin/python scripts/verify_colour_math.py ../Documentation/test_results/colour_numeric_report.json` | independent numpy re-computation of the colour matrices and shader output | **run**: `cases 9, patches 72: max |matrix diff| 6.20e-08, max |8-bit shader diff| 0.53 levels`, `PASS` |
| `scripts/run_still.py` | `venv/bin/python scripts/run_still.py <image> <threshold> <tag>` | ONNX Runtime CPU detection on one image, writes `out/<tag>.json` and an annotated PNG (needs an `out/` folder) | **run** in a scratch copy on the public-domain photo |
| `scripts/make_golden.py` | `venv/bin/python scripts/make_golden.py <out_dir>` | regenerates the golden reference used by Unity's start-up check | not run (it overwrites reference data; see section 16) |
| `scripts/compare_preprocess.py`, `scripts/cmp_masks.py`, `scripts/ops.py` | see each file's first lines | preprocessing and mask-layout comparisons, operator listing | not run |

---

## 12. Test status: PASS / FAIL / NOT TESTED

Dates are 2026-11-19. Machine: Apple M1 MacBook, macOS, Unity 6000.3.28f1, Unity Editor (batch or interactive), GPUCompute backend. **Nothing here is a statement about Windows, Android or any headset.**

### PASS (evidence exists)

| Item | Evidence |
|---|---|
| 55 pure-logic tests | `Documentation/test_results/logic_tests_log.txt` (recorded, not re-run for this README) |
| Model file integrity | SHA-256 matches; signature confirmed by `inspect_model.py` (run) |
| Model golden validation at start-up (dets, labels, masks vs ONNX Runtime) | passes in every recorded run; dets max abs 1.9e-4, labels 1.6e-3, masks 1e-3, mask sign agreement 100% |
| Colour matrices and shader vs independent numpy | `verify_colour_math.py` PASS (run); `colour_numeric_report.json` |
| Headless walkable-demo test, previous UI, phases A, B, C | A 20/20, B 10/10, C 4/4 checks passed (local `tools/out/demoA`, `demoB`, `demoC`, not in Git; ran **before** the UI redesign) |
| New settings UI layout | `UiLayoutCheck`: **304 checks passed, 0 failed** at 10 resolutions/aspects (1280x720, 1920x1080, 1366x768, 1024x768, 800x600, 640x480, 2560x1080, 900x1200, 1280x400, 480x800): controls on screen, panel inside screen, no horizontal clipping, every control reachable by scrolling, every control the top raycast hit at its centre, single-line text fits, simulated slider press-and-drag moves the value and reaches the processor. Rendered PNGs were inspected for a **subset** of resolutions and sections, not all 50. |
| Debug overlay hidden by default | checked in the same run |

### PARTIAL

| Item | Status |
|---|---|
| Full headless regression against the **new** UI | started; **25 checks passed, 0 failed**, then a domain reload during play ended the test coroutine. Phase C (UI screenshots and baseline frame time) did not run. Log kept locally as `tools/demo_ui_regress.log` (git-ignored, so not in the repository). |
| AI detections in the walkable scene | recorded runs found traffic lights and "bus" detections (the low-poly cars). Those runs had the manual-tag fallback **enabled**, so they do not prove AI recognition independently. |

### NOT TESTED

| Item | Note |
|---|---|
| **Real keyboard and mouse movement** in the Editor after the movement fixes | the earlier "no movement" report was traced to an Editor holding a stale in-memory scene; the cursor-lock change in `DesktopPlayerInput` has **not** been confirmed with physical input |
| **AI-only verification** (`AiPipelineVerification`: manual tags off, scripted walk and turn, recognised classes, fallback needs, importance and colour response) | the script is written and compiles; it has **never been run**. No claim is made about which object classes are recognised in the Phase 4 scene. |
| Physical mouse dragging of sliders | only an event-system simulation passed |
| Windows, Intel Mac, non-Apple GPUs | never run |
| Android, Google Cardboard, XR, any player build | never attempted; Build Settings contain only `SampleScene` |
| A clean **fresh-clone** setup on another machine | not performed; see the review of setup blockers delivered with this README |
| Memory use | not measured (one 7.5 MB managed-heap delta only) |

### FAIL / known defects (documented, not fixed)

| Item | Detail |
|---|---|
| **CPU inference backend in play mode** | did not finish start-up validation in 15 minutes (twice); cause undiagnosed. The pipeline now fails after 120 s with a message recommending GPUCompute. Treat CPU play mode as **not working**. (CPU passes golden validation in edit mode.) |
| Detection of the scene's pedestrians and stop sign | in the earlier Phase 2/3 generated scene, 0 of 5 visible pedestrians and no sign were detected; cars were labelled "bus". Not re-measured in the Phase 4 scene. |
| Tritan correction | measured to make most listed colour pairs worse; developer-adapted matrix |
| Headless regression stability | a script recompile triggered by "Define symbols changed" can reload the domain mid-test |

---

## 13. Known limitations

- **Single-machine validation.** No real-time claim: GPU inference takes about 0.4 s per frame here and runs at 2 Hz or less (`inferenceIntervalSeconds` 0.5). Colour correction itself runs every frame.
- **Model.** 117 MiB FP32, not quantised. Input is stretched to 312x312, masks are 78x78, so small or distant objects are fragile. The model is good on real photographs and unreliable on the primitive-mesh scene; hence the manual-tag fallback. No precision/recall figures exist.
- **Mask layout.** With `com.unity.ai.inference` 2.3.0, 2.4.1, 2.5.0 and 2.6.1 the `masks` output is numerically right but its flat buffer is in `[x][query][y]` order. The code corrects this (`MaskLayout.UnityEinsumTransposed`) and the golden check fails loudly if it ever changes. **Do not change the package version without re-running the golden validation.**
- **AI freshness.** AI instance masks drive the correction only while the pose is close to the captured one (fade over 12 degrees, 2.5 m, 4 s). When the player turns or walks quickly the layer falls back to the live `SemanticTag` buffer.
- **Manual tags.** Need a `MeshRenderer` under a `SemanticTag`; refreshed only at start (`Rebuild()` after spawning objects). Small objects can end up with no label.
- **Colour.** Severity between 0 and 100 is interpolation. The `_GammaProject` shader branch for Gamma-space projects exists but is untested.
- **Importance profiles.** Navigation and General are proposed values, not derived from user studies.
- **UI.** The settings UI is UGUI built from code and is not designed for VR. The older IMGUI panels (`PerceptionDebugUI`, `ColourPreviewUI`) still exist but are disabled in the demo.
- **Unity noise.** The Search indexer logs an `ArgumentOutOfRangeException` at batch start-up (Unity-internal). A Burst native crash happened once on a fresh-`Library` batch import and passed on retry.
- **Repository.** No repository `LICENSE`, no CI, no `CONTRIBUTING.md`, one branch, one commit on the remote.

---

## 14. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Console: `No ModelAsset assigned. Place rfdetr_seg_nano.onnx in Assets/Models` | model missing or misnamed. Follow [section 7](#7-ai-model-download-placement-checksum); the name must be exactly `rfdetr_seg_nano.onnx`. If the file is present, select it in Unity and confirm it imported as a Model Asset; then re-open the scene. |
| Status pill: `AI unavailable (tags only)` | open the Console. Causes: model missing, signature mismatch, golden validation failed, or the backend timed out. The demo still runs on manual tags. |
| `Golden validation FAILED ... LAYOUT CHANGED` | package version changed the mask layout. Restore `com.unity.ai.inference` 2.6.1 in `Packages/manifest.json`. |
| Backend did not finish start-up validation within 120 s | you are on the CPU backend. Set the `PerceptionPipeline` *Device* to `GPUCompute` (the default). First GPU run can take tens of seconds for shader compilation. |
| WASD and mouse do nothing | (1) check the open scene is the **current** `PerceptionDemo` and that `ViewerRig` shows *Desktop Player Input*, *Player Controller* and *Character Controller* (if not, re-open the scene, see the stale-scene note in [section 6](#6-clone-and-unity-setup)); (2) click inside the Game view for focus; (3) press Tab/Esc to leave the menu state; (4) read the `[DesktopPlayerInput]` Console lines (menu state, lock state, focus, keyboard availability); (5) *Edit > Project Settings > Player > Active Input Handling* must be *Input System Package (New)*. |
| Settings panel looks clipped | should be fixed by the responsive layout. Report the Game view size. Run the UI layout test ([section 10.2](#102-headless-play-mode-tests)) to reproduce at that size. |
| Thousands of `NullReferenceException` in `TrafficLightController.PhaseAt` | the domain reloaded during play (a script recompile while playing). Exit play mode and enter it again. In a headless run, kill that Unity process and rerun. |
| Batch run: "another Unity instance is running with this project open" | close the Editor first, or run on a copy of the project. |
| Unity batch run exits immediately without running the test | you passed `-quit` to a play-mode test. Omit it (only the logic tests use `-quit`). |
| Packages fail to resolve on first open | no network/proxy. Open *Window > Package Manager* to see the error; the manifest pins exact versions. |
| About 56 warnings importing the model | expected and harmless (LayerNormalization `axis`, Resize `cubic_coeff_a`). |
| Python: `No such file ... model/model.onnx` | run scripts from `tools/`, and copy the model to `tools/model/model.onnx`. |
| Windows: Unity Hub cannot find the editor version | install `6000.3.28f1` from Unity's archive; the path in the batch commands may differ. (Windows has not been tested.) |

---

## 15. Contributing

**Current reality:** the repository has one branch (`main`) and one remote commit. No CI, no branch protection, no pull-request template exists. The workflow below is a **proposal** for the team to adopt; nothing enforces it yet.

**Branches** (proposed): `main` stays releasable. Work on short-lived branches named
`feature/<topic>`, `fix/<topic>`, `docs/<topic>`, `test/<topic>`, `refactor/<topic>`.

**Day-to-day**
```bash
git checkout main && git pull
git checkout -b feature/<topic>
# ... work in Unity, then review exactly what changed:
git status
git add SemanticVR_Unity/Assets SemanticVR_Unity/Packages SemanticVR_Unity/ProjectSettings Documentation README.md
git status                  # confirm: no model, no Library, no tools/unity_dev, no logs
git commit -m "area: concise summary"
git push -u origin feature/<topic>
```
Open a pull request into `main` on GitHub (`https://github.com/Nayuraikar/SemanticVR`).

**Before you push**
- The project compiles with **0 errors** in the Console, and the Console is clean on entering Play mode.
- *SemanticVR > Run Logic Self-Tests* shows `failed=0` (and add tests for logic you add).
- Every new asset **has its `.meta` file committed**; renamed or moved assets are moved inside Unity, never in the file manager.
- You did not save a scene that Unity loaded stale (see [section 6](#6-clone-and-unity-setup)).
- You did not commit: the `.onnx` model, `Library/`, `Temp/`, `UserSettings/`, `tools/unity_dev/`, `tools/venv/`, `tools/out/`, logs or backups.
- Claims in documentation are matched by evidence: use the PASS / PARTIAL / NOT TESTED vocabulary from [section 12](#12-test-status-pass--fail--not-tested).

**Pull-request description should state:** what changed and why; which scenes/prefabs/settings assets changed (scene merges are painful, so say so); what you tested and on which OS/GPU; what you did **not** test.

**Scene and prefab conflicts.** Assets are Force Text but a scene is still one large file. Agree on who edits `PerceptionDemo.unity` at a time. Prefer prefabs and code over scene edits. `MainScene` and the Phase 1 prefabs/materials are intentionally untouched by all tools; keep it that way unless the owner agrees.

**Suggested repository hygiene tasks** (not yet done, listed so they are not forgotten): add a repository `LICENSE`; add a `requirements.txt` for the Python tools; make the shell scripts in `tools/scripts` path-independent; decide the fate of the empty placeholder folders; add `PerceptionDemo` to Build Settings if a player build is ever attempted; set up CI.

---

## 16. What to know before modifying each part

**Everyone**
- Install exactly Unity 6000.3.28f1. Do not upgrade the editor, URP or `com.unity.ai.inference` casually; the mask layout and golden validation depend on the inference package version.
- Never commit the model. Verify its SHA-256 after every download.
- The Editor can hold a **stale scene** in memory if files were changed outside it. Re-open scenes after pulling.
- The project has no asmdefs. A new script folder is compiled into `Assembly-CSharp`, or `Assembly-CSharp-Editor` if a path segment is named `Editor`.

**Perception (`Scripts/Perception`)**
- Preprocessing, the 312x312 stretch, normalisation and mask layout are validated against `Assets/Resources/Golden`. If you change any of them, the start-up check will (correctly) fail until the golden data is regenerated with `tools/scripts/make_golden.py` from ONNX Runtime and reviewed.
- The backend is stepped under a per-frame millisecond budget (`stepBudgetMs`, default 1). Raising it caused visible hitches in measurements.
- Do not add NMS: the model is DETR-style, with query outputs.
- Perception must not reference colour or importance code.

**Semantics (`Scripts/Semantics`)**
- `SemanticImportanceConfig` weights are user data. No AI-facing setter may exist. Edits at run time go to a runtime copy.
- Adding a COCO class: edit `CocoToSemanticMap` (it validates names against `CocoClasses`). Categories with no COCO class can only come from `SemanticTag`.
- `ManualSemanticBuffer` needs renderers under a `SemanticTag` and is refreshed only at start.

**Colour (`Scripts/ColourProcessing`, `Scripts/Accessibility`)**
- Matrices act on **linear RGB**. Output matrices are validated (finite, |entry| <= 4, |det| >= 0.05 for corrections); invalid output yields identity plus an error.
- New correction algorithms (including any PAGP work) implement `IColourMatrixProvider` and are assigned to `ColourProcessor.Provider`; do not edit the shader or the baseline to hold them.
- `tools/scripts/verify_colour_math.py` recomputes the maths independently; keep it passing.

**Assist and player (`Scripts/Assist`, `Scripts/Player`)**
- Input goes through `IPlayerInput`. Anything that reads the keyboard or mouse directly breaks the device-independence a Cardboard input would need.
- `DesktopPlayerInput` owns cursor locking and the menu state. The `debugLog` block is temporary.

**World and demo UI (`Scripts/World`, `Scripts/DemoUI`)**
- The headless test `DemoAutoTest` finds buttons by their **exact label text** (`Original`, `Simulated`, `Debug info  [F1]`) and takes the **first `Slider` in hierarchy order** as the severity slider. Renaming those or reordering the Vision page breaks the tests.
- The settings UI is created in code at start; there are no UI objects to edit in the scene. Layout comes from the canvas size (reference 1024x576), so change `DemoUiController.ApplyLayout` and the constants at its top, not individual positions.
- `UiLayoutCheck` and `AiPipelineVerification` are inert unless their environment variables are set.

**Editor tooling (`Scripts/Editor`)**
- The Setup and Build Walkable Demo menu items are re-runnable but **rewrite `PerceptionDemo.unity`**. Phase 1's generator is `Tools > SemanticVR > Phase 1 > Generate Traffic Intersection` and its counterpart `Remove Generated Intersection`; run them only on a scene you intend to change.

**Documentation and evidence**
- Do not delete files in `Documentation/test_results/`; they back the numbers in this README.
- Do not write "works" for anything outside [section 12](#12-test-status-pass--fail--not-tested)'s PASS list.

---

## 17. Model provenance and licences

- **Model:** RF-DETR-Seg Nano by Roboflow, code and Seg Nano weights Apache-2.0 per `https://github.com/roboflow/rf-detr`.
- **File used:** FP32 ONNX conversion (opset 17, batch 1, 312x312, about 33.6 M parameters) from `https://huggingface.co/imagingforgood/rfdetr-seg-nano-coco-onnx`, a byte-identical copy of AnnotateIt's conversion made with Roboflow's `rfdetr` exporter (tag 1.9.4), according to the model card. The conversion itself is trusted through the SHA-256 and the golden test only; it has not been independently verified.
- **SHA-256:** `c03620923d319de71fc756c8585e2585a5ecc6e3d4398cf298c8d2991ceb37c6`; **size** 122,833,543 bytes; downloaded 2026-11-19; checksum verified.
- **Licence files:** `Documentation/test_results/model_LICENSE_Apache-2.0.txt`, `Documentation/test_results/model_provenance.md`, `tools/model/PROVENANCE.md`, `tools/model/README.md`, `tools/model/LICENSE`.
- **Not verified:** licences of the DINOv2 backbone and of the pre-training data (COCO annotations CC BY 4.0, images under their Flickr licences). Check before any redistribution.
- **Rejected:** YOLO11n-seg (AGPL-3.0 for code and weights).
- **Test image:** `Assets/Textures/Test/street_photo_oxford.jpg` (Wikimedia Commons, tagged public domain; see `README_street_photo.txt` next to it).
- **Colour-science sources:** Machado, Oliveira, Fernandes (2009) for simulation; Fidaner, Lin, Ozguven (2005) for the baseline error-shift. Full notes: `Documentation/README_Phase2_Phase3.md`.
- **This repository's own licence:** none has been chosen (no `LICENSE` file). Until one is added, do not assume any reuse rights.
