---
title: "Shader FX and Processor"
parent: "English"
nav_order: 9
lang: "en"
permalink: "/en/shader-fx/"
alternate: "ru/shader-fx.md"
previous_page: "en/blending.md"
next_page: "en/preview.md"
---

# Shader FX and Processor

Use a custom shader effect when you need a look that the built-in layers do not provide.
You can use an existing effect and adjust its parameters without writing code.

## Apply an existing effect

1. Select the layer you want to change.
2. Use **+ Preset ▾** in its FX section and choose an effect by category.
3. Adjust the effect's exposed sliders, colors or textures.
4. Project HLSL effects follow their source file. Choose **Embed Copy** if you want to edit their code independently inside the document.

Each catalog effect has its own settings. The included **Color → Gain** adjusts brightness and tint;
**Transform → UV Transform** repositions the incoming image.
Effects added to the project become available automatically; no preset folder setup is needed.

**+ Reference** is still available for choosing an asset manually. Its settings are shared with other
places using that asset; **Embed** makes an independent copy.
When several effects are present, their order matters.

## Save your own preset

In the effect's code editor, click **Save HLSL Preset…**. This creates an `.hlsl` file
with the current parameter values as defaults, including colors and Transform 2D.
The file name becomes the preset name. You can save in the user library's **ShaderFX**
subfolder or anywhere under the project's **Assets** folder. Overwriting keeps a `.bak` copy.

Set the shared library location in **User Settings → Presets Folder**. Its **Brushes**
and **ShaderFX** subfolders hold the two kinds of presets. You can also place existing
HLSL presets in ShaderFX or its subfolders; reopen **+ Preset** to see them under **User**.
User presets are copied into the document; later changes to their files do not change
effects you have already added. Project HLSL effects still follow their source files.

Texture defaults are references, not embedded images. To use them in another project,
also transfer the referenced texture assets with their `.meta` files, or assign replacements.

## Adjust an effect on the canvas

Effects with a Transform 2D parameter offer **Edit on Canvas**. Select it to show a green frame,
then move, resize or rotate the frame. Rotation is around its center; there is no pivot control.
Click the button again or switch tools to leave this mode. Escape cancels the current drag.

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

The frame edits the effect, not the layer transform. Its purpose depends on the effect:
it may place an image, change a pattern's scale, or define a local area. It is not automatically a mask.

## Distortion presets

Choose **FX → + Preset → Distortion → Spherize** or **Twirl**.

- **Spherize / Strength:** positive values expand the center; negative values pinch it. Zero leaves the image unchanged.
- **Twirl / Angle:** twists around the center; the sign reverses direction. The angle is measured in degrees at the frame's local radius 1 and grows with distance.
- **Area / Edit on Canvas:** move, resize or rotate the green coordinate frame. Stretch it to make the distortion elliptical.

These effects do not mask or fade at the frame's edge. They continue outside it, including beyond
local coordinates 0–1. Strong settings can sample outside the input image; those samples use the
input texture's edge addressing. RGB and alpha are sampled together.

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

Writing an effect is optional. The [shader authoring reference](../ShaderFX.md)
is for creating code and reusable libraries.
