---
title: "Area selections"
parent: "English"
nav_order: 5
lang: "en"
permalink: "/en/selection/"
translations: "en/selection.md,ru/selection.md,zh/selection.md"
previous_page: "en/painting.md"
next_page: "en/symmetry.md"
---

# Area selections

Select an area to paint, erase or fill without touching the rest of the image.
Use **Area Select** (`M`) for a rectangle or ellipse, **Polygonal Lasso** for an outline through placed points, or **Freehand Lasso** for a drawn outline. `L` returns to the chosen lasso mode.

Use the edge mode dropdown above the canvas to control the selection boundary:

- **Aliased** (default) selects each pixel fully or leaves it unselected, keeping a hard pixel edge.
- **Antialiased** partially selects pixels along the boundary for smoother edges. It does not add feathering.

The setting is shared by Rectangle, Ellipse, both lasso modes and UV Island Select, and stays selected when you switch tools. Changing it affects the next selection; an outline already being drawn keeps the mode chosen when it started. Existing selections keep their edges.

## Make and adjust a selection

Hold or drag the Area Select toolbar button to open its shape list. Release over **Rectangle** or **Ellipse**, then drag on the canvas. The button shows the chosen shape; `M` returns to it.

Hold or drag the Lasso toolbar button to choose **Polygonal Lasso** or **Freehand Lasso**, then release over the mode you want.

With **Freehand Lasso**, hold the left mouse button and draw around the area. Release to close the outline from its endpoint to its start and apply the selection. `Escape` cancels the current outline.

With **Polygonal Lasso**, click around the outline and finish with
`Enter`, a double-click at the same position when adding a new point, or a click on the first point. Double-clicking an older point or segment does not finish the outline.
`Backspace` or a right-click removes the last point; `Escape` cancels the unfinished outline.
Small squares mark the placed points. The outline stays open, with its last segment following the cursor, until you finish the selection.
Choose **Straight** for this behavior, or **Smooth** above the canvas for an automatically
smoothed Catmull–Rom curve through every placed point. With three or more points,
Smooth calculates the curve as a closed shape but displays an open outline: the
closing curve from the last point to the first stays hidden until you finish.
The visible curves keep their shape on completion. The faint blue line to the cursor
only helps place the next point. Moving or inserting points updates the outline
immediately; points can be inserted only on visible curves. You can switch
Straight/Smooth while editing the outline.

In Smooth, round markers are smooth points and square markers are corners. Hover a
point and press `C` to toggle its kind. The **Corner** checkbox edits the selected
point, highlighted in blue; placing, inserting or dragging a point selects it.
With no point under the cursor, `C` edits that selected point. The boundary preview
and the applied selection share the same contour; **Aliased / Antialiased** still
controls how its edge covers pixels. A smooth curve can extend beyond the straight
sides between points.
Once the outline has moved away from its start and has at least three points, move near the first point: it turns blue and the endpoint snaps to it. Click and release to close; dragging the first point moves it instead.

Before finishing, you can adjust the placed points:

- Drag a point marker to move its point.
- Click an existing segment to insert a point. The segment following the cursor does not accept inserted points.
- `Alt+click` a point marker to remove its point and reconnect its neighbors.

When placing or inserting a point, keep the mouse button held and drag to position it immediately.
Hold `Shift` when adding or dragging a new endpoint to constrain its segment to the nearest horizontal, vertical or 45° diagonal direction relative to the canvas, even with a rotated view. `Ctrl+Shift` keeps this direction constraint while disabling snapping.
When dragging an already placed point, `Shift` constrains its movement to those directions from its position at the start of the drag. Press `Ctrl` during the drag to disable guide snapping while keeping the angle constraint.

A short click near the last point continues the outline. Hold `Ctrl` before pressing the mouse button to place a new point even over an existing point or segment, without snapping or closing. During a point drag that has already started, `Ctrl` only disables snapping.

For a **square or circle**, start dragging, then hold `Shift`. Release `Shift` to return to free proportions. Holding `Shift` before starting instead adds to the selection. To add a square or circle, choose **Add** above the canvas and press `Shift` during the drag.

The controls above the canvas choose **Add**, **Subtract** or **Intersect** (keep the overlap).
With no mode selected, a new drag replaces the selection. You can also hold:

- `Shift` to add an area.
- `Alt` to subtract.
- `Shift+Alt` to keep only the overlap.

For Lasso, these modifiers choose the operation when you place the first point or start the freehand drag; editing or drawing the outline does not change that operation.

`Ctrl+A` selects the whole canvas. `Ctrl+Shift+I` selects the opposite area.
`Ctrl+D` removes the selection so you can paint everywhere again.
The selection survives tool and layer changes, but is not saved with the document.

## Quick Selection

Choose **Quick Selection** (`W`) and brush inside an area. The tool looks for a boundary
along colour changes while avoiding unnecessary expansion. A stroke on a flat background
stays local without needing a Subtract stroke. Brush over another area to include it.
Fine details missed by the first result can be guided with another Add or Subtract stroke.

- **Add** includes regions; **Subtract** removes them. Paint with the right mouse button or hold `Alt` to subtract temporarily.
- **Size**, or `[` / `]`, changes the brush diameter in canvas pixels.
- **Strength** controls how firmly a stroke marks foreground or background. At 100% the
  hint is fixed; lower values can yield to opposing hints. At 0% the brush clears hints
  under it. This changes boundary guidance, not selection opacity.
- **Tolerance** controls how much colour variation can join a region. Reduce it when the
  selection crosses a subtle boundary; raise it to include a larger tonal range.
- **Visible Layers** samples the composite; **Current Layer** samples the selected layer,
  including its effects.

Add and Subtract strokes remain as foreground/background hints throughout the selection.
Brush Subtract on unwanted background, then continue Add inside the object: both sets of
hints guide the boundary together. You can mark background before making the first selection.
The latest stroke replaces earlier hints under the brush; hints elsewhere remain.
**Deselect** (`Ctrl+D`) clears both hints and the result.
Returning to the tool keeps the hints unless the source image or selection was changed.

While brushing, **Preview** shows an approximate contour, including on 8192 × 8192 canvases.
After a short pause or release, **Refining** adjusts the boundary to the original pixels.
Keep brushing to correct it: new hints interrupt the previous refinement. Fine details can
appear during refinement. Switching tools or copying waits for the refined selection.
**Stop** or `Escape` cancels unfinished work and pending actions, removes the preview and
keeps only the selection already committed. Changing the source image or making another
kind of selection also stops unfinished work.
The tool changes only the selection, never source pixels. Its boundary follows the native
pixel grid; the Rectangle/Lasso antialias setting does not change Quick Selection.
Very detailed outlines may use a coarser, connected display contour. This does not reduce
the precision of the selection mask itself.

## Select UV islands

Use a model's UV layout to paint individual parts of its texture:

1. Open the **UV** panel, check **UV** in its header and assign a mesh or model from Project to **Mesh**. Models automatically use the first mesh in their hierarchy, including inactive nodes. To choose another mesh, expand the model and assign that mesh directly.
2. Choose **UV Channel** (usually **UV0**) and optionally a **Submesh** to show one material slot.
3. Select **UV Island Select** below the toolbar separator, or click **Select UV Islands** in the UV panel. Enabling UV makes the tool available without selecting it.
4. Click anywhere inside an island. `Shift` adds islands, `Alt` subtracts, and `Shift+Alt` intersects.
5. Switch to Brush, Pencil or Fill to work inside the selected area. Copy and cut work with the same selection; `Ctrl+D` clears it.

Only island boundaries are drawn, without internal triangle diagonals. Small dots and hover highlights appear only in UV Island mode, so they do not intercept brush strokes.

**Line Color** and **Opacity** control the overlay. Close the side panel to keep drawing with the outlines visible; disable **UV** to hide them without clearing the selection. The overlay is never included in saved texture pixels or exports and does not edit the mesh.

Only the **0–1 UV tile** on the main canvas is shown and selectable, including when Tiled preview is enabled. If islands overlap, they share the same texture pixels: painting changes every model surface using those coordinates. Clicking an overlap consistently chooses the first matching island. **Refresh UV** reloads the layout if a procedural mesh changed without updating its asset.

## Fill from existing texture details

Select the area to rebuild, then click **Content-Aware Fill** above the canvas while a selection tool is active.
It reuses details already in the image: useful for filling holes, extending texture patterns and touching up UV borders, not for inventing new objects.

Choose a sampling area with similar colors and texture. The fill tries different patches to reduce repetition,
but limited source detail can still produce repeated shapes. Unique lighting gradients or missing features
without a similar sample may need manual correction.

1. Choose **Source**: **Visible Composition**, or **Selected Layer** (the layer selected when the fill window opened).
2. Set **Fill Area** to **Entire Selection**, or **Inner Border**. **Border Width (px)** fills a strip inward from the selection contour, leaving the center and everything outside untouched.
3. Enable **Transparent Only** to keep visible pixels and fill only empty areas.
4. Choose where to sample: **Nearby** with **Sampling Distance (px)**, **Whole Image**, or **Custom Selection**. For a custom source, make another selection in the main window, then click **Use Current Selection as Sampling Area**. Your original fill area stays fixed.
5. Click **Preview**. Compare with **Show Before**, or try **New Variation**. Higher **Quality** takes longer.
6. Click **Apply** to add the filled pixels as a new Drawing layer above the stack. **Cancel** stops processing without changing the document; once processing finishes, the button becomes **Close** and closes the window.

Sampling Distance only changes where details are borrowed from; it does not expand the fill area. Keep unwanted objects out of the sampling region. Source pixels must be visible and outside the pixels being filled; the untouched center of an Inner Border can also supply details.

Enable **Invert Selection** to fill outside the captured selection instead. Inversion happens before **Inner Border**, so its strip follows the inverted area (including the canvas boundary). The selection on the canvas and a custom sampling selection stay unchanged. Selecting the entire canvas and then inverting leaves nothing to fill.

This is a flat-texture operation: it does not match corresponding edges across a model's 3D seams. Large or very structured missing areas may need smaller selections and several passes. The selection and sampling region's combined bounding rectangle is limited to 4 million pixels per pass. If the source changes while the window is open, generate a fresh preview before applying.

## Select a layer on the canvas

Choose **Layer Select** (`V`) and click the image. The topmost layer whose alpha
meets **Alpha ≥ %** is selected; transparent areas let you pick layers below. The default threshold is **10%**.
Change it in the tool settings in Canvas View or **User Settings → Layer Select**; both controls share the same preference.
Even at 0%, fully transparent pixels are ignored.

- `Shift+click` toggles a layer in the selection. `Shift+click` on empty space leaves the selection unchanged.
- Repeat clicks in the same place to select covered layers from top to bottom, then wrap to the first hit. Clicking elsewhere or changing tools restarts at the top.
- Click a group to select it, then click again to pick a child under the cursor. Repeated clicks enter nested groups one level at a time; `Ctrl+click` picks a nested layer directly. Its parent groups open in Layers.
- Click empty space to deselect. Picking does not move or paint anything.

Picking respects transforms, layer FX, Mapping, clipping masks and layer/group opacity. Hidden layers are skipped.
It uses each layer's own alpha, not the final color produced by its blend mode or preview Post FX.

## Select a layer's shape

Hold `Ctrl` and click a layer thumbnail or the group's arrow.
The selection follows its visible shape, including soft edges, rather than its rectangular bounds.
You can use a hidden layer as the shape.

## Copy and paste

| Shortcut | Action |
| :--- | :--- |
| `Ctrl+C` | Copy the selected area from the active layer; without an area selection, copy the selected layers. |
| `Ctrl+Shift+C` | Copy the visible composition in the selected area, or the whole canvas when there is no area selection. |
| `Ctrl+V` | Insert copied layers, or paste copied pixels onto a new Drawing layer. |

To transfer editable layers, deselect the canvas with `Ctrl+D`, select one or more rows in **Layers**, then press `Ctrl+C`.
Switch to another WhimTex window and press `Ctrl+V`. Copies appear at the top of the stack with their names,
settings, group contents, embedded FX and Drawing pixels. Each paste is independent; the source window can be closed after copying.
Drawing textures keep their original resolution. Layer transforms are copied as-is, without adjusting to the destination canvas size.
Copy effects together with their target layers to preserve those links; targets outside the copied set must be assigned again.
The layer clipboard lasts until another copy, a script reload or closing Unity. Text fields keep normal text copy/paste.

With an area selection, only its pixels are copied. On a canvas of the same size, these keep their position;
on a different-sized canvas, they are centered. `Ctrl+Shift+C` without an area selection copies the whole visible canvas as pixels.

On Windows, these pixel-copy commands also place a PNG image on the system clipboard for pasting into other applications.
The image preserves transparency and is cropped to the copied region's bounding rectangle.
WhimTex keeps an HDR copy and its original canvas position for pasting between its windows in the same Unity session,
while that copy remains on the clipboard. Copying something else replaces it; the next paste uses the new clipboard content.
Editable-layer copies made with `Ctrl+C` without an area selection stay in WhimTex's layer clipboard.
On macOS and Linux, pixel copying currently works within WhimTex.

On Windows, you can also copy an image in another application or take a screenshot with `Win+Shift+S`,
then click Canvas View and press `Ctrl+V`. It becomes a new Drawing layer, centered at its original
pixel size without cropping the stored image. In an empty document, the canvas takes the image's dimensions.
PNG transparency is preserved. System image paste supports images up to 16 megapixels and your GPU's texture-size limit.
You can also copy a direct HTTP(S) image link and press `Ctrl+V`: WhimTex downloads the PNG or JPEG
as an independent Drawing layer, keeping its original pixels and transparency. It fits inside the canvas,
centered with its proportions preserved, using the layer transform rather than resizing the image. The same size limits apply;
downloads are limited to 64 MB. Links to web pages are not supported. Closing the window or switching documents
cancels the download. For a local file, first drag it into Project, then into Canvas View.
Old linked-image JSON is unsupported and is not migrated. Edit it with a matching older checkout;
new recipes use the [current JSON contract](../JSON_FORMAT.md).
Current `whimtex.document` JSON does not download images or store Drawing pixels. Paste an image link
directly as described above, or use verified project asset references in [document JSON](ai-authoring.md).

If painting seems blocked, try `Ctrl+D`.
