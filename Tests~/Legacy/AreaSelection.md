# Area selection validation

Compile manually in the Unity Editor first. Do not trigger an import, refresh, build or
compilation from these checks. Run scripts only when editor-side testing is authorized.

- `CanvasSelectionSmoke.cs` checks rectangle/polygon rasterization, soft coverage,
  combination modes, empty selections, and wrapping. It creates no Unity assets.
- `AreaSelectionPaintSmoke.cs` checks GPU selection clipping for Brush/Pencil/eraser,
  a rotated Drawing layer, and alpha/copy source rendering. It uses transient objects,
  does not save/import assets, and does not register Undo operations.

## Window checks

Use a disposable document; do not overwrite an existing asset.

1. Press `M`, drag a rectangle, switch to Brush/Pencil, and paint/erase across its boundary.
   Only the selected pixels change. Repeat with Fill, both contiguous and non-contiguous.
2. Press `L`, click at least three vertices. Close with `Enter`, double-click, or the first
   vertex. `Backspace`/RMB removes a vertex; `Escape` cancels without replacing the old selection.
3. Check Replace/Add/Subtract/Intersect, modifier overrides, All, Invert and Deselect.
   An active but empty mask blocks painting; deselecting restores unrestricted painting.
4. Ctrl-click a thumbnail (group arrow for groups). Verify soft alpha, transformed layers,
   effects, hidden layers and zero-opacity layers. The selected layer's own visibility and
   opacity do not affect alpha selection. Ctrl-click elsewhere still toggles layer selection.
5. Copy an area from a layer, then Copy Merged. Paste creates a new root Drawing layer with
   identity transform and preserved pixels/position. Undo removes it; Redo restores it.
   Test HDR values and a soft-alpha selection. Paste does not apply the mask a second time.
6. Copy without an area selection: the full canvas is copied. Paste into a differently sized
   document: copied bounds are centered and clipped to the destination canvas.
7. Type and copy/paste inside layer names, numeric fields and shader code: normal text editing
   wins over canvas shortcuts. Selection changes do not dirty the document or enter Undo.
8. Zoom/pan with a selection, resize the window, and switch tools. Contours remain aligned and
   clipped to the preview; moving the pointer does not rebuild the UI.
9. In Tiled preview, select across a tile seam and paint at either copy. Check a rotated/scaled
   Drawing layer without applying its transform. The mask wraps in document coordinates.
10. Switch documents or resize the canvas: the previous area selection is discarded.
    Window close/domain reload also clears the selection; the internal clipboard survives
    window changes but not domain reload/editor exit.

The full-resolution mask is limited to 16,777,216 pixels. Very complex contour displays use
bounded LOD; this must not change painting or copied coverage. Polygon selection supports up
to 256 vertices and, in Tiled preview, a vertical span of 16 canvas heights.
