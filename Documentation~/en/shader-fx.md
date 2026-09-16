---
title: "Shader FX and Processor"
parent: "English"
nav_order: 9
lang: "en"
permalink: "/en/shader-fx/"
translations: "en/shader-fx.md,ru/shader-fx.md,zh/shader-fx.md"
previous_page: "en/blending.md"
next_page: "en/preview.md"
---

# Shader FX and Processor

Use a custom shader effect when you need a look that the built-in layers do not provide.
You can use an existing effect and adjust its parameters without writing code.

## Apply an existing effect

1. Select the layer you want to change.
2. Use **+ Preset ▾** in its FX section and choose an effect by category.
3. Adjust its sliders, colors, textures, toggles and dropdowns. Changes appear immediately.

An effect can offer a slider and a dropdown for the same setting: changing either updates the shared
value. **Custom** means the current number is not one of the dropdown's predefined choices.

Hover over a parameter to read its description, if the effect's author supplied one.

Effects can also offer a gradient field. Click its strip to edit colors, transparency and interpolation,
including HDR colors. New gradients start black-to-white; changes update the effect immediately.

Alternatively, drag a WhimTex effect `.hlsl` from Project onto a row in **Layers**.
Dropping it onto the preview or empty space in the list creates a **Shader Processor** at the top of the composition.
HLSL brush presets and files without the effect marker are not accepted.

If a preset contains invalid code or parameters, selecting it reports an error in Console and leaves the layer unchanged.

Each catalog effect has its own settings. The included **Color → Gain** adjusts brightness and tint;
**Transform → UV Transform** repositions the incoming image within a visible frame. Everything outside the frame is transparent,
so moving the image clips it at the frame. Distortion effects do not have this clipping.
Effects added to the project become available automatically; no preset folder setup is needed.

**+ Reference** selects a Shader FX asset whose settings are shared everywhere it is used.
**Embed** on the effect row creates an independent copy in the document without changing the external asset.
Project HLSL effects receive code changes from their source `.hlsl` file.
To edit the code independently in the document, click **Embed Copy** under **Code & Parameters**.

Effect order matters: the **↑** and **↓** arrows on each effect row apply it earlier or later in the sequence.

**Gradient mapping**

**Color → Gradient Map** recolors shadows, midtones and highlights using a gradient.
Click **Gradient** to choose colors, use **Amount** to mix with the original image and
**Reverse** to swap the mapping direction. Input brightness outside 0..1 uses the endpoint colors.
The original alpha is preserved; gradient alpha is ignored.

**Normal map normalization**

**Normal Map → Normalize** restores unit-length normals while preserving alpha.
Use it after processing an RGB normal map if its vectors have changed length.
Neutral `(0.5, 0.5, 1)` stays unchanged; an undefined direction becomes neutral.
Keep **Packed Color** enabled for the Normal Map layer's default encoding; disable it for **Linear Data**.
This toggle accounts for color encoding, but does not unpack platform-specific normal-map formats.

## Save your own preset

In the effect's code editor, click **Save HLSL Preset…**. This creates an `.hlsl` file
with the current parameter values as defaults, including colors and Transform 2D.
The file name becomes the preset name. You can save in the user library's **ShaderFX**
subfolder or anywhere under the project's **Assets** folder. Overwriting keeps a `.bak` copy.

Set the shared library location in **User Settings → Presets → Presets Folder**. Its **Brushes**
and **ShaderFX** subfolders hold the two kinds of presets. You can also place existing
HLSL presets in ShaderFX or its subfolders; reopen **+ Preset** to see them under **User**.
User presets are copied into the document; later changes to their files do not change
effects you have already added. Presets saved under the project's **Assets** remain linked to their files:
editing a file updates all effects using it.

Texture defaults are references, not embedded images. To use them in another project,
also transfer the referenced texture assets with their `.meta` files, or assign replacements.

Edited gradient keys are saved in the document, but not in an exported HLSL preset yet.
A new instance of that preset starts with a black-to-white gradient.

## Adjust an effect on the canvas

Effects with a Transform 2D parameter offer **Edit on Canvas**. Select it to show a green frame,
then move, resize or rotate the frame. Rotation is around its center; there is no pivot control.
Click the button again or switch tools to leave this mode. Escape cancels the current drag.

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

The frame edits the effect, not the layer transform. Its purpose depends on the effect:
it may place an image, change a pattern's scale, or define a local area. It is not automatically a mask,
but **UV Transform** leaves pixels outside the frame transparent.

## Distortion presets

Choose **FX → + Preset → Distortion → Spherize** or **Twirl**.

- **Spherize / Strength:** positive values expand the center; negative values pinch it. Zero leaves the image unchanged.
- **Twirl / Angle:** twists around the center; the sign reverses direction. The angle is measured in degrees at the frame's local radius 1 and grows with distance.
- **Area / Edit on Canvas:** move, resize or rotate the green coordinate frame. Stretch it to make the distortion elliptical.

Distortion effects—Spherize, Twirl and Polar Coordinates—do not mask or fade at the frame's edge.
Distortion continues outside it. Strong settings can reveal areas beyond the input image,
where its edge pixels are extended. RGB and alpha are distorted together.

### Polar coordinates

**Distortion → Polar Coordinates** contains two effects:

- **To Polar** wraps a strip into a circle: horizontal runs around the center, vertical runs outward.
- **From Polar** unwraps a circle into a strip: left to right covers one full turn, bottom to top covers distance from the center.

**Area / Edit on Canvas** positions and shapes the circle: the output circle for To Polar, or the source circle for From Polar.
**Angle Offset** shifts the start of the turn in degrees; zero starts to the right of the center and runs counterclockwise.
**Radial Offset** shifts the starting radius: positive values move the pattern outward in To Polar,
or start sampling farther from the center in From Polar. One unit reaches from the center to the frame edge along its axes.

Radius continues beyond the frame without a fade. Match the left and right edges of the source strip
to avoid a visible seam around the circle. The entire strip width converges at the center,
so unwrapping cannot recover details lost at that point.

## Affect one layer or the image below?

**Shader FX** belongs to one layer and changes that layer's image.

A **Shader Processor** is a separate layer that changes the combined image below it.
Place it above the layers you want to process.
Use **Normal** blending and lower Opacity to mix the effect with the original;
hide the Processor to compare before and after.

In a Pass Through group, a Processor can also affect the background beneath the group.
Use an isolated group if the effect should stay within that group's contents.

Unlike [Post FX preview](post-fx.md), both are included in the saved image.

## Create your own effect

If you have shader code, use **+ Shader FX**, paste it into the editor and click **Apply**.
The code and settings stay with the document; no separate file is required.
If the code contains an error, the previous working version stays visible.

Custom HLSL effects and brushes can use the built-in noise library for grain,
organic masks and distortion. See the [noise functions and example](../AI/README.md#built-in-noise-library).

Writing an effect is optional. The [shader authoring reference](../ShaderFX.md)
is for creating code and reusable libraries.
