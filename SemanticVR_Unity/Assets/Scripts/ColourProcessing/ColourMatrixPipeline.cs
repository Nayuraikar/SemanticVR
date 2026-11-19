using SemanticVR.Accessibility;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>The validated matrices and parameters handed to the shader. All matrices act on LINEAR RGB.</summary>
    public struct ColourTransform
    {
        public Matrix3x3 Simulation;       // what a person with this CVD perceives
        public Matrix3x3 Correction;       // what we display to compensate (baseline or provider-supplied)
        public float Contrast;
        public EnvironmentColourContext Environment;
        public bool Valid;
        public string Error;
        public string ProviderName;
    }

    /// <summary>
    /// Composes simulation and correction matrices from the user's profile and settings through an
    /// <see cref="IColourMatrixProvider"/>, and validates them. On failure it returns identity matrices and an error,
    /// never silently-wrong values.
    /// </summary>
    public sealed class ColourMatrixPipeline
    {
        /// <summary>The correction source. Defaults to <see cref="BaselineMatrixProvider"/>; the PAGP optimiser can replace it later.</summary>
        public IColourMatrixProvider Provider = new BaselineMatrixProvider();

        public ColourTransform Build(in ColourContext ctx)
        {
            var t = new ColourTransform { Simulation = Matrix3x3.Identity, Correction = Matrix3x3.Identity, Contrast = 1f, Environment = ctx.Environment, ProviderName = Provider.Name };
            if (ctx.Profile == null || ctx.Settings == null) { t.Error = "missing profile or settings"; return t; }
            var sim = CvdSimulation.GetMatrix(ctx.Profile.type, ctx.Profile.EffectiveSeverity01, ctx.Profile.customMatrix);
            if (!MatrixValidator.Validate(sim, false, out var err)) { t.Error = "simulation: " + err; return t; }
            if (!Provider.TryGetCorrection(ctx, out var corr, out float contrast)) { t.Error = "provider returned no matrix"; return t; }
            if (!MatrixValidator.Validate(corr, true, out err)) { t.Error = "correction: " + err; return t; }
            if (float.IsNaN(contrast) || contrast < 0.25f || contrast > 3f) { t.Error = "contrast out of range"; return t; }
            t.Simulation = sim; t.Correction = corr; t.Contrast = contrast; t.Valid = true;
            return t;
        }
    }
}
