using System;
using System.Text;

namespace SemanticVR.Perception
{
    /// <summary>Thrown when the model outputs do not match the golden reference. Masks must not be used.</summary>
    public sealed class MaskLayoutValidationException : Exception
    {
        public MaskLayoutValidationException(string message) : base(message) { }
    }

    /// <summary>Result of a golden validation run.</summary>
    public struct ValidationReport
    {
        public bool Passed;
        public float DetsMaxAbs, LabelsMaxAbs;
        public float MaskMaxAbsConfigured, MaskSignAgreementConfigured;
        public float MaskMaxAbsIdentity, MaskSignAgreementIdentity;
        public MaskLayout ConfiguredLayout;
        public string Message;
    }

    /// <summary>
    /// Compares model outputs for the fixed golden input against ONNX Runtime references and verifies that the
    /// configured <see cref="MaskLayout"/> still holds. Never silently accepts wrong masks.
    /// </summary>
    public static class MaskLayoutValidator
    {
        public const float DetsTolerance = 0.01f;
        public const float LabelsTolerance = 0.05f;
        public const float MaskLogitTolerance = 0.1f;   // observed max diff ~0.005 on CPU and GPUCompute
        public const float MinSignAgreement = 0.999f;

        /// <summary>Validates <paramref name="dets"/>, <paramref name="labels"/> and <paramref name="masks"/> (flat float buffers).</summary>
        public static ValidationReport Validate(GoldenReference g, float[] dets, float[] labels, float[] masks, MaskLayout configured)
        {
            var r = new ValidationReport { ConfiguredLayout = configured };
            r.DetsMaxAbs = MaxAbs(g.Dets, dets);
            r.LabelsMaxAbs = MaxAbs(g.Labels, labels);
            Compare(g, masks, configured, out r.MaskMaxAbsConfigured, out r.MaskSignAgreementConfigured);
            var other = configured == MaskLayout.Identity ? MaskLayout.UnityEinsumTransposed : MaskLayout.Identity;
            Compare(g, masks, MaskLayout.Identity, out r.MaskMaxAbsIdentity, out r.MaskSignAgreementIdentity);

            bool detsOk = r.DetsMaxAbs <= DetsTolerance, labelsOk = r.LabelsMaxAbs <= LabelsTolerance;
            bool maskOk = r.MaskMaxAbsConfigured <= MaskLogitTolerance && r.MaskSignAgreementConfigured >= MinSignAgreement;
            r.Passed = detsOk && labelsOk && maskOk;

            var sb = new StringBuilder();
            sb.Append($"dets maxAbs {r.DetsMaxAbs:E2} (tol {DetsTolerance}), labels maxAbs {r.LabelsMaxAbs:E2} (tol {LabelsTolerance}), ");
            sb.Append($"masks[{configured}] maxAbs {r.MaskMaxAbsConfigured:F3} signAgree {r.MaskSignAgreementConfigured:P3}");
            if (!maskOk)
            {
                Compare(g, masks, other, out float oMax, out float oSign);
                bool otherOk = oMax <= MaskLogitTolerance && oSign >= MinSignAgreement;
                sb.Append(otherOk
                    ? $". LAYOUT CHANGED: the {other} layout matches instead. Update the configured MaskLayout."
                    : ". No known layout matches; the model, importer or package changed in an unexpected way.");
            }
            if (!detsOk || !labelsOk) sb.Append(". Detection/label outputs deviate from ONNX Runtime.");
            r.Message = sb.ToString();
            return r;
        }

        /// <summary>Throws <see cref="MaskLayoutValidationException"/> unless the report passed.</summary>
        public static void ThrowIfFailed(ValidationReport r)
        {
            if (!r.Passed) throw new MaskLayoutValidationException("Golden validation FAILED: " + r.Message);
        }

        static void Compare(GoldenReference g, float[] masks, MaskLayout layout, out float maxAbs, out float signAgree)
        {
            const int H = RfDetrModelSpec.MaskSize, W = RfDetrModelSpec.MaskSize;
            float max = 0; long agree = 0, total = 0;
            for (int i = 0; i < g.TopQueries.Length; i++)
            {
                int q = g.TopQueries[i];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = masks[MaskLayoutUtil.Index(layout, q, y, x)];
                        float r = g.TopMasks[(i * H + y) * W + x];
                        float d = Math.Abs(u - r);
                        if (!(d <= max)) max = d;      // also records NaN
                        if ((u > 0) == (r > 0)) agree++;
                        total++;
                    }
            }
            maxAbs = max; signAgree = total == 0 ? 0 : (float)agree / total;
        }

        static float MaxAbs(float[] a, float[] b)
        {
            if (a.Length != b.Length) return float.PositiveInfinity;
            float m = 0;
            for (int i = 0; i < a.Length; i++) { float d = Math.Abs(a[i] - b[i]); if (!(d <= m)) m = d; }
            return m;
        }
    }
}
