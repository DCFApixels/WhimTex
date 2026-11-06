#ifndef WHIMTEX_GLOBAL_SURFACE_LIGHTING_INCLUDED
#define WHIMTEX_GLOBAL_SURFACE_LIGHTING_INCLUDED
// DocumentLighting is supplied by the shared program wrapper.
float4 WhimTexGlobalSurfaceLighting(float3 position, float3 normal, float inputAlpha, float outputMode,
    float4 baseColor, float4 lightColor, float4 shadowColor, float intensity, float ambient,
    float4 specularColor, float specularIntensity, float roughness)
{
    float3 n = WTLightingUnitNormal(normal);
    float3 filled = baseColor.rgb * max(ambient, 0) * WTAmbientColor();
    float3 overlay = 0;
    float overlayAlpha = 0;
    float3 specular = 0;
    float specularAlpha = 0;
    [loop] for (int i = 0; i < WTLightCount(); i++)
    {
#ifdef WHIMTEX_AREA_LIGHTS
        float sourceOverlayAlpha = 0, sourceSpecularAlpha = 0;
        [loop] for (int sampleIndex = 0; sampleIndex < WTLightSampleCount(i); sampleIndex++)
        {
        WTLightSample light = WTSampleLight(i, position, sampleIndex);
#else
        WTLightSample light = WTSampleLight(i, position);
#endif
        float facing = dot(n, light.direction);
        float power = max(intensity, 0) * light.attenuation;
        filled += baseColor.rgb * lerp(shadowColor.rgb, lightColor.rgb, saturate(facing)) * light.color * power;
        float delta = (facing - light.direction.z) * power;
        float4 tint = delta >= 0 ? lightColor : shadowColor;
        float weight = (outputMode == 1 && delta < 0 || outputMode == 2 && delta >= 0) ? 0 : abs(delta) * saturate(tint.a);
        float radiance = max(max(abs(light.color.r), abs(light.color.g)), abs(light.color.b));
        float a = saturate(weight * radiance);
        overlay += tint.rgb * light.color * weight;
#ifdef WHIMTEX_AREA_LIGHTS
        sourceOverlayAlpha += weight * radiance;
#else
        overlayAlpha += a * (1 - overlayAlpha);
#endif
        if (outputMode != 2)
        {
            float s = WTLightSpecular(n, light.direction, roughness) * max(specularIntensity, 0) *
                saturate(specularColor.a) * light.attenuation * saturate(inputAlpha);
            specular += specularColor.rgb * light.color * s;
#ifdef WHIMTEX_AREA_LIGHTS
            sourceSpecularAlpha += s * radiance;
#else
            specularAlpha += saturate(s * radiance) * (1 - specularAlpha);
#endif
        }
#ifdef WHIMTEX_AREA_LIGHTS
        }
        overlayAlpha += saturate(sourceOverlayAlpha) * (1 - overlayAlpha);
        specularAlpha += saturate(sourceSpecularAlpha) * (1 - specularAlpha);
#endif
    }
    float blend = saturate(baseColor.a);
    float alpha = outputMode == 3 ? 0 : lerp(overlayAlpha, saturate(inputAlpha), blend);
    float3 premultiplied = outputMode == 3 ? float3(0, 0, 0) : lerp(overlay, filled * saturate(inputAlpha), blend);
    alpha += specularAlpha * (1 - alpha);
    premultiplied += specular;
    return float4(alpha > 0 ? premultiplied / max(alpha, 1e-20) : float3(0, 0, 0), alpha);
}
#endif
