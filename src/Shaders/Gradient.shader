Shader "Hidden/WhimTex/Gradient"
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
            #include "GradientPalette.cginc"
            #include "ProceduralUv.cginc"

            float4 _GradientShape;
            float2 _GradientOutputSize;
            float _GradientStartTurn;
            int _GradientType, _GradientPingPong;
            #include "FreeformGradient.cginc"

            float Coordinate(float2 uv)
            {
                // Canonical pixel centers avoid interpolator error selecting the other
                // side of the angular seam, especially on odd-sized canvases.
                float2 pixel = _UnboundedUv != 0 ? uv * _GradientOutputSize : floor(uv * _GradientOutputSize) + .5;
                uv = pixel / _GradientOutputSize;
                if (_GradientType == 1) return uv.x;
                float2 p = (pixel - _GradientShape.xy * _GradientOutputSize) / _GradientOutputSize;
                if (_GradientType == 3)
                {
                    // Match Mathf.Atan2(0, 0) at the exact center.
                    float angle = all(p == 0) ? 0 : atan2(p.y, p.x);
                    float turn = (angle + UNITY_PI) / (2 * UNITY_PI) - _GradientStartTurn;
                    if (turn < 0 || turn > 1) turn = frac(turn);
                    float t = turn * _GradientShape.w;
                    return _GradientPingPong != 0 ? 1 - abs(frac(t * .5) * 2 - 1) : frac(t);
                }
                float distance = _GradientType == 2 ? length(p) : max(abs(p.x), abs(p.y));
                return _GradientShape.z <= 0 ? 0 : distance / _GradientShape.z;
            }

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = ProceduralSourceUv(input.uv);
                if (_GradientType == 6) return FreeformColor(uv);
                return SampleGradientPalette(Coordinate(uv));
            }
            ENDCG
        }
    }
}
