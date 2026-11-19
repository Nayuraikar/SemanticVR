using System;
using SemanticVR.Environment;
using UnityEngine;

namespace SemanticVR.Semantics
{
    /// <summary>Context profiles for semantic importance.</summary>
    public enum ImportanceProfileType { RoadSafety = 0, Navigation = 1, General = 2, Custom = 3 }

    /// <summary>
    /// User-controlled semantic importance weights (0..5 per <see cref="SemanticCategory"/>) for four context profiles.
    /// Only user code (UI, inspector, scripts) writes these values. No AI/perception class references this type's
    /// setters, so detections can never change a weight. Changes at runtime go to a runtime copy
    /// (<see cref="CreateRuntimeCopy"/>) so the saved asset is not modified by play-mode edits.
    /// </summary>
    [CreateAssetMenu(menuName = "SemanticVR/Semantic Importance Config", fileName = "SemanticImportanceConfig")]
    public sealed class SemanticImportanceConfig : ScriptableObject
    {
        public const float MinWeight = 0f, MaxWeight = 5f;
        public static readonly int CategoryCount = Enum.GetValues(typeof(SemanticCategory)).Length;

        [Serializable]
        public sealed class Profile
        {
            public string name;
            [Range(0, 5)] public float[] weights;
        }

        [SerializeField] ImportanceProfileType activeProfile = ImportanceProfileType.RoadSafety;
        [SerializeField] Profile[] profiles;
        [SerializeField, Range(0, 5)] float unmappedWeight = 0f;

        /// <summary>Raised after any weight/profile change.</summary>
        public event Action Changed;

        void OnEnable() { EnsureProfiles(); }
        void Reset() { profiles = null; EnsureProfiles(); }
        void OnValidate() { EnsureProfiles(); foreach (var p in profiles) for (int i = 0; i < p.weights.Length; i++) p.weights[i] = Mathf.Clamp(p.weights[i], MinWeight, MaxWeight); }

        /// <summary>Currently selected profile.</summary>
        public ImportanceProfileType ActiveProfile
        {
            get => activeProfile;
            set { activeProfile = value; Changed?.Invoke(); }
        }

        /// <summary>Weight for detections without any semantic category.</summary>
        public float UnmappedWeight => unmappedWeight;

        /// <summary>Weight of <paramref name="c"/> in the active profile.</summary>
        public float GetWeight(SemanticCategory c) => GetWeight(activeProfile, c);

        /// <summary>Weight of <paramref name="c"/> in <paramref name="p"/>.</summary>
        public float GetWeight(ImportanceProfileType p, SemanticCategory c) { EnsureProfiles(); return profiles[(int)p].weights[(int)c]; }

        /// <summary>Sets a weight (clamped to 0..5).</summary>
        public void SetWeight(ImportanceProfileType p, SemanticCategory c, float value)
        {
            EnsureProfiles();
            profiles[(int)p].weights[(int)c] = Mathf.Clamp(value, MinWeight, MaxWeight);
            Changed?.Invoke();
        }

        /// <summary>Sets a weight in the active profile.</summary>
        public void SetWeight(SemanticCategory c, float value) => SetWeight(activeProfile, c, value);

        /// <summary>Restores a profile's built-in defaults (Custom resets to the Road Safety defaults).</summary>
        public void ResetProfile(ImportanceProfileType p)
        {
            EnsureProfiles();
            profiles[(int)p].weights = Defaults(p);
            Changed?.Invoke();
        }

        /// <summary>Copies all weights from one profile to another.</summary>
        public void CopyProfile(ImportanceProfileType from, ImportanceProfileType to)
        {
            EnsureProfiles();
            profiles[(int)to].weights = (float[])profiles[(int)from].weights.Clone();
            Changed?.Invoke();
        }

        /// <summary>Creates an in-memory copy for runtime editing (does not touch the asset).</summary>
        public SemanticImportanceConfig CreateRuntimeCopy()
        {
            var c = CreateInstance<SemanticImportanceConfig>();
            c.name = name + " (runtime)";
            c.EnsureProfiles();
            c.activeProfile = activeProfile; c.unmappedWeight = unmappedWeight;
            EnsureProfiles();
            for (int i = 0; i < profiles.Length; i++) c.profiles[i].weights = (float[])profiles[i].weights.Clone();
            return c;
        }

        void EnsureProfiles()
        {
            if (profiles == null || profiles.Length != 4) profiles = new Profile[4];
            for (int i = 0; i < 4; i++)
            {
                if (profiles[i] == null) profiles[i] = new Profile();
                profiles[i].name = ((ImportanceProfileType)i).ToString();
                if (profiles[i].weights == null || profiles[i].weights.Length != CategoryCount) profiles[i].weights = Defaults((ImportanceProfileType)i);
            }
        }

        /// <summary>
        /// Built-in defaults. Road Safety (and Custom's start) follow the project specification. Navigation and General are
        /// PROPOSED starting points chosen by the developer (not derived from user studies); edit them freely.
        /// Order: Ground, Road, RoadMarking, Sidewalk, TrafficLight, TrafficSign, Vehicle, Pedestrian, Building, Vegetation.
        /// </summary>
        public static float[] Defaults(ImportanceProfileType p)
        {
            switch (p)
            {
                case ImportanceProfileType.Navigation: return new float[] { 1, 4, 5, 4, 4, 5, 3, 3, 3, 1 };
                case ImportanceProfileType.General:    return new float[] { 2, 3, 3, 3, 4, 4, 3, 4, 2, 2 };
                default:                               return new float[] { 1, 3, 4, 3, 5, 5, 4, 5, 1, 1 };
            }
        }
    }
}
