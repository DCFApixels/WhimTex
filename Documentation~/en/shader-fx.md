---
title: "Shader FX and Processor"
parent: "English"
nav_order: 9
has_children: true
lang: "en"
permalink: "/en/shader-fx/"
translations: "en/shader-fx.md,ru/shader-fx.md,zh/shader-fx.md"
previous_page: "en/blending.md"
next_page: "en/preview.md"
---

# Shader FX and Processor

Use **Shader FX** to change one layer or **Shader Processor** to process the image below it. Both support ready-made presets and your own HLSL. No code is needed to use a preset.

## Affect one layer or the image below?

**Shader FX** belongs to one layer and changes that layer's image.

A **Shader Processor** is a separate layer that changes the combined image below it.
Place it above the layers you want to process.
Use **Normal** blending and lower Opacity to mix the effect with the original;
hide the Processor to compare before and after.

In a Pass Through group, a Processor can also affect the background beneath the group.
Use an isolated group if the effect should stay within that group's contents.

Unlike [Post FX preview](post-fx.md), both are included in the saved image.

## Apply an existing effect

1. Select the layer you want to change.
2. Use **+ Preset ▾** in its FX section and choose an effect by category.
3. Adjust its sliders, colors, textures, toggles and dropdowns. Changes appear immediately.

Alternatively, drag a WhimTex effect `.hlsl` from Project onto a row in **Layers**.
Dropping it onto Canvas View or empty space in the list creates a **Shader Processor** at the top of the composition.
HLSL brush presets and files without the effect marker are not accepted.

If a preset contains invalid code or parameters, selecting it reports an error in Console and leaves the layer unchanged.

Each catalog effect has its own settings. The included **Color → Gain** adjusts brightness and tint;
**Transform → UV Transform** repositions the incoming image within a visible frame. Its default
**Input Tiling → Clip** makes the area outside the frame transparent; Repeat or Mirror fills it with tiles.
Effects added to the project become available automatically; no preset folder setup is needed.

## Parameter controls

The unlabeled checkbox in each Shader FX header enables or bypasses that effect without removing its settings. External FX references share this state; use **Embed** for an independent copy.

Hover over a parameter to read its description, if the effect's author supplied one.

### Numbers and limits

Numeric FX header fields have a **↔** handle: drag left or right to change the value. Shift gives finer adjustment; Ctrl makes it faster.

An effect can offer a slider and a dropdown for the same setting: changing either updates the shared
value. **Custom** means the current number is not one of the dropdown's predefined choices.

Some sliders allow numbers past one or both ends of their visible range: type in the adjacent field
or drag the parameter label. Each end can independently be a hard or soft limit.
The thumb stays at the nearest endpoint, but the effect uses your number. Other sliders keep
both dragging and numeric input limited to their range, as chosen by the effect's author.

Gain, Levels, Threshold, ambient lighting and distortion offsets offer soft limits where appropriate. Levels and Threshold can work above 1 for HDR; blend amounts remain limited to 0–1. Pixelate and Posterize allow more than 64 levels and Gamma above 5 through numeric input.

### Texture sources

A texture parameter can use **Texture** (an asset) or **Layer** (a layer in this document). Choose the source mode or drag a layer onto the parameter. Procedural and Drawing layers are supported, even when hidden. Groups provide their colored contents; hidden children remain hidden. Missing sources produce transparency, and circular references cannot be selected.

Texture source **Self** reads the image before the current FX, including earlier effects. **None** returns transparent pixels. These modes need no assigned asset or layer; Self continues to work when copied to another layer.

### Points and directions

Vector parameters provide two, three or four numeric components. A `point` parameter sets a position: bottom-left `(0, 0)`, top-right `(1, 1)`, default `(0.5, 0.5)`. Click the hand icon (**Edit on Canvas**) beside the field to drag the point. You can drag or enter coordinates outside the canvas. Dragging snaps to canvas edges and visible guides; hold Ctrl to disable snapping. The radius uses **User Settings → Guides & Snapping → Snap Radius (px)**. A normal parameter provides a unit direction and the same button. Drag its endpoint: near the center it faces the camera; at the maximum radius it points along the canvas. Click the endpoint to switch between **+** (toward the camera) and **−** (away).

### Curves and gradients

Effects can offer a curve field for adjusting a numeric profile. Click it to edit keys and tangents
with Unity's curve editor. It starts as a straight line from 0 to 1; values may go below 0 or above 1.
Saving an HLSL preset preserves the edited curve.

Effects can also offer a gradient field. Click its strip to edit colors, transparency and interpolation,
including HDR colors. New gradients start black-to-white unless the HLSL declaration supplies two endpoint colors,
for example `// @param gradient _Ramp = #FF0000FF -> #0000FF`. Hex colors use RGBA order; six digits imply full opacity.
Without an explicit default, interpolation is Perceptual; the two-color HLSL form uses Classic.
Changes update the effect immediately.

In the gradient editor, **Wrap** chooses what happens outside 0–1: **Clamp** holds the endpoint colors, **Repeat** repeats the gradient, and **Mirror** alternates its direction. An effect that clamps its own input may never reach the repeated range.

For HLSL declarations, see [writing parameter controls](shader-controls.md).

## Adjust an effect on the canvas

Effects with a Transform 2D parameter offer **Edit on Canvas**. Select it to show a green frame,
then move, resize or rotate the frame. Rotation is around its center; there is no pivot control.
Use Ctrl/Cmd + corner to distort, Ctrl/Cmd + edge to skew, or Ctrl/Cmd + Alt + Shift + corner
for paired perspective adjustment. Alt with Ctrl/Cmd moves the opposite corner symmetrically.
Position, Size and Rotation preserve the deformation; **Reset Transform** removes it.
**Edit on Canvas** activates one temporary hand tool below the context tools, also used for Point and Normal parameters. Its tooltip identifies the parameter. Click its toolbar button, click **Edit on Canvas** again or press Escape to return to the previous tool. During a drag, the first Escape only cancels that drag. Editing another parameter replaces the temporary tool without changing the return tool. See [context tools](preview.md#context-tools).

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the canvas" width="720"></a>

The frame edits the effect, not the layer transform. Its purpose depends on the effect:
it may place an image, change a pattern's scale, or define a local area. It is not automatically a mask,
but **UV Transform** with **Input Tiling → Clip** leaves pixels outside the frame transparent.

## Order, copying and shared effects

Effect order matters. Drag an FX header to reorder effects, or drop it onto another layer's row to move it there. **Move Up** and **Move Down** in the header context menu also change the order.

**+ Reference** selects a Shader FX asset whose settings are shared everywhere it is used.
**⋮ → Embed Copy** in the effect header creates an independent copy in the document without changing the external asset. The same menu contains **Move Up**, **Move Down** and **Remove**.
Use **⋮ → Copy FX**, then **Paste FX As New** on another row to insert an independent copy after it. Shader FX code and parameters are copied; layer-texture links stay within the same document and are cleared when pasted into another document. Material rows copy the Material reference.
Project HLSL effects receive code changes from their source `.hlsl` file.
To edit the code independently in the document, click **Embed Copy** under **Code**. Parameters appear below the header; **Code** contains the editor, **Apply**, **Save Preset…** and **Shader inputs**. Diagnostics appear when there is something to report.

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

Edited gradients are saved in the document. HLSL defaults support two endpoints, Classic interpolation,
Gamma color space, Clamp wrapping, Smoothness 100% and centered midpoints. Other gradients must be
simplified before this export; unsupported settings are not silently discarded.

## Bake effects into a layer

Baking turns the current result into Drawing pixels. Choose the command by scope:

| Command | Action |
| :--- | :--- |
| **Code → Apply** | Compile HLSL; keep the effect editable. Does not bake pixels. |
| **⋮ → Apply** in an FX header/menu | Bake that FX and every preceding FX, in stack order. Later effects remain editable. |
| **Apply All** beside the add-FX buttons | Bake the entire FX stack. |

Disabled FX in the baked range are removed without contributing to the image.

A non-Drawing layer asks for confirmation before becoming Drawing. Transform stays editable and keeps its values; opacity, blending, channel mapping and clipping remain separate. Baking captures the current canvas at full canvas resolution into floating-point pixels, not an infinite procedural source or off-canvas content. Undo restores the original layer and FX stack. Groups flatten their visible children and warn about lost child targets and possible pass-through changes.

Shader Processor also supports Apply. It captures its current input, including the external backdrop in Pass Through groups, without merging or deleting lower layers. **Normal** becomes **Overwrite** with the Processor's opacity blending retained, including partially transparent pixels; other blend modes stay unchanged. Later edits below no longer recalculate the baked effects. Remaining FX still operate on the snapshot, and Transform stays editable.

## Create your own effect

If you have shader code, use **+ Shader FX**, paste it into the editor and click **Apply**.
The code and settings stay with the document; no separate file is required.
If compilation fails, the FX is skipped until a successful **Apply**; its code and settings remain available for repair.

Custom HLSL effects and brushes can use the built-in noise library for grain,
organic masks and distortion. See the [noise functions and example](../AI/README.md#built-in-noise-library).

Writing an effect is optional. The [shader authoring reference](../ShaderFX.md)
is for creating code and reusable libraries.
## Editing in an external editor

- **Open Code** opens a working file in Unity's selected script editor. Saving updates the draft in WhimTex; click **Apply** to compile it.
- **Open in VS Code** also installs WhimTex directive highlighting, completions and checks automatically in an isolated profile. Saving this working file requests **Apply** in Unity, without returning to the WhimTex window. The language mode remains **HLSL**; the bundled extension supports Restricted Mode.
- If the VS Code button is missing, set **User Settings → External Code Editor → VS Code Command**. WhimTex checks Unity's registered editors and PATH before this fallback.
- An invalid shader is skipped and reports diagnostics until a successful Apply; its code and parameter values remain editable. Save the document in WhimTex to keep changes in TIFF or JSON; saving the code file alone does not save the document. JSON can retain broken FX, while TIFF requires fixing them before saving. After a Unity script reload, reopen the code from WhimTex to reconnect.

The VS Code extension highlights types, modifiers, parameter names, values and ranges as well as directive names. It checks defaults, tooltips, linked controls, conditions and groups; shader compilation errors still come from Unity. After an extension update, reopen code through **Open in VS Code**; an already-running window may need **Developer: Reload Window**.

Type `// @if` or `// @group`, then press **Tab** to create a block with its closing directive. Tab moves through the condition or group title, then into the body. The compact spelling `//@if` / `//@group` also works. If your VS Code settings disable Tab completion, select the snippet with **Ctrl+Space** instead.

External working files are temporary, not backups. Inactive copies unused for more than **24 hours** are automatically removed. Save your document in WhimTex to keep the code; saving in the code editor alone does not save the document. After restarting Unity or reloading scripts, reopen code from WhimTex to reconnect it.

## Compilation warnings

A yellow **!** beside the layer and warning triangles in the **FX** section and individual effect
headers identify FX errors and warnings, even with the panels collapsed. Hover for the reason;
open **Code** for full diagnostics. Errors skip the FX without disabling or deleting it; warnings keep
it working. Fix the code and click **Apply** to resume it. Indicators clear when no issues remain.
A failed Apply does not use an older compiled version.

JSON documents can save and reopen broken FX without losing their source or parameter values.
UI, Console and agent inspection show the same FX messages and severity. Console reports errors as
Error and warnings as Warning; repeats from the same source are suppressed until scripts reload.
UI and agent diagnostics remain visible, including Unity time and parameter warnings.

## Preset guide

### Levels, contrast and color balance

**Color → Levels** offers a **Curve** after input black/white and Gamma, before output black/white. It starts linear. With **Preserve Color**, the curve remaps luminance; otherwise it remaps each RGB channel separately. Alpha is unchanged.

**Color → Brightness Contrast** adjusts midtone brightness and tonal separation. Both controls start at **0** (unchanged). Positive Contrast separates dark and light tones; negative Contrast brings them toward middle gray. The slider tracks cover −100…100, but numeric input and label dragging can go beyond either end. The effect uses smooth tone curves, not a uniform RGB offset: black and white remain fixed, alpha is unchanged, and RGB values outside 0–1 pass through. It operates in the incoming RGB space; it is not exposure control or a pixel-exact match to another application. Extreme settings can collapse details through floating-point precision.

Color Balance groups Shadows, Midtones and Highlights in one block. **RGB Offset** adjusts the three signed RGB components; **Range** controls shadow/highlight influence, and zero hides the corresponding offsets. **Preserve Luma** retains luminance. **Opacity** in the FX header blends the result with the original; alpha is unchanged.

**Profile** in Bevel Emboss maps the selected height channel to relief height. **Mapping** in Gradient Map redistributes brightness before choosing a gradient color. Both curves start linear.

### HSV correction
**Color → HSV** adjusts hue, saturation and brightness. **Hue** shifts the hue in degrees;
**Saturation** and **Value** are multipliers: 1 is neutral, 0 removes saturation or makes the image black.
**Amount** mixes the correction with the original. Gray pixels stay gray when changing hue or saturation.
Alpha is preserved and HDR brightness is supported. HSV correction treats negative RGB channels as zero;
neutral settings and Amount 0 leave the original unchanged.

### Negative
**Color → Negative** blends the source RGB toward its inverse (`1 - RGB`) with **Amount**.
Alpha is preserved by default; enable **Invert Alpha** to apply the same blend to it.

### Mask
**Color → Mask** reads a chosen channel from **Mask** (Self by default). **Transform** positions the mask, **Profile** remaps its values, and **Invert** reverses the result. **Apply To → Channels** selects which channels to multiply; only alpha is enabled initially. In **Color** mode, each color component controls how much of the original channel to preserve: 1 leaves it unchanged, 0 applies the full mask. This is not a tint. **Amount** scales the overall effect.

### Gradient mapping
**Color → Gradient Map** recolors shadows, midtones and highlights using a gradient.
**Source Channel** selects Luminance (default), R, G, B or Alpha as the gradient position.
The selected value chooses the full gradient color, not a single output component. RGB sources
use sRGB values; Alpha is read directly. Reverse and Mapping apply after source selection.
Click **Gradient** to choose colors, use **Amount** to mix with the original image and
**Reverse** to swap the mapping direction. Input brightness outside 0..1 uses the endpoint colors.
Gradient alpha multiplies source alpha: transparent stops hide the corresponding tones,
while an opaque gradient preserves source transparency. FX strength controls both color and alpha;
fully transparent source pixels stay invisible.

### Pixelation and dithering
**Stylization → Pixelate** replaces every block of **Pixel Size** canvas pixels with a single value.
**Average** samples a 4×4 grid inside the block instead of its center, so thin details survive.
**Levels** sets how many values each channel keeps and **Gamma** moves the tonal steps between
shadows and highlights. **Dither** picks the pattern that spreads the error between levels:
**Bayer2**, **Bayer4** and **Bayer8** give the classic ordered look, **Interleaved** is irregular
noise, **Checker** is a two-tone grid, **Halftone** builds a clustered-dot screen and **Hash** is
stable noise without a visible grid. The pattern is evaluated per block, so it stays visible after
pixelation. **Dither Strength** weakens it down to plain rounding. **Color → Quantization** uses Levels and Gamma; **Color → One Bit** uses **Low Color** and **High Color** by luminance instead. **Offset** shifts the grid without moving the layer. Alpha is preserved unless **Alpha Clip** is enabled; **Alpha Cutoff** sets the transparent/opaque boundary.
The same pattern list works per pixel in **Stylization → Posterize**.

### Edge contours

Choose **FX → + Preset → Stylization → Edge Outline** to draw lines along sharp transitions.
**Method → Boundary** creates uniform contours; **Scharr** makes weaker transitions less opaque.
In Scharr, raise **Strength** to make these lines stronger. This control is hidden in Boundary.
Both methods share the controls below; switching methods keeps their values.
**Detection → Color** finds RGB changes, including between colors of equal brightness.
**Luminance** finds only brightness changes, ignoring color changes of equal brightness.
Raise **Threshold** to ignore weaker transitions, gentle gradients or small texture variations.
**Hue** outlines changes in color tone rather than brightness or saturation. **Hue Threshold (°)**
sets the minimum difference in degrees (0–180). Raise **Min Saturation** to ignore nearly gray
colors; a boundary is ignored if either side falls below it. Gray and black are always excluded.

**Thickness (px)** sets the total line width, centered on the boundary; **Softness (px)** softens its
edges. Both use canvas pixels. Scharr also responds to the width of the image's transitions:
broad transitions can form bands rather than a precisely uniform line. **Contour** chooses rounded (**Round**), square (**Square**) or
diamond-shaped (**Diamond**) joins. **Color** in Output sets the tint; its alpha controls opacity.
**Output → Overlay** keeps the image; **Outline Only** leaves just the lines on transparency.
The Opacity field in the FX header blends the full result with the original image: 0 keeps
the original, 1 applies the full effect. This also applies to Outline Only.
Zero Thickness removes the contour. Source transparency is preserved: this FX does not
outline an alpha-only silhouette or fill transparent areas. Wider lines take more time to render,
especially with Scharr.

### Other stylization effects
- **Step** thresholds the enabled color channels separately. Red, Green and Blue start enabled; Alpha starts disabled. Choose **Hard** for a two-value result or **Smoothstep** to soften the transition with **Hardness**. **Apply To → Color** uses RGBA as per-channel effect strengths: 0 preserves the original channel, 1 applies the full step. It does not replace the image with that color. **Threshold** instead tests luminance (or alpha) and maps the result between two colors, optionally with a soft boundary; it preserves source alpha.
- **Halftone** turns the image into monochrome, CMYK or RGB dot screens. Set dot size and shape; CMYK/RGB modes also expose screen angles and manual or automatic plate registration.
- **Chromatic Aberration** shifts red and blue in opposite directions, radially from a point or along an angle. **Amount** is in canvas pixels; green and alpha stay unchanged.
- **CRT** combines curved edges, scanlines, RGB phosphor stripes, vignette, color fringing, grain and flicker. **VHS** adds line wobble, chroma bleed, noise and a moving tracking band. **Seed** changes the deterministic pattern; **Effect Time** selects another frame.
- **Digital Glitch** combines line tears, independently segmented corruption blocks, channel shifts, color/noise/alpha artifacts and optional gradient tinting. **Seed**, **Effect Time** and **Frame Rate** control its repeatable animated variation; **Block Order** chooses independent row-first or column-first block layouts.

### Lighting and embossing
- **Normal Map → Lighting** turns an RGB normal map into a shaded surface. **Normals** defaults to Self. Set **Light Direction** with its canvas handle, then adjust **Base Color**, light/shadow colors, **Intensity** and **Ambient**. Keep **Packed Color** enabled for the Normal Map layer's default output; disable it for Linear Data. **Flip Y** reverses the green-axis convention. Platform-packed normal textures are not supported.
- **Lighting → Bevel Emboss** derives normals from **Height Map** (Self by default, or another layer). **Height Channel** selects luminance, R, G, B or Alpha; **Profile** maps height, **Depth** raises or engraves, and **Smoothing** sets the sampling radius in document pixels. For SDF, the visible gradient and distance ranges define bevel width.

Both effects use **Base Color alpha** to blend transparent lighting (0) into the filled, shaded surface (1). Intermediate values crossfade with alpha-aware mixing. RGB tints the surface; **Ambient** fades in with it. **Output** selects Both, Highlight Only or Shadow Only for the transparent component and has no effect at Base Color alpha = 1. Light Color/Shadow Color alpha controls that component's strength. Overall opacity and blending belong to the layer. Both effects default to Base Color alpha 0. Re-add the preset to update an older embedded copy.

### Normal map normalization
**Normal Map → Normalize** restores unit-length normals while preserving alpha.
Use it after processing an RGB normal map if its vectors have changed length.
Neutral `(0.5, 0.5, 1)` stays unchanged; an undefined direction becomes neutral.
Keep **Packed Color** enabled for the Normal Map layer's default encoding; disable it for **Linear Data**.
This toggle accounts for color encoding, but does not unpack platform-specific normal-map formats.

## Distortion presets

Choose **FX → + Preset → Distortion → Spherize**, **Twirl**, **Radial Shear** or **Displacement Map**.

The numeric field in each FX header controls distortion: **Strength** for Spherize and
Radial Shear, **Angle** for Twirl, and **Amount** for Displacement Map and Polar Coordinates.
It changes the mapping, not the opacity of a finished result.

- **Spherize / Mode:** `Classic` keeps the existing unbounded radial distortion; `Sphere` projects the image onto a sphere and clips it to a circular silhouette. The edge is antialiased by about one pixel.
- **Spherize / Strength:** positive values bulge the center; negative values pinch it. In `Classic`, zero leaves the image unchanged. In `Sphere`, zero keeps the circular shape but removes the texture distortion.
- **Twirl / Angle:** twists around the center; the sign reverses direction. The angle is measured in degrees at the frame's local radius 1 and grows with distance.
- **Radial Shear:** twists sampling coordinates progressively farther from **Center**; **Strength** controls the direction and amount, while **Offset** adds a base shift.
- **Area / Edit on Canvas:** move, resize or rotate the green coordinate frame. Stretch it to make the distortion elliptical.
- **Displacement Map / Mode:** `VectorRG` reads R/G as a signed direction field; the `Neutral` value (0.5 by default) means no offset. Use a linear/data texture for vector and height maps. X/Y strengths are in canvas pixels. `Grayscale` reads a selected channel and moves pixels horizontally, vertically, radially, tangentially, or along an angle. `ParallaxOcclusion` treats the selected map channel as height and shifts the sample along a virtual view ray.
- **Displacement Map / Amount:** multiplies X/Y strengths, Grayscale strength or Parallax depth. Zero keeps the original image; 1 uses the configured values, and values above 1 amplify them. **Output Mix** remains a separate image blend.
- **Parallax / Depth and View:** **Depth** sets the height range in canvas pixels; **View Angle** sets the ray direction, and a lower **View Elevation** increases the shift. **Invert Height** swaps raised and recessed areas. **Parallax Steps** selects the quality/cost tradeoff (4–32 height samples); the default is 8.
- **Map / Transform and Wrap:** position the map independently. `Clamp`, `Repeat`, and `Mirror` control map coordinates; they do not affect the displaced image's edges.
- **Strength Mask:** `Constant1` is the default, so one map is enough and no strength mask is required. `MapChannel` reuses a channel of that same map, `InputAlpha` follows the input image's alpha, and `SeparateTexture` adds an optional second mask texture. Invert the mask or remap it with **Mask Profile**.
- **Output / Mix:** blend the displaced sample with the original.

**Tiling**, below each distortion's parameters, controls samples beyond the input image:
`Clamp` extends edge pixels, `Repeat` tiles the image, `Mirror` reflects alternate tiles,
and `Clip` returns transparency. Distortions default to Clamp; UV Transform defaults to Clip.
This is separate from the layer's Tiling and Displacement Map's Map Wrap. Sphere keeps its
circular silhouette regardless of Tiling. These modes sample the existing image;
they do not extend a procedural source beyond the raster.

`Classic` Spherize, Twirl and Polar Coordinates do not mask at the frame edge, so distortion continues outside it.
`Sphere` is the exception: it creates a circular mask with a crisp, antialiased edge. Strong settings
in unmasked modes can reveal areas beyond the input image, where its edge pixels are extended. RGB and alpha are distorted together.

### Polar coordinates

**Distortion → Polar Coordinates** is one effect with a **Mode** selector (To Polar by default):

- **Amount** in the FX header gradually changes sampling coordinates: 0 keeps the original image, 1 applies the full mapping below.
- **To Polar** wraps a strip into a circle: horizontal runs around the center, vertical runs outward.
- **From Polar** unwraps a circle into a strip: left to right covers one full turn, bottom to top covers distance from the center.

**Input** selects the source frame: a strip for To Polar, a circle for From Polar.
**Output** positions and shapes the output: a circle for To Polar, a strip for From Polar.
Both frames have position, size and rotation, plus **Edit on Canvas** and reset buttons.
Their default position `(0.5, 0.5)`, size `(1, 1)` and rotation `0` cover the whole image.
Move, scale or rotate Input to choose the source pattern independently of its placement in Output.
**Angle Offset** shifts the start of the turn in degrees; zero starts to the right of the center and runs counterclockwise.
**Radial Offset** shifts the starting radius: positive values move the pattern outward in To Polar,
or start sampling farther from the center in From Polar. One unit equals half the circle frame's width or height along its axes: Output for To Polar, Input for From Polar.

Radius continues beyond the frame without a fade. Match the left and right edges of the source strip
to avoid a visible seam around the circle. The entire strip width converges at the center,
so unwrapping cannot recover details lost at that point.
