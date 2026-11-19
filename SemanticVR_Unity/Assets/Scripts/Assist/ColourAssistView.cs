using SemanticVR.ColourProcessing;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SemanticVR.Assist
{
    public enum AssistViewMode { Original = 0, Simulated = 1, Corrected = 2 }

    /// <summary>
    /// Shows what the colour processor produces on the player's screen: one full-screen view (Original, CVD-simulated or
    /// Corrected) or a three-way split for comparison. Only the views on screen are rendered. While a processed view is
    /// shown the player camera's own direct render is suspended (the processor re-renders the same camera into the
    /// views), so the scene is drawn once, not twice. Display only: it owns no colour maths and no input.
    /// </summary>
    public sealed class ColourAssistView : MonoBehaviour
    {
        public ColourProcessor processor;
        public Camera playerCamera;
        [Tooltip("Resolution (width) of the processed views.")]
        public int viewWidth = 1280;
        [Tooltip("Disable the player camera's direct render while a processed view covers the screen.")]
        public bool suspendDirectRender = true;

        public AssistViewMode View { get; set; } = AssistViewMode.Corrected;
        public bool Compare { get; set; }
        /// <summary>True when processed output is currently what the player sees.</summary>
        public bool ProcessedOnScreen { get; private set; }

        Canvas canvas; RawImage full; RawImage[] panes = new RawImage[3]; GameObject compareRoot; Text[] paneLabels = new Text[3];
        Camera displayCamera; int framesShown;

        void Awake()
        {
            // The headless colour/perception autotests drive the processor themselves: stay out of their way.
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SVR_AUTOTEST_OUT"))) { enabled = false; return; }
            if (processor == null) processor = GetComponent<ColourProcessor>();
            if (playerCamera == null) playerCamera = Camera.main;
            if (processor != null) processor.previewWidth = viewWidth;
            BuildCanvas();
        }

        void BuildCanvas()
        {
            // A camera that draws nothing keeps the display valid while the player camera is suspended.
            var dc = new GameObject("~AssistDisplayCamera") { hideFlags = HideFlags.DontSave };
            dc.transform.SetParent(transform, false);
            displayCamera = dc.AddComponent<Camera>();
            displayCamera.clearFlags = CameraClearFlags.SolidColor; displayCamera.backgroundColor = Color.black;
            displayCamera.cullingMask = 0; displayCamera.depth = -100;
            var data = displayCamera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;

            var go = new GameObject("AssistViewCanvas");
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -5;
            full = NewImage("Full", go.transform);
            Stretch(full.rectTransform);
            compareRoot = new GameObject("Compare", typeof(RectTransform)); compareRoot.transform.SetParent(go.transform, false);
            Stretch((RectTransform)compareRoot.transform);
            string[] names = { "ORIGINAL", "CVD SIMULATED", "CORRECTED" };
            for (int i = 0; i < 3; i++)
            {
                var img = NewImage("Pane" + i, compareRoot.transform);
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(i / 3f, 0); rt.anchorMax = new Vector2((i + 1) / 3f, 1); rt.offsetMin = rt.offsetMax = Vector2.zero;
                img.uvRect = new Rect(i / 3f, 0, 1 / 3f, 1);          // three slices of one continuous image
                panes[i] = img;
                paneLabels[i] = null;
            }
            full.gameObject.SetActive(false); compareRoot.SetActive(false);
        }

        static RawImage NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<RawImage>(); img.raycastTarget = false; return img;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        /// <summary>Normalised x of the compare dividers (for the UI to draw labels and lines).</summary>
        public static readonly float[] CompareDividers = { 1f / 3f, 2f / 3f };

        void Update()
        {
            if (processor == null || canvas == null) return;
            bool processed = Compare || View != AssistViewMode.Original;
            processor.active = processed;
            processor.renderOriginal = Compare || View == AssistViewMode.Original;
            processor.renderSimulated = Compare || View == AssistViewMode.Simulated;
            processor.renderCorrected = Compare || View == AssistViewMode.Corrected;
            if (!processed) { framesShown = 0; ProcessedOnScreen = false; Show(false); return; }

            Texture o = processor.OriginalView, s = processor.SimulatedView, c = processor.CorrectedView;
            bool ready = c != null && s != null && o != null;
            if (!ready) { Show(false); return; }
            framesShown++;
            if (Compare)
            {
                panes[0].texture = o; panes[1].texture = s; panes[2].texture = c;
                full.gameObject.SetActive(false); compareRoot.SetActive(true);
            }
            else
            {
                full.texture = View == AssistViewMode.Original ? o : View == AssistViewMode.Simulated ? s : c;
                full.gameObject.SetActive(true); compareRoot.SetActive(false);
            }
            ProcessedOnScreen = framesShown > 2;
            Show(true);
        }

        void LateUpdate()
        {
            // Suspend the direct render only after the processor has produced valid frames (needs the camera once).
            if (playerCamera != null && suspendDirectRender) playerCamera.enabled = !ProcessedOnScreen;
            else if (playerCamera != null) playerCamera.enabled = true;
        }

        void Show(bool on) { canvas.enabled = on; }

        void OnDisable() { if (playerCamera != null) playerCamera.enabled = true; }
    }
}
