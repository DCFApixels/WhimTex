// Procedural erosion built from WhimTex's existing Perlin and Gabor kernels.
// The slope guide steers progressively finer detail; it is not a simulation
// of water transport and is not the exact derivative of the final height.

float ErosionNoiseDetail(float2 position, float2 period, float2 baseSlope, int seed,
    float detail, float slopeStrength, float branching, int octaves,
    float lacunarity, float gain)
{
    WTNoiseSettings settings = WTNoiseDefaults(seed);
    settings.period = float3(period, 0.0);
    WTNoiseGaborSettings carrier = WTNoiseGaborDefaults();
    carrier.anisotropy = 1.0;
    float frequency = max(abs(detail), 0.001);
    float amplitude = 1.0;
    float totalWeight = 0.0;
    float height = 0.0;
    float2 detailSlope = 0.0;
    float slopeMask = smoothstep(0.02, 0.3, length(baseSlope));

    [loop] for (int erosionOctave = 0; erosionOctave < octaves; erosionOctave++)
    {
        float2 guide = baseSlope + max(branching, 0.0) * detailSlope;
        // Carrier direction is across the slope, so its ridges run downhill.
        carrier.direction = float3(guide.y, -guide.x, 0.0);
        carrier.frequency = 0.5 + length(guide) * max(slopeStrength, 0.0);
        settings.scale = float3(frequency, frequency, 1.0);
        settings.seed = WTNoiseInternalSeed(seed, erosionOctave);
        WTNoiseGaborSample sample;
        float3 gradient;
        float value = WTNoiseInternalGabor(float3(position, 0.0), settings,
            carrier, 2, sample, gradient);
        height += amplitude * (value * 2.0 - 1.0);
        // Partial derivatives hold carrier settings constant. Only use these
        // as a directional guide, never as the output surface normal.
        detailSlope += gradient.xy * (2.0 * amplitude / max(abs(detail), 0.001));
        totalWeight += amplitude;
        amplitude *= saturate(gain);
        frequency *= max(abs(lacunarity), 1.0);
    }
    return totalWeight > 0.0 ? height * slopeMask / totalWeight : 0.0;
}

// @whimtex-node Procedural/Noise/Erosion Noise
// @param float2 UV
// @param float2 Scale = (3, 3)
// @param float Seed = 1337
// @param float Strength = 0.15
// @param float Detail = 4
// @param float SlopeStrength = 2
// @param float Branching = 0.1
// @param float Octaves = 5
// @param float Lacunarity = 2
// @param float Gain = 0.45
// @param toggle(bool) Periodic = false
// @if Periodic == true
// @param float2 Period = (1, 1)
// @endif
void WhimTexNodeErosionNoise(float2 UV, float2 Scale, float Seed, float Strength,
    float Detail, float SlopeStrength, float Branching, float Octaves,
    float Lacunarity, float Gain, bool Periodic, float2 Period,
    out float Value, out float Base, out float Erosion)
{
    float2 position = UV * Scale;
    // Period is in incoming UV units, before Scale. WT fits every octave's
    // lattice to this common tile, including fractional Detail/Lacunarity.
    // Periodic basis slopes also make the entire feedback chain periodic.
    float2 period = Periodic ? max(Period, 0.0) * abs(Scale) : float2(0.0, 0.0);
    int seed = (int)Seed;
    WTNoiseSettings settings = WTNoiseDefaults(seed);
    settings.period = float3(period, 0.0);
    Base = 0.0;
    float2 baseSlope = 0.0;
    float frequency = 1.0;
    float amplitude = 1.0;
    float weight = 0.0;
    [loop] for (int terrainOctave = 0; terrainOctave < 3; terrainOctave++)
    {
        settings.seed = WTNoiseInternalSeed(seed, terrainOctave);
        settings.scale = float3(frequency, frequency, 1.0);
        float3 gradient;
        Base += amplitude * WTNoiseInternalBasisSampleSingle(float3(position, 0.0),
            settings, 2, WT_NOISE_PERLIN, gradient);
        baseSlope += amplitude * gradient.xy;
        weight += amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    Base /= weight;
    baseSlope /= weight;
    Erosion = 0.0;
    int octaves = (int)clamp(floor(Octaves), 0.0, 8.0);
    if (Strength != 0.0 && octaves > 0)
        Erosion = Strength * ErosionNoiseDetail(position, period, baseSlope,
            WTNoiseInternalSeed(seed, 3), Detail, SlopeStrength, Branching,
            octaves, Lacunarity, Gain);
    Value = Base + Erosion;
}
