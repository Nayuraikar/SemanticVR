using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SemanticVR.Tests
{
    /// <summary>05_NightCity: lighting presets (N), scoring toggle (K), irradiance change detection, and edges that flip with the lighting.</summary>
    public class NightCityTests : InputTestFixture
    {
        const float SimThr = 15f, MinOrig = 10f;

        IEnumerator Load()
        {
            var op = SceneManager.LoadSceneAsync("05_NightCity", LoadSceneMode.Single);
            if (op == null) Assert.Ignore("05_NightCity is not in Build Settings: run SemanticVR > Build NightCity Scene first.");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        static IEnumerator Settle() { yield return null; yield return null; }

        static SemanticTag Tag(string name)
        {
            var t = SemanticRegistry.Tags.FirstOrDefault(x => x.name == name);
            Assert.IsNotNull(t, "tag " + name);
            return t;
        }

        static PairEdge EdgeBetween(string a, string b, float maxDistance = 6f)
        {
            var edges = new List<PairEdge>();
            SemanticGraph.Build(edges, CvdType.Deutan, 1f, maxDistance);
            Tag(a); Tag(b);
            foreach (var e in edges) if ((e.A.name == a && e.B.name == b) || (e.A.name == b && e.B.name == a)) return e;
            Assert.Fail($"no edge between {a} and {b}");
            return default;
        }

        [UnityTest]
        public IEnumerator Scene_HasSwitcherDetectorAndBothPresets()
        {
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            Assert.IsNotNull(sw); Assert.IsNotNull(Object.FindFirstObjectByType<LightingChangeDetector>());
            CollectionAssert.AreEqual(new[] { "Day", "Night" }, sw.presets.Select(p => p.name));
            Assert.IsNotNull(RenderSettings.sun, "the main light is the scene sun");
            Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Flat, RenderSettings.ambientMode, "flat ambient keeps the Python design maths exact");
            Assert.GreaterOrEqual(SemanticRegistry.Tags.Count(t => t.Class != null), 20);
            Assert.IsTrue(SemanticRegistry.Tags.Any(t => t.Class.displayName == "Neon Sign" && t.CurrentState.emissionIntensity > 0));
        }

        [UnityTest]
        public IEnumerator Hotkeys_N_SwitchesLighting_K_TogglesScoring()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            var prev = CvdDebugState.Scoring;
            try
            {
                Assert.AreEqual("Day", sw.Current.name);
                Press(kb.nKey); yield return null; Release(kb.nKey); yield return null;
                Assert.AreEqual("Night", sw.Current.name);
                Assert.Less(RenderSettings.sun.intensity, 0.5f, "moon is dim");
                Press(kb.nKey); yield return null; Release(kb.nKey); yield return null;
                Assert.AreEqual("Day", sw.Current.name);

                CvdDebugState.Scoring = ConfusionScoring.Lit;
                Press(kb.kKey); yield return null; Release(kb.kKey); yield return null;
                Assert.AreEqual(ConfusionScoring.Albedo, CvdDebugState.Scoring);
                Press(kb.kKey); yield return null; Release(kb.kKey); yield return null;
                Assert.AreEqual(ConfusionScoring.Lit, CvdDebugState.Scoring);
            }
            finally { CvdDebugState.Scoring = prev; }
        }

        [UnityTest]
        public IEnumerator ChangeDetector_FiresOnDayToNightSwitch()
        {
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            var det = Object.FindFirstObjectByType<LightingChangeDetector>();
            det.pollInterval = 0f;
            sw.Apply("Day"); yield return Settle();
            det.Poll(); det.ResetBaseline();
            int fired = 0; float delta = 0f; det.Changed += d => { fired++; delta = d; };
            sw.Apply("Night"); yield return Settle();
            det.Poll(); // the detector's own Update may already have fired during Settle()
            Assert.GreaterOrEqual(fired, 1, "Day->Night must raise the event");
            Assert.AreEqual(1, fired, "exactly once");
            Assert.Greater(delta, det.threshold);
            Debug.Log($"[C1] Day->Night irradiance delta = {delta:0.000} (threshold {det.threshold})");
            Assert.IsFalse(det.Poll(), "no repeat while lighting is stable");
            sw.Apply("Day"); yield return Settle(); // the detector's own Update may already have fired during these frames
            det.Poll();
            Assert.AreEqual(2, fired, "Night->Day fires too (exactly once more)");
        }

        [UnityTest]
        public IEnumerator Edges_FlipBetweenPresets_LitScoreOnly()
        {
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            // (a, b, confusable by day, confusable at night): the design targets from docs/design/phase2a.md
            var cases = new (string a, string b, bool day, bool night)[]
            {
                ("Sign_NeonRed_A", "Sign_ShopGreen_A", true, false),
                ("Sign_ShopYellow_B", "Sign_NeonAmber_B", true, false),
                ("Sign_ShopGreen_C", "Sign_ShopPink_C", false, true),
                ("Sign_ShopViolet_D", "Sign_ShopTeal_D", false, true),
                ("Sign_ShopBlue_D", "Sign_ShopTeal_D", false, true),
                ("Sign_ShopBlue_D", "Sign_ShopViolet_D", true, true),
                ("Sign_ShopRed_E", "Sign_ShopGreen_E", true, true),
            };
            var lit = new Dictionary<string, PairEdge[]>();
            var albedoFlags = new Dictionary<string, bool[]>();
            foreach (var p in new[] { "Day", "Night" })
            {
                sw.Apply(p); yield return Settle();
                foreach (var c in cases)
                {
                    var e = EdgeBetween(c.a, c.b);
                    string key = c.a + "|" + c.b;
                    if (!lit.ContainsKey(key)) { lit[key] = new PairEdge[2]; albedoFlags[key] = new bool[2]; }
                    int i = p == "Day" ? 0 : 1;
                    lit[key][i] = e; albedoFlags[key][i] = e.IsConfusable(ConfusionScoring.Albedo, SimThr, MinOrig);
                    Debug.Log($"[C1] {p}: {c.a} / {c.b}  lit {e.LitDeltaEOriginal:0.0}/{e.LitDeltaESimulated:0.0}  albedo {e.DeltaEOriginal:0.0}/{e.DeltaESimulated:0.0}");
                }
            }
            foreach (var c in cases)
            {
                string key = c.a + "|" + c.b;
                bool litDay = lit[key][0].IsConfusable(ConfusionScoring.Lit, SimThr, MinOrig), litNight = lit[key][1].IsConfusable(ConfusionScoring.Lit, SimThr, MinOrig);
                Assert.AreEqual(c.day, litDay, $"{key}: lit-colour confusable by day");
                Assert.AreEqual(c.night, litNight, $"{key}: lit-colour confusable at night");
                Assert.AreEqual(albedoFlags[key][0], albedoFlags[key][1], $"{key}: the albedo-only baseline cannot see the lighting change");
                Assert.AreEqual(lit[key][0].DeltaESimulated, lit[key][1].DeltaESimulated, 1e-4f, $"{key}: albedo score is independent of lighting");
            }
            Assert.IsTrue(cases.Any(c => c.day && !c.night) && cases.Any(c => !c.day && c.night), "flips in both directions are covered");
        }

        [UnityTest]
        public IEnumerator PairGraph_ChangesWithLighting_OnlyInLitScoring()
        {
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            var gz = Object.FindFirstObjectByType<SemanticGraphGizmos>();
            var prevScoring = CvdDebugState.Scoring; var prevType = CvdDebugState.Type; var prevSev = CvdDebugState.Severity;
            try
            {
                CvdDebugState.Type = CvdType.Deutan; CvdDebugState.Severity = 1f;
                var counts = new Dictionary<(string, ConfusionScoring), int>();
                foreach (var p in new[] { "Day", "Night" })
                {
                    sw.Apply(p); yield return Settle();
                    foreach (var mode in new[] { ConfusionScoring.Albedo, ConfusionScoring.Lit })
                    {
                        CvdDebugState.Scoring = mode;
                        gz.Rebuild();
                        counts[(p, mode)] = gz.ConfusableCount;
                        Debug.Log($"[C1] graph confusable edges {p}/{mode}: {gz.ConfusableCount}");
                    }
                }
                Assert.AreEqual(counts[("Day", ConfusionScoring.Albedo)], counts[("Night", ConfusionScoring.Albedo)], "albedo-only graph ignores lighting");
                Assert.AreNotEqual(counts[("Day", ConfusionScoring.Lit)], counts[("Night", ConfusionScoring.Lit)], "lit-colour graph changes when the lighting changes");
            }
            finally { CvdDebugState.Scoring = prevScoring; CvdDebugState.Type = prevType; CvdDebugState.Severity = prevSev; }
        }

        [UnityTest]
        public IEnumerator Captures_DayAndNight_FromTheRig()
        {
            yield return Load();
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            var rig = Object.FindFirstObjectByType<StereoRig>();
            rig.stereo = false; rig.Apply();
            foreach (var p in new[] { "Day", "Night" })
            {
                sw.Apply(p); yield return new WaitForSeconds(0.5f);
                var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var cam = rig.LeftEye; var prevRect = cam.rect; var prevTarget = cam.targetTexture;
                cam.rect = new Rect(0, 0, 1, 1); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(960, 540, TextureFormat.RGB24, false, false); tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = prevTarget; cam.rect = prevRect;
                string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../results/quality/c1"));
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"nightcity_{p.ToLowerInvariant()}.png"), tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
            }
        }
    }
}
