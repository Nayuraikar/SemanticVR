<h1 align="center">SemanticVR</h1>
<p align="center"><b>Confusion-Aware Rendering</b><br>
Illumination-aware, stereo-consistent semantic recolouring for colour-vision-deficient users in dynamic VR under frame budgets.</p>

<p align="center">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-6.3%20LTS-black?logo=unity">
  <img alt="URP" src="https://img.shields.io/badge/pipeline-URP-blue">
  <img alt="Status" src="https://img.shields.io/badge/status-Phase%200-orange">
</p>

> **Hero GIF goes here** (original / simulated / corrected, side by side).

## Idea
Predict from the scene's *lighting* which semantic object pairs a CVD user will confuse, and correct only those, in material space, identically in both eyes, within a frame budget.

## Contributions
| | |
|---|---|
| **C1** | Light-transport-aware confusion prediction (SH-L2 probes → Machado → CIEDE2000) |
| **C2** | Material-space, stereo-consistent palette correction |
| **C3** | Budget-optimal allocation (MCKP) with head-pose prediction |
| **C4** | Amortised learned solver (differentiable observer) |
| **C5** | Demand-driven correction, slew limiting, non-colour fallback cues |

## Repo map
`packages/` plugin · `unity/` host project · `python/` reference + offline tracks · `docs/` design & evaluation · `results/` outputs

## Roadmap
Phase 0 Foundation → 1 Semantics+scenes → 2 Analysis+C1 → 3 Core correction → 4 Stability+budget → 5 PAGP track → 6 Packaging → 7 Evaluation

## Limitations
Simulated observers only; 3DoF viewer target; probe prediction misses real-time shadows/specular.
