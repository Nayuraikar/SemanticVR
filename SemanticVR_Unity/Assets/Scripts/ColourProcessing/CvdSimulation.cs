using SemanticVR.Accessibility;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>
    /// Colour-vision-deficiency SIMULATION (what a person with CVD perceives). This is not a correction.
    /// <para><b>Method.</b> Machado, Oliveira &amp; Fernandes, "A Physiologically-based Model for Simulation of Color Vision
    /// Deficiency", IEEE TVCG 15(6), 2009. The model is a 3x3 matrix on linear RGB per deficiency and severity.
    /// Severity-1.0 matrices (protanopia, deuteranopia, tritanopia) were checked against the authors' published table
    /// (https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html).</para>
    /// <para><b>Approximation.</b> The authors tabulate matrices at 0.1 severity steps. This implementation only embeds the
    /// verified severity-1.0 matrices and interpolates linearly from the identity (severity 0 is exactly the identity in the
    /// paper). The authors list interpolation between table rows as a fast approximation; interpolating from the identity
    /// is a coarser approximation of the same idea.</para>
    /// <para><b>Achromatopsia</b> uses Rec.709 luminance (Y = 0.2126 R + 0.7152 G + 0.0722 B) on linear RGB; severity blends from
    /// the original colour to that grey. This is a simple monochromacy model, not a physiological one.</para>
    /// </summary>
    public static class CvdSimulation
    {
        /// <summary>Machado 2009, protanopia (severity 1.0).</summary>
        public static readonly Matrix3x3 ProtanopiaFull = new Matrix3x3(0.152286f, 1.052583f, -0.204868f, 0.114503f, 0.786281f, 0.099216f, -0.003882f, -0.048116f, 1.051998f);
        /// <summary>Machado 2009, deuteranopia (severity 1.0).</summary>
        public static readonly Matrix3x3 DeuteranopiaFull = new Matrix3x3(0.367322f, 0.860646f, -0.227968f, 0.280085f, 0.672501f, 0.047413f, -0.011820f, 0.042940f, 0.968881f);
        /// <summary>Machado 2009, tritanopia (severity 1.0).</summary>
        public static readonly Matrix3x3 TritanopiaFull = new Matrix3x3(1.255528f, -0.076749f, -0.178779f, -0.078411f, 0.930809f, 0.147602f, 0.004733f, 0.691367f, 0.303900f);
        /// <summary>Rec.709 luminance on every output channel.</summary>
        public static readonly Matrix3x3 Achromat = new Matrix3x3(0.2126f, 0.7152f, 0.0722f, 0.2126f, 0.7152f, 0.0722f, 0.2126f, 0.7152f, 0.0722f);

        /// <summary>Simulation matrix (linear RGB) for a type and severity (0..1). Custom blends identity toward <paramref name="custom"/>.</summary>
        public static Matrix3x3 GetMatrix(CvdType type, float severity01, Matrix3x3 custom)
        {
            float s = Mathf.Clamp01(severity01);
            switch (type)
            {
                case CvdType.Protanopia: return ProtanopiaFull;
                case CvdType.Deuteranopia: return DeuteranopiaFull;
                case CvdType.Tritanopia: return TritanopiaFull;
                case CvdType.Protanomaly: return Matrix3x3.Lerp(Matrix3x3.Identity, ProtanopiaFull, s);
                case CvdType.Deuteranomaly: return Matrix3x3.Lerp(Matrix3x3.Identity, DeuteranopiaFull, s);
                case CvdType.Tritanomaly: return Matrix3x3.Lerp(Matrix3x3.Identity, TritanopiaFull, s);
                case CvdType.Achromatopsia: return Matrix3x3.Lerp(Matrix3x3.Identity, Achromat, s);
                case CvdType.Custom: return Matrix3x3.Lerp(Matrix3x3.Identity, custom, s);
                default: return Matrix3x3.Identity;
            }
        }
    }
}
