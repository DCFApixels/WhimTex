---
title: "Shader layer"
parent: "Layers and groups"
grand_parent: "English"
nav_order: 3
lang: "en"
description: "Build procedural textures and masks with a Shader layer in WhimTex. Connect nodes, expose parameters and combine the result with ordinary layers and FX."
permalink: "/en/shader-layer/"
translations: "en/shader-layer.md,ru/shader-layer.md,zh/shader-layer.md"
---

# Shader layer

Use **Shader** to build a texture or mask by connecting operations in a node graph.
The graph produces the layer's own image. **Shader Processor** instead processes the combined
image below it; **Shader FX** processes the image of the layer it belongs to. See [Shader FX and Processor](shader-fx.md).

## Create and adjust an image

1. Add a **Shader** layer from **+** at the bottom of Layers.
2. Open **Properties** and choose **Edit Graph**.
3. Connect the calculation to **Output**, which supplies the image's red, green, blue and alpha channels.
4. Declare parameters for values you want to adjust from the layer's **Properties**.
   Use **Parameter** nodes to read them in the graph; **Constant** keeps a value inside the graph.
5. Return to the layer's **Properties** to adjust the result without rebuilding the graph.

A new graph starts with a Color parameter connected to Output. To try a fixed value instead,
choose **Color** in a **Constant** node's type menu, pick opaque red with its color picker,
and connect it to **Output**. A four-component Constant set to `(1, 0, 0, 1)` gives the same color.
Use a **Color** parameter when you want to choose the color from Properties.

The result uses the usual layer **Transform**, **Rendering**, **FX** and blending controls.
To use another image in the calculation, declare a **Texture** parameter and read it with
**Sample Texture**. Placing a Shader layer above an image does not automatically make that image its input.

## Choose nodes

| Nodes | Use them for |
| --- | --- |
| **Parameter**, **Constant** | Adjustable layer values or values kept inside the graph. |
| **UV**, **Image Size**, **Texture Size** | Position within the image, output dimensions or an input texture's dimensions. |
| **Sample Texture** | Read an image at chosen UV coordinates. Change the coordinates to distort it. |
| **Sample Gradient**, **Sample Curve** | Turn a number into a gradient color or reshape it with a curve. |
| **Split**, **Combine** | Separate vector/color channels or assemble them again. |
| **Add**, **Subtract**, **Multiply**, **Divide** | Offset, scale and combine values. |
| **Min**, **Max**, **Clamp**, **Abs**, **One Minus**, **Negate** | Limit values, take their magnitude or reverse them. One Minus calculates `1 − x`. |
| **Power**, **Square Root**, **Floor**, **Ceil**, **Round**, **Fraction** | Shape transitions, create steps or repeat a numeric interval. |
| **Sine**, **Cosine** | Create waves and repeating patterns. |
| **Lerp**, **Remap**, **Step**, **Smoothstep** | Mix values, change ranges and create hard or soft thresholds. |
| **Length**, **Distance**, **Dot**, **Normalize** | Calculate distances and directions. |
| **Compare**, **Select** | Choose between two values using a condition. |
| **Transform UV** | Move, scale or rotate the coordinates used by downstream nodes. |
| **Noise** | Generate a procedural pattern from coordinates and a seed. |
| **Shape** | Create ellipse, rectangle or rounded-rectangle masks with smooth edges. |
| **Blend** | Composite two colors with a blend mode and alpha. Lerp is ordinary numeric interpolation. |
| **Output** | Produce the final RGBA image. |

For colored noise, connect **UV → Noise → Sample Gradient → Output** and supply a
**Gradient** parameter to Sample Gradient. Adjust the palette in Properties.
For a threshold mask, place **Smoothstep** after Noise; use its result as alpha with **Combine**.

Unconnected numeric inputs have visible, editable fields outside the node body, to the
left of their ports. The ports stay unobstructed for wiring. Connecting a wire hides the field;
disconnecting it brings the field back. Unconnected UV ports keep implicit current-pixel
coordinates (`PixelUV`): use the local **Custom / UV** option to enter coordinates or use UV.

Drag a wire from an output port onto empty graph space to open the compatible-node search.
The menu opens to the right of the cursor with its left edge aligned to it.
Each entry names the node and input, for example `Multiply(A)` and `Multiply(B)`. Selecting
an entry creates the node at the drop position and connects that input in one Undo operation.
Cancel the search to leave the graph unchanged.

## Move and delete nodes

Press **Del** to delete selected nodes, including **Reroute**. **Output** is required and
cannot be deleted. While editing a text or numeric field, **Del** edits the value without
deleting the node.

While dragging, connected input/output port centers gently snap to the same height.
This connector snap leaves X unchanged and yields to normal Y snapping between node rectangles.
Hold **Shift** to disable snapping. Nodes moved together keep their relative positions.

## Arrange connections with Reroute

Double-click an existing connection to insert a **Reroute**, or choose **Add Reroute** from
that connection's context menu. Reroute is created only on an existing connection; it does not
appear in **Add Node** or node search.

The mini-node has no header, preview or parameter controls. Its **In → Out** connection passes
the source type and value unchanged: numbers, vectors, Color, Boolean, Texture, Gradient and Curve.
It adds no calculations and does not change the image. Drag the mini-node to arrange the wires,
and connect **Out** to several inputs to branch the same value.

Deleting a Reroute reconnects its incoming source directly to its downstream inputs, including
when several Reroutes form a chain. Deleting an ordinary connection breaks that connection.
Reroute positions and connections are saved with the document; insertion, movement, connection
changes and deletion support **Undo/Redo**.

## Preview a node

Calculation nodes other than **Output** have their own collapsible **128 × 128** previews inside
the node. **Output**, **Parameter** and **Reroute** have no per-node preview. The sidebar has no
shared selected-node preview. If a node has several outputs, use its preview's
output selector to choose one, for example a Split channel or Image Size's Size/Texel.
Previews refresh when their result changes, only while expanded and visible in the graph
viewport. Collapsed or off-screen previews wait until they are expanded and visible again.

Numbers appear as grayscale; two-component vectors use red and green, three-component vectors
use RGB, and four-component vectors and Color include alpha. Texture, Gradient and Curve resources cannot
be shown directly: connect the corresponding Sample node and expand its preview.

**Output** has a subtle visual accent to make the graph's destination easy to find. Use
**Layer Preview** for the layer result. Node previews show values before the layer's Transform
and FX. A node-preview error clears its image and shows a message,
so it does not display an old image as the result of the selected calculation.

## Parameters and color

Parameters include numbers, vectors, Boolean values, colors, textures, gradients and curves.
Multiple Parameter nodes can use the same declared parameter, so one Properties field can control
several parts of the image.

A **Parameter** node is a compact single row with the parameter name and an output connector, without
an **Out** label, an internal preview or a dropdown. To choose another declared parameter, use
the node's context menu; adjust the parameter's value in the layer's **Properties**.

Numeric and **Color** ports are circles, **Boolean** ports are diamonds, and **Texture**,
**Gradient** and **Curve** ports are squares.

You can connect numeric vectors with different component counts. A `float4` connected to a
`float2` input keeps X and Y and drops Z and W. Shorter vectors gain zero components, so
`float2(x, y)` becomes `float4(x, y, 0, 0)`; a vector connected to a scalar input keeps X.
A scalar repeats across every component of a numeric vector: `x` becomes `(x, x, x, x)` for `float4`.

**Color** is a separate port type carrying the same RGBA components as `float4`. Output's RGBA
input, Blend's color inputs and result, Sample Texture and Sample Gradient color outputs, and
Color Parameter outputs use fixed Color ports. Sample Curve produces a number. Constant's type
menu also includes Color and shows a color picker.

Connecting a number or vector to a Color input gives these results:

| Source | Color result |
| --- | --- |
| `float(x)` | `(x, x, x, 1)` — opaque gray. |
| `float2(x, y)` | `(x, y, 0, 1)`. |
| `float3(x, y, z)` | `(x, y, z, 1)`. |
| `float4(x, y, z, w)` | `(x, y, z, w)` — alpha is preserved. |

Color connected to a numeric input follows the ordinary `float4` conversion rules.
Dynamic math such as Add, Multiply and Lerp treats Color as `float4` and uses the widest
numeric input width. Scalars repeat across that width and shorter vectors gain zeros; math
does not add alpha 1. For example, multiplying Color by `0.5` halves all four channels,
including alpha. Dynamic numeric results are numeric vectors or scalars, not Color.

Color picker values use the same color preparation as other WhimTex color fields; connecting
a wire does not perform an extra color-space conversion.

The graph retains HDR and negative numeric values. Use Clamp when you want an explicit limit,
and check the layer's **Rendering** color-range settings when you need HDR output.
Alpha controls transparency; do not multiply the RGB channels by alpha before Output.

## Inspect Generated HLSL

Drag the **Generated HLSL** footer upward to read the graph's generated code. Its caption and
horizontal grip lines work like the Layer Preview handle. The panel starts collapsed and opens
after more than 14 logical pixels of upward dragging, regardless of graph zoom. Clicking alone
does nothing. Continue dragging to adjust its height; drag downward to hide the code. The panel
fits the available window space while leaving room for the graph, and the window remembers
its open/closed state and height.

The panel uses the same code field as FX Code in read-only mode: you can select and copy text,
with the same syntax highlighting, line numbers and horizontal/vertical scrolling supported
by your Editor version. Reopening shows the current generated code and keeps your selection
if the source is unchanged. Change the graph to change the generated code.

## Scope

The graph belongs to the Shader layer and stays editable with the document. Parameter changes
adjust the image; changing operations or connections changes the calculation itself.
Connections must lead toward Output without a cycle.

You can save an unfinished graph, including a missing resource connection or a Parameter node
that has not been assigned a parameter. If it cannot compile, the editor reports the errors and
keeps the last working result when available. That image does not represent the unfinished
changes. The document saves the editable graph, so you can complete it later.

This first version does not include custom HLSL nodes, subgraphs, Blur or Time nodes.
Use the layer's normal FX stack for additional processing. For port types and execution rules,
see the [Shader layer contract](../ShaderLayer.md).
