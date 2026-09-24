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

To show controls conditionally in the editor, put `// @param` declarations between `// @if _Mode == 1` (or `!=`) and `// @endif`. Only numeric `==` and `!=` comparisons are supported; the condition must reference an unconditional `float`, `bool` or `enum` parameter. Nested blocks are not supported. This only hides editor fields: values stay stored and continue to affect the shader. Preset export preserves the conditions.

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

In HLSL, `// @header(Lighting)` before a parameter adds a bold section heading, without a foldout. Use `// @helpbox(Your hint text.)` to show an informational help box above the next parameter. These directives are UI-only metadata and are preserved when saving presets and portable code; directives without a following parameter are ignored.

Use `// @group(Tint; _Parameter)` and `// @endgroup` to place controls in a bordered block and link a parameter declared unconditionally inside it to the header. A bool becomes an unlabeled checkbox to the left of the title; it only edits the value. Use `@if` to show or hide dependent controls, and use the bool in HLSL to enable or disable the effect itself. Compact enum, float, color, float2, float3, and float4 parameters appear on the right without a second label; the group title labels the value, and dragging a float's title changes its value. These controls are omitted from the body. `hidden` suppresses the normal body row but does not suppress an explicitly linked supported header control; unlinked hidden parameters remain invisible. Unsupported or multi-row controls stay in the body and leave the title plain. When all body rows are hidden by `@if`, the body collapses and only the header remains. Groups may contain `@if` blocks but cannot be nested. For a plain titled box use `// @group(Advanced)`; for a box without a title use `// @group`.

Add a custom UI label inline with a declaration, for example `// @param label(Tint Strength) float _Strength = 1`. `hidden` and `label(...)` may appear in either order, such as `// @param label(Optional Mode) hidden enum _Mode = Off {Off: 0, On: 1}`. Quote the label when it contains parentheses. Labels only affect the UI and are preserved when exporting presets.

Put `// @formerlyserializedas(_OldName)` immediately before a `// @param` declaration to migrate compatible values and parameter identity from a previous name when the FX is applied. Repeat the directive for multiple prior names. Update the HLSL to use the new uniform name; preset save/export preserves the aliases.

The `one` curve default is flat at 1, with keys at times 0 and 1.

Curve defaults also include `easeIn` for a slow start and `easeOut` for a slow finish. Both are quadratic 0→1 curves.

Effect authors can choose `curve _Profile = linear` or `curve _Profile = easeInOut` in a `// @param` declaration. Both start at (0,0) and end at (1,1); Ease In Out smoothly flattens at both ends.

**Color → Levels** offers a **Curve** after input black/white and Gamma, before output black/white. It starts linear. With **Preserve Color**, the curve remaps luminance; otherwise it remaps each RGB channel separately. Alpha is unchanged.

Texture source **Self** reads the image before the current FX, including earlier effects. **None** returns transparent pixels. These modes need no assigned asset or layer; Self continues to work when copied to another layer.

Vector parameters provide two, three or four numeric components. A `point` parameter is a `float2` in normalized canvas UV (bottom-left `(0, 0)` to top-right `(1, 1)`), defaulting to `(0.5, 0.5)`; **Edit on Canvas** adds a draggable point handle. A normal parameter provides a unit direction and **Edit on Canvas**. Drag its endpoint: near the center it faces the camera; at the maximum radius it points along the canvas. Click the endpoint to switch between **+** (toward the camera) and **−** (away).

A texture parameter can use **Texture** (an asset) or **Layer** (a layer in this document). Choose the source mode or drag a layer onto the parameter. Procedural and Drawing layers are supported, even when hidden. Groups provide their colored contents; hidden children remain hidden. Missing sources produce transparency, and circular references cannot be selected.

Procedural shapes written in an FX can follow the layer transform using `LayerToLocal(uv)`. See the [shader authoring reference](../ShaderFX.md) for the coordinate contract.

Use a custom shader effect when you need a look that the built-in layers do not provide.
You can use an existing effect and adjust its parameters without writing code.

**Profile** in Bevel Emboss maps the selected height channel to relief height. **Mapping** in Gradient Map redistributes brightness before choosing a gradient color. Both curves start linear.

Color Balance uses three signed RGB components. Gain, Levels, Threshold, ambient lighting and distortion offsets offer soft limits where appropriate. Levels and Threshold can work above 1 for HDR; blend amounts remain limited to 0–1. Pixelate and Posterize allow more than 64 levels and Gamma above 5 through numeric input.

The unlabeled checkbox in each Shader FX header enables or bypasses that effect without removing its settings. External FX references share this state; use **Embed** for an independent copy.

FX and Shader Processor results are cached while their inputs and parameters stay unchanged. Unity time
inputs (`_Time`, `_SinTime`, `_CosTime`, `_TimeParameters`, `unity_DeltaTime`) are unsupported:
their use is allowed, but **Apply** reports a warning and disables caching for that result.

## Apply an existing effect

1. Select the layer you want to change.
2. Use **+ Preset ▾** in its FX section and choose an effect by category.
3. Adjust its sliders, colors, textures, toggles and dropdowns. Changes appear immediately.

An effect can offer a slider and a dropdown for the same setting: changing either updates the shared
value. **Custom** means the current number is not one of the dropdown's predefined choices.

Hover over a parameter to read its description, if the effect's author supplied one.

Some sliders allow numbers past one or both ends of their visible range: type in the adjacent field
or drag the parameter label. Each end can independently be a hard or soft limit.
The thumb stays at the nearest endpoint, but the effect uses your number. Other sliders keep
both dragging and numeric input limited to their range, as chosen by the effect's author.

Effects can offer a curve field for adjusting a numeric profile. Click it to edit keys and tangents
with Unity's curve editor. It starts as a straight line from 0 to 1; values may go below 0 or above 1.
Saving an HLSL preset preserves the edited curve.

Effects can also offer a gradient field. Click its strip to edit colors, transparency and interpolation,
including HDR colors. New gradients start black-to-white unless the HLSL declaration supplies two endpoint colors,
for example `// @param gradient _Ramp = #FF0000FF -> #0000FF`. Hex colors use RGBA order; six digits imply full opacity.
Changes update the effect immediately.

In the gradient editor, **Wrap** chooses what happens outside 0–1: **Clamp** holds the endpoint colors, **Repeat** repeats the gradient, and **Mirror** alternates its direction. An effect that clamps its own input may never reach the repeated range.

Alternatively, drag a WhimTex effect `.hlsl` from Project onto a row in **Layers**.
Dropping it onto the preview or empty space in the list creates a **Shader Processor** at the top of the composition.
HLSL brush presets and files without the effect marker are not accepted.

If a preset contains invalid code or parameters, selecting it reports an error in Console and leaves the layer unchanged.

Each catalog effect has its own settings. The included **Color → Gain** adjusts brightness and tint;
**Transform → UV Transform** repositions the incoming image within a visible frame. Everything outside the frame is transparent,
so moving the image clips it at the frame. Distortion effects do not have this clipping.
Effects added to the project become available automatically; no preset folder setup is needed.

**+ Reference** selects a Shader FX asset whose settings are shared everywhere it is used.
**⋮ → Embed Copy** in the effect header creates an independent copy in the document without changing the external asset. The same menu contains **Move Up**, **Move Down** and **Remove**.
Use **⋮ → Copy FX**, then **Paste FX As New** on another row to insert an independent copy after it. Shader FX code and parameters are copied; layer-texture links stay within the same document and are cleared when pasted into another document. Material rows copy the Material reference.
Project HLSL effects receive code changes from their source `.hlsl` file.
To edit the code independently in the document, click **Embed Copy** under **Code**. Parameters appear below the header; **Code** contains the editor, **Apply**, **Save Preset…** and **Shader inputs**. Diagnostics appear when there is something to report.

**Editing in an external editor**

- **Open Code** opens a working file in Unity's selected script editor. Saving updates the draft in WhimTex; click **Apply** to compile it.
- **Open in VS Code** also installs WhimTex directive highlighting, completions and checks automatically in an isolated profile. Saving this working file requests **Apply** in Unity, without returning to the WhimTex window. The language mode remains **HLSL**; the bundled extension supports Restricted Mode.
- If the VS Code button is missing, set **User Settings → External Code Editor → VS Code Command**. WhimTex checks Unity's registered editors and PATH before this fallback.
- An invalid shader leaves the last working result visible and reports diagnostics. Save the document in WhimTex to keep your changes in the TIFF; saving the code file alone does not save it. After a Unity script reload, reopen the code from WhimTex to reconnect.

Effect order matters. Drag an FX header to reorder effects, or drop it onto another layer's row to move it there. **Move Up** and **Move Down** in the header context menu also change the order.

**HSV correction**

**Color → HSV** adjusts hue, saturation and brightness. **Hue** shifts the hue in degrees;
**Saturation** and **Value** are multipliers: 1 is neutral, 0 removes saturation or makes the image black.
**Amount** mixes the correction with the original. Gray pixels stay gray when changing hue or saturation.
Alpha is preserved and HDR brightness is supported. HSV correction treats negative RGB channels as zero;
neutral settings and Amount 0 leave the original unchanged.

**Negative**

**Color → Negative** blends the source RGB toward its inverse (`1 - RGB`) with **Amount**.
Alpha is preserved by default; enable **Invert Alpha** to apply the same blend to it.

**Mask**

**Color → Mask** reads a chosen channel from **Mask** (Self by default). **Transform** positions the mask, **Profile** remaps its values, and **Invert** reverses the result. **Apply To → Channels** selects which channels to multiply; only alpha is enabled initially. In **Color** mode, each color component controls how much of the original channel to preserve: 1 leaves it unchanged, 0 applies the full mask. This is not a tint. **Amount** scales the overall effect.

**Gradient mapping**

**Color → Gradient Map** recolors shadows, midtones and highlights using a gradient.
Click **Gradient** to choose colors, use **Amount** to mix with the original image and
**Reverse** to swap the mapping direction. Input brightness outside 0..1 uses the endpoint colors.
The original alpha is preserved; gradient alpha is ignored.

**Pixelation and dithering**

**Stylization → Pixelate** replaces every block of **Pixel Size** canvas pixels with a single value.
**Average** samples a 4×4 grid inside the block instead of its center, so thin details survive.
**Levels** sets how many values each channel keeps and **Gamma** moves the tonal steps between
shadows and highlights. **Dither** picks the pattern that spreads the error between levels:
**Bayer2**, **Bayer4** and **Bayer8** give the classic ordered look, **Interleaved** is irregular
noise, **Checker** is a two-tone grid, **Halftone** builds a clustered-dot screen and **Hash** is
stable noise without a visible grid. The pattern is evaluated per block, so it stays visible after
pixelation. **Dither Strength** weakens it down to plain rounding. **Color → Quantization** uses Levels and Gamma; **Color → One Bit** uses **Low Color** and **High Color** by luminance instead. **Offset** shifts the grid without moving the layer. Alpha is preserved unless **Alpha Clip** is enabled; **Alpha Cutoff** sets the transparent/opaque boundary.
The same pattern list works per pixel in **Stylization → Posterize**.

**Other stylization effects**

- **Step** thresholds the enabled color channels separately. Red, Green and Blue start enabled; Alpha starts disabled. Choose **Hard** for a two-value result or **Smoothstep** to soften the transition with **Hardness**. **Apply To → Color** uses RGBA as per-channel effect strengths: 0 preserves the original channel, 1 applies the full step. It does not replace the image with that color. **Threshold** instead tests luminance (or alpha) and maps the result between two colors, optionally with a soft boundary; it preserves source alpha.
- **Halftone** turns the image into monochrome, CMYK or RGB dot screens. Set dot size and shape; CMYK/RGB modes also expose screen angles and manual or automatic plate registration.
- **Chromatic Aberration** shifts red and blue in opposite directions, radially from a point or along an angle. **Amount** is in canvas pixels; green and alpha stay unchanged.
- **CRT** combines curved edges, scanlines, RGB phosphor stripes, vignette, color fringing, grain and flicker. **VHS** adds line wobble, chroma bleed, noise and a moving tracking band. **Seed** changes the deterministic pattern; **Effect Time** selects another frame.
- **Digital Glitch** combines line tears, independently segmented corruption blocks, channel shifts, color/noise/alpha artifacts and optional gradient tinting. **Seed**, **Effect Time** and **Frame Rate** control its repeatable animated variation; **Block Order** chooses independent row-first or column-first block layouts.

**Lighting and embossing**

- **Normal Map → Lighting** turns an RGB normal map into a shaded surface. **Normals** defaults to Self. Set **Light Direction** with its canvas handle, then adjust **Base Color**, light/shadow colors, **Intensity** and **Ambient**. Keep **Packed Color** enabled for the Normal Map layer's default output; disable it for Linear Data. **Flip Y** reverses the green-axis convention. Platform-packed normal textures are not supported.
- **Lighting → Bevel Emboss** derives normals from **Height Map** (Self by default, or another layer). **Height Channel** selects luminance, R, G, B or Alpha; **Profile** maps height, **Depth** raises or engraves, and **Smoothing** sets the sampling radius in document pixels. For SDF, the visible gradient and distance ranges define bevel width.

Both effects use **Base Color alpha** to blend transparent lighting (0) into the filled, shaded surface (1). Intermediate values crossfade with alpha-aware mixing. RGB tints the surface; **Ambient** fades in with it. **Output** selects Both, Highlight Only or Shadow Only for the transparent component and has no effect at Base Color alpha = 1. Light Color/Shadow Color alpha controls that component's strength. Overall opacity and blending belong to the layer. Both effects default to Base Color alpha 0. Re-add the preset to update an older embedded copy.

**Normal map normalization**

**Normal Map → Normalize** restores unit-length normals while preserving alpha.
Use it after processing an RGB normal map if its vectors have changed length.
Neutral `(0.5, 0.5, 1)` stays unchanged; an undefined direction becomes neutral.
Keep **Packed Color** enabled for the Normal Map layer's default encoding; disable it for **Linear Data**.
This toggle accounts for color encoding, but does not unpack platform-specific normal-map formats.

## Save your own preset

In the effect's code editor, click **Save Preset…**. This creates an `.hlsl` file
with the current parameter values as defaults, including colors and Transform 2D.
The file name becomes the preset name. You can save in the user library's **ShaderFX**
subfolder or anywhere under the project's **Assets** folder. Overwriting keeps a `.bak` copy.

Set the shared library location in **User Settings → Presets → Presets Folder**. Its **Brushes**
and **ShaderFX** subfolders hold the two kinds of presets. You can also place existing
HLSL presets in ShaderFX or its subfolders; reopen **+ Preset** to see them under **User**.
User presets are copied into the document; later changes to their files do not change
effects you have already added. Adding a preset from the project's **Assets** creates a linked instance:
editing its file updates the effects linked to it. Saving a preset does not relink the effect you exported.

Texture defaults are references, not embedded images. To use them in another project,
also transfer the referenced texture assets with their `.meta` files, or assign replacements.

Edited gradients are saved in the document. A simple two-endpoint gradient can also be exported as an HLSL default. Extra stops or non-default interpolation, smoothness or wrapping must be simplified before HLSL export.

## Adjust an effect on the canvas

Effects with a Transform 2D parameter offer **Edit on Canvas**. Select it to show a green frame,
then move, resize or rotate the frame. Rotation is around its center; there is no pivot control.
Use Ctrl/Cmd + corner to distort, Ctrl/Cmd + edge to skew, or Ctrl/Cmd + Alt + Shift + corner
for paired perspective adjustment. Alt with Ctrl/Cmd moves the opposite corner symmetrically.
Position, Size and Rotation preserve the deformation; **Reset Transform** removes it.
Click the button again or switch tools to leave this mode. Escape cancels the current drag.

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

The frame edits the effect, not the layer transform. Its purpose depends on the effect:
it may place an image, change a pattern's scale, or define a local area. It is not automatically a mask,
but **UV Transform** leaves pixels outside the frame transparent.

## Distortion presets

Choose **FX → + Preset → Distortion → Spherize**, **Twirl**, **Radial Shear** or **Displacement Map**.

- **Spherize / Mode:** `Classic` keeps the existing unbounded radial distortion; `Sphere` projects the image onto a sphere and clips it to a circular silhouette. The edge is antialiased by about one pixel.
- **Spherize / Strength:** positive values bulge the center; negative values pinch it. In `Classic`, zero leaves the image unchanged. In `Sphere`, zero keeps the circular shape but removes the texture distortion.
- **Twirl / Angle:** twists around the center; the sign reverses direction. The angle is measured in degrees at the frame's local radius 1 and grows with distance.
- **Radial Shear:** twists sampling coordinates progressively farther from **Center**; **Strength** controls the direction and amount, while **Offset** adds a base shift.
- **Area / Edit on Canvas:** move, resize or rotate the green coordinate frame. Stretch it to make the distortion elliptical.
- **Displacement Map / Mode:** `VectorRG` reads R/G as a signed direction field; the `Neutral` value (0.5 by default) means no offset. Use a linear/data texture for vector and height maps. X/Y strengths are in canvas pixels. `Grayscale` reads a selected channel and moves pixels horizontally, vertically, radially, tangentially, or along an angle. `ParallaxOcclusion` treats the selected map channel as height and shifts the sample along a virtual view ray.
- **Parallax / Depth and View:** **Depth** sets the height range in canvas pixels; **View Angle** sets the ray direction, and a lower **View Elevation** increases the shift. **Invert Height** swaps raised and recessed areas. **Parallax Steps** selects the quality/cost tradeoff (4–32 height samples); the default is 8.
- **Map / Transform and Wrap:** position the map independently. `Clamp`, `Repeat`, and `Mirror` control map coordinates; they do not affect the displaced image's edges.
- **Strength Mask:** `Constant1` is the default, so one map is enough and no strength mask is required. `MapChannel` reuses a channel of that same map, `InputAlpha` follows the input image's alpha, and `SeparateTexture` adds an optional second mask texture. Invert the mask or remap it with **Mask Profile**.
- **Output / Mix and Input Edge:** blend the displaced sample with the original, and choose how out-of-bounds image samples are handled: `Clamp`, `Repeat`, `Mirror`, or `Transparent`.

`Classic` Spherize, Twirl and Polar Coordinates do not mask at the frame edge, so distortion continues outside it.
`Sphere` is the exception: it creates a circular mask with a crisp, antialiased edge. Strong settings
in unmasked modes can reveal areas beyond the input image, where its edge pixels are extended. RGB and alpha are distorted together.

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
