Shader "Hidden/TextureCompositor/SdfGradient"
{
    Properties
    {
        _MainTex ("Distance Field", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "HdrColor.cginc"
            sampler2D _MainTex, _GradientLut, _ProfileLut;
            float4 _GradientLut_TexelSize;
            float _MaxDistance, _InsideDistance, _OutsideDistance, _ContourOffset;
            int _Position, _Inverted, _GradientWrapMode;
            float4 frag(v2f_img input) : SV_Target
            {
                float d = tex2Dlod(_MainTex, float4(input.uv, 0, 0)).r - _ContourOffset;
                if (_Position == 0) d = max(d, 0);
                else if (_Position == 1) d = max(-d, 0);
                else if (_Position == 2) d = abs(d);
                float t = saturate(_Position == 3 ? .5 + .5 * d / max(d < 0 ? _InsideDistance : _OutsideDistance, .0001) : d / _MaxDistance);
                if (_Inverted != 0) t = 1 - t;
                t = saturate(tex2Dlod(_ProfileLut, float4(lerp(.5 / 512, 1 - .5 / 512, t), .5, 0, 0)).r);
                if (_GradientWrapMode == 1) t = frac(t);
                else if (_GradientWrapMode == 2) t = 1 - abs(frac(t * .5) * 2 - 1);
                float u = lerp(.5 * _GradientLut_TexelSize.x, 1 - .5 * _GradientLut_TexelSize.x, t);
                float4 c = tex2Dlod(_GradientLut, float4(u, .5, 0, 0));
                c.rgb = SpriteDecode(c.rgb);
                return c;
            }
            ENDCG
        }
    }
}
