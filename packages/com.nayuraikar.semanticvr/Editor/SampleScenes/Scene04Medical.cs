using System.Collections.Generic;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>
    /// 04_Medical: ICU bay. Patient monitor (ECG/SpO2/BP/temp traces + alarm banners), medical-gas cylinders, a drug-syringe tray,
    /// an infusion pump and a patient with memory-colour skin plus cyanosis / wound tint regions.
    /// Inspired by, not a copy of, medical colour conventions (see docs/design/phase1b.md). +Z is towards the bed head.
    /// </summary>
    public static class Scene04Medical
    {
        public static void Build()
        {
            var scene = Begin("04_Medical");
            LightAndSkybox();
            var env = Ignore(Group("Environment", null)).transform;
            var sem = Group("Semantic", null).transform;

            Room(env);
            Bed(env);
            Patient(sem, env);
            Monitor(sem, env);
            GasRack(sem, env);
            DrugTray(sem, env);
            InfusionPump(sem, env);
            var sky = SkyDome(sem); sky.transform.localScale = new Vector3(-290f, 290f, 290f);

            var rig = Rig(new Vector3(0f, 0f, -1.2f), 0f, 1.2f);
            rig.eyeHeight = 1.55f; rig.convergenceDistance = 2.2f; rig.initialPitch = 8f; rig.moveSpeed = 1.6f;
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        // ---------- room ----------
        static void Room(Transform env)
        {
            var floor = Mat("ICUFloor", "#B9BEC0", 0.35f); var wall = Mat("ICUWall", "#DADCD6", 0.1f); var trim = Mat("ICUTrim", "#8E969B", 0.3f);
            var ceil = Mat("ICUCeiling", "#E6E7E2", 0.1f); var panel = UnlitMat("CeilingPanel", Hex("#FFFFFF"));
            Box("Floor", env, new Vector3(0, -0.05f, 2.5f), new Vector3(7f, 0.1f, 8f), floor);
            Box("BackWall", env, new Vector3(0, 1.5f, 6.55f), new Vector3(7f, 3f, 0.1f), wall);
            Box("LeftWall", env, new Vector3(-3.55f, 1.5f, 2.5f), new Vector3(0.1f, 3f, 8f), wall);
            Box("RightWall", env, new Vector3(3.55f, 1.5f, 2.5f), new Vector3(0.1f, 3f, 8f), wall);
            Box("FrontWall", env, new Vector3(0, 1.5f, -1.55f), new Vector3(7f, 3f, 0.1f), wall);
            var roof = Box("Ceiling", env, new Vector3(0, 3.05f, 2.5f), new Vector3(7f, 0.1f, 8f), ceil);
            roof.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;   // let the sun in
            Box("Skirting", env, new Vector3(0, 0.08f, 6.48f), new Vector3(7f, 0.16f, 0.05f), trim);
            for (int i = 0; i < 3; i++)
            {
                var p = Box("CeilingLight", env, new Vector3(-2f + i * 2f, 2.98f, 3f), new Vector3(1.2f, 0.03f, 0.6f), panel);
                p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            // headwall rail the monitor and gas outlets hang from
            Box("HeadwallRail", env, new Vector3(0, 1.0f, 6.45f), new Vector3(5.5f, 0.08f, 0.06f), trim);
        }

        static void Bed(Transform env)
        {
            var frame = Mat("BedFrame", "#9AA1A6", 0.5f, 0.5f); var mattress = Mat("Mattress", "#CFD6DA", 0.2f); var sheet = Mat("Sheet", "#B8D2DC", 0.15f);
            Box("BedBase", env, new Vector3(0, 0.35f, 3.9f), new Vector3(1.0f, 0.12f, 2.2f), frame);
            Box("BedLegs", env, new Vector3(0, 0.17f, 3.9f), new Vector3(0.8f, 0.3f, 1.9f), Mat("BedLegs", "#6E757B", 0.3f));
            Box("Mattress", env, new Vector3(0, 0.5f, 3.9f), new Vector3(0.92f, 0.18f, 2.05f), mattress);
            Box("HeadBoard", env, new Vector3(0, 0.85f, 5.0f), new Vector3(1.0f, 0.7f, 0.06f), frame);
            Box("RailL", env, new Vector3(-0.52f, 0.78f, 3.8f), new Vector3(0.04f, 0.04f, 1.3f), frame);
            Box("RailR", env, new Vector3(0.52f, 0.78f, 3.8f), new Vector3(0.04f, 0.04f, 1.3f), frame);
            Box("Pillow", env, new Vector3(0, 0.66f, 4.8f), new Vector3(0.5f, 0.1f, 0.3f), Mat("Pillow", "#E3E7EA", 0.1f));
            Box("Blanket", env, new Vector3(0, 0.68f, 3.55f), new Vector3(0.88f, 0.14f, 1.25f), sheet);
        }

        // ---------- patient: skin is a memory colour; cyanosis + wound tint regions sit on top of it ----------
        static void Patient(Transform sem, Transform env)
        {
            var g = Group("Patient", sem).transform;
            string skin = "Light";
            TaggedPrim(PrimitiveType.Sphere, "Head", g, new Vector3(0, 0.84f, 4.78f), new Vector3(0.24f, 0.28f, 0.26f), C.Skin, skin, smooth: 0.3f);
            TaggedPrim(PrimitiveType.Capsule, "Neck", g, new Vector3(0, 0.74f, 4.58f), new Vector3(0.11f, 0.08f, 0.11f), C.Skin, skin, new Vector3(90, 0, 0), 0.3f);
            // arms rest on the blanket: upper arm + forearm + hand
            foreach (float sx in new[] { -1f, 1f })
            {
                string n = sx < 0 ? "L" : "R";
                TaggedPrim(PrimitiveType.Capsule, "UpperArm_" + n, g, new Vector3(sx * 0.37f, 0.82f, 4.33f), new Vector3(0.1f, 0.2f, 0.1f), C.Skin, skin, new Vector3(90, 0, 0), 0.3f);
                TaggedPrim(PrimitiveType.Capsule, "Forearm_" + n, g, new Vector3(sx * 0.4f, 0.8f, 3.82f), new Vector3(0.085f, 0.24f, 0.085f), C.Skin, skin, new Vector3(90, 0, 0), 0.3f);
                TaggedPrim(PrimitiveType.Sphere, "Hand_" + n, g, new Vector3(sx * 0.4f, 0.8f, 3.5f), new Vector3(0.1f, 0.05f, 0.14f), C.Skin, skin, smooth: 0.3f);
            }
            // gown (not skin: non-semantic)
            Ignore(Box("Gown", g, new Vector3(0, 0.72f, 4.25f), new Vector3(0.55f, 0.12f, 0.6f), Mat("Gown", "#9FBFC6", 0.1f)));
            // cyanosis tint: lips + fingertips fade Pale <-> Cyanosis (FSM); wound patch + dressing on the left forearm
            var cyan = new List<SemanticTag>();
            cyan.Add(TaggedPrim(PrimitiveType.Sphere, "Lips", g, new Vector3(0, 0.80f, 4.655f), new Vector3(0.07f, 0.025f, 0.03f), C.Tissue, "Pale", smooth: 0.4f));
            foreach (float sx in new[] { -1f, 1f })
                for (int f = 0; f < 3; f++)
                    cyan.Add(TaggedPrim(PrimitiveType.Sphere, "Fingertip", g, new Vector3(sx * (0.4f + (f - 1) * 0.03f), 0.815f, 3.43f), Vector3.one * 0.026f, C.Tissue, "Pale", smooth: 0.4f));
            var fsm = Fsm("PerfusionFsm", g, ("Pale", 6f), ("Cyanosis", 6f));
            for (int i = 0; i < cyan.Count; i++) fsm.AddTarget(cyan[i], 0f);
            fsm.Evaluate();
            TaggedPrim(PrimitiveType.Sphere, "Wound", g, new Vector3(-0.4f, 0.845f, 3.78f), new Vector3(0.07f, 0.02f, 0.11f), C.Tissue, "Wound", smooth: 0.5f);
            Ignore(Box("Dressing", g, new Vector3(-0.4f, 0.835f, 3.9f), new Vector3(0.1f, 0.012f, 0.07f), Mat("Dressing", "#F1EFE9", 0.1f)));
            // ECG leads on the chest
            for (int i = 0; i < 3; i++) Ignore(Prim(PrimitiveType.Cylinder, "EcgPad", g, new Vector3(-0.1f + i * 0.1f, 0.795f, 4.35f), new Vector3(0.04f, 0.004f, 0.04f), Mat("EcgPad", "#D8D8D2", 0.2f)));
        }

        // ---------- patient monitor on the headwall ----------
        static List<Vector2> Wave(string kind, int n, float w, float h)
        {
            var pts = new List<Vector2>();
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1), x = (u - 0.5f) * w, y = 0f;
                switch (kind)
                {
                    case "ecg":   // QRS spikes roughly every 0.25 of the width
                    {
                        float ph = (u * 4f) % 1f;
                        y = ph < 0.08f ? -0.3f * Mathf.Sin(ph / 0.08f * Mathf.PI) : ph < 0.14f ? 1f * Mathf.Sin((ph - 0.08f) / 0.06f * Mathf.PI) : ph < 0.2f ? -0.4f * Mathf.Sin((ph - 0.14f) / 0.06f * Mathf.PI)
                            : (ph > 0.45f && ph < 0.65f) ? 0.25f * Mathf.Sin((ph - 0.45f) / 0.2f * Mathf.PI) : 0f;
                        break;
                    }
                    case "spo2": { float ph = (u * 4f) % 1f; y = Mathf.Pow(Mathf.Sin(ph * Mathf.PI), 2f) * (ph < 0.5f ? 1f : 0.7f) - 0.4f; break; }
                    case "bp":   { float ph = (u * 4f) % 1f; y = ph < 0.2f ? ph / 0.2f : 1f - Mathf.Pow((ph - 0.2f) / 0.8f, 0.7f) * 0.75f; y -= 0.45f; break; }
                    default:     y = 0.15f * Mathf.Sin(u * 5f) + (u - 0.5f) * 0.2f; break;   // slow temperature trend
                }
                pts.Add(new Vector2(x, y * h * 0.5f));
            }
            return pts;
        }

        static void Monitor(Transform sem, Transform env)
        {
            var g = Group("PatientMonitor", sem, new Vector3(0, 1.7f, 6.35f)).transform;
            Ignore(Box("Arm", g, new Vector3(0, -0.45f, 0.1f), new Vector3(0.06f, 0.5f, 0.06f), Mat("MonitorArm", "#6E757B", 0.5f, 0.5f)));
            Ignore(Box("Housing", g, new Vector3(0, 0, 0.04f), new Vector3(1.5f, 0.95f, 0.08f), Mat("MonitorHousing", "#1C1F23", 0.4f)));
            Ignore(Box("Screen", g, new Vector3(0, 0, 0f), new Vector3(1.42f, 0.88f, 0.01f), UnlitMat("MonitorScreen", Hex("#07090B"))));

            // alarm banners across the top: high (red) / medium (yellow) / low (cyan), cycled out of phase
            var hi = Panel("AlarmBanner_High", g, new Vector3(-0.48f, 0.37f, -0.012f), new Vector2(0.44f, 0.09f), C.AlarmHigh, "Red", 0.006f);
            var med = Panel("AlarmBanner_Medium", g, new Vector3(0.0f, 0.37f, -0.012f), new Vector2(0.44f, 0.09f), C.AlarmMedium, "Yellow", 0.006f);
            var lo = Panel("AlarmBanner_Low", g, new Vector3(0.48f, 0.37f, -0.012f), new Vector2(0.44f, 0.09f), C.AlarmLow, "Cyan", 0.006f);
            var f1 = Fsm("AlarmHighFsm", g, ("Red", 2.5f), ("Off", 3.5f)); f1.AddTarget(hi, 0f);
            var f2 = Fsm("AlarmMediumFsm", g, ("Yellow", 3f), ("Off", 3f)); f2.AddTarget(med, 1.5f);
            var f3 = Fsm("AlarmLowFsm", g, ("Cyan", 4f), ("Off", 2f)); f3.AddTarget(lo, 3f);
            f1.Evaluate(); f2.Evaluate(); f3.Evaluate();

            // four traces + a numeric block each (placeholder blocks, no font rendering)
            string[] kinds = { "ecg", "spo2", "bp", "temp" };
            string[] states = { "Ecg", "Spo2", "Bp", "Temp" };
            for (int i = 0; i < 4; i++)
            {
                float y = 0.2f - i * 0.17f;
                var mesh = Ribbon("Trace_" + states[i], Wave(kinds[i], 96, 1.0f, 0.15f), 0.007f);
                var go = new GameObject("Trace_" + states[i]);
                go.transform.SetParent(g, false); go.transform.localPosition = new Vector3(-0.17f, y, -0.012f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = StateMat(Class(C.VitalTrace), states[i], 0.3f);
                Tag(go, C.VitalTrace, states[i]);
                Panel("Value_" + states[i], g, new Vector3(0.55f, y, -0.012f), new Vector2(0.2f, 0.1f), C.VitalTrace, states[i], 0.006f);
                Ignore(Box("Label", g, new Vector3(-0.64f, y + 0.045f, -0.011f), new Vector3(0.1f, 0.02f, 0.003f), Mat("MonitorLabel", "#7A8087", 0.1f)));
            }
        }

        // ---------- medical-gas cylinders against the wall ----------
        static void GasRack(Transform sem, Transform env)
        {
            var g = Group("GasRack", sem, new Vector3(-2.3f, 0, 5.9f)).transform;
            Ignore(Box("Rack", g, new Vector3(0, 0.45f, 0.18f), new Vector3(2.4f, 0.06f, 0.06f), Mat("RackBar", "#6E757B", 0.5f, 0.5f)));
            string[] states = { "Oxygen", "Helium", "Air", "NitrousOxide", "CO2", "Vacuum" };
            var metal = Mat("CylinderMetal", "#9AA0A6", 0.6f, 0.7f);
            for (int i = 0; i < states.Length; i++)
            {
                float x = -1.0f + i * 0.4f;
                var c = Group("Cylinder_" + states[i], g, new Vector3(x, 0, 0)).transform;
                Ignore(Prim(PrimitiveType.Cylinder, "Base", c, new Vector3(0, 0.02f, 0), new Vector3(0.22f, 0.02f, 0.22f), metal));
                TaggedPrim(PrimitiveType.Cylinder, "Body", c, new Vector3(0, 0.55f, 0), new Vector3(0.2f, 0.5f, 0.2f), C.GasCylinder, states[i], smooth: 0.45f);
                Ignore(Prim(PrimitiveType.Sphere, "Shoulder", c, new Vector3(0, 1.05f, 0), new Vector3(0.2f, 0.12f, 0.2f), metal));
                Ignore(Prim(PrimitiveType.Cylinder, "Neck", c, new Vector3(0, 1.17f, 0), new Vector3(0.05f, 0.04f, 0.05f), metal));
                Ignore(Box("Valve", c, new Vector3(0, 1.24f, 0), new Vector3(0.12f, 0.06f, 0.05f), Mat("Brass", "#B08D3A", 0.6f, 0.8f)));
                Ignore(Box("Gauge", c, new Vector3(0.06f, 1.18f, -0.06f), new Vector3(0.07f, 0.07f, 0.03f), Mat("GaugeDark", "#2B2E33", 0.3f)));
            }
        }

        // ---------- drug syringes on a tray (class-coloured labels; barrels and plungers are neutral) ----------
        static void DrugTray(Transform sem, Transform env)
        {
            var g = Group("DrugTrolley", sem, new Vector3(2.0f, 0, 4.2f)).transform;
            var steel = Mat("Steel", "#B3B8BC", 0.6f, 0.7f);
            Ignore(Box("Top", g, new Vector3(0, 0.95f, 0), new Vector3(0.9f, 0.04f, 0.6f), steel));
            Ignore(Box("Lower", g, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.04f, 0.6f), steel));
            foreach (float x in new[] { -0.42f, 0.42f }) foreach (float z in new[] { -0.27f, 0.27f })
                Ignore(Prim(PrimitiveType.Cylinder, "Leg", g, new Vector3(x, 0.47f, z), new Vector3(0.03f, 0.48f, 0.03f), steel));
            var barrel = Mat("SyringeBarrel", "#D9E2E6", 0.7f); var plunger = Mat("Plunger", "#8E969B", 0.3f);
            string[] states = { "Yellow", "Blue", "Red", "Orange", "Violet", "Green" };
            for (int i = 0; i < states.Length; i++)
            {
                float z = -0.23f + i * 0.092f;
                var s = Group("Syringe_" + states[i], g, new Vector3(0, 0.99f, z)).transform;
                // lying along X, 0.2 m long barrel (exaggerated ~1.4x for legibility)
                Ignore(Prim(PrimitiveType.Cylinder, "Barrel", s, Vector3.zero, new Vector3(0.036f, 0.1f, 0.036f), barrel, new Vector3(0, 0, 90)));
                TaggedPrim(PrimitiveType.Cylinder, "Label", s, new Vector3(-0.01f, 0, 0), new Vector3(0.039f, 0.04f, 0.039f), C.DrugLabel, states[i], new Vector3(0, 0, 90), 0.3f);
                Ignore(Prim(PrimitiveType.Cylinder, "Plunger", s, new Vector3(0.14f, 0, 0), new Vector3(0.012f, 0.05f, 0.012f), plunger, new Vector3(0, 0, 90)));
                Ignore(Prim(PrimitiveType.Cylinder, "Flange", s, new Vector3(0.1f, 0, 0), new Vector3(0.05f, 0.004f, 0.05f), plunger, new Vector3(0, 0, 90)));
                Ignore(Prim(PrimitiveType.Cylinder, "Tip", s, new Vector3(-0.12f, 0, 0), new Vector3(0.008f, 0.02f, 0.008f), plunger, new Vector3(0, 0, 90)));
            }
        }

        // ---------- IV stand + infusion pump with a status light ----------
        static void InfusionPump(Transform sem, Transform env)
        {
            var g = Group("InfusionPump", sem, new Vector3(1.0f, 0, 4.3f)).transform;
            var steel = Mat("StandSteel", "#B3B8BC", 0.6f, 0.7f); var body = Mat("PumpBody", "#E7E9E6", 0.3f); var dark = Mat("PumpDark", "#25282D", 0.3f);
            Ignore(Prim(PrimitiveType.Cylinder, "Pole", g, new Vector3(0, 0.95f, 0), new Vector3(0.03f, 0.95f, 0.03f), steel));
            for (int i = 0; i < 5; i++) Ignore(Box("Foot", g, new Vector3(0, 0.03f, 0), new Vector3(0.55f, 0.03f, 0.05f), steel, new Vector3(0, i * 36f, 0)));
            Ignore(Box("Pump", g, new Vector3(0, 1.15f, -0.06f), new Vector3(0.3f, 0.2f, 0.16f), body));
            Ignore(Box("Display", g, new Vector3(-0.04f, 1.17f, -0.145f), new Vector3(0.16f, 0.1f, 0.01f), dark));
            Ignore(Box("Keys", g, new Vector3(-0.04f, 1.09f, -0.145f), new Vector3(0.16f, 0.03f, 0.01f), Mat("PumpKeys", "#9AA0A6", 0.3f)));
            var light = TaggedPrim(PrimitiveType.Sphere, "StatusLight", g, new Vector3(0.1f, 1.17f, -0.145f), Vector3.one * 0.04f, C.StatusLight, "Green", smooth: 0.5f);
            Ignore(Box("Bag", g, new Vector3(0, 1.75f, -0.02f), new Vector3(0.2f, 0.3f, 0.05f), Mat("BagPlastic", "#D8E6EA", 0.8f)));
            Ignore(Box("Hanger", g, new Vector3(0, 1.93f, 0), new Vector3(0.3f, 0.02f, 0.02f), steel));
            Ignore(Prim(PrimitiveType.Cylinder, "Line", g, new Vector3(0.02f, 1.45f, -0.05f), new Vector3(0.008f, 0.17f, 0.008f), Mat("IvLine", "#E4EDEF", 0.8f)));
            // 12 s cycle: running (green), paused (amber), occlusion alarm (red), standby (off)
            var fsm = Fsm("PumpFsm", g, ("Green", 5f), ("Amber", 2.5f), ("Red", 2.5f), ("Off", 2f));
            fsm.AddTarget(light, 0f); fsm.Evaluate();
        }
    }
}
