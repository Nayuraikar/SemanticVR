using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SemanticVR.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SemanticVR.Confusion
{
    public enum SampleStatus { Ok, Occluded, Shadowed, Textured }

    /// <summary>One tagged object's validation sample: the predicted lit colour at a visible surface point vs the rendered pixel there.</summary>
    public sealed class LitSample
    {
        public SemanticTag Tag;
        public string Name, ClassName, StateName;
        public bool Unlit;
        public SampleStatus Status;
        public Vector3 Point, Normal, CameraPosition;
        public int ViewIndex;
        public float3 PredictedLin;   // displayed (saturated), linear
        public float3 RenderedLin;    // mean of the centre pixels, linear
        public float3 AlbedoLin;      // baseline prediction: albedo only (saturated emission-free)
        public float DeltaE00;        // predicted lit vs rendered
        public float DeltaE00Albedo;  // albedo-only baseline vs rendered
        public bool HasRender;        // rendered/predicted fields are valid (Ok, and Shadowed which is rendered for reporting)
    }

    public readonly struct ErrorStats
    {
        public readonly int Count; public readonly float Mean, P95, Max;
        public ErrorStats(IList<float> v)
        {
            Count = v.Count;
            if (Count == 0) { Mean = P95 = Max = float.NaN; return; }
            var s = v.OrderBy(x => x).ToArray();
            Mean = s.Average(); Max = s[s.Length - 1];
            P95 = s[Mathf.Clamp(Mathf.CeilToInt(0.95f * s.Length) - 1, 0, s.Length - 1)]; // nearest-rank percentile
        }
    }

    public sealed class ValidationResult
    {
        public string Scene, Preset;
        /// <summary>Readback calibration: dE00 between known unlit colours and what the pipeline reads back (should be ~0).</summary>
        public ErrorStats Calibration;
        public List<LitSample> Samples = new List<LitSample>();
        public int Count(SampleStatus s) => Samples.Count(x => x.Status == s);
        public IEnumerable<LitSample> Ok => Samples.Where(x => x.Status == SampleStatus.Ok);
        public ErrorStats Overall => new ErrorStats(Ok.Select(s => s.DeltaE00).ToList());
        public ErrorStats OverallAlbedo => new ErrorStats(Ok.Select(s => s.DeltaE00Albedo).ToList());
        public Dictionary<string, ErrorStats> PerClass => Ok.GroupBy(s => s.ClassName).ToDictionary(g => g.Key, g => new ErrorStats(g.Select(s => s.DeltaE00).ToList()));
        /// <summary>Calibration: mean dE00 of the unlit samples (their predicted colour is exactly their albedo, so this is the render/readback pipeline error).</summary>
        public ErrorStats Unlit => new ErrorStats(Ok.Where(s => s.Unlit).Select(s => s.DeltaE00).ToList());
        /// <summary>Error of the objects that were skipped as shadowed (they are rendered anyway so the size of the shadow miss is known; excluded from every other statistic).</summary>
        public ErrorStats ShadowedError => new ErrorStats(Samples.Where(x => x.Status == SampleStatus.Shadowed && x.RenderedLin.x >= 0f && x.HasRender).Select(x => x.DeltaE00).ToList());
        public ErrorStats Lit => new ErrorStats(Ok.Where(s => !s.Unlit).Select(s => s.DeltaE00).ToList());
    }

    /// <summary>
    /// Validates <see cref="LitColorPredictor"/> against the renderer. For each tagged object it finds a visible surface point
    /// (raycast from a camera placed in front of the object, against temporary mesh colliders), renders the scene to a small LDR
    /// RenderTexture with post-processing, tone mapping (HDR off), anti-aliasing and the CVD pass disabled, reads the centre pixels
    /// and compares them with the prediction at the same point and normal (CIEDE2000).
    /// <para><b>Skipped:</b> objects with no unobstructed view (Occluded), objects whose sample point is in the main light's real-time
    /// shadow (Shadowed), and textured materials (Textured). The predictor has no shadows and no specular/reflection model, so
    /// unskipped error still includes specular and sky reflection; shadows are only avoided, never predicted.</para>
    /// </summary>
    public static class LitPredictionValidator
    {
        public static int RtSize = 32;
        public static string ResultsDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../results/quality/c1"));

        static readonly (float, float)[] FaceGrid = BuildFaceGrid();
        static (float, float)[] BuildFaceGrid()
        {
            var g = new List<(float, float)> { (0f, 0f) };
            foreach (float r in new[] { 0.35f, 0.7f })
                for (int i = 0; i < 8; i++) g.Add((r * Mathf.Cos(i * Mathf.PI / 4f), r * Mathf.Sin(i * Mathf.PI / 4f)));
            return g.ToArray();
        }

        static readonly List<Collider> TempColliders = new List<Collider>();

        public static ValidationResult Run(string scene, string preset = "")
        {
            var res = new ValidationResult { Scene = scene, Preset = preset };
            var prevMode = CvdDebugState.Mode; CvdDebugState.Mode = DebugViewMode.Original;
#if UNITY_EDITOR
            // With async shader compilation the Editor draws nothing for an object whose shader variant is still compiling, so the
            // readback would be the background: compile synchronously while validating.
            bool prevAsync = UnityEditor.ShaderUtil.allowAsyncCompilation; UnityEditor.ShaderUtil.allowAsyncCompilation = false;
#endif
            AddTempColliders();
            Physics.SyncTransforms();
            var rt = new RenderTexture(RtSize, RtSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, filterMode = FilterMode.Point };
            var tex = new Texture2D(RtSize, RtSize, TextureFormat.RGBA32, false, false);
            var camGo = new GameObject("LitValidationCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            try
            {
                ConfigureCamera(cam, rt);
                var env = LitColorPredictor.Capture();
                res.Calibration = Calibrate(cam, rt, tex);
                var normalCache = new Dictionary<Mesh, (Vector3[] n, int[] t)>();
                foreach (var tag in SemanticRegistry.Tags.ToList())
                {
                    if (tag == null || tag.TargetRenderer == null || tag.CurrentState == null) continue;
                    res.Samples.Add(SampleTag(tag, env, cam, rt, tex, normalCache));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                RenderTexture.active = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                RemoveTempColliders();
                CvdDebugState.Mode = prevMode;
#if UNITY_EDITOR
                UnityEditor.ShaderUtil.allowAsyncCompilation = prevAsync;
#endif
            }
            return res;
        }

        static void ConfigureCamera(Camera cam, RenderTexture rt)
        {
            cam.enabled = false;
            cam.targetTexture = rt;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 6f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 2000f;
            cam.allowHDR = false; cam.allowMSAA = false; cam.useOcclusionCulling = false;
            var d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = false;                      // no post-processing, so no tone mapping / bloom / colour grading
            d.antialiasing = AntialiasingMode.None;
            d.dithering = false;
        }

        static void AddTempColliders()
        {
            TempColliders.Clear();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.GetComponent<Collider>() != null) continue;
                if (r.bounds.size.magnitude > 200f) continue; // sky dome and the like
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                TempColliders.Add(mc);
            }
        }

        static void RemoveTempColliders()
        {
            foreach (var c in TempColliders) if (c != null) UnityEngine.Object.DestroyImmediate(c);
            TempColliders.Clear();
        }

        static LitSample SampleTag(SemanticTag tag, in LightingSnapshot env, Camera cam, RenderTexture rt, Texture2D tex, Dictionary<Mesh, (Vector3[], int[])> cache)
        {
            var s = new LitSample
            {
                Tag = tag, Name = tag.name, ClassName = tag.Class != null ? tag.Class.displayName : "?", StateName = tag.StateName,
                Unlit = tag.IsUnlit, AlbedoLin = math.saturate(SemanticGraph.SrgbToLinear(tag.CurrentAlbedo)),
            };
            if (tag.HasBaseTexture) { s.Status = SampleStatus.Textured; return s; }
            var col = tag.TargetRenderer.GetComponent<Collider>();
            if (col == null) { s.Status = SampleStatus.Occluded; return s; }

            Vector3 centre = tag.Bounds.center;
            float radius = Mathf.Max(tag.Bounds.extents.magnitude, 0.02f);
            float dist = Mathf.Clamp(radius * 3f, 0.4f, 8f);
            Vector3 dn = tag.DominantNormal(SemanticGraph.ViewerPosition(), env.Main.Valid ? new Vector3(env.Main.ToLight.x, env.Main.ToLight.y, env.Main.ToLight.z) : (Vector3?)null);

            RaycastHit hit = default; Vector3 camPos = default; bool found = false; int viewIndex = 0;
            foreach (var dir in ViewDirections(tag, dn))
            {
                viewIndex++;
                bool dominantView = viewIndex == 1;
                // A decal or trim piece may cover the object's centre (road-sign bars, labels), so aim at a small grid of points
                // across the face seen from this direction: the centre first, then rings at 35% and 70% of the radius.
                Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.9f ? Vector3.forward : Vector3.up).normalized, v = Vector3.Cross(dir, u);
                foreach (var (a, b) in FaceGrid)
                {
                    Vector3 target = centre + (u * a + v * b) * radius;
                    camPos = target + dir * dist;
                    if (!Physics.Raycast(camPos, -dir, out hit, dist * 2f + radius) || hit.collider != col) continue;
                    // head-on hit; on the dominant view also on the dominant face, so the graph's prediction and this sample use the same normal
                    if (Vector3.Dot(hit.normal, dir) < 0.8f) continue;
                    if (dominantView && Vector3.Dot(hit.normal, dn) < 0.9f) continue;
                    found = true; break;
                }
                if (found) break;
            }
            if (!found) { s.Status = SampleStatus.Occluded; return s; }

            s.Point = hit.point; s.Normal = SurfaceNormal(hit, cache); s.CameraPosition = camPos; s.ViewIndex = viewIndex;
            bool shadowed = false;
            if (env.Main.Valid)
            {
                var tl = new Vector3(env.Main.ToLight.x, env.Main.ToLight.y, env.Main.ToLight.z);
                if (Vector3.Dot(s.Normal, tl) > 0f && Physics.Raycast(s.Point + s.Normal * 0.02f, tl, 500f)) shadowed = true;
            }

            camPos = RaiseAboveHorizon(camPos, hit.point, col, dist);
            s.CameraPosition = camPos;
            s.RenderedLin = RenderCentre(cam, rt, tex, camPos, hit.point);
            s.PredictedLin = LitColorPredictor.Displayed(LitColorPredictor.Predict(tag, env, s.Point, s.Normal));
            var labR = SemanticGraph.LabLinear(s.RenderedLin);
            s.DeltaE00 = ColorMath.Ciede2000(SemanticGraph.LabLinear(s.PredictedLin), labR);
            s.DeltaE00Albedo = ColorMath.Ciede2000(SemanticGraph.LabLinear(s.AlbedoLin), labR);
            s.HasRender = true;
            s.Status = shadowed ? SampleStatus.Shadowed : SampleStatus.Ok;
            return s;
        }

        /// <summary>
        /// A camera looking exactly horizontally puts the skybox horizon on the image centre, and a 1-pixel row of sky shows through
        /// the object there (seen in testing: it corrupted the centre-pixel readback of signs viewed at eye height). Move the camera
        /// up (or down) a little so the sample point is never on the horizon row, keeping line of sight to the point.
        /// </summary>
        static Vector3 RaiseAboveHorizon(Vector3 camPos, Vector3 point, Collider col, float dist)
        {
            if (Mathf.Abs(camPos.y - point.y) > dist * 0.1f) return camPos;
            foreach (float k in new[] { 0.25f, -0.25f, 0.15f, -0.15f, 0.4f })
            {
                var p = camPos + Vector3.up * (dist * k);
                var d = point - p;
                if (Physics.Raycast(p, d.normalized, out var h, d.magnitude + 0.05f) && h.collider == col) return p;
            }
            return camPos;
        }

        /// <summary>Render from <paramref name="camPos"/> towards <paramref name="target"/> and return the mean linear colour of the 4 centre pixels.</summary>
        static float3 RenderCentre(Camera cam, RenderTexture rt, Texture2D tex, Vector3 camPos, Vector3 target)
        {
            cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation((target - camPos).normalized, Vector3.up));
            // Render until two consecutive reads agree (guards against first-frame / still-compiling renders), at most 8 times.
            float3 prev = float3.zero, cur = float3.zero;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                cur = RenderOnce(cam, rt, tex);
                if (attempt > 0 && math.cmax(math.abs(cur - prev)) < 0.002f) return cur;
                prev = cur;
                System.Threading.Thread.Sleep(10);
            }
            return cur;
        }

        static float3 RenderOnce(Camera cam, RenderTexture rt, Texture2D tex)
        {
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = null;
            int c = rt.width / 2;
            float3 sum = float3.zero;
            var px = tex.GetPixels32();
            for (int y = c - 1; y <= c; y++) for (int x = c - 1; x <= c; x++) { Color32 p = px[y * rt.width + x]; sum += new float3(Dec(p.r / 255f), Dec(p.g / 255f), Dec(p.b / 255f)); }
            return sum / 4f;
        }

        /// <summary>
        /// Render/readback calibration: an UNLIT cube far from the scene in a handful of known colours must read back as exactly
        /// that colour (an unlit shader ignores lighting), so any dE00 here is pipeline error (sRGB encode/decode, 8-bit
        /// quantisation), not prediction error.
        /// </summary>
        static ErrorStats Calibrate(Camera cam, RenderTexture rt, Texture2D tex)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return new ErrorStats(new List<float>());
            var mat = new Material(shader);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = cube.GetComponent<Collider>(); if (col != null) UnityEngine.Object.DestroyImmediate(col);
            cube.transform.position = new Vector3(0f, -1500f, 0f); cube.GetComponent<Renderer>().sharedMaterial = mat;
            var errors = new List<float>();
            try
            {
                foreach (var hex in new[] { "#C86432", "#404040", "#2A8F8A", "#F1EFE9", "#101010", "#7B4BA8", "#E5322D" })
                {
                    ColorUtility.TryParseHtmlString(hex, out var c);
                    mat.SetColor("_BaseColor", c);
                    var got = RenderCentre(cam, rt, tex, cube.transform.position + new Vector3(0, 0.8f, -3f), cube.transform.position); // slightly above, never on the horizon row
                    float e = ColorMath.Ciede2000(SemanticGraph.LabLinear(SemanticGraph.SrgbToLinear(c)), SemanticGraph.LabLinear(got));
                    errors.Add(e);
                    if (e > 0.5f) Debug.Log($"[C1] calibration patch {hex}: read back {got} vs {SemanticGraph.SrgbToLinear(c)} dE00 {e:0.00}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(cube); UnityEngine.Object.DestroyImmediate(mat); }
            return new ErrorStats(errors);
        }

        // The RT is an sRGB 8-bit target, so the stored bytes are the display-encoded values: decode to linear.
        static float Dec(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);

        /// <summary>Candidate camera directions: the dominant normal first, then the other faces, then oblique views.</summary>
        static IEnumerable<Vector3> ViewDirections(SemanticTag tag, Vector3 dominant)
        {
            yield return dominant;
            var t = tag.transform;
            Vector3[] axes = { t.right, t.up, t.forward, -t.right, -t.up, -t.forward };
            foreach (var a in axes) if (Vector3.Dot(a, dominant) < 0.99f) yield return a;
            foreach (var a in axes) yield return (a + dominant + Vector3.up * 0.5f).normalized;
            yield return Vector3.up;
        }

        static Vector3 SurfaceNormal(RaycastHit hit, Dictionary<Mesh, (Vector3[] n, int[] t)> cache)
        {
            if (hit.collider is MeshCollider mc && mc.sharedMesh != null && mc.sharedMesh.isReadable && hit.triangleIndex >= 0)
            {
                var mesh = mc.sharedMesh;
                if (!cache.TryGetValue(mesh, out var d)) cache[mesh] = d = (mesh.normals, mesh.triangles);
                if (d.n != null && d.n.Length == mesh.vertexCount && hit.triangleIndex * 3 + 2 < d.t.Length)
                {
                    var b = hit.barycentricCoordinate;
                    Vector3 nl = d.n[d.t[hit.triangleIndex * 3]] * b.x + d.n[d.t[hit.triangleIndex * 3 + 1]] * b.y + d.n[d.t[hit.triangleIndex * 3 + 2]] * b.z;
                    // normals transform with the inverse transpose of the object matrix (non-uniform scale safe)
                    Vector3 nw = hit.collider.transform.worldToLocalMatrix.transpose.MultiplyVector(nl).normalized;
                    if (nw.sqrMagnitude > 0.5f) return nw;
                }
            }
            return hit.normal;
        }

        // ---- CSV ----

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        public static string ObjectCsv(IEnumerable<ValidationResult> runs)
        {
            var sb = new StringBuilder("scene,preset,tag_id,name,class,state,unlit,status,px,py,pz,nx,ny,nz,cam_x,cam_y,cam_z,view_index,pred_r,pred_g,pred_b,rend_r,rend_g,rend_b,de00_lit,de00_albedo_baseline\n");
            foreach (var r in runs)
                foreach (var s in r.Samples)
                {
                    bool ok = s.HasRender;
                    sb.Append(string.Join(",", r.Scene, r.Preset, s.Tag.Id, Q(s.Name), Q(s.ClassName), Q(s.StateName), s.Unlit ? 1 : 0, s.Status,
                        ok ? F(s.Point.x) : "", ok ? F(s.Point.y) : "", ok ? F(s.Point.z) : "", ok ? F(s.Normal.x) : "", ok ? F(s.Normal.y) : "", ok ? F(s.Normal.z) : "",
                        ok ? F(s.CameraPosition.x) : "", ok ? F(s.CameraPosition.y) : "", ok ? F(s.CameraPosition.z) : "", ok ? s.ViewIndex.ToString() : "",
                        ok ? F(s.PredictedLin.x) : "", ok ? F(s.PredictedLin.y) : "", ok ? F(s.PredictedLin.z) : "",
                        ok ? F(s.RenderedLin.x) : "", ok ? F(s.RenderedLin.y) : "", ok ? F(s.RenderedLin.z) : "",
                        ok ? F(s.DeltaE00) : "", ok ? F(s.DeltaE00Albedo) : "")).Append('\n');
                }
            return sb.ToString();
        }

        public static string SummaryCsv(IEnumerable<ValidationResult> runs)
        {
            var sb = new StringBuilder("scene,preset,group,n,mean_de00,p95_de00,max_de00,skipped_occluded,skipped_shadowed,skipped_textured\n");
            foreach (var r in runs)
            {
                void Row(string g, ErrorStats e) => sb.Append(string.Join(",", r.Scene, r.Preset, Q(g), e.Count, F(e.Mean), F(e.P95), F(e.Max),
                    r.Count(SampleStatus.Occluded), r.Count(SampleStatus.Shadowed), r.Count(SampleStatus.Textured))).Append('\n');
                Row("ALL", r.Overall);
                Row("ALL_lit_materials", r.Lit);
                Row("ALL_unlit_tagged_objects", r.Unlit);
                Row("CALIBRATION_unlit_patches", r.Calibration);
                Row("SKIPPED_shadowed_objects_rendered_anyway", r.ShadowedError);
                Row("ALL_albedo_only_baseline", r.OverallAlbedo);
                foreach (var kv in r.PerClass.OrderBy(k => k.Key, StringComparer.Ordinal)) Row("class:" + kv.Key, kv.Value);
            }
            return sb.ToString();
        }

        public static string Write(string fileName, string content)
        {
            Directory.CreateDirectory(ResultsDir);
            string path = Path.Combine(ResultsDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>Writes lit_prediction_&lt;scene&gt;.csv (per object, one block per lighting preset) and lit_prediction_&lt;scene&gt;_summary.csv.</summary>
        public static string WriteScene(string scene, IList<ValidationResult> runs)
        {
            Write($"lit_prediction_{scene}_summary.csv", SummaryCsv(runs));
            return Write($"lit_prediction_{scene}.csv", ObjectCsv(runs));
        }

        public static string Describe(ValidationResult r)
        {
            var o = r.Overall;
            return $"{r.Scene}{(string.IsNullOrEmpty(r.Preset) ? "" : "/" + r.Preset)}: sampled {o.Count}, occluded {r.Count(SampleStatus.Occluded)}, shadowed {r.Count(SampleStatus.Shadowed)}, textured {r.Count(SampleStatus.Textured)}; " +
                   $"dE00 lit mean {o.Mean:0.00} p95 {o.P95:0.00} max {o.Max:0.00} (albedo-only baseline mean {r.OverallAlbedo.Mean:0.00}); shadowed-skipped objects dE00 mean {r.ShadowedError.Mean:0.00} max {r.ShadowedError.Max:0.00} (n {r.ShadowedError.Count}); readback calibration max {r.Calibration.Max:0.00}";
        }
    }
}
