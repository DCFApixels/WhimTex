---
title: "Layers and groups"
parent: "English"
has_children: true
nav_order: 2
lang: "en"
permalink: "/en/layers/"
translations: "en/layers.md,ru/layers.md,zh/layers.md"
previous_page: "en/getting-started.md"
next_page: "en/transform.md"
---

# Layers and groups

Build an image from separate layers so you can move or adjust each part independently.
The top of the list is the front of the image.

## Share layers

Select layers and choose **Copy as Portable** from their context menu. Send the JSON as text or a `.json` file; the recipient copies its contents and presses **Ctrl+V** in WhimTex. Ordinary Ctrl+C is unchanged.

Procedural layers, groups, transforms and self-contained HLSL FX keep their settings. Include source layers used by Target, FX textures and clipping masks. Raster pixels are not embedded. Drawing uses its original URL until its pixels are edited; otherwise it is copied as an empty layer with a warning. Transforms and FX keep the URL usable. File keeps its asset GUID and local ID: the same asset and `.meta` file must exist in the receiving project. If unavailable, it is pasted empty with a warning. Empty layers retain settings and references. Opening a saved document remains offline, but pasting linked Drawing JSON downloads the image again. Check links before sharing: they can expire, change, or contain private access tokens. Unsupported dependencies still stop copying with an explanation.

Custom HLSL includes are expanded when copying; helper functions and their calls remain separate. Built-in UnityCG, WhimTex noise and dithering libraries remain references. The limit is 64 KiB per FX and 8 include levels; missing, cyclic or oversized dependencies stop copying.

## Add a layer

Use **+** at the bottom of Layers to choose a type:

| Layer | Use it for |
| :--- | :--- |
| File | An existing texture from Project. |
| Drawing Layer | Painting, erasing and filling. |
| Color Fill | A single-color background or shape. |
| Gradient | A smooth color transition. |
| Noise | A generated pattern. See [Noise](noise.md). |
| Shape | An editable rectangle, ellipse, polygon, star or line. |

You can also drag a Project texture onto the Preview to add it at the top,
or drop it between rows to choose its position. A newly assigned image keeps its original proportions.
Assigning an HDR texture to a File layer sets **Color Range** and **Blend Range** to **HDR**.
You can change both afterwards in **Color & Blending**.

File layers linked to another WhimTex document have a thin orange line along the left edge of their row.
Double-click the thumbnail or row background to open that document without replacing the current one. An already open document is focused;
otherwise Unity attempts to add a WhimTex tab beside an existing WhimTex window, falling back to a separate window.
Double-clicking text or number fields still edits those fields.

Effect layers also show thumbnails of their results. Animated Shader FX use still thumbnails.

## Draw a shape

Choose **Shape** (`U`), pick a shape in the Preview header, then drag to create it.
New layers are named after the figure, such as **Rectangle 1**, **Line 2**, or **Star 3**, with one shared numbering sequence for all shapes.
You can also hold the Shape tool button briefly, or drag from it, to open an icon list on its right.
Move over a figure and release to select it. Releasing outside the list cancels the choice.
Each drag adds a separate Shape layer, including in an empty document.
Hold `Shift` for equal proportions or a line angle in 45° steps; `Ctrl` bypasses guide snapping.
Press `Escape` before releasing to cancel.

Use **Transform** to move, resize or rotate an existing figure. In **Properties (Shape)**,
change its type, fill and stroke colors, rectangle roundness, polygon sides or star points and inner radius.
For a rectangle, **Roundness (%)** has four fields around a square, one per corner.
Drag their **TL / TR / BR / BL** labels to adjust values without typing.
The chain links proportional changes; the crossed-out chain lets you edit each corner independently.
Linking keeps existing values. Linked changes stop when a corner reaches 100%; editing a zero corner
adds the same amount to all four. The diagram shows the shape before its Transform rotation.
The stroke sits inside the edge, and its width is measured in canvas pixels.
For a line, adjust its length and thickness with Transform.
You can also create a centered shape through **+ → Shape**.

A Shape stays editable: it works with clipping masks, blending and FX, just like other layers.
For example, put a Gradient above it and enable the gradient's clipping mask to color the figure.
Convert it to Drawing only when you want to paint directly on it.

## Select and arrange

Click a row to select it. Hold `Ctrl` to select several layers or `Shift` to select a range.
The **last selected layer is active**: this is the layer you paint on and edit in Layer Settings.

Drag a row's thumbnail, empty space, name, opacity field or eye to move the selected layers.
Hold the layers near the top or bottom edge of the list to scroll.
In the name and opacity fields, drag up or down to move layers; drag left or right to select text.
While a field is focused for editing, dragging only selects text; leave the field to move layers from it again.
Dragging from a field cancels its unconfirmed input; dragging the eye does not toggle visibility.
Confirm name and opacity edits with `Enter` or by leaving the field.
Edit the name directly in the row. **Opacity** controls how much the layer shows;
**Blend** controls how it combines with the image below.
Changing either on a selected row updates all selected layers.

Use the eye to hide a layer. The eye in the column header reveals all layers.

Select several layers in **Layers**, then use **Transform (T)** to edit them together. The initial frame is axis-aligned and encloses only the selected layer frames; selected groups contribute their own frame, not their children's bounds. Move, rotate, scale, skew and perspective work relative to this shared frame. Selecting both a group and its child does not apply the transform twice. **Esc** cancels the current drag; Undo restores the entire operation. Per-layer toolbar settings are disabled during multi-transform.

## Edit layer settings

The selected layer's settings are divided into four foldouts:

- **Transform:** position, size, rotation and tiling.
- **Color & Blending:** Opacity, Blend Mode, color ranges and Swizzle. Opacity and Blend Mode are also available in the Layers list. The Standard/HDR selector remains available in the header.
- **Properties (layer type):** settings specific to this layer, such as its source texture, effect target or drawing symmetry.
- **FX:** add and adjust shader effects.

Open the same settings in a separate window through **layer ⋮ → Properties**.
Sections that do not apply to the selected layer are greyed out.

## Keep related parts in a group

Select layers and click **Folder**, or drag layers into an existing group.
Use the group's arrow to expand or collapse it.

Groups start in **Pass Through**, so their layers can blend with layers outside the group.
Choose another blend mode to blend the group as one image. Group opacity fades the whole group.
Add **FX** to a group to process its combined image. FX automatically isolate a Pass Through group using Normal blending, so they do not affect the image outside it. Removing all FX restores Pass Through unless clipping or Swizzle still requires isolation. **Properties → Compositing** shows the effective mode, read-only. Groups also have **Transform**: move, rotate, scale, skew or distort the group with perspective. Children keep local transforms relative to their group. The group frame represents its own transform, not the bounds of its children. Moving layers between groups or ungrouping preserves their canvas placement.

## Duplicate, merge or delete

Right-click a row or open **⋮** for actions on the selection.
**Duplicate** makes a copy you can edit separately. File layers still use the same source texture.

| Footer icon | Click | Drop selected layers |
| :--- | :--- | :--- |
| **+** (type menu) | Choose a layer type. | Duplicate. |
| **Page +** (page with a plus) | Add a Drawing layer. | Make a merged Drawing copy. |
| **Folder** | Group the selection. | Group. |
| **Trash** | Delete the selection. | Delete. |

See [merging and conversion](transform.md#merge-layers-or-convert-them-to-drawing)
when you want to paint on the combined result.

## Repair a missing layer

A layer whose type is unavailable keeps its name, position, visibility and common settings.
A group also keeps its children. Select the row to see the warning in Layer Settings.
Choose **Replace with**, then click **Replace Behaviour**. **Transfer saved settings** copies
compatible settings from the previous layer type. The panel lists anything that cannot
be transferred; check the result before saving. Groups containing children can only be restored as groups.

You can still move, hide or remove the unavailable layer. It does not appear on the canvas until restored.

{: .warning }
The current layer format is incompatible with documents created before the Layer/Behaviour redesign.
There is no automatic conversion. Keep those documents with their original WhimTex version,
or export their images there before updating.
