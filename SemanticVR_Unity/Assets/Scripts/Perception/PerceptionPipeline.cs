using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using SemanticVR.Semantics;
using Unity.InferenceEngine;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SemanticVR.Perception
{
    /// <summary>Where the analysed frame comes from.</summary>
    public enum FrameSourceMode { GameCamera, StaticPhoto }

    /// <summary>Lifecycle of the pipeline.</summary>
    public enum PipelineStatus { Uninitialised, Initialising, Ready, Failed }

    /// <summary>
    /// Orchestrates the perception stages: frame source -> preprocessing -> inference -> detection parsing -> mask
    /// processing -> semantic mapping. Inference is rate-limited, spread across frames under a per-frame budget and
    /// never blocks rendering; between updates the latest valid result is served. Importance weights are only read here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerceptionPipeline : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Imported rfdetr_seg_nano.onnx (Assets/Models). Not stored in Git, see README.")]
        public ModelAsset modelAsset;
        public InferenceDevice device = InferenceDevice.GPUCompute;
        [Tooltip("Run the golden ONNX Runtime comparison (including the mask-layout check) at start-up.")]
        public bool validateOnStart = true;
        [Tooltip("Log initialisation stages and progress (diagnostics).")]
        public bool verboseLog = false;
        [Tooltip("Fail with a clear message if start-up validation takes longer than this (seconds). GPU shader compilation on first run can take tens of seconds.")]
        [Min(5f)] public float initTimeoutSeconds = 120f;
        [Tooltip("Fail if a single inference stays in flight longer than this (seconds).")]
        [Min(5f)] public float inferenceTimeoutSeconds = 60f;
        public MaskLayout maskLayout = MaskLayout.UnityEinsumTransposed;

        [Header("Frame source")]
        public FrameSourceMode sourceMode = FrameSourceMode.GameCamera;
        public Camera sourceCamera;
        public Texture2D staticPhoto;
        [Tooltip("Capture width in pixels; height follows the camera aspect. Frames are stretched to 312x312 for the model.")]
        public int captureWidth = 640;
        [Tooltip("Width/height of the capture; 0 uses the Game camera's own aspect. The frame is always stretched to a square for the model.")]
        public float captureAspect = 0f;

        [Header("Scheduling")]
        [Min(0f)] public float inferenceIntervalSeconds = 0.5f;
        [Tooltip("Main-thread milliseconds the model may submit per frame. Measured on M1 (GPUCompute, 60 FPS cadence): 4 ms gave p95 frames of ~80 ms, 1 ms gave ~20 ms, 0.25 ms was smooth but inference took ~1.4 s.")]
        [Range(0.1f, 16f)] public float stepBudgetMs = 1f;

        [Header("Detection")]
        [Range(0.05f, 0.95f)] public float confidenceThreshold = 0.3f;
        [Range(0.05f, 0.95f)] public float maskThreshold = 0.5f;
        [Tooltip("Edge of the combined instance-ID map in pixels.")]
        public int instanceMapSize = 156;

        [Header("Semantics")]
        public CocoToSemanticMap cocoMap;
        [Tooltip("Asset with the user's importance weights. A runtime copy is edited at run time.")]
        public SemanticImportanceConfig importanceConfig;
        public bool useManualTags = true;
        public ConflictPolicy conflictPolicy = ConflictPolicy.AiAboveConfidence;
        [Range(0f, 1f)] public float aiConfidenceThreshold = 0.6f;

        // ---- public state
        public PipelineStatus Status { get; private set; } = PipelineStatus.Uninitialised;
        public string StatusMessage { get; private set; } = "";
        public ValidationReport LastValidation { get; private set; }
        public InferenceDevice ActiveDevice { get; private set; }
        public string BackendDescription => backend != null ? backend.Description : "none";
        /// <summary>Latest published result (null until the first inference completes).</summary>
        public PerceptionResult LatestResult { get; private set; }
        /// <summary>Semantic objects for <see cref="LatestResult"/>.</summary>
        public IReadOnlyList<DetectedObject> LatestObjects => objects[cur];
        /// <summary>Runtime copy of the importance weights (edit this at run time, e.g. from UI).</summary>
        public SemanticImportanceConfig Importance { get; private set; }
        public ManualSemanticBuffer ManualBuffer => manual;
        public Texture InstanceIdTexture => masks.InstanceIdTexture;
        public bool IsInferenceRunning => backend != null && backend.IsBusy;
        public int CompletedInferences { get; private set; }
        public float InferenceIntervalMeasured { get; private set; }
        /// <summary>Last captured frame texture (camera RT or photo).</summary>
        public Texture LastFrame { get; private set; }
        /// <summary>Camera holding the pose of the most recent capture (null for photo mode). Used to judge how stale a result is.</summary>
        public Camera CaptureViewCamera => source != null ? source.ViewCamera : null;
        public event Action<PerceptionResult> ResultPublished;

        // ---- internals
        IInferenceBackend backend;
        ICameraFrameSource source;
        ModelInputPreprocessor pre;
        ManualSemanticBuffer manual;
        readonly InferenceScheduler scheduler = new InferenceScheduler();
        readonly DetectionParser parser = new DetectionParser();
        readonly MaskProcessor masks = new MaskProcessor();
        readonly SemanticResolver resolver = new SemanticResolver();
        readonly PerceptionResult[] results = { new PerceptionResult(), new PerceptionResult() };
        readonly List<DetectedObject>[] objects = { new List<DetectedObject>(), new List<DetectedObject>() };
        int cur;
        PerceptionTimings pending;
        bool awaitingManual; float lastPublish, inferenceStartedAt;
        Model model;

        /// <summary>Debug only: blocking readback of the last model input tensor (312*312*3 floats, NCHW).</summary>
        public float[] DebugReadInputTensor()
        {
            using (var cpu = backend.InputTensor.ReadbackAndClone()) return cpu.DownloadToArray();
        }

        void Start() { StartCoroutine(Initialise()); }

        /// <summary>Switches between the Game camera and the static photo at run time.</summary>
        public void SetSourceMode(FrameSourceMode mode)
        {
            sourceMode = mode; BuildSource(); scheduler.Reset();
        }

        IEnumerator Initialise()
        {
            Status = PipelineStatus.Initialising; StatusMessage = "Loading model...";
            Importance = importanceConfig != null ? importanceConfig.CreateRuntimeCopy() : CreateDefaultImportance();
            if (cocoMap != null) { var err = cocoMap.Validate(); if (err != null) { Fail("COCO map does not match class table:\n" + err); yield break; } }
            yield return null;

            Log("loading model");
            model = RfDetrModelLoader.Load(modelAsset, out var error);
            if (model == null) { Fail(error); yield break; }

            ActiveDevice = device;
            if (!RfDetrInferenceBackend.IsSupported(device))
            {
                Debug.LogWarning($"[SemanticVR] {device} unsupported on this device; falling back to CPU.");
                ActiveDevice = InferenceDevice.CPU;
            }
            Log("creating backend " + ActiveDevice);
            backend = new RfDetrInferenceBackend(model, ActiveDevice);
            if (!backend.IsReady) { Fail(backend.LastError ?? "Backend failed to start."); yield break; }

            pre = new ModelInputPreprocessor();
            if (!pre.IsValid) { Fail("Preprocessing shader missing."); yield break; }
            BuildSource();
            if (source == null) { Fail("No frame source available (assign a camera or a static photo)."); yield break; }

            if (validateOnStart)
            {
                StatusMessage = "Validating against golden ONNX Runtime reference (first GPU run can take seconds)...";
                var golden = GoldenReference.Load(out var gerr);
                if (golden == null) { Fail(gerr); yield break; }
                backend.InputTensor.Upload(golden.Input);
                Log("golden: begin");
                backend.Begin();
                int goldenFrames = 0; float goldenStart = Time.realtimeSinceStartup;
                while (!backend.Step(stepBudgetMs))
                {
                    if (verboseLog && ++goldenFrames % 200 == 0) Log($"golden: waiting, frames={goldenFrames}, busy={backend.IsBusy}");
                    if (!backend.IsReady) { Fail(backend.LastError); yield break; }
                    if (Time.realtimeSinceStartup - goldenStart > initTimeoutSeconds) { Fail($"{ActiveDevice} backend did not finish the start-up validation within {initTimeoutSeconds:0} s (known issue: the CPU backend can stall in Editor play mode). Try GPUCompute."); yield break; }
                    yield return null;
                }
                var rep = MaskLayoutValidator.Validate(golden, backend.Outputs.Dets, backend.Outputs.Labels, backend.Outputs.Masks, maskLayout);
                LastValidation = rep;
                if (!rep.Passed) { Fail("Golden validation FAILED. " + rep.Message); yield break; }
                Debug.Log("[SemanticVR] Golden validation passed: " + rep.Message);
            }

            ApplySettings();
            scheduler.Reset();
            Status = PipelineStatus.Ready; StatusMessage = "Ready";
        }

        static SemanticImportanceConfig CreateDefaultImportance() => ScriptableObject.CreateInstance<SemanticImportanceConfig>();

        void BuildSource()
        {
            source?.Dispose(); source = null;
            manual?.Dispose(); manual = null;
            if (sourceMode == FrameSourceMode.GameCamera)
            {
                var cam = sourceCamera != null ? sourceCamera : Camera.main;
                if (cam == null) return;
                source = new CameraRenderTextureSource(cam, captureWidth, captureAspect);
                if (useManualTags) manual = new ManualSemanticBuffer(instanceMapSize);
            }
            else if (staticPhoto != null) source = new StaticTextureSource(staticPhoto);
        }

        void ApplySettings()
        {
            scheduler.IntervalSeconds = inferenceIntervalSeconds; scheduler.StepBudgetMs = stepBudgetMs;
            parser.ConfidenceThreshold = confidenceThreshold;
            masks.MaskProbabilityThreshold = maskThreshold; masks.IdSize = instanceMapSize; masks.Layout = maskLayout;
            resolver.Policy = conflictPolicy; resolver.AiConfidenceThreshold = aiConfidenceThreshold;
        }

        void Update()
        {
            if (Status != PipelineStatus.Ready) return;
            ApplySettings();
            if (!backend.IsReady) { Fail(backend.LastError ?? "Backend fault."); return; }

            if (awaitingManual)
            {
                if (manual == null || !manual.IsPending) FinishResult();
                return;
            }
            if (backend.IsBusy)
            {
                if (backend.Step(scheduler.StepBudgetMs)) OnInferenceDone();
                else if (Time.realtimeSinceStartup - inferenceStartedAt > inferenceTimeoutSeconds) Fail($"Inference exceeded {inferenceTimeoutSeconds:0} s and was abandoned.");
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (scheduler.ShouldStart(now, backend.IsBusy)) StartInference(now);
        }

        void StartInference(float now)
        {
            var sw = Stopwatch.StartNew();
            LastFrame = source.Capture();
            if (manual != null && source.ViewCamera != null) manual.Render(source.ViewCamera);
            pending = default; pending.CaptureMs = (float)sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            pre.Process(LastFrame, backend.InputTensor);
            pending.PreprocessMs = (float)sw.Elapsed.TotalMilliseconds;
            if (backend.Begin()) { inferenceStartedAt = now; InferenceIntervalMeasured = now - lastPublish; scheduler.MarkStarted(now); }
        }

        void OnInferenceDone()
        {
            var next = results[1 - cur];
            var sw = Stopwatch.StartNew();
            parser.Parse(backend.Outputs.Dets, backend.Outputs.Labels, next);
            pending.ParseMs = (float)sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            masks.BuildMasks(next, backend.Outputs.Masks);
            masks.BuildInstanceMap(next);
            pending.MaskMs = (float)sw.Elapsed.TotalMilliseconds;
            pending.InferenceWallMs = backend.LastWallMs; pending.InferenceMainMs = backend.LastMainThreadMs;
            pending.MaxStepMs = backend.LastMaxStepMs; pending.ReadbackMs = backend.LastReadbackMs;
            var fs = source.FrameSize; next.SourceWidth = fs.x; next.SourceHeight = fs.y;
            awaitingManual = manual != null && manual.IsPending;
            if (!awaitingManual) FinishResult();
        }

        void FinishResult()
        {
            awaitingManual = false;
            var next = results[1 - cur];
            var sw = Stopwatch.StartNew();
            resolver.Resolve(next, cocoMap, manual, Importance, objects[1 - cur]);
            pending.SemanticMs = (float)sw.Elapsed.TotalMilliseconds;
            next.Timings = pending; next.PublishedAt = Time.realtimeSinceStartup; next.Serial = ++CompletedInferences;
            lastPublish = next.PublishedAt;
            cur = 1 - cur; LatestResult = next;
            ResultPublished?.Invoke(next);
        }

        void Log(string m) { if (verboseLog) Debug.Log("[SemanticVR] " + m); }

        void Fail(string message)
        {
            Status = PipelineStatus.Failed; StatusMessage = message;
            Debug.LogError("[SemanticVR] Perception pipeline failed: " + message);
        }

        void OnDestroy()
        {
            backend?.Dispose(); pre?.Dispose(); source?.Dispose(); manual?.Dispose(); masks.Dispose();
            if (Importance != null) Destroy(Importance);
        }
    }
}
