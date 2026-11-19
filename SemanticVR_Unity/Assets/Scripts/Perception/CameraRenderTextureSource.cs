using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Captures a Game camera through a hidden mirror camera rendering into a mip-mapped RenderTexture. The mirror
    /// only renders when <see cref="Capture"/> is called (at the inference interval), never every frame, so the
    /// visible camera is untouched.
    /// </summary>
    public sealed class CameraRenderTextureSource : ICameraFrameSource
    {
        readonly Camera source;
        readonly Camera mirror;
        readonly RenderTexture rt;

        public Camera ViewCamera => mirror;
        public Vector2Int FrameSize => new Vector2Int(rt.width, rt.height);

        /// <param name="source">The Game camera to mirror.</param>
        /// <param name="aspectOverride">Width/height to render at; 0 uses the camera's own aspect.</param>
        /// <param name="width">Capture width in pixels; height follows the camera aspect ratio.</param>
        public CameraRenderTextureSource(Camera source, int width, float aspectOverride = 0f)
        {
            this.source = source;
            float aspect = aspectOverride > 0.1f ? aspectOverride : (source.aspect > 0.01f ? source.aspect : 16f / 9f);
            int height = Mathf.Max(16, Mathf.RoundToInt(width / aspect));
            var go = new GameObject("~SemanticVR_PerceptionCapture") { hideFlags = HideFlags.HideAndDontSave };
            mirror = go.AddComponent<Camera>();
            mirror.enabled = false;
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "PerceptionCapture", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, antiAliasing = 1
            };
            rt.Create();
        }

        public Texture Capture()
        {
            mirror.CopyFrom(source);
            mirror.aspect = (float)rt.width / rt.height;
            mirror.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            mirror.targetTexture = rt;
            mirror.enabled = false;
            var data = mirror.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            mirror.Render();
            return rt;
        }

        public void Dispose()
        {
            if (mirror != null) Object.Destroy(mirror.gameObject);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
        }
    }
}
