using Unity.InferenceEngine;
using UnityEngine;

namespace SemanticVR.Perception
{
    /// <summary>
    /// Turns any frame into the model input: stretch-resize to 312x312 (the geometry used for the ONNX Runtime
    /// validation), RGB, (v - mean) / std, NCHW float tensor. All work stays on the GPU (blit + TextureConverter).
    /// </summary>
    public sealed class ModelInputPreprocessor : System.IDisposable
    {
        readonly Material material;
        readonly RenderTexture target;
        RenderTexture gammaRt;
        static readonly int MeanId = Shader.PropertyToID("_Mean"), StdId = Shader.PropertyToID("_Std");
        static readonly int GammaId = Shader.PropertyToID("_ToGamma"), LodId = Shader.PropertyToID("_Lod");

        /// <summary>False if the preprocessing shader could not be found.</summary>
        public bool IsValid => material != null;

        public ModelInputPreprocessor()
        {
            var shader = Resources.Load<Shader>("SemanticVR/ModelPreprocess");
            if (shader == null) { Debug.LogError("[SemanticVR] Preprocess shader missing (Resources/SemanticVR/ModelPreprocess)."); return; }
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetVector(MeanId, new Vector4(RfDetrModelSpec.Mean[0], RfDetrModelSpec.Mean[1], RfDetrModelSpec.Mean[2], 0));
            material.SetVector(StdId, new Vector4(RfDetrModelSpec.Std[0], RfDetrModelSpec.Std[1], RfDetrModelSpec.Std[2], 1));
            material.SetFloat(GammaId, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f : 0f);
            target = new RenderTexture(RfDetrModelSpec.InputSize, RfDetrModelSpec.InputSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { name = "ModelInput", filterMode = FilterMode.Bilinear };
            target.Create();
        }

        /// <summary>Writes the preprocessed <paramref name="source"/> into <paramref name="dst"/> ([1,3,312,312]).</summary>
        public void Process(Texture source, Tensor<float> dst)
        {
            // 1) gamma-encode at source size into a mip-mapped, non-sRGB RT so mip filtering happens on sRGB values
            if (gammaRt == null || gammaRt.width != source.width || gammaRt.height != source.height)
            {
                if (gammaRt != null) { gammaRt.Release(); Object.Destroy(gammaRt); }
                gammaRt = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "ModelInputGamma", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
                gammaRt.Create();
            }
            Graphics.Blit(source, gammaRt, material, 1);
            // 2) stretch-resize to 312x312 with trilinear LOD (approximate area filter) and normalise
            float lod = Mathf.Clamp(Mathf.Log(Mathf.Max(source.width, source.height) / (float)RfDetrModelSpec.InputSize, 2f), 0f, gammaRt.mipmapCount - 1);
            material.SetFloat(LodId, lod);
            Graphics.Blit(gammaRt, target, material, 0);
            var tf = new TextureTransform()
                .SetTensorLayout(TensorLayout.NCHW).SetCoordOrigin(CoordOrigin.TopLeft);
            TextureConverter.ToTensor(target, dst, tf);
        }

        public void Dispose()
        {
            if (material != null) Object.Destroy(material);
            if (target != null) { target.Release(); Object.Destroy(target); }
            if (gammaRt != null) { gammaRt.Release(); Object.Destroy(gammaRt); }
        }
    }
}
