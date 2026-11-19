using System.Collections.Generic;
using SemanticVR.Accessibility;
using SemanticVR.Semantics;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>Hooks for environment-dependent colour adaptation. Baseline processing does NOT use them yet; they travel with the transform so a later optimiser can.</summary>
    [System.Serializable]
    public struct EnvironmentColourContext
    {
        public bool isNight;
        [Range(0, 1)] public float nightFactor;
        [Range(0, 1)] public float shadowStrength;
        [Range(0, 1)] public float fogDensity;
        public Color fogColour;
        public static EnvironmentColourContext Daylight => new EnvironmentColourContext { fogColour = new Color(0.7f, 0.75f, 0.8f) };
    }

    /// <summary>Everything a matrix provider may need. Semantic fields are optional and null for the baseline.</summary>
    public struct ColourContext
    {
        public CvdProfile Profile;
        public CorrectionSettings Settings;
        public EnvironmentColourContext Environment;
        /// <summary>Latest semantic objects (for importance-aware providers such as the future PAGP optimiser), may be null.</summary>
        public IReadOnlyList<DetectedObject> Objects;
        /// <summary>Instance-ID map from the perception pipeline (R8, 0 = none), may be null.</summary>
        public Texture InstanceIdMap;
        public SemanticImportanceConfig Importance;
    }

    /// <summary>
    /// Supplies the 3x3 correction matrix. <see cref="BaselineMatrixProvider"/> is the default; the later PAGP
    /// optimiser (native C++) can implement this interface and be assigned to <see cref="ColourProcessor"/> without changes elsewhere.
    /// </summary>
    public interface IColourMatrixProvider
    {
        string Name { get; }
        /// <summary>Computes the correction matrix (linear RGB) and contrast. Returns false if no matrix is available.</summary>
        bool TryGetCorrection(in ColourContext context, out Matrix3x3 correction, out float contrast);
    }

    /// <summary>The documented baseline (<see cref="BaselineCorrection"/>). Ignores semantic and environment data.</summary>
    public sealed class BaselineMatrixProvider : IColourMatrixProvider
    {
        public string Name => "Baseline daltonisation (not PAGP)";

        public bool TryGetCorrection(in ColourContext c, out Matrix3x3 correction, out float contrast)
        {
            var sim = CvdSimulation.GetMatrix(c.Profile.type, c.Profile.EffectiveSeverity01, c.Profile.customMatrix);
            var mode = BaselineCorrection.ModeFor(c.Settings.mode, c.Settings.customContrast);
            var shift = (c.Settings.mode == CorrectionMode.Custom || c.Profile.type == CvdType.Custom) ? c.Settings.customErrorShift : BaselineCorrection.ErrorShiftFor(c.Profile.type, Matrix3x3.Zero);
            correction = BaselineCorrection.Compute(sim, shift, c.Settings.strength * mode.StrengthMultiplier);
            contrast = mode.Contrast;
            return true;
        }
    }
}
