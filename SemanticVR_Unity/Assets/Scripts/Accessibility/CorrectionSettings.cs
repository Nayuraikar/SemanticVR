using System;
using System.Collections.Generic;
using SemanticVR.ColourProcessing;
using UnityEngine;

namespace SemanticVR.Accessibility
{
    /// <summary>A pair of colours the user finds hard to tell apart, with a priority weight (0..5). Input for future optimisation; the baseline only reports their distinguishability.</summary>
    [Serializable]
    public struct ColourPair
    {
        public string label;
        public Color a, b;
        [Range(0, 5)] public float weight;
    }

    /// <summary>User preferences for the correction (strength, mode, troublesome colour pairs). Separate from the CVD profile and from the algorithm.</summary>
    [CreateAssetMenu(menuName = "SemanticVR/Correction Settings", fileName = "CorrectionSettings")]
    public sealed class CorrectionSettings : ScriptableObject
    {
        [Range(0, 1)] public float strength = 1f;
        public CorrectionMode mode = CorrectionMode.Balanced;
        [Range(0.5f, 2f), Tooltip("Contrast for Custom mode.")] public float customContrast = 1f;
        [Tooltip("Error-shift matrix E for Custom mode / Custom CVD type: C = I + k * E * (I - S).")]
        public Matrix3x3 customErrorShift = BaselineCorrection.ErrorShiftProtanDeutan;
        public List<ColourPair> troublesomePairs = DefaultPairs();

        /// <summary>Colour pairs commonly confused by red-green CVD (illustrative defaults, editable).</summary>
        public static List<ColourPair> DefaultPairs() => new List<ColourPair>
        {
            new ColourPair { label = "Red / Green",    a = new Color(0.85f, 0.15f, 0.15f), b = new Color(0.15f, 0.60f, 0.15f), weight = 3 },
            new ColourPair { label = "Green / Brown",  a = new Color(0.15f, 0.60f, 0.15f), b = new Color(0.55f, 0.35f, 0.10f), weight = 2 },
            new ColourPair { label = "Orange / Green", a = new Color(1.00f, 0.55f, 0.00f), b = new Color(0.40f, 0.70f, 0.10f), weight = 2 },
            new ColourPair { label = "Blue / Purple",  a = new Color(0.20f, 0.30f, 0.90f), b = new Color(0.55f, 0.25f, 0.75f), weight = 1 },
        };

        /// <summary>Restores defaults.</summary>
        public void ResetToDefault() { strength = 1f; mode = CorrectionMode.Balanced; customContrast = 1f; customErrorShift = BaselineCorrection.ErrorShiftProtanDeutan; troublesomePairs = DefaultPairs(); }

        /// <summary>Creates an in-memory copy for runtime editing.</summary>
        public CorrectionSettings CreateRuntimeCopy() { var c = CreateInstance<CorrectionSettings>(); c.name = name + " (runtime)"; JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), c); return c; }

        public void SaveToFile(string path) => System.IO.File.WriteAllText(path, JsonUtility.ToJson(this, true));
        public bool LoadFromFile(string path) { if (!System.IO.File.Exists(path)) return false; JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(path), this); return true; }
    }
}
