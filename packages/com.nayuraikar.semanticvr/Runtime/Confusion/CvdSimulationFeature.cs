using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace SemanticVR.Confusion
{
    /// <summary>
    /// Fullscreen colour-vision-deficiency simulation (Machado 2009) for the debug view, run in LINEAR space
    /// using Shaders/SemanticVRColor.hlsl. Render Graph only (RecordRenderGraph + AddBlitPass).
    /// Reads <see cref="CvdDebugState"/>; in Original mode no pass is enqueued (zero cost).
    /// </summary>
    public class CvdSimulationFeature : ScriptableRendererFeature
    {
        [Tooltip("Hidden/SemanticVR/CvdSimulation (assigned automatically).")]
        public Shader shader;

        Material _material;
        CvdSimulationPass _pass;

        public override void Create()
        {
            if (shader == null) shader = Shader.Find("Hidden/SemanticVR/CvdSimulation");
            if (shader != null) _material = CoreUtils.CreateEngineMaterial(shader);
            _pass = new CvdSimulationPass(_material)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
                requiresIntermediateTexture = true, // we read the camera colour, so it must not be the backbuffer
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || CvdDebugState.Mode == DebugViewMode.Original) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        sealed class CvdSimulationPass : ScriptableRenderPass
        {
            static readonly int ModeId = Shader.PropertyToID("_SVR_Mode");
            static readonly int TypeId = Shader.PropertyToID("_SVR_CvdType");
            static readonly int SeverityId = Shader.PropertyToID("_SVR_Severity");
            readonly Material _material;

            public CvdSimulationPass(Material material)
            {
                _material = material;
                profilingSampler = new ProfilingSampler("SemanticVR CVD Simulation");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return; // nothing to read from

                _material.SetFloat(ModeId, (float)CvdDebugState.Mode);
                _material.SetFloat(TypeId, (float)(int)CvdDebugState.Type);
                _material.SetFloat(SeverityId, CvdDebugState.Severity);

                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "SemanticVR_CvdColor";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0),
                    "SemanticVR CVD Simulation");
                resources.cameraColor = destination;
            }
        }
    }
}
