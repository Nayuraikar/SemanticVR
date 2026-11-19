#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SemanticVR.EditorTools
{
    /// <summary>Batch entry point: opens the demo scene and enters play mode; PerceptionAutoTest writes the report and exits.</summary>
    public static class PerceptionBatchTest
    {
        public static void Run()
        {
            if (System.Environment.GetEnvironmentVariable("SVR_BURST_SYNC") == "1") Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            UnityEngine.Debug.Log("[SemanticVR] Burst sync compile = " + Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously + ", burst enabled = " + Unity.Burst.BurstCompiler.Options.EnableBurstCompilation);
            EditorSceneManager.OpenScene("Assets/Scenes/PerceptionDemo.unity");
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
