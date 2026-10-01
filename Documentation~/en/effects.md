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
| Join opposite texture edges | Make Seamless | Method, Channels, method-specific edges and blending, optional Poisson Correction. |

## Choose what the effect uses

**Input → Previous** uses the layer directly below the effect in the same group.
Choose **Specific** to use another layer or group. Click **Target** to choose from the list,
or drag a layer into the field. **None (Layer)** means no source is selected.
When dragging several selected layers, the active one becomes the target.

**All Below** uses the combined visible layers below the effect, with their opacity, blending and effects. It stays within the current group, even for Pass Through; at the root it uses the lower document stack. An empty stack is transparent. Target selection is hidden in this mode. An opaque background makes the combined alpha opaque, so Outline/SDF using Alpha cannot detect the separate silhouettes above that background. Choose Specific or place the sources in a separate group when you need those silhouettes.

With Previous or Specific, you can hide the source and still see the effect. For a group, hide the group itself,
not the children you want included. The effect uses only that group's contents, not the background behind it.

## Outline and SDF

Use **Outline** for a border around a shape. Adjust its width and softness,
choose **Source Channel** (Alpha by default, or Red, Green, Blue or Luminance),
then choose whether it sits inside, outside or across the edge.

Use **SDF** when you want a gradual transition based on distance from the shape.
**Output → Gradient** is the default, with a black-to-white **Perceptual** gradient (black at 0, white at 1). **Inverted** remains available and reverses normalized distance before Profile and palette sampling. Choose **Output → Linear Data** for normalized distance as raw 0–1 RGB with opaque alpha and no color gamma conversion. Inverted and Profile apply in both modes. Switching Output preserves the palette and inversion setting. Signed values are low inside and high outside; both outputs therefore have the same brightness direction with the default palette.
**Threshold** sets the contour threshold; **Max Distance (px, 0 = auto)** sets the transition distance.
At 0, the distance is chosen automatically. **Position** selects which side receives the gradient:
Outside covers the outside, Inside the inside, and Center both sides.
The default, **Signed**, covers both sides of the contour. In both output modes, **Inverted** reverses normalized distance before Profile.

**Source Offset (px)** shifts the input while retaining the influence of contours moved outside the canvas. **Source Edges** extends the image as Transparent (default), Clamp, Repeat (including distances across seams), or Mirror. It cannot recover details already clipped by the source layer.

**Contour Offset (px)** expands the contour when positive and shrinks it when negative. In **Signed**, **Inside Distance** and **Outside Distance** independently set the interior/exterior range; 0 inherits Max Distance or auto. Before Profile, the contour maps to 0.5. **Profile** remaps the transition in both modes, after Inverted and before any palette sampling; linear leaves it unchanged. For bevel lighting, use Linear Data or a grayscale gradient and shape the height profile here.

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

**Method** offers **Offset Blend** (the default for new layers), **Mirror**, **Screened Poisson**, and **Patch Quilting**.
Existing layers keep their saved mode.
New layers use **Blend Width 20%**, **Transition Start -25%** and enabled **Poisson Correction → All Edges**
for both Offset Blend and Mirror. All Offset copy edges and both Mirror reflection axes are enabled.
Existing layers retain their settings; switching methods does not reset them.

**Copy Edges**, **Mirror Direction** and **Patch Edges** select the main pass; **Poisson Edges** selects the independent correction pass. Clearing a pass's edges disables its dependent settings, not the selectors or another pass. Values are retained. Quilting's Contrast Compensation is unavailable at **Feather 0%**; raise Feather to use it again.

Linked opposite edges highlight together on hover. Click the center image to invert the selection.
For **Mirror Direction**, the center instead turns both axes off if either is active; clicking again
enables both, Left To Right and Bottom To Top.

**Contrast Compensation** adds computation. The first evaluation after a long idle period or a size change can take longer than subsequent updates.

**Channels — R / G / B / A** selects which channels receive seam processing in every mode,
including Mirror's optional compensation and correction. All four are enabled by default.
Unchecked channels retain their input values. Disable **A** to keep the original transparency;
disable all four to bypass seam processing. Subsequent layer FX, Swizzle and blending can still change the final appearance.

**Screened Poisson** globally smooths selected pairs of opposite edges. **Poisson Edges** offers
**All Edges** (default), **Top & Bottom** (vertical tiling), and **Left & Right** (horizontal tiling).
Click edges of the square to toggle opposite edges together. Both pairs can be cleared to skip this pass; the selector stays interactive.
This also applies to Patch Quilting edges and every Poisson Correction selector. Their selections are independent.
**Radius (%)** (0.5–25, default 5) controls the correction distance relative to the smaller dimension.
There is no hard edge-band boundary. Interior brightness is preserved approximately, and unselected
edges may also change, but are not joined. The layer updates with its source; Mirror controls do not apply.
Highlights can exceed the output range and clip; continuous details are not guaranteed.
Check **Tiled** preview. No document migration or automatic file rewrite is performed.

**Offset Blend** blends half-period-shifted copies instead of reflections.
**Blend Width (%)** (2–50, default 20) controls the bands.
**Transition Start (%)** (-100–95, new-layer default -25) sets where fading starts within each band,
not across the canvas. Before that point the strip is fully copied. Lower values spread the
transition over more of the band; 0 starts at the edge. Higher values narrow the transition.
Negative values start outside the canvas and can bring back the seam. Enable **Poisson Correction**
and select the required edge pairs if needed. It is on for new layers, but changing Transition Start does not toggle it.
Optional corrections:

- **Contrast Compensation** (on by default) reduces contrast loss; **Strength (%)**
  (0–100, default 100) sets its strength. Off or zero skips histogram analysis.
- **Poisson Correction** (on by default) adds a global Screened Poisson pass with its own
  **Poisson Edges** choice, independent of the copy edges. It can change pixels outside the blend bands.
  **Automatic Radius** defaults on, using one quarter of
  Blend Width, minimum 0.5%. Disable it to set **Radius (%)** (0.5–25) manually.
  Radius is relative to the smaller dimension. This pass costs more and can exceed the color range.

Repeated details and colors can change, especially on non-noise images.
Distribution preservation is approximate; check **Tiled**.
The layer updates with its source and does not bake the result.

In Offset Blend, click the four edges around the image icon to select copy bands independently.
All are enabled by default. With Poisson Correction off, pixels outside the bands stay unchanged
(apart from float/alpha roundoff); overlapping bands share corners. Bands have a minimum of two pixels.
Turning all copy edges off skips copying, but an enabled Poisson Correction still runs.

**Patch Quilting** searches for translated strips from the source and joins them along low-error cuts.
It repairs boundary bands without resynthesizing the whole image. It can retain sharp details,
but may duplicate motifs or leave visible cut lines on soft noise. Compare in **Tiled** preview.

- **Patch Edges:** All Edges, Top & Bottom, or Left & Right; opposite edges toggle together on the square.
- **Patch Width (%)** (2–45, default 20): width of each repaired edge band. Wider bands allow more room for cuts but change more of the image.
- **Feather (%)** (0–100, default 50): share of the available transition width, independently for each cut. 0 keeps a hard cut; 100 uses the widest safe centered transition without leaving the band or reopening the tile join. Blends patches, not a texture blur. It is not a percentage of Patch Width.
- **Contrast Compensation** (off by default) reduces contrast loss inside Feather transitions. **Strength (%)** controls its strength (0–100). It considers how similar the source and selected patch are; colors can change. It adds processing cost when enabled, but does nothing at zero strength or Feather 0%. With both edge pairs selected, the second pass uses the compensated first pass and may choose a different patch.
- **Search Quality:** Draft, Normal (default), High. Higher quality searches more candidates on a finer analysis grid and is slower; it does not guarantee a better-looking result.
- **Along-Seam Search (%):** 0–25%, default 0 (original search). Adds donor displacement along the seam, measured against usable strip length. Displacement tapers to zero at the ends without wrapping; details can stretch. Nonzero values divide the same candidate budget between straight and shifted strips and cost more to evaluate; improvement is not guaranteed.
- **Seed / Random:** try another repeatable donor selection. Some seeds can produce the same result.
- **Channel Matching:** Linked (default) keeps one patch/cut for the selected color channels; Independent searches each checked channel separately, useful for packed masks/noise but not for preserving color relationships or transparent color edges.
- **Poisson Correction** (off by default): optional global correction, with independent **Poisson Edges** and **Radius (%)** (0.5–25, default 5). It can change the center, contrast and HDR range.

With correction off, pixels outside the repaired bands stay unchanged apart from floating-point rounding.
Patch Width and Feather update after releasing the slider. Narrow analysis bands use centered cuts with Feather still active. A one-pixel output band has no room for smoothing; with very few pixels, different percentages can look identical.
Existing Feather numbers are now percentages without migration: a saved 16 becomes 16%, so old results can change.
Reduced-resolution previews can choose different patches and cuts. Ordinary preview is limited to 512 pixels; **Live Quality 100%** is not a full-resolution switch for non-painting preview. Select **Pencil** without painting and enable **Tiled** to check the full-resolution result before saving or exporting.
Patch search is accelerated without reducing the chosen Search Quality. Adjusting Poisson Correction
does not repeat the patch search; changing the source or quilting controls still requires a new search.
High search also avoids evaluating identical candidate patches repeatedly, without changing the chosen
result for the same seed. This can help more with wide patch bands; recalculation still blocks the editor.

In **Mirror**, click an edge of the square image control to choose the destination for the reflected opposite edge.
Click the highlighted edge again to turn that axis off; choosing its opposite switches direction.
An axis with no highlighted edge remains unchanged. The copied strip is mirrored and fades into the original;
when both axes are enabled, the corners are joined too.

**Blend Width (%)** widens the transition. **Falloff** controls its shape: higher values keep
the reflection closer to the destination edge. Enable **Tiled** to inspect the joins while adjusting.
**Transition Start (%)** (-100–95, new-layer default -25) sets where reflection starts fading within Blend Width.
Zero keeps the original transition; positive values delay and narrow it. Negative values extend the
transition beyond the canvas and may expose a seam; enable **Poisson Correction** if needed.
It works with and without Contrast Compensation, independently of Offset Blend's setting.

For scripted or AI-authored layers, see the [Make Seamless parameter reference](../AgentAPI.md#make-seamless-settings)
and [clipboard recipe](../Examples/Clipboard/seamless-noise.json).

Mirror offers two independent options: Contrast Compensation is off and Poisson Correction is on for new layers:

- **Contrast Compensation** enables histogram-based mixing of reflected pixels. **Strength (%)**
  controls its strength (0–100); 0 keeps ordinary Mirror. It reduces contrast loss in the fade,
  but may change colors and does not remove mirrored motifs.
- **Poisson Correction** adds a global Screened Poisson pass with an independent **Poisson Edges** choice.
  **Automatic Radius** defaults on: one quarter of **Blend Width**, minimum 0.5%. The Radius field
  displays this value read-only. Disable Automatic Radius to restore and edit the saved manual radius.
  **Radius (%)** (0.5–25, default 5) controls its reach relative to the smaller dimension,
  without confinement to **Blend Width**. It can change the interior and other edges, and runs even
  when both reflection axes are Off. Highlights may clip. This option increases render cost substantially.

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
