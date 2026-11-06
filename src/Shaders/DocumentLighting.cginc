#ifndef WHIMTEX_DOCUMENT_LIGHTING_INCLUDED
#define WHIMTEX_DOCUMENT_LIGHTING_INCLUDED

#define WT_MAX_LIGHTS 64
int _WhimTex_LightCount;
float4 _WhimTex_LightPositions[WT_MAX_LIGHTS];
float4 _WhimTex_LightColors[WT_MAX_LIGHTS];
float4 _WhimTex_LightDirections[WT_MAX_LIGHTS];
float4 _WhimTex_LightCones[WT_MAX_LIGHTS];
float4 _WhimTex_AmbientColor;
#if defined(WHIMTEX_AREA_LIGHTS) || defined(WHIMTEX_LIGHT_COOKIES)
float4 _WhimTex_LightRight[WT_MAX_LIGHTS];
float4 _WhimTex_LightUp[WT_MAX_LIGHTS];
#endif
#ifdef WHIMTEX_LIGHT_COOKIES
sampler2D _WhimTex_LightCookieAtlas;
float4 _WhimTex_LightCookieViews[WT_MAX_LIGHTS];
#endif

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
#ifdef WHIMTEX_AREA_LIGHTS
        if (source.w > 2.5) light.attenuation *= saturate(dot(-light.direction, _WhimTex_LightDirections[index].xyz));
#endif
        if (source.w > 1.5
#ifdef WHIMTEX_AREA_LIGHTS
            && source.w < 2.5
#endif
            )
        {
            // Cone axis points out of the source; light.direction points into it.
            float facing = dot(_WhimTex_LightDirections[index].xyz, -light.direction);
            float4 cone = _WhimTex_LightCones[index];
            float edge = cone.z > .5 ? smoothstep(0, 1,
                saturate((facing - cone.x) / max(cone.y - cone.x, 1e-7))) : step(cone.x, facing);
            light.attenuation *= distance > 1e-6 ? edge : 1;
#ifdef WHIMTEX_LIGHT_COOKIES
            float4 view = _WhimTex_LightCookieViews[index];
            [branch] if (view.z > 0 && light.attenuation > 0)
            {
                float3 ray = -offset;
                float depth = dot(ray, _WhimTex_LightDirections[index].xyz);
                float tangent = sqrt(max(1 - cone.x * cone.x, 0)) / max(cone.x, 1e-6);
                float2 uv = float2(dot(ray, _WhimTex_LightRight[index].xyz), dot(ray, _WhimTex_LightUp[index].xyz)) /
                    max(2 * depth * tangent, 1e-6) + .5;
                float4 cookie = tex2Dlod(_WhimTex_LightCookieAtlas, float4(view.xy + saturate(uv) * view.zw, 0, 0));
                light.color *= cookie.rgb * saturate(cookie.a);
                light.attenuation *= depth > 0 && all(uv >= 0) && all(uv <= 1) ? 1 : 0;
            }
#endif
        }
    }
    return light;
}

int WTLightSampleCount(int index)
{
#ifdef WHIMTEX_AREA_LIGHTS
    if (index >= 0 && index < WTLightCount() && _WhimTex_LightPositions[index].w > 2.5 &&
        any(_WhimTex_LightColors[index].rgb != 0)) return 64;
#endif
    return 1;
}

// Gaussian quadrature. The overload includes the integration weight in attenuation.
// The two-argument overload intentionally remains a center-direction sample.
#ifdef WHIMTEX_AREA_LIGHTS
float2 WTLightQuadrature(uint index)
{
    if (index == 0u) return float2(-.9602898565, .1012285363);
    if (index == 1u) return float2(-.7966664774, .2223810345);
    if (index == 2u) return float2(-.5255324099, .3137066459);
    if (index == 3u) return float2(-.1834346425, .3626837834);
    if (index == 4u) return float2(.1834346425, .3626837834);
    if (index == 5u) return float2(.5255324099, .3137066459);
    if (index == 6u) return float2(.7966664774, .2223810345);
    return float2(.9602898565, .1012285363);
}
#endif
WTLightSample WTSampleLight(int index, float3 position, int sampleIndex)
{
    WTLightSample result = (WTLightSample)0;
#ifdef WHIMTEX_AREA_LIGHTS
    [branch] if (index >= 0 && index < WTLightCount() && _WhimTex_LightPositions[index].w > 2.5)
    {
        uint sampleId = (uint)clamp(sampleIndex, 0, 63);
        uint x = sampleId & 7u, y = sampleId >> 3u;
        float2 gx = WTLightQuadrature(x), gy = WTLightQuadrature(y);
        float2 q = float2(gx.x, gy.x);
        float weight = gx.y * gy.y * .25;
        if (_WhimTex_LightCones[index].w > .5)
        {
            float radius = sqrt((gy.x + 1) * .5);
            float phi = (x + .5) * .78539816339;
            q = radius * float2(cos(phi), sin(phi));
            weight = gy.y / 16;
        }
        float4 u = _WhimTex_LightRight[index], v = _WhimTex_LightUp[index];
        float3 offset = _WhimTex_LightPositions[index].xyz + u.xyz * (q.x * u.w) + v.xyz * (q.y * v.w) - position;
        float distance = length(offset);
        result.direction = distance > 1e-6 ? offset / distance : float3(0,0,1);
        result.color = _WhimTex_LightColors[index].rgb;
        float fade = saturate(1 - distance / max(_WhimTex_LightDirections[index].w, 1e-6));
        result.attenuation = pow(fade, max(_WhimTex_LightColors[index].w, .01)) *
            saturate(dot(-result.direction, _WhimTex_LightDirections[index].xyz)) * weight;
    }
    else
#endif
    { result = WTSampleLight(index, position); }
    return result;
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
#ifdef WHIMTEX_AREA_LIGHTS
        [loop] for (int s = 0; s < WTLightSampleCount(i); s++)
        {
        WTLightSample light = WTSampleLight(i, position, s);
#else
        WTLightSample light = WTSampleLight(i, position);
#endif
        rgb += light.color * light.attenuation * (surface.rgb * saturate(dot(n, light.direction)) +
            specularColor * specularIntensity * WTLightSpecular(n, light.direction, roughness));
#ifdef WHIMTEX_AREA_LIGHTS
        }
#endif
    }
    return float4(rgb, surface.a);
}

#endif
