// @whimtex-effect Normal Map/Lighting
// @param texture2D _Normals = self // RGB normal map. Self reads the image before this FX.
// @param bool _PackedColor = true // Match Normal Map Output Encoding: enabled for Packed Color, disabled for Linear Data.
// @param bool _FlipY = false // Reverse the green axis for maps using the opposite Y convention.
// @param normal _LightDirection = (-0.5, 0.5, 1) // Direction from the surface toward the light.
// @param color _BaseColor = (1, 1, 1, 1) // Surface tint; alpha is preserved from the input.
// @param color _LightColor = (1, 1, 1, 1)
// @param color _ShadowColor = (0.05, 0.05, 0.05, 1)
// @param float _Intensity = 1 [0 .. ~4]
// @param float _Ambient = 0.15 [0 .. ~1]

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"

float4 ApplyFX(float2 uv, float4 color)
{
    float3 packed = tex2D(_Normals, uv).rgb;
    packed = _PackedColor > 0.5 ? SpriteEncode(packed) : packed;
    float3 n = packed * 2.0 - 1.0;
    float magnitude = max(abs(n.x), max(abs(n.y), abs(n.z)));
    n = magnitude > 1e-6 ? n / magnitude : float3(0, 0, 1);
    n *= rsqrt(max(dot(n, n), 1e-12));
    if (_FlipY > 0.5) n.y = -n.y;
    float diffuse = saturate(dot(n, _LightDirection));
    float3 lighting = lerp(_ShadowColor.rgb, _LightColor.rgb, diffuse) * max(_Intensity, 0.0) + _Ambient;
    return float4(_BaseColor.rgb * lighting, color.a);
}
