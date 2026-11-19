using SemanticVR.ColourProcessing;
using UnityEngine;

namespace SemanticVR.Accessibility
{
    /// <summary>
    /// The user's colour vision: type, severity and an optional custom simulation matrix. Describes the viewer only;
    /// it contains no correction algorithm (see <see cref="CorrectionSettings"/> and <see cref="ColourMatrixPipeline"/>).
    /// Serialisable to JSON for saving user profiles.
    /// </summary>
    [CreateAssetMenu(menuName = "SemanticVR/CVD Profile", fileName = "CvdProfile")]
    public sealed class CvdProfile : ScriptableObject
    {
        public CvdType type = CvdType.Deuteranomaly;
        [Range(0, 100), Tooltip("Severity in percent. Used by -omaly types, Achromatopsia and Custom. Dichromacy types are always 100%.")]
        public float severityPercent = 100f;
        [Tooltip("Custom simulation matrix on linear RGB (used when type is Custom).")]
        public Matrix3x3 customMatrix = Matrix3x3.Identity;

        /// <summary>Severity as 0..1 after applying the type's rules.</summary>
        public float EffectiveSeverity01
        {
            get
            {
                switch (type)
                {
                    case CvdType.Normal: return 0f;
                    case CvdType.Protanopia: case CvdType.Deuteranopia: case CvdType.Tritanopia: return 1f;
                    default: return Mathf.Clamp01(severityPercent / 100f);
                }
            }
        }

        /// <summary>Restores the defaults (deuteranomaly, 100%, identity custom matrix).</summary>
        public void ResetToDefault() { type = CvdType.Deuteranomaly; severityPercent = 100f; customMatrix = Matrix3x3.Identity; }

        /// <summary>Creates an in-memory copy for runtime editing.</summary>
        public CvdProfile CreateRuntimeCopy() { var c = CreateInstance<CvdProfile>(); c.name = name + " (runtime)"; JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), c); return c; }

        /// <summary>Writes the profile as JSON.</summary>
        public void SaveToFile(string path) => System.IO.File.WriteAllText(path, JsonUtility.ToJson(this, true));
        /// <summary>Loads a profile written by <see cref="SaveToFile"/>.</summary>
        public bool LoadFromFile(string path) { if (!System.IO.File.Exists(path)) return false; JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(path), this); return true; }
    }
}
