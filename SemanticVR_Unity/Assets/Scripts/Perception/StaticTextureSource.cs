using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>A still image as frame source (used for the real-street-photo test and demos).</summary>
    public sealed class StaticTextureSource : ICameraFrameSource
    {
        readonly Texture texture;
        public Camera ViewCamera => null;
        public Vector2Int FrameSize => new Vector2Int(texture.width, texture.height);
        public StaticTextureSource(Texture texture) { this.texture = texture; }
        public Texture Capture() => texture;
        public void Dispose() { }   // texture is owned by the caller
    }
}
