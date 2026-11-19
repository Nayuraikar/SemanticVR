using System;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Golden reference produced by ONNX Runtime (tools/scripts/make_golden.py) for a fixed 312x312 input.
    /// Loaded from Resources/Golden/*.bytes. Used by <see cref="MaskLayoutValidator"/>.
    /// </summary>
    public sealed class GoldenReference
    {
        /// <summary>Normalised NCHW float input tensor data (1*3*312*312).</summary>
        public float[] Input;
        public float[] Dets;
        public float[] Labels;
        /// <summary>Query indices whose full masks are stored.</summary>
        public int[] TopQueries;
        /// <summary>Logical [topIndex][y][x] mask logits from ONNX Runtime.</summary>
        public float[] TopMasks;

        /// <summary>Loads the golden files; returns null and sets <paramref name="error"/> if any is missing.</summary>
        public static GoldenReference Load(out string error)
        {
            error = null;
            var rgb = Resources.Load<TextAsset>("Golden/golden_input_rgb");
            var dets = Resources.Load<TextAsset>("Golden/golden_dets");
            var labels = Resources.Load<TextAsset>("Golden/golden_labels");
            var masks = Resources.Load<TextAsset>("Golden/golden_masks_top");
            if (rgb == null || dets == null || labels == null || masks == null)
            {
                error = "Golden reference files missing in Resources/Golden (expected golden_input_rgb, golden_dets, golden_labels, golden_masks_top).";
                return null;
            }
            const int S = RfDetrModelSpec.InputSize;
            var g = new GoldenReference();
            var u8 = rgb.bytes;
            if (u8.Length != S * S * 3) { error = "golden_input_rgb has wrong size"; return null; }
            g.Input = new float[3 * S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        // Same float32 operation order as make_golden.py: (v/255 - mean)/std
                        float v = u8[(y * S + x) * 3 + c] / 255f;
                        g.Input[(c * S + y) * S + x] = (v - RfDetrModelSpec.Mean[c]) / RfDetrModelSpec.Std[c];
                    }
            g.Dets = ToFloats(dets.bytes);
            g.Labels = ToFloats(labels.bytes);
            var mb = masks.bytes;
            int n = BitConverter.ToInt32(mb, 0);
            g.TopQueries = new int[n];
            Buffer.BlockCopy(mb, 4, g.TopQueries, 0, n * 4);
            int off = 4 + n * 4;
            g.TopMasks = new float[(mb.Length - off) / 4];
            Buffer.BlockCopy(mb, off, g.TopMasks, 0, mb.Length - off);
            if (g.TopMasks.Length != n * RfDetrModelSpec.MaskSize * RfDetrModelSpec.MaskSize) { error = "golden_masks_top has wrong size"; return null; }
            return g;
        }

        static float[] ToFloats(byte[] b) { var f = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, f, 0, b.Length); return f; }
    }
}
