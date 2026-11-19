using System;
using Unity.InferenceEngine;

namespace SemanticVR.Perception
{
    /// <summary>Compute device used to run the model.</summary>
    public enum InferenceDevice { CPU = 0, GPUCompute = 1 }

    /// <summary>Raw model outputs copied into reusable managed buffers (allocated once).</summary>
    public sealed class InferenceOutputs
    {
        public readonly float[] Dets = new float[RfDetrModelSpec.Queries * 4];
        public readonly float[] Labels = new float[RfDetrModelSpec.Queries * RfDetrModelSpec.Classes];
        public readonly float[] Masks = new float[RfDetrModelSpec.Queries * RfDetrModelSpec.MaskSize * RfDetrModelSpec.MaskSize];
    }

    /// <summary>
    /// A non-blocking inference engine. The caller fills <see cref="InputTensor"/>, calls <see cref="Begin"/>, then
    /// calls <see cref="Step"/> once per frame with a time budget until it returns true.
    /// </summary>
    public interface IInferenceBackend : IDisposable
    {
        /// <summary>Human-readable device/backend description.</summary>
        string Description { get; }
        /// <summary>True when a worker exists and no fault occurred.</summary>
        bool IsReady { get; }
        /// <summary>True while an inference is in flight.</summary>
        bool IsBusy { get; }
        /// <summary>Last error message, or null.</summary>
        string LastError { get; }
        /// <summary>Preallocated [1,3,312,312] input tensor owned by the backend.</summary>
        Tensor<float> InputTensor { get; }
        /// <summary>Outputs of the last completed inference.</summary>
        InferenceOutputs Outputs { get; }
        /// <summary>Wall-clock ms of the last inference (Begin to outputs available).</summary>
        float LastWallMs { get; }
        /// <summary>Main-thread ms spent in Step() during the last inference (excluding readback copy).</summary>
        float LastMainThreadMs { get; }
        /// <summary>Longest single Step() call of the last inference, in ms.</summary>
        float LastMaxStepMs { get; }
        /// <summary>Main-thread ms spent copying outputs into managed buffers.</summary>
        float LastReadbackMs { get; }
        /// <summary>Starts an inference on <see cref="InputTensor"/>. Returns false if busy or faulted.</summary>
        bool Begin();
        /// <summary>Advances the inference using at most ~<paramref name="budgetMs"/> of main-thread time. True when outputs are ready.</summary>
        bool Step(float budgetMs);
    }
}
