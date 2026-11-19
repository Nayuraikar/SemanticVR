#if UNITY_EDITOR
using System.Collections.Generic;
using SemanticVR.Assist;
using SemanticVR.ColourProcessing;
using SemanticVR.DemoUI;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Player;
using SemanticVR.UI;
using SemanticVR.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SemanticVR.EditorTools
{
    /// <summary>
    /// Turns Assets/Scenes/PerceptionDemo.unity into the walkable, living demo: colliders, first-person rig, traffic
    /// lights, moving vehicles and pedestrians, priority-aware colour assist and the new UI. Idempotent. It never opens
    /// or changes MainScene or any Phase 1 prefab/material (new assets get new names).
    /// Menu: SemanticVR / Phase 4 / Build Walkable Demo (PerceptionDemo scene).
    /// </summary>
    public static class WalkableDemoBuilder
    {
        const string DemoScene = "Assets/Scenes/PerceptionDemo.unity";
        const string MatDir = "Assets/Materials/Environment", PrefabDir = "Assets/Prefabs/Environment", WalkerDir = "Assets/Prefabs/Environment/Phase4";
        const string AutoGroup = "Phase4_Generated";

        [MenuItem("SemanticVR/Phase 4/Build Walkable Demo (PerceptionDemo scene)")]
        public static void Run()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScene) == null) { Debug.LogError("[SemanticVR] PerceptionDemo scene missing. Run SemanticVR/Setup Perception Demo Scene first."); return; }
            var scene = EditorSceneManager.OpenScene(DemoScene);
            var root = GameObject.Find("TrafficIntersection");
            if (root == null) { Debug.LogError("[SemanticVR] TrafficIntersection not found in PerceptionDemo."); return; }
            Folder(WalkerDir);

            AddColliders(root);
            var lights = SetupLights(root);
            var walkers = BuildWalkers(root);
            var vehicles = BuildVehicles(root);
            var sim = SetupSimulation(root, lights, vehicles, walkers);
            SetupPlayer(sim);
            SetupAssistAndUi(sim, lights);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SemanticVR] Walkable demo built: {vehicles.Count} vehicles, {walkers.Count} pedestrians. Scene saved: {DemoScene}");
        }

        // ------------------------------------------------------------------ colliders
        static void AddColliders(GameObject root)
        {
            foreach (var g in new[] { "Ground", "Roads", "Sidewalks" })
            {
                var grp = root.transform.Find(g); if (grp == null) continue;
                foreach (Transform c in grp) if (c.GetComponent<Collider>() == null && c.GetComponent<MeshFilter>() != null) c.gameObject.AddComponent<BoxCollider>();
            }
            Each(root, "Buildings", b => Ensure<BoxCollider>(b.Find("Mass") != null ? b.Find("Mass").gameObject : b.gameObject));
            Each(root, "Vegetation", v =>
            {
                var trunk = v.Find("Trunk");
                if (trunk != null) { var cc = Ensure<CapsuleCollider>(trunk.gameObject); cc.radius = 0.5f; }
                else if (v.Find("Foliage") != null) { var sc = Ensure<SphereCollider>(v.Find("Foliage").gameObject); sc.radius = 0.5f; }
            });
            Each(root, "TrafficLights", l => { var p = l.Find("Pole"); if (p != null) Ensure<CapsuleCollider>(p.gameObject); });
            Each(root, "TrafficSigns", sg => { var p = sg.Find("Post"); if (p != null) Ensure<CapsuleCollider>(p.gameObject); });

            var old = root.transform.Find("Boundary"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var b2 = new GameObject("Boundary"); b2.transform.SetParent(root.transform, false);
            foreach (var (pos, size) in new[] { (new Vector3(0, 5, 92), new Vector3(190, 10, 1)), (new Vector3(0, 5, -92), new Vector3(190, 10, 1)), (new Vector3(92, 5, 0), new Vector3(1, 10, 190)), (new Vector3(-92, 5, 0), new Vector3(1, 10, 190)) })
            {
                var w = new GameObject("Wall"); w.transform.SetParent(b2.transform, false); w.transform.localPosition = pos;
                w.AddComponent<BoxCollider>().size = size;
            }
        }

        static void Each(GameObject root, string group, System.Action<Transform> a)
        {
            var g = root.transform.Find(group); if (g == null) return;
            foreach (Transform c in g) a(c);
        }
        static T Ensure<T>(GameObject go) where T : Component { var c = go.GetComponent<T>(); return c != null ? c : go.AddComponent<T>(); }

        // ------------------------------------------------------------------ traffic lights
        static TrafficLightController SetupLights(GameObject root)
        {
            var grp = root.transform.Find("TrafficLights");
            // lamps change material at run time: they must not be statically batched
            foreach (var t in grp.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            var ctl = Ensure<TrafficLightController>(grp.gameObject);
            ctl.redOn = Mat("Mat_LampRed"); ctl.greenOn = Mat("Mat_LampGreen"); ctl.lampOff = Mat("Mat_LampOff");
            ctl.yellowOn = LoadOrCreateEmissive("Mat_LampYellow", new Color(1f, 0.78f, 0.05f));
            var heads = new List<TrafficLightController.Head>();
            foreach (Transform l in grp)
            {
                if (!l.name.StartsWith("TrafficLight_")) continue;
                bool ns = l.name.EndsWith("North") || l.name.EndsWith("South");
                heads.Add(new TrafficLightController.Head
                {
                    axis = ns ? RoadAxis.NorthSouth : RoadAxis.EastWest,
                    red = l.Find("Lamp_Red").GetComponent<Renderer>(), yellow = l.Find("Lamp_Yellow").GetComponent<Renderer>(), green = l.Find("Lamp_Green").GetComponent<Renderer>()
                });
            }
            ctl.heads = heads.ToArray();
            EditorUtility.SetDirty(ctl);
            return ctl;
        }

        // ------------------------------------------------------------------ vehicles
        static List<Transform> BuildVehicles(GameObject root)
        {
            var grp = root.transform.Find("Vehicles");
            var list = new List<Transform>();
            for (int i = grp.childCount - 1; i >= 0; i--) { var c = grp.GetChild(i); if (c.name.StartsWith("Extra_")) Object.DestroyImmediate(c.gameObject); }
            foreach (Transform c in grp) list.Add(c);
            var extra = new[] { "Vehicle_Car_Red", "Vehicle_Car_Blue", "Vehicle_Car_White", "Vehicle_Car_Red", "Vehicle_Car_Blue", "Vehicle_Car_White", "Vehicle_Bus", "Vehicle_Car_Blue" };
            int n = 0;
            foreach (var pf in extra)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{pf}.prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grp); go.name = $"Extra_{pf.Replace("Vehicle_", "")}_{n++:00}";
                list.Add(go.transform);
            }
            foreach (var t in list)
            {
                bool bus = t.name.Contains("Bus");
                var box = Ensure<BoxCollider>(t.gameObject);
                box.center = new Vector3(0, bus ? 1.65f : 0.85f, 0); box.size = bus ? new Vector3(2.5f, 3.0f, 11f) : new Vector3(1.8f, 1.4f, 4.2f);
                var rb = Ensure<Rigidbody>(t.gameObject); rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            }
            return list;
        }

        // ------------------------------------------------------------------ pedestrians
        static List<Transform> BuildWalkers(GameObject root)
        {
            var grp = root.transform.Find("Pedestrians");
            for (int i = grp.childCount - 1; i >= 0; i--) Object.DestroyImmediate(grp.GetChild(i).gameObject);   // the 7 static Phase 1 placements (scene copy only)
            var shirts = new (string name, Color c)[]
            {
                ("Orange", new Color(1f, 0.5f, 0f)), ("Blue", new Color(0.10f, 0.40f, 0.90f)), ("Magenta", new Color(0.80f, 0.15f, 0.60f)),
                ("Red", new Color(0.85f, 0.12f, 0.12f)), ("Green", new Color(0.15f, 0.60f, 0.15f))
            };
            var prefabs = new List<GameObject>();
            foreach (var s in shirts) prefabs.Add(WalkerPrefab(s.name, s.c));
            var list = new List<Transform>();
            for (int i = 0; i < 16; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Count], grp); go.name = $"Pedestrian_{shirts[i % shirts.Length].name}_{i:00}";
                list.Add(go.transform);
            }
            return list;
        }

        static GameObject WalkerPrefab(string name, Color shirt)
        {
            string path = $"{WalkerDir}/Walker_{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var shirtMat = LoadOrCreate($"Mat_Walker_{name}", shirt);
            var skin = Mat("Mat_Skin"); var trousers = LoadOrCreate("Mat_Trousers", new Color(0.16f, 0.17f, 0.22f));
            var r = new GameObject("Walker_" + name);
            var torso = Node("Torso", r.transform, Vector3.zero);
            Mesh(PrimitiveType.Capsule, "TorsoMesh", torso, new Vector3(0, 1.2f, 0), new Vector3(0.42f, 0.36f, 0.28f), shirtMat);
            Mesh(PrimitiveType.Sphere, "Head", torso, new Vector3(0, 1.68f, 0), Vector3.one * 0.25f, skin);
            foreach (var (n, x) in new[] { ("ArmL", -0.27f), ("ArmR", 0.27f) })
            {
                var arm = Node(n, torso, new Vector3(x, 1.46f, 0));
                Mesh(PrimitiveType.Capsule, n + "Mesh", arm, new Vector3(0, -0.28f, 0), new Vector3(0.1f, 0.3f, 0.1f), shirtMat);
            }
            foreach (var (n, x) in new[] { ("LegL", -0.1f), ("LegR", 0.1f) })
            {
                var leg = Node(n, r.transform, new Vector3(x, 0.9f, 0));
                Mesh(PrimitiveType.Cylinder, n + "Mesh", leg, new Vector3(0, -0.45f, 0), new Vector3(0.14f, 0.45f, 0.14f), trousers);
            }
            var cap = r.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.88f, 0); cap.radius = 0.3f; cap.height = 1.76f;
            var rb = r.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            r.AddComponent<SemanticTag>().Category = SemanticCategory.Pedestrian;
            var saved = PrefabUtility.SaveAsPrefabAsset(r, path);
            Object.DestroyImmediate(r);
            return saved;
        }

        static Transform Node(string name, Transform parent, Vector3 pos) { var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos; return g.transform; }
        static void Mesh(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale; g.GetComponent<Renderer>().sharedMaterial = m;
        }

        // ------------------------------------------------------------------ simulation, player, assist, UI
        static TrafficSimulation SetupSimulation(GameObject root, TrafficLightController lights, List<Transform> vehicles, List<Transform> walkers)
        {
            var go = root.transform.Find("Simulation")?.gameObject;
            if (go == null) { go = new GameObject("Simulation"); go.transform.SetParent(root.transform, false); }
            var sim = Ensure<TrafficSimulation>(go);
            sim.lights = lights; sim.vehicles = vehicles.ToArray(); sim.pedestrians = walkers.ToArray();
            EditorUtility.SetDirty(sim);
            return sim;
        }

        static void SetupPlayer(TrafficSimulation sim)
        {
            var rig = GameObject.Find("ViewerRig");
            var cam = Camera.main;
            if (rig == null || cam == null) { Debug.LogError("[SemanticVR] ViewerRig / Main Camera missing."); return; }
            Ensure<CharacterController>(rig);
            Ensure<DesktopPlayerInput>(rig);
            var pc = Ensure<PlayerController>(rig); pc.head = cam.transform;
            cam.fieldOfView = 70f; cam.nearClipPlane = 0.08f; cam.farClipPlane = 250f;
            sim.player = rig.transform;
            EditorUtility.SetDirty(sim);
        }

        static void SetupAssistAndUi(TrafficSimulation sim, TrafficLightController lights)
        {
            var go = GameObject.Find("SemanticVR Perception");
            if (go == null) { Debug.LogError("[SemanticVR] 'SemanticVR Perception' object missing."); return; }
            var proc = go.GetComponent<ColourProcessor>(); var pipe = go.GetComponent<PerceptionPipeline>();
            var layer = Ensure<SemanticAssistLayer>(go); layer.processor = proc; layer.perception = pipe;
            var view = Ensure<ColourAssistView>(go); view.processor = proc; view.playerCamera = Camera.main;
            var dbg = Ensure<DemoDebugOverlay>(go); dbg.sim = sim; dbg.lights = lights;
            var ui = Ensure<DemoUiController>(go);
            ui.processor = proc; ui.assistView = view; ui.assistLayer = layer; ui.perception = pipe; ui.debug = dbg; ui.input = Object.FindFirstObjectByType<DesktopPlayerInput>();
            var old = go.GetComponent<ColourPreviewUI>(); if (old != null) old.enabled = false;        // developer panel kept, off by default
            var pdbg = go.GetComponent<PerceptionDebugUI>(); if (pdbg != null) pdbg.enabled = false;
            Ensure<DemoAutoTest>(go);
            EditorUtility.SetDirty(go);
        }

        // ------------------------------------------------------------------ materials / folders
        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        static Material LoadOrCreate(string name, Color c)
        {
            var m = Mat(name); if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(m, $"{MatDir}/{name}.mat"); return m;
        }

        static Material LoadOrCreateEmissive(string name, Color c)
        {
            var m = Mat(name); if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 2f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(m, $"{MatDir}/{name}.mat"); return m;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Folder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
        }
    }
}
#endif
