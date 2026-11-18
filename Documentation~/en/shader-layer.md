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

Identical compilation messages appear once. Expand **Pass details** below the summary
to see which main or Buffer passes reported them, including available source locations.

## Add nodes from the sidebar

Select two or more nodes and choose **Arrange Selected** from the context menu to lay them out along their connections. Other nodes and notes stay in place. One Undo restores the entire selection.

**Color Mode** in the top toolbar colors the strip under node headings. `<None>`
keeps the usual appearance; **Category** distinguishes node families; **Heatmap**
shows estimated local cost from dark purple to bright yellow (cyan means unknown).
Heatmap is not a performance measurement and does not include upstream branches.
**Precision** shows blue for the graph's float calculations. **User Defined** enables
**Color → Change... / Reset** in a node's context menu, including multiple selected
nodes. Custom colors stay in the document; the chosen mode persists between sessions.
Parameter tokens and Reroute points keep their existing appearance.

Switch from **Parameters** to **Nodes** in the left sidebar. Expand a category or
search by node name or category, then drag a node onto the graph. It appears where
you release it, at any zoom level. Click anywhere on a category row to expand or
collapse it. Clicking a node row only selects it.

While dragging, the node follows the pointer with its ports and controls at the canvas zoom. Drop it on the canvas to place it; Esc or dropping outside the canvas cancels. The image preview starts updating after placement.

**Nodes** and **Create Node** use the same category colors and operation icons,
including search results and the menu opened by dragging a connection into empty
space. Category icons use the neutral theme color. Full-height strips at the left
edge of category and node rows match the graph's **Category** palette. The strips
stay at the edge regardless of category nesting. Catalog colors remain visible
regardless of **Color Mode**.

Use **Thumbnails** beside the search field to recognize noise, shapes and patterns by their appearance. **Compact** keeps smaller rows with icons. The choice is shared by **Nodes** and **Create Node** and remembered between sessions. Thumbnails are fixed examples; changing the graph does not change them. Noise examples use a readable scale and contrast; newly created nodes retain their standard defaults. Filter and UV examples use one shared image. Filters change its details and UV nodes change its coordinates. Sample Texture and Sample Input share the mountains and sun from the Texture Size thumbnail. The shared image contains an outlined cyan arrow, a white ring and an orange wedge on a grid, with a grayscale ramp below. Height-driven examples use a separate smooth height map. Color adjustments, mapping, selection and mixing use a separate table of hues and lightness with a grayscale ramp. Height and Normal examples share a relief with a rounded mound, a bevelled block and a diagonal groove, making slopes, shadows and curvature easier to compare. Nodes without an example keep their operation icon and compact row height. Thumbnail rows use tighter vertical spacing.

Fractal shows a shape repeated at several scales; Scatter shows scattered arrows. Range Mask, Step and Smoothstep illustrate range boundaries, One Minus inverts the reference image, Power changes a grayscale ramp, and Blackbody shows a temperature ramp. These are baked results of the actual nodes, with no extra compilation while browsing.

The scroll area uses the exact heights of all listed rows, including offscreen entries. Expanding or collapsing categories and switching display modes update the scrollbar immediately.

Noise Sine Wave, Sawtooth Wave, Square Wave and Triangle Wave show three periods in
the same direction, with signed values mapped to visible brightness. Sphere Mask
shows a soft circular edge; Combine Distance shows a circle joined to a rectangle.
Sample Gradient and Sample Curve use a fixed multicolor gradient and a curve with
a rise and a dip. The examples help identify the operation; their settings are not
the defaults of newly created nodes.

Sample Curve, Power, Smoothstep, Triangle Wave, Range Mask, Step, Sawtooth Wave,
Square Wave, Absolute and Saturate show a thin green curve over their grayscale
thumbnail. Absolute shows a V. Clamp and Saturate show the same sine wave with clipped
peaks: Clamp uses inner limits, while Saturate reaches the image edges, keeping its green line visible.
Step and the discontinuous waves retain sharp jumps. Smoothstep keeps a one-pixel horizontal segment at each
edge so the beginning and end of its smooth transition remain visible at 32×32.

Square Root, Exponential, Log, Soft Sign and Hyperbolic Tangent also show curves.
A muted line marks zero in the two signed examples. Fresnel Effect shows a lit
sphere rim, Distance a radial ramp, and DDX, DDY and FWidth highlight different
edges of one shape. Premultiply darkens the soft edge of a color swatch, and
Unpremultiply restores its color.

Set Alpha shows a color swatch fading to the checkerboard. Convert Color pairs
the original color table with its HSV components displayed as RGB. Sine and Cosine
show one period, and Inverse Soft Sign shows the steepening at the ends. Reflection
and Refract show where the same incoming ray goes at a boundary.

The catalog also illustrates coordinates on a rotated layer, sampled input,
per-instance scatter color, light falloff, vector operations and logic choices.
Rounding examples use a shared scale so their steps can be compared directly.
The logic thumbnails highlight the selected areas of circles. All shows where all
three overlap, and Any fills all three circles.
Is NaN and Is Infinite show NaN and ∞ respectively.
Constant shows π and Value shows 1.0. UV shows a red/green coordinate gradient
inside the rotated rectangle over a muted grid; Canvas Position fills the image with that gradient.
Channel and matrix thumbnails show where components move. Arithmetic uses large operation
signs. For Begin and For End show a circular arrow flanked by curly braces, with the
opening or closing brace highlighted.
Lerp, Inverse Lerp and Remap show two gradient strips connected by a funnel.
Lerp expands the grayscale factor into a color ramp, Inverse Lerp reverses the
diagram, and Remap connects two offset ranges.

Root categories are grouped by color and alphabetically within each color. Procedural follows the red input categories; the remaining colors continue in spectrum order, with gray after them. Sub Graphs and HLSL follow all other categories, with HLSL last. Nodes and Create Node share this order.

The list follows **Create Node**, including graph parameters, HLSL functions and
available Sub Graphs. In debug builds, **SVM Nodes Only** also filters this panel. Use **Refresh HLSL**
after changing the function library. Use **Parameters** to edit declarations and
their controls. Use the **+** at the upper right of the panel to add a parameter.
Edit values in **Inspector**, which shows the same label/value columns and controls
as **Layer Settings**, including parameter groups and resource fields.
Vector components stay on one row. Inspector uses narrower labels to leave more room for values.
The selected tab, search and expanded categories are kept with the window.

The default layout is a full-height panel docked to the left edge. Its upper section
occupies 38% of the height and contains **Parameters** (selected) and **Inspector**;
**Nodes** fills the lower section. **Reset Panel Layout** restores this arrangement.

**Parameters**, **Nodes** and **Inspector** can share a floating panel as tabs or sections,
arranged vertically or horizontally, or occupy separate panels. Drag a tab or section heading out to detach it. Drop a
panel on another panel's header to combine tabs, or near a content edge
to place sections above, below, left or right. The blue target indicates the result. Compact rectangular tabs form the header; the active tab joins the content background without a lower divider. The other tabs and header strip are darker. Separate panels and sections use the same styling. Use the free header space to move the whole group. The **⋮** menu on that row also offers
**Combine as Tabs**, **Combine as Sections**, **Combine Side by Side**, **Detach Group**,
**Separate Tabs** and **Reset Panel Layout**. Arrangement actions affect the selected group.

Drag a tab along its header row to reorder it. A ghost tab follows the pointer while
neighboring tabs make room for it; the target row has a faint blue tint. This also
works when docking a tab from another panel. The order persists;
**Esc** cancels the move. Drag outside the row to detach the tab.

Groups can nest and contain any number of panels. For example, keep two tabs above Inspector,
or place Parameters and Nodes side by side with Inspector spanning the width below them.
Drop near a section's content edge to split that section; drop on a containing group's outer
edge to place the panel alongside the whole group. The blue target shows its scope.
Drag a tab to move only that tab. **Detach Group** keeps its tabs together in a separate window.
Right-click a divider to arrange or detach the group it separates. **Esc** cancels a drag and
restores its previous grouping.

Move a panel by its header and resize it by its edges. Panels and **Layer Preview**
snap flush to the graph area's edges and corners. Resizing to any edge
also anchors it; anchoring opposite edges makes the panel follow the graph area's
height or width. Sections keep their proportions; drag their divider to change the proportion.
Panel placement, grouping and the selected tab persist between sessions.

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

In **Properties**, expand **Shader** above the shader parameters.
**Mode** inside this section selects **Shader Graph** (the default) or **Code**.
Switching keeps the graph, code and each mode's parameter values. It does not convert one into
the other. Save the document to keep both versions and return to either later.

For editable Code, paste or write WhimTex HLSL using `ApplyFX` and `@param`, then click **Compile**.
Linked presets compile automatically.
A new Code version starts as opaque white. Editing the text does not change the image until
compilation succeeds. If compilation fails, the previous applied result and parameter values
remain available; correct the text and compile again. Adjust the accepted parameters in
Properties using the same [shader controls](shader-controls.md) as Shader FX, including previews.

For embedded Code, **Open Code** opens the draft in Unity's selected editor.
Saving updates the draft. Click **Compile** to apply it. **Open in VS Code** opens the same
draft with WhimTex language support and requests compilation on save.

Code generates an image in the layer's local coordinates. The incoming color and `SampleInput`
read the selected **Input**, described below. For additional images, declare Texture parameters
and read them with `SampleTexture`. The
[HLSL contract](../ShaderLayer.md#independent-graph-and-code-modes) describes the interface.

When returning to Shader Graph, your graph and its values are still there. An already open
graph window is read-only while Code is selected; switch back to edit it.

## Reuse a shader preset

In **Shader**, click **▾** beside the empty **Preset** field to open the shared catalog.
For a linked preset, use **⋯ → Change Preset…**. Right-click the field for actions in
either state. The same controls are available in [Shader FX](shader-fx.md). **Save Preset…** saves
Code as `.hlsl` and Graph as `.whimtexshader`; Graph presets retain both modes, reusable
functions, Sub Graphs and captured includes for later editing. Clear Texture parameters'
Layer references in both modes before saving a preset, including inactive selections.

A linked preset follows only its saved ID and exact version. If its source disappears, the
document keeps its copy, including the graph. Double-click **Preset** to open the source.
The link locks **Mode** and the code editor. **Open Graph** shows the graph in read-only mode.
**Embed Copy** in the preset menu enables local editing.
**Clear Preset** clears the field and restores the default program for the current mode.
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

Graph reads this image with **Sampling/Sample Input**. Connect its Color **RGBA** to your
calculation; its **UV** defaults to local coordinates and accepts a distorted coordinate.
Separate **R**, **G**, **B** and **A** outputs read individual channels.
Wrap and Filter work as on Sample Texture. Code uses `SampleInput(uv)` or the incoming
`color` in `ApplyFX`. A missing source produces transparent black. Switching Input or Mode
keeps your source selections.

To process the image below, select **Code**, **Composite Below** and **Crossfade**. Compile `float4 ApplyFX(float4 color) { return color; }`, then add effects to **FX**. Opacity mixes before/after without increasing alpha. In Graph, connect **Sample Input → Output** instead. Pass Through groups include their external backdrop; isolated/clipped groups stop it.

## Create and adjust an image

1. Add a **Shader** layer from **+** at the bottom of Layers.
2. Open **Properties**, select **Shader Graph** in **Mode**, and choose **Edit Graph**.
   When **Shader** is collapsed, **Edit Graph** is available in its header.
   Open graphs remain editable after script recompilation, and their previews refresh automatically.
   Add nodes through **Create Node** in the graph context menu or press **Space**.
3. Connect the calculation to **Output**, which supplies the image's red, green, blue and alpha channels.
4. Declare parameters for values you want to adjust from the layer's **Properties**.
   Use **Parameter** nodes to read them in the graph; **Constant** supplies mathematical constants.
5. Return to the layer's **Properties** to adjust the result without rebuilding the graph.

A new graph starts with a Color parameter connected to Output. Choose opaque red in Properties
to try it. For a value kept inside the graph, disconnect an input and edit its value beside the port.

Ports straddle the inner background edges and remain visible above the selection outline.
Hovering a connected port's connection-drag area thickens its outline outward, preserving the inner opening and connection position. Hovering its label does not highlight it.
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
Press **Enter** to apply the formula and finish editing, or leave the field. **Ctrl+Enter**
inserts a line break without applying. Empty text returns zero. Connections follow variable
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

Use **Input → Value** to set a value directly in the graph. **Type** changes the
editable field and output port. Drag a wire from an input onto empty space and
choose **Value** to create and connect one of the matching type. For matrix inputs
on **Custom Function**, choose the matching size. Edit the matrix row by row
using the **X**, **Y**, **Z** and **W** fields.

**Math → Basic** contains **Add**, **Divide**, **Multiply**, **Power**,
**Square Root** and **Subtract**.

**Math → Interpolation** contains **Inverse Lerp**, **Lerp** and **Smoothstep**.

In **Math → Derivative**, **DDX** and **DDY** measure signed change over one render
pixel horizontally and vertically. **FWidth** adds their magnitudes. Use it to
set the transition width of a procedural mask. Searching for `DDXY` finds **FWidth**.

Use **Math → Matrix** to build a matrix from vectors, read its determinant,
split it into vectors or transpose it. **Matrix Construction** and **Matrix Split**
let you choose **Row** or **Column**. To apply a matrix to UV, connect a 2×2 matrix
to **Multiply.A** and UV to **Multiply.B**.

In **Math → Vector**, **Projection** and **Rejection** split a vector into the
parts along and across another vector. **Fresnel Effect** creates a rim mask
from a normal and a viewing direction. **Sphere Mask** masks points by distance
from **Center**. **Transform** converts positions between **Layer** and **Canvas**.

**Swizzle** rearranges or repeats input channels. Enter 1–4 characters in **Mask** using
`xyzw` or `rgba`; uppercase letters also work. `_` inserts zero.
For a two-component input, `_x_y` produces `(0, x, 0, y)` as a four-component vector.
The mask length sets the output width. Commit with Enter or by leaving the field.
The input uses the larger width required by its source and mask: `y` needs Float2, `z` Float3,
and `w` Float4. Unconnected inputs show that many editable components. Connected scalars repeat
across components; shorter vectors gain zeros. `_` always inserts zero.

## More math and pattern controls

**Sign** separates negative, zero and positive values; **Saturate** limits a value to `0..1`.
Use **Modulo** for repeated numeric intervals. **Math → Range** contains **Clamp**, **Fraction**,
**Maximum**, **Minimum**, **One Minus**, **Random Range**, **Range Mask**, **Remap** and **Saturate**.
**Random Range** returns a repeatable value between **Min** and **Max** from a two-component **Seed**.
Connect **UV** to vary the result across the image.

**Math → Round** contains **Ceiling**, **Floor**, **Round**, **Sign**, **Step** and **Truncate**.
**Truncate** removes the fractional part of each component toward zero. For example, `-1.7` becomes `-1`.

**Math → Wave** contains **Sawtooth Wave**, **Square Wave**, **Triangle Wave** and **Noise Sine Wave**.
The first three repeat every input unit and return signed values. **Noise Sine Wave**
takes cycles and adds noise within the bounds set by **MinMax**; one input unit is one sine cycle.

In **Math → Trigonometry**, **Sine**,
**Cosine**, and **Tangent** take radians. **Arcsine**, **Arccosine**, **Arctangent**, and
**Arctangent2** return radians. **Arctangent2** takes Y and X, and **Arcsine** and
**Arccosine** need values within `-1..1`. **Hyperbolic Sine**, **Hyperbolic Cosine**, and
**Hyperbolic Tangent** apply numeric functions to each component; they do not use angle units.

Use **Convert Angle** in **Math → Trigonometry** to connect controls with different angle units.
Choose **From** and **To** from **Radians**, **Degrees** and **Turns**; the defaults convert
Degrees to Radians. It replaces Degrees To Radians and Radians To Degrees. Scalars and vectors
of up to four components convert without wrapping or clamping; matching units leave values exactly unchanged.
Wires do not convert units automatically. One turn is `360° = 2π rad`; `0.25` turns is a quarter turn.
Negative values and values above one turn are allowed. Labels show `°`, `rad` or `turn`.

Manual orientation stays in degrees for Transform UV, Apply Transform 2D, Shape, Stripes,
Wave's Direction, Kaleidoscope, Gradient Factor, Directional Blur, RGB Shift, grain Direction
and Radial Blur's Spin Angle. Existing phase controls stay in cycles. Evolution has no common
normalized cycle, and vector Direction controls remain vectors.
Existing authored values are not migrated or rescaled; adjust affected values and replace removed
nodes/ports explicitly. See the [angle unit reference](../ShaderLayer.md#angle-unit-contract).

**Log** needs positive values and offers **Base E** (default), **Base 2** and **Base 10**. **Exponential** raises `e`
to the input by default. Choose **Base 2** to raise `2` to the input instead.
Find both in **Math → Advanced**, alongside **Absolute**, **Length**, **Modulo**, **Negate**,
**Normalize**, **Posterize**, **Reciprocal** and **Reciprocal Square Root**.
**Reciprocal** computes `1/x`, and **Reciprocal Square Root** computes `1/√x` for each component.
**Soft Sign** and **Inverse Soft Sign** are also in **Math → Advanced**, with **In = 0**
and output **Out**. They compute `x/(1+abs(x))` and `x/(1-abs(x))` respectively, for each
component of Float through Float4. Color is treated as Float4, including alpha.
Soft Sign compresses signed values toward −1 and 1. Inverse Soft Sign reverses this for
`-1 < x < 1`; at ±1 the denominator is zero. Neither node clamps its input or converts colors.
**Cross Product** returns a 3D perpendicular vector. **Reflection** and **Refract** change an incident
direction around a normal; supply unit vectors. **Refract** takes **IORSource** and **IORMedium**
and outputs **Refracted** and **Intensity**. Beyond the critical angle, **Safe** keeps the tangential
direction with zero Intensity; **Critical Angle** reflects with Intensity `1`.
**Modulo** computes a truncating remainder: `Modulo(-1,3)` is `-1`.
Combine Boolean conditions with **And**, **Or** and **Not** before Select.

Use **Channel → Append** to join A's components followed by B's. The result has up to four
components: A contributes at most three and B fills the remaining places. For example,
joining a two-component UV and a scalar gives a three-component vector.

Use **Logic → Switch** to choose among several values. Connect a number to **Predicate**,
edit the ordered case comparisons and their values, and set **Fallback** for no match.
The first matching case wins; the initial cases are `== 0` and `== 1`. **Floor Predicate**
is on by default, so `1.8` tests as `1`; turn it off for fractional thresholds.
Case values can be scalars, vectors or colors. Removing a case also removes its connection.
An Enum parameter supplies its selected number; set the Switch cases explicitly.

**All** checks whether every input component is nonzero; **Any** checks whether at least one
is nonzero. Negative components count too. **Is NaN** and **Is Infinite** test a single
number for an invalid numeric result or either infinity. Their Boolean outputs can drive
**Select**; split vectors first when testing individual components.

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

Changing connections or the displayed output clears messages from the previous branch,
including collapsed previews. Current errors reappear after checking the new branch;
the last valid layer image remains available while compilation is pending.

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
the required parameters are copied with their values and the containers that hold them,
keeping their nesting and order. A header parameter stays linked when it is also copied;
otherwise that header is cleared. Unrelated groups and empty blocks are not included in a
node selection. Existing destination parameters keep their placement. Layer texture inputs
must be reassigned when pasting into another document. Copying while editing a field copies its text.
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
| **Minimum**, **Maximum**, **Clamp**, **Absolute**, **One Minus**, **Negate** | Limit values, take their magnitude or reverse them. One Minus calculates `1 − x`. |
| **Power**, **Square Root**, **Floor**, **Ceiling**, **Round**, **Fraction** | Shape transitions, create steps or repeat a numeric interval. |
| **Sine**, **Cosine** | Create waves and repeating patterns. |
| **Lerp**, **Remap**, **Step**, **Smoothstep** | Mix values, change ranges and create hard or soft thresholds. |
| **Inverse Lerp** | Find the position of T within A–B: `(T-A)/(B-A)`, without clamping. Defaults: A = 0, B = 1, T = 0. Supports vectors component by component; equal bounds are undefined. |
| **Range Mask** | Select values around Center. Width is the full interval, including inward Feather on each edge. Falloff selects Linear or Smooth. Input=Components processes vector components independently; Input=Luminance selects brightness and returns one Float mask. |
| **Length**, **Distance**, **Dot Product**, **Normalize** | Calculate distances and directions. |
| **Logic → Compare**, **Select** | Compare two numbers and use the condition to choose between two values. |
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
| **RGB Shift** | Shift channels of a texture or procedural color branch. Radial grows outward from Center with Falloff. Directional shifts red and blue along Angle. Triangle shifts all three channels equally in directions 120° apart, and Angle rotates the triangle. Amount uses canvas pixels. Blend mixes with the original while preserving alpha and HDR values. The selected mode shows its own controls. Connected inactive ports stay dimmed. |
| **Height Blend** | Combine height layers and reuse the blend mask for their colors. |
| **Height Curvature**, **Height to AO**, **Height Shadow** | Select raised or recessed detail and shade a height field with local occlusion or directional shadows. |
| **Palette Quantize** | Reduce colors to candidates sampled from a gradient palette. |
| **Supersample**, **Make Tileable** | Smooth subpixel detail or blend a calculation into a repeating tile. |
| **Flow Warp** | Move sampling coordinates along a vector field. |
| **Median**, **Kuwahara** | Remove isolated specks or simplify detail into locally smooth regions. |
| **Normal Curvature** | Select convex and concave detail from a normal calculation. |
| **Truchet** | Generate a seeded tile pattern of arcs or lines. |
| **Tile UV**, **Perspective UV** | Place repeated motifs in cells or map an image into a quadrilateral. |
| **Scatter**, **Scatter Instance** | Scatter overlapping motifs and vary their source for each instance. |
| **Gabor Noise**, **Rotate About Axis**, **Blackbody** | Create directional texture detail, rotate vectors or generate temperature colors. |
| **Slope Blur**, **Bilateral Blur** | Smear along a height slope or soften detail while preserving guide edges. |
| **Height to Normal**, **Normal Blend**, **Height to Distortion**, **Normal to Distortion** | Turn a texture or height calculation into normals or displacement and combine normal detail. |
| **Blend** | Composite two colors with a blend mode and alpha. The Mode menu uses the same names, order, and groups as the layer blend selector. Mix Color interpolates colors, and Lerp interpolates numeric components. |
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
distance from Center. Connected inactive controls remain dimmed. Sampling density is automatic:
**Quality: Standard** spaces samples at most one document pixel apart; **High** halves that
spacing over the same path. Radial Blur adds samples farther from Center. High reduces sparse
trails on thin detail, but costs more. All RGBA channels filter independently, retaining HDR.
Use explicit Premultiply/Unpremultiply for alpha-weighted averaging. Long blurs of a complex
procedural branch can be expensive; an explicit **Buffer** lets them sample its baked image instead.

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
Sample Input, Sample Gradient and Sample Texture provide **RGBA** and separate scalar **R**, **G**,
**B**, **A** outputs. Existing full-color connections continue to use RGBA.

Unconnected numeric inputs have visible, editable fields outside the node body, to the
left of their ports. The ports stay unobstructed for wiring. Connecting a wire hides the field;
disconnecting it brings the field back. Unconnected UV ports keep implicit current-pixel
coordinates (`PixelUV`): use the local **Custom / UV** option to enter coordinates or use UV.
Right-click an unconnected input field and choose **Reset** to restore that port's default.
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
sets a random angle range in turns; **RandomScale** controls scale variation from `0` to `1`.
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
**Phase Offset=0** moves the bands in **Value** and **Phasor** in cycles. Adding 1 repeats the
pattern. **Phase**, **Gradient** and **Curl** follow the shift. **Intensity** measures signal amplitude.
**Phasor** produces more even-contrast bands. Choose **Sine**, **Triangle** or **Pulse** in **Profile**.
**Width=0.5** controls Pulse's bright fraction, from black at 0 to white at 1. Width hides outside Pulse unless connected,
in which case it remains visible but inactive. Values and wires survive profile changes.
Filtering smooths small bands toward the profile average.
True phase singularities remain, with fading near signal zeros inside a pixel.
**Gradient** and 2D/3D **Curl** retain signed strength. Sampled filters can make this noise expensive.

**Rotate About Axis**, under **Math → Vector**, rotates the three-component **In** around **Axis**
by **Rotation**, retaining its length. **Unit** selects **Radians** (default), **Degrees** or **Turns**.
Set a nonzero Axis, such as `(0,0,1)` for UV rotation. In, Axis and Rotation start at `0`.
To rotate around a chosen center, subtract the center before rotation and add it back afterward.

**Blackbody**, under **Color/Generation**, produces **Out**, an approximate linear RGB temperature color with
alpha `1`. **Temperature** starts at `6500` Kelvin and is limited to `1000..40000`: lower values
give warm reds/oranges and higher values give cool blues. Adjust brightness separately with
Multiply or Exposure; temperature sets the normalized color.

## Scatter motifs

Choose **Procedural/Scatter → Scatter** to spread shapes, procedural detail or sampled
images across a grid. Connect the motif to **Source**, then connect Scatter's result to
**Output**. Source is evaluated within each instance's local `0..1` square, and instances
can overlap neighboring cells. An unconnected Source is a constant `1`.

**Count** starts at `(8,8)` cells per UV unit. **Size** starts at `(0.8,0.8)` cell widths;
**SizeRandom = 0.25** adds random shrinking. **Rotation = 0** sets the base angle in turns and
**RotationRandom = 0.5** adds up to ±0.5 turns. **Jitter = 1** lets centers move across
the whole cell; set it to `0` to align them. **Seed = 1337** makes the arrangement repeatable.

Connect fields to **Density**, **Mask**, **ScaleMap** or **RotationMap** to control whole
instances from their centers. Density is the chance of keeping an instance; Mask weights
its contribution; ScaleMap shrinks it; RotationMap adds turns. Their defaults are
`1`, `1`, `1` and `0`. Use values from `0..1` for the first three; SizeRandom also only shrinks.
In **Mode**, choose **Max** for strongest contributions over a zero background, **Add**
to accumulate values, or **Alpha Over** to composite transparent colors. Max is the default.

Add **Scatter Instance** to vary the source: **UV** gives its local coordinates,
**Center** its placement, **Index** a stable instance ID and **Random** a value from `0..1`.
For example, connect Random to a source noise seed or use it to sample a color gradient.
Noise seeds do not vary automatically. These outputs work in Source and the four
center-sampled controls; nested Scatter uses the innermost instance. Source-node previews
show an example instance; using the outputs outside Scatter reports a diagnostic.

Enable the bottom **Periodic** checkbox and leave **Period = (1,1)** to repeat over
the UV square. Count then describes cells per Period, and Center wraps into that period.
Periodic starts off; disabling it retains the Period value and connection.
Buffer before Source repeats a fixed image and cannot bake live per-instance variation;
Buffer after Scatter stores the completed pattern. Scatter uses HLSL fallback in SVM
previews. See the [Scatter reference](../ShaderLayer.md#scatter-and-scatter-instance) for bounds and exact types.

## Build procedural patterns and distortions

Find these nodes in the following nested **Create Node** categories. The same branches appear
when creating a node from a dragged connection:

| Category | Nodes |
| --- | --- |
| **Procedural/Noise** | Perlin Noise, Value Noise, Value Cubic Noise, OpenSimplex2 Noise, OpenSimplex2S Noise, Flow Noise, Caustics Noise, Cellular Noise, Triangle Voronoi Noise, Gabor Noise, Spots Noise, Shard Noise. |
| **Procedural/Noise/Warp** | OpenSimplex2 Warp Noise, OpenSimplex2 Reduced Warp Noise, Basic Grid Warp Noise. |
| **Procedural/Noise/Grain** | White Noise, Blue Noise. |
| **Math/Random** | Random. |
| **Sampling** | Buffer. |
| **Procedural** | Fractal. |
| **Procedural/Scatter** | Scatter, Scatter Instance. |
| **Procedural/Pattern** | Checkerboard, Stripes, Grid, Rings, Gradient Factor. |
| **Procedural/Shape** | Shape, Combine Distance. |
| **UV** | Transform UV, Apply Transform 2D, Polar Coordinates, Repeat, Mirror, Kaleidoscope. |
| **UV/Distortion** | Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple. |

Noise nodes use the shared WT library. Choose **Dimensions**: **1D** for a line,
**2D** (default) for a plane, or **3D** for a volume. Unconnected **Position** uses the
pixel's UV, its X component in 1D, or UV plus an editable **Z** (default 0) in 3D.
In 3D, the UV selector occupies X/Y; Z remains a native numeric field with label dragging.
Switching UV/Custom or Dimensions preserves Z. **Scale=8** controls density per
axis. **Periodic** is off by default. Enable it to expose **Offset=0** and **Period=1**
per axis. Offset shifts the pattern in Position units, before Scale and repetition.
Positive Period repeats the field; nonpositive values disable an axis. When Periodic is off,
use **Add** before **Position** to shift the pattern. Unconnected Offset/Period ports hide;
connected ones stay visible but inactive, retaining their values and connections.
**Seed=1337** selects the repeatable variation.
Changing Dimensions retains values and all connections. A connected Curl output stays visible but inactive in 1D.
**Filtering**, disabled by default, reduces unresolved detail, including inside sampled filters.
**Triangle Voronoi Noise** and **Caustics Noise** are fixed to **2D** and have no Dimensions selector; their position and domain controls stay two-component.

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

The passes that fill Buffer and the shader that reads it compile separately. Unchanged
compiled programs are reused, so editing one side does not by itself recompile the other.
Changes to inputs or time still redraw affected buffers, even when no shader needs
recompilation. This applies to Buffer nodes you add explicitly; no buffers are inserted
automatically and no new node settings are needed.

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

**Shape** offers Ellipse, Rectangle, Rounded Rectangle, Polygon, Star, Capsule, Line Segment
and Rounded Polygon.
Its **Out** is a smooth mask; **Distance** is negative inside and positive outside the shape.
Polygon and Star keep a uniform distance and Feather when stretched along either axis.
Connect two Distance outputs to **Combine Distance**, then use its **Mask** as coverage.
Choose Union, Intersection or Subtract (A minus B); Smoothness blends the join, while Feather
softens the mask edge. Capsule uses Center, Size and Rotation; Line Segment uses Start, End
and Width. Switching Shape mode replaces its controls and removes connections to absent inputs.
The ellipse distance is approximate, so distance-based borders can vary in width.

Choose **Rounded Polygon** for a polygon with softened corners. **Sides** sets the number
of sides and **Roundness** runs from sharp corners at 0 to a circular profile at 1.
Start with Sides = 5 and Roundness = 0.3; use Size, Center and Rotation to place it, and
Feather to soften its mask. Nonuniform Size stretches the profile. Its distance is approximate,
so distance-based borders can vary in width here too.

For a colored gradient, connect **Gradient Factor.Out → Sample Gradient.T** and a **Gradient**
parameter to **Sample Gradient.Gradient**; connect the sampled color to **Output**.
Checkerboard, Stripes, Grid, Rings and shape masks have smoothed edges.
**Stripes**, **Rings**, **Wave** and **Ripple** have a separate **Phase** input to shift the
pattern in cycles. Drive it with a parameter, or connect **Time** to animate it in the graph preview.
Phase repeats every cycle, including negative values. Grid's Offset repeats every unit per axis;
Checkerboard's repeats every two units per axis. Large offsets retain clean pattern fills and edges.
Stripes offers Square, Triangle and Sine profiles; Width affects Square only.
Pattern/grain orientation stays in degrees; Gabor/Spots Direction is a vector.
**Twirl.Strength** uses turns and starts at `0.25`. The full signed value controls the radial
falloff: a whole turn still twists the interior, so do not reduce Strength to its fractional part.

Connect a UV node's **Out** to the next pattern or **Sample Texture.UV**.
**Repeat.Cell** contains cell coordinates, including negative coordinates; **Input/Instance Index**
uses a separate index (currently zero) and is unrelated to it. **OpenSimplex2 Warp Noise.Displacement** exposes the offset;
its **Position** output supplies the displaced coordinates. Distortions leave coordinates unchanged at **Strength = 0**
(Radial Shear also needs zero Offset);
displacement vectors keep their length. UV can extend beyond `0..1`, so choose the desired
wrap setting on Sample Texture. Transform UV changes the coordinates within the graph,
independently of the layer's Transform.

**Vector Displacement** accepts **Signed RG** vectors directly or decodes **Packed RG** from
`0..1`, where `(0.5,0.5)` is neutral. Strength scales X and Y separately.
Positive **Bulge / Pinch** Strength magnifies the sampled image; negative values pinch it.
**Radial Shear** bends coordinates around **Center**, increasingly toward the outer area.
**Strength** controls X and Y separately; **Offset** shifts the result. Start from
Strength = (10,10) and Offset = (0,0), then connect Out to a pattern or Sample Texture.UV.
**Polar Coordinates** maps to angle in turns and twice the distance from Center, or back to UV.
Its **Angle** offset also uses turns in both directions.
**Mirror** folds selected axes onto the positive side of Center; **Kaleidoscope** repeats radial sectors.
See the [procedural node contract](../ShaderLayer.md#procedural-and-uv-nodes) for exact ports and modes.

## Follow a flow field and build Truchet patterns

**Flow Warp** bends sampling coordinates along a vector field. Connect a Float2 calculation
to **Flow**, then connect **Out** to a texture or pattern's UV input. **UV** starts at the
current pixel. **Strength = 16** document pixels scales the motion; negative values reverse
it. Flow is not normalized, so longer vectors move farther. The default Flow `(0,0)` and
zero Strength leave UV unchanged.

**Steps = 8** rounds to a whole number in 1–32. Each step reads Flow twice, using the
midpoint direction to advance along the field. More steps can follow changing directions
more closely, at greater cost. Keep Flow dependent on UV so the path sees the field change.

**Truchet** creates a mask of randomly oriented tile motifs. Connect **Out** to a color
blend factor or alpha. **Scale = (8,8)** controls cell density, **Width = 0.1** controls line
thickness in cell units, and **Seed = 1337** chooses the arrangement. Use **Arcs** (default)
for curved paths or **Lines** for straight motifs. **UV** defaults to the current pixel;
scaling or moving UV changes the portion of the pattern you see. The bottom **Shape**
selector switches the motif. Edge smoothing follows the rendered pixel size and transformed UV; zero or negative
Width gives an empty mask. **Feather = 0** keeps pixel antialiasing; positive values widen
the soft transition in cell units, centered on the same boundary. **Distance** returns the
signed field before smoothing: negative inside the line, zero at its edge, positive outside.
Feather does not affect Distance. Cell orientations are hashed across the plane, with no Period
control or guaranteed repetition over the unit UV square.

## Give a height map apparent depth

Add **UV/Distortion/Parallax Mapping** for a small shift by height, or **Parallax Occlusion
Mapping** when nearer features should hide farther ones. Connect **Out** directly to
**Sample Texture.UV**, using the same coordinates for the surface's color and normal maps.
These nodes change sampling coordinates; they do not move geometry or write depth.

Keep **Source: Texture** to supply a height texture and choose its **Channel**, **Wrap** and
**Filter**. Choose **Source: Value** for a procedural height branch. A height of 0.5 is the
reference plane. **View Direction** defaults to `(0.5,0.5,1)`; change X/Y to tilt the view.
The direction is normalized, and `(0,0,1)` gives no shift. Zero or back-facing directions
also leave the coordinates unchanged.

**Amplitude**, initially `0.05`, controls the relief in UV units. Parallax Mapping reads the
height once. Parallax Occlusion Mapping searches along the viewing ray with **Steps = 16**
initially; steps round to a whole number between 1 and 64. This is the minimum search
count: long UV displacements automatically increase it, up to 64. Some final refinement
work moves into the denser search to reduce missed relief features. More Steps still help
with narrow details, at greater cost; thin features can still fall between samples.
POM bounds height to 0–1; the simpler node permits values beyond
that range. Neither clamps the output UV, so set the final image sampler's Wrap as needed.
If the height branch produces an invalid number, that sample uses the neutral height 0.5.
Invalid direction or amplitude, or an overflowing displacement, leaves the UV unchanged.

In Value mode, POM repeatedly evaluates the height branch. An explicit **Buffer** before a
costly branch's Height connection can trade repeated calculations for texture sampling.
Both nodes support SVM previews within its existing limits; other branches use HLSL.

## Select and shade height detail

Connect a scalar height calculation to **Height** on **Height Curvature**, **Height to AO**
or **Height Shadow**. For a height texture, sample it first and connect the desired channel.
**UV** selects where to evaluate the height field; leave it unconnected for the current pixel.
The nodes sample the connected calculation at nearby positions, so its coordinates must
follow UV for spatial detail to appear.

Height defaults to 0. Pixel distances and height scales use document pixels, so their
size stays consistent between node previews and the full layer render.

**Height Curvature** separates raised and recessed detail. **Sample Distance = 2** pixels sets the
neighbor distance, and **Strength = 8** controls the response. **Convex** is a 0–1 mask for
heights above their four-neighbor average; **Concave** selects heights below it. **Curvature**
keeps the signed response, positive for raised detail and negative for depressions. Use the
masks to blend edge highlights, wear or cavity colors. This is a local height comparison,
so its appearance changes with Sample Distance and the scale of the source detail.

**Height to AO** produces **Out**, a visibility mask: 1 is unoccluded and 0 is fully occluded.
Multiply it into surface color to darken recesses. It estimates the surrounding height horizon
relative to the local tangent, so a flat ramp stays lit.

| Control | Visible effect |
| --- | --- |
| **Radius = 16** pixels | How far surrounding heights can contribute to occlusion. |
| **Height Scale = 16** pixels | Relief height represented by one unit of the input height. |
| **Strength = 1** | Amount of occlusion darkening. |
| **Bias = 0.05** pixels | Height tolerance that helps avoid self-darkening. |
| **Directions = 8**, range 1–16 | Number of directions inspected around each pixel. |
| **Steps = 4**, range 1–16 | Number of height samples along each direction. |

**Height Shadow** returns **Out**, with 1 lit and 0 shadowed. **Light Direction** starts at
`(0.5,0.5,1)`; change X/Y to change the light direction across the height field. **Height Scale = 16**
pixels sets relief height, and **Distance = 64** pixels sets the shadow search reach.
**Steps = 32**, in 1–64, controls the search detail. **Bias = 0.05** pixels helps avoid
self-shadowing; **Softness = 0.1** is an angular softness control, not a blur radius.
Multiply Out into the surface color or use it as a lighting blend mask.

AO and shadows approximate a height field, so thin blockers can fall between samples.
More Directions or Steps increase cost, especially with complex height calculations.
These nodes support HLSL and SVM previews; branches beyond SVM's evaluation limits use HLSL.

## Adjust color and transparency

To recolor a texture, connect **Sample Texture → Hue → Saturation → Output**. Find the
adjustments under **Color/Adjustments** in Create Node. Start with **Offset = 0** turns and
**Amount = 1**, then change the hue angle or reduce saturation toward zero for grayscale.
Hue's **Offset** replaces the Degrees input; `1` is a full hue turn.
These adjustments preserve the image's transparency.

| Controls | Visible effect |
| --- | --- |
| **Exposure / Stops** | Brightens or darkens linear RGB; +1 doubles it, −1 halves it. |
| **Lightness / Amount** | Adjusts lightness from −100 to 100; 0 leaves it unchanged. |
| **Contrast / Contrast** | Changes contrast around middle gray in linear RGB (about 0.218); 1 leaves it unchanged. |
| **Levels / Input Range, Gamma, Output Range** | Sets black/white points and midtones. Luminance adjusts brightness together; RGB adjusts channels separately. Disable the bottom **Clamp Input** checkbox to keep values beyond Input Range. |
| **Color Balance / Shadows, Midtones, Highlights** | Tints different tonal regions. Range controls adjust their reach; Preserve Luminance retains brightness. |
| **Posterize / Levels** | Reduces smooth shading to steps; at least two levels are used. |

**Replace Color** changes RGB near **From** to **To**. Increase **Range** to include more
colors and **Fuzziness** to soften the transition back to the original. The image's alpha
stays unchanged; From and To alpha do not affect the replacement. **Mask** exposes the
selected area and matches Color Mask with the same color, key and settings.

For a dissolve, connect a noise or mask to **Filter → Fade Transition.NoiseValue**.
Raise **FadeValue** from 0 to 1 to reveal a 0–1 mask, and increase **FadeContrast** to narrow
its transition. This endpoint behavior assumes a source mask in 0–1 and nonnegative contrast.
Connect Out to a blend factor or **Set Alpha.Alpha**; Multiply retains the image's transparency.

For a palette-driven result, add **Color/Mapping/Gradient Map**. Connect the image to Color
and a **Gradient** parameter to Gradient, then choose Channel. Luminance maps dark-to-bright
tones across the gradient. The default Preserve mode keeps the image's alpha; Multiply combines
it with gradient alpha, and Replace uses gradient alpha alone.

**Palette Quantize** reduces an image to a palette while preserving its original alpha.
Connect the image to **Color** and a Gradient to **Palette**. **Colors = 8**, in 1–64,
sets the uniform candidate count in **Palette: Samples** (default). Switch to **Gradient Keys**
to use only authored stop colors, including closely spaced stops. **Colors** is then inactive;
its stored value and connection are kept. Interpolation and alpha stops do not add candidates. **Distance** defaults to **Perceptual**, comparing perceived
color differences. **Linear** compares linear RGB components. Switching modes changes which palette color
wins without changing the palette itself.
The **Out** port returns Color; palette alpha does not replace source transparency. With
Colors set to 1, the only candidate is the gradient's start color. Use fewer candidates for
larger color steps, or adjust the gradient to change the available colors.

Use **Color Curves** to reshape tones with Curve parameters. Master affects RGB together or
luminance, then Red, Green and Blue refine individual channels. Leave a curve unassigned for
no change. Assign it inline or connect a Curve parameter. Alpha changes only through its own curve. Identity curves retain values above
white and below black.

To select a color, add **Color/Selection/Color Mask** and choose Key Color. Both this node
and Replace Color use **Range = 0.1** for the fully selected radius and **Fuzziness = 0.05**
for the outer linear fade. Fuzziness = 0 gives a hard edge; negative values act as zero.
Neither control has an upper limit. **Distance** defaults to **Perceptual**; **Linear**
compares linear RGB, and **Chroma** ignores perceptual lightness while still distinguishing
hue and saturation. **Hue Range** selects a hue interval in turns; the default **Range = (11/12,1/12)**
crosses red. **Feather = 1/24** turns softens its edges. A distance of at least one turn between
the endpoints selects every hue; equal endpoints select nothing unless Feather is positive.
Saturation Range excludes or fades near-gray colors even when every hue is selected.
Both nodes output a mask with white for selected colors. They do not apply the image's alpha.
Connect the mask to **Color/Alpha/Set Alpha.Alpha**: Replace uses it directly; Multiply also
keeps the source transparency. Set Alpha leaves RGB unchanged.

**Color/Mixing/Mix Color** blends A and B by T, with Perceptual selected initially. Linear
and Gamma give different intermediate colors. T = 0 gives A and T = 1 gives B; values outside
that range extrapolate. Alpha is interpolated too. Use **Blend** for alpha compositing.

Color connections perform no automatic color-space conversion. **Color/Conversion/Convert Color**
offers Linear, Gamma, HSV, HSL or Perceptual; convert back to Linear before ordinary
color processing or Output. HSV and HSL use sRGB-encoded RGB with hue measured in turns.
An unconnected Convert Color input shows a color picker for Linear and raw Float4 fields for other spaces.
HSV loses negative RGB but keeps HDR brightness; HSL limits values to `0..1` and loses both
HDR and negative RGB. Linear RGB/sRGB/Oklab retain them. **Luminance** extracts a brightness value from linear RGB.
Color adjustments generally retain HDR and negative values. Levels has its explicit Clamp Input switch;
Set Alpha does not clamp transparency. Use Clamp where needed and choose the desired color range
in the layer's Rendering settings.

## Choose a channel for height and masks

**Channel** selects **Luminance**, **Red**, **Green**, **Blue** or **Alpha** before a
color/vector input becomes height or a mask. Luminance is the default and uses Linear RGB;
alpha does not affect luminance. A scalar noise or mask passes through unchanged, even
when Alpha is selected. Float2/3 inputs use zero for missing RGB components and one for
missing alpha. There is no extra clamp or Gamma conversion.

This applies to Height to Normal/Distortion and both Parallax nodes with **Texture or Value**,
Height Curvature/AO/Shadow, Caustics, lighting Height, Slope Blur's Slope, Height Blend's
Bottom/Top/Opacity, Fade Transition's NoiseValue, Scatter's Density/Mask, Set Alpha's Alpha,
Mix Color's T and Blend's Opacity. One Channel setting applies to these inputs on the node;
use Split upstream when separate inputs need different channels. Other numeric controls
and ordinary math conversions keep their existing component rules.

## Build normals and displacement

For a normal map, add **Normal/Height to Normal**. Keep **Source: Texture** to connect
a Texture parameter directly to Height and choose its Channel. Sample Distance measures source texture
texels in this mode; Wrap and Filter control sampling at edges.
Choose **Source: Value** to connect a noise, mask or other scalar height calculation.
The node reads that calculation at neighboring coordinates, with Sample Distance measured in document pixels,
independent of preview size.
This also works on **Height to Distortion**. Adjust Strength for relief and Sample Distance for the
sampling distance. Both sources support Scharr, Sobel and Central Difference derivatives;
Flip X and Flip Y reverse the slope direction. A branch with fixed coordinates has no varying height.
Switching **Source: Texture / Value** retains both sources' connections and assigned values.
A connected inactive port stays visible and dimmed; switching back restores its contribution.
Both Parallax nodes follow the same rule.

On Height to Normal, **Flip X** and **Flip Y** are checkboxes in the node's lower settings.
An existing connected Flip port remains visible until disconnected; its checkbox is disabled while connected.

Normal nodes use **Signed XYZ**, with flat normal `(0,0,1)`. Connect them directly;
there is no Encoding selector. Preview shows the actual XYZ as RGB, without packing:
a flat normal looks blue, and negative channels look black while retaining their values
in calculations. Use **Normal Encode** explicitly for a packed normal map or packed visualization.
Add **Normal Blend** to combine Base and Detail using Reoriented or Whiteout, and adjust Strength
for the detail. Its output is a normalized signed XYZ vector.

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

**Normal Reconstruct Z** builds a signed XYZ normal from signed XY, with nonnegative Z.
Use **Normal Unpack → Tangent** for a sampled imported normal map, retaining all four sampled
channels. **Object** decodes ordinary RGB from 0–1 to signed XYZ without changing coordinate
space. Both return signed normals ready for downstream normal nodes.

**Normal Encode** converts signed normals to packed RGB; **Normal Decode** converts them back.
They change representation without normalizing. **Normal Strength** flattens detail at 0,
preserves a unit normal at 1 and reverses XY with negative values.
To shade the result, connect it to **Normal Lighting.Normal**, and choose
the surface Color, LightDirection, LightColor, Intensity and Ambient. Raise **SpecularIntensity**
from its default 0 to add a highlight; SpecularColor tints it, Roughness widens it, and
SpecularOnly shows only that contribution. Surface alpha is preserved.

**Normal Curvature** selects raised and recessed detail from a normal field. Connect a
signed Float3 normal calculation to **Normal**. The default `(0,0,1)` is flat.
**Sample Distance = 1** document pixel sets the neighbor distance, **Strength = 1** scales the response,
and **UV** defaults to the current pixel. **Convex** and **Concave** provide 0–1 masks;
**Curvature** keeps the signed response. Use the masks for edge highlights or cavity color.
The node normalizes four nearby Normal samples before comparing their XY
changes, so constant normals contain no curvature detail.

**Premultiply** multiplies RGB by alpha; **Unpremultiply** restores straight RGB for nonzero
alpha and returns black RGB at zero alpha. Alpha itself stays unchanged. The layer's Output
expects straight color, so use Unpremultiply after calculations that produce premultiplied color.
For alpha-weighted averaging, use **Premultiply → Blur → Unpremultiply**. Without these nodes,
filters process RGBA as four independent components. An intermediate Multiply by 1 or Reroute
does not change this behavior.

## Filter a calculation

Inputs that repeatedly evaluate their source, even when unconnected, start their label with **↻** and
a space, using the same font and baseline. Hover any connected input label, including
this symbol, to highlight the source branch without a tooltip.
An output label highlights downstream nodes using that output through active connections.
Buffer and Caustics show a storage-cylinder icon after their output label,
between the text and connector, even when unconnected. It marks a value read from an intermediate texture
and shares the label's hover, selection and drag handling.
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
and **Dilate**/**Erode** expand or shrink bright areas. All components, including alpha, are processed independently. To average transparent artwork
without hidden RGB bleeding into it, connect **Premultiply → Blur → Unpremultiply**.
Use a scalar mask for one shared contour.

Radius sets the area in document pixels for Blur, ordinary Sharpen, Dilate and Erode.
**Quality** defaults to **Standard**: at least **3 × 3**, with more samples at larger radii
to keep their spacing within one pixel. **High** halves the spacing over the same area,
retaining the Standard sample positions and adding points between them. It can capture
finer procedural details, at greater cost. Radius 0 preserves the input. Edge Detection uses a fixed
3 × 3 Sobel kernel; its **Sample Distance** sets the spacing in document pixels. Strength adjusts Sharpen and Edge Detection. Results can exceed `0..1`;
use Clamp or Saturate if you need a bounded mask.

On **Sharpen**, enable the bottom **Adaptive** checkbox to use a fixed 3 × 3 neighborhood with a
response adjusted by local contrast. **Sample Distance** sets sample spacing and **Strength**
sets the sharpening amount. Adaptive hides and ignores Quality, retaining the selection for ordinary Sharpen. The setting starts
off, retaining the ordinary sharpening path. It bounds the correction to the local
component range, including alpha.

**Median** removes isolated bright or dark specks by taking the middle value in the
complete square around the current pixel, independently for every component. Connect
numeric or Color **In**; **Out** keeps its type, including signed and HDR values.
**Radius** uses document pixels, rounded down to a nonnegative whole number: 0 keeps
the input, 1 covers 3 × 3 pixels, 2 covers 5 × 5, and 8 covers 17 × 17. Increasing
Radius includes all intervening pixels. Fine lines and small features can disappear.
Radius can also come from Value, a parameter, Time or a supported math chain. When it is the same across the image, filtering can retain its accelerated path; pixel-dependent radii still work with direct evaluation.

**Kuwahara** simplifies detail into locally smooth regions while favoring quieter sides
of an edge. Connect numeric or Color **In** and use **Out** of the same type.
Choose **Classic** (default) or **Anisotropic**.

**Classic** uses four square regions. **Radius = 2**
document pixels sets the neighborhood extent. Fractional Radius rounds down to whole pixels;
negative values act as zero, and zero leaves the input unchanged. The node samples four dense quadrants and
returns the average from the quadrant with the lowest variance. It compares the average
variance of all active components, including alpha for Float4. Increasing Radius covers
more source points and simplifies detail over a larger area.

**High Precision** is available only in Classic and starts off. Above Radius 5, with unconnected Radius and standard
current-pixel UV (not Value or a connection), the filter
can use a faster calculation. Enable High Precision if accumulation artifacts appear:
it forces direct calculation of the same filter, which can be much slower at large Radius.
Small radii, connected Radius/UV, Value UV and branches evaluated repeatedly already use direct
calculation, so the checkbox has no additional effect there. There is no separate Samples
or Density setting.

The prepared result uses document resolution, interpolates between pixel centers and
holds the nearest edge value outside the canvas. Edge neighborhoods still include actual
input samples beyond the canvas. This rasterized result can differ from evaluating the
procedural input at arbitrary coordinates, especially when zooming or transforming it.

**Anisotropic** follows local contours with an oriented elliptical neighborhood, creating
more directional, brush-like smoothing. **Radius** sets the neighborhood size and retains
fractional pixel values in this mode; it is not rounded down as in Classic.

| Input | Default | Effect |
| --- | --- | --- |
| **Sharpness** | 1 | In 0..1, controls how strongly quieter regions are favored. Higher values make the selection sharper. |
| **Uniformity** | 4 pixels | Smooths the local direction field over a wider area. Rounds down to whole pixels; zero leaves directions unsmoothed. |
| **Eccentricity** | 1 | In 0..2, controls elongation along the local direction. Larger values allow more stretched regions; zero is nearly circular. |

These three inputs are active only in Anisotropic; High Precision is not used in this mode.
Connected controls and branches evaluated repeatedly can require more computation.
Every active component, including alpha, is filtered directly.

Median and Kuwahara both take **UV**, defaulting to the current pixel. Every component, including alpha, is processed directly without automatic alpha weighting.

Filters read the connected calculation at neighboring coordinates. Keep the upstream pattern
dependent on UV; fixed coordinates produce a constant result. Large radii, especially in chained
filters, can slow previews. Radius uses the same document scale in node previews and full layer renders.
Radius can vary across the image; built-in shapes and patterns retain antialiasing inside
the filter. At zero Radius, Blur, ordinary Sharpen, Dilate and Erode preserve the original input.

**Slope Blur** smears **In** downhill along a scalar **Slope** field. Connect a height or noise
branch to Slope; its default `0` leaves the image unchanged. **Strength** defaults to `8`;
negative values reverse the path. Steeper slopes travel farther because the gradient is not
normalized. **Steps** defaults to `16` and rounds to `1..64`; more steps follow the path more
closely. **Average** softens the path, **Min** retains darker values and **Max** retains brighter
values. Average, Min and Max process every component, including alpha.
Zero Strength preserves In. This filter has no Radius control.

**Bilateral Blur** softens detail while retaining edges selected by a guide. **Radius** defaults
to `2` document pixels. **Tolerance** defaults to `0.1`: smaller values preserve more guide
differences; larger values allow more smoothing across them. It cannot be negative, and `0`
accepts only exact guide matches. **Guide = Source** uses In itself; **Custom** adds a numeric
or Color **Guide** input for an independent pattern, defaulting to scalar `0`. Both modes compare all active Guide components directly, including hidden RGB and alpha.
Guide does not change the output type: it follows In. All output components are filtered
independently, and zero Radius preserves In.

**Quality = Standard** is the default, with spacing at most one document pixel.
**High** halves spacing in both axes over the same area, preserving Standard sample positions.
At Radius=8 the grid grows from 289 to 1089 samples. High can improve fine detail but costs
roughly four times as many input evaluations at large radii. It does not change edge tolerance.

Both nodes keep the input's numeric width; Color values use Float4. Slope Blur repeatedly samples both the image
and its slope; Bilateral Blur reads both image and guide. High Steps, large Radius and chains
of filters can slow previews, especially when their inputs contain Gabor Noise.
The first compilation of a deeply nested filter chain can also take several minutes.

## Smooth detail and make a repeating tile

**Supersample** smooths fine detail by evaluating **In** at several positions within each
pixel. Connect a numeric or Color calculation and use **Out** of the same type. **UV**
defaults to the current pixel. The bottom **Quality** selector offers **2x2** (default),
**3x3** and **4x4**, giving 4, 9 or 16 source evaluations per pixel. Higher quality can
reduce jagged edges and flicker in fine patterns, at greater cost.

**Make Tileable** blends four evaluations of **In**, shifted by half a tile along X, Y or
both axes, across the unit UV square.
Connect a numeric or Color calculation; **Out** keeps its type. Leave **UV** unconnected
for the current pixel, or connect scaled coordinates to repeat the result. All four source
coordinates wrap into 0–1, so the blend works with both Clamp and Repeat textures. Smooth
quintic weights fade out the original edge seams and the midpoint seams introduced by the
shifted copies. **BlendWidth = 0.5** blends across the whole tile. Lower values narrow the
blend toward each edge, leaving the center unchanged; for example, **0.1** affects the outer
10% on each side. Values above 0.5 act as 0.5; zero or negative values repeat the source
without hiding seams. Coordinates outside 0–1 repeat the tile. Distinct shapes can appear doubled or faded where copies overlap;
inspect the repeated result when using recognizable motifs.

Both nodes average or blend all components independently, including alpha. For alpha-weighted
averaging, put Premultiply before the filter and Unpremultiply after it. They reevaluate the connected branch; fixed upstream coordinates stay fixed.
Nested sampling multiplies the work. Both support HLSL and SVM previews, with HLSL fallback
when the branch exceeds SVM's evaluation limits.

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
Drag either port to create a connection; drag the body to move the node. A Reroute chain without a source is treated as disconnected, without an error: each receiving input uses its own saved or default value. The nodes and wires remain in the graph; reconnecting the source restores the signal.

Deleting a Reroute reconnects its incoming source directly to its downstream inputs, including
when several Reroutes form a chain. Deleting an ordinary connection breaks that connection.
Reroute positions and connections are saved with the document; insertion, movement, connection
changes and deletion support **Undo/Redo**.

## Preview a node

Hover a node to switch its preview between **Image** and **Height** using the icons
at the bottom left. Height shows the node's image on a 45° orthographic relief.
The **⋮** button selects **Height Channel** (R, G, B, A or Luminance), **Height Scale**
and **Texture**. Texture is enabled initially; disable it to inspect a neutral relief.
Enable **Highlight Outside 0–1** to mark heights below 0 or above 1 in solid error magenta,
unaffected by lighting. It checks Height Channel before Height Scale, and is off by default.
Each node keeps its own settings in the graph window. A faint relief icon identifies
Height previews when the controls are hidden. For nodes with multiple outputs,
the selector on the right chooses the output shown in either mode.
Image/Height mode and the selected output are saved per node in the document, including
nested Sub Graph nodes, and support Undo. Reopening restores both choices. If an output is
temporarily unavailable, the first available output is shown without replacing the saved choice.

Height uses the same small image as the node preview, so it is useful for judging
shape and slope rather than checking individual pixels of a large canvas. Use
[Layer Preview](preview.md) for the more detailed relief of the final result.

**Accurate Lighting** controls lighting in node previews. By default, Global Normal Lighting
uses a fixed white directional light from the upper left at 45°. Enable the toggle in the toolbar
or User Settings → Shader Graph to preview the document's lights, including Area and Cookie.
The choice is remembered after closing the window or restarting the Editor. Local lighting keeps
its settings; Layer Preview, Canvas and export always use the actual lighting.

An Info badge indicates multiple Global lighting calculations in one pass. Where suitable,
mix surface inputs with a mask before one Normal Lighting node. Mixing lit results still calculates
both branches; mixing inputs can change the appearance, especially specular highlights.

In debug builds, enable **SVM Nodes Only** to limit Create Node, Replace Node and connection-drop
menus to nodes supported by SVM with their initial settings. This filter is independent
of **SVM Preview (Experimental)**. Changing settings or connecting an unsupported
branch can still make a preview use HLSL.

**Layer Preview** updates on the next Editor update without a fixed waiting period.
Rapid edits are combined; an available Canvas result is reused. A hidden or collapsed
preview does not render.

A centered spinner indicates that the displayed preview is awaiting an update.
The image is dimmed with a soft highlight sweeping downward while waiting. A teal lightning icon in a node's header
identifies its displayed SVM preview; Output reflects the result preview's renderer.
The spinner disappears when the current image is rendered or an error is reported. Hidden
previews stop animating; ordinary time animation does not keep the spinner active.

Enable **SVM Preview (Experimental)** in the graph toolbar to try the alternative
preview renderer. It applies to supported node previews and the result preview
of a Shader layer in this window. Layer FX and clipping still apply.
Shader FX result previews, the document canvas and export use the native renderer;
native graph compilation continues in the background.
First use can require a lengthy compilation of the shared SVM shader. Previews
keep their existing image while a replacement is prepared.
This experimental option is off by default.

The prototype supports numeric/Boolean math, Swizzle, color adjustments, normals,
UV transformations, Time and numeric/Color/Boolean parameters. Perlin, Value, Value
Cubic, OpenSimplex, Flow, Gabor, Cellular and Shard support 1D/2D/3D. Flow's Phase
can be animated with Time. Gradient outputs retain their dimension; Curl is available
in 2D/3D. These noises support Filtering and Periodic, including filtered sources
inside Fractal. Fractal supports its own filtering and up to two nested Fractal nodes.
Sample Gradient and Sample Curve accept inline values or parameters. Blend,
Height to Normal/Distortion from a texture or value branch, Local Lighting and simplified Global
Lighting also use SVM. Accurate Global Lighting retains exact document lighting.
When its input branches are supported, SVM prepares all lighting inputs together;
unsupported inputs use HLSL. The resulting lighting stays the same.

Canvas Position/Data, Pixelate UV, Tile UV, Blackbody, Dither, Shape, Combine Distance,
Checkerboard, Stripes, Grid and Rings are also supported, including pattern smoothing
inside Fractal. Gradient Map and Color Curves preserve HDR and their alpha controls.
Append, Switch, Fade Transition, Replace Color, Contrast, Normal Reconstruct Z, Normal Unpack,
Radial Shear, All, Any, Is NaN and Is Infinite also have SVM implementations, as does
Shape's Rounded Polygon mode.

Spots Noise and all three Warp Noise nodes support 1D/2D/3D; Triangle Voronoi Noise
supports all its 2D outputs. Filtering, Periodic, distributions and Warp accumulation
retain the native calculations. White/Blue Noise support 1D/2D, Value/Color, Invert
and custom Image Size. Edge Detection processes every component, including alpha.
Apply Transform2D accepts inline values or parameters and supports Local/Input,
including projective transforms. These nodes can also feed Fractal within the SVM limits.

Texture Size, Sample Texture and Sample Input also use SVM. Sampling preserves HDR,
alpha, channels, Point/Bilinear and boundary modes. Sample Input reads the selected layer
input, or the image before the current FX. Texture parameters can read another layer;
Self reads the current FX input. Unsupported combinations still fall back to HLSL.


Integer scalar/vector parameters and pixel-unit parameters also work in SVM. Pixel Scale
is applied at render time. Sub Graph uses the same expansion as native HLSL, including
nested calls, multiple outputs and per-instance defaults. Supported previews inside a
definition use its interface defaults. Unsupported dependencies retain native fallback.

In Shader-layer previews, supported nodes after an explicit **Buffer** can return
to SVM even when the buffered branch uses HLSL. The same continuation is available
after **Caustics** and direct **Global Normal Lighting** results. **Caustics Noise** runs directly in SVM.
Buffer's Size, Wrap and Filter still control the sampled result. This does not cover
Global lighting inside Fractal or a blur filter. Other branches, settings
or graphs exceeding the prototype's limits use native HLSL automatically.
Turn the option off to use native previews throughout this window. See the
[supported subset and limits](../ShaderGraph.md#experimental-svm-preview) for details.

**Layer Preview** floats above the graph, initially at the bottom-right. Drag its header or image
to move it and its sides or corners to resize it. Near the viewport edges it snaps flush and stays
attached when the window changes size; near a corner it attaches to both edges. Highlighted borders
show the attachment. Drag away to detach. The window retains placement, attachments and size;
graph zoom and panning do not move it. The header's **R/G/B/A** buttons change only the preview channels.

UV, sampling, generators and image-processing math have previews up to **128 × 128**.

The final image has compilation priority. Next come previews of directly edited nodes, including nodes whose inputs were connected or disconnected, followed by previews nearest the last edit location. Panning and zooming do not move this priority location. Before the first edit, previews are ordered from the view center. Graph code preparation runs in the background, and the last working image remains visible while a replacement compiles. A new complex shader can still take time to compile.

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

## Organize parameter controls

Use **Group** to keep related controls together, **Foldout** to collapse a section, and
**Tabs** to switch between pages. The arrangement appears in the graph's **Inspector**,
Shader-layer Properties and Shader FX controls.

1. In **Parameters**, open **+** and choose **Group**, **Foldout** or **Tabs**.
   Select a container first to add inside it; click the empty list area to add at the root.
2. Expand the block and edit **Title**. Tabs starts with one **Tab**; select Tabs or one
   of its pages, then choose **+ → Tab** to add another page.
3. Drag parameter or container headers to change their order or parent. Put controls inside
   a Tab page. To nest another Group inside a Group, place a Foldout between them.
4. Optionally choose a direct child in **Header**. Choose **None** to clear it before moving
   that parameter elsewhere. A Tab header accepts a scalar **Toggle**; Tabs has no header.

Group and Foldout headers accept **Float**, **Vector2**, **Vector3**, **Vector4**, **Color**,
**Toggle**, **Enum** and **Seed**. Texture, Gradient, Curve and Transform2D controls cannot be
placed in a header. The parameter must be visible, unconditional and have a name used by no
other parameter in the graph.

For Group and Tabs, the eye button beside **Title** hides the heading without erasing its text.
Clear a linked Header before hiding a Group title. Foldout and individual Tab titles cannot be hidden.

Foldout, Tab and any container using Header need a nonblank **Title**. Pages in the same
Tabs need different titles.
Right-click a container and choose **Ungroup** to remove its grouping while keeping the
parameters and their values. Ungrouping Tabs keeps the contents in page order; ungrouping
one Tab places its contents after the containing Tabs. Empty blocks remain in Parameters
for later use, although a value inspector may omit them until they contain controls.
Saving, copying a layer and saving a shader preset retain the arrangement.

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

**Color** values are Float4 throughout calculations and outputs. Only an input marked `col`
uses color padding. Output.RGBA and Blend color inputs use this policy; sampled colors and
Color parameters output Float4. Sample Curve produces a number.

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

Compilation status appears at the right of the **Generated HLSL** line, even while the code is hidden.

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

## Erosion Noise

**Procedural → Noise → Erosion Noise** generates terrain with branching ridges
using WT Perlin and Gabor. **Scale** and **Seed** control the base; **Strength**
controls detail amplitude, **Detail** its starting frequency, **SlopeStrength**
its carrier frequency response to the slope, and **Branching** feedback between
octaves. **Octaves** (0–8), **Lacunarity** and **Gain** control finer detail.
**Value** is the final height, **Base** the original terrain, and **Erosion** the
signed addition: Value = Base + Erosion. Strength 0 returns Base. For normals,
connect Value to Height to Normal. Enable the bottom **Periodic** checkbox and set **Period = (1, 1)**
to tile the UV square. Period is measured before Scale; nonpositive components
disable repetition on that axis. Fractional Scale, Detail and Lacunarity also tile.
Other inputs must be constant or repeat over the same period. Periodic is off by
default. The Period port hides when disabled; connected ports remain dimmed and
retain their wires. This 2D node runs through HLSL. **Position** defaults to UV. Use **Offset** to move the pattern when Periodic is enabled.

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

Marked HLSL libraries appear by category in node search. The separate Contrast function in
`Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl` retains **Amount**, **Result**
and a midpoint of 0.5. The built-in **Contrast** uses **Contrast**, **Out** and a linear-RGB
midpoint of about 0.218. Both preserve alpha and HDR. **Refresh HLSL**
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

Both **Caustics Noise** and **Caustics** offer a **Color** output for colored light patterns.
Both use **CIE/D65** by default. Choose **Custom** at the **Spectrum** input
to edit a gradient, or connect a Gradient parameter. Right-click the unconnected
gradient and choose **Reset** to return to CIE/D65. In either gradient, colors run from long wavelengths on the left to short
wavelengths on the right. **White Balance** keeps overlapping gradient colors neutral
by default. Turn it off to retain the palette tint. Gradient alpha is ignored.

**Dispersion** defaults to **1**. Increase it to separate the
light into colored fringes. Both combine six spectral samples.
Negative values use zero; there is no upper limit. Values above 1 keep the same six
samples and can produce visibly separate color bands.
At **0**, the default spectrum gives grayscale **Value**. Color alpha is always 1.
**Value** remains a monochrome light mask regardless of Dispersion. With nonzero
Dispersion, Color's green channel can differ from Value.

**Caustics Noise** in **Procedural/Noise** makes a water-light pattern immediately.
Choose **OpenSimplex2S** or **Simplex** (default) in the bottom **Algorithm** selector
to change the pattern. Both use 2D **Position**, initially UV, **Scale = (8,8)**,
**Seed = 1337**, **Evolution = 0** and **Strength = 1**.
Scale controls the pattern size; Seed gives another arrangement. Strength controls
distortion and the shape of the bright lines. Larger magnitudes take more work to render
and extreme values can create folds. Connect **Time** to Evolution for motion.
**Value** already includes the contrast profile and final **Smoothstep** in 0–1;
it is ready to use as a light mask. Use **Fractal** for octave detail.

For **Caustics Noise**, Dispersion shifts the six samples along a smoothly varying local
direction while keeping Strength, the contrast profile, Seed, Scale and Evolution shared.
Its dispersion strength is halved: a value of 1 now matches the previous 0.5.
Color stays in 0–1 and works with either Algorithm, Periodic and Filtering.

**Periodic** and **Filtering** work with both algorithms and start off. Periodic enables **Offset** `(0,0)` and **Period**
`(1,1)` for seamless repetition along the enabled axes. Otherwise, translate Position with
Add. In Simplex, the pattern repeats without blending copies. Periodic changes its orientation
and fits a whole number of noise units into each period: Scale=8 and Period=1 retain density 8. OpenSimplex2S also repeats the lattice without blending copies; enabling Periodic changes
its pattern orientation while keeping its existing density calibration and animation loop. The original Simplex kernel can still show small abrupt changes in some patterns, also without Periodic. Filtering reduces fine-detail flicker but can leave aliasing in strongly distorted patterns.

**Caustics** in **Filter** projects light from a connected **Height**: try
**Perlin Noise → Fractal → Caustics** or use a sampled height texture. Its controls are:

- **Strength** controls the height relief; **Depth** controls how far light travels.
  Increasing either tightens the folds and eventually makes them overlap.
- **IOR** controls refraction; **1** removes it. **Light Direction** points toward the
  receiver, with `(0,0,-1)` for overhead light.
- **Dispersion** varies refraction across six wavelengths on the same height surface.
  Small values give gentle color fringes; near **1**, separation is deliberately stronger.
  Above 1, the refractive spread continues linearly with no jump in value or slope.
  **IOR** stays anchored at 550 nm. At IOR=1.333, Dispersion around **0.1** gives a
  water-like refractive spread. As Depth approaches **0** or IOR approaches **1**,
  color separation fades. CIE/D65 and balanced custom spectra give neutral overlap.
- **Softness** softens light footprints; **Intensity** changes brightness. **Value** and **Color** RGB can
  exceed 1, so preserve HDR when bright peaks matter. A flat surface gives uniform light.
- **UV** places the finished pattern. **Resolution** defaults to Custom **512 × 512**;
  increasing it sharpens the output, but the surface detail has a finite sampling limit.
- **Wrap = Repeat** tiles the light pattern. For **Caustics**, use a smooth, seamless
  height input as well; wrapping cannot repair a discontinuity in the source.

The result depends on surface curvature: smooth inputs produce flowing folds, while
sharp height discontinuities produce angular shapes. For richer surfaces, put Fractal
before Caustics. Fractal after Caustics resamples its image and requires
**Seed Step = 0**.
