using System;
using System.Collections;
using System.Diagnostics;
using Unity.InferenceEngine;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// RF-DETR-Seg Nano inference on Unity Inference Engine. Execution is spread over frames with
    /// <c>Worker.ScheduleIterable</c> under a per-frame time budget, and outputs are read back asynchronously.
    /// All failures are captured in <see cref="LastError"/> instead of being thrown.
    /// </summary>
    public sealed class RfDetrInferenceBackend : IInferenceBackend
    {
        enum State { Idle, Scheduling, AwaitingReadback, Faulted }

        readonly InferenceDevice device;
        Worker worker;
        Tensor<float> input;
        IEnumerator schedule;
        Tensor<float> tDets, tLabels, tMasks;
        State state = State.Idle;
        readonly Stopwatch wall = new Stopwatch();
        float mainMs, maxStep;

        public string Description => "Unity Inference Engine / " + device;
        public bool IsReady => worker != null && state != State.Faulted;
        public bool IsBusy => state == State.Scheduling || state == State.AwaitingReadback;
        public string LastError { get; private set; }
        public Tensor<float> InputTensor => input;
        public InferenceOutputs Outputs { get; } = new InferenceOutputs();
        public float LastWallMs { get; private set; }
        public float LastMainThreadMs { get; private set; }
        public float LastMaxStepMs { get; private set; }
        public float LastReadbackMs { get; private set; }

        /// <summary>True if the device can be used on this machine (GPUCompute needs compute shaders).</summary>
        public static bool IsSupported(InferenceDevice d) => d == InferenceDevice.CPU || SystemInfo.supportsComputeShaders;

        public RfDetrInferenceBackend(Model model, InferenceDevice device)
        {
            this.device = device;
            try
            {
                if (!IsSupported(device)) throw new InvalidOperationException(device + " is not supported on this device.");
                worker = new Worker(model, device == InferenceDevice.CPU ? BackendType.CPU : BackendType.GPUCompute);
                input = new Tensor<float>(new TensorShape(1, 3, RfDetrModelSpec.InputSize, RfDetrModelSpec.InputSize));
            }
            catch (Exception e) { Fault("Worker creation failed: " + e.Message); }
        }

        public bool Begin()
        {
            if (!IsReady || IsBusy) return false;
            try
            {
                schedule = worker.ScheduleIterable(input);
                state = State.Scheduling; mainMs = 0; maxStep = 0;
                wall.Restart();
                return true;
            }
            catch (Exception e) { Fault("Schedule failed: " + e.Message); return false; }
        }

        public bool Step(float budgetMs)
        {
            if (!IsBusy) return false;
            var sw = Stopwatch.StartNew();
            try
            {
                if (state == State.Scheduling)
                {
                    while (sw.Elapsed.TotalMilliseconds < budgetMs)
                    {
                        if (!schedule.MoveNext())
                        {
                            tDets = worker.PeekOutput(RfDetrModelSpec.DetsName) as Tensor<float>;
                            tLabels = worker.PeekOutput(RfDetrModelSpec.LabelsName) as Tensor<float>;
                            tMasks = worker.PeekOutput(RfDetrModelSpec.MasksName) as Tensor<float>;
                            tDets.ReadbackRequest(); tLabels.ReadbackRequest(); tMasks.ReadbackRequest();
                            state = State.AwaitingReadback;
                            break;
                        }
                    }
                }
                bool done = false;
                if (state == State.AwaitingReadback && tDets.IsReadbackRequestDone() && tLabels.IsReadbackRequestDone() && tMasks.IsReadbackRequestDone())
                {
                    float stepMs = (float)sw.Elapsed.TotalMilliseconds; mainMs += stepMs; maxStep = Mathf.Max(maxStep, stepMs);
                    var rb = Stopwatch.StartNew();
                    CopyOut(tDets, Outputs.Dets); CopyOut(tLabels, Outputs.Labels); CopyOut(tMasks, Outputs.Masks);
                    LastReadbackMs = (float)rb.Elapsed.TotalMilliseconds;
                    wall.Stop(); LastWallMs = (float)wall.Elapsed.TotalMilliseconds;
                    LastMainThreadMs = mainMs; LastMaxStepMs = maxStep;
                    state = State.Idle; schedule = null; done = true;
                    return done;
                }
                float ms = (float)sw.Elapsed.TotalMilliseconds; mainMs += ms; maxStep = Mathf.Max(maxStep, ms);
                return false;
            }
            catch (Exception e) { Fault("Inference failed: " + e.Message); return false; }
        }

        static void CopyOut(Tensor<float> t, float[] dst)
        {
            using (var cpu = t.ReadbackAndClone())
            {
                var arr = cpu.DownloadToArray();   // one managed array per output per inference
                if (arr.Length != dst.Length) throw new InvalidOperationException($"Output size {arr.Length} != expected {dst.Length}");
                Buffer.BlockCopy(arr, 0, dst, 0, dst.Length * sizeof(float));
            }
        }

        void Fault(string msg)
        {
            LastError = msg; state = State.Faulted; schedule = null;
            UnityEngine.Debug.LogError("[SemanticVR] " + msg);
        }

        public void Dispose()
        {
            schedule = null;
            worker?.Dispose(); worker = null;
            input?.Dispose(); input = null;
            state = State.Faulted;
        }
    }
}
