#if UNITY_EDITOR
using SemanticVR.Accessibility;
using SemanticVR.ColourProcessing;
using SemanticVR.Perception;
using SemanticVR.Semantics;
using SemanticVR.UI;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SemanticVR.EditorTools
{
    /// <summary>
    /// One-click setup: creates the editable config assets and a PerceptionDemo scene (a copy of MainScene, so the
    /// Phase 1 scene is never modified) with the perception pipeline and debug UI attached.
    /// Menu: SemanticVR / Setup Perception Demo Scene.
    /// </summary>
    public static class SemanticVRSetup
    {
        const string Dir = "Assets/Settings/SemanticVR";
        const string ModelPath = "Assets/Models/rfdetr_seg_nano.onnx";
        const string PhotoPath = "Assets/Textures/Test/street_photo_oxford.jpg";
        const string MainScene = "Assets/Scenes/MainScene.unity";
        const string DemoScene = "Assets/Scenes/PerceptionDemo.unity";

        [MenuItem("SemanticVR/Setup Perception Demo Scene")]
        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Settings", "SemanticVR");
            var map = GetOrCreate<CocoToSemanticMap>(Dir + "/CocoToSemanticMap.asset");
            var imp = GetOrCreate<SemanticImportanceConfig>(Dir + "/SemanticImportanceConfig.asset");
            var cvd = GetOrCreate<CvdProfile>(Dir + "/CvdProfile.asset");
            var corr = GetOrCreate<CorrectionSettings>(Dir + "/CorrectionSettings.asset");

            var ti = AssetImporter.GetAtPath(PhotoPath) as TextureImporter;
            if (ti != null && (!ti.mipmapEnabled || !ti.sRGBTexture || ti.npotScale != TextureImporterNPOTScale.None || ti.maxTextureSize < 2048)) { ti.mipmapEnabled = true; ti.sRGBTexture = true; ti.isReadable = false; ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 2048; ti.SaveAndReimport(); }
            var photo = AssetDatabase.LoadAssetAtPath<Texture2D>(PhotoPath);
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            if (model == null) Debug.LogWarning("[SemanticVR] " + ModelPath + " not found/imported yet. Copy the ONNX model there (see README) and re-run setup.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScene) == null) AssetDatabase.CopyAsset(MainScene, DemoScene);
            var scene = EditorSceneManager.OpenScene(DemoScene);

            var go = GameObject.Find("SemanticVR Perception");
            if (go == null) go = new GameObject("SemanticVR Perception");
            var pipe = go.GetComponent<PerceptionPipeline>() ?? go.AddComponent<PerceptionPipeline>();
            pipe.modelAsset = model; pipe.cocoMap = map; pipe.importanceConfig = imp; pipe.staticPhoto = photo;
            pipe.stepBudgetMs = 1f; pipe.sourceCamera = Camera.main; pipe.sourceMode = FrameSourceMode.GameCamera;
            if (go.GetComponent<PerceptionDebugUI>() == null) go.AddComponent<PerceptionDebugUI>();
            if (go.GetComponent<PerceptionAutoTest>() == null) go.AddComponent<PerceptionAutoTest>();
            var colour = go.GetComponent<ColourProcessor>() ?? go.AddComponent<ColourProcessor>();
            colour.profileAsset = cvd; colour.settingsAsset = corr; colour.sourceCamera = Camera.main; colour.perception = pipe;
            if (go.GetComponent<ColourPreviewUI>() == null) go.AddComponent<ColourPreviewUI>();
            if (go.GetComponent<ColourAutoTest>() == null) go.AddComponent<ColourAutoTest>();
            EditorUtility.SetDirty(go);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SemanticVR] Perception demo scene ready: " + DemoScene);
        }

        static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }
    }
}
#endif
