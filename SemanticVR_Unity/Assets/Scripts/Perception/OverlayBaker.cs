using System.Collections.Generic;
using SemanticVR.Semantics;
using SemanticVR.UI;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>Bakes the frame plus coloured masks and boxes into a CPU texture (used by the automated tests/reports).</summary>
    public static class OverlayBaker
    {
        /// <summary>Copies the frame into a CPU RGBA32 texture at the given width (caller destroys it).</summary>
        public static Texture2D Snapshot(Texture frame, int width)
        {
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)frame.height / frame.width));
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(frame, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>Returns a new RGBA32 texture (caller destroys it). Rows are bottom-up like any Unity texture.</summary>
        public static Texture2D Bake(Texture frame, PerceptionResult res, IReadOnlyList<DetectedObject> objs, int width = 640)
        {
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)frame.height / frame.width));
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(frame, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels32();
            var colours = new Color32[256];
            foreach (var o in objs)
                colours[o.Detection.FrameIndex] = o.Source == CategorySource.None ? CategoryPalette.Unclassified : CategoryPalette.Of(o.FinalCategory);
            int S = res.IdSize;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // texture row y is counted from the bottom; instance buffer rows from the top
                    int id = res.InstanceIds[(int)((height - 1 - y + 0.5f) / height * S) * S + (int)((x + 0.5f) / width * S)];
                    if (id == 0) continue;
                    var c = colours[id]; var p = px[y * width + x];
                    px[y * width + x] = new Color32((byte)((p.r + c.r) / 2), (byte)((p.g + c.g) / 2), (byte)((p.b + c.b) / 2), 255);
                }
            foreach (var o in objs)
            {
                var c = (Color32)(o.Source == CategorySource.None ? CategoryPalette.Unclassified : CategoryPalette.Of(o.FinalCategory));
                var b = o.Detection.Box;
                int x0 = (int)(b.xMin * (width - 1)), x1 = (int)(b.xMax * (width - 1));
                int yTop = (int)((1 - b.yMin) * (height - 1)), yBot = (int)((1 - b.yMax) * (height - 1));
                for (int x = x0; x <= x1; x++) { px[yTop * width + x] = c; px[yBot * width + x] = c; }
                for (int y = yBot; y <= yTop; y++) { px[y * width + x0] = c; px[y * width + x1] = c; }
            }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }
    }
}
