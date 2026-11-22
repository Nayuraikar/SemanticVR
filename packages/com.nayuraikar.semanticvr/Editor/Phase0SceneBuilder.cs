using System.Collections.Generic;
using System.IO;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SemanticVR.Editor
{
    /// <summary>Builds Assets/Scenes/Phase0_Demo.unity: confusable colour pairs, gradient sky, StereoRig + debug view.</summary>
    public static class Phase0SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Phase0_Demo.unity";
        const string AssetDir = "Assets/Scenes/Phase0_Demo";
        const string OldSample = "Assets/Scenes/SampleScene.unity";

        // Palette (sRGB hex). Pairs are chosen so deutan simulation collapses each pair (checked in python/semanticvr_ref).
        static readonly (string name, string a, string b)[] Pairs =
        {
            ("red / green",    "#C8312B", "#7E8F20"),
            ("orange / olive", "#E0801F", "#A0A01C"),
            ("blue / purple",  "#3E6FD2", "#7B55B8"),
            ("pink / grey",    "#E58FB0", "#A8A0A4"),
        };
        const string GroundA = "#C9C4BA", GroundB = "#B9B4AA", Pedestal = "#EFECE6", Wall = "#D8D2C6",
                     Rock = "#8E8A83", Trunk = "#6B5846", CanopyA = "#4F7F78", CanopyB = "#5E8F86";

        [MenuItem("SemanticVR/Build Phase0 Demo Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder(AssetDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Environment").transform;
            BuildGround(root);
            BuildPairs(root);
            BuildBackdrop(root);
            BuildLight();
            BuildSky();
            BuildRig();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureRendererFeature();
            SetBuildSettings();
            if (File.Exists(OldSample)) AssetDatabase.DeleteAsset(OldSample);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SemanticVR] Phase 0 demo scene built: " + ScenePath);
        }

        // ---------- helpers ----------
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static readonly Dictionary<string, Material> MatCache = new();
        static Material Mat(string name, string hex, float smoothness = 0.25f, Texture tex = null, Vector2? tiling = null)
        {
            string key = name + hex;
            if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;
            string path = $"{AssetDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", Hex(hex));
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (tex != null)
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            }
            EditorUtility.SetDirty(m);
            MatCache[key] = m;
            return m;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Texture2D CheckerTexture()
        {
            string path = $"{AssetDir}/GroundChecker.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                Color a = Hex(GroundA), b = Hex(GroundB);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        t.SetPixel(x, y, ((x / 32) + (y / 32)) % 2 == 0 ? a : b);
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Bilinear; imp.anisoLevel = 8; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ---------- scene parts ----------
        static void BuildGround(Transform root)
        {
            var mat = Mat("Ground", "#FFFFFF", 0.08f, CheckerTexture(), new Vector2(30, 30));
            Prim(PrimitiveType.Plane, "Ground", root, Vector3.zero, new Vector3(8, 1, 8), mat);
        }

        static void BuildPairs(Transform root)
        {
            var row = new GameObject("ConfusablePairs").transform;
            row.SetParent(root, false);
            var pedMat = Mat("Pedestal", Pedestal, 0.35f);
            PrimitiveType[] shapes = { PrimitiveType.Sphere, PrimitiveType.Cube, PrimitiveType.Capsule, PrimitiveType.Cylinder };
            float spacing = 3.4f, z = 5.5f, sep = 0.62f;
            for (int i = 0; i < Pairs.Length; i++)
            {
                float cx = (i - (Pairs.Length - 1) * 0.5f) * spacing;
                var pair = new GameObject("Pair_" + Pairs[i].name).transform;
                pair.SetParent(row, false);
                pair.localPosition = new Vector3(cx, 0, z);
                for (int side = 0; side < 2; side++)
                {
                    float x = (side == 0 ? -sep : sep);
                    string hex = side == 0 ? Pairs[i].a : Pairs[i].b;
                    var colMat = Mat($"Pair{i}_{(side == 0 ? "A" : "B")}", hex, 0.3f);
                    Prim(PrimitiveType.Cylinder, "Pedestal", pair, new Vector3(x, 0.22f, 0), new Vector3(0.95f, 0.22f, 0.95f), pedMat);
                    Vector3 scale = shapes[i] == PrimitiveType.Capsule ? new Vector3(0.7f, 0.5f, 0.7f) : Vector3.one * 0.78f;
                    float y = 0.44f + (shapes[i] == PrimitiveType.Capsule ? 0.5f : 0.39f);
                    Vector3 euler = shapes[i] == PrimitiveType.Cube ? new Vector3(0, 25f, 0) : Vector3.zero;
                    Prim(shapes[i], Pairs[i].name + (side == 0 ? " (A)" : " (B)"), pair, new Vector3(x, y, 0), scale, colMat, euler);
                }
            }
        }

        static void BuildBackdrop(Transform root)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);
            var wall = Mat("Wall", Wall, 0.1f);
            Prim(PrimitiveType.Cube, "BackWall", props, new Vector3(0, 1.5f, 11f), new Vector3(30f, 3f, 0.4f), wall);
            for (int i = -3; i <= 3; i++)
                Prim(PrimitiveType.Cube, "Pillar", props, new Vector3(i * 4.6f, 1.9f, 10.7f), new Vector3(0.6f, 3.8f, 0.6f), wall);

            var rock = Mat("Rock", Rock, 0.12f);
            Vector3[] rocks = { new(-9, 0.2f, 3), new(8.5f, 0.25f, 2), new(-3.2f, 0.18f, 9), new(12, 0.3f, 7), new(-12, 0.22f, 8), new(3.5f, 0.16f, 3.2f) };
            for (int i = 0; i < rocks.Length; i++)
                Prim(PrimitiveType.Sphere, "Rock", props, rocks[i], new Vector3(0.9f + 0.2f * (i % 3), 0.5f, 0.7f + 0.15f * (i % 2)), rock, new Vector3(0, i * 47f, 0));

            var trunk = Mat("Trunk", Trunk, 0.1f);
            var canA = Mat("CanopyA", CanopyA, 0.15f);
            var canB = Mat("CanopyB", CanopyB, 0.15f);
            Vector3[] trees = { new(-10.5f, 0, 6), new(-13, 0, 12), new(10.5f, 0, 6.5f), new(13.5f, 0, 11), new(-8.6f, 0, 9.4f), new(8.8f, 0, 9.2f), new(-15.5f, 0, 5), new(16, 0, 4.5f) };
            for (int i = 0; i < trees.Length; i++)
            {
                float s = 0.85f + 0.12f * (i % 4);
                var t = new GameObject("Tree").transform;
                t.SetParent(props, false);
                t.localPosition = trees[i];
                Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0, 1.0f * s, 0), new Vector3(0.22f, 1.0f * s, 0.22f), trunk);
                Prim(PrimitiveType.Sphere, "Canopy", t, new Vector3(0, 2.5f * s, 0), Vector3.one * 2.2f * s, i % 2 == 0 ? canA : canB);
                Prim(PrimitiveType.Sphere, "Canopy", t, new Vector3(0.2f * s, 3.5f * s, 0.1f), Vector3.one * 1.5f * s, i % 2 == 0 ? canB : canA);
            }
        }

        static void BuildLight()
        {
            var go = new GameObject("Directional Light");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = Hex("#FFF1DC");
            l.intensity = 1.25f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;
            l.shadowBias = 0.05f; l.shadowNormalBias = 0.4f;
            go.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            go.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = l;
        }

        static void BuildSky()
        {
            string path = $"{AssetDir}/GradientSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(Shader.Find("SemanticVR/GradientSkybox")); AssetDatabase.CreateAsset(sky, path); }
            sky.SetColor("_Top", Hex("#6C8DB5"));
            sky.SetColor("_Horizon", Hex("#E9E2D6"));
            sky.SetColor("_Bottom", Hex("#B5B0A6"));
            sky.SetFloat("_Exponent", 0.8f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#A9BBD3");
            RenderSettings.ambientEquatorColor = Hex("#CFC9BD");
            RenderSettings.ambientGroundColor = Hex("#8A857C");
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }

        static void BuildRig()
        {
            var go = new GameObject("StereoRig");
            go.transform.position = new Vector3(0, 0, -1.5f);
            var rig = go.AddComponent<StereoRig>();
            go.AddComponent<HeadPoseGazeSource>();
            var dbg = go.AddComponent<DebugViewController>();
            dbg.rig = rig;
            var hud = go.AddComponent<HudOverlay>();
            hud.controller = dbg;
        }

        // ---------- project wiring ----------
        static void EnsureRendererFeature()
        {
            var shader = Shader.Find("Hidden/SemanticVR/CvdSimulation");
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" })) // Assets only: never touch package assets
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                CvdSimulationFeature existing = null;
                foreach (var f in data.rendererFeatures) if (f is CvdSimulationFeature c) existing = c;
                if (existing != null) { existing.shader = shader; EditorUtility.SetDirty(existing); continue; }

                var feat = ScriptableObject.CreateInstance<CvdSimulationFeature>();
                feat.name = "SemanticVR CVD Simulation";
                feat.shader = shader;
                AssetDatabase.AddObjectToAsset(feat, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out _, out long localId);

                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feat;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                Debug.Log("[SemanticVR] Added CvdSimulationFeature to " + data.name);
            }
        }

        static void SetBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(sc => sc.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); // keep scenes added by other builders
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
