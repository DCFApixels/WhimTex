---
title: "Color, HDR and channels"
parent: "English"
nav_order: 11
lang: "en"
description: "Edit HDR textures and pack RGBA channels in Unity with WhimTex. Inspect individual channels, configure Swizzle and control layer color and blend ranges."
permalink: "/en/color/"
translations: "en/color.md,ru/color.md,zh/color.md"
previous_page: "en/preview.md"
next_page: "en/post-fx.md"
---

# Color, HDR and channels

For ordinary painting, leave **HDR** off and all four **R / G / B / A** buttons on.
Use the controls below when you need extra brightness, channel masks or texture data.

## Choose and reuse colors

With HDR off, **History** hides colors whose RGB components fall outside 0–1. They remain saved and reappear when HDR is enabled. In the gradient editor this follows the selected color key's HDR mode; opacity does not affect filtering.

WhimTex color fields and gradient keys open the WhimTex Color Picker. Use the hue ring and its central saturation/value square,
RGB or HSV channels, Hex, and A for transparency. HDR can be switched inside the picker; inputs that
require Standard or HDR lock this switch. Exposure adjusts HDR intensity in stops.
Changing the HDR switch alone preserves the stored color; editing in Standard replaces it with a bounded color.
Changes preview immediately. Closing the picker confirms them; Escape restores the opening color. There are no confirmation buttons.
The last selected **RGB 0–255**, **RGB 0–1** or **HSV** mode is remembered for all color pickers, including after restarting Unity. Switching modes does not change the color.
The bottom **Preview EV** slider (−10 to +10), styled like the gradient editor's footer, adjusts display brightness only. Lower it to inspect bright HDR colors. It affects the saturation/value square, slider gradients and color/History swatches, but not the hue ring, screen eyedropper, checkerboards or alpha values. Numeric values and saved colors remain unchanged. Each new picker starts at 0; **Exposure** still edits the actual HDR color.
Hexadecimal has a separate **#** prefix. Paste RGB or RGBA hex in either case, with or without `#` (`RRGGBB` / `RRGGBBAA`, or short `RGB` / `RGBA`). Confirm with Enter or leave the field. RGBA updates editable alpha; RGB preserves it. The field always displays six RGB digits, without alpha. HDR exposure affects RGB only.
Alt sampling also updates the open primary brush color picker for that WhimTex window, including its channels, Hex and markers. Layer, gradient and secondary-color pickers are unaffected. Without a matching picker, Alt continues to change the primary brush color normally.

The picker's eyedropper temporarily replaces the hue ring with a magnified pixel grid and a frame around the sampled pixel. Selecting a color or canceling sampling restores the ring. Escape during sampling cancels only the eyedropper, keeping the picker open and its current color unchanged. If the magnifier is unavailable in your Editor, ordinary eyedropper selection still works.

Channel sliders show color gradients, with a checkerboard under alpha. Original and new colors appear at the top right.
The hue ring and its selection marker have smoothed edges when the interface is scaled.
Both selection markers use a white circle with a subtle, thin black outline on the outside.
History is a collapsible, compact grid of the document's colors. New unique confirmed colors are inserted at the beginning, not every slider intermediate.
The gray **+** square at the start adds the current color without closing the picker. This explicit addition remains even if you later cancel the color selection with Escape. Existing colors are not duplicated.
Hover a color swatch to show the same eyedropper cursor as Alt sampling, with a thin dark outline for visibility on light colors. Click a swatch to reuse it and move it to the first color slot after **+**, or drag to reorder. Drag outside the history area until the swatch has a red outline,
then release to remove it. Return inside or press Escape before releasing to cancel the removal.
Right-click → Remove and Delete/Backspace also remove a swatch.
Removing a swatch does not alter colors already used by layers. Inputs outside a document have no document history.
Confirming a manually entered color that exactly matches an existing History color (including alpha and HDR intensity) moves it to the front without a duplicate. Intermediate manual edits and canceling them do not automatically record or promote a color. Explicit History actions—clicking a swatch, adding, reordering or removing—remain after canceling color selection with Escape.

The gradient editor also shows **History** above **Presets**, without the **+** button. Select a color key and click a swatch to apply its RGB and HDR intensity directly, without opening the color picker. Opacity keys and the selected key's position are preserved. The palette is disabled for opacity keys and midpoints; drag reordering and drag-out removal work as in the picker.

**History** in both windows and **Presets** in the gradient editor have matching dark foldout headers. Click a header to expand or collapse its palette; each section remembers its state between sessions. History colors remain saved with the document.

## Inspect a channel

**Channels** in the color picker follows the main preview's R/G/B/A buttons for document colors (brushes, layers, gradient keys and FX). Turn it off for ordinary color display. The ring, square and slider gradients adapt; swatches compare the original color in the upper-left half with the channel view in the lower-right half. Their single alpha bar still shows actual transparency. RGB/HSV/HEX numbers and saved History colors remain original; editing is not restricted to visible channels.

This choice is remembered across color pickers. Interface colors such as guides, UV overlays and checkerboards do not adapt. The eyedropper magnifier always shows the actual screen pixels. Mini-preview channel buttons are independent and do not affect the picker.
HDR color fields keep their intensity gradients in both diagonal halves; the shared alpha bar still represents the stored alpha.

The footer channel buttons let you see parts of the image separately:

- **R, G or B alone:** show that channel in black and white. Keep A on to retain transparency.
- **A alone:** show transparency as black and white.
- **Several color channels:** keep their colors and hide the others.
- **A off:** view colors without transparency.

These buttons also mask **new painting**. Disabled RGB channels receive zero;
if A is off, Brush, Pencil and Fill leave no mark. They do not change existing pixels.

## Paint bright HDR colors

Enable **HDR** beside EV in the main preview footer to choose colors with RGB values above ordinary white (1).
This is useful for luminous details you want to use with bloom.

HDR values are real, but the current WhimTex editor previews are SDR, including the color picker and gradient editor. An HDR monitor alone does not make them display extra physical brightness. Lower **Preview EV** to inspect bright values without changing the color; intensity gradients in color fields are visual indicators, not HDR monitor output. The picker, gradient editor and main preview have independent exposure controls.

In **Layer Settings → Color & Blending**, choose **HDR** in the header dropdown
to let the layer retain that extra brightness. **Standard** is the usual choice for ordinary artwork.

The HDR button controls color picking; it does not convert your existing image.
Turning it off lets you paint with the color without its extra intensity.
Turning it back on restores that intensity unless you have edited the color in between.
The gradient editor has its own HDR switch for the selected color key; it does not follow the main footer's shared HDR preference.

## Fine-tune a layer's color range

You can leave **Color & Blending** closed for most work. Expand it to set the two ranges separately:

| Setting | Controls |
| :--- | :--- |
| Color Range | Whether the layer itself keeps extra brightness. |
| Blend Range | Whether its blend works with that extra brightness or within the ordinary range. |

The dropdown in the foldout header changes both settings together.
A blank value means the two are different.

Switching to Standard does not erase stored HDR colors.
Use **Convert to 8-bit** only if you want to permanently reduce the stored color range.

## Edit a gradient

New gradients, including brush-tip and brush-tint gradients, use **Perceptual** unless a control explicitly defines another mode, such as Linear for Pattern gradients. Existing gradients retain their selected interpolation.

Select a Gradient layer to activate **Gradient Handles**, the contextual hand tool. Square handles change its geometry;
colored points move color keys. Click the line to add a key, or double-click a point to open WhimTex's
Color Picker. Alpha keys remain in the gradient editor.
Drag the small diamonds to move the midpoint between colors (except in Fixed mode).
Delete a selected color key with Delete while the preview has focus, or drag it away from the line and release.
At least one color key remains.
Position, size and rotation are controlled by the layer transform; there are no separate Center or Radius settings.
Radial, diamond and square gradients share position, scale and rotation controls. Angular (Circular) gradients have no
canvas controls yet. Select a basic tool to hide the handles, or press Escape when not dragging to return to your last basic tool. The contextual hand button restores the handles. Selecting another Gradient layer activates them automatically.

New Color Fill, Gradient and Noise layers use **Unbounded** tiling by default.

Click a gradient field to edit its colors and opacity. **Classic** gives familiar color blends;
**Linear** blends light, **Perceptual** keeps perceived color transitions more even,
and **Fixed** makes hard bands. **Smoothness** softens transitions around keys;
the small diamonds move the halfway point between neighboring keys. Fixed ignores both controls.
**Rounded** is the built-in algorithm for both new and old gradients. There is no Transition setting; older artwork may look different without needing migration or resaving.
**Rounded** prioritizes a smooth shoulder where the gradient meets a constant color.
It spreads the compensating speed change up to the adjacent midpoint, reducing visible shoulders within the fade.
Use **Perceptual** and **100% Smoothness** for the rounded color progression.
The rounding extends to both sides of an interior held boundary: its key stays in place,
but the evaluated color or opacity at that key may differ from its stored value.
Keys at 0 and 1 and ordinary interior keys remain exact. Midpoint positions remain fixed.
Some near-constant color remains; this is not a guarantee against every visible rim.
At 0% Smoothness interpolation is linear in the selected working color space;
Fixed uses hard bands. A narrow fade or a midpoint near an endpoint can still produce a visible rim.
**Wrap** controls samples outside 0–1: **Clamp** holds the nearest endpoint, **Repeat** tiles the gradient,
and **Mirror** reflects each repeated interval.

Double-click a color-key marker in the gradient editor to open its color picker. A single click selects it; dragging moves it.
Closing the color picker or canceling it with Escape returns to the gradient editor without closing that editor.
Use **HDR** beside the selected color when extra brightness is needed. Drag a key vertically
away from its track to delete it; each track keeps at least one key. Right-click a gradient field
to **Copy** or **Paste** an independent copy. SDF and Noise palettes start with **Perceptual** interpolation.

In **Presets**, click the **New** swatch to save the current gradient without naming it.
Click a swatch to apply it; right-click for **Copy** or **Delete**. Presets are
listed newest first, immediately after **New**. The folder is
chosen in User Settings; gradients use its **Gradients** subfolder. **↻** reloads the list.
Deleted presets can be recovered from **Gradients/.trash**. Built-in Unity gradient preset libraries are not imported.

**Paste** also accepts gradient JSON from an AI or another application. Copy a complete
[gradient JSON value](../AI/README.md#standalone-gradient-json), then right-click the gradient field
or the gradient strip in its editor and choose **Paste**.

## Rearrange channels with Swizzle

**Swizzle** chooses what goes into each output channel of a layer or group.
For example, choose R in the R, G and B fields to make a grayscale image from the red channel.

Each field offers the original channels, their inverses, black (`0`), white (`1`),
or a color channel multiplied by transparency (`R * A`, `G * A`, `B * A`).
Changing a group's Swizzle treats its contents as one image, so outside blending can look different.

## Pack several masks

Select layers and choose **Assign Channels** in the row menu.
The order is top to bottom, and each layer uses its red channel multiplied by its transparency.

- **1–3 layers:** assign them to R, G and B. The upper selected layers use Add to combine the masks.
- **4 layers:** assign them to R, G, B and A. This only sets Swizzle: with ordinary blending,
  the RGB layers have no visible opacity, so it is not a ready-to-export four-channel combination.

The command is unavailable for more than four layers. It leaves the layers separate.
