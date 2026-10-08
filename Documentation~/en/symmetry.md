---
title: "Symmetry and seamless painting"
parent: "English"
nav_order: 6
lang: "en"
description: "Paint seamless textures and repeating patterns in Unity with WhimTex. Use tiled preview, wrapped brush strokes, Mirror and Radial symmetry."
permalink: "/en/symmetry/"
translations: "en/symmetry.md,ru/symmetry.md,zh/symmetry.md"
previous_page: "en/selection.md"
next_page: "en/effects.md"
---

# Symmetry and seamless painting

Use symmetry to paint matching details, ornaments or repeated shapes.
Use Tiled view to paint a texture that joins at its edges.

## Repeat a stroke

Select a Drawing layer and open **Symmetry & Repeat** in Layer Settings.
Each Drawing layer can have its own setup.

| Mode | Use it for |
| :--- | :--- |
| Mirror | **Mirror X** and **Mirror Y** toggle reflections, not axes. X reflects across a vertical line through **Center**; Y across a horizontal line. **Angle** rotates both lines. |
| Horizontal / Vertical | A row or column of copies. |
| Grid | Copies in rows and columns. |
| Radial | Copies around Center. Choose the number of sectors and rotate Start Angle to position them. |
| None | Ordinary painting without copies. |

**Count** sets the number of copies (2–64); Grid uses **Count X** for columns and **Count Y** for rows.

In repeat modes, choose **Copy** for identical copies or **Alternate Mirror**
to reflect every second copy.

## Keep strokes inside a segment

**Edges → Clip** keeps each stroke inside the segment where you started it.
Choose **Continue** when you want a stroke to travel into neighboring segments.

## Paint seamless edges

1. Enable **Tiled** in the Canvas View footer.
2. Choose Brush or Pencil and paint on any visible copy.
3. Paint across a border: the clipped part continues on the opposite edge.
4. Zoom out to check the repeated pattern.

Tiled shows repetitions and wraps strokes across edges, but does not enlarge the canvas.
Layer transforms and export size stay unchanged. See [Effect layers](effects.md) for effect **Edges** settings.

## Which repeat setting do I need?

| Setting | What repeats |
| :--- | :--- |
| Drawing: **Symmetry & Repeat** | New strokes within this layer. |
| Layer: **Transform → Tiling** | The existing layer image outside its bounds. |
| Canvas View: **Tiled** | The whole canvas for inspection and wrapped painting. It does not remove an existing seam. |
| Noise: **Seamless** | The generated noise joins at opposite edges. See [Noise](noise.md). |
| Effect Layer: **Edges / Source Edges → Repeat** | The input image across its borders, for operations such as blur. |
| Distortion FX: **Input Tiling / Tiling** | The incoming image when distorted coordinates leave its bounds. See [Distortion](shader-fx.md#distortion-presets). |
| Displacement Map: **Map Wrap** | The displacement map, independently of the incoming image. |
| Gradient: **Wrap** | The palette outside its 0–1 range, not the canvas image. |

For a painted seamless texture, create Noise with **Seamless** enabled, add a Drawing layer above it,
then enable **Tiled** and paint across the borders. The Noise layer stays editable.
To paint directly into its pixels, [convert it to Drawing](transform.md#merge-layers-or-convert-them-to-drawing) first.
