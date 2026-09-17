// @whimtex-effect SDF/Bevel Emboss
// @param texture2D _Surface = self // Surface color before this FX. The relief comes from this SDF layer's raw distances.
// @param float _Width = 12 [0.1 .. ~128] // Bevel width in document pixels before the SDF layer transform.
// @param float _Depth = 6 [~-64 .. ~64] // Positive raises the surface; negative engraves it.
// @param curve _Profile // Height profile from the edge (0) toward the interior (1), before Smoothness.
// @param float _Smoothness = 1 [0 .. 1] // Blend a straight bevel into a smoothly rounded profile.
// @param float _Smoothing = 2 [0 .. ~8] // Normal sampling radius in document pixels; reduces contour stair steps.
// @param normal _LightDirection = (-0.5, 0.5, 1)
// @param color _Highlight = (1, 1, 1, 1)
// @param color _Shadow = (0.05, 0.05, 0.05, 1)
// @param float _Intensity = 1 [0 .. ~4]

float BevelHeight(float2 uv)
{
    float t = saturate(_Profile_Sample(saturate(-SampleLayerSDF(uv) / max(_Width, 0.001))));
    return lerp(t, t * t * (3.0 - 2.0 * t), saturate(_Smoothness)) * _Depth;
}

float4 ApplyFX(float2 uv, float4 color)
{
    if (!HasLayerSDF()) return color;
    float radius = max(_PreviewScale, _Smoothing);
    float2 stepUV = radius * _CanvasSize.zw;
    float tl = BevelHeight(uv + stepUV * float2(-1, 1));
    float tr = BevelHeight(uv + stepUV * float2(1, 1));
    float bl = BevelHeight(uv + stepUV * float2(-1, -1));
    float br = BevelHeight(uv + stepUV * float2(1, -1));
    float dx = (3.0 * (tr + br - tl - bl) + 10.0 * (BevelHeight(uv + float2(stepUV.x, 0)) - BevelHeight(uv - float2(stepUV.x, 0)))) / (32.0 * radius);
    float dy = (3.0 * (tl + tr - bl - br) + 10.0 * (BevelHeight(uv + float2(0, stepUV.y)) - BevelHeight(uv - float2(0, stepUV.y)))) / (32.0 * radius);
    float3 n = normalize(float3(-dx, -dy, 1.0));
    float delta = (dot(n, _LightDirection) - _LightDirection.z) * max(_Intensity, 0.0);
    float3 surface = tex2D(_Surface, uv).rgb;
    float3 shaded = lerp(surface, _Shadow.rgb, saturate(-delta));
    shaded = lerp(shaded, _Highlight.rgb, saturate(delta));
    return float4(shaded, color.a);
}
