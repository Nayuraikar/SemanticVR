using System.Collections.Generic;
using SemanticVR.Core;
using UnityEditor;
using UnityEngine;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>
    /// 05_NightCity: a shopping street with two lighting presets (Day, Night; N switches at runtime). Painted shop signs and
    /// emissive neon signs hang on gantries in clusters whose colours were chosen with python/scripts/phase2a_nightcity.py so that
    /// some pairs are deutan-confusable in one preset only (see docs/design/phase2a.md). Both presets use a flat ambient colour
    /// (ambient SH = L0 only), so the Python design numbers are exact; the sun/moon is the only directional light.
    /// The python script's PRESETS must match <see cref="Day"/> and <see cref="Night"/>.
    /// </summary>
    public static class Scene05NightCity
    {
        public static LightingPreset Day => new LightingPreset
        {
            name = "Day", sunColor = Hex("#FFF1DC"), sunIntensity = 1.25f, sunEuler = new Vector3(48f, -32f, 0f), ambient = Hex("#8FA0B8"),
            skyTop = Hex("#6C8DB5"), skyHorizon = Hex("#E9E2D6"), skyBottom = Hex("#B5B0A6"), skyState = "Day", nightProps = false,
        };

        public static LightingPreset Night => new LightingPreset
        {
            name = "Night", sunColor = Hex("#7F9CFF"), sunIntensity = 0.12f, sunEuler = new Vector3(35f, -32f, 0f), ambient = Hex("#6B4426"),
            skyTop = Hex("#080E22"), skyHorizon = Hex("#2B2230"), skyBottom = Hex("#14121A"), skyState = "Night", nightProps = true,
        };

        public static void Build()
        {
            var scene = Begin("05_NightCity");
            LightAndSkybox();
            C.EnsureState(C.Sky, SemanticClass.State("Night", Hex("#1B2A55")));
            var env = Ignore(Group("Environment", null)).transform;
            var sem = Group("Semantic", null).transform;
            var nightProps = new List<GameObject>();

            Street(env);
            Buildings(env, nightProps);
            Lamps(env, nightProps);
            var sky = SkyDome(sem);
            Signs(sem, env);
            Cars(sem);
            var rig = Rig(new Vector3(0, 0, -4f), 0f, 6f);

            var sw = rig.gameObject.AddComponent<LightingPresetSwitcher>();
            sw.sun = RenderSettings.sun; sw.skyTag = sky; sw.nightProps = nightProps;
            sw.presets = new List<LightingPreset> { Day, Night };
            sw.current = 0;
            rig.gameObject.AddComponent<SemanticVR.Confusion.LightingChangeDetector>();
            // bake the Day preset into the saved scene (flat ambient, sun, sky gradient); at runtime the switcher takes over
            LightingPresetSwitcher.ApplyToScene(Day, RenderSettings.sun, RenderSettings.skybox);
            foreach (var go in nightProps) go.SetActive(false);
            EditorUtility.SetDirty(RenderSettings.skybox);
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        static void Street(Transform env)
        {
            var asphalt = Mat("Asphalt", Asphalt, 0.12f);
            var walk = Mat("Sidewalk", Concrete, 0.1f);
            var curb = Mat("Curb", ConcreteDark, 0.1f);
            var paint = Mat("RoadPaint", White, 0.2f);
            var ground = Mat("FarGround", "#FFFFFF", 0.08f);
            ground.SetTexture("_BaseMap", CheckerTexture()); ground.SetTextureScale("_BaseMap", new Vector2(40, 40));
            Prim(PrimitiveType.Plane, "FarGround", env, new Vector3(0, -0.1f, 40), new Vector3(40, 1, 40), ground);
            Box("Road", env, new Vector3(0, -0.05f, 40), new Vector3(11, 0.1f, 110), asphalt);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box("Sidewalk", env, new Vector3(sx * 7.0f, 0.07f, 40), new Vector3(3f, 0.15f, 110), walk);
                Box("Curb", env, new Vector3(sx * 5.6f, 0.08f, 40), new Vector3(0.2f, 0.17f, 110), curb);
            }
            for (float z = -8; z < 90; z += 6f) Box("Dash", env, new Vector3(0, 0.01f, z), new Vector3(0.15f, 0.02f, 2.6f), paint);
        }

        static void Buildings(Transform env, List<GameObject> nightProps)
        {
            string[] cols = { Wall, "#CFC7B8", "#BFB8AA", "#D6CFC0" };
            var glass = Mat("Glass", "#3A4350", 0.85f, 0.2f);
            var glow = UnlitMat("WindowGlow", Hex("#FFC870"));
            var rng = Rng(21);
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                for (float z = -2; z < 88; z += 13f)
                {
                    float h = rng.Range(9f, 18f), w = 12f, x = sx * 17.5f;
                    Box("Building", env, new Vector3(x, h * 0.5f, z), new Vector3(10f, h, w), Mat("Bld" + (n % cols.Length), cols[n % cols.Length], 0.1f));
                    for (float y = 3f; y < h - 1.5f; y += 3.2f)
                    {
                        Box("Windows", env, new Vector3(x - sx * 5.03f, y, z), new Vector3(0.1f, 1.5f, w * 0.86f), glass);
                        // lit windows (night only): a few warm panes per floor
                        for (int k = 0; k < 4; k++)
                            if (rng.Range(0f, 1f) > 0.45f)
                                nightProps.Add(Box("WindowLit", env, new Vector3(x - sx * 5.06f, y, z - 4.2f + k * 2.8f), new Vector3(0.05f, 1.2f, 1.9f), glow));
                    }
                    n++;
                }
        }

        static void Lamps(Transform env, List<GameObject> nightProps)
        {
            var metal = Mat("LampMetal", Metal, 0.5f, 0.6f);
            var lens = UnlitMat("LampGlow", Hex("#FFB45A"));
            var dull = Mat("LampDull", "#CDBFA8", 0.3f);
            for (float z = 2; z < 88; z += 12f)
                foreach (float sx in new[] { -1f, 1f })
                {
                    Prim(PrimitiveType.Cylinder, "LampPole", env, new Vector3(sx * 8.2f, 3.2f, z), new Vector3(0.12f, 3.2f, 0.12f), metal);
                    Box("LampArm", env, new Vector3(sx * 7.5f, 6.3f, z), new Vector3(1.6f, 0.1f, 0.1f), metal);
                    Box("LampHeadDay", env, new Vector3(sx * 6.8f, 6.2f, z), new Vector3(0.6f, 0.12f, 0.3f), dull);
                    nightProps.Add(Box("LampHeadNight", env, new Vector3(sx * 6.8f, 6.2f, z), new Vector3(0.6f, 0.14f, 0.3f), lens));
                }
        }

        // ---------- shop and neon signs ----------
        // Each cluster hangs on a gantry over the street. The pairs inside a cluster are the design targets (docs/design/phase2a.md).
        // (z, side, (class, state, tagName)...)
        static readonly (float z, int side, (int cls, string state, string name)[] signs)[] Clusters =
        {
            // A: Neon Red / Shop Green .......... confusable by DAY only
            (8f,  -1, new[] { (C.NeonSign, "Red", "Sign_NeonRed_A"), (C.ShopSign, "Green", "Sign_ShopGreen_A") }),
            // B: Shop Yellow / Neon Amber ....... confusable by DAY only
            (15f,  1, new[] { (C.ShopSign, "Yellow", "Sign_ShopYellow_B"), (C.NeonSign, "Amber", "Sign_NeonAmber_B") }),
            // C: Shop Green / Shop Pink ......... confusable at NIGHT only
            (22f, -1, new[] { (C.ShopSign, "Green", "Sign_ShopGreen_C"), (C.ShopSign, "Pink", "Sign_ShopPink_C") }),
            // D: Violet / Teal and Blue / Teal .. confusable at NIGHT only; Blue / Violet confusable in both
            (29f,  1, new[] { (C.ShopSign, "Violet", "Sign_ShopViolet_D"), (C.ShopSign, "Teal", "Sign_ShopTeal_D"), (C.ShopSign, "Blue", "Sign_ShopBlue_D") }),
            // E: Red / Green / Olive ............ confusable in BOTH presets
            (36f, -1, new[] { (C.ShopSign, "Red", "Sign_ShopRed_E"), (C.ShopSign, "Green", "Sign_ShopGreen_E"), (C.ShopSign, "Olive", "Sign_ShopOlive_E") }),
            // F: neon variety
            (43f,  1, new[] { (C.NeonSign, "Cyan", "Sign_NeonCyan_F"), (C.NeonSign, "Magenta", "Sign_NeonMagenta_F"), (C.ShopSign, "Orange", "Sign_ShopOrange_F") }),
        };

        static void Signs(Transform sem, Transform env)
        {
            var g = Group("Signs", sem).transform;
            var metal = Mat("GantryMetal", DarkMetal, 0.4f, 0.5f);
            foreach (var (z, side, signs) in Clusters)
            {
                float x0 = side < 0 ? -5.2f : 1.8f;
                float span = signs.Length * 1.7f;
                Box("GantryBeam", env, new Vector3(x0 + span * 0.5f - 0.5f, 4.15f, z + 0.12f), new Vector3(span + 0.4f, 0.1f, 0.1f), metal);
                Box("GantryPost", env, new Vector3(side < 0 ? -5.6f : 5.6f, 2.1f, z + 0.12f), new Vector3(0.12f, 4.2f, 0.12f), metal);
                for (int i = 0; i < signs.Length; i++)
                {
                    float x = x0 + i * 1.7f;
                    var (cls, state, name) = signs[i];
                    Box("Rod", env, new Vector3(x, 3.95f, z + 0.12f), new Vector3(0.04f, 0.3f, 0.04f), metal);
                    Panel(name, g, new Vector3(x, 3.4f, z), new Vector2(1.5f, 0.75f), cls, state, 0.08f);
                }
            }
        }

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
        }

        static void Cars(Transform sem)
        {
            var g = Group("Cars", sem).transform;
            Car(g, "Car_Red", new Vector3(2.3f, 0, 12f), 0, "Red");
            Car(g, "Car_Green", new Vector3(2.3f, 0, 19f), 0, "Green");
            Car(g, "Car_Blue", new Vector3(-2.3f, 0, 26f), 180, "Blue");
            Car(g, "Car_Yellow", new Vector3(-2.3f, 0, 33f), 180, "Yellow");
        }
    }
}
