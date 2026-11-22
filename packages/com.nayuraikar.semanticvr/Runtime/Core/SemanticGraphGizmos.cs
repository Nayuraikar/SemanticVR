using System;
using System.Collections.Generic;
using SemanticVR.Confusion;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>What text (if any) the pair graph draws. Cycled with L.</summary>
    public enum GraphLabelDetail { Off = 0, DeltaE = 1, FullNames = 2 }

    /// <summary>
    /// Pair-graph overlay: thin lines between confusable tagged objects, coloured by simulated ΔE00 on a luminance ramp
    /// (white = very low, dark blue = borderline; stays distinguishable under deutan/protan), semi-transparent and faded with distance. Only the <see cref="maxEdges"/> lowest-ΔE edges whose endpoints are both inside a camera
    /// frustum are drawn. Rendered with pooled LineRenderers (unlit, world space) so it works in builds and in both
    /// stereo eyes; an optional Editor-Gizmos path uses the same selection.
    /// Keys: G toggle, + / - edge cap, L label detail (off, ΔE at midpoints, full names).
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Semantic Graph Gizmos")]
    public class SemanticGraphGizmos : MonoBehaviour
    {
        [Tooltip("Master switch (G key at runtime; shown in the HUD).")] public bool showEdges = true;
        [Tooltip("Draw every edge in range, not just confusable ones.")] public bool showAllEdges = false;
        [Tooltip("Maximum edges drawn (lowest ΔE first). + / - change it at runtime.")] [Range(1, 200)] public int maxEdges = 25;
        [Tooltip("Step for the + / - keys.")] public int maxEdgesStep = 5;
        [Tooltip("Label detail (L key).")] public GraphLabelDetail labelDetail = GraphLabelDetail.Off;
        [Tooltip("Draw with pooled LineRenderers (works in builds, both eyes, no Gizmos button).")] public bool runtimeLines = true;
        [Tooltip("Also draw in the Editor with Gizmos (needs the Gizmos button on). Same selection and limits.")] public bool editorGizmos = false;
        [Tooltip("Only edges with both endpoints inside one of these cameras' frusta. Empty = the StereoRig eyes, else Camera.main.")]
        public Camera[] cullCameras;
        [Tooltip("Line width in metres.")] public float lineWidth = 0.006f;
        [Tooltip("Line opacity at close range.")] [Range(0f, 1f)] public float nearAlpha = 0.75f;
        [Tooltip("Line opacity at fadeEndDistance and beyond.")] [Range(0f, 1f)] public float farAlpha = 0.15f;
        [Tooltip("Distance (m, camera to edge midpoint) up to which lines keep nearAlpha.")] public float fadeStartDistance = 1.5f;
        [Tooltip("Distance (m) at which lines reach farAlpha.")] public float fadeEndDistance = 8f;
        [Tooltip("Only pairs closer than this (metres) become edges.")] public float maxEdgeDistance = 8f;
        [Tooltip("Simulated ΔE00 at or below which a pair counts as confusable.")] public float confusableThreshold = 15f;
        [Tooltip("Original ΔE00 must be at least this for a pair to count (otherwise normal vision cannot tell it apart either).")]
        public float minOriginalDeltaE = 10f;
        [Tooltip("Seconds between graph rebuilds.")] public float refreshInterval = 0.25f;

        // Luminance-led ramp (white -> dark blue): the blue/yellow axis survives deutan and protan, and lightness differs strongly.
        static readonly Color VeryLow = new Color(1f, 1f, 1f), Borderline = new Color(0.08f, 0.16f, 0.62f);

        readonly List<PairEdge> _edges = new List<PairEdge>();
        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly List<Candidate> _shown = new List<Candidate>();
        readonly List<LineRenderer> _pool = new List<LineRenderer>();
        readonly Plane[] _planes = new Plane[6];
        readonly List<Camera> _cams = new List<Camera>(2);
        Transform _lineRoot;
        Material _lineMat;
        GUIStyle _labelStyle;
        float _lastBuild = float.NegativeInfinity;
        int _lastVersion = -1;
        CvdType _lastType; float _lastSeverity = -1f;

        struct Candidate { public PairEdge Edge; public Vector3 P, Q; public float Alpha; }
        static readonly Comparison<Candidate> ByDeltaE = (a, b) => a.Edge.DeltaESimulated.CompareTo(b.Edge.DeltaESimulated);

        public IReadOnlyList<PairEdge> Edges => _edges;
        public int ConfusableCount { get; private set; }
        /// <summary>Edges eligible to be drawn (confusable, or all in range), before the frustum test and the cap.</summary>
        public int TotalCount => _candidatesTotal;
        /// <summary>Edges actually drawn this frame (never exceeds <see cref="maxEdges"/>).</summary>
        public int ShownCount => _shown.Count;
        /// <summary>The pooled renderers (active ones are the drawn edges).</summary>
        public IReadOnlyList<LineRenderer> Lines => _pool;
        int _candidatesTotal;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.gKey.wasPressedThisFrame) showEdges = !showEdges;
                if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) AdjustMaxEdges(+maxEdgesStep);
                if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) AdjustMaxEdges(-maxEdgesStep);
                if (kb.lKey.wasPressedThisFrame) CycleLabels();
            }
            Refresh();
        }

        public void AdjustMaxEdges(int delta) => maxEdges = Mathf.Clamp(maxEdges + delta, 1, 200);
        public void CycleLabels() => labelDetail = (GraphLabelDetail)(((int)labelDetail + 1) % 3);

        /// <summary>Rebuild the edge list now (also used by tests).</summary>
        public void Rebuild()
        {
            SemanticGraph.Build(_edges, CvdDebugState.Type, CvdDebugState.Severity, maxEdgeDistance);
            ConfusableCount = 0;
            foreach (var e in _edges) if (e.IsConfusable(confusableThreshold, minOriginalDeltaE)) ConfusableCount++;
            _lastBuild = Time.realtimeSinceStartup; _lastVersion = SemanticRegistry.Version;
            _lastType = CvdDebugState.Type; _lastSeverity = CvdDebugState.Severity;
        }

        /// <summary>Rebuild if stale, pick the edges to show, and sync the line renderers. Called every frame by Update.</summary>
        public void Refresh()
        {
            if (!showEdges) { _shown.Clear(); _candidatesTotal = 0; SyncLines(); return; }
            bool dirty = _lastVersion != SemanticRegistry.Version || _lastType != CvdDebugState.Type || !Mathf.Approximately(_lastSeverity, CvdDebugState.Severity);
            if (dirty || Time.realtimeSinceStartup - _lastBuild > refreshInterval) Rebuild(); // time-based too: objects move
            SelectEdges();
            SyncLines();
        }

        void CollectCameras()
        {
            _cams.Clear();
            if (cullCameras != null) foreach (var c in cullCameras) if (c != null && c.isActiveAndEnabled) _cams.Add(c);
            if (_cams.Count > 0) return;
            var rig = FindFirstObjectByType<StereoRig>();
            if (rig != null) { if (rig.LeftEye != null) _cams.Add(rig.LeftEye); if (rig.RightEye != null && rig.stereo) _cams.Add(rig.RightEye); }
            if (_cams.Count == 0 && Camera.main != null) _cams.Add(Camera.main);
        }

        bool InFrustum(Vector3 p)
        {
            if (_cams.Count == 0) return true; // no camera (edit mode, tests): nothing to cull against
            foreach (var c in _cams)
            {
                var v = c.WorldToViewportPoint(p);
                if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) return true;
            }
            return false;
        }

        void SelectEdges()
        {
            _candidates.Clear(); _shown.Clear();
            CollectCameras();
            _candidatesTotal = 0;
            foreach (var e in _edges)
            {
                if (e.A == null || e.B == null) continue;
                if (!showAllEdges && !e.IsConfusable(confusableThreshold, minOriginalDeltaE)) continue;
                _candidatesTotal++;
                Vector3 p = e.A.Bounds.ClosestPoint(e.B.Position), q = e.B.Bounds.ClosestPoint(p); // surface to surface, so lawns do not draw to their centre
                if (!InFrustum(p) || !InFrustum(q)) continue;
                _candidates.Add(new Candidate { Edge = e, P = p, Q = q, Alpha = DistanceAlpha((p + q) * 0.5f) });
            }
            _candidates.Sort(ByDeltaE);
            int n = Mathf.Min(Mathf.Max(maxEdges, 0), _candidates.Count);
            for (int i = 0; i < n; i++) _shown.Add(_candidates[i]);
        }

        /// <summary>Opacity for an edge whose midpoint is at <paramref name="mid"/>: nearAlpha up close, easing to farAlpha, by distance to the nearest camera.</summary>
        float DistanceAlpha(Vector3 mid)
        {
            if (_cams.Count == 0) return nearAlpha;
            float d = float.MaxValue;
            foreach (var c in _cams) d = Mathf.Min(d, Vector3.Distance(c.transform.position, mid));
            return AlphaAtDistance(d);
        }

        public float AlphaAtDistance(float d) =>
            Mathf.Lerp(nearAlpha, farAlpha, Mathf.Clamp01((d - fadeStartDistance) / Mathf.Max(fadeEndDistance - fadeStartDistance, 1e-3f)));

        public Color EdgeColour(float deltaE) =>
            Color.Lerp(VeryLow, Borderline, Mathf.Clamp01(deltaE / Mathf.Max(confusableThreshold, 1e-3f)));

        Material LineMaterial()
        {
            if (_lineMat != null) return _lineMat;
            var sh = Resources.Load<Shader>("SemanticVRGraphLine");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _lineMat = new Material(sh) { name = "SemanticVR_GraphLine", hideFlags = HideFlags.HideAndDontSave };
            return _lineMat;
        }

        LineRenderer GetLine(int i)
        {
            if (_lineRoot == null)
            {
                var root = new GameObject("SemanticVR_PairGraphLines") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(transform, false);
                _lineRoot = root.transform;
                _pool.Clear();
            }
            while (_pool.Count <= i)
            {
                var go = new GameObject("edge" + _pool.Count) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(_lineRoot, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2; lr.useWorldSpace = true;
                lr.sharedMaterial = LineMaterial();
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false; lr.numCapVertices = 0; lr.alignment = LineAlignment.View;
                _pool.Add(lr);
            }
            return _pool[i];
        }

        void SyncLines()
        {
            int n = runtimeLines && showEdges ? _shown.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var lr = GetLine(i);
                var c = _shown[i];
                var col = EdgeColour(c.Edge.DeltaESimulated); col.a = c.Alpha;
                lr.startColor = lr.endColor = col;
                lr.widthMultiplier = lineWidth;
                lr.SetPosition(0, c.P); lr.SetPosition(1, c.Q);
                if (!lr.enabled) lr.enabled = true;
            }
            for (int i = n; i < _pool.Count; i++) if (_pool[i] != null && _pool[i].enabled) _pool[i].enabled = false;
        }

        static string Name(SemanticTag t) => t.Class != null ? $"{t.Class.displayName}:{t.StateName}" : t.name;

        string LabelFor(in Candidate c) => labelDetail == GraphLabelDetail.DeltaE
            ? c.Edge.DeltaESimulated.ToString("0.0")
            : $"{Name(c.Edge.A)} ↔ {Name(c.Edge.B)}  ΔE {c.Edge.DeltaESimulated:0.0}";

        // Runtime labels: drawn once per eye camera (IMGUI, projected through each eye's own pixel rect).
        void OnGUI()
        {
            if (!showEdges || labelDetail == GraphLabelDetail.Off || Event.current.type != EventType.Repaint) return;
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
                _labelStyle.normal.textColor = Color.white;
            }
            foreach (var c in _shown)
            {
                var mid = (c.P + c.Q) * 0.5f;
                string text = LabelFor(c);
                foreach (var cam in _cams)
                {
                    var s = cam.WorldToScreenPoint(mid);
                    if (s.z <= 0f) continue;
                    var size = _labelStyle.CalcSize(new GUIContent(text));
                    var r = new Rect(s.x - size.x * 0.5f, Screen.height - s.y - size.y * 0.5f, size.x, size.y);
                    var prev = GUI.color;
                    GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = prev; GUI.Label(r, text, _labelStyle);
                }
            }
        }

        void OnDisable() { SyncLinesOff(); }
        void SyncLinesOff() { foreach (var lr in _pool) if (lr != null) lr.enabled = false; }

        void OnDestroy()
        {
            if (_lineRoot != null) Destroy(_lineRoot.gameObject);
            if (_lineMat != null) Destroy(_lineMat);
        }

        // Editor Gizmos path: same selection and limits, no tag boxes, labels or icons.
        void OnDrawGizmos()
        {
            if (!editorGizmos || !showEdges) return;
            if (!Application.isPlaying) { Rebuild(); SelectEdges(); }
            foreach (var c in _shown)
            {
                var gc = EdgeColour(c.Edge.DeltaESimulated); gc.a = c.Alpha; Gizmos.color = gc;
                Gizmos.DrawLine(c.P, c.Q);
#if UNITY_EDITOR
                if (labelDetail != GraphLabelDetail.Off) UnityEditor.Handles.Label((c.P + c.Q) * 0.5f, LabelFor(c));
#endif
            }
        }
    }
}
