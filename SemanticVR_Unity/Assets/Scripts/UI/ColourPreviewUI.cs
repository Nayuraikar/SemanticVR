using SemanticVR.Accessibility;
using SemanticVR.ColourProcessing;
using UnityEngine;

namespace SemanticVR.UI
{
    /// <summary>
    /// IMGUI demo interface for the baseline colour processing: Original / CVD-simulated / Corrected previews, CVD profile
    /// selector, severity and correction-strength sliders, correction-mode selector, reset, JSON save/load and a
    /// distinguishability read-out for the user's troublesome colour pairs. All edits go to runtime copies.
    /// </summary>
    [RequireComponent(typeof(ColourProcessor))]
    public sealed class ColourPreviewUI : MonoBehaviour
    {
        public enum FullView { None, Original, Simulated, Corrected }
        public bool visible = true;
        public FullView fullView = FullView.None;

        ColourProcessor proc;
        GUIStyle box, label;
        static readonly string[] TypeNames = { "Normal", "Protanopia", "Protanomaly", "Deuteranopia", "Deuteranomaly", "Tritanopia", "Tritanomaly", "Achromatopsia", "Custom" };
        static readonly string[] ModeNames = { "Balanced", "Max distinguish.", "Natural", "High contrast", "Custom" };
        string status = "";

        void Awake() { proc = GetComponent<ColourProcessor>(); }

        string ProfilePath => System.IO.Path.Combine(Application.persistentDataPath, "cvd_profile.json");
        string SettingsPath => System.IO.Path.Combine(Application.persistentDataPath, "correction_settings.json");

        void OnGUI()
        {
            if (proc == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            }
            proc.active = visible;
            if (visible && fullView != FullView.None)
            {
                GUI.depth = 10;
                var tex = fullView == FullView.Original ? proc.OriginalView : fullView == FullView.Simulated ? proc.SimulatedView : proc.CorrectedView;
                if (tex != null) GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex, ScaleMode.StretchToFill, false);
                GUI.depth = 0;
            }
            float s = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float vw = Screen.width / s, vh = Screen.height / s;
            if (!visible) { if (GUI.Button(new Rect(vw - 130, vh - 34, 122, 26), "Colour panel")) visible = true; GUI.matrix = Matrix4x4.identity; return; }
            DrawPreviews(vw, vh);
            DrawControls(vw, vh);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawPreviews(float vw, float vh)
        {
            const float W = 240, H = 135;
            float x = 446, y = vh - H - 34;
            string[] names = { "Original", "CVD simulated", proc.correctedAsPerceived ? "Corrected (as perceived)" : "Corrected" };
            Texture[] texs = { proc.OriginalView, proc.SimulatedView, proc.CorrectedView };
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(x + i * (W + 8), y, W, H);
                if (texs[i] != null) GUI.DrawTexture(r, texs[i], ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(r.x + 4, r.yMax + 2, W, 20), names[i], label);
            }
        }

        void DrawControls(float vw, float vh)
        {
            var p = proc.Profile; var st = proc.Settings;
            const float W = 300;
            GUILayout.BeginArea(new Rect(vw - W - 8, 392, W, vh - 400), box);
            GUILayout.Label("<b>Colour vision profile</b> (baseline processing)");
            int t = GUILayout.SelectionGrid((int)p.type, TypeNames, 3);
            if (t != (int)p.type) p.type = (CvdType)t;
            bool sevUsed = !(p.type == CvdType.Normal || p.type == CvdType.Protanopia || p.type == CvdType.Deuteranopia || p.type == CvdType.Tritanopia);
            GUI.enabled = sevUsed;
            GUILayout.Label($"severity {p.severityPercent:0}%" + (sevUsed ? "" : "  (fixed by type)"));
            p.severityPercent = GUILayout.HorizontalSlider(p.severityPercent, 0, 100);
            GUI.enabled = true;
            GUILayout.Label($"correction strength {st.strength * 100f:0}%");
            st.strength = GUILayout.HorizontalSlider(st.strength, 0, 1);
            GUILayout.Label("correction mode");
            int m = GUILayout.SelectionGrid((int)st.mode, ModeNames, 3);
            if (m != (int)st.mode) st.mode = (CorrectionMode)m;
            proc.correctedAsPerceived = GUILayout.Toggle(proc.correctedAsPerceived, "Corrected pane shows CVD-perceived result");
            GUILayout.BeginHorizontal();
            GUILayout.Label("full view:", GUILayout.Width(60));
            fullView = (FullView)GUILayout.Toolbar((int)fullView, new[] { "off", "orig", "sim", "corr" });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset")) { proc.ResetToAssets(); status = "reset"; }
            if (GUILayout.Button("Save")) { p.SaveToFile(ProfilePath); st.SaveToFile(SettingsPath); status = "saved to " + Application.persistentDataPath; }
            if (GUILayout.Button("Load")) { status = p.LoadFromFile(ProfilePath) & st.LoadFromFile(SettingsPath) ? "loaded" : "no saved profile"; }
            if (GUILayout.Button("Hide")) visible = false;
            GUILayout.EndHorizontal();
            if (status.Length > 0) GUILayout.Label(status);

            var tr = proc.Current;
            if (!tr.Valid && tr.Error != null) GUILayout.Label("<color=#f66>invalid: " + tr.Error + "</color>");
            else
            {
                GUILayout.Label($"<b>Troublesome pairs</b> perceived ΔE76 (uncorrected → corrected)");
                foreach (var pair in st.troublesomePairs)
                {
                    float before = ColourMetrics.PerceivedDeltaE(pair.a, pair.b, tr.Simulation, Matrix3x3.Identity);
                    float after = ColourMetrics.PerceivedDeltaE(pair.a, pair.b, tr.Simulation, tr.Correction);
                    GUILayout.Label($"{pair.label}: {before:0.0} → {after:0.0}");
                }
                GUILayout.Label($"matrix {proc.LastMatrixMs:0.000} ms | 3 views {proc.LastProcessMs:0.0} ms CPU");
                GUILayout.Label("provider: " + tr.ProviderName);
            }
            GUILayout.EndArea();
        }
    }
}
