using System.Collections.Generic;
using System.IO;
using SemanticVR.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SemanticVR.Editor
{
    /// <summary>Shared helpers for the sample-scene builders. Lighting, sky and rig mirror Phase0SceneBuilder so the
    /// scenes look and behave like Phase0_Demo.</summary>
    public static class SceneKit
    {
        public const string HostRoot = "Assets/Samples/SemanticVR";

        public static string SampleName { get; private set; }
        public static string Dir => $"{HostRoot}/{SampleName}";
        public static string ScenePath => $"{Dir}/{SampleName}.unity";

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static int _nextTagId;
        static readonly Dictionary<int, SemanticClass> ClassCache = new Dictionary<int, SemanticClass>();

        // ---- shared neutral palette (same family as Phase0) ----
        public const string Concrete = "#C9C4BA", ConcreteDark = "#B3AEA4", Asphalt = "#4A4D52", Wall = "#D8D2C6", Metal = "#7C8188",
                            DarkMetal = "#2B2E33", White = "#F1EFE9", Wood = "#7A5B40", Trunk = "#6B5846", Soil = "#6E5B47";

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Start a fresh scene + asset folder for <paramref name="sampleName"/>.</summary>
        public static Scene Begin(string sampleName)
        {
            SampleName = sampleName;
            Mats.Clear(); ClassCache.Clear(); _nextTagId = 1;
            EnsureFolder(Dir); EnsureFolder(Dir + "/Materials"); EnsureFolder(Dir + "/Textures");
            DefaultClassLibrary.EnsureAssets();
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        public static void Save(Scene scene)
        {
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AddToBuildSettings(ScenePath);
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Exists(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---- classes / tags ----
        public static SemanticClass Class(int id)
        {
            if (!ClassCache.TryGetValue(id, out var c)) ClassCache[id] = c = DefaultClassLibrary.Load(id);
            return c;
        }

        /// <summary>Add a SemanticTag (with the next stable ID) to <paramref name="go"/>'s renderer.</summary>
        public static SemanticTag Tag(GameObject go, int classId, string state, bool includeInGraph = true)
        {
            var cls = Class(classId);
            var t = go.AddComponent<SemanticTag>();
            t.StableId = _nextTagId++;
            t.includeInGraph = includeInGraph;
            t.TargetRenderer = go.GetComponent<Renderer>();
            t.Configure(cls, state);
            return t;
        }

        /// <summary>Create + tag a primitive; its material is the class-state colour (the tag overrides it at runtime).</summary>
        public static SemanticTag TaggedPrim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, int classId, string state,
                                             Vector3? euler = null, float smooth = 0.3f, bool includeInGraph = true)
        {
            var cls = Class(classId);
            var go = Prim(type, name, parent, pos, scale, StateMat(cls, state, smooth), euler);
            return Tag(go, classId, state, includeInGraph);
        }

        public static Material StateMat(SemanticClass cls, string state, float smooth = 0.3f)
        {
            var s = cls.GetState(Mathf.Max(0, cls.IndexOfState(state)));
            var m = Mat($"{cls.name}_{s.name}", s.albedo, smooth);
            if (s.emissionIntensity > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", SemanticTag.EmissionColor(s));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        // ---- materials / primitives ----
        public static Material Mat(string name, string hex, float smooth = 0.25f, float metallic = 0f) => Mat(name, Hex(hex), smooth, metallic);

        public static Material Mat(string name, Color color, float smooth = 0.25f, float metallic = 0f, Texture tex = null, Vector2? tiling = null)
        {
            if (Mats.TryGetValue(name, out var cached) && cached != null) return cached;
            string path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            EditorUtility.SetDirty(m);
            return Mats[name] = m;
        }

        public static GameObject Group(string name, Transform parent, Vector3? pos = null, Vector3? euler = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos ?? Vector3.zero;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            return go;
        }

        /// <summary>Mark <paramref name="go"/> (and children) as intentionally non-semantic.</summary>
        public static GameObject Ignore(GameObject go) { if (go.GetComponent<SemanticIgnore>() == null) go.AddComponent<SemanticIgnore>(); return go; }

        public static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            var col = go.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3? euler = null) =>
            Prim(PrimitiveType.Cube, name, parent, pos, size, mat, euler);

        /// <summary>Disc facing -Z (towards a viewer standing at lower Z), diameter d, thickness th.</summary>
        public static GameObject DiscZ(string name, Transform parent, Vector3 pos, float d, float th, Material mat) =>
            Prim(PrimitiveType.Cylinder, name, parent, pos, new Vector3(d, th * 0.5f, d), mat, new Vector3(90, 0, 0));

        // ---- environment shared by every scene ----
        public static Texture2D CheckerTexture(string hexA = Concrete, string hexB = ConcreteDark, int cells = 2)
        {
            string path = $"{Dir}/Textures/Checker.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                Color a = Hex(hexA), b = Hex(hexB);
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) t.SetPixel(x, y, ((x / (64 / cells)) + (y / (64 / cells))) % 2 == 0 ? a : b);
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Bilinear; imp.anisoLevel = 8; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Same light + ambient + gradient skybox as Phase0_Demo.</summary>
        public static void LightAndSkybox()
        {
            var go = new GameObject("Directional Light");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = Hex("#FFF1DC"); l.intensity = 1.25f;
            l.shadows = LightShadows.Soft; l.shadowStrength = 0.85f; l.shadowBias = 0.05f; l.shadowNormalBias = 0.4f;
            go.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            go.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = l;

            string path = $"{Dir}/Materials/GradientSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null) { sky = new Material(Shader.Find("SemanticVR/GradientSkybox")); AssetDatabase.CreateAsset(sky, path); }
            sky.SetColor("_Top", Hex("#6C8DB5")); sky.SetColor("_Horizon", Hex("#E9E2D6")); sky.SetColor("_Bottom", Hex("#B5B0A6"));
            sky.SetFloat("_Exponent", 0.8f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#A9BBD3"); RenderSettings.ambientEquatorColor = Hex("#CFC9BD"); RenderSettings.ambientGroundColor = Hex("#8A857C");
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
        }

        /// <summary>Tagged sky: a large inverted unlit sphere (a skybox has no Renderer to tag). The tag colour multiplies a
        /// luminance gradient; excluded from the pair graph (it would connect to everything).</summary>
        public static SemanticTag SkyDome(Transform parent)
        {
            string tp = $"{Dir}/Textures/SkyGradient.png";
            if (!File.Exists(tp))
            {
                var t = new Texture2D(4, 128, TextureFormat.RGBA32, false);
                for (int y = 0; y < 128; y++)
                {
                    float v = y / 127f;                                  // 0 = nadir, 0.5 = horizon, 1 = zenith
                    float k = Mathf.Lerp(1f, 0.74f, Mathf.Pow(Mathf.Clamp01((v - 0.5f) * 2f), 0.7f));
                    for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(k, k * 1.01f, Mathf.Min(1f, k * 1.04f), 1f));
                }
                File.WriteAllBytes(tp, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(tp);
                var imp = (TextureImporter)AssetImporter.GetAtPath(tp);
                imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear;
                imp.SaveAndReimport();
            }
            string mp = $"{Dir}/Materials/SkyDome.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, mp); }
            var cls = Class(DefaultClassLibrary.Sky);
            m.SetColor("_BaseColor", cls.GetAlbedo(0));
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            EditorUtility.SetDirty(m);

            var go = Prim(PrimitiveType.Sphere, "SkyDome", parent, Vector3.zero, new Vector3(-290f, 290f, 290f), m); // negative X: renders the inside
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return Tag(go, DefaultClassLibrary.Sky, "Day", includeInGraph: false);
        }

        /// <summary>Phase0 rig + debug view + HUD, plus the pair-graph gizmos (G).</summary>
        public static StereoRig Rig(Vector3 position, float yaw, float maxEdgeDistance)
        {
            var go = new GameObject("StereoRig");
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var rig = go.AddComponent<StereoRig>();
            go.AddComponent<HeadPoseGazeSource>();
            var dbg = go.AddComponent<DebugViewController>(); dbg.rig = rig;
            var gz = go.AddComponent<SemanticGraphGizmos>(); gz.maxEdgeDistance = maxEdgeDistance;
            var hud = go.AddComponent<HudOverlay>(); hud.controller = dbg; hud.graph = gz;
            return rig;
        }

        public static SemanticStateMachine Fsm(string name, Transform parent, params (string state, float seconds)[] steps)
        {
            var go = Group(name, parent);
            var f = go.AddComponent<SemanticStateMachine>();
            foreach (var (s, d) in steps) f.AddStep(s, d);
            return f;
        }

        /// <summary>Deterministic RNG for scatter placement (stable scenes between builds).</summary>
        public static System.Random Rng(int seed) => new System.Random(seed);
        public static float Range(this System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
