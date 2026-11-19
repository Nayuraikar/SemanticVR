using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SemanticVR.Semantics;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Headless test driver. Active only when the environment variable SVR_AUTOTEST_OUT names an output directory.
    /// For each frame source it waits for N completed inferences, then writes a JSON report and an annotated PNG,
    /// and quits the player/editor. Used for the measured results in the README; does nothing in normal play.
    /// </summary>
    [RequireComponent(typeof(PerceptionPipeline))]
    public sealed class PerceptionAutoTest : MonoBehaviour
    {
        IEnumerator Start()
        {
            string outDir = System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_OUT");
            if (string.IsNullOrEmpty(outDir)) yield break;
            Directory.CreateDirectory(outDir);
            var pipe = GetComponent<PerceptionPipeline>();
            int runs = int.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_RUNS"), out var r) ? r : 4;
            string deviceEnv = System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_DEVICE");
            if (deviceEnv == "CPU") pipe.device = InferenceDevice.CPU;
            if (System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_VERBOSE") == "1") pipe.verboseLog = true;
            if (float.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_BUDGET"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bud)) pipe.stepBudgetMs = bud;
            if (float.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_INITTIMEOUT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var it)) pipe.initTimeoutSeconds = it;
            bool cameraOnly = System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_MODES") == "camera";
            float tStart = Time.realtimeSinceStartup;
            if (int.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_FPS"), out var fps) && fps > 0) { Application.targetFrameRate = fps; QualitySettings.vSyncCount = 0; }
            if (float.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_ASPECT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var asp)) pipe.captureAspect = asp;
            if (int.TryParse(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_WIDTH"), out var cw)) pipe.captureWidth = cw;
            var sb = new StringBuilder("{\n");
            sb.Append($"\"device_requested\": \"{pipe.device}\", \"target_fps\": {Application.targetFrameRate},\n\"modes\": [\n");
            bool first = true;
            foreach (var mode in cameraOnly ? new[] { FrameSourceMode.GameCamera } : new[] { FrameSourceMode.GameCamera, FrameSourceMode.StaticPhoto })
            {
                float wait = 0;
                while (pipe.Status != PipelineStatus.Ready && pipe.Status != PipelineStatus.Failed && wait < 180) { wait += Time.unscaledDeltaTime; yield return null; }
                if (!first) sb.Append(",\n"); first = false;
                sb.Append($"{{\"mode\": \"{mode}\", \"status\": \"{pipe.Status}\", \"message\": {Json(pipe.StatusMessage)}, \"backend\": {Json(pipe.BackendDescription)}, \"validation\": {Json(pipe.LastValidation.Message)}");
                if (pipe.Status != PipelineStatus.Ready) { sb.Append("}"); continue; }
                sb.Append($", \"step_budget_ms\": {pipe.stepBudgetMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, \"time_to_ready_s\": {(Time.realtimeSinceStartup - tStart).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}");
                pipe.SetSourceMode(mode);
                // warm-up: first inference of a mode includes one-off costs (RT/material creation); not counted in frame stats
                int warm = pipe.CompletedInferences;
                float tw = Time.realtimeSinceStartup;
                while (pipe.CompletedInferences < warm + 1 && Time.realtimeSinceStartup < 600) yield return null;
                var cold = pipe.LatestResult.Timings; float coldWall = (Time.realtimeSinceStartup - tw) * 1000f;
                int start = pipe.CompletedInferences; float t0 = Time.realtimeSinceStartup;
                var frameMs = new List<float>();
                while (pipe.CompletedInferences < start + runs && Time.realtimeSinceStartup - t0 < 240) { frameMs.Add(Time.unscaledDeltaTime * 1000f); yield return null; }
                var res = pipe.LatestResult; var objs = pipe.LatestObjects;
                if (pipe.ManualBuffer != null) Debug.Log("[SemanticVR] manual renderers per category: " + string.Join(",", pipe.ManualBuffer.CountsPerCategory()));
                sb.Append($", \"cold_first_inference\": {{\"total_wall_ms\": {coldWall:F0}, \"capture\": {cold.CaptureMs:F1}, \"preprocess\": {cold.PreprocessMs:F1}, \"inference_wall\": {cold.InferenceWallMs:F0}, \"inference_main_thread\": {cold.InferenceMainMs:F1}, \"max_step\": {cold.MaxStepMs.ToString("F1")}}}");
                sb.Append($", \"inferences\": {pipe.CompletedInferences - start}, \"manual_renderers\": {(pipe.ManualBuffer != null ? pipe.ManualBuffer.RendererCount : 0)}");
                frameMs.Sort();
                float med = frameMs.Count > 0 ? frameMs[frameMs.Count / 2] : 0, max = frameMs.Count > 0 ? frameMs[frameMs.Count - 1] : 0;
                sb.Append($", \"frame_ms_median\": {med:F2}, \"frame_ms_p95\": {(frameMs.Count > 0 ? frameMs[Mathf.Min(frameMs.Count - 1, (int)(frameMs.Count * 0.95f))] : 0):F2}, \"frame_ms_max\": {max:F2}, \"frames_measured\": {frameMs.Count}");
                if (res != null)
                {
                    var t = res.Timings;
                    sb.Append($", \"timings_ms\": {{\"capture\": {t.CaptureMs:F2}, \"preprocess\": {t.PreprocessMs:F2}, \"inference_wall\": {t.InferenceWallMs:F1}, \"inference_main_thread\": {t.InferenceMainMs:F2}, \"max_step\": {t.MaxStepMs:F2}, \"readback\": {t.ReadbackMs:F2}, \"parse\": {t.ParseMs:F3}, \"mask\": {t.MaskMs:F3}, \"semantic\": {t.SemanticMs.ToString("F3")}}}");
                    sb.Append(", \"detections\": [");
                    for (int i = 0; i < objs.Count; i++)
                    {
                        var o = objs[i]; var d = o.Detection;
                        if (i > 0) sb.Append(",");
                        sb.Append($"\n  {{\"id\": {d.UniqueId}, \"class\": \"{d.ClassName}\", \"coco_id\": {d.CocoClassId}, \"conf\": {d.Confidence:F3}, \"box\": [{d.Box.xMin:F3},{d.Box.yMin:F3},{d.Box.xMax:F3},{d.Box.yMax:F3}], \"mask_cells\": {d.MaskCells}, \"ai\": \"{(o.HasAiCategory ? o.AiCategory.ToString() : "")}\", \"manual\": \"{(o.HasManualCategory ? o.ManualCategory.ToString() : "")}\", \"manual_share\": {o.ManualShare:F2}, \"final\": \"{(o.Source == CategorySource.None ? "" : o.FinalCategory.ToString())}\", \"source\": \"{o.Source}\", \"disagree\": {(o.Disagreement ? "true" : "false")}, \"importance\": {o.Importance.ToString("F1")}}}");
                    }
                    sb.Append("]");
                    if (pipe.ManualBuffer != null && pipe.ManualBuffer.HasData)
                        File.WriteAllBytes(Path.Combine(outDir, "manual_buffer_raw.bin"), pipe.ManualBuffer.DebugData);
                    var raw = OverlayBaker.Snapshot(pipe.LastFrame, pipe.LastFrame.width);
                    File.WriteAllBytes(Path.Combine(outDir, $"frame_{mode}.png"), raw.EncodeToPNG()); Destroy(raw);
                    var inp = pipe.DebugReadInputTensor();
                    var ib = new byte[inp.Length * 4]; System.Buffer.BlockCopy(inp, 0, ib, 0, ib.Length);
                    File.WriteAllBytes(Path.Combine(outDir, $"input_{mode}.bin"), ib);
                    var tex = OverlayBaker.Bake(pipe.LastFrame, res, objs);
                    File.WriteAllBytes(Path.Combine(outDir, $"annotated_{mode}.png"), tex.EncodeToPNG());
                    Destroy(tex);
                }
                sb.Append("}");
            }
            sb.Append("\n]\n}\n");
            File.WriteAllText(Path.Combine(outDir, "report.json"), sb.ToString());
            Debug.Log("[SemanticVR] AutoTest finished: " + outDir);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        static string Json(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ") + "\"";
    }
}
