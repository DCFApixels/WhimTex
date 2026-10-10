---
title: "Brush, Pencil and Fill"
parent: "English"
nav_order: 4
lang: "en"
description: "Paint textures in Unity with WhimTex Brush, Pencil and Fill. Use textured brushes, gradients, scatter and presets for quick touch-ups and VFX masks."
permalink: "/en/painting/"
translations: "en/painting.md,ru/painting.md,zh/painting.md"
previous_page: "en/transform.md"
next_page: "en/selection.md"
---

# Brush, Pencil and Fill

Select a **Drawing** layer and paint directly on the canvas.
Choose **Brush** (`B`) for soft strokes or **Pencil** (`P`) for crisp pixels.
If you try to paint on another layer type, the editor offers to convert it first.

A small cross marks the brush center when its outline becomes large relative to Canvas View.

## Shape the stroke

| Control | What it changes |
| :--- | :--- |
| Size | Stroke width. You can drag the label or use `[` / `]`. |
| Hardness / Gradient | Next to Size in the Canvas View header. Drag Hardness or type a percentage; click the gradient strip to edit it. The arrow switches procedural brushes between Hardness and Gradient without losing either setting. |
| Opacity | Maximum strength of a whole stroke. Release and paint again to build another coat. |
| Flow | Strength of each brush stamp. Lower values let overlapping stamps build color gradually. |

For example, **Opacity 40%** keeps one stroke at no more than 40%, even when you go
over it repeatedly. **Flow 10%** builds up gently as you draw, up to the Opacity limit.
These controls also work when erasing with Brush.

For a textured brush, enable **SDF** in **Brushes → Tip** to use the Gradient control.
Without SDF, the texture defines its own edge and this header block is inactive.
The same controls are also available in **Brushes → Tip** as **Mode**, **Hardness** and
**Gradient**. Changes in either place stay in sync.

Pencil has no hardness or spacing controls. Choose **Circle**, **Square** or **Diamond**
for its tip. Zoom in to see its exact pixel outline.

## Steady the path and protect channels

The shared controls in each tool's Canvas View header row apply to **Brush, Pencil, Blur Brush and Smudge Brush**.
**Smoothing → None** follows the pointer directly (default). **Smooth** reduces small deviations;
**Stabilizer** lets the brush follow behind a rope. **Distance (px)** sets the filter distance or rope
length in canvas pixels: larger values steady the path more but add delay. Zoom does not change it.
The image itself is not blurred. Shift lines and snapped points stay exact.

**Finish Stroke** completes the delayed path at the pointer when you release; disable it to leave
the endpoint at the filtered position. **Smooth Pressure** filters pressure separately, even with
Smoothing set to None. Enable the tool's **Pressure** option to use it; Pencil has no pressure dynamics.

**Lock Alpha** preserves transparency and empty pixels while you recolor a shape. **Write R/G/B/A**
allows changes only in enabled channels; disabled channels keep their previous values. Turn off
G, B and A to edit a packed texture's red channel. Lock Alpha takes priority over Write A, so erasing
cannot change transparency while it is enabled. Protection also applies to **Fill and Healing Brush**.
The footer R/G/B/A buttons only change what you see, not what you paint.

Smoothing is remembered as an input preference. Write channels and Lock Alpha are included in brush presets.

## Customize the brush

With **Brush** selected, open the upper arrow on the right edge of Canvas View to show
**Brushes**. It shares the drawer with Post FX: opening one closes the other.

<a href="{{ '/Images/brush-settings.png' | relative_url }}"><img src="{{ '/Images/brush-settings.png' | relative_url }}" alt="WhimTex Brushes drawer with a neon red stroke, stamp controls, tint, blend mode and live brush preview" width="720"></a>

Tip and stamp settings are available here: **Size** is in **Tip**, and **Opacity / Flow**
are in **Color**. The Canvas View header provides shortcuts and the shared write-protection controls.

The brush sample previews the current tip without layer FX or symmetry; Eraser previews on gray paint.
Use **Preview Scale (%)** to fit a large tip or wide scatter. It changes only the sample, not the painted brush size.

Use **↺** in the **Tip**, **Stamps** or **Color** header to reset that section,
preserving **Size, Hardness, Spacing, Opacity and Flow**. The Hardness/Gradient mode can still change.
Palette colors are not reset.

| Setting | What it changes |
| :--- | :--- |
| Spacing (%) | Distance between stamps, measured against Size. 100% is one diameter; lower values make a continuous stroke, higher values leave separate marks. |
| Scatter | Random displacement around the stroke. 100% spreads centers by up to one brush diameter. |
| Scatter Bias | Negative values concentrate stamps near the stroke; positive values move them toward the outer edge of the scatter disk. **0** keeps the distribution uniform across its area. Available when Scatter is above zero; works with Random and Sobol. |
| Randomization | **Random** (default) gives ordinary random variation. **Sobol** distributes variation more evenly across stamps. Applies to Scatter, Size Jitter, Angle Jitter, Flip X/Y and Tint. |
| Size Jitter | Random variation around Size. 50% gives stamps from half to one-and-a-half times the chosen size. |
| Rotation | **Fixed** keeps the texture tip's original orientation. **Stroke Direction** turns it along the stroke, with the tip's right-facing axis following movement. The first click uses the original orientation until you move. |
| Angle Offset (°) | Constant turn from −180° to 180°, added after Rotation. For example, 90° in Stroke Direction turns the tip across the stroke. Requires a texture tip. |
| Angle Jitter (°) | Random turn added after Rotation and Angle Offset. 0° adds no variation, 180° covers every direction. Requires a texture tip; a procedural brush is unchanged by rotation. |
| Flip X / Flip Y | Chance to mirror each texture stamp horizontally or vertically: **0** never, **0.5** roughly half, **1** always. Axes follow the tip's rotation. Requires a texture tip; each axis is sampled separately. |
| Texture | Drag in a texture to use its shape as a brush. Clear the field to return to the procedural brush. Size measures its longest side. |
| Tip Channel | Alpha uses transparency; Luminance uses white as ink; Inverted Luminance uses black as ink. Color keeps the tip's color, multiplied by the painting color. All modes respect the tip's alpha. |
| SDF | Treat the texture as a distance field and map it through Gradient. Use Alpha for a field in transparency, Luminance for a grayscale field, or Inverted Luminance when dark areas are inside. Color uses the alpha field while keeping the tip's RGB color. |
| Gradient (Canvas View header) | Left is the interior (**0**), right is the outer edge (**1**). Alpha keys control the edge and coverage; color keys multiply the brush color and Tint. Move alpha keys closer for a sharp edge or apart for softness. Shift the transition right to expand the shape, left to shrink it. |
| Tint | Different gradient color or alpha keys produce a random tint per stamp, multiplied by the palette color. Identical keys give a constant tint. The small ↺ button on the right resets to opaque white, which leaves the palette color unchanged. |
| Blend | How paint combines with existing pixels on the active layer. Independent of the layer's Blend setting; ignored when erasing. |
| Apply Blend | **Per Stroke** (default) applies Blend to the complete stroke. **Per Stamp** applies it to every stamp, including where stamps overlap within the same stroke. Opacity controls the whole result; Flow controls each stamp. With dense Spacing, Per Stamp can be slower. |

For scattered colored marks, increase Spacing and Scatter, add a little Size Jitter,
then add different colors to the Tint gradient. Start with white in the palette to see
the gradient's colors without an additional color tint.

These advanced settings belong to **Brush**, not Pencil or Fill.

For a procedural brush, the gradient runs from **0 at the circular tip's center** to **1 at its outer edge**.
Assigning a texture preserves the selected procedural brush mode.

For a textured SDF brush, enable **SDF** in Tip, choose the channel containing the distance field,
then edit **Gradient** in the Canvas View header while watching the sample. Start with white color keys to keep
the brush color, and use the alpha keys to shape the edge. You can add transparent bands
for hollow shapes or color bands for a multicolored tip. The field should use the 0–1 range,
with higher values inside (or choose Inverted Luminance). Keep SDF off for ordinary image
tips. Gradient is included when saving a brush preset.

### Scripted tips and brush JSON

In **Brushes → Tip → Source**, choose **Standard** or **HLSL**.
Standard is a round procedural brush when **Texture** is empty; assign an image to use a textured tip.
HLSL generates its tip with a script and uses the same Tip Channel, SDF/Gradient, rotation and flip controls as a textured Standard brush.
Choose **HLSL Presets** or open **Edit Code…**, edit the script and press **Apply**.
Declared settings use the same fields, groups and conditional visibility as FX, including gradients and curves.
The entry point selects **Static** (baked on Apply) or **Dynamic** (evaluated while painting).
**Tip Resolution** controls Static tip detail; **Size** controls the stamp size in both modes.
Dynamic tips can vary by stroke, stamp, accumulated path length or movement direction. **Seed** sets a stable
sequence, and **Reset Sequence** restarts it; the sample preview does not advance your strokes.
The code window can save an HLSL preset to the project or the user preset folder.
Files in the project and in the user folder's **Brushes/HLSL** subfolder appear in the menu.
The regular brush **Save As…** also keeps the HLSL code and settings. See the
[HLSL contract](../AI/BRUSHES.md#full-hlsl-specification) for signatures and context fields.

A browser AI can provide brush JSON: paste it with **Ctrl+V** outside a text field to
replace the current brush without changing layers. See the [brush contract and examples](../AI/BRUSHES.md).

### Brush presets

Project `.sebrush` files appear as brush assets with stroke thumbnails in the Project window.
Select one to see a larger preview in the Inspector. Drag the asset anywhere into WhimTex to choose the brush, or use the brush preset menu. Dropping a brush does not create a layer or switch tools.

Choose a saved brush from the selector at the top of **Brushes**. Use **Save As…** to
name a new preset; the selector menu also offers **Overwrite Selected…** and
**Open Brushes Folder**. An asterisk means you have changed the brush since saving or
selecting it. Changes are not saved automatically.

Presets include Size, Hardness, procedural Mode, Gradient, Spacing, Opacity, Flow, texture tip, Stamps and Color
settings. Your palette colors, Brush/Eraser mode, Pencil/Fill settings and layer symmetry
stay unchanged. The preview updates when you select a preset.

In **Window tab ⋮ → User Settings… → Presets**, choose **Presets Folder** with **…**
or enter an absolute path. The folder setting is shared across projects on this computer
for your user account. **↺** restores the default location without deleting files.
If presets are missing after an update, select their existing folder here; the files are not
moved or deleted. Tool and Canvas View preferences may reset after updates.

Brushes are stored in its **Brushes** subfolder. A `.sebrush` contains settings and any
image/Static tip; a Dynamic tip keeps its code instead. Copy the preset to another computer's
Brushes folder without importing the original texture. Overwriting keeps the previous file as `.sebrush.bak`; to restore it, rename that
backup to a different name ending in `.sebrush`.

The selector also finds `.sebrush` files anywhere in the project's **Assets** or installed
packages. Use **Save to Project…** in the selector menu, or copy a preset into an Assets
folder to share it with the project. **User** and **Project** keep the two sources distinct;
subfolders are supported. Presets supplied by packages can be used, but save your changes
as a new user or project preset.

## Color and erasing


The first color swatch is the painting color; `X` swaps the two swatches.
Hold `Alt` in WhimTex to use the screen eyedropper; the outlined center pixel in its magnifier is the color you'll pick.
Click or drag with the left mouse button to pick, then release `Alt` to return to your tool.
You can sample across the screen, including other Unity windows and other applications. The eyedropper picks the visible color,
including Post FX, EV and the transparency checkerboard, rather than the original HDR value. Brush alpha stays unchanged.
With Brush or Pencil, hold the right mouse button to erase, or choose erasing in the tool options.

Brush and fill settings follow you between layers. [Symmetry](symmetry.md) is set separately for each Drawing layer.

## Draw straight lines

Click and begin dragging, then hold `Shift` to draw horizontally or vertically.
These directions follow the screen, even when the canvas view is rotated.
If the stroke starts snapped to a [guide](preview.md#guides), `Shift` follows that guide instead, at any angle and regardless of the pointer's distance from it.
To connect points, click the first point, then hold `Shift` and click the next one.
Repeat to draw a chain of straight segments.

## Smudge Brush

Choose the **finger icon** and drag to stretch existing pixels along the stroke.
A click without movement does not paint. Smudge uses a round tip and does not add
the selected painting color.

- **Size / Hardness** set the diameter and edge softness; `[` / `]` change Size.
- **Mixing** balances stretching and color mixing. At 0%, details stretch without cumulative color blur. Higher values mix colors more; 100% uses only color mixing. The default is 25%.
- **Strength** controls the amount of stretching and, when Mixing is enabled, how long the picked-up image is retained. At 100%, the carried patch stays unchanged; lower values gradually pick up the painted result.
- **Flow** controls the amount applied at each step. **Pressure** scales Flow with tablet pressure.
- **Current Layer** samples raw Drawing pixels. **Below Layers** includes the active layer and the visible layers below it in its group; **All Layers** samples the visible composition.

Only the active Drawing layer is changed, and the area selection limits deposits.
Other layer types offer conversion on click. Use an empty Drawing above the image
with Below Layers to keep the original intact. Sampled FX become pixels in the
stroke; painting back onto a layer with those FX can apply them again.

Each drag is one Undo step. **Tiled** wraps strokes across canvas edges.
Smudge settings are independent of Blur Brush; brush presets and symmetry do not apply.

## Healing Brush

Choose the bandage icon, paint over a scratch, small hole or seam, then release the
mouse button. The colored stroke marks the repair area; reconstruction runs after release.
Cover the entire defect with a little surrounding texture. The result is one Undo step.

The repair borrows similar colors and texture from the surrounding image, including fine detail.
It softens joins between smooth patches and tries alternative patches to reduce repetition.
It cannot recreate unique missing features: large shapes and interrupted lines may need touch-ups.
Wide repairs in smooth noise can lose contrast and leave a flatter-looking band; repeated details are still possible.
**Hardness** controls the blend at the stroke edge, not the sharpness of the recovered detail.

- **Size** sets the stroke diameter in canvas pixels; `[` / `]` also change it.
- **Hardness** sets the mask edge: low values blend the repair softly; high values give a firm edge.
- **Search** sets how far around the stroke to look for replacement details.
- **Current Layer** samples raw Drawing pixels before its FX. **Current & Below**
  samples the visible stack from the active layer down, within its group. This can
  paint onto an empty Drawing layer above the source. Sampled FX are baked into the
  repair, so an empty retouching layer is preferable to reapplying the same FX.
- **Fast / Balanced / High** trade calculation time for more matching iterations.
- **Transparent Only** fills empty pixels in the sampled image, leaving visible ones alone.

Only the active Drawing layer is changed. Other layer types offer conversion on click.
The canvas selection limits the repair; the search can use pixels outside the selection.
Press **Escape** or **Cancel** to discard the stroke or pending calculation. Changing
the document, selection or tool cancels pending work; wait for completion before saving.

Choose its color and opacity in **User Settings → Healing Brush → Stroke Color**.
This changes only the overlay, not mask strength or the repaired image. **Reset Canvas View Appearance** restores the default blue at 40% opacity.

The colored preview shows the painted mask, including its soft edge and selection. Painting
over it again does not increase coverage. In **Tiled** preview, draw on any copy: the mask
wraps across edges and corners, and the repair searches across those seams too.
For a stroke around all four sides, the search keeps opposing edges together. Some
patch transitions can still remain in ambiguous textures.
The round tip does not use brush presets, symmetry, pressure or Flow. Processing uses canvas
resolution but retains the Drawing texture's native dimensions. A stroke plus its search
area may cover at most 1 million pixels. Smaller strokes are more responsive; large
missing areas and unique details may need several attempts or manual touch-ups.

## Fill an area

Choose **Fill** (`G`) and click the area you want to color.

| Option | When to use it |
| :--- | :--- |
| All Layers | Follow outlines in the whole visible image while filling only the active Drawing layer. Leave off to use that layer alone. |
| Contiguous | Leave on to fill just the connected area you clicked. Turn off to replace matching colors throughout the layer. |
| Tolerance | Increase it to include more similar colors; reduce it if the fill spreads too far. |
| Antialias | Soften the filled edge. |
| Expand (px) | Extend the fill slightly under an outline to close thin gaps. |

Fill does not repeat through brush symmetry. To fill transformed copies outside the layer's
original area, first use **Convert to Drawing → Apply Transform**.

Use an [area selection](selection.md) to keep painting or filling inside a chosen shape.
