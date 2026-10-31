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

## Parameters and ports

Parameter declarations define the fields shown in the layer's Properties. A Parameter node reads
a declaration, and multiple nodes can read the same declaration. Constant holds a graph-local
value. Texture, Gradient and Curve parameters use the shared parameter controls and bindings.

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
Color Parameter outputs have fixed Color types. Sample Curve remains numeric. Constant's type
menu includes Color, with a color picker for its value.

Dynamic math normalizes Color to `float4` before choosing the widest numeric operand width.
Scalars splat to that width; shorter vectors are zero-extended. The Color destination's
alpha-one defaults do not apply in math. For example, Add of Color `(r, g, b, a)` and `float(x)`
uses `(x, x, x, x)`, and Add with `float2(x, y)` uses `(x, y, 0, 0)`. Dynamic numeric results use
numeric types, not Color; operations with fixed scalar or Boolean results retain those results.

Constant and unconnected Color values store linear RGBA; their picker displays encoded RGB
and converts edits back to linear RGB without changing alpha. Color parameters retain their
existing encoded storage and color preparation into linear working values.
Connecting Color and numeric ports does not insert a color-space conversion, and numeric
vectors do not receive color preparation merely because they have four components. Use Split
and Combine for explicit component selection and assembly. Resources require their Sample
operation before numeric use; Boolean conditions remain distinct from numeric and resource ports.

Unconnected numeric input fields sit outside the node body, to the left of their ports, leaving
the port dots unobstructed for wiring. These fields already existed but were hidden by `Node`'s
built-in inline clipping; they are now visible and editable. Connecting an input hides its field; disconnecting it
restores the field. Unconnected UV ports retain implicit `PixelUV` coordinates. Their local
**Custom / UV** option selects an explicit numeric value or the implicit current-pixel UV;
Custom fields follow the same external placement and connection visibility rules. These are
editor controls over the existing input semantics, not changes to graph math or storage.

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
absent from **Add Node** and node search.

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
require compilation. A Constant is edited inside the graph rather than exposed as a Properties
parameter; its numeric value is also bound separately from the generated code.
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

## Node previews

Calculation nodes other than Output own collapsible previews inside their nodes. Output,
Parameter and Reroute have no per-node preview.
There is no shared Selected Node preview in the sidebar, and selecting a node does not retarget
another node's preview. An output selector appears only when the node has multiple outputs.
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
| Parameter, Constant | Shared parameter reads and graph-local values. |
| Reroute | Forward a typed connection unchanged; insert on an existing edge, not through Add Node or search. |
| UV, Image Size, Texture Size | Coordinates and image dimensions. |
| Sample Texture, Sample Gradient, Sample Curve | Read typed resources. |
| Split, Combine | Component access and vector construction. |
| Add, Subtract, Multiply, Divide | Basic arithmetic. |
| Min, Max, Clamp, Abs, One Minus, Negate | Bounds, absolute value, `1 − x` and sign inversion. |
| Power, Square Root | Exponentiation and square root. |
| Floor, Ceil, Round, Fraction | Integer boundaries and fractional part. |
| Sine, Cosine | Periodic functions. |
| Lerp, Remap, Step, Smoothstep | Interpolation, range mapping and thresholds. |
| Length, Distance, Dot, Normalize | Vector calculations. |
| Compare, Select | Conditions and conditional values. |
| Transform UV | Translation, scale and rotation of coordinates. |
| Noise | Procedural sampling using the shared noise library. |
| Shape | Ellipse, rectangle and rounded-rectangle masks with antialiased edges. |
| Blend | RGBA blending using the existing blend semantics. |
| Output | Final straight RGBA through a fixed Color input; modest visual distinction and no per-node preview. |

Custom HLSL nodes, reusable subgraphs, Blur and Time nodes are outside this first version.
HLSL FX authoring continues to use the [separate effect contract](ShaderFX.md).

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
`shaderNodes` (exact port names), `shaderValueTypes`, `shaderWrapModes` and `shaderFilterModes`.

In stored `whimtex.document` JSON, place this same graph at `layers[].behaviour.graph`, with
`behaviour.$type` set to `ShaderLayerBehaviour`; the API's `op`, `as` and `settings` wrapper is
not stored. Keep `graph.version:1`. Parameter `controls` are saved declarations, not regenerated
HLSL metadata. Texture parameters use the existing `textureSource`, `textureLayerId` and
`textureValue` asset-reference fields. Native TIFF can embed non-asset textures; JSON requires
persisted asset references. The [generated schema](AI/document.schema.json) describes those fields.
