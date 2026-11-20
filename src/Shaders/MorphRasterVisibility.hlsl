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
    float support = 0, absoluteArea = 0, textureWeight = 0, matchedTexture = 0;
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
            absoluteArea += voteWeight * abs(tap.z);
            // Trust area changes where nearby features vary together on this side.
            // A flat neighbor or two opposite gradients cannot verify a match.
            float2 magnitude = abs(delta);
            float strongest = max(magnitude.x, magnitude.y);
            float detailWeight = voteWeight * smoothstep(.004, .035, strongest);
            textureWeight += detailWeight;
            matchedTexture += detailWeight * step(0, delta.x * delta.y) * min(magnitude.x, magnitude.y) / max(.001, strongest);
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
    float agreement = abs(geometry.x) * support / max(.001, absoluteArea);
    float oneSided = abs(variance.x - variance.y) / (variance.x + variance.y);
    float verified = smoothstep(.45, .85, matchedTexture / max(.001, textureWeight))
        * smoothstep(.04, .20, textureWeight);
    // An isolated edge against a flat key can still indicate an occlusion.
    // Two differently textured surfaces need corroboration from their neighbors.
    float confidence = agreement * lerp(.24 + .34 * smoothstep(.85, .995, oneSided), 1, verified);
    float compression = clamp(1.5 * geometry.x, -1, 1);
    // Small signed votes can guide a fade. Require corroboration as a vote
    // approaches replacement of one key by the other.
    compression *= confidence / max(.0001, confidence + (1 - confidence) * abs(compression));
    return float4(mismatch * clamp(compression + 4 * geometry.y, -1, 1), here.xy, here.w);
}

Texture2D<float4> _VisibilityFeatures;
float2 _MaskGrid;

float2 WarpedVisibilityDetail(float2 uv, float2 motion)
{
    float2 a = _VisibilityFeatures.SampleLevel(sampler_linear_clamp, uv - .5 * motion, 0).xy;
    float2 b = _VisibilityFeatures.SampleLevel(sampler_linear_clamp, uv + .5 * motion, 0).zw;
    return .5 * float2(a.x + a.y, b.x + b.y);
}

float2 NearbyVisibilityDetail(float2 uv)
{
    return WarpedVisibilityDetail(uv, _Flow.SampleLevel(sampler_linear_clamp, uv, 0).xy);
}

// Quadratic B-spline: three samples per axis, continuous as the footprint moves.
float2 VisibilityKernel(float2 d)
{
    d = abs(d);
    float2 tail = max(0, 1.5 - d);
    return lerp(.75 - d * d, .5 * tail * tail, step(.5, d));
}

// The Liao field stores displacement in RG, visibility in B and opacity coverage in A.
float4 PackVisibility(Varyings input) : SV_Target
{
    float4 field = _Flow.Load(int3((int2)input.pos.xy, 0));
    float2 uv = input.pos.xy * _Level.zw;
    float2 grid = uv * _MaskGrid - .5;
    int2 center = (int2)floor(grid + .5);
    float4 coarse = _MainTex.SampleLevel(sampler_linear_clamp, uv, 0);
    float2 detail = WarpedVisibilityDetail(uv, field.xy);
    float2 dx = NearbyVisibilityDetail(uv + float2(_Level.z, 0)) - NearbyVisibilityDetail(uv - float2(_Level.z, 0));
    float2 dy = NearbyVisibilityDetail(uv + float2(0, _Level.w)) - NearbyVisibilityDetail(uv - float2(0, _Level.w));
    float2 energy = dx * dx + dy * dy;
    // Coincident fine edges need little endpoint preference. One-sided edges
    // still supply evidence of a real occlusion.
    float sharedDetail = saturate(2 * (dx.x * dx.y + dy.x * dy.y) / (energy.x + energy.y + .0004));
    float sharp = smoothstep(.025, .15, sqrt(max(energy.x, energy.y)));
    float tolerance = lerp(.12, .035, sharp);
    float vote = 0, support = 0;
    [unroll] for (int y = -1; y <= 1; y++)
    {
        [unroll] for (int x = -1; x <= 1; x++)
        {
            int2 tap = center + int2(x, y);
            float4 sampleMask = _MainTex.Load(int3(clamp(tap, 0, (int2)_MaskGrid - 1), 0));
            float2 spatial = VisibilityKernel(grid - tap);
            float2 difference = (detail - sampleMask.yz) / tolerance;
            float w = spatial.x * spatial.y / (1 + dot(difference, difference));
            vote += w * sampleMask.x;
            support += w;
        }
    }
    // Where the coarse guide explains little of the fine detail, fade the
    // preference instead of drawing the edge of the coarse mask on both keys.
    float refined = vote / max(.0001, support) * smoothstep(0, .65, support) * (1 - sharedDetail);
    field.z = lerp(refined, coarse.x, abs(coarse.w));
    return field;
}
