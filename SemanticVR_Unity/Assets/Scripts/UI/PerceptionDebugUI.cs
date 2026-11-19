using System.Collections.Generic;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Semantics;
using UnityEngine;

namespace SemanticVR.UI
{
    /// <summary>
    /// IMGUI debug interface for the perception system (no extra packages): detections, masks, categories, importance
    /// weights, profile selector, timings and backend info. Edits go to the pipeline's runtime importance copy only.
    /// </summary>
    [RequireComponent(typeof(PerceptionPipeline))]
    public sealed class PerceptionDebugUI : MonoBehaviour
    {
        public bool showDetections = true, showMasks = true, showInfo = true, showImportanceEditor = true;
        [Range(0.2f, 0.9f)] public float maskAlpha = 0.45f;

        PerceptionPipeline pipe;
        Texture2D overlay, white;
        int overlaySerial = -1;
        Color32[] overlayPixels;
        float smoothDt = 0.016f;
        Vector2 listScroll;
        GUIStyle label, shadow, box;

        void Awake() { pipe = GetComponent<PerceptionPipeline>(); white = Texture2D.whiteTexture; }

        void Update() { smoothDt = Mathf.Lerp(smoothDt, Time.unscaledDeltaTime, 0.05f); }

        void OnDestroy() { if (overlay != null) Destroy(overlay); }

        Rect ImageRect()
        {
            if (pipe.sourceMode == FrameSourceMode.StaticPhoto && pipe.staticPhoto != null)
            {
                float ia = (float)pipe.staticPhoto.width / pipe.staticPhoto.height, sa = (float)Screen.width / Screen.height;
                float w = ia > sa ? Screen.width : Screen.height * ia, h = ia > sa ? Screen.width / ia : Screen.height;
                return new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            }
            return new Rect(0, 0, Screen.width, Screen.height);
        }

        void BuildOverlay(PerceptionResult res, IReadOnlyList<DetectedObject> objs)
        {
            int S = res.IdSize;
            if (overlay == null || overlay.width != S) { if (overlay != null) Destroy(overlay); overlay = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point }; overlayPixels = new Color32[S * S]; }
            var pal = new Color32[256];
            foreach (var o in objs)
            {
                var c = o.Source == CategorySource.None ? CategoryPalette.Unclassified : CategoryPalette.Of(o.FinalCategory);
                pal[o.Detection.FrameIndex] = new Color(c.r, c.g, c.b, maskAlpha);
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    overlayPixels[(S - 1 - y) * S + x] = pal[res.InstanceIds[y * S + x]];
            overlay.SetPixels32(overlayPixels); overlay.Apply(false);
            overlaySerial = res.Serial;
        }

        void OnGUI()
        {
            if (pipe == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                shadow = new GUIStyle(label) { normal = { textColor = Color.black } };
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
            }
            var res = pipe.LatestResult; var objs = pipe.LatestObjects;
            var ir = ImageRect();
            if (pipe.sourceMode == FrameSourceMode.StaticPhoto && pipe.staticPhoto != null) GUI.DrawTexture(ir, pipe.staticPhoto, ScaleMode.StretchToFill, false);

            if (res != null && pipe.Status == PipelineStatus.Ready)
            {
                if (showMasks)
                {
                    if (overlaySerial != res.Serial) BuildOverlay(res, objs);
                    GUI.DrawTexture(ir, overlay, ScaleMode.StretchToFill, true);
                }
                if (showDetections) DrawDetections(ir, objs);
            }

            float s = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float vw = Screen.width / s, vh = Screen.height / s;
            DrawControlPanel(vh);
            if (showImportanceEditor) DrawImportancePanel(vw);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawDetections(Rect ir, IReadOnlyList<DetectedObject> objs)
        {
            foreach (var o in objs)
            {
                var d = o.Detection; var col = o.Source == CategorySource.None ? CategoryPalette.Unclassified : CategoryPalette.Of(o.FinalCategory);
                var r = new Rect(ir.x + d.Box.xMin * ir.width, ir.y + d.Box.yMin * ir.height, d.Box.width * ir.width, d.Box.height * ir.height);
                var old = GUI.color; GUI.color = col;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), white); GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), white);
                GUI.DrawTexture(new Rect(r.x, r.y, 2, r.height), white); GUI.DrawTexture(new Rect(r.xMax - 2, r.y, 2, r.height), white);
                GUI.color = old;
                string cat = o.Source == CategorySource.None ? "unclassified" : o.FinalCategory + (o.Source == CategorySource.ManualTag ? " (tag)" : " (AI)");
                string text = $"{d.ClassName} {d.Confidence:0.00} | {cat} | w={o.Importance:0.#}" + (o.Disagreement ? " !" : "");
                var lr = new Rect(r.x + 2, Mathf.Max(ir.y, r.y - 17), 420, 18);
                GUI.Label(new Rect(lr.x + 1, lr.y + 1, lr.width, lr.height), text, shadow); GUI.Label(lr, text, label);
            }
        }

        void DrawControlPanel(float vh)
        {
            GUILayout.BeginArea(new Rect(8, 8, 430, vh - 16), box);
            GUILayout.Label($"<b>SemanticVR perception</b>  status: <b>{pipe.Status}</b>");
            if (pipe.Status != PipelineStatus.Ready) GUILayout.Label(pipe.StatusMessage);
            GUILayout.Label($"backend: {pipe.BackendDescription} (requested {pipe.device})");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(pipe.sourceMode == FrameSourceMode.GameCamera, "Game camera", GUI.skin.button)) { if (pipe.sourceMode != FrameSourceMode.GameCamera) pipe.SetSourceMode(FrameSourceMode.GameCamera); }
            if (GUILayout.Toggle(pipe.sourceMode == FrameSourceMode.StaticPhoto, "Street photo", GUI.skin.button)) { if (pipe.sourceMode != FrameSourceMode.StaticPhoto && pipe.staticPhoto != null) pipe.SetSourceMode(FrameSourceMode.StaticPhoto); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            showDetections = GUILayout.Toggle(showDetections, "Detections");
            showMasks = GUILayout.Toggle(showMasks, "Masks");
            showInfo = GUILayout.Toggle(showInfo, "Info");
            showImportanceEditor = GUILayout.Toggle(showImportanceEditor, "Weights");
            GUILayout.EndHorizontal();
            GUILayout.Label($"confidence threshold {pipe.confidenceThreshold:0.00}");
            pipe.confidenceThreshold = GUILayout.HorizontalSlider(pipe.confidenceThreshold, 0.1f, 0.9f);
            GUILayout.Label($"inference interval {pipe.inferenceIntervalSeconds:0.00} s");
            pipe.inferenceIntervalSeconds = GUILayout.HorizontalSlider(pipe.inferenceIntervalSeconds, 0f, 3f);

            var res = pipe.LatestResult;
            if (showInfo)
            {
                GUILayout.Space(4);
                GUILayout.Label($"frame {smoothDt * 1000f:0.0} ms ({1f / smoothDt:0} fps)  | inferences done {pipe.CompletedInferences}" + (pipe.IsInferenceRunning ? "  <b>[running]</b>" : ""));
                if (res != null)
                {
                    var t = res.Timings;
                    GUILayout.Label($"inference wall {t.InferenceWallMs:0} ms (main-thread {t.InferenceMainMs:0.0} ms, worst step {t.MaxStepMs:0.0} ms)\n" +
                                    $"capture {t.CaptureMs:0.0} | preprocess {t.PreprocessMs:0.0} | readback {t.ReadbackMs:0.0}\n" +
                                    $"parse {t.ParseMs:0.00} | masks {t.MaskMs:0.00} | semantics {t.SemanticMs:0.00} ms\n" +
                                    $"result age {Time.realtimeSinceStartup - res.PublishedAt:0.0} s | detections {res.Detections.Count}");
                }
                if (pipe.LastValidation.Passed) GUILayout.Label("golden validation: <color=#6f6>passed</color> (mask layout " + pipe.LastValidation.ConfiguredLayout + ")");
            }
            GUILayout.Space(4);
            GUILayout.Label("<b>Detections</b>  (class conf | category | weight; ! = AI/manual disagree)");
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var o in pipe.LatestObjects)
            {
                string ai = o.HasAiCategory ? o.AiCategory.ToString() : "-";
                string man = o.HasManualCategory ? o.ManualCategory + $" {o.ManualShare:P0}" : "-";
                string fin = o.Source == CategorySource.None ? "none" : o.FinalCategory + (o.Source == CategorySource.AI ? " [AI]" : " [tag]");
                GUILayout.Label($"#{o.Detection.UniqueId} <b>{o.Detection.ClassName}</b> {o.Detection.Confidence:0.00}  box {o.Detection.Box.xMin:0.00},{o.Detection.Box.yMin:0.00} {o.Detection.Box.width:0.00}x{o.Detection.Box.height:0.00}\n   AI {ai} | manual {man} | -> {fin} | w {o.Importance:0.#}" + (o.Disagreement ? "  <color=#fa4>DISAGREE</color>" : ""));
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawImportancePanel(float vw)
        {
            var imp = pipe.Importance; if (imp == null) return;
            const float W = 300;
            int n = SemanticImportanceConfig.CategoryCount;
            GUILayout.BeginArea(new Rect(vw - W - 8, 8, W, 60 + n * 28 + 34), box);
            GUILayout.Label("<b>Semantic importance (user-set, 0-5)</b>");
            var names = new[] { "Road Safety", "Navigation", "General", "Custom" };
            int sel = GUILayout.Toolbar((int)imp.ActiveProfile, names);
            if (sel != (int)imp.ActiveProfile) imp.ActiveProfile = (ImportanceProfileType)sel;
            for (int i = 0; i < n; i++)
            {
                var c = (SemanticCategory)i;
                GUILayout.BeginHorizontal();
                GUILayout.Label(c.ToString(), GUILayout.Width(100));
                float w = GUILayout.HorizontalSlider(imp.GetWeight(c), 0, 5, GUILayout.Width(140));
                w = Mathf.Round(w * 2f) / 2f;
                if (!Mathf.Approximately(w, imp.GetWeight(c))) imp.SetWeight(c, w);
                GUILayout.Label(imp.GetWeight(c).ToString("0.0"), GUILayout.Width(30));
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset profile")) imp.ResetProfile(imp.ActiveProfile);
            if (GUILayout.Button("Copy to Custom") && imp.ActiveProfile != ImportanceProfileType.Custom) imp.CopyProfile(imp.ActiveProfile, ImportanceProfileType.Custom);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
