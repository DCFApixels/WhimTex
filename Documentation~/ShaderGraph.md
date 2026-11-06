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
on creation defaults, without GPU work. Numeric/Color parameter entries are checked
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

- Numeric scalar/vector operations: Add, Subtract, Multiply, Divide, Min, Max,
  Power, Step, Dot, Distance, Abs, One Minus, Negate, Square Root, Floor, Ceil,
  Round, Fraction, Sine, Cosine, Length, Normalize, Lerp, Clamp, Smoothstep and
  Unlerp; Combine, Split, constants, UV, Time, Instance Index and numeric Reroute.
- Numeric and Color Parameter values. Integer parameters, pixel-unit parameters,
  Normal and resource/Boolean parameter types use native fallback. Color values
  use the same HDR decoding as the native path.
- Perlin and Gabor: 2D, unfiltered, nonperiodic **Value** only. Gabor uses its
  default profile; Phase, Intensity, Phasor and derivative outputs use native fallback.
- Cellular: 2D, unfiltered, nonperiodic **Value**, **F1**, **F2**, **CellValue** and
  **ToPoint**. Existing distribution, metric and output settings remain inputs.
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
