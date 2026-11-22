using SemanticVR.Core;
using UnityEngine;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>01_Street: two-lane street with a signalised crossing at z=34, road signs, cars, grass verges, trees and sky.</summary>
    public static class Scene01Street
    {
        public static void Build()
        {
            var scene = Begin("01_Street");
            LightAndSkybox();
            var env = Ignore(Group("Environment", null)).transform;   // decor: never semantic
            var sem = Group("Semantic", null).transform;               // everything below is tagged (or has an ignored Trim child)

            Roads(env);
            Buildings(env);
            Furniture(env);
            Grass(sem);
            Trees(sem, env);
            SkyDome(sem);
            Signals(sem, env);
            Signs(sem, env);
            Cars(sem);
            RoadWorks(sem, env);
            Rig(new Vector3(0, 0, -4f), 0f, 8f);
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        static void Roads(Transform env)
        {
            var asphalt = Mat("Asphalt", Asphalt, 0.12f);
            var walk = Mat("Sidewalk", Concrete, 0.1f);
            var curb = Mat("Curb", ConcreteDark, 0.1f);
            var paint = Mat("RoadPaint", White, 0.2f);
            var ground = Mat("FarGround", "#FFFFFF", 0.08f);
            ground.SetTexture("_BaseMap", CheckerTexture()); ground.SetTextureScale("_BaseMap", new Vector2(40, 40));

            Prim(PrimitiveType.Plane, "FarGround", env, new Vector3(0, -0.1f, 40), new Vector3(40, 1, 40), ground);
            Box("MainRoad", env, new Vector3(0, -0.05f, 50), new Vector3(11, 0.1f, 160), asphalt);
            Box("CrossRoad", env, new Vector3(0, -0.045f, 34), new Vector3(160, 0.1f, 11), asphalt);

            // sidewalks + curbs in four quadrants (broken by the cross road)
            foreach (float sx in new[] { -1f, 1f })
                foreach (var (z0, z1) in new[] { (-12f, 28.5f), (39.5f, 125f) })
                {
                    float len = z1 - z0, cz = (z0 + z1) * 0.5f;
                    Box("Sidewalk", env, new Vector3(sx * 7.0f, 0.07f, cz), new Vector3(3f, 0.15f, len), walk);
                    Box("Curb", env, new Vector3(sx * 5.6f, 0.08f, cz), new Vector3(0.2f, 0.17f, len), curb);
                }

            // centre line dashes + crosswalks + stop lines
            for (float z = -8; z < 120; z += 6f)
                if (z < 27f || z > 41f) Box("Dash", env, new Vector3(0, 0.01f, z), new Vector3(0.15f, 0.02f, 2.6f), paint);
            foreach (float cz in new[] { 26f, 42f })
                for (int i = -5; i <= 5; i++)
                    Box("Crosswalk", env, new Vector3(i * 0.95f, 0.012f, cz), new Vector3(0.5f, 0.02f, 2.6f), paint);
            Box("StopLine", env, new Vector3(2.7f, 0.012f, 23.8f), new Vector3(5.2f, 0.02f, 0.35f), paint);
            Box("StopLine", env, new Vector3(-2.7f, 0.012f, 44.2f), new Vector3(5.2f, 0.02f, 0.35f), paint);
        }

        static void Buildings(Transform env)
        {
            string[] cols = { Wall, "#CFC7B8", "#BFB8AA", "#D6CFC0" };
            var glass = Mat("Glass", "#3A4350", 0.85f, 0.2f);
            var rng = Rng(11);
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                for (float z = -4; z < 110; z += 17f)
                {
                    if (Mathf.Abs(z - 34f) < 14f) continue; // keep the crossing clear
                    float h = rng.Range(9f, 22f), w = rng.Range(12f, 15f), x = sx * 27f;
                    var b = Box("Building", env, new Vector3(x, h * 0.5f, z), new Vector3(10f, h, w), Mat("Bld" + (n % cols.Length), cols[n % cols.Length], 0.1f));
                    for (float y = 3f; y < h - 1.5f; y += 3.2f)
                        Box("Windows", env, new Vector3(x - sx * 5.03f, y, z), new Vector3(0.1f, 1.5f, w * 0.86f), glass);
                    n++;
                }
        }

        static void Furniture(Transform env)
        {
            var metal = Mat("LampMetal", Metal, 0.5f, 0.6f);
            var lampLens = Mat("LampWhite", "#FFF6DC", 0.3f);
            for (float z = 2; z < 115; z += 14f)
                foreach (float sx in new[] { -1f, 1f })
                {
                    if (z > 28f && z < 44f) continue;
                    Prim(PrimitiveType.Cylinder, "StreetLampPole", env, new Vector3(sx * 8.2f, 3.2f, z), new Vector3(0.12f, 3.2f, 0.12f), metal);
                    Box("StreetLampArm", env, new Vector3(sx * 7.5f, 6.3f, z), new Vector3(1.6f, 0.1f, 0.1f), metal);
                    Box("StreetLampHead", env, new Vector3(sx * 6.8f, 6.2f, z), new Vector3(0.6f, 0.12f, 0.3f), lampLens);
                }
        }

        static void Grass(Transform sem)
        {
            var g = Group("Grass", sem).transform;
            int i = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { -5f, 4.5f, 14f, 23.5f, 44.5f, 54f, 64f, 74f, 84f, 94f })
                {
                    string state = (i % 7 == 3) ? "Dry" : "Green"; // a couple of dry lawns for variation
                    TaggedPrim(PrimitiveType.Cube, $"Lawn_{(sx < 0 ? "L" : "R")}{z:0}", g, new Vector3(sx * 13.7f, 0.04f, z), new Vector3(10f, 0.1f, 9f), C.Grass, state, smooth: 0.05f);
                    i++;
                }
        }

        static void Trees(Transform sem, Transform env)
        {
            var g = Group("Trees", sem).transform;
            var trunk = Mat("Trunk", Trunk, 0.1f);
            var rng = Rng(5);
            int i = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { 2f, 14f, 50f, 62f, 76f, 90f })
                {
                    float s = rng.Range(0.9f, 1.25f), x = sx * rng.Range(11f, 14f), zz = z + rng.Range(-2f, 2f);
                    var t = Group("Tree", g, new Vector3(x, 0, zz));
                    Ignore(Prim(PrimitiveType.Cylinder, "Trunk", t.transform, new Vector3(0, 1.3f * s, 0), new Vector3(0.3f, 1.3f * s, 0.3f), trunk));
                    TaggedPrim(PrimitiveType.Sphere, "Canopy", t.transform, new Vector3(0, 3.6f * s, 0), Vector3.one * 3.4f * s, C.Foliage, i % 2 == 0 ? "Leaf" : "Dark", smooth: 0.1f);
                    TaggedPrim(PrimitiveType.Sphere, "Canopy", t.transform, new Vector3(0.4f * s, 4.9f * s, 0.2f), Vector3.one * 2.3f * s, C.Foliage, i % 2 == 0 ? "Dark" : "Leaf", smooth: 0.1f);
                    i++;
                }
        }

        // ---------- traffic signals (FSM) ----------
        static SemanticTag SignalHead(Transform parent, string name, Vector3 pos, float yaw)
        {
            var g = Group(name, parent, pos, new Vector3(0, yaw, 0)).transform;
            var metal = Mat("SignalMetal", Metal, 0.5f, 0.6f);
            var housing = Mat("SignalHousing", DarkMetal, 0.35f);
            Ignore(Prim(PrimitiveType.Cylinder, "Pole", g, new Vector3(0, 2.4f, 0.0f), new Vector3(0.16f, 2.4f, 0.16f), metal));
            Ignore(Box("Housing", g, new Vector3(0, 4.15f, -0.05f), new Vector3(0.52f, 1.4f, 0.36f), housing));
            // dark ("unlit") lenses below, the live lens on top
            Ignore(DiscZ("DarkLens", g, new Vector3(0, 3.8f, -0.24f), 0.34f, 0.04f, Mat("DarkLens", "#1A1C1F", 0.7f)));
            Ignore(DiscZ("DarkLens", g, new Vector3(0, 4.15f, -0.24f), 0.34f, 0.04f, Mat("DarkLens", "#1A1C1F", 0.7f)));
            Ignore(Box("Visor", g, new Vector3(0, 4.62f, -0.26f), new Vector3(0.44f, 0.04f, 0.2f), housing));
            var cls = Class(C.TrafficSignal);
            var go = Prim(PrimitiveType.Cylinder, "Lens", g, new Vector3(0, 4.5f, -0.24f), new Vector3(0.34f, 0.02f, 0.34f), StateMat(cls, "Red", 0.85f), new Vector3(90, 0, 0));
            return Tag(go, C.TrafficSignal, "Red");
        }

        static void Signals(Transform sem, Transform env)
        {
            var g = Group("TrafficSignals", sem).transform;
            var ns1 = SignalHead(g, "Signal_NS_L", new Vector3(-7.2f, 0, 25.2f), 0f);
            var ns2 = SignalHead(g, "Signal_NS_R", new Vector3(7.2f, 0, 25.2f), 0f);
            var ew1 = SignalHead(g, "Signal_EW_W", new Vector3(7.2f, 0, 40.8f), 90f);   // faces -X (traffic from the west)
            var ew2 = SignalHead(g, "Signal_EW_E", new Vector3(-7.2f, 0, 27.6f), -90f);   // faces +X (traffic from the east)
            // Cycle 16 s: Green 6, Amber 2, Red 8. The EW axis runs 8 s out of phase, so exactly one axis is ever green.
            var fsm = Fsm("SignalController", g, ("Green", 6f), ("Amber", 2f), ("Red", 8f));
            fsm.AddTarget(ns1, 0f).AddTarget(ns2, 0f).AddTarget(ew1, 8f).AddTarget(ew2, 8f);
            fsm.Evaluate();
        }

        // ---------- road signs ----------
        static void Sign(Transform parent, string name, Vector3 pos, string state)
        {
            var g = Group(name, parent, pos).transform;
            var metal = Mat("SignMetal", Metal, 0.5f, 0.6f);
            var white = Mat("SignWhite", White, 0.2f);
            var black = Mat("SignBlack", "#1D1F22", 0.2f);
            Ignore(Prim(PrimitiveType.Cylinder, "Pole", g, new Vector3(0, 1.3f, 0), new Vector3(0.09f, 1.3f, 0.09f), metal));
            float y = 2.7f;
            switch (state)
            {
                case "Prohibit":
                    Ignore(DiscZ("Backing", g, new Vector3(0, y, 0.02f), 1.0f, 0.03f, metal));
                    TaggedPrim(PrimitiveType.Cylinder, "Face", g, new Vector3(0, y, -0.01f), new Vector3(0.9f, 0.02f, 0.9f), C.RoadSign, state, new Vector3(90, 0, 0), 0.4f);
                    Ignore(Box("Bar", g, new Vector3(0, y, -0.035f), new Vector3(0.62f, 0.14f, 0.02f), white));
                    break;
                case "Warning":
                    Ignore(Box("Backing", g, new Vector3(0, y, 0.02f), new Vector3(1.12f, 1.12f, 0.03f), metal, new Vector3(0, 0, 45)));
                    TaggedPrim(PrimitiveType.Cube, "Face", g, new Vector3(0, y, -0.01f), new Vector3(1.0f, 1.0f, 0.03f), C.RoadSign, state, new Vector3(0, 0, 45), 0.4f);
                    Ignore(Box("Mark", g, new Vector3(0, y + 0.06f, -0.035f), new Vector3(0.09f, 0.42f, 0.02f), black));
                    Ignore(Box("Mark", g, new Vector3(0, y - 0.26f, -0.035f), new Vector3(0.12f, 0.12f, 0.02f), black));
                    break;
                case "Mandatory":
                    Ignore(DiscZ("Backing", g, new Vector3(0, y, 0.02f), 1.0f, 0.03f, metal));
                    TaggedPrim(PrimitiveType.Cylinder, "Face", g, new Vector3(0, y, -0.01f), new Vector3(0.9f, 0.02f, 0.9f), C.RoadSign, state, new Vector3(90, 0, 0), 0.4f);
                    Ignore(Box("Arrow", g, new Vector3(0, y - 0.05f, -0.035f), new Vector3(0.12f, 0.5f, 0.02f), white));
                    Ignore(Box("ArrowHead", g, new Vector3(0, y + 0.26f, -0.035f), new Vector3(0.28f, 0.28f, 0.02f), white, new Vector3(0, 0, 45)));
                    break;
                default: // Info (green rectangle)
                    Ignore(Box("Backing", g, new Vector3(0, y, 0.02f), new Vector3(1.5f, 0.9f, 0.03f), metal));
                    TaggedPrim(PrimitiveType.Cube, "Face", g, new Vector3(0, y, -0.01f), new Vector3(1.4f, 0.8f, 0.03f), C.RoadSign, state, null, 0.4f);
                    for (int i = 0; i < 3; i++) Ignore(Box("Text", g, new Vector3(0, y + 0.2f - i * 0.2f, -0.035f), new Vector3(1.0f - i * 0.2f, 0.07f, 0.02f), white));
                    break;
            }
        }

        static void Signs(Transform sem, Transform env)
        {
            var g = Group("RoadSigns", sem).transform;
            Sign(g, "Sign_Prohibit", new Vector3(8.0f, 0, 6f), "Prohibit");
            Sign(g, "Sign_Mandatory", new Vector3(8.0f, 0, 12f), "Mandatory");
            Sign(g, "Sign_Stop", new Vector3(8.0f, 0, 20f), "Prohibit");
            Sign(g, "Sign_Warning", new Vector3(-8.0f, 0, 9f), "Warning");
            Sign(g, "Sign_Info", new Vector3(-8.0f, 0, 17f), "Info");
            Sign(g, "Sign_Info_Far", new Vector3(8.0f, 0, 56f), "Info");
            Sign(g, "Sign_Warning_Far", new Vector3(-8.0f, 0, 62f), "Warning");
        }

        // ---------- cars ----------
        static void Car(Transform parent, string name, Vector3 pos, float yaw, string state)
        {
            var g = Group(name, parent, pos, new Vector3(0, yaw, 0)).transform;
            TaggedPrim(PrimitiveType.Cube, "Body", g, new Vector3(0, 0.68f, 0), new Vector3(1.85f, 0.7f, 4.3f), C.Vehicle, state, null, 0.55f);
            var trim = Ignore(Group("Trim", g)).transform;
            Box("Cabin", trim, new Vector3(0, 1.28f, -0.2f), new Vector3(1.6f, 0.58f, 2.2f), Mat("Glass", "#3A4350", 0.85f, 0.2f));
            var tyre = Mat("Tyre", "#17181A", 0.2f);
            foreach (float sx in new[] { -0.95f, 0.95f })
                foreach (float sz in new[] { -1.35f, 1.35f })
                    Prim(PrimitiveType.Cylinder, "Wheel", trim, new Vector3(sx, 0.36f, sz), new Vector3(0.72f, 0.12f, 0.72f), tyre, new Vector3(0, 0, 90));
            var head = Mat("Headlamp", "#FFF3D0", 0.5f); var tail = Mat("Taillamp", "#8E1F1C", 0.5f);
            foreach (float sx in new[] { -0.62f, 0.62f })
            {
                Box("Headlamp", trim, new Vector3(sx, 0.78f, 2.16f), new Vector3(0.38f, 0.16f, 0.05f), head);
                Box("Taillamp", trim, new Vector3(sx, 0.78f, -2.16f), new Vector3(0.38f, 0.14f, 0.05f), tail);
            }
        }

        static void Cars(Transform sem)
        {
            var g = Group("Cars", sem).transform;
            Car(g, "Car_Red", new Vector3(2.3f, 0, 18f), 0, "Red");
            Car(g, "Car_Green", new Vector3(2.3f, 0, 10.5f), 0, "Green");
            Car(g, "Car_Silver", new Vector3(2.3f, 0, 3f), 0, "Silver");
            Car(g, "Car_Blue", new Vector3(-2.3f, 0, 24f), 180, "Blue");
            Car(g, "Car_Yellow", new Vector3(-2.3f, 0, 47f), 180, "Yellow");
            Car(g, "Car_Green2", new Vector3(-2.3f, 0, 62f), 180, "Green");
            Car(g, "Car_Red2", new Vector3(2.3f, 0, 54f), 0, "Red");
            Car(g, "Car_ParkedRed", new Vector3(-4.35f, 0, 6f), 0, "Red");
            Car(g, "Car_ParkedGreen", new Vector3(-4.35f, 0, 12.5f), 0, "Green");
            Car(g, "Car_ParkedBlue", new Vector3(4.35f, 0, 8f), 0, "Blue");
        }

        // ---------- roadworks with flashing hazard lights ----------
        static void RoadWorks(Transform sem, Transform env)
        {
            var g = Group("RoadWorks", sem, new Vector3(0, 0, 0.5f)).transform;
            var orange = Mat("BarrierOrange", "#D9742A", 0.3f); var white = Mat("BarrierWhite", White, 0.3f);
            var metal = Mat("LampMetal", Metal, 0.5f, 0.6f);
            var decor = Ignore(Group("Barrier", g)).transform;
            for (int i = 0; i < 6; i++)
                Box("Plank", decor, new Vector3(-4.1f + i * 0.9f, 0.9f, 0), new Vector3(0.86f, 0.22f, 0.06f), i % 2 == 0 ? orange : white);
            Box("Leg", decor, new Vector3(-4.3f, 0.45f, 0), new Vector3(0.08f, 0.9f, 0.08f), metal);
            Box("Leg", decor, new Vector3(1.2f, 0.45f, 0), new Vector3(0.08f, 0.9f, 0.08f), metal);
            var cls = Class(C.HazardLight);
            SemanticTag Lamp(string n, float x)
            {
                var go = Prim(PrimitiveType.Sphere, n, g, new Vector3(x, 1.12f, 0), Vector3.one * 0.26f, StateMat(cls, "Amber", 0.6f));
                return Tag(go, C.HazardLight, "Amber");
            }
            var l1 = Lamp("HazardLamp_A", -4.3f); var l2 = Lamp("HazardLamp_B", 1.2f);
            var fsm = Fsm("HazardFlasher", g, ("Amber", 0.5f), ("Off", 0.5f));
            fsm.AddTarget(l1, 0f).AddTarget(l2, 0.5f); // alternating
            fsm.Evaluate();
        }
    }
}
