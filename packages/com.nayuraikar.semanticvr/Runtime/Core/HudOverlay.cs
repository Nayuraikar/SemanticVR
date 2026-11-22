using SemanticVR.Confusion;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>Small IMGUI HUD: view mode, CVD type, severity, stereo/mono, frame time and FPS.</summary>
    [DisallowMultipleComponent]
    public class HudOverlay : MonoBehaviour
    {
        public DebugViewController controller;
        [Tooltip("Optional: pair-graph gizmos (G toggles). Found automatically.")] public SemanticGraphGizmos graph;
        public bool visible = true;
        [Tooltip("Press H to hide/show.")] public bool allowToggle = true;

        float _smoothedDt = 1f / 60f;
        Texture2D _panel, _white;
        GUIStyle _title, _label, _value, _hint;
        int _styleHeight = -1;

        static readonly Color Bg = new Color(0.07f, 0.08f, 0.10f, 0.80f);
        static readonly Color Accent = new Color(0.36f, 0.78f, 0.72f, 1f);
        static readonly Color Text = new Color(0.93f, 0.94f, 0.95f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.66f, 0.70f, 1f);

        void OnEnable()
        {
            if (controller == null) controller = GetComponent<DebugViewController>();
            if (controller == null) controller = FindFirstObjectByType<DebugViewController>();
            if (graph == null) graph = FindFirstObjectByType<SemanticGraphGizmos>();
        }

        void Update()
        {
            _smoothedDt = Mathf.Lerp(_smoothedDt, Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (allowToggle && UnityEngine.InputSystem.Keyboard.current != null &&
                UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame) visible = !visible;
        }

        void OnDestroy()
        {
            if (_panel != null) Destroy(_panel);
            if (_white != null) Destroy(_white);
        }

        static Texture2D MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + 0.5f)));
                }
            t.Apply();
            return t;
        }

        void BuildStyles(float scale)
        {
            if (_panel == null) _panel = MakeRounded(32, 10);
            if (_white == null) { _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            int fs(int s) => Mathf.RoundToInt(s * scale);
            GUIStyle Mk(int size, FontStyle fs_, Color c, TextAnchor a)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = fs(size), fontStyle = fs_, alignment = a, wordWrap = false };
                s.normal.textColor = c;
                return s;
            }
            _title = Mk(15, FontStyle.Bold, Accent, TextAnchor.MiddleLeft);
            _label = Mk(13, FontStyle.Normal, Dim, TextAnchor.MiddleLeft);
            _value = Mk(13, FontStyle.Bold, Text, TextAnchor.MiddleRight);
            _hint = Mk(11, FontStyle.Normal, Dim, TextAnchor.MiddleLeft);
            _styleHeight = Screen.height;
        }

        void OnGUI()
        {
            if (!visible || controller == null || Event.current.type != EventType.Repaint) return;
            float scale = Mathf.Clamp(Screen.height / 900f, 1f, 2.5f);
            if (_styleHeight != Screen.height || _title == null) BuildStyles(scale);

            float pad = 14 * scale, row = 22 * scale, w = 350 * scale;
            float h = pad * 2 + row * (graph != null ? 8 : 7) + 6 * scale;
            var r = new Rect(16 * scale, 16 * scale, w, h);

            GUI.color = Bg;
            GUI.DrawTexture(r, _panel, ScaleMode.StretchToFill, true);
            GUI.color = Color.white;

            float x = r.x + pad, y = r.y + pad, iw = w - pad * 2;
            GUI.Label(new Rect(x, y, iw, row), "SemanticVR  ·  Phase 0", _title); y += row + 4 * scale;
            Row(ref y, x, iw, row, "View", controller.Mode.ToString());
            Row(ref y, x, iw, row, "CVD type", controller.Type.ToString());
            Row(ref y, x, iw, row, "Severity", controller.Severity.ToString("0.00"));
            // severity bar
            var bar = new Rect(x, y, iw, 4 * scale);
            GUI.color = new Color(1, 1, 1, 0.12f); GUI.DrawTexture(bar, _white);
            GUI.color = Accent; GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * controller.Severity, bar.height), _white);
            GUI.color = Color.white; y += 10 * scale;
            Row(ref y, x, iw, row, "Display", controller.Stereo ? "Stereo (SBS)" : "Mono");
            if (graph != null)
                Row(ref y, x, iw, row, "Pair graph (G)", graph.showEdges ? $"On · {graph.ConfusableCount} confusable" : "Off");
            Row(ref y, x, iw, row, "Frame", $"{_smoothedDt * 1000f:0.0} ms   {1f / _smoothedDt:0} fps");
            GUI.Label(new Rect(x, y, iw, row), "1/2/3 view · P/D/T type · [ ] severity · Tab stereo · G graph", _hint);
        }

        void Row(ref float y, float x, float w, float h, string label, string value)
        {
            GUI.Label(new Rect(x, y, w, h), label, _label);
            GUI.Label(new Rect(x, y, w, h), value, _value);
            y += h;
        }
    }
}
