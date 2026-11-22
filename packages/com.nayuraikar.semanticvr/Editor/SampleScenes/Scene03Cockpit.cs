using System.Collections.Generic;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using static SemanticVR.Editor.SceneKit;
using C = SemanticVR.Editor.DefaultClassLibrary;

namespace SemanticVR.Editor
{
    /// <summary>
    /// 03_Cockpit: airliner flight deck (captain's eye, +Z forward). Glareshield with master warning/caution, a central annunciator
    /// panel, overhead panel, PFD + ND, and a windscreen view of a runway with PAPI and edge lights.
    /// Inspired by, not a copy of, aviation colour conventions (see docs/design/phase1b.md).
    /// Units are metres; the floor is y = 0, the eye is at y = 1.15, the runway is 8 m below on short final.
    /// </summary>
    public static class Scene03Cockpit
    {
        const float PanelZ = 0.95f;         // instrument panel face
        const float RunwayY = -8f;

        public static void Build()
        {
            var scene = Begin("03_Cockpit");
            LightAndSkybox();
            var env = Ignore(Group("Environment", null)).transform;
            var sem = Group("Semantic", null).transform;

            Shell(env);
            Glareshield(sem, env);
            AnnunciatorPanel(sem, env);
            OverheadPanel(sem, env);
            Displays(sem, env);
            Outside(sem, env);
            var sky = SkyDome(sem); sky.transform.localScale = new Vector3(-3000f, 3000f, 3000f);

            var rig = Rig(new Vector3(0f, 0f, -0.1f), 0f, 0.55f);
            rig.eyeHeight = 1.15f; rig.convergenceDistance = 1.2f; rig.initialPitch = 0f; rig.farClip = 3300f; rig.moveSpeed = 1.2f;
            Save(scene);
            Debug.Log("[SemanticVR] Built " + ScenePath);
        }

        // ---------- cockpit shell: floor, side walls, panel body, windscreen frame ----------
        static void Shell(Transform env)
        {
            var trim = Mat("CockpitTrim", "#2E3136", 0.3f); var panel = Mat("PanelBody", "#23262B", 0.35f);
            var carpet = Mat("CockpitFloor", "#3A3D42", 0.05f); var seat = Mat("SeatFabric", "#4A4F57", 0.1f);
            var ceil = Mat("CockpitCeiling", "#3C4046", 0.2f);
            Box("Floor", env, new Vector3(0, -0.05f, 1.2f), new Vector3(3.0f, 0.1f, 3.4f), carpet);
            Box("SideL", env, new Vector3(-1.45f, 1.1f, 1.2f), new Vector3(0.1f, 2.2f, 3.4f), trim);
            Box("SideR", env, new Vector3(1.45f, 1.1f, 1.2f), new Vector3(0.1f, 2.2f, 3.4f), trim);
            Box("Back", env, new Vector3(0, 1.1f, -1.35f), new Vector3(3.0f, 2.2f, 0.1f), trim);
            var roof = Box("Roof", env, new Vector3(0, 2.15f, 1.2f), new Vector3(3.0f, 0.1f, 3.4f), ceil);
            roof.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;   // let the sun in
            // instrument panel body below the windscreen sill; coaming around the opening
            Box("PanelLower", env, new Vector3(0, 0.49f, PanelZ + 0.05f), new Vector3(2.9f, 0.98f, 0.12f), panel);
            Box("Pedestal", env, new Vector3(0, 0.2f, 0.5f), new Vector3(0.3f, 0.4f, 0.8f), trim);
            Box("Header", env, new Vector3(0, 2.0f, 1.3f), new Vector3(2.9f, 0.35f, 0.6f), trim);
            Box("PillarL", env, new Vector3(-1.1f, 1.45f, 1.3f), new Vector3(0.35f, 0.75f, 0.1f), trim);
            Box("PillarR", env, new Vector3(1.1f, 1.45f, 1.3f), new Vector3(0.35f, 0.75f, 0.1f), trim);
            // captain's seat back (just behind the eye)
            Box("SeatBack", env, new Vector3(0, 0.95f, -0.45f), new Vector3(0.55f, 1.0f, 0.12f), seat);
            Box("SeatBase", env, new Vector3(0, 0.45f, -0.1f), new Vector3(0.55f, 0.12f, 0.5f), seat);
            // side-stick / yoke stub
            Prim(PrimitiveType.Cylinder, "Yoke", env, new Vector3(1.15f, 0.55f, 0.3f), new Vector3(0.04f, 0.25f, 0.04f), trim);
        }

        // ---------- glareshield: master warning + caution (both pilots), plus a strip of mode buttons ----------
        static void Glareshield(Transform sem, Transform env)
        {
            var g = Group("Glareshield", sem, new Vector3(0, 1.02f, 1.15f)).transform;
            Ignore(Box("Shield", g, new Vector3(0, 0f, 0f), new Vector3(2.9f, 0.08f, 0.5f), Mat("GlareshieldBody", "#1C1E22", 0.3f)));
            var warn = new List<SemanticTag>(); var caut = new List<SemanticTag>();
            foreach (float sx in new[] { -1f, 1f })
            {
                var w = Panel("MasterWarning", g, new Vector3(sx * 0.92f, 0f, -0.27f), new Vector2(0.14f, 0.07f), C.Warning, "Red", 0.03f); warn.Add(w);
                var c = Panel("MasterCaution", g, new Vector3(sx * 0.74f, 0f, -0.27f), new Vector2(0.14f, 0.07f), C.Caution, "Amber", 0.03f); caut.Add(c);
            }
            // mode-control strip (white advisory legends)
            var adv = new List<SemanticTag>();
            for (int i = 0; i < 6; i++)
                adv.Add(Panel("ModeButton", g, new Vector3(-0.42f + i * 0.17f, 0f, -0.27f), new Vector2(0.12f, 0.05f), C.Advisory, "White", 0.03f));
            var fw = Fsm("MasterWarningFsm", g, ("Red", 2f), ("Off", 2f));
            fw.AddTarget(warn[0], 0f).AddTarget(warn[1], 0f); fw.Evaluate();
            var fc = Fsm("MasterCautionFsm", g, ("Amber", 3f), ("Off", 1.5f));
            fc.AddTarget(caut[0], 0f).AddTarget(caut[1], 0f); fc.Evaluate();
            var fa = Fsm("ModeStripFsm", g, ("White", 4f), ("Blue", 3f), ("Off", 1.5f));
            for (int i = 0; i < adv.Count; i++) fa.AddTarget(adv[i], i * 1.3f);
            fa.Evaluate();
        }

        // ---------- central annunciator panel: 3 columns x 4 rows (warning / caution / advisory / normal) ----------
        static void AnnunciatorPanel(Transform sem, Transform env)
        {
            var g = Group("AnnunciatorPanel", sem, new Vector3(0.62f, 0.66f, PanelZ - 0.01f)).transform;
            Ignore(Box("Bezel", g, new Vector3(0, 0, 0.02f), new Vector3(0.78f, 0.62f, 0.04f), Mat("AnnunBezel", "#15171A", 0.4f)));
            string[] rowState = { "Red", "Amber", "White", "Green" };
            int[] rowClass = { C.Warning, C.Caution, C.Advisory, C.NormalStatus };
            var fsms = new[]
            {
                Fsm("WarningAnnunFsm", g, ("Red", 2.5f), ("Off", 2f)),
                Fsm("CautionAnnunFsm", g, ("Amber", 3f), ("Off", 1.5f)),
                Fsm("AdvisoryAnnunFsm", g, ("White", 2.5f), ("Blue", 2.5f), ("Off", 1.5f)),
                Fsm("NormalAnnunFsm", g, ("Green", 5f), ("Off", 1.5f)),
            };
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 3; c++)
                {
                    var t = Panel($"Annun_R{r}C{c}", g, new Vector3(-0.25f + c * 0.25f, 0.225f - r * 0.15f, -0.01f), new Vector2(0.22f, 0.12f), rowClass[r], rowState[r], 0.03f);
                    fsms[r].AddTarget(t, c * (r == 3 ? 2f : 1.1f) + (r == 1 ? 0.7f : 0f));
                    // legend placeholder (no font rendering in the sample): two short bars
                    Ignore(Box("Legend", g, new Vector3(-0.25f + c * 0.25f, 0.225f - r * 0.15f + 0.02f, -0.03f), new Vector3(0.12f, 0.012f, 0.004f), Mat("LegendDark", "#14161A", 0.1f)));
                    Ignore(Box("Legend", g, new Vector3(-0.25f + c * 0.25f, 0.225f - r * 0.15f - 0.012f, -0.03f), new Vector3(0.08f, 0.012f, 0.004f), Mat("LegendDark", "#14161A", 0.1f)));
                }
            foreach (var f in fsms) f.Evaluate();
        }

        // ---------- overhead panel: a grid of status lights + switch caps ----------
        static void OverheadPanel(Transform sem, Transform env)
        {
            var g = Group("OverheadPanel", sem, new Vector3(0, 1.88f, 0.55f), new Vector3(-60f, 0, 0)).transform;   // tilted towards the pilot
            Ignore(Box("Body", g, new Vector3(0, 0, 0.02f), new Vector3(1.6f, 0.9f, 0.04f), Mat("OverheadBody", "#25282D", 0.3f)));
            // one FSM per class (an FSM's states must exist on every target's class)
            var normalF = Fsm("OverheadNormalFsm", g, ("Green", 4f), ("Off", 1.5f));
            var advisoryF = Fsm("OverheadAdvisoryFsm", g, ("White", 3f), ("Blue", 2f), ("Off", 1f));
            var cautionF = Fsm("OverheadFaultFsm", g, ("Amber", 3f), ("Off", 2.5f));
            var sw = Mat("SwitchCap", "#8A8F96", 0.4f, 0.4f);
            int n = 0;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 8; c++)
                {
                    float x = -0.65f + c * 0.185f, y = 0.33f - r * 0.2f;
                    Ignore(Box("Switch", g, new Vector3(x, y - 0.05f, -0.03f), new Vector3(0.03f, 0.05f, 0.03f), sw));
                    // each switch has an indicator light above it: green normal / white advisory / amber fault
                    bool fault = (r + c) % 3 == 0;
                    var t = Panel($"Light_{r}{c}", g, new Vector3(x, y + 0.035f, -0.02f), new Vector2(0.11f, 0.04f), fault ? C.Caution : (r % 2 == 0 ? C.NormalStatus : C.Advisory),
                                  fault ? "Amber" : (r % 2 == 0 ? "Green" : "White"), 0.02f);
                    (fault ? cautionF : r % 2 == 0 ? normalF : advisoryF).AddTarget(t, n * 0.7f);
                    n++;
                }
            normalF.Evaluate(); advisoryF.Evaluate(); cautionF.Evaluate();
        }

        // ---------- PFD + ND: neutral texture backgrounds, colour-coded elements as tagged panels ----------
        static Texture2D PfdTexture()
        {
            const int W = 256;
            var t = new Texture2D(W, W, TextureFormat.RGBA32, false);
            Color sky = Hex("#34506F"), ground = Hex("#5A4630"), line = Hex("#D8DADD"), tape = Hex("#1A1D21");
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++) t.SetPixel(x, y, (y > W / 2 + (x - W / 2) / 24) ? sky : ground);   // banked horizon
            for (int x = 0; x < W; x++) { int y = W / 2 + (x - W / 2) / 24; for (int k = -1; k <= 1; k++) t.SetPixel(x, y + k, line); }
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
                if (x < 40 || x > W - 40) t.SetPixel(x, y, tape);                                                            // speed + altitude tapes
            for (int y = 0; y < W; y += 12) for (int x = 0; x < 8; x++) { t.SetPixel(34 - x, y, line); t.SetPixel(W - 34 + x, y, line); }
            for (int a = 0; a < 5; a++) for (int x = W / 2 - 18 + a * 4; x < W / 2 + 18 - a * 4; x++) t.SetPixel(x, W / 2 + 30 + a * 14, line); // pitch ladder
            return SaveTexture("PFD", t);
        }

        static Texture2D NdTexture()
        {
            const int W = 256;
            var t = new Texture2D(W, W, TextureFormat.RGBA32, false);
            Color bg = Hex("#0E1114"), ring = Hex("#5A6068"), tick = Hex("#C9CCD0");
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++) t.SetPixel(x, y, bg);
            var c = new Vector2(W / 2f, 40f);
            for (int y = 0; y < W; y++) for (int x = 0; x < W; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c), ang = Mathf.Atan2(y - c.y, x - c.x) * Mathf.Rad2Deg;
                if (ang < 25f || ang > 155f) continue;
                if (Mathf.Abs(d - 190f) < 1.5f) t.SetPixel(x, y, tick);                 // compass arc
                if (Mathf.Abs(d - 130f) < 1f || Mathf.Abs(d - 70f) < 1f) t.SetPixel(x, y, ring);  // range rings
                if (Mathf.Abs(d - 190f) < 8f && Mathf.RoundToInt(ang) % 10 == 0) t.SetPixel(x, y, tick);   // 10-degree ticks
            }
            return SaveTexture("ND", t);
        }

        static void Displays(Transform sem, Transform env)
        {
            var pfdTex = PfdTexture(); var ndTex = NdTexture();
            Disp("PFD", sem, new Vector3(-0.58f, 0.74f, PanelZ - 0.02f), UnlitMat("PFD", Color.white, pfdTex), Pfd);
            Disp("ND", sem, new Vector3(-0.1f, 0.74f, PanelZ - 0.02f), UnlitMat("ND", Color.white, ndTex), Nd);
        }

        static void Disp(string name, Transform sem, Vector3 pos, Material screen, System.Action<Transform> fill)
        {
            var g = Group(name, sem, pos).transform;
            Ignore(Box("Bezel", g, new Vector3(0, 0, 0.03f), new Vector3(0.40f, 0.40f, 0.05f), Mat("DisplayBezel", "#111316", 0.4f)));
            Ignore(Prim(PrimitiveType.Quad, "Screen", g, new Vector3(0, 0, -0.002f), new Vector3(0.36f, 0.36f, 1f), screen));   // Quad UVs are upright when seen from -Z (a cube face flips them)
            fill(g);
        }

        // element helper in screen space: x,y in [-0.5,0.5] of the 0.36 m screen
        static SemanticTag El(Transform g, string name, float x, float y, float w, float h, string state, float rotZ = 0f)
        {
            var t = Panel(name, g, new Vector3(x * 0.36f, y * 0.36f, -0.012f), new Vector2(w * 0.36f, h * 0.36f), C.DisplayText, state, 0.006f);
            if (rotZ != 0f) t.transform.localEulerAngles = new Vector3(0, 0, rotZ);
            return t;
        }

        static void Pfd(Transform g)
        {
            // flight-mode annunciator row: active modes green, a changing armed/selected one
            var fma = new List<SemanticTag> { El(g, "FMA_Thrust", -0.30f, 0.44f, 0.22f, 0.07f, "Green"), El(g, "FMA_Roll", -0.02f, 0.44f, 0.22f, 0.07f, "Green"),
                                              El(g, "FMA_Pitch", 0.26f, 0.44f, 0.22f, 0.07f, "Green"), };
            // selected values (magenta): speed bug, altitude bug, heading bug
            El(g, "SelSpeedBug", -0.40f, 0.12f, 0.08f, 0.04f, "Magenta");
            El(g, "SelAltBug", 0.40f, 0.20f, 0.08f, 0.04f, "Magenta");
            El(g, "SelHdgBug", 0.18f, -0.43f, 0.06f, 0.05f, "Magenta");
            // tuned navaid (cyan): ILS ident + localiser/glideslope scales
            El(g, "ILS_Ident", -0.30f, -0.43f, 0.20f, 0.06f, "Cyan");
            El(g, "LocScale", 0.0f, -0.38f, 0.36f, 0.015f, "Cyan");
            El(g, "GsScale", 0.31f, -0.05f, 0.015f, 0.34f, "Cyan");
            // aircraft symbol + current speed/alt boxes (white)
            El(g, "AcSymbolL", -0.12f, 0.0f, 0.14f, 0.025f, "White"); El(g, "AcSymbolR", 0.12f, 0.0f, 0.14f, 0.025f, "White");
            El(g, "SpeedBox", -0.40f, 0.0f, 0.12f, 0.08f, "White"); El(g, "AltBox", 0.40f, 0.0f, 0.12f, 0.08f, "White");
            // caution / warning flags (amber, red)
            var flap = El(g, "FlapFlag", -0.40f, -0.22f, 0.12f, 0.06f, "Amber");
            var stall = El(g, "StallCue", 0.40f, -0.22f, 0.12f, 0.06f, "Red");
            var fsm = Fsm("PfdFsm", g, ("Green", 4f), ("Magenta", 3f), ("Cyan", 3f));
            fsm.AddTarget(fma[2], 0f); fsm.Evaluate();
            var f2 = Fsm("PfdFlagFsm", g, ("Amber", 3f), ("Red", 3f), ("White", 2f));
            f2.AddTarget(flap, 0f).AddTarget(stall, 3f); f2.Evaluate();
            
        }

        static void Nd(Transform g)
        {
            // route: magenta flight-plan line, green active leg, white waypoints
            El(g, "RouteLine", 0.0f, 0.02f, 0.01f, 0.5f, "Magenta");
            El(g, "RouteBend", 0.12f, 0.30f, 0.01f, 0.28f, "Magenta", -45f);
            El(g, "ActiveLeg", 0.0f, -0.28f, 0.012f, 0.22f, "Green");
            El(g, "AircraftSym", 0.0f, -0.42f, 0.07f, 0.05f, "White");
            El(g, "WptA", 0.0f, 0.24f, 0.04f, 0.04f, "White", 45f); El(g, "WptB", 0.24f, 0.40f, 0.04f, 0.04f, "White", 45f);
            // tuned VOR + frequency readout (cyan), weather (green), traffic (amber / red), mode text (green)
            El(g, "TunedVor", -0.30f, 0.18f, 0.06f, 0.06f, "Cyan", 45f); El(g, "VorFreq", -0.34f, 0.28f, 0.16f, 0.04f, "Cyan");
            El(g, "WxCell", 0.28f, 0.05f, 0.14f, 0.10f, "Green");
            var tcasA = El(g, "TrafficAdvisory", -0.20f, 0.0f, 0.05f, 0.05f, "Amber", 45f);
            var tcasR = El(g, "TrafficResolution", 0.20f, -0.12f, 0.05f, 0.05f, "Red");
            El(g, "ModeText", -0.36f, 0.44f, 0.12f, 0.04f, "Green"); El(g, "RangeText", 0.38f, 0.44f, 0.12f, 0.04f, "White");
            var f = Fsm("NdFsm", g, ("Amber", 3f), ("Red", 2.5f), ("White", 2.5f));
            f.AddTarget(tcasA, 0f).AddTarget(tcasR, 3f); f.Evaluate();
        }

        // ---------- outside: runway, PAPI, edge lights, grass, sky ----------
        static void Outside(Transform sem, Transform env)
        {
            var o = Group("Outside", sem, new Vector3(0, RunwayY, 0)).transform;
            var asphalt = Mat("RunwayAsphalt", "#3C3F44", 0.1f); var paint = Mat("RunwayPaint", White, 0.2f);
            TaggedPrim(PrimitiveType.Cube, "GrassField", o, new Vector3(0, -0.3f, 370f), new Vector3(1600f, 0.5f, 1400f), C.Grass, "Green", smooth: 0.05f);
            Ignore(Box("Runway", o, new Vector3(0, -0.02f, 460f), new Vector3(45f, 0.1f, 600f), asphalt));
            Ignore(Box("Centreline", o, new Vector3(0, 0.05f, 460f), new Vector3(0.9f, 0.02f, 600f), Mat("RunwayDash", "#B9B6AE", 0.2f)));
            for (float z = 160f; z < 750f; z += 60f) Ignore(Box("Dash", o, new Vector3(0, 0.06f, z), new Vector3(0.9f, 0.02f, 30f), paint));
            for (int i = 0; i < 6; i++) { Ignore(Box("Threshold", o, new Vector3(-17f + i * 6.8f, 0.06f, 152f), new Vector3(2.4f, 0.02f, 24f), paint)); }
            Ignore(Box("Aim", o, new Vector3(-9f, 0.06f, 280f), new Vector3(3.5f, 0.02f, 45f), paint));
            Ignore(Box("Aim", o, new Vector3(9f, 0.06f, 280f), new Vector3(3.5f, 0.02f, 45f), paint));

            // edge lights: white pairs every 40 m, FSM-flashed so some are off (Off state is dark)
            var edge = Group("EdgeLights", o).transform;
            var tags = new List<SemanticTag>();
            for (float z = 152f; z < 750f; z += 40f)
                foreach (float sx in new[] { -1f, 1f })
                    tags.Add(TaggedPrim(PrimitiveType.Sphere, "EdgeLight", edge, new Vector3(sx * 24f, 0.5f, z), Vector3.one * 1.3f, C.RunwayLight, "White", smooth: 0.4f));
            var fsm = Fsm("EdgeLightFsm", edge, ("White", 6f), ("Off", 0.6f));
            for (int i = 0; i < tags.Count; i++) fsm.AddTarget(tags[i], (i / 2) * 0.35f);
            fsm.Evaluate();

            // PAPI: two banks of four lights (left = static on-slope WWRR; right = FSM wave through every glide-path indication)
            Papi(o, "PapiLeft", -33f, false);
            Papi(o, "PapiRight", 33f, true);
            // approach / threshold lights: red wing bars at the threshold (unrelated to PAPI, always red)
            foreach (float sx in new[] { -1f, 1f })
                for (int i = 0; i < 4; i++)
                    TaggedPrim(PrimitiveType.Sphere, "WingBar", edge, new Vector3(sx * (26f + i * 2.4f), 0.5f, 148f), Vector3.one * 1.1f, C.RunwayLight, "Red", smooth: 0.4f);
        }

        static void Papi(Transform o, string name, float x, bool cycling)
        {
            var g = Group(name, o, new Vector3(x, 0f, 240f)).transform;
            Ignore(Box("Bar", g, new Vector3(0, 0.3f, 0.4f), new Vector3(15.5f, 0.5f, 0.6f), Mat("PapiBar", "#B8B5AD", 0.3f)));
            var lights = new List<SemanticTag>();
            for (int i = 0; i < 4; i++)
            {
                Ignore(Box("Housing", g, new Vector3(-5.4f + i * 3.6f, 0.9f, 0.2f), new Vector3(2.4f, 1.8f, 1.6f), Mat("PapiHousing", "#2B2E33", 0.3f)));
                lights.Add(Tag(Prim(PrimitiveType.Cylinder, "Lens", g, new Vector3(-5.4f + i * 3.6f, 1.1f, -0.65f), new Vector3(1.7f, 0.04f, 1.7f), StateMat(Class(C.RunwayLight), "White", 0.9f), new Vector3(90, 0, 0)),
                               C.RunwayLight, i < 2 ? "White" : "Red"));
            }
            if (!cycling) return;
            var fsm = Fsm(name + "Fsm", g, ("White", 3f), ("Red", 3f));
            for (int i = 0; i < 4; i++) fsm.AddTarget(lights[i], i * 1.5f);
            fsm.Evaluate();
        }
    }
}
