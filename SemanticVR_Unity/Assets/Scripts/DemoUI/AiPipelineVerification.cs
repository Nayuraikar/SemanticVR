using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using SemanticVR.Assist;
using SemanticVR.ColourProcessing;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Player;
using SemanticVR.Semantics;
using UnityEngine;

namespace SemanticVR.DemoUI
{
    /// <summary>
    /// Verifies that the AI pipeline works on the live first-person view. Inert unless SVR_AI_VERIFY names an output directory.
    /// The pipeline runs in PURE-AI mode (manual tag fallback off: no tag buffer is built, so no object can take its category from a
    /// SemanticTag). A separate, test-owned tag buffer is rendered only as an independent ground truth to SCORE the AI, never as
    /// evidence that the AI recognised anything. Every published result is recorded while a scripted player stands, turns and walks.
    /// </summary>
    public sealed class AiPipelineVerification : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SVR_AI_VERIFY"))) return;
            new GameObject("~AiPipelineVerification").AddComponent<AiPipelineVerification>();
        }

        static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
        static readonly int NCat = SemanticImportanceConfig.CategoryCount;

        sealed class Det { public string cls; public int coco; public float conf; public Rect box; public int maskCells, maskPx, frameIndex; public bool hasAi; public SemanticCategory ai, fin; public CategorySource src; public float importance; public bool gtKnown; public SemanticCategory gt; public float gtShare; }
        sealed class Snap
        {
            public int serial; public string segment; public Vector3 pos; public float yaw; public float speed; public float aiFade;
            public List<Det> dets = new List<Det>(); public byte[] ids; public int S; public Texture2D frame; public bool scored;
        }
        sealed class ClassStat { public int n, agree, disagree, untagged; public float sumConf, sumPx, minConf = 9; }
        sealed class CatStat { public long vis, cov, covAny; public int framesVisible, framesDetected; }

        readonly List<string> lines = new List<string>(); string outDir; bool allPass = true;
        readonly List<Snap> snaps = new List<Snap>();
        readonly Dictionary<string, ClassStat> perClass = new Dictionary<string, ClassStat>();
        readonly CatStat[] perCat = new CatStat[NCat];
        readonly Dictionary<string, List<Snap>> bySegment = new Dictionary<string, List<Snap>>();
        readonly List<int> cats = new List<int>();   // COCO ids the semantic map turns into a category (filled in Start, read in Report)
        PerceptionPipeline pipe; SemanticAssistLayer layer; PlayerController pc; ScriptedPlayerInput input;
        ManualSemanticBuffer gt; Snap evalSnap; string segment = "init"; int savedFrames; float curSpeed;
        const int MinVisPx = 20;   // a category counts as "visible" in a frame when at least this many cells of the id map show it

        void Log(string s) { lines.Add(s); Debug.Log("[AiVerify] " + s); }
        void Check(string n, bool ok, string d = "") { if (!ok) allPass = false; Log($"{(ok ? "PASS" : "FAIL")} {n} {d}"); }

        IEnumerator Start()
        {
            outDir = System.Environment.GetEnvironmentVariable("SVR_AI_VERIFY"); Directory.CreateDirectory(outDir);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            for (int i = 0; i < perCat.Length; i++) perCat[i] = new CatStat();
            pipe = FindFirstObjectByType<PerceptionPipeline>(); layer = FindFirstObjectByType<SemanticAssistLayer>();
            var proc = FindFirstObjectByType<ColourProcessor>(); var view = FindFirstObjectByType<ColourAssistView>();
            pc = FindFirstObjectByType<PlayerController>(); pc.GetComponent<DesktopPlayerInput>().enabled = false;
            input = new ScriptedPlayerInput(); pc.Input = input;
            float wait = 0; while (pipe.Status != PipelineStatus.Ready && pipe.Status != PipelineStatus.Failed && wait < 300) { wait += Time.unscaledDeltaTime; yield return null; }
            Check("AI pipeline ready", pipe.Status == PipelineStatus.Ready, $"{pipe.Status} after {wait:0}s; device {pipe.ActiveDevice}; backend {pipe.BackendDescription}; golden: {pipe.LastValidation.Message}");
            if (pipe.Status != PipelineStatus.Ready) { Finish(); yield break; }

            // ---- pure-AI mode: rebuild the frame source without the manual tag buffer
            pipe.useManualTags = false; pipe.SetSourceMode(FrameSourceMode.GameCamera); yield return null;
            Check("PURE-AI mode: pipeline has no manual tag buffer", pipe.ManualBuffer == null, "objects can only take AI categories");
            gt = new ManualSemanticBuffer(pipe.instanceMapSize);    // test-owned ground truth, used for scoring only
            Check("ground-truth tag buffer built (scoring only)", gt.IsValid && gt.RendererCount > 0, $"{gt.RendererCount} tagged renderers in the scene");

            // ---- user importance weights before anything runs
            var w0 = Weights(pipe.Importance); var asset0 = pipe.importanceConfig != null ? Weights(pipe.importanceConfig) : null;
            cats.Clear(); for (int c = 1; c <= 90; c++) { if (pipe.cocoMap != null && pipe.cocoMap.TryGetCategory(c, out var cc)) cats.Add(c); }
            var mapped = new StringBuilder(); foreach (int c in cats) { pipe.cocoMap.TryGetCategory(c, out var cc); mapped.Append($"{CocoClasses.NameOf(c)}->{cc}, "); }
            Log("COCO classes mapped to a semantic category: " + mapped);

            pipe.ResultPublished += OnResult;
            yield return Route(proc, view);
            pipe.ResultPublished -= OnResult;
            while (evalSnap != null) yield return null;

            // ---- weights untouched by AI
            Check("importance weights unchanged by AI over the whole run (runtime copy)", Same(w0, Weights(pipe.Importance)), "all 4 profiles x categories compared");
            if (asset0 != null) Check("importance asset unchanged", Same(asset0, Weights(pipe.importanceConfig)));

            Report();
            yield return ImportanceAndColour(proc, view);
            Finish();
        }

        // ------------------------------------------------------------------ scripted route
        IEnumerator Route(ColourProcessor proc, ColourAssistView view)
        {
            // viewpoint A: south sidewalk looking at the junction (same pose the demo test uses)
            Vector3 A = new Vector3(-6.5f, 0.15f, -11f); float yawA = Quaternion.LookRotation(new Vector3(6.5f, 0, 11f)).eulerAngles.y;
            pc.Teleport(A, yawA); yield return Seg("stand_junction", 10f);
            yield return Turn("turn_in_place_360", 24f, 15f);
            pc.Teleport(A, yawA); yield return Walk("walk_forward", 12f, 0f);
            yield return Walk("walk_and_turn", 8f, 12f);
            pc.Teleport(new Vector3(6.6f, 0.15f, 20f), 90f); yield return Seg("stand_east", 8f);
            yield return Turn("turn_in_place_180", 12f, 15f);
        }

        IEnumerator Seg(string name, float seconds) { segment = name; input.Move = Vector2.zero; input.Look = Vector2.zero; curSpeed = 0; float t = 0; while (t < seconds) { t += Time.deltaTime; yield return null; } }
        IEnumerator Turn(string name, float seconds, float degPerSec) { segment = name; input.Move = Vector2.zero; curSpeed = 0; float t = 0; while (t < seconds) { input.Look = new Vector2(degPerSec * Time.deltaTime, 0); t += Time.deltaTime; yield return null; } input.Look = Vector2.zero; }
        IEnumerator Walk(string name, float seconds, float turnDegPerSec) { segment = name; curSpeed = 1.5f; input.Move = new Vector2(0, 1); float t = 0; while (t < seconds) { input.Look = new Vector2(turnDegPerSec * Time.deltaTime, 0); t += Time.deltaTime; yield return null; } input.Move = Vector2.zero; input.Look = Vector2.zero; curSpeed = 0; }

        // ------------------------------------------------------------------ recording
        void OnResult(PerceptionResult r)
        {
            var cam = pipe.CaptureViewCamera; if (cam == null) return;
            var s = new Snap { serial = r.Serial, segment = segment, pos = cam.transform.position, yaw = cam.transform.eulerAngles.y, speed = curSpeed, aiFade = layer != null ? layer.AiFade : -1f, S = r.IdSize };
            if (r.InstanceIds != null) { s.ids = new byte[r.InstanceIds.Length]; Buffer.BlockCopy(r.InstanceIds, 0, s.ids, 0, s.ids.Length); }
            foreach (var o in pipe.LatestObjects)
            {
                var d = o.Detection;
                var x = new Det { cls = d.ClassName, coco = d.CocoClassId, conf = d.Confidence, box = d.Box, maskCells = d.MaskCells, frameIndex = d.FrameIndex, hasAi = o.HasAiCategory, ai = o.AiCategory, fin = o.FinalCategory, src = o.Source, importance = o.Importance };
                if (s.ids != null) for (int i = 0; i < s.ids.Length; i++) if (s.ids[i] == d.FrameIndex) x.maskPx++;
                s.dets.Add(x);
            }
            snaps.Add(s); if (!bySegment.TryGetValue(segment, out var l)) bySegment[segment] = l = new List<Snap>(); l.Add(s);
            if (evalSnap == null && gt != null && !gt.IsPending) { gt.Render(cam); evalSnap = s; if (savedFrames < 14 && (s.dets.Count >= 2 || savedFrames < 3)) { s.frame = ReadFrame(pipe.LastFrame); } }
        }

        void Update()
        {
            if (evalSnap != null && gt != null && !gt.IsPending && gt.HasData) { Score(evalSnap); if (evalSnap.frame != null) SaveSnap(evalSnap); evalSnap = null; }
        }

        static Texture2D ReadFrame(Texture src)
        {
            var rt = src as RenderTexture; if (rt == null) return null;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); t.Apply(); RenderTexture.active = prev; return t;
        }

        void Score(Snap s)
        {
            var g = gt.DebugData; int S = s.S; if (s.ids == null || g.Length < S * S) return;
            var vis = new int[NCat]; var cov = new int[NCat]; var covAny = new int[NCat];
            var detByIndex = new Dictionary<int, Det>(); foreach (var d in s.dets) detByIndex[d.frameIndex] = d;
            var hist = new Dictionary<Det, int[]>(); foreach (var d in s.dets) hist[d] = new int[NCat + 1];
            for (int i = 0; i < S * S; i++)
            {
                int v = g[i]; int id = s.ids[i]; Det d = null; if (id != 0) detByIndex.TryGetValue(id, out d);
                if (v >= 1 && v <= NCat)
                {
                    vis[v - 1]++;
                    if (d != null) { covAny[v - 1]++; if (d.hasAi && (int)d.ai == v - 1) cov[v - 1]++; }
                }
                if (d != null) hist[d][v <= NCat ? v : 0]++;
            }
            for (int c = 0; c < NCat; c++)
            {
                var cs = perCat[c]; cs.vis += vis[c]; cs.cov += cov[c]; cs.covAny += covAny[c];
                if (vis[c] >= MinVisPx) { cs.framesVisible++; bool any = false; foreach (var d in s.dets) if (d.hasAi && (int)d.ai == c) any = true; if (any) cs.framesDetected++; }
            }
            foreach (var d in s.dets)
            {
                var h = hist[d]; int tagged = 0, best = 0; for (int c = 1; c <= NCat; c++) { tagged += h[c]; if (h[c] > h[best == 0 ? 1 : best]) best = c; }
                if (tagged > 0) { int bi = 1; for (int c = 2; c <= NCat; c++) if (h[c] > h[bi]) bi = c; d.gtKnown = true; d.gt = (SemanticCategory)(bi - 1); d.gtShare = h[bi] / (float)tagged; }
            }
            s.scored = true;
        }

        void SaveSnap(Snap s)
        {
            string stem = $"frame_{savedFrames:00}_{s.segment}_r{s.serial}"; savedFrames++;
            File.WriteAllBytes(Path.Combine(outDir, stem + ".png"), s.frame.EncodeToPNG());
            var sb = new StringBuilder($"{{\"serial\":{s.serial},\"segment\":\"{s.segment}\",\"S\":{s.S},\"ids\":\"{stem}_ids.bin\",\"dets\":[");
            for (int i = 0; i < s.dets.Count; i++)
            {
                var d = s.dets[i]; if (i > 0) sb.Append(',');
                sb.Append($"{{\"cls\":\"{d.cls}\",\"conf\":{d.conf.ToString("F3", CI)},\"x0\":{d.box.xMin.ToString("F4", CI)},\"y0\":{d.box.yMin.ToString("F4", CI)},\"x1\":{d.box.xMax.ToString("F4", CI)},\"y1\":{d.box.yMax.ToString("F4", CI)},\"id\":{d.frameIndex},\"cat\":\"{(d.hasAi ? d.ai.ToString() : "unmapped")}\",\"src\":\"{d.src}\",\"maskPx\":{d.maskPx},\"gt\":\"{(d.gtKnown ? d.gt.ToString() : "none")}\"}}");
            }
            File.WriteAllText(Path.Combine(outDir, stem + ".json"), sb + "]}");
            if (s.ids != null) File.WriteAllBytes(Path.Combine(outDir, stem + "_ids.bin"), s.ids);
            Destroy(s.frame); s.frame = null;
        }

        // ------------------------------------------------------------------ report
        void Report()
        {
            // item 1 + 2: capture and processing while moving
            float maxDisp = 0, minYaw = 360, maxYaw = 0; var distinct = new HashSet<string>();
            foreach (var s in snaps) { distinct.Add($"{s.pos.x:F1},{s.pos.z:F1},{s.yaw:F0}"); maxDisp = Mathf.Max(maxDisp, Vector3.Distance(s.pos, snaps[0].pos)); }
            float yawSpan = 0; { var ys = new List<float>(); foreach (var s in bySegment.TryGetValue("turn_in_place_360", out var t360) ? t360 : new List<Snap>()) ys.Add(s.yaw); if (ys.Count > 0) { float acc = 0; for (int i = 1; i < ys.Count; i++) acc += Mathf.Abs(Mathf.DeltaAngle(ys[i - 1], ys[i])); yawSpan = acc; } }
            Check("1+2: frames captured and processed by RF-DETR throughout the route", snaps.Count >= 30, $"{snaps.Count} results published; {distinct.Count} distinct capture poses; max displacement {maxDisp:0.0} m; yaw swept {yawSpan:0}° during the 360° turn; interval {pipe.InferenceIntervalMeasured:0.00}s; inference wall {(pipe.LatestResult != null ? pipe.LatestResult.Timings.InferenceWallMs : 0):0} ms");
            foreach (var kv in bySegment) Log($"segment {kv.Key}: {kv.Value.Count} results, mean detections {Mean(kv.Value, s => s.dets.Count):0.0}, results with >=1 detection {Frac(kv.Value, s => s.dets.Count > 0):P0}, mean AI-fade {Mean(kv.Value, s => s.aiFade):0.00}");
            foreach (var kv in bySegment) Check($"7: pipeline keeps publishing during '{kv.Key}'", kv.Value.Count >= 3, $"{kv.Value.Count} results");

            // item 3: boxes, labels, confidence, masks
            int nd = 0, okBox = 0, okLabel = 0, okConf = 0, okMask = 0; foreach (var s in snaps) foreach (var d in s.dets) { nd++; if (d.box.width > 0 && d.box.height > 0 && d.box.xMin >= -0.01f && d.box.xMax <= 1.01f && d.box.yMin >= -0.01f && d.box.yMax <= 1.01f) okBox++; if (!string.IsNullOrEmpty(d.cls)) okLabel++; if (d.conf >= pipe.confidenceThreshold && d.conf <= 1f) okConf++; if (d.maskCells > 0 && d.maskPx > 0) okMask++; perClass.TryGetValue(d.cls, out var cs); if (cs == null) perClass[d.cls] = cs = new ClassStat(); }
            Check("3: detections carry box, label, confidence and a non-empty instance mask", nd > 0 && okBox == nd && okLabel == nd && okConf == nd && okMask >= nd * 0.9, $"{nd} detections: valid box {okBox}, label {okLabel}, confidence {okConf}, mask {okMask}");

            // per class
            foreach (var s in snaps) foreach (var d in s.dets)
            {
                var cs = perClass[d.cls]; cs.n++; cs.sumConf += d.conf; cs.sumPx += d.maskPx; cs.minConf = Mathf.Min(cs.minConf, d.conf);
                if (s.scored) { if (!d.gtKnown) cs.untagged++; else if (d.hasAi && d.gt == d.ai) cs.agree++; else cs.disagree++; }
            }
            var sbc = new StringBuilder("\n  class                 n   meanConf  meanMaskCells  category        gt-agree gt-disagree untagged-region\n");
            foreach (var kv in perClass) { var c = kv.Value; bool m = pipe.cocoMap.TryGetCategory(CocoIdOf(kv.Key), out var cat); sbc.AppendLine($"  {kv.Key,-18} {c.n,4}   {c.sumConf / c.n,6:0.00}   {c.sumPx / c.n,10:0}    {(m ? cat.ToString() : "(unmapped)"),-14} {c.agree,6} {c.disagree,10} {c.untagged,12}"); }
            Log("AI-recognised classes (every detection below came from the model; no tag involved)" + sbc);
            int manualSrc = 0, aiSrc = 0, noneSrc = 0; foreach (var s in snaps) foreach (var d in s.dets) { if (d.src == CategorySource.ManualTag) manualSrc++; else if (d.src == CategorySource.AI) aiSrc++; else noneSrc++; }
            Check("PURE-AI sanity: no object took its category from a manual tag", manualSrc == 0, $"AI-categorised {aiSrc}, unmapped class (no category) {noneSrc}, from tag {manualSrc}");

            // item 4: categories
            Check("4: COCO->semantic mapping table matches the class table", pipe.cocoMap != null && pipe.cocoMap.Validate() == null);
            var sbk = new StringBuilder("\n  category      AI-mappable  frames-visible  frames-with-AI-det  tag-cells-visible  covered-by-correct-AI-mask  covered-by-any-AI-mask\n");
            for (int c = 0; c < NCat; c++)
            {
                bool mappable = false; foreach (int id in cats) { pipe.cocoMap.TryGetCategory(id, out var cc); if ((int)cc == c) mappable = true; }
                var cs = perCat[c]; sbk.AppendLine($"  {(SemanticCategory)c,-12} {(mappable ? "yes" : "NO (fallback)"),-12} {cs.framesVisible,10} {cs.framesDetected,15} {cs.vis,18} {(cs.vis > 0 ? (cs.cov / (float)cs.vis) : 0f),22:P0} {(cs.vis > 0 ? (cs.covAny / (float)cs.vis) : 0f),22:P0}");
            }
            Log("Per-category ground truth vs AI (ground truth = the scene's SemanticTags, used only for scoring)" + sbk);
        }

        static int CocoIdOf(string name) { for (int i = 0; i < 91; i++) if (CocoClasses.NameOf(i) == name) return i; return -1; }
        static float Mean(List<Snap> l, Func<Snap, float> f) { float s = 0; foreach (var x in l) s += f(x); return l.Count == 0 ? 0 : s / l.Count; }
        static float Frac(List<Snap> l, Func<Snap, bool> f) { int n = 0; foreach (var x in l) if (f(x)) n++; return l.Count == 0 ? 0 : n / (float)l.Count; }

        static float[] Weights(SemanticImportanceConfig c) { var w = new List<float>(); for (int p = 0; p < 4; p++) for (int k = 0; k < NCat; k++) w.Add(c.GetWeight((ImportanceProfileType)p, (SemanticCategory)k)); return w.ToArray(); }
        static bool Same(float[] a, float[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (!Mathf.Approximately(a[i], b[i])) return false; return true; }

        // ------------------------------------------------------------------ items 5 + 6
        IEnumerator ImportanceAndColour(ColourProcessor proc, ColourAssistView view)
        {
            Vector3 A = new Vector3(-6.5f, 0.15f, -11f); float yawA = Quaternion.LookRotation(new Vector3(6.5f, 0, 11f)).eulerAngles.y;
            pc.Teleport(A, yawA); segment = "importance_test"; input.Move = Vector2.zero; input.Look = Vector2.zero;
            view.View = AssistViewMode.Corrected; view.Compare = false; layer.prioritise = true; layer.focus = 1f; layer.useAiMasks = true;
            // 5: user-set weights are what AI detections report
            var imp = pipe.Importance; imp.ActiveProfile = ImportanceProfileType.RoadSafety;
            float tl0 = imp.GetWeight(SemanticCategory.TrafficLight), ve0 = imp.GetWeight(SemanticCategory.Vehicle);
            imp.SetWeight(SemanticCategory.TrafficLight, 0.5f); imp.SetWeight(SemanticCategory.Vehicle, 4.5f);
            yield return NewResults(3, 40f);
            int seenAi = 0, bad = 0; var sb = new StringBuilder();
            foreach (var o in pipe.LatestObjects)
            {
                if (o.Source != CategorySource.AI) continue; seenAi++;
                float expect = imp.GetWeight(o.FinalCategory); if (!Mathf.Approximately(o.Importance, expect)) { bad++; sb.Append($"[{o.Detection.ClassName} {o.FinalCategory} reported {o.Importance} expected {expect}] "); }
                if (o.FinalCategory == SemanticCategory.TrafficLight && !Mathf.Approximately(o.Importance, 0.5f)) bad++;
                if (o.FinalCategory == SemanticCategory.Vehicle && !Mathf.Approximately(o.Importance, 4.5f)) bad++;
            }
            Check("5: AI detections report the USER-configured importance (custom values 0.5 / 4.5 applied, not overwritten by AI)", seenAi > 0 && bad == 0, $"{seenAi} AI objects checked, {bad} mismatches {sb}; weights now TrafficLight {imp.GetWeight(SemanticCategory.TrafficLight)}, Vehicle {imp.GetWeight(SemanticCategory.Vehicle)}");
            imp.SetWeight(SemanticCategory.TrafficLight, tl0); imp.SetWeight(SemanticCategory.Vehicle, ve0);

            // 6: colour correction responds to AI-detected objects and their importance. Remove every SemanticTag from the scene (play mode only)
            // and rebuild the assist layer's tag buffer empty, so the only weight source left for detected objects is the AI instance mask.
            foreach (var t in FindObjectsByType<SemanticTag>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Destroy(t);
            yield return null;
            var f = typeof(SemanticAssistLayer).GetField("tags", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) { (f.GetValue(layer) as ManualSemanticBuffer)?.Dispose(); f.SetValue(layer, null); }
            Check("6: scene has no SemanticTag left; assist layer tag buffer rebuilt empty", FindObjectsByType<SemanticTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0 && f != null);
            yield return NewResults(2, 40f);
            // pick the largest AI object with a category
            DetectedObject target = null; foreach (var o in pipe.LatestObjects) if (o.HasAiCategory && (target == null || o.Detection.MaskCells > target.Detection.MaskCells)) target = o;
            if (target == null) { Check("6: an AI-detected object is in view for the colour test", false, "no detection at the test pose"); yield break; }
            var cat = target.AiCategory; string cname = target.Detection.ClassName; int fi = target.Detection.FrameIndex;
            float d5 = 0, d0 = 0; var res = pipe.LatestResult;
            foreach (float w in new[] { 5f, 0f })
            {
                imp.SetWeight(cat, w); yield return NewResults(2, 40f); yield return null; yield return null;
                var o2 = FindObj(cat); if (o2 == null) { Log($"colour test: no {cat} object in view at weight {w}"); continue; }
                float diff = RegionDiff(proc, pipe.LatestResult, o2.Detection.FrameIndex);
                Log($"colour test: AI object '{o2.Detection.ClassName}' ({cat}) at weight {w}: reported importance {o2.Importance}, mean |corrected-original| inside its AI mask = {diff:0.00}/255, aiFade {layer.AiFade:0.00}");
                if (w == 5f) d5 = diff; else d0 = diff;
            }
            Check($"6: correction inside the AI mask of '{cname}' ({cat}) grows with importance (tags removed, AI masks only)", d5 > d0 + 0.5f, $"weight 5 -> {d5:0.00}, weight 0 -> {d0:0.00}");
            layer.useAiMasks = false; yield return null; yield return null; yield return null;
            var o3 = FindObj(cat);
            float dOff = o3 != null ? RegionDiff(proc, pipe.LatestResult, o3.Detection.FrameIndex) : -1f;
            imp.SetWeight(cat, 5f); layer.useAiMasks = true; yield return NewResults(2, 40f); yield return null; yield return null;
            var o4 = FindObj(cat); float dOn = o4 != null ? RegionDiff(proc, pipe.LatestResult, o4.Detection.FrameIndex) : -1f;
            Check("6: switching the AI masks off removes the AI-driven correction (tags are gone, so nothing else drives it)", dOn > dOff + 0.5f, $"AI masks on {dOn:0.00}, off {dOff:0.00}");
        }

        DetectedObject FindObj(SemanticCategory cat) { DetectedObject b = null; foreach (var o in pipe.LatestObjects) if (o.HasAiCategory && o.AiCategory == cat && (b == null || o.Detection.MaskCells > b.Detection.MaskCells)) b = o; return b; }

        IEnumerator NewResults(int n, float timeout) { int s0 = pipe.CompletedInferences; float t = 0; while (pipe.CompletedInferences < s0 + n && t < timeout) { t += Time.unscaledDeltaTime; yield return null; } }

        float RegionDiff(ColourProcessor proc, PerceptionResult r, int frameIndex)
        {
            var a = ReadFrame(proc.OriginalView); var b = ReadFrame(proc.CorrectedView); if (a == null || b == null || r.InstanceIds == null) return -1f;
            var pa = a.GetPixels32(); var pb = b.GetPixels32(); int S = r.IdSize; double sum = 0; int n = 0;
            for (int y = 0; y < a.height; y++) for (int x = 0; x < a.width; x++)
                {
                    int ix = Mathf.Min(S - 1, x * S / a.width), iy = Mathf.Min(S - 1, (a.height - 1 - y) * S / a.height);   // ReadPixels is bottom-up, the id map top-down
                    if (r.InstanceIds[iy * S + ix] != frameIndex) continue;
                    var p = pa[y * a.width + x]; var q = pb[y * a.width + x]; sum += (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b)) / 3.0; n++;
                }
            Destroy(a); Destroy(b); return n == 0 ? -1f : (float)(sum / n);
        }

        void Finish()
        {
            Log($"DONE all_pass={allPass}");
            File.WriteAllText(Path.Combine(outDir, "ai_verification_report.txt"), string.Join("\n", lines));
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(allPass ? 0 : 1);
#else
            Application.Quit(allPass ? 0 : 1);
#endif
        }
    }
}
