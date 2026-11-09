#ifndef WHIMTEX_WT_NOISE_CAUSTICS_TYPES_INCLUDED
#define WHIMTEX_WT_NOISE_CAUSTICS_TYPES_INCLUDED
struct WTNoiseCausticsSettings
{
    float strength;
    float evolution;
};
WTNoiseCausticsSettings WTNoiseCausticsDefaults()
{
    WTNoiseCausticsSettings s;
    s.strength=0.0625; s.evolution=0;
    return s;
}
#endif
