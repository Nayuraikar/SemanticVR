using System.Collections.Generic;
using SemanticVR.Confusion;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>
    /// Debug gizmos: tag markers plus the confusable-pair graph (lines between tagged objects) coloured by the current
    /// ΔE00 under the CVD profile selected in <see cref="CvdDebugState"/>. Drawn with Gizmos, so in the Game view the
    /// "Gizmos" toggle must be on (Editor only). Toggle with G or from the HUD state.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Semantic Graph Gizmos")]
    public class SemanticGraphGizmos : MonoBehaviour
    {
        [Tooltip("Master switch (G key at runtime; shown in the HUD).")] public bool showEdges = true;
        public bool showTagMarkers = true;
        public bool showLabels = true;
        [Tooltip("Draw every edge in range, not just confusable ones.")] public bool showAllEdges = false;
        [Tooltip("Only pairs closer than this (metres) become edges.")] public float maxEdgeDistance = 8f;
        [Tooltip("Simulated ΔE00 at or below which a pair counts as confusable.")] public float confusableThreshold = 15f;
        [Tooltip("Original ΔE00 must be at least this for a pair to count (otherwise normal vision cannot tell it apart either).")]
        public float minOriginalDeltaE = 10f;
        [Tooltip("Seconds between graph rebuilds.")] public float refreshInterval = 0.25f;

        readonly List<PairEdge> _edges = new List<PairEdge>();
        float _lastBuild = float.NegativeInfinity;
        int _lastVersion = -1;
        CvdType _lastType; float _lastSeverity = -1f;

        public IReadOnlyList<PairEdge> Edges => _edges;
        public int ConfusableCount { get; private set; }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) showEdges = !showEdges;
            if (showEdges) RefreshIfStale(); // keeps the HUD count live even when the Game view Gizmos toggle is off
        }

        /// <summary>Rebuild the edge list now (also used by tests).</summary>
        public void Rebuild()
        {
            SemanticGraph.Build(_edges, CvdDebugState.Type, CvdDebugState.Severity, maxEdgeDistance);
            ConfusableCount = 0;
            foreach (var e in _edges) if (e.IsConfusable(confusableThreshold, minOriginalDeltaE)) ConfusableCount++;
            _lastBuild = Time.realtimeSinceStartup; _lastVersion = SemanticRegistry.Version;
            _lastType = CvdDebugState.Type; _lastSeverity = CvdDebugState.Severity;
        }

        void RefreshIfStale()
        {
            bool dirty = _lastVersion != SemanticRegistry.Version || _lastType != CvdDebugState.Type || !Mathf.Approximately(_lastSeverity, CvdDebugState.Severity);
            if (dirty || Time.realtimeSinceStartup - _lastBuild > refreshInterval) Rebuild(); // time-based too: objects move
        }

        void OnDrawGizmos()
        {
            if (!showEdges) return;
            RefreshIfStale();
            if (showTagMarkers)
                foreach (var t in SemanticRegistry.Tags)
                {
                    if (t == null || t.TargetRenderer == null || t.CurrentState == null) continue;
                    var c = t.CurrentAlbedo; c.a = 1f;
                    Gizmos.color = c;
                    var b = t.TargetRenderer.bounds;
                    if (t.includeInGraph) Gizmos.DrawWireCube(b.center, b.size * 1.02f);
#if UNITY_EDITOR
                    if (showLabels && t.includeInGraph && t.Class != null)
                        UnityEditor.Handles.Label(b.center + Vector3.up * (b.extents.y + 0.12f), $"#{t.Id} {t.Class.displayName}:{t.StateName}");
#endif
                }
            foreach (var e in _edges)
            {
                bool conf = e.IsConfusable(confusableThreshold, minOriginalDeltaE);
                if (!conf && !showAllEdges) continue;
                if (e.A == null || e.B == null) continue;
                Gizmos.color = SemanticGraph.EdgeColor(e.DeltaESimulated, confusableThreshold * 1.6f);
                Vector3 p = e.A.Bounds.ClosestPoint(e.B.Position), q = e.B.Bounds.ClosestPoint(p); // surface-to-surface, so lawns do not draw to their centre
                Gizmos.DrawLine(p, q);
                Gizmos.DrawSphere(p, 0.04f); Gizmos.DrawSphere(q, 0.04f);
#if UNITY_EDITOR
                if (showLabels && conf) UnityEditor.Handles.Label((p + q) * 0.5f, $"ΔE {e.DeltaESimulated:0.0}");
#endif
            }
        }
    }
}
