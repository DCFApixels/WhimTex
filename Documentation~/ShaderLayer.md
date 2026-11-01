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
parameters, read by Sample Texture or the height-processing nodes. The generated image participates in the ordinary layer
transform, FX and composition pipeline; Output is not the final document composite.

Create nodes through the graph context menu's **Create Node**, **Space**, or by dropping an input or output
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

Visible port labels append the inferred type in parentheses: `1`, `2`, `3`, `4` for numeric
widths, `T2` for Texture, `G` for Gradient, `C` for Curve, `col` for Color and `bool` for Boolean.
Parameter names and custom HLSL labels use the same notation. Reroutes remain label-free;
the suffix is display-only and never changes a stored port name or connection.

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
Sub Graph instances also carry their referenced definitions and nested definition closure,
including required parameters and HLSL sources. Same-graph paste reuses existing definitions;
cross-graph paste imports definitions and remaps node/reference IDs while retaining interface IDs.
Output and definition-only interface nodes are not copyable. Text-field commands retain normal text editing behavior.

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
`0..1`. Image Size describes document dimensions; Texture Size describes a selected texture.
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

## Sampled branch filters

`Blur`, `Sharpen`, `EdgeDetection`, `Dilate` and `Erode` take numeric or Color `In`,
Float2 `UV` (current-pixel UV by default) and Float `Radius` (default 1). Sharpen and
EdgeDetection also take Float `Strength` (default 1). Each has one `Out` with the same
type as In. Numeric vectors are processed component-wise. For Color, Blur filters premultiplied
RGB and alpha, then returns straight RGB (zero at exactly zero alpha); Sharpen and EdgeDetection
process RGB while retaining center alpha, independent of Strength. Dilate/Erode remain component-wise.
Resource and Boolean inputs
must be converted to a numeric/Color branch first.

The compiler emits an HLSL function for the upstream branch and calls it at `UV + offset`.
This reevaluates UV-dependent calculations, including procedural generation and texture
sampling. It does not allocate intermediate GPU textures or add rendering passes. Explicit
constant coordinates in the branch remain constant. Nested filters reevaluate their upstream
filters for each sample and can be expensive even when generated function code is shared.

Radius is measured in **document pixels**, using reciprocal document dimensions for UV offsets;
it is not measured in a bound texture's texels. Node previews and layer renders use the same
pixel metric; reducing preview resolution does not enlarge the filter footprint. Image Size
also reports document dimensions. `_InputSize` remains the actual render-target size for HLSL.
Radius uses its absolute value; at zero, Blur/Sharpen/Dilate/Erode return In and EdgeDetection
returns zero RGB with center alpha for Color, or zero for numeric inputs.
UVs are not implicitly clipped; upstream texture samplers determine edge behavior.

| `kind` | Kernel and result |
| --- | --- |
| `Blur` | Normalized finite Gaussian kernel; constant inputs stay constant. |
| `Sharpen` | `input + (input - blur) * Strength`, using the same Gaussian kernel; Color alpha is retained. |
| `Dilate` | Component-wise maximum over the kernel. |
| `Erode` | Component-wise minimum over the kernel. |
| `EdgeDetection` | Fixed 3 × 3 Sobel gradient magnitude, multiplied by Strength. Color retains alpha; numeric vectors process every component. No implicit luminance conversion. |

For Blur, Sharpen, Dilate and Erode, `option` selects **0: 3 × 3**, **1: 5 × 5** or
**2: 9 × 9** (default 0) as the **Minimum Kernel**. Samples span `[-Radius, Radius]` in each axis;
the actual half-extent is `max(selectedHalfExtent, ceil(abs(Radius)))`, keeping the step at most
one document pixel. Large radii increase sample count quadratically, and nested filters multiply
that cost. This samples the pixel neighborhood; subpixel features can still require a denser
minimum kernel. EdgeDetection always uses its 3 × 3 stencil.
None of these nodes clamps its result to `0..1`.

Built-in antialiasing in sampled Shape/Combine Distance masks, Checkerboard, Stripes, Grid
and Rings uses centered field differences over the render pixel's UV footprint. The entry
point captures that footprint before filter loops; connected UV transforms and field inputs
are reevaluated at the neighboring coordinates. Thus a spatially varying Radius does not
require hardware derivatives inside divergent loops. Direct, unfiltered evaluation keeps its
hardware derivatives. This adds field evaluations to sampled antialiasing, including nested
branches. Custom HLSL retains its own derivative semantics: `SampleTexture`/`SampleTextureLod`
can be sampled in these loops; raw implicit-LOD `tex2D`/`tex2Dbias` or explicit derivatives
inside varying control flow still need care in the function's implementation.

## Pixelate UV, color adjustments and path blurs

| Category / kind | Inputs and defaults | Output |
| --- | --- | --- |
| UV / `PixelateUV` | UV: current pixel; CellSize: Float2=(8,8); Offset: Float2=(0,0) | Float2 |
| Color/Adjustments / `ChannelMixer` | Color; Red: Float3=(1,0,0); Green=(0,1,0); Blue=(0,0,1) | Color |
| Color/Adjustments / `WhiteBalance` | Color; Temperature=0; Tint=0 | Color |
| Filter / `DirectionalBlur` | In: dynamic numeric/Color; UV: current pixel; Radius=8; Angle=0; Samples=17 | Same as In |
| Filter / `RadialBlur` | In: dynamic numeric/Color; UV: current pixel; Center: Float2=(0.5,0.5); Angle=10; Strength=0.1; Samples=17 | Same as In |

Pixelate UV snaps to cell centers using `(floor((UV*CanvasSize-Offset)/size)+0.5)*size+Offset`,
then divides by CanvasSize. CellSize and Offset use document pixels, independently of preview
resolution. Each effective cell dimension is `max(abs(CellSize),1)`; coordinates are not clamped.
Channel Mixer computes three dot products with its coefficient rows; 1 is full contribution,
weights may be negative, and neither weights nor output are normalized or clamped. Alpha is retained.
White Balance applies Bradford chromatic adaptation in linear RGB. Temperature and Tint clamp
to -1..1; zero is neutral, positive Temperature warms, positive Tint shifts toward magenta.
The source white uses `x=0.3127-Temperature*0.05`,
`y=2.87*x-3*x*x-0.27509507+Tint*0.05`, adapted to D65. RGB remains signed/HDR and alpha is retained.

Directional Blur samples a symmetric line over ±Radius document pixels at Angle degrees.
Radial Blur's `option` is 0=Spin (default), 1=Zoom. Spin samples ±Angle degrees around Center
in document-pixel space, so rectangular documents retain circular rotation. Zoom samples radius
factors `1 ± abs(Strength)`. Angle is inactive in Zoom and Strength is inactive in Spin; inactive
branches do not affect the compiled output. A zero amount or the radial center returns the input.
Both use normalized Gaussian weights over a single path, reevaluate the connected branch without
an intermediate texture, and preserve its numeric type. Color filters premultiplied RGB/alpha and
returns straight RGBA; numeric vectors process components independently. Samples rounds upward
to an odd count in 3..129. Longer paths or finer detail require more samples; nested filters multiply
the work. Sampling retains the existing explicit AA footprint rules. Results are not clamped.

## Reusable Sub Graphs

`graph.subGraphs` is an embedded `List<ShaderGraphSubGraph>`, empty by default. Each definition
stores `id`, `name`, `inputs`, `outputs`, `nodes`, `edges`, `notes` and `groups`.
Each interface port stores `id`, `name`, `type`, `value` and `uvDefault`. `value` is the
unconnected default; `uvDefault` is valid only for Float2 and supplies the evaluation UV.
Resource ports require resource connections. Input and output IDs must be unique across
the definition's interface; display names can change without changing wiring keys.

- A `SubGraph` node references a definition through `subGraphId`. Its input/output port keys
  are the definition's stable interface IDs, not its display names.
- A definition's `SubGraphInput` node uses `parameterId` to reference one declared input
  and exposes `Out`. This does not reference `graph.parameters`.
- Exactly one `SubGraphOutput` terminates each definition. Its dynamic input keys are the
  definition's output interface IDs. It has no outputs. Root graphs use ordinary Output;
  definition-only interface nodes cannot be added to the root graph.

Definitions share root `parameters`, `functions` and `sources`; these are not copied into
each definition. Compilation expands instances into the enclosing calculation. Editing a
definition affects all its instances; it introduces no external file or asset dependency.
Snapshots, document JSON and native TIFF retain the embedded table with the graph.

Limits: at most **128 definitions**, **64 input pins** and **64 output pins** per definition
(at least one output), **16 nesting levels**, and **4096 expanded nodes**. Recursion is forbidden.
The ordinary limits of 512 nodes and 4096 edges apply within each scope; node IDs must be
unique across the root and every definition. Notes/groups retain their ordinary per-scope rules.

Select a connected calculation and use **Convert Selection to Sub Graph** in the context menu. Double-click
an instance to edit its definition, use **Back to Graph** to return to the root, and edit
**Inputs**/**Outputs** in the sidebar. Existing definitions are available in the creation menu.
Discovery marks SubGraph with `requiresDefinition`, `definitionField: "subGraphId"`,
`definitionTable: "subGraphs"`, `dynamicInputs` and `dynamicOutputs`. Its empty catalog port
lists are placeholders; inspect the referenced definition. SubGraphInput/SubGraphOutput are
not ordinary catalog creation entries.

## Compilation and invalidation

The connected calculation generates a WhimTex FX script (`@param` declarations, helper functions
and `ApplyFX`). The shared FX source builder then produces ShaderLab for a single
fragment-shader generation pass. Layer rendering and individual node previews use this same path.
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

The displayed source is the intermediate FX script passed to the shared source builder, before
the ShaderLab wrapper. Only reachable exposed parameters and required hidden input uniforms are
declared. Library metadata inside HLSL nodes remains inert comments; it does not declare extra FX
parameters or a preset identity. Resource values and IDs remain bound from the graph document,
including multi-key/Custom gradients, curves and layer texture sources. The script is not a
resource export. Numeric declaration defaults are captured at compilation; subsequent value-only
edits update material bindings without regenerating the script.

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

## Node set

Names below identify operations; grouped rows do not imply a single combined node.

| Operations | Purpose |
| --- | --- |
| Parameter, Constant | Shared parameter reads and named mathematical constants. |
| Instance Index, Time | Input nodes with one Float output, Out, and no own preview. Both have Float Start (default 0) and Scale (default 1) inputs. Instance Index returns `Start + (float)_WhimTexInstanceIndex * Scale`; the supplied index currently defaults to 0. Time returns `Start + _WhimTexTime * Scale`; Editor seconds animate graph previews, while other renders supply time 0. |
| Custom Function, cataloged HLSL functions | Inline function bodies or functions imported from an HLSL library. |
| Reroute | Forward a typed connection unchanged; insert on an existing edge, not through node search. |
| UV, Image Size, Texture Size | Coordinates and image dimensions. |
| Sample Texture, Sample Gradient, Sample Curve | Read typed resources. |
| Split, Combine, Swizzle | Component access, vector construction, reordering and zero insertion. |
| Add, Subtract, Multiply, Divide | Basic arithmetic. |
| Min, Max, Clamp, Saturate, Abs, Sign, One Minus, Negate | Bounds, absolute value, sign, `1 − x` and sign inversion. |
| Power, Square Root, Exp, Log, Modulo | Powers, roots, exponentials, logarithms and remainders. |
| Floor, Ceil, Round, Fraction | Integer boundaries and fractional part. |
| Sine, Cosine, Atan2, Asin, Acos | Trigonometric functions. |
| Lerp, Remap, Step, Smoothstep | Interpolation, range mapping and thresholds. |
| Unlerp | Component-wise `(T-A)/(B-A)` without clamping. Dynamic numeric inputs A=0, B=1, T=0; Out uses the widest input width, with Color treated as Float4. Equal bounds are undefined. |
| Length, Distance, Dot, Normalize, Cross, Reflect, Refract | Vector calculations. |
| Compare, Select, And, Or, Not | Boolean conditions and conditional values. |
| Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope, Flipbook | Coordinate transforms and atlas-frame UVs. |
| Domain Warp, Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple | UV distortions for downstream sampling and procedural nodes. |
| Noise, Cellular, Random, Tileable Noise, Voronoi Edge | Spatial and repeating noise, cellular distances and cell boundaries. |
| Checkerboard, Stripes, Grid, Rings, Gradient Factor, Dither | Procedural masks, gradient coordinates and dithering. |
| Shape, Combine Distance | Antialiased shape masks and signed-distance operations. |
| Hue, Saturation, Exposure, Lightness, Levels, Color Balance, Posterize | Color adjustments that preserve source alpha. Contrast is the existing cataloged HLSL function. |
| Convert Color, Luminance, Gradient Map, Color Curves | Explicit color conversion, luminance extraction and resource-based remapping. |
| Color Mask, Hue Range, Mix Color, Set Alpha, Premultiply, Unpremultiply | Color selection, interpolation, alpha control and alpha representation. |
| Height to Normal, Normal Blend, Height to Distortion, Normal to Distortion | Normals and displacement from textures or height branches. |
| Normal Encode, Normal Decode, Normal Strength, Normal Lighting | Normal representation, detail strength and lighting with specular. |
| Blur, Sharpen, Edge Detection, Dilate, Erode | Sample an upstream numeric/Color branch at neighboring UVs. |
| Sub Graph | Reuse an embedded definition through its declared interface. |
| Blend | RGBA blending using the existing blend semantics. |
| Output | Final straight RGBA through a fixed Color input; modest visual distinction and no per-node preview. |

HLSL FX authoring continues to use the [separate effect contract](ShaderFX.md).

`Time` uses one shared Editor-time sample per update in visible node previews and the graph's
result preview, including active FX. It returns `Start + _WhimTexTime * Scale`; Instance Index returns
`Start + (float)_WhimTexInstanceIndex * Scale`. HLSL libraries and Custom Function can read the same
automatically declared `float _WhimTexTime` and `uint _WhimTexInstanceIndex` directly. The HLSL
uint keeps all 32 bits; the graph's Float output has ordinary floating-point precision.
Only compiled outputs that may depend on time update continuously; hidden/collapsed
previews do not render. Time is a uniform, so animation does not change the code fingerprint,
recompile shaders, or dirty the document. Graph result previews bypass the layer output/content
caches and retain transforms, FX and clipping. Referenced layers, Canvas, ordinary layer previews,
thumbnails and export supply time 0, so Time.Out equals Start there. Start and Scale are saved as
ordinary node inputs; no clock value is saved in the document.
`ShaderLayerBehaviour.UsesTime` / `UsesInstanceIndex` describe the compiled output, including
reachable HLSL modules; node previews analyze their own reachable subgraph. Within a module,
analysis is conservative over its complete expanded source. See the [shared context contract](ShaderFX.md#evaluation-time-and-instance-index)
for FX, dependency flags and unresolved macros/includes.

### Extended math, normals and generators

Use these exact `kind` and port identifiers in JSON. Every node in this section has one
`Out`. Dynamic numeric nodes infer the widest numeric operand width, treating Color as Float4;
Boolean and resource values do not enter numeric math implicitly. Angles returned by inverse
trigonometric nodes are **radians**, unlike the degree controls on UV transforms.

| Category | `kind` | Inputs and defaults | Result |
| --- | --- | --- | --- |
| Math | `Sign`, `Saturate` | numeric In = 0 | Component-wise sign (-1, 0, 1), or clamp to `0..1`. |
| Math | `Modulo` | numeric A = 0, B = 1 | `A - B * floor(A/B)`; nonzero results have B's sign. B = 0 is undefined. |
| Math | `Atan2` | numeric Y = 0, X = 1 | `atan2(Y,X)` in radians; `(0,0)` is undefined. |
| Math | `Asin`, `Acos` | numeric In = 0 | Inverse sine/cosine in radians; requires `[-1,1]`, without implicit domain clipping. |
| Math | `Exp`, `Log` | numeric In = 0 for Exp, 1 for Log | Natural exponential/logarithm; Log requires positive input without implicit domain clipping. |
| Vector | `Cross` | Float3 A = (1,0,0), B = (0,1,0) | Float3 cross product, without normalization. |
| Vector | `Reflect` | numeric Incident = -1, Normal = 1 | Reflection at their widest numeric width; supply a unit Normal. |
| Vector | `Refract` | numeric Incident = -1, Normal = 1; Float Eta = 1 | Refraction at their widest numeric width; supply unit vectors. Total internal reflection returns zero. |
| Logic | `And`, `Or` | Boolean A = false, B = false | Boolean conjunction/disjunction. |
| Logic | `Not` | Boolean In = false | Boolean negation. |
| Normal | `NormalEncode` | Float3 Normal = (0,0,1) | Float3 `Normal * 0.5 + 0.5`, signed XYZ to packed RGB. |
| Normal | `NormalDecode` | Float3 Normal = (0.5,0.5,1) | Float3 `Normal * 2 - 1`, packed RGB to signed XYZ. |
| Normal | `NormalStrength` | Float3 Normal = encoding-dependent flat normal; Float Strength = 1 | Normalized Float3 in the selected encoding; 0 is flat, 1 preserves the unit normal, negative values reverse XY. |
| Normal | `NormalLighting` | Color = white; Float3 Normal = encoding-dependent flat normal; LightDirection: Float3 = (-0.5,0.5,1); LightColor: Color = white; Intensity = 1; Ambient = 0.15; SpecularColor: Color = white; SpecularIntensity = 0; Roughness = 0.25; SpecularOnly: Boolean = false | Color lit by ambient, diffuse and optional specular; surface alpha passes through. |
| Color/Alpha | `Premultiply`, `Unpremultiply` | Color = white | Color with RGB multiplied/divided by alpha; alpha is retained. |
| UV | `Flipbook` | UV; Columns = 4; Rows = 4; Frame = 0 | Float2 atlas coordinates for one frame. |
| Procedural/Noise | `TileableNoise` | UV; Period: Float2 = (1,1); Cells: Float2 = (8,8); Offset: Float2 = (0,0); Seed = 1337 | Float periodic Perlin noise with a wrapped gradient lattice. |
| Procedural/Noise | `VoronoiEdge` | UV; Scale: Float2 = (8,8); Seed = 1337; Jitter = 1 | Float nonnegative distance to the nearest cell boundary in `UV * Scale` coordinates. |
| Color/Adjustments | `Dither` | Color = white; UV; Levels = 8; PixelSize = 1 | Color with Bayer-quantized RGB and unchanged alpha. |

Unqualified scalar inputs are Float; UV is Float2 with current-pixel coordinates by default.
Cross, Reflect and Refract do not normalize automatically. NormalEncode/NormalDecode have
fixed directions and do not use `normalEncoding`, normalize, clamp or convert color spaces.
NormalStrength and NormalLighting use `normalEncoding: "PackedRGB"` by default, with
`"SignedXYZ"` as the alternative. Their neutral input defaults are `(0.5,0.5,1)` and `(0,0,1)`.
NormalStrength decodes and normalizes, scales XY by Strength, blends Z from 1 to the unit normal's
Z by `saturate(abs(Strength))`, then normalizes and reencodes.

NormalLighting normalizes Normal and LightDirection. Its diffuse term is
`Color.rgb * (Ambient + LightColor.rgb * max(dot(N,L),0) * Intensity * LightColor.a)`.
Specular uses the fixed view direction `(0,0,1)` and the normalized halfway vector with L.
Roughness is bounded to `0.01..1`; smaller values sharpen the highlight. SpecularColor alpha
scales SpecularIntensity, independently of the diffuse light tint and intensity. SpecularOnly
omits ambient/diffuse while preserving Color alpha. No final RGB clamp is applied, including
for negative intensities or HDR inputs. SpecularIntensity defaults to zero, so enable it explicitly.

Unpremultiply returns zero RGB at exactly zero alpha; any other alpha, including tiny or negative
values, is used directly as the divisor. Premultiply/Unpremultiply do not clamp. Output expects
straight RGBA; restore straight color before connecting a premultiplied branch to Output.

Flipbook floors Columns/Rows and clamps them to `1..4096`; Frame is floored and wraps across
the frame count, including negative frames. `option: 0` is Bottom to Top (frame 0 at bottom-left),
`option: 1` is Top to Bottom. Row order does not flip local UV orientation. Flipbook returns UV,
without sampling or clipping it; connect Out to SampleTexture.UV.

TileableNoise's Period is a UV repeat length: its absolute value is bounded below by `1e-5`.
Cells is rounded and clamped to `1..4096` per axis; Offset is measured in UV units. It wraps
gradient-lattice indices, not completed images; ordinary Noise's `option` and `fractal` do not
apply. VoronoiEdge clamps Jitter to `0..1` and returns distance to the cell boundary, not `F2-F1`
or a coverage mask. Threshold that distance explicitly for cell-border lines.

Dither's `option` values are **0: Bayer 4x4**, **1: Bayer 2x2**, **2: Bayer 8x8**. Levels rounds
to at least 2; RGB quantization spacing is `1 / (Levels - 1)`, continuing beyond `0..1` without
clipping signed/HDR values. PixelSize uses its absolute value, at least one document pixel.
Its pattern follows document dimensions, independent of preview size; alpha is not quantized.

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

The UV distortion nodes in this table return coordinates, not sampled colors or masks. Connect their `Out` to
the `UV` input of a procedural or sampling node. They do not clamp coordinates to `0..1`;
`SampleTexture` wrap settings determine how out-of-range image coordinates are sampled.
`Strength = 0` leaves the input UV unchanged. Displacement vectors retain their magnitude;
they are not normalized. `TransformUV` belongs to **UV**, independently of the layer Transform.

`Wave`, `Ripple`, `Stripes` and `Rings` expose a separate Float `Phase` input. Supply a parameter
or another scalar expression to shift the pattern; `Time.Out` animates it inside graph previews.
Periodic patterns reduce spatial coordinates and Phase/Offset separately before combining them,
so a large uniform offset does not erase the spatial fraction. Antialiasing uses the signed
differences of the unwrapped components, including in sampled branches, rather than derivatives
across wrapped seams. Pulse and triangle coverage integrate local intervals plus complete periods
without subtracting large accumulated integrals. Phase repeats every cycle; Grid Offset repeats
every unit and Checkerboard every two units per axis. Sine's narrow-footprint attenuation uses
the near-zero sinc limit to avoid dividing an inaccurate tiny sine by its argument. This preserves the supplied float values;
it cannot recover precision already lost in upstream arithmetic or change Time's precision.
Mask edges are smoothed. A signed-distance output is negative inside, zero on the boundary
and positive outside; do not treat it as a `0..1` mask. `Shape.Distance` retains the existing
approximate ellipse distance, so an ellipse is not guaranteed to have an exact Euclidean SDF.
Polygon and Star measure the nearest actual edge in rotated UV space, including nonuniform Size;
their Distance and Feather no longer inherit the shorter axis's scale as an approximation.
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
current-pixel UV; `Random.Coordinates` is Float2 and uses the same current-pixel UV default.
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
- Random returns a deterministic scalar in `[0,1)`. Coordinates use current-pixel UV when no
  edge or explicit input literal is present. Custom sets constant coordinates; existing explicit coordinates remain Custom.
  Connect `Repeat.Cell` for one value per repeated cell, or another source to override the default.
  Repeat computes `p = UV * Scale + Offset`, `Out = frac(p)`, `Cell = floor(p)`, including
  negative cell coordinates. `Cell` is unrelated to the index supplied to `InstanceIndex` (currently zero).
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

### Color and normal nodes

All native nodes below have a single `Out` port. The existing Contrast HLSL function appears
at **Color/Adjustments/Contrast** and keeps its `Result` port; there is no native `Contrast` kind.

| Category | Serialized `kind` | `Out` type |
| --- | --- | --- |
| Color/Adjustments | `Hue`, `Saturation`, `Exposure`, `Lightness`, `Levels`, `ColorBalance`, `Posterize` | Color |
| Color/Conversion | `ConvertColor` | Color |
| Color/Conversion | `Luminance` | Float |
| Color/Mapping | `GradientMap`, `ColorCurves` | Color |
| Color/Selection | `ColorMask`, `HueRange` | Float |
| Color/Mixing | `MixColor`, existing `Blend` | Color |
| Color/Alpha | `SetAlpha`, `Premultiply`, `Unpremultiply` | Color |
| Normal | `HeightToNormal`, `NormalBlend`, `NormalEncode`, `NormalDecode`, `NormalStrength` | Float3 |
| Normal | `NormalLighting` | Color |
| UV/Distortion | `HeightToDistortion`, `NormalToDistortion` | Float2 |

#### Color inputs and modes

Inputs belong in `node.inputs`. Unless a type is stated, a numeric input is Float. `Color`
is a Color input, default opaque white `(1,1,1,1)`. Resource inputs require a connection except
the explicitly optional ColorCurves inputs. The defaults below describe newly created nodes.

| `kind` | Inputs and defaults |
| --- | --- |
| `Hue` | Color; Degrees = 0 |
| `Saturation` | Color; Amount = 1 |
| `Exposure` | Color; Stops = 0 |
| `Lightness` | Color; Amount = 0 |
| `Levels` | Color; InputRange: Float2 = (0,1); Gamma = 1; OutputRange: Float2 = (0,1); Clamp: Boolean = true |
| `ColorBalance` | Color; Shadows, Midtones, Highlights: Float3 = (0,0,0); ShadowRange, HighlightRange = 0.5; PreserveLuminance: Boolean = true |
| `Posterize` | Color; Levels = 8 |
| `ConvertColor`, `Luminance` | Color |
| `GradientMap` | Color; Gradient: required Gradient resource |
| `ColorCurves` | Color; Master, Red, Green, Blue, Alpha: optional Curve resources, each identity when disconnected |
| `ColorMask` | Color; KeyColor: Color = (0,1,0,1); Tolerance = 0.1; Softness = 0.05 |
| `HueRange` | Color; Range: Float2 = (330,30); Feather = 15; SaturationRange: Float2 = (0.05,0.1) |
| `MixColor` | A: Color = (0,0,0,1); B: Color = (1,1,1,1); T = 0.5 |
| `SetAlpha` | Color; Alpha = 1 |
| `HeightToNormal`, `HeightToDistortion` | Height: required Texture for `heightSource: "Texture"`, Float = 0 for `"Value"`; UV: Float2 = current-pixel UV; Strength = 8; Radius = 1; FlipX, FlipY: Boolean = false |
| `NormalBlend` | Base, Detail: Float3 = (0.5,0.5,1) for PackedRGB or (0,0,1) for SignedXYZ; Strength = 1 |
| `NormalToDistortion` | Normal: Float3 with the same encoding-dependent neutral default; Strength: Float2 = (1,1); FlipX, FlipY: Boolean = false |

`option` remains a zero-based integer. Mode labels are not serialized strings.

| `kind` | `option`: label | New-node default |
| --- | --- | --- |
| `Levels` | 0: Luminance; 1: RGB | 0 |
| `ColorCurves` | 0: RGB; 1: Luminance | 0 |
| `ColorMask` | 0: Chroma; 1: RGB; 2: Perceptual | 0 |
| `MixColor` | 0: Perceptual; 1: Linear RGB; 2: sRGB | 0 |
| `GradientMap` | 0: Preserve; 1: Multiply; 2: Replace | 0 |
| `SetAlpha` | 0: Replace; 1: Multiply | 0 |
| `NormalBlend` | 0: Reoriented; 1: Whiteout | 0 |
| `HeightToDistortion`, `NormalToDistortion` | 0: Signed RG; 1: Packed RG | 0 |

An omitted `MixColor.option` is `0` (Perceptual), matching the editor's default.

The following are node-level enum fields, separate from `inputs` and `option`. Write enum
identifiers, not their spaced UI labels. Discovery's `settings` object supplies both spellings.

| Field | Applies to | Values in enum order | Default |
| --- | --- | --- | --- |
| `inputColorSpace` | ConvertColor | `LinearRGB`, `SRGB`, `HSV`, `HSL`, `Oklab` | `LinearRGB` |
| `outputColorSpace` | ConvertColor | same as inputColorSpace | `SRGB` |
| `channel` | GradientMap, HeightToNormal, HeightToDistortion | `Luminance`, `Red`, `Green`, `Blue`, `Alpha` | `Luminance` |
| `heightSource` | HeightToNormal, HeightToDistortion | `Texture`, `Value` | `Texture` |
| `derivative` | HeightToNormal, HeightToDistortion | `Scharr`, `Sobel`, `CentralDifference` | `Scharr` |
| `normalEncoding` | HeightToNormal, NormalBlend, NormalToDistortion, NormalStrength, NormalLighting | `PackedRGB`, `SignedXYZ` | `PackedRGB` |
| `wrap` | HeightToNormal, HeightToDistortion | `Clamp`, `Repeat`, `Mirror`, `Transparent` | `Clamp` |
| `filter` | HeightToNormal, HeightToDistortion | `Point`, `Bilinear` | `Bilinear` |

#### Color, alpha and numeric range

Connections do not convert color spaces. Color ports carry RGBA components without a space
tag; `ConvertColor` performs an explicit conversion, preserving alpha. Ordinary color
operations expect linear RGB. When working with encoded RGB, HSV, HSL or Oklab components,
convert them back to LinearRGB before those operations or Output.
ConvertColor's unconnected input uses a color picker for LinearRGB and raw Float4 numeric
components for every other input space.

ConvertColor uses sRGB-encoded RGB for HSV/HSL, with hue in turns (`1` is a full revolution),
not degrees. HSV conversion clamps negative encoded RGB to zero, but V retains values above 1;
conversion from HSV clamps S to `0..1` and V to nonnegative values. HSL is an SDR representation:
conversion to HSL clamps encoded RGB to `0..1`, and conversion from it clamps L and S to `0..1`.
HSL cannot roundtrip HDR or negative RGB, and HSV cannot retain negative RGB. LinearRGB, SRGB
and Oklab conversions retain signed/HDR values through signed transfer functions and signed
cube roots. Converting a space to itself is an exact bypass, including its out-of-range values.

Hue, Saturation and Lightness use sRGB-encoded calculations internally and return linear RGB.
Hue rotates by Degrees; Saturation clamps Amount to nonnegative values. Lightness clamps its
Amount to `-100..100`. These operations retain signed/HDR results without a final RGB `0..1`
clamp. Exposure multiplies linear RGB by `exp2(Stops)`. Color Balance applies the three Float3
adjustments according to tonal weights and optionally preserves luminance. Posterize uses
`max(2, floor(Levels))` steps including the 0 and 1 endpoints, without clipping RGB to that range.
Luminance computes `dot(rgb, (0.2126,0.7152,0.0722))` from linear RGB, without alpha or a `0..1` clamp.

Levels maps InputRange through Gamma into OutputRange. In Luminance mode it adjusts the
brightness of RGB together; RGB mode maps channels independently. Its explicit Clamp input
defaults to true and bounds values to InputRange. With Clamp false, signed-gamma extrapolation
allows negative values and values above the range. OutputRange is a remapping range, not a
general output clamp. Gamma is bounded below by `0.0001`; an equal or reversed InputRange
becomes a step at its first endpoint. Luminance mapping scales RGB by the new/old luminance
ratio; near zero luminance it adds the luminance difference instead.

Gradient Map samples the chosen channel through the connected Gradient, whose wrap settings
govern coordinates outside `0..1`. Preserve keeps source alpha; Multiply multiplies it by the
gradient alpha; Replace uses gradient alpha. Color Curves applies Master in RGB or Luminance
mode, then the individual channel curves. A connected curve uses
`x + curve(saturate(x)) - saturate(x)`: the input to the curve is limited to `0..1`, but values
outside that interval retain their excess. Identity curves therefore preserve HDR and negative
values. Disconnected curves are identity; alpha changes only when the Alpha curve is connected.

Color Mask outputs 1 for a matching KeyColor. Tolerance controls the selected region and
Softness its falloff. Chroma and RGB modes analyze sRGB-encoded components without SDR clipping;
Perceptual compares Oklab. Hue Range selects a cyclic degree interval: `(330,30)` crosses red,
an absolute span of at least 360 degrees selects the full cycle, and equal endpoints select
nothing unless Feather is positive. Feather is in degrees; SaturationRange fades selection
for low-saturation colors, even with a full hue interval. Feather clamps to `0..180` and cannot
extend past the opposite hue; SaturationRange endpoints clamp to `0..1`. Neither mask multiplies
by input alpha. ColorMask clamps Tolerance to nonnegative values; Softness at or below zero
selects a hard threshold.

Mix Color interpolates RGB in Linear RGB, sRGB or Perceptual space and interpolates alpha
linearly. T is unbounded, so values outside `0..1` extrapolate. This differs from Blend's
alpha compositing and Lerp's raw component interpolation. Set Alpha replaces or multiplies
alpha without changing RGB or clamping the result. Other color adjustments preserve source
alpha. Color ports do not add a general clamp; use an explicit Clamp and the layer's Rendering
color-range controls when a limited result is required.

#### Height, normals and displacement

Height to Normal and Height to Distortion use node-level `heightSource`, default `Texture`.
In Texture mode, connect a **Texture** resource to Height. Channel selects its height component;
Radius measures bound-source texels, independent of graph-preview size or document Pixel Scale.
Wrap/Filter control those texture reads. In Value mode, Height is **Float**: connect a noise,
mask or other scalar branch. The compiler reevaluates that branch at neighboring UVs;
Radius measures document pixels, independent of node-preview resolution.
Texture Channel/Wrap/Filter do not apply to Value; upstream sampling nodes keep their own settings.
UV defaults to the current pixel in both modes. Strength, FlipX/FlipY and the Scharr, Sobel or
Central Difference derivative remain available. The finite difference is divided by the
sampling distance, so Radius changes the neighborhood without directly multiplying the slope.
An upstream branch with fixed coordinates remains constant when reevaluated at another UV.

Height to Normal returns a normalized Float3 normal. Normal Blend combines Base and Detail
using Reoriented or Whiteout, with Strength controlling Detail, and normalizes its result.
Output Encoding on Height to Normal and Normal Encoding on Normal Blend select packed RGB
(`n * 0.5 + 0.5`) or signed XYZ. Flat normals are
`(0.5,0.5,1)` and `(0,0,1)` respectively. These are raw vector encodings, with no implicit
sRGB conversion. In the editor, changing Normal Encoding converts literal normal inputs;
wired inputs must already use the selected encoding.

Height to Distortion keeps the finite-difference gradient's magnitude. Normal to Distortion
decodes Normal, takes XY without normalization and scales its components by Strength. FlipX
and FlipY reverse the corresponding displacement components. Both return **displacement**,
not UV: connect Out to `VectorDisplacement.Displacement`, match its Signed RG/Packed RG mode,
then connect `VectorDisplacement.Out` to a sampling node's UV. Signed neutral is `(0,0)`;
packed neutral is `(0.5,0.5)`. Packing does not clamp strong or negative displacements to `0..1`.
For a slope `(dx,dy)`, Height to Normal builds `normalize(-dx,-dy,1)`, while Height to Distortion
uses `(dx,dy)`. Routing a generated normal through Normal to Distortion therefore changes both
the sign and magnitude compared with direct height displacement. Reverse Strength or FlipX/Y
when the desired distortion direction requires it.

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

The bundled **Color/Adjustments/Contrast** node is
[`src/ShaderGraphFunctions.hlsl`](https://github.com/DCFApixels/WhimTex/blob/main/src/ShaderGraphFunctions.hlsl),
at `Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl` in the project. It changes
contrast around 0.5, retains alpha and leaves HDR values unclamped. Its declaration follows
this pattern (the library also declares its own preset identity):

```hlsl
// @whimtex-node Color/Adjustments/Contrast
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

JSON, clipboard and Unity Undo retain plain source strings. Native TIFF encodes source `code`
and function `body`/`draft` strings using tagged value 33 and container blocks named
`shader-source:sha256:<text digest>`; null drafts remain null.
Compression uses the existing container codec; identical text is stored once across every
graph in the TIFF, even when the modules have different include bindings. Bindings and source
IDs stay in the document model. Reads check the block size and SHA-256 before reconstructing
ordinary strings, with a 32 MiB limit on the unique shared text table. Older readers fail on
the unknown tag instead of interpreting the block reference as source code.
Compilation and node-preview fingerprints include module text and include bindings, even
when source IDs remain unchanged. Text hashes are cached until the string changes.

### Create from a connection

Dropping a new wire from an input or output on empty graph space opens the existing search window,
filtered with the same numeric/color conversions and resource-type rules as ordinary wiring.
The menu opens to the right of the cursor with its left edge anchored there; native screen-edge fitting still applies.
Dragging from an output lists compatible inputs as `NodeName(InputName)`; dragging from an input
lists compatible outputs as `NodeName(OutputName)`, including declared parameters and HLSL functions.
Each compatible port is a separate choice. Input searches respect downstream Reroute constraints
and exclude disabled outputs. Creation and connection
share one document Undo operation; cancellation changes no model data. The node uses the drop
position in graph coordinates. Selection revalidates the source and current type, graph identity,
editability and capacity limits. Choosing a source for an occupied input replaces its old connection
in the same Undo operation; cancelling keeps it. Parameter choices create a node using the existing
parameter, without duplicating its declaration. Output and Reroute are excluded from this creation
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
preserve existing IDs and include every retained node, parameter, edge, Sub Graph definition,
function, source and annotation.
Missing texture `$asset` identities supplied by this operation survive native save/reopen;
an explicit null replacement clears the old identity for that parameter.
Inspection returns the graph at `settings.shader.graph`. Discovery supplies `shaderDefaults`,
`shaderNodes`, `shaderValueTypes`, `shaderWrapModes` and `shaderFilterModes`.
Each node catalog entry includes `category`, exact `inputs` and the existing string-array
`outputs`. Nodes with fixed procedural, normal, color, vector or Boolean outputs additionally
expose `outputTypes`, an output-name-to-type map; dynamic numeric types remain inferred from inputs.
Nodes with shared mode metadata provide `modeLabel`, `defaultOption` and `modes` entries
containing `{ option, name }`. Shape marks `dynamicInputs:true`; every Shape mode also contains
its own `inputs` array, while the entry's top-level inputs describe the default mode.
Noise and DomainWarp provide `fractalModes` (serialized enum names) and `defaultFractal`.
Color/normal nodes also expose applicable node-level `settings`: each field maps to
`{ default, values: [{ value, name }] }`, where `value` is the serialized enum identifier and
`name` is its UI label. For NormalBlend, NormalToDistortion, NormalStrength and NormalLighting, each normalEncoding value also
includes `inputs` with that encoding's neutral defaults; top-level inputs use the default encoding.
Height nodes mark `dynamicInputs:true`; `settings.heightSource.values[]` supplies the input layout
and `radiusUnits` (SourceTexels or OutputPixels) for each source mode. Sampled filters expose
`sampledInputs:["In"]`, `outputTypeFromInput:"In"` and `radiusUnits:"OutputPixels"`.
ColorCurves resource inputs carry `optional:true` and evaluate as identity when disconnected.
Names, categories, mode labels, port sets, defaults and these fixed output types come from the shared
node model; descriptions explain units and evaluation. These discovery fields are not extra
fields to copy into a serialized node.

In stored `whimtex.document` JSON, place this same graph at `layers[].behaviour.graph`, with
`behaviour.$type` set to `ShaderLayerBehaviour`; the API's `op`, `as` and `settings` wrapper is
not stored. Keep `graph.version:1`. Parameter `controls` are saved declarations, not regenerated
HLSL metadata. Texture parameters use the existing `textureSource`, `textureLayerId` and
`textureValue` asset-reference fields. Native TIFF can embed non-asset textures; JSON requires
persisted asset references. The [generated schema](AI/document.schema.json) describes those fields.
