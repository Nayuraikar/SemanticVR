// Simple three-stop vertical gradient skybox (bottom -> horizon -> top). Colours are sRGB, converted to linear by Unity.
Shader "SemanticVR/GradientSkybox"
{
    Properties
    {
        _Top ("Top", Color) = (0.42, 0.55, 0.71, 1)
        _Horizon ("Horizon", Color) = (0.91, 0.89, 0.84, 1)
        _Bottom ("Bottom", Color) = (0.71, 0.69, 0.65, 1)
        _Exponent ("Top falloff", Range(0.2, 4)) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Top;
                half4 _Horizon;
                half4 _Bottom;
                float _Exponent;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float y = normalize(i.dir).y;
                half3 up = lerp(_Horizon.rgb, _Top.rgb, pow(saturate(y), _Exponent));
                half3 down = lerp(_Horizon.rgb, _Bottom.rgb, pow(saturate(-y), 0.6));
                return half4(y >= 0 ? up : down, 1);
            }
            ENDHLSL
        }
    }
}
