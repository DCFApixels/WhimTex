Shader "Hidden/WhimTex/Text"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float2 _TextCanvasSize;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy * 2 / _TextCanvasSize, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y = -o.vertex.y;
                #endif
                o.uv = v.uv; return o;
            }
            float4 frag(v2f i) : SV_Target { float coverage = tex2D(_MainTex, i.uv).a; return coverage.xxxx; }
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _TextColor;
            float4 frag(v2f_img i) : SV_Target { return float4(_TextColor.rgb, _TextColor.a * tex2D(_MainTex, i.uv).a); }
            ENDCG
        }
    }
}
