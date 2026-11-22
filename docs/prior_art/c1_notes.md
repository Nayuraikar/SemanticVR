# C1 prior-art notes: lighting-aware confusion prediction

No novelty claim is made here. This lists what still has to be **checked** before any positioning statement goes into a paper. None of
the items below has been looked up in this session; they are search leads, not findings.

## To verify

1. **AR/VR recolouring for colour-vision deficiency.** Search for daltonisation / recolouring methods in AR/VR and optical see-through
   displays (head-mounted recolouring, video see-through CVD assistance, Unity/Unreal accessibility plug-ins, game-engine CVD filters).
   Questions: do any operate per object/semantic class rather than per pixel? Do any account for scene lighting, or do they all work on
   the final framebuffer or on authored albedo?
2. **Lighting-aware or illuminant-aware colour correction.** Colour-constancy and illuminant-compensation work for CVD observers; chromatic
   adaptation under changing illuminants; recolouring that uses estimated illumination from the camera feed in AR. Check whether "lit colour
   from the lighting model, no readback" appears under another name (e.g. shading-aware, radiance-aware, intrinsic-image-based recolouring).
3. **Probe-based colour/radiance prediction.** Spherical-harmonic irradiance (Ramamoorthi and Hanrahan; Sloan's precomputed radiance
   transfer; Unity's L2 probes) is standard for shading. Check uses of SH probes to *predict* appearance for perceptual or accessibility
   purposes rather than to shade, and any prior "predict final pixel colour without readback" for UI/contrast/legibility checks (e.g.
   automated contrast testing in 3D/AR, WCAG-style checks for 3D scenes).
4. **CVD simulation models.** Machado et al. 2009 (the model used here), Vienot/Brettel; whether published work simulates CVD on lit
   rather than albedo colours in 3D, and how CIEDE2000 thresholds for "confusable" were chosen in prior work (the 10/15 thresholds here are
   a project default, not taken from a source).
5. **Semantic/object-aware recolouring.** Prior work using object classes or saliency to prioritise which colours to preserve (the priority
   prior and naturalness bound in this project), including game-accessibility literature on state-coded colours (signals, alarms, status lights).
6. **Change detection of lighting.** Prior work on triggering recomputation when scene lighting changes (light-probe invalidation,
   irradiance-volume update triggers). The irradiance fingerprint here is a simple RMS over fixed normals; check whether a standard
   metric exists.

## Known limitations of this implementation (relevant to how any claim is worded)

* Validated only on five procedural scenes with directional light + a flat/trilight ambient; no baked probes, no point/spot lights, no
  textured albedo, no metallic materials, no real-time shadows in the predictor.
* The rendered ground truth comes from Unity URP's own shading, not from a photometric measurement or a display; it validates the
  predictor against the renderer, not against what a viewer perceives.
* The Machado model and the 10/15 CIEDE2000 thresholds are simulation conventions, not a user study.
