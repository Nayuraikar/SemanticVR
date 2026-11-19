using System;
using UnityEngine;

namespace SemanticVR.World
{
    public enum SignalState { Red, Yellow, Green }
    public enum RoadAxis { NorthSouth, EastWest }

    /// <summary>
    /// Fixed-time two-phase signal for the intersection: NS green, NS yellow, all red, EW green, EW yellow, all red.
    /// Drives the lamp materials of every head (swapping shared materials, no per-frame allocation) and answers the
    /// questions vehicles and pedestrians ask: may I go, and for how long is the road crossing safe?
    /// </summary>
    public sealed class TrafficLightController : MonoBehaviour
    {
        [Serializable]
        public sealed class Head
        {
            public RoadAxis axis;
            public Renderer red, yellow, green;
            [NonSerialized] public SignalState shown = (SignalState)(-1);
        }

        public Head[] heads = new Head[0];
        public Material redOn, yellowOn, greenOn, lampOff;
        [Min(1)] public float greenSeconds = 14f;
        [Min(0.5f)] public float yellowSeconds = 3f;
        [Min(0.5f)] public float allRedSeconds = 2f;
        [Tooltip("Seconds into the cycle at start (0 = NS green begins).")]
        public float startOffset = 0f;

        struct Phase { public SignalState ns, ew; public float dur; }
        Phase[] phases;
        float cycle;
        public float CycleSeconds => cycle;
        public SignalState NorthSouth { get; private set; }
        public SignalState EastWest { get; private set; }
        /// <summary>Raised when either axis changes state.</summary>
        public event Action StateChanged;

        void Awake() { Build(); }

        void Build()
        {
            phases = new[]
            {
                new Phase { ns = SignalState.Green,  ew = SignalState.Red,    dur = greenSeconds },
                new Phase { ns = SignalState.Yellow, ew = SignalState.Red,    dur = yellowSeconds },
                new Phase { ns = SignalState.Red,    ew = SignalState.Red,    dur = allRedSeconds },
                new Phase { ns = SignalState.Red,    ew = SignalState.Green,  dur = greenSeconds },
                new Phase { ns = SignalState.Red,    ew = SignalState.Yellow, dur = yellowSeconds },
                new Phase { ns = SignalState.Red,    ew = SignalState.Red,    dur = allRedSeconds },
            };
            cycle = 0; foreach (var p in phases) cycle += p.dur;
        }

        int PhaseAt(float time, out float intoPhase)
        {
            float t = Mathf.Repeat(time + startOffset, cycle);
            for (int i = 0; i < phases.Length; i++)
            {
                if (t < phases[i].dur) { intoPhase = t; return i; }
                t -= phases[i].dur;
            }
            intoPhase = 0; return 0;
        }

        public SignalState StateFor(RoadAxis a) => a == RoadAxis.NorthSouth ? NorthSouth : EastWest;

        /// <summary>
        /// Seconds from now until vehicles on <paramref name="road"/> next get something other than red (0 if not red now).
        /// A pedestrian crossing that road needs this to exceed the crossing time.
        /// </summary>
        public float SecondsUntilNotRed(RoadAxis road)
        {
            int i = PhaseAt(Time.time, out float into);
            if (Of(phases[i], road) != SignalState.Red) return 0f;
            float t = phases[i].dur - into;
            for (int k = 1; k <= phases.Length; k++)
            {
                var p = phases[(i + k) % phases.Length];
                if (Of(p, road) != SignalState.Red) return t;
                t += p.dur;
            }
            return t;
        }

        static SignalState Of(Phase p, RoadAxis a) => a == RoadAxis.NorthSouth ? p.ns : p.ew;

        void Update()
        {
            int i = PhaseAt(Time.time, out _);
            var ns = phases[i].ns; var ew = phases[i].ew;
            bool changed = ns != NorthSouth || ew != EastWest || heads.Length > 0 && heads[0].shown == (SignalState)(-1);
            NorthSouth = ns; EastWest = ew;
            if (!changed) return;
            foreach (var h in heads)
            {
                var s = StateFor(h.axis);
                if (h.shown == s) continue;
                h.shown = s;
                if (h.red) h.red.sharedMaterial = s == SignalState.Red ? redOn : lampOff;
                if (h.yellow) h.yellow.sharedMaterial = s == SignalState.Yellow ? yellowOn : lampOff;
                if (h.green) h.green.sharedMaterial = s == SignalState.Green ? greenOn : lampOff;
            }
            StateChanged?.Invoke();
        }
    }
}
