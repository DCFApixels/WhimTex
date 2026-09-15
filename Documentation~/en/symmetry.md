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
| Mirror | Reflected strokes across X, Y or both axes. Move Center and rotate Angle to place the axes. |
| Horizontal / Vertical | A row or column of copies. |
| Grid | Copies in rows and columns. |
| Radial | Copies around Center. Choose the number of sectors and rotate Start Angle to position them. |
| None | Ordinary painting without copies. |

In repeat modes, choose **Copy** for identical copies or **Alternate Mirror**
to reflect every second copy.

## Keep strokes inside a segment

**Edges → Clip** keeps each stroke inside the segment where you started it.
Choose **Continue** when you want a stroke to travel into neighboring segments.

For Mirror at 0°, X reflects across a vertical line and Y across a horizontal line.
Rotate the angle when you need a diagonal axis.

## Paint seamless edges

1. Enable **Tiled** above the Preview.
2. Choose Brush or Pencil and paint on any visible copy.
3. Paint across a border: the clipped part continues on the opposite edge.
4. Zoom out to check the repeated pattern.

Tiled does not enlarge the saved image.
For effects such as blur, also choose **Edges → Repeat** to avoid seams at their borders.

## Which repeat setting do I need?

- **Symmetry & Repeat** makes copies of new strokes.
- **Transform → Tiling** repeats an existing layer image.
- **Tiled preview** shows copies of the whole canvas and lets you paint across its edges.
