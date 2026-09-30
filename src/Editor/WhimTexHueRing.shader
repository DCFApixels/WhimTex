Shader "Hidden/WhimTex/HueRing"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Size ("Size", Vector) = (220, 220, 0, 0)
        _Hue ("Hue", Float) = 0
        _Opacity ("Opacity", Float) = 1
        _Channels ("Channels", Vector) = (1,1,1,1)
        _SourceAlpha ("Source alpha", Float) = 1
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _Size;
            float _Hue, _Opacity;
            float4 _Channels;
            float _SourceAlpha;

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float Coverage(float distance)
            {
                return saturate(0.5 - distance / max(fwidth(distance), 0.0001));
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _Size.xy;
                float outer = min(_Size.x, _Size.y) * 0.5;
                float radius = length(p);
                float alpha = Coverage(max(radius - outer, outer * 0.8 - radius));
                float hue = frac(atan2(p.y, p.x) / UNITY_TWO_PI + 1.0);
                float3 rgb = saturate(abs(frac(hue + float3(0, 2.0/3.0, 1.0/3.0)) * 6 - 3) - 1);
                float count = _Channels.r + _Channels.g + _Channels.b;
                if (count < 0.5) rgb = _SourceAlpha * _Channels.a;
                else if (count < 1.5) rgb = dot(rgb, _Channels.rgb);
                else rgb *= _Channels.rgb;

                float angle = _Hue * UNITY_TWO_PI;
                float2 marker = float2(cos(angle), sin(angle)) * (outer * 0.9);
                float edge = length(p - marker) - outer * 0.08;
                float black = Coverage(abs(edge - 1.25) - 0.375) * 0.2;
                float white = Coverage(abs(edge) - 0.875);
                rgb = lerp(rgb, 0, black);
                rgb = lerp(rgb, 1, white);
                alpha = alpha + black * (1 - alpha);
                alpha = alpha + white * (1 - alpha);
                return float4(rgb, alpha * _Opacity);
            }
            ENDCG
        }
    }
}
