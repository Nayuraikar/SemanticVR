using System.Collections.Generic;
using System.Linq;
using SemanticVR.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SemanticVR.Editor
{
    public enum IssueSeverity { Info, Warning, Error }

    public struct ValidationIssue
    {
        public IssueSeverity severity;
        public string code;
        public string message;
        public Object context;
        public override string ToString() => $"[{severity}] {code}: {message}";
    }

    /// <summary>Scene checks for the semantic layer: untagged renderers, duplicate class/tag IDs, missing materials, bad tags.</summary>
    public static class SceneValidator
    {
        public const string Untagged = "UNTAGGED_RENDERER", TagNoRenderer = "TAG_NO_RENDERER", TagNoClass = "TAG_NO_CLASS",
            ClassNoStates = "CLASS_NO_STATES", DupClassId = "DUPLICATE_CLASS_ID", DupTagId = "DUPLICATE_TAG_ID",
            NoStableId = "NO_STABLE_ID", MissingMaterial = "MISSING_MATERIAL", BadShader = "BAD_SHADER",
            NoEmission = "EMISSION_KEYWORD_OFF", FsmBadState = "FSM_UNKNOWN_STATE", FsmEmpty = "FSM_EMPTY";

        static bool IsSemanticRenderer(Renderer r) => !(r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r is BillboardRenderer);

        public static List<ValidationIssue> Validate(Scene scene, bool includeProjectClasses = false)
        {
            var issues = new List<ValidationIssue>();
            void Add(IssueSeverity s, string code, string msg, Object ctx) => issues.Add(new ValidationIssue { severity = s, code = code, message = msg, context = ctx });

            var renderers = new List<Renderer>(); var tags = new List<SemanticTag>(); var fsms = new List<SemanticStateMachine>();
            foreach (var root in scene.GetRootGameObjects())
            {
                renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
                tags.AddRange(root.GetComponentsInChildren<SemanticTag>(true));
                fsms.AddRange(root.GetComponentsInChildren<SemanticStateMachine>(true));
            }
            var taggedRenderers = new HashSet<Renderer>(tags.Where(t => t.TargetRenderer != null).Select(t => t.TargetRenderer));

            // Renderers: untagged / materials
            foreach (var r in renderers.Where(IsSemanticRenderer))
            {
                bool tagged = taggedRenderers.Contains(r) || r.GetComponent<SemanticTag>() != null;
                if (!tagged && r.GetComponentInParent<SemanticIgnore>(true) == null)
                    Add(IssueSeverity.Warning, Untagged, $"Renderer '{Path(r.transform)}' has no SemanticTag (add one, or a SemanticIgnore on it or a parent).", r);

                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) Add(IssueSeverity.Error, MissingMaterial, $"Renderer '{Path(r.transform)}' has no materials.", r);
                else
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) Add(IssueSeverity.Error, MissingMaterial, $"Renderer '{Path(r.transform)}' material slot {i} is missing.", r);
                        else if (mats[i].shader == null || mats[i].shader.name == "Hidden/InternalErrorShader")
                            Add(IssueSeverity.Error, BadShader, $"Material '{mats[i].name}' on '{Path(r.transform)}' uses a missing/broken shader.", r);
                    }
            }

            // Tags
            foreach (var t in tags)
            {
                if (t.TargetRenderer == null) Add(IssueSeverity.Error, TagNoRenderer, $"SemanticTag on '{Path(t.transform)}' has no Renderer.", t);
                if (t.Class == null) { Add(IssueSeverity.Error, TagNoClass, $"SemanticTag on '{Path(t.transform)}' has no SemanticClass.", t); continue; }
                if (t.Class.StateCount == 0) Add(IssueSeverity.Error, ClassNoStates, $"Class '{t.Class.displayName}' (used by '{Path(t.transform)}') has no states.", t);
                if (t.StableId <= 0) Add(IssueSeverity.Info, NoStableId, $"SemanticTag on '{Path(t.transform)}' has no stable ID (registry IDs will vary between sessions).", t);

                if (t.TargetRenderer != null && t.Class.states.Any(s => s.emissionIntensity > 0f))
                {
                    var m = t.TargetRenderer.sharedMaterial;
                    if (m != null && !m.IsKeywordEnabled("_EMISSION"))
                        Add(IssueSeverity.Warning, NoEmission, $"Class '{t.Class.displayName}' has emissive states but material '{m.name}' on '{Path(t.transform)}' lacks the _EMISSION keyword.", t);
                }
            }
            foreach (var g in tags.Where(t => t.StableId > 0).GroupBy(t => t.StableId).Where(g => g.Count() > 1))
                Add(IssueSeverity.Error, DupTagId, $"Stable tag ID {g.Key} is used by {g.Count()} tags: {string.Join(", ", g.Select(t => Path(t.transform)))}.", g.First());

            // Class IDs: classes referenced by the scene (+ optionally every class asset in the project)
            var classes = new HashSet<SemanticClass>(tags.Where(t => t.Class != null).Select(t => t.Class));
            if (includeProjectClasses)
                foreach (var guid in AssetDatabase.FindAssets("t:SemanticClass"))
                {
                    var c = AssetDatabase.LoadAssetAtPath<SemanticClass>(AssetDatabase.GUIDToAssetPath(guid));
                    if (c != null) classes.Add(c);
                }
            foreach (var g in classes.GroupBy(c => c.id).Where(g => g.Count() > 1))
                Add(IssueSeverity.Error, DupClassId, $"Class ID {g.Key} is shared by: {string.Join(", ", g.Select(c => c.name + " ('" + c.displayName + "')"))}.", g.First());

            // State machines
            foreach (var f in fsms)
            {
                if (f.cycle.Count == 0 || f.targets.Count == 0) { Add(IssueSeverity.Warning, FsmEmpty, $"State machine on '{Path(f.transform)}' has no cycle steps or no targets.", f); continue; }
                foreach (var tg in f.targets)
                {
                    if (tg.tag == null || tg.tag.Class == null) continue;
                    foreach (var step in f.cycle.Where(s => tg.tag.Class.IndexOfState(s.state) < 0).Select(s => s.state).Distinct())
                        Add(IssueSeverity.Warning, FsmBadState, $"State machine on '{Path(f.transform)}' uses state '{step}' unknown to class '{tg.tag.Class.displayName}' (target '{Path(tg.tag.transform)}').", f);
                }
            }
            return issues;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        [MenuItem("SemanticVR/Validate Open Scenes")]
        static void Menu()
        {
            int e = 0, w = 0, inf = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var issue in Validate(sc, includeProjectClasses: true))
                {
                    var msg = $"[SemanticVR:{sc.name}] {issue}";
                    switch (issue.severity)
                    {
                        case IssueSeverity.Error: e++; Debug.LogError(msg, issue.context); break;
                        case IssueSeverity.Warning: w++; Debug.LogWarning(msg, issue.context); break;
                        default: inf++; Debug.Log(msg, issue.context); break;
                    }
                }
            }
            Debug.Log($"[SemanticVR] Validation finished: {e} error(s), {w} warning(s), {inf} info.");
        }
    }
}
