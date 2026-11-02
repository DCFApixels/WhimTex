---
title: "Shader layer"
parent: "Layers and groups"
grand_parent: "English"
nav_order: 3
lang: "en"
description: "Build procedural textures and masks with Shader Graph or WhimTex HLSL Code. Keep both versions and their parameters while switching modes."
permalink: "/en/shader-layer/"
translations: "en/shader-layer.md,ru/shader-layer.md,zh/shader-layer.md"
---

# Shader layer

Use **Shader** to build a texture or mask with a node graph or WhimTex HLSL code.
Both modes produce the layer's own image. **Shader Processor** instead processes the combined
image below it; **Shader FX** processes the image of the layer it belongs to. See [Shader FX and Processor](shader-fx.md).

## Choose Graph or Code

In **Properties**, **Mode** selects **Shader Graph** (the default) or **Code**.
Switching keeps the graph, code and each mode's parameter values. It does not convert one into
the other. Save the document to keep both versions and return to either later.

For Code, paste or write WhimTex HLSL using `ApplyFX` and `@param`, then click **Compile**.
A new Code version starts as opaque white. Editing the text does not change the image until
compilation succeeds. If compilation fails, the previous applied result and parameter values
remain available; correct the text and compile again. Adjust the accepted parameters in
Properties using the same [shader controls](shader-controls.md) as Shader FX, including previews.

Code generates an image in the layer's local coordinates. The incoming color and `SampleInput`
read the selected **Input**, described below. For additional images, declare Texture parameters
and read them with `SampleTexture`. The
[HLSL contract](../ShaderLayer.md#independent-graph-and-code-modes) describes the interface.

When returning to Shader Graph, your graph and its values are still there. An already open
graph window is read-only while Code is selected; switch back to edit it.

## Choose an input image

**Input** in Properties is shared by Shader Graph and Code:

| Source | Result |
| --- | --- |
| **None** | Transparent black; selected by default. |
| **Texture** | Read an assigned texture asset. |
| **Layer** | Read a selected layer or group. |
| **Composite Below** | Read the combined lower layers in the same group or root stack. The Shader itself and layers above it are excluded. |

Graph reads this image with **Sampling/Sample Input**. Connect its Color **Out** to your
calculation; its **UV** defaults to local coordinates and accepts a distorted coordinate.
Wrap and Filter work as on Sample Texture. Code uses `SampleInput(uv)` or the incoming
`color` in `ApplyFX`. A missing source produces transparent black. Switching Input or Mode
keeps your source selections.

The layer still uses ordinary blending. Passing Composite Below through unchanged may make
translucent areas more opaque because the Shader result is placed over the lower image.
Use Shader Processor when the result should replace that composition.

## Create and adjust an image

1. Add a **Shader** layer from **+** at the bottom of Layers.
2. Open **Properties**, select **Shader Graph** in **Mode**, and choose **Edit Graph**.
   Add nodes through **Create Node** in the graph context menu or press **Space**.
3. Connect the calculation to **Output**, which supplies the image's red, green, blue and alpha channels.
4. Declare parameters for values you want to adjust from the layer's **Properties**.
   Use **Parameter** nodes to read them in the graph; **Constant** supplies mathematical constants.
5. Return to the layer's **Properties** to adjust the result without rebuilding the graph.

A new graph starts with a Color parameter connected to Output. Choose opaque red in Properties
to try it. For a value kept inside the graph, disconnect an input and edit its value beside the port.

Ports straddle the inner background edges and remain visible above the selection outline.
Connection leads are shorter to suit the compact nodes.
Reroute keeps its existing inset ports.
Preview and additional settings areas share the output-port column's background.

The result uses the usual layer **Transform**, **Rendering**, **FX** and blending controls.
Use **Input** and **Sample Input** for the shared image, or declare additional **Texture**
parameters and read them with **Sample Texture**. Input defaults to None.

## Mathematical constants and Swizzle

**Input/Instance Index** and **Input/Time** each provide a Float output named **Out**.
Neither has its own preview. Both have Float inputs **Start** (default `0`) and **Scale** (default `1`).
**Instance Index** returns **Start + index × Scale**; the current index is `0`, so the result equals Start.
**Time** supplies Editor time in seconds to dependent node previews and the result in the graph window.
Its output is **Start + time × Scale**. Set either input manually or connect another node.
The layer itself, Canvas, thumbnails and export always use time `0`, so Time returns Start there.
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
number of levels, and PixelSize sets the pattern scale in document pixels. Alpha is preserved;
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
The vector icons remain sharp when zooming in.
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

**Canvas Position** anchors coordinates to the document. Its three vector outputs are
**Normalized(2)** (`0..1`), **Aspect X(2)** (X multiplied by width/height) and **Aspect Y(2)**
(Y multiplied by height/width). All start at the lower-left corner. On a 2:1 canvas, Aspect X
spans `0..2 × 0..1`; Aspect Y spans `0..1 × 0..0.5`. Layer transforms do not move the field.
**Canvas Data** provides **Size(2)**, **Texel(2)** and **Pixel Scale(1)**, the pixel multiplier
from the Frame tool. Graph zoom and preview resolution do not change these values.

| Nodes | Use them for |
| --- | --- |
| **Parameter**, **Constant** | Adjustable layer values or mathematical constants: PI, TAU, PHI, E, SQRT2. |
| **UV**, **Canvas Position**, **Canvas Data**, **Texture Size** | Local or canvas position, document dimensions/pixel scale, or an input texture's dimensions. |
| **Sample Texture** | Read an image at chosen UV coordinates. Change the coordinates to distort it. |
| **Sample Gradient**, **Sample Curve** | Turn a number into a gradient color or reshape it with a curve. |
| **Split**, **Combine**, **Swizzle** | Separate, assemble or reorder vector/color channels. |
| **Add**, **Subtract**, **Multiply**, **Divide** | Offset, scale and combine values. |
| **Min**, **Max**, **Clamp**, **Abs**, **One Minus**, **Negate** | Limit values, take their magnitude or reverse them. One Minus calculates `1 − x`. |
| **Power**, **Square Root**, **Floor**, **Ceil**, **Round**, **Fraction** | Shape transitions, create steps or repeat a numeric interval. |
| **Sine**, **Cosine** | Create waves and repeating patterns. |
| **Lerp**, **Remap**, **Step**, **Smoothstep** | Mix values, change ranges and create hard or soft thresholds. |
| **Unlerp** | Find the position of T within A–B: `(T-A)/(B-A)`, without clamping. Defaults: A = 0, B = 1, T = 0. Supports vectors component by component; equal bounds are undefined. |
| **Length**, **Distance**, **Dot**, **Normalize** | Calculate distances and directions. |
| **Compare**, **Select** | Choose between two values using a condition. |
| **Transform UV** | Move, scale or rotate the coordinates used by downstream nodes. |
| **Pixelate UV** | Snap coordinates to cell centers before sampling a texture or generating a pattern. |
| **Noise**, **Cellular**, **Random** | Generate noise, cell distances or repeatable random values from coordinates and a seed. |
| **Checkerboard**, **Stripes**, **Grid**, **Rings**, **Gradient Factor** | Create smooth pattern masks or a position for sampling a gradient. |
| **Shape**, **Combine Distance** | Create shape masks and combine their signed distances. |
| **Polar Coordinates**, **Repeat**, **Mirror**, **Kaleidoscope** | Remap or fold coordinates to repeat and reflect motifs. |
| **Domain Warp**, **Vector Displacement**, **Twirl**, **Bulge / Pinch**, **Wave**, **Ripple** | Distort coordinates before generating a pattern or sampling an image. |
| **Hue**, **Saturation**, **Exposure**, **Lightness**, **Contrast**, **Levels**, **Color Balance**, **Posterize** | Adjust color, brightness and tonal steps. |
| **Convert Color**, **Luminance**, **Gradient Map**, **Color Curves** | Convert color components or remap an image's tones. |
| **Color Mask**, **Hue Range**, **Mix Color**, **Set Alpha** | Select colors, mix them and control transparency. |
| **Channel Mixer**, **White Balance** | Mix RGB channel contributions or adjust Temperature and Tint while retaining alpha and HDR. |
| **Directional Blur**, **Radial Blur** | Blur a connected branch along a line, around a center or toward/away from that center. |
| **Height Blend** | Combine height layers and reuse the blend mask for their colors. |
| **Tile UV**, **Perspective UV** | Place repeated motifs in cells or map an image into a quadrilateral. |
| **Gabor Noise**, **Rotate Vector**, **Blackbody** | Create directional texture detail, rotate vectors or generate temperature colors. |
| **Slope Blur**, **Bilateral Blur** | Smear along a height slope or soften detail while preserving guide edges. |
| **Height to Normal**, **Normal Blend**, **Height to Distortion**, **Normal to Distortion** | Turn a texture or height calculation into normals or displacement and combine normal detail. |
| **Blend** | Composite two colors with a blend mode and alpha. Mix Color interpolates colors; Lerp interpolates numeric components. |
| **Output** | Produce the final RGBA image. |

For colored noise, connect **UV → Noise → Sample Gradient → Output** and supply a
**Gradient** parameter to Sample Gradient. Adjust the palette in Properties.
For a threshold mask, place **Smoothstep** after Noise; use its result as alpha with **Combine**.

**Pixelate UV** uses **CellSize** `(8,8)` and **Offset** `(0,0)` in document pixels. Each cell
dimension has a minimum of one pixel. Connect Out to a texture or pattern's UV input.
**Channel Mixer** exposes Red, Green and Blue coefficient vectors: `1` means full contribution.
Its defaults preserve the original RGB; negative weights and HDR values are supported.
**White Balance** starts at Temperature = Tint = `0`. Both controls use `-1..1`;
positive Temperature warms, negative cools, positive Tint adds magenta and negative adds green.

**Directional Blur** uses Radius in document pixels and Angle in degrees; the path extends
by Radius on either side. **Radial Blur** offers **Spin** (±Angle degrees around Center) and
**Zoom** (radius factors `1 ± abs(Strength)`). Strength defaults to `0.1`, giving `0.9..1.1` of the
distance from Center. The unused control stays visible but inactive. Both blurs preserve Color
transparency without bleeding hidden RGB. **Samples** defaults to `17` and rounds upward to an
odd count between `3` and `129`. Increase it for longer paths or thin detail; more samples cost more.

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

## Blend heights and place surface detail

**Height Blend** combines **Bottom** and **Top** heights and returns **Height** plus **Mask**.
Use Height for the surface and Mask to mix the matching colors. Offset raises or lowers Top;
Smoothness softens the transition, and Opacity reduces the top contribution. Defaults are
Bottom `0`, Top `1`, Offset `0`, Smoothness `0.1` and Opacity `1`. Signed and HDR heights are retained.

**Tile UV** repeats a motif in **Grid** or **Staggered** cells. Connect its **UV** output to the
motif's UV input and multiply its alpha by **Mask**. **Count** defaults to `(4,4)`; **Offset**
uses cell units. **RowOffset** defaults to `0.5` and affects Staggered rows only. **RandomRotation**
sets a random angle range in degrees; **RandomScale** controls scale variation from `0` to `1`.
Both start at `0`; **Seed** `1337` makes the variation repeatable. **Cell**, **Center** and **Random**
provide cell coordinates, the cell center in image UV and one random value per cell. Motifs stay
within their original cells; rotated or enlarged parts are clipped rather than overlapping neighbors.

**Perspective UV** fits an image into four corners: **BottomLeft**, **BottomRight**, **TopRight**
and **TopLeft**. Their defaults are `(0,0)`, `(1,0)`, `(1,1)` and `(0,1)`. Connect **UV** to the
image sampler and multiply sampled alpha by **Mask** to hide the area outside the quad.
The mapping preserves perspective. Collapsed, crossed or nonconvex quads return zero UV and Mask.

**Gabor Noise**, under **Procedural/Noise**, creates directional texture detail with approximate antialiasing.
**Scale** `(8,8)` controls density, **Frequency** `2` controls the fine oscillations, **Anisotropy**
`0.8` controls directional coherence, and **Angle** `0` rotates the direction in degrees.
**Seed** defaults to `1337`. **Value**, **Phase** and **Intensity** each provide a different
`0..1` field for masks or gradient colors. Gabor Noise costs more than simple noise; placing it
before a sampled filter can noticeably slow previews.

**Rotate Vector**, under **Vector**, rotates **In** by **Angle** degrees without changing its
length. **2D** uses two components; **3D** adds **Axis**, initially `(0,0,1)`, and uses three.
In starts along X, Angle starts at `0`, and a zero Axis leaves In unchanged. To rotate UV around
a chosen center, subtract the center before rotation and add it back afterward.

**Blackbody**, under **Color/Generation**, produces **Out**, an approximate linear RGB temperature color with
alpha `1`. **Temperature** starts at `6500` Kelvin and is limited to `1000..40000`: lower values
give warm reds/oranges and higher values give cool blues. Adjust brightness separately with
Multiply or Exposure; temperature sets the normalized color.

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
Polygon and Star keep a uniform distance and Feather when stretched along either axis.
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
Phase repeats every cycle, including negative values. Grid's Offset repeats every unit per axis;
Checkerboard's repeats every two units per axis. Large offsets retain clean pattern fills and edges.
Stripes offers Square, Triangle and Sine profiles; Width affects Square only.
Angle, Rotation and Direction use degrees; Twirl's Strength is also an angle in degrees.

Connect a UV node's **Out** to the next pattern or **Sample Texture.UV**.
**Repeat.Cell** contains cell coordinates, including negative coordinates; **Input/Instance Index**
uses a separate index (currently zero) and is unrelated to it. **Domain Warp.Displacement** exposes the offset
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
The node reads that calculation at neighboring coordinates, with Radius measured in document pixels,
independent of preview size.
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
and **Dilate**/**Erode** expand or shrink bright areas. Blur accounts for Color alpha to avoid
halos from invisible colors; its Color input and output use straight RGBA. Sharpen and Edge
Detection preserve Color alpha. Numeric vectors and Dilate/Erode still process components
independently; use a scalar mask for one shared contour.

Radius sets the sampling distance in document pixels. Blur, Sharpen, Dilate and Erode offer
**3 × 3**, **5 × 5** and **9 × 9** as a Minimum Kernel. Larger radii automatically add samples
to keep their spacing within one pixel. A denser minimum helps with subpixel details. Edge Detection uses a fixed
3 × 3 Sobel kernel. Strength adjusts Sharpen and Edge Detection. Results can exceed `0..1`;
use Clamp or Saturate if you need a bounded mask.

Filters read the connected calculation at neighboring coordinates. Keep the upstream pattern
dependent on UV; fixed coordinates produce a constant result. Large radii, especially in chained
filters, can slow previews. Radius uses the same document scale in node previews and full layer renders.
Radius can vary across the image; built-in shapes and patterns retain antialiasing inside
the filter. At zero Radius, Blur, Sharpen, Dilate and Erode preserve the original input.

**Slope Blur** smears **In** downhill along a scalar **Slope** field. Connect a height or noise
branch to Slope; its default `0` leaves the image unchanged. **Strength** defaults to `8`;
negative values reverse the path. Steeper slopes travel farther because the gradient is not
normalized. **Samples** defaults to `16` and rounds to `1..64`; more steps follow the path more
closely. **Average** softens the path, **Min** retains darker values and **Max** retains brighter
values. Average accounts for Color alpha; Min/Max process each component, including alpha.
Zero Strength preserves In. This filter has no Radius control.

**Bilateral Blur** softens detail while retaining edges selected by a guide. **Radius** defaults
to `2` document pixels. **Tolerance** defaults to `0.1`: smaller values preserve more guide
differences; larger values allow more smoothing across them. It cannot be negative, and `0`
accepts only exact guide matches. **Guide = Source** uses In itself; **Custom** adds a numeric
or Color **Guide** input for an independent pattern, defaulting to scalar `0`. In both modes,
Color guides compare premultiplied RGBA and ignore hidden RGB in fully transparent areas.
Guide does not change the output type: it follows In. Color filtering respects alpha,
and zero Radius preserves In.

Both nodes keep the input's numeric or Color type. Slope Blur repeatedly samples both the image
and its slope; Bilateral Blur reads both image and guide. High Samples, large Radius and chains
of filters can slow previews, especially when their inputs contain Gabor Noise.
The first compilation of a deeply nested filter chain can also take several minutes.

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

UV, sampling, generators and image-processing math have previews up to **128 × 128**.

Node previews compile asynchronously and share a limited update budget. Zooming out reduces their resolution to match their screen size; zooming in restores it. Unrelated branch edits reuse existing shaders. Animated node previews update at up to 30 Hz in the focused graph window and 10 Hz otherwise.

Initial previews of graphs using built-in nodes are prepared faster, without reducing their resolution or changing the calculations.

**Constant**, **Canvas Data**, **Texture Size**, **Split**, **Compare**, **Output**, **Parameter**
and **Reroute** have no preview; their fields and ports show the useful information.
Nodes without a preview, or with their preview collapsed, have no shared minimum width;
their contents determine the width. Expanding the preview restores its minimum width.
HLSL functions preview their numeric/Color outputs; Boolean and resource outputs are omitted.
If several previewable outputs exist, choose one with the output selector.

There is no Preview heading. Hover over the node to reveal an upward arrow over the top of
the image. Click it to collapse the preview to a thin row with a compact, centered downward-arrow
button; click that button to expand it again.
Each node's expanded or collapsed state is saved with the document and retained when copying nodes.
The sidebar has no shared selected-node preview.
Previews refresh when their result changes, only while expanded and visible in the graph
viewport. Collapsed or off-screen previews wait until they are expanded and visible again.

Numbers appear as grayscale; two-component vectors use red and green, three-component vectors
use RGB, and four-component vectors and Color include alpha. Texture, Gradient and Curve resources cannot
be shown directly: connect the corresponding Sample node and expand its preview.

**Output** is a horizontal end block: input on the left, **Output / Layer result** on the right,
with a rounded end. **RGBA** accepts color with alpha, or RGB with alpha 1.
**Alpha Scale** defaults to **1** and multiplies the input alpha; connect a mask or enter a value.
RGB is unchanged and the multiplier does not clamp the result. Use
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

The panel shows the WhimTex FX script generated from your graph, including `@param` declarations
and `ApplyFX`. This is the code before it is wrapped in ShaderLab for Unity.

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
