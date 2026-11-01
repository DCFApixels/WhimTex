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
   Add nodes through **Create Node** in the graph context menu or press **Space**.
3. Connect the calculation to **Output**, which supplies the image's red, green, blue and alpha channels.
4. Declare parameters for values you want to adjust from the layer's **Properties**.
   Use **Parameter** nodes to read them in the graph; **Constant** supplies mathematical constants.
5. Return to the layer's **Properties** to adjust the result without rebuilding the graph.

A new graph starts with a Color parameter connected to Output. Choose opaque red in Properties
to try it. For a value kept inside the graph, disconnect an input and edit its value beside the port.

The result uses the usual layer **Transform**, **Rendering**, **FX** and blending controls.
To use another image in the calculation, declare a **Texture** parameter and read it with
**Sample Texture**. Placing a Shader layer above an image does not automatically make that image its input.

## Mathematical constants and Swizzle

**Input/Instance Index** and **Input/Time** each provide a Float output named **Out**.
Neither has its own preview. **Instance Index** returns `0`.
**Time** supplies Editor time in seconds to dependent node previews and the result in the graph window.
Its Float input **Scale** defaults to `1`: **Out = time × Scale**. Set it manually or connect another node.
The layer itself, Canvas, thumbnails and export always use **Time = 0**.
HLSL nodes and Custom Function can read `_WhimTexTime` and `_WhimTexInstanceIndex` directly.
FX on the Shader layer share its animated time inside the graph's result preview.

**Constant** outputs a number selected from **PI**, **TAU** (2π), **PHI** (golden ratio),
**E** and **SQRT2** (√2). PI is selected initially.

**Swizzle** rearranges or repeats input channels. Enter 1–4 characters in **Mask** using
`xyzw` or `rgba`; uppercase letters also work. `_` inserts zero.
For a two-component input, `_x_y` produces `(0, x, 0, y)` as a four-component vector.
The mask length sets the output width. Commit with Enter or by leaving the field.
The input uses the larger width required by its source and mask: `y` needs Float2, `z` Float3,
and `w` Float4. Unconnected inputs show that many editable components. Connected scalars repeat
across components; shorter vectors gain zeros. `_` always inserts zero.

## More math and pattern controls

**Sign** separates negative, zero and positive values; **Saturate** limits a value to `0..1`.
Use **Modulo** for repeated numeric intervals, **Atan2** for an angle from Y and X, and
**Asin**/**Acos** for inverse trigonometry. These angles are radians. Asin/Acos need values
within `-1..1`; **Log** needs positive values. **Exp** and Log use the natural base.
**Cross** returns a 3D perpendicular vector. **Reflect** and **Refract** change an incident
direction around a normal; supply unit vectors and use Eta for the refraction ratio.
Combine Boolean conditions with **And**, **Or** and **Not** before Select.

For repeating noise, use **Tileable Noise**: Period sets the repeat length in UV, Cells sets
the detail count within that period, and Offset shifts the pattern. **Voronoi Edge** produces
distance to cell boundaries; pass it through a threshold to draw cell-border lines.
**Flipbook** selects a cell of an atlas with Columns, Rows and Frame. Connect its Out to
Sample Texture.UV. Frames wrap, including negative values; choose bottom-to-top or top-to-bottom
row order without flipping the image inside each frame.

**Dither** reduces color levels with a Bayer 2 × 2, 4 × 4 or 8 × 8 pattern. Levels sets the
number of levels, and PixelSize sets the pattern scale in output pixels. Alpha is preserved;
HDR and negative colors are not clipped automatically.

## Notes and groups

Right-click the graph and choose **Add Note** for a resizable text note, or **Group Selection**
to frame the selected nodes and notes. With no selection, Group Selection creates an empty group.
These are separate context-menu actions, outside **Create Node**. Rename a group in its header,
drag elements into it, and move its contents together by dragging the header.
**Ungroup Selection** releases selected elements; deleting a group keeps its nodes and notes.
Groups cannot contain other groups.

The note’s context-menu **Hue…** opens a rainbow slider. It rotates the native background,
outline and text colors while preserving their saturation, brightness and non-color styling.
The hue shift stays the same when changing note themes. **Reset** restores the standard colors.
Editing the title or body keeps its displayed font size and text area.

Notes and groups stay with the document and support **Undo/Redo**. Copying a group includes its
contents and internal connections. They organize the graph without changing the image.

Information, warnings and errors appear as icons to the right of the affected node. Hover the
icon to read its messages in a panel to the right; the icon indicates the most severe message.
Errors that concern the whole graph remain in the graph's message area.

## Copy nodes

Select nodes and use **Ctrl+C / Ctrl+V** to copy and paste, or **Ctrl+D** to duplicate
(**Cmd** on macOS). The same commands are available in the graph's context menu.
Settings, relative positions and connections between selected nodes are retained, including
Reroutes and HLSL functions. Sub Graph copies carry their nested definitions; copies in the same
graph reuse the existing definitions, while another graph receives its own copies.
Output and Sub Graph interface nodes are not copied. Paste and Duplicate support **Undo/Redo**.
Copies appear at the center of the current graph view. Centering uses the combined rectangle
of nodes, notes and group frames, including their sizes. Repeated paste stays centered.

In the same graph, copied Parameter nodes use the existing parameters. In another graph,
the required parameters are copied with their values. Layer texture inputs must be reassigned
when pasting into another document. Copying while editing a field copies its text.
Copy/paste shortcuts remain available after script recompilation, including when starting a new copy.
The node clipboard also survives recompilation and remains valid until the Editor closes.

## Choose nodes

| Nodes | Use them for |
| --- | --- |
| **Parameter**, **Constant** | Adjustable layer values or mathematical constants: PI, TAU, PHI, E, SQRT2. |
| **UV**, **Image Size**, **Texture Size** | Position within the image, output dimensions or an input texture's dimensions. |
| **Sample Texture** | Read an image at chosen UV coordinates. Change the coordinates to distort it. |
| **Sample Gradient**, **Sample Curve** | Turn a number into a gradient color or reshape it with a curve. |
| **Split**, **Combine**, **Swizzle** | Separate, assemble or reorder vector/color channels. |
| **Add**, **Subtract**, **Multiply**, **Divide** | Offset, scale and combine values. |
| **Min**, **Max**, **Clamp**, **Abs**, **One Minus**, **Negate** | Limit values, take their magnitude or reverse them. One Minus calculates `1 − x`. |
| **Power**, **Square Root**, **Floor**, **Ceil**, **Round**, **Fraction** | Shape transitions, create steps or repeat a numeric interval. |
| **Sine**, **Cosine** | Create waves and repeating patterns. |
| **Lerp**, **Remap**, **Step**, **Smoothstep** | Mix values, change ranges and create hard or soft thresholds. |
| **Length**, **Distance**, **Dot**, **Normalize** | Calculate distances and directions. |
| **Compare**, **Select** | Choose between two values using a condition. |
| **Transform UV** | Move, scale or rotate the coordinates used by downstream nodes. |
| **Noise**, **Cellular**, **Random** | Generate noise, cell distances or repeatable random values from coordinates and a seed. |
| **Checkerboard**, **Stripes**, **Grid**, **Rings**, **Gradient Factor** | Create smooth pattern masks or a position for sampling a gradient. |
| **Shape**, **Combine Distance** | Create shape masks and combine their signed distances. |
| **Polar Coordinates**, **Repeat**, **Mirror**, **Kaleidoscope** | Remap or fold coordinates to repeat and reflect motifs. |
| **Domain Warp**, **Vector Displacement**, **Twirl**, **Bulge / Pinch**, **Wave**, **Ripple** | Distort coordinates before generating a pattern or sampling an image. |
| **Hue**, **Saturation**, **Exposure**, **Lightness**, **Contrast**, **Levels**, **Color Balance**, **Posterize** | Adjust color, brightness and tonal steps. |
| **Convert Color**, **Luminance**, **Gradient Map**, **Color Curves** | Convert color components or remap an image's tones. |
| **Color Mask**, **Hue Range**, **Mix Color**, **Set Alpha** | Select colors, mix them and control transparency. |
| **Height to Normal**, **Normal Blend**, **Height to Distortion**, **Normal to Distortion** | Turn a texture or height calculation into normals or displacement and combine normal detail. |
| **Blend** | Composite two colors with a blend mode and alpha. Mix Color interpolates colors; Lerp interpolates numeric components. |
| **Output** | Produce the final RGBA image. |

For colored noise, connect **UV → Noise → Sample Gradient → Output** and supply a
**Gradient** parameter to Sample Gradient. Adjust the palette in Properties.
For a threshold mask, place **Smoothstep** after Noise; use its result as alpha with **Combine**.

Unconnected numeric inputs have visible, editable fields outside the node body, to the
left of their ports. The ports stay unobstructed for wiring. Connecting a wire hides the field;
disconnecting it brings the field back. Unconnected UV ports keep implicit current-pixel
coordinates (`PixelUV`): use the local **Custom / UV** option to enter coordinates or use UV.
Single Float fields also have an **X** label. Drag it horizontally to adjust the value, as with vector components.
Inputs that do not apply to the selected mode remain visible but are disabled, along with their fields.
For example, **Fractal = None** disables Octaves, Lacunarity and Gain; PingPongStrength is active
only for Ping Pong. Hover a port to see why it is inactive. Inactive ports, including unavailable
Split components, cannot accept new wires. Existing values and wires remain when the port stays
present, and become editable again when its mode applies.

Drag a wire from an input or output port onto empty graph space to open the compatible-node search.
The menu opens to the right of the cursor with its left edge aligned to it.
Dragging from an output lists compatible inputs, such as `Multiply(A)` and `Multiply(B)`.
Dragging from an input lists compatible outputs, such as `Cellular(F1)` and `Cellular(F2)`,
including matching parameters and HLSL functions. Selecting creates and connects the node at the
drop position in one Undo operation. An occupied input receives the new connection instead of
the old one. Cancel the search to leave the graph unchanged.

## Build procedural patterns and distortions

Find these nodes in the following nested **Create Node** categories. The same branches appear
when creating a node from a dragged connection:

| Category | Nodes |
| --- | --- |
| **Procedural/Noise** | Noise, Cellular, Random. |
| **Procedural/Pattern** | Checkerboard, Stripes, Grid, Rings, Gradient Factor. |
| **Procedural/Shape** | Shape, Combine Distance. |
| **UV** | Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope. |
| **UV/Distortion** | Domain Warp, Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple. |

**Noise** offers **None**, **FBm**, **Ridged** and **Ping Pong** fractals. Octaves adds detail,
Lacunarity changes the frequency between octaves, and Gain controls their relative strength.
**Domain Warp** offers **None**, **Progressive** and **Independent** fractals; None uses one octave.
**Cellular** provides nonnegative **F1** and **F2** distances using the selected Metric,
and a repeatable **Cell** value in `0..1`. Cell is not a unique identifier; Cellular has no Out port.
**Random** uses the current pixel's **UV** for unconnected Coordinates by default. Choose **Custom**
for constant coordinates, or connect **Repeat.Cell** for one random value per repeated cell.
Previously saved constant coordinates remain Custom; choose UV to switch them.

**Shape** offers Ellipse, Rectangle, Rounded Rectangle, Polygon, Star, Capsule and Line Segment.
Its **Out** is a smooth mask; **Distance** is negative inside and positive outside the shape.
Connect two Distance outputs to **Combine Distance**, then use its **Mask** as coverage.
Choose Union, Intersection or Subtract (A minus B); Smoothness blends the join, while Feather
softens the mask edge. Capsule uses Center, Size and Rotation; Line Segment uses Start, End
and Width. Switching Shape mode replaces its controls and removes connections to absent inputs.
The ellipse distance is approximate, so distance-based borders can vary in width.

For a colored gradient, connect **Gradient Factor.Out → Sample Gradient.T** and a **Gradient**
parameter to **Sample Gradient.Gradient**; connect the sampled color to **Output**.
Checkerboard, Stripes, Grid, Rings and shape masks have smoothed edges.
**Stripes**, **Rings**, **Wave** and **Ripple** have a separate **Phase** input to shift the
pattern in cycles. Drive it with a parameter, or connect **Time** to animate it in the graph preview.
Stripes offers Square, Triangle and Sine profiles; Width affects Square only.
Angle, Rotation and Direction use degrees; Twirl's Strength is also an angle in degrees.

Connect a UV node's **Out** to the next pattern or **Sample Texture.UV**.
**Repeat.Cell** contains cell coordinates, including negative coordinates; **Input/Instance Index**
remains zero and is unrelated to it. **Domain Warp.Displacement** exposes the offset
as well as the warped UV. Distortions leave coordinates unchanged at **Strength = 0**;
displacement vectors keep their length. UV can extend beyond `0..1`, so choose the desired
wrap setting on Sample Texture. Transform UV changes the coordinates within the graph,
independently of the layer's Transform.

**Vector Displacement** accepts **Signed RG** vectors directly or decodes **Packed RG** from
`0..1`, where `(0.5,0.5)` is neutral. Strength scales X and Y separately.
Positive **Bulge / Pinch** Strength magnifies the sampled image; negative values pinch it.
**Polar Coordinates** maps to angle in turns and twice the distance from Center, or back to UV.
**Mirror** folds selected axes onto the positive side of Center; **Kaleidoscope** repeats radial sectors.
See the [procedural node contract](../ShaderLayer.md#procedural-and-uv-nodes) for exact ports and modes.

## Adjust color and transparency

To recolor a texture, connect **Sample Texture → Hue → Saturation → Output**. Find the
adjustments under **Color/Adjustments** in Create Node. Start with **Degrees = 0** and
**Amount = 1**, then change the hue angle or reduce saturation toward zero for grayscale.
These adjustments preserve the image's transparency.

| Controls | Visible effect |
| --- | --- |
| **Exposure / Stops** | Brightens or darkens linear RGB; +1 doubles it, −1 halves it. |
| **Lightness / Amount** | Adjusts lightness from −100 to 100; 0 leaves it unchanged. |
| **Contrast / Amount** | Changes contrast around 0.5; 1 leaves it unchanged. |
| **Levels / Input Range, Gamma, Output Range** | Sets black/white points and midtones. Luminance adjusts brightness together; RGB adjusts channels separately. Disable Clamp to keep values beyond Input Range. |
| **Color Balance / Shadows, Midtones, Highlights** | Tints different tonal regions. Range controls adjust their reach; Preserve Luminance retains brightness. |
| **Posterize / Levels** | Reduces smooth shading to steps; at least two levels are used. |

For a palette-driven result, add **Color/Mapping/Gradient Map**. Connect the image to Color
and a **Gradient** parameter to Gradient, then choose Channel. Luminance maps dark-to-bright
tones across the gradient. The default Preserve mode keeps the image's alpha; Multiply combines
it with gradient alpha, and Replace uses gradient alpha alone.

Use **Color Curves** to reshape tones with Curve parameters. Master affects RGB together or
luminance, then Red, Green and Blue refine individual channels. Leave a curve disconnected for
no change. Alpha changes only if its own curve is connected. Identity curves retain values above
white and below black.

To select a color, add **Color/Selection/Color Mask**, choose Key Color and adjust Tolerance
and Softness. Chroma compares color difference, RGB includes brightness differences, and
Perceptual measures Oklab difference. **Hue Range** selects a hue interval in degrees; `(330,30)`
crosses red. Feather softens its edges and Saturation Range excludes or fades near-gray colors.
Both nodes output a mask with white for selected colors. They do not apply the image's alpha.
Connect the mask to **Color/Alpha/Set Alpha.Alpha**: Replace uses it directly; Multiply also
keeps the source transparency. Set Alpha leaves RGB unchanged.

**Color/Mixing/Mix Color** blends A and B by T, with Perceptual selected initially. Linear RGB
and sRGB give different intermediate colors. T = 0 gives A and T = 1 gives B; values outside
that range extrapolate. Alpha is interpolated too. Use **Blend** for alpha compositing.

Color connections perform no automatic color-space conversion. **Color/Conversion/Convert Color**
explicitly converts Linear RGB, sRGB, HSV, HSL or Oklab; convert back to Linear RGB before ordinary
color processing or Output. HSV and HSL use sRGB-encoded RGB with hue measured in turns.
An unconnected Convert Color input shows a color picker for Linear RGB and raw Float4 fields for other spaces.
HSV loses negative RGB but keeps HDR brightness; HSL limits values to `0..1` and loses both
HDR and negative RGB. Linear RGB/sRGB/Oklab retain them. **Luminance** extracts a brightness value from linear RGB.
Color adjustments generally retain HDR and negative values. Levels has its explicit Clamp switch;
Set Alpha does not clamp transparency. Use Clamp where needed and choose the desired color range
in the layer's Rendering settings.

## Build normals and displacement

For a normal map, add **Normal/Height to Normal**. Keep **Source: Texture** to connect
a Texture parameter directly to Height and choose its Channel. Radius measures source texture
texels in this mode; Wrap and Filter control sampling at edges.
Choose **Source: Value** to connect a noise, mask or other scalar height calculation.
The node reads that calculation at neighboring coordinates, with Radius measured in output pixels.
This also works on **Height to Distortion**. Adjust Strength for relief and Radius for the
sampling distance. Both sources support Scharr, Sobel and Central Difference derivatives;
Flip X and Flip Y reverse the slope direction. A branch with fixed coordinates has no varying height.

**Packed RGB** produces a flat normal of `(0.5,0.5,1)` and is convenient for a visible normal map.
**Signed XYZ** produces `(0,0,1)`. Match **Output Encoding** on Height to Normal to
**Normal Encoding** on connected normal nodes; the graph does not apply color conversion to these vectors.
Add **Normal Blend** to combine Base and Detail using Reoriented or Whiteout, and adjust Strength
for the detail. Its output is normalized and uses the selected encoding.

For image distortion, use **UV/Distortion/Height to Distortion** with a height texture, or
**Normal to Distortion** with a normal vector. Connect its output to
**Vector Displacement.Displacement**, choose the same Signed RG or Packed RG mode on both nodes,
then connect **Vector Displacement.Out → Sample Texture.UV**. These two source nodes produce
offsets; they do not supply the final UV. Their vectors retain strength instead of being normalized.
Normal to Distortion scales X and Y separately. Signed neutral is `(0,0)`; packed neutral is `(0.5,0.5)`.
Height to Normal points opposite the height slope, while Height to Distortion follows it.
If distortion from a normal goes in the wrong direction, reverse Strength or use Flip X/Flip Y;
its amount also differs because the normal was normalized.
See the [color and normal node contract](../ShaderLayer.md#color-and-normal-nodes) for exact inputs and modes.

**Normal Encode** converts signed normals to packed RGB; **Normal Decode** converts them back.
They change representation without normalizing. **Normal Strength** flattens detail at 0,
preserves a unit normal at 1 and reverses XY with negative values.
To shade the result, connect it to **Normal Lighting.Normal**, match Normal Encoding, and choose
the surface Color, LightDirection, LightColor, Intensity and Ambient. Raise **SpecularIntensity**
from its default 0 to add a highlight; SpecularColor tints it, Roughness widens it, and
SpecularOnly shows only that contribution. Surface alpha is preserved.

**Premultiply** multiplies RGB by alpha; **Unpremultiply** restores straight RGB for nonzero
alpha and returns black RGB at zero alpha. Alpha itself stays unchanged. The layer's Output
expects straight color, so use Unpremultiply after calculations that produce premultiplied color.

## Filter a calculation

Connect a pattern or **Sample Texture.Out** to **Blur.In**, then connect Blur.Out to Output
to soften the image. **Sharpen** restores local contrast, **Edge Detection** extracts changes,
and **Dilate**/**Erode** expand or shrink bright areas. For colored inputs, each channel and alpha
is processed separately; connect a scalar mask when you want one shared contour.

Radius sets the sampling distance in output pixels. Blur, Sharpen, Dilate and Erode offer
**3 × 3**, **5 × 5** and **9 × 9** kernels. More samples retain more detail at the same radius;
a large radius with a small kernel can miss narrow features. Edge Detection uses a fixed
3 × 3 Sobel kernel. Strength adjusts Sharpen and Edge Detection. Results can exceed `0..1`;
use Clamp or Saturate if you need a bounded mask.

Filters read the connected calculation at neighboring coordinates. Keep the upstream pattern
dependent on UV; fixed coordinates produce a constant result. Chaining large kernels can slow
previews. Radius follows the current output resolution, so the small node preview can look
softer than the full layer result at the same setting.

## Reuse a Sub Graph

Select a calculation and choose **Convert Selection to Sub Graph** from the context menu.
Its external connections become the new node's inputs and outputs. Double-click the node to
edit the calculation, adjust **Inputs** and **Outputs** in the sidebar, then use **Back to Graph**.
Choose the definition from **Sub Graphs** in the creation menu to reuse it.

Editing a definition updates every instance in this Shader graph. Rename interface pins to
describe their purpose; their existing connections use stable identities. Definitions stay with
the document. Sub Graphs can contain other definitions, but cannot call themselves directly or
indirectly. Use ordinary parameters for controls shared by all instances, and interface inputs
for values that should differ between instances.

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
appear in node search.

The mini-node has no header, preview or parameter controls. Its **In → Out** connection passes
the source type and value unchanged: numbers, vectors, Color, Boolean, Texture, Gradient and Curve.
It adds no calculations and does not change the image. Drag the mini-node to arrange the wires,
and connect **Out** to several inputs to branch the same value.

Deleting a Reroute reconnects its incoming source directly to its downstream inputs, including
when several Reroutes form a chain. Deleting an ordinary connection breaks that connection.
Reroute positions and connections are saved with the document; insertion, movement, connection
changes and deletion support **Undo/Redo**.

## Preview a node

**Layer Preview** floats above the graph, initially at the bottom-right. Drag its header or image
to move it and its sides or corners to resize it. Near the viewport edges it snaps flush and stays
attached when the window changes size; near a corner it attaches to both edges. Highlighted borders
show the attachment. Drag away to detach. The window retains placement, attachments and size;
graph zoom and panning do not move it. The header's **R/G/B/A** buttons change only the preview channels.

UV, sampling, generators and image-processing math have **128 × 128** previews.
**Constant**, **Image Size**, **Texture Size**, **Split**, **Compare**, **Output**, **Parameter**
and **Reroute** have no preview; their fields and ports show the useful information.
Nodes without a preview, or with their preview collapsed, have no shared minimum width;
their contents determine the width. Expanding the preview restores its minimum width.
HLSL functions preview their numeric/Color outputs; Boolean and resource outputs are omitted.
If several previewable outputs exist, choose one with the output selector.

There is no Preview heading. Hover over the node to reveal an upward arrow over the top of
the image. Click it to collapse the preview to a thin row with a compact, centered downward-arrow
button; click that button to expand it again.
The sidebar has no shared selected-node preview.
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

Drag a parameter header within **Parameters** to reorder it, or onto the graph to create a
Parameter node at the drop position. Right-click the header for **Use in Graph**, **Duplicate**
and **Remove**. Duplicate creates an independent parameter with the same settings and value;
Remove also removes its nodes and connections. The indicator on the right matches the shape
and color of the parameter's output port.
Hover over a parameter to highlight its row and all its Parameter nodes without changing selection.

Parameters include numbers, vectors, Boolean values, colors, textures, gradients and curves.
Multiple Parameter nodes can use the same declared parameter, so one Properties field can control
several parts of the image.

A **Parameter** node is a compact single row with the parameter name and an output connector, without
an **Out** label, an internal preview or a dropdown. To choose another declared parameter, use
the node's context menu; adjust the parameter's value in the layer's **Properties**.

Port labels show the type in parentheses: `1`–`4` for numeric widths, `T2` for textures,
`G` for gradients, `C` for curves, `col` for colors and `bool` for Boolean values.
For example, `UV(2)` and `Color(col)`. The suffix follows the current port type.

Numeric and **Color** ports are circles, **Boolean** ports are diamonds, and **Texture**,
**Gradient** and **Curve** ports are squares.

You can connect numeric vectors with different component counts. A `float4` connected to a
`float2` input keeps X and Y and drops Z and W. Shorter vectors gain zero components, so
`float2(x, y)` becomes `float4(x, y, 0, 0)`; a vector connected to a scalar input keeps X.
A scalar repeats across every component of a numeric vector: `x` becomes `(x, x, x, x)` for `float4`.

**Color** is a separate port type carrying the same RGBA components as `float4`. Output's RGBA
input, Blend's color inputs and result, Sample Texture and Sample Gradient color outputs, and
Color Parameter outputs use fixed Color ports. Sample Curve produces a number.

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
horizontal grip lines work like the preview handle in Layer Settings. The panel starts collapsed and opens
after more than 14 logical pixels of upward dragging, regardless of graph zoom. Clicking alone
does nothing. Continue dragging to adjust its height; drag downward to hide the code. The panel
fits the available window space while leaving room for the graph, and the window remembers
its open/closed state and height.

The panel uses the same code field as FX Code in read-only mode: you can select and copy text,
with the same syntax highlighting, line numbers and horizontal/vertical scrolling supported
by your Editor version. Reopening shows the current generated code and keeps your selection
if the source is unchanged. Change the graph to change the generated code.

## HLSL and Custom Function nodes

Choose **Utility/Custom Function** to write a calculation inside a node. Open **Ports** to set
input/output names, types and directions, then edit the function body in **Code** and press
**Compile**. Code uses the same HLSL editor as FX. Collapse it when you need graph space, or drag
its lower-right resize handle to change node width and code height; width stops at the ordinary
node minimum. The code field fills the node width without a bottom gap.
Collapsing Code restores the normal node width; expanding restores your chosen size.
The document saves both the expansion state and size for each node.

Body and port edits remain saved drafts until Compile succeeds. Declared port settings remain
visible after script reload and copying; compiling unchanged code preserves input values.
The previous applied code and ports keep working if compilation fails, including when checking
an unconnected node.
Float/half vectors, Boolean and Color are supported; Texture is an input type. A function return
appears as **Result**; output arguments keep their names, and an inout argument has both ports.
Inputs named **UV** or **uv** with two numeric components use current-pixel coordinates by
default; switch **Custom / UV** to enter a fixed value.

Marked HLSL libraries appear by category in node search. **Color/Adjustments/Contrast** is provided in
`Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl`; Amount 1 keeps the color,
and other amounts change contrast while retaining alpha and HDR values. **Refresh HLSL**
rescans the catalog. **File…** on a Custom Function also lets you choose an `.hlsl` file and
select a supported function without a catalog marker.

Library nodes look like ordinary nodes: title, ports and preview, without a code panel.
Their context menu provides **Refresh HLSL** and **Choose HLSL File…**. The document uses its
source backup until an explicit refresh compiles successfully; missing or incompatible files
leave it intact. Editing a library does not automatically change an existing document.

Choose **Convert to Custom Function** from a library node's context menu to make an independent,
editable copy. Ports, values, connections and the current image are preserved, and the file link
is removed. **Code** contains the complete main source, including helper declarations; include
backups stay with the document. Edit the function declarations in Code to change its ports, then
press **Compile**. Existing include backups are retained, so editing works without the original
files. Conversion supports **Undo/Redo**. See the [function declaration contract](../ShaderLayer.md#hlsl-functions).

## Scope

The graph belongs to the Shader layer and stays editable with the document. Parameter changes
adjust the image; changing operations or connections changes the calculation itself.
Connections must lead toward Output without a cycle.

You can save an unfinished graph, including a missing resource connection or a Parameter node
that has not been assigned a parameter. If it cannot compile, the editor reports the errors and
keeps the last working result when available. That image does not represent the unfinished
changes. The document saves the editable graph, so you can complete it later.

Use the layer's normal FX stack for additional processing. For port types and execution rules,
see the [Shader layer contract](../ShaderLayer.md).
