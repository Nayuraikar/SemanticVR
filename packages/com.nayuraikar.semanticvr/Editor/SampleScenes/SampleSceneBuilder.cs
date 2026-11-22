using System.IO;
using UnityEditor;
using UnityEngine;

namespace SemanticVR.Editor
{
    /// <summary>
    /// Builds the four sample scenes (Street, Signals, Cockpit, Medical) into the host project (Assets/Samples/SemanticVR/&lt;scene&gt;/) and then
    /// snapshots each folder (including .meta files, so GUID references stay valid) into the package's Samples~/ folder.
    /// Headless: <c>Unity -batchmode -projectPath unity/SemanticVR -executeMethod SemanticVR.Editor.SampleSceneBuilder.BuildAll -quit</c>
    /// (only works while no other Editor instance has the project open).
    /// </summary>
    public static class SampleSceneBuilder
    {
        const string PackageName = "com.nayuraikar.semanticvr";
        public static readonly string[] SampleNames = { "01_Street", "02_Signals", "03_Cockpit", "04_Medical" };

        [MenuItem("SemanticVR/Build Sample Scenes")]
        public static void BuildAll()
        {
            Scene01Street.Build();
            Scene02Signals.Build();
            Scene03Cockpit.Build();
            Scene04Medical.Build();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            SyncToPackage();
            Debug.Log("[SemanticVR] Sample scenes built and synced to Samples~.");
        }

        /// <summary>The Forest scene is no longer a listed sample (replaced by 03_Cockpit); its builder is kept for reference.</summary>
        [MenuItem("SemanticVR/Build Legacy Forest Scene")]
        public static void BuildLegacyForest() { Scene03Forest.Build(); AssetDatabase.SaveAssets(); Debug.Log("[SemanticVR] Built legacy 03_Forest (not synced to Samples~)."); }

        /// <summary>Copy Assets/Samples/SemanticVR/&lt;name&gt; to &lt;package&gt;/Samples~/&lt;name&gt; (replacing it).</summary>
        public static void SyncToPackage()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}");
            if (info == null) { Debug.LogWarning("[SemanticVR] Package not found; skipping Samples~ sync."); return; }
            foreach (var name in SampleNames)
            {
                string src = Path.GetFullPath($"{SceneKit.HostRoot}/{name}");
                string dst = Path.Combine(info.resolvedPath, "Samples~", name);
                if (!Directory.Exists(src)) { Debug.LogWarning("[SemanticVR] Missing " + src); continue; }
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                CopyDir(src, dst);
            }
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
