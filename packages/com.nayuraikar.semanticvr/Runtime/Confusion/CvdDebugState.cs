namespace SemanticVR.Confusion
{
    public enum DebugViewMode { Original = 0, Simulated = 1, Split = 2 }

    /// <summary>Which colours the pair graph scores: the base albedo (baseline) or the predicted lit colour (C1).</summary>
    public enum ConfusionScoring { Albedo = 0, Lit = 1 }

    /// <summary>Shared state between DebugViewController (writer) and the CVD renderer feature (reader).</summary>
    public static class CvdDebugState
    {
        public static DebugViewMode Mode = DebugViewMode.Original;
        public static CvdType Type = CvdType.Deutan;
        public static float Severity = 1f;
        /// <summary>Scoring used by the pair-graph overlay and HUD (K toggles). Edges always carry both scores.</summary>
        public static ConfusionScoring Scoring = ConfusionScoring.Lit;
    }
}
