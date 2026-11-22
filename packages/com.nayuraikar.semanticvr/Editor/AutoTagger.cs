using System;
using System.Collections.Generic;
using System.Linq;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEditor;
using UnityEngine;

namespace SemanticVR.Editor
{
    /// <summary>One proposed tagging of a renderer.</summary>
    public class TagSuggestion
    {
        public Renderer renderer;
        public SemanticClass cls;
        public int state;
        public string reason;
        public float confidence;
        public bool accept = true;
    }

    /// <summary>Suggests semantic classes (and states) from object names and material colours; applies after confirmation.</summary>
    public static class AutoTagger
    {
        // keyword tokens (lowercase) -> default class ID. First match wins, so specific rules come first.
        static readonly (int classId, string[] tokens)[] NameRules =
        {
            (DefaultClassLibrary.RailSignal, new[] { "rail", "railway", "train", "semaphore" }),
            (DefaultClassLibrary.StatusPanel, new[] { "status", "board", "departure", "arrival" }),
            (DefaultClassLibrary.HazardLight, new[] { "hazard", "beacon", "flasher", "warninglight", "crossing" }),
            (DefaultClassLibrary.TrafficSignal, new[] { "signal", "traffic", "stoplight", "lamp", "lens" }),
            (DefaultClassLibrary.RoadSign, new[] { "sign", "signpost", "yield" }),
            (DefaultClassLibrary.Vehicle, new[] { "car", "vehicle", "truck", "bus", "van", "taxi" }),
            (DefaultClassLibrary.Berry, new[] { "berry", "berries", "cherry" }),
            (DefaultClassLibrary.Fruit, new[] { "fruit", "apple", "tomato", "orange", "peach", "plum" }),
            (DefaultClassLibrary.Foliage, new[] { "foliage", "leaf", "leaves", "bush", "hedge", "canopy", "tree" }),
            (DefaultClassLibrary.Grass, new[] { "grass", "lawn", "turf", "meadow" }),
            (DefaultClassLibrary.Sky, new[] { "sky", "skydome", "heaven" }),
            (DefaultClassLibrary.Skin, new[] { "skin", "face", "hand", "head" }),
        };

        public static List<string> Tokenize(string name)
        {
            var tokens = new List<string>(); var cur = new System.Text.StringBuilder();
            void Flush() { if (cur.Length > 0) { tokens.Add(cur.ToString().ToLowerInvariant()); cur.Clear(); } }
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (!char.IsLetter(ch)) { Flush(); continue; }
                if (char.IsUpper(ch) && cur.Length > 0 && char.IsLower(name[i - 1])) Flush(); // camelCase split
                cur.Append(ch);
            }
            Flush();
            return tokens;
        }

        static bool TokenHit(List<string> tokens, string kw) =>
            tokens.Any(t => t == kw || (kw.Length >= 4 && t.StartsWith(kw, StringComparison.Ordinal)));

        public static Color MaterialColor(Renderer r)
        {
            var m = r != null ? r.sharedMaterial : null;
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>Index of the state whose albedo is closest (CIEDE2000) to <paramref name="c"/>, and that distance.</summary>
        public static int NearestState(SemanticClass cls, Color c, out float deltaE)
        {
            int best = 0; deltaE = float.MaxValue;
            if (cls == null) return 0;
            for (int i = 0; i < cls.StateCount; i++)
            {
                float d = SemanticGraph.DeltaE(c, cls.states[i].albedo);
                if (d < deltaE) { deltaE = d; best = i; }
            }
            return best;
        }

        /// <summary>Suggest a tagging for every untagged renderer under <paramref name="objects"/>.</summary>
        public static List<TagSuggestion> Suggest(IEnumerable<GameObject> objects, IReadOnlyList<SemanticClass> classes)
        {
            var result = new List<TagSuggestion>();
            var seen = new HashSet<Renderer>();
            foreach (var go in objects)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!seen.Add(r) || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    if (r.GetComponent<SemanticTag>() != null) continue;
                    var s = SuggestFor(r, classes);
                    if (s != null) result.Add(s);
                }
            return result;
        }

        static SemanticClass ClassOf(IReadOnlyList<SemanticClass> classes, int id) => classes?.FirstOrDefault(c => c != null && c.id == id);

        public static TagSuggestion SuggestFor(Renderer r, IReadOnlyList<SemanticClass> classes)
        {
            var tokens = Tokenize(r.gameObject.name);
            if (r.transform.parent != null) tokens.AddRange(Tokenize(r.transform.parent.name));
            Color col = MaterialColor(r);

            foreach (var (classId, kws) in NameRules)
            {
                var cls = ClassOf(classes, classId);
                if (cls == null) continue;
                foreach (var kw in kws)
                    if (TokenHit(tokens, kw))
                    {
                        int st = NearestState(cls, col, out float d);
                        return new TagSuggestion { renderer = r, cls = cls, state = st, confidence = 0.9f, reason = $"name contains '{kw}'; nearest state {cls.states[st].name} (ΔE00 {d:0.0})" };
                    }
            }

            // Colour only: closest state over all classes; accept only close matches, low confidence.
            SemanticClass bestC = null; int bestS = 0; float bestD = 12f;
            foreach (var cls in classes)
            {
                if (cls == null) continue;
                int st = NearestState(cls, col, out float d);
                if (d < bestD) { bestD = d; bestC = cls; bestS = st; }
            }
            if (bestC == null) return null;
            return new TagSuggestion { renderer = r, cls = bestC, state = bestS, confidence = Mathf.Clamp01(0.5f * (1f - bestD / 12f)), accept = bestD < 5f, reason = $"material colour close to {bestC.displayName}/{bestC.states[bestS].name} (ΔE00 {bestD:0.0})" };
        }

        /// <summary>Add tags for the accepted suggestions (undoable). Returns the number of tags created.</summary>
        public static int Apply(IEnumerable<TagSuggestion> suggestions)
        {
            int nextId = 1;
            foreach (var t in UnityEngine.Object.FindObjectsByType<SemanticTag>(FindObjectsInactive.Include, FindObjectsSortMode.None)) nextId = Mathf.Max(nextId, t.StableId + 1);
            int n = 0;
            foreach (var s in suggestions.Where(x => x.accept && x.renderer != null && x.cls != null))
            {
                var tag = s.renderer.GetComponent<SemanticTag>();
                if (tag == null) tag = Undo.AddComponent<SemanticTag>(s.renderer.gameObject);
                Undo.RecordObject(tag, "Auto-tag");
                tag.TargetRenderer = s.renderer;
                tag.StableId = nextId++;
                tag.Configure(s.cls, s.state);
                EditorUtility.SetDirty(tag);
                n++;
            }
            return n;
        }

        [MenuItem("SemanticVR/Auto-tag selected")]
        static void Menu()
        {
            var classes = DefaultClassLibrary.LoadAllProjectClasses();
            var list = Suggest(Selection.gameObjects, classes);
            if (list.Count == 0) { EditorUtility.DisplayDialog("SemanticVR Auto-tag", "No untagged renderers with a recognisable class in the selection.", "OK"); return; }
            AutoTaggerWindow.Open(list, classes);
        }
        [MenuItem("SemanticVR/Auto-tag selected", true)] static bool MenuValidate() => Selection.gameObjects.Length > 0;
    }

    /// <summary>Confirmation window: review, edit and accept/reject each suggestion before any tag is created.</summary>
    public class AutoTaggerWindow : EditorWindow
    {
        List<TagSuggestion> _items; List<SemanticClass> _classes; string[] _classNames; Vector2 _scroll;

        public static void Open(List<TagSuggestion> items, List<SemanticClass> classes)
        {
            var w = GetWindow<AutoTaggerWindow>(true, "SemanticVR Auto-tag", true);
            w._items = items; w._classes = classes; w._classNames = classes.Select(c => $"{c.id}  {c.displayName}").ToArray();
            w.minSize = new Vector2(720, 320);
            w.ShowUtility();
        }

        void OnGUI()
        {
            if (_items == null) { Close(); return; }
            EditorGUILayout.LabelField($"{_items.Count} suggestion(s). Untick to skip, or change class/state.", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var s in _items)
            {
                EditorGUILayout.BeginHorizontal();
                s.accept = EditorGUILayout.Toggle(s.accept, GUILayout.Width(18));
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(s.renderer, typeof(Renderer), true, GUILayout.Width(190));
                int ci = Mathf.Max(0, _classes.IndexOf(s.cls));
                int nci = EditorGUILayout.Popup(ci, _classNames, GUILayout.Width(170));
                if (nci != ci) { s.cls = _classes[nci]; s.state = 0; }
                var states = s.cls.states.Select(x => x.name).ToArray();
                s.state = Mathf.Clamp(EditorGUILayout.Popup(s.state, states, GUILayout.Width(90)), 0, Mathf.Max(0, states.Length - 1));
                var swatch = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18)); EditorGUI.DrawRect(swatch, s.cls.GetAlbedo(s.state));
                EditorGUILayout.LabelField($"{s.confidence:P0}  {s.reason}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
            if (GUILayout.Button("Apply", GUILayout.Width(90)))
            {
                int n = AutoTagger.Apply(_items);
                Debug.Log($"[SemanticVR] Auto-tagged {n} renderer(s).");
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
