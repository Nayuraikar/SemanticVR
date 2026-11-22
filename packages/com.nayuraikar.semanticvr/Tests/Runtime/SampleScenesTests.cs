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
        public static readonly string[] Scenes = { "01_Street", "02_Signals", "03_Forest" };

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
                Assert.Greater(confusable.Count, 0, "confusable pairs expected");
                Assert.Less(confusable.Min(e => e.DeltaESimulated), 11f, "at least one strongly collapsed pair");
                Assert.IsTrue(confusable.All(e => e.DeltaEOriginal >= gz.minOriginalDeltaE));
            }
            finally { CvdDebugState.Type = prevT; CvdDebugState.Severity = prevS; }
        }

        [UnityTest]
        public IEnumerator FsmScenes_ChangeStatesOverTime([ValueSource(nameof(Scenes))] string scene)
        {
            yield return Load(scene);
            var fsms = Object.FindObjectsByType<SemanticStateMachine>(FindObjectsSortMode.None);
            if (scene == "03_Forest") { Assert.AreEqual(0, fsms.Length); yield break; }
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
