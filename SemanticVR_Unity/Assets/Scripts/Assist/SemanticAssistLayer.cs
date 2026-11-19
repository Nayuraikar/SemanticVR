using SemanticVR.ColourProcessing;
using SemanticVR.Environment;
using SemanticVR.Perception;
using SemanticVR.Semantics;
using UnityEngine;

namespace SemanticVR.Assist
{
    /// <summary>
    /// Makes the colour correction priority-aware while the player moves. Per pixel it supplies the user's importance
    /// weight (0..5, from <see cref="SemanticImportanceConfig"/>, never changed here) from two sources:
    ///  1. the live SemanticTag category buffer, rendered every frame from the current camera (tracks motion exactly);
    ///  2. the AI instance map of the latest RF-DETR result (snapshotted when it is published), faded out as the camera
    ///     moves away from the pose it was captured at, because the result is then stale.
    /// The larger of the two wins. Correction strength per pixel = bg + (1 - bg) * weight/5 with bg = 1 - focus, so
    /// focus 0 reproduces the uniform baseline and focus 1 corrects only what the user marked important.
    /// </summary>
    public sealed class SemanticAssistLayer : MonoBehaviour, ISemanticAssist
    {
        public ColourProcessor processor;
        public PerceptionPipeline perception;
        [Tooltip("Master switch. Off = uniform correction over the whole image.")]
        public bool prioritise = true;
        [Range(0f, 1f), Tooltip("0 = same strength everywhere, 1 = only important objects are corrected.")]
        public float focus = 0.5f;
        [Tooltip("Also use the (faded) AI instance masks. Off = SemanticTag weights only.")]
        public bool useAiMasks = true;
        [Min(64)] public int tagBufferSize = 192;
        [Header("Staleness of AI masks")]
        public float aiFadeDegrees = 12f, aiFadeMetres = 2.5f, aiMaxAgeSeconds = 4f;

        public float AiFade { get; private set; }
        public int AiObjectCount { get; private set; }
        public float LastPrepareMs { get; private set; }
        /// <summary>Live tag buffer (R8, (category + 1)/255), for tests and debug views.</summary>
        public Texture TagTexture => tags != null ? tags.Texture : null;

        ManualSemanticBuffer tags;
        Texture2D idSnapshot;
        readonly float[] catW = new float[16];
        readonly float[] idW = new float[128];
        Vector3 snapPos; Quaternion snapRot; float snapTime = -999f; bool haveSnap;
        static readonly int CatTex = Shader.PropertyToID("_CatTex"), IdTex = Shader.PropertyToID("_IdTex"), BgId = Shader.PropertyToID("_BgAssist"),
            FadeId = Shader.PropertyToID("_AiFade"), CatWId = Shader.PropertyToID("_CatW"), IdWId = Shader.PropertyToID("_IdW");

        void OnEnable()
        {
            if (processor == null) processor = GetComponent<ColourProcessor>();
            if (perception == null) perception = GetComponent<PerceptionPipeline>();
            if (processor != null) processor.semanticAssist = this;
            if (perception != null) perception.ResultPublished += OnResult;
        }

        void OnDisable()
        {
            if (processor != null && ReferenceEquals(processor.semanticAssist, this)) processor.semanticAssist = null;
            if (perception != null) perception.ResultPublished -= OnResult;
        }

        void OnDestroy()
        {
            tags?.Dispose(); if (idSnapshot != null) Destroy(idSnapshot);
        }

        void OnResult(PerceptionResult r)
        {
            var src = perception.InstanceIdTexture as Texture2D;
            var cam = perception.CaptureViewCamera;
            if (src == null || cam == null) return;
            if (idSnapshot == null || idSnapshot.width != src.width)
            {
                if (idSnapshot != null) Destroy(idSnapshot);
                idSnapshot = new Texture2D(src.width, src.height, TextureFormat.R8, false, true) { name = "InstanceIdSnapshot", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            Graphics.CopyTexture(src, idSnapshot);   // map, weights and pose below all belong to this one result
            System.Array.Fill(idW, -1f);
            int n = 0;
            foreach (var o in perception.LatestObjects)
            {
                int id = o.Detection.FrameIndex;
                if (id <= 0 || id >= idW.Length) continue;
                idW[id] = Mathf.Clamp01(o.Importance / SemanticImportanceConfig.MaxWeight);
                n++;
            }
            AiObjectCount = n;
            snapPos = cam.transform.position; snapRot = cam.transform.rotation; snapTime = Time.realtimeSinceStartup; haveSnap = true;
        }

        public bool Prepare(Camera view)
        {
            if (!prioritise || view == null) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (tags == null) tags = new ManualSemanticBuffer(tagBufferSize);
            if (!tags.IsValid) return false;
            tags.Render(view, readback: false);

            var imp = perception != null && perception.Importance != null ? perception.Importance : (perception != null ? perception.importanceConfig : null);
            catW[0] = -1f;                                       // untagged (sky, untagged props)
            for (int c = 0; c < SemanticImportanceConfig.CategoryCount; c++)
                catW[c + 1] = imp != null ? Mathf.Clamp01(imp.GetWeight((SemanticCategory)c) / SemanticImportanceConfig.MaxWeight) : 1f;
            for (int c = SemanticImportanceConfig.CategoryCount + 1; c < catW.Length; c++) catW[c] = -1f;

            float fade = 0f;
            if (haveSnap && idSnapshot != null)
            {
                float ang = Quaternion.Angle(snapRot, view.transform.rotation), dist = Vector3.Distance(snapPos, view.transform.position);
                float age = Time.realtimeSinceStartup - snapTime;
                fade = Mathf.Clamp01(1f - ang / aiFadeDegrees) * Mathf.Clamp01(1f - dist / aiFadeMetres) * Mathf.Clamp01(1f - age / aiMaxAgeSeconds);
            }
            AiFade = fade;
            LastPrepareMs = (float)sw.Elapsed.TotalMilliseconds;
            return true;
        }

        public void Bind(Material m)
        {
            m.SetTexture(CatTex, tags.Texture);
            m.SetTexture(IdTex, idSnapshot != null ? (Texture)idSnapshot : Texture2D.blackTexture);
            m.SetFloat(BgId, 1f - focus);
            m.SetFloat(FadeId, useAiMasks ? AiFade : 0f);
            m.SetFloatArray(CatWId, catW);
            m.SetFloatArray(IdWId, idW);
        }
    }
}
