using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>Marks a renderer (and all its descendants) as intentionally non-semantic (ground, trim, wheels...).
    /// SceneValidator does not warn about untagged renderers under this marker.</summary>
    [DisallowMultipleComponent, AddComponentMenu("SemanticVR/Semantic Ignore")]
    public class SemanticIgnore : MonoBehaviour { }
}
