using SemanticVR.Accessibility;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>Parameters of a baseline correction mode (presets of the baseline method, not optimised values).</summary>
    public struct ModeParameters
    {
        /// <summary>Multiplies the user's correction strength.</summary>
        public float StrengthMultiplier;
        /// <summary>Contrast about mid-grey applied after the matrix (1 = none).</summary>
        public float Contrast;
    }

    /// <summary>
    /// BASELINE daltonisation (not the PAGP optimisation). Computes C = I + k * E * (I - S) on linear RGB, where S is the
    /// simulation matrix, (I - S) is the colour information a person with CVD loses, E redistributes that error into the
    /// channels they can still tell apart, and k is the effective strength.
    /// <para>E for protan/deutan is the error-shift matrix of Fidaner, Lin &amp; Ozguven, "Analysis of Color Blindness" (2005):
    /// [0 0 0; 0.7 1 0; 0.7 0 1]. For tritan, E is a developer ADAPTATION of the same idea (lost blue shifted into red and
    /// green) and is NOT from a published paper. Achromatopsia has no recoverable chroma, so E is zero and only contrast is
    /// offered. Mode presets (strength multiplier, contrast) are plain tunable heuristics. None of this is claimed optimal.</para>
    /// </summary>
    public static class BaselineCorrection
    {
        public static readonly Matrix3x3 ErrorShiftProtanDeutan = new Matrix3x3(0, 0, 0, 0.7f, 1, 0, 0.7f, 0, 1);
        public static readonly Matrix3x3 ErrorShiftTritanAdapted = new Matrix3x3(1, 0, 0.7f, 0, 1, 0.7f, 0, 0, 0);
        public const float MaxEffectiveStrength = 2f;

        /// <summary>Error-shift matrix for a deficiency type.</summary>
        public static Matrix3x3 ErrorShiftFor(CvdType type, Matrix3x3 custom)
        {
            switch (type)
            {
                case CvdType.Protanopia: case CvdType.Protanomaly: case CvdType.Deuteranopia: case CvdType.Deuteranomaly: return ErrorShiftProtanDeutan;
                case CvdType.Tritanopia: case CvdType.Tritanomaly: return ErrorShiftTritanAdapted;
                case CvdType.Custom: return custom;
                default: return Matrix3x3.Zero;
            }
        }

        /// <summary>Mode preset values.</summary>
        public static ModeParameters ModeFor(CorrectionMode mode, float customContrast)
        {
            switch (mode)
            {
                case CorrectionMode.MaxDistinguishability: return new ModeParameters { StrengthMultiplier = 1.6f, Contrast = 1f };
                case CorrectionMode.NaturalAppearance: return new ModeParameters { StrengthMultiplier = 0.5f, Contrast = 1f };
                case CorrectionMode.HighContrast: return new ModeParameters { StrengthMultiplier = 1f, Contrast = 1.3f };
                case CorrectionMode.Custom: return new ModeParameters { StrengthMultiplier = 1f, Contrast = Mathf.Clamp(customContrast, 0.5f, 2f) };
                default: return new ModeParameters { StrengthMultiplier = 1f, Contrast = 1f };   // Balanced
            }
        }

        /// <summary>C = I + k * E * (I - S).</summary>
        public static Matrix3x3 Compute(Matrix3x3 simulation, Matrix3x3 errorShift, float effectiveStrength)
        {
            float k = Mathf.Clamp(effectiveStrength, 0f, MaxEffectiveStrength);
            return Matrix3x3.Identity + k * (errorShift * (Matrix3x3.Identity - simulation));
        }
    }
}
