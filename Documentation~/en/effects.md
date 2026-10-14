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

The document's [Pixel Scale](preview.md#crop-extend-or-scale-the-canvas) multiplies pixel lengths such as Outline width, SDF distances, Blur and Sharpen radii, and Normal Map smoothing. Entered values remain unchanged: a radius of 3 with Pixel Scale 2 uses 6 canvas pixels. Built-in Shader FX pixel lengths follow the same multiplier; colors, angles and strength ratios keep their usual meaning.

Choose by where you want the result:

| Kind | Where it lives | What it changes |
| --- | --- | --- |
| **Effect Layer** | A separate row in Layers | A chosen source: for example, Outline around a hidden Shape. |
| **FX** | The selected layer's FX section | That layer's image. Use [Shader FX](shader-fx.md) for presets or custom HLSL. |
| **Shader Processor** | A separate row in Layers | The combined image below it. See [its scope](shader-fx.md#affect-one-layer-or-the-image-below). |
| **Post FX** | Canvas View's preview controls | The view only, not saved pixels. See [Post FX](post-fx.md). |

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

| Input | Source |
| :--- | :--- |
| Previous | The layer directly below the effect, in the same group. |
| Specific | The layer or group selected in **Target**. |
| All Below | The combined visible layers below the effect, including opacity, blending and FX. |

For **Specific**, choose Target from the list or drag a layer into the field.
When dragging several selected layers, the active layer becomes the target. **None (Layer)** means no source is assigned.

Previous and Specific can use a hidden source. For a group, hide the group itself, not the children needed in the result.
Only its contents are used, without the background behind it.

**All Below** stays within the current group, even in Pass Through; at the root it uses the lower document stack.
An empty stack is transparent, and Target is hidden.
An opaque background also makes the combined alpha opaque: Outline/SDF using Alpha cannot separate silhouettes above it.
Use Specific or a separate source group for those silhouettes.

## Outline and SDF

**Outline** adds a border. **SDF** turns distance from a contour into a gradual transition,
useful for masks, glows or a height profile. Both keep the source editable.

### Outline: border and fill

Choose **Source Channel** (Alpha by default, or Red, Green, Blue or Luminance),
then place the border inside, outside or across the edge.

| Setting | Visible effect |
| :--- | :--- |
| Width (px) | Border width; fractional values are supported. |
| Softness (px) | Feathers both sides without changing Width. Zero keeps a crisp, antialiased edge. |
| Offset (px) | Moves the border inward when negative, outward when positive, without changing its width. |
| Color | Border color. |
| Fill Center / Fill Color | Adds a filled center with its own color and opacity. |

Try a small negative Offset if a border looks detached from a soft source.
To back a drawing with a solid silhouette, place Outline below it and select that drawing as Specific input.
Above the drawing, the fill covers it. Fill follows the contour, not the source's soft alpha; holes remain holes.

### SDF: distance as a gradient or data

Choose **Source Channel**: Alpha, an RGB channel or Luminance, including for groups.
**Threshold** determines the contour; **Max Distance (px)** determines how far the transition extends. Zero selects the distance automatically.

| Output | Result |
| :--- | :--- |
| Gradient (default) | The palette supplies RGB, HDR intensity and alpha. Source alpha is not retained. |
| Linear Data | Normalized distance in raw 0–1 RGB, opaque alpha, without color gamma conversion. |
| Multi Channel Data | MTSDF only: three corner-preserving distance channels in RGB and true contour distance in alpha. |

The default palette is black at 0, white at 1, with **Perceptual** interpolation.
**Inverted** reverses normalized distance; **Profile** then reshapes it before palette sampling.
Both apply to Gradient and Linear Data. With MTSDF, these outputs use the median RGB distance.
A linear Profile leaves the transition unchanged; changing Output preserves the palette and inversion.

For Gradient and Linear Data, **Position** chooses the distance region: Outside, Inside, Center (both sides), or **Signed** (default).
Signed is low inside and high outside; the contour maps to 0.5 before Profile.
**Inside Distance** and **Outside Distance** set its two ranges independently; zero inherits Max Distance or auto.
In these two outputs, **Contour Offset (px)** expands the shape when positive and shrinks it when negative.

For bevel lighting, use Linear Data or a grayscale gradient and shape the height with Profile.
With the default palette, both outputs have the same inside-to-outside brightness direction.

For a corner-preserving distance texture, choose **Distance Algorithm → MTSDF** and
**Output → Multi Channel Data**. RGB contains signed perpendicular/pseudo distances that preserve corners;
alpha contains true signed Euclidean distance to the smooth subpixel contour. Alpha is distance data,
not transparency. The contour is 0.5 in each channel, lower values are inside and higher values outside.
**Max Distance** sets one symmetric range for all four channels; values beyond it stop at 0 or 1.
Position, Contour Offset, Inside/Outside Distance, Inverted, Profile and the palette do not change this output.
For raw RGBA composition or export, set **Rendering → Blend Mode → Overwrite**.
Normal blending uses alpha as coverage and can change the data. Ordinary FX or effect layers reading
this standalone SDF layer receive its RGBA channels; a group or All Below input uses the combined image.
Export with linear encoding and preserve RGBA. Choosing another algorithm switches this output to Linear Data.
The reconstructed contour can slightly alter tiny corners and subpixel details; it does not reproduce an exact original curve.

### Source edges and distance quality

**Source Offset (px)** moves the SDF input while retaining the influence of contours beyond the canvas.
**Source Edges** extends it as Transparent (default), Clamp, Repeat (including distance across seams) or Mirror.
It cannot recover details already clipped by the source layer.

Large offsets and Repeat need more memory. If the extended calculation exceeds 64 million pixels,
reduce the offset or resolution; WhimTex reports an error instead of silently cropping.

**Distance Algorithm** offers ten antialiased modes for both Outline and SDF:

| Algorithm | Result |
| :--- | :--- |
| Round (Antialiased), default | Rounded Euclidean offsets; keeps the existing Round AA result. |
| Sharp (Antialiased) | Extends contour edges to miter joins, preserving sharp corners. |
| Bevel (Antialiased) | Cuts the offset corners with straight bevel joins. |
| Diamond (Antialiased) | L1 distance with diamond-shaped offsets aligned to the calculation axes. |
| Square (Antialiased) | L∞ distance with square-shaped offsets aligned to the calculation axes. |
| MTSDF | Preserves corners through three distance channels; alpha stores true contour distance. |
| AAEDT + 8SED (experimental) | Estimates the raster edge from pixel coverage; wide external contours remain round. |
| Contour AA (experimental) | Measures distance to straight subpixel segments along the current raster threshold boundary; expanded corners round. |
| Corner Fit AA (experimental) | Fits corner sides across several pixels and checks their local AA coverage before allowing bounded sharp joins. |
| Spring Simulation (experimental) | Accepts geometrically safe spring relaxation without requiring a global match to source AA coverage, then uses the same local corner checks and bounded sharp joins as Corner Fit AA. |

Choose **Corner Fit AA** to test sharp-corner recovery from the raster source. It starts with the raw
Contour boundary, fits sides over several pixels and re-rasterizes each proposed corner locally with
antialiasing. Intersecting or unconfirmed extensions are rejected; ambiguous areas retain the raw
contour. Verified endpoint joins blend continuously back to finite distance as another boundary
becomes nearer; a remote corner cannot take over the field. This is an
experimental hybrid; it cannot guarantee sharp recovery from every raster.

**Spring Simulation** first applies automatic spring relaxation to the raw contour. Geometrically safe
relaxation is accepted without requiring the whole relaxed contour to match the source's AA coverage.
Spring forces, movement limits and topology protection remain in place; the subsequent local AA
corner checks are still shared with Corner Fit AA. The shape can shift locally relative to the source,
and neither better quality nor faster rendering is guaranteed.
It adapts ideas from [Matusovic, Parakkat and Eisemann (2023)](https://doi.org/10.1111/cgf.14743)
on interactive pixel-art depixelization. That paper does not generate SDFs; this mode does not reproduce
the full interactive tool or its C++ implementation. Both modes use the same scalar output controls.

For these two modes, **Threshold** remaps the selected channel as pixel coverage; Outline uses its fixed
midpoint. Soft masks, blur and values that do not represent coverage give only an approximate fit.
**Miter Limit** bevels long validated joins; it cannot make an uncertain corner valid.
**Contour Merge (px)** has no effect. SDF supports Gradient or Linear Data, with no Multi Channel Data.
Round AA stays the default, and the previous eight modes keep their behavior.

These are testing modes, not replacements for the existing algorithms. Mixed verified-sharp and
fallback-round regions can still show visible bands inside complex stars. Open contours crossing the
image boundary retain raw geometry. Missing coverage evidence or an exhausted corner-fitting budget
keeps the geometry entering that stage, which can already be relaxed in Spring Simulation.

Once the source is prepared, changing distance ranges, the profile, gradient or Outline width
reuses the field. Miter Limit reuses the contour and recalculates the field. Changing the source,
threshold, source channel or preparation mode requires preparing the source again.

The field recalculation used by Miter Limit also reuses prepared corner parameters.

Choose **Contour AA** to compare a segment-based boundary with Round AA. It locates threshold
crossings between source pixels and joins them with finite straight segments, without fitting,
smoothing, corner recovery, merging or extending sharp corners. It measures the current raster
boundary and cannot restore lost detail. Contour AA is separate from AAEDT and does not produce
MSDF channels. **Source Channel**, **Threshold** (fixed midpoint for Outline), **Source Edges**,
offsets and the usual output controls keep their existing behavior. **Miter Limit** and
**Contour Merge (px)** have no effect; SDF supports Gradient or Linear Data.
Round AA remains the default. Quality comparison is experimental; improvement is not guaranteed.

To compare raster-edge estimates, choose **AAEDT + 8SED** and compare it with the default Round AA.
It assumes each source alpha value measures how much of a pixel is covered by a crisp edge.
Blur, feather and arbitrary transparency cannot recover exact geometry with this mode; the same
limitation applies when another Source Channel is used as coverage. **Threshold** adjusts the
coverage midpoint in SDF; Outline uses its fixed midpoint. At the minimum and maximum threshold,
the input is classified as a binary mask. AAEDT estimates the boundary without extending sharp
corners; **Miter Limit** and **Contour Merge (px)** have no effect. Use Gradient or Linear Data
for SDF output. Round AA remains the default. AAEDT is experimental; improved accuracy or speed
is not guaranteed.

Sharp and Bevel describe contour offsets rather than Euclidean distance. **Miter Limit** is shared by
Outline and SDF and affects Sharp, Corner Fit AA and Spring Simulation: default 4, range 1–64.
Lower it to cut long corner spikes sooner.
Diamond and Square depend on axis direction, so rotating a silhouette changes their offset shape.
Sharp, Bevel, Diamond, Square and MTSDF estimate corner sides from several contour samples,
avoiding a diagonal cap caused by pixel sampling. Intersecting reconstructed regions are combined
before forming offset corners, so an internal seam does not create another bevel. Holes and
genuinely separate small tips are retained. Reconstruction can still change subpixel
details; the result does not reproduce the original matte exactly. Round retains its original AA result.
AA smooths the edge while retaining the selected corner shape.
Sharp and MTSDF preserve angular extensions only when both sides have enough straight
support. Their safety limit is 4: very acute tips (roughly below 29 degrees), tiny details
and uncertain corners can round instead of producing distant spikes. Raising Miter Limit
above 4 does not override this protection. MTSDF also checks suspect RGB transitions
against finite contour distance; corrected regions may lose some sharpness.

For SDF with Sharp or MTSDF, **Contour Merge (px)** experimentally merges nearby consecutive
vertices within the same contour; separate loops are never merged by this setting. The range is
0–2 current render pixels, with 0 (default) off. It is independent of the distance transition width.
Other algorithms keep the value but ignore it and hide the control. One pass replaces eligible
adjacent pairs with their midpoints, shortest pairs first, using each original vertex at most once.
A topology conflict cancels the whole merge batch; existing corner protections still apply.
Short details may shift; sharper corners are not guaranteed.

Outline follows 50% of the selected Source Channel; SDF uses Threshold in every mode.
Regions that never reach that threshold do not form a silhouette.

## Blur

Add **Blur**, then choose **Mode** in Properties: **Gaussian**, **Linear** or **Circular**.
Only the relevant controls are shown; switching modes keeps their settings.

### Gaussian

Increase **Radius** for a softer image. Start small for edge cleanup;
use a larger radius for broad, soft shapes.

**Strength (%)** controls intensity: 0% shows the original, 100% gives normal blur,
and up to 400% makes translucent areas denser without changing the radius or brightening the colors.
Values above 100% do not change fully opaque areas.

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

## Make Seamless

Add **+ → Make Seamless** above a texture or group, choose **Input**, and hide the source if you want to see only the processed result.
Enable **Tiled** to compare the repeated edges.

| Method | How it joins edges | Main trade-off |
| --- | --- | --- |
| **Offset Blend** | Blends copies shifted by half a period. | A useful starting point for noise and surfaces; colors and repeated details can change. |
| **Mirror** | Reflects the opposite edge into a transition strip. | Fast to adjust, but recognizable details may look mirrored. |
| **Screened Poisson** | Applies a smooth correction to opposing edges. | Can also change the center and other edges; it does not reconstruct matching details. |
| **Patch Quilting** | Finds strips in the source and joins them along a low-error cut. | Can retain sharper detail, but may repeat features or leave visible joins in smooth noise. |

New layers use **Offset Blend**. Offset Blend and Mirror start with **Blend Width 20%**, **Transition Start −25%**, and **Poisson Correction → All Edges** enabled.
All copy edges and both mirror axes start enabled. Switching methods retains each method's settings; existing layers keep their saved values.

### Edges and channels

**Channels → R / G / B / A** chooses which channels receive the result; all start enabled.
Turn off A to retain source transparency. With all channels off, seam processing is bypassed.
Later FX, Mapping and layer blending can still change the image.

The square selects edges of the current pass:

- **Copy Edges:** four independent edges for Offset Blend.
- **Mirror Direction:** an edge is the destination for a reflection of the opposite edge. Click it again to disable the axis, or click the opposite edge to reverse direction. Two enabled axes also join the corners.
- **Patch Edges / Poisson Edges:** opposing edges toggle together. **Top & Bottom** repeats vertically; **Left & Right** horizontally; **All Edges** enables both. Both pairs can be off.

Linked edges highlight together. Clicking the center inverts the selection.
For Mirror, it disables both axes if either is active; the next click enables Left To Right and Bottom To Top.
Disabling a pass greys out its dependent fields, not the square or the other pass, and retains its values.

**Poisson Correction has its own edges.** It can run even when copying, mirroring or patching is disabled.

### Offset Blend

| Control | Effect |
| --- | --- |
| **Blend Width (%)** | Width of the copied strips: 2–50%, default 20. |
| **Transition Start (%)** | Start of fading within the strip, not the whole canvas: −100…95%, default −25. Before this point, the copy has full influence. |
| **Contrast Compensation / Strength (%)** | Reduce contrast lost through blending. Enabled by default; Strength 0–100%, default 100. |

A lower Transition Start widens the fade; 0 starts at the edge. Positive values narrow it.
Negative values extend the fade beyond the canvas and may bring back a seam. **Poisson Correction** can reduce that seam; changing Transition Start does not toggle it.

Strips overlap at corners and are at least two pixels wide. Without Poisson Correction, pixels outside them remain unchanged apart from floating-point/alpha rounding.
Contrast compensation preserves the distribution approximately, not exactly. Disabling it or setting Strength to 0 skips histogram analysis.

### Mirror

**Blend Width (%)** sets the transition width. **Falloff** concentrates reflection closer to the destination edge as it increases.
The reflected strip fades into the original; an axis with no selected edge is unchanged.

**Transition Start (%)** uses −100…95%, default −25, within Blend Width.
Zero keeps the original fade profile; positive values delay and narrow it, negative values broaden it beyond the canvas and may reintroduce a seam.
It works with or without Contrast Compensation and is independent of Offset Blend's setting.

**Contrast Compensation** is off by default. It blends reflected pixels with histogram-based compensation to reduce contrast loss, but can change colors and does not remove mirrored motifs.
**Strength (%)** is 0–100; 0 uses ordinary Mirror.

### Screened Poisson and Poisson Correction

**Radius (%)** controls the correction's reach relative to the shorter image side: 0.5–25%, default 5.
The correction has no hard strip boundary. It approximately preserves central brightness, but may change the center and unselected edges; only selected edge pairs are joined.
Bright values may leave the permitted range and clip. Mirror controls do not affect this method.

Offset Blend, Mirror and Patch Quilting offer the same operation as **Poisson Correction**:

- Each has an independent **Poisson Edges** selection. Its reach is not restricted to the copy/patch strip.
- Correction is on by default for Offset Blend and Mirror, off for Patch Quilting.
- Offset Blend and Mirror also have **Automatic Radius**, on by default: one quarter of Blend Width, with a minimum of 0.5%. The Radius field shows the calculated value; turn Automatic Radius off to restore and edit the saved manual radius.
- Correction adds calculation time. Check both contrast and seams in Tiled view.

### Patch Quilting

Only edge strips are reconstructed, not the whole image.

| Control | Effect |
| --- | --- |
| **Patch Width (%)** | Width of each strip: 2–45%, default 20. Wider strips allow more cut placement but change more of the image. |
| **Feather (%)** | Share of the available symmetric blend around each cut: 0–100%, default 50. This blends patches, not a texture blur or a percentage of Patch Width. |
| **Search Quality** | Draft, Normal (default), High. Higher settings test more candidates at higher analysis resolution; they cost more and do not guarantee a better-looking result. |
| **Along-Seam Search (%)** | Shift donor strips along the seam: 0–25%, default 0. Zero uses unshifted candidates. Shifts fade at strip ends without wrapping and may stretch detail. |
| **Seed / Random** | Choose another reproducible match. Different seeds can select the same result. |
| **Channel Matching** | **Linked** (default) shares the donor and cut across selected channels. **Independent** matches each channel separately: useful for packed maps, not for preserving color and transparent colored edges. |

Feather 0 leaves a hard cut; 100 uses the widest symmetric transition available without leaving the strip or opening the seam.
Very narrow strips may show little difference between percentages. At one output pixel there is no room for feathering; a tiny analysis strip uses centered cuts.
Without Poisson Correction, pixels outside the strips are retained apart from rounding.

**Contrast Compensation** is off by default. With Feather above 0, it can reduce contrast loss inside the fade.
**Strength (%)** is 0–100. The correction considers the similarity of the source and donor and may change colors.
At Strength 0 or Feather 0 it does no work; the control is unavailable at Feather 0.
With both axes enabled, the second search uses the first corrected result and can choose a different patch.

### Preview and calculation

- Sources remain editable: changes update the result; the layer does not bake them.
- Patch Width and Feather apply after releasing their sliders. Quilting calculation can temporarily block editing; higher quality and wider searches can take longer.
- A smaller preview can choose different patches and cuts. Ordinary preview is limited to 512 pixels; **Live Quality 100%** does not remove that limit outside painting. Select **Pencil** without painting and enable **Tiled** to check full resolution before export.

**Older documents:** saved Feather values are now percentages without migration (16 means 16%), so the result may differ.
Loading does not automatically rewrite files.
Later transforms and FX may break seamlessness; check the final composite as well.

For scripts and agents, see the [Make Seamless parameter reference](../AgentAPI.md#make-seamless-settings) and [clipboard recipe](../Examples/Clipboard/seamless-noise.json).
To repair an area rather than join opposite edges, use [Content-Aware Fill](selection.md#fill-from-existing-texture-details).

## Keep the edges right

Blur effects have an **Edges** setting:

- **Transparent:** fade into empty space.
- **Clamp:** extend the edge colors.
- **Repeat:** wrap around; useful for seamless textures.
- **Mirror:** reflect the image at the border.

Normal Map offers **Clamp**, **Repeat** and **Mirror**, but not **Transparent**.

For seamless work, set Edges on the effect as well as enabling [Tiled preview](symmetry.md).
