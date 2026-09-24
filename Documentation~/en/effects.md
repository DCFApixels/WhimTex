---
title: "Effect layers"
parent: "English"
has_children: true
nav_order: 7
lang: "en"
description: "Build texture effects with WhimTex in Unity. Apply Outline, SDF, Normal Map, Gaussian Blur and Motion Blur to layers or groups while keeping sources editable."
permalink: "/en/effects/"
translations: "en/effects.md,ru/effects.md,zh/effects.md"
previous_page: "en/symmetry.md"
next_page: "en/blending.md"
---

# Effect layers

Effect layers create outlines, soften images or turn texture detail into surface relief.
They keep the source editable, so you can adjust it without rebuilding the effect.

## Choose an effect

Use **+** at the bottom of Layers:

| Goal | Layer | Start with |
| :--- | :--- | :--- |
| Outline a shape | Outline | Width, softness and inside/outside placement. |
| Make a mask based on distance from an edge | SDF | Source channel, Threshold and Max Distance. |
| Create surface relief | Normal Map | Height Map or Texture. See [Normal Map](normal-map.md). |
| Soften an image | Blur | Mode → Gaussian, then Radius. |
| Create a motion trail | Blur | Mode → Linear or Circular. |
| Restore edge contrast | Sharpen | Strength and Radius. |
| Join opposite texture edges | Make Seamless | Source edges, Fade Width and Falloff. |

## Choose what the effect uses

**Input → Previous** uses the layer directly below the effect in the same group.
Choose **Specific** to use another layer or group. Click **Target** to choose from the list,
or drag a layer into the field. **None (Layer)** means no source is selected.
When dragging several selected layers, the active one becomes the target.

You can hide the source and still see the effect. For a group, hide the group itself,
not the children you want included. The effect uses only that group's contents, not the background behind it.

## Outline and SDF

Use **Outline** for a border around a shape. Adjust its width and softness,
choose **Source Channel** (Alpha by default, or Red, Green, Blue or Luminance),
then choose whether it sits inside, outside or across the edge.

Use **SDF** when you want a gradual transition based on distance from the shape.
**Threshold** sets the contour threshold; **Max Distance (px, 0 = auto)** sets the transition distance.
At 0, the distance is chosen automatically. **Position** selects which side receives the gradient:
Outside covers the outside, Inside the inside, and Center both sides.
The default, **Signed**, covers both sides of the contour. **Inverted** reverses the gradient direction.

**Source Offset (px)** shifts the input while retaining the influence of contours moved outside the canvas. **Source Edges** extends the image as Transparent (default), Clamp, Repeat (including distances across seams), or Mirror. It cannot recover details already clipped by the source layer.

**Contour Offset (px)** expands the contour when positive and shrinks it when negative. In **Signed**, **Inside Distance** and **Outside Distance** independently set the interior/exterior range; 0 inherits Max Distance or auto. The contour stays at the gradient midpoint. **Profile** remaps the transition after Inverted and before gradient coloring; linear leaves it unchanged. For bevel lighting, start with a grayscale gradient and shape its height profile here.

Large offsets and Repeat use more memory. Extended domains above 64 million pixels report an error rather than silently cropping; reduce offset or resolution if needed.
The distance algorithm changes the character of corners and diagonals:
Euclidean gives rounded distances, while Manhattan and Chebyshev give more angular results.

For a source with smooth, partially transparent edges, choose **Distance Algorithm → Euclidean Antialiased**
in either SDF or Outline. Outline follows the 50% threshold of its selected **Source Channel**, even across a broad soft transition;
SDF uses its **Threshold** setting. Areas that never reach that threshold do not form a silhouette.
Keep **Euclidean Exact** for a hard-threshold silhouette or pixel masks.

Outline supports fractional **Width (px)** values. **Softness (px) = 0** keeps a crisp, smoothed edge;
increasing **Softness (px)** feathers both sides without changing **Width (px)**.
**Offset (px)** moves the border without changing its width: negative moves inward, positive outward.
Try a small negative offset if the border looks detached from a soft source.

Enable **Fill Center** for a solid shape instead of a hollow border. **Color** sets the border color;
**Fill Color** sets the center color and opacity independently. To back a soft drawing with a solid silhouette,
place Outline below the drawing and set its Input to that specific layer. Above the drawing, the fill covers it.
The fill follows the contour, not the source's original soft transparency. Holes in the source remain holes.
For SDF, **Source Channel** can use Alpha, an individual RGB channel or Luminance, including when the source is a group.

## Blur

Add **Blur**, then choose **Mode** in Properties: **Gaussian**, **Linear** or **Circular**.
Only the relevant controls are shown; switching modes keeps their settings.

### Gaussian

Increase **Radius** for a softer image. Start small for edge cleanup;
use a larger radius for broad, soft shapes.

**Strength (%)** controls intensity: 0% shows the original, 100% gives normal blur,
and up to 400% makes translucent areas denser without changing the radius or brightening the colors.
Values above 100% do not change fully opaque areas.

## Sharpen

Add **Sharpen** to restore local edge contrast without changing the source alpha.
Choose **Gaussian** for conventional unsharp masking or **Adaptive** to favor coherent edges
over weak, directionless detail using a local edge-coherence mask. **Strength (%)** controls the
amount (0–400%) and **Radius (px)** controls the comparison distance in canvas pixels.
**Threshold** sets the minimum detail to sharpen. **Noise Reduction** further suppresses irregular
detail in Adaptive mode only; it does not remove noise already in the source. **Halo Suppression**
limits edge overshoot. **Channels** can process RGB or luminance. **Edges** chooses
Transparent, Clamp, Repeat or Mirror sampling outside the source. The operation preserves HDR color values.
During an active edit WhimTex uses a faster approximation; the wider Gaussian-weighted result
is calculated when the interaction settles.

To blur several layers together:

1. Put them in a group and add Blur above it with Mode set to Gaussian.
2. Leave Input at Previous.
3. Hide the group itself to show only the blurred result.
4. Adjust Radius and Strength.

### Linear and Circular

Choose **Linear** for a straight trail. **Distance (px)** sets its length and **Angle (deg)** sets its direction.
Choose **Circular** for a rotating trail, then set **Center** and **Arc**.

**Direction** places the trail around the source, ahead of it or behind it.
**Strength** below 100% brings back more of the sharp original.
Above 100%, it makes translucent trails denser without making them longer.

## Make Seamless

Add **+ → Make Seamless** above a texture or group. Select the source with **Input**,
then hide the source itself if you want to see only the processed result.

Choose which edge is copied onto its opposite edge with **Horizontal** and **Vertical**.
You can also click an edge of the image icon above the fields to choose the destination.
Click the highlighted edge again to turn that axis off; choosing its opposite switches direction.
Set either axis to **Off** to leave it unchanged. The copied strip is mirrored and fades into the original;
when both axes are enabled, the corners are joined too.

**Fade Width (%)** widens the transition. **Falloff** controls its shape: higher values keep
the reflection closer to the destination edge. Enable **Tiled** to inspect the joins while adjusting.
This is useful for noise and surface textures, but recognizable shapes may look mirrored near a join.
Further transforms or effects can change the matching edges, so check the final tiled result as well.
To repair a region using nearby detail rather than join opposite edges, use [content-aware fill](selection.md#fill-from-existing-texture-details).

## Keep the edges right

Blur effects have an **Edges** setting:

- **Transparent:** fade into empty space.
- **Clamp:** extend the edge colors.
- **Repeat:** wrap around; useful for seamless textures.
- **Mirror:** reflect the image at the border.

Normal Map offers **Clamp**, **Repeat** and **Mirror**, but not **Transparent**.

For seamless work, set Edges on the effect as well as enabling [Tiled preview](symmetry.md).
