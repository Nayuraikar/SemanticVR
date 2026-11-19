using UnityEngine;

namespace SemanticVR.ColourProcessing
{
    /// <summary>
    /// Optional per-pixel priority input for the corrected view. <see cref="ColourProcessor"/> calls
    /// <see cref="Prepare"/> once per frame with the camera that captured the frame, then <see cref="Bind"/> to set the
    /// shader inputs. Independent of any input device or platform (works for a desktop camera or a VR eye camera).
    /// </summary>
    public interface ISemanticAssist
    {
        /// <summary>Prepares weights for <paramref name="view"/>. Returns false to fall back to uniform correction.</summary>
        bool Prepare(Camera view);
        /// <summary>Sets textures/arrays/parameters on the colour material.</summary>
        void Bind(Material material);
    }
}
