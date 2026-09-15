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

## Inspect a channel

The footer channel buttons let you see parts of the image separately:

- **R, G or B alone:** show that channel in black and white. Keep A on to retain transparency.
- **A alone:** show transparency as black and white.
- **Several color channels:** keep their colors and hide the others.
- **A off:** view colors without transparency.

These buttons also mask **new painting**. Disabled RGB channels receive zero;
if A is off, Brush, Pencil and Fill leave no mark. They do not change existing pixels.

## Paint bright HDR colors

Enable **HDR** beside EV to choose colors brighter than ordinary white.
This is useful for luminous details you want to use with bloom.

In **Layer Settings → Color & Blending**, choose **HDR** in the header dropdown
to let the layer retain that extra brightness. **Standard** is the usual choice for ordinary artwork.

The HDR button controls color picking; it does not convert your existing image.
Turning it off lets you paint with the color without its extra intensity.
Turning it back on restores that intensity unless you have edited the color in between.

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

Select a Gradient layer to show its controls on the canvas. Square handles change its geometry;
colored points move color keys. Click the line to add a key, or double-click a point to open Unity's
Color Picker. Alpha keys remain in the gradient editor.
Drag the small diamonds to move the midpoint between colors (except in Fixed mode).
Delete a selected color key with Delete while the preview has focus, or drag it away from the line and release.
At least one color key remains.
Position, size and rotation are controlled by the layer transform; there are no separate Center or Radius settings.
Radial, diamond and square gradients share position, scale and rotation controls. Angular (Circular) gradients have no
canvas controls yet. The controls disappear when another layer is selected; no extra mode is needed.

New Gradient and Noise layers use **Unbounded** tiling by default.

Click a gradient field to edit its colors and opacity. **Classic** gives familiar color blends;
**Linear** blends light, **Perceptual** keeps perceived color transitions more even,
and **Fixed** makes hard bands. **Smoothness** softens transitions around keys;
the small diamonds move the halfway point between neighboring keys. Fixed ignores both controls.

Use **HDR** beside the selected color when extra brightness is needed. Drag a key vertically
away from its track to delete it; each track keeps at least one key. Right-click a gradient field
to **Copy** or **Paste** an independent copy. SDF layers start with **Linear** interpolation.

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
