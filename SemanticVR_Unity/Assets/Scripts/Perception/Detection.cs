using System.Collections.Generic;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// One AI detection (raw model output, no semantic meaning). Mask logits are stored in the model's 78x78
    /// grid, in logical [y][x] order, covering the whole (stretched) source image, with everything outside the
    /// detection box set to <see cref="MaskProcessor.OutsideLogit"/>.
    /// </summary>
    public sealed class Detection
    {
        /// <summary>Monotonic id, unique over the whole run.</summary>
        public long UniqueId;
        /// <summary>1-based index within the current result (value stored in the instance-ID texture, max 255).</summary>
        public int FrameIndex;
        /// <summary>DETR query row this detection came from.</summary>
        public int Query;
        /// <summary>COCO-91 class id.</summary>
        public int CocoClassId;
        public string ClassName;
        /// <summary>Sigmoid of the winning class logit, 0..1.</summary>
        public float Confidence;
        /// <summary>Box in normalised source-image coordinates, origin top-left, y down.</summary>
        public Rect Box;
        /// <summary>78x78 mask logits (logical order, cropped to <see cref="Box"/>).</summary>
        public float[] MaskLogits = new float[RfDetrModelSpec.MaskSize * RfDetrModelSpec.MaskSize];
        /// <summary>Number of mask-grid cells above the threshold (inside the box).</summary>
        public int MaskCells;
    }

    /// <summary>Per-stage timings of the most recent perception update, in milliseconds (measured, not estimated).</summary>
    public struct PerceptionTimings
    {
        public float CaptureMs;         // mirror-camera render + mip generation (main thread)
        public float PreprocessMs;      // blit + texture->tensor (main thread submission)
        public float InferenceWallMs;   // begin -> outputs readable (wall clock, spans several frames)
        public float InferenceMainMs;   // main-thread time spent stepping the model across all frames
        public float MaxStepMs;         // longest single main-thread step (worst-case frame hitch)
        public float ReadbackMs;        // main-thread copy of outputs into managed buffers
        public float ParseMs;
        public float MaskMs;
        public float SemanticMs;
    }

    /// <summary>A published perception result: detections plus the combined instance-ID map.</summary>
    public sealed class PerceptionResult
    {
        public readonly List<Detection> Detections = new List<Detection>(32);
        /// <summary>Top-origin instance-ID buffer (0 = none, otherwise <see cref="Detection.FrameIndex"/>), size IdSize^2.</summary>
        public byte[] InstanceIds;
        public int IdSize;
        public int SourceWidth, SourceHeight;
        public PerceptionTimings Timings;
        /// <summary>Time.realtimeSinceStartup when the result was published.</summary>
        public float PublishedAt;
        public int Serial;
    }
}
