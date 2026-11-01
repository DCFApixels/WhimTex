---
title: "Shader layer contract"
parent: "Technical reference"
nav_order: 7.1
lang: en
permalink: /reference/shader-layer/
search_exclude: true
---

# Shader layer contract

The Shader layer generates its own image from an embedded node graph. The graph belongs to the
layer's behavior; it is not a document-wide graph or an FX preset. The artist workflow is in the
[Shader layer guide](en/shader-layer.md).

## Layer boundary

- **Shader:** evaluate a graph for each output pixel and produce straight RGBA.
- **Shader FX:** process the owning layer's image with an effect.
- **Shader Processor:** process the composite below its stack position with FX.

Shader does not implicitly sample the lower stack. Images enter through declared Texture
parameters and Sample Texture nodes. The generated image participates in the ordinary layer
transform, FX and composition pipeline; Output is not the final document composite.

Create nodes through the graph context menu's **Create Node**, **Space**, or by dropping an output
connection on empty graph space. Notes, groups and edge reroutes have separate context actions.

## Parameters and ports

Parameter headers can be dragged to reorder `graph.parameters` or dropped on the graph to
create a Parameter node at the transformed canvas position. Header context actions are
Use in Graph, Duplicate and Remove. Duplicate deep-copies parameter settings and values
(retaining texture/layer references), assigns a new ID and unique name, and inserts after
the original. Remove deletes its Parameter nodes and incident edges. These operations support
Undo/Redo. Header indicators reuse output-port types, colors and shapes.

Parameter declarations define the fields shown in the layer's Properties. A Parameter node reads
a declaration, and multiple nodes can read the same declaration. Constant selects PI, TAU,
PHI, E or SQRT2 and always outputs Float. Texture, Gradient and Curve parameters use the shared controls and bindings.

In the graph editor, Parameter nodes use one compact row containing the parameter name and
output connector. There is no visible `Out` label, internal preview or dropdown. Choose another
declared parameter through the node's context menu. This presentation does not rename the
serialized `Out` port or change parameter declarations and values.

| Port type | Meaning |
| --- | --- |
| `float`, `float2`, `float3`, `float4` | Numeric scalar/vector values. |
| Color | RGBA with the same four components as `float4`, with alpha-one defaults when converting from a scalar or shorter vector. |
| Boolean | Conditions for Compare and Select. |
| Texture | An image resource; Sample Texture produces its color. |
| Gradient | A gradient resource; Sample Gradient evaluates it at a scalar position. |
| Curve | A curve resource; Sample Curve evaluates it at a scalar position. |

Numeric and Color ports use circular connectors, Boolean ports use diamonds, and Texture,
Gradient and Curve ports use squares. The shape follows the port type, including Parameter
outputs and Reroutes.

Numeric connections allow different widths. Narrowing keeps the leading components and drops
the rest: `float4(x, y, z, w)` → `float2(x, y)`. Widening a vector fills missing components with
zero: `float2(x, y)` → `float4(x, y, 0, 0)`. A scalar still splats to every component of a numeric
vector: `float(x)` → `float4(x, x, x, x)`. Converting a vector to `float` keeps its first component.

Color is a distinct port type with `float4` storage and HLSL representation. Conversion into a
Color port follows these rules:

| Source | Color result |
| --- | --- |
| `float(x)` | `(x, x, x, 1)` — opaque grayscale. |
| `float2(x, y)` | `(x, y, 0, 1)`. |
| `float3(x, y, z)` | `(x, y, z, 1)`. |
| `float4(x, y, z, w)` | `(x, y, z, w)` — all components, including alpha, are retained. |
| Color | All RGBA components are retained. |

Conversion from Color to a numeric port follows the ordinary `float4` rules. Output's `RGBA`
input, Blend's color inputs and result, Sample Texture and Sample Gradient color outputs, and
Color Parameter outputs have fixed Color types. Sample Curve remains numeric.

Dynamic math normalizes Color to `float4` before choosing the widest numeric operand width.
Scalars splat to that width; shorter vectors are zero-extended. The Color destination's
alpha-one defaults do not apply in math. For example, Add of Color `(r, g, b, a)` and `float(x)`
uses `(x, x, x, x)`, and Add with `float2(x, y)` uses `(x, y, 0, 0)`. Dynamic numeric results use
numeric types, not Color; operations with fixed scalar or Boolean results retain those results.

Unconnected Color values store linear RGBA; their picker displays encoded RGB
and converts edits back to linear RGB without changing alpha. Color parameters retain their
existing encoded storage and color preparation into linear working values.
Connecting Color and numeric ports does not insert a color-space conversion, and numeric
vectors do not receive color preparation merely because they have four components. Use Split
and Combine for explicit component selection and assembly. Resources require their Sample
operation before numeric use; Boolean conditions remain distinct from numeric and resource ports.

Unconnected numeric input fields sit outside the node body, to the left of their ports, leaving
the port dots unobstructed for wiring. These fields already existed but were hidden by `Node`'s
built-in inline clipping; they are now visible and editable. Connecting an input hides its field; disconnecting it
restores the field. Single Float fields use an X component label; dragging it adjusts the value
through Unity's native numeric control. Unconnected UV ports retain implicit `PixelUV` coordinates. Their local
**Custom / UV** option selects an explicit numeric value or the implicit current-pixel UV;
Custom fields follow the same external placement and connection visibility rules. These are
editor controls over the existing input semantics, not changes to graph math or storage.

Visible inputs that do not apply to the selected mode are disabled together with their fields:
Noise/DomainWarp Octaves, Lacunarity and Gain with Fractal None; Noise PingPongStrength outside
PingPong; Shape Radius outside Rounded Rectangle; Stripes Width outside Square; GradientFactor
Angle in Radial mode. A tooltip explains the dependency. Split outputs beyond the input width
are disabled. Connection candidates and create-connected-node search exclude inactive ports.
Changing modes preserves values and wires for ports that remain present; reactivating a port
restores editing. This editor availability state adds no serialized fields or evaluation rules.

## Notes, groups and node messages

`ShaderGraph.notes` and `groups` are optional arrays (empty by default, at most 512 each).
A note stores `id`, `title`, `contents`, `position`, `size`, `theme` (0 Classic, 1 Black) and
`fontSize` (0 Small, 1 Medium, 2 Large, 3 Huge), and `hueShift` (−180…180 degrees, default 0).
The Hue context action opens a rainbow slider without a label. It rotates the native background,
outline and text palette in HSV, retaining each color’s saturation, value and alpha plus non-color
styles. Zero removes overrides entirely; changing the native note theme retains the shift and
applies it to the new theme’s palette. Selection and resize affordances keep their standard colors. Note selection uses the native node’s
2-pixel outward gap and 8-pixel corner radius, measured in Unity 6000.7. A group stores `id`, `title`, `position` and
`elements`: IDs of nodes or notes. IDs are unique across all three element kinds. Each element
belongs to at most one group; groups cannot contain groups. Deleting a member prunes its IDs.
Deleting the group only removes its frame. Notes use native StickyNote sizing and editing;
groups use native GraphView Group layout. Note text editors copy the visible label’s measured
rectangle and typography into the native TextField and nested input, preserving the text area and
font size when entering edit mode or typing longer content. Both are created through separate context-menu actions,
not node search. They support document Undo/Redo and clipboard; copying a group expands its
members, remaps their IDs and preserves internal edges. Notes can be copied independently.
Annotations are excluded from shader and render fingerprints and do not request compilation or
rendering when edited. TIFF and document JSON retain their layout and text.

Shader diagnostics retain node ID, text and Info/Warning/Error level, including Unity compiler
warnings. Emitted node expressions and HLSL modules map source locations to node IDs. A node's
compile, preview and function-draft messages share a badge at its right, using the highest severity.
Only hovering that badge opens the message panel to its right. Preview errors no longer add text
below the preview, and node errors do not repeat in the bottom message area. Global compilation
errors remain in the graph message area. An unsuccessful compile retains the last valid layer
program; an unsuccessful preview clears its image.

## Node clipboard

Native GraphView Copy/Paste/Cut/Duplicate commands use a session-scoped JSON fragment.
The canvas also handles Ctrl/Cmd+C/V/X/D key events through those same native callbacks,
so fresh clipboard operations do not depend on Editor command forwarding after script reload.
Handled keys are consumed once; text fields and active pointer gestures keep their own input.
The fragment includes selected nodes, edges whose endpoints are both selected, required
parameter values, functions (including drafts and editor sizes), and their include closure.
Output is not copyable. Text-field commands retain normal text editing behavior.

Paste assigns new node/function IDs, remaps internal edges, and selects the inserted nodes.
In the same graph, compatible Parameter IDs are reused; otherwise parameters are copied
with new IDs and unique display names. Unusable layer texture references are cleared, including
references across documents. Unity texture object references are retained within the Editor session.
Includes are deduplicated by ID and matching content/bindings; conflicting content is rejected.
The merged graph is validated before one document Undo mutation, so invalid data or capacity
limits do not partially insert nodes. Copy records the combined laid-out rectangles of nodes,
notes and group frames in graph coordinates. Paste and Duplicate align that rectangle's center
with the current viewport center, accounting for pan and zoom. Repeated paste uses the same
centering rule without an accumulating offset. Duplicate leaves the system clipboard unchanged.
The clipboard survives script reload: its Editor-session marker is retained in SessionState.
An Editor restart invalidates it; this is not a document format.

## Node movement and deletion

**Del** deletes selected nodes, including Reroutes with the reconnect behavior described below.
Output remains required and cannot be deleted. When a text or numeric field is being edited,
**Del** edits the value without deleting the node.

During dragging, a weak Y-only snap aligns the centers of connected input/output connectors
within a radius of 4 logical screen pixels, accounting for graph zoom. Native Y snapping
between node rectangles takes priority; connector snapping never changes X. **Shift** disables
snapping. A multi-node drag applies the same adjustment to all moved nodes, preserving their
relative positions.

## Reroute connections

Double-clicking an existing edge inserts a Reroute at that location, replacing the edge with
source → `In` and `Out` → destination connections. The edge context menu offers **Add Reroute**
with the same behavior. In the editor, Reroute is created only on an existing edge and is
absent from node search.

Reroute is a movable mini-node with no header, preview or parameter controls. Its `In` → `Out`
path forwards the source's exact type and value: `float`, `float2`, `float3`, `float4`, Color, Boolean,
Texture, Gradient or Curve. It adds no HLSL calculations, casts or resource sampling.
The output can feed multiple inputs; a chain of Reroutes preserves the same source value.

Deleting a Reroute reconnects its incoming source directly to each downstream input, preserving
branches and the remaining Reroute chain. Ordinary edge deletion removes that connection
without reconnecting its endpoints. Node positions and connections persist with the graph;
insertion, movement, connection edits and deletion support Undo/Redo.

## Coordinate and output contract

UV supplies the current pixel's local layer coordinates, with the ordinary image domain at
`0..1`. Image Size describes the generated image; Texture Size describes a selected texture.
Both expose `Size` (width, height) and `Texel` (reciprocal width, reciprocal height) as `float2`.
Transform UV changes coordinates for subsequent calculations, independently of the layer's
ordinary Transform. Sampling uses the supplied coordinates.

Output receives RGBA through a fixed Color input and uses a restrained visual distinction from
ordinary calculation nodes. It has no per-node preview; Layer Preview shows the layer result.
RGB is straight, not premultiplied by alpha. The graph adds no blanket
`0..1` clamp to its numeric results: HDR and negative values remain available, subject to the
selected operation and the normal layer Rendering settings. Alpha retains the existing layer
coverage semantics. Lerp interpolates numeric components; Blend performs color compositing
with the chosen blend mode and alpha handling.

## Compilation and invalidation

The connected calculation compiles to HLSL for a single fragment-shader generation pass.
Intermediate numeric node values are shader expressions or variables, not an image allocated
for every node. Declared resource inputs retain their normal resource preparation requirements;
the subsequent layer FX and document composition remain separate from this generation pass.

Changing parameter values updates bindings and the rendered result without recompiling the
shader. Changes to operations, connections or parameter types change the generated program and
require compilation. Constant emits the selected mathematical value as a float literal rather
than a material uniform; changing `constant` or a Swizzle's `swizzle` mask changes the code fingerprint.
Moving nodes in the editor only changes layout. The computation must be acyclic and connections
must satisfy the port types to compile. If compilation fails, diagnostics remain explicit and
the runtime retains its last successfully compiled shader when one is available. That shader's
result is not proof that the current draft compiled or produced valid output.

### Generated HLSL

**Generated HLSL** uses a draggable footer with the shared `VerticalRevealHandle` extracted from
`LayerPreviewPanel`. The handle keeps the same 20-logical-pixel height, caption and horizontal
grip lines. It responds to pointer dragging; a click alone does nothing. From the collapsed
20-pixel height, dragging upward reveals the code only when the requested height exceeds
34 logical pixels: more than 14 pixels of upward travel in normal window coordinates,
independent of graph zoom. Dragging downward hides the code again.

The footer starts collapsed. Dragging the handle resizes the code panel up to the available
window space while leaving 80 logical pixels for the rest of the window. The open/closed state
and panel height are serialized in the graph window and restored with its window state.

The code field is created lazily on the first opening and reuses the actual FX code field in
read-only mode. While closed, it is detached from the visual tree: no code drawing, syntax
highlighting or line-number callbacks run, and generated-source updates are not sent to the
field. The field instance is retained for reopening. Reopening displays the current generated
source; unchanged source is not reassigned, preserving the retained instance and text selection.

Text remains selectable and copyable, with the same syntax highlighting, line numbers and
horizontal/vertical scrolling as FX Code, subject to the same Editor-version support. The graph
remains the editable source; this view does not edit the generated program.

<a id="selected-node-preview"></a>

## Constant and Swizzle

`Constant` uses `constant: "PI"` (default), `"TAU"` (2π), `"PHI"` (golden ratio),
`"E"` or `"SQRT2"`. `Out` is always Float; the selected value is emitted directly into HLSL.
Arbitrary numbers/colors belong to parameters or unconnected input values. The old node-level
`value` field is removed; JSON/TIFF graphs containing it are unsupported, without migration.

`Swizzle` has one dynamic numeric/Color input `In` and numeric output `Out`. Its `swizzle`
string defaults to `"xxxx"`. Valid masks have 1–4 characters: `xyzw`, `rgba` (case-insensitive,
aliases may be mixed) and `_` for literal zero. Components may repeat. Output is Float through
Float4 according to mask length, including when the input is Color; no color conversion occurs.
For Float2 `(x,y)`, `"_x_y"` produces `float4(0,x,0,y)`. `"_"` produces Float zero.
Input width is the maximum of the connected source width (or the stored unconnected input type)
and the highest component referenced by the mask: y/g requires Float2, z/b Float3 and w/a Float4.
`_` does not increase input width. Color sources count as four numeric components.
Connected sources use the usual numeric conversions: scalars repeat, shorter vectors gain zeros.
Unconnected fields expose all inferred components directly and retain their stored values.
An empty/oversized mask or unsupported character is a node-specific compilation error.
Invalid masks remain saveable drafts.
The Mask field commits on Enter or focus loss before rebuilding typed ports. Choice/mask edits,
Undo/Redo, clipboard and JSON/TIFF persistence use the ordinary graph mechanisms.

## Node previews

Layer Preview uses the shared `LayerPreviewPanel` in a floating overlay beside the graph canvas,
outside its content transform. It starts at the bottom-right, supports dragging the header or image and edge
or corner resizing, and keeps the existing R/G/B/A channel controls. Placement, size and channels
belong to window state, never graph/document data. Viewport resizing keeps the panel reachable.
Dragging within 16 logical pixels of an edge snaps each axis independently, allowing edge
and corner attachments. Attached edges sit flush with the viewport, follow viewport
resizing and show an accent border. Dragging away releases them. Attachment flags are window state;
resizing from a free edge preserves the opposite attachment, while resizing an attached edge releases it.
The docked Layer Settings preview retains its existing reveal/resize behavior.

Hovering a parameter declaration highlights its row and all matching Parameter nodes by ID.
This transient feedback does not alter selection, Undo history or rendering.

Preview availability is shared by node construction and cache cleanup. Constant, Image Size,
Texture Size, Split, Compare, Output, Parameter and Reroute do not create a preview or GPU target.
UV, sampling, generators and numeric image-processing operations retain previews.
Nodes with an expanded preview retain the 200-pixel minimum width. Collapsing the preview
removes that constraint, so the node sizes to its contents like an ordinary node without a preview;
expanding it restores the minimum. Rebuilding the node view respects the cached preview state.
Only numeric and Color output ports are offered; Boolean/resource-only HLSL functions have no preview.
This is a node-kind/output-type policy, not an analysis of whether the current inputs vary with UV.
There is no shared Selected Node preview in the sidebar, and selecting a node does not retarget
another node's preview. An output selector appears only when multiple previewable outputs exist.
The preview has no Foldout or heading. Its collapse button overlays the image at the top and
appears on node hover (or button focus). A collapsed preview leaves only an expand row.
Control heights and icon dimensions come from installed `com.unity.shadergraph` 17.7.0,
`Editor/Resources/Styles/MaterialNodeView.uss`: collapse 20 × 20 with a 4-pixel top inset,
expand 56 × 16 and icon 16 × 16 logical pixels. The expand button is centered without
margins or padding; its chevron is centered. Scoped button selectors override the native node's
button margins. Chevrons are drawn locally; there is no package
asset dependency. Expansion state remains local to the window's cached preview across node rebuilds.
Resource ports require a Sample node; scalar values become opaque grayscale, `float2` becomes
`(x, y, 0, 1)`, `float3` gains alpha 1, and `float4` and Color are displayed as RGBA. These display
conversions do not change numeric wire conversions or math results.

Each preview uses a 128 × 128 linear ARGBFloat target with Bilinear filtering, before the owning
layer's Transform and FX. It is separate from Layer Preview and does not replace the graph's
Output connection. Preview rendering is dirty-driven: a preview renders only when invalidated,
expanded and visible in the graph viewport. Collapsed, hidden or off-screen previews do not
render; pending invalidation is processed when they are expanded and visible again. Program
reuse depends on the graph code fingerprint, the preview's node and its chosen output port;
parameter values rebind without recompilation. A preview compilation/render error clears that
preview's image and displays a diagnostic. This differs from retaining the last successful
shader for the layer itself. Layer rendering, graph math and storage are unchanged by this UI.

## Initial node set

Names below identify operations; grouped rows do not imply a single combined node.

| Operations | Purpose |
| --- | --- |
| Parameter, Constant | Shared parameter reads and named mathematical constants. |
| Instance Index, Time | Input nodes with one Float output, Out. Both currently return 0, with no controls or preview. |
| Custom Function, cataloged HLSL functions | Inline function bodies or functions imported from an HLSL library. |
| Reroute | Forward a typed connection unchanged; insert on an existing edge, not through node search. |
| UV, Image Size, Texture Size | Coordinates and image dimensions. |
| Sample Texture, Sample Gradient, Sample Curve | Read typed resources. |
| Split, Combine, Swizzle | Component access, vector construction, reordering and zero insertion. |
| Add, Subtract, Multiply, Divide | Basic arithmetic. |
| Min, Max, Clamp, Abs, One Minus, Negate | Bounds, absolute value, `1 − x` and sign inversion. |
| Power, Square Root | Exponentiation and square root. |
| Floor, Ceil, Round, Fraction | Integer boundaries and fractional part. |
| Sine, Cosine | Periodic functions. |
| Lerp, Remap, Step, Smoothstep | Interpolation, range mapping and thresholds. |
| Length, Distance, Dot, Normalize | Vector calculations. |
| Compare, Select | Conditions and conditional values. |
| Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope | Coordinate transforms in the UV category. |
| Domain Warp, Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple | UV distortions for downstream sampling and procedural nodes. |
| Noise, Cellular, Random | Spatial noise, cellular distances and deterministic random values. |
| Checkerboard, Stripes, Grid, Rings, Gradient Factor | Procedural masks and scalar gradient coordinates. |
| Shape, Combine Distance | Antialiased shape masks and signed-distance operations. |
| Blend | RGBA blending using the existing blend semantics. |
| Output | Final straight RGBA through a fixed Color input; modest visual distinction and no per-node preview. |

Reusable subgraphs and Blur are outside this first version. Time exists but currently returns 0.
HLSL FX authoring continues to use the [separate effect contract](ShaderFX.md).

### Procedural and UV nodes

Use the exact `kind` and output-port names below when authoring graph JSON. Spaces in editor
labels are not part of serialized kinds. All listed outputs have fixed numeric types.

| Category | `kind` | Outputs | Purpose |
| --- | --- | --- | --- |
| Procedural/Noise | `Noise` | `Out`: Float | Noise with None, FBm, Ridged or PingPong fractals. |
| Procedural/Noise | `Cellular` | `F1`, `F2`, `Cell`: Float | Nearest and second-nearest feature distances and a cell value. |
| Procedural/Noise | `Random` | `Out`: Float | Deterministic random values from coordinates and seed. |
| Procedural/Pattern | `Checkerboard`, `Stripes`, `Grid`, `Rings` | `Out`: Float | Antialiased pattern masks. |
| Procedural/Pattern | `GradientFactor` | `Out`: Float | Scalar position for `SampleGradient.T`. |
| Procedural/Shape | `Shape` | `Out`, `Distance`: Float | Smoothed mask and signed distance for Ellipse, Rectangle, Rounded Rectangle, Polygon, Star, Capsule or Line Segment. |
| Procedural/Shape | `CombineDistance` | `Out`, `Mask`: Float | Combine signed distances and produce a smoothed mask. |
| UV | `TransformUV`, `PolarCoordinates`, `Mirror`, `Kaleidoscope` | `Out`: Float2 | Transform, remap or fold coordinates. |
| UV | `Repeat` | `Out`, `Cell`: Float2 | Repeated coordinates and the corresponding cell coordinates. |
| UV/Distortion | `DomainWarp` | `Out`, `Displacement`: Float2 | Warped UV and its displacement. |
| UV/Distortion | `VectorDisplacement`, `Twirl`, `BulgePinch`, `Wave`, `Ripple` | `Out`: Float2 | Offset or deform coordinates. |

Categories are nested branches in Create Node and the connection search. Discovery represents
each path with `/` separators. Cellular's default output is `F1`; it has no `Out` port. Set an
edge's `outputPort` to `F1`, `F2` or `Cell` explicitly when using Cellular in JSON.

UV distortion nodes return coordinates, not sampled colors or masks. Connect their `Out` to
the `UV` input of a procedural or sampling node. They do not clamp coordinates to `0..1`;
`SampleTexture` wrap settings determine how out-of-range image coordinates are sampled.
`Strength = 0` leaves the input UV unchanged. Displacement vectors retain their magnitude;
they are not normalized. `TransformUV` belongs to **UV**, independently of the layer Transform.

`Wave`, `Ripple`, `Stripes` and `Rings` expose a separate Float `Phase` input. Supply a parameter
or another scalar expression to shift the pattern; the current `Time.Out` is always zero.
Mask edges are smoothed. A signed-distance output is negative inside, zero on the boundary
and positive outside; do not treat it as a `0..1` mask. `Shape.Distance` retains the existing
approximate ellipse distance, so an ellipse is not guaranteed to have an exact Euclidean SDF.
Connect two `Shape.Distance` outputs to `CombineDistance` and use its `Mask` for coverage.

For a colored gradient, connect `GradientFactor.Out` to `SampleGradient.T`, a Gradient parameter
to `SampleGradient.Gradient`, and its color `Out` to `Output.RGBA`. `GradientFactor` supplies
the position; it does not hold or sample a gradient resource.

#### Serialized modes and inputs

`node.option` is a zero-based integer, default `0`. The following labels are listed in that
integer order; they are not string values to write into `option`.

| `kind` | `option`: label |
| --- | --- |
| `Noise` | 0: OpenSimplex2; 1: OpenSimplex2S; 2: Cellular; 3: Perlin; 4: ValueCubic; 5: Value |
| `Cellular` | 0: Euclidean; 1: Euclidean Squared; 2: Manhattan; 3: Hybrid |
| `Stripes` | 0: Square; 1: Triangle; 2: Sine |
| `GradientFactor` | 0: Linear; 1: Radial; 2: Angular |
| `Shape` | 0: Ellipse; 1: Rectangle; 2: Rounded Rectangle; 3: Polygon; 4: Star; 5: Capsule; 6: Line Segment |
| `CombineDistance` | 0: Union; 1: Intersection; 2: Subtract |
| `PolarCoordinates` | 0: To Polar; 1: From Polar |
| `Mirror` | 0: X; 1: Y; 2: Both |
| `DomainWarp` | 0: OpenSimplex2; 1: OpenSimplex2Reduced; 2: BasicGrid |
| `VectorDisplacement` | 0: Signed RG; 1: Packed RG |

`node.fractal` is separate from `option`. Use enum names in JSON: Noise accepts `None`, `FBm`,
`Ridged`, `PingPong`; DomainWarp accepts `None`, `Progressive`, `Independent`. Their underlying
enum values are respectively `0`, `1`, `2`, `3`, `4`, `5`; both nodes default to `None`.
The UI label **Ping Pong** serializes as `PingPong`. Progressive warps each octave's sampling
position using previous octaves; Independent samples each octave from the original position.

All inputs below are Float unless marked Float2. Every `UV` input is Float2 and defaults to
current-pixel UV; `Random.Coordinates` is an ordinary Float2 input with a constant zero default.
Input values belong in `node.inputs`, with exact case-sensitive port names. Discovery returns
their types and defaults; a mode's controls are not additional node-level JSON fields.

| `kind` | Inputs |
| --- | --- |
| `Noise` | UV, Scale (Float2), Seed, Octaves, Lacunarity, Gain, PingPongStrength |
| `Cellular` | UV, Scale (Float2), Seed, Jitter |
| `Random` | Coordinates (Float2), Seed |
| `Checkerboard` | UV, Scale (Float2), Offset (Float2) |
| `Stripes` | UV, Frequency, Width, Angle, Phase |
| `Grid` | UV, Scale (Float2), Width (Float2), Offset (Float2) |
| `Rings` | UV, Center (Float2), Frequency, Width, Phase |
| `GradientFactor` | UV, Center (Float2), Scale (Float2), Angle |
| `CombineDistance` | A, B, Smoothness, Feather |
| `TransformUV` | UV, Offset (Float2), Scale (Float2), Rotation, Center (Float2) |
| `PolarCoordinates` | UV, Center (Float2), Angle |
| `Repeat` | UV, Scale (Float2), Offset (Float2) |
| `Mirror` | UV, Center (Float2) |
| `Kaleidoscope` | UV, Center (Float2), Sectors, Angle |
| `DomainWarp` | UV, Scale (Float2), Seed, Strength, Offset (Float2), Octaves, Lacunarity, Gain |
| `VectorDisplacement` | UV, Displacement (Float2), Strength (Float2) |
| `Twirl`, `BulgePinch` | UV, Center (Float2), Strength, Radius |
| `Wave` | UV, Strength, Frequency, Direction, Phase |
| `Ripple` | UV, Center (Float2), Strength, Frequency, Radius, Phase |

Shape inputs depend on `option`. Use the active mode's port set when wiring or serializing
inputs. Changing Shape mode in the editor removes inputs and incoming edges absent from the
new mode and initializes newly introduced inputs with their defaults.

| Shape `option` | Inputs |
| --- | --- |
| 0, 1, 2 | UV, Center (Float2), Size (Float2), Radius, Feather |
| 3: Polygon | UV, Center (Float2), Size (Float2), Sides, Rotation, Feather |
| 4: Star | UV, Center (Float2), Size (Float2), Sides, InnerRadius, Rotation, Feather |
| 5: Capsule | UV, Center (Float2), Size (Float2), Rotation, Feather |
| 6: Line Segment | UV, Start (Float2), End (Float2), Width, Feather |

#### Units and evaluation

- `Angle`, `Rotation` and `Direction` are degrees. `Twirl.Strength` is also degrees.
  `Phase` is cycles: Stripes, Rings and Wave add it to their periodic coordinate; Ripple
  subtracts it. `Frequency` is cycles per input-coordinate unit. No implicit time is read.
- Noise and DomainWarp round and clamp `Octaves` to `1..8`, clamp `Lacunarity` to `0.01..16`
  and `Gain` to `0..1`. `Noise.PingPongStrength` is clamped to nonnegative values.
  DomainWarp with `fractal: "None"` always evaluates one octave. Its Scale and Offset change
  noise sampling; `Out = UV + Displacement`, with the displacement retaining its magnitude.
- Cellular evaluates a shared 5 × 5 cell neighborhood for F1, F2 and Cell. F1/F2 are
  nonnegative distances in `UV * Scale` coordinates using the selected metric, without
  FastNoiseLite's `-1` distance offset. Hybrid adds Manhattan distance and squared Euclidean
  distance. `Jitter` clamps to `0..1`. `Cell` is a deterministic value in `[0,1)`, not a unique ID.
- Random returns a deterministic scalar in `[0,1)`. Its default Coordinates produce a constant;
  connect `UV.Out` for spatial variation or `Repeat.Cell` for one value per repeated cell.
  Repeat computes `p = UV * Scale + Offset`, `Out = frac(p)`, `Cell = floor(p)`, including
  negative cell coordinates. `Cell` is unrelated to the currently constant `InstanceIndex`.
- Stripes uses Width only in Square mode. Width is the filled fraction of a period for Square
  Stripes and Rings; Grid has independent X/Y widths. Pattern filtering smooths pixel coverage.
  Shape.Out and CombineDistance.Mask use derivative antialiasing with optional Feather.
  CombineDistance subtracts **B from A**; positive Smoothness blends the distance operation,
  while Feather only affects Mask. Sides rounds/clamps to `3..64` for Polygon and `2..64` for Star;
  Star.InnerRadius clamps to `0.001..1`. Shape.Radius affects Rounded Rectangle only.
  Line Segment's Width is its full thickness.
- GradientFactor first computes `p = (UV - Center) * Scale`. Linear returns its projection
  along Angle plus `0.5`; Radial returns `2 * length(p)`; Angular returns a wrapped turn minus
  `Angle / 360`. Linear and Radial can exceed `0..1`.
- To Polar returns `(wrapped angle in turns, 2 * distance from Center)`, subtracting Angle
  from the angular coordinate. From Polar reads `UV.x` as turns, adds Angle, and uses
  `UV.y * 0.5` as radius around Center. Mirror folds selected axes onto the positive
  half-planes relative to Center. Kaleidoscope rounds/clamps Sectors to `1..128`.
- VectorDisplacement applies `UV + decoded Displacement * Strength` component-wise. Signed RG
  uses the vector directly; Packed RG decodes `2 * Displacement - 1`, making `(0.5,0.5)` neutral.
  Both Strength components must be zero for identity. No vector normalization is performed.
- Twirl, Bulge / Pinch and Ripple have smooth radial falloff and preserve UV when Radius is
  nonpositive. Positive Bulge / Pinch Strength magnifies the sampled image; negative values pinch
  it. Wave displaces along Direction while its phase varies along the perpendicular axis.

## HLSL functions

### File declarations and ports

Declare a catalog entry with `// @whimtex-node Category/Name`, followed by optional existing
`@param` metadata and an optional `@preset("GUID", version)` pair, then the HLSL function
definition. Metadata belongs to that function; multiple marked functions may share one file.
The preset pair uses a nonempty GUID and a positive integer version. Without it, a saved file
path and exact function variant identify an explicit refresh candidate.

Inputs accept `float`/`half` scalars and vectors of two to four components, `bool`, and
`WhimTexTexture`. `float1`/`half1` are scalar aliases. The return value becomes **Result**;
`out` arguments become named outputs and `inout` arguments expose both input and output ports.
`void` functions need at least one output argument. Texture values are input-only. Arrays,
matrices, arbitrary structs and default argument expressions are not supported port declarations.

`@param` names must match actual argument names. For a non-void function, `@param ... Result`
may describe the return port; an argument also named Result makes that metadata ambiguous.
Metadata and HLSL must have the same component count and numeric/Boolean/resource type;
float and half precision aliases normalize to the same port width. `color(float4)` or
`color(half4)` selects Color semantics for a four-component argument or return value.
Metadata supplies defaults, labels and numeric bounds to the portable port definition.
For Boolean metadata use the existing `toggle(bool)` syntax. Texture inputs require graph
connections; texture metadata cannot bind an external image.

Input-only `float2`/`half2` arguments named exactly `UV` or `uv` default to the current pixel UV.
The port's **Custom / UV** control can select a numeric value instead. Other input names have
ordinary defaults; there is no automatic UV binding for `out` or `inout` arguments.

The bundled **Colors/Contrast** example is
[`src/ShaderGraphFunctions.hlsl`](https://github.com/DCFApixels/WhimTex/blob/main/src/ShaderGraphFunctions.hlsl),
at `Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl` in the project. It changes
contrast around 0.5, retains alpha and leaves HDR values unclamped. Its declaration follows
this pattern (the library also declares its own preset identity):

```hlsl
// @whimtex-node Colors/Contrast
// @param color(float4) Color = (1, 1, 1, 1)
// @param float Amount = 1 [0 .. 4]
// @param color(float4) Result
void WhimTexNodeContrast(float4 Color, float Amount, out float4 Result)
{
    Result = float4((Color.rgb - 0.5) * Amount + 0.5, Color.a);
}
```

### Custom Function editing

**Utility/Custom Function** creates an inline node. Configure its **Ports**, edit the function
body in **Code**, then choose **Compile**. The node reuses the existing HLSL code editor.
Code fills the node width without a bottom gap. It can be expanded or collapsed; the resize
handle in its lower-right corner changes node width and code height,
with the ordinary node width as the minimum. The displayed signature and active graph ports
continue to describe the applied function while body or port edits remain drafts.
Collapsing Code restores the normal node width; expanding restores the resized dimensions.
Both the expansion state and custom dimensions are saved per node in the document.

`draft` and optional `draftPorts` are saved independently from `body` and `ports`. Compile
validates a standalone invocation even when the node is unconnected, then applies body and
signature together on success. An error leaves the last applied definition intact.
**File…** selects a supported function from an `.hlsl` file, including unmarked functions.
Catalog entries use ordinary node styling and preview, without constructing a code field,
function toolbar or resize control. **Refresh HLSL**, **Choose HLSL File…** and **Convert to
Custom Function** are available in their context menu.

Conversion clones the applied definition into an independent local function (`inline: true`,
`fullSource: true`), clears preset/path/fingerprint identity and preserves node ID, values,
ports, connections and include closure. Shared definitions stay unchanged for other nodes.
`body` and `draft` initially contain the archived main module, not an expanded include bundle.
The code editor edits the full source; its entry point remains `entryPoint`. On Compile the
portable parser resolves that declaration and applies signature changes with the code.
Module declarations replace the separate Ports editor. The compiler emits the full module
inside its own namespace, without wrapping it as a function body. Other local Custom Functions
keep `fullSource: false` and their existing body/Ports workflow. Conversion is one Undo operation.

An inline body may contain `#include` snippets. Compile captures the applied body and its
complete dependency closure in `graph.sources`, assigning the inline function a `sourceId`;
without includes, `sourceId` is null. The archived root's `code` must exactly equal the applied
`body`, otherwise validation fails. Compilation expands the archived body. API/JSON input
with inline includes but no archive is rejected; loading never fetches dependencies from disk.
New inline functions resolve relative includes from `Assets`; prefer explicit `Assets/...` or
`Packages/...` paths. Source cleanup follows every function's `sourceId`, including inline ones.
For converted full-source functions, Compile retains archived bindings for unchanged include
names, even if the original files changed or disappeared. Newly added includes are captured
from disk using the same path rules. Source changes are staged and validated before applying.

### Discovery and explicit refresh

`ShaderGraphFunctionCatalog.Entries` caches project/package and user-library discovery.
**Refresh HLSL** rescans it explicitly. Ordinary discovery parses only marked files; it does
not capture include archives. File picking uses `LoadFile(path)` with unmarked functions
enabled. Duplicate menu paths receive signature/path suffixes so every entry remains selectable.
`Errors` exposes catalog diagnostics for the editor.

`Entry.Import(graph)` clones the definition, captures the full source dependency closure and
adds a new function to the graph. Each import has its own definition and values; equal source
modules may share the archive. `sourceFingerprint` records the captured root content key,
including dependency bindings. Link checks cache closure fingerprints until the next refresh.

`Find(definition)` requires a unique identity/version and matching function variant plus an
exact backup fingerprint. `FindForRefresh(definition)` permits changed code for the same unique
identity/version and variant; it also checks the saved path for unmarked or external functions.
The library node's **Refresh HLSL** action validates the candidate before applying it. Rendering always uses
the document archive. Missing, changed, incompatible or ambiguous sources do not automatically
replace it; failed refresh retains the backup. Use **Choose HLSL File…** for an intentional replacement.

On Unity 6.5+, the optional adapter reads only public `ShaderInclude.Reflection.ReflectedFunctions`.
Functions marked with `UNITY_EXPORT_REFLECTION` can be converted into the same portable model;
WhimTex declarations take precedence. Automatic native discovery excludes `Packages/com.unity.*`
engine libraries. Explicit file import still reports unsupported signatures or missing includes.
The adapter maps native display/search names, category, provider key, Color, default, range and
UV hints. A native provider key is retained as an opaque identity with version 0. Unsupported
native functions report individual diagnostics. Unity 6.3 uses the portable declarations and
archived source without depending on the native reflection API.

### Archived module compilation

`ShaderGraphModuleSource.Prepare` prepares archived code for an isolated namespace per root.
It preserves macro names and token pasting (`##`), using ownership markers and conditional
cleanup. An active `#define` or `#undef` that collides with an already global macro produces a
diagnostic instead of changing that macro. This preparation does not rewrite the saved source.

Unity expands engine includes at module scope outside the wrapper namespace, retaining their
preprocessor conditions. Engine includes inside nested namespaces or functions are rejected,
as are scope-changing macros used with engine includes. Include placement and macro scope
must therefore remain explicit in an imported module.

## Authoring and persistence

The editable graph and parameter data belong to the behavior. Preserve its node identities,
connections, parameter declarations and values when storing or copying the layer; generated
HLSL is a compilation product, not a substitute for the editable graph. The graph is the saved
source of truth; retaining a working runtime shader does not replace the saved draft with it.

`ShaderGraph.Validate()` validates storage structure, not compilation readiness. Structurally
valid incomplete drafts can be saved, including an unconnected resource input or a Parameter
node with no declaration assigned. Saving or passing storage validation does not establish that
the draft can compile. Complete the missing inputs and resolve compilation diagnostics before
treating the current graph as a valid image generator.

Use the [document format](JSON_FORMAT.md) and [generated model schema](AI/document.schema.json)
for exact serialized fields, and the [Agent API](AgentAPI.md) for supported operations. This
contract does not introduce a separate JSON envelope or new `@param` syntax. Effect directives
describe HLSL FX and must not be substituted for the Shader layer's graph declarations.

### Embedded HLSL source archive

Custom Function definitions are stored in `graph.functions`; nodes refer to them by
`functionId`. A file-backed definition refers to its embedded backup through `sourceId`.
The optional `draftPorts` list stores pending signature edits alongside `draft`; the existing
`ports` and compiled body remain separate from those pending edits. A nonempty `draftPorts`
implies an active draft; `hasDraftPorts: true` also represents an intentionally empty draft.
An empty/null list without the flag is not a draft. This distinction survives Unity domain reload,
Undo and JsonUtility copies, which can materialize null lists as empty lists. The UI displays the
applied ports in that case. Successful Compile clears the draft list and its flag.

`graph.sources` is a list of `{ id, code, includes }` modules. `code` is the original text,
including its include directives. Each `includes` entry is `{ requested, sourceId }`, where
`requested` is the exact include string without its quotes or angle brackets. Bindings belong
to the including module: two files may resolve `"common.hlsl"` to different dependencies.
Repeated directives with the same requested spelling use one binding and still expand at
each original directive location. The archive service does not prune sources, branches or
unreferenced entries; editor structural cleanup may remove obsolete function versions separately.

`ShaderGraphSources.Capture(source, path, destination)` accepts an absolute source-file path
(including the user node library), or an `Assets/...` or `Packages/...` path. Package paths
resolve through the public package API. The source string may contain unsaved edits; the
supplied path establishes the relative-include base. A null path permits inline source with
explicit project/package includes. Capture returns an opaque `src_<sha256>` ID and merges
modules only when their code and dependency bindings match. It publishes additions only after
the entire capture succeeds; existing entries remain unchanged on failure.

`Expand(sourceId, sources)` validates and expands only the saved archive, without reading any
source files. Preprocessor order and conditions are retained, including includes in inactive
branches. Capture therefore requires every non-engine literal include to resolve, even inside
`#if 0`. Macro-generated includes and unknown missing filenames fail with diagnostics.
Only exact names in the engine whitelist may remain unbundled, including `UnityCG.cginc` and
`ShaderApiReflectionSupport.hlsl`; arbitrary path prefixes or matching filename suffixes do
not qualify. Unity 6.3 compilation removes the optional reflection support include and export
marker in the compiler adapter, without changing the archived text.

Explicit include guards are preserved. Top-level `#pragma once` is represented by a stable
module guard during expansion; the stored directive remains unchanged. Conditional
`#pragma once` and all include cycles, including guarded cycles, are rejected explicitly.

Limits are measured in UTF-8 bytes: 8 MiB per source, 32 MiB per archive and expanded result,
4,096 modules, 32,768 include directives and 64 nested modules. Expansion also limits include
visits to 65,536. Capture limits inspected input to 256 MiB and 65,536 file visits, even when
duplicate modules reduce the final archive size. Source IDs contain 1–128 ASCII letters,
digits, underscores or hyphens; saved IDs are opaque and can remain stable during explicit edits.

JSON, clipboard and Unity Undo retain plain `code` strings. Native TIFF encodes those fields
using tagged value 33 and container blocks named `shader-source:sha256:<text digest>`.
Compression uses the existing container codec; identical text is stored once across every
graph in the TIFF, even when the modules have different include bindings. Bindings and source
IDs stay in the document model. Reads check the block size and SHA-256 before reconstructing
ordinary strings, with a 32 MiB limit on the unique shared text table. Older readers fail on
the unknown tag instead of interpreting the block reference as source code.

### Create from an output connection

Dropping a new wire from an output on empty graph space opens the existing search window,
filtered with the same numeric/color conversions and resource-type rules as ordinary wiring.
The menu opens to the right of the cursor with its left edge anchored there; native screen-edge fitting still applies.
Every compatible input gets a separate `NodeName(InputName)` entry. Creation and connection
share one document Undo operation; cancellation changes no model data. The node uses the drop
position in graph coordinates. Selection revalidates the source and current type, graph identity,
editability and capacity limits. Output, Parameter and Reroute are excluded from this creation
menu; the existing Output remains unique and Reroute still inserts into an existing connection.

### Reroute storage and compilation

A Reroute node uses JSON `kind: "Reroute"`, with ports named `In` and `Out`. Store its node ID,
`position` and ordinary graph edges alongside the other nodes. Its `valueType` stores the type
used to display the node while disconnected. When connected, the incoming source determines
the forwarded type and value; `valueType` does not cast, override or supply a default value.

`In` must be connected for the Reroute to compile. An unconnected Reroute can remain in a saved
incomplete draft, but its display type does not make it a compilable value source. Compilation
forwards the source expression or resource through `Out` without adding HLSL calculations.
Discovery marks this input with `passthrough:true`; it accepts the source's numeric, Color,
Boolean or resource type. Dynamic math inputs accept numeric and Color values and normalize
Color to `float4` for computation.

### API and document JSON

This complete `add` operation creates a Shader layer with one exposed Color parameter connected
to Output. Put it in the request's `operations` array:

```json
{
  "op": "add", "type": "shader", "as": "tint",
  "settings": {
    "name": "Tint", "shader": {
      "graph": {
        "version": 1,
        "parameters": [
          { "id": "11111111111111111111111111111111", "name": "_Tint", "type": "Color", "colorValue": [0.15, 0.5, 1, 1] }
        ],
        "nodes": [
          { "id": "22222222222222222222222222222222", "kind": "Parameter", "position": [40, 80], "parameterId": "11111111111111111111111111111111" },
          { "id": "33333333333333333333333333333333", "kind": "Output", "position": [360, 80] }
        ],
        "edges": [
          { "outputNode": "22222222222222222222222222222222", "outputPort": "Out", "inputNode": "33333333333333333333333333333333", "inputPort": "RGBA" }
        ]
      }
    }
  }
}
```

For an existing layer, use `op:"set"`, its `layer` ID or alias, and the same
`settings.shader.graph` object. The graph is replaced completely, not patched by array index;
preserve existing parameter/node IDs and include every retained node, parameter and edge.
Inspection returns the graph at `settings.shader.graph`. Discovery supplies `shaderDefaults`,
`shaderNodes`, `shaderValueTypes`, `shaderWrapModes` and `shaderFilterModes`.
Each node catalog entry includes `category`, exact `inputs` and the existing string-array
`outputs`. Procedural/UV entries additionally expose `outputTypes`, an output-name-to-type map.
Nodes with shared mode metadata provide `modeLabel`, `defaultOption` and `modes` entries
containing `{ option, name }`. Shape marks `dynamicInputs:true`; every Shape mode also contains
its own `inputs` array, while the entry's top-level inputs describe the default mode.
Noise and DomainWarp provide `fractalModes` (serialized enum names) and `defaultFractal`.
Names, categories, mode labels, port sets and procedural output types come from the shared
node model; descriptions explain units and evaluation. These discovery fields are not extra
fields to copy into a serialized node.

In stored `whimtex.document` JSON, place this same graph at `layers[].behaviour.graph`, with
`behaviour.$type` set to `ShaderLayerBehaviour`; the API's `op`, `as` and `settings` wrapper is
not stored. Keep `graph.version:1`. Parameter `controls` are saved declarations, not regenerated
HLSL metadata. Texture parameters use the existing `textureSource`, `textureLayerId` and
`textureValue` asset-reference fields. Native TIFF can embed non-asset textures; JSON requires
persisted asset references. The [generated schema](AI/document.schema.json) describes those fields.
