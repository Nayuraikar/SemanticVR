using System.Collections;
using System.IO;
using System.Text;
using SemanticVR.Accessibility;
using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>
    /// Headless colour test. Active only when SVR_AUTOTEST_OUT is set. Pushes known colour patches through the real
    /// shader for several profiles/modes and writes matrices and 8-bit outputs to colour_report.json, which
    /// tools/scripts/verify_colour_math.py compares against an independent numpy implementation.
    /// </summary>
    [RequireComponent(typeof(ColourProcessor))]
    public sealed class ColourAutoTest : MonoBehaviour
    {
        static readonly Color32[] Patches =
        {
            new Color32(220, 40, 40, 255), new Color32(40, 150, 40, 255), new Color32(140, 90, 30, 255), new Color32(255, 140, 0, 255),
            new Color32(50, 80, 230, 255), new Color32(140, 60, 190, 255), new Color32(128, 128, 128, 255), new Color32(240, 240, 240, 255)
        };

        IEnumerator Start()
        {
            string outDir = System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_OUT");
            if (string.IsNullOrEmpty(outDir)) yield break;
            var proc = GetComponent<ColourProcessor>();
            bool only = System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_ONLY_COLOUR") == "1";
            if (only) { var pp = GetComponent<SemanticVR.Perception.PerceptionPipeline>(); pp.StopAllCoroutines(); pp.enabled = false; }
            yield return null;
            proc.active = false;
            var tex = new Texture2D(Patches.Length, 1, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point };   // sRGB-flagged pixel data (linear=false)
            tex.SetPixels32(Patches); tex.Apply();

            var cases = new (CvdType t, float sev, CorrectionMode m, float k)[]
            {
                (CvdType.Protanopia, 100, CorrectionMode.Balanced, 1f), (CvdType.Deuteranopia, 100, CorrectionMode.Balanced, 1f),
                (CvdType.Tritanopia, 100, CorrectionMode.Balanced, 1f), (CvdType.Deuteranomaly, 50, CorrectionMode.Balanced, 1f),
                (CvdType.Protanomaly, 30, CorrectionMode.MaxDistinguishability, 0.8f), (CvdType.Tritanomaly, 70, CorrectionMode.NaturalAppearance, 1f),
                (CvdType.Deuteranomaly, 100, CorrectionMode.HighContrast, 1f), (CvdType.Achromatopsia, 100, CorrectionMode.Balanced, 1f),
                (CvdType.Normal, 0, CorrectionMode.Balanced, 1f)
            };
            var sb = new StringBuilder("{\"cases\":[");
            bool first = true;
            foreach (var c in cases)
            {
                proc.Profile.type = c.t; proc.Profile.severityPercent = c.sev;
                proc.Settings.mode = c.m; proc.Settings.strength = c.k;
                proc.RenderViews(tex);
                var tr = proc.Current;
                if (!tr.Valid && c.t != CvdType.Normal) Debug.LogWarning("[SemanticVR] colour case invalid: " + c.t + " " + tr.Error);
                if (!first) sb.Append(","); first = false;
                float eff = proc.Profile.EffectiveSeverity01 * 100f;
                sb.Append($"{{\"type\":\"{c.t}\",\"severity\":{proc.Profile.EffectiveSeverity01.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},\"mode\":\"{c.m}\",\"strength\":{c.k.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},\"valid\":{(tr.Valid ? "true" : "false")},\"sim_matrix\":{Arr(tr.Simulation)},\"corr_matrix\":{Arr(tr.Correction)},\"patches\":[");
                var sim = Read(proc.SimulatedView); var cor = Read(proc.CorrectedView);
                for (int i = 0; i < Patches.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    int px = (int)((i + 0.5f) / Patches.Length * sim.width);
                    var a = Patches[i]; var s = sim.GetPixel(px, sim.height / 2); var k = cor.GetPixel(px, cor.height / 2);
                    sb.Append($"{{\"in\":[{a.r},{a.g},{a.b}],\"sim_out\":[{s.r * 255f:F2},{s.g * 255f:F2},{s.b * 255f:F2}],\"corr_out\":[{k.r * 255f:F2},{k.g * 255f:F2},{k.b * 255f:F2}]}}");
                }
                sb.Append("]}");
                Destroy(sim); Destroy(cor);
            }
            sb.Append("]}");
            File.WriteAllText(Path.Combine(outDir, "colour_report.json"), sb.ToString().Replace("NaN", "0"));
            Debug.Log("[SemanticVR] ColourAutoTest wrote colour_report.json");
            Destroy(tex);
            if (only) yield return Perf(proc, outDir);
        }

        // Colour-only performance: real camera capture + 3 blits every frame at a paced frame rate.
        IEnumerator Perf(ColourProcessor proc, string outDir)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            proc.Profile.type = CvdType.Deuteranomaly; proc.Profile.severityPercent = 100; proc.Settings.mode = CorrectionMode.Balanced; proc.Settings.strength = 1;
            var rows = new StringBuilder();
            foreach (bool active in new[] { false, true })
            {
                proc.active = active;
                for (int i = 0; i < 30; i++) yield return null;   // warm-up
                var frame = new System.Collections.Generic.List<float>(); var proc_ms = new System.Collections.Generic.List<float>();
                for (int i = 0; i < 240; i++) { yield return null; frame.Add(Time.unscaledDeltaTime * 1000f); if (active) proc_ms.Add(proc.LastProcessMs); }
                frame.Sort(); proc_ms.Sort();
                float Pct(System.Collections.Generic.List<float> l, float q) => l.Count == 0 ? 0 : l[Mathf.Min(l.Count - 1, (int)(l.Count * q))];
                if (rows.Length > 0) rows.Append(",");
                rows.Append($"{{\"colour_processing_active\":{(active ? "true" : "false")},\"target_fps\":{Application.targetFrameRate},\"preview_width\":{proc.previewWidth},\"frame_ms_median\":{Pct(frame, .5f).ToString("F2", ci)},\"frame_ms_p95\":{Pct(frame, .95f).ToString("F2", ci)},\"frame_ms_max\":{Pct(frame, 1f).ToString("F2", ci)},\"colour_cpu_ms_median\":{Pct(proc_ms, .5f).ToString("F3", ci)},\"colour_cpu_ms_p95\":{Pct(proc_ms, .95f).ToString("F3", ci)},\"matrix_ms\":{proc.LastMatrixMs.ToString("F4", ci)}}}");
            }
            // Sustained throughput: how long do N back-to-back three-view passes take when the GPU is forced to finish?
            var photo = proc.perception != null ? proc.perception.staticPhoto : null;
            if (photo != null)
            {
                proc.active = false; for (int i = 0; i < 20; i++) proc.RenderViews(photo);
                var tmp = Read(proc.CorrectedView); Destroy(tmp);
                var sw = System.Diagnostics.Stopwatch.StartNew(); const int N = 300;
                for (int i = 0; i < N; i++) proc.RenderViews(photo);
                var t2 = Read(proc.CorrectedView); Destroy(t2);
                rows.Append($",{{\"sustained_three_view_pass_ms\":{(sw.Elapsed.TotalMilliseconds / N).ToString("F3", ci)},\"passes\":{N},\"source\":\"static photo\",\"preview_width\":{proc.previewWidth}}}");
            }
            File.WriteAllText(Path.Combine(outDir, "colour_perf.json"), "{\"runs\":[" + rows + "]}");
            Debug.Log("[SemanticVR] ColourAutoTest wrote colour_perf.json");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        static string Arr(Matrix3x3 m)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("[");
            for (int i = 0; i < 9; i++) { if (i > 0) sb.Append(","); sb.Append(m[i].ToString("R", ci)); }
            return sb.Append("]").ToString();
        }

        static Texture2D Read(Texture rt)
        {
            var r = (RenderTexture)rt; var prev = RenderTexture.active; RenderTexture.active = r;
            var t = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, r.width, r.height), 0, 0); t.Apply();
            RenderTexture.active = prev; return t;
        }
    }
}
