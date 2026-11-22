using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SemanticVR.Tests
{
    /// <summary>Loads each Phase 1 sample scene (built by SemanticVR > Build Sample Scenes) and checks the exit criteria.</summary>
    public class SampleScenesTests
    {
        /// <summary>Class IDs mirrored from the Editor-only DefaultClassLibrary (never renumbered).</summary>
        static class Ids
        {
            public const int Skin = 12, Warning = 13, Caution = 14, Advisory = 15, NormalStatus = 16, DisplayText = 17, RunwayLight = 18,
                             AlarmHigh = 19, AlarmMedium = 20, AlarmLow = 21, VitalTrace = 22, GasCylinder = 23, DrugLabel = 24, StatusLight = 25, Tissue = 26;
        }

        public static readonly string[] Scenes = { "01_Street", "02_Signals", "03_Cockpit", "04_Medical" };

        /// <summary>Minimum number of confusable pair edges (deutan, severity 1, dE00 sim &lt;= 15, original &gt;= 10) each scene must show.
        /// Set well below the measured counts (see docs/design/phase1b.md) so layout tweaks do not make the test flaky.</summary>
        static readonly Dictionary<string, int> MinConfusableEdges = new Dictionary<string, int>
        {
            { "01_Street", 3 }, { "02_Signals", 3 }, { "03_Cockpit", 50 }, { "04_Medical", 12 },
        };

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (op == null) Assert.Ignore($"{scene} is not in Build Settings: run SemanticVR > Build Sample Scenes first.");
            while (!op.isDone) yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scene_HasRig_Tags_Hud_AndGizmos([ValueSource(nameof(Scenes))] string scene)
        {
            yield return Load(scene);
            var rig = Object.FindFirstObjectByType<StereoRig>();
            Assert.IsNotNull(rig, "Phase0 rig");
            Assert.IsNotNull(rig.GetComponent<DebugViewController>());
            Assert.IsNotNull(rig.GetComponent<HudOverlay>());
            var gz = rig.GetComponent<SemanticGraphGizmos>();
            Assert.IsNotNull(gz, "pair-graph gizmos on the rig");
            Assert.IsTrue(rig.GetComponent<HudOverlay>().graph == gz, "HUD is wired to the gizmos");
            Assert.GreaterOrEqual(SemanticRegistry.Tags.Count(t => t.Class != null), 8, "scene is tagged");
            foreach (var t in SemanticRegistry.Tags) Assert.IsNotNull(t.Class, t.name + " has a class");
            Assert.IsTrue(SemanticRegistry.Tags.Any(t => t.Class.memoryColour), "has memory-colour objects (grass/sky/foliage)");
        }

        [UnityTest]
        public IEnumerator Scene_EdgeGraph_ShowsLowDeltaEEdges_UnderDeutan([ValueSource(nameof(Scenes))] string scene)
        {
            yield return Load(scene);
            var gz = Object.FindFirstObjectByType<SemanticGraphGizmos>();
            var prevT = CvdDebugState.Type; var prevS = CvdDebugState.Severity;
            try
            {
                CvdDebugState.Type = CvdType.Deutan; CvdDebugState.Severity = 1f;
                gz.Rebuild();
                var confusable = gz.Edges.Where(e => e.IsConfusable(gz.confusableThreshold, gz.minOriginalDeltaE)).ToList();
                Debug.Log($"[Phase1] {scene}: {SemanticRegistry.Count} tags, {gz.Edges.Count} edges, {confusable.Count} confusable (deutan, dE00<={gz.confusableThreshold})");
                Assert.GreaterOrEqual(confusable.Count, MinConfusableEdges[scene], $"{scene}: at least {MinConfusableEdges[scene]} confusable edges under deutan");
                Assert.Less(confusable.Min(e => e.DeltaESimulated), 11f, "at least one strongly collapsed pair");
                Assert.IsTrue(confusable.All(e => e.DeltaEOriginal >= gz.minOriginalDeltaE));
            }
            finally { CvdDebugState.Type = prevT; CvdDebugState.Severity = prevS; }
        }

        static List<SemanticTag> WithClass(int id) => SemanticRegistry.Tags.Where(t => t.Class != null && t.Class.id == id).ToList();

        [UnityTest]
        public IEnumerator Cockpit_Smoke()
        {
            yield return Load("03_Cockpit");
            foreach (int id in new[] { Ids.Warning, Ids.Caution, Ids.Advisory, Ids.NormalStatus,
                                       Ids.DisplayText, Ids.RunwayLight })
                Assert.Greater(WithClass(id).Count, 0, $"cockpit has class {id}");
            var warn = WithClass(Ids.Warning)[0].Class; var caut = WithClass(Ids.Caution)[0].Class;
            Assert.Greater(warn.priorityPrior, caut.priorityPrior, "Warning outranks Caution");
            Assert.LessOrEqual(warn.naturalnessTau, caut.naturalnessTau, "Warning has the lowest tau");
            Assert.IsTrue(WithClass(Ids.RunwayLight).Any(t => t.StateName == "Red") && WithClass(Ids.RunwayLight).Any(t => t.StateName == "White"), "PAPI shows red and white");
            Assert.GreaterOrEqual(WithClass(Ids.DisplayText).Select(t => t.StateName).Distinct().Count(), 4, "PFD/ND use several colour states");
            Assert.IsTrue(SemanticRegistry.Tags.Any(t => t.name.StartsWith("MasterWarning")), "MASTER WARNING exists");
        }

        [UnityTest]
        public IEnumerator Medical_Smoke()
        {
            yield return Load("04_Medical");
            foreach (int id in new[] { Ids.AlarmHigh, Ids.AlarmMedium, Ids.AlarmLow, Ids.VitalTrace,
                                       Ids.GasCylinder, Ids.DrugLabel, Ids.StatusLight, Ids.Skin, Ids.Tissue })
                Assert.Greater(WithClass(id).Count, 0, $"medical has class {id}");
            var skin = WithClass(Ids.Skin)[0].Class;
            Assert.IsTrue(skin.memoryColour, "skin is a memory colour");
            Assert.LessOrEqual(skin.naturalnessTau, 3f, "skin has a small tau");
            Assert.AreEqual(4, WithClass(Ids.VitalTrace).Select(t => t.StateName).Distinct().Count(), "ecg/spo2/bp/temp traces");
            Assert.GreaterOrEqual(WithClass(Ids.GasCylinder).Count, 6);
            Assert.GreaterOrEqual(WithClass(Ids.DrugLabel).Count, 6);
        }

        [UnityTest]
        public IEnumerator FsmScenes_ChangeStatesOverTime([ValueSource(nameof(Scenes))] string scene)
        {
            yield return Load(scene);
            var fsms = Object.FindObjectsByType<SemanticStateMachine>(FindObjectsSortMode.None);
            Assert.Greater(fsms.Length, 0);
            foreach (var f in fsms)
            {
                f.autoRun = false;
                var seen = new HashSet<string>();
                for (float t = 0; t < f.CycleLength; t += 0.25f) { f.SetTime(t); foreach (var tg in f.targets) seen.Add(tg.tag.StateName); }
                Assert.GreaterOrEqual(seen.Count, 2, f.name + " cycles through several states");
                foreach (var s in f.cycle.Select(x => x.state).Distinct()) Assert.IsTrue(f.targets.All(tg => tg.tag.Class.IndexOfState(s) >= 0), $"{f.name}: {s} exists");
            }
        }
    }
}
