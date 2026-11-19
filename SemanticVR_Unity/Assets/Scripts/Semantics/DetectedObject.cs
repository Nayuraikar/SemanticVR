using SemanticVR.Environment;
using SemanticVR.Perception;

namespace SemanticVR.Semantics
{
    /// <summary>Where the final semantic category came from.</summary>
    public enum CategorySource { None, AI, ManualTag }

    /// <summary>
    /// A <see cref="Detection"/> plus its semantic interpretation. This is separate from the scene's permanent
    /// <see cref="SemanticTag"/> values, which AI detections never overwrite.
    /// </summary>
    public sealed class DetectedObject
    {
        public Detection Detection;
        /// <summary>AI category from the COCO map, valid when <see cref="HasAiCategory"/>.</summary>
        public SemanticCategory AiCategory; public bool HasAiCategory;
        /// <summary>Majority manual SemanticTag category under the mask, valid when <see cref="HasManualCategory"/>.</summary>
        public SemanticCategory ManualCategory; public bool HasManualCategory;
        /// <summary>Share (0..1) of tagged mask pixels that carry the manual category.</summary>
        public float ManualShare;
        /// <summary>Category used downstream, valid when <see cref="Source"/> is not None.</summary>
        public SemanticCategory FinalCategory; public CategorySource Source;
        /// <summary>True when AI and manual labels both exist and differ.</summary>
        public bool Disagreement;
        /// <summary>User-configured importance (0..5) of the final category.</summary>
        public float Importance;
    }
}
