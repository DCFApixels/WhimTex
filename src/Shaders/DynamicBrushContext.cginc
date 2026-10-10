struct DynamicBrushContext
{
    uint seed;
    uint strokeIndex;
    uint stampIndex;
    float distance;
    float totalDistance;
    float2 deltaPixels;
};

uint _WhimTex_BrushHash(uint value)
{
    value = (value ^ (value >> 16)) * 0x7feb352du;
    value = (value ^ (value >> 15)) * 0x846ca68bu;
    return value ^ (value >> 16);
}

float BrushRandom(uint seed, uint index)
{
    return (_WhimTex_BrushHash(seed ^ _WhimTex_BrushHash(index + 0x9e3779b9u)) >> 8) * (1.0 / 16777216.0);
}

float BrushRandom(uint seed, uint strokeIndex, uint stampIndex)
{
    return BrushRandom(_WhimTex_BrushHash(seed ^ _WhimTex_BrushHash(strokeIndex + 0x85ebca6bu)), stampIndex);
}
