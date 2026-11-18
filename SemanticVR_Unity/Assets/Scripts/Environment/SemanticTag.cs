using UnityEngine;

namespace SemanticVR.Environment
{
    /// <summary>
    /// Marks a GameObject (and its children) as belonging to a semantic category.
    /// Phase 1: label only. No weights, no AI, no colour processing.
    /// </summary>
    [DisallowMultipleComponent]
    public class SemanticTag : MonoBehaviour
    {
        [SerializeField] private SemanticCategory category = SemanticCategory.Ground;

        public SemanticCategory Category
        {
            get => category;
            set => category = value;
        }
    }
}
