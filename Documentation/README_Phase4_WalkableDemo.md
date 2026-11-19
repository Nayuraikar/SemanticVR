# Phase 4: walkable demo (PerceptionDemo scene)

Open `Assets/Scenes/PerceptionDemo.unity`, press Play. (MainScene and the Phase 1 assets are untouched. The previous
PerceptionDemo is backed up in `tools/backup_pre_phase4/`.) Rebuild any time: menu **SemanticVR > Phase 4 > Build Walkable Demo**.

Controls: WASD move, mouse look, Shift run, **Tab / Esc** settings (frees the cursor; click the world to resume),
**1 / 2 / 3** Original / Simulated / Corrected, **C** compare (three slices of one image), **F1** debug info, **F2** detection overlay.

## Architecture (all new code is additive)
- `Scripts/Player`: `IPlayerInput` (device interface), `DesktopPlayerInput`, `PlayerController` (CharacterController, eye 1.6 m, walk 1.5 / run 3.2 m/s).
  A Cardboard input would implement `IPlayerInput`; nothing in the colour pipeline references input or desktop APIs.
- `Scripts/World`: `TrafficLightController` (NS/EW cycle), `TrafficSimulation` (one Update for all agents: lane car-following with stop lines, pedestrians on a sidewalk/crosswalk graph with walk windows and limb animation).
- `Scripts/Assist`: `SemanticAssistLayer` (priority weights per pixel: live SemanticTag buffer + faded AI instance map; user importance weights are only read), `ColourAssistView` (shows Original / Simulated / Corrected / compare).
- `Scripts/DemoUI`: code-built UGUI (`DemoUiController`, `UiKit`), `DemoDebugOverlay` (hidden), `DemoAutoTest` (inert unless `SVR_DEMO_TEST_OUT` is set).
- Edits to existing files: `ColourProcessor` (per-view flags, `semanticAssist` hook, capture camera), `ColourTransform.shader` (optional per-pixel strength, off by default = identical output), `ManualSemanticBuffer` (texture accessor, GPU-only render), `PerceptionPipeline` (`CaptureViewCamera`).

Correction strength per pixel = bg + (1 - bg) * importance/5, bg = 1 - "Priority focus". Focus 0 = uniform baseline.

## Measured (M1 Mac, Unity 6000.3.28f1, GPUCompute, batch Editor play mode, 60 fps target; not a device/Android result)
See the hand-over report in the chat. Raw outputs: `tools/out/demoA`, `demoB`, `demoC`, `reg_gpu`.
