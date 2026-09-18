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

1. Enable **Tiled** in the preview footer.
2. Choose Brush or Pencil and paint on any visible copy.
3. Paint across a border: the clipped part continues on the opposite edge.
4. Zoom out to check the repeated pattern.

Tiled shows repetitions and wraps strokes across edges, but does not enlarge the canvas.
Layer transforms and export size stay unchanged. See [Effect layers](effects.md) for effect **Edges** settings.

## Which repeat setting do I need?

- **Symmetry & Repeat** makes copies of new strokes.
- **Transform → Tiling** repeats an existing layer image.
- **Tiled preview** shows copies of the whole canvas and lets you paint across its edges.
