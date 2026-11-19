using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SemanticVR.Assist;
using SemanticVR.Accessibility;
using SemanticVR.ColourProcessing;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Player;
using SemanticVR.Semantics;
using SemanticVR.World;
using UnityEngine;
using UnityEngine.UI;

namespace SemanticVR.DemoUI
{
    /// <summary>Scripted input for tests (same IPlayerInput interface the desktop and a future Cardboard input implement).</summary>
    public sealed class ScriptedPlayerInput : IPlayerInput
    {
        public Vector2 Move { get; set; }
        public Vector2 Look { get; set; }
        public bool Sprint { get; set; }
        public bool Active { get; set; } = true;
    }

    /// <summary>
    /// Headless end-to-end test of the walkable demo. Inert unless SVR_DEMO_TEST_OUT names an output directory. Drives the
    /// real scene with scripted input and writes demo_report.json plus screenshots. Measures only; makes no real-time or
    /// device claims beyond what it records on the machine it runs on.
    /// </summary>
    public sealed class DemoAutoTest : MonoBehaviour
    {
        readonly List<string> checks = new List<string>();
        readonly StringBuilder extra = new StringBuilder();
        string outDir; bool allPass = true;
        static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;

        void Check(string name, bool pass, string detail)
        {
            if (!pass) allPass = false;
            checks.Add($"{{\"name\":\"{name}\",\"pass\":{(pass ? "true" : "false")},\"detail\":\"{detail.Replace("\"", "'")}\"}}");
            Debug.Log($"[DemoTest] {(pass ? "PASS" : "FAIL")} {name}: {detail}");
        }
        void Num(string k, float v) { extra.Append($"\"{k}\":{v.ToString("F4", CI)},"); }

        IEnumerator Start()
        {
            outDir = System.Environment.GetEnvironmentVariable("SVR_DEMO_TEST_OUT");
            if (string.IsNullOrEmpty(outDir)) yield break;
            Directory.CreateDirectory(outDir);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            var pipe = GetComponent<PerceptionPipeline>(); var proc = GetComponent<ColourProcessor>(); var view = GetComponent<ColourAssistView>();
            var layer = GetComponent<SemanticAssistLayer>(); var ui = GetComponent<DemoUiController>();
            var sim = FindFirstObjectByType<TrafficSimulation>(); var lights = FindFirstObjectByType<TrafficLightController>();
            var pc = FindFirstObjectByType<PlayerController>(); var desk = pc.GetComponent<DesktopPlayerInput>();

            float wait = 0;
            while (pipe.Status != PipelineStatus.Ready && pipe.Status != PipelineStatus.Failed && wait < 240) { wait += Time.unscaledDeltaTime; yield return null; }
            Check("AI pipeline ready (golden validation passed)", pipe.Status == PipelineStatus.Ready, $"{pipe.Status} after {wait:0}s {pipe.StatusMessage}; {(pipe.LastValidation.Message ?? "")}");
            Num("pipeline_ready_s", wait);

            desk.enabled = false;                      // scripted input replaces the keyboard/mouse implementation
            var input = new ScriptedPlayerInput(); pc.Input = input;
            Check("scene links", sim != null && lights != null && sim.VehicleCount >= 12 && sim.PedestrianCount >= 12, $"vehicles {sim?.VehicleCount}, pedestrians {sim?.PedestrianCount}");
            Check("colour pipeline wired", proc.semanticAssist != null && view != null && ui != null, "semanticAssist hooked");

            string phase = System.Environment.GetEnvironmentVariable("SVR_DEMO_PHASE") ?? "";   // "A", "B" or all (an editor recompile after play start can reload the domain, so the test can be split)
            var cam = Camera.main != null ? Camera.main : pc.head.GetComponent<Camera>();
            Vector3 p0 = pc.transform.position; float t0;
            Slider sev = null; var frames = new List<float>();
            if (phase == "" || phase == "A") {
            // ---------- movement: eye height, WASD speed, mouse yaw/pitch, kerb
            view.View = AssistViewMode.Original; view.Compare = false;
            yield return Frames(10);
            float eye = pc.head.position.y - pc.transform.position.y;
            Check("eye height", Mathf.Abs(eye - 1.6f) < 0.06f, $"{eye:0.000} m above feet");
            p0 = pc.transform.position; t0 = Time.time;
            input.Move = new Vector2(0, 1);
            while (Time.time - t0 < 3f) yield return null;
            input.Move = Vector2.zero; yield return Frames(20);
            float dist = Vector3.Distance(new Vector3(p0.x, 0, p0.z), new Vector3(pc.transform.position.x, 0, pc.transform.position.z));
            Check("walk speed ~1.5 m/s", dist > 3.6f && dist < 4.8f, $"{dist:0.00} m in 3 s (walk, with acceleration)");
            Check("grounded on ground/kerb", pc.transform.position.y > -0.05f && pc.transform.position.y < 0.3f, $"y={pc.transform.position.y:0.000}");
            float yaw0 = pc.transform.eulerAngles.y;
            input.Look = new Vector2(30f, 0); yield return null; input.Look = Vector2.zero; yield return null;
            Check("mouse yaw", Mathf.Abs(Mathf.DeltaAngle(yaw0, pc.transform.eulerAngles.y) - 30f) < 1f, $"yaw delta {Mathf.DeltaAngle(yaw0, pc.transform.eulerAngles.y):0.0}");
            for (int i = 0; i < 30; i++) { input.Look = new Vector2(0, 10f); yield return null; }
            input.Look = Vector2.zero; yield return null;
            float pitch = pc.head.localEulerAngles.x; if (pitch > 180) pitch -= 360;
            Check("pitch clamped", pitch <= -pc.maxPitch + 0.5f && pitch >= -pc.maxPitch - 0.5f, $"pitch {pitch:0.0} (limit {pc.maxPitch})");
            input.Look = new Vector2(0, -400f); yield return null; input.Look = Vector2.zero;

            // ---------- collision with a building
            pc.Teleport(new Vector3(6.6f, 0.15f, 20f), 90f); yield return Frames(5);
            input.Move = new Vector2(0, 1); input.Sprint = true; t0 = Time.time;
            while (Time.time - t0 < 6f) yield return null;
            input.Move = Vector2.zero; input.Sprint = false; yield return Frames(5);
            Check("building collision", pc.transform.position.x < 12.05f, $"walked east 6 s at run speed into the building at x=12, ended at x={pc.transform.position.x:0.00}");

            // ---------- traffic lights, vehicles, pedestrians over two full cycles (accelerated simulation clock)
            pc.Teleport(new Vector3(-6.5f, 0.15f, -30f), 0f);      // stand clear of lanes on the south sidewalk
            Time.timeScale = 4f;
            var seen = new HashSet<string>(); string last = ""; int conflict = 0, lampMismatch = 0; int maxStopped = 0, maxCrossing = 0;
            float maxVehStep = 0, maxPedStep = 0; int samples = 0;
            var vpos = new Dictionary<Transform, Vector3>(); var ppos = new Dictionary<Transform, Vector3>();
            float tStart = Time.time; float span = lights.CycleSeconds * 2f + 2f;
            while (Time.time - tStart < span)
            {
                yield return null; samples++;
                string st = lights.NorthSouth + "/" + lights.EastWest;
                if (st != last) { seen.Add(st); last = st; }
                if (lights.NorthSouth != SignalState.Red && lights.EastWest != SignalState.Red) conflict++;
                foreach (var h in lights.heads)
                {
                    var s = lights.StateFor(h.axis); var expectedMat = s == SignalState.Red ? lights.redOn : s == SignalState.Yellow ? lights.yellowOn : lights.greenOn;
                    Renderer r = s == SignalState.Red ? h.red : s == SignalState.Yellow ? h.yellow : h.green;
                    if (r.sharedMaterial != expectedMat) lampMismatch++;
                }
                maxStopped = Mathf.Max(maxStopped, sim.VehiclesStopped); maxCrossing = Mathf.Max(maxCrossing, sim.PedestriansCrossing);
                float dt = Mathf.Min(Time.deltaTime, 0.1f);
                foreach (var v in sim.vehicles)
                {
                    if (!v.gameObject.activeSelf) { vpos.Remove(v); continue; }
                    if (vpos.TryGetValue(v, out var pp) && dt > 0) maxVehStep = Mathf.Max(maxVehStep, Vector3.Distance(pp, v.position) / dt);
                    vpos[v] = v.position;
                }
                foreach (var p in sim.pedestrians)
                {
                    if (ppos.TryGetValue(p, out var pp) && dt > 0) maxPedStep = Mathf.Max(maxPedStep, Vector3.Distance(pp, p.position) / dt);
                    ppos[p] = p.position;
                }
            }
            Time.timeScale = 1f;
            Check("traffic light cycle", seen.Contains("Green/Red") && seen.Contains("Yellow/Red") && seen.Contains("Red/Green") && seen.Contains("Red/Yellow") && seen.Contains("Red/Red"), "states seen: " + string.Join(", ", seen));
            Check("no conflicting greens", conflict == 0, $"{conflict} frames with both axes non-red");
            Check("lamp materials follow state", lampMismatch == 0, $"{lampMismatch} mismatches over {samples} frames");
            Check("vehicles stop at red", maxStopped >= 2 && sim.RedLightViolations == 0, $"max stopped {maxStopped}, red-light violations {sim.RedLightViolations}");
            Check("vehicle motion continuous", maxVehStep <= sim.maxSpeed * 1.15f + 0.2f, $"max per-frame speed {maxVehStep:0.00} m/s (limit {sim.maxSpeed}), observed v {sim.MaxVehicleSpeedObserved:0.00}, respawns (fade at far end) {sim.VehicleRespawns}");
            Check("pedestrian motion continuous", maxPedStep <= 3.0f, $"max per-frame speed {maxPedStep:0.00} m/s (walk <= {sim.walkSpeedMax})");
            Check("pedestrians use crosswalks safely", maxCrossing >= 1 && sim.PedestrianConflictFrames == 0, $"max crossing at once {maxCrossing}, frames on crosswalk without red for vehicles {sim.PedestrianConflictFrames}");
            Num("sim_cpu_ms", sim.SimMs);

            // ---------- colour assist: priority-aware weights on moving-world snapshot (world frozen for exact comparison)
            pc.Teleport(new Vector3(-6.5f, 0.15f, -11f), Quaternion.LookRotation(new Vector3(6.5f, 0, 11f)).eulerAngles.y);
            yield return Frames(15);
            Time.timeScale = 0f;
            view.Compare = true; layer.useAiMasks = false; layer.focus = 0.8f;
            yield return Frames(6);
            layer.prioritise = false; yield return Frames(4);
            var cOff = new CatStats(); yield return Measure(proc, layer, cOff);
            layer.prioritise = true; yield return Frames(4);
            var cOn = new CatStats(); yield return Measure(proc, layer, cOn);
            Check("priority-aware pass active", proc.LastCorrectionPriorityAware, "LastCorrectionPriorityAware");
            var sb = new StringBuilder(); int tested = 0, okCount = 0; var exp = new List<float>(); var meas = new List<float>(); var imp = pipe.Importance != null ? pipe.Importance : pipe.importanceConfig;
            for (int c = 0; c < SemanticImportanceConfig.CategoryCount; c++)
            {
                if (cOn.count[c + 1] < 400 || cOff.change[c + 1] < 0.004f) continue;
                float w = imp.GetWeight((SemanticCategory)c) / 5f, expect = 0.2f + 0.8f * w, ratio = cOn.change[c + 1] / cOff.change[c + 1];
                bool ok = Mathf.Abs(ratio - expect) < 0.30f; tested++; if (ok) okCount++; exp.Add(expect); meas.Add(ratio);
                sb.Append($"{(SemanticCategory)c}: weight {imp.GetWeight((SemanticCategory)c):0}, pixels {cOn.count[c + 1]}, strength expected {expect:0.00} measured {ratio:0.00}{(ok ? "" : " (!)")}; ");
            }
            // sRGB encoding makes small linear changes look larger, so measured ratios run a little above the linear expectation; ordering must still hold
            int inversions = 0; for (int a = 0; a < exp.Count; a++) for (int b = 0; b < exp.Count; b++) if (exp[a] > exp[b] + 0.25f && meas[a] < meas[b]) inversions++;
            Check("correction strength follows importance weights", tested >= 3 && okCount == tested && inversions == 0, $"{okCount}/{tested} within 0.30 of linear expectation, {inversions} order inversions: {sb}");
            SaveViews(proc, "assist_tags_only");
            // AI masks on top: stationary => fade ~1
            layer.useAiMasks = true; view.Compare = true; yield return Frames(3);
            Time.timeScale = 1f;
            float aiWait = 0; int inf0 = pipe.CompletedInferences;
            while (pipe.CompletedInferences < inf0 + 2 && aiWait < 60) { aiWait += Time.unscaledDeltaTime; yield return null; }
            Check("AI inference runs in the walkable scene", pipe.CompletedInferences >= inf0 + 2, $"{pipe.CompletedInferences - inf0} new inferences, objects {pipe.LatestObjects.Count}, AI fade {layer.AiFade:0.00}");
            var objs = new StringBuilder(); foreach (var o in pipe.LatestObjects) objs.Append($"{CocoClasses.NameOf(o.Detection.CocoClassId)}:{o.Detection.Confidence:0.00}->{(o.Source == CategorySource.None ? "none" : o.FinalCategory.ToString())}({o.Importance:0}) ");
            extra.Append($"\"ai_objects\":\"{objs.ToString().Replace("\"", "'")}\",");
            SaveViews(proc, "assist_with_ai");

            }   // end phase A
            if (phase == "" || phase == "B") {
            // ---------- profile / severity / mode change live, no restart
            ui.SetView(AssistViewMode.Corrected); yield return Frames(4);
            var m0 = proc.Current.Simulation; var c0 = proc.Current.Correction;
            ui.SetFamily(3); yield return Frames(3);
            Check("profile switch live", proc.Profile.type == CvdType.Tritanomaly && !Mat3Eq(m0, proc.Current.Simulation), $"type {proc.Profile.type}");
            ui.SetFamily(2); sev = FindSlider("Slider", 0); sev.value = 40f; yield return Frames(3);
            var simAt40 = proc.Current.Simulation; sev.value = 90f; yield return Frames(3);
            Check("severity slider live", Mathf.Approximately(proc.Profile.severityPercent, 90f) && !Mat3Eq(simAt40, proc.Current.Simulation), $"severity {proc.Profile.severityPercent}");
            var mb = proc.Current.Correction; ui.SetMode(3); yield return Frames(3);
            Check("correction mode live", proc.Settings.mode == CorrectionMode.HighContrast && (proc.Current.Contrast != 1f || !Mat3Eq(mb, proc.Current.Correction)), $"mode {proc.Settings.mode}, contrast {proc.Current.Contrast:0.00}");
            ui.SetMode(0);
            var simBtn = FindButton("Simulated"); simBtn.onClick.Invoke(); yield return Frames(3);
            Check("UI button changes view", view.View == AssistViewMode.Simulated && !view.Compare, "clicked 'Simulated'");
            FindButton("Original").onClick.Invoke(); yield return Frames(3);
            Check("original view restores direct render", view.View == AssistViewMode.Original && !view.ProcessedOnScreen && cam.enabled, $"camera enabled {cam.enabled}");
            ui.SetView(AssistViewMode.Corrected);

            // ---------- walking with assist on: visible every sampled frame, and performance
            view.Compare = false; ui.SetFamily(2); sev.value = 100f; layer.focus = 0.5f; layer.prioritise = true; layer.useAiMasks = true; yield return Frames(10);
            input.Move = new Vector2(0, 1); var diffs = new List<float>(); int k = 0;
            float tw = Time.time;
            for (int i = 0; i < 360; i++)
            {
                input.Look = new Vector2(i % 120 < 60 ? 0.6f : -0.6f, 0);
                if (i % 90 == 89) input.Move = new Vector2(i % 180 < 90 ? 0.3f : -0.3f, 1);
                yield return null; frames.Add(Time.unscaledDeltaTime * 1000f);
                if (i % 40 == 20) { var pair = ViewDiff(proc, true); diffs.Add(pair); if (k++ == 2) SaveViews(proc, "walking", true); }
            }
            input.Move = Vector2.zero; input.Look = Vector2.zero;
            float minDiff = float.MaxValue; foreach (var d in diffs) minDiff = Mathf.Min(minDiff, d);
            Check("correction visible while moving", diffs.Count >= 8 && minDiff > 0.004f, $"mean |corrected-original| per sampled frame: min {minDiff:0.0000} over {diffs.Count} samples, walked {Vector3.Distance(p0, pc.transform.position):0.0} m");
            frames.Sort(); float med = frames[frames.Count / 2], p95 = frames[(int)(frames.Count * 0.95f)], max = frames[frames.Count - 1];
            extra.Append($"\"walk_frame_ms\":{{\"median\":{med.ToString("F2", CI)},\"p95\":{p95.ToString("F2", CI)},\"max\":{max.ToString("F2", CI)},\"samples\":{frames.Count}}},\"assist_on\":{{\"colour_cpu_ms\":{proc.LastProcessMs.ToString("F2", CI)},\"tag_pass_ms\":{layer.LastPrepareMs.ToString("F2", CI)}}},");
            Debug.Log($"[DemoTest] walking frame ms median {med:0.0} p95 {p95:0.0} max {max:0.0} (60 fps target)");

            }   // end phase B
            if (phase == "" || phase == "C") {
            // baseline: same scene, assist off (Original view)
            ui.SetView(AssistViewMode.Original); yield return Frames(30);
            input.Move = new Vector2(0, -1); frames.Clear();
            for (int i = 0; i < 240; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000f); }
            input.Move = Vector2.zero; frames.Sort();
            extra.Append($"\"walk_frame_ms_assist_off\":{{\"median\":{frames[frames.Count / 2].ToString("F2", CI)},\"p95\":{frames[(int)(frames.Count * 0.95f)].ToString("F2", CI)},\"max\":{frames[frames.Count - 1].ToString("F2", CI)}}},");

            // ---------- UI screenshots
            ui.SetView(AssistViewMode.Corrected); yield return Frames(10);
            yield return Shot("ui_walking_corrected");
            desk.SetMenu(true); yield return Frames(10); yield return Shot("ui_menu_corrected");
            ui.SetCompare(true); yield return Frames(10); yield return Shot("ui_menu_compare");
            ui.SetCompare(false); ui.SetView(AssistViewMode.Original); yield return Frames(10); yield return Shot("ui_menu_original");
            ui.SetView(AssistViewMode.Simulated); yield return Frames(10); yield return Shot("ui_menu_simulated");
            FindButton("Debug info  [F1]").onClick.Invoke(); yield return Frames(5); yield return Shot("ui_debug_visible");
            }   // end phase C
            Finish(phase);
        }

        // ------------------------------------------------------------------ helpers
        static WaitForFrames Frames(int n) => new WaitForFrames(n);
        sealed class WaitForFrames : CustomYieldInstruction
        {
            int left; public WaitForFrames(int n) { left = n; }
            public override bool keepWaiting => --left >= 0;
        }

        static bool Mat3Eq(Matrix3x3 a, Matrix3x3 b) => (a.Row0 - b.Row0).sqrMagnitude + (a.Row1 - b.Row1).sqrMagnitude + (a.Row2 - b.Row2).sqrMagnitude < 1e-8f;

        Slider FindSlider(string n, int index) { var all = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None); System.Array.Sort(all, (a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform))); return all[Mathf.Min(index, all.Length - 1)]; }
        static string Path(Transform t) { var s = new StringBuilder(); for (var x = t; x != null; x = x.parent) s.Insert(0, x.GetSiblingIndex().ToString("D3") + "/"); return s.ToString(); }
        Button FindButton(string label)
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { var t = b.GetComponentInChildren<Text>(true); if (t != null && t.text == label) return b; }
            return null;
        }

        sealed class CatStats { public int[] count = new int[16]; public float[] change = new float[16]; }

        Texture2D Read(Texture rt)
        {
            var r = (RenderTexture)rt; var prev = RenderTexture.active; RenderTexture.active = r;
            var t = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false); t.ReadPixels(new Rect(0, 0, r.width, r.height), 0, 0); t.Apply(); RenderTexture.active = prev; return t;
        }

        // Mean |corrected - original| per tag category (tag texture sampled at each view pixel).
        IEnumerator Measure(ColourProcessor proc, SemanticAssistLayer layer, CatStats s)
        {
            yield return null;
            var o = Read(proc.OriginalView); var c = Read(proc.CorrectedView); var tg = Read(layer.TagTexture);
            var op = o.GetPixels32(); var cp = c.GetPixels32(); var tp = tg.GetPixels32();
            int W = o.width, H = o.height, TW = tg.width, TH = tg.height;
            var sum = new double[16];
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                {
                    int ti = Mathf.Min(TH - 1, y * TH / H) * TW + Mathf.Min(TW - 1, x * TW / W);
                    int cat = Mathf.Clamp(Mathf.RoundToInt(tp[ti].r), 0, 15);
                    int i = y * W + x;
                    float d = (Mathf.Abs(op[i].r - cp[i].r) + Mathf.Abs(op[i].g - cp[i].g) + Mathf.Abs(op[i].b - cp[i].b)) / (3f * 255f);
                    sum[cat] += d; s.count[cat]++;
                }
            for (int i = 0; i < 16; i++) s.change[i] = s.count[i] > 0 ? (float)(sum[i] / s.count[i]) : 0f;
            Destroy(o); Destroy(c); Destroy(tg);
        }

        float ViewDiff(ColourProcessor proc, bool corrected)
        {
            var o = Read(proc.OriginalView); var c = Read(corrected ? proc.CorrectedView : proc.SimulatedView);
            var op = o.GetPixels32(); var cp = c.GetPixels32(); double s = 0; int n = 0;
            for (int i = 0; i < op.Length; i += 7) { s += (Mathf.Abs(op[i].r - cp[i].r) + Mathf.Abs(op[i].g - cp[i].g) + Mathf.Abs(op[i].b - cp[i].b)) / (3f * 255f); n++; }
            Destroy(o); Destroy(c); return (float)(s / n);
        }

        void SaveViews(ColourProcessor proc, string name, bool singleCorrectedOnly = false)
        {
            foreach (var (n, t) in new[] { ("original", proc.OriginalView), ("simulated", proc.SimulatedView), ("corrected", proc.CorrectedView) })
            {
                if (t == null || (singleCorrectedOnly && n != "corrected")) continue;
                var tx = Read(t); File.WriteAllBytes(Path2(name + "_" + n + ".png"), tx.EncodeToPNG()); Destroy(tx);
            }
        }
        string Path2(string f) => System.IO.Path.Combine(outDir, f);

        // Off-screen UI screenshot (batch mode has no window): player camera + every overlay canvas rendered through a camera into a texture.
        IEnumerator Shot(string name)
        {
            yield return null;
            const int W = 1280, H = 720;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<PlayerController>().head.GetComponent<Camera>();
            var prevTarget = cam.targetTexture; cam.targetTexture = rt; cam.aspect = (float)W / H;
            var canvases = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { canvases.Add(c); c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 0.2f; }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var c in canvases) c.renderMode = RenderMode.ScreenSpaceOverlay;
            cam.targetTexture = prevTarget; cam.ResetAspect();
            var go = new GameObject("~unused");
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tx = new Texture2D(W, H, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, W, H), 0, 0); tx.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes(Path2(name + ".png"), tx.EncodeToPNG());
            Destroy(tx); Destroy(go); rt.Release(); Destroy(rt);
        }

        void Finish(string phase)
        {
            Check("overall", allPass, "all checks above");
            File.WriteAllText(Path2("demo_report" + (phase == "" ? "" : "_" + phase) + ".json"), "{" + extra + $"\"all_pass\":{(allPass ? "true" : "false")},\"checks\":[{string.Join(",\n", checks)}]}}");
            Debug.Log("[DemoTest] report written. all_pass=" + allPass);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(allPass ? 0 : 1);
#else
            Application.Quit(allPass ? 0 : 1);
#endif
        }
    }
}
