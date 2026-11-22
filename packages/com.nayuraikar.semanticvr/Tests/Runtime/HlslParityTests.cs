using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Tests
{
    public class HlslParityTests
    {
        const float TolRgb = 1e-4f, TolLab = 1e-2f, TolDe = 1e-3f;

        ComputeShader _cs;

        [SetUp]
        public void SetUp()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders not supported on this device/graphics API.");
            _cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(GoldenLoader.Dir + "ComputeParity.compute");
            Assert.IsNotNull(_cs, "ComputeParity.compute not found");
        }

        static ComputeBuffer Buf<T>(T[] data) where T : struct
        {
            var b = new ComputeBuffer(data.Length, System.Runtime.InteropServices.Marshal.SizeOf<T>());
            b.SetData(data);
            return b;
        }
        static ComputeBuffer Out<T>(int n) where T : struct =>
            new ComputeBuffer(n, System.Runtime.InteropServices.Marshal.SizeOf<T>());

        static float MaxAbs3(Vector4 a, float[] b) =>
            Mathf.Max(Mathf.Abs(a.x - b[0]), Mathf.Abs(a.y - b[1]), Mathf.Abs(a.z - b[2]));

        void Dispatch(int kernel, int n) => _cs.Dispatch(kernel, (n + 63) / 64, 1, 1);

        [Test]
        public void CvdPipeline_MatchesGolden()
        {
            var cases = GoldenLoader.AllCvd(GoldenLoader.Load());
            int n = cases.Length;
            var inp = new Vector4[n]; var type = new int[n];
            for (int i = 0; i < n; i++)
            {
                var c = cases[i];
                inp[i] = new Vector4(c.rgb_lin[0], c.rgb_lin[1], c.rgb_lin[2], c.severity);
                type[i] = GoldenLoader.CvdIndex(c.cvd_type);
            }
            ComputeBuffer bIn = Buf(inp), bType = Buf(type);
            ComputeBuffer bSim = Out<Vector4>(n), bL0 = Out<Vector4>(n), bL1 = Out<Vector4>(n), bDe = Out<float>(n);
            try
            {
                int k = _cs.FindKernel("CvdKernel");
                _cs.SetInt("_Count", n);
                _cs.SetBuffer(k, "_CvdIn", bIn); _cs.SetBuffer(k, "_CvdType", bType);
                _cs.SetBuffer(k, "_OutSim", bSim); _cs.SetBuffer(k, "_OutLab0", bL0);
                _cs.SetBuffer(k, "_OutLab1", bL1); _cs.SetBuffer(k, "_OutDe", bDe);
                Dispatch(k, n);
                var sim = new Vector4[n]; var l0 = new Vector4[n]; var l1 = new Vector4[n]; var de = new float[n];
                bSim.GetData(sim); bL0.GetData(l0); bL1.GetData(l1); bDe.GetData(de);

                var rep = new ErrorReport();
                for (int i = 0; i < n; i++)
                {
                    var c = cases[i];
                    rep.Add("simulated_rgb", MaxAbs3(sim[i], c.simulated_rgb), TolRgb);
                    rep.Add("lab_original", MaxAbs3(l0[i], c.lab_original), TolLab);
                    rep.Add("lab_simulated", MaxAbs3(l1[i], c.lab_simulated), TolLab);
                    rep.Add("de00_orig_vs_sim", Math.Abs(de[i] - c.de00_original_vs_simulated), TolDe,
                        $"#{i} {c.name} rgb=({c.rgb_lin[0]:G9},{c.rgb_lin[1]:G9},{c.rgb_lin[2]:G9}) {c.cvd_type} sev={c.severity:G9} gpu={de[i]:G9} ref={c.de00_original_vs_simulated:G9} labSim=({c.lab_simulated[0]:G6},{c.lab_simulated[1]:G6},{c.lab_simulated[2]:G6}) gpuLabSim=({l1[i].x:G6},{l1[i].y:G6},{l1[i].z:G6})");
                }
                rep.PrintAndAssert("HlslParity/cvd (" + SystemInfo.graphicsDeviceType + ")");
            }
            finally { bIn.Release(); bType.Release(); bSim.Release(); bL0.Release(); bL1.Release(); bDe.Release(); }
        }

        [Test]
        public void SharmaPairs_MatchGolden()
        {
            var pairs = GoldenLoader.Load().sharma_pairs;
            int n = pairs.Length;
            var a = new Vector4[n]; var b = new Vector4[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = new Vector4(pairs[i].lab1[0], pairs[i].lab1[1], pairs[i].lab1[2], 0);
                b[i] = new Vector4(pairs[i].lab2[0], pairs[i].lab2[1], pairs[i].lab2[2], 0);
            }
            ComputeBuffer bA = Buf(a), bB = Buf(b), bO = Out<float>(n);
            try
            {
                int k = _cs.FindKernel("DeltaEKernel");
                _cs.SetInt("_Count", n);
                _cs.SetBuffer(k, "_LabA", bA); _cs.SetBuffer(k, "_LabB", bB); _cs.SetBuffer(k, "_OutDeLab", bO);
                Dispatch(k, n);
                var o = new float[n]; bO.GetData(o);
                var rep = new ErrorReport();
                for (int i = 0; i < n; i++)
                {
                    rep.Add("sharma_vs_reference", Math.Abs(o[i] - pairs[i].de00_reference), TolDe, $"pair {i + 1} gpu={o[i]:G9} ref={pairs[i].de00_reference:G9}");
                    rep.Add("sharma_vs_published(4dp)", Math.Abs(o[i] - pairs[i].de00_expected_published), TolDe);
                }
                rep.PrintAndAssert("HlslParity/sharma");
            }
            finally { bA.Release(); bB.Release(); bO.Release(); }
        }

        [Test]
        public void MachadoMatrices_MatchGolden()
        {
            var ms = GoldenLoader.Load().machado_matrices;
            int n = ms.Length;
            var inp = new Vector4[n];
            for (int i = 0; i < n; i++) inp[i] = new Vector4(ms[i].severity, GoldenLoader.CvdIndex(ms[i].cvd_type), 0, 0);
            ComputeBuffer bIn = Buf(inp), bO = Out<Vector4>(n * 3);
            try
            {
                int k = _cs.FindKernel("MatrixKernel");
                _cs.SetInt("_Count", n);
                _cs.SetBuffer(k, "_MatIn", bIn); _cs.SetBuffer(k, "_OutRows", bO);
                Dispatch(k, n);
                var o = new Vector4[n * 3]; bO.GetData(o);
                var rep = new ErrorReport();
                for (int i = 0; i < n; i++)
                    for (int r = 0; r < 3; r++)
                    {
                        var row = o[i * 3 + r];
                        float err = Mathf.Max(Mathf.Abs(row.x - ms[i].matrix_row_major[r * 3]),
                                              Mathf.Abs(row.y - ms[i].matrix_row_major[r * 3 + 1]),
                                              Mathf.Abs(row.z - ms[i].matrix_row_major[r * 3 + 2]));
                        rep.Add("machado_matrix", err, TolRgb);
                    }
                rep.PrintAndAssert("HlslParity/matrix");
            }
            finally { bIn.Release(); bO.Release(); }
        }
    }
}
