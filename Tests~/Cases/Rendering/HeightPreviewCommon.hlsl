// Shared sampling, projection and lighting for the isolated height-preview experiment.
Texture2D<float> _Height;
SamplerState sampler_Height;
int _SourceSize, _OutputSize, _Steps, _Pattern;
float _HeightScale;

float HeightAt(float2 uv) { return _Height.SampleLevel(sampler_Height, saturate(uv), 0); }
float ProjectY(float v, float height) { return 0.5 + ((v - 0.5) * 1.41421356237 + height * _HeightScale * 0.70710678118) * 0.52; }
float4 Surface(float2 uv)
{
    float step = 1.0 / _SourceSize;
    float dx = (HeightAt(uv + float2(step, 0)) - HeightAt(uv - float2(step, 0))) / (4 * step);
    float dy = (HeightAt(uv + float2(0, step)) - HeightAt(uv - float2(0, step))) / (4 * step);
    float3 n = normalize(float3(-dx * _HeightScale, -dy * _HeightScale, 1));
    float light = 0.2 + 0.8 * saturate(dot(n, normalize(float3(-0.35, -0.5, 1))));
    // Height, illumination, source depth, coverage: retain unbounded height for numerical comparison.
    return float4(HeightAt(uv), light, uv.y, 1);
}

float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float ValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
}
float Fixture(float2 uv)
{
    float2 p = uv * 2 - 1;
    if (_Pattern == 0) return 0.0; // Flat-plane oracle.
    if (_Pattern == 1) return 0.45 * exp(-dot(p - float2(-.25, .1), p - float2(-.25, .1)) * 7)
        + 0.22 * sin(uv.x * 12) * sin(uv.y * 10);
    if (_Pattern == 2) return (floor(uv.y * 9) % 3) * .18 + (length(p - float2(.2, -.3)) < .27 ? .35 : 0);
    if (_Pattern == 3)
    {
        // Four-source-pixel diagonal ridge plus a small peak: exposes missed thin geometry.
        float ridge = abs(uv.y - (.22 + .53 * uv.x)) < 2.0 / _SourceSize ? .65 : 0;
        return max(ridge, length(uv - float2(.63, .65)) < .012 ? .7 : 0);
    }
    if (_Pattern == 4) return (ValueNoise(uv * 9) * .55 + ValueNoise(uv * 37) * .28 + ValueNoise(uv * 151) * .17) * .65;
    return 1.4 * sin(uv.x * 7) * cos(uv.y * 6) + .7; // Signed and >1 HDR heights.
}
