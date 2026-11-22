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
    /// <summary>
    /// C1 validation: predicted lit colour vs rendered pixels (per scene and lighting preset) and the headline experiment
    /// (albedo-only vs lit-colour prediction vs rendered ground truth, per pair-graph edge). Writes results/quality/c1/*.csv.
    /// </summary>
    public class LitPredictionValidationTests
    {
        public static readonly string[] Scenes = { "01_Street", "02_Signals", "03_Cockpit", "04_Medical", "05_NightCity" };

        static IEnumerator Load(string scene)
        {
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
            if (op == null) Assert.Ignore($"{scene} is not in Build Settings: run SemanticVR > Build Sample Scenes first.");
            while (!op.isDone) yield return null;
            yield return null; yield return null;
        }

        static IEnumerator RunScene(string scene, List<ValidationResult> runs, List<HeadlineResult> heads)
        {
            yield return Load(scene);
            var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
            var gz = Object.FindFirstObjectByType<SemanticGraphGizmos>();
            float maxDist = gz != null ? gz.maxEdgeDistance : 8f;
            int presets = sw != null ? sw.presets.Count : 1;
            var prevMode = CvdDebugState.Mode;
            for (int i = 0; i < presets; i++)
            {
                string preset = "";
                if (sw != null) { sw.Apply(i); preset = sw.Current.name; yield return new WaitForSeconds(0.5f); } // let the ambient probe and the sky reflection settle
                var v = LitPredictionValidator.Run(scene, preset);
                runs.Add(v); heads.Add(HeadlineExperiment.Run(v, maxDist));
                Debug.Log("[C1] " + LitPredictionValidator.Describe(v));
                Debug.Log("[C1] " + HeadlineExperiment.Describe(heads[heads.Count - 1]));
                foreach (var kv in v.PerClass) Debug.Log($"[C1]   {scene}/{preset} class {kv.Key}: n {kv.Value.Count} mean {kv.Value.Mean:0.00} p95 {kv.Value.P95:0.00} max {kv.Value.Max:0.00}");
            }
            Assert.AreEqual(prevMode, CvdDebugState.Mode, "the validator restores the CVD debug mode");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator LitPrediction_WritesCsv_AndCalibratesTheReadback([ValueSource(nameof(Scenes))] string scene)
        {
            var runs = new List<ValidationResult>(); var heads = new List<HeadlineResult>();
            yield return RunScene(scene, runs, heads);
            string path = LitPredictionValidator.WriteScene(scene, runs);
            Assert.IsTrue(System.IO.File.Exists(path), path);
            foreach (var v in runs)
            {
                Assert.Greater(v.Calibration.Count, 0, "calibration patches were rendered");
                Assert.Less(v.Calibration.Max, 1.0f, $"{scene}/{v.Preset}: known unlit colours read back through the render pipeline (dE00, pure readback error)");
                // 04_Medical is an enclosed room: the ceiling puts every object in the sun's shadow, so nothing qualifies as "sampled"
                // (it is reported through the shadowed-object statistics instead). Every other scene must have a real sample.
                if (scene != "04_Medical") { Assert.GreaterOrEqual(v.Overall.Count, 5, $"{scene}/{v.Preset}: enough objects sampled"); Assert.IsFalse(float.IsNaN(v.Overall.Mean)); }
                else Assert.Greater(v.Count(SampleStatus.Shadowed) + v.Overall.Count, 5, "objects were found (and either sampled or shadowed)");
            }
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Headline_WritesCsv_AndLitPredictionIsNotWorseThanAlbedoOnly([ValueSource(nameof(Scenes))] string scene)
        {
            var runs = new List<ValidationResult>(); var heads = new List<HeadlineResult>();
            yield return RunScene(scene, runs, heads);
            LitPredictionValidator.Write($"headline_{scene}.csv", HeadlineExperiment.EdgeCsv(heads));
            LitPredictionValidator.Write($"headline_{scene}_summary.csv", HeadlineExperiment.SummaryCsv(heads));
            int albedoWrong = heads.Sum(h => h.AlbedoWrong), litWrong = heads.Sum(h => h.LitWrong);
            Debug.Log($"[C1] HEADLINE {scene}: albedo-only wrong {albedoWrong}, lit-colour wrong {litWrong} of {heads.Sum(h => h.Evaluated)} edges");
            if (heads.Sum(h => h.Evaluated) == 0) { Debug.Log($"[C1] HEADLINE {scene}: no edge has two unshadowed, visible endpoints (enclosed scene): not evaluable"); yield break; }
            Assert.LessOrEqual(litWrong, albedoWrong, $"{scene}: lit-colour scoring must not disagree with the rendered ground truth more often than albedo-only");
        }
    }
}
