using NUnit.Framework;
using SemanticVR.Confusion;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SemanticVR.Tests
{
    public class LightingChangeDetectorTests
    {
        Light _sun; GameObject _go;
        AmbientMode _mode; Color _ambient; Light _prevSun;

        [SetUp]
        public void SetUp()
        {
            _mode = RenderSettings.ambientMode; _ambient = RenderSettings.ambientLight; _prevSun = RenderSettings.sun;
            var l = new GameObject("TestSun"); _sun = l.AddComponent<Light>();
            _sun.type = LightType.Directional; _sun.color = Color.white; _sun.intensity = 1f; _sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.sun = _sun; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);
            DynamicGI.UpdateEnvironment();
            _go = new GameObject("Detector");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go); Object.DestroyImmediate(_sun.gameObject);
            RenderSettings.sun = _prevSun; RenderSettings.ambientMode = _mode; RenderSettings.ambientLight = _ambient;
            DynamicGI.UpdateEnvironment();
        }

        [Test]
        public void Fingerprint_DeltaIsZeroForIdentical_AndPositiveForAnyComponentChange()
        {
            var a = IrradianceFingerprint.Capture();
            Assert.AreEqual(0f, IrradianceFingerprint.Delta(a, a), 1e-7f);
            var b = a; b.Main.Color *= 0.5f; Assert.Greater(IrradianceFingerprint.Delta(a, b), 0.05f, "main light colour/intensity");
            b = a; b.Main.ToLight = math.normalize(new float3(-0.5f, 0.5f, 0.3f)); Assert.Greater(IrradianceFingerprint.Delta(a, b), 0.05f, "main light direction");
            b = a; b.Sh.C0 += new float3(0.1f, 0f, 0f); Assert.Greater(IrradianceFingerprint.Delta(a, b), 0.05f, "SH L0");
            b = a; b.Sh.C3 += new float3(0.1f); Assert.Greater(IrradianceFingerprint.Delta(a, b), 0.01f, "SH L1");
        }

        [Test]
        public void Poll_FiresOnceForBigChange_NotForSmallOnes_AndRebaselines()
        {
            var d = _go.AddComponent<LightingChangeDetector>(); d.threshold = 0.05f; d.pollInterval = 0f;
            int fired = 0; float lastDelta = 0f; d.Changed += x => { fired++; lastDelta = x; };
            Assert.IsFalse(d.Poll(), "unchanged lighting does not fire");
            _sun.intensity = 1.01f; Assert.IsFalse(d.Poll(), "a 1% change stays under the threshold"); Assert.AreEqual(0, fired);
            int version = LightingChangeDetector.Version;
            _sun.intensity = 0.1f; _sun.color = new Color(0.5f, 0.6f, 1f);
            RenderSettings.ambientLight = new Color(0.1f, 0.06f, 0.03f); DynamicGI.UpdateEnvironment();
            Assert.IsTrue(d.Poll(), "a Day->Night style change fires"); Assert.AreEqual(1, fired); Assert.Greater(lastDelta, 0.05f);
            Assert.Greater(LightingChangeDetector.Version, version, "static version bumps so caches rebuild");
            Assert.IsFalse(d.Poll(), "baseline moved to the new lighting: no repeat firing");
        }

        [Test]
        public void Poll_FiresWhenSlowDriftAccumulatesPastTheThreshold()
        {
            var d = _go.AddComponent<LightingChangeDetector>(); d.threshold = 0.05f; d.pollInterval = 0f;
            int fired = 0; d.Changed += _ => fired++;
            for (int i = 1; i <= 40; i++) { _sun.transform.rotation = Quaternion.Euler(50 - i * 1.0f, -30, 0); d.Poll(); }
            Assert.GreaterOrEqual(fired, 1, "a sun sweeping 40 degrees fires at least once");
        }
    }
}
