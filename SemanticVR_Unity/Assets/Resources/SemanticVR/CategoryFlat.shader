// Flat unlit shader used to render the manual SemanticTag category ID buffer: R = (category + 1) / 255.
Shader "Hidden/SemanticVR/CategoryFlat"
{
    Properties { _Id ("Id", Float) = 0  _ZTestMode ("ZTest", Float) = 4 }
    SubShader
    {
        Cull Off ZWrite On ZTest [_ZTestMode]   // set from C#: LessEqual, or GreaterEqual on reversed-Z platforms
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Id;
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag () : SV_Target { return float4(_Id / 255.0, 0, 0, 1); }
            ENDCG
        }
    }
}
