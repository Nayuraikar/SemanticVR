// Applies out = Contrast(M * rgb) on LINEAR RGB. Used for the original / simulated / corrected previews.
// Plain texture-in/texture-out, so the same material works with Graphics.Blit and as a URP full-screen pass.
Shader "Hidden/SemanticVR/ColourTransform"
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
            float4 _M0, _M1, _M2;     // matrix rows (linear RGB)
            float _Contrast;          // about linear mid-grey
            float _GammaProject;      // 1 when the project uses Gamma colour space (inputs are sRGB-encoded)
            float4 _Env;              // reserved hooks: x = night, y = shadow, z = fog density (unused by the baseline)
            float4 _B0, _B1, _B2;     // base matrix rows (identity by default; the CVD simulation for 'as perceived' corrected views)

            // Priority-aware assist (off unless _UseSemantic > 0.5). Per pixel strength s = bg + (1 - bg) * w, where w is the
            // user's importance (0..1) of the semantic category (live SemanticTag buffer) or of the AI-detected instance.
            sampler2D _CatTex, _IdTex;
            float _UseSemantic, _BgAssist, _AiFade;
            float _CatW[16];          // by (category + 1); -1 = no information
            float _IdW[128];          // by detection frame index; -1 = no information

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

            float4 frag (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                if (_GammaProject > 0.5) c = GammaToLinearSpace(c);
                float3 target = float3(dot(_M0.xyz, c), dot(_M1.xyz, c), dot(_M2.xyz, c));
                float contrast = _Contrast;
                if (_UseSemantic > 0.5)
                {
                    int ci = (int)(tex2D(_CatTex, i.uv).r * 255.0 + 0.5);
                    float w = _CatW[min(ci, 15)];
                    int ii = (int)(tex2D(_IdTex, i.uv).r * 255.0 + 0.5);
                    if (ii > 0) w = max(w, _IdW[min(ii, 127)] * _AiFade);
                    float s = w < 0.0 ? _BgAssist : lerp(_BgAssist, 1.0, w);
                    float3 baseC = float3(dot(_B0.xyz, c), dot(_B1.xyz, c), dot(_B2.xyz, c));
                    target = lerp(baseC, target, s);
                    contrast = lerp(1.0, _Contrast, s);
                }
                c = target;
                const float pivot = 0.214;                      // sRGB 0.5 in linear light
                c = pivot + (c - pivot) * contrast;
                c = saturate(c);
                if (_GammaProject > 0.5) c = LinearToGammaSpace(c);
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
