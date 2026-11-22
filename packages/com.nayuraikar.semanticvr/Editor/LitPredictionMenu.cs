using System.Collections.Generic;
using System.IO;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SemanticVR.Editor
{
    /// <summary>
    /// SemanticVR > Validate Lit Prediction: opens each sample scene in turn (Edit Mode), renders per-object views to a small LDR
    /// RenderTexture (no post-processing / tone mapping / CVD pass) and compares them with <see cref="LitColorPredictor"/>.
    /// Writes results/quality/c1/lit_prediction_&lt;scene&gt;.csv (+ _summary) and headline_&lt;scene&gt;.csv (+ _summary).
    /// Headless: Unity -batchmode -projectPath unity/SemanticVR -executeMethod SemanticVR.Editor.LitPredictionMenu.ValidateAll -quit (no -nographics; needs rendering).
    /// </summary>
    public static class LitPredictionMenu
    {
        [MenuItem("SemanticVR/Validate Lit Prediction")]
        public static void ValidateAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var allHeadline = new List<HeadlineResult>();
            foreach (var name in SampleSceneBuilder.SampleNames)
            {
                string path = $"{SceneKit.HostRoot}/{name}/{name}.unity";
                if (!File.Exists(path)) { Debug.LogWarning("[SemanticVR] Missing scene " + path); continue; }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var runs = new List<ValidationResult>(); var heads = new List<HeadlineResult>();
                var sw = Object.FindFirstObjectByType<LightingPresetSwitcher>();
                var gz = Object.FindFirstObjectByType<SemanticGraphGizmos>();
                float maxDist = gz != null ? gz.maxEdgeDistance : 8f;
                int presets = sw != null ? sw.presets.Count : 1;
                for (int i = 0; i < presets; i++)
                {
                    string preset = "";
                    if (sw != null) { sw.Apply(i); preset = sw.Current.name; }
                    var v = LitPredictionValidator.Run(name, preset);
                    runs.Add(v); heads.Add(HeadlineExperiment.Run(v, maxDist));
                    Debug.Log("[SemanticVR] " + LitPredictionValidator.Describe(v));
                    Debug.Log("[SemanticVR] " + HeadlineExperiment.Describe(heads[heads.Count - 1]));
                }
                string f = LitPredictionValidator.WriteScene(name, runs);
                LitPredictionValidator.Write($"headline_{name}.csv", HeadlineExperiment.EdgeCsv(heads));
                LitPredictionValidator.Write($"headline_{name}_summary.csv", HeadlineExperiment.SummaryCsv(heads));
                allHeadline.AddRange(heads);
                Debug.Log("[SemanticVR] Wrote " + f);
            }
            LitPredictionValidator.Write("headline_summary.csv", HeadlineExperiment.SummaryCsv(allHeadline));
        }
    }
}
