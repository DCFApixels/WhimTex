Shader "Hidden/TextureCompositor/Gradient"
{
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

            sampler2D _GradientPalette;
            float4 _GradientPalette_TexelSize;
            float4 _GradientIntervals[17], _GradientShape, _GradientStart;
            float2 _GradientOutputSize;
            int _GradientType, _GradientPingPong, _GradientIntervalCount;

            float Coordinate(float2 uv)
            {
                // Canonical pixel centers avoid interpolator error selecting the other
                // side of the angular seam, especially on odd-sized canvases.
                float2 pixel = floor(uv * _GradientOutputSize) + .5;
                uv = pixel / _GradientOutputSize;
                if (_GradientType == 0) return uv.y;
                if (_GradientType == 1) return uv.x;
                float2 p = (pixel - _GradientShape.xy * _GradientOutputSize) / _GradientOutputSize;
                if (_GradientType == 3)
                {
                    // Match Mathf.Atan2(0, 0) at the exact center.
                    float angle = all(p == 0) ? 0 : atan2(p.y, p.x);
                    float t = (angle + UNITY_PI) / (2 * UNITY_PI) * _GradientShape.w;
                    return _GradientPingPong != 0 ? 1 - abs(frac(t * .5) * 2 - 1) : frac(t);
                }
                float distance = _GradientType == 2 ? length(p) :
                    _GradientType == 4 ? abs(p.x) + abs(p.y) : max(abs(p.x), abs(p.y));
                return _GradientShape.z <= 0 ? 0 : saturate(distance / _GradientShape.z);
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float t = saturate(Coordinate(input.uv));
                int row = 0;
                // Fixed gradients select the right key within an interval, retaining the
                // preceding interval at its exact endpoint (Unity Gradient semantics).
                [loop] for (int i = 0; i < _GradientIntervalCount - 1; i++)
                    if (t > _GradientIntervals[i].y) row = i + 1;
                float4 interval = _GradientIntervals[row];
                float local = saturate((t - interval.x) * interval.z);
                float2 uv = float2(lerp(.5 * _GradientPalette_TexelSize.x,
                    1 - .5 * _GradientPalette_TexelSize.x, local), (row + .5) * _GradientPalette_TexelSize.y);
                float4 color = t <= 0 ? _GradientStart : tex2Dlod(_GradientPalette, float4(uv, 0, 0));
                color.rgb = SpriteDecode(color.rgb);
                return color;
            }
            ENDCG
        }
    }
}
