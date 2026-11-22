using System.Collections;
using System.IO;
using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SemanticVR.Tests
{
    /// <summary>
    /// Smoke + visual-capture tests for Assets/Scenes/Phase0_Demo.unity. Loads the scene, renders the stereo rig into a
    /// side-by-side RenderTexture in each debug mode (exercising the Render Graph CVD feature), writes PNGs to
    /// results/quality/phase0/, and checks that Unity logged no errors (the test runner fails on error logs).
    /// </summary>
    public class Phase0DemoTests
    {
        const int W = 1920, H = 540; // two 960x540 eye views side by side

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../results/quality/phase0"));

        StereoRig rig;
        DebugViewController dbg;

        IEnumerator Load()
        {
            var op = SceneManager.LoadSceneAsync("Phase0_Demo", LoadSceneMode.Single);
            Assert.IsNotNull(op, "Phase0_Demo must be in Build Settings");
            while (!op.isDone) yield return null;
            yield return null;
            rig = Object.FindFirstObjectByType<StereoRig>();
            dbg = Object.FindFirstObjectByType<DebugViewController>();
            Assert.IsNotNull(rig); Assert.IsNotNull(dbg);
        }

        static Texture2D Render(StereoRig rig)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rig.LeftEye.targetTexture = rt;
            rig.RightEye.targetTexture = rt;
            rig.Apply(); // recompute projection for the new target size
            return ReadBack(rig, rt);
        }

        static Texture2D ReadBack(StereoRig rig, RenderTexture rt)
        {
            if (rig.stereo) { rig.LeftEye.Render(); rig.RightEye.Render(); } else rig.LeftEye.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            rig.LeftEye.targetTexture = null; rig.RightEye.targetTexture = null;
            rt.Release();
            return tex;
        }

        static void Save(Texture2D t, string name)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), t.EncodeToPNG());
        }

        [UnityTest]
        public IEnumerator Scene_LoadsWithTwoEyeCameras_AndRendersAllModes()
        {
            yield return Load();
            Assert.AreEqual(2, rig.GetComponentsInChildren<Camera>().Length);
            Assert.AreEqual(0.064f, rig.ipd, 1e-6f);
            Assert.AreEqual(new Rect(0, 0, 0.5f, 1), rig.LeftEye.rect);
            Assert.AreEqual(new Rect(0.5f, 0, 0.5f, 1), rig.RightEye.rect);
            Assert.IsTrue(rig.TryGetComponent<HeadPoseGazeSource>(out var gaze));
            Assert.IsTrue(gaze.TryGetGazeRay(out var ray));
            Assert.That(Vector3.Dot(ray.direction, Vector3.forward), Is.GreaterThan(0.99f));

            dbg.SetType(CvdType.Deutan); dbg.SetSeverity(1f);

            dbg.SetMode(DebugViewMode.Original); yield return null;
            Save(Render(rig), "01_original_stereo");
            dbg.SetMode(DebugViewMode.Simulated); yield return null;
            Save(Render(rig), "02_deutan_simulated_stereo");
            dbg.SetMode(DebugViewMode.Split); yield return null;
            Save(Render(rig), "03_deutan_split_stereo");
            dbg.SetType(CvdType.Protan); dbg.SetMode(DebugViewMode.Simulated); yield return null;
            Save(Render(rig), "04_protan_simulated_stereo");
            rig.stereo = false; dbg.SetMode(DebugViewMode.Original); yield return null;
            Save(Render(rig), "05_mono_original");
            dbg.SetMode(DebugViewMode.Original);
        }

        [UnityTest]
        public IEnumerator OffAxisProjection_IsAsymmetric_AndMirrored()
        {
            yield return Load();
            var l = rig.OffAxisProjection(-0.5f * rig.ipd, 960, 540);
            var r = rig.OffAxisProjection(+0.5f * rig.ipd, 960, 540);
            Assert.That(Mathf.Abs(l.m02 + r.m02), Is.LessThan(1e-6f), "frusta must be mirror images");
            Assert.That(Mathf.Abs(l.m02), Is.GreaterThan(1e-3f), "off-axis shear expected");
            Assert.AreEqual(l.m00, r.m00, 1e-6f);
            Assert.AreEqual(l.m11, r.m11, 1e-6f);
        }

        [UnityTest]
        public IEnumerator FrameTime_Report()
        {
            yield return Load();
            dbg.SetMode(DebugViewMode.Simulated);
            for (int i = 0; i < 30; i++) yield return null; // warm up
            float t0 = Time.realtimeSinceStartup; int n = 120;
            for (int i = 0; i < n; i++) yield return null;
            float ms = (Time.realtimeSinceStartup - t0) / n * 1000f;
            Debug.Log($"[Phase0] avg frame time (batch, simulated stereo): {ms:0.00} ms ({1000f / ms:0} fps), {Screen.width}x{Screen.height}");
            dbg.SetMode(DebugViewMode.Original);
        }
    }
}
