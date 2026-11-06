#ifndef WHIMTEX_WT_NOISE_SHARD_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_SHARD_TYPES_INCLUDED

struct WTNoiseShardSettings
{
    // Artist control, clamped to 0..1: soft slopes to sharp facets.
    float sharpness;
};
WTNoiseShardSettings WTNoiseShardDefaults()
{
    WTNoiseShardSettings s; s.sharpness=0.7; return s;
}

#endif
