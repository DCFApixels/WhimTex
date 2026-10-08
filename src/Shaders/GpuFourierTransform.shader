Shader "Hidden/WhimTex/GpuFourierTransform"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _Size, _Factors[16];
        int _Axis, _FactorCount, _Radix, _Previous;
        float _Sign, _Scale;
        float2 Coord(float2 pixel) { return (pixel + .5) * _Size.zw; }
        float4 Permute(v2f_img i) : SV_Target
        {
            float2 p = floor(i.uv * _Size.xy);
            int index = _Axis == 0 ? (int)p.x : (int)p.y;
            int reversed = 0;
            [loop] for (int f = 0; f < _FactorCount; f++)
            {
                int radix = (int)_Factors[f].x;
                reversed += (index % radix) * (int)_Factors[f].y;
                index /= radix;
            }
            if (_Axis == 0) p.x = reversed; else p.y = reversed;
            return tex2D(_MainTex, Coord(p));
        }
        float4 Butterfly(v2f_img i) : SV_Target
        {
            float2 p = floor(i.uv * _Size.xy);
            int index = _Axis == 0 ? (int)p.x : (int)p.y;
            int size = _Previous * _Radix;
            int k = index % size;
            int start = index - k + k % _Previous;
            float4 sum = 0;
            [loop] for (int t = 0; t < _Radix; t++)
            {
                if (_Axis == 0) p.x = start + t * _Previous; else p.y = start + t * _Previous;
                float4 z = tex2D(_MainTex, Coord(p));
                float angle = _Sign * 6.28318530718 * ((float)((t * k) % size) / size);
                float s, c; sincos(angle, s, c);
                sum += float4(z.x*c-z.y*s,z.x*s+z.y*c,z.z*c-z.w*s,z.z*s+z.w*c);
            }
            return sum * _Scale;
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Permute
            ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert_img
            #pragma fragment Butterfly
            ENDCG }
    }
}
