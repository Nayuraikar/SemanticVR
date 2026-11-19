using SemanticVR.Environment;
using UnityEngine;

namespace SemanticVR.UI
{
    /// <summary>Fixed debug colours per semantic category (visualisation only).</summary>
    public static class CategoryPalette
    {
        static readonly Color[] Colours =
        {
            new Color(0.55f,0.45f,0.30f), new Color(0.50f,0.50f,0.55f), new Color(1.00f,1.00f,1.00f), new Color(0.70f,0.70f,0.45f),
            new Color(1.00f,0.15f,0.15f), new Color(1.00f,0.60f,0.00f), new Color(0.15f,0.55f,1.00f), new Color(0.20f,1.00f,0.30f),
            new Color(0.65f,0.30f,0.85f), new Color(0.10f,0.65f,0.20f)
        };
        /// <summary>Colour for detections that have no semantic category.</summary>
        public static readonly Color Unclassified = new Color(0.75f, 0.75f, 0.75f);
        /// <summary>Colour of a category.</summary>
        public static Color Of(SemanticCategory c) => Colours[(int)c];
    }
}
