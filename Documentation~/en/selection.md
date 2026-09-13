---
title: "Area selections"
parent: "English"
nav_order: 5
lang: "en"
permalink: "/en/selection/"
alternate: "ru/selection.md"
previous_page: "en/painting.md"
next_page: "en/symmetry.md"
---

# Area selections

Select an area to paint, erase or fill without touching the rest of the image.
Use **Area Select** (`M`) for a rectangle or ellipse, or **Polygonal Lasso** (`L`) for a shape with straight sides.

## Make and adjust a selection

Hold or drag the Area Select toolbar button to open its shape list. Release over **Rectangle** or **Ellipse**, then drag on the canvas. The button shows the chosen shape; `M` returns to it.

With Lasso, click around the outline and finish with
`Enter`, a double-click or a click on the first point.
`Backspace` or a right-click removes the last point; `Escape` cancels the unfinished outline.

For a **square or circle**, start dragging, then hold `Shift`. Release `Shift` to return to free proportions. Holding `Shift` before starting instead adds to the selection. To add a square or circle, choose **Add** above the canvas and press `Shift` during the drag.

Choose **Replace**, **Add**, **Subtract** or **Intersect** above the canvas.
You can also hold:

- `Shift` to add an area.
- `Alt` to subtract.
- `Shift+Alt` to keep only the overlap.

`Ctrl+A` selects the whole canvas. `Ctrl+Shift+I` selects the opposite area.
`Ctrl+D` removes the selection so you can paint everywhere again.

## Select UV islands

Use a model's UV layout to paint individual parts of its texture:

1. Enable **UV** in the preview footer and assign a **Mesh**. Expand the model asset in Project to find its meshes.
2. Choose **UV Channel** (usually **UV0**) and optionally a **Submesh** to show one material slot.
3. Hold or drag **Area Select** (`M`) and release over **UV Island**, or click **Select UV Islands** in the UV panel.
4. Click anywhere inside an island. `Shift` adds islands, `Alt` subtracts, and `Shift+Alt` intersects.
5. Switch to Brush, Pencil or Fill to work inside the selected area. Copy and cut work with the same selection; `Ctrl+D` clears it.

Only island boundaries are drawn, including holes; triangle diagonals are hidden. Small dots and hover highlights appear only in UV Island mode, so they do not intercept brush strokes. Holes remain outside the selection.

The Mesh reference and channel/submesh choices are saved with the document. **Line Color** and **Opacity** control the overlay. Close the side panel to keep drawing with the outlines visible; disable **UV** to hide them without clearing the selection. The overlay is never included in saved texture pixels or exports and does not edit the mesh.

Only the **0–1 UV tile** on the main canvas is shown and selectable, including when Tiled preview is enabled. If islands overlap, they share the same texture pixels: painting changes every model surface using those coordinates. Clicking an overlap consistently chooses the first matching island. **Refresh UV** reloads the layout if a procedural mesh changed without updating its asset.

## Fill from existing texture details

Select the area to rebuild, then click **Content-Aware Fill** above the canvas while a selection tool is active.
It reuses details already in the image: useful for filling holes, extending texture patterns and touching up UV borders, not for inventing new objects.

1. Choose **Source**: **Visible Composition**, or **Selected Layer** (the layer selected when the fill window opened).
2. Set **Fill Area** to **Entire Selection**, or **Inner Border**. **Border Width (px)** fills a strip inward from the selection contour, leaving the center and everything outside untouched.
3. Enable **Transparent Only** to keep visible pixels and fill only empty areas.
4. Choose where to sample: **Nearby** with **Sampling Distance (px)**, **Whole Image**, or **Custom Selection**. For a custom source, make another selection in the main window, then click **Use Current Selection as Sampling Area**. Your original fill area stays fixed.
5. Click **Preview**. Compare with **Show Before**, or try **New Variation**. Higher **Quality** takes longer.
6. Click **Apply** to add the filled pixels as a new Drawing layer above the stack. **Cancel** stops processing without changing the document.

Sampling Distance only changes where details are borrowed from; it does not expand the fill area. Keep unwanted objects out of the sampling region. Source pixels must be visible and outside the pixels being filled; the untouched center of an Inner Border can also supply details.

Enable **Invert Selection** to fill outside the captured selection instead. Inversion happens before **Inner Border**, so its strip follows the inverted area (including the canvas boundary). The selection on the canvas and a custom sampling selection stay unchanged. Selecting the entire canvas and then inverting leaves nothing to fill.

This is a flat-texture operation: it does not match corresponding edges across a model's 3D seams. Large or very structured missing areas may need smaller selections and several passes. The selection and sampling region's combined bounding rectangle is limited to 4 million pixels per pass. If the source changes while the window is open, generate a fresh preview before applying.

## Select a layer on the canvas

Choose **Layer Select** (`V`, formerly No Tool) and click the image. The topmost layer whose alpha
meets **Alpha ≥ %** is selected; transparent areas let you pick layers below. The default threshold is **10%**.
Change it in the tool's preview toolbar or **User Settings → Layer Select**; both controls share the same preference.
Even at 0%, fully transparent pixels are ignored.

- `Shift+click` toggles a layer in the selection. `Shift+click` on empty space leaves the selection unchanged.
- Click a group to select it, then click again to pick a child under the cursor. Repeated clicks enter nested groups one level at a time; `Ctrl+click` picks a nested layer directly. Its parent groups open in Layers.
- Click empty space to deselect. Picking does not move or paint anything.

Picking respects transforms, layer FX, Swizzle, clipping masks and layer/group opacity. Hidden layers are skipped.
It uses each layer's own alpha, not the final color produced by its blend mode or preview Post FX.

## Select a layer's shape

Hold `Ctrl` and click a layer thumbnail or the group's arrow.
The selection follows its visible shape, including soft edges, rather than its rectangular bounds.
You can use a hidden layer as the shape.

## Copy and paste

| Shortcut | Action |
| :--- | :--- |
| `Ctrl+C` | Copy the selected area from the active layer; without an area selection, copy the selected layers. |
| `Ctrl+Shift+C` | Copy what is visible in that area across all layers. |
| `Ctrl+V` | Insert copied layers, or paste copied pixels onto a new Drawing layer. |

To transfer editable layers, deselect the canvas with `Ctrl+D`, select one or more rows in **Layers**, then press `Ctrl+C`.
Switch to another WhimTex window and press `Ctrl+V`. Copies appear at the top of the stack with their names,
settings, group contents, embedded FX and Drawing pixels. Each paste is independent; the source window can be closed after copying.
Drawing textures keep their stored resolution. Layer transforms keep their values on the destination canvas.
Copy effects together with their target layers to preserve those links; targets outside the copied set must be assigned again.
The layer clipboard lasts until another copy, a script reload or closing Unity. Text fields keep normal text copy/paste.

With an area selection, only its pixels are copied. On a canvas of the same size, these keep their position;
on a different-sized canvas, they are centered. `Ctrl+Shift+C` without an area selection copies the whole visible canvas as pixels.

On Windows, you can also copy an image in another application or take a screenshot with `Win+Shift+S`,
then click the WhimTex preview and press `Ctrl+V`. It becomes a new Drawing layer, centered at its original
pixel size without cropping the stored image. In an empty document, the canvas takes the image's dimensions.
PNG transparency is preserved. System image paste supports images up to 16 megapixels and your GPU's texture-size limit.
Copying inside WhimTex takes priority until you copy something else to the system clipboard.
This imports image contents, not a copied file path or a web link; drag a file into the preview to import it instead.

A selection stays active when you change tools or layers. It is not saved with the document.
If painting seems blocked, try `Ctrl+D`.
