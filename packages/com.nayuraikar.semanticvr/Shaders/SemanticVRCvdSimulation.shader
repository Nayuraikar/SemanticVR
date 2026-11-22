// Fullscreen CVD simulation for the debug view. Operates on LINEAR colour (the camera colour target is
// sRGB / linear-float in a Linear project, so sampled values are already linear).
Shader "Hidden/SemanticVR/CvdSimulation"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "CvdSimulation"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.nayuraikar.semanticvr/Shaders/SemanticVRColor.hlsl"

            float _SVR_Mode;      // 0 original, 1 simulated, 2 split (left original / right simulated)
            float _SVR_CvdType;   // 0 protan, 1 deutan, 2 tritan
            float _SVR_Severity;  // 0..1

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                int mode = (int)round(_SVR_Mode);
                bool simulate = (mode == 1) || (mode == 2 && uv.x >= 0.5);
                if (simulate)
                    c.rgb = saturate(SVR_SimulateCvd(c.rgb, (int)round(_SVR_CvdType), _SVR_Severity));

                if (mode == 2)
                {
                    float halfLine = 1.5 * _BlitTexture_TexelSize.x;       // ~3 px divider
                    float divider = step(abs(uv.x - 0.5), halfLine);
                    c.rgb = lerp(c.rgb, float3(1, 1, 1), divider * 0.9);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
