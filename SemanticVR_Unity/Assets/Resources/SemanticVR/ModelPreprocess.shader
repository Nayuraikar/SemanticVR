// Stretch-resizes any source texture to the model input size and applies (rgb - mean) / std.
// Output is written to a float render target and converted to an NCHW tensor by TextureConverter.
Shader "Hidden/SemanticVR/ModelPreprocess"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Mean;
            float4 _Std;
            float _ToGamma;   // 1 when the project is in Linear colour space (convert back to the sRGB values the model expects)
            float _Lod;       // mip level approximating an area-average downscale

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 c = tex2Dlod(_MainTex, float4(i.uv, 0, _Lod)).rgb;
                c = (c - _Mean.rgb) / _Std.rgb;
                return float4(c, 1);
            }
            ENDCG
        }
        // Pass 1: gamma-encode only, so the later down-scale averages sRGB values exactly like the Python reference.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragGamma
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _ToGamma;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 fragGamma (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                if (_ToGamma > 0.5) c = LinearToGammaSpace(c);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
