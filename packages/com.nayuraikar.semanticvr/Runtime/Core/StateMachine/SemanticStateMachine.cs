using System;
using System.Collections.Generic;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>
    /// Small timer-driven FSM for traffic signals, rail signals, hazard lights and status boards. A cycle is a list of
    /// (state name, duration) steps; every target tag shows the step at (time + its phase offset) modulo the cycle length,
    /// so opposing signals just use different offsets. Deterministic: <see cref="StateAt"/> is a pure function of time.
    /// </summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Semantic State Machine")]
    public class SemanticStateMachine : MonoBehaviour
    {
        [Serializable] public class Step { public string state = "Red"; [Min(0.01f)] public float duration = 5f; }
        [Serializable] public class Target { public SemanticTag tag; [Tooltip("Seconds added to the clock for this target.")] public float phaseSeconds; }

        public List<Step> cycle = new List<Step>();
        public List<Target> targets = new List<Target>();
        [Tooltip("Run the clock from Update. Disable to drive it from code/tests with Advance()/SetTime().")]
        public bool autoRun = true;
        public float timeScale = 1f;
        [Tooltip("Initial clock value (seconds).")] public float startTime;

        public float Time { get; private set; }
        public float CycleLength { get { float t = 0; foreach (var s in cycle) t += Mathf.Max(0.01f, s.duration); return t; } }

        void OnEnable() { Time = startTime; Evaluate(); }
        void Update()
        {
            if (!autoRun || !Application.isPlaying) return;
            Advance(UnityEngine.Time.deltaTime * timeScale);
        }

        public void Advance(float dt) { Time += dt; Evaluate(); }
        public void SetTime(float t) { Time = t; Evaluate(); }

        /// <summary>State name shown at clock value <paramref name="t"/> (seconds), or null for an empty cycle.</summary>
        public string StateAt(float t) => cycle.Count == 0 ? null : cycle[StepIndexAt(t)].state;

        public int StepIndexAt(float t)
        {
            float len = CycleLength;
            if (cycle.Count == 0 || len <= 0f) return -1;
            float x = t - len * Mathf.Floor(t / len);
            for (int i = 0; i < cycle.Count; i++)
            {
                x -= Mathf.Max(0.01f, cycle[i].duration);
                if (x < 0f) return i;
            }
            return cycle.Count - 1;
        }

        /// <summary>Push the current step to every target.</summary>
        public void Evaluate()
        {
            foreach (var tg in targets)
                if (tg != null && tg.tag != null) tg.tag.SetState(StateAt(Time + tg.phaseSeconds));
        }

        public SemanticStateMachine AddStep(string state, float duration) { cycle.Add(new Step { state = state, duration = duration }); return this; }
        public SemanticStateMachine AddTarget(SemanticTag tag, float phase = 0f) { targets.Add(new Target { tag = tag, phaseSeconds = phase }); return this; }
    }
}
