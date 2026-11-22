using System.Collections.Generic;
using SemanticVR.Core;
using UnityEngine;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>02_Signals: station platform with a departure/status board, rail signals (gantry + masts) and a level crossing,
    /// all driven by SemanticStateMachine timers.</summary>
    public static class Scene02Signals
    {
        const float PlatformTop = 0.9f;

        public static void Build()
        {
            var scene = Begin("02_Signals");
            LightAndSkybox();
            var env = Ignore(Group("Environment", null)).transform;
            var sem = Group("Semantic", null).transform;

            Trackbed(env);
            Platform(env);
            SkyDome(sem);
            RailSignals(sem);
            Crossing(sem, env);
            DepartureBoard(sem);
            Rig(new Vector3(-5.5f, PlatformTop, -10f), 0f, 10f);
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        static void Trackbed(Transform env)
        {
            var ground = Mat("FarGround", "#FFFFFF", 0.08f);
            ground.SetTexture("_BaseMap", CheckerTexture()); ground.SetTextureScale("_BaseMap", new Vector2(40, 40));
            Prim(PrimitiveType.Plane, "FarGround", env, new Vector3(0, -0.1f, 40), new Vector3(40, 1, 40), ground);
            var ballast = Mat("Ballast", "#8C867D", 0.05f); var sleeper = Mat("Sleeper", "#5B4A3B", 0.1f); var rail = Mat("Rail", "#9AA0A6", 0.7f, 0.8f);
            Box("Ballast", env, new Vector3(2.1f, 0.05f, 40f), new Vector3(9.2f, 0.2f, 130f), ballast);
            foreach (float cx in new[] { 0f, 4.2f })
            {
                for (float z = -22; z < 100; z += 0.9f) Box("Sleeper", env, new Vector3(cx, 0.17f, z), new Vector3(2.5f, 0.12f, 0.26f), sleeper);
                foreach (float rx in new[] { -0.72f, 0.72f }) Box("Rail", env, new Vector3(cx + rx, 0.29f, 40f), new Vector3(0.08f, 0.14f, 130f), rail);
            }
        }

        static void Platform(Transform env)
        {
            var conc = Mat("PlatformConcrete", Concrete, 0.1f); var edge = Mat("PlatformEdge", "#E6D36A", 0.2f);
            var dark = Mat("CanopyDark", "#454A51", 0.3f); var post = Mat("CanopyPost", Metal, 0.5f, 0.6f);
            var bench = Mat("Bench", Wood, 0.2f);
            Box("Platform", env, new Vector3(-5.4f, PlatformTop * 0.5f, 10f), new Vector3(6f, PlatformTop, 56f), conc);
            Box("SafetyEdge", env, new Vector3(-2.65f, PlatformTop + 0.005f, 10f), new Vector3(0.3f, 0.015f, 56f), edge);
            Box("CanopyRoof", env, new Vector3(-5.6f, 5.6f, 8f), new Vector3(7f, 0.2f, 40f), dark);
            for (float z = -10; z <= 26; z += 9f)
                foreach (float x in new[] { -7.8f, -3.2f }) Box("CanopyPost", env, new Vector3(x, PlatformTop + 2.35f, z), new Vector3(0.22f, 4.7f, 0.22f), post);
            for (int i = 0; i < 5; i++) Box("Bench", env, new Vector3(-4.0f, PlatformTop + 0.5f, -6f + i * 8f), new Vector3(0.5f, 0.1f, 2.0f), bench);
        }

        // ---------- rail signals: gantry + masts ----------
        static SemanticTag RailHead(Transform parent, string name, Vector3 pos)
        {
            var g = Group(name, parent, pos).transform;
            var housing = Mat("RailHousing", DarkMetal, 0.35f);
            Ignore(Box("Housing", g, new Vector3(0, 0, 0f), new Vector3(0.8f, 0.8f, 0.4f), housing));
            Ignore(Box("Hood", g, new Vector3(0, 0.46f, -0.14f), new Vector3(0.72f, 0.05f, 0.4f), housing));
            Ignore(Box("Backplate", g, new Vector3(0, 0, 0.14f), new Vector3(0.95f, 0.95f, 0.04f), Mat("RailBackplate", "#E8E8E4", 0.2f)));
            var cls = Class(C.RailSignal);
            var go = Prim(PrimitiveType.Cylinder, "Lens", g, new Vector3(0, 0, -0.22f), new Vector3(0.58f, 0.02f, 0.58f), StateMat(cls, "Red", 0.85f), new Vector3(90, 0, 0));
            return Tag(go, C.RailSignal, "Red");
        }

        static void RailSignals(Transform sem)
        {
            var g = Group("RailSignals", sem).transform;
            var metal = Mat("GantryMetal", Metal, 0.5f, 0.6f);
            var decor = Ignore(Group("Gantry", g)).transform;
            Box("Beam", decor, new Vector3(2.1f, 5.4f, 22f), new Vector3(8.4f, 0.25f, 0.25f), metal);
            Box("PostL", decor, new Vector3(-2.1f, 2.7f, 22f), new Vector3(0.25f, 5.4f, 0.25f), metal);
            Box("PostR", decor, new Vector3(6.3f, 2.7f, 22f), new Vector3(0.25f, 5.4f, 0.25f), metal);
            var a1 = RailHead(g, "Signal_A_Gantry", new Vector3(0f, 4.6f, 21.7f));
            var b1 = RailHead(g, "Signal_B_Gantry", new Vector3(4.2f, 4.6f, 21.7f));
            var mast = Ignore(Group("Masts", g)).transform;
            Prim(PrimitiveType.Cylinder, "MastA", mast, new Vector3(2.1f, 1.8f, 33f), new Vector3(0.14f, 1.8f, 0.14f), metal);
            Prim(PrimitiveType.Cylinder, "MastB", mast, new Vector3(6.0f, 1.8f, 33f), new Vector3(0.14f, 1.8f, 0.14f), metal);
            var a2 = RailHead(g, "Signal_A_Mast", new Vector3(2.1f, 3.7f, 32.7f));
            var b2 = RailHead(g, "Signal_B_Mast", new Vector3(6.0f, 3.7f, 32.7f));
            // Aspect cycle R -> Y -> G -> Y (15 s). Heads are offset so every aspect is always on show somewhere.
            var fsm = Fsm("RailController", g, ("Red", 5f), ("Yellow", 2.5f), ("Green", 5f), ("Yellow", 2.5f));
            fsm.AddTarget(a1, 0f).AddTarget(b1, 5f).AddTarget(a2, 7.5f).AddTarget(b2, 12.5f);
            fsm.Evaluate();
        }

        // ---------- level crossing: alternating red flashers ----------
        static void Crossing(Transform sem, Transform env)
        {
            var g = Group("LevelCrossing", sem).transform;
            Box("CrossingRoad", env, new Vector3(14f, 0.14f, 46f), new Vector3(32f, 0.04f, 4.5f), Mat("Asphalt", Asphalt, 0.12f));
            var white = Mat("CrossingWhite", White, 0.25f); var red = Mat("CrossingRedWhite", "#C9302C", 0.25f); var metal = Mat("LampMetal", Metal, 0.5f, 0.6f);
            var cls = Class(C.HazardLight);
            var lamps = new List<SemanticTag>();
            foreach (float px in new[] { -1.9f, 6.3f })
            {
                var post = Group("Post", g, new Vector3(px, 0, 43.4f)).transform;
                var decor = Ignore(Group("Furniture", post)).transform;
                Prim(PrimitiveType.Cylinder, "Mast", decor, new Vector3(0, 1.5f, 0), new Vector3(0.12f, 1.5f, 0.12f), metal);
                Box("Panel", decor, new Vector3(0, 2.75f, 0f), new Vector3(1.3f, 0.55f, 0.06f), Mat("CrossingPanel", "#E8E8E4", 0.2f));
                Box("CrossA", decor, new Vector3(0, 3.35f, 0), new Vector3(1.5f, 0.16f, 0.05f), red, new Vector3(0, 0, 22));
                Box("CrossB", decor, new Vector3(0, 3.35f, 0), new Vector3(1.5f, 0.16f, 0.05f), white, new Vector3(0, 0, -22));
                foreach (float lx in new[] { -0.36f, 0.36f })
                {
                    var go = Prim(PrimitiveType.Sphere, "Flasher", post, new Vector3(lx, 2.75f, -0.08f), Vector3.one * 0.3f, StateMat(cls, "Red", 0.6f));
                    lamps.Add(Tag(go, C.HazardLight, "Off"));
                }
            }
            var fsm = Fsm("CrossingFlasher", g, ("Red", 0.5f), ("Off", 0.5f));
            for (int i = 0; i < lamps.Count; i++) fsm.AddTarget(lamps[i], (i % 2) * 0.5f); // left/right alternate
            fsm.Evaluate();
        }

        // ---------- departure/status board ----------
        static void DepartureBoard(Transform sem)
        {
            var g = Group("DepartureBoard", sem, new Vector3(-5.6f, PlatformTop, 9f)).transform;
            var frame = Mat("BoardFrame", "#1E2126", 0.4f); var screen = Mat("BoardScreen", "#0F1318", 0.6f);
            var txt = Mat("BoardText", "#E8E6DF", 0.2f); var dim = Mat("BoardTextDim", "#8C9096", 0.2f);
            var decor = Ignore(Group("Frame", g)).transform;
            Box("Post", decor, new Vector3(-1.9f, 1.3f, 0.2f), new Vector3(0.14f, 2.6f, 0.14f), frame);
            Box("Post", decor, new Vector3(1.9f, 1.3f, 0.2f), new Vector3(0.14f, 2.6f, 0.14f), frame);
            Box("Frame", decor, new Vector3(0, 3.1f, 0.1f), new Vector3(4.6f, 3.1f, 0.2f), frame);
            Box("Screen", decor, new Vector3(0, 3.1f, -0.02f), new Vector3(4.4f, 2.9f, 0.04f), screen);
            for (int i = 0; i < 3; i++) Box("Header", decor, new Vector3(-1.6f + i * 1.0f, 4.35f, -0.05f), new Vector3(0.8f - i * 0.1f, 0.1f, 0.02f), dim);

            var cells = new List<SemanticTag>();
            for (int i = 0; i < 6; i++)
            {
                float y = 3.95f - i * 0.46f;
                Box("Time", decor, new Vector3(-1.75f, y, -0.05f), new Vector3(0.55f, 0.12f, 0.02f), txt);
                Box("Destination", decor, new Vector3(-0.55f, y, -0.05f), new Vector3(1.3f + 0.12f * (i % 3), 0.12f, 0.02f), txt);
                Box("Platform", decor, new Vector3(0.75f, y, -0.05f), new Vector3(0.22f, 0.12f, 0.02f), dim);
                cells.Add(TaggedPrim(PrimitiveType.Cube, $"Status_{i + 1}", g, new Vector3(1.6f, y, -0.06f), new Vector3(0.85f, 0.32f, 0.04f), C.StatusPanel, "OnTime", null, 0.3f));
            }
            // 14 s cycle; rows are 3 s out of phase so on-time / delayed / cancelled are always side by side
            var fsm = Fsm("BoardController", g, ("OnTime", 7f), ("Delayed", 3.5f), ("Cancelled", 3.5f));
            for (int i = 0; i < cells.Count; i++) fsm.AddTarget(cells[i], i * 3f);
            fsm.Evaluate();
        }
    }
}
