using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using Unity.Mathematics;
using UnityEngine;

namespace SemanticVR.Tests
{
    /// <summary>Pair-graph (gizmo) data must equal the ColorMath pipeline, which is itself parity-tested against python/semanticvr_ref.</summary>
    public class SemanticGraphTests
    {
        SemanticFixture fx;
        [SetUp] public void SetUp() => fx = new SemanticFixture();
        [TearDown] public void TearDown() => fx.Cleanup();

        static readonly Vector3 Far = new Vector3(-7000, 0, 7000);

        // Independent re-implementation of the pipeline (no SemanticGraph helpers): sRGB -> linear -> Machado -> clamp -> Lab -> dE00.
        static float3 Lin(Color c)
        {
            float F(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return new float3(F(c.r), F(c.g), F(c.b));
        }
        static float Expected(Color a, Color b, CvdType t, float sev, bool simulate)
        {
            float3 la = Lin(a), lb = Lin(b);
            if (simulate) { la = math.saturate(ColorMath.SimulateCvd(la, t, sev)); lb = math.saturate(ColorMath.SimulateCvd(lb, t, sev)); }
            return ColorMath.Ciede2000(ColorMath.RgbLinToLab(la), ColorMath.RgbLinToLab(lb));
        }

        static PairEdge Find(List<PairEdge> edges, SemanticTag a, SemanticTag b)
        {
            foreach (var e in edges) if ((e.A == a && e.B == b) || (e.A == b && e.B == a)) return e;
            Assert.Fail("edge not found");
            return default;
        }

        [TestCase(CvdType.Deutan, 1.0f)]
        [TestCase(CvdType.Protan, 1.0f)]
        [TestCase(CvdType.Tritan, 0.7f)]
        [TestCase(CvdType.Deutan, 0.4f)]
        public void EdgeDeltaE_MatchesColorMath(CvdType type, float severity)
        {
            var cls = fx.Signal();
            var red = fx.MakeTag(cls, "Red", Far);
            var green = fx.MakeTag(cls, "Green", Far + Vector3.right * 0.5f);
            var amber = fx.MakeTag(cls, "Amber", Far + Vector3.right * 1.0f);
            var edges = new List<PairEdge>();
            SemanticGraph.Build(edges, type, severity, 5f);
            foreach (var (a, b) in new[] { (red, green), (red, amber), (green, amber) })
            {
                var e = Find(edges, a, b);
                Assert.AreEqual(Expected(a.CurrentAlbedo, b.CurrentAlbedo, type, severity, false), e.DeltaEOriginal, 1e-3f);
                Assert.AreEqual(Expected(a.CurrentAlbedo, b.CurrentAlbedo, type, severity, true), e.DeltaESimulated, 1e-3f);
            }
        }

        // Golden values from python/semanticvr_ref (colour-science-validated): ciede2000 on rgb_lin_to_lab, Machado, clipped to [0,1].
        [TestCase("#E5322D", "#3DBB5C", CvdType.Deutan, 1.0f, 73.1886f, 10.4844f)]
        [TestCase("#C8312B", "#4F7F34", CvdType.Deutan, 1.0f, 57.0721f, 5.1016f)]
        [TestCase("#E5322D", "#F2A516", CvdType.Deutan, 1.0f, 36.9972f, 17.0192f)]
        [TestCase("#E0302A", "#46B04A", CvdType.Protan, 1.0f, 70.9256f, 26.6179f)]
        [TestCase("#2FA25A", "#D23B2E", CvdType.Deutan, 0.6f, 66.6616f, 20.3932f)]
        public void DeltaE_MatchesPythonReference(string ha, string hb, CvdType type, float sev, float orig, float sim)
        {
            Color a = SemanticFixture.Hex(ha), b = SemanticFixture.Hex(hb);
            Assert.AreEqual(orig, SemanticGraph.DeltaE(a, b), 0.02f);
            Assert.AreEqual(sim, SemanticGraph.DeltaESimulated(a, b, type, sev), 0.02f);
        }

        [Test]
        public void Build_RespectsDistance_IncludeFlag_AndNullClass()
        {
            var cls = fx.Signal();
            var a = fx.MakeTag(cls, "Red", Far);
            var near = fx.MakeTag(cls, "Green", Far + Vector3.right * 1f);
            var far = fx.MakeTag(cls, "Green", Far + Vector3.right * 40f);
            var hidden = fx.MakeTag(cls, "Green", Far + Vector3.forward * 1f); hidden.includeInGraph = false;
            var noClass = fx.MakeTag(null, "x", Far + Vector3.up * 1f);

            var edges = new List<PairEdge>();
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, 3f);
            Find(edges, a, near);
            Assert.IsFalse(edges.Any(e => e.A == far || e.B == far), "beyond maxDistance");
            Assert.IsFalse(edges.Any(e => e.A == hidden || e.B == hidden), "includeInGraph = false");
            Assert.IsFalse(edges.Any(e => e.A == noClass || e.B == noClass), "no class -> no colour");
            Assert.AreEqual(1f - 0.2f, Find(edges, a, near).Distance, 1e-3f, "distance is the gap between the 0.2 m boxes");
        }

        [Test]
        public void Confusable_RedGreenUnderDeutan_ButNotRedAmber()
        {
            var cls = fx.Signal();
            var red = fx.MakeTag(cls, "Red", Far);
            var green = fx.MakeTag(cls, "Green", Far + Vector3.right * 0.5f);
            var amber = fx.MakeTag(cls, "Amber", Far + Vector3.forward * 0.5f);
            var edges = new List<PairEdge>();
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, 3f);
            Assert.IsTrue(Find(edges, red, green).IsConfusable(15f, 10f), "red/green collapse for deutans (dE~10.5)");
            Assert.IsFalse(Find(edges, red, amber).IsConfusable(15f, 10f), "red/amber stay apart (dE~17)");
            // identical colours are not 'confusable': normal vision cannot tell them apart either
            var red2 = fx.MakeTag(cls, "Red", Far + Vector3.up * 0.5f);
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, 3f);
            Assert.IsFalse(Find(edges, red, red2).IsConfusable(15f, 10f));
        }

        [Test]
        public void GizmoComponent_RebuildsWithSelectedCvdProfile()
        {
            var cls = fx.Signal();
            var red = fx.MakeTag(cls, "Red", Far);
            var green = fx.MakeTag(cls, "Green", Far + Vector3.right * 0.5f);
            var go = new GameObject("gizmos"); fx.Created.Add(go);
            var gz = go.AddComponent<SemanticGraphGizmos>();
            gz.maxEdgeDistance = 3f;
            var prevT = CvdDebugState.Type; var prevS = CvdDebugState.Severity;
            try
            {
                CvdDebugState.Type = CvdType.Deutan; CvdDebugState.Severity = 1f;
                gz.Rebuild();
                float deutan = gz.Edges.First(e => (e.A == red && e.B == green) || (e.A == green && e.B == red)).DeltaESimulated;
                CvdDebugState.Type = CvdType.Tritan; CvdDebugState.Severity = 1f;
                gz.Rebuild();
                float tritan = gz.Edges.First(e => (e.A == red && e.B == green) || (e.A == green && e.B == red)).DeltaESimulated;
                Assert.Less(deutan, 15f); Assert.Greater(tritan, 30f, "tritan sees red/green fine");
                Assert.AreEqual(Expected(red.CurrentAlbedo, green.CurrentAlbedo, CvdType.Tritan, 1f, true), tritan, 1e-3f);
            }
            finally { CvdDebugState.Type = prevT; CvdDebugState.Severity = prevS; }
        }

        // ---- runtime line overlay ----
        SemanticGraphGizmos MakeOverlay(int tagCount, int cap)
        {
            var cls = fx.Signal();
            string[] states = { "Red", "Green", "Amber" };
            for (int i = 0; i < tagCount; i++) fx.MakeTag(cls, states[i % 3], Far + Vector3.right * (0.4f * i));
            var go = new GameObject("overlay"); fx.Created.Add(go);
            var gz = go.AddComponent<SemanticGraphGizmos>();
            gz.maxEdgeDistance = 100f; gz.showAllEdges = true; gz.maxEdges = cap;
            return gz;
        }
        static int Active(SemanticGraphGizmos gz) => gz.Lines.Count(l => l != null && l.enabled);

        [Test]
        public void Overlay_CreatesLineRenderers_AfterGraphOn()
        {
            var gz = MakeOverlay(6, 25);
            gz.showEdges = true; gz.Refresh();
            Assert.Greater(gz.TotalCount, 0);
            Assert.AreEqual(gz.ShownCount, Active(gz));
            Assert.Greater(Active(gz), 0, "edges exist as renderers");
            Assert.IsNotNull(gz.Lines[0].sharedMaterial);
            gz.showEdges = false; gz.Refresh();
            Assert.AreEqual(0, Active(gz), "G off hides every line");
        }

        [Test]
        public void Overlay_RespectsCap_AndKeepsLowestDeltaE()
        {
            var gz = MakeOverlay(9, 5);   // 36 edges in range
            gz.Refresh();
            Assert.AreEqual(gz.Edges.Count, gz.TotalCount, "all edges eligible (showAllEdges)");
            Assert.GreaterOrEqual(gz.TotalCount, 36);
            Assert.AreEqual(5, gz.ShownCount);
            Assert.AreEqual(5, Active(gz));
            var sorted = gz.Edges.Select(e => e.DeltaESimulated).OrderBy(d => d).ToList();
            Assert.LessOrEqual(sorted[4], sorted[5] + 1e-4f);
            gz.AdjustMaxEdges(+5); gz.Refresh();
            Assert.AreEqual(10, Active(gz));
            gz.AdjustMaxEdges(-100); gz.Refresh();
            Assert.AreEqual(1, gz.maxEdges, "cap clamps at 1");
            Assert.AreEqual(1, Active(gz));
        }

        [Test]
        public void Overlay_EdgeCountNeverExceedsN([Values(1, 3, 10, 25)] int cap)
        {
            var gz = MakeOverlay(10, cap);   // 45 edges
            for (int i = 0; i < 3; i++) { gz.Refresh(); Assert.LessOrEqual(gz.ShownCount, cap); Assert.LessOrEqual(Active(gz), cap); }
            Assert.AreEqual(cap, Active(gz), "enough edges exist to fill the cap");
        }

        [Test]
        public void Overlay_FrustumCull_DropsEdgesOutsideCamera()
        {
            var gz = MakeOverlay(6, 25);
            var camGo = new GameObject("cam"); fx.Created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = Far + new Vector3(1f, 0f, -5f);
            gz.cullCameras = new[] { cam };
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            gz.Refresh();
            Assert.Greater(gz.ShownCount, 0, "looking at the tags");
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            gz.Refresh();
            Assert.AreEqual(0, gz.ShownCount, "looking away");
            Assert.AreEqual(0, Active(gz));
        }

        [Test]
        public void Overlay_LabelKey_CyclesOffDeltaENames()
        {
            var gz = MakeOverlay(3, 25);
            Assert.AreEqual(GraphLabelDetail.Off, gz.labelDetail);
            gz.CycleLabels(); Assert.AreEqual(GraphLabelDetail.DeltaE, gz.labelDetail);
            gz.CycleLabels(); Assert.AreEqual(GraphLabelDetail.FullNames, gz.labelDetail);
            gz.CycleLabels(); Assert.AreEqual(GraphLabelDetail.Off, gz.labelDetail);
        }
    }
}
