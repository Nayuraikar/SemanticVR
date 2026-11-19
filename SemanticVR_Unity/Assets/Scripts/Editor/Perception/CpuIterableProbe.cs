#if UNITY_EDITOR
using System.Diagnostics;
using SemanticVR.Perception;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SemanticVR.EditorTools
{
    /// <summary>Diagnostic: steps ScheduleIterable on the CPU backend in edit mode with a hard time cap; logs step timing.</summary>
    public static class CpuIterableProbe
    {
        public static void RunBackend()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/rfdetr_seg_nano.onnx");
            var model = ModelLoader.Load(asset);
            var golden = GoldenReference.Load(out _);
            var be = new RfDetrInferenceBackend(model, InferenceDevice.CPU);
            be.InputTensor.Upload(golden.Input);
            for (int pass = 0; pass < 2; pass++)
            {
                be.Begin();
                var sw = Stopwatch.StartNew(); int calls = 0; bool done = false;
                while (sw.Elapsed.TotalSeconds < 90)
                {
                    calls++;
                    if (be.Step(4f)) { done = true; break; }
                    System.Threading.Thread.Sleep(1);
                    if (calls % 500 == 0) Debug.Log($"[CPUPROBE] backend pass {pass}: still waiting, calls={calls}, {sw.Elapsed.TotalSeconds:F1}s, busy={be.IsBusy}, ready={be.IsReady}, err={be.LastError}");
                }
                Debug.Log($"[CPUPROBE] backend pass {pass}: done={done} calls={calls} wall={sw.Elapsed.TotalMilliseconds:F0} ms main={be.LastMainThreadMs:F1} maxStep={be.LastMaxStepMs:F1}");
                if (!done) break;
                var rep = MaskLayoutValidator.Validate(golden, be.Outputs.Dets, be.Outputs.Labels, be.Outputs.Masks, MaskLayout.UnityEinsumTransposed);
                Debug.Log("[CPUPROBE] backend validation: passed=" + rep.Passed + " " + rep.Message);
            }
            be.Dispose();
        }

        public static void Run()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/rfdetr_seg_nano.onnx");
            var model = ModelLoader.Load(asset);
            var golden = GoldenReference.Load(out _);
            using var worker = new Worker(model, BackendType.CPU);
            using var input = new Tensor<float>(new TensorShape(1, 3, 312, 312), golden.Input);
            for (int pass = 0; pass < 2; pass++)
            {
                var it = worker.ScheduleIterable(input);
                var total = Stopwatch.StartNew(); int n = 0; double worst = 0; bool done = false;
                while (total.Elapsed.TotalSeconds < 120)
                {
                    var s = Stopwatch.StartNew();
                    bool more = it.MoveNext();
                    double ms = s.Elapsed.TotalMilliseconds; worst = System.Math.Max(worst, ms); n++;
                    if (ms > 500) Debug.Log($"[CPUPROBE] pass {pass} slow step #{n}: {ms:F0} ms");
                    if (!more) { done = true; break; }
                }
                Debug.Log($"[CPUPROBE] pass {pass}: steps={n} done={done} total={total.Elapsed.TotalMilliseconds:F0} ms worstStep={worst:F1} ms");
                if (!done) break;
                using var o = (worker.PeekOutput("dets") as Tensor<float>).ReadbackAndClone();
                Debug.Log($"[CPUPROBE] pass {pass}: readback ok after {total.Elapsed.TotalMilliseconds:F0} ms");
            }
        }
    }
}
#endif
