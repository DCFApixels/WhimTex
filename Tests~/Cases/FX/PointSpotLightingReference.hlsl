#ifndef WHIMTEX_DOCUMENT_LIGHTING_INCLUDED
#define WHIMTEX_DOCUMENT_LIGHTING_INCLUDED

#define WT_MAX_LIGHTS 64
int _WhimTex_LightCount;
float4 _WhimTex_LightPositions[WT_MAX_LIGHTS];
float4 _WhimTex_LightColors[WT_MAX_LIGHTS];
float4 _WhimTex_LightDirections[WT_MAX_LIGHTS];
float4 _WhimTex_LightCones[WT_MAX_LIGHTS];
float4 _WhimTex_AmbientColor;

struct WTLightSample
{
    float3 direction;
    float3 color;
    float attenuation;
};

int WTLightCount() { return _WhimTex_LightCount; }
float3 WTAmbientColor() { return _WhimTex_AmbientColor.rgb; }

// Position is in full canvas pixels, bottom-left origin, +Z toward the viewer.
// Disabled sources retain their index and contribute zero radiance.
WTLightSample WTSampleLight(int index, float3 position)
{
    WTLightSample light = (WTLightSample)0;
    light.direction = float3(0, 0, 1);
    light.color = 0;
    light.attenuation = 0;
    if (index < 0 || index >= _WhimTex_LightCount) return light;
    float4 source = _WhimTex_LightPositions[index];
    light.color = _WhimTex_LightColors[index].rgb;
    light.direction = _WhimTex_LightDirections[index].xyz;
    light.attenuation = 1;
    if (source.w > 0.5)
    {
        float3 offset = source.xyz - position;
        float distance = length(offset);
        light.direction = distance > 1e-6 ? offset / distance : float3(0, 0, 1);
        float fade = saturate(1 - distance / max(_WhimTex_LightDirections[index].w, 1e-6));
        light.attenuation = pow(fade, max(_WhimTex_LightColors[index].w, .01));
        if (source.w > 1.5)
        {
            // Cone axis points out of the source; light.direction points into it.
            float facing = dot(_WhimTex_LightDirections[index].xyz, -light.direction);
            float4 cone = _WhimTex_LightCones[index];
            float edge = cone.z > .5 ? smoothstep(0, 1,
                saturate((facing - cone.x) / max(cone.y - cone.x, 1e-7))) : step(cone.x, facing);
            light.attenuation *= distance > 1e-6 ? edge : 1;
        }
    }
    return light;
}

float3 WTLightingUnitNormal(float3 n)
{
    float scale = max(max(abs(n.x), abs(n.y)), abs(n.z));
    n = scale > 1e-10 ? n / scale : float3(0, 0, 1);
    return n * rsqrt(max(dot(n, n), 1e-12));
}

float WTLightSpecular(float3 normal, float3 direction, float roughness)
{
    float3 h = direction + float3(0, 0, 1);
    float h2 = dot(h, h);
    if (h2 <= 1e-12 || normal.z <= 0) return 0;
    float r = max(saturate(roughness), .01);
    return pow(saturate(dot(normal, h * rsqrt(h2))), max(2 / (r*r) - 2, 1)) * saturate(dot(normal, direction));
}

float4 WTGlobalLighting(float3 position, float4 surface, float3 normal, float ambient,
    float3 specularColor, float specularIntensity, float roughness)
{
    float3 n = WTLightingUnitNormal(normal);
    float3 rgb = surface.rgb * ambient * WTAmbientColor();
    [loop] for (int i = 0; i < WTLightCount(); i++)
    {
        WTLightSample light = WTSampleLight(i, position);
        rgb += light.color * light.attenuation * (surface.rgb * saturate(dot(n, light.direction)) +
            specularColor * specularIntensity * WTLightSpecular(n, light.direction, roughness));
    }
    return float4(rgb, surface.a);
}

#endif
