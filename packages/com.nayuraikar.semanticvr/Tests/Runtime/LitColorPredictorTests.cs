using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Tests
{
    /// <summary>C1 maths: explicit SH basis vs Unity, Lambert term, lit = albedo identity, emission, dominant normal.</summary>
    public class LitColorPredictorTests
    {
        SemanticFixture fx;
        [SetUp] public void SetUp() => fx = new SemanticFixture();
        [TearDown] public void TearDown() => fx.Cleanup();

        static readonly Vector3 Far = new Vector3(6000, 0, 6000);

        static SphericalHarmonicsL2 RandomUnitySh(System.Random rng)
        {
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) sh[c, i] = (float)(rng.NextDouble() * 2 - 1);
            return sh;
        }

        static Vector3 RandomDir(System.Random rng)
        {
            while (true)
            {
                var v = new Vector3((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1));
                if (v.sqrMagnitude > 0.05f && v.sqrMagnitude <= 1f) return v.normalized;
            }
        }

        [Test]
        public void ShBasis_MatchesUnityEvaluate_ForRandomCoefficientsAndDirections()
        {
            var rng = new System.Random(1234);
            for (int k = 0; k < 20; k++)
            {
                var unity = RandomUnitySh(rng);
                var mine = ShL2.FromUnity(unity);
                for (int j = 0; j < 50; j++)
                {
                    var d = RandomDir(rng);
                    var r = new Color[1]; unity.Evaluate(new[] { d }, r);
                    float3 e = ShMath.Evaluate(mine, d);
                    Assert.AreEqual(r[0].r, e.x, 1e-4f, $"R at {d}"); Assert.AreEqual(r[0].g, e.y, 1e-4f, $"G at {d}"); Assert.AreEqual(r[0].b, e.z, 1e-4f, $"B at {d}");
                }
            }
        }

        [Test]
        public void ShBasis_EachCoefficientMatchesUnityAlone([NUnit.Framework.Range(0, 8)] int coefficient)
        {
            var unity = new SphericalHarmonicsL2(); unity[0, coefficient] = 1f;
            var mine = ShL2.FromUnity(unity);
            var rng = new System.Random(7 + coefficient);
            for (int j = 0; j < 30; j++)
            {
                var d = RandomDir(rng);
                var r = new Color[1]; unity.Evaluate(new[] { d }, r);
                Assert.AreEqual(r[0].r, ShMath.Evaluate(mine, d).x, 1e-5f, $"coefficient {coefficient} at {d}");
            }
        }

        [Test]
        public void ShFromUnity_AmbientLight_IsConstantInEveryDirection()
        {
            var unity = new SphericalHarmonicsL2(); unity.AddAmbientLight(new Color(0.5f, 0.25f, 0.1f));
            var mine = ShL2.FromUnity(unity);
            foreach (var d in new[] { Vector3.up, Vector3.down, Vector3.left, new Vector3(1, 1, 1).normalized })
            {
                var e = ShMath.Evaluate(mine, d);
                Assert.AreEqual(0.5f, e.x, 1e-5f); Assert.AreEqual(0.25f, e.y, 1e-5f); Assert.AreEqual(0.1f, e.z, 1e-5f);
            }
        }

        [Test]
        public void Lambert_IsColourTimesMaxZeroNDotL()
        {
            float3 col = new float3(2f, 1f, 0.5f), l = new float3(0, 1, 0);
            Assert.AreEqual(col.x, LitColorPredictor.Lambert(col, l, new float3(0, 1, 0)).x, 1e-6f, "facing the light");
            Assert.AreEqual(0f, LitColorPredictor.Lambert(col, l, new float3(1, 0, 0)).x, 1e-6f, "perpendicular");
            Assert.AreEqual(0f, LitColorPredictor.Lambert(col, l, new float3(0, -1, 0)).y, 1e-6f, "facing away clamps to zero, never negative");
            var n60 = new float3(math.sin(math.radians(60f)), math.cos(math.radians(60f)), 0);
            var e = LitColorPredictor.Lambert(col, l, n60);
            Assert.AreEqual(0.5f * col.x, e.x, 1e-5f); Assert.AreEqual(0.5f * col.y, e.y, 1e-5f); Assert.AreEqual(0.5f * col.z, e.z, 1e-5f);
        }

        [Test]
        public void Irradiance_IsProbePlusLambert()
        {
            var rng = new System.Random(5);
            var sh = ShL2.FromUnity(RandomUnitySh(rng));
            var main = new MainLight { Valid = true, Color = new float3(1.2f, 1f, 0.8f), ToLight = math.normalize(new float3(0.3f, 0.8f, -0.5f)) };
            var n = math.normalize(new float3(0.1f, 0.9f, -0.4f));
            float3 expect = ShMath.Evaluate(sh, n) + main.Color * math.max(0f, math.dot(n, main.ToLight));
            Assert.AreEqual(0f, math.length(expect - LitColorPredictor.Irradiance(sh, main, n)), 1e-6f);
            main.Valid = false;
            Assert.AreEqual(0f, math.length(ShMath.Evaluate(sh, n) - LitColorPredictor.Irradiance(sh, main, n)), 1e-6f, "no main light: probe only");
        }

        [Test]
        public void Lit_EqualsAlbedo_WhenIrradianceIsWhiteAndOne()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Amber", Far);
            var env = new LightingSnapshot { Ambient = ShL2.Constant(new float3(1f)), Main = default };
            float3 albedo = SemanticGraph.SrgbToLinear(tag.CurrentAlbedo);
            foreach (var n in new[] { Vector3.up, Vector3.back, new Vector3(1, 1, -1) })
            {
                float3 lit = LitColorPredictor.Predict(tag, env, tag.Position, n);
                Assert.AreEqual(0f, math.length(lit - albedo), 1e-5f, $"normal {n}");
            }
            Assert.AreEqual(albedo.x, LitColorPredictor.Lit(albedo, new float3(1f), float3.zero).x, 1e-7f);
        }

        [Test]
        public void Lit_ScalesWithIrradiance_AndAddsEmission()
        {
            var cls = ScriptableObject.CreateInstance<SemanticClass>(); fx.Created.Add(cls);
            cls.Configure(90, "Lamp", 0.5f, 5f, false, SemanticClass.State("On", SemanticFixture.Hex("#E5322D"), 2f));
            var tag = fx.MakeTag(cls, "On", Far);
            var env = new LightingSnapshot { Ambient = ShL2.Constant(new float3(0.5f, 0.25f, 0.125f)), Main = default };
            float3 a = SemanticGraph.SrgbToLinear(tag.CurrentAlbedo);
            float3 expect = a * new float3(0.5f, 0.25f, 0.125f) + a * 2f;
            Assert.AreEqual(0f, math.length(expect - LitColorPredictor.Predict(tag, env, tag.Position, Vector3.up)), 1e-5f);
            Assert.AreEqual(0f, math.length(a * 2f - LitColorPredictor.EmissionLinear(tag)), 1e-6f);
            Assert.AreEqual(1f, LitColorPredictor.Displayed(new float3(3f, 1f, 0.5f)).x, "displayed colour is clamped");
        }

        [Test]
        public void UnlitMaterial_ShowsAlbedoRegardlessOfLighting()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Green", Far);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); fx.Created.Add(mat);
            tag.TargetRenderer.sharedMaterial = mat; tag.InvalidateMaterialInfo();
            Assert.IsTrue(tag.IsUnlit);
            var env = new LightingSnapshot { Ambient = ShL2.Constant(new float3(0.2f)), Main = default };
            float3 a = SemanticGraph.SrgbToLinear(tag.CurrentAlbedo);
            Assert.AreEqual(0f, math.length(a - LitColorPredictor.Predict(tag, env, tag.Position, Vector3.up)), 1e-6f);
        }

        [Test]
        public void DominantNormal_ThinPanelFacesViewer_AndOverrideWins()
        {
            var cls = fx.Signal();
            var tag = fx.MakeTag(cls, "Red", Far);
            tag.transform.localScale = new Vector3(1.5f, 0.75f, 0.05f); tag.transform.rotation = Quaternion.identity;
            var viewerFront = Far + Vector3.back * 5f; var viewerBack = Far + Vector3.forward * 5f;
            Assert.AreEqual(0f, Vector3.Distance(Vector3.back, tag.DominantNormal(viewerFront)), 1e-5f, "front face towards a viewer at -Z");
            Assert.AreEqual(0f, Vector3.Distance(Vector3.forward, tag.DominantNormal(viewerBack)), 1e-5f, "back face towards a viewer at +Z");
            Assert.AreEqual(0f, Vector3.Distance(Vector3.forward, tag.DominantNormal(null, Vector3.forward)), 1e-5f, "no viewer: faces the light");
            tag.useNormalOverride = true; tag.normalOverride = Vector3.up;
            Assert.AreEqual(0f, Vector3.Distance(Vector3.up, tag.DominantNormal(viewerFront)), 1e-5f, "override ignores the geometry");
            tag.transform.rotation = Quaternion.Euler(0, 90, 0);
            Assert.AreEqual(0f, Vector3.Distance(Vector3.up, tag.DominantNormal(viewerFront)), 1e-5f, "override is local-space; up stays up under yaw");
        }

        [Test]
        public void Graph_ExposesBothScores_AndTheyDifferWhenLightingIsColoured()
        {
            var cls = fx.Signal();
            var a = fx.MakeTag(cls, "Red", Far + Vector3.right * 0.3f); var b = fx.MakeTag(cls, "Green", Far + Vector3.right * 0.9f);
            // strong amber light: both objects shift towards orange, so the lit difference differs from the albedo difference
            var env = new LightingSnapshot { Ambient = ShL2.Constant(new float3(0.6f, 0.2f, 0.02f)), Main = default };
            var edges = new System.Collections.Generic.List<PairEdge>();
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, 2f, env);
            PairEdge e = default; bool found = false;
            foreach (var x in edges) if ((x.A == a && x.B == b) || (x.A == b && x.B == a)) { e = x; found = true; }
            Assert.IsTrue(found);
            Assert.Greater(Mathf.Abs(e.LitDeltaEOriginal - e.DeltaEOriginal), 1f, "lit and albedo-only scores are distinct");
            Assert.AreEqual(e.DeltaESimulated, e.SimulatedFor(ConfusionScoring.Albedo)); Assert.AreEqual(e.LitDeltaESimulated, e.SimulatedFor(ConfusionScoring.Lit));
            var white = new LightingSnapshot { Ambient = ShL2.Constant(new float3(1f)), Main = default };
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, 2f, white);
            foreach (var x in edges) if ((x.A == a && x.B == b) || (x.A == b && x.B == a)) e = x;
            Assert.AreEqual(e.DeltaEOriginal, e.LitDeltaEOriginal, 1e-3f, "white irradiance 1: lit scoring reduces to the albedo baseline");
            Assert.AreEqual(e.DeltaESimulated, e.LitDeltaESimulated, 1e-3f);
        }
    }
}
