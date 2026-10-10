#ifndef WHIMTEX_NOISE_VECTOR_INCLUDED
#define WHIMTEX_NOISE_VECTOR_INCLUDED

float CellMetric(float3 v)
{
    float squared = dot(v, v);
    float manhattan = dot(abs(v), float3(1, 1, 1));
    return _NoiseCellularDistance == 2 ? manhattan
        : _NoiseCellularDistance == 3 ? squared + manhattan : squared;
}

float3 CellDirection(float2 uv)
{
    #if defined(WT_NOISE_PERIODIC)
        float3 displacement = PeriodicWarp(uv, 0);
        if ((_NoisePeriodic & 1) != 0) uv.x = frac(uv.x);
        if ((_NoisePeriodic & 2) != 0) uv.y = frac(uv.y);
        float3 p = WtPosition(0, uv, displacement, WT_NOISE_LATTICE_LAYOUT);
    #else
        float3 p = WarpPosition(NoisePosition(uv, 0));
    #endif
    int3 center = int3(_fnlFastRound(p.x), _fnlFastRound(p.y), _fnlFastRound(p.z));
    float best = 1e10;
    float3 nearest = 0;
    #if defined(WT_NOISE_3D)
        float jitter = .39614353 * _NoiseCellularJitter;
        [loop] for (int z = center.z - 1; z <= center.z + 1; z++)
    #else
        float jitter = .43701595 * _NoiseCellularJitter;
        int z = 0;
    #endif
    [loop] for (int y = center.y - 1; y <= center.y + 1; y++)
    [loop] for (int x = center.x - 1; x <= center.x + 1; x++)
    {
        #if defined(WT_NOISE_3D)
            int hash = _fnlHash3D(_NoiseSeed, x * PRIME_X, y * PRIME_Y, z * PRIME_Z);
            int index = hash & (255 << 2);
            float3 v = float3(x, y, z) - p
                + float3(RAND_VECS_3D[index], RAND_VECS_3D[index | 1], RAND_VECS_3D[index | 2]) * jitter;
        #else
            int hash = _fnlHash2D(_NoiseSeed, x * PRIME_X, y * PRIME_Y);
            int index = hash & (255 << 1);
            float3 v = float3(float2(x, y) - p.xy
                + float2(RAND_VECS_2D[index], RAND_VECS_2D[index | 1]) * jitter, 0);
        #endif
        float distance = CellMetric(v);
        if (distance < best) { best = distance; nearest = v; }
    }
    // Direction is measured in the warped lattice, not an inverse warp solution.
    return nearest;
}

float3 VectorField(fnl_state state, float2 uv)
{
    #if WHIMTEX_NOISE_TYPE == 2
    return CellDirection(uv);
    #else
    // One common change of units keeps Strength useful as Scale changes, and
    // preserves Curl identities. Axis ratios, warp and all octaves stay inside Potential.
    float baseScale = max(_NoiseScale.x, _NoiseScale.y);
    #if defined(WT_NOISE_3D)
        baseScale = max(baseScale, _NoiseScale.z);
    #endif
    float frequency = _NoiseFractal == 0 ? 1 : pow(_NoiseFractalSettings.x, _NoiseOctaves - 1);
    float h = max(.001 / min(frequency, 64), baseScale * max(_NoiseDomain.x, _NoiseDomain.y) * 2e-7);
    float3 steps = float3(h / (baseScale * _NoiseDomain.xy), h * _NoiseScale.z / baseScale);
    #if defined(WT_NOISE_3D)
        bool curl = _NoiseField == 1;
        uint count = curl ? 12u : 6u;
    #else
        bool curl = false;
        uint count = 4u;
    #endif
    float3 result = 0;
    float positiveSample = 0;
    [loop] for (uint sampleIndex = 0u; sampleIndex < count; sampleIndex++)
    {
        uint component = curl ? sampleIndex >> 2 : 0u;
        uint axis = curl ? (sampleIndex & 3u) >> 1 : sampleIndex >> 1;
        if (curl && axis >= component) axis++;
        float sign = (sampleIndex & 1u) == 0u ? 1 : -1;
        float3 step = float3(axis == 0u, axis == 1u, axis == 2u) * steps * sign;
        fnl_state potential = state;
        if (component == 1) potential.seed ^= 0x68bc21eb;
        if (component == 2) potential.seed ^= 0x63d83595;
        float sampleValue = Potential(potential, uv + step.xy, step.z);
        if ((sampleIndex & 1u) == 0u) { positiveSample = sampleValue; continue; }
        float value = (positiveSample - sampleValue) / (2 * h);
        uint outputAxis = curl ? 3u - component - axis : axis;
        if (curl)
        {
            bool positive = axis == 0u ? component == 1u : axis == 1u ? component == 2u : component == 0u;
            if (!positive) value = -value;
        }
        result += float3(outputAxis == 0u, outputAxis == 1u, outputAxis == 2u) * value;
    }
    #if !defined(WT_NOISE_3D)
        if (_NoiseField == 1) result = float3(result.y, -result.x, 0);
    #endif
    return result;
    #endif
}

#endif
