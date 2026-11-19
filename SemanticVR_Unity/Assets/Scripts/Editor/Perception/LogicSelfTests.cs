#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using SemanticVR.Accessibility;
using SemanticVR.ColourProcessing;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Semantics;
using UnityEditor;
using UnityEngine;

namespace SemanticVR.EditorTools
{
    /// <summary>
    /// Pure-logic automated tests (no GPU, no model inference). Run: Unity -batchmode -quit -executeMethod
    /// SemanticVR.EditorTools.LogicSelfTests.Run  (exit code 1 on any failure) or the menu SemanticVR/Run Logic Self-Tests.
    /// </summary>
    public static class LogicSelfTests
    {
        static int passed, failed;
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) { passed++; Debug.Log("[TEST] PASS " + name); }
            else { failed++; Debug.LogError("[TEST] FAIL " + name + " " + detail); }
        }
        static bool Near(float a, float b, float tol = 1e-4f) => Mathf.Abs(a - b) <= tol;

        [MenuItem("SemanticVR/Run Logic Self-Tests")]
        public static void Run()
        {
            passed = failed = 0;
            ClassTable(); Parser(); Mapping(); Importance(); Policy(); Scheduler(); Matrices(); Layout(); Simulation();
            Debug.Log($"[TEST] SUMMARY passed={passed} failed={failed}");
            if (Application.isBatchMode) EditorApplication.Exit(failed == 0 ? 0 : 1);
        }

        static void ClassTable()
        {
            Check("COCO table has 80 valid classes", CocoClasses.ValidCount == 80, CocoClasses.ValidCount.ToString());
            Check("COCO names person/bicycle/car/motorcycle/bus/truck/traffic light/stop sign",
                CocoClasses.NameOf(1) == "person" && CocoClasses.NameOf(2) == "bicycle" && CocoClasses.NameOf(3) == "car" && CocoClasses.NameOf(4) == "motorcycle" &&
                CocoClasses.NameOf(6) == "bus" && CocoClasses.NameOf(8) == "truck" && CocoClasses.NameOf(10) == "traffic light" && CocoClasses.NameOf(13) == "stop sign");
            Check("COCO unused ids invalid (0,12,26,29,30,45,66,68,69,71,83)", Array.TrueForAll(new[] { 0, 12, 26, 29, 30, 45, 66, 68, 69, 71, 83 }, i => !CocoClasses.IsValid(i)));
        }

        static void Parser()
        {
            var g = GoldenReference.Load(out var err);
            Check("golden reference loads", g != null, err);
            if (g == null) return;
            var parser = new DetectionParser { ConfidenceThreshold = 0.3f };
            var res = new PerceptionResult();
            parser.Parse(g.Dets, g.Labels, res);
            // Reference: tools/scripts/run_still.py on the same image at threshold 0.3 -> 19 detections, first six below.
            Check("parser finds 19 detections at 0.3 (matches ONNX Runtime script)", res.Detections.Count == 19, res.Detections.Count.ToString());
            string[] exp = { "bus", "bus", "car", "person", "person", "bus" };
            bool names = res.Detections.Count >= 6; for (int i = 0; names && i < 6; i++) names &= res.Detections[i].ClassName == exp[i];
            Check("top-6 class order bus,bus,car,person,person,bus", names);
            Check("top confidence ~0.933", res.Detections.Count > 0 && Near(res.Detections[0].Confidence, 0.933f, 0.005f), res.Detections.Count > 0 ? res.Detections[0].Confidence.ToString() : "");
            Check("detection ids unique & ascending", UniqueAscending(res));
            var d0 = res.Detections[0].Box;
            // ONNX Runtime script (tools/out/real_oxford.json): box [0.8, 489.5, 287.1, 840.8] px in 1280x923 -> [0.0006, 0.530, 0.224, 0.911]
            Check("top detection box matches ONNX Runtime ([0.0006,0.530]-[0.224,0.911] normalised)", Near(d0.xMin, 0.0006f, 0.002f) && Near(d0.yMin, 0.530f, 0.003f) && Near(d0.xMax, 0.224f, 0.003f) && Near(d0.yMax, 0.911f, 0.003f), d0.ToString());
            var pr = new PerceptionResult(); parser.ConfidenceThreshold = 0.99f; parser.Parse(g.Dets, g.Labels, pr);
            Check("threshold 0.99 yields no detections", pr.Detections.Count == 0);
            parser.ConfidenceThreshold = 0.3f; parser.Parse(g.Dets, g.Labels, res);
            Check("pool reuse: re-parse gives same count", res.Detections.Count == 19);
            // mask processing on golden masks with the validated layout (reference data are logical order => use Identity on a re-laid buffer)
            var mp = new MaskProcessor { Layout = MaskLayout.Identity };
            var buf = new float[RfDetrModelSpec.Queries * 78 * 78];
            for (int i = 0; i < g.TopQueries.Length; i++) Array.Copy(g.TopMasks, i * 78 * 78, buf, g.TopQueries[i] * 78 * 78, 78 * 78);
            mp.BuildMasks(res, buf); mp.IdSize = 156; mp.BuildInstanceMap(res);
            Check("top bus mask has many cells inside its box (python: ~450 px at 78x78 scale)", res.Detections[0].MaskCells > 300, res.Detections[0].MaskCells.ToString());
            int nz = 0; foreach (var b in res.InstanceIds) if (b != 0) nz++;
            Check("instance-ID map non-empty", nz > 1000, nz.ToString());
            mp.Dispose();
        }

        static bool UniqueAscending(PerceptionResult r)
        { long last = 0; foreach (var d in r.Detections) { if (d.UniqueId <= last) return false; last = d.UniqueId; } return true; }

        static void Mapping()
        {
            var map = ScriptableObject.CreateInstance<CocoToSemanticMap>();
            Check("default map validates against class table", map.Validate() == null, map.Validate());
            bool ok = map.TryGetCategory(1, out var c1) && c1 == SemanticCategory.Pedestrian && map.TryGetCategory(10, out var c10) && c10 == SemanticCategory.TrafficLight
                && map.TryGetCategory(13, out var c13) && c13 == SemanticCategory.TrafficSign && map.TryGetCategory(6, out var c6) && c6 == SemanticCategory.Vehicle;
            Check("map: person->Pedestrian, light->TrafficLight, stop sign->TrafficSign, bus->Vehicle", ok);
            Check("unsupported COCO classes are not mapped (handbag 31, vase 86, bench 15)", !map.TryGetCategory(31, out _) && !map.TryGetCategory(86, out _) && !map.TryGetCategory(15, out _));
            UnityEngine.Object.DestroyImmediate(map);
        }

        static void Importance()
        {
            var cfg = ScriptableObject.CreateInstance<SemanticImportanceConfig>();
            Check("default Road Safety weights per spec", cfg.GetWeight(SemanticCategory.TrafficLight) == 5 && cfg.GetWeight(SemanticCategory.TrafficSign) == 5 && cfg.GetWeight(SemanticCategory.Pedestrian) == 5 &&
                cfg.GetWeight(SemanticCategory.Vehicle) == 4 && cfg.GetWeight(SemanticCategory.RoadMarking) == 4 && cfg.GetWeight(SemanticCategory.Sidewalk) == 3 && cfg.GetWeight(SemanticCategory.Road) == 3 &&
                cfg.GetWeight(SemanticCategory.Building) == 1 && cfg.GetWeight(SemanticCategory.Vegetation) == 1 && cfg.GetWeight(SemanticCategory.Ground) == 1);
            cfg.SetWeight(SemanticCategory.Building, 99); Check("weights clamp to 5", cfg.GetWeight(SemanticCategory.Building) == 5);
            cfg.SetWeight(SemanticCategory.Building, -3); Check("weights clamp to 0", cfg.GetWeight(SemanticCategory.Building) == 0);
            cfg.ActiveProfile = ImportanceProfileType.Navigation; Check("profile switch changes weights", cfg.GetWeight(SemanticCategory.RoadMarking) == 5);
            cfg.ActiveProfile = ImportanceProfileType.RoadSafety; cfg.ResetProfile(ImportanceProfileType.RoadSafety);
            Check("reset restores defaults", cfg.GetWeight(SemanticCategory.Building) == 1);
            var copy = cfg.CreateRuntimeCopy(); copy.SetWeight(SemanticCategory.Vehicle, 0);
            Check("runtime copy edits do not touch the original", cfg.GetWeight(SemanticCategory.Vehicle) == 4 && copy.GetWeight(SemanticCategory.Vehicle) == 0);
            cfg.SetWeight(ImportanceProfileType.Custom, SemanticCategory.Road, 5); cfg.CopyProfile(ImportanceProfileType.Custom, ImportanceProfileType.General);
            Check("copy profile", cfg.GetWeight(ImportanceProfileType.General, SemanticCategory.Road) == 5);
            UnityEngine.Object.DestroyImmediate(cfg); UnityEngine.Object.DestroyImmediate(copy);
        }

        static void Policy()
        {
            DetectedObject Obj(bool ai, SemanticCategory a, bool man, SemanticCategory m, float conf) => new DetectedObject
            { Detection = new Detection { Confidence = conf }, HasAiCategory = ai, AiCategory = a, HasManualCategory = man, ManualCategory = m, Disagreement = ai && man && a != m };
            var o = Obj(true, SemanticCategory.Vehicle, false, 0, 0.5f); SemanticResolver.Choose(o, ConflictPolicy.PreferManual, 0.6f);
            Check("AI only -> AI", o.Source == CategorySource.AI && o.FinalCategory == SemanticCategory.Vehicle);
            o = Obj(false, 0, true, SemanticCategory.Pedestrian, 0.5f); SemanticResolver.Choose(o, ConflictPolicy.PreferAI, 0.6f);
            Check("manual only -> manual tag (fallback)", o.Source == CategorySource.ManualTag && o.FinalCategory == SemanticCategory.Pedestrian);
            o = Obj(false, 0, false, 0, 0.9f); SemanticResolver.Choose(o, ConflictPolicy.PreferAI, 0.6f);
            Check("neither -> None", o.Source == CategorySource.None);
            o = Obj(true, SemanticCategory.Vehicle, true, SemanticCategory.Building, 0.9f); SemanticResolver.Choose(o, ConflictPolicy.AiAboveConfidence, 0.6f);
            Check("conflict, high AI confidence -> AI, disagreement exposed", o.Source == CategorySource.AI && o.Disagreement);
            o = Obj(true, SemanticCategory.Vehicle, true, SemanticCategory.Building, 0.4f); SemanticResolver.Choose(o, ConflictPolicy.AiAboveConfidence, 0.6f);
            Check("conflict, low AI confidence -> manual", o.Source == CategorySource.ManualTag && o.FinalCategory == SemanticCategory.Building);
            o = Obj(true, SemanticCategory.Vehicle, true, SemanticCategory.Building, 0.4f); SemanticResolver.Choose(o, ConflictPolicy.PreferAI, 0.6f);
            Check("PreferAI wins regardless of confidence", o.Source == CategorySource.AI);
            o = Obj(true, SemanticCategory.Vehicle, true, SemanticCategory.Building, 0.99f); SemanticResolver.Choose(o, ConflictPolicy.PreferManual, 0.6f);
            Check("PreferManual wins regardless of confidence", o.Source == CategorySource.ManualTag);
            o = Obj(true, SemanticCategory.Vehicle, true, SemanticCategory.Vehicle, 0.4f); SemanticResolver.Choose(o, ConflictPolicy.PreferManual, 0.6f);
            Check("agreement -> AI source, no disagreement", o.Source == CategorySource.AI && !o.Disagreement);
        }

        static void Scheduler()
        {
            var s = new InferenceScheduler { IntervalSeconds = 0.5f };
            Check("scheduler starts immediately", s.ShouldStart(0f, false));
            s.MarkStarted(10f);
            Check("scheduler waits for interval", !s.ShouldStart(10.2f, false) && s.ShouldStart(10.6f, false));
            Check("scheduler never starts while busy", !s.ShouldStart(99f, true));
            s.Reset(); Check("reset allows immediate start", s.ShouldStart(10.0f, false));
        }

        static void Matrices()
        {
            Check("identity valid", MatrixValidator.Validate(Matrix3x3.Identity, true, out _));
            var nan = Matrix3x3.Identity; nan.m11 = float.NaN;
            Check("NaN rejected", !MatrixValidator.Validate(nan, false, out _));
            var big = Matrix3x3.Identity; big.m00 = 9;
            Check("oversized entry rejected", !MatrixValidator.Validate(big, false, out _));
            Check("singular rejected when invertibility required, allowed otherwise", !MatrixValidator.Validate(CvdSimulation.Achromat, true, out _) && MatrixValidator.Validate(CvdSimulation.Achromat, false, out _));
            var a = new Matrix3x3(1, 2, 3, 4, 5, 6, 7, 8, 10);
            var p = a * Matrix3x3.Identity; Check("matrix * identity", Near(p.m22, 10) && Near(p.m01, 2));
            Check("determinant", Near(a.Determinant, -3f));
            var v = CvdSimulation.GetMatrix(CvdType.Deuteranopia, 1, Matrix3x3.Identity).Apply(new Vector3(1, 1, 1));
            Check("deuteranopia keeps white ~white (rows sum ~1)", Near(v.x, 1, 0.01f) && Near(v.y, 1, 0.01f) && Near(v.z, 1, 0.01f), v.ToString());
        }

        static void Layout()
        {
            // flat index (x*Q+q)*H+y must be a bijection over all (q,y,x)
            var seen = new bool[RfDetrModelSpec.Queries * 78 * 78]; bool bij = true;
            for (int q = 0; q < RfDetrModelSpec.Queries; q++) for (int y = 0; y < 78; y++) for (int x = 0; x < 78; x++) { int i = MaskLayoutUtil.Index(MaskLayout.UnityEinsumTransposed, q, y, x); if (seen[i]) bij = false; seen[i] = true; }
            Check("transposed layout index is a bijection", bij);
            Check("known mapping point q=3,y=5,x=7 -> (7*100+3)*78+5", MaskLayoutUtil.Index(MaskLayout.UnityEinsumTransposed, 3, 5, 7) == (7 * 100 + 3) * 78 + 5);
            // validator must reject a wrong layout (negative control)
            var g = GoldenReference.Load(out _);
            var wrong = new float[RfDetrModelSpec.Queries * 78 * 78];
            for (int i = 0; i < g.TopQueries.Length; i++) for (int y = 0; y < 78; y++) for (int x = 0; x < 78; x++) wrong[MaskLayoutUtil.Index(MaskLayout.Identity, g.TopQueries[i], y, x)] = g.TopMasks[(i * 78 + y) * 78 + x];
            var rep = MaskLayoutValidator.Validate(g, g.Dets, g.Labels, wrong, MaskLayout.UnityEinsumTransposed);
            Check("validator rejects masks in the wrong layout and says so", !rep.Passed && rep.Message.Contains("LAYOUT CHANGED"), rep.Message);
            bool threw = false; try { MaskLayoutValidator.ThrowIfFailed(rep); } catch (MaskLayoutValidationException) { threw = true; }
            Check("ThrowIfFailed throws on failed validation", threw);
            var right = new float[wrong.Length];
            for (int i = 0; i < g.TopQueries.Length; i++) for (int y = 0; y < 78; y++) for (int x = 0; x < 78; x++) right[MaskLayoutUtil.Index(MaskLayout.UnityEinsumTransposed, g.TopQueries[i], y, x)] = g.TopMasks[(i * 78 + y) * 78 + x];
            Check("validator accepts correctly laid-out golden data", MaskLayoutValidator.Validate(g, g.Dets, g.Labels, right, MaskLayout.UnityEinsumTransposed).Passed);
        }

        static void Simulation()
        {
            var prof = ScriptableObject.CreateInstance<CvdProfile>(); var set = ScriptableObject.CreateInstance<CorrectionSettings>();
            var pipe = new ColourMatrixPipeline();
            prof.type = CvdType.Normal; var t = pipe.Build(new ColourContext { Profile = prof, Settings = set });
            Check("Normal -> identity simulation and correction", t.Valid && Near(t.Simulation.m00, 1) && Near(t.Correction.m11, 1) && Near(t.Correction.m10, 0));
            prof.type = CvdType.Deuteranomaly; prof.severityPercent = 0; t = pipe.Build(new ColourContext { Profile = prof, Settings = set });
            Check("severity 0 -> identity simulation", t.Valid && Near(t.Simulation.m00, 1) && Near(t.Simulation.m01, 0));
            prof.severityPercent = 100; set.strength = 0; t = pipe.Build(new ColourContext { Profile = prof, Settings = set });
            Check("strength 0 -> identity correction", t.Valid && Near(t.Correction.m10, 0) && Near(t.Correction.m11, 1));
            Check("Deuteranomaly 100% equals deuteranopia matrix", Near(t.Simulation.m00, CvdSimulation.DeuteranopiaFull.m00, 1e-6f) && Near(t.Simulation.m22, CvdSimulation.DeuteranopiaFull.m22, 1e-6f));
            prof.type = CvdType.Protanopia; prof.severityPercent = 10; t = pipe.Build(new ColourContext { Profile = prof, Settings = set });
            Check("Protanopia ignores the severity slider (always 100%)", Near(t.Simulation.m00, CvdSimulation.ProtanopiaFull.m00, 1e-6f));
            set.strength = 1; set.mode = CorrectionMode.Custom; set.customErrorShift = new Matrix3x3(40, 0, 0, 0, 40, 0, 0, 0, 40);
            t = pipe.Build(new ColourContext { Profile = prof, Settings = set });
            Check("provider output outside limits is rejected, not applied (identity + error)", !t.Valid && t.Error != null && Near(t.Correction.m00, 1), t.Error);
            var sev = ScriptableObject.CreateInstance<CvdProfile>(); sev.type = CvdType.Tritanopia; sev.severityPercent = 5;
            Check("EffectiveSeverity01 for Tritanopia is 1", Near(sev.EffectiveSeverity01, 1));
            Check("a null profile yields an error, not a crash", !pipe.Build(new ColourContext { Profile = null, Settings = set }).Valid);
            UnityEngine.Object.DestroyImmediate(prof); UnityEngine.Object.DestroyImmediate(set); UnityEngine.Object.DestroyImmediate(sev);
        }
    }
}
#endif
