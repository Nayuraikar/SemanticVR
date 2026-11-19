using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>Colour-difference helpers (sRGB, D65). Uses CIE76 delta-E for the preview read-out only; CIEDE2000 belongs to the later optimiser.</summary>
    public static class ColourMetrics
    {
        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        /// <summary>sRGB colour (0..1) to linear RGB vector.</summary>
        public static Vector3 SrgbToLinear(Color c) => new Vector3(ToLinear(c.r), ToLinear(c.g), ToLinear(c.b));

        /// <summary>Linear RGB (clamped to 0..1) to CIE L*a*b* (D65).</summary>
        public static Vector3 LinearToLab(Vector3 rgb)
        {
            rgb = new Vector3(Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z));
            float X = 0.4124564f * rgb.x + 0.3575761f * rgb.y + 0.1804375f * rgb.z;
            float Y = 0.2126729f * rgb.x + 0.7151522f * rgb.y + 0.0721750f * rgb.z;
            float Z = 0.0193339f * rgb.x + 0.1191920f * rgb.y + 0.9503041f * rgb.z;
            float fx = F(X / 0.95047f), fy = F(Y), fz = F(Z / 1.08883f);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }
        static float F(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

        /// <summary>CIE76 delta-E between two linear RGB colours.</summary>
        public static float DeltaE76(Vector3 linA, Vector3 linB) => Vector3.Distance(LinearToLab(linA), LinearToLab(linB));

        /// <summary>
        /// Distinguishability of a pair for a viewer: delta-E of what they perceive, i.e. Simulation applied to
        /// (optionally corrected) colours. <paramref name="correction"/> may be identity for the uncorrected case.
        /// </summary>
        public static float PerceivedDeltaE(Color a, Color b, Matrix3x3 simulation, Matrix3x3 correction)
        {
            var A = simulation.Apply(correction.Apply(SrgbToLinear(a)));
            var B = simulation.Apply(correction.Apply(SrgbToLinear(b)));
            return DeltaE76(A, B);
        }
    }
}
