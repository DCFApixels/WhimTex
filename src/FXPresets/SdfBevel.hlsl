// @whimtex-effect Lighting/Bevel Emboss
// @header(Height Source)
// @param texture2D _HeightMap = self // Height image before this FX, or another layer. No raw distance data is used.
// @param enum _HeightChannel = Luminance {Luminance: 0, R: 1, G: 2, B: 3, Alpha: 4} // Channel mapped to height in the 0 to 1 range.
// @header(Surface Profile)
// @param float _Depth = 6 [~-64 .. ~64] // Positive raises the surface; negative engraves it.
// @param curve _Profile // Map the selected channel from 0 to 1 into height. Linear preserves the input profile.
// @param label(Normal Radius (px)) float _Smoothing = 2 [0 .. ~8] // Normal sampling radius in document pixels; reduces contour stair steps.
// @header(Lighting)
// @param enum _Output = Both {Both: 0, HighlightOnly: 1, ShadowOnly: 2} // Select the transparent lighting component. No effect when Base Color alpha is 1.
// @param normal _LightDirection = (-0.5, 0.5, 1) // Direction from the surface toward the light.
// @param color _BaseColor = (1, 1, 1, 0) // RGB tints the surface. Alpha blends lighting only (0) into the filled surface (1); use layer opacity for overall opacity.
// @param color _LightColor = (1, 1, 1, 1) // Highlight tint; alpha controls strength in Lighting Only mode.
// @param color _ShadowColor = (0.05, 0.05, 0.05, 1) // Shadow tint; alpha controls strength in Lighting Only mode.
// @param float _Intensity = 1 [0 .. ~4]
// @param float _Ambient = 0.15 [0 .. ~1] // Ambient surface illumination, faded in by Base Color alpha.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/SurfaceLighting.cginc"

float BevelHeight(float2 uv)
{
    float4 source = tex2D(_HeightMap, uv);
    float height = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
    if (_HeightChannel == 1) height = source.r;
    else if (_HeightChannel == 2) height = source.g;
    else if (_HeightChannel == 3) height = source.b;
    else if (_HeightChannel == 4) height = source.a;
    // Hidden RGB must not produce relief outside the image's coverage.
    if (_HeightChannel != 4) height *= saturate(source.a);
    return _Profile_Sample(saturate(height)) * _Depth;
}

float4 ApplyFX(float2 uv, float4 color)
{
    float radius = max(_PreviewScale, _Smoothing);
    float2 stepUV = radius * _CanvasSize.zw;
    float tl = BevelHeight(uv + stepUV * float2(-1, 1));
    float tr = BevelHeight(uv + stepUV * float2(1, 1));
    float bl = BevelHeight(uv + stepUV * float2(-1, -1));
    float br = BevelHeight(uv + stepUV * float2(1, -1));
    float dx = (3.0 * (tr + br - tl - bl) + 10.0 * (BevelHeight(uv + float2(stepUV.x, 0)) - BevelHeight(uv - float2(stepUV.x, 0)))) / (32.0 * radius);
    float dy = (3.0 * (tl + tr - bl - br) + 10.0 * (BevelHeight(uv + float2(0, stepUV.y)) - BevelHeight(uv - float2(0, stepUV.y)))) / (32.0 * radius);
    return WhimTexSurfaceLighting(float3(-dx, -dy, 1.0), color.a, _Output, _LightDirection,
        _BaseColor, _LightColor, _ShadowColor, _Intensity, _Ambient);
}
