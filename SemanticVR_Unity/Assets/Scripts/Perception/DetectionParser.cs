using System;
using System.Collections.Generic;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Decodes the RF-DETR outputs: boxes (normalised cx,cy,w,h), COCO-91 class logits and confidence filtering.
    /// RF-DETR is a query-based DETR model, so no NMS is applied (verified: overlapping boxes in the validation
    /// photo were distinct objects, e.g. a queue of buses). Mirrors tools/scripts/run_still.py.
    /// </summary>
    public sealed class DetectionParser
    {
        /// <summary>Minimum sigmoid confidence to keep a detection.</summary>
        public float ConfidenceThreshold = 0.3f;
        /// <summary>Upper bound on detections per frame (instance-ID texture holds at most 255).</summary>
        public int MaxDetections = 64;

        readonly Stack<Detection> pool = new Stack<Detection>();
        long nextUniqueId = 1;
        readonly List<Detection> scratch = new List<Detection>(32);

        /// <summary>Returns the result's detections to the pool.</summary>
        public void Release(PerceptionResult result)
        {
            foreach (var d in result.Detections) pool.Push(d);
            result.Detections.Clear();
        }

        /// <summary>Parses boxes/classes into <paramref name="result"/>; masks are filled later by <see cref="MaskProcessor"/>.</summary>
        public void Parse(float[] dets, float[] labels, PerceptionResult result)
        {
            Release(result);
            scratch.Clear();
            const int Q = RfDetrModelSpec.Queries, C = RfDetrModelSpec.Classes;
            for (int q = 0; q < Q; q++)
            {
                int best = -1; float bestLogit = float.NegativeInfinity;
                int row = q * C;
                for (int c = 0; c < C; c++)
                {
                    if (!CocoClasses.IsValid(c)) continue;      // skip the 10 unused COCO-91 ids
                    float l = labels[row + c];
                    if (l > bestLogit) { bestLogit = l; best = c; }
                }
                if (best < 0) continue;
                float conf = 1f / (1f + Mathf.Exp(-bestLogit));
                if (conf < ConfidenceThreshold) continue;

                float cx = dets[q * 4], cy = dets[q * 4 + 1], w = dets[q * 4 + 2], h = dets[q * 4 + 3];
                float x0 = Mathf.Clamp01(cx - w * 0.5f), y0 = Mathf.Clamp01(cy - h * 0.5f);
                float x1 = Mathf.Clamp01(cx + w * 0.5f), y1 = Mathf.Clamp01(cy + h * 0.5f);

                var d = pool.Count > 0 ? pool.Pop() : new Detection();
                d.UniqueId = 0; d.Query = q; d.CocoClassId = best; d.ClassName = CocoClasses.NameOf(best);
                d.Confidence = conf; d.Box = Rect.MinMaxRect(x0, y0, x1, y1); d.MaskCells = 0;
                scratch.Add(d);
            }
            scratch.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            int n = Math.Min(scratch.Count, Math.Min(MaxDetections, 255));
            for (int i = 0; i < scratch.Count; i++)
            {
                var d = scratch[i];
                if (i < n) { d.FrameIndex = i + 1; d.UniqueId = nextUniqueId++; result.Detections.Add(d); }
                else pool.Push(d);
            }
        }
    }
}
