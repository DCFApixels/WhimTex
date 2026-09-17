#ifndef WHIMTEX_SURFACE_LIGHTING_INCLUDED
#define WHIMTEX_SURFACE_LIGHTING_INCLUDED

float3 WhimTexLightingNormal(float3 normal)
{
    float magnitude = max(abs(normal.x), max(abs(normal.y), abs(normal.z)));
    normal = magnitude > 1e-6 ? normal / magnitude : float3(0, 0, 1);
    return normal * rsqrt(max(dot(normal, normal), 1e-12));
}

float4 WhimTexSurfaceLighting(float3 normal, float inputAlpha, float outputMode,
    float3 lightDirection, float4 baseColor, float4 lightColor, float4 shadowColor, float intensity, float ambient)
{
    normal = WhimTexLightingNormal(normal);
    lightDirection = WhimTexLightingNormal(lightDirection);
    float facing = dot(normal, lightDirection);
    intensity = max(intensity, 0.0);
    float3 lighting = lerp(shadowColor.rgb, lightColor.rgb, saturate(facing)) * intensity + max(ambient, 0.0);

    float delta = (facing - lightDirection.z) * intensity;
    float highlight = outputMode == 2 ? 0.0 : saturate(delta);
    float shadow = outputMode == 1 ? 0.0 : saturate(-delta);
    float4 tint = delta >= 0.0 ? lightColor : shadowColor;
    float weight = saturate(baseColor.a);
    float overlayAlpha = saturate((highlight + shadow) * tint.a);
    float surfaceAlpha = saturate(inputAlpha);
    float alpha = lerp(overlayAlpha, surfaceAlpha, weight);
    float3 premultiplied = lerp(tint.rgb * overlayAlpha, baseColor.rgb * lighting * surfaceAlpha, weight);
    return float4(alpha > 0.0 ? premultiplied / max(alpha, 1e-20) : float3(0, 0, 0), alpha);
}

#endif
