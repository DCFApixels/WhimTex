// Seven angles per displacement. The outer ring also tests longer motion;
// the last column stores the inherited Liao field at its exact displacement.
float4 _RotationGrid;
Texture2D<float4> _RotationFeatures, _RotationScores, _RotationIndependent;

float2 RotationDelta(float2 offset, float slope)
{
    float2 pixels = offset * _RasterSize;
    return slope * float2(-pixels.y, pixels.x) / _RasterSize;
}

float3 RotationPose(uint x, uint y)
{
    float2 metric = min(_RasterSize.x, _RasterSize.y) / _RasterSize;
    float2 step = float2(x % 9u, y % 9u) - 4;
    float2 translation = (step + sign(step) * max(abs(step) - 3, 0)) * .075 * metric;
    // Full endpoint displacement on the halfway domain uses 2*tan(theta/2).
    float slope = 2 * tan((float(x / 9u) - 3) * .261799388);
    return float3(translation, slope);
}

float2 RotationSignal(float2 p, float2 halfMotion)
{
    float2 a = _RotationFeatures.SampleLevel(sampler_linear_clamp, p - halfMotion, 0).xy;
    float2 b = _RotationFeatures.SampleLevel(sampler_linear_clamp, p + halfMotion, 0).zw;
    return .5 * float2(a.x + a.y, b.x + b.y);
}

float RotationAppearance(float2 center, float3 pose, float dilation, bool baseline)
{
    float2 spacing = .0375 * min(_RasterSize.x, _RasterSize.y) / _RasterSize;
    float2 centerShift = .5 * ((_AnchorCount > 0 || baseline ? LiaoInherited(center) : 0) + pose.xy);
    float2 reference = RotationSignal(center, centerShift);
    float4 moments = 0;
    float product = 0, count = 0, mass = 0, mismatch = 0;
    [loop] for (int y = -3; y <= 3; y++)
    {
        [unroll] for (int x = -3; x <= 3; x++)
        {
            float2 offset = float2(x, y) * spacing;
            float2 p = center + offset;
            float2 initial = _AnchorCount > 0 || baseline ? LiaoInherited(p) : 0;
            float2 v = .5 * (initial + pose.xy + RotationDelta(offset, pose.z) + dilation * offset);
            if (any(abs(p - v - .5) > .5) || any(abs(p + v - .5) > .5)) continue;
            float2 signals = RotationSignal(p, v);
            float2 jump = signals - reference;
            float edge = max(abs(jump.x), abs(jump.y));
            float weight = .25 + .75 / (1 + 80 * edge * edge);
            moments += weight * float4(signals, signals * signals);
            product += weight * signals.x * signals.y;
            mass += weight;
            mismatch += abs(jump.x - jump.y) / (.05 + abs(jump.x) + abs(jump.y));
            count++;
        }
    }
    if (count < 32) return -2;
    float2 mean = moments.xy / mass;
    float2 variance = max(0, moments.zw - mass * mean * mean);
    float score = 2 * (product - mass * mean.x * mean.y) / (variance.x + variance.y + .0004 * mass);
    float boundary = saturate(2 * (1 - mass / count));
    return score + boundary * .25 * (.35 - mismatch / count) - (.22 + .28 * boundary) * abs(mean.x - mean.y)
        - .13 * length(pose.xy) - .015 * abs(pose.z);
}

float RotationScore(Varyings input) : SV_Target
{
    uint2 cell = (uint2)input.pos.xy;
    bool baseline = cell.x % 64u == 63u;
    if (baseline && cell.y % 9u != 0) return -2;
    float2 center = (float2(cell.x / 64u, cell.y / 9u) + .5) * _RotationGrid.zw;
    float3 pose = baseline ? 0 : RotationPose(cell.x % 64u, cell.y % 9u);
    return RotationAppearance(center, pose, 0, baseline);
}

// Check the path in both images. A strong edge in either endpoint weakens the link.
float RotationLink(float2 center, float2 q, float3 pose)
{
    float2 start = _AnchorCount > 0 ? LiaoInherited(center) : 0;
    float2 last = RotationSignal(center, .5 * (start + pose.xy));
    float peak = 0;
    [unroll] for (int tap = 1; tap <= 4; tap++)
    {
        float2 p = lerp(center, q, tap * .25);
        float2 base = _AnchorCount > 0 ? LiaoInherited(p) : 0;
        float2 next = RotationSignal(p, .5 * (base + pose.xy + RotationDelta(p - center, pose.z)));
        float2 change = abs(next - last);
        peak = max(peak, max(change.x, change.y));
        last = next;
    }
    return rcp(1 + 180 * peak * peak);
}

float RotationGuideWeight(float2 center, float3 pose)
{
    if (_AnchorCount == 0) return 1;
    float2 metric = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    float agreement = 0, total = 0;
    // Measure how the proposed local rotation changes error on nearby manual pairs.
    [loop] for (int i = 0; i < _AnchorCount; i++)
    {
        float4 pin = _AnchorTable.Load(int3((uint)i % (uint)_AnchorWidth, (uint)i / (uint)_AnchorWidth, 0));
        float2 midpoint = .5 * (pin.xy + pin.zw);
        float2 delta = midpoint - center;
        float2 distance = delta * metric;
        float weight = exp(-dot(distance, distance) / .05);
        float2 oldError = (LiaoInherited(midpoint) - (pin.zw - pin.xy)) * metric;
        float2 newError = oldError + (pose.xy + RotationDelta(delta, pose.z)) * metric;
        agreement += weight * (length(oldError) - length(newError));
        total += weight;
    }
    return total > 0 ? lerp(1, smoothstep(-.05, .01, agreement / total), saturate(total)) : 1;
}

bool RotationOuterOffset(uint x, uint y)
{
    return x % 9u == 0u || x % 9u == 8u || y == 0u || y == 8u;
}

float RotationConfidence(float score, float inherited)
{
    return smoothstep(.55, .9, score) * smoothstep(.08, .35, score - inherited);
}

float4 RotationBest(Varyings input) : SV_Target
{
    int2 origin = (int2)input.pos.xy * int2(64, 9);
    float inherited = _MainTex.Load(int3(origin + int2(63, 0), 0)).x;
    float best = inherited;
    uint2 winner = uint2(31, 4);
    [loop] for (uint y = 0; y < 9; y++)
    [loop] for (uint x = 0; x < 63; x++)
    {
        if (_AnchorCount == 0 && RotationOuterOffset(x, y)) continue;
        float score = _MainTex.Load(int3(origin + int2(x, y), 0)).x;
        if (score > best) { best = score; winner = uint2(x, y); }
    }
    float3 pose = RotationPose(winner.x, winner.y);
    float confidence = RotationConfidence(best, inherited);
    if (confidence <= 0) return 0;
    return float4(pose, confidence * RotationGuideWeight(input.pos.xy * _RotationGrid.zw, pose));
}

static const int2 RotationOffsets[8] = { int2(-1, 0), int2(1, 0), int2(0, -1), int2(0, 1),
                                         int2(-1, -1), int2(1, -1), int2(-1, 1), int2(1, 1) };

float RotationAgreement(float3 pose, float4 neighbours[8], float2 metric)
{
    float sum = 0, strongest = 0, weight = 0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float2 offset = RotationOffsets[i] * _RotationGrid.zw;
        float2 error = (pose.xy + RotationDelta(offset, pose.z) - neighbours[i].xy) * metric;
        float turn = pose.z - neighbours[i].z;
        float vote = neighbours[i].w * exp(-dot(error, error) / .012 - .18 * turn * turn);
        strongest = max(strongest, vote);
        sum += vote;
        weight += neighbours[i].w;
    }
    // A patch should not confirm its own movement solely through the neighbour that proposed it.
    return (sum - .75 * strongest) / max(.8, weight - .75 * strongest);
}

// Transport each neighbour's rotation to this patch center before comparing
// displacement. Transported candidates can reach beyond the local search grid.
float4 RotationConsensus(Varyings input) : SV_Target
{
    int2 region = (int2)input.pos.xy;
    int2 origin = region * int2(64, 9);
    float2 center = input.pos.xy * _RotationGrid.zw;
    float2 metric = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    float4 neighbours[8];

    [unroll] for (int i = 0; i < 8; i++)
    {
        int2 other = region + RotationOffsets[i];
        neighbours[i] = 0;
        if (any(other < 0) || any(other >= (int2)_RotationGrid.xy)) continue;
        float4 match = _MainTex.Load(int3(other, 0));
        if (match.w <= 0) continue;
        float2 q = (other + .5) * _RotationGrid.zw;
        float affinity = RotationLink(q, center, match.xyz);
        neighbours[i] = float4(match.xyz, match.w * affinity / dot(float2(RotationOffsets[i]), float2(RotationOffsets[i])));
    }
    float inherited = _RotationScores.Load(int3(origin + int2(63, 0), 0)).x;
    float bestScore = inherited;
    float3 bestPose = 0;
    float bestSupport = 0;
    float bestMerit = inherited;
    [loop] for (uint y = 0; y < 9; y++)
    [loop] for (uint x = 0; x < 63; x++)
    {
        float score = _RotationScores.Load(int3(origin + int2(x, y), 0)).x;
        if (score + .32 <= bestMerit) continue;
        float3 pose = RotationPose(x, y);
        float support = RotationAgreement(pose, neighbours, metric);
        if (_AnchorCount == 0 && RotationOuterOffset(x, y) && support < .22) continue;
        float merit = score + .32 * support;
        if (merit > bestMerit)
        {
            bestScore = score;
            bestPose = pose;
            bestSupport = support;
            bestMerit = merit;
        }
    }
    [loop] for (int j = 0; j < 8; j++)
    {
        if (neighbours[j].w <= .01) continue;
        float2 offset = RotationOffsets[j] * _RotationGrid.zw;
        float spin = neighbours[j].z;
        float3 pose = float3(neighbours[j].xy - RotationDelta(offset, spin), spin);
        float support = RotationAgreement(pose, neighbours, metric);
        float score = RotationAppearance(center, pose, 0, false);
        float merit = score + .32 * support;
        if (merit > bestMerit)
        {
            bestScore = score;
            bestPose = pose;
            bestSupport = support;
            bestMerit = merit;
        }
    }
    if (bestMerit <= inherited) return 0;
    float confidence = smoothstep(.55, .9, bestScore + .12 * bestSupport) * smoothstep(.08, .35, bestMerit - inherited);
    if (confidence <= 0) return 0;
    return float4(bestPose, confidence * RotationGuideWeight(center, bestPose));
}

struct RotationFit
{
    float mass, spread;
    float2 position, velocity, covariance, prediction;
};

void RotationObserve(inout RotationFit fit, float weight, float2 delta, float2 velocity, float2 prediction)
{
    fit.position += weight * delta;
    fit.velocity += weight * velocity;
    fit.covariance += weight * float2(dot(delta, velocity), delta.x * velocity.y - delta.y * velocity.x);
    fit.spread += weight * dot(delta, delta);
    fit.prediction += weight * prediction;
    fit.mass += weight;
}

float4 RotationPrior(Varyings input) : SV_Target
{
    float2 p = input.pos.xy * _Level.zw;
    float2 inherited = _AnchorCount == 0 ? LiaoInherited(p) : 0;
    RotationFit fit = (RotationFit)0;
    float3 original = 0;
    float initialMass = 0;
    float2 metric = _RasterSize / min(_RasterSize.x, _RasterSize.y);
    [loop] for (int y = 0; y < (int)_RotationGrid.y; y++)
    {
        [loop] for (int x = 0; x < (int)_RotationGrid.x; x++)
        {
            float4 match = _MainTex.Load(int3(x, y, 0));
            float2 center = (float2(x, y) + .5) * _RotationGrid.zw;
            float2 delta = (center - p) * metric;
            if (dot(delta, delta) > .18) continue;
            float4 local = _RotationIndependent.Load(int3(x, y, 0));
            float localWeight = local.w * exp(-dot(delta, delta) / .018);
            original += localWeight * float3(local.xy + RotationDelta(p - center, local.z) - inherited, local.z);
            initialMass += localWeight;
            if (match.w <= 0) continue;
            float weight = match.w * exp(-dot(delta, delta) / .036)
                * RotationLink(center, p, match.xyz);
            RotationObserve(fit, weight, delta, match.xy * metric, match.xy + RotationDelta(p - center, match.z));
        }
    }
    // A manual pair also supplies an observed velocity on this surface. Use the
    // residual to the guide frame, since the coarse relaxation already inherits it.
    [loop] for (int pinIndex = 0; pinIndex < _AnchorCount; pinIndex++)
    {
        float4 pin = _AnchorTable.Load(int3((uint)pinIndex % (uint)_AnchorWidth, (uint)pinIndex / (uint)_AnchorWidth, 0));
        float2 center = .5 * (pin.xy + pin.zw);
        float2 delta = (center - p) * metric;
        float distanceSq = dot(delta, delta);
        if (distanceSq > .18) continue;
        float2 residual = pin.zw - pin.xy - LiaoInherited(center);
        float weight = 2 * exp(-distanceSq / .036) * RotationLink(center, p, float3(residual, 0));
        RotationObserve(fit, weight, delta, residual * metric, residual);
    }
    original /= max(1, initialMass);
    if (fit.mass <= 0) return float4(original.xy, 0, 0);
    // Weighted least-squares similarity fit of the observed displacement at p.
    float2 mean = fit.position / fit.mass;
    float2 velocity = fit.velocity / fit.mass;
    float variance = max(0, fit.spread / fit.mass - dot(mean, mean));
    float2 crossMoment = float2(dot(mean, velocity), mean.x * velocity.y - mean.y * velocity.x);
    float2 coeff = (fit.covariance / fit.mass - crossMoment) / max(.001, variance);
    float2 estimate = (velocity - coeff.x * mean - coeff.y * float2(-mean.y, mean.x)) / metric;
    float2 movement = lerp(fit.prediction / fit.mass, estimate, .75 * smoothstep(.002, .014, variance));
    float2 correction = (movement - inherited) * saturate(fit.mass / .45);
    float oldScore = RotationAppearance(p, float3(inherited + original.xy, original.z), 0, false);
    float newScore = RotationAppearance(p, float3(inherited + correction, coeff.y), clamp(coeff.x, -.8, .8), false);
    float trust = smoothstep(-.18, .02, newScore - oldScore);
    return float4(lerp(original.xy, correction, trust), 0, 0);
}
