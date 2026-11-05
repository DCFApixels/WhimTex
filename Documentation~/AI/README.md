---
layout: default
title: AI authoring contract
lang: en
permalink: /ai-authoring/
description: "Generate editable WhimTex documents and layers using unified JSON and HLSL effects."
---

# WhimTex AI authoring: JSON documents and HLSL effects

Use **`whimtex.document`, version 3** for new documents, selected layers and clipboard recipes.
Files use **`.json`**. The operation (open, paste or API insert/replace), not a `kind` field,
decides what to do with the content. Brush and standalone gradient presets remain separate formats.

This guide targets the checked-out version, not older package releases. A browser AI can return
JSON for the user to paste without a Unity connection. Do not claim to have rendered, compiled or
inserted it unless you actually did so.

## Single-pass For loops in Shader Graph

Declare `ForBegin` and `ForEnd` together, each with `loopPartnerId` pointing to the
other's `id` in the same graph. Begin inputs: `Iterations` (Float, default 4), `Initial`
(Float–Float4/Color, default 0). Begin outputs: `Index` (0-based Float), `Value` (state).
End input: `Value`; output: `Result` (Initial's type). Connect the body through ordinary
edges. Counts are floored and bounded below by zero; zero returns Initial. Nested loops
are supported. Index/Value may not escape their paired body. A Buffer can precede or
follow the loop, but its baked contents must not depend on an active iteration.
See the [loop contract](../ShaderLayer.md#for-loops) for previews and copying.

## Where to start

1. Read the [shared JSON contract](../JSON_FORMAT.md) and [generated model schema](document.schema.json).
2. Open an actual recipe from the [clipboard examples](../Examples/Clipboard/README.md) before adapting it.
3. For more involved textures, consult the [38 editable samples and individual previews](https://github.com/DCFApixels/WhimTex/tree/main/Samples~/AgentTextures).
4. For shader code, use the [HLSL interface below](#hlsl-interface--shader-only-or-inside-json)
   and [FX reference](../ShaderFX.md).

For brushes use the separate [brush contract](BRUSHES.md) and [brush examples](../Examples/Brushes/README.md).
Brush JSON replaces the brush rather than creating layers.

Shader layers and Shader FX store the same serializable `program`: `mode`, `draftGraph`, `draftCode`,
`workingCode`, `draftCodeParameters`, `presetId` and `presetVersion`. Use the
[shared program contract](../ShaderLayer.md#shared-shaderprogram) and generated model schema.
It has independent `Graph` and `Code` modes; Graph is the Shader layer default. Switching converts nothing
and retains both modes' parameters. Code uses WhimTex HLSL `ApplyFX`/`@param`: `draftCode` is a draft,
`workingCode` is the applied source, and `draftCodeParameters` stores its accepted declarations and values.
API `settings.shader.compile:true` compiles the selected mode; in Code it applies the draft only
after successful GPU compilation. The stored applied pair lives inside `program`, separately
from the draft. A code-only recipe has no saved applied program. Shader-layer restoration uses
`workingCode`; current unlinked FX preparation on JSON/clipboard load can apply `draftCode` instead.
Do not promise that Code can never be applied automatically; see [FX preparation](../ShaderFX.md#shared-program-in-an-fx).
Shader-layer Code UV is local procedural UV. The behavior's
`inputMode`, outside `program`, selects `None` (default transparent black), `Texture`
(`inputTexture` asset identity), `Layer` (`inputLayerId`), or `CompositeBelow` (lower composition,
including external backdrops through Pass Through ancestors). Code `color`/`SampleInput` and Graph's **Sample Input** read this source;
additional textures use parameters and `SampleTexture`. Input changes reading only: ordinary
blending remains. Select **Crossfade** to mix the lower image with processed RGBA without accumulating alpha.
Passthrough Code (`return color`) followed by the ordinary FX list replaces the former Processor workflow. Graph and Code each
generate their own image, with parameters for the selected mode shown in Properties.

In an FX, Graph **Sample Input** and Code `SampleInput` read the preceding chain image.
Unconnected Texture, Gradient, Curve and Transform2D inputs accept `inputs[]` entries with `port`,
graph `type` and `resource` (a `ShaderParameter`). Set its matching `type` to `Texture2D`,
`Gradient`, `Curve` or `Transform2D`, and assign `textureValue`, `gradientValue`, `curveValue`
or `transformValue`.
Inline textures use `textureSource: "Texture"`; use parameters for layer sources.
Discovery identifies these ports with `valueField: "resource"` and `resourceType`.
Omitted resources use defaults. Connections override literals without removing them;
Sub Graph calls use the stable interface port ID in `port`.
Sample Gradient and Sample Curve use UV.x when T has neither a connection nor an
explicit `inputs` entry. Discovery reports `defaultUV: true` and `defaultExpression: "UV.x"`.
For scalar UV inputs, `inputs[].source` accepts `Value` (manual), `UVX` or `UVY`;
discovery lists them in `sources`. This includes 1D noise Position. Float3 UV inputs use
UV for X/Y and the input's `value.z` for Z (default 0). For example, `source: "UVX"` with
`value: [0,0,0.5,0]` selects `float3(UV,0.5)`; UVY behaves identically for vector ports.
Discovery reports `editableUVComponents: ["z"]`. Sample Texture and
Sample Gradient keep `Out` as their RGBA port key and add scalar `R`, `G`, `B`, `A`.
Discovery includes `outputTypes` and `outputLabels: {"Out":"RGBA"}`.

Graph public parameters use ordinary FX controls and **Edit Graph** reuses the existing graph
window. Host Input, transform and blending are not portable program fields; changing placement
does not guarantee pixel-identical results under all transforms. PixelLocal is determined
conservatively from generated HLSL; do not infer it merely from Graph mode.

Graph presets use [`.whimtexshader`](../ShaderFX.md#portable-shader-presets), portable JSON
`whimtex.shader-preset` v1 with `program`, `menuPath`, `group`, `icon` and a compressed shared
source archive. Code presets remain `.hlsl`. Both use the same project/user catalog and exact
GUID/version linking. Embedded programs retain both modes, functions, Sub Graphs and captured
includes for offline editing; a missing preset must not erase the graph. Clear document-layer
texture references, including inactive ones, before preset export. Atomic saves preserve drafts.

`RangeMask` is a Math node with dynamic numeric `In` (default 0) and matching `Out`.
Color is processed as Float4, including alpha, with no color conversion. Scalar ports:
`Center=0.5`, `Width=0.2`, `Feather=0`. Width includes both inward transitions;
Feather clamps to 0…Width/2. `option` selects Falloff: 0 Linear (default), 1 Smooth.
Width≤0 returns zero; Feather≤0 includes both hard boundaries. In and Center retain
signed/HDR values; the mask is 0…1. Use Luminance before RangeMask for a brightness mask.

`InstanceIndex` and `Time` are Input nodes with one Float `Out`.
Both have Float `Start` (default `0`) and `Scale` (default `1`) inputs.
`InstanceIndex` returns `Start + (float)_WhimTexInstanceIndex * Scale`, with ordinary
floating-point precision; standard rendering currently supplies index `0`.
`Time` returns `Start + _WhimTexTime * Scale`. Canvas and its dependencies use the document's
controlled time in seconds, with Play/Pause and seeking. The graph editor keeps an independent
Editor-time clock; Save, ordinary export, default renders and Live Update use `0`.
FX, HLSL nodes and Custom Function can read the automatically supplied `_WhimTexTime` (float
seconds) and `_WhimTexInstanceIndex` (uint, currently 0). Do not redeclare these uniforms or
create `@param` controls with their names. Graph-result FX share the graph's independent time.
Canvas time/playback state is session-only, starts at `0`/paused on open/reload and
is not part of clipboard JSON. Seed is not modified implicitly. See the
[evaluation context and C# dependency flags](../ShaderFX.md#evaluation-time-and-instance-index).

The graph's [procedural and UV nodes](../ShaderLayer.md#procedural-and-uv-nodes) are grouped as follows:

| Category | Serialized `kind` values |
| --- | --- |
| Procedural/Noise | `PerlinNoise`, `ValueNoise`, `ValueCubicNoise`, `OpenSimplex2Noise`, `OpenSimplex2SNoise`, `FlowNoise`, `CellularNoise`, `GaborNoise`, `SpotsNoise`, `ShardNoise` |
| Procedural/Noise/Warp | `OpenSimplex2WarpNoise`, `OpenSimplex2ReducedWarpNoise`, `BasicGridWarpNoise` |
| Procedural/Noise/Grain | `WhiteNoise`, `BlueNoise` |
| Math/Random | `Random` (unchanged coordinate hashing) |
| Sampling | `Buffer` |
| Procedural | `Fractal` |
| Procedural/Pattern | `Checkerboard`, `Stripes`, `Grid`, `Rings`, `GradientFactor` |
| Procedural/Shape | `Shape`, `CombineDistance` |
| UV | `TransformUV`, `PolarCoordinates`, `Repeat`, `Mirror`, `Kaleidoscope` |
| UV/Distortion | `VectorDisplacement`, `Twirl`, `BulgePinch`, `Wave`, `Ripple` |

Discover exact ports and defaults through `shaderNodes`, including `category`, `outputTypes`
and `modes` entries with numeric `option` and display `name`. Set the node's `option` to the
selected integer, not its label. Shape has mode-specific inputs: read `modes[].inputs`, including
Start/End/Width for Line Segment (`option: 6`), instead of copying only the default-mode inputs.
WT scalar noise evaluates one level and has no embedded fractal or octave inputs.
The separate `Fractal` node accepts `FBm` (default), `Ridged`, `PingPong`, `Multifractal`, `HybridMultifractal`,
`HeteroTerrain`, `RidgedMultifractal` and repeats
a numeric/Color branch in the 2D graph domain. This is not exact WT per-noise fractal evaluation;
the public WT HLSL API is unchanged. Warp retains `None` (default), `Progressive`, `Independent`
as a separate domain-warp operation; grain has no embedded fractal.
Read `settings.noiseDimensions.values[]` for per-dimension `inputs`, `outputs` and
`outputTypes`: scalar/warp use OneD/TwoD/ThreeD except Triangle Voronoi (only TwoD is
listed, with no Dimensions selector in the UI);
grain uses OneD/TwoD (default TwoD).
Fractal/warp `settings.fractal.values[]` and Cellular's `settings.noiseOutput.values[]` include input
availability (`active`, `inactiveReason`). Scalar/warp `noiseFiltering` defaults true;
grain `noiseInverted` defaults false. Cellular/Triangle Voronoi use
`noiseDistribution` Uniform/FixedRadius; Cellular additionally uses `noiseMetric`
Euclidean/EuclideanSquared/Manhattan/Hybrid and `noiseOutput`
CellValue/F1/F2/Add/Subtract/Multiply/Divide/DistanceToEdge (default F1).
All 18 kinds use the [WT noise contract](../ShaderLayer.md#wt-noise-nodes), including
family inputs, defaults and units. The retired Noise/Cellular/DomainWarp/TileableNoise/
VoronoiEdge nodes and old Gabor storage are unsupported, with no automatic migration.
The [shared contract](../ShaderLayer.md#serialized-modes-and-inputs) lists all modes and inputs.

`Buffer` has numeric/Color In, Float2 UV (implicit UV) and Out matching In. Set
`bufferResolution:"Canvas"` (default, current render-target size including previews) or
`"Custom"` with positive integer `bufferSize:[512,512]`; wrap/filter control sampling.
It uses a reusable transient linear float32 RGBA texture and stores no generated pixels.
Bound data/context changes invalidate contents; external RenderTexture inputs or CustomFunction
code conservatively refresh each owner render, once per buffer per draw in dependency order.
`Fractal` has In, Octaves=3, Lacunarity=2, Gain=0.5, WeightedStrength=0,
PingPongStrength=2, Bias=1, Feedback=2 and SeedStep=1. It combines components independently, including Color alpha;
FBm retains signed/HDR values. The four multifractal/terrain modes interpret `s=2*In-1`
and return raw unclamped heights. Bias applies to Hybrid/Hetero/RidgedMultifractal;
Feedback (minimum 0) applies to Hybrid/RidgedMultifractal. Gain still controls amplitude decay.
WeightedStrength/PingPongStrength are unused by these four modes. Unconnected unused ports
are hidden; stored values and connected inactive ports survive mode switches. Its `noiseFiltering` attenuates octave contributions using
local UV pixel footprint; upstream WT filtering remains independent. SeedStep offsets all
noise-node seeds, including grain, but not arbitrary CustomFunction seed parameters.
A Buffer anywhere in Fractal's source branch requires **unconnected literal SeedStep=0**;
a connected seed step is conservatively rejected even at zero. Buffer after Fractal is allowed.
Buffer captures the local `0..1` image; out-of-range Fractal samples use Buffer wrap
(Clamp by default, Repeat for tiled octaves). Zooming into that fixed image adds no detail.
Buffer's Generated HLSL depends on graph-managed intermediate textures and passes;
copying code alone does not allocate or populate them.
See [Buffer/Fractal](../ShaderLayer.md#buffer-and-fractal-nodes) for formulas, bounds and storage.


Do not assume every node has only an `Out` port: scalar noise starts with Float `Value`;
supported bases add dimension-width `Gradient` and 2D/3D `Curl`. Cellular also has raw
first-octave Float `F1`, `F2`, `CellValue` and dimension-width `ToPoint`, with no uint ID.
`TriangleVoronoiNoise` has normalized border `Value`, raw Euclidean feature `Distance`, `CellValue`,
Float2 `ToPoint` and Float2 `BorderNormal` (unit direction toward the nearest boundary,
or zero at a degenerate boundary). Its auxiliary
geometry stays unfiltered. Triangle Voronoi has no Gradient/Curl or embedded octaves.
Gabor has first-octave Float `Phase`, `Intensity` and `Phasor`. For Phasor only, `option` selects
0 Sine (default), 1 Triangle, 2 Pulse; Float `PhaseOffset=0` uses cycles and Float `Width=0.5`
is Pulse duty cycle (0..1). Width is hidden unless Pulse or connected; inactive wires/values
are retained. Other outputs do not evaluate profile-control branches. Phasor filtering uses
analytic phase footprints and fades unresolved detail toward the profile mean; genuine phase
singularities remain, and spatially varying profile controls are not integrated. Warp outputs `Displacement` and
`Position` (input plus displacement); grain outputs Float `Value` and independent-RGB
`Color` (alpha 1). Write the intended `outputPort` explicitly. Noise `Position` defaults
to UV.x/UV/float3(UV,value.z), with Z=0 by default; grain Position is always Float2 and ImageSize defaults to CanvasSize.
Discovery exposes these implicit inputs as `defaultExpression`; ImageSize additionally
has `default:"CanvasSize"` with no numeric `value`. Canvas/Custom selects the binding.
`Repeat` has Float2 `Out` and `Cell`; `Shape` has Float `Out` (mask) and `Distance` (signed);
`CombineDistance` has Float `Out` (signed) and `Mask`. `Random.Out` and `GradientFactor.Out`
are Float. Repeat.Cell is `floor(UV * Scale + Offset)`,
unrelated to **Input/Instance Index**, which still returns zero. Random.Coordinates defaults to
current-pixel UV when no edge or explicit input value is supplied. Set an explicit Float2 for
constant coordinates, or connect Repeat.Cell for one value per cell.

The UV distortions listed above return Float2 UV, with no `0..1` clamp. `Strength = 0` preserves input UV and
displacement vectors are not normalized. Connect the resulting UV to a procedural or sampling
node. VectorDisplacement decodes Signed RG directly or Packed RG as `2*v-1`; Packed RG's neutral
value is `(0.5,0.5)`. `Wave`, `Ripple`, `Stripes` and `Rings` expose Float `Phase` in cycles;
Angle/Rotation/Direction use degrees. Drive Phase with a parameter or connect Time to animate
Canvas with document playback, or graph previews with their independent clock. Save and ordinary
export use time 0. Pattern and shape masks have smoothed edges. Signed distances are negative
inside; the ellipse distance remains approximate. Use `Shape.Distance` for distance operations
and `CombineDistance.Mask` for coverage. To color a gradient field, connect `GradientFactor.Out`
to `SampleGradient.T` and a Gradient parameter to `SampleGradient.Gradient`.

Graph `Constant` nodes select `constant: "PI"`, `"TAU"`, `"PHI"`, `"E"` or `"SQRT2"`
and output Float. Arbitrary values use parameters or unconnected inputs, not a node-level `value`.
`Swizzle` takes numeric/Color `In`; `swizzle` is 1–4 case-insensitive `xyzw`/`rgba` characters
or `_` for zero. Its numeric output width equals mask length: Float2 with `"_x_y"` yields
Float4 `(0,x,0,y)`. Input width is the maximum of source width and the mask's highest component
(y/g: 2, z/b: 3, w/a: 4). Connected scalars repeat; shorter vectors gain zeros. Unconnected
values expose the inferred components directly. `_` does not increase input width.

`Expression` uses `expression: "a + b * c / 2"` and `valueType: "Float"` (Float–Float4).
Variables are case-sensitive input keys, discovered in first-appearance order; each input
and `Out` use the selected type. Functions, types, numeric literals and swizzles are not
inputs. Empty expressions return zero. Store numeric defaults in `inputs` as usual;
connections use the variable's exact name. Expression text is limited to 4096 characters.

Additional native kinds use one `Out` and the following input keys. Discover exact defaults
through `shaderNodes`; fixed outputs use `outputTypes`, while dynamic numeric math treats Color
as Float4 and infers the widest numeric width.

| `kind` | Inputs | Behavior |
| --- | --- | --- |
| `Sign`, `Saturate`, `Asin`, `Acos`, `Exp`, `Log` | In | Component-wise sign, 0..1 clamp, inverse sine/cosine, natural exponential/logarithm. Asin/Acos require -1..1; Log requires positive input. Domains are not silently clipped. |
| `Modulo` | A, B | Floor modulo `A-B*floor(A/B)`, not truncating remainder; B=0 is undefined. |
| `Atan2` | Y, X | `atan2(Y,X)` in radians; Asin/Acos also return radians. |
| `Cross` | A, B | Float3 cross product. |
| `Reflect`, `Refract` | Incident, Normal; Refract adds scalar Eta | Dynamic numeric result; supply unit vectors explicitly. Refract returns zero for total internal reflection. |
| `And`, `Or`, `Not` | A/B for And/Or, In for Not | Boolean inputs and output, without numeric coercion. |
| `NormalEncode`, `NormalDecode` | Normal | Fixed Float3 signed→packed (`n*0.5+0.5`) or packed→signed (`n*2-1`). No normalization, clamp or normalEncoding setting. |
| `NormalStrength` | Normal, Strength | Float3; Strength 0 is flat, 1 preserves the unit normal, negatives reverse XY. |
| `NormalLighting` | Color, Normal, LightDirection, LightColor, Intensity, Ambient, SpecularColor, SpecularIntensity, Roughness, SpecularOnly | Color with preserved surface alpha; specular defaults off (SpecularIntensity=0), fixed view direction (0,0,1). |
| `Premultiply`, `Unpremultiply` | Color | Color RGB times/divided by alpha; alpha unchanged. Unpremultiply returns zero RGB at exactly zero alpha, otherwise divides by alpha without an epsilon cutoff. Output expects straight RGBA. |
| `Flipbook` | UV, Columns, Rows, Frame | Float2 atlas UV. Dimensions floor/clamp to 1..4096, floored frames wrap including negatives. option 0 Bottom to Top, 1 Top to Bottom. |
| `Dither` | Color, UV, Levels, PixelSize | Color with Bayer-quantized RGB and unchanged alpha. option 0/1/2: Bayer 4x4/2x2/8x8. Levels rounds to at least 2, absolute PixelSize is at least one output pixel. |

NormalStrength and NormalLighting accept `normalEncoding: "PackedRGB"` (default) or `"SignedXYZ"`;
discovery lists encoding-specific neutral inputs. NormalLighting normalizes Normal/LightDirection,
uses LightColor alpha for diffuse light strength and SpecularColor alpha for specular strength.
SpecularOnly removes ambient/diffuse, and Roughness controls highlight width. RGB/intensities
remain signed/HDR; Dither and alpha conversion also have no implicit RGB clamp. See
[exact extended-node defaults and evaluation](../ShaderLayer.md#extended-math-normals-and-generators).

For image color processing, use the native [color and normal nodes](../ShaderLayer.md#color-and-normal-nodes).
All of these native nodes have a single `Out`; use exact `kind` identifiers in JSON:

| Category | `kind` | `Out` type |
| --- | --- | --- |
| Color/Adjustments | `Hue`, `Saturation`, `Exposure`, `Lightness`, `Levels`, `ColorBalance`, `Posterize` | Color |
| Color/Conversion | `ConvertColor` / `Luminance` | Color / Float |
| Color/Mapping | `GradientMap`, `ColorCurves` | Color |
| Color/Selection | `ColorMask`, `HueRange` | Float |
| Color/Mixing | `MixColor`, existing `Blend` | Color |
| Color/Alpha | `SetAlpha`, `Premultiply`, `Unpremultiply` | Color |
| Normal | `HeightToNormal`, `NormalBlend`, `NormalEncode`, `NormalDecode`, `NormalStrength` | Float3 |
| Normal | `NormalLighting` | Color |
| UV/Distortion | `HeightToDistortion`, `NormalToDistortion` | Float2 |

Contrast remains a cataloged HLSL function at **Color/Adjustments/Contrast**, with output `Result`;
do not invent a native `Contrast` kind. Color modes use integer `option`, listed in discovery's
`modes`. MixColor uses `0: Perceptual` (default, including omitted `option`), `1: Linear RGB`,
`2: sRGB`; discovery's `defaultOption` matches the editor.

Discovery's `settings` object lists applicable node-level enum fields as
`{ default, values: [{ value, name }] }`. Write `value` identifiers into the corresponding node
field, not the UI `name` and not `node.inputs`. ConvertColor uses `inputColorSpace` (default
`LinearRGB`) and `outputColorSpace` (default `SRGB`), accepting `LinearRGB`, `SRGB`, `HSV`, `HSL`,
`Oklab`. GradientMap and height nodes use `channel`, default `Luminance`; height nodes also use
`derivative`, default `Scharr`, and `wrap`/`filter`, default `Clamp`/`Bilinear`.
HeightToNormal, NormalBlend, NormalToDistortion, NormalStrength and NormalLighting use
`normalEncoding`, default `PackedRGB`. NormalBlend, NormalToDistortion, NormalStrength and
NormalLighting list encoding-specific input defaults in
`settings.normalEncoding.values[].inputs`. The [shared reference](../ShaderLayer.md#color-inputs-and-modes)
lists every port, enum and default.

Connections never convert color spaces implicitly. Ordinary color operations expect linear RGB;
ConvertColor explicitly changes component representation and retains alpha. Adjustments preserve
alpha, with dedicated exceptions: GradientMap alpha modes Preserve/Multiply/Replace,
ColorCurves with a connected Alpha curve, MixColor's alpha interpolation, SetAlpha and Blend.
ColorMask and HueRange masks do not multiply by alpha. HDR and negative RGB are not generally
clipped: Levels has an explicit Clamp input, default true; false allows signed-gamma extrapolation.
Hue/Saturation/Lightness work internally in encoded sRGB and return linear RGB without a final
SDR clamp. Exposure multiplies linear RGB by `exp2(Stops)`. Posterize quantizes each channel with
at least two endpoint-inclusive levels without clipping RGB. MixColor.T and SetAlpha.Alpha are
unbounded. ColorCurves marks Master/Red/Green/Blue/Alpha as `optional:true`: disconnected curves
are identity. Connected curves extend with `x + curve(saturate(x)) - saturate(x)`, preserving HDR
and negative values at identity. GradientMap needs a connected Gradient resource.

ConvertColor's HSV/HSL use sRGB-encoded RGB and hue in turns. HSV clamps negative RGB to zero
but preserves HDR V; conversion from HSV bounds S to `0..1` and V to nonnegative values.
HSL clamps encoded RGB, L and S to `0..1`, so it cannot roundtrip HDR or negative RGB.
LinearRGB/SRGB/Oklab retain signed/HDR values. A same-space conversion bypasses exactly.
Its unconnected input uses a color picker for LinearRGB and raw Float4 numeric components for other spaces.

HeightToNormal and HeightToDistortion use `heightSource: "Texture"` by default: Height requires
a Texture resource and Radius is in bound-source texels. With `heightSource: "Value"`, Height
is Float and the connected branch is reevaluated at neighboring UVs; Radius is in output pixels.
Read `settings.heightSource.values[].inputs` and `radiusUnits` in discovery for the two layouts.
Value mode retains Strength, Radius, FlipX/FlipY and derivative (Scharr/Sobel/CentralDifference).
Channel/wrap/filter apply to Texture mode; sampling nodes inside a Value branch use their own settings.
HeightToNormal and NormalBlend return normalized Float3 normals in PackedRGB or SignedXYZ;
normal vectors have no implicit sRGB encoding. HeightToDistortion preserves the finite-difference
gradient magnitude. NormalToDistortion decodes XY without normalizing and applies Float2 Strength.
Both distortion nodes output displacement, not UV. Connect Out to VectorDisplacement.Displacement
with matching Signed RG (`option: 0`) or Packed RG (`option: 1`), then use VectorDisplacement.Out
as sampling UV. Packed neutral is `(0.5,0.5)`; packing does not clamp the displacement.
For height slope `(dx,dy)`, HeightToNormal uses `normalize(-dx,-dy,1)`; HeightToDistortion uses
`(dx,dy)`. NormalToDistortion takes the normal's XY directly, so reverse Strength or FlipX/Y
to change direction when needed; the two routes also differ in magnitude after normalizing.

`Blur`, `Sharpen`, `EdgeDetection`, `Dilate` and `Erode` sample numeric/Color **In** by reevaluating
its upstream branch at UV offsets. All accept UV (current pixel by default) and Radius (default 1,
in output pixels); Sharpen and EdgeDetection add Strength (default 1). Out preserves In's type
and processes every component, including alpha. Discovery uses `sampledInputs: ["In"]`,
`outputTypeFromInput: "In"` and `radiusUnits: "OutputPixels"` for these nodes.
For Blur/Sharpen/Dilate/Erode, `option` is 0: 3 × 3, 1: 5 × 5 or 2: 9 × 9. Blur uses a normalized
finite Gaussian kernel; Sharpen is `input + (input - blur) * Strength`; Dilate/Erode take
component-wise max/min. EdgeDetection uses fixed 3 × 3 Sobel magnitude times Strength.
These are generated HLSL branch functions, without intermediate GPU textures or multipass rendering.
There is no implicit output clamp. Larger radii retain a fixed sample count and can miss thin
detail; nested filters multiply branch evaluation cost. Fixed upstream UVs remain fixed.
See [sampled filters](../ShaderLayer.md#sampled-branch-filters).

Reusable definitions live in `graph.subGraphs`, not a separate asset. Each `ShaderGraphSubGraph`
has `id`, `name`, `inputs`, `outputs`, `nodes`, `edges`, `notes`, `groups`; each interface pin has
`id`, `name`, `type`, `value`, `uvDefault`. A `SubGraph` node's `subGraphId` references a definition.
Connect using **interface IDs**, never display names. Inside the definition, `SubGraphInput`
uses `parameterId` for an input pin and exposes `Out`; exactly one `SubGraphOutput` has dynamic
input pins keyed by output interface IDs and no outputs. These interface nodes are invalid at root.
Definitions share root parameters/functions/sources and expand during compilation; edits affect
all instances. Snapshots, JSON and TIFF retain the table. Preserve it when replacing a graph.
Limits are 128 definitions, 64 input and 64 output pins per definition (at least one output),
16 nesting levels, no recursion and 4096 expanded nodes. Node IDs are unique across all scopes.
Discovery omits interface-only kinds and marks SubGraph `requiresDefinition: true` with
`definitionField: "subGraphId"`, `definitionTable: "subGraphs"` and dynamic input/output flags;
its empty port lists do not describe an instantiable standalone node.
See the [Sub Graph contract](../ShaderLayer.md#reusable-sub-graphs).

HLSL nodes reference definitions in `graph.functions` through `node.functionId`; file functions
and inline bodies with includes reference complete embedded modules in `graph.sources` through
`sourceId`. Use the current schema for exact fields and preserve source IDs, original include text and per-module include
maps. Do not replace this archive with a pruned function body or generated shader output.
Source IDs may remain stable when explicitly replacing module text or include bindings;
both participate in compilation and preview cache invalidation.
Inline Custom Function keeps the applied `body`/`ports` separate from saved `draft`/`draftPorts`.
A nonempty `draftPorts` implies a pending signature. Set `hasDraftPorts: true` to preserve an
intentionally empty port draft; an empty/null list without that flag uses the applied ports.
`inline: true, fullSource: true` represents an independent editable module converted from a
library node. Its `body`/`draft` contain the main module and `entryPoint` selects the function;
preserve its include archive. With `fullSource: false`, `body` contains only a function body.
Compile applies the candidate only after validation. File refresh is explicit and preserves
the document backup on failure. Inline includes require an archive whose root `code` equals
the applied `body`; API/JSON loading rejects missing archives instead of fetching source files.
Prefer explicit `Assets/...` or `Packages/...` include paths.

Catalog syntax starts with `// @whimtex-node Category/Name`, followed by optional `@param`
metadata and an optional exact `@preset("GUID", version)` pair before the function definition.
Metadata names refer to actual arguments or the non-void return port Result. Return values,
out arguments and inout arguments define outputs; inputs support float/half scalar-vectors,
Boolean, Color metadata on four-component values, and WhimTexTexture. Input-only float2/half2
arguments named UV or uv default to current-pixel UV. Follow the [HLSL function contract](../ShaderLayer.md#hlsl-functions)
for declaration syntax and the separate [source archive contract](../ShaderLayer.md#embedded-hlsl-source-archive)
for backups; do not infer new JSON fields from HLSL metadata.

## Graph parameter parity through Transform2D

Use the existing `graph.parameters` / `ShaderParameter` fields; Graph and Code keep separate
declarations within the shared program. The [full contract](../ShaderLayer.md#graph-parameter-authoring-through-transform2d)
defines the new selectable controls:

| Graph selection | Value/control fields | Graph output |
| --- | --- | --- |
| `Enum` | `valueType: "float"`, `floatValue`; `controls[].optionNames` paired with `optionValues` (float values, including fractions). | `Float` |
| `Seed` | Parameter `type: "Float"`; control `type: "Seed"`, `seed: true`, `valueType: "float"`, `hasMinimum: true`, `hasMaximum: true`, explicit `valueMinimum`/`valueMaximum`. Integer `floatValue` in −16777216…16777216 inclusive; shared **Random** control. | `Float` |
| `Point` | `vectorValue.xy`, normalized-UV position, default `(0.5,0.5)`; values outside the canvas are valid. | `Float2` |
| `Normal` | `vectorValue.xyz`, normalized for the shader, default/zero fallback `(0,0,1)`. | `Float3` |
| `Transform2D` | `transformValue`: existing `position`, `size`, `rotation`, `storage`, `matrix`. | `Transform2D` resource |

Other rows use the selection name as parameter `type`. An omitted/null parameter `valueType`
uses the native float width. The Seed picker selection is stored as Float plus a Seed control,
not as a new integer port.
Seed manual values, endpoints and Random results must be exact integers in that float domain,
even with soft limits. Random uses the control's inclusive `valueMinimum`/`valueMaximum`.
Always explicitly set both control flags `hasMinimum`/`hasMaximum` to `true`, including
with Graph **Range** off; `ShaderSeed.Validate` and Random reject a control missing either.
The parameter-level flags describe the UI range and remain `false` with Range off. In that
case, explicitly store `valueMinimum: -16777216` and `valueMaximum: 16777216` on the control.
Do not copy the parameter's disabled flags to its Seed control or omit its generation bounds.
Copying, loading or rendering must retain the chosen value. Enum stores the selected float
value, not the option index. For example, this is a parameter declaration fragment:

```json
{
  "id": "detail-mode", "name": "_Detail", "type": "Enum", "valueType": "float", "floatValue": 0.5,
  "controls": [{ "type": "Enum", "valueType": "float", "label": "Detail",
    "optionNames": ["Low", "Medium", "High"], "optionValues": [0.25, 0.5, 1] }]
}
```

This float seed has **Range off** and can randomize across the full exact-integer domain:

```json
{
  "id": "detail-seed-full", "name": "_SeedFull", "type": "Float", "valueType": "float", "floatValue": 42,
  "hasMinimum": false, "hasMaximum": false,
  "controls": [{ "type": "Seed", "seed": true, "valueType": "float", "label": "Seed",
    "hasMinimum": true, "hasMaximum": true,
    "valueMinimum": -16777216, "valueMaximum": 16777216 }]
}
```

With **Range on**, a hard minimum and soft maximum, both parameter flags become `true`.
Keep both control flags `true` and store the UI endpoints in parameter/control `minimum`/
`maximum` as well as control `valueMinimum`/`valueMaximum`:

```json
{
  "id": "detail-seed", "name": "_Seed", "type": "Float", "valueType": "float", "floatValue": 42,
  "hasMinimum": true, "hasMaximum": true, "minimum": 0, "maximum": 10000, "softMaximum": true,
  "controls": [{ "type": "Seed", "seed": true, "valueType": "float", "label": "Seed",
    "hasMinimum": true, "hasMaximum": true, "minimum": 0, "maximum": 10000, "softMaximum": true,
    "valueMinimum": 0, "valueMaximum": 10000 }]
}
```

`Float` limits use independent `hasMinimum`/`hasMaximum` and `softMinimum`/`softMaximum`
flags with `minimum`/`maximum`. Hard endpoints clamp; soft endpoints allow manual values
outside the slider's suggestions. A single endpoint must be hard; soft boundaries require
both finite endpoints with min < max. `cyclicRange` needs two hard finite endpoints, min < max.
`Vector2` supports an ordinary unbounded vector or a two-ended hard/cyclic interval, without
soft bounds; cyclic intervals retain seam crossing and full-cycle winding. Point and Normal
do not accept ranges. Preserve both parameter-level limits and the declarations in `controls`.
`pixelUnits` is the stored `px` flag for supported numeric lengths: retain logical values,
without multiplying by pixel scale in JSON. It is not valid for these five new control types.

**Apply Transform 2D** uses `kind: "ApplyTransform2D"` (164). Input keys are `Transform`
(`Transform2D`) and `UV` (`Float2`, implicit current-pixel UV). Output keys are `Local` and
`Input`, both `Float2`, matching `_Name_ToLocal(UV)` and `_Name_ToInput(UV)` respectively.
There is no `Out`, mask or implicit clamp. Both directions use the same UV input; choose
the output appropriate to the coordinate space you supplied. Wire a Transform2D parameter
to `Transform`, or use `inputs[]` with `port: "Transform"`, `type: "Transform2D"` and a
`resource` whose `type` is `Transform2D` and whose value is `transformValue`.
Omission uses identity (`ShaderTransform.Default`). The inline **Transform** button opens a
`PopupWindow` with the shared `ShaderParameterView`: TRS fields and Reset, without a canvas
button. Canvas manipulators belong to the parameter. This is a resource reference, not numeric matrix
components; numeric math cannot consume it. Reroute and matching Sub Graph pins retain its type.

Discovery's `shaderValueTypes` comes from the model enum. `shaderNodes` gives fixed
`outputTypes` for ApplyTransform2D and `valueField: "resource"` / `resourceType: "Transform2D"`
for its transform input. Parameter's `dynamicOutputs:true` means resolve its declaration by
`parameterId`; Reroute's `outputTypeFromInput:"In"` can preserve a resource as well as a number.
Do not infer resource compatibility from a dynamic numeric port's null catalog type.

Graph replacement and full document copy retain every control's labels, option tables,
range flags/values, `pixelUnits`, current values, stable IDs and both Draft/Working branches.
Use the complete stored model, not a reduced Properties snapshot. Do not invent `options`,
`range`, `random` or `px` storage fields. This increment adds no typed integer graph ports,
expanded color/toggle vectors, Graph layout/condition authoring (`group`, tabs, `if`) or
preview authoring; preserve any existing Code metadata for those features when copying.

## Instructions for an AI assistant

- Return one complete JSON object in a `json` code block, without comments or trailing commas.
- Use editable layers for distinct parts. Prefer Shape, Text, Gradient, Noise and targeted effects;
  use HLSL only where a custom algorithm is useful. Name layers and groups meaningfully.
- Use exact model fields and case-sensitive enum names from the schema. Do not put live API
  `settings`/operations or legacy `type`/`properties` objects into document JSON.
- Layer arrays are **top to bottom**; `fx` execute **first to last**.
  Give every layer a unique nonempty `id`. IDs may be descriptive local strings;
  paste remaps them and internal references to fresh document IDs.
- Omit values only when the version-3 default is intended. Do not infer storage defaults from
  a current UI factory or a slider range. Use FullOptimized by default; Full or Compact on request.
- Do not invent asset GUIDs or paths. For an existing project asset, use its verified `$asset`
  identity. Drawing pixels and remote image URLs are not stored in this format. Text layers use
  `fontFamily` for an installed system font, not a project Font asset. Save manages their bounded
  appearance backup; do not invent `fallbackPng`/`fallbackKey` values or promise portability of font files.
- Keep shaders self-contained and deterministic; avoid unnecessary layers, huge blur radii and
  expensive loops. Preserve alpha unless the requested effect intentionally changes coverage.
- For spatial noise in Shader-layer Code, FX or brush tips, consider [built-in WT noise](#built-in-noise-library)
  before writing a noise implementation. Prefer it when suitable; custom algorithms remain valid.
  Built-in helpers are available in self-contained code without an include.
- If an error is reported, return a corrected complete object using its field path/message.
  Re-pasting inserts another copy; it does not update the previous paste.

## JSON envelope

```json
{
  "format": "whimtex.document",
  "version": 3,
  "document": {
    "width": 256,
    "height": 256,
    "outputFilter": "Bilinear",
    "outputSrgb": true
  },
  "layers": [
    {
      "id": "clouds",
      "layerName": "Clouds",
      "behaviour": {
        "$type": "NoiseLayerBehaviour",
        "noiseType": "Perlin",
        "scale": 8,
        "scaleY": 8,
        "fractal": "FBm",
        "octaves": 3,
        "encoding": "Linear"
      }
    }
  ]
}
```

Only `format`, `version` and `layers` are required. `document` and all its settings are optional.
Opening uses version defaults for omitted settings (512 × 512 for the canvas).
Pasting inherits each omitted canvas axis from the destination. With neither axis supplied there
is no size prompt. Standard writers always include both dimensions, including Compact output.
Include them in generated recipes for predictable source-canvas context.

`writeMode` is optional and defaults to `FullOptimized`:

| Mode | Result |
| --- | --- |
| `Full` | All persistent settings, including defaults and inactive values. |
| `FullOptimized` | Active settings including their defaults; inactive branches omitted. |
| `Compact` | Same inactive-branch rules, also omitting frozen default values except canvas dimensions. |

Disabled layers and FX remain in all modes. Collapsed UI and hidden shader controls do not mean
their values are inactive. Omitted inactive settings restore version defaults when re-enabled.

## Layer fields

Every layer requires `id` and `behaviour`. Settings belong to their native model owner:

| Location | Examples |
| --- | --- |
| Layer | `layerName`, `enabled`, `opacity`, `blendMode`, `colorRange`, `blendRange`, `filterMode`, `clippingMask` |
| `behaviour` | `$type` and type-specific persistent fields |
| `transform` | Native `TextureTransform` fields; inspect the schema and an exported example |
| `fx` | FX with `$type: "ShaderFX"`, nested `program`, `active`, optional `$name`; Code parameters live in `program.draftCodeParameters`, Graph parameters in `program.draftGraph.parameters` |
| `children` | Group children in top-to-bottom order |

`$type` is an allowlisted model name, not an arbitrary assembly-qualified type.
For instance, Color Fill uses `ColorFillLayerBehaviour.storedColor`, not `properties.color`.
Groups use `GroupLayerBehaviour`, `group: true`, `children` and layer-level `compositing`.
They can carry their own FX; FX process the combined children before group opacity/blending.

For targeted effects, use `behaviour.inputMode` and `behaviour.targetLayerId` with IDs from
this tree. `Previous`, `Specific` and `AllBelow` have their usual stack semantics;
include required sources and clipping bases when copying a selection. Do not use `@aliases`.
Every FX model instance owns its parameter values and enabled state in the document. Reuse a marked
Code/Graph preset source or author an embedded program; supply independent FX models for separate effects.
JSON `fx` items may only be `ShaderFX` model objects, `null`, or internal `$ref` references to
`ShaderFX` models in this document. `$id`/`$ref` are serialization identities, separate from layer
IDs; a `$ref` refers to the same model instance. `$asset` is not allowed as an FX list item.
External Material, Shader and ShaderFX `.asset` FX references are unsupported in JSON/TIFF
and are explicitly rejected, even when unresolved, with
`External FX references are no longer supported. Use a marked .hlsl preset or inline HLSL code.`
There is no migration or source-document rewrite.
This restriction does not affect texture asset parameters or same-document layer bindings.

Native vector/color values follow the schema: Unity vectors and RGBA colors are numeric arrays,
while transform `Double2` values are objects with `x` and `y`.
Do not transfer the legacy array-based transform syntax or live API convenience setters unchanged.
Numbers and booleans are not quoted strings. Enum names are case-sensitive.

### Noise, gradients and SDF

Noise settings are directly in `behaviour`, not `properties.noise`.
`scale`, `scaleY` and `scaleZ` store explicit axis scales; omitted fields use frozen version-3 defaults (`8`, `8`, `1`), not another axis.
`warpScale` and `warpScaleY` are per-axis multipliers of the main scale.
`offset` is `[x,y,z]`; Z is used by `ThreeD` slices.
Seamless uses `periodic: "None" | "X" | "Y" | "XY"` for TwoD/ThreeD, and `periodic1D`
for OneD. White/Blue Noise ignore Seamless and do not have a true ThreeD variant.

Noise Output uses `encoding` (`Linear`, `Gamma`, `Gradient`), with
`gradient` storing the palette and `inverted` reversing the value before palette sampling.
Color White/Blue Noise does not use Gradient output.
SDF uses `SDFLayerBehaviour.encoding`: `Gradient` (default), `LinearData` or `MultiChannelData`.
SDF and Outline share `metric`: `EuclideanAntialiased` (Round AA, default, numeric ID 4),
`Sharp` (5), `Bevel` (6), `Diamond` (7), `Square` (8), `MTSDF` (9), `AAEDT` (10,
UI **AAEDT + 8SED**, experimental), `Contour` (11, UI **Contour AA**, experimental),
`CornerFit` (12, UI **Corner Fit AA**, experimental) and `Spring` (13, UI **Spring Simulation**, experimental).
All ten are antialiased;
the old numeric values 0–3 are removed without aliases. Sharp and Bevel are contour-offset fields,
not Euclidean SDFs. Diamond uses L1 and Square uses L∞; both depend on the calculation axes.
The shared `miterLimit` float has range 1..64, defaults to 4, must be finite and affects Sharp,
CornerFit and Spring. For the new modes it bevels excessive AA-verified joins within their finite
region of ownership; it does not validate uncertain corners.
SDF alone adds experimental `contourMergeDistance`: a finite float in 0..2 inclusive, default 0 (off),
in current raster/render pixels, independent of distance remap width and not divided by render scale.
It applies only to Sharp/MTSDF, including MultiChannelData; other modes retain but ignore it and
still require a valid value. The UI shows **Contour Merge (px)** only for Sharp/MTSDF.
One pass merges disjoint pairs of consecutive vertices within one contour to their midpoints,
shortest eligible pairs first. Pair distance is bounded by the requested value; each original vertex
is used at most once. It does not build multi-vertex clusters or merge different loops. A topology
conflict rolls back the whole batch. Existing safety protections remain active. It may shift short
details and does not guarantee restored sharpness.
Round AA keeps its original crossing-seed Euclidean distance transform (EDT), rather than distance
to continuous contour segments. Sharp, Bevel, Diamond, Square and MTSDF share reconstructed,
normalized subpixel contours.

`CornerFit` starts with raw Contour geometry, fits sides over multiple pixels and verifies proposed
corners by local AA re-rasterization. `Spring` first applies automatic spring relaxation to that raw
contour, then uses the same AA-tested proposals. Both reject intersecting or unconfirmed extensions,
retain raw geometry where ambiguous and allow bounded sharp joins only at AA-verified corners.
These are raster-only scalar modes using the existing downstream controls: Gradient/LinearData
are supported; MultiChannelData remains MTSDF-only. `contourMergeDistance` is retained and validated
but ignored. SDF's threshold is supported through coverage remapping of the selected channel;
Outline uses fixed byte threshold 128. Fitting is approximate for soft or noncoverage masks.
CornerFit is an experimental hybrid, with no guaranteed sharp recovery. Spring adapts ideas from
[Matusovic, Parakkat and Eisemann (2023)](https://doi.org/10.1111/cgf.14743), whose paper concerns
interactive depixelization, not SDF generation. It is not the full interactive tool or a C++ replica.
The previous eight modes and the Round AA default are unchanged.

`Contour` uses raw marching-squares threshold crossings on alpha or the selected source channel,
with linear interpolation to subpixel endpoints, and measures Euclidean distance to the finite
straight segments. It performs no contour fitting, smoothing, simplification, corner recovery,
merge/union, miter extension or MSDF channel construction. Expanded corners round. It measures
the current raster threshold boundary and cannot restore detail lost during rasterization.
Contour AA is a separate algorithm, not an AAEDT modification or a sharp-miter mode.
The existing source-channel, threshold, `SourceEdges`, offset and rendering wrapper still apply;
SDF exposes the threshold and Outline uses fixed byte threshold 128. `miterLimit` and SDF
`contourMergeDistance` remain validated but are ignored. SDF supports Gradient/LinearData;
MultiChannelData remains MTSDF-only. Quality comparison is experimental, with no guaranteed
improvement across all inputs. Round AA remains the default at ID 4; AAEDT stays ID 10 and
the existing modes are unchanged.

`AAEDT` estimates the raster boundary with an anti-aliased Euclidean sweep-and-update transform
and 8-neighbor vector propagation, based on Stefan Gustavson's `edtaa3` reference. It uses a
portable Burst job. Input alpha is assumed to represent the area coverage of a crisp edge
(box-filter sampling); blur, feather and arbitrary opacity do not describe exact geometry.
The same assumption applies when selecting another source channel. Wide external contours
remain round. This mode does not extend sharp/miter corners or reconstruct contour segments;
it ignores `miterLimit` and SDF `contourMergeDistance`, while retaining their validated values.
Round AA remains the default, unchanged.

The sweep keeps one candidate seed per pixel and is an approximate transform, not an exact
or rotation-invariant solver. Its local update tolerance is 0.0001 pixel (tighter than the
reference's 0.001); this is not a global distance-error bound.

For `AAEDT`, SDF's byte `threshold` is adapted to coverage before distance estimation. With
source coverage `a` in 0..1 and `t = threshold / 255`, for `0 < t < 1` use
`c(a) = 0.5*a/t` when `a <= t`, otherwise `c(a) = 0.5 + 0.5*(a-t)/(1-t)`.
This monotonic piecewise remap sends `t` to 0.5 and preserves 0 and 1. Thresholds 0 and 255
use binary classification instead: coverage is 1 only when the selected byte-range source value
exceeds the threshold, otherwise 0. Threshold 0 keeps nonzero samples; 255 leaves no covered samples.
Outline uses fixed byte threshold 128. AAEDT supports
Gradient and LinearData; MultiChannelData still requires MTSDF. The experimental mode is
provided for comparison without a claim of verified accuracy or performance superiority.

For MTSDF, RGB stores corner-preserving signed perpendicular/pseudo distances; alpha stores
true signed Euclidean distance to the reconstructed subpixel contour, not to a binary bitmap.
Gradient and LinearData reconstruct the scalar using the RGB median, then apply Position,
normalization, Inverted and Profile; Gradient also samples `gradient`. LinearData has opaque alpha.
`MultiChannelData` requires `metric:"MTSDF"` and encodes each raw linear RGBA channel as
`saturate(0.5 + 0.5 * d / maxDistanceNormalization)` (negative inside, positive outside, zero at 0.5;
zero normalization selects the automatic range). Alpha is data, not transparency.
Position, Contour Offset, separate inside/outside ranges, Inverted, Profile and the palette do not remap these channels.
Sharp, Bevel, Diamond, Square and MTSDF first fit straight flanks to multiple raw isocontour samples using
orthogonal least squares. Recovery requires reliable flanks and bounded deviation: sampled
flank deviation is at most 0.6 pixels. Acute tips may use independently straight
16-pixel flanks agreeing with the base 8-pixel fits; these take priority over short fits.
Only their tiny AA cap has a `sqrt(0.5)`-pixel engineering allowance; tip displacement
stays within measured base support when `sqrt(0.5) px / cosHalfTurn` fits in it.
Otherwise `min(2 px, 0.6 px / cosHalfTurn)` applies. This is not an exact raster inverse.
Enclosed holes are protected before
union by rejecting a conflicting local corner or simplification. Negative rings outside
all filled outlines are separate NonZero components, not enclosed holes.
Supported concave fits may minimally shift one wall outward within the existing
0.6-pixel envelope to preserve a nearby hole, then recheck finite-tip/topology bounds.
A merged convex AA cap receives one bounded enclosure pass using arc samples and
exact union vertices. Tiny closed components (perimeter <4 pixels) keep their finite
geometry but do not generate remote miter/pseudo extensions. Sharp/MTSDF also require
2 pixels of straight support on both sides of an endpoint after union (direction dot >=0.995).
Angular amplification has a separate safety cap of 4, approximately a 29-degree ideal tip.
Unsupported or more acute endpoints use finite segment distance. Requested `miterLimit`
values above 4 remain stored but cannot override that cap. Such corners may round.
Sharp selects the nearest finite owner within each contour before applying an angular
join, then combines contour fields; unrelated islands cannot cause a premature owner switch.
Uncertain corners are preserved. All five segment modes then use fixed 0.25-pixel simplification;
paths with perimeter below 4 pixels bypass simplification. Closed filled contours are normalized
by NonZero union at 1e-6-pixel precision before generating joins or RGB edge colors.
Holes and genuinely separate islands remain; regions overlapping after reconstruction share one
filled boundary. Open and zero-area threshold contours bypass filled-region union.
Post-union cleanup uses a 0.05-pixel budget and rejects replacement chords crossing nonincident
original or replacement boundaries or changing contour containment/contact states. MTSDF resolves each contour's RGB median before combining
contours, avoiding cross-contour channel mixing; alpha remains nearest true Euclidean distance.
Away from the contour, a common-channel bound certifies safe RGB interpolation. Suspect
cells receive nine checks against finite contour distance. Geometric sign/amplification
errors fall back to true distance; color-only incompatibility retains the scalar median.
Alpha is unchanged. Scalar MTSDF uses the corrected RGB median. Near-boundary texels are unchanged;
their neighbouring cells may interpolate them with corrected far texels.
Recovery has an angle- and support-dependent tip allowance and can alter subpixel detail; the result is not an exact
original matte. The true distance in alpha refers to the
reconstructed contour, not an exact original continuous curve. API patches reject a mismatched metric/encoding pair.
The UI switches Multi Channel Data to Linear Data when choosing an algorithm other than MTSDF.
For raw RGBA composition/export, set the layer's native `blendMode:"Overwrite"`; Normal composition
treats alpha as coverage and can alter the data. Ordinary FX/effect-layer inputs reading the standalone
SDF layer receive its rendered RGBA channels; group/AllBelow inputs use the composed image.
Use linear document output and preserve RGBA when exporting. Live settings use `blend:"Overwrite"`.
SDF has no Gamma output mode.
Document JSON stores `maxDistanceNormalization`; live patches use `maxDistance`.
See the [shared SDF/Outline field contract](../AgentAPI.md#layer-settings) for mode semantics,
but use native behaviour fields here, not live patch wrappers.
For document gradients use the schema's native `WhimTexGradient` model;
the standalone clipboard format below is a separate value contract.
Gradient layers additionally support `gradientType:"Freeform"` and `freeform.points`
with local `position`, straight encoded HDR `color` and relative `influence`.
`freeform.colorMode` chooses `Linear`, `Gamma`, `Perceptual` (OKLab, default) or `Hue` RGB
mixing; alpha is weighted independently. Figure fills and strokes choose their own model.
See [Freeform document fields](../JSON_FORMAT.md#freeform-gradients) and
[shared editing settings](../AgentAPI.md#freeform-gradient-settings).
Gradient layers also accept `gradientType:"Mesh"` with a complete `mesh` Bézier
lattice with `nodeModes`: one `Smooth`/`Corner` entry per node, or `null` for all Smooth. Its `colors`
are working-space coefficients, not ordinary encoded RGBA; retain all controls,
node modes, signed HDR and alpha overshoot. Positions are finite local coordinates;
folds and overlaps are allowed. Normalized color/opacity blending is followed by
smooth reconstruction across the whole overlap using surrounding colors and boundary
variation, with a narrow fading transition band around the overlap. It retains HDR,
leaves pixels outside the overlap and its band unchanged and does not accumulate alpha. Mesh is not a Shape/Vector/Text
paint mode. Mesh also accepts `colorMode:"Hue"`, with OKLab/alpha `colors` and a required
`hueChroma` scalar lattice of the same length. Retain both lattices on subdivision or replacement.
Both Freeform and Mesh Hue favor short circular transitions and smoothly fall back to
Perceptual where hues conflict; neither exposes `huePath`.
See [Mesh document fields](../JSON_FORMAT.md#mesh-gradients) and
[Mesh editing settings](../AgentAPI.md#mesh-gradient-settings).
Shape and Vector/Boolean Group styles support `fillMode:"Freeform"` with `freeform`,
and `strokeMode:"Freeform"` with independent `strokeFreeform` points. Text excludes Freeform.
Shape and Vector figures share `fillMode`: None, Solid, LinearGradient, RadialGradient,
SquareGradient, AngularGradient. Use native `gradient`,
`gradientStart`, `gradientEnd` and `gradientRepetitions` fields; Shape's `fill` boolean remains the overall
gate. Anchors are normalized layer-local coordinates. Shape measures them in current
canvas pixels before Transform; Vector uses `referenceSize`. Angular advances
counterclockwise from the start-to-end ray, with a seam there. `gradientRepetitions`
is a finite float of at least `0.0001`, default `1`, applied only to AngularGradient
before palette wrapping; one period spans `360 / gradientRepetitions` degrees.
Equal anchors give `t=0`. See the complete
[figure-fill contract](../JSON_FORMAT.md#figure-fills). Independent stroke paint uses
`strokeMode`, `strokeGradient`, `strokeGradientStart`, `strokeGradientEnd` and
`strokeGradientRepetitions`, with the same modes and palette behavior. Defaults are
Solid, a separate black-to-white Perceptual/Balanced/Clamp palette, `[0,0]`, `[1,0]` and `1`.
`stroke:false` or `strokeMode:"None"` disables painting while preserving settings.
Anchors must be finite and repetitions finite and at least `0.0001` even while
inactive; a gradient mode requires a non-null palette. Boolean Group stores these
fields in `style` and uses its own `referenceSize`. Inactive fill and stroke
settings survive every JSON write mode.
For algorithm behavior see [Noise](../AgentAPI.md#noise-settings) and
[Make Seamless](../AgentAPI.md#make-seamless-settings), but use document-schema field shapes,
not the live API patch wrappers from those pages.

### Editable text

Text uses `TextLayerBehaviour`, with editable `text` and an installed `fontFamily`.
An omitted or empty family uses the machine's default font. Use a verified installed family for
a particular appearance; font files are not included. Save prepares the appearance backup.
For a fitted label, use Frame layout with `autoSize`, its minimum `fontSize` and maximum
`maxFontSize`. Spacing fields are in em; `characterHorizontalScale` changes width and advance.
`edgeMode` is `HintedAA` by default: antialiased glyphs with pixel-grid fitting for small text.
Choose `Antialiased` for smooth glyphs without hinting, or `Pixel` for binary glyph coverage,
snapped placement and nearest sampling by default. Explicit filters and FX can still soften Pixel
output; it does not quantize the final alpha. The mode is included in the saved-appearance key.

```json
{
  "format": "whimtex.document",
  "version": 3,
  "document": { "width": 256, "height": 128 },
  "layers": [
    {
      "id": "label",
      "layerName": "Label",
      "behaviour": {
        "$type": "TextLayerBehaviour",
        "text": "ENERGY",
        "fontStyle": "Bold",
        "edgeMode": "HintedAA",
        "layoutMode": "Frame",
        "frameSize": [192, 80],
        "fontSize": 24,
        "maxFontSize": 64,
        "autoSize": true,
        "wrapping": "Words",
        "overflow": "Ellipsis",
        "spacing": { "character": 0.03 },
        "color": [0.2, 0.8, 1, 1]
      }
    }
  ]
}
```

Point layout uses fixed `fontSize`; Frame settings are retained for later edits.
`layoutOffset: [x,y]` translates the layout in local document pixels before rasterization
without editing the layer Transform (X right, Y up; finite ±1000000 per axis, default `[0,0]`).
Point/Frame layout handles edit this offset; switching Point/Frame preserves it.
Frame resize changes `frameSize` and the local offset together, leaving the layer Transform unchanged.
For defaults, limits and missing-font behavior see [Text and system fonts](../JSON_FORMAT.md#text-and-system-fonts).

## Paste, copy and export

Copy the JSON content, focus the preview or Layers panel and press **Ctrl+V** (Cmd+V on macOS)
outside text editing. One surrounding JSON code fence is accepted.
The tree is inserted above existing layers with remapped IDs; one Undo removes the insertion and
any accepted canvas resize. Existing layers are not replaced. A Shader with Composite Below and Crossfade can affect the lower stack.
Source output filtering/encoding does not replace destination settings during unified JSON paste.

Unknown fields, wrong types and broken internal references reject the paste without partial insertion.
Clipboard FX requires trust confirmation before compilation. A compilation failure does not reject
the tree: paste offers a warning, keeps the source, parameter values and enabled state, and skips
the unavailable effect during rendering, just like opening a JSON document. Canceling that warning
inserts nothing. Repair the code and Compile to resume the effect. Broken internal references still fail.

**Copy as JSON** exports selected layers using the same format and the FullOptimized default.
Include their dependencies in the selection. Ordinary Ctrl+C remains unchanged.
**Export** opens the shared export window; choose WhimTex JSON and a write mode.
**Save As** creates TIFF only; Ctrl+S on an already open JSON document still saves JSON.

Nonempty Drawing content cannot be represented in JSON. A confirmed omission keeps a layer
placeholder, settings and FX but loses its painted pixels in the exported copy.
Copy as JSON reports omitted content; JSON file export requests confirmation. The source remains intact.
Use TIFF when preserving Drawing pixels is required.
External assets retain GUID, path, local ID and type; resolution tries GUID, then path if the GUID
is absent. Missing assets warn rather than preventing opening, and their identity survives saving.

## Complete examples and validation

The [example index](../Examples/Clipboard/README.md) contains nine current procedural recipes:
neon ring, shock wave, car wheel, forked lightning, heart, mystic fog, seamless noise,
retro processor and local distortion. Schema checks cover their structures; Unity tests read,
compile and compare their renders before and after saving/reopening.

A schema check alone does not test dependencies, shader compilation or visual quality.
Use the connected API's document JSON validation when available, then inspect an actual render.
Command envelopes are separate from content; see the [JSON agent API](../AgentAPI.md#unified-json-documents).

## A prompt users can copy

> Read https://dcfapixels.github.io/WhimTex/ai-authoring/ and its linked document schema and examples.
> Create a 512 × 512 magical ring using editable procedural layers, grouped and named in English.
> Return one complete whimtex.document JSON code block. Do not invent external asset identities.
> Keep HLSL self-contained and do not claim it was tested unless it was.

## Common mistakes

| Mistake | Use instead |
| --- | --- |
| `"format": "whimtex.layers"` for new output | `"format": "whimtex.document"` |
| Root `kind: "document"` or `kind: "layers"` | Omit it; the caller chooses the operation. |
| `canvas`, `type`, `name`, `properties` from legacy recipes | `document`, `behaviour.$type`, `layerName` and native fields |
| `scale: [8,12]` for Noise | `scale: 8, scaleY: 12` in its behaviour |
| A remote Drawing `url` or Base64 pixels | TIFF for Drawing pixels, or a verified File asset reference |
| `opacity: 80` for 80% | `opacity: 0.8` |
| ShaderLab, GLSL `mix`, invented helpers | HLSL `ApplyFX`, `lerp`, documented helpers below |
| Live FX operations (`op`, `presetId`, `set`) inside stored content | `fx[]` with `$type: "ShaderFX"` and nested `program`, including both modes and native parameter values |

## Upgrading old clipboard data

The `whimtex.layers` reader is removed, and version-1 documents are unsupported.
Use a matching older checkout for old data; this checkout has no automatic migration.
Plain image URL paste, brush-tip URL downloads and standalone gradient presets remain available.
See the [current format and unsupported-input boundary](../JSON_FORMAT.md).

## Standalone gradient JSON

For a gradient field (not canvas layer paste), right-click the field or its editor strip
and choose **Paste**. **Copy** produces this independent value:

```json
{
  "format": "whimtex.gradient",
  "version": 2,
  "gradient": {
    "colorMode": "Perceptual",
    "interpolation": "Balanced",
    "huePath": "Shortest",
    "chromaCorrection": 0,
    "wrapMode": "Clamp",
    "colorSpace": "Gamma",
    "smoothness": 1,
    "colors": [
      { "time": 0, "color": [1, 0.1, 0, 1], "midpoint": 0.5 },
      { "time": 1, "color": [0.2, 0, 1, 1] }
    ],
    "alphas": [
      { "time": 0, "alpha": 1, "midpoint": 0.5 },
      { "time": 1, "alpha": 0 }
    ]
  }
}
```

The body alone or a bare color-stop array is also accepted, optionally inside one JSON
code fence. Each track requires 1..64 keys with strictly increasing times in 0..1. Colors
are RGBA arrays; standalone RGB accepts finite values from -65504 to 65504, alpha 0..1.
If `alphas` is omitted, alpha components of the colors form the opacity track.
The live layer/brush API retains its own RGB limits; this clipboard range does not widen them.

| Field | Values | Default |
| --- | --- | --- |
| `colorMode` | `Linear`, `Gamma`, `Perceptual`, `Hue` | `Perceptual` |
| `interpolation` | `Blend`, `Balanced`, `Smooth`, `Adaptive`, `Fixed`, `Custom` | `Balanced` |
| `customColorMode` | Same values as `colorMode`; required with nonempty `customKeys` | No curves |
| `customKeys` | Independent channel keys, described below; retained while inactive | No curves |
| `huePath` | `Shortest`, `Longest`, `Increasing`, `Decreasing` | `Shortest` |
| `chromaCorrection` | Finite 0..1 | 0 |
| `smoothness` | Finite 0..1 | 1 |
| `wrapMode` | `Clamp`, `Repeat`, `Mirror` | `Clamp` |
| `colorSpace` | `Gamma`, `Linear`: encoding of stored key components only | `Gamma` |

Pattern palette inputs explicitly default to Linear color mode; other settings use the table.
All settings survive copying, serialization and switching to a mode that does not use them.
`colorSpace` describes storage, not the selected color model. Color modes operate in linear
RGB, encoded RGB, OKLab and OKLCH respectively. Each interval between color keys uses one
shared RGB range: `min(0, A.r, A.g, A.b, B.r, B.g, B.b)` to
`max(1, A.r, A.g, A.b, B.r, B.g, B.b)`. RGB is clamped after color conversion and chroma
correction, with both bounds and the result in the same encoded RGB space, before output
decoding. This preserves signed HDR keys while bounding intermediate overshoot; ordinary
0..1 keys produce 0..1 RGB. Bounds expand continuously with the keys and are not per-channel.
Exact keys and Fixed retain their values. Alpha is an independent scalar curve using the same interpolation;
color-mode conversions, hue direction and chroma correction never apply to alpha.

Blend interpolates each neighboring pair with its midpoint and blends linear versus eased
weights using Smoothness. Balanced inserts midpoint nodes and uses monotone cubic interpolation
with shaped boundary spans; Smoothness controls its contribution. Smooth uses a fixed 17-value
easing table. Adaptive prepares a curve using neighboring keys; its midpoint changes the curve
shape and is not a guarantee of a half-color sample. Smooth and Adaptive retain but ignore
Smoothness. Curve preparation is cached and shared by Evaluate/Bake consumers.

`midpoint` is stored in 0.01..0.99, default 0.5; the final key has no following interval.
Blend and Smooth evaluate it within 0.05..0.95. Hue Path affects only Hue mode; achromatic
regions avoid arbitrary hue spins. Chroma Correction reduces opposing chroma with the
physical-interval envelope `[4u(1-u)]^3`; it is independent of alpha and Smoothness.
Fixed ignores midpoint, Smoothness, Hue Path and Chroma Correction. It keeps the established
step convention: choose the first key whose time is greater than or equal to the wrapped
coordinate, using the last key beyond that track. Color and alpha tracks are selected separately.
Clamp/Repeat/Mirror apply before evaluation; Repeat does not make different endpoint colors seamless.

The current clipboard envelope is version 2. Version 1, retired `mode`/`transition`, old
prefixes, color objects, numeric enums, unknown/duplicate fields and unsupported versions
are rejected, without migration or rewriting source files. The input limit is 262,144 characters.
The standalone envelope differs from the internal gradient object in `whimtex.document` v3.
User presets store it in `<user presets folder>/Gradients/<GUID>.json` (256 KiB maximum).

### Custom channel curves

`Custom` stores four independent channel curves. `colors`, `alphas` and `colorMode` always
describe the retained automatic gradient, including when Custom is selected; never replace
them with sampled or projected Custom colors. `customColorMode` describes the manual curves:
Linear/Gamma use R/G/B, Perceptual uses OKLab L/a/b, Hue uses OKLCH L/C/H (hue in degrees).
Channel 3 is alpha in every mode. `colorSpace` remains the output/key encoding, not the
manual curve's color model. Custom does not apply midpoint, Smoothness, Hue Path or Chroma Correction.

Each `customKeys` entry is an object such as
`{"id":1,"channel":0,"time":0,"value":0,"inTangent":0,"outTangent":0,"inMode":"Free","outMode":"Free","broken":false,"inWeight":0.33333334,"outWeight":0.33333334,"weightedMode":"Both"}`.
`id`, `channel`, `time`, `value`, `inMode`, `outMode` and `broken` are required in API/clipboard input.
Tangents default to zero; weights default to 1/3, and `weightedMode` defaults to `None`.
IDs are unique positive 32-bit integers.
`channel` is 0..3. Times are 0..1, strictly increasing within each channel with spacing
at least 0.000001. Each channel has 1..64 keys; the union of times of channels 0..2 has
at most 64 distinct positions, and alpha has at most 64. Values and tangents are finite
floats; alpha values stay in 0..1. Signed/HDR curve values are permitted.
Combinations whose encoded or linear RGB at a color-key position cannot fit a finite float
are rejected before replacing the previous manual data; there is no separate fixed cap on
the working-space channel values.

Keys are independent; there are no persistent links. The obsolete `link` field is rejected,
including `link:0`, without migration. Markers are derived by sorting all channels together
by time, then ID, and collecting keys within 0.00001 of the earliest remaining anchor.
This is not transitive: times 0, 0.000009 and 0.000018 produce two clusters. Color/alpha
tracks filter inside these same clusters and choose the first matching key, never regroup
filtered data. The combined track chooses the first key regardless of channel. A marker
does not merge or average the underlying values/times and may contain same-channel keys.
Curve edits move/remove only the addressed key; marker gestures freeze their ID set at
gesture start and move it with a common delta limited by 0..1 and unselected same-channel
neighbours. Deletion must keep every channel nonempty. See the exact atomic movement,
rounding and selection contract in the [C# API](../AgentAPI.md#custom-gradient-settings).
Each side has `Free`, `ClampedAuto`, `Linear`, `Constant` or `ConstantNext` mode.
Evaluation uses Unity `AnimationCurve` weighted cubic Bezier segments. `weightedMode` is
`None`, `In`, `Out` or `Both`. A weighted handle's horizontal length is its weight (0..1)
times the adjacent time interval; an unweighted side uses 1/3. The slope controls its height.
`broken:false` requires both modes to be `Free` or both `ClampedAuto`. Joined Free sides
share `outTangent`; their lengths remain independent. Auto slopes use Unity's ClampedAuto.
`Constant` holds the previous key; `ConstantNext` holds the next key and preserves Fixed
conversion and reversal of steps. Exact keys retain their own values. The latter mode wins
if both step directions are assigned to the same segment. Legacy `tangentMode` is rejected.
Native document/TIFF storage also requires `customCurveFormat:1` whenever manual data exists;
the API/standalone palette reader sets this from the validated key body. Older Custom data
is rejected explicitly, without conversion or overwriting documents.

Custom still uses the existing GPU palette resolutions and ordinary filtering; a Constant
tangent does not select the dedicated Fixed GPU path. Sharp Custom steps are therefore
limited by LUT sampling and may soften or shift within a sampling interval. The CPU
step convention does not promise the same rendered step behavior as GPU Fixed.

Active Custom requires explicit nonempty `customKeys` and `customColorMode`. Switching
or fitting automatic data is a UI/C# operation, not an implicit import conversion.
Other interpolation modes may retain these same fields; empty/null curves are allowed
only outside Custom. Invalid IDs, times, channels or nonfinite values are rejected.
Copy/inspect preserves both datasets and can be used as the body of a later gradient write.
The source RGB curves retain their entered values; final RGB uses the shared expanded
range at adjacent positions in the union of color-key times, and alpha is clamped to 0..1.

Changing `customColorMode` in a payload reinterprets its values. To preserve appearance,
use explicit **Convert Color Mode** or `ConvertCustomColorMode`: it approximately refits the curves,
may change keys/tangents, and retains the separate automatic gradient. The observed
conversion error is diagnostic rather than a certified bound.

GPU palette layout, dimensions, formats and filtering are unchanged in this CPU update.

## HLSL interface — shader-only or inside JSON

Preset headers may declare `// @group("Color")` after the effect marker and optional
`@control`, before HLSL declarations/includes. It creates a flat block within each Add FX
category submenu: groups and names sort alphabetically, with separators between groups.
The effect marker retains its `Category/Name` path. No `@order` is used.
Quoted preset groups have no end directive; unquoted `@group(Title)` / `@endgroup`
still controls parameter layout. The catalog returns `group` and searches it with `query`.
See [preset list groups](../ShaderFX.md#preset-list-groups).

FX may declare `// @icon("../Icons/name.svg")`, `// @icon("guid:...")` or a leading
`/* @icon-svg ... */` block. Keep these after the effect marker and optional `@control`,
before HLSL code. Icons do not change shader sampling or compilation. See the
[icon syntax, supported SVG features and transport rules](../ShaderFX.md#fx-icons).

FX inspector previews use `// @preview(Function)` for an independent image, or
`// @param preview(Function) float _Amount = 0.5 [0 .. 1]` for a Slider track.
Standalone images preserve the canvas aspect, fit the available Layer Settings width and cap their height at 128 logical pixels; no size metadata is required. Magnified canvases render at native canvas resolution with Point filtering, including native inspector inputs; reduced previews retain Bilinear filtering. Magnification includes Editor DPI scaling.
Other inline fields show a small right-side rectangle. Functions return linear straight
`float4` RGBA, take `float2 uv` first and optionally the declared parameter's shader value
second (standalone previews take UV only). Alternatively, `preview(Function, _A, _B)` or
`// @preview(Function, _A, _B)` explicitly passes declared `@param` values after UV in that
order, with exact shader types and no extra own value. Forward/hidden references are allowed;
unknown names and signature mismatches produce a local preview diagnostic. A listed own float
on a Slider uses its track candidate; other references and standalone entries use actual values.
All normal uniforms/helpers remain available;
the Slider candidate never overwrites the selected uniform. Guard preview-only functions
with `#ifdef WHIMTEX_PREVIEW`. See [preview types, inputs and limits](../ShaderFX.md#inspector-previews).

All FX and HLSL brush wrappers also expose the global baked Hue LUT:
`WhimTexHue(h)` returns HSV `(h,1,1)` RGB components, while `WhimTexHueLinear(h)` returns
linear color. Hue is measured in turns and repeats. The 1536×1 float texture is globally bound
as `_WhimTexHueGradient`, with `_WhimTexHueGradient_TexelSize`; do not shadow these with
material Properties or `@param`. See [shared hue lookup](../ShaderFX.md#shared-hue-lookup).

### FX conditional controls

Wrap rows in `// @if _Mode == 1` / `// @endif` to hide them when false, or
`// @enableif _Enabled == 1` / `// @endenableif` to leave them visible but disabled.
Both accept `==` or `!=` against a finite numeric or true/false literal and a declared scalar
or an explicit vector component such as `_Channels.a`. Drivers may be forward references or declared under
`@enableif`, but no driver declaration may be under `@if`. Unknown/vector drivers are
invalid when an actual parameter or standalone preview uses the condition; empty blocks
and headings/help boxes alone add no driver dependencies. Literal syntax is always validated.

Enabled blocks can nest with themselves and with one active `@if` in either order;
combined condition depth is at most 16. Nested `@if` is still forbidden. Close the
innermost condition with its own end directive, without arguments. All enclosing enabled
conditions must pass; an inner condition cannot override a disabled or read-only ancestor.
Fields, attached headings/help boxes and previews follow the same conditions. Values,
uniform uploads and HLSL behavior do not change. Preset exports preserve the blocks.
`ShaderParameterControl.enableIf` is an outer-to-inner `ShaderEnableCondition[]`, empty
outside enabled blocks; each struct contains `string parameter`, `bool notEqual`, `double value`.
Standalone previews carry the same metadata. See [parameter declarations](../ShaderFX.md#parameter-declarations).

### FX layout metadata

Named groups and foldouts share Copy/Paste/Reset on the entire header, including the foldout arrow and collapsed state. The commands include hidden and nested values and preserve expansion state; no extra metadata is needed. Containers holding only standalone previews and no parameters do not show a parameter context menu.

Built-in Chroma Key `_KeyColor`, Halftone `_PaperColor`/`_InkColor`, and Pixelate/Threshold `_LowColor`/`_HighColor` use `color(float3)`; supply three RGB components. Color Filter `_FilterColor` remains RGBA: its alpha scales tint strength. Inspector availability conditions never reset values or change HLSL execution.

Keep `// @group`, `// @group(Title)` or `// @group(Title; _HeaderParameter)` paired with `// @endgroup` for non-collapsible bordered groups. Add `// @foldout(Title)` or `// @foldout(Title; _HeaderParameter)`, paired with `// @endfoldout`, for collapsible content; the foldout title is required. A header parameter must be declared exactly once in the source, outside both `@if` and `@enableif` and directly inside its own group/foldout. Supported hidden header fields are promoted into the header. Header checkboxes edit values independently of folding and tab selection; use `@if` for dependent row visibility, `@enableif` for editing availability and HLSL for the effect behavior.

Use `// @tabs` or `// @tabs(Title)` around `// @tab(Page title)` / `// @endtab` pages, then `// @endtabs`. Page titles are required, non-empty and unique within one tabs block after trimming, with case-sensitive comparison. A page accepts `// @tab(Page title; _Enabled)` for a scalar toggle left of its label. Declare that toggle exactly once, unconditionally and directly in that page; hidden toggles are promoted and the body row is omitted. Other types are invalid. The checkbox changes only its value, without switching pages; page selection leaves it unchanged. Use `@enableif`/`@if` for dependent rows. The overall `@tabs` heading accepts no header binding. Put all parameters and standalone previews inside pages, never directly under `@tabs`. Empty `@tabs()` is invalid; omit parentheses to omit the overall title. Individual tab headers share group-header Copy/Paste/Reset actions for all values in that page, including hidden/disabled fields and descendants; sibling pages and shared fields are excluded.

```hlsl
// @tabs
// @tab(Color)
// @foldout(Tint; _Enabled)
// @param hidden toggle(float) _Enabled = true
// @enableif _Enabled == 1
// @param color _Tint = (1, 1, 1, 1)
// @endenableif
// @endfoldout
// @endtab
// @tab(Shape)
// @param float _Radius = 0.5 [0 .. 1]
// @endtab
// @endtabs
```

Groups/foldouts can nest inside pages and in one another, except that a group cannot open when the nearest active group/foldout is another group. `group → foldout → group` is allowed. Nested tabs are forbidden anywhere, including through a foldout. Maximum depth is 16 containers, counting pages. All container/page starts and ends must be outside both `@if` and `@enableif`; conditions may wrap rows inside containers/pages. Close the innermost container first; end directives take no arguments. Every container/page needs at least one non-hidden parameter declaration or standalone preview in its subtree; hidden declarations alone, even header bindings, do not satisfy this rule.

This is layout only: all values are still uploaded and the effect runs regardless of collapse or selected page; `@if` controls row visibility and `@enableif` controls editing availability. Tabs use content-width window-style tabs with the active tab joined to its content, no blank heading row for untitled `@tabs`, and no close/new/reorder controls. Per-FX choices live in UI-local `SessionState`, keyed by stable FX `ShaderKey` and container kind/id/title, outside shader values and Undo. They survive reloads in the current Editor session, not a full Editor relaunch. C# controls carry an outer-to-inner `ShaderLayoutContainer[] containers` path with `kind`, `id`, `title`, `headerParameter`. Flat `inGroup`, `groupId`, `groupTitle`, `groupHeaderParameter` retain only the nearest group/foldout summary. See the [full layout contract](../ShaderFX.md#layout-containers).

### Built-in noise library

WT noise is included automatically in Shader-layer Code, HLSL FX and Static/Dynamic
brush tips. Use `WTNoise*` and `WT_NOISE_*` public names; no copied source or
explicit include is needed. FastNoiseLite remains a kernel provider behind this API.
Shader Graph uses WT through its dedicated noise kinds. Noise Layer retains its separate
calculations; matching algorithm names and seeds does not guarantee parity with that layer.
Custom noise remains valid.

```hlsl
// @param float _Scale = 8 [0.1 .. 64]
float4 ApplyFX(PinnedUV uv, float4 color)
{
    WTNoiseSettings noise = WTNoiseDefaults(123);
    noise.scale = _Scale.xxx;
    noise.period = float3(1, 1, 0);
    noise.fractal.mode = WT_NOISE_FBM;
    noise.fractal.octaves = 4;
    float value = WTNoisePerlin(float2(uv.x, uv.y), noise);
    return float4(value.xxx, color.a);
}
```

Position type selects 1D/2D/3D (`float`/`float2`/`float3`); Triangle Voronoi accepts only
`float2`. Settings use X/XY/XYZ respectively.
Offset is applied before scale, in input units. Scale uses positive magnitudes; period is an
input-space repeat length per axis, with zero disabling an axis. Periodic lattice counts are
rounded per octave: the repeat length is retained while actual density can change. Keep
settings themselves periodic when they vary across the image, and avoid large coordinates
that lose fractional float precision.

Scalar `WTNoisePerlin`, `WTNoiseValue`, `WTNoiseValueCubic`, `WTNoiseOpenSimplex2`,
`WTNoiseOpenSimplex2S`, `WTNoiseFlow`, `WTNoiseCellular`,
`WTNoiseTriangleVoronoi`, `WTNoiseGabor`, `WTNoiseSpots` and `WTNoiseShard`
return 0–1 without gamma conversion.
`WTNoiseShard(p, noise [, shard])` uses `WTNoiseShardSettings` from `WTNoiseShardDefaults()`;
`sharpness=0.7` (clamped 0..1) controls soft slopes through sharp facets. It supports native
1D/2D/3D, common periods, precision and explicit footprints. Graph `ShardNoise` exposes
Position, Scale, Period, Seed and Sharpness inputs, plus Value; use the separate
Fractal node for octaves.

`WTNoiseTriangleVoronoi(float2 p, noise [, t] [, out firstOctave])` uses WT Cellular
feature points selected by a triangular gauge. `WTNoiseTriangleVoronoiDefaults()`
returns `WTNoiseTriangleVoronoiSettings`: distribution Uniform/FixedRadius (default
Uniform), jitter=1 (clamped 0..1), evolution=0, stretch=(1,1) (absolute components clamped 0.5..2).
With None and zero footprints, the scalar is `saturate(edgeDistance/sqrt(2))`; the
optional `WTNoiseTriangleVoronoiSample2D` contains distance, edgeDistance, value, id,
toPoint and borderNormal. Geometry remains first-octave and unfiltered.
Graph `TriangleVoronoiNoise` is 2D only, with Distribution, Jitter, Evolution and Stretch
plus the common domain inputs. Its Distance is Euclidean length to the triangle-selected
feature, edgeDistance is true dominance-boundary distance, and both use unstretched
noise-lattice coordinates. BorderNormal points toward the nearest boundary and is not
an analytic derivative. The gauge defines dominance regions, not a triangular mesh.
Deterministic ties resolve coincident affine-face equality plateaus, including at
Jitter=0; their interiors are not borders. Scalar filtering uses an approximate
cell-family mean, not the exact triangle-tile mean. There is no Gradient/Curl
support. See [Triangle Voronoi](../NoiseLibrary.md#triangle-voronoi) for the full contract.

Fractals are `WT_NOISE_NONE`, `WT_NOISE_FBM`, `WT_NOISE_RIDGED`,
`WT_NOISE_PINGPONG`, up to eight octaves, configured through `noise.fractal` (None by default).
Calls use `(position, noise [, familySettings])`. Cellular supports Uniform/FixedRadius.
Cellular and Triangle Voronoi settings share `evolution=0`;
Graph exposes a Float Evolution input. It is signed and unclamped, with no common
0..1 cycle. Zero preserves the static field. FixedRadius preserves length in 2D/3D;
1D projects a rotation between the endpoints. Spatial tiling holds for constant or
correspondingly periodic Evolution inputs. IDs stay attached to moving sites.
See [Evolution](../NoiseLibrary.md#evolution).
Its optional trailing `out WTNoiseCellularSample1D/2D/3D` exposes raw first-octave distances,
cell ID/value and direction, while the scalar return includes the requested fractal.
Cellular Distance to Edge uses Euclidean geometry; with Smoothness 0, None and zero footprints, multiply its scalar by
`sqrt(dimension)` to recover lattice distance within float rounding. Combined/filtered fields
have no such inverse. Scalar Cellular F1/F2 share one fixed scale for the selected metric/dimension.
With no fractal or filtering, Add is their mean, Subtract is F2 minus F1, Multiply is their
product, and Divide is F1/F2 (zero for zero denominator). Fractals combine these results per octave.
`c.smoothness` defaults to 0 and clamps to 0–1 for every scalar output. Positive values
blend Cell Value neighbors or smooth the F1/F2 pair used by distance combinations.
Use `WT_NOISE_CELL_DISTANCE_TO_EDGE` for boundary distance; it ignores Metric and uses
Euclidean geometry. Its smoothing softens interior ridges and is no longer exact distance.
The optional first-octave geometry stays hard and unfiltered. Separate Voronoi Edge and
Voronoise functions/settings and the SmoothF1 selector are removed, without aliases.
See [the unified Cellular contract](../NoiseLibrary.md#smoothness).
Cellular-family filtered means are folded ensemble approximations independent of seed/period;
small periodic tiles, nonlinear outputs and strong folds can depart from those estimates.

Shared `noise.dpdx`/`noise.dpdy` are `float3` footprints in **unwrapped input-position units**
before WT offset/scale/period. All defaults set them to zero for point sampling. Supply
`float3(ddx(p), 0)` / `float3(ddy(p), 0)` for a float2 fragment position, or explicit steps;
the library never evaluates screen derivatives implicitly. Former `g.dpdx/g.dpdy` fields
have been removed: move those assignments to the common settings, with no compatibility alias.
Filtering attenuates unresolved octaves using each family's approximation; it does not exactly
integrate nonlinear folds, weighted amplitudes or spatially varying settings.

Gabor's optional trailing `out WTNoiseGaborSample` exposes first-octave Value/Phase/Intensity/Phasor.
`g.profile` uses `WT_NOISE_PHASOR_SINE`, `WT_NOISE_PHASOR_TRIANGLE` or `WT_NOISE_PHASOR_PULSE`;
`g.phaseOffset=0` cycles and `g.width=0.5` affect only `sample.phasor`. The scalar return and
existing outputs are unchanged. All outputs reuse the same impulse traversal; the profile
uses analytic phase filtering. See [Gabor](../NoiseLibrary.md#gabor) for its approximation and singularities.
`WTNoiseGaborSettings.bandwidth` defaults to 1 and clamps to 0.5–4: support radius is its
reciprocal in lattice units, independent of carrier `frequency`. Carrier/envelope filtering
uses the shared footprint. `WTNoiseSpots(p, noise [, spots])` creates positive soft spots,
with `WTNoiseSpotSettings` from `WTNoiseSpotDefaults()`: density 1 (0–4 centers/cell), radius
0.35 (0–2 lattice units), softness 0.5 (0.01–1), elongation 1 (1–8), direction +X.
Elongation compresses transverse axes at fixed radius; direction/elongation are ignored in 1D.
Spots has no phase/carrier, auxiliary sample or Gradient/Curl overloads. Its filtered mean
uses an approximate overlap model; detailed bounds and limitations are in the reference.

`WTNoiseFlow(p, noise [, flow])` uses shared OpenSimplex2S kernels with rotating gradients.
`WTNoiseFlowSettings.phase` is in cycles, defaults to 0 through `WTNoiseFlowDefaults()`,
and repeats at 1. Phase 0 is the static unrotated field; 1D is a fixed 2D slice. Set phase
explicitly (for example `_WhimTexTime * 0.25`); Flow does not read time automatically.

`WTNoiseGradient` and 2D/3D `WTNoiseCurl` are signed, unnormalized analytic fields.
The optional basis algorithm after `noise` selects Perlin (default), Value, ValueCubic or
OpenSimplex2/2S. Pass `WTNoiseGaborSettings` or `WTNoiseFlowSettings` instead of the algorithm
ID for that family: `WTNoiseGradient(p, noise, g)` / `WTNoiseCurl(p, noise, flow)`.
They include fitted input-space scale, filtered octaves and weighted-fractal amplitude
derivatives. Position is differentiated with settings, phase and footprints held constant;
local slopes at clamp/fold cusps are zero. Gradient supports 1D/2D/3D; Curl supports 2D/3D.
The derivatives describe the implemented filtering approximation, not an exact pixel integral.
WT ValueCubic 2D/3D fixes incorrect `+2` lattice neighbors from signed constant overflow;
those images can change from earlier WT results. Non-WT FNL arithmetic is retained.
`WTNoiseDisplacement` returns an offset to add to the input, with its own warp modes and
Strength; octave count, lacunarity and gain come from `noise.fractal`.
`WTNoiseWhite`/`WTNoiseBlue` return RGB through `WTNoiseGrainSettings`: pixel grain size,
pixel offsets, 1D direction, Color and Inverted. Blue textures are supplied globally.
See [WT noise library](../NoiseLibrary.md) for exact settings, overloads, output units and limits.

For an arbitrary HLSL field, declare
`WT_DEFINE_FRACTAL(Name, Source, PositionType, ValueType)` after defining
`ValueType Source(PositionType position, int seed)`. It generates
`ValueType Name(PositionType position, WTNoiseSettings settings)` in Shader-layer Code,
FX, Static/Dynamic brushes and CustomFunction. PositionType is float/float2/float3;
ValueType is float/float2/float3/float4. `settings.fractal` keeps its existing defaults and
bounds; None evaluates one octave. Offset/Scale precede octave frequency; seed increases
by one each octave. The graph-style weighted mean applies component-wise, preserving
signed/HDR FBm values without color conversion. Explicit unwrapped dpdx/dpdy attenuate
octaves; zero means point sampling. Period wraps input coordinates but cannot make an
arbitrary source smoothly seamless; no analytic Gradient/Curl is generated. Built-in
WTNoise family filtering, period fitting and derivatives remain available through their
existing calls. Graph Fractal keeps its own 2D domain and SeedStep behavior while sharing
the accumulation math. See [custom-function fractals](../NoiseLibrary.md#fractals-of-custom-functions) for an example and limits.

The same noise calls work inside Static or Dynamic `BrushTip` signatures;
the script still starts with `// @whimtex-brush Category/Name`.
For independent random values per stroke or stamp, use the brush's
[indexed `BrushRandom` helper](BRUSHES.md#dynamic-context-and-randomization).
It is not spatial noise; the WT noise recommendation does not replace it or rule out
custom hashes and specialized algorithms that the library does not provide.

### Built-in distortion controls

Distortion presets expose `_Strength` (Spherize, Radial Shear), `_Angle` (Twirl, degrees)
or `_Amount` (Polar Coordinates, Displacement Map) in the FX header. Amount defaults to 1,
preserving the full configured mapping; zero returns the original image. Polar Amount
interpolates sampling coordinates (hard range 0–1). Displacement Amount multiplies vector
X/Y strength, Grayscale strength or Parallax depth (range `[0 .. ~2]`); `_Mix` remains an
independent output blend. Spherize Sphere clipping and Radial Shear Offset remain when
their existing Strength is zero. See [the shader reference](../ShaderFX.md#distortion-header-controls).
Polar Coordinates has independent `transform2D` frames: `_Input` selects the source,
`_Output` places the output. Both default to position `(0.5, 0.5)`, size `(1, 1)`, rotation `0`.
To Polar maps Output's circle to Input's strip; From Polar maps Output's strip to Input's circle.
Sampling is `_Input_ToInput(P_or_Q(_Output_ToLocal(uv)))`; reverse Mode and swap frames
for the inverse coordinate map. Both use standard Edit on Canvas/reset controls.
Old one-frame layouts and previous parameter names are not converted.
Distortion presets expose **Tiling** (`_Tiling`); UV Transform uses **Input Tiling** (`_InputTiling`):
`Clamp: 0`, `Repeat: 1`, `Mirror: 2`, `Clip: 3`. Map wrapping is independent. UV Transform
defaults to Clip, distortions to Clamp. These modes read the existing input image, not
unbounded procedural noise outside its raster.

### Built-in edge contours

`Stylization/Edge Outline` offers Color (RGB differences), Luminance (brightness only)
and Hue (color tone) detection with two methods: Boundary (default) creates uniform
contours; Scharr weights opacity by a 3×3 gradient. Strength amplifies Scharr only and is
hidden in Boundary. Detection sensitivity, Thickness, Shape, Softness, tint and Output
are shared. Broad transitions may produce bands in Scharr; thick Scharr contours cost more.
Hue Threshold is the shortest color-wheel difference in
degrees (0–180); Min Saturation excludes a boundary if either side is too muted. Gray
and black never seed Hue boundaries. Threshold applies to Color/Luminance only.
Detection sensitivity filters transitions independently of Thickness
(0–32 canvas pixels). Shape selects Round, Square or Diamond joins; Softness controls the
line edges. Overlay preserves source alpha; Outline Only emits transparent contours within
source coverage. Neither mode outlines alpha-only silhouettes. Use the linked preset when
available rather than duplicating its code; see [the shader reference](../ShaderFX.md#edge-outline-preset).
`_Opacity` (0–1, default 1; FX header control) blends the complete selected output with
the original input. Zero leaves the image unchanged, including in Outline Only.

### HSV and seeds

`Color/Hue & Saturation` replaces HSV. `_MasterHue`, `_MasterSaturation` and `_MasterLightness`
correct the whole image; four `_Range1`…`_Range4` groups override Master inside selected hues.
Each range has Enabled, a float2 Hue Range and Feather (degrees), signed Hue, Unify Hue, Saturation
and Lightness relative to the source, not to Master's output. Saturation scales distance from
weighted gray (0 removes color, 1 is unchanged, above 1 boosts it); Lightness uses -100…100
with neutral 0, reaching black/white at its endpoints. It is not HSV Value or a brightness multiplier.
`_Range1UnifyHue`…`_Range4UnifyHue` are floats from 0 to 100, default 0. They pull source hues
toward their forward range center along the shortest arc, then apply that range's signed Hue shift.
At 100 and full coverage the target is center + Hue; at 0 the previous result is retained.
Unification preserves HSV saturation/value, alpha and HDR; ordinary Saturation/Lightness still apply afterward.
Feather, the source saturation gate and normalized overlaps scale the hue offset; source masks do not change.
Master and Range 1–4 retain five
tabs in an untitled block, with Enabled checkboxes in the tab headers. Master opens first. All ranges start
disabled; `@enableif` keeps their body controls visible and gray while preserving values.
Source Saturation (HSV) and Output remain shared below the tabs; selection still measures the original HSV saturation.
With no enabled ranges, Master uses only the correction without selection/overlap work.
Use the `_Master*` names for whole-image correction; there is no second HSV preset and no Value-to-Lightness alias.
Hue ranges use `[0 % 360]`: `[330, 30]` crosses red, `[0, 360]` selects the full circle,
and equal stored endpoints have zero width. Anchored full cycles such as `[-330, 30]` retain
one positive turn in storage while both handles display their meeting angle. The preview track shows one hue turn.
Ranges use source hue; overlaps average only range correction offsets. Feather and the shared
`_SaturationRange` cutoff/full-strength endpoints blend overrides back toward Master. Gray pixels use Master.
To desaturate everything except blue, set `_MasterSaturation` to 0, `_Range3Enabled` to true,
`_Range3` to `[210, 270]` and `_Range3Saturation` to 1, then adjust endpoints/Feather.
Output selects Result or source range masks (unaffected by Master), and `_Opacity` blends
that output with the input. See the
[preset contract](../ShaderFX.md#hsv-preset) for boundary and alpha rules.

Declare an explicitly randomizable seed as `// @param seed(float) _Seed = 0 [0 .. ~10000]`.
It remains an ordinary uniform. Random chooses an integer within both declared bounds;
the finite bounds must be within −16777216…16777216 and contain at least one integer.
Manual input keeps normal hard/soft rules. Preview/save/load never randomize. Labels, groups,
conditions, `hidden` and repeated controls work normally; FX edits can explicitly invoke
[`randomizeSeeds`](../AgentAPI.md#fx-edits-and-presets). The same modifier works in HLSL brush tips.

### Effect entry point

Write a fragment function, **not a complete ShaderLab shader**:

```hlsl
// @whimtex-effect Color/Invert
// @param float _Amount = 1 [0 .. 1]
float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, 1 - color.rgb, _Amount), color.a);
}
```

The supported signatures are `float4 ApplyFX(float4 color)` and
`float4 ApplyFX(PinnedUV uv, float4 color)`. Define exactly one; parameter names may vary,
and `const`/`in` are optional. `ApplyFX(float2, float4)` is no longer supported.
`PinnedUV` is immutable and converts to `float2` implicitly or explicitly. Arithmetic,
swizzles and numeric helpers lose the pin; conversion back, construction and mutation are
forbidden. Same-type copies and pinned helper arguments/returns retain it.
Locality follows the coordinate type in reachable `SampleInput`/`SampleTexture` calls:
pinned is local; numeric coordinates or explicit tiling are spatial. Procedural UV math
without image reads remains local. Unverifiable macro/bypass dependencies use a conservative path.

**`SampleInput` and `SampleTexture` are the primary WhimTex API for reading textures.**
`SampleTexture(_Mask, uv)` reads a declared `texture2D` parameter, including Texture, Layer,
Self and None sources. Both reading functions accept pinned or numeric coordinates;
only unchanged pinned coordinates confirm a local read.
`SampleInputLod(uv, lod)` and `SampleTextureLod(_Mask, uv, lod)` select an explicit mip
level through the same texture-view mapping. Both return straight RGBA, preserve HDR,
and accept `PinnedUV` through its numeric conversion. Both are always spatial, even
at LOD 0 with unchanged coordinates: compilation reports `spatial` / `spatial_sample`
without a bypass warning unless another operation is unverified. Use them for sampling
inside data-dependent loops, as in Edge Outline, or for base-level derivatives, as in
Height to Distortion. LOD 0 is the supplied texture's base
level, including reduced previews; these functions do not create mipmaps or request
native document resolution. Mip availability, filtering and wrapping follow the sampler.
`_MainTex` and declared `texture2D` parameters have the read-only HLSL type
`WhimTexTexture`. Use that type for helper/preview arguments and texture return values,
not `sampler2D`. `SampleTexture` maps whole-input UV to each texture's own stored region;
call sites such as `SampleTexture(_Mask, uv)` do not change. `.texelSize` and
`_Name_TexelSize` contain `(1/W, 1/H, W, H)` for the complete input at its render scale,
even if only a crop is stored. Copies retain that metadata. Construction, casts, mutation
and `out`/`inout` are invalid; `.pixels`/`.uvMap` access is an unconfirmed bypass.
Gradient/curve lookup samplers and brush-tip texture parameters keep their existing types.
Verified pixel-local FX use the visible canvas region. Each referenced layer independently
renders its spatial FX prefix completely before cropping; locality does not propagate
from a consumer to its dependencies. Inspector callbacks use a separate complete input
reduced to at most 512 pixels, so they can sample outside the current canvas view.
Save/export and agent renders still evaluate complete images.
An untransformed Shader with built-in passthrough Code and Composite Below can reuse the regional stack backdrop. A Shader
transform that moves/resamples the input or a spatial FX prefix requires the full
backdrop. Pass-through groups inherit their external backdrop; isolated groups do not.
Direct `tex2D`, resource `.Sample`/`.Load` and similar HLSL reads are bypass paths. Detected
bypasses or derivatives prevent pixel-local confirmation and produce a warning rather than
disabling an otherwise valid shader. This is an authoring contract, not a sandbox.
`ShaderFX.IsPixelLocal` describes the applied usable shader, not pending edits;
`Layer.AreActiveFxPixelLocal()` checks active non-null FX on that layer only, with an empty chain true.
Unavailable/unconfirmed active FX return false; disabled FX are ignored.
The built-in Mask preset samples `SampleTexture(_Mask, uv)` with `PinnedUV` and is pixel-local.
It has no `_Transform` parameter; channel selection, Profile, Invert and Apply To remain available.
Its inspector preview has a separate float2 entry and does not change production locality.
A spatial FX in the referenced mask layer can still require a full render of that dependency.

See [pixel-local rules and mask example](../ShaderFX.md#pixel-local-entry-points-and-texture-reads).

`color` is the incoming straight (not premultiplied) RGBA. `SampleInput(uv)` reads that same
input at another UV, including earlier FX. Return straight RGBA in linear working space.
Shader receives the lower composite; a regular layer's FX receives that layer's image.
To generate an image from scratch, use a Color layer with FX replacing its color.

`SampleInput(uv, tiling)` uses spatial addressing: Clamp (0), Repeat (1), Mirror (2), or
Clip (3; transparent outside UV 0–1). Repeat filters across opposite edges and respects
the current input's Point/Bilinear filtering without changing texture import settings.
Expose tiling through a normal enum `@param`. The one-argument function is unchanged.

`LayerToLocal(uv)` converts canvas UV to the owning layer's local UV, including parent group transforms and perspective. Use it for procedural shapes that must follow the layer transform. `ApplyFX` UV and `SampleInput` remain canvas-space; do not pass local UV to `SampleInput`. The helper does not wrap or clamp coordinates.

Available inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`
(width, height, reciprocal width, reciprocal height), `_RenderScale`, and `_PixelScale`;
`UnityCG.cginc` is already included. `_PixelScale` is the document's logical-pixel multiplier,
default `1`; texture dimensions, texel sizes and `_RenderScale` remain physical pixels.
`_PixelScale` is a canvas-FX input, not an HLSL brush-tip input.
Do not redeclare these or generated parameters/helpers. Prefer explicit parameters for predictable
time-dependent FX. Unity time inputs such as `_Time`, `_SinTime`, `_CosTime`, `_TimeParameters` or
`unity_DeltaTime` are allowed and do not block compilation, but WhimTex does not control their updates:
Canvas, thumbnails and export may differ. Their use disables reusable result caching and produces
a warning in Diagnostics and agent compilation `diagnostics`/`warnings`, not `errors`.
Unity time warnings use the shared FX diagnostic list and Console reporter, just like compiler
and declaration messages. Console deduplicates each source path + severity/location/message until
scripts reload; repeated Apply, agent preflight and rendering do not repeat it. UI/API diagnostics
are never suppressed. Errors and warnings retain their respective severity even when both occur.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Bias = 0 [.. 10]
// @param float _Amount = 10
// @param px float _Radius = 3 [0 .. ~64]
// @param px float2 _PixelOffset = (0, 0)
// @param toggle(float) _IncludeAlpha = false
// @param float2 _Offset = (0, 0)
// @param float3 _Direction = (1, 0, 0)
// @param normal _Normal = (0, 0, 1)
// @param point _Center = (0.5, 0.5)
// @param float4 _Channels = (0, 0, 0.5, 1)
// @param color _Tint = (1, 1, 1, 1)
// @param texture2D _Input = self
// @param texture2D _Optional = none
// @param texture2D _Mask
// @param gradient _Ramp
// @param transform2D _Area = (0.5, 0.5, 0.75, 0.75, 30)
```

No semicolon on metadata lines. Ranges apply to floats only. Defaults must be finite.
For canvas FX, the `px` modifier marks artistic lengths in logical document pixels. It supports `float`,
`float2`, `float3` and `float4`, combined with `hidden` and `label(...)`; it cannot be combined
with seed, enum, toggle, color, normal, point or resource parameters. Every declaration of
the same variable must agree on `px`. Stored values, UI values and declared ranges remain
logical pixels; FX upload multiplies these numeric values by `_PixelScale` exactly once.
Do not multiply marked parameters by `_PixelScale` again in HLSL. For unmarked custom
calculations, use `_PixelScale` explicitly for artistic pixel lengths or grid spacing;
keep physical texture indices, sampling precision and antialiasing separate.

HLSL brush tips render into their own fixed-resolution texture. Their numeric lengths,
including declarations marked `px` through the shared metadata parser, stay in that texture's
own units and do not receive the document multiplier. Pixel Scale already scales the physical
brush diameter when the texture is stamped; multiplying the tip's lengths again would scale
it twice. Changing document Pixel Scale does not rebake or resize the stored brush-tip texture.

`[min .. max]` clamps edits through that control. Put `~` before a boundary value to allow crossing it:
`[0 .. ~2]` allows values above 2, `[~0 .. 2]` allows values below 0, and `[~0 .. ~2]` allows both.
The slider always stays within 0..2; numeric input and label dragging obey only the hard boundaries.
For example: `// @param float _Strength = 5 [0 .. ~2]` or `// @param float _Strength [~0 .. ~2]`.
Ranges with a soft boundary require both values and `min < max`; one-sided or equal soft bounds are errors.
The old `~[0 .. 2]` syntax is rejected. Boundary flags survive FX and HLSL brush preset export.
Bounds written inside HLSL still apply independently.
For FX and HLSL brush tips, `toggle` displays a checkbox and defaults to a `float` value of `0` or `1`. `toggle(type)` chooses its exact scalar or vector type. An optional default is `true`/`false` or `1`/`0`; ranges are not allowed. Use `if (_IncludeAlpha > 0.5)` or use it directly in arithmetic.

FX and HLSL brush tips also support dropdown controls and repeated declarations of one variable:
```hlsl
// @param float _Strength = 0.63 [0 .. 1]
// @param enum _Strength { Low: 0.2, Medium: 0.5, High: 1 }
// @param enum _Mode = SoftLight { SoftLight: 0, HardLight: 1, CustomBlend: 0.5 }
```
Any FX or HLSL brush parameter declaration may end with `// tooltip text`, for example:
`// @param float _Strength = 0.65 [0 .. 1] // How strongly to apply the effect.`
This literal, single-line text appears on hover and is preserved when exporting presets.
Repeated declarations can have separate tooltips for their separate controls.
Enum option names are unquoted identifiers, used only as nicified UI labels, never as HLSL constants.
Each option requires an explicit finite value, including fractional values; duplicate names or values
are errors. Defaults may be option names or numbers. Unknown numeric values display as Custom.
All parameter types allow omitting `= value`. The last explicit default for a variable wins; if none
exists, scalar/vector/color defaults are zero. Repeated `float`/`toggle(float)`/`enum(float)` controls share one float
value. Linked controls must have matching HLSL value types and compatible storage; color storage stays separate. Control ranges do not clamp values set through
another control. Preset export saves the current value once.
Unbounded `float2`, `float3` and `float4` are raw vectors with two, three and four components. `point` is a `float2` position in normalized canvas UV: bottom-left `(0, 0)`, top-right `(1, 1)`. Coordinates and tuple defaults may lie outside these bounds. Its hand button (**Edit on Canvas**) activates a handle that can also be dragged outside the canvas. Its default is `(0.5, 0.5)`; an explicit tuple is optional and ranges are not accepted. `normal` generates a normalized `float3`; its default and zero-vector fallback are `(0, 0, 1)`. It also offers an on-canvas direction handle; no range is accepted. Defaults are optional. Unknown parameter types are rejected.

`// @param preview(PreviewRange) float2 _Range = (0.2, 0.8) [0 .. 1]` is an ordered interval using a native MinMaxSlider and X/Y fields. It requires two hard finite limits with min < max and min <= X <= Y <= max; omitted defaults select the whole interval. `[0 % 360]` instead permits wrapped start/end pairs such as `(330, 30)` and wraps dragged endpoints across track edges. `(0, 360)` is full; equal stored endpoints are empty before Feather. Full cycles can also retain their meeting angle with endpoints one positive period apart: `(-330, 30)` or `(330, 690)`. At least one endpoint must be within the limits; all nonfull pairs must have both endpoints within them. The UI wraps full-cycle coordinates to the displayed angles but retains their winding in storage and shader values, so the handles stop where they meet rather than resetting to the limits. A scalar `float` also accepts `%`: ordinary slider dragging, repeating manual input (`370` → `10`, `-10` → `350`). Missing/soft `%` limits are rejected. Inline preview accepts `float2 uv` followed by optional `float2 value`: UV spans the whole track, value contains both actual endpoints. No extra JSON type is needed: values remain two-number arrays. Live FX snapshots expose `cyclicRange`. See [float2 intervals](../ShaderFX.md#float2-intervals).

FX use ordinary input images and explicit parameters, not hidden layer-specific data. Lighting/Bevel Emboss reads a height texture (Self by default) and shares lighting with Normal Map/Lighting. Base Color alpha blends transparent lighting (0) into the shaded surface (1); Output selects Both/Highlight Only/Shadow Only for the transparent part. SDF inputs use the selected layer output; use Gradient or LinearData for a scalar height map. MultiChannelData remains an ordinary RGBA texture input, with distance data in alpha; it does not introduce a typed SDF parameter or hidden field access. Normal Map/Lighting uses `_InputEncoding`; Normal Map/Normalize uses `_Encoding`. Both are enum parameters: `Linear:0`, `Gamma:1` (default Gamma). Gamma means sRGB-encoded channel values; Linear uses raw channels. See [shader reference](../ShaderFX.md) for the complete contract.

`float4` is a raw vector; `color` is a color picker. Texture parameters without an explicit default keep Texture mode and sample white when empty. Use `= none` for transparent black, or `= self` to sample the current layer immediately before this FX (including earlier FX, excluding current/later FX). Both are unquoted declaration keywords and are preserved in exported presets. These are FX parameter defaults, not layer IDs. Users can change the texture source in the editor. In unified JSON, existing texture assets use `$asset` identity references in the parameter value. Do not invent asset identities. Drawing pixels and URL downloads are not part of this format.
Names generate labels: `_NoiseScale` → Noise Scale. No need for a second uniform declaration.

Use `// @param curve _Profile = one` for a constant 1 curve with keys (0,1) and (1,1).
Curve defaults also accept `easeIn` (`t²`) and `easeOut` (`1-(1-t)²`), for example `// @param curve _Profile = easeIn`. Both span (0,0) to (1,1).

`curve` declares a scalar mapping for FX or HLSL tips: `// @param curve _Profile`, sampled with
`_Profile_Sample(t)`. Default: linear (0,0) to (1,1). Input clamps to 0..1; output is unrestricted.
Named defaults: `// @param curve _Profile = linear` or `// @param curve _Profile = easeInOut`.
The latter smoothly eases between the same endpoints with horizontal endpoint tangents.
For clipboard FX, an optional default can be included directly in `fx[].program.draftCode`:
`// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))`.
Each tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
Use strictly increasing finite times, finite values, weights 0..1 and mode 0/1/2/3
(none/in/out/both). Tangents additionally allow `inf` or `-inf` for steps. Maximum 256 keys;
`keys()` evaluates to zero. No range. See [curve reference](../ShaderFX.md#curve-parameters)
for sampling precision and preset persistence. This is not a layer property.

`gradient` for FX or HLSL tips is declared as `// @param gradient _Ramp`, optionally with two endpoint colors:
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint may be `#RRGGBB` (opaque),
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Without an initializer it starts opaque
black-to-white (Perceptual color mode, Balanced interpolation). Explicit two-endpoint defaults use Gamma/Blend with Gamma key encoding, Clamp, Chroma Correction 0,
Smoothness 0 and midpoint .5 to preserve the endpoint-only HLSL export contract.
The user can edit colors, HDR, alpha and interpolation in the gradient field.
The same hex forms are accepted for `color` defaults; color defaults also accept numeric RGBA tuples.
Call `_Ramp_Sample(t)` for straight linear RGBA; the gradient's Wrap setting controls `t` (Clamp by default).
For example, `return _Ramp_Sample(uv.x);`. Do not declare a sampler yourself. A cached 512×2
LUT supplies the samples; editing keys does not recompile the shader. Edited values persist in the
owning effect/brush; two-endpoint defaults are exported with HLSL presets. Active or retained
Custom curves cannot be encoded by that endpoint-only syntax, so export rejects them instead
of discarding manual data. Save the gradient separately or keep it in the document. Brush-specific texture
sources and tip coordinates are described in the [brush contract](BRUSHES.md#declared-parameters).

Transform2D uses `(centerX, centerY, width, height, angleDegrees)` in normalized input units.
Omitted default means the full image. For skew/perspective, use `// @param transform2D _Area = matrix(1, 0.2, 0, 0, 1, 0, 0.15, 0, 1)`: nine row-major values mapping local UV to input UV. The matrix must be invertible with no horizon crossing the unit rectangle. Do not combine matrix and TRS defaults.
Generated helpers are `_Area_ToLocal(uv)` and `_Area_ToInput(localUV)`; both support perspective.
Local `[0,0]`/`[1,1]` are corners and `[0.5,0.5]` is center. Out-of-range coordinates are valid.
There is **no automatic mask or falloff**: implement it in HLSL if desired. The user can edit the
frame with green canvas handles. Never reference reserved `_WhimTex_` internal uniforms.

For a `.hlsl` catalog preset the **first physical line** must be `// @whimtex-effect Category/Name`:
no blank line or license header before it. For inline/JSON FX this header is optional.

Linked presets also declare `// @preset("12bc047d-f5b2-43e4-91fd-84e0c2276cde", 1)` once in
the initial comment header, preferably immediately after the marker. The ID and positive integer
version are required together; nonzero GUID D and N forms are accepted and stored as lowercase N.
Omit this directive for an independent embedded copy, including project/package presets; Unity
asset GUIDs do not supply a missing preset ID. Project/package and external user-library links
both require an exact version match. Missing, broken, conflicting or differently versioned sources
use the saved document program while retaining the link for later retry. TIFF/JSON store
`program.presetId` and `program.presetVersion` with both modes and captured sources; source paths and dependency hashes
are transient. `shaderKey` remains the separate FX instance identity. For connected operations,
use the ID returned by `whimtex_fx_catalog`: declared preset ID when available, otherwise source
path. Do not copy an example GUID when creating a new source; generate a new GUID or use
**Save Preset…**, which creates a new ID/version 1 and preserves an overwritten destination’s
valid ID/version. See [preset identity and version](../ShaderFX.md#preset-identity-and-version).
To encode HLSL inside JSON, use a string with `\n` for newlines; escape quotes normally. JSON escaping
is decoded before compilation. Keep generated FX self-contained and use the built-in helpers.
The render fallback expands custom project/package and user-library includes; engine includes and
external texture inputs remain dependencies. Code whose dependencies cannot be expanded still needs
repair before it can serve as a usable fallback on another machine.

For additional engine-specific authoring details, see [Shader authoring](../ShaderFX.md).

### Document lights

Documents may contain `document.lights`, an array of up to 64 sources. Each source stores a stable `id`, `name`, `enabled`, `type` (`Directional` or `Point`), linear HDR `color` RGBA, nonnegative `intensity`, `position` XYZ, `direction` XYZ, positive `range` and positive `falloff`. Position/range use document pixels with bottom-left XY and height Z. Direction points toward the source. A Directional source's position is only its editor handle location. Lights are saved in both TIFF and document JSON.

Use Normal Lighting or Bevel Emboss `_LightingMode = 1` for document lighting (`0` keeps Local). Shader Graph Normal Lighting uses `option:1` for Global and `option:0` for Local. Light Count/Direction/Color/Attenuation nodes expose source slots for custom graphs. HLSL receives `WTLightCount`, `WTSampleLight` and `WTGlobalLighting`; see [document lighting inputs](../ShaderFX.md#document-lighting-inputs). Source RGB remains HDR; keep the host layer's Color Range appropriate for values above one.

### Typed numeric shader parameters

The field syntax is `toggle(type)`, `color(type)`, `enum(type)` or `seed(type)`; defaults are float, float4, float and int. Toggle/color accept bool/int/uint/half/float scalars and vectors2/3/4; enum accepts numeric scalars; seed accepts int/uint/float. `rgba` changes component labels only. Use `rgba toggle(float4)` for channel switches and explicit vector components in conditions. `gradient`, `curve` and `transform2D` are unchanged. API parameter values use JSON booleans for bool, numbers for other scalar types and arrays of the exact component count for vectors. Integer endpoints remain exact. FX reads expose `valueType` and each control’s `field`, `valueType` and `componentLabels`. Float/half color values retain encoded-color semantics; integer/bool colors are raw numeric values, without 0–255 scaling. Seed values must be integral; float seeds stay within ±16777216. See [the shader parameter contract](../ShaderFX.md#field-presentation-and-numeric-type).

Shader graph annotations use optional `notes` and `groups` arrays, separate from executable
`nodes`. Notes store title/text and a position/size; groups store member node/note IDs in `elements`.
A member belongs to one group, and groups cannot nest. Both arrays are limited to 512 entries.
See [the annotation contract](../ShaderLayer.md#notes-groups-and-node-messages) for fields and defaults.

Caustics graph operators: `CausticsNoise` generates a 2D water surface, while `Caustics` accepts scalar `Height`. Both return HDR `Value` through graph-managed projection passes and store their editable graph, not generated textures. See [Caustics nodes](../ShaderLayer.md#caustics-nodes) for ports, defaults and buffer boundaries.
