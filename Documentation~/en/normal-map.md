---
title: "Normal Map"
parent: "Effect layers"
nav_order: 1
lang: "en"
permalink: "/en/normal-map/"
grand_parent: "English"
translations: "en/normal-map.md,ru/normal-map.md,zh/normal-map.md"
---

# Normal Map

A normal map makes a surface appear raised or indented when lit in a material.
Start in **Simple** for the common controls; open **Advanced** when you need finer adjustments.
Returning to Simple keeps your advanced settings.

## From a height map

1. Add Normal Map above File, Drawing or Noise.
2. Leave **Input → Previous**, or assign another Target.
3. Choose **Generation → Height Map** and the channel that contains the height.
4. Start with **Strength** 4 and **Smoothing** 1 px, then adjust the relief.
5. Hide the source layer if you want to see only the normal map.

Light areas are high; dark areas are low. **Invert Height** reverses them.
**Strength** makes the relief stronger; **Smoothing** softens fine bumps.
For a Noise source with Linear Data encoding, choose **Input Space → Linear** in Advanced.

## From a color texture

Choose **Generation → Texture** to estimate relief from an ordinary image.
This is a starting point, not an exact reconstruction: shadows or painted color changes can look like dents.

In Advanced, choose **Output → Height** to check the inferred height.
Adjust **Fine / Medium / Large Detail** for small texture, medium features and broad shapes.
Try **Light Removal** if lighting in the source is creating unwanted slopes.
Return **Output** to **Normal** when you are happy with the surface.

## Useful finishing controls

- **Height Levels:** adjust Black/White Level and Gamma if the relief is too flat or too harsh.
- **Edges → Repeat:** use for a seamless source.
- **Alpha → Opaque:** make a solid map; **Source** keeps the source transparency.
- **Flip Y:** try this if the target material shows bumps as dents.

## Save for a material

For a normal map on its own, keep **Normal** blending, full opacity and unchanged Swizzle.
Avoid color effects, which can distort the relief.

Choose **Packed Color** for PNG/TGA/PSD. Import the exported PNG or TGA into Unity as
**Normal Map**, without grayscale conversion.
Choose **Linear Data** when your workflow needs a linear EXR or Texture2D.

Rotating a finished normal-map layer rotates its image, not the direction of its lighting.
For correctly oriented relief, transform the source before generating the map.
