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
    int2 lo = max(0, pixel - 1), hi = min((int2)_Level.xy - 1, pixel + 1);
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

float4 VisibilityMask(Varyings input) : SV_Target
{
    int2 center = (int2)input.pos.xy, last = (int2)_Level.xy - 1;
    float4 here = _MainTex.Load(int3(center, 0));
    float2 mean = 0;
    float3 secondMoment = 0;
    float2 geometry = 0;
    float support = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float4 tap = _MainTex.Load(int3(clamp(center + int2(x, y), 0, last), 0));
            float2 delta = tap.xy - here.xy;
            // Separable [1 2 1] / 4. An edge present in just one endpoint
            // is itself mismatch evidence, including when the other side is flat.
            float weight = (x == 0 ? .5 : .25) * (y == 0 ? .5 : .25);
            mean += weight * delta;
            secondMoment += weight * delta.xxy * delta.xyy;

            // Borrow area/validity only from this side of both warped images.
            // A jump of .15 in either bounded feature cuts the vote off completely.
            float jump = max(abs(delta.x), abs(delta.y));
            float voteWeight = weight * (1 - smoothstep(.05, .15, jump));
            geometry += voteWeight * tap.zw;
            support += voteWeight;
        }
    }
    geometry /= support; // The center always contributes .25.
    float3 central = secondMoment - mean.xxy * mean.xyy;
    float residual = max(0, central.x + central.z - 2 * central.y);
    float2 variance = max(0, central.xz) + .0001;
    float correlation = (central.y + .0001) * rsqrt(variance.x * variance.y);
    float mismatch = smoothstep(.00015, .0025, residual) * smoothstep(.06, .65, 1 - correlation);

    // A mismatched patch can prefer an endpoint only with directional evidence.
    // Cap compression at bad correspondences, preserving the stronger validity vote.
    float preference = clamp(.75 * geometry.x, -.65, .65) + 2 * geometry.y;
    return mismatch * clamp(2 * preference, -1, 1);
}

// The Liao field stores displacement in RG, visibility in B and opacity coverage in A.
float4 PackVisibility(Varyings input) : SV_Target
{
    float4 field = _Flow.Load(int3((int2)input.pos.xy, 0));
    field.z = _MainTex.SampleLevel(sampler_linear_clamp, input.pos.xy * _Level.zw, 0).r;
    return field;
}
