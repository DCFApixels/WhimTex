---
title: "Transform and rasterize"
parent: "English"
nav_order: 3
lang: "en"
permalink: "/en/transform/"
translations: "en/transform.md,ru/transform.md,zh/transform.md"
previous_page: "en/layers.md"
next_page: "en/painting.md"
---

# Transform and rasterize

Choose **Transform** (`T`) to move, resize or rotate a layer.
Drag the frame to move it, an edge or corner to resize, and the round handle to rotate.
For exact values, expand **Transform** in Layer Settings.

**Original Aspect** restores the image's original proportions.

**Original Size** in the Transform toolbar also restores its pixel size: one source pixel equals one canvas pixel.
It preserves the image center, rotation, pivot and flips. Generated layers use the canvas size.
**Reset** returns the transform to its starting state.

## Position and pivot

The gold pivot is the point around which the layer rotates and scales.
Drag it to a new position without moving the image.

The pivot and transform snap to nearby guide points. Hold `Ctrl` to move freely.
Hold `Shift` to move along one axis, resize proportionally or rotate in 15° steps.
`Escape` cancels a drag; `T` or `Enter` leaves the tool.

## Repeat an image or keep crisp edges

**Tiling** controls what appears beyond the image's original bounds:

- **Source** follows the source texture's setting.
- **Clip** leaves the outside transparent.
- **Repeat** tiles the image.
- **Mirror** alternates reflected copies.
- **Clamp** stretches the outermost pixels beyond the image bounds.
- **Unbounded** continues Noise, Gradient, Color Fill and Shape calculations outside the frame.
  File, Drawing and raster effects use Clip instead. Gradient colors still follow their keys;
  this does not extrapolate new colors beyond the first or last key.

**Filter** controls edge smoothness. Choose **Point** for pixel art or **Bilinear** for smooth scaling.
**Source** follows the texture's filter; **Trilinear** also smooths transitions between mip levels when present.

To paint across the canvas edges instead, use [Tiled preview](symmetry.md).

## Merge layers or convert them to Drawing

Use **Merge** in the row menu or `Ctrl+E` to replace selected layers with one Drawing layer.
`Ctrl+Alt+E` makes a merged copy and keeps the originals.
The merged result includes visible layers, their transforms and effects.

Merging only part of an image can change how it blends with the remaining layers.
Make a copy first if you want to compare.

**Convert to Drawing** makes a layer paintable. It also works on an existing Drawing layer:

| Mode | What happens |
| :--- | :--- |
| **Keep Transform** | Turn the source into pixels and keep its transform. |
| **Apply Transform** | Keep its current appearance in the pixels and reset the transform. |

Converting a group combines its visible contents. Effects targeting individual layers
inside that group will need a new target.
