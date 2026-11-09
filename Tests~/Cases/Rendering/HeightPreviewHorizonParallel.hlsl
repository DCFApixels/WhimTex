// Appended to the same owned compute shader; prefix maxima preserve the front-to-back horizon.
// One group owns one screen column, with 64 depth samples processed in parallel.
groupshared float projected[64];
groupshared float projectedMin[64];
groupshared float carryHorizon;
groupshared float carryLower;

[numthreads(64, 1, 1)]
void RenderHorizonParallel(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    int x = group.x;
    for (int y = lane; y < _OutputSize; y += 64) _Output[int2(x, y)] = 0;
    GroupMemoryBarrierWithGroupSync();
    float u = (((x + .5) / _OutputSize) - .5) / .84 + .5;
    if (u < 0 || u > 1) return;
    if (lane == 0) { carryHorizon = ProjectY(0, HeightAt(float2(u, 0))) * _OutputSize; carryLower = carryHorizon; }
    GroupMemoryBarrierWithGroupSync();
    for (int chunk = 0; chunk < _Steps; chunk += 64)
    {
        int i = chunk + lane;
        float v0 = (float)i / _Steps, v1 = (float)(i + 1) / _Steps;
        float h0 = HeightAt(float2(u, v0)), h1 = HeightAt(float2(u, v1));
        float y0 = ProjectY(v0, h0) * _OutputSize;
        float y1 = ProjectY(v1, h1) * _OutputSize;
        float prior = carryHorizon;
        float priorLower = carryLower;
        projected[lane] = max(y1, prior);
        projectedMin[lane] = min(y1, priorLower);
        GroupMemoryBarrierWithGroupSync();
        // Inclusive maximum scan. Shared-memory fences are portable across Unity compute backends.
        for (uint offset = 1; offset < 64; offset *= 2)
        {
            float other = lane >= offset ? projected[lane - offset] : -1e20;
            float otherMin = lane >= offset ? projectedMin[lane - offset] : 1e20;
            GroupMemoryBarrierWithGroupSync();
            projected[lane] = max(projected[lane], other);
            projectedMin[lane] = min(projectedMin[lane], otherMin);
            GroupMemoryBarrierWithGroupSync();
        }
        float previousMaximum = lane == 0 ? prior : projected[lane - 1];
        float previousMinimum = lane == 0 ? priorLower : projectedMin[lane - 1];
        int begin = clamp((int)ceil((y1 > y0 ? max(y0, previousMaximum) : y1) - .5), 0, _OutputSize);
        int end = clamp((int)ceil((y1 > y0 ? y1 : min(y0, previousMinimum)) - .5), 0, _OutputSize);
        if (y1 != y0 && end > begin)
            for (int y = begin; y < end; ++y)
            {
                float fraction = saturate((y + .5 - y0) / (y1 - y0));
                float4 value = Surface(float2(u, lerp(v0, v1, fraction))); value.r = lerp(h0, h1, fraction);
                _Output[int2(x, y)] = value;
            }
        GroupMemoryBarrierWithGroupSync();
        if (lane == 0) { carryHorizon = projected[63]; carryLower = projectedMin[63]; }
        GroupMemoryBarrierWithGroupSync();
        if (carryHorizon >= _OutputSize && carryLower <= 0) break;
    }
}
