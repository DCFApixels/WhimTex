// @whimtex-effect Normal Map/Lighting
// @group("Lighting")
// @icon("../Editor/Icons/FX/Lighting.svg")
// @header(Normal Source)
// @param texture2D _Normals = self // RGB normal map. Self reads the image before this FX.
// @param enum _InputEncoding = Gamma {Linear: 0, Gamma: 1} // Match Normal Map Output Encoding. Gamma reads sRGB-encoded channel values; Linear reads raw channel values.
// @param toggle(float) _FlipY = false // Reverse the green axis for maps using the opposite Y convention.
// @group(Lighting Output; _Output)
// @param hidden enum _Output = Both {Both: 0, HighlightOnly: 1, ShadowOnly: 2, SpecularOnly: 3} // Both/Highlight Only include enabled Specular. Shadow Only excludes it. Specular Only omits the surface and diffuse lighting.
// @param normal _LightDirection = (-0.5, 0.5, 1) // Direction from the surface toward the light.
// @param color _BaseColor = (1, 1, 1, 0) // RGB tints the surface. Alpha blends lighting only (0) into the filled surface (1); use layer opacity for overall opacity.
// @param color _LightColor = (1, 1, 1, 1) // Highlight tint; alpha controls strength in Lighting Only mode.
// @param color _ShadowColor = (0.05, 0.05, 0.05, 1) // Shadow tint; alpha controls strength in Lighting Only mode.
// @param float _Intensity = 1 [0 .. ~4]
// @param float _Ambient = 0.15 [0 .. ~1] // Ambient surface illumination, faded in by Base Color alpha.
// @endgroup
// @group(Specular; _SpecularEnabled)
// @param hidden toggle(float) _SpecularEnabled = false // Enable an orthographic specular highlight; disabled preserves the original lighting.
// @enableif _SpecularEnabled == 1
// @param color _SpecularColor = (1, 1, 1, 1) // Highlight tint; alpha scales its strength.
// @param float _SpecularIntensity = 1 [0 .. ~4] // Independent of the diffuse Intensity and Light Color.
// @param float _Roughness = 0.25 [0 .. 1] // Low values make a tight highlight; high values make it broad. Zero uses a finite sharpness limit.
// @endenableif
// @endgroup

#include "Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc"
#include "Packages/com.dcfapixels.whimtex/src/Shaders/SurfaceLighting.cginc"

float NormalLightingSpecular(float3 normal, float3 lightDirection)
{
    // Orthographic view: every pixel looks toward +Z. Avoid an undefined half vector
    // for a light exactly opposite the viewer, and reject back-facing surfaces.
    float3 halfDirection = lightDirection + float3(0, 0, 1);
    float halfLengthSquared = dot(halfDirection, halfDirection);
    if (halfLengthSquared <= 1e-12 || normal.z <= 0.0)
        return 0.0;
    halfDirection *= rsqrt(halfLengthSquared);
    float roughness = max(saturate(_Roughness), 0.01);
    float exponent = max(2.0 / (roughness * roughness) - 2.0, 1.0);
    return pow(saturate(dot(normal, halfDirection)), exponent)
        * saturate(dot(normal, lightDirection));
}

float4 ApplyFX(PinnedUV uv, float4 color)
{
    float3 packed = SampleTexture(_Normals, uv).rgb;
    packed = _InputEncoding > 0.5 ? SpriteEncode(packed) : packed;
    float3 n = packed * 2.0 - 1.0;
    n.y *= lerp(1.0, -1.0, step(0.5001, _FlipY));
    float4 lighting = _Output == 3 ? float4(0, 0, 0, 0) : WhimTexSurfaceLighting(n, color.a, _Output, _LightDirection,
        _BaseColor, _LightColor, _ShadowColor, _Intensity, _Ambient);
    if (_SpecularEnabled < 0.5 || _Output == 2)
        return lighting;

    float strength = NormalLightingSpecular(WhimTexLightingNormal(n), WhimTexLightingNormal(_LightDirection))
        * max(_SpecularIntensity, 0.0) * saturate(_SpecularColor.a);
    float coverage = saturate(color.a);
    float specularAlpha = saturate(strength) * coverage;
    float alpha = lighting.a + specularAlpha * (1.0 - lighting.a);
    // Add light in premultiplied space, then return straight alpha. The highlight
    // supplies coverage on transparent output; intensity above one stays HDR.
    float3 premultiplied = lighting.rgb * lighting.a + _SpecularColor.rgb * strength * coverage;
    return float4(alpha > 0.0 ? premultiplied / max(alpha, 1e-20) : float3(0, 0, 0), alpha);
}
