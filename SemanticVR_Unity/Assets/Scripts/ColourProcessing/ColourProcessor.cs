using System.Diagnostics;
using SemanticVR.Accessibility;
using SemanticVR.Perception;
using SemanticVR.Semantics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SemanticVR.ColourProcessing
{
    /// <summary>
    /// Baseline colour processing: builds the matrices from the user's CVD profile and correction settings (runtime
    /// copies, editable from the UI) and produces three views of the scene, Original / CVD-simulated / Corrected, through
    /// one URP-compatible shader. Not the PAGP optimiser. Assign <see cref="Provider"/> to plug one in later.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ColourProcessor : MonoBehaviour
    {
        public CvdProfile profileAsset;
        public CorrectionSettings settingsAsset;
        public EnvironmentColourContext environment = EnvironmentColourContext.Daylight;
        public Camera sourceCamera;
        [Tooltip("Optional perception pipeline: supplies the static photo, semantic objects and instance map to the context.")]
        public PerceptionPipeline perception;
        [Min(64)] public int previewWidth = 480;
        [Tooltip("Run the three-view processing every frame. Disable to save frame time.")]
        public bool active = true;
        [Tooltip("Corrected view shows what a viewer with this CVD would perceive after correction (simulation applied on top).")]
        public bool correctedAsPerceived = false;
        [Tooltip("Optional priority-aware layer (SemanticAssistLayer). Null = uniform correction, exactly the baseline.")]
        public ISemanticAssist semanticAssist;
        [Tooltip("Which of the three views to render each frame (the interactive demo only renders what it shows).")]
        public bool renderOriginal = true, renderSimulated = true, renderCorrected = true;

        public CvdProfile Profile { get; private set; }
        public CorrectionSettings Settings { get; private set; }
        public ColourTransform Current { get; private set; }
        public Texture OriginalView => original;
        public Texture SimulatedView => simulated;
        public Texture CorrectedView => corrected;
        /// <summary>Camera whose pose/projection describe the last captured frame (null for photo mode / before the first frame).</summary>
        public Camera CaptureCamera => source != null ? source.ViewCamera : null;
        /// <summary>True when the last corrected view used per-pixel priority weights.</summary>
        public bool LastCorrectionPriorityAware { get; private set; }
        /// <summary>CPU time to capture + build matrices + issue the three blits, last frame (GPU time not measured here).</summary>
        public float LastProcessMs { get; private set; }
        public float LastMatrixMs { get; private set; }
        public ColourMatrixPipeline MatrixPipeline { get; } = new ColourMatrixPipeline();
        /// <summary>The matrix provider (default baseline). Replace to plug in another optimiser.</summary>
        public IColourMatrixProvider Provider { get => MatrixPipeline.Provider; set => MatrixPipeline.Provider = value; }

        Material mat;
        RenderTexture original, simulated, corrected;
        ICameraFrameSource source; Camera sourceFor;
        static readonly int M0 = Shader.PropertyToID("_M0"), M1 = Shader.PropertyToID("_M1"), M2 = Shader.PropertyToID("_M2");
        static readonly int B0 = Shader.PropertyToID("_B0"), B1 = Shader.PropertyToID("_B1"), B2 = Shader.PropertyToID("_B2"), UseSem = Shader.PropertyToID("_UseSemantic");
        static readonly int ContrastId = Shader.PropertyToID("_Contrast"), GammaId = Shader.PropertyToID("_GammaProject"), EnvId = Shader.PropertyToID("_Env");

        void Awake()
        {
            Profile = profileAsset != null ? profileAsset.CreateRuntimeCopy() : ScriptableObject.CreateInstance<CvdProfile>();
            Settings = settingsAsset != null ? settingsAsset.CreateRuntimeCopy() : ScriptableObject.CreateInstance<CorrectionSettings>();
            var shader = Resources.Load<Shader>("SemanticVR/ColourTransform");
            if (shader == null) { Debug.LogError("[SemanticVR] ColourTransform shader missing."); enabled = false; return; }
            mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.SetFloat(GammaId, QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1f : 0f);
        }

        /// <summary>Resets profile and settings to the assets' saved values.</summary>
        public void ResetToAssets()
        {
            if (profileAsset != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profileAsset), Profile); else Profile.ResetToDefault();
            if (settingsAsset != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settingsAsset), Settings); else Settings.ResetToDefault();
        }

        ColourContext MakeContext() => new ColourContext
        {
            Profile = Profile, Settings = Settings, Environment = environment,
            Objects = perception != null ? perception.LatestObjects : null,
            InstanceIdMap = perception != null ? perception.InstanceIdTexture : null,
            Importance = perception != null ? perception.Importance : null
        };

        void EnsureTargets(int w, int h)
        {
            if (original != null && original.width == w && original.height == h) return;
            Release();
            original = New(w, h, "CvdOriginal"); simulated = New(w, h, "CvdSimulated"); corrected = New(w, h, "CvdCorrected");
        }

        static RenderTexture New(int w, int h, string n) { var r = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = n }; r.Create(); return r; }

        void Apply(Texture src, RenderTexture dst, Matrix3x3 m, float contrast, bool semantic = false, Matrix3x3? baseMatrix = null)
        {
            mat.SetVector(M0, m.Row0); mat.SetVector(M1, m.Row1); mat.SetVector(M2, m.Row2);
            var bm = baseMatrix ?? Matrix3x3.Identity;
            mat.SetVector(B0, bm.Row0); mat.SetVector(B1, bm.Row1); mat.SetVector(B2, bm.Row2);
            mat.SetFloat(UseSem, 0f);
            if (semantic) { semanticAssist.Bind(mat); mat.SetFloat(UseSem, 1f); }
            mat.SetFloat(ContrastId, contrast);
            mat.SetVector(EnvId, new Vector4(environment.nightFactor, environment.shadowStrength, environment.fogDensity, 0));
            Graphics.Blit(src, dst, mat);
        }

        Texture AcquireFrame()
        {
            if (perception != null && perception.sourceMode == FrameSourceMode.StaticPhoto && perception.staticPhoto != null)
            {
                source?.Dispose(); source = null; sourceFor = null;
                return perception.staticPhoto;
            }
            var cam = sourceCamera != null ? sourceCamera : Camera.main;
            if (cam == null) return null;
            if (source == null || sourceFor != cam || source is StaticTextureSource) { source?.Dispose(); source = new CameraRenderTextureSource(cam, previewWidth); sourceFor = cam; }
            return source.Capture();
        }

        void LateUpdate()
        {
            if (!active || mat == null) return;
            var sw = Stopwatch.StartNew();
            var frame = AcquireFrame();
            if (frame == null) return;
            RenderViews(frame, source != null ? source.ViewCamera : null);
            LastProcessMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Builds the matrices from the current profile/settings and renders the three views from <paramref name="frame"/>.</summary>
        public void RenderViews(Texture frame, Camera viewCamera = null)
        {
            if (mat == null) return;
            int w = previewWidth, h = Mathf.Max(16, Mathf.RoundToInt(previewWidth * (float)frame.height / frame.width));
            EnsureTargets(w, h);
            var ms = Stopwatch.StartNew();
            Current = MatrixPipeline.Build(MakeContext());
            LastMatrixMs = (float)ms.Elapsed.TotalMilliseconds;
            var t = Current;
            if (!t.Valid) { Apply(frame, original, Matrix3x3.Identity, 1f); Apply(frame, simulated, Matrix3x3.Identity, 1f); Apply(frame, corrected, Matrix3x3.Identity, 1f); return; }
            if (renderOriginal) Apply(frame, original, Matrix3x3.Identity, 1f);
            if (renderSimulated) Apply(frame, simulated, t.Simulation, 1f);
            if (renderCorrected)
            {
                bool sem = semanticAssist != null && viewCamera != null && semanticAssist.Prepare(viewCamera);
                LastCorrectionPriorityAware = sem;
                if (correctedAsPerceived) Apply(frame, corrected, t.Simulation * t.Correction, t.Contrast, sem, sem ? t.Simulation : (Matrix3x3?)null);
                else Apply(frame, corrected, t.Correction, t.Contrast, sem);
            }
        }

        void Release()
        {
            foreach (var r in new[] { original, simulated, corrected }) if (r != null) { r.Release(); Destroy(r); }
            original = simulated = corrected = null;
        }

        void OnDestroy()
        {
            Release(); source?.Dispose();
            if (mat != null) Destroy(mat);
            if (Profile != null) Destroy(Profile);
            if (Settings != null) Destroy(Settings);
        }
    }
}
