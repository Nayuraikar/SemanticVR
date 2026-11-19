using System.Text;
using Unity.InferenceEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Loads and sanity-checks the RF-DETR-Seg Nano model asset. (Named RfDetrModelLoader to avoid clashing with
    /// <c>Unity.InferenceEngine.ModelLoader</c>.)
    /// </summary>
    public static class RfDetrModelLoader
    {
        /// <summary>Loads the model; returns null and an explanatory error on failure. Never throws.</summary>
        public static Model Load(ModelAsset asset, out string error)
        {
            error = null;
            if (asset == null)
            {
                error = "No ModelAsset assigned. Place rfdetr_seg_nano.onnx in Assets/Models (see README) and assign it on PerceptionPipeline.";
                return null;
            }
            try
            {
                var model = Unity.InferenceEngine.ModelLoader.Load(asset);
                var sb = new StringBuilder();
                if (model.inputs.Count != 1 || model.inputs[0].name != RfDetrModelSpec.InputName) sb.Append("unexpected inputs; ");
                var names = new System.Collections.Generic.HashSet<string>();
                foreach (var o in model.outputs) names.Add(o.name);
                foreach (var n in new[] { RfDetrModelSpec.DetsName, RfDetrModelSpec.LabelsName, RfDetrModelSpec.MasksName })
                    if (!names.Contains(n)) sb.Append("missing output '" + n + "'; ");
                if (sb.Length > 0) { error = "Model signature mismatch: " + sb; return null; }
                return model;
            }
            catch (System.Exception e) { error = "Model load failed: " + e.Message; return null; }
        }
    }
}
