#if UNITY_EDITOR
using UnityEditor;

namespace SemanticVR.EditorTools
{
    /// <summary>Batch entry for the walkable-demo test: defers script recompiles until play mode ends (a recompile during play would kill the test), then runs the standard play-mode entry.</summary>
    public static class DemoBatchTest
    {
        public static void Run()
        {
            EditorPrefs.SetInt("ScriptCompilationDuringPlay", 2);   // 2 = recompile after finished playing
            PerceptionBatchTest.Run();
        }
    }
}
#endif
