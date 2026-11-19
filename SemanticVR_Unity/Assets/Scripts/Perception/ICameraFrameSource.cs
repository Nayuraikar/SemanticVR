using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Supplies the image the perception pipeline analyses. Implementations: <see cref="CameraRenderTextureSource"/>
    /// (a Game camera via RenderTexture) and <see cref="StaticTextureSource"/> (a still photo). A future stereo
    /// Cardboard source can implement this interface (e.g. one eye, or a shared frame) without touching the pipeline.
    /// </summary>
    public interface ICameraFrameSource : System.IDisposable
    {
        /// <summary>Renders/returns the current frame. The texture is sRGB-flagged and may have mip maps.</summary>
        Texture Capture();
        /// <summary>Camera whose matrices describe the last captured frame, or null for non-camera sources.</summary>
        Camera ViewCamera { get; }
        /// <summary>Pixel size of the last captured frame.</summary>
        Vector2Int FrameSize { get; }
    }
}
