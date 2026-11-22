using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>
    /// Links a Renderer to (SemanticClass, state). The state can be changed at runtime (e.g. by a
    /// <see cref="SemanticStateMachine"/>); the base albedo (and emission) of the current state is pushed to the
    /// renderer through a MaterialPropertyBlock, so shared materials are never modified.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("SemanticVR/Semantic Tag")]
    public class SemanticTag : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Renderer targetRenderer;
        [SerializeField] SemanticClass semanticClass;
        [SerializeField] int stateIndex;
        [Tooltip("Stable integer ID (0 = assigned automatically at registration; auto IDs are NOT stable across sessions).")]
        [SerializeField] int stableId;
        [Tooltip("Push the class's state albedo/emission to the renderer. Disable if the material already has the right colour.")]
        public bool applyToRenderer = true;
        [Tooltip("Include in the confusable-pair graph. Disable for huge objects such as the sky dome.")]
        public bool includeInGraph = true;
        [Tooltip("Use 'normalOverride' instead of the automatic dominant face normal when predicting this object's lit colour (C1).")]
        public bool useNormalOverride;
        [Tooltip("Surface normal (LOCAL space of this transform) used for lit-colour prediction when 'useNormalOverride' is on.")]
        public Vector3 normalOverride = Vector3.back;

        MaterialPropertyBlock _mpb;
        int _matInfoFrame = -1;
        bool _unlit, _baseTex;

        /// <summary>Registry-assigned ID (equals <see cref="StableId"/> when that is set and free).</summary>
        public int Id { get; internal set; } = -1;
        public int StableId { get => stableId; set => stableId = value; }
        public Renderer TargetRenderer { get => targetRenderer; set { targetRenderer = value; Apply(); } }
        public SemanticClass Class => semanticClass;
        public int StateIndex => stateIndex;
        public string StateName => semanticClass != null ? semanticClass.GetState(stateIndex)?.name : null;
        public SemanticState CurrentState => semanticClass != null ? semanticClass.GetState(stateIndex) : null;
        /// <summary>Base albedo (sRGB) of the current state; magenta if unconfigured.</summary>
        public Color CurrentAlbedo => CurrentState?.albedo ?? Color.magenta;
        /// <summary>World-space anchor used by the pair graph.</summary>
        public Vector3 Position => targetRenderer != null ? targetRenderer.bounds.center : transform.position;
        /// <summary>World-space bounds used by the pair graph (a zero-size box at the transform if there is no renderer).</summary>
        public Bounds Bounds => targetRenderer != null ? targetRenderer.bounds : new Bounds(transform.position, Vector3.zero);

        /// <summary>True when the renderer's material is an Unlit shader: it shows its colour regardless of lighting (screens, sky dome), so lit prediction returns the albedo.</summary>
        public bool IsUnlit { get { RefreshMaterialInfo(); return _unlit; } }
        /// <summary>True when the material has a base texture: the predictor only knows the tag colour, so such objects are skipped by validation.</summary>
        public bool HasBaseTexture { get { RefreshMaterialInfo(); return _baseTex; } }

        void RefreshMaterialInfo()
        {
            if (_matInfoFrame >= 0) return; // material swaps at runtime are not tracked; call InvalidateMaterialInfo() if you do
            _matInfoFrame = 0;
            var m = targetRenderer != null ? targetRenderer.sharedMaterial : null;
            _unlit = m != null && m.shader != null && m.shader.name.Contains("Unlit");
            _baseTex = m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null;
        }
        public void InvalidateMaterialInfo() => _matInfoFrame = -1;

        /// <summary>
        /// The face normal used to predict this object's lit colour when no surface point is known (world space).
        /// <b>Choice (documented, see docs/design/phase2a.md):</b> take the object's three local axes scaled to world size; the
        /// "dominant" face is the one with the largest area (e.g. the front of a thin panel, the top of a disc). Each axis has two
        /// opposite faces, and axes whose face area is within 5% of the largest count as tied (spheres, cubes, poles), so we
        /// pick, among the candidate faces, the one facing <paramref name="viewer"/> most; with no viewer, the one facing
        /// <paramref name="toLight"/> most; with neither, the most upward one. Override with <see cref="useNormalOverride"/>.
        /// </summary>
        public Vector3 DominantNormal(Vector3? viewer = null, Vector3? toLight = null)
        {
            if (useNormalOverride && normalOverride.sqrMagnitude > 1e-6f) return transform.TransformDirection(normalOverride).normalized;
            if (targetRenderer == null) return Vector3.up;
            Vector3 size = targetRenderer.localBounds.size, sc = transform.lossyScale;
            Vector3 s = new Vector3(Mathf.Abs(size.x * sc.x), Mathf.Abs(size.y * sc.y), Mathf.Abs(size.z * sc.z));
            Vector3 area = new Vector3(s.y * s.z, s.x * s.z, s.x * s.y);
            float max = Mathf.Max(area.x, Mathf.Max(area.y, area.z));
            Vector3 best = Vector3.up; float bestScore = float.NegativeInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                if (area[axis] < max * 0.95f) continue;
                Vector3 a = transform.TransformDirection(axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward).normalized;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = a * sign;
                    float score = viewer.HasValue ? Vector3.Dot(n, (viewer.Value - Position).normalized)
                                : toLight.HasValue ? Vector3.Dot(n, toLight.Value)
                                : n.y;
                    if (score > bestScore) { bestScore = score; best = n; }
                }
            }
            return best;
        }

        void Reset() { targetRenderer = GetComponent<Renderer>(); }
        void OnEnable()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            SemanticRegistry.Register(this);
            Apply();
        }
        void OnDisable() { SemanticRegistry.Unregister(this); }
        void OnValidate()
        {
            if (semanticClass != null && semanticClass.StateCount > 0) stateIndex = Mathf.Clamp(stateIndex, 0, semanticClass.StateCount - 1);
            if (stableId < 0) stableId = 0;
            if (isActiveAndEnabled) Apply();
        }

        /// <summary>Set class and state in one go (used by editor tools, builders and tests).</summary>
        public void Configure(SemanticClass cls, int state)
        {
            semanticClass = cls;
            stateIndex = cls != null && cls.StateCount > 0 ? Mathf.Clamp(state, 0, cls.StateCount - 1) : 0;
            Apply();
            SemanticRegistry.NotifyChanged(this);
        }
        public void Configure(SemanticClass cls, string state)
        {
            int i = cls != null ? cls.IndexOfState(state) : -1;
            Configure(cls, Mathf.Max(i, 0));
        }

        /// <summary>Change state by index. Returns true if the state actually changed.</summary>
        public bool SetState(int index)
        {
            if (semanticClass == null || semanticClass.StateCount == 0) return false;
            index = Mathf.Clamp(index, 0, semanticClass.StateCount - 1);
            if (index == stateIndex) return false;
            int old = stateIndex;
            stateIndex = index;
            Apply();
            SemanticRegistry.NotifyStateChanged(this, old, index);
            return true;
        }

        /// <summary>Change state by name (case-insensitive). Returns false if unknown or unchanged.</summary>
        public bool SetState(string stateName)
        {
            if (semanticClass == null) return false;
            int i = semanticClass.IndexOfState(stateName);
            return i >= 0 && SetState(i);
        }

        /// <summary>Colour to upload as _EmissionColor. Colour properties are gamma->linear converted on upload (HDR ones
        /// included), so the linear intensity is converted to gamma first (the Unity-documented recipe).</summary>
        public static Color EmissionColor(SemanticState s)
        {
            if (s == null || s.emissionIntensity <= 0f) return Color.black;
            var c = s.albedo * Mathf.LinearToGammaSpace(s.emissionIntensity);
            c.a = 1f;
            return c;
        }

        /// <summary>Push the current state's colour to the renderer.</summary>
        public void Apply()
        {
            if (!applyToRenderer || targetRenderer == null || CurrentState == null) return;
            var s = CurrentState;
            _mpb ??= new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, s.albedo);
            _mpb.SetColor(EmissionColorId, EmissionColor(s)); // needs the _EMISSION keyword on the material
            targetRenderer.SetPropertyBlock(_mpb);
        }
    }
}
