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
Both modes can read an input image. **Composite Below** reads the lower composition;
**Crossfade** mixes its processed result by Opacity. The ordinary **FX** list applies after the graph or Code.

**Shader Program** is the shared content of a Shader layer or an FX. **Draft** is the editable version; **Working** is the last successfully compiled version. **Compile** updates the working version, while **Apply** on an FX bakes its result.

## Repeat a branch with For

Add **Utility → Loop → For Begin** or **For End** to create a connected pair.
Set **Iterations** and **Initial** on Begin. Use **Value** inside the body and connect
its new value to End. **Index** starts at 0; **Result** returns the final value.
For example, Initial=1 and multiplying Value by 2 for 4 iterations gives 16.
Zero or negative counts return Initial; fractional counts are rounded down.

Loops can be nested and carry numbers, vectors or colors. Internal previews show the
first iteration; End previews the completed loop. Read a previously baked Buffer inside
a loop, or place Buffer after the whole loop; writing an image on each iteration is not
supported. Copying or deleting an endpoint also includes its partner. Select the body
as well when copying a complete loop.

## Choose Graph or Code

You can save an unfinished or failed draft when the selected mode has a working version.
Reopening restores that result and keeps the draft editable, including without the original preset.
If there is no working version, compile successfully before saving TIFF.

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

## Reuse a shader preset

Choose **Preset…** in Properties to load a Code or Graph preset from the shared project/user
catalog. The same presets can be used in [Shader FX](shader-fx.md). **Save Preset…** saves
Code as `.hlsl` and Graph as `.whimtexshader`; Graph presets retain both modes, reusable
functions, Sub Graphs and captured includes for later editing. Clear Texture parameters'
Layer references in both modes before saving a preset, including inactive selections.

A linked preset follows only its saved ID and exact version. If its source disappears, the
document keeps its editable copy, including the graph. **Embed Copy** makes it independent.
Save the document to retain drafts and both modes. Input, Transform and blending remain
settings of this layer; using the same preset in an FX can produce different pixels because
the input and coordinates differ. Check the result after changing placement or transforms.

## Choose an input image

**Input** in Properties is shared by Shader Graph and Code:

| Source | Result |
| --- | --- |
| **None** | Transparent black; selected by default. |
| **Texture** | Read an assigned texture asset. |
| **Layer** | Read a selected layer or group. |
| **Composite Below** | Read the lower composition, including the external backdrop of Pass Through groups. The Shader itself and layers above it are excluded. |

Graph reads this image with **Sampling/Sample Input**. Connect its Color **Out** to your
calculation; its **UV** defaults to local coordinates and accepts a distorted coordinate.
Wrap and Filter work as on Sample Texture. Code uses `SampleInput(uv)` or the incoming
`color` in `ApplyFX`. A missing source produces transparent black. Switching Input or Mode
keeps your source selections.

To process the image below, select **Code**, **Composite Below** and **Crossfade**. Compile `float4 ApplyFX(float4 color) { return color; }`, then add effects to **FX**. Opacity mixes before/after without increasing alpha. In Graph, connect **Sample Input → Output** instead. Pass Through groups include their external backdrop; isolated/clipped groups stop it.

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

**Utility/Expression** replaces a short calculation chain with a formula. Enter
`a + b * c / 2` to get inputs `a`, `b`, `c` and output **Out**. With values 1, 2 and 10,
the result is 11. **Type** selects Float–Float4 for all ports together.
HLSL functions, parentheses, swizzles and vector constructors work too: for example,
`saturate(a * b)` or `float2(a.x, b.y)` with Type set to Float2.
Leave the field to apply the formula. Empty text returns zero. Connections follow variable
names, including letter case; removed connected inputs stay inactive until their name returns.
Errors appear beside the node. Use Custom Function for a function body with statements.

**Input/Instance Index** and **Input/Time** each provide a Float output named **Out**.
Neither has its own preview. Both have Float inputs **Start** (default `0`) and **Scale** (default `1`).
**Instance Index** returns **Start + index × Scale**; the current index is `0`, so the result equals Start.
**Time** supplies Editor time in seconds to dependent node previews and the result in the graph window.
Its output is **Start + time × Scale**. Set either input manually or connect another node.
Canvas uses the document's separate [Play/Pause and seeking](preview.md#document-playback) controls; these do not stop the graph's clock. Save, ordinary export and Live Update use time `0`, so Time returns Start there.
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

For repeating noise, enable **Periodic** and set a positive **Period** on a noise node such as **Perlin Noise**.
**Scale** controls density; use **Add** before **Position** to shift the pattern. **Cellular Noise → Distance to Edge** produces
normalized boundary distance; threshold Value to draw cell-border lines.
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
| **Range Mask** | Select values around Center. Width is the full interval, including inward Feather on each edge. Falloff selects Linear or Smooth. Works independently on each vector component; use Luminance first to select brightness from a color. |
| **Length**, **Distance**, **Dot**, **Normalize** | Calculate distances and directions. |
| **Compare**, **Select** | Choose between two values using a condition. |
| **Transform UV** | Move, scale or rotate the coordinates used by downstream nodes. |
| **Apply Transform 2D** | Use a Transform2D parameter to map UV into its local frame or back into the input frame. |
| **Pixelate UV** | Snap coordinates to cell centers before sampling a texture or generating a pattern. |
| **Perlin Noise**, **Cellular Noise**, **Random** | Generate noise, cell distances or repeatable random values from coordinates and a seed. |
| **Checkerboard**, **Stripes**, **Grid**, **Rings**, **Gradient Factor** | Create smooth pattern masks or a position for sampling a gradient. |
| **Shape**, **Combine Distance** | Create shape masks and combine their signed distances. |
| **Polar Coordinates**, **Repeat**, **Mirror**, **Kaleidoscope** | Remap or fold coordinates to repeat and reflect motifs. |
| **OpenSimplex2 Warp Noise**, **Vector Displacement**, **Twirl**, **Bulge / Pinch**, **Wave**, **Ripple** | Distort coordinates before generating a pattern or sampling an image. |
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

For colored noise, connect **UV → Perlin Noise (Position → Value) → Sample Gradient → Output** and supply a
**Gradient** parameter to Sample Gradient. Adjust the palette in Properties.
For a threshold mask, place **Smoothstep** after Perlin Noise; use its result as alpha with **Combine**.

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

Unconnected **Texture**, **Gradient** and **Curve** inputs also have inline editors:
a Texture2D asset picker, the gradient editor and the native curve editor. Fields are
108 px wide. A connection hides the field and keeps its value for disconnection.
The value is saved in the document and working graph; edits update previews and FX.
Sub Graph inputs and Color Curves support the same fields. An unassigned texture samples
white; gradient and curve defaults match graph parameters.

For **T** on Sample Gradient and Sample Curve, choose **UV.x**, **UV.y**, or **Custom**
to enter a number. The same choice is available for scalar noise Position in 1D.
New nodes default to UV.x; existing numeric values
and connections are preserved.
Sample Gradient and Sample Texture provide **RGBA** and separate scalar **R**, **G**,
**B**, **A** outputs. Existing full-color connections continue to use RGBA.

Unconnected numeric inputs have visible, editable fields outside the node body, to the
left of their ports. The ports stay unobstructed for wiring. Connecting a wire hides the field;
disconnecting it brings the field back. Unconnected UV ports keep implicit current-pixel
coordinates (`PixelUV`): use the local **Custom / UV** option to enter coordinates or use UV.
Single Float fields also have an **X** label. Drag it horizontally to adjust the value, as with vector components.
Depending on the node, inputs that do not apply to the selected mode are hidden or disabled along with their fields.
For example, warp **Fractal = None** disables Octaves, Lacunarity and Gain; the separate Fractal node's PingPongStrength is active
only for Ping Pong. Hover a port to see why it is inactive. Inactive ports, including unavailable
Split components, cannot accept new wires. If a setting hides a connected port, it stays visible
and dimmed with its wire intact. Switching back restores the connection. Hidden unconnected ports
remain hidden. A temporarily unavailable output leaves the receiving input at its saved/default value.

Drag a wire from an input or output port onto empty graph space to open the compatible-node search.
The menu opens to the right of the cursor with its left edge aligned to it.
Dragging from an output lists compatible inputs, such as `Multiply(A)` and `Multiply(B)`.
Dragging from an input lists compatible outputs, such as `Cellular Noise(F1)` and `Cellular Noise(F2)`,
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

**Gabor Noise**, under **Procedural/Noise**, creates directional detail in 1D/2D/3D.
**Frequency=2** controls the fine oscillations, **Bandwidth=1** the envelope width,
**Anisotropy=0.8** directional coherence, and vector **Direction** (initially along X) orientation.
**Value**, **Phase**, **Intensity** and **Phasor** share one noise evaluation.
**Phasor** produces more even-contrast bands. Choose **Profile: Sine / Triangle / Pulse**;
**Phase Offset=0** moves the bands in cycles (one repeats the pattern). **Width=0.5** controls
Pulse's bright fraction; 0 is black and 1 is white. Width hides outside Pulse unless connected,
in which case it remains visible but inactive. Values and wires survive profile changes.
These controls affect only Phasor. Filtering smooths small bands toward the profile average;
true phase singularities remain, with fading near signal zeros inside a pixel.
**Gradient** and 2D/3D **Curl** retain signed strength. Sampled filters can make this noise expensive.

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
| **Procedural/Noise** | Perlin Noise, Value Noise, Value Cubic Noise, OpenSimplex2 Noise, OpenSimplex2S Noise, Flow Noise, Cellular Noise, Triangle Voronoi Noise, Gabor Noise, Spots Noise, Shard Noise. |
| **Procedural/Noise/Warp** | OpenSimplex2 Warp Noise, OpenSimplex2 Reduced Warp Noise, Basic Grid Warp Noise. |
| **Procedural/Noise/Grain** | White Noise, Blue Noise. |
| **Math/Random** | Random. |
| **Sampling** | Buffer. |
| **Procedural** | Fractal. |
| **Procedural/Pattern** | Checkerboard, Stripes, Grid, Rings, Gradient Factor. |
| **Procedural/Shape** | Shape, Combine Distance. |
| **UV** | Transform UV, Apply Transform 2D, Polar Coordinates, Repeat, Mirror, Kaleidoscope. |
| **UV/Distortion** | Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple. |

Noise nodes use the shared WT library. Choose **Dimensions**: **1D** for a line,
**2D** (default) for a plane, or **3D** for a volume. Unconnected **Position** uses the
pixel's UV, its X component in 1D, or UV plus an editable **Z** (default 0) in 3D.
In 3D, the UV selector occupies X/Y; Z remains a native numeric field with label dragging.
Switching UV/Custom or Dimensions preserves Z. **Scale=8** controls density per
axis. Use **Add** before **Position** to translate the pattern. **Periodic** is off by default. Enable it to expose **Period=1** per axis; positive values repeat the field, and nonpositive values disable an axis. When disabled, period calculations are omitted; connected Period remains visible but inactive, retaining its values and connection. **Seed=1337** selects the repeatable variation.
Changing Dimensions retains values and all connections. A connected Curl output stays visible but inactive in 1D.
**Filtering**, disabled by default, reduces unresolved detail, including inside sampled filters.
**Triangle Voronoi Noise** is fixed to **2D** and has no Dimensions selector; its position and domain controls stay two-component.

Scalar noise starts with **Value**, a `0..1` field for masks and gradients. Perlin, Value,
Value Cubic, OpenSimplex2/2S, Flow and Gabor also provide **Gradient**, plus **Curl** in
2D/3D. These signed vectors retain their strength. **Flow Noise Phase=0** rotates the
gradients smoothly; Phase is cycles, so 0 and 1 give the same pattern.

**Shard Noise** creates a faceted pattern. **Sharpness=0.7** controls the transition from
soft slopes at 0 to sharp shards at 1. Its **Value** output supports the same 1D/2D/3D,
Period and Filtering controls. For animation, use 3D and vary Position Z.

**Triangle Voronoi Noise** creates angular cells in 2D; it does not generate a triangular
mesh, and individual cells need not be triangles. **Distribution** selects
**Uniform** (default) or **Fixed Radius**, **Jitter=1** moves their feature points, and
**Stretch=(1,1)** changes the cell shapes along X/Y, limiting each axis's magnitude to
`0.5..2`; Jitter is limited to `0..1`. At Jitter=0, equal gauge values can form plateaus;
their interiors are not drawn as borders. Threshold **Value**, a
normalized `0..1` border field, to draw cell boundaries. **Distance** measures raw
Euclidean distance to the selected feature; **CellValue** supplies its repeatable value and
**ToPoint** points toward it. **BorderNormal** is a unit direction toward the nearest
boundary, or zero when that direction is degenerate.
These auxiliary outputs stay unfiltered; they are not Gradient/Curl outputs.

For layered detail, connect the result to the separate **Fractal** node in **Procedural**.
It repeats any numeric or Color branch in the graph's 2D coordinates; it is not exact
per-noise WT fractal evaluation, even when the source contains 1D/3D noise. Its **Out**
keeps the type of **In**. Scalar noise nodes have no embedded fractal or octave controls.
**FBm** (default) blends detail and retains negative/HDR values. **Ridged** creates ridges
with `1-abs(2*v-1)`; **Ping Pong** folds with `1-abs(frac(v*max(strength,0))*2-1)`.
The normalized weighted combination applies to each component, including Color alpha.
**Octaves=3** sets count (`1..8` after integer conversion), **Lacunarity=2** the frequency
step (`0.01..16`), **Gain=0.5** the amplitude step (`0..1`), and **Weighted Strength=0**
the modulation by earlier values (`0..1`). **Ping Pong Strength=2** controls folding and
is shown only in Ping Pong; stored values and connections are retained.
**Filtering**, initially on, fades octaves using the local UV pixel footprint; the source
noise's filtering stays independent. **Seed Step=1** changes the noise seed each octave,
including White/Blue grain. It does not automatically vary seed parameters in custom HLSL functions.

**Multifractal** creates uneven detail through multiplication. **Hybrid Multifractal**
mixes smooth areas with rough peaks; **Hetero Terrain** adds detail in proportion to the
height already formed. **Ridged Multifractal** creates squared ridges with feedback between
octaves; the existing **Ridged** remains unchanged. These modes expect source noise in 0..1,
internally center it around zero and return unnormalized heights, which may exceed 0..1.
**Bias=1** shifts the height level in Hybrid/Hetero and the ridge level in Ridged Multifractal.
**Feedback=2** controls detail propagation in Hybrid/Ridged Multifractal; **Gain** still
controls amplitude decay. Weighted Strength is unused in these four modes. Unused controls
are hidden unless connected; connected ports stay visible but inactive.

Use **Buffer** in **Sampling** to render a costly branch once and sample its result later.
Connect numeric/Color **In** and use **Out** with the same type. **UV** defaults to the
current pixel's UV; **Wrap** and **Filter** control sampling. **Resolution → Canvas**
uses the current render-target size, so node previews use their preview resolution.
**Custom** uses the exact **Size** (initially `512 × 512`). Values retain float32 precision,
negative values and HDR. The buffer reuses its allocation, updates when its source changes,
and its generated pixels are not saved into the document. External RenderTexture or
custom-function sources refresh each owner render; nested buffers run in dependency order.
Buffer's Generated HLSL needs intermediate textures and passes managed by the graph;
copying the code alone does not create them.

**Buffer → Fractal** requires an unconnected **Seed Step=0**, because the buffered image
cannot change its seed per octave. A connected Seed Step is rejected even if it currently
outputs zero. **Fractal → Buffer** is allowed and stores the completed fractal result.
Buffer captures the upstream field only within local `0..1`. Outside that image, Fractal
uses Buffer's **Wrap**: **Clamp** by default, or **Repeat** for tiled octaves. Zooming into
one fixed buffered image does not reveal new detail.
Warp keeps **None**, **Progressive** and **Independent** as a different domain-warp operation.

**Evolution=0** on Cellular and Triangle Voronoi moves
points continuously within their Jitter range. Connect Time for animation; negative
values run the motion backwards, while zero keeps the original pattern. There is
no common 0..1 cycle. Fixed Radius rotates at constant radius in 2D/3D; in 1D points
move between the two endpoints. Matching Evolution and other point settings keep
both noises aligned. Tiling is retained when Evolution is constant across the
image or repeats with the same spatial period.

**Cellular Noise** combines cell values, distance patterns and cell boundaries.
**Output** selects **Cell Value**, **F1** (default), **F2**, **Add**, **Subtract**,
**Multiply**, **Divide** or **Distance to Edge**. **Smoothness=0** preserves hard
patterns; increasing it blends neighboring values or softens distance ridges.
**Metric** offers **Euclidean**, **Euclidean Squared**, **Manhattan** and **Hybrid**;
it is hidden only for Distance to Edge, which uses Euclidean boundaries.
**Distribution** selects **Uniform** or **Fixed Radius**; **Jitter=1** controls point
displacement. Separate **F1**, **F2**, **CellValue** and **ToPoint** ports retain hard,
unfiltered geometry. CellValue is repeatable, not a unique identifier.

**Spots Noise** creates soft marks. **Density=1** changes their count, **Radius=0.35**
their size, **Softness=0.5** their edges, **Elongation=1** their stretching and vector
**Direction** (initially along X) their orientation.

The three **Procedural/Noise/Warp** nodes share Position, Scale, Period and Seed.
**Strength=0.1** controls displacement. Their fractals are **None**, **Progressive**
(each octave bends the next) or **Independent** (each samples the original position),
using Octaves, Lacunarity and Gain. **Displacement** is the signed offset;
**Position** is input plus offset. Connect Position to the next noise or image sampler;
Strength=0 preserves it. Warp has no Weighted Strength or Ping Pong Strength.

**White Noise** and **Blue Noise**, under **Procedural/Noise/Grain**, support **1D** stripes or
**2D** grain. **Position** stays two-component UV; use **Add** to shift it. For a shift in pixels, divide by **ImageSize** first.
**Grain Size=1** in pixels, **Direction=0** in degrees, and **Seed=1337** selects the variation.
**Image Size** uses **Canvas** dimensions by default; choose **Custom** to enter a size. **Inverted** starts off.
**Value** is monochrome; **Color** has independent RGB and alpha 1. Grain has no fractal; its Direction applies only in 1D. Gabor/Spots Direction,
Anisotropy and Elongation apply only in 2D/3D.

Old Noise, Cellular, Domain Warp, Tileable Noise, Voronoi Edge and old Gabor nodes are
unsupported and are not converted automatically. Recreate them using the current nodes.
The separate Noise layer keeps its existing behavior.

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
Angle, Rotation and pattern/grain Direction use degrees; Gabor/Spots Direction is a vector.
Twirl's Strength is also an angle in degrees.

Connect a UV node's **Out** to the next pattern or **Sample Texture.UV**.
**Repeat.Cell** contains cell coordinates, including negative coordinates; **Input/Instance Index**
uses a separate index (currently zero) and is unrelated to it. **OpenSimplex2 Warp Noise.Displacement** exposes the offset;
its **Position** output supplies the displaced coordinates. Distortions leave coordinates unchanged at **Strength = 0**;
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
luminance, then Red, Green and Blue refine individual channels. Leave a curve unassigned for
no change. Assign it inline or connect a Curve parameter. Alpha changes only through its own curve. Identity curves retain values above
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

Inputs that repeatedly evaluate their source, even when unconnected, start their label with **↻** and
a space, using the same font and baseline. Hover any connected input label, including
this symbol, to highlight the source branch without a tooltip.
An output label highlights downstream nodes using that output through active connections.
The starting node, its other outputs and unrelated source branches are excluded.
The same click and drag behavior applies to output labels, including parameters.
Release without moving to select its nodes; **Ctrl-click** adds them to the current selection (**Command-click**
also works on macOS). Dragging from the label moves the node and cancels branch selection.
Connector dots remain dedicated to connections; hovering an unconnected
port's label does not light its dot. Moving away clears
the hover highlight, keeping the selection. A short bar at a highlighted Buffer output marks
a cached source boundary: its source branch is excluded, while UV calculations remain
highlighted. Source parameters and mathematical constants are included.

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

Right-click a node and choose **Replace Node** to select a replacement from the creation
catalog. It opens the current category and subtly highlights the current node. The back
arrow returns to parent categories; search covers the entire catalog. Matching port names and types keep their connections and inline values.
Other connections are removed; the new node uses its own default options and retains the
position, selection and group. One Undo restores the complete replacement. Required Output
and Sub Graph interface nodes cannot be replaced.

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

Enable **SVM Nodes Only** to limit Create Node, Replace Node and connection-drop
menus to nodes supported by SVM with their initial settings. This filter is independent
of **SVM Preview (Experimental)**. Changing settings or connecting an unsupported
branch can still make a preview use HLSL.

**Layer Preview** updates on the next Editor update without a fixed waiting period.
Rapid edits are combined; an available Canvas result is reused. A hidden or collapsed
preview does not render.

Enable **SVM Preview (Experimental)** in the graph toolbar to try the alternative
preview renderer. It applies to supported node previews and the result preview
of a Shader layer in this window. The result still includes the layer's Transform,
FX and clipping. Shader FX result previews, the document canvas and export use
the native renderer; native graph compilation continues in the background.
First use can require a lengthy compilation of the shared SVM shader. Previews
use native HLSL while it becomes ready; this experimental option is off by default.

The prototype supports selected numeric math, UV, Time, numeric/Color parameters,
unfiltered nonperiodic 2D Perlin and Gabor **Value**, Cellular outputs, and the
Fractal modes with filtering and up to two nested Fractal nodes. Other branches,
settings or graphs exceeding the prototype's limits use native HLSL automatically.
Turn the option off to use native previews throughout this window. See the
[supported subset and limits](../ShaderGraph.md#experimental-svm-preview) for details.

**Layer Preview** floats above the graph, initially at the bottom-right. Drag its header or image
to move it and its sides or corners to resize it. Near the viewport edges it snaps flush and stays
attached when the window changes size; near a corner it attaches to both edges. Highlighted borders
show the attachment. Drag away to detach. The window retains placement, attachments and size;
graph zoom and panning do not move it. The header's **R/G/B/A** buttons change only the preview channels.

UV, sampling, generators and image-processing math have previews up to **128 × 128**.

The final image has compilation priority. Waiting node previews nearer the center of the graph view update first; moving the view changes their priority. Graph code preparation runs in the background, and the last working image remains visible while a replacement compiles. A new complex shader can still take time to compile.

Graphs with many expanded previews share preparation work after an edit. Ready images update without refreshing unchanged controls or waiting for another edit delay; values, connections and preview outputs keep the same behavior.

Node previews compile asynchronously and share a limited update budget. Zooming out reduces their resolution to match their screen size; zooming in restores it. Unrelated branch edits reuse existing shaders. Animated node previews update at up to 30 Hz in the focused graph window and 10 Hz otherwise.

While graph changes compile, the layer or FX keeps its last working image. Returning to a recently compiled variant reuses it when available. **Compile**, saving and export wait for the required result; a failed draft retains the working version and its error message.

Initial previews of graphs using built-in nodes are prepared faster, without reducing their resolution or changing the calculations.

**Constant**, **Canvas Data**, **Texture Size**, **Split**, **Compare**, **Output**, **Parameter**
and **Reroute** have no preview; their fields and ports show the useful information.
Nodes without a preview, or with their preview collapsed, have no shared minimum width;
their contents determine the width. Expanding the preview restores its minimum width.
HLSL functions preview their numeric/Color outputs; Boolean and resource outputs are omitted.
If several previewable outputs exist, hover the node and choose one from the selector
inside the preview's bottom-right corner. The list opens upward, aligned to the preview's right edge.
When zoomed in, the whole menu grows proportionally, including checkmarks, text and spacing,
so the options remain at least as large as the selector.

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

## Choose parameter controls

Use these parameter types to make a graph easier to adjust from **Properties**:

| Type | Control and visible effect |
| --- | --- |
| **Enum** | A list of named numeric choices. Give each option its own value, including fractions such as `0.25` or `0.5`; choosing it supplies that value to the graph. |
| **Seed** | An integer field with **Random** for choosing another repeatable variation. Values and range endpoints stay within −16777216…16777216. With **Range** off, Random uses that full interval; both endpoints are included. Saving or reopening retains the chosen value. |
| **Point** | A two-coordinate position, initially `(0.5,0.5)`. `(0,0)` is bottom-left and `(1,1)` top-right; positions outside the canvas are allowed. |
| **Normal** | A three-component direction supplied to the graph with unit length. The default and zero-vector fallback are `(0,0,1)`. |
| **Transform2D** | A reusable frame controlled by position, size and rotation, with canvas editing on the parameter. Connect it to **Apply Transform 2D** to use that frame. |

A **Float** can have a minimum, a maximum, both or neither. Make each enabled limit hard
to keep edits inside it, or soft to suggest a slider range while allowing manual values
beyond it. A cyclic range repeats values between two hard limits: in `0…360`, entering
`370` gives `10`. A single enabled Float limit must be hard; soft limits need both endpoints
with the minimum below the maximum. **Vector2** can remain a free vector or become an interval with two hard
limits. Its cyclic mode allows a selection across the seam, such as `(330,30)`; a complete
turn retains its meeting angle. Vector2 intervals have no soft limits. Seed soft limits
restrict **Random** while manual input can exceed them within the exact-integer range above.

For a movable procedural frame, add **Transform2D** and **Apply Transform 2D**. Connect the
parameter to **Transform**, then use **Local** to feed coordinates into a pattern or sampler.
**Input** maps local coordinates in the opposite direction. Both outputs use the node's
**UV** input, which defaults to current-pixel UV. The node does not clip the image or make a
mask; add a mask when needed. Its transform port cannot connect directly to numeric math.

An unconnected **Transform** shows a compact **Transform** button and starts as identity.
Click the button to open a popup with position/size/rotation fields and **Reset**.
The popup has no canvas button; use the parameter for canvas editing.
Parameter labels, named options, limits and pixel-unit settings stay with the document when
it is copied or reopened.

## Parameters and color

Drag a parameter header within **Parameters** to reorder it, or onto the graph to create a
Parameter node at the drop position. Right-click the header for **Use in Graph**, **Duplicate**
and **Remove**. Duplicate creates an independent parameter with the same settings and value;
Remove also removes its nodes and connections. The indicator on the right matches the shape
and color of the parameter's output port.
Hover over a parameter to highlight its row and all its Parameter nodes without changing selection.

Parameters include numbers, vectors, Boolean values, colors, textures, gradients, curves,
Enum, Seed, Point, Normal and Transform2D.
Multiple Parameter nodes can use the same declared parameter, so one Properties field can control
several parts of the image.

A **Parameter** node is a compact single row with the parameter name and an output connector, without
an **Out** label, an internal preview or a dropdown. To choose another declared parameter, use
the node's context menu; adjust the parameter's value in the layer's **Properties**.

Port labels show the type in parentheses: `1`–`4` for numeric widths, `T2` for textures,
`G` for gradients, `C` for curves, `TR2` for Transform2D, `col` for colors and `bool` for Boolean values.
For example, `UV(2)` and `Color(col)`. The suffix follows the current port type.

Numeric and **Color** ports are circles, **Boolean** ports are diamonds, and **Texture**,
**Gradient**, **Curve** and **Transform2D** ports are squares.

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
Generated functions, branches and loops use four-space indentation, preserved when copying.

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

## Caustics

**Caustics Noise** in **Procedural/Noise** makes a water-light pattern immediately.
**Caustics** in **Filter** turns a connected **Height** into the same kind of pattern: try
**Perlin Noise → Fractal → Caustics** or use a sampled height texture. Both are 2D.

**Caustics Noise** also starts with **Periodic** and noise **Filtering** off. Period defaults to 1 per axis; the separate Wrap setting still controls sampling of the completed image.

- **Strength** controls the height relief; **Depth** controls how far light travels.
  Increasing either tightens the folds and eventually makes them overlap.
- **IOR** controls refraction; **1** removes it. **Light Direction** points toward the
  receiver, with `(0,0,-1)` for overhead light.
- **Softness** softens light footprints; **Intensity** changes brightness. **Value** can
  exceed 1, so preserve HDR when bright peaks matter. A flat surface gives uniform light.
- On **Caustics Noise**, **Scale**, **Period** and **Seed** shape the surface; shift **Position** with **Add**.
  **Phase** animates it in cycles; connect Time for motion.
- **UV** places the finished pattern. **Resolution** defaults to Custom **512 × 512**;
  increasing it sharpens the output, but the surface detail has a finite sampling limit.
- **Wrap = Repeat** tiles the light pattern. For **Caustics**, use a smooth, seamless
  height input as well; wrapping cannot repair a discontinuity in the source.

The result depends on surface curvature: smooth inputs produce flowing folds, while
sharp height discontinuities produce angular shapes. For richer surfaces, put Fractal
before Caustics. Fractal after either caustics node resamples its image and requires
**Seed Step = 0**.
