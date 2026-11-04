---
title: "WT noise library"
parent: "Technical reference"
nav_order: 17
lang: en
permalink: /reference/noise-library/
search_exclude: true
---

# WT noise library

WhimTex automatically includes `WTNoise.hlsl` in Shader-layer Code, HLSL FX and HLSL
brush tips. Use the public `WTNoise*` and `WT_NOISE_*` names; do not copy
the library into a script. FastNoiseLite supplies some kernels behind this API.
Shader Graph uses this library through [19 dedicated noise kinds](ShaderLayer.md#wt-noise-nodes),
including analytic outputs, warp and grain. Noise Layer retains its separate implementation;
matching a type name and Seed does not guarantee matching pixels with that layer.
Ordinary graph noise nodes evaluate a single WT level; the graph's separate
[Fractal node](ShaderLayer.md#buffer-and-fractal-nodes) repeats an arbitrary branch in 2D
and is not exact WT per-noise fractal evaluation. For a custom HLSL source, use
[`WT_DEFINE_FRACTAL`](#fractals-of-custom-functions); built-in noise settings and behavior remain unchanged.


## Common settings and scalar calls

```hlsl
// @param seed _Seed = 1337
// @param float _Scale = 8 [0.1 .. 64]

float4 ApplyFX(PinnedUV uv, float4 color)
{
    WTNoiseSettings n = WTNoiseDefaults(_Seed);
    n.scale = _Scale.xxx;
    n.period = float3(1, 1, 0);

    n.fractal.mode = WT_NOISE_FBM;
    n.fractal.octaves = 4;

    float value = WTNoisePerlin(float2(uv.x, uv.y), n);
    return float4(value.xxx, color.a);
}
```

Positions select dimension: `float` for 1D, `float2` for 2D, `float3` for 3D.
Triangle Voronoi is restricted to `float2`.
`PinnedUV` is not one of these numeric types; construct a numeric position from its components.
The following functions return normalized scalar values in `0..1`, without color-space
conversion. There is no additional `value * 0.5 + 0.5` step.

| Function | Optional settings after `position, noise` |
| --- | --- |
| `WTNoisePerlin` | None |
| `WTNoiseValue` | None |
| `WTNoiseValueCubic` | None |
| `WTNoiseOpenSimplex2` | None |
| `WTNoiseOpenSimplex2S` | None |
| `WTNoiseFlow` | `WTNoiseFlowSettings` |
| `WTNoiseCellular` | `WTNoiseCellularSettings` |
| `WTNoiseVoronoiEdge` | `WTNoiseVoronoiSettings` |
| `WTNoiseTriangleVoronoi` (2D only) | `WTNoiseTriangleVoronoiSettings` |
| `WTNoiseVoronoise` | `WTNoiseVoronoiseSettings` |
| `WTNoiseGabor` | `WTNoiseGaborSettings` |
| `WTNoiseSpots` | `WTNoiseSpotSettings` |
| `WTNoiseShard` | `WTNoiseShardSettings` |
| `WTNoiseWavelet` | `WTNoiseWaveletSettings` |

All except Triangle Voronoi provide 1D/2D/3D overloads; Triangle Voronoi accepts only `float2`.
Fractals live in `noise.fractal`; defaults evaluate one level.
Omitted algorithm settings use that algorithm's defaults. For example,
`WTNoiseCellular(p, n, c)` uses the same call whether `n.fractal.mode` is None or FBm.
Cellular, Triangle Voronoi and Gabor also accept a trailing `out` result for first-octave data; the scalar
return still includes all requested octaves. Other scalar calls need no auxiliary result.
Perlin, Value and Value Cubic have dedicated 1D kernels. OpenSimplex 1D is a fixed slice
of its 2D kernel; it is not a separate simplex family.

WT ValueCubic 2D/3D corrects the `+2` lattice-neighbor offsets on shader compilers that
saturate overflowing signed constant products. Those WT images can change from earlier
results; do not assume pixel identity. The non-WT FNL path retains its original arithmetic.

| `WTNoiseSettings` field | Default | Meaning |
| --- | --- | --- |
| `int seed` | 1337 | Repeatable integer seed; `WTNoiseDefaults(seed)` overrides it. |
| `float3 scale` | `(1,1,1)` | Positive noise density per input axis. Magnitudes are used; zero is floored to `0.00001`. Negate the position explicitly to reflect it. |
| `float3 offset` | `(0,0,0)` | Translation in input-position units, before scale. |
| `float3 period` | `(0,0,0)` | Repeat length in input-position units; nonpositive components disable that axis. |
| `float3 dpdx`, `float3 dpdy` | `(0,0,0)` | Explicit changes in the unwrapped input position across one screen pixel in X/Y, before WT offset, scale and period. Both zero select point sampling. |
| `WTNoiseFractal fractal` | None, settings below | Shared octave configuration, initialized by `WTNoiseDefaults`. |
| `float3 scaleLow` | `(0,0,0)` | Rounding residual paired with `scale`; normally supplied by the CPU domain helper. |
| `float3 offsetLow` | `(0,0,0)` | Rounding residual paired with `offset`. |
| `float3 periodLow` | `(0,0,0)` | Rounding residual paired with `period`. |

Only X is used in 1D, XY in 2D. Without periodicity the coordinate rule is
`(position + offset) * abs(scale)`, subject to the minimum scale above.
An octave multiplies density by its frequency. Built-in noise calls fit an integer number
of lattice cells into the requested input-space repeat length on each periodic axis and octave.

| `WTNoiseFractal` field | Default | Accepted calculation range |
| --- | --- | --- |
| `int mode` | `WT_NOISE_NONE` | `WT_NOISE_NONE`, `WT_NOISE_FBM`, `WT_NOISE_RIDGED`, `WT_NOISE_PINGPONG` |
| `int octaves` | 3 | Clamped to 1–8; None evaluates one level. |
| `float lacunarity` | 2 | Density multiplier per octave, clamped to 0.01–16. |
| `float gain` | 0.5 | Amplitude multiplier, clamped to 0–1. |
| `float weightedStrength` | 0 | Value-dependent amplitude weighting, clamped to 0–1. |
| `float pingPongStrength` | 2 | Nonnegative fold strength, used only by PingPong. |

Each octave uses an incremented seed and normalized amplitude. Ridged folds the signal
around its midpoint; PingPong repeatedly folds it. Normalization is fixed by the calculation,
not measured from an image, and does not imply that every seed reaches both 0 and 1.

### Shared sampling footprint

Set the footprint on `WTNoiseSettings`, including for Gabor:

```hlsl
// p is the unwrapped float2 input position in a fragment context.
n.dpdx = float3(ddx(p), 0);
n.dpdy = float3(ddy(p), 0);
float value = WTNoisePerlin(p, n);
```

The library never calls `ddx`/`ddy` implicitly. In other contexts supply explicit sampling
steps, or leave both fields zero. Only X/XY/XYZ is used for 1D/2D/3D. Do not difference
wrapped positions: a seam jump is not the pixel's footprint. Each octave transforms these
input-space steps using its actual fitted scale, including high/low domain preparation.
All `WTNoiseDefaults` overloads initialize the footprints to zero; the CPU domain binding
still uploads only its six scale/offset/period vectors.

Basis noise and Flow attenuate unresolved octaves toward a neutral 0.5 after fractal folding.
Their visibility fades from full to zero as the largest singular value of the scaled
footprint grows from 0.25 to 1 lattice unit. This accounts for diagonal/sheared footprints,
but is a bounded approximation, not exact integration of the field. Other families use
the same footprint units with the filtering rules described below; displacement attenuates
each octave toward zero.

Settings and footprints are assumed constant over the sampled pixel. Spatial variation of
seed, scale, offset, period, phase or family/fractal settings is not integrated. In particular,
filtered octaves do not exactly integrate nonlinear Ridged/PingPong folds or value-weighted
amplitudes; some aliasing and changes in contrast can remain.

**Source update:** move former `g.dpdx`/`g.dpdy` assignments to `n.dpdx`/`n.dpdy`.
`WTNoiseGaborSettings` no longer contains these fields; there are no compatibility aliases.

## Shard noise

`WTNoiseShard(position, noise [, shard])` returns a faceted `0..1` field. Each cell
contributes a randomly oriented ramp, shaped with a soft sign and blended with normalized
Gaussian weights. The Gaussian tails fade to zero at the finite search boundary to keep
cell transitions continuous. This is an independent implementation of the shard-field
idea described by [ENDEsGA](https://www.shadertoy.com/view/dlKyWw), using WT integer hashing
and native 1D/2D/3D lattices; 1D/2D are not slices of the 3D result.

`WTNoiseShardSettings` from `WTNoiseShardDefaults()` contains `sharpness=0.7`, clamped
to `0..1`. Zero gives soft slopes; one gives sharp facets. Internally the ramp steepness
is `exp2(8 * sharpness * sharpness)`. Seed, scale, offset, per-axis period, high/low domain
preparation and `noise.fractal` follow the common rules. No Folds option is embedded.

```hlsl
WTNoiseSettings n = WTNoiseDefaults(1337);
n.scale = float3(8, 8, 8);
n.period = float3(1, 1, 1);
WTNoiseShardSettings shard = WTNoiseShardDefaults();
shard.sharpness = 0.7;
float value = WTNoiseShard(position, n, shard);
```

Use `float`, `float2` or `float3` for `position`; animate the third component for a moving
3D slice. Supplied footprints broaden unresolved sharp transitions and fade unresolved
octaves toward 0.5. Filtering is approximate, assumes constant settings across the pixel
and does not exactly integrate nonlinear fractal folds. Zero footprints preserve point
sampling. Work is bounded to 5/25/125 candidate cells per octave in 1D/2D/3D; there are
no intermediate textures or derivative outputs. In Shader Graph, connect **Shard Noise**
to the existing **Fractal** node for octave detail.

## Wavelet noise

`WTNoiseWavelet(position, noise [, wavelet])` returns a normalized `0..1` scalar field.
Its point kernel combines two staggered grids of sinusoids with compact conical envelopes
and random orientations. The kernel is generalized to `float`, `float2` and `float3`
positions for 1D/2D/3D sampling.

`WTNoiseWaveletSettings` from `WTNoiseWaveletDefaults()` contains `frequency=2` and
`phase=0`. Frequency uses its absolute value and controls the sinusoidal detail within
each envelope. Phase is in cycles and wraps every cycle: 0 and 1 give the same pattern.
Seed, scale, offset, per-axis period, high/low domain preparation and explicit footprints
use the common noise settings.

```hlsl
WTNoiseSettings n = WTNoiseDefaults(1337);
n.scale = float3(8, 8, 8);
n.period = float3(1, 1, 1);
WTNoiseWaveletSettings w = WTNoiseWaveletDefaults();
w.frequency = 2;
w.phase = 0;
float value = WTNoiseWavelet(position, n, w);
```

The point kernel uses envelopes `max(1 - 2*length(local), 0)` on two grids shifted by
half a cell on every active axis. Each grid contributes a nonnegative sinusoid inside
its envelope. Orientations are random signs in 1D, directions on the circle in 2D and
directions on the sphere in 3D. Zero footprints leave this point sampling unchanged.

Filtering applies approximate Gaussian carrier attenuation along each sinusoid's
orientation, then the shared octave fade toward a family mean. For Phase=0 and no
fractal folding, the mean is `0.5` in 1D, `pi/12` in 2D and `pi/24` in 3D; a fully
minified 2D/3D field therefore need not approach 0.5. Phase=0.5 uses the same mean.
With None/FBm and Frequency=0, the phase-dependent mean is that envelope mean times
`1 + sin(2*pi*frac(phase))`. Other phases and nonlinear folds use a deterministic
32-sample ensemble estimate, independent of the requested seed and period. This is
an approximation to minification, not an exact tile or pixel integral. The compact
envelope is not strictly band-limited; filtering does not guarantee alias-free output.

HLSL uses the shared None/FBm/Ridged/PingPong modes in `noise.fractal`. No
additive-frequency fractal modes are added; these common modes do not promise exact
equivalence to a reference algorithm's additive frequency sequence. Graph `WaveletNoise`
evaluates one octave, with Frequency and Phase inputs and only a Float Value output.
Use the separate graph Fractal node for branch repetition. Wavelet has no auxiliary
sample or Gradient/Curl overloads.

## Fractals of custom functions

`WT_DEFINE_FRACTAL(Name, Source, PositionType, ValueType)` declares a function with this signature:

```hlsl
ValueType Name(PositionType position, WTNoiseSettings settings);
```

Define `ValueType Source(PositionType position, int seed)` before the macro, at global scope.
`PositionType` is `float`, `float2` or `float3`; `ValueType` is `float`, `float2`, `float3` or
`float4`, independently of position width. The macro is automatically available in
Shader-layer Code, HLSL FX, Static/Dynamic brush code and graph CustomFunction code.
The generated function evaluates the source repeatedly; it does not create a new graph node.

```hlsl
float3 DetailSource(float2 position, int seed)
{
    float phase = (float)seed * 0.01;
    return float3(sin(position.x + phase), cos(position.y - phase), 2.0);
}
WT_DEFINE_FRACTAL(DetailFractal, DetailSource, float2, float3)

float4 ApplyFX(PinnedUV uv, float4 color)
{
    float2 p = float2(uv.x, uv.y);
    WTNoiseSettings n = WTNoiseDefaults(1337);
    n.scale = float3(8, 8, 1);
    n.fractal.mode = WT_NOISE_FBM;
    n.dpdx = float3(ddx(p), 0);
    n.dpdy = float3(ddy(p), 0);
    return float4(DetailFractal(p, n), color.a);
}
```

The existing `settings.fractal` modes and bounds apply: None evaluates one octave; FBm,
Ridged and PingPong use the requested count, clamped to `1..8`. `WTNoiseDefaults` still
selects None, with Octaves=3, Lacunarity=2, Gain=0.5, WeightedStrength=0 and PingPongStrength=2.
Offset and Scale transform the input before octave frequency; the source seed starts at
`settings.seed` and increases by one each octave. Only X/XY/XYZ settings components are used
for 1D/2D/3D positions.

High/low coordinate preparation is retained up to the callback boundary, where Source
receives ordinary `float`/`float2`/`float3` coordinates. At very large coordinates, the
callback cannot guarantee the same local precision as built-in noise, which keeps lattice
indices separate from the fractional position.

Accumulation follows the graph Fractal node's normalized weighted mean, independently for
each output component. FBm retains negative and HDR values; the example's blue component
stays 2. This is not a remap into `0..1`, and no color-space conversion is performed.
Ridged applies `1-abs(2*v-1)`; PingPong applies
`1-abs(frac(v*max(pingPongStrength,0))*2-1)`. Gain and WeightedStrength control successive
amplitudes. A float4 alpha component participates in the same calculation as RGB.

Explicit `dpdx`/`dpdy` describe the unwrapped input-position footprint before Offset/Scale.
They attenuate octave contributions; both zero select point sampling. This generic octave
filter does not infer or integrate arbitrary detail inside Source. Period wraps coordinates
in input units on positive axes; nonpositive axes do not wrap. Repetition can leave a visible
jump at the seam: wrapping an arbitrary function does not make it smoothly seamless.
Analytic Gradient/Curl functions are not generated.

Built-in `WTNoise*` calls keep their family filtering, per-octave period fitting and supported
analytic derivatives. Applying this macro to a wrapper around a built-in noise call does not
reproduce that noise's native fractal. Graph Fractal shares the accumulation math while
keeping its existing 2D domain, settings, filtering and configurable SeedStep; the macro
always advances Source's seed by one and does not add a SeedStep field to WTNoiseSettings.

## Periodicity and precision

The lattice-fitting and precision rules here describe the built-in noise calls. The custom
function macro above uses coordinate wrapping and does not fit Source to a periodic lattice.

- `n.period = float3(1, 1, 0)` repeats over one input unit in X/Y, without repeating Z.
  Fractal octaves keep that same input-space period.
- Cell counts are rounded separately per octave with a minimum of one and no artificial
  upper density cap. Simplex uses layout-specific spacing. Actual density can differ from requested scale.
  Rounded counts retain their high/low parts until integer conversion, including counts above `2^24`.
  Animating scale or lacunarity can cross a cell-count boundary and change the pattern abruptly.
- Position-varying settings must themselves repeat across the seam. A spatially varying
  seed, offset, scale, period or algorithm setting can break periodicity. Arbitrary UV warps
  must preserve the period as well.
- Coordinates use compensated arithmetic, keeping integer lattice indices separate from
  local fractions. Large offsets retain more detail than ordinary float addition; use the
  CPU helper below when settings originate as doubles. Inputs and intermediates must remain
  finite and within the integer lattice range. Results are not guaranteed bit-identical across GPUs.
- Wrapping coordinates does not add antialiasing. Fine detail can alias when undersampled.

### CPU domain preparation and high/low values

`WTNoiseDomain(double3 scale, double3 offset, double3 period)` prepares six `Vector4`
values on the CPU: `Scale`, `ScaleLow`, `Offset`, `OffsetLow`, `Period`, `PeriodLow`.
For each component it stores `high = (float)value` and
`low = (float)(value - (double)high)`; W is zero. Before splitting, each offset with a
positive period is reduced using double-precision `%`. The period itself is unchanged;
zero and negative periods disable repetition. Inputs must be finite and the prepared
high parts must remain finite floats. A positive period that rounds to zero is rejected;
invalid values throw instead of being clamped.

Reuse a `WTNoiseDomainBinding` to cache the six property IDs. Construct the binding and
apply it on Unity's main thread; `Apply` uploads all six vectors without per-call managed
allocations on its successful path. A default-constructed binding or null/destroyed material
is rejected. This is a host-side C# API: the caller supplies the material used for rendering.

```csharp
using DCFApixels.WhimTex;
using Unity.Mathematics;
using UnityEngine;

public sealed class NoiseDomainUpload
{
    private readonly WTNoiseDomainBinding binding = new WTNoiseDomainBinding("_WTDomain");

    public void Apply(Material material, double3 offset)
    {
        var domain = new WTNoiseDomain(
            new double3(8.125, 8.125, 1.0),
            offset,
            new double3(1.0, 1.0, 0.0));
        binding.Apply(material, in domain);
    }
}
// Keep one uploader and pass offsets as doubles, e.g. new double3(1000000000.125, 0, 0).
```

Declare the six matching uniforms in the HLSL script; they are supplied by the C# caller,
not inspector parameters. The prefix and suffixes must match the binding exactly:

```hlsl
float4 _WTDomain_Scale;
float4 _WTDomain_ScaleLow;
float4 _WTDomain_Offset;
float4 _WTDomain_OffsetLow;
float4 _WTDomain_Period;
float4 _WTDomain_PeriodLow;

float4 ApplyFX(PinnedUV uv, float4 color)
{
    WTNoiseSettings n = WTNoiseDefaults(1337,
        _WTDomain_Scale.xyz, _WTDomain_ScaleLow.xyz,
        _WTDomain_Offset.xyz, _WTDomain_OffsetLow.xyz,
        _WTDomain_Period.xyz, _WTDomain_PeriodLow.xyz);
    float value = WTNoisePerlin(float2(uv.x, uv.y), n);
    return float4(value.xxx, color.a);
}
```

`WTNoiseDefaults(seed, scale, scaleLow, offset, offsetLow, period, periodLow)`
accepts six `float3` values in that order. The ordinary `WTNoiseDefaults()` and
`WTNoiseDefaults(seed)` initialize all three residuals to zero. If replacing a high part
after domain initialization, also replace its residual, or set that residual to zero for
an ordinary float value. Do not add high and low together before passing them to the library.

The library uses compensated coordinate arithmetic and separates the integer lattice origin
from the local fraction. This preserves more coordinate detail; it is **not full double
precision**, and does not promise bit-identical results to Noise Layer or across GPU backends.
Arbitrary HLSL is not moved to the CPU automatically. Keep the large offset in the settings,
separate from the local position `p`: calculating `p + largeOffset` in ordinary float first
can lose detail that neither this API nor conversion to double can recover.

The integer lattice still uses signed 32-bit indices, with additional arithmetic for transforms,
periods and neighbor offsets; all of those must fit, not only the initial cell count. In particular,
2D simplex uses doubled periods and sums/differences of indices; 3D simplex uses periods multiplied
by nine and integer basis projections. Hash multiplication intentionally wraps bits.
Finite uploaded values alone do not guarantee valid intermediate calculations at extreme scales.
Out-of-range or nonfinite input has no guaranteed result and does not produce an HLSL diagnostic;
it is not silently replaced by a capped density. Subnormal values can be flushed to zero by GPU
arithmetic; do not rely on such periods.

## Cellular and Voronoi geometry

`WTNoiseCellularDefaults()` returns Uniform distribution, Euclidean metric, F1 output,
jitter 1, smoothness 0.5 and evolution 0. `WTNoiseCellularSettings` has these fields:

| Setting | Choices |
| --- | --- |
| `distribution` | `WT_NOISE_CELL_UNIFORM`, `WT_NOISE_CELL_FIXED_RADIUS` |
| `metric` | `WT_NOISE_DISTANCE_EUCLIDEAN`, `WT_NOISE_DISTANCE_EUCLIDEAN_SQUARED`, `WT_NOISE_DISTANCE_MANHATTAN`, `WT_NOISE_DISTANCE_HYBRID` |
| `output` | `WT_NOISE_CELL_VALUE`, `WT_NOISE_CELL_F1`, `WT_NOISE_CELL_F2`, `WT_NOISE_CELL_ADD`, `WT_NOISE_CELL_SUBTRACT`, `WT_NOISE_CELL_MULTIPLY`, `WT_NOISE_CELL_DIVIDE`, `WT_NOISE_CELL_SMOOTH_F1` |
| `jitter` | Clamped to 0–1. |
| `smoothness` | Clamped to 0–1; used only by Smooth F1. |
| `evolution` | Signed, unclamped motion parameter; default 0 preserves the static field. |

Uniform scatters a point inside each unit cell; jitter 0 places it at the cell center.
FixedRadius scatters along a fixed-radius direction around each integer lattice node;
jitter 0 places it on that node. Their distributions and shapes differ, not just their seeds.
Hybrid distance is Manhattan distance plus squared Euclidean distance.

Scalar distance outputs share one fixed scale `B` for both F1 and F2. For dimension `d`,
`B` is `sqrt(d + 1.25)` for Euclidean, `d + 1.25` for Euclidean Squared,
`d + 0.5` for Manhattan, and `2*d + 1.75` for Hybrid. These conservative geometric
bounds cover both nearest distances for either distribution and the supported jitter range.
They do not adapt to the image, seed or jitter; changing jitter does not rescale the output.

Let `D1` and `D2` be the unnormalized nearest and second-nearest distances:

| Output | Unfiltered one-level result, clamped to 0–1 |
| --- | --- |
| `WT_NOISE_CELL_F1` | `D1 / B` |
| `WT_NOISE_CELL_F2` | `D2 / B` |
| `WT_NOISE_CELL_ADD` | `(D1 + D2) / (2*B)`; the mean of normalized F1 and F2 |
| `WT_NOISE_CELL_SUBTRACT` | `(D2 - D1) / B` |
| `WT_NOISE_CELL_MULTIPLY` | `(D1 * D2) / (B*B)` |
| `WT_NOISE_CELL_DIVIDE` | `D1 / D2`; zero when both distances are zero |
| `WT_NOISE_CELL_VALUE` | Nearest site's random value, independent of distance normalization |
| `WT_NOISE_CELL_SMOOTH_F1` | Compact soft minimum of feature distances, divided by the same `B`; details below |

Operations use raw distances before normalization, with no separate clipping of the operands.
For one unfiltered level, F2 is never below F1; Subtract, Multiply and Divide agree with arithmetic on
those normalized outputs. Euclidean Squared F1/F2 are the squares of their Euclidean outputs.
The bounds do not imply that each image reaches white. The auxiliary cell distances remain unnormalized.

Fractals apply these formulas separately to each octave, then fold and combine the values.
The one-level arithmetic identities do not generally hold after that combination; Ridged and
PingPong are not monotonic, and value-dependent weighting can also change the final F1/F2 order.
Footprint filtering also makes the arithmetic identities approximate.

### Evolution

Cellular, Voronoi Edge, Triangle Voronoi and Voronoise share `float evolution=0` in
their family settings. With matching seed, domain, distribution, jitter and evolution,
all four use the same feature points; their selection and blending rules still differ.
Evolution is a signed motion parameter in radians before deterministic per-site speed
multipliers, not a seed, time source or repeating 0..1 phase. It is not clamped or
wrapped as a whole; there is no prescribed common animation period. Connect Time to
**Evolution** in Graph, or explicitly assign the desired value in HLSL.

At zero the original feature positions and IDs are preserved exactly. Uniform points
follow sinusoidal paths within their jitter-scaled cell box. FixedRadius points rotate
at constant radius in 2D/3D. In 1D a fixed-radius direction has only two positions:
Evolution instead projects a rotation onto the line, continuously moving between
those endpoints. Jitter=0 disables motion. Jitter still clamps to 0..1 and controls
the displacement envelope; Evolution never expands the neighbor search. Cell IDs
and values stay attached to their sites; nearest-cell outputs can change when a
boundary passes the sample.

Trajectories derive from wrapped cell identity. Spatial periods hold at every
Evolution value when it is constant across the image or itself repeats with those
periods. An arbitrary nonperiodic input field is not made tileable. Animated Uniform
coordinates remain bounded but need not remain uniformly distributed at every value.
Filtering keeps the existing approximate ensemble means, not an exact integration of
the animated distribution. Very large Evolution values lose temporal precision in
float arithmetic; nonfinite values have no guaranteed result.

### Smooth F1

Smooth Cellular is an output of `WTNoiseCellular`, selected by
`c.output = WT_NOISE_CELL_SMOOTH_F1`; there is no separate SmoothCellular function.
It uses a compact log-sum-exp soft minimum with temperature `0.25 * saturate(c.smoothness)`
in the selected metric's units. A temperature at or below `0.000001` takes the hard F1
path. Negative soft distances are clamped to zero, then normalized with the same F1 bound.
This softens nearest-site transitions; metric cusps and clamping still limit smoothness.

```hlsl
WTNoiseCellularSettings c = WTNoiseCellularDefaults();
c.output = WT_NOISE_CELL_SMOOTH_F1;
c.smoothness = 0.75;
float value = WTNoiseCellular(p, n, c);
```

### Auxiliary geometry and edges

For geometric data, pass a trailing `out` argument to `WTNoiseCellular(position, n, c, cell)`.
Its type is `WTNoiseCellularSample1D`, `WTNoiseCellularSample2D` or `WTNoiseCellularSample3D`:

| Member | Meaning |
| --- | --- |
| `distance1`, `distance2` | Nearest and second-nearest distances under the selected metric, without normalization; squared metric gives squared units. |
| `value` | Nearest cell's scalar value in 0–1. |
| `id` | Nearest cell's `uint` hash, repeatable but not collision-free. |
| `toPoint` | Signed vector from the position to its nearest feature point in lattice units; scalar/float2/float3 by dimension. |

`c.output` only selects the scalar return. The auxiliary result always contains all members
above for the **first octave**, before fractal folding or combination: several octaves have
no single nearest cell. With None this is the only octave. The data reuses the scalar call's
neighbor search. Periodic copies share their wrapped cell identity. Smooth F1 leaves this
hard nearest-site geometry unchanged. Even when filtering completely hides the scalar
first octave, its auxiliary geometry is still queried and remains unfiltered.

`WTNoiseVoronoiSettings` contains `distribution`, `jitter` and `evolution`; defaults are
Uniform, 1 and 0. Voronoi Edge uses **Euclidean** geometry. It has no metric selector, so a Manhattan
Cellular query cannot silently select Euclidean edges.

```hlsl
WTNoiseCellularSettings c = WTNoiseCellularDefaults();
c.distribution = WT_NOISE_CELL_FIXED_RADIUS;
WTNoiseCellularSample2D cell;
float value = WTNoiseCellular(uv, n, c, cell);

WTNoiseVoronoiSettings v = WTNoiseVoronoiDefaults();
v.distribution = c.distribution;
v.jitter = c.jitter;
v.evolution = c.evolution;
n.fractal.mode = WT_NOISE_NONE;
n.dpdx = 0;
n.dpdy = 0;
float mask = WTNoiseVoronoiEdge(uv, n, v);
float edge = mask * sqrt(2.0); // 2D boundary distance in lattice units
```

Here `uv` is `float2`. The normalized edge value divides distance by `sqrt(dimension)`;
multiply by that factor to recover the one-level lattice distance (within float rounding).
This inverse applies only with `n.fractal.mode = WT_NOISE_NONE`, zero footprints and valid
inputs before any additional remapping; a combined or filtered field has no such inverse.
`WTNoiseVoronoiEdge(uv, n)` uses default settings. It uses the same feature points as
Euclidean Cellular with matching settings.
Edge requires an additional neighbor search; request it only when needed.

Cellular and Euclidean Voronoi Edge search local neighbors, not all cells in a period. Their adaptive bounds
are contained in integer offsets `[-(d+2), d+2]` on each active axis, where `d` is the dimension.
This envelope follows from the supported feature distributions and jitter range; it does not
discard valid nearest sites or facets. Higher density does not enlarge it. Clamping the loop
bounds also bounds the work for invalid coordinates, without promising useful output for them.

### Triangle Voronoi

`WTNoiseTriangleVoronoi(float2 position, WTNoiseSettings noise [, triangle]
[, out firstOctave])` is **2D only**. It uses the same WT feature points as Cellular
with matching seed, distribution, jitter and evolution, but chooses the nearest feature using
a triangular gauge. Its scalar return is a normalized border field in `0..1`,
including the requested shared fractal modes.
The pattern consists of gauge-defined dominance regions, not a triangular mesh;
individual regions need not be triangles. The nearest-boundary wedge-distance
calculation was independently derived for WhimTex, without copied external source.

`WTNoiseTriangleVoronoiSettings` from `WTNoiseTriangleVoronoiDefaults()` has these fields:

| Field | Default | Calculation |
| --- | --- | --- |
| `int distribution` | `WT_NOISE_CELL_UNIFORM` | Uniform or `WT_NOISE_CELL_FIXED_RADIUS`, using Cellular feature points. |
| `float jitter` | 1 | Clamped to 0..1. |
| `float evolution` | 0 | Shared continuous feature motion; signed and unclamped. |
| `float2 stretch` | `(1,1)` | Absolute value of each component, then clamped to 0.5..2. |

Stretch controls the triangular gauge along X/Y. For a vector `v` from the sample to a
candidate point, set `q = v * clamp(abs(t.stretch), 0.5, 2)`; the selection gauge is
`max(-q.y, sqrt(3)/2 * abs(q.x) + 0.5*q.y)`. The stretch bounds keep the exact local
neighbor search finite. They affect selection and boundaries, but do not rescale the
reported geometric distances. There is no metric or output selector. Common scale,
offset, period, precision and explicit footprint settings apply in XY.

```hlsl
WTNoiseSettings n = WTNoiseDefaults(1337);
n.scale = float3(8, 8, 1);
n.period = float3(1, 1, 0);
WTNoiseTriangleVoronoiSettings t = WTNoiseTriangleVoronoiDefaults();
WTNoiseTriangleVoronoiSample2D firstOctave;
float value = WTNoiseTriangleVoronoi(float2(uv.x, uv.y), n, t, firstOctave);
```

Omitting `t` uses the family defaults; omitting the trailing sample returns only the
scalar. `WTNoiseTriangleVoronoiSample2D` has these members:

| Member | Meaning |
| --- | --- |
| `float distance` | Euclidean length to the triangle-selected site, not the triangular gauge value. |
| `float edgeDistance` | True nearest dominance-boundary distance in unstretched noise-lattice coordinates. |
| `float value` | Selected site's repeatable scalar value in 0..1. |
| `uint id` | Selected site's hash; not collision-free. |
| `float2 toPoint` | Vector from the sample to the selected site in unstretched noise-lattice coordinates. |
| `float2 borderNormal` | Unit direction toward the nearest boundary in those coordinates, or zero at a degenerate boundary. |

All geometry describes the **first octave**, before folding, accumulation and filtering,
even when the scalar uses multiple octaves or filtering fully hides the first octave.
These coordinates include the common domain scale, but not the additional stretch
used by the gauge. Several octaves have no single combined cell interpretation.
Coincident affine-face equality plateaus can occur, including at Jitter=0. They use
deterministic cell tie ordering; their interiors are not treated as borders.

With None and zero footprints, the scalar is `saturate(firstOctave.edgeDistance / sqrt(2))`.
Other modes fold and combine each octave's normalized border value. Filtering uses the
shared footprint fade toward a cell-family mean estimate. That estimate is not an exact
mean of the triangle tile, including for stretched cells; it does not integrate each
pixel or guarantee alias-free borders. Auxiliary geometry remains unfiltered.

Graph `TriangleVoronoiNoise` evaluates one octave. Float **Value** is the normalized
border field; Float **Distance** is raw Euclidean distance to the selected feature, not boundary
distance. Float **CellValue** is its repeatable cell value. Float2 **ToPoint** points
from the sample to that feature; Float2 **BorderNormal** points toward the nearest
boundary, with zero at a degenerate boundary. There is no raw edgeDistance or uint ID
graph port. The auxiliary outputs stay unfiltered. There are no Gradient/Curl outputs
or corresponding HLSL overloads; BorderNormal is a geometric direction, not a
derivative of the normalized or fractal scalar field.

### Voronoise

`WTNoiseVoronoise(position, n, v)` blends random **cell values**, using positive compact
Gaussian weights in Euclidean geometry. It is not a distance output or a Voronoi edge.
`WTNoiseVoronoiseSettings` contains `distribution`, `jitter`, `smoothness` and `evolution`;
`WTNoiseVoronoiseDefaults()` initializes them to Uniform, 1, 0.5 and 0. Jitter and smoothness
clamp to 0–1; distribution accepts Uniform or FixedRadius, using the original Cellular
feature points, hashes and periods. There is no metric selector.

```hlsl
WTNoiseVoronoiseSettings v = WTNoiseVoronoiseDefaults();
v.smoothness = 0.8;
float value = WTNoiseVoronoise(p, n, v);
```

At smoothness 0, the unfiltered base field returns the nearest cell's value. Increasing
smoothness broadens the blend; 1 is the broadest setting. The weight temperature is
`0.5 * saturate(v.smoothness) * saturate(v.smoothness)`; at or below `0.000001`, sampling
uses the nearest-value path. A compact window makes contributions fade as sites enter or leave the
search. The normalized weighted value is in 0–1 before the shared fractal combination.
Calls support 1D/2D/3D, with `WTNoiseVoronoise(p, n)` using defaults. No auxiliary sample or
Gradient/Curl overload is supplied for Voronoise, Cellular or Voronoi Edge.

Smooth F1 and Voronoise searches stay within the existing `d+2` axis envelope, but can cost
substantially more than hard F1, especially in 3D. The 3D envelope contains at most
`11^3` cell slots per search before pruning.

### Cellular-family filtering

Cellular (all outputs), Voronoi Edge and Voronoise use the shared footprint visibility to
fade unresolved octaves toward **folded family-specific mean estimates**. Distance outputs
use a bounded 16-point regular-cell approximation with rounded distribution/jitter corrections;
Smooth F1 adds a nearest-neighbor soft-minimum estimate. For 1D edges, the jitter correction
comes from the analytic second moment of Voronoi interval lengths; 2D/3D edges use geometric
cubature with a small jitter correction. Random cell values use a uniform-value fold integral;
Voronoise approximates the reduced value spread from the effective number of Gaussian-weighted sites.
For Cellular distance outputs and Voronoi Edge, the estimated PingPong mean blends toward
the cycle mean as `pingPongStrength` increases from 8 to 16, and uses 0.5 at 16 or above.

These means are seed/period-independent ensemble approximations, not exact integrals of a
particular tile. Short periods, nonlinear distance combinations, high smoothness, strong
PingPong folding and weighted amplitudes can differ from the approximation. There is no
per-pixel neighbor quadrature or cache. Fully hidden later octaves can skip sampling;
Cellular still queries first-octave geometry as described above.

## Flow

`WTNoiseFlow(position, n, flow)` evaluates OpenSimplex2S with rotating gradient vectors,
using the same scalar kernels as the static basis. `WTNoiseFlowSettings` contains one field:
`float phase`, in cycles, default 0 from `WTNoiseFlowDefaults()`. Phase 0 and 1 close the same
cycle; adding an integer repeats it. Phase 0 uses the unrotated OpenSimplex2S field.

```hlsl
WTNoiseFlowSettings flow = WTNoiseFlowDefaults();
flow.phase = _WhimTexTime * 0.25; // One cycle in four seconds.
float value = WTNoiseFlow(p, n, flow);
float2 gradient = WTNoiseGradient(p, n, flow); // p is float2 here.
float2 curl = WTNoiseCurl(p, n, flow);
```

Flow takes 1D/2D/3D positions; its 1D form is a fixed slice of the 2D field.
`WTNoiseFlow(p, n)` uses phase 0. Time is not read automatically. Shared periods, precision,
fractals and `n.dpdx/dpdy` apply. Gradient has 1D/2D/3D overloads and Curl has 2D/3D
overloads taking `flow` in place of the basis selector. They differentiate position at a
fixed phase, not phase or time, and retain signed magnitude.

## Gabor

`WTNoiseGabor(position, n, g)` returns a normalized scalar in 0–1; omit `g` for defaults.
All use 1D/2D/3D positions, `n.fractal` and the common per-axis periods.
`WTNoiseGaborDefaults()` constructs these algorithm settings:

| `WTNoiseGaborSettings` field | Default | Meaning |
| --- | --- | --- |
| `float frequency` | 2 | Carrier cycles per lattice unit; the magnitude is used. This is separate from `n.scale`, which controls the domain's density. |
| `float bandwidth` | 1 | Inverse compact-envelope width, clamped to 0.5–4; support radius is `1 / bandwidth` in lattice units. Independent of carrier frequency. |
| `float anisotropy` | 0.8 | Clamped to 0–1: random directions at 0, the specified direction at 1. Ignored in 1D. |
| `float3 direction` | `(1,0,0)` | Normalized internally, using XY in 2D; zero falls back to +X. Ignored in 1D. |

Bandwidth 1 uses the original envelope and normalization. Below 1, wider envelopes normalize
by their own weights. Above 1, narrower envelopes normalize against the radius-one reference
envelope so their edges fade instead of normalizing isolated impulses back to full amplitude.
There are three impulses per lattice cell: at most 15/75/375 candidates per octave in
1D/2D/3D, or 9/27/81 when bandwidth is at least 1. Fractals evaluate at most eight octaves.

An optional trailing `out WTNoiseGaborSample` exposes `value` in 0–1 with neutral value
0.5, `phase` in turns `[0,1)`, and `intensity` in 0–1. These describe the **first octave**,
including its footprint filtering, before fractal folding or combination. They reuse the
scalar evaluation; no additional impulse search is needed. Phase becomes 0 when intensity
is negligible. A combination of octaves has no single carrier phase.

Filtering is explicit:

```hlsl
// p is float2. The footprint belongs to the shared settings n.
WTNoiseGaborSettings g = WTNoiseGaborDefaults();
n.dpdx = float3(ddx(p), 0);
n.dpdy = float3(ddy(p), 0);
WTNoiseGaborSample firstOctave;
float value = WTNoiseGabor(p, n, g, firstOctave);
// Use WTNoiseGabor(p, n, g) when only the scalar result is needed.
```

The [shared footprint contract](#shared-sampling-footprint) applies. Gabor attenuates each
octave's carrier according to its direction and filters its Gaussian envelope using the
footprint scaled by bandwidth, before the fractal combiner.
It assumes settings are constant across the footprint: it does not filter spatial variations
of scale, offset, period, seed, frequency, bandwidth, direction, anisotropy or fractal settings, nor exactly
integrate nonlinear Ridged/PingPong/weighted combinations. High-frequency phase can remain
unstable where intensity is near zero. 3D and multiple octaves increase the impulse-sampling cost.

## Spots

`WTNoiseSpots(position, n, s)` returns a scalar in 0–1 in 1D/2D/3D; omit `s` to use
`WTNoiseSpotDefaults()`. It creates positive spots on an empty background with a flat core
and a quintic soft edge whose first two derivatives join continuously. Overlap combines as
`1 - product(1 - kernel)`. There is no oscillating carrier, random phase or signed impulse;
this is a separate field from Gabor with frequency zero.

| `WTNoiseSpotSettings` field | Default | Meaning |
| --- | --- | --- |
| `float density` | 1 | Expected active centers per lattice cell, clamped to 0–4. Zero leaves the base field empty. |
| `float radius` | 0.35 | Longest support semi-axis in lattice units, clamped to 0–2. Zero leaves the base field empty. |
| `float softness` | 0.5 | Fraction of the radius occupied by the soft edge, clamped to 0.01–1. Zero requests the minimum soft edge, not a hard threshold. |
| `float elongation` | 1 | Major/minor axis ratio, clamped to 1–8; compresses transverse axes at fixed radius. Ignored in 1D. |
| `float3 direction` | `(1,0,0)` | Major axis, normalized internally using XY in 2D; zero falls back to +X. Ignored in 1D. |

```hlsl
WTNoiseSpotSettings spots = WTNoiseSpotDefaults();
spots.elongation = 3;
spots.direction = float3(1, 1, 0);
float value = WTNoiseSpots(p, n, spots);
```

Four deterministic candidates per cell are independently retained with probability
`density / 4`; changing density reveals or hides centers without moving the surviving ones.
The search visits at most 20/100/500 candidates per octave in 1D/2D/3D, with up to eight
octaves. Period fitting, precision and all shared fractal modes apply. An empty base field
can become nonzero after fractal folding or amplitude combination.

Filtering uses the shared footprint and the shortest soft-edge width,
`radius * softness / elongation`, to fade unresolved detail toward an approximate mean.
This conservative isotropic filter uses an analytic kernel volume and a Poisson overlap
approximation to the bounded cell distribution. Folding that mean does not exactly integrate
nonlinear fractals or weighted amplitudes. Spots has no auxiliary sample, Gradient or Curl API.

## Gradient and curl fields

Triangle Voronoi and Wavelet do not add Gradient/Curl support. Triangle's BorderNormal
is described in [Triangle Voronoi](#triangle-voronoi) and is not an analytic field derivative.

Use `WTNoiseGradient(position, n, algorithm)` or `WTNoiseCurl(position, n, algorithm)`.
The optional algorithm defaults to `WT_NOISE_PERLIN`; supported alternatives are
`WT_NOISE_VALUE`, `WT_NOISE_VALUE_CUBIC`, `WT_NOISE_OPENSIMPLEX2`, `WT_NOISE_OPENSIMPLEX2S`.
Both read `n.fractal` and use **analytic derivatives** for all five basis algorithms.
Gradient returns a scalar/float2/float3 derivative for 1D/2D/3D with respect to the
**input position**, including the actual period-fitted scale, filtered octaves and derivatives
of value-weighted fractal amplitudes. Scale therefore affects derivative magnitude.
These are not unit vectors and are not packed into 0–1. At clamp/fold cusps, the local
slope is defined as zero; contributions from other octaves remain.

Curl returns the corresponding 2D/3D field. In 2D the result is
`float2(gradient.y, -gradient.x)`; 3D uses three seeded potential fields. There is no 1D
Curl overload. The 3D form performs three analytic potential evaluations. Floating-point
evaluation does not guarantee exactly zero numerical divergence.

For Gabor, pass `WTNoiseGaborSettings` as the third argument instead of an algorithm ID:

```hlsl
WTNoiseGaborSettings g = WTNoiseGaborDefaults();
g.frequency = 3;
g.anisotropy = 0.8;
// Optional filtering, with the same footprint contract as WTNoiseGabor:
n.dpdx = float3(ddx(uv), 0);
n.dpdy = float3(ddy(uv), 0);
float2 gradient = WTNoiseGradient(uv, n, g);
float2 curl = WTNoiseCurl(uv, n, g);
```

These overloads use **analytic derivatives**, without sampling neighboring positions.
Gradient supports 1D/2D/3D; Curl supports 2D/3D. They differentiate the scalar result of
`WTNoiseGabor`, including its compact envelope, carrier, position-dependent normalization,
explicit filtering and all fractal modes. Weighted fractals include the derivative of
each octave's amplitude. Input-space derivatives use the actual per-octave domain scale,
including period fitting and high/low coordinate preparation.

The same operator names accept `WTNoiseFlowSettings` for the analytic Flow derivatives
described above. For every supported family, all settings, phase and `n.dpdx/n.dpdy`
are held constant while differentiating the input position.
If the caller varies settings spatially, these functions do not include derivatives of
those settings. At nondifferentiable clamp/fold points the local slope is defined as zero;
other octave contributions remain. Results keep their signed magnitude, without length
normalization or 0–1 packing. 2D Curl rotates the gradient; 3D Curl uses three potentials
of the selected family with distinct seeds and performs three analytic evaluations.
Analytic derivatives describe the implemented filtered approximation; they do not make
that approximation an exact pixel integral.

## Domain displacement

`WTNoiseDisplacement(position, n, w)` returns a signed offset with the position's dimension.
Add it explicitly; it does not overwrite the input or return an already-warped coordinate.

```hlsl
WTNoiseWarpSettings w = WTNoiseWarpDefaults();
w.strength = 0.1;
float2 warped = uv + WTNoiseDisplacement(uv, n, w);
```

`WTNoiseWarpSettings` has `algorithm`, `mode`, `strength`.
Defaults are `WT_NOISE_WARP_OPENSIMPLEX2`, `WT_NOISE_WARP_NONE`, 0.1.
`WTNoiseDisplacement(position, n)` uses these defaults.
Algorithms are `WT_NOISE_WARP_OPENSIMPLEX2`, `WT_NOISE_WARP_OPENSIMPLEX2_REDUCED`
and `WT_NOISE_WARP_BASIC_GRID`. Modes are `WT_NOISE_WARP_NONE` (one level),
`WT_NOISE_WARP_PROGRESSIVE` (each level reads the previously displaced position) and
`WT_NOISE_WARP_INDEPENDENT` (each level reads the original position).
Octaves, lacunarity and gain come from `n.fractal` with the same limits as scalar fractals.
Warp's own `w.mode` controls accumulation; `n.fractal.mode`, `weightedStrength` and
`pingPongStrength` do not affect displacement. Strength is a signed
amplitude in input-position units. No unit-length normalization or Signed/Packed remapping occurs.
The shared footprint attenuates each displacement octave toward zero. Progressive warp
uses the supplied footprint without propagating a Jacobian through previous displacements;
filtering remains approximate.

## White and Blue grain

`WTNoiseWhite(float2 uv, WTNoiseGrainSettings grain)` and
`WTNoiseBlue(float2 uv, WTNoiseGrainSettings grain)` return `float3` in 0–1.
These retain the Noise layer's grain controls, separately from the lattice-noise contract.
They do not take `WTNoiseSettings` or fractals and have no 3D overload.

| `WTNoiseGrainSettings` field | Default | Meaning |
| --- | --- | --- |
| `int seed` | 1337 | Reproducible grain variation. |
| `float2 imageSize` | Required | Dimensions of the coordinate domain in pixels. |
| `float2 offset` | `(0,0)` | Pixel offset, before division by grain size. |
| `float grainSize` | 1 | Grain cell size in pixels, floored to a small positive value. |
| `float direction` | 0 | Degrees for the 1D projection, measured from X. |
| `bool oneD` | false | Project pixel position about the image center onto the direction axis. |
| `bool color` | false | Independent RGB grain; false repeats the same scalar in RGB. |
| `bool inverted` | false | Return `1 - value`. |

```hlsl
WTNoiseGrainSettings grain = WTNoiseGrainDefaults(_InputSize.xy, 1337);
grain.grainSize = 2;
grain.color = true;
float3 value = WTNoiseBlue(uv, grain);
```

Use the pixel dimensions belonging to your UV domain; `imageSize` is not inferred from
the preview's render target. The overload without a seed uses 1337. OneD uses offset X
along the projected axis; offset Y identifies a grain row, not a second spatial axis.
There is no image-edge seamless switch. Blue grain reads the existing shared 256×1 and
128×128 rank textures with integer loads, then varies shifts/orientation by seed and channel.
Those tiles repeat in grain coordinates; they do not promise a user-selected UV period.
WhimTex supplies `_WTNoiseBlue1D` and `_WTNoiseBlue2D` globally; scripts need no texture
parameters or copied assets. Neither grain function applies gamma conversion or a palette.

For indexed randomness per brush stroke/stamp, keep using
[`BrushRandom`](AI/BRUSHES.md#dynamic-context-and-randomization), not spatial grain.

## Integration boundary

The package include is `Packages/com.dcfapixels.whimtex/src/Shaders/WTNoise.hlsl`.
WhimTex injects it; do not add an earlier FastNoiseLite include. Internal `WTNoiseInternal*`
helpers and FNL types/constants are implementation details, not the recommended authoring API.
Custom noise and point distributions remain valid when the supplied algorithms do not fit.
