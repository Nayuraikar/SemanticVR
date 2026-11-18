using System.Collections.Generic;
using SemanticVR.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SemanticVR.EditorTools
{
    /// <summary>
    /// Phase 1: generates a lightweight URP traffic intersection in the open scene.
    /// Menu: Tools > SemanticVR > Phase 1 > Generate Traffic Intersection
    /// Units are metres. Intersection centre is the world origin; roads run along X and Z.
    /// </summary>
    public static class TrafficIntersectionGenerator
    {
        const string RootName = "TrafficIntersection";
        const string RigName = "ViewerRig";
        const string LightName = "Sun_Directional";
        const string MatDir = "Assets/Materials/Environment";
        const string PrefabDir = "Assets/Prefabs/Environment";

        const float RoadWidth = 10f;
        const float HalfRoad = RoadWidth * 0.5f;
        const float ArmLength = 75f;      // road arm length beyond the junction square
        const float RoadH = 0.04f;
        const float MarkH = 0.01f;
        const float CurbH = 0.15f;
        const float SidewalkW = 3f;

        static readonly Vector3[] ArmDirs =
            { Vector3.forward, Vector3.back, Vector3.right, Vector3.left }; // N, S, E, W
        static readonly string[] ArmNames = { "North", "South", "East", "West" };

        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/SemanticVR/Phase 1/Generate Traffic Intersection")]
        public static void Generate()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
            {
                EditorUtility.DisplayDialog("SemanticVR", "URP Lit shader not found. Is URP active?", "OK");
                return;
            }

            var existing = GameObject.Find(RootName);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("SemanticVR",
                        $"'{RootName}' already exists in this scene. Replace it with a freshly generated one?\n(Only the generated '{RootName}' and '{RigName}' objects are replaced.)",
                        "Replace", "Cancel"))
                    return;
                RemoveGenerated();
            }

            matCache.Clear();
            EnsureFolder(MatDir);
            EnsureFolder(PrefabDir);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Generate Traffic Intersection");

            BuildGround(Group(root, "Ground"));
            BuildRoads(Group(root, "Roads"));
            BuildMarkings(Group(root, "RoadMarkings"));
            BuildSidewalks(Group(root, "Sidewalks"));
            BuildTrafficLights(Group(root, "TrafficLights"));
            BuildSigns(Group(root, "TrafficSigns"));
            var vehicles = Group(root, "Vehicles");
            BuildVehicles(vehicles);
            var peds = Group(root, "Pedestrians");
            BuildPedestrians(peds);
            BuildBuildings(Group(root, "Buildings"));
            BuildVegetation(Group(root, "Vegetation"));

            // Static (non-moving) groups can be batched. Vehicles/pedestrians stay dynamic.
            foreach (Transform g in root.transform)
                if (g != vehicles.transform && g != peds.transform)
                    SetStaticRecursive(g.gameObject);

            SetupLighting();
            SetupCamera();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log("[SemanticVR] Traffic intersection generated. Save the scene with Cmd+S.");
        }

        [MenuItem("Tools/SemanticVR/Phase 1/Remove Generated Intersection")]
        public static void RemoveGenerated()
        {
            // Keep the camera: detach it from the rig before deleting the rig.
            var cam = Camera.main;
            if (cam != null && cam.transform.parent != null && cam.transform.parent.name == RigName)
                cam.transform.SetParent(null, true);

            var rig = GameObject.Find(RigName);
            if (rig != null) Object.DestroyImmediate(rig);
            var root = GameObject.Find(RootName);
            if (root != null) Object.DestroyImmediate(root);
            var sun = GameObject.Find(LightName);
            if (sun != null) Object.DestroyImmediate(sun);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        // -------------------------------------------------------------- geometry

        // Position on an arm: 'along' metres outward from centre, 'lat' metres to the
        // right of a driver APPROACHING the junction on that arm (right-hand traffic).
        static Vector3 ArmPos(Vector3 outward, float along, float lat)
            => outward * along + Vector3.Cross(Vector3.up, -outward) * lat;

        static void BuildGround(GameObject parent)
        {
            var g = Prim(PrimitiveType.Cube, "Ground_Terrain", parent, new Vector3(0, -0.1f, 0),
                new Vector3(400, 0.2f, 400), Mat("Mat_Ground", new Color(0.36f, 0.50f, 0.27f)));
            Tag(g, SemanticCategory.Ground);
        }

        static void BuildRoads(GameObject parent)
        {
            var asphalt = Mat("Mat_Asphalt", new Color(0.17f, 0.17f, 0.19f));
            Tag(Prim(PrimitiveType.Cube, "Road_JunctionCentre", parent, new Vector3(0, RoadH / 2, 0),
                new Vector3(RoadWidth, RoadH, RoadWidth), asphalt), SemanticCategory.Road);

            for (int i = 0; i < 4; i++)
            {
                var d = ArmDirs[i];
                var arm = Prim(PrimitiveType.Cube, $"Road_Arm_{ArmNames[i]}", parent,
                    ArmPos(d, HalfRoad + ArmLength / 2, 0) + Vector3.up * RoadH / 2,
                    new Vector3(RoadWidth, RoadH, ArmLength), asphalt);
                arm.transform.rotation = Quaternion.LookRotation(d);
                Tag(arm, SemanticCategory.Road);
            }
        }

        static void BuildMarkings(GameObject parent)
        {
            var white = Mat("Mat_PaintWhite", new Color(0.92f, 0.92f, 0.9f));
            var yellow = Mat("Mat_PaintYellow", new Color(0.95f, 0.78f, 0.10f));
            var cross = Group(parent, "Crosswalks");
            var stop = Group(parent, "StopLines");
            var lanes = Group(parent, "LaneDividers");
            float y = RoadH + MarkH / 2;

            for (int i = 0; i < 4; i++)
            {
                var d = ArmDirs[i];
                var rot = Quaternion.LookRotation(d);
                string n = ArmNames[i];

                // Zebra crossing: stripes parallel to the road, 3 m deep, 1 m pitch.
                for (int s = -4; s <= 4; s++)
                {
                    var st = Prim(PrimitiveType.Cube, $"Crosswalk_{n}_{s + 4:00}", cross,
                        ArmPos(d, 7f, s) + Vector3.up * y, new Vector3(0.5f, MarkH, 3f), white);
                    st.transform.rotation = rot;
                    Tag(st, SemanticCategory.RoadMarking);
                }

                // Stop line across the approaching lane only.
                var sl = Prim(PrimitiveType.Cube, $"StopLine_{n}", stop,
                    ArmPos(d, 9.3f, HalfRoad / 2) + Vector3.up * y, new Vector3(HalfRoad, MarkH, 0.4f), white);
                sl.transform.rotation = rot;
                Tag(sl, SemanticCategory.RoadMarking);

                // Dashed yellow centre line.
                int k = 0;
                for (float a = 11f; a < ArmLength; a += 6f, k++)
                {
                    var dash = Prim(PrimitiveType.Cube, $"CentreDash_{n}_{k:00}", lanes,
                        ArmPos(d, a + 1.5f, 0) + Vector3.up * y, new Vector3(0.15f, MarkH, 3f), yellow);
                    dash.transform.rotation = rot;
                    Tag(dash, SemanticCategory.RoadMarking);
                }
            }
        }

        static void BuildSidewalks(GameObject parent)
        {
            var mat = Mat("Mat_Sidewalk", new Color(0.62f, 0.60f, 0.57f));
            float c = HalfRoad + SidewalkW / 2; // 6.5
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var corner = Prim(PrimitiveType.Cube, $"Sidewalk_Corner_{(sx > 0 ? "E" : "W")}{(sz > 0 ? "N" : "S")}",
                        parent, new Vector3(sx * c, CurbH / 2, sz * c), new Vector3(SidewalkW, CurbH, SidewalkW), mat);
                    Tag(corner, SemanticCategory.Sidewalk);
                }

            float start = HalfRoad + SidewalkW; // strips begin after the corner blocks
            float len = ArmLength - SidewalkW;
            for (int i = 0; i < 4; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    var d = ArmDirs[i];
                    var strip = Prim(PrimitiveType.Cube, $"Sidewalk_{ArmNames[i]}_{(side > 0 ? "Right" : "Left")}", parent,
                        ArmPos(d, start + len / 2, side * c) + Vector3.up * CurbH / 2,
                        new Vector3(SidewalkW, CurbH, len), mat);
                    strip.transform.rotation = Quaternion.LookRotation(d);
                    Tag(strip, SemanticCategory.Sidewalk);
                }
        }

        // -------------------------------------------------------------- prefabs

        static GameObject BuildTrafficLightPrefab()
        {
            return GetOrCreatePrefab("TrafficLight", SemanticCategory.TrafficLight, root =>
            {
                var pole = Mat("Mat_Pole", new Color(0.35f, 0.36f, 0.38f));
                var housing = Mat("Mat_LightHousing", new Color(0.08f, 0.08f, 0.09f));
                var off = Mat("Mat_LampOff", new Color(0.12f, 0.12f, 0.12f));
                Prim(PrimitiveType.Cylinder, "Pole", root, new Vector3(0, 1.6f, 0), new Vector3(0.14f, 1.6f, 0.14f), pole);
                Prim(PrimitiveType.Cube, "Housing", root, new Vector3(0, 3.6f, 0), new Vector3(0.5f, 1.3f, 0.4f), housing);
                Prim(PrimitiveType.Sphere, "Lamp_Red", root, new Vector3(0, 3.95f, 0.2f), Vector3.one * 0.3f, off);
                Prim(PrimitiveType.Sphere, "Lamp_Yellow", root, new Vector3(0, 3.6f, 0.2f), Vector3.one * 0.3f, off);
                Prim(PrimitiveType.Sphere, "Lamp_Green", root, new Vector3(0, 3.25f, 0.2f), Vector3.one * 0.3f, off);
            });
        }

        static GameObject BuildSignPrefab(string name, PrimitiveType shape, Color face, bool diamond)
        {
            return GetOrCreatePrefab(name, SemanticCategory.TrafficSign, root =>
            {
                Prim(PrimitiveType.Cylinder, "Post", root, new Vector3(0, 1.1f, 0), new Vector3(0.07f, 1.1f, 0.07f),
                    Mat("Mat_Pole", new Color(0.35f, 0.36f, 0.38f)));
                var plate = Prim(shape, "Plate", root, new Vector3(0, 2.4f, 0.05f), Vector3.one,
                    Mat("Mat_" + name, face));
                if (shape == PrimitiveType.Cylinder)
                {
                    plate.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    plate.transform.localScale = new Vector3(0.75f, 0.02f, 0.75f);
                }
                else
                {
                    plate.transform.localScale = new Vector3(0.65f, 0.65f, 0.03f);
                    if (diamond) plate.transform.localRotation = Quaternion.Euler(0, 0, 45);
                }
            });
        }

        static GameObject BuildCarPrefab(string name, Color body)
        {
            return GetOrCreatePrefab(name, SemanticCategory.Vehicle, root =>
            {
                var bodyMat = Mat("Mat_" + name, body);
                var glass = Mat("Mat_Glass", new Color(0.10f, 0.14f, 0.20f));
                var tyre = Mat("Mat_Tyre", new Color(0.05f, 0.05f, 0.05f));
                Prim(PrimitiveType.Cube, "Body", root, new Vector3(0, 0.65f, 0), new Vector3(1.8f, 0.6f, 4.2f), bodyMat);
                Prim(PrimitiveType.Cube, "Cabin", root, new Vector3(0, 1.2f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f), glass);
                AddWheels(root, tyre, 0.9f, 1.3f, 0.33f);
            });
        }

        static GameObject BuildBusPrefab()
        {
            return GetOrCreatePrefab("Vehicle_Bus", SemanticCategory.Vehicle, root =>
            {
                var bodyMat = Mat("Mat_Vehicle_Bus", new Color(0.95f, 0.62f, 0.05f));
                var glass = Mat("Mat_Glass", new Color(0.10f, 0.14f, 0.20f));
                var tyre = Mat("Mat_Tyre", new Color(0.05f, 0.05f, 0.05f));
                Prim(PrimitiveType.Cube, "Body", root, new Vector3(0, 1.65f, 0), new Vector3(2.5f, 2.7f, 11f), bodyMat);
                Prim(PrimitiveType.Cube, "WindowBand", root, new Vector3(0, 2.0f, 0), new Vector3(2.54f, 0.8f, 9.5f), glass);
                AddWheels(root, tyre, 1.25f, 3.6f, 0.5f);
            });
        }

        static void AddWheels(GameObject root, Material tyre, float x, float z, float r)
        {
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    var w = Prim(PrimitiveType.Cylinder, "Wheel", root, new Vector3(sx * x, r, sz * z),
                        new Vector3(r * 2, 0.12f, r * 2), tyre);
                    w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
        }

        static GameObject BuildPedestrianPrefab(string name, Color shirt)
        {
            return GetOrCreatePrefab(name, SemanticCategory.Pedestrian, root =>
            {
                Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0, 0.8f, 0), new Vector3(0.45f, 0.8f, 0.45f),
                    Mat("Mat_" + name, shirt));
                Prim(PrimitiveType.Sphere, "Head", root, new Vector3(0, 1.68f, 0), Vector3.one * 0.26f,
                    Mat("Mat_Skin", new Color(0.85f, 0.65f, 0.5f)));
            });
        }

        static GameObject BuildBuildingPrefab(string name, Color wall)
        {
            // Unit cube with its pivot on the ground: scale the instance to set footprint/height.
            return GetOrCreatePrefab(name, SemanticCategory.Building, root =>
            {
                Prim(PrimitiveType.Cube, "Mass", root, new Vector3(0, 0.5f, 0), Vector3.one, Mat("Mat_" + name, wall));
            });
        }

        static GameObject BuildTreePrefab()
        {
            return GetOrCreatePrefab("Tree_Round", SemanticCategory.Vegetation, root =>
            {
                Prim(PrimitiveType.Cylinder, "Trunk", root, new Vector3(0, 1.0f, 0), new Vector3(0.3f, 1.0f, 0.3f),
                    Mat("Mat_Trunk", new Color(0.35f, 0.22f, 0.12f)));
                Prim(PrimitiveType.Sphere, "Foliage", root, new Vector3(0, 3.3f, 0), Vector3.one * 2.8f,
                    Mat("Mat_Foliage", new Color(0.12f, 0.42f, 0.14f)));
            });
        }

        static GameObject BuildBushPrefab()
        {
            return GetOrCreatePrefab("Bush", SemanticCategory.Vegetation, root =>
            {
                Prim(PrimitiveType.Sphere, "Foliage", root, new Vector3(0, 0.4f, 0), new Vector3(1.2f, 0.9f, 1.2f),
                    Mat("Mat_Bush", new Color(0.25f, 0.55f, 0.20f)));
            });
        }

        // ------------------------------------------------------- scene instances

        static void BuildTrafficLights(GameObject parent)
        {
            var prefab = BuildTrafficLightPrefab();
            var red = Mat("Mat_LampRed", new Color(1f, 0.1f, 0.1f), true);
            var green = Mat("Mat_LampGreen", new Color(0.1f, 1f, 0.2f), true);
            for (int i = 0; i < 4; i++)
            {
                var d = ArmDirs[i];
                var go = Spawn(prefab, $"TrafficLight_{ArmNames[i]}", parent,
                    ArmPos(d, 7f, HalfRoad + SidewalkW / 2) + Vector3.up * CurbH, Quaternion.LookRotation(d));
                bool nsRed = i < 2; // N/S red, E/W green
                go.transform.Find(nsRed ? "Lamp_Red" : "Lamp_Green").GetComponent<Renderer>().sharedMaterial = nsRed ? red : green;
            }
        }

        static void BuildSigns(GameObject parent)
        {
            var stopSign = BuildSignPrefab("Sign_Stop", PrimitiveType.Cylinder, new Color(0.8f, 0.05f, 0.05f), false);
            var speed = BuildSignPrefab("Sign_SpeedLimit", PrimitiveType.Cube, new Color(0.95f, 0.95f, 0.95f), false);
            var crossing = BuildSignPrefab("Sign_PedestrianCrossing", PrimitiveType.Cube, new Color(0.85f, 0.95f, 0.15f), true);
            float lat = HalfRoad + SidewalkW / 2;
            for (int i = 0; i < 4; i++)
            {
                var d = ArmDirs[i];
                var rot = Quaternion.LookRotation(d);
                string n = ArmNames[i];
                Spawn(crossing, $"Sign_PedestrianCrossing_{n}", parent, ArmPos(d, 12f, lat) + Vector3.up * CurbH, rot);
                if (i < 2)
                    Spawn(speed, $"Sign_SpeedLimit_{n}", parent, ArmPos(d, 24f, lat) + Vector3.up * CurbH, rot);
                else
                    Spawn(stopSign, $"Sign_Stop_{n}", parent, ArmPos(d, 24f, lat) + Vector3.up * CurbH, rot);
            }
        }

        static void BuildVehicles(GameObject parent)
        {
            var red = BuildCarPrefab("Vehicle_Car_Red", new Color(0.80f, 0.10f, 0.10f));
            var blue = BuildCarPrefab("Vehicle_Car_Blue", new Color(0.10f, 0.30f, 0.80f));
            var white = BuildCarPrefab("Vehicle_Car_White", new Color(0.90f, 0.90f, 0.90f));
            var bus = BuildBusPrefab();
            Vector3 up = Vector3.up * RoadH;
            Vector3 N = ArmDirs[0], S = ArmDirs[1], E = ArmDirs[2], W = ArmDirs[3];

            // Approaching vehicles head toward the centre (-outward); departing ones away from it.
            Spawn(red, "Car_Red_North_Queue1", parent, ArmPos(N, 12f, 2.5f) + up, Quaternion.LookRotation(-N));
            Spawn(blue, "Car_Blue_North_Queue2", parent, ArmPos(N, 18.5f, 2.5f) + up, Quaternion.LookRotation(-N));
            Spawn(bus, "Bus_South_Approach", parent, ArmPos(S, 16f, 2.5f) + up, Quaternion.LookRotation(-S));
            Spawn(white, "Car_White_East_Departing", parent, ArmPos(E, 15f, -2.5f) + up, Quaternion.LookRotation(E));
            Spawn(blue, "Car_Blue_West_Approach", parent, ArmPos(W, 14f, 2.5f) + up, Quaternion.LookRotation(-W));
            Spawn(white, "Car_White_Crossing", parent, new Vector3(0f, 0f, -2.5f) + up, Quaternion.LookRotation(Vector3.right));
        }

        static void BuildPedestrians(GameObject parent)
        {
            var orange = BuildPedestrianPrefab("Pedestrian_Orange", new Color(1f, 0.5f, 0f));
            var blue = BuildPedestrianPrefab("Pedestrian_Blue", new Color(0.10f, 0.40f, 0.90f));
            var mag = BuildPedestrianPrefab("Pedestrian_Magenta", new Color(0.80f, 0.15f, 0.60f));
            Vector3 onRoad = Vector3.up * RoadH, onWalk = Vector3.up * CurbH;

            // On crosswalks (walking across the road).
            Spawn(orange, "Pedestrian_Crosswalk_North", parent, new Vector3(1.5f, 0, 7f) + onRoad, Quaternion.LookRotation(Vector3.right));
            Spawn(blue, "Pedestrian_Crosswalk_North_2", parent, new Vector3(-2f, 0, 7f) + onRoad, Quaternion.LookRotation(Vector3.left));
            Spawn(mag, "Pedestrian_Crosswalk_East", parent, new Vector3(7f, 0, -1f) + onRoad, Quaternion.LookRotation(Vector3.back));
            // Waiting on corners / strolling on sidewalks.
            Spawn(blue, "Pedestrian_Corner_NE", parent, new Vector3(6.2f, 0, 5.8f) + onWalk, Quaternion.LookRotation(Vector3.left));
            Spawn(orange, "Pedestrian_Corner_SW", parent, new Vector3(-5.8f, 0, -5.6f) + onWalk, Quaternion.LookRotation(Vector3.forward));
            Spawn(mag, "Pedestrian_Sidewalk_South", parent, new Vector3(-6.5f, 0, -14f) + onWalk, Quaternion.LookRotation(Vector3.forward));
            Spawn(orange, "Pedestrian_Sidewalk_West", parent, new Vector3(-16f, 0, 6.5f) + onWalk, Quaternion.LookRotation(Vector3.right));
        }

        static void BuildBuildings(GameObject parent)
        {
            var prefabs = new[]
            {
                BuildBuildingPrefab("Building_Beige", new Color(0.78f, 0.72f, 0.60f)),
                BuildBuildingPrefab("Building_Grey", new Color(0.52f, 0.54f, 0.58f)),
                BuildBuildingPrefab("Building_Brick", new Color(0.60f, 0.32f, 0.25f))
            };
            var rng = new System.Random(42);
            // (centre x, centre z, size x, size z) per quadrant, mirrored over signs.
            var plots = new[]
            {
                new Vector4(20, 20, 16, 16), new Vector4(42, 20, 20, 16),
                new Vector4(20, 42, 16, 20), new Vector4(46, 46, 22, 22)
            };
            int n = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    foreach (var p in plots)
                    {
                        float h = 9f + (float)rng.NextDouble() * 20f;
                        var b = Spawn(prefabs[rng.Next(prefabs.Length)], $"Building_{n++:00}", parent,
                            new Vector3(sx * p.x, 0, sz * p.y), Quaternion.identity);
                        b.transform.localScale = new Vector3(p.z, h, p.w);
                    }
        }

        static void BuildVegetation(GameObject parent)
        {
            var tree = BuildTreePrefab();
            var bush = BuildBushPrefab();
            var rng = new System.Random(7);
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                var d = ArmDirs[i];
                for (float a = 12f; a < 60f; a += 9f)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float s = 0.85f + (float)rng.NextDouble() * 0.4f;
                        var t = Spawn(tree, $"Tree_{n++:00}", parent,
                            ArmPos(d, a + (float)rng.NextDouble() * 2f, side * 9.8f), Quaternion.Euler(0, rng.Next(360), 0));
                        t.transform.localScale = Vector3.one * s;
                    }
            }
            // Bushes on lawns near the corners.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    for (int k = 0; k < 2; k++)
                        Spawn(bush, $"Bush_{n++:00}", parent,
                            new Vector3(sx * (9.8f + k * 1.4f), 0, sz * (9.8f + (1 - k) * 1.4f)), Quaternion.identity);
        }

        // ---------------------------------------------------- lighting / camera

        static void SetupLighting()
        {
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                var go = new GameObject(LightName);
                Undo.RegisterCreatedObjectUndo(go, "Create Sun");
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            // Existing directional lights are reused (not deleted), just re-aimed.
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Hard; // cheap on mobile
            RenderSettings.sun = sun;
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(go, "Create Camera");
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            // Eye-level view from the south-west sidewalk looking at the junction.
            var rig = new GameObject(RigName);
            Undo.RegisterCreatedObjectUndo(rig, "Create ViewerRig");
            Vector3 pos = new Vector3(-6.5f, CurbH, -11f);
            rig.transform.position = pos;
            rig.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, 0) - new Vector3(pos.x, 0, pos.z));
            cam.transform.SetParent(rig.transform, false);
            cam.transform.localPosition = new Vector3(0, 1.6f, 0);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 250f;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) data.renderPostProcessing = false;
        }

        // ---------------------------------------------------------------- helpers

        static GameObject Group(GameObject parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            return g;
        }

        static GameObject Prim(PrimitiveType type, string name, GameObject parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // no physics needed in Phase 1
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return go;
        }

        static void Tag(GameObject go, SemanticCategory c)
        {
            var t = go.GetComponent<SemanticTag>();
            if (t == null) t = go.AddComponent<SemanticTag>();
            t.Category = c;
        }

        static GameObject Spawn(GameObject prefab, string name, GameObject parent, Vector3 pos, Quaternion rot)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
            go.name = name;
            go.transform.position = pos;
            go.transform.rotation = rot;
            return go;
        }

        static GameObject GetOrCreatePrefab(string name, SemanticCategory cat, System.Action<GameObject> build)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing; // never overwrite user-edited prefabs
            var root = new GameObject(name);
            build(root);
            Tag(root, cat);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        static Material Mat(string name, Color color, bool emissive = false)
        {
            if (matCache.TryGetValue(name, out var cached)) return cached;
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Smoothness", 0.1f);
                m.SetFloat("_Metallic", 0f);
                if (emissive)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", color * 2f);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                AssetDatabase.CreateAsset(m, path);
            }
            matCache[name] = m;
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void SetStaticRecursive(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            foreach (Transform t in go.transform) SetStaticRecursive(t.gameObject);
        }
    }
}
