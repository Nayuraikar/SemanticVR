namespace SemanticVR.Perception
{
    /// <summary>
    /// Memory layout of the flat <c>masks</c> buffer as returned by Unity Inference Engine.
    /// </summary>
    public enum MaskLayout
    {
        /// <summary>Logical [query][y][x] order, identical to ONNX Runtime.</summary>
        Identity = 0,
        /// <summary>
        /// Observed in com.unity.ai.inference 2.3.0, 2.4.1, 2.5.0 and 2.6.1 (CPU and GPUCompute): the decomposed
        /// Einsum leaves the buffer in [x][query][y] order, i.e. flat index = (x * Q + q) * H + y.
        /// </summary>
        UnityEinsumTransposed = 1
    }

    /// <summary>Index helpers for <see cref="MaskLayout"/>.</summary>
    public static class MaskLayoutUtil
    {
        /// <summary>Flat index of logical element (query q, row y, column x) in the buffer.</summary>
        public static int Index(MaskLayout layout, int q, int y, int x)
        {
            const int Q = RfDetrModelSpec.Queries, H = RfDetrModelSpec.MaskSize, W = RfDetrModelSpec.MaskSize;
            return layout == MaskLayout.Identity ? (q * H + y) * W + x : (x * Q + q) * H + y;
        }

        /// <summary>Copies one query's mask logits into a logical [y][x] array.</summary>
        public static void ExtractQuery(float[] buffer, MaskLayout layout, int q, float[] dst)
        {
            const int H = RfDetrModelSpec.MaskSize, W = RfDetrModelSpec.MaskSize;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    dst[y * W + x] = buffer[Index(layout, q, y, x)];
        }
    }
}
