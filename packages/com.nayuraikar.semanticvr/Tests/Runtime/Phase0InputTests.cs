using System.Collections;
using NUnit.Framework;
using SemanticVR.Confusion;
using SemanticVR.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SemanticVR.Tests
{
    /// <summary>Drives the demo scene with simulated keyboard input to verify the hotkeys and WASD/arrow locomotion.</summary>
    public class Phase0InputTests : InputTestFixture
    {
        IEnumerator LoadScene()
        {
            var op = SceneManager.LoadSceneAsync("Phase0_Demo", LoadSceneMode.Single);
            while (!op.isDone) yield return null;
            yield return null;
        }

        IEnumerator Tap(Keyboard kb, UnityEngine.InputSystem.Controls.KeyControl key)
        {
            Press(key); yield return null;
            Release(key); yield return null;
        }

        [UnityTest]
        public IEnumerator Hotkeys_ChangeModeTypeSeverityAndStereo()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return LoadScene();
            var dbg = Object.FindFirstObjectByType<DebugViewController>();
            var rig = Object.FindFirstObjectByType<StereoRig>();
            Assert.AreEqual(DebugViewMode.Original, dbg.Mode);

            yield return Tap(kb, kb.digit2Key); Assert.AreEqual(DebugViewMode.Simulated, dbg.Mode); Assert.AreEqual(DebugViewMode.Simulated, CvdDebugState.Mode);
            yield return Tap(kb, kb.digit3Key); Assert.AreEqual(DebugViewMode.Split, dbg.Mode);
            yield return Tap(kb, kb.digit1Key); Assert.AreEqual(DebugViewMode.Original, dbg.Mode);

            yield return Tap(kb, kb.pKey); Assert.AreEqual(CvdType.Protan, dbg.Type);
            yield return Tap(kb, kb.tKey); Assert.AreEqual(CvdType.Tritan, dbg.Type);
            yield return Tap(kb, kb.dKey); Assert.AreEqual(CvdType.Deutan, dbg.Type);
            Assert.AreEqual(CvdType.Deutan, CvdDebugState.Type);

            float s0 = dbg.Severity;
            yield return Tap(kb, kb.leftBracketKey); Assert.AreEqual(s0 - 0.1f, dbg.Severity, 1e-4f);
            yield return Tap(kb, kb.rightBracketKey); yield return Tap(kb, kb.rightBracketKey);
            Assert.AreEqual(1f, dbg.Severity, 1e-4f, "severity clamps at 1");

            Assert.IsTrue(rig.stereo);
            yield return Tap(kb, kb.tabKey); Assert.IsFalse(rig.stereo);
            Assert.AreEqual(new Rect(0, 0, 1, 1), rig.LeftEye.rect, "mono = single full-window camera");
            Assert.IsFalse(rig.RightEye.enabled);
            yield return Tap(kb, kb.tabKey); Assert.IsTrue(rig.stereo);
            yield return null;
            Assert.AreEqual(new Rect(0.5f, 0, 0.5f, 1), rig.RightEye.rect);
            Assert.IsTrue(rig.RightEye.enabled);
        }

        [UnityTest]
        public IEnumerator WasdAndArrows_MoveTheRig()
        {
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return LoadScene();
            var rig = Object.FindFirstObjectByType<StereoRig>();
            Vector3 p0 = rig.transform.position;

            Press(kb.wKey);
            yield return new WaitForSecondsRealtime(0.25f);
            Release(kb.wKey); yield return null;
            Vector3 d = rig.transform.position - p0;
            Assert.Greater(d.z, 0.1f, "W moves forward (+Z at yaw 0)");

            p0 = rig.transform.position;
            Press(kb.rightArrowKey);
            yield return new WaitForSecondsRealtime(0.25f);
            Release(kb.rightArrowKey); yield return null;
            Assert.Greater(rig.transform.position.x - p0.x, 0.1f, "Right arrow strafes right");

            p0 = rig.transform.position;
            Press(kb.sKey);
            yield return new WaitForSecondsRealtime(0.25f);
            Release(kb.sKey); yield return null;
            Assert.Less(rig.transform.position.z - p0.z, -0.1f, "S moves back");
        }
    }
}
