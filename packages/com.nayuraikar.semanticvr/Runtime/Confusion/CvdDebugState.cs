namespace SemanticVR.Confusion
{
    public enum DebugViewMode { Original = 0, Simulated = 1, Split = 2 }

    /// <summary>Shared state between DebugViewController (writer) and the CVD renderer feature (reader).</summary>
    public static class CvdDebugState
    {
        public static DebugViewMode Mode = DebugViewMode.Original;
        public static CvdType Type = CvdType.Deutan;
        public static float Severity = 1f;
    }
}
