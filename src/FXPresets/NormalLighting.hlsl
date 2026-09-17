// @whimtex-effect Normal Map/Lighting
// @header(Normal Source)
// @param texture2D _Normals = self // RGB normal map. Self reads the image before this FX.
// @param bool _PackedColor = true // Match Normal Map Output Encoding: enabled for Packed Color, disabled for Linear Data.
// @param bool _FlipY = false // Reverse the green axis for maps using the opposite Y convention.
// @header(Lighting)
// @param enum _Output = Both {Both: 0, HighlightOnly: 1, ShadowOnly: 2} // Select the transparent lighting component. No effect when Base Color alpha is 1.
// @param normal _LightDirection = (-0.5, 0.5, 1) // Direction from the surface toward the light.
// @param color _BaseColor = (1, 1, 1, 0) // RGB tints the surface. Alpha blends lighting only (0) into the filled surface (1); use layer opacity for overall opacity.
// @param color _LightColor = (1, 1, 1, 1) // Highlight tint; alpha controls strength in Lighting Only mode.
// @param color _ShadowColor = (0.05, 0.05, 0.05, 1) // Shadow tint; alpha controls strength in Lighting Only mode.
// @param float _Intensity = 1 [0 .. ~4]
// @param float _Ambient = 0.15 [0 .. ~1] // Ambient surface illumination, faded in by Base Color alpha.

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"
#include "Packages/com.dcfapixels.whimtex/src/Shaders/SurfaceLighting.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float3 packed = tex2D(_Normals, uv).rgb;
    packed = _PackedColor > 0.5 ? SpriteEncode(packed) : packed;
    float3 n = packed * 2.0 - 1.0;
    if (_FlipY > 0.5) n.y = -n.y;
    return WhimTexSurfaceLighting(n, color.a, _Output, _LightDirection,
        _BaseColor, _LightColor, _ShadowColor, _Intensity, _Ambient);
}
