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
            float2 _TextBoldOffset;
            float4 _TextClipRect;
            int _TextPixel, _TextClip;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float2 pixel : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = float4(v.vertex.xy * 2 / _TextCanvasSize, 0, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.vertex.y = -o.vertex.y;
                #endif
                o.uv = v.uv; o.pixel = v.vertex.xy; return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                if (_TextClip) clip(min(min(i.pixel.x - _TextClipRect.x, _TextClipRect.z - i.pixel.x), min(i.pixel.y - _TextClipRect.y, _TextClipRect.w - i.pixel.y)));
                float coverage = tex2D(_MainTex, i.uv).a;
                if (_TextBoldOffset.x > 0)
                {
                    coverage = max(coverage, tex2D(_MainTex, i.uv + float2(_TextBoldOffset.x, 0)).a);
                    coverage = max(coverage, tex2D(_MainTex, i.uv - float2(_TextBoldOffset.x, 0)).a);
                    coverage = max(coverage, tex2D(_MainTex, i.uv + float2(0, _TextBoldOffset.y)).a);
                    coverage = max(coverage, tex2D(_MainTex, i.uv - float2(0, _TextBoldOffset.y)).a);
                }
                if (_TextPixel) coverage = step(.5, coverage);
                return coverage.xxxx;
            }
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
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment TextPaintWhole
            #include "TextPaint.cginc"
            ENDCG
        }
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex TextPaintVert
            #pragma fragment TextPaintGlyph
            #include "TextPaint.cginc"
            ENDCG
        }
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _TextCoverage;
            int _TextOpaqueGradient;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                return c.a > 1e-12 ? float4(c.rgb / c.a, _TextOpaqueGradient ? tex2D(_TextCoverage, i.uv).a : c.a) : 0;
            }
            ENDCG
        }
    }
}
