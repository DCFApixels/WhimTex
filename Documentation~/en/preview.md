---
title: "Preview and navigation"
parent: "English"
nav_order: 10
lang: "en"
permalink: "/en/preview/"
alternate: "ru/preview.md"
previous_page: "en/shader-fx.md"
next_page: "en/color.md"
---

# Preview and navigation

Use the Preview to inspect your image up close, check seams or look at individual channels.
Changing the view does not resize the document.

## Move around

- Hold the mouse wheel and drag to pan.
- Hold `Shift` and drag with the mouse wheel pressed to rotate the view with any tool. Rotation lightly snaps to 90° steps and to angles that make a guide horizontal or vertical on screen. The closest angle wins; hold `Ctrl` to bypass snapping. Hidden guides and guides with **Snap to Guides** turned off do not add magnetic angles.
- Scroll to zoom around the pointer with any tool.
- With **Zoom** (`Z`), click to zoom in, `Alt`-click to zoom out, or drag a rectangle around the area you want to inspect.
- **Fit** shows the whole canvas upright; **100%** is useful for checking pixel detail.
- Enter a precise percentage in **Zoom %**, then press Enter or leave the field. The center of the view and its rotation stay in place.
- Enter an exact angle in **Zoom → Angle °** and press Enter (or leave the field) to apply it without snapping. **0°** resets rotation without changing zoom or pan.

View rotation does not rotate the layers or affect saving and export.

Turn on **Tiled** to see repeated copies of the image and paint across its edges.
See [seamless painting](symmetry.md).

## Mesh UV overlay

Enable **UV** in the footer and assign a **Mesh** to see its UV island outlines over the image. The UV panel lets you choose the channel, submesh, line color and opacity. The outlines follow zoom, pan and view rotation and never appear in exports. To paint a specific part of the model, use [UV Island selection](selection.md#select-uv-islands).

## Guides

Guides that are horizontal or vertical in the current preview appear bright cyan; angled guides use a softer blue-gray. The colors update as you rotate the view, without changing snapping.

In **User Settings → Guides & Snapping**, choose colors for **Aligned Guides**, **Angled Guides** and the **Active Guide** (hovered, selected or dragged). A guide about to be deleted stays red.
**Snap Radius (px)** sets the attraction distance for guides, intersections, canvas edges and pivot anchors: 1–64 UI pixels, default 8, independent of zoom. Angular snapping is unchanged. These preferences apply across windows and are saved between sessions; **Reset Guides & Snapping** restores their defaults.

Drag from the thin strip on the left of Preview to create a vertical guide, or from the top strip for a horizontal one.
The new line is parallel to the strip even on a rotated canvas; afterwards it moves, zooms and rotates with the canvas.

Drag an existing line to reposition it. Drop it back on either strip or outside Preview to remove it; `Esc` cancels the drag.
You can create guides from the edge strips with any tool. Only **Zoom**, **Transform** and **Layer Select** can grab existing lines to move or drag-delete them. Painting, filling and area-selection tools ignore existing guides; the lines stay visible.
Click a guide to select it: arrow keys nudge it, `Shift` increases the step tenfold, and `Delete` removes it. Click elsewhere or press `Esc` to deselect. Double-click a guide to enter an exact **Position (px)** and **Angle (°)**. Angles are relative to the canvas: 0° is horizontal, 90° is vertical. For those two orientations, Position is the distance from the top or left edge; at other angles it is the signed perpendicular distance from the top-left corner.

Right-click a guide to **Edit**, **Duplicate** or **Delete** it. Right-click either edge strip for the shared controls:

- **Show Guides** — hide or show the lines. Hidden guides do not attract tools; creating a new guide shows them again.
- **Lock Guides** — protect existing lines from editing. You can still create new guides and snap tools to locked ones.
- **Snap to Guides** — toggle snapping; hold `Ctrl` to bypass it temporarily. `Ctrl` also lets Zoom and Transform grab through an existing line.
- **Undo Guide Change / Redo Guide Change** — undo or redo guide edits without affecting painting. These commands are separate from the document's `Ctrl+Z` history.
- **Clear Guides** — remove all guides.

Moving or resizing a layer (including a Shader FX Transform 2D area) snaps its edges to parallel guides. Moving also snaps its center lines. Edges ignore oblique guides; rotating a transform can align it parallel or perpendicular to a guide. `Shift` retains its usual rotation and resize constraints.

Nearby guide intersections take priority over individual lines, with the same snap radius regardless of zoom. They attract the brush/pencil center, selection and shape points, the pivot, and a transform's center, corners and edge midpoints. When movement is constrained, only intersections on the allowed path attract the point. Holding `Ctrl` bypasses snapping.

The pivot and polygonal lasso vertices can also snap to a single guide at any angle. Rectangle selection snaps to individual lines only when they are parallel to the canvas axes; intersections work at any angle.

**Brush** and **Pencil** also snap their stroke center to guides at any angle and to intersections, including while erasing. The cursor shows the snapped position. If you start a stroke snapped to a guide, holding `Shift` keeps it on that same line no matter how far the pointer moves away. Release `Shift` for ordinary nearby snapping; press it again during the same stroke to return to the original guide. At an intersection, the guide closest to the starting pointer is used. If you start away from guides, `Shift` keeps its usual horizontal or vertical screen direction. Hold `Ctrl` to bypass guide snapping and locking. Pencil strokes still follow the pixel grid; brush Scatter is applied after snapping the stroke path.

Dragging a guide snaps it to parallel canvas edges, the canvas center, the selected layer's parallel edges and center lines, and other parallel guides. Hold `Ctrl` for free placement.

Guides never appear in exports. They stay in the current window, including script reloads, but are cleared when switching documents and are not saved in the compositor file. Guide undo history lasts until a document switch or script reload.

## Balance detail and responsiveness

Lower **Live Quality** in the footer if painting on a large image feels slow.
Save and export still use full resolution.

Pencil always shows crisp pixels at full quality, so you can place individual pixels accurately.

## See your paint on a model

1. Save the compositor and assign its texture asset to your model's material.
2. Turn on **Live Update** (the circle button) in the preview footer, next to **Post FX**.
3. Paint or adjust layers: objects using that texture update in Scene View.

The material keeps its texture reference when you toggle Live Update or close WhimTex.
Turning it off or closing without saving restores the last saved image. Press **Save** or `Ctrl+S`
to keep your edits; **Save As** creates a different asset and does not redirect existing materials.

Live Update shows the composition without EV, channel-display masks, Debug or preview Post FX.
While editing it uses preview quality, then refines the result when you stop.
If you resize the canvas, the live image fits the saved texture size until you save again.
Live Update starts off when you open the window and turns off when you switch documents or reload scripts.

### Use a compositor inside another document

Assign the saved compositor's output texture to a **File** layer in another document.
Saving the source refreshes the receiving window automatically. To see changes while editing, enable
**Live Update in the source window**. The receiving window does not need Live Update enabled just to display them.
Turning Live Update off restores the saved image in the receiving window too.

For a chain of documents, enable Live Update in each intermediate window that should pass its updated
result onwards. **Save As** creates a new asset; existing File layers keep referencing the original.
Automatic refresh across cyclic links, such as A using B while B uses A, is disabled to prevent feedback loops.

## Check brightness and channels

**EV** changes the viewing exposure, not the image itself. Keep it at **0** for the normal view.
If white looks gray or colors look overexposed, check this field first.

**R / G / B / A** lets you inspect the color channels or transparency separately.
See [color and channels](color.md) for the display modes.

> Channel buttons also affect new paint: disabled color channels receive zero,
> and turning off A prevents Brush, Pencil and Fill from leaving a mark.
> Turn all four channels on for ordinary painting.

## Background and problem pixels

Change the transparency checkerboard's colors and size in
**Window tab ⋮ → User Settings…**.

The bug button highlights pixels with invalid color values. Use it to investigate a broken-looking
effect; its highlight color is also in User Settings. The overlay is not included in saved images.

Zoom, Tiled, EV and [Post FX](post-fx.md) change only the view.
