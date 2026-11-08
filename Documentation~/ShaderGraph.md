---
title: "Shader Graph preview contract"
parent: "Technical reference"
nav_order: 7.2
lang: en
permalink: /reference/shader-graph/
search_exclude: true
---

# Shader Graph preview contract

Graph authoring and graph storage remain described by the
[Shader layer contract](ShaderLayer.md). The artist workflow is in the
[Shader layer guide](en/shader-layer.md).

## Independent Buffer compilation

Explicit **Buffer** nodes define native compilation boundaries. The producer passes
that render their source branches and the consumer shader that samples their textures
compile separately. A shared executable cache reuses unchanged compiled programs;
an edit on one side does not by itself require recompilation on the other when that
program remains unchanged. This uses existing Buffer nodes, with no automatic Buffer
insertion or new node settings.

Reusing an executable does not freeze the buffered image. Dependency changes,
including input values, textures and time, still invalidate and rerender affected
textures even when no shader recompilation is needed. Nested buffers retain their
dependency order.

The existing [Buffer contract](ShaderLayer.md#buffer-and-fractal-nodes) still applies:
the source is captured over local `0..1`; **Canvas** resolution follows the current
render target and **Custom** uses the exact **Size**. Point/bilinear filtering,
**Wrap** behavior and float32 storage of negative/HDR values are unchanged.
**Buffer → Fractal** still requires an unconnected **Seed Step=0**, even when a
connected value would currently be zero; **Fractal → Buffer** stores the completed
fractal result.

## Separate exact Global lighting

Eligible **Normal Lighting → Global** evaluations use a surface-input pass and a
separate evaluator that calls the existing `WTGlobalLighting`. This retains the
document's light calculations, including Area and Cookie; it does not introduce a
different lighting model. Local lighting and the simplified node-preview light
keep their existing paths. The split requires at least four simultaneous render
targets and `CopyTextureSupport.Basic`. It adds no graph nodes or user settings.

The surface-input pass writes four linear RGBA float32 targets:

| Target | RGB | Alpha |
| --- | --- | --- |
| 0 | Surface color | Surface alpha |
| 1 | Decoded normal | Ambient |
| 2 | Canvas/world XY and Height | Roughness |
| 3 | Specular color | Specular Intensity × Specular Color alpha |

Surface preparation and lighting evaluation run in tiles of at most 512 × 512
pixels, with smaller tiles at the image edges. The evaluator loads the four inputs
at the same pixel within each tile. Multi-tile results are assembled into the full
linear RGBA float32 result with `Graphics.CopyTexture`. The consumer reads the
corresponding screen pixel, accounting for the render viewport. Negative/HDR values
and surface alpha are retained without display-color conversion or normalization.
Output AlphaScale remains in the consumer. Each native program still goes through
source preparation and shader diagnostics; the MRT wrapper changes the producer's
outputs, not its graph inputs.

“Exact” refers to the lighting calculation, not bit-identical floating-point output.
Materializing intermediate values in float32 passes introduces rounding points and
can change compiler reassociation. Narrow specular highlights can amplify small
input differences. Validate against the inline reference at matched time, dimensions
and inputs with explicit error tolerances and nonfinite checks; float-pass rounding
does not excuse coordinate, sampling or alpha errors.

The split shares the existing ordered Buffer runtime and executable cache. Changes
to bound values, textures, time, lighting, transforms or viewport invalidate the
dependent images. The four input targets and temporary lighting-output tile are
released after tile evaluation, including on failure; the full lighting result
remains cached. Active temporary working storage is at most 20 MiB: four input
targets plus one output tile, each with 16 bytes per pixel at 512 × 512.
Each retained result costs another 16 bytes per pixel: 4 MiB at 512²,
16 MiB at 1024², 256 MiB at 4096² or 1 GiB at 8192². These figures exclude
other images and driver overhead. Released temporary textures may remain in Unity's
pool, so the 20 MiB bound is not a bound on total resident GPU memory.

The supported boundary scope is direct root-pixel evaluation. A screen-space result
cannot replace lighting evaluated at another UV, inside a loop, inside a Buffer
producer or inside another lighting-input producer. This also applies to a filter's
center sample when the filter supplies its own UV. Such evaluations require the
inline lighting calculation. Arbitrary HLSL branches are not automatically partitioned.

## Experimental SVM preview

**SVM Preview (Experimental)** is an opt-in, per-window setting. The default is
native HLSL. SVM evaluates a supported graph branch with a shared instruction
executor shader instead of generating a separate native shader for that preview.
It is an Editor preview prototype, not a replacement for final graph compilation.
The shared executor's first compilation can be expensive. Existing preview images
remain visible while replacements are prepared. Unsupported branches use native HLSL.
Warm edit timings exclude this initial executor cost; they do not justify making
the prototype a default renderer.

**SVM Nodes Only** is a separate per-window toggle, off by default. It filters
Create Node, Replace Node and connection-drop searches using the instruction builder
on creation defaults, without GPU work. Numeric/Color/Boolean parameter entries are checked
against their current settings. Connection searches from an input also filter the
candidate's output port. Sub Graph entries are checked through their expanded definitions;
HLSL function entries are omitted. The filter
does not alter the graph, enable SVM rendering or guarantee support after changing
node settings or connecting an unsupported upstream branch. Built-in default checks
are cached; live parameter checks are not.

| Render path | Behavior with the option enabled |
| --- | --- |
| Visible, expanded node preview | Uses SVM when its selected output and dependencies are supported; otherwise uses native HLSL. |
| Shader-layer result in the graph window | Evaluates supported root-graph output through a scoped SVM override, using the existing layer-preview processing. Unsupported output uses the native path. |
| Shader FX result in the graph window | Remains native. Supported individual node previews can use pure SVM; previews requiring native image boundaries retain native rendering. |
| Document canvas, export and ordinary layer rendering | Remain native. The preview override is scoped to the graph-window render call. |
| Final graph compilation | Continues through the native compiler, independently of SVM image readiness. |

The result preview evaluates the root graph even while editing a Sub Graph. This
uses the same validated Sub Graph expansion as native HLSL. Preview
selection is per branch: an unsupported node outside the evaluated dependencies
does not by itself require every other preview to use native HLSL.

### Supported subset

- Numeric scalar/vector operations: arithmetic, trigonometry, rounding, powers,
  exp/log, modulo, remapping, Range Mask, dot/cross, reflection/refraction,
  normalization, interpolation, Combine, Split, Swizzle, constants, UV, Time,
  Instance Index and Reroute. Compare, And/Or/Not drive Select through Boolean ports.
  Boolean outputs do not become image previews.
- Numeric, Color, Boolean and Normal Parameter values, including int/uint scalar
  and vector storage. Integer values become graph float values at the same point as
  native HLSL; integer Color values do not receive floating-color HDR decoding.
  Pixel-unit parameters read the current render context Pixel Scale at draw time. Texture parameters and inline texture assets
  feed Texture Size, Sample Texture and texture-height nodes. Gradient/Curve
  parameters and inline resources feed Sample Gradient/Sample Curve through the existing LUT caches. HDR decoding and
  normal normalization are shared with the native path.
- Sub Graph calls, nested definitions, multiple outputs and per-instance defaults use
  the native compiler's validated expansion and port mapping. A shared background
  preparation serves node previews; editing inside a definition uses interface defaults.
  Resources and explicit native Buffer boundaries retain their existing rules.
- UV operations: Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope,
  Vector Displacement, Twirl, Bulge/Pinch, Wave, Ripple, Flipbook, Perspective UV,
  Rotate Vector, Gradient Factor and Random.
- Color/normal operations: Hue, Saturation, Exposure, Lightness, Levels,
  Color Balance, Channel Mixer, White Balance, Posterize, Convert Color,
  Luminance, Color Mask, Hue Range, Mix Color, Set Alpha, Premultiply/Unpremultiply,
  Normal Encode/Decode, Normal Strength/Blend, Normal to Distortion, Height Blend and Blend.
- Sample Gradient (RGBA/R/G/B/A) and Sample Curve, including resource parameters through
  Reroute, Fixed gradients, HDR, alpha and Clamp/Repeat/Mirror gradient wrapping.
- Texture Size exposes Size/Texel. Sample Texture exposes RGBA/R/G/B/A; Sample Input returns RGBA. Both preserve
  Point/Bilinear and Clamp/Repeat/Mirror/Transparent. Texture parameters reuse the normal
  asset/layer/None/Self bindings; Sample Input uses the Shader layer's None/Texture/Layer/
  Composite Below source, or the input before the edited FX. Texture views retain logical
  dimensions and coordinate mapping. Eight image slots are shared with native boundaries;
  texture reads do not add a baking pass. FX result previews still use native rendering.
- Height to Normal/Distortion accept texture or value inputs. Central Difference, Sobel and Scharr
  replay the upstream instruction span for values, or use the shared texture-height stencil.
  Channel, Radius, flips and output encoding retain native behavior. Local Normal Lighting and the simplified Global node-preview policy
  use the same lighting helpers as native HLSL. Accurate Global lighting uses native boundaries.
- Perlin, Value, Value Cubic, OpenSimplex 2, OpenSimplex 2S and Flow: filtered/unfiltered
  1D/2D/3D Value and Gradient, plus Curl in 2D/3D, with or without Periodic. Flow uses
  the shared rotating-gradient kernel and accepts a varying or animated Phase.
- Gabor: filtered/unfiltered 1D/2D/3D Value, Gradient, Phase, Intensity and Phasor, plus Curl in 2D/3D, including
  profile, Phase Offset, Width and Periodic settings.
- Cellular: filtered/unfiltered 1D/2D/3D Value, F1, F2, CellValue and ToPoint, optionally periodic.
  Distribution, metric, output, Jitter, Smoothness and Evolution remain supported.
- Shard Noise: 1D/2D/3D, Sharpness, Filtering and Periodic.
- Spots Noise: 1D/2D/3D with Density, Radius, Softness and directional elongation.
  Triangle Voronoi Noise: all five 2D outputs, both distributions, Evolution and Stretch.
  Both retain Filtering and Periodic. White/Blue Noise retain 1D/2D grain, Value/Color,
  Invert, Direction and canvas/custom Image Size, using the existing blue-noise textures.
- Basic Grid, OpenSimplex2 and OpenSimplex2 Reduced Warp Noise: 1D/2D/3D Displacement
  and Position, Filtering, Periodic, None/Progressive/Independent accumulation.
- Edge Detection replays the upstream span for the native eight-sample Sobel calculation.
  Numeric channels are processed independently; Color preserves the center alpha.
  Radius, Strength, transformed UV and nested sampled evaluation retain native behavior.
- Apply Transform2D accepts inline or parameter resources, including through Reroute.
  Local/Input use the same six matrix rows as native HLSL, computed for the document
  dimensions; TRS, inverse and projective transforms are supported. Up to eight distinct
  transform resources fit one executor, without allocating render textures.
- Fractal: FBm, Ridged, Ping Pong, Multifractal, Hybrid Multifractal, Hetero
  Terrain and Ridged Multifractal, including Fractal filtering and SeedStep.
  Filtered source noise uses finite differences of its Position branch inside sampled
  scopes, matching the native footprint even after UV transforms. Root derivative
  operations combined with varying iteration counts conservatively retain native fallback.
- Canvas Position (Normalized/Aspect X/Aspect Y) and Canvas Data (Size/Texel/Pixel Scale),
  using the same layer-to-canvas coordinate preparation as native HLSL.
- Pixelate UV, all Tile UV outputs, Blackbody and Dither reuse their native helpers.
- Shape (all seven modes), Combine Distance, Checkerboard, Stripes, Grid and Rings.
  Anti-aliasing differentiates the unwrapped field at root scope and replays that
  field at neighboring coordinates inside Fractal/height sampling. This preserves
  animated phase and transformed-UV filtering without implicit buffers.
- Gradient Map preserves source/gradient alpha modes and channel selection. Color
  Curves preserves optional Master/R/G/B/A curves, RGB/luminance Master modes and
  native HDR extension, reusing the existing palette slots and LUT caches.
- Full graph output: RGBA with AlphaScale applied. Node output conversion follows
  the graph's numeric/Color component rules.
- Supported operations after explicit Buffer, Caustics/Caustics Noise and eligible
  exact Global lighting outputs, through the native-image boundaries below.

The initial executor is limited to 128 instructions, at most two nested Fractal
evaluations, four combined Fractal/sampling frames and a 4096-operation expanded
work budget. Constant octave counts contribute their actual clamped count; varying
counts contribute the eight-iteration maximum. The instruction count includes
literals, casts and control operations; 128 graph nodes are not guaranteed to fit.
Unsupported operations/settings and exceeded limits decline VM creation and
retain the native HLSL path. Custom Function, Expression
and general loops are not VM instructions; an explicit image boundary can keep its
upstream work native while supported downstream work uses SVM. Devices without the executor's
structured-buffer/shader support also use native previews.

Shared executor variants include only the required noise families. A supported node
waiting for its shared executor does not also launch a native consumer compilation;
existing images remain visible, with the updating indicator, until its replacement is ready.

Identical typed constants share instruction slots, including across Fractal bodies;
the comparison preserves the exact float bits, including signed zero.

The executor has lightweight, expanded and volume keyword variants in one shader.
Basic workloads keep the lightweight variant; color/UV helpers and extended noise
outputs activate the expanded variant. 3D noise branches activate the
volume variant, keeping their kernels out of 2D executor variants. Readiness is cached until the source revision
or variant changes, avoiding repeated shader diagnostics on each draw.
Image continuations also select an image variant. When the instruction stream has
neither noise/Fractal nor expanded helpers, an image/math variant excludes the noise
includes and dispatch arms. It retains the same image-sampling and math operations;
this variant selection is not an algorithm change or a compile-time guarantee.
Switching variants requests compilation
once; image publication waits for the selected variant. `Graph*.hlsl` helper modules
are shared with native source generation and cached per source revision.

### Native-image boundaries

The editor calls `BeginHybrid`, which first attempts a pure SVM program. If that
fails, it captures an immutable graph snapshot and queues a hybrid
`ShaderGraphPreparation.Job`. The CPU worker validates and compiles the graph,
builds the continuation instructions, and prepares the native producer sources.
Unity material/resource creation and rendering remain on the Editor thread.
`TryCreateHybrid` provides synchronous CPU preparation for direct callers and checks.

The native compiler supplies the existing Buffer/Caustics passes and eligible exact
Global lighting outputs. The bridge builds a bundle containing those producers and
a stub consumer; only the producers render. Supported downstream operations execute
in SVM. It does not discover arbitrary HLSL cut points or automatically insert Buffer nodes.

The VM image instruction accepts at most eight distinct raw output textures. Buffer
sampling preserves the selected point/bilinear filter and all four Wrap modes. It
uses the buffer's numeric type and raw float32 channels, including negative values,
HDR and Float4/Color alpha; it does not sample a display-converted node preview.
Caustics outputs remain scalar. Exact lighting uses the same screen pixel and is
rejected inside a VM Fractal body. Buffer sampling inside Fractal retains the native
requirement for an unconnected literal **Seed Step=0**.

Hybrid node and Shader-layer result previews are supported. Shader FX hybrids retain
the native path because their input ownership is not handled by this bridge. A
hybrid image becomes ready only after both its native producers and VM executor
are ready. Producer time dependencies participate in VM animation scheduling.
Node-preview requests forward their rank and demand to both the CPU preparation
and native producer queues. Hiding or collapsing a preview disposes its hybrid VM;
showing it again can request preparation. Already-started shared executables remain
owned by the executable cache.

Native and VM evaluation share `GraphCoordinates.cginc`. Producer coordinate
bindings, VM UV mapping, viewport and the context returned to the outer layer render
must agree: a transform consumed during graph evaluation must not be applied again.
The graph-window source-revision path rebuilds the VM; an independent caller must
also replace it when graph/source dependencies change. Direct boundary parity checks
do not establish async lifecycle or complete layer-render parity.

### Preview update indicator

The 24-pixel corner spinner and lightly dimmed image indicate a pending update.
The dimming changes the UI image tint, preserving texture pixels and alpha without
an additional render pass. A teal vector lightning icon in the node header identifies its
displayed SVM image; Output uses the result preview's renderer. Icons update on
image publication and clear on native rendering, errors or release. They describe
the displayed image, not a capability test or the renderer of every consumer branch.
Node previews
reuse dirty flags, pending requests and their existing code/value fingerprints;
the result panel uses its dirty flag and the final program's pending state. The
indicator clears after rendering the current image or a terminal error. A current
SVM image does not wait for unrelated final native compilation. Animation alone
does not keep the spinner active. Hidden/collapsed/detached indicators stop drawing.
One shared 12 Hz Editor callback repaints active vector indicators without graph
traversal, extra fingerprint calculation or GPU readiness polling.

### Runtime boundary

The internal `ShaderGraphPreviewVM.TryCreate(graph, nodeId, port, out vm)` returns
false for unsupported input and sets `LastFallbackReason`. A null `nodeId` selects
the root graph's Output. `InstructionCount` describes the resulting program;
`Ready` includes executor-shader readiness and resource initialization, and
`Render(target, time)` reports whether it rendered. Callers own and dispose the VM.

For native-image continuation in the editor, use `BeginHybrid`, configure its
document/layer owner with `ConfigureHybrid`, wait for `Ready`, and render through
`RenderBoundaries(target, context)`. A returned placeholder alone is not executable.
The VM owns its preparation job, compilation request and native runtime; sampled
image textures are borrowed from that runtime. Replacement transfers the pending
job from the prepared VM, and disposal releases owned work/resources. The ordinary
`Render` entry point does not populate native producer images.

Node preview code/value fingerprints and selected output must match the current
graph before declaring an image ready. A non-null VM, a ready executor or a completed
instruction builder alone does not identify the renderer that produced the current
image. Node previews use `svmRendered`; the layer panel retains `renderedVM` for its
last rendered image. Native fallback retains the existing diagnostic
path. The Shader-layer override is installed through a disposable scope for the
target behavior; it must not escape to document canvas/export rendering.

### Measurement boundaries

Treat first executor initialization, first graph-window opening, subsequent edits
and native final compilation as distinct measurements. Report main-image and
all-visible-node readiness separately, together with actual backend counts and
the native runtime revision. An SVM image may become ready while native final
compilation is still pending; a mixed SVM/native result is not an exclusive VM run.

Layer Preview coalesces edits into one standalone render per Editor update, without
a fixed debounce or fallback timer. It uses an available current Canvas snapshot;
otherwise it waits only for an already queued full-canvas render, brought forward
to the next window update. Viewport renders cannot provide a complete layer image.
Without a suitable pending Canvas render, it uses the existing standalone path.
Published Canvas snapshots are copied while their borrowed texture is valid; they
also suppress an unnecessary standalone render in that update. Closing a Canvas
window removes it from the pending-work query. Preview requests never schedule a
new full-canvas render. Shader readiness and resource-change notifications remain
part of the existing native compilation path.

Compare node readiness, main-image readiness and native compilation separately.
Older measurements include a 120 ms panel fallback delay and are not an equivalent
scheduling baseline. Compare matched cache-warm rows separately from first-use
rows and record native compilation counts; a new graph does not prove a cold cache.

A rendering batch measured through CPU submission and a final synchronous pixel
readback is completed wall time, including GPU synchronization. It is not isolated
GPU time. Pixel comparisons use identical input values, time, dimensions and
linear floating-point output, with explicit tolerances and nonfinite checks.
An unsupported VM case has no VM parity result. No numeric performance or parity
guarantee is implied by enabling the experimental option.
