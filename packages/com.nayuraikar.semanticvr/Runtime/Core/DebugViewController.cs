using SemanticVR.Confusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SemanticVR.Core
{
    /// <summary>
    /// Debug hotkeys for the CVD simulation view.
    /// 1/2/3 = Original / Simulated / Split; P/D/T = protan/deutan/tritan; [ and ] = severity -/+ 0.1; Tab = stereo/mono.
    /// </summary>
    [DisallowMultipleComponent]
    public class DebugViewController : MonoBehaviour
    {
        public DebugViewMode mode = DebugViewMode.Original;
        public CvdType cvdType = CvdType.Deutan;
        [Range(0f, 1f)] public float severity = 1f;
        public float severityStep = 0.1f;
        public StereoRig rig;

        public DebugViewMode Mode => mode;
        public CvdType Type => cvdType;
        public float Severity => severity;
        public bool Stereo => rig != null && rig.stereo;

        void OnEnable()
        {
            if (rig == null) rig = GetComponent<StereoRig>();
            if (rig == null) rig = FindFirstObjectByType<StereoRig>();
            Push();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit1Key.wasPressedThisFrame) SetMode(DebugViewMode.Original);
            if (kb.digit2Key.wasPressedThisFrame) SetMode(DebugViewMode.Simulated);
            if (kb.digit3Key.wasPressedThisFrame) SetMode(DebugViewMode.Split);
            if (kb.pKey.wasPressedThisFrame) SetType(CvdType.Protan);
            if (kb.dKey.wasPressedThisFrame) SetType(CvdType.Deutan);
            if (kb.tKey.wasPressedThisFrame) SetType(CvdType.Tritan);
            if (kb.leftBracketKey.wasPressedThisFrame) SetSeverity(severity - severityStep);
            if (kb.rightBracketKey.wasPressedThisFrame) SetSeverity(severity + severityStep);
            if (kb.tabKey.wasPressedThisFrame) ToggleStereo();
            Push(); // inspector edits propagate too
        }

        public void SetMode(DebugViewMode m) { mode = m; Push(); }
        public void SetType(CvdType t) { cvdType = t; Push(); }
        public void SetSeverity(float s) { severity = Mathf.Round(Mathf.Clamp01(s) * 100f) / 100f; Push(); }
        public void ToggleStereo() { if (rig != null) rig.stereo = !rig.stereo; }

        void Push()
        {
            CvdDebugState.Mode = mode;
            CvdDebugState.Type = cvdType;
            CvdDebugState.Severity = severity;
        }
    }
}
