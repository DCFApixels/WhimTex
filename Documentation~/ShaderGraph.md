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

## Experimental SVM preview

**SVM Preview (Experimental)** is an opt-in, per-window setting. The default is
native HLSL. SVM evaluates a supported graph branch with a shared instruction
executor shader instead of generating a separate native shader for that preview.
It is an Editor preview prototype, not a replacement for final graph compilation.
The shared executor's first compilation can be expensive. Native HLSL previews
remain available while it warms, then supported previews can switch to SVM.
Warm edit timings exclude this initial executor cost; they do not justify making
the prototype a default renderer.

**SVM Nodes Only** is a separate per-window toggle, off by default. It filters
Create Node, Replace Node and connection-drop searches using the instruction builder
on creation defaults, without GPU work. Numeric/Color/Boolean parameter entries are checked
against their current settings. Connection searches from an input also filter the
candidate's output port. HLSL functions and Sub Graph entries are omitted. The filter
does not alter the graph, enable SVM rendering or guarantee support after changing
node settings or connecting an unsupported upstream branch. Built-in default checks
are cached; live parameter checks are not.

| Render path | Behavior with the option enabled |
| --- | --- |
| Visible, expanded node preview | Uses SVM when its selected output and dependencies are supported; otherwise uses native HLSL. |
| Shader-layer result in the graph window | Evaluates supported root-graph output through a scoped SVM override; retains the existing Transform, FX, clipping and layer-preview processing. Unsupported output uses the native path. |
| Shader FX result in the graph window | Remains native. Supported individual node previews can still use SVM. |
| Document canvas, export and ordinary layer rendering | Remain native. The preview override is scoped to the graph-window render call. |
| Final graph compilation | Continues through the native compiler, independently of SVM image readiness. |

The result preview evaluates the root graph even while editing a Sub Graph. This
does not make Sub Graph nodes part of the supported VM instruction set. Preview
selection is per branch: an unsupported node outside the evaluated dependencies
does not by itself require every other preview to use native HLSL.

### Supported subset

- Numeric scalar/vector operations: arithmetic, trigonometry, rounding, powers,
  exp/log, modulo, remapping, Range Mask, dot/cross, reflection/refraction,
  normalization, interpolation, Combine, Split, Swizzle, constants, UV, Time,
  Instance Index and Reroute. Compare, And/Or/Not drive Select through Boolean ports.
  Boolean outputs do not become image previews.
- Numeric, Color, Boolean and Normal Parameter values. Integer parameters,
  pixel-unit parameters and resources retain native fallback. HDR decoding and
  normal normalization are shared with the native path.
- UV operations: Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope,
  Vector Displacement, Twirl, Bulge/Pinch, Wave, Ripple, Flipbook, Perspective UV,
  Rotate Vector, Gradient Factor and Random.
- Color/normal operations: Hue, Saturation, Exposure, Lightness, Levels,
  Color Balance, Channel Mixer, White Balance, Posterize, Convert Color,
  Luminance, Color Mask, Hue Range, Mix Color, Set Alpha, Premultiply/Unpremultiply,
  Normal Encode/Decode, Normal Strength/Blend, Normal to Distortion and Height Blend.
- Perlin, Value, Value Cubic, OpenSimplex 2 and OpenSimplex 2S: unfiltered 2D
  Value, Gradient and Curl, with or without Periodic.
- Gabor: unfiltered 2D/3D Value, Gradient, Curl, Phase, Intensity and Phasor, including
  profile, Phase Offset, Width and Periodic settings.
- Cellular: unfiltered 2D/3D Value, F1, F2, CellValue and ToPoint, optionally periodic.
  Distribution, metric, output, Jitter, Smoothness and Evolution remain supported.
- Fractal: FBm, Ridged, Ping Pong, Multifractal, Hybrid Multifractal, Hetero
  Terrain and Ridged Multifractal, including Fractal filtering and SeedStep.
  Fractal filtering is independent of source-noise filtering: a filtered noise
  source still falls outside the initial noise subset.
- Full graph output: RGBA with AlphaScale applied. Node output conversion follows
  the graph's numeric/Color component rules.

The initial executor is limited to 128 instructions, at most two nested Fractal
evaluations and a bounded expanded work budget. The instruction count includes
literals, casts and control operations; 128 graph nodes are not guaranteed to fit.
Unsupported operations/settings and exceeded limits decline VM creation and
retain the native HLSL path. Custom Function, sampled resources, Buffer, Sub Graph
and general loops are outside this prototype. Devices without the executor's
structured-buffer/shader support also use native previews.

Identical typed constants share instruction slots, including across Fractal bodies;
the comparison preserves the exact float bits, including signed zero.

The executor has lightweight, expanded and volume keyword variants in one shader.
Basic workloads keep the lightweight variant; color/UV helpers and extended noise
outputs activate the expanded variant. Only 3D Gabor/Cellular branches activate the
volume variant, keeping their kernels out of 2D executor variants. Readiness is cached until the source revision
or variant changes, avoiding repeated shader diagnostics on each draw.
Switching variants requests compilation
once and retains native fallback while it is pending. `Graph*.hlsl` helper modules
are shared with native source generation and cached per source revision.

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
