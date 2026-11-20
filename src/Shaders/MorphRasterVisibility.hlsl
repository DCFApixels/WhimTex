// Visibility is estimated on the solved halfway domain. Compare the endpoint
// footprints of a patch, where compression leaves less image evidence.
float4 VisibilityEvidence(Varyings input) : SV_Target
{
    int2 pixel = (int2)input.pos.xy;
    float2 p = input.pos.xy * _Level.zw;
    float2 d = _Flow.SampleLevel(sampler_linear_clamp, p, 0).xy;
    float2 aUV = p - .5 * d, bUV = p + .5 * d;
    float2 a = _MainTex.SampleLevel(sampler_linear_clamp, aUV, 0).xy;
    float2 b = _MainTex.SampleLevel(sampler_linear_clamp, bUV, 0).zw;
    int2 lo = max(0, pixel - 2), hi = min((int2)_Level.xy - 1, pixel + 2);
    float2 dx = (_Flow.SampleLevel(sampler_linear_clamp, float2((hi.x + .5) * _Level.z, p.y), 0).xy
        - _Flow.SampleLevel(sampler_linear_clamp, float2((lo.x + .5) * _Level.z, p.y), 0).xy) * _Level.xy / max(1, hi.x - lo.x);
    float2 dy = (_Flow.SampleLevel(sampler_linear_clamp, float2(p.x, (hi.y + .5) * _Level.w), 0).xy
        - _Flow.SampleLevel(sampler_linear_clamp, float2(p.x, (lo.y + .5) * _Level.w), 0).xy) * _Level.xy / max(1, hi.y - lo.y);
    float base = 1 + .25 * (dx.x * dy.y - dx.y * dy.x);
    float trace = .5 * (dx.x + dy.y);
    float areaA = max(.04, base - trace), areaB = max(.04, base + trace);
    float validA = all(abs(aUV - .5) <= .5) ? 1 : 0;
    float validB = all(abs(bUV - .5) <= .5) ? 1 : 0;
    return float4(.5 * (a.x + a.y), .5 * (b.x + b.y),
        clamp(log2(areaB / areaA), -3, 3), validB - validA);
}

Texture2D<float4> _VisibilityPrior;

// A broad contrast cue breaks ties coherently across a disconnected pair of
// features. Read the coarsest feature pyramid once for the entire image pair.
float4 VisibilityPrior(Varyings input) : SV_Target
{
    float2 sum = 0, squares = 0;
    [loop] for (int y = 0; y < (int)_Level.y; y++)
    {
        [loop] for (int x = 0; x < (int)_Level.x; x++)
        {
            float4 features = _MainTex.Load(int3(x, y, 0));
            float2 sample = .5 * (features.xz + features.yw);
            sum += sample;
            squares += sample * sample;
        }
    }
    float invCount = _Level.z * _Level.w;
    float2 mean = sum * invCount;
    float2 variance = max(0, squares * invCount - mean * mean);
    return clamp(log2((.001 + variance.y) / (.001 + variance.x)), -1, 1);
}

struct VisibilityWindow
{
    float4 sum;
    float3 secondMoment;
    float weight;
};

void AddVisibilitySample(inout VisibilityWindow window, float4 sample, float weight)
{
    window.sum += weight * sample;
    window.secondMoment += weight * sample.xxy * sample.xyy;
    window.weight += weight;
}

// Centering and correlation keep matched features neutral across exposure changes.
float VisibilityMismatch(VisibilityWindow window)
{
    float2 mean = window.sum.xy / window.weight;
    float3 central = window.secondMoment / window.weight - mean.xxy * mean.xyy;
    float residual = max(0, central.x + central.z - 2 * central.y);
    float2 variance = max(0, central.xz) + .0001;
    float correlation = (central.y + .0001) * rsqrt(variance.x * variance.y);
    return smoothstep(.00015, .0025, residual) * smoothstep(.06, .65, 1 - correlation);
}

float4 VisibilityMask(Varyings input) : SV_Target
{
    int2 center = (int2)input.pos.xy, last = (int2)_Level.xy - 1;
    float shortSide = min(_Level.x, _Level.y);
    float sigma = max(1, shortSide * .055);
    int radius = min(6, (int)ceil(sigma * 3));
    VisibilityWindow local = (VisibilityWindow)0, region = (VisibilityWindow)0;
    [loop] for (int y = -radius; y <= radius; y++)
    {
        [loop] for (int x = -radius; x <= radius; x++)
        {
            int2 d = int2(x, y);
            float4 sample = _MainTex.Load(int3(clamp(center + d, 0, last), 0));
            float weight = exp(-.125 * dot(float2(d), float2(d)) / (sigma * sigma));
            AddVisibilitySample(region, sample, weight);
            AddVisibilitySample(local, sample, weight * weight * weight * weight);
        }
    }
    float2 nearGeometry = local.sum.zw / local.weight;
    float regionArea = region.sum.z / region.weight;
    float prior = _VisibilityPrior.Load(int3(0, 0, 0)).r;
    // Extreme compression is also common at a bad correspondence. Limit its
    // vote so one folded patch cannot bring back a second copy of a feature.
    float areaVote = clamp(.25 * nearGeometry.x + .5 * regionArea, -.65, .65);
    float preference = areaVote + 2 * nearGeometry.y + 2.5 * prior;
    float mismatch = max(VisibilityMismatch(local), .85 * VisibilityMismatch(region));
    return mismatch * clamp(2 * preference, -1, 1);
}

// The Liao field stores displacement in RG, visibility in B and opacity coverage in A.
float4 PackVisibility(Varyings input) : SV_Target
{
    float4 field = _Flow.Load(int3((int2)input.pos.xy, 0));
    field.z = _MainTex.SampleLevel(sampler_linear_clamp, input.pos.xy * _Level.zw, 0).r;
    return field;
}
