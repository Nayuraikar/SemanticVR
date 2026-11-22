using System;
using System.Collections.Generic;
using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>One discrete state of a <see cref="SemanticClass"/> (e.g. "Red" for a traffic signal) and its base albedo.</summary>
    [Serializable]
    public class SemanticState
    {
        public string name = "Default";
        [Tooltip("Base albedo (sRGB) of this state, before lighting and before any correction.")]
        public Color albedo = Color.white;
        [Tooltip("Emission intensity (linear multiplier of albedo). 0 = non-emissive. Used for lamps/signals.")]
        [Min(0f)] public float emissionIntensity = 0f;
    }

    /// <summary>
    /// A semantic class: what an object *is* for the purpose of colour correction. The palette is indexed by
    /// (class, state); both eyes read the same entry.
    /// </summary>
    [CreateAssetMenu(menuName = "SemanticVR/Semantic Class", fileName = "SemanticClass")]
    public class SemanticClass : ScriptableObject
    {
        [Tooltip("Stable integer class ID. Must be unique across the project (SceneValidator checks this).")]
        public int id;
        public string displayName = "Unnamed";
        [Tooltip("Priority prior in [0,1]: how much it matters that this class stays distinguishable.")]
        [Range(0f, 1f)] public float priorityPrior = 0.5f;
        [Tooltip("Naturalness bound tau: the largest CIEDE2000 shift the correction may apply to this class.")]
        [Min(0f)] public float naturalnessTau = 10f;
        [Tooltip("Memory colour (grass, sky, skin...): viewers expect a specific colour, so avoid drastic recolouring.")]
        public bool memoryColour;
        public List<SemanticState> states = new List<SemanticState> { new SemanticState() };

        public int StateCount => states?.Count ?? 0;

        public int IndexOfState(string stateName)
        {
            if (states == null) return -1;
            for (int i = 0; i < states.Count; i++)
                if (string.Equals(states[i].name, stateName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The state at <paramref name="index"/>, clamped; null only when the class has no states.</summary>
        public SemanticState GetState(int index)
        {
            if (StateCount == 0) return null;
            return states[Mathf.Clamp(index, 0, states.Count - 1)];
        }

        public Color GetAlbedo(int stateIndex) => GetState(stateIndex)?.albedo ?? Color.magenta;

        /// <summary>Builder helper for code-created classes (default library, tests).</summary>
        public SemanticClass Configure(int classId, string name, float priority, float tau, bool memory, params SemanticState[] stateList)
        {
            id = classId; displayName = name; priorityPrior = Mathf.Clamp01(priority); naturalnessTau = Mathf.Max(0f, tau);
            memoryColour = memory; states = new List<SemanticState>(stateList);
            return this;
        }

        public static SemanticState State(string name, Color albedo, float emission = 0f) =>
            new SemanticState { name = name, albedo = albedo, emissionIntensity = emission };

        void OnValidate()
        {
            priorityPrior = Mathf.Clamp01(priorityPrior);
            if (states == null) states = new List<SemanticState>();
        }
    }
}
