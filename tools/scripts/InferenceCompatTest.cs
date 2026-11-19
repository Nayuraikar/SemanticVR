using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.InferenceEngine;
using Debug = UnityEngine.Debug;

public static class InferenceCompatTest
{
    const string ModelPath = "Assets/Models/rfdetr_seg_nano.onnx";

    static float[] ReadBin(string p)
    {
        var b = File.ReadAllBytes(p); var f = new float[b.Length / 4];
        Buffer.BlockCopy(b, 0, f, 0, b.Length); return f;
    }

    public static void Run()
    {
        string refDir = Environment.GetEnvironmentVariable("REF_DIR");
        Debug.Log("[COMPAT] Unity " + Application.unityVersion + " device=" + SystemInfo.graphicsDeviceName + " compute=" + SystemInfo.supportsComputeShaders);
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        Debug.Log("[COMPAT] ModelAsset loaded: " + (asset != null));
        if (asset == null) { Debug.Log("[COMPAT] RESULT import=FAIL"); return; }

        Model model;
        try { model = ModelLoader.Load(asset); }
        catch (Exception e) { Debug.Log("[COMPAT] ModelLoader.Load FAILED: " + e); return; }
        foreach (var i in model.inputs) Debug.Log($"[COMPAT] input {i.name} {i.shape} {i.dataType}");
        foreach (var o in model.outputs) Debug.Log($"[COMPAT] output {o.name}");
        Debug.Log("[COMPAT] layers=" + model.layers.Count);
        var counts = new System.Collections.Generic.SortedDictionary<string, int>();
        foreach (var l in model.layers) { var n = l.GetType().Name; counts[n] = counts.TryGetValue(n, out var c) ? c + 1 : 1; }
        foreach (var kv in counts) Debug.Log($"[COMPAT] layerType {kv.Key} x{kv.Value}");

        var input = ReadBin(Path.Combine(refDir, "input.bin"));
        var refs = new[] { "dets", "labels", "masks" };
        foreach (var backend in new[] { BackendType.CPU, BackendType.GPUCompute })
        {
            Debug.Log("[COMPAT] ===== backend " + backend);
            try
            {
                long mem0 = GC.GetTotalMemory(true);
                var sw = Stopwatch.StartNew();
                using var worker = new Worker(model, backend);
                Debug.Log($"[COMPAT] {backend} worker created in {sw.ElapsedMilliseconds} ms");
                using var t = new Tensor<float>(new TensorShape(1, 3, 312, 312), input);
                sw.Restart();
                worker.Schedule(t);
                var o0 = worker.PeekOutput("dets") as Tensor<float>;
                var cl = o0.ReadbackAndClone();
                Debug.Log($"[COMPAT] {backend} first run (schedule+readback) {sw.ElapsedMilliseconds} ms");
                cl.Dispose();
                var times = new double[10];
                for (int r = 0; r < 10; r++)
                {
                    sw.Restart(); worker.Schedule(t);
                    foreach (var n in refs) { using var x = (worker.PeekOutput(n) as Tensor<float>).ReadbackAndClone(); }
                    times[r] = sw.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                Debug.Log($"[COMPAT] {backend} steady median {times[5]:F1} ms min {times[0]:F1} max {times[9]:F1} (all 3 outputs read back)");
                foreach (var n in refs)
                {
                    using var x = (worker.PeekOutput(n) as Tensor<float>).ReadbackAndClone();
                    var u = x.DownloadToArray(); { var ob = new byte[u.Length*4]; Buffer.BlockCopy(u,0,ob,0,ob.Length); File.WriteAllBytes(Path.Combine(refDir, "unity_"+backend+"_"+n+".bin"), ob);} var rf = ReadBin(Path.Combine(refDir, n + ".bin"));
                    double maxAbs = 0, sum = 0; int nan = 0;
                    for (int i = 0; i < rf.Length; i++) { if (float.IsNaN(u[i])) { nan++; continue; } var d = Math.Abs(u[i] - rf[i]); maxAbs = Math.Max(maxAbs, d); sum += d; }
                    Debug.Log($"[COMPAT] {backend} {n} shape={x.shape} len={u.Length}/{rf.Length} maxAbsDiff={maxAbs:E3} meanAbsDiff={sum / rf.Length:E3} NaN={nan}");
                }
                Debug.Log($"[COMPAT] {backend} managed heap delta {(GC.GetTotalMemory(false) - mem0) / 1048576.0:F1} MB");
                Debug.Log("[COMPAT] RESULT " + backend + " = OK");
            }
            catch (Exception e) { Debug.Log("[COMPAT] RESULT " + backend + " = FAIL: " + e); }
        }
    }
}

