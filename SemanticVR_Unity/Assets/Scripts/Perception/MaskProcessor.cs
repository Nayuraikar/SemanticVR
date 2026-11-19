using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Per-instance mask reconstruction. Applies the validated <see cref="MaskLayout"/>, crops each query's
    /// 78x78 logit grid to its box and builds a combined instance-ID map for later colour processing (PAGP).
    /// </summary>
    public sealed class MaskProcessor
    {
        /// <summary>Logit written outside a detection's box.</summary>
        public const float OutsideLogit = -100f;
        /// <summary>Mask probability threshold (0.5 == logit 0, as used in the ONNX Runtime validation).</summary>
        public float MaskProbabilityThreshold = 0.5f;
        /// <summary>Edge of the combined instance-ID map in pixels.</summary>
        public int IdSize = 156;

        public MaskLayout Layout = MaskLayout.UnityEinsumTransposed;

        Texture2D idTexture;
        byte[] flipped;

        float LogitThreshold => Mathf.Log(MaskProbabilityThreshold / (1f - MaskProbabilityThreshold));

        /// <summary>Instance-ID texture (R8, UV-correct: row 0 is the bottom of the image). Owned by this processor.</summary>
        public Texture2D InstanceIdTexture => idTexture;

        /// <summary>Fills <see cref="Detection.MaskLogits"/> for every detection from the raw masks buffer.</summary>
        public void BuildMasks(PerceptionResult result, float[] masksBuffer)
        {
            const int M = RfDetrModelSpec.MaskSize;
            float thr = LogitThreshold;
            foreach (var d in result.Detections)
            {
                var box = d.Box; int cells = 0;
                for (int y = 0; y < M; y++)
                {
                    float v = (y + 0.5f) / M;
                    bool rowIn = v >= box.yMin && v <= box.yMax;
                    for (int x = 0; x < M; x++)
                    {
                        float u = (x + 0.5f) / M;
                        float logit = OutsideLogit;
                        if (rowIn && u >= box.xMin && u <= box.xMax)
                        {
                            logit = masksBuffer[MaskLayoutUtil.Index(Layout, d.Query, y, x)];
                            if (logit > thr) cells++;
                        }
                        d.MaskLogits[y * M + x] = logit;
                    }
                }
                d.MaskCells = cells;
            }
        }

        /// <summary>Rasterises all masks into the instance-ID buffer/texture. Lower confidence is painted first.</summary>
        public void BuildInstanceMap(PerceptionResult result)
        {
            int S = IdSize;
            if (result.InstanceIds == null || result.InstanceIds.Length != S * S) result.InstanceIds = new byte[S * S];
            else System.Array.Clear(result.InstanceIds, 0, result.InstanceIds.Length);
            result.IdSize = S;
            float thr = LogitThreshold;
            var ids = result.InstanceIds;
            for (int i = result.Detections.Count - 1; i >= 0; i--)   // detections are sorted by descending confidence
            {
                var d = result.Detections[i];
                int px0 = Mathf.Max(0, Mathf.FloorToInt(d.Box.xMin * S)), px1 = Mathf.Min(S - 1, Mathf.CeilToInt(d.Box.xMax * S));
                int py0 = Mathf.Max(0, Mathf.FloorToInt(d.Box.yMin * S)), py1 = Mathf.Min(S - 1, Mathf.CeilToInt(d.Box.yMax * S));
                for (int py = py0; py <= py1; py++)
                    for (int px = px0; px <= px1; px++)
                        if (SampleLogit(d.MaskLogits, (px + 0.5f) / S, (py + 0.5f) / S) > thr)
                            ids[py * S + px] = (byte)d.FrameIndex;
            }
            UploadTexture(ids, S);
        }

        void UploadTexture(byte[] ids, int S)
        {
            if (idTexture == null || idTexture.width != S)
            {
                if (idTexture != null) Object.Destroy(idTexture);
                idTexture = new Texture2D(S, S, TextureFormat.R8, false, true) { name = "InstanceIdMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                flipped = new byte[S * S];
            }
            for (int y = 0; y < S; y++) System.Buffer.BlockCopy(ids, (S - 1 - y) * S, flipped, y * S, S);
            idTexture.SetPixelData(flipped, 0);
            idTexture.Apply(false);
        }

        /// <summary>Bilinear sample of a 78x78 logit grid at normalised (u,v), origin top-left.</summary>
        public static float SampleLogit(float[] grid, float u, float v)
        {
            const int M = RfDetrModelSpec.MaskSize;
            float fx = u * M - 0.5f, fy = v * M - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, M - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, M - 1);
            int x1 = Mathf.Min(x0 + 1, M - 1), y1 = Mathf.Min(y0 + 1, M - 1);
            float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
            float a = Mathf.Lerp(grid[y0 * M + x0], grid[y0 * M + x1], tx);
            float b = Mathf.Lerp(grid[y1 * M + x0], grid[y1 * M + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        /// <summary>Releases the owned texture.</summary>
        public void Dispose() { if (idTexture != null) Object.Destroy(idTexture); idTexture = null; }
    }
}
