# Phase 0: laptop stereo rig + CVD debug view

Goal: develop on the laptop first. A simulated side-by-side stereo view in the Game view, plus a fullscreen colour-vision-deficiency (CVD) simulation to see what a CVD user sees. Nothing here touches Android/Cardboard; the desktop path stays the reference.

## What is where (all in `packages/com.nayuraikar.semanticvr`)
| Piece | File |
|---|---|
| Stereo rig (2 off-axis eye cameras, mouse-look, WASD/arrows) | `Runtime/Core/StereoRig.cs` |
| Gaze abstraction (head pose now, eye tracker later) | `Runtime/Core/IGazeSource.cs`, `HeadPoseGazeSource.cs` |
| Hotkeys / debug state | `Runtime/Core/DebugViewController.cs`, `Runtime/Confusion/CvdDebugState.cs` |
| HUD (OnGUI) | `Runtime/Core/HudOverlay.cs` |
| Fullscreen CVD pass (URP Render Graph, `RecordRenderGraph` + `AddBlitPass`) | `Runtime/Confusion/CvdSimulationFeature.cs`, `Shaders/SemanticVRCvdSimulation.shader` |
| Colour maths (shared with C# / tests) | `Shaders/SemanticVRColor.hlsl`, `Runtime/Confusion/ColorMath.cs` |
| Scene builder | `Editor/Phase0SceneBuilder.cs` (menu **SemanticVR > Build Phase0 Demo Scene**) |

The pass runs in **linear** space on the camera colour target (sRGB/float RT in a Linear project). In *Original* mode no pass is enqueued (zero cost). Palette entries applied to albedo/emission before shading (project rule) are a later phase; this view is a post-process for debugging only.

## Controls
| Input | Action |
|---|---|
| Click in Game view | capture mouse for look (Esc releases) |
| W / S, Up / Down | forward / back |
| A, Left / Right arrow | strafe left / right (**D is reserved for deutan**, so strafe-right is the Right arrow) |
| Shift | sprint |
| 1 / 2 / 3 | Original / Simulated / Split (left half original, right half simulated; per eye) |
| P / D / T | protan / deutan / tritan |
| [ and ] | severity -0.1 / +0.1 |
| Tab | stereo (side by side) <-> mono (single full-window camera) |
| H | hide/show HUD |

## Stereo geometry
IPD 0.064 m (configurable). Each eye is offset +/- IPD/2 from the Head transform and uses an asymmetric frustum through a virtual screen of vertical FOV 60 deg placed at the convergence distance (2.5 m, zero parallax): `left = (-w - e) * n/d`, `right = (w - e) * n/d`. Viewports are 0..0.5 and 0.5..1.

## Demo scene
Four confusable pairs on pedestals (red/green, orange/olive, blue/purple, pink/grey), checker ground, gradient sky, soft-shadow sun, trilight ambient, trees/rocks/wall as neutral context. Pair colours were chosen with `python/semanticvr_ref` so that **deutan severity 1.0 collapses each pair** (CIEDE2000 between the pair under simulation):

| Pair | Original | Protan | Deutan | Tritan |
|---|---|---|---|---|
| red / green | 52.0 | 21.9 | 6.5 | 35.5 |
| orange / olive | 28.4 | 5.0 | 1.7 | 21.4 |
| blue / purple | 16.6 | 6.8 | 3.7 | 29.9 |
| pink / grey | 19.7 | 6.8 | 3.6 | 20.6 |

## Automated checks
`Tests/Runtime/Phase0DemoTests.cs` (loads the scene, renders SBS frames in every mode into `results/quality/phase0/*.png`, asserts no logged errors, checks mirrored off-axis frusta) and `Phase0InputTests.cs` (simulated keyboard: hotkeys, Tab, WASD/arrows).

Run: Window > General > Test Runner > PlayMode > Run All, or
`Unity -batchmode -projectPath unity/SemanticVR -runTests -testPlatform PlayMode -testResults $PWD/results/quality/parity_playmode.xml` (no `-nographics`).

## Screenshot checklist (Game view, ~16:9 or wider)
Save to `docs/figures/phase0/`.
- [ ] `original_stereo.png`: mode 1, stereo. Two near-identical halves with slight parallax on the rock/pedestals; all four pairs clearly different colours.
- [ ] `deutan_simulated_stereo.png`: mode 2, press D, severity 1.0. Red/green and orange/olive look the same olive-yellow; pink/grey look the same grey; blue/purple nearly identical blue.
- [ ] `deutan_split.png`: Tab to mono, mode 3, D. Left half original, right half simulated, thin white divider; the red sphere is red on the left, olive on the right.
- [ ] (optional) `protan_simulated.png` (P) and `tritan_simulated.png` (T) for comparison.
- [ ] HUD visible and legible in each shot (View / CVD type / Severity / Display / Frame ms and fps).
- [ ] Note the fps shown in the HUD with the Game view at its normal size (target 60+).
