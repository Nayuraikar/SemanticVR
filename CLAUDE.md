# SemanticVR: instructions for Claude Code

Unity (2022.3 LTS, URP, Linear) UPM plugin + Python offline reference. Develop on LAPTOP first
(simulated side-by-side stereo in Game view); Android/Cardboard comes later. Never break desktop path.

## Rules
- Plugin code lives ONLY in packages/com.nayuraikar.semanticvr (Runtime / Editor / Shaders / Tests).
- Every HLSL/C# colour function (Machado, XYZ->Lab, CIEDE2000) must have a numeric parity test vs python/semanticvr_ref (colour-science).
- Keep ISolver (exact/learned) and IGazeSource (head pose now, eye tracker later) interfaces.
- Palette indexed by (class, state); applied to albedo/emission BEFORE shading; both eyes read same entry.
- Priority: C1 + C2 + exact solver + one baseline + 3 scenes first. C3 next. C4/OpenACC are stretch.
- Do not make "first to" claims; log prior art in docs/prior_art.
- Do NOT commit or push unless explicitly asked.

## Contributions
C1 lit-colour prediction (SH-L2 probes), C2 material-space stereo-consistent correction,
C3 MCKP allocator + pose prediction, C4 differentiable-observer learned solver, C5 slew limiter + fallback cues.
