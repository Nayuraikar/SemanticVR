using System;
using Unity.Mathematics;
using UnityEngine;

namespace SemanticVR.Confusion
{
    /// <summary>
    /// A compact description of the scene's diffuse lighting: the ambient SH-L2 coefficients plus the main directional light's
    /// colour and direction. Two fingerprints are compared through the irradiance they produce, not through any image statistics.
    /// </summary>
    public struct IrradianceFingerprint
    {
        public ShL2 Sh;
        public MainLight Main;

        public static IrradianceFingerprint Capture()
        {
            var s = LitColorPredictor.Capture();
            return new IrradianceFingerprint { Sh = s.Ambient, Main = s.Main };
        }

        /// <summary>Fixed probe normals: the 6 axes and the 8 cube diagonals.</summary>
        static readonly float3[] Normals = BuildNormals();
        static float3[] BuildNormals()
        {
            var l = new System.Collections.Generic.List<float3>
            {
                new float3(1, 0, 0), new float3(-1, 0, 0), new float3(0, 1, 0), new float3(0, -1, 0), new float3(0, 0, 1), new float3(0, 0, -1),
            };
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                l.Add(math.normalize(new float3(x, y, z)));
            return l.ToArray();
        }

        public float3 IrradianceAt(float3 n) => LitColorPredictor.Irradiance(Sh, Main, n);

        /// <summary>
        /// RMS over the fixed normals of |E_a(n) - E_b(n)| (Euclidean norm of the linear-RGB irradiance difference). The unit is
        /// "linear reflectance of a white surface": 0.03 is a 3% change in how a white surface is lit, in any direction.
        /// Depends on SH coefficients, main-light colour/intensity and direction only.
        /// </summary>
        public static float Delta(in IrradianceFingerprint a, in IrradianceFingerprint b)
        {
            float sum = 0f;
            foreach (var n in Normals) { float3 d = a.IrradianceAt(n) - b.IrradianceAt(n); sum += math.dot(d, d); }
            return math.sqrt(sum / Normals.Length);
        }
    }

    /// <summary>
    /// Watches the scene lighting and raises <see cref="Changed"/> when the irradiance fingerprint has moved more than
    /// <see cref="threshold"/> from the last committed baseline (slow drifts, e.g. a moving sun, therefore fire once the accumulated
    /// change crosses the threshold, then the baseline resets). The pair graph rebuilds on <see cref="Version"/> bumps.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Lighting Change Detector")]
    public class LightingChangeDetector : MonoBehaviour
    {
        [Tooltip("Irradiance delta (see IrradianceFingerprint.Delta) above which the lighting counts as changed.")]
        public float threshold = 0.03f;
        [Tooltip("Seconds between checks (0 = every frame).")] public float pollInterval = 0.1f;

        /// <summary>Raised with the delta that tripped the threshold.</summary>
        public event Action<float> Changed;
        /// <summary>Raised by any detector instance.</summary>
        public static event Action<float> AnyChanged;
        /// <summary>Bumped whenever any detector fires; consumers poll this to invalidate caches.</summary>
        public static int Version { get; private set; }

        public IrradianceFingerprint Baseline { get; private set; }
        public float LastDelta { get; private set; }
        public int FireCount { get; private set; }
        bool _hasBaseline;
        float _next;

        void OnEnable() { ResetBaseline(); }

        /// <summary>Take the current lighting as the new baseline without firing.</summary>
        public void ResetBaseline() { Baseline = IrradianceFingerprint.Capture(); _hasBaseline = true; LastDelta = 0f; }

        void Update()
        {
            if (pollInterval > 0f && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + pollInterval;
            Poll();
        }

        /// <summary>Compare against the baseline now; fires and re-baselines if over the threshold. Returns true if it fired.</summary>
        public bool Poll()
        {
            var now = IrradianceFingerprint.Capture();
            if (!_hasBaseline) { Baseline = now; _hasBaseline = true; return false; }
            LastDelta = IrradianceFingerprint.Delta(Baseline, now);
            if (LastDelta <= threshold) return false;
            Baseline = now; FireCount++; Version++;
            Changed?.Invoke(LastDelta);
            AnyChanged?.Invoke(LastDelta);
            return true;
        }
    }
}
