---
title: "Color, HDR and channels"
parent: "English"
nav_order: 11
lang: "en"
description: "Edit HDR textures and pack RGBA channels in Unity with WhimTex. Inspect individual channels, configure Mapping and control layer color and blend ranges."
permalink: "/en/color/"
translations: "en/color.md,ru/color.md,zh/color.md"
previous_page: "en/preview.md"
next_page: "en/post-fx.md"
---

# Color, HDR and channels

For ordinary painting, leave **HDR** off and all four **R / G / B / A** buttons on.
Use the controls below when you need extra brightness, channel masks or texture data.

## Choose and reuse colors

Click a color field or double-click a gradient color key to open the WhimTex Color Picker.
Choose a hue on the ring, then saturation and brightness in the central square.
Original and new colors appear at the top right; changes preview immediately.

Close the window to confirm. Press Escape to restore the opening color. There are no confirmation buttons.

### Enter a color

Use **RGB 0–255**, **RGB 0–1** or **HSV** for numeric entry, and **A** for transparency.
The selected numeric mode is remembered across pickers and Unity sessions; switching it does not change the color.
Channel sliders show gradients, with a checkerboard under alpha.

**Hexadecimal** accepts these formats, in either case and with or without `#`:

| Format | Example | Alpha |
| :--- | :--- | :--- |
| RGB | `FFAA66` or `FA6` | Preserved |
| RGBA | `FFAA6680` or `FA68` | Updated if editable |

Press Enter or leave the field to apply. The field always displays six RGB digits with a separate **#** prefix.

### HDR and viewing exposure

**HDR** unlocks RGB values above 1. Inputs restricted to Standard or HDR lock this switch.
Switching alone preserves the stored color; editing in Standard replaces it with a bounded color.

| Control | Effect |
| :--- | :--- |
| Exposure | Changes the actual HDR intensity in stops. RGB changes; alpha does not. |
| Preview EV | Changes display brightness only, from −10 to +10. Saved colors and numeric values do not change. |

Lower **Preview EV** to inspect bright colors. It affects the square, slider gradients and color/History swatches,
not the hue ring, screen eyedropper, checkerboards or alpha. Each new picker starts at 0.
Its footer matches the gradient editor; their exposure settings are independent.

### Sample with the eyedropper

The picker's eyedropper replaces the ring with a magnified pixel grid and a frame around the sampled pixel.
Choose a color or cancel to restore the ring. During sampling, Escape cancels only the eyedropper;
the picker stays open and retains its current color. If your Editor cannot show the magnifier, ordinary sampling still works.

Alt sampling in WhimTex also updates that window's open **primary brush color** picker, including numbers and markers.
Layer, gradient and secondary-color pickers are unaffected. Without a matching picker, Alt still changes the primary brush color.

### Reuse colors from History

**History** stores colors with the document. Confirmed colors go to the front; intermediate slider values are not recorded.
Confirming a color already in History moves it to the front without a duplicate, including when entered manually.
Matching includes alpha and HDR intensity. Fields outside a document have no History.

| Action | Result |
| :--- | :--- |
| Click a color | Apply it and move it to the first slot after **+**. |
| Click **+** | Add the current color without closing the picker. |
| Drag within History | Reorder colors. |
| Drag outside until the outline turns red, then release | Remove the color. Return inside or press Escape before release to cancel. |
| Right-click → Remove, or Delete/Backspace | Remove the selected swatch. |

Removing a swatch does not change layers that use that color. Explicit History actions remain even if you later
cancel color selection with Escape; canceled manual color edits are not recorded.

With **HDR off**, History hides colors with RGB outside 0–1 without deleting them.
In the gradient editor, filtering follows the selected color key's HDR mode; alpha does not affect filtering.

The gradient editor's History sits above **Presets**, without **+**.
Select a color key, then click a swatch to apply RGB and HDR intensity; opacity keys and the key's position stay unchanged.
The palette is unavailable for opacity keys and midpoints. Drag sorting and removal work as in the picker.

History and gradient Presets remember their expanded state between sessions.

## Inspect a channel

The footer channel buttons let you see parts of the image separately:

- **R, G or B alone:** show that channel in black and white. Keep A on to retain transparency.
- **A alone:** show transparency as black and white.
- **Several color channels:** keep their colors and hide the others.
- **A off:** view colors without transparency.

These buttons also mask **new painting**. Disabled RGB channels receive zero;
if A is off, Brush, Pencil and Fill leave no mark. They do not change existing pixels.

### Color picker channel view

**Channels** in the color picker follows the R/G/B/A buttons in Canvas View for document colors (brushes, layers, gradient keys and FX). Turn it off for ordinary color display. The ring, square and slider gradients adapt; swatches compare the original color in the upper-left half with the channel view in the lower-right half. Their single alpha bar still shows actual transparency. RGB/HSV/HEX numbers and saved History colors remain original; editing is not restricted to visible channels.

This choice is remembered across color pickers. Interface colors such as guides, UV overlays and checkerboards do not adapt. The eyedropper magnifier always shows the actual screen pixels. Layer Preview channel buttons are independent and do not affect the picker.
HDR color fields keep their intensity gradients in both diagonal halves; the shared alpha bar still represents the stored alpha.

## Paint bright HDR colors

Enable **HDR** beside EV in the Canvas View footer to choose colors with RGB values above ordinary white (1).
This is useful for luminous details you want to use with bloom.

HDR values are real, but the current WhimTex editor previews are SDR, including the color picker and gradient editor. An HDR monitor alone does not make them display extra physical brightness. Lower **Preview EV** to inspect bright values without changing the color; intensity gradients in color fields are visual indicators, not HDR monitor output. The picker, gradient editor and Canvas View have independent exposure controls.

In **Layer Settings → Rendering**, choose **HDR** in the header dropdown
to let the layer retain that extra brightness. **Standard** is the usual choice for ordinary artwork.

The HDR button controls color picking; it does not convert your existing image.
Turning it off lets you paint with the color without its extra intensity.
Turning it back on restores that intensity unless you have edited the color in between.
The gradient editor has its own HDR switch for the selected color key; it does not follow the main footer's shared HDR preference.

## Fine-tune a layer's color range

You can leave **Rendering** closed for most work. Expand it to set the two ranges separately:

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

### Interpolation and smoothing

Click a gradient field to edit its colors and opacity. **Classic** gives familiar color blends;
**Linear** blends light, **Perceptual** keeps perceived color transitions more even,
and **Fixed** makes hard bands. **Smoothness** softens transitions around keys;
the small diamonds move the halfway point between neighboring keys. Fixed ignores both controls.

**Wrap** controls samples outside 0–1: **Clamp** holds the nearest endpoint, **Repeat** tiles the gradient,
and **Mirror** reflects each repeated interval.

### Edit keys

Double-click a color-key marker in the gradient editor to open its color picker. A single click selects it; dragging moves it.
Closing the color picker or canceling it with Escape returns to the gradient editor without closing that editor.
Use **HDR** beside the selected color when extra brightness is needed. Drag a key vertically
away from its track to delete it; each track keeps at least one key. Right-click a gradient field
to **Copy** or **Paste** an independent copy. SDF and Noise palettes start with **Perceptual** interpolation.

### Save and reuse gradients

In **Presets**, click the **New** swatch to save the current gradient without naming it.
Click a swatch to apply it; right-click for **Copy** or **Delete**. Presets are
listed newest first, immediately after **New**. The folder is
chosen in User Settings; gradients use its **Gradients** subfolder. **↻** reloads the list.
Deleted presets can be recovered from **Gradients/.trash**. Built-in Unity gradient preset libraries are not imported.

**Paste** also accepts gradient JSON from an AI or another application. Copy a complete
[gradient JSON value](../AI/README.md#standalone-gradient-json), then right-click the gradient field
or the gradient strip in its editor and choose **Paste**.

Old gradient JSON is no longer accepted. Use the current gradient format;
retired fields and previous names are not migrated.

### Shape the gradient on canvas

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

### How Rounded smoothing behaves

**Rounded** is built in; there is no Transition setting.
**Rounded** prioritizes a smooth shoulder where the gradient meets a constant color.
It spreads the compensating speed change up to the adjacent midpoint, reducing visible shoulders within the fade.
Use **Perceptual** and **100% Smoothness** for the rounded color progression.
The rounding extends to both sides of an interior held boundary: its key stays in place,
but the evaluated color or opacity at that key may differ from its stored value.
Keys at 0 and 1 and ordinary interior keys remain exact. Midpoint positions remain fixed.
Some near-constant color remains; this is not a guarantee against every visible rim.
At 0% Smoothness interpolation is linear in the selected working color space;
Fixed uses hard bands. A narrow fade or a midpoint near an endpoint can still produce a visible rim.

<a id="rearrange-channels-with-swizzle"></a>

## Rearrange channels with Channel Mapping

**Mapping** chooses what goes into each output channel of a layer or group.
For example, choose R in the R, G and B fields to make a grayscale image from the red channel.

Each field offers the original channels, their inverses, black (`0`), white (`1`),
or a color channel multiplied by transparency (`R * A`, `G * A`, `B * A`).
`Luminance` combines RGB into grayscale brightness; `Luminance * A` also accounts for transparency.

The arrow button on the right offers builtin presets:

- **Default:** restore `R G B A`.
- **Default without Alpha:** keep RGB and make the image opaque (`R G B 1`).
- **R**, **G**, **B:** put brightness in the selected color channel, set the other two to `0` and alpha to `1`.
- **Luminance to Alpha:** make RGB white and use brightness as alpha (`1 1 1 Luminance`).
- **Alpha to Grayscale:** show alpha as an opaque grayscale image (`A A A 1`).

Changing a group's Mapping treats its contents as one image, so outside blending can look different.

## Pack several masks

Select layers and choose **Assign Channels** in the row menu.
The order is top to bottom, and each layer uses its red channel multiplied by its transparency.

- **1–3 layers:** assign them to R, G and B. The upper selected layers use Add to combine the masks.
- **4 layers:** assign them to R, G, B and A. This only sets Mapping: with ordinary blending,
  the RGB layers have no visible opacity, so it is not a ready-to-export four-channel combination.

The command is unavailable for more than four layers. It leaves the layers separate.
