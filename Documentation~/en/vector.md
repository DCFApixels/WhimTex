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
The toolbar also provides **Vector Select** and **Vector Nodes**.

## Draw and edit

With **Vector Pen**, click to add a corner and drag to add a curved point. Click the
first point to close the path, press **Enter** to finish an open path, or **Escape**
to cancel. Only closed contours have a fill; open paths can have a stroke.

Use **Vector Select** to move a figure, resize it with the corner handles or rotate
it with the round handle. These operations change the layer transform; its frame
describes the figure rather than its stroke or FX. **Vector Nodes** moves individual points and tangent
handles. Hold **Shift** for uniform resizing or 15-degree rotation steps.
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

## Adjust the last brush figure

**Vector Brush** fits editable closed cubic Bezier curves to the painted mask when you
release the pointer. In **Last Stroke — Original Mask**, lower **Tolerance (px)**
(0.25..64 reference pixels) to follow the mask more closely; higher values usually give
fewer anchors and simpler curves. Narrow features and holes can require more anchors
than this setting suggests. Positive **Expand (px)** (-256..256) grows the figure; negative values
shrink it. These adjustments rebuild from the original mask, so repeated changes
do not accumulate fitting error. Numeric edits apply when you confirm them.

Each brush figure retains its mask after saving and reopening TIFF or JSON. The next
brush stroke creates a separate layer; the previous figure keeps its mask. Manually editing
that figure clears its mask. **Release Mask** also discards this adjustment source
while keeping the vector figure. The mask is scaled down from the reference size
to at most 1024 pixels on its longest edge, so a large image's finest detail may not survive brush conversion. Complex strokes can reach
a reported geometry or work limit.

## Convert a Shape

Select a Shape and choose **Convert to Vector** in Properties or its context menu.
Rectangle, Ellipse, Polygon, Star, Line, Arc and Sector use their geometric contours,
including rounded/beveled corners. The layer keeps its placement and FX.

Conversion changes **Clip** tiling to **Unbounded** to preserve protruding line caps
and outer strokes: Vector's Clip would crop them to the layer frame. Other tiling
modes stay selected. The frame is fitted to the figure while preserving placement;
the local coordinates and transform values may change together. Repeat/Mirror/Source edge
sampling can differ because Vector samples its rendered source image.

Conversion reports an unsupported setting before replacing the Shape when active
Feather is nonzero, a visible-width stroke uses a nonuniform/projective transform,
an overlapping translucent stroke replaces a fill, or a thick arc develops offset
cusps or overlapping end caps. Set Feather to zero, adjust the indicated style, or
keep the original Shape. Curves use cubic approximations; edge antialiasing may differ.
See the [data contract](../JSON_FORMAT.md#vector-layers) for exact storage and API fields.

## Combine figures without merging them

**Boolean Group** combines the geometry of its Shape, Vector and nested Boolean Group
children, then gives the result one common fill and stroke. Each child stays editable
and can be moved independently; moving the group moves the whole construction.
Select figures in the same parent, open their context menu, then choose
**Boolean Group → Combine**, **Union**, **Subtract**, **Intersect** or **Xor**.

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
