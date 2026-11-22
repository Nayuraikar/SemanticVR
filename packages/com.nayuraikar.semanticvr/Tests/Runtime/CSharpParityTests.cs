using System;
using NUnit.Framework;
using SemanticVR.Confusion;
using Unity.Mathematics;

namespace SemanticVR.Tests
{
    public class CSharpParityTests
    {
        const float TolRgb = 1e-4f, TolLab = 1e-2f, TolDe = 1e-3f;

        static float3 V(float[] a) => new float3(a[0], a[1], a[2]);
        static float MaxAbs(float3 a, float3 b) => math.cmax(math.abs(a - b));

        [Test]
        public void CvdPipeline_MatchesGolden()
        {
            var g = GoldenLoader.Load();
            var rep = new ErrorReport();
            foreach (var c in GoldenLoader.AllCvd(g))
            {
                var type = (CvdType)GoldenLoader.CvdIndex(c.cvd_type);
                float3 rgb = V(c.rgb_lin);
                float3 sim = ColorMath.SimulateCvd(rgb, type, c.severity);
                float3 lab0 = ColorMath.RgbLinToLab(rgb);
                float3 lab1 = ColorMath.RgbLinToLab(sim);
                rep.Add("simulated_rgb", MaxAbs(sim, V(c.simulated_rgb)), TolRgb);
                rep.Add("lab_original", MaxAbs(lab0, V(c.lab_original)), TolLab);
                rep.Add("lab_simulated", MaxAbs(lab1, V(c.lab_simulated)), TolLab);
                rep.Add("de00_orig_vs_sim", Math.Abs(ColorMath.Ciede2000(lab0, lab1) - c.de00_original_vs_simulated), TolDe);
            }
            rep.PrintAndAssert("CSharpParity/cvd");
        }

        [Test]
        public void SharmaPairs_MatchReferenceAndPublished()
        {
            var g = GoldenLoader.Load();
            Assert.AreEqual(34, g.sharma_pairs.Length);
            var rep = new ErrorReport();
            foreach (var p in g.sharma_pairs)
            {
                float de = ColorMath.Ciede2000(V(p.lab1), V(p.lab2));
                rep.Add("sharma_vs_reference", Math.Abs(de - p.de00_reference), TolDe);
                rep.Add("sharma_vs_published(4dp)", Math.Abs(de - p.de00_expected_published), TolDe);
            }
            rep.PrintAndAssert("CSharpParity/sharma");
        }

        [Test]
        public void MachadoMatrices_MatchGolden()
        {
            var g = GoldenLoader.Load();
            var rep = new ErrorReport();
            foreach (var m in g.machado_matrices)
            {
                float3x3 M = ColorMath.MachadoMatrix((CvdType)GoldenLoader.CvdIndex(m.cvd_type), m.severity);
                float err = 0;
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        err = math.max(err, math.abs(M[c][r] - m.matrix_row_major[r * 3 + c])); // M[col][row]
                rep.Add("machado_matrix", err, TolRgb);
            }
            rep.PrintAndAssert("CSharpParity/matrix");
        }
    }
}
