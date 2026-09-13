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
Use **Rectangle Select** (`M`) for a rectangle or **Polygonal Lasso** (`L`) for a shape with straight sides.

## Make and adjust a selection

Drag with Rectangle Select. With Lasso, click around the outline and finish with
`Enter`, a double-click or a click on the first point.
`Backspace` or a right-click removes the last point; `Escape` cancels the unfinished outline.

Choose **Replace**, **Add**, **Subtract** or **Intersect** above the canvas.
You can also hold:

- `Shift` to add an area.
- `Alt` to subtract.
- `Shift+Alt` to keep only the overlap.

`Ctrl+A` selects the whole canvas. `Ctrl+Shift+I` selects the opposite area.
`Ctrl+D` removes the selection so you can paint everywhere again.

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
| `Ctrl+C` | Copy the selected area from the active layer. |
| `Ctrl+Shift+C` | Copy what is visible in that area across all layers. |
| `Ctrl+V` | Paste onto a new Drawing layer. |

Without a selection, copy uses the whole canvas. You can paste between WhimTex windows.
On a canvas of the same size, the copy keeps its position; on a different-sized canvas, it is centered.

On Windows, you can also copy an image in another application or take a screenshot with `Win+Shift+S`,
then click the WhimTex preview and press `Ctrl+V`. It becomes a new Drawing layer, centered at its original
pixel size without cropping the stored image. In an empty document, the canvas takes the image's dimensions.
PNG transparency is preserved. System image paste supports images up to 16 megapixels and your GPU's texture-size limit.
Copying inside WhimTex takes priority until you copy something else to the system clipboard.
This imports image contents, not a copied file path or a web link; drag a file into the preview to import it instead.

A selection stays active when you change tools or layers. It is not saved with the document.
If painting seems blocked, try `Ctrl+D`.
