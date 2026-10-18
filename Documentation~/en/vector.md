---
title: "Vector paths"
parent: "Layers and groups"
grand_parent: "English"
nav_order: 2
lang: "en"
description: "Draw and edit vector figures, curves, fills and strokes in WhimTex."
permalink: "/en/vector/"
translations: "en/vector.md,ru/vector.md,zh/vector.md"
---

# Vector paths

A Vector layer holds one editable figure with its own transform. A figure may have
several contours, including holes. Add a layer with **+ → Vector**, or draw a new
figure with **Vector Pen** or **Vector Brush**. Each new figure gets its own layer.
Use **Layer Select** to select figures and **Vector Nodes** to edit their points.
New figures use **Tiling → Source**, which selects Unbounded for Vector. **Clip** instead cuts geometry, strokes and
Feather at the layer's local frame, not just at the document canvas.

## Draw and edit

With **Vector Pen**, click to add a corner and drag to add a curved point. Click the
first point to close the path, press **Enter** to finish an open path, or **Escape**
to cancel. Only closed contours have a fill; open paths can have a stroke.

**Layer Select (V)** picks Vector figures by their geometry as well as visible pixels.
Open paths can be selected near their curve; holes remain empty. Shift toggles layer
selection; Ctrl or a repeated group click selects inside groups. Selected Vector
contours are shown in Layer Select and **Transform (T)**, without node handles.
Use Transform to move, resize or rotate a figure through its layer transform.
Hold **Shift** for uniform resizing or 15-degree rotation steps.
**Vector Nodes** moves individual points and tangent handles.
With Nodes, **Shift** adds points to the selection; dragging empty space selects a
rectangle of points. Double-click an edge to insert a point; **Alt-click** removes
a point; **Alt-drag** breaks the linked handles. In the header, **Corner** lets the two handles move independently, **Aligned** keeps them
on one line, and **Mirrored** also keeps their lengths equal.

## Fill and stroke

Select the figure's layer in **Layers**, then edit its appearance in **Properties (Vector)**.
Use the layer list to rename, hide, reorder, duplicate or delete figures. **Fill → Type** offers
None, Solid, Linear, Radial, Square, Diamond and Circular. **Fill Rule** controls how
overlapping contours and holes fill. Shape offers the same fill controls while its
**Fill** toggle is enabled.

Choose **Linear** for a straight transition between **Start** and **End**. **Radial**,
**Square** and **Diamond** spread colors from **Center**; **Radius / Direction** sets
their size and, for Square/Diamond, orientation. **Circular** runs counterclockwise
around Center, beginning and ending along the ray through **Direction**. Different
first and last colors make the seam visible. The coordinates are normalized within
the layer: `[0,0]` is bottom left, `[1,1]` top right. Coincident points use the first
gradient color everywhere.

Click **Gradient** to edit colors, transparency and the existing wrap controls.
The palette and points stay available when you switch fill types or turn Shape's
Fill off and on. Gradients follow the layer transform. Their proportions use the
current canvas size for Shape and the creation reference size for Vector, so
changing the canvas dimensions can change Shape's gradient proportions.

Enable **Stroke**, then choose its **Type**: None, Solid, Linear, Radial, Square,
Diamond or Circular. Stroke has its own color or gradient, points and circular
**Repetitions**. Its gradient controls work like Fill's; changing one keeps the
other's appearance. None or turning Stroke off hides the outline and keeps its
settings for later. Circular Repetitions controls how many periods fit around the
center; the palette's wrap setting controls what happens beyond the first period.

Use **Gradient Handles** to place the active gradient on the canvas. When both
Fill and Stroke have active gradients, choose **Fill** or **Stroke** in the tool's
selector. Dragging these points changes that gradient within the figure and keeps
the figure's placement and the other gradient unchanged. With only one available
gradient, the selector chooses it automatically and is disabled. Boolean Group offers
the same independent Fill and Stroke controls for its result; with Combine and
**Appearance → Layers**, edit the children's gradients.

Set the outline's width, inside/center/outside position, joins and end caps.
**Position** offers Inside, Center and Outside icon buttons, with a thin
line marking the original contour. **Join** offers icon buttons for Miter, Round and Bevel; **Cap** offers
Butt, Round and Square. The active button is outlined; hover to see its name and effect.
Open paths always use a centered stroke. Width and dashes scale with the layer.
For closed paths, Inside/Outside moves the trajectory by half the stroke width
before placing dashes and caps. Round dots and ends stay whole; they are not cut
against the fill. This also applies to Shape. Dash positions can change because the
shifted path has a different length. Very narrow inward offsets can split or disappear;
at tight corners, complete caps can cross the original boundary.
**Edge Mode → Step** removes geometric antialiasing; colors,
opacity and FX can still produce partial transparency.

### Feather

Use **Feather (px)** in **Properties (Vector)** to soften the fill and outline
edges. It also softens dash ends and the inner and outer sides of a stroke.
The width is measured in the figure's reference pixels and scales with the figure,
like stroke width. **Feather Position** chooses where the transition lies:
**Inside** fades inward, **Outside** outward, and **Centered** equally on both sides.
Large values can fade thin details or close narrow gaps.

Set Feather to `0` for the normal crisp edge. **Edge Mode → Step** ignores Feather
but keeps the settings, so switching back to Antialiased restores them. Feather
softens the boundary rather than blurring the fill or stroke colors.

### Dashes

Shape uses these same dash controls. Its stroke width and dash lengths stay fixed in
document pixels when resizing the shape; Vector scales them with its layer.

Choose a visual preset under **Dashes**, or enter a pattern in the always-visible
**Pattern (× Width)** field: `2 1` means a dash two stroke widths long and a gap one width long; empty
means solid. Spaces, commas and semicolons separate values. Expand **Segments** to
edit individual **Dash** and **Gap** lengths, add pairs or remove them. **Add Pair**
copies the last displayed pair; an empty pattern starts with `2 1`. All controls
stay synchronized; editing a preset can produce a **Custom** pattern.

The preview updates while typing and accounts for width, caps and **Dash Offset**.
Press **Enter** or leave the text field to apply the pattern to the figure;
**Escape** restores the applied value. Invalid input stays highlighted without
replacing the working pattern. Multipliers must be positive and at most 8192,
with at most 256 values. An odd list repeats twice, swapping dash and gap roles;
its repeated fields stay linked. Adding/removing pairs expands that list to its
equivalent full cycle, within the same limit.

Presets keep color, width, joins, position and offset. Presets marked **Round** also
select round caps: dots are short dashes of 0.01 times the width, rounded by those caps.
Changing **Width** scales dashes and gaps together without changing Pattern values.
If caps close the gaps, increase **Gap** to separate them. **Dash Offset** shifts
the pattern along the contour in pixels. Lengths are not automatically fitted to corners.

## Adjust a brush figure

**Vector Brush** starts a figure with the first left stroke. Further left strokes add to
that figure; right strokes erase, including holes. Red marks preview the erased area;
release the pointer to rebuild the closed cubic Bezier curves. **Enter** or **Finish Editing**
ends the figure, keeping its mask. The next left stroke starts a new layer.
Rebuilding runs in the background, including changes to **Tolerance** and **Expand**.
While **Rebuilding mask…** is shown, wait before the next stroke or saving; the stroke
preview remains visible. **Escape**, **Cancel**, or **Cancel Rebuild** discards the pending
result, keeping the previous figure. Changing layer/tool or using Undo also cancels it.
Finishing editing during rebuilding finishes the figure after the result is applied.
To resume an older figure, select its layer and press **Edit Mask** in the toolbar or
**Original Mask**. Figures without a retained mask are not rasterized automatically.
**Escape** cancels only the current stroke. **Shape** selects Circle, Square or Diamond, as in Pencil;
the cursor follows the tip shape, **Size**, canvas zoom and rotation.
On a manually scaled or deformed figure, the tip still follows the mask's local
pixel metric while the cursor uses canvas pixels, so their footprints can differ.
Hold **Shift** while dragging to lock a horizontal or vertical line on screen;
release it to continue freehand. **Shift-click** connects to the previous stroke's
endpoint within the active figure, with the same paint/erase action. Finishing or changing the figure resets this connection.
Shift lines take priority over guide snapping. Shape changes apply to the next stroke.
Drag the **Size** label or use `[` / `]` to resize the brush (1–2048 px), with the same
shortcut steps as the raster brush. The field and cursor update immediately, including
during a stroke. Toolbar **Fill** and **Tolerance** set defaults for new figures;
an edited figure keeps its own appearance and Original Mask settings.
There are no Hardness, Flow or raster-blending controls.
In **Original Mask**, lower **Tolerance (px)**
(0.25..64 reference pixels) to follow the mask more closely; higher values usually give
fewer anchors and simpler curves. Narrow features and holes can require more anchors
than this setting suggests. Positive **Expand (px)** (-256..256) grows the figure; negative values
shrink it. These adjustments rebuild from the original mask, so repeated changes
do not accumulate fitting error. Numeric edits apply when you confirm them.

Each brush figure retains its mask after saving and reopening TIFF or JSON. The next
brush stroke stays in the active figure until you finish editing. Manually editing
its nodes clears its mask. **Release Mask** also discards this adjustment source
while keeping the vector figure. The mask is scaled down from the reference size
to at most 1024 pixels on its longest edge, so a large image's finest detail may not survive brush conversion.
Further strokes grow the mask without resampling existing pixels. If it would exceed
1024 × 1024 pixels, the stroke is rejected; finish the figure and start another.
Painting and erasing are clipped to the document canvas, including on transformed
figures. The pointer can leave the canvas and return; only the overlapping part of
the brush is applied, without clamping its center to the edge. Existing mask content
outside the canvas is retained. Outside-only contributions do not grow the buffer
or trigger a rebuild. New masks allocate only the touched region, and empty borders
are removed on completion without resampling.
Expand remains a procedural offset over the whole edited source mask. Complex strokes can reach
a reported geometry or work limit.

## Convert a Shape

Select a Shape and choose **Convert to Vector** in Properties or its context menu.
Rectangle, Ellipse, Polygon, Star, Line, Arc and Sector use their geometric contours,
including rounded/beveled corners. The layer keeps its placement and FX.

Conversion retains Source and every explicitly selected tiling mode. Clip keeps
the original clipping frame; otherwise the frame is fitted to the figure while preserving placement;
the local coordinates and transform values may change together. Repeat/Mirror edge
sampling can differ because Vector samples its rendered source image.

Conversion reports an unsupported setting before replacing the Shape when active
Feather is nonzero, a visible-width stroke uses a nonuniform/projective transform,
an overlapping translucent stroke replaces a fill, or a thick arc develops offset
cusps or overlapping end caps. Set Feather to zero, adjust the indicated style, or
keep the original Shape. Curves use cubic approximations; edge antialiasing may differ.
See the [data contract](../JSON_FORMAT.md#vector-layers) for exact storage and API fields.

## Convert Text

With **Scope → Character** gradient fill, conversion creates an isolated group with a
separate Vector layer for each visible letter. The common placement and FX stay on the
group; each letter retains its gradient position and can be moved or edited independently.
FX retain their original local coordinates, rather than switching to the ordinary group's canvas coordinates.
Spaces and completely clipped letters create no layers. Inside a Boolean Group the container
uses Combine/Layers, preserving its operand role. The conversion is one Undo step and rejects
an exceeded layer limit before changing the source. Other fills keep the compound-figure behavior below.

Select a Text layer and choose **Convert to Vector**. The letters become one editable
figure with separate contours and holes. The current line layout, alignment, spacing,
Horizontal Scale, Layout Offset and Frame Clip/Ellipsis are preserved, along with the
layer's placement and FX. You can then reshape letters with the node tools.
Text conversion retains the selected tiling, including Source and explicit Clip.
Unless Clip keeps the original frame, local coordinates and transform values may change together while preserving
canvas placement. Text's Frame Clip is applied before this fitting step.

Conversion uses the font's original curves. It does not trace the rendered text;
Hinted AA/Pixel grid fitting is not retained, so edges may look slightly different.
Glyphs cut by Frame Clip may use a fine straight-segment approximation at the cut.
The wording and font are no longer editable as Text. Duplicate the layer first if
you want to keep that option; the resulting Vector no longer needs the source font.

The source font must be available and contain supported outlines. TTF, OTF with CFF
outlines and TTC faces are supported; variable axes, color and bitmap fonts are not.
Synthetic italic is supported. Bold needs a real bold face: synthetic bold reports an
error. Missing or unsupported outlines leave the original Text unchanged; a saved
raster backup cannot be converted this way.

## Combine figures without merging them

**Boolean Group** combines the geometry of its Shape, Vector, Text and nested Boolean Group
children, then gives the result one common fill and stroke. Each child stays editable
and can be moved independently; moving the group moves the whole construction.
Select figures in the same parent, open their context menu, then choose
**Boolean Group → Combine**, **Union**, **Subtract**, **Intersect** or **Xor**.

Text can go straight into the group and remain editable. For a text cutout, place Text
above the base figure and choose **Subtract** for that child. Edit the wording or font
in **Properties (Text)** to update the cutout. The group uses the same original font
outlines and font restrictions as [Text conversion](#convert-text), including Frame
Clip/Ellipsis. Keep the font available when reopening the document; its raster backup
cannot supply the group's geometry.

To change an existing group, use the five **Mode** icon buttons in its Properties.
The filled area in each icon shows the result of combining two shapes. A button
sets the operation for all direct children, without changing operations inside nested groups.
**Mixed** means the children use different operations; no icon is selected until you choose one.
You can still set an individual child's **Operation** in its inspector.

The bottom enabled child is the base. Moving upward through the layer list, choose
each next child's operation: **Union** adds it, **Subtract** cuts it out,
**Intersect** keeps the overlap, and **Xor** keeps the parts that do not overlap.
The bottom child's operation has no effect. Reordering operands can change the result.

Edit the final fill and stroke on the Boolean Group. When creating the group from
figures, the bottom figure's style is copied once; later changes to that figure's
style do not change the group. Child fills and strokes stay stored; Boolean operations
use the group style. Child FX are not applied. Their strokes do not enlarge the regions being combined.
Move a child out of the group to use its own styling again.

**Combine** keeps separate figures in bottom-to-top order, without resolving their overlap.
Each retains a complete outline; translucent fills overlap instead of becoming one fill.
For this mode, **Appearance → Group** applies the common fill and stroke to every figure;
**Layers** uses each child's own fill, gradient, stroke and edge treatment, including Shape Feather.
Child FX, opacity, blend modes and channel mapping remain inactive. Union, Subtract,
Intersect and Xor always use the group style; the Appearance choice is retained when switching modes.

Raster layers and paths containing open contours cannot be Boolean operands.
Use closed figures; an unsupported operand produces a message rather than being
silently skipped. A figure can still have several closed contours and holes.
Opening older multi-figure Vector layers separates their figures into child layers
of an ordinary Group, retaining their shared placement and FX.
