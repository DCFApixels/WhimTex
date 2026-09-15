---
title: "Blending and clipping"
parent: "English"
nav_order: 8
lang: "en"
permalink: "/en/blending/"
translations: "en/blending.md,ru/blending.md,zh/blending.md"
previous_page: "en/effects.md"
next_page: "en/shader-fx.md"
---

# Blending and clipping

Use **Blend** in a layer row to change how its colors combine with the image below.
Lower **Opacity** to make the result less pronounced.

## Choose a blend

Start with these common choices:

| Blend | Typical use |
| :--- | :--- |
| Normal | Place the layer over the image, respecting its transparency. |
| Multiply | Add shadows or darken a texture. |
| Screen | Lighten an image or add a soft glow. |
| Add | Add bright highlights and luminous details. |
| Overlay / Soft Light | **Overlay** strongly increases contrast; **Soft Light** adds gentler tint and light while retaining detail. |
| Darken / Lighten | Keep the darker or lighter parts of the two layers. |
| Difference | Compare two images or create contrasting patterns. |

**Overwrite** replaces the image below entirely, including transparent pixels: empty parts of the layer erase the image underneath.
**None** leaves it unchanged.
**Subtract** subtracts color values, **Divide** divides them, **Color Dodge** brightens the image,
and **Linear Light** produces strong contrast.

For groups, **Pass Through** lets the contents blend with layers outside the group.
Choose another mode to treat the group as one image.

## Keep paint inside another layer's shape

Clipping is useful for shading a character, adding texture to lettering or coloring a silhouette:

1. Put the shape layer below the layer you want to clip.
2. Open the upper layer's **⋮** menu and enable **Clipping Mask**.
3. Paint or add effects on the upper layer: they remain inside the base shape.

You can also `Alt`-click the boundary above the base layer to toggle clipping.
Several consecutive clipped layers can use the same base. Keep them with their base in one group
or at the top level: clipping does not look for a source outside its group.
If the base is hidden or empty, its clipped layers disappear too.
Without a base below it, a clipped layer is invisible and its tooltip reads **Clipping Mask: no base below this layer**.
