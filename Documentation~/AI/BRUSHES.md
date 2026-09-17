---
title: "AI brush authoring: JSON and HLSL"
nav_order: 6
lang: en
permalink: /ai-brushes/
description: "Generate Standard brushes with optional image tips and cached HLSL brushes for WhimTex with a browser AI and paste JSON into the editor."
---

# WhimTex brush authoring

Use this contract when the user requests a **brush**, not a texture composition.
Return one complete JSON block with `format: "whimtex.brush"`. The user copies it and
pastes with Ctrl+V in an active WhimTex window, outside text fields. This replaces the
current brush; it does not create layers, resize the canvas or change palette colors.
Painting still requires a Drawing layer. Save As in Brushes saves the result as a brush preset.

## Start with an example

Ready-to-copy files are in **Documentation~/Examples/Brushes/**:

- [Standard scattered brush](../Examples/Brushes/standard-scatter.json)
- [Texture brush from a direct image URL](../Examples/Brushes/texture-url.json)
- [HLSL ring brush](../Examples/Brushes/hlsl-ring.json)
- [HLSL sparkle brush](../Examples/Brushes/hlsl-sparkle.json)

Validate the structure against [brush.schema.json](brush.schema.json).
These are **not** `whimtex.layers` examples or Unity serialized settings.

## Common mistakes

- Use exact source names: **Standard**, **HLSL**. Texture is not a separate source.
- Standard optionally accepts a plain HTTP(S) URL returning PNG/JPEG, not a Markdown link or a webpage.
  No local asset paths, base64 or image-generation API requests in JSON.
- HLSL uses `BrushTip(float2 uv)`, not `ApplyFX`, `SampleInput` or ShaderLab.
- Begin code with `// @whimtex-brush Category/Name` on **line 1**.
- Parameter declarations include a type: `// @param float _Width = 0.1 [0 .. 1]`.
  No semicolon; one-sided bounds use `[0 ..]` or `[.. 1]`.
- JSON units are fractions: opacity 0.5 = 50%, spacing 0.16 = 16% of the brush diameter.
- HLSL does not run per stamp. Do not expect time, stroke position, seed per stamp,
  background sampling, pressure or live animation inside the script.
- Use `tipChannel: "Color"` to retain RGB from a colored tip.
  Alpha is the default, so RGB alone does not color the brush.
- Only copy one JSON object (optionally inside one json code fence), without surrounding prose.

## Full JSON specification

Required root fields: `format: "whimtex.brush"`, `version: 1`, `source`.
Optional `name` is descriptive metadata, not a saved preset name. Optional `settings`
starts from fresh brush defaults; omitted properties do not inherit the previous brush.
Unknown fields and duplicate JSON keys are rejected. Maximum JSON size: **1 MiB UTF-8**.

Source-specific root fields:

| Source | Fields |
| :--- | :--- |
| Standard | Without `url`, a round procedural tip. Optional `url`: direct HTTP(S) PNG/JPEG for a texture tip. No code or resolution. Download requires confirmation; max 64 MB and 16 megapixels. Native image resolution and aspect ratio are retained. |
| HLSL | Required `code`; optional integer `resolution` 32..2048, default 512. Generates a square texture. No url. |

`settings` fields:

| Field | Values / default |
| :--- | :--- |
| size | 1..4096 canvas pixels; 32 |
| hardness | 0..1; 0.8; Standard without a texture only |
| spacing | 0.01..4 brush diameters; 0.16 |
| opacity, flow | 0..1; 1 |
| scatter | 0..4; 0 |
| scatterBias | −1..1; 0 |
| sizeJitter | 0..1; 0 |
| angleJitter | 0..180 degrees; 0 |
| angleOffset | −180..180 degrees; 0 |
| flipX, flipY | 0..1 probability; 0 |
| rotationMode | Fixed / StrokeDirection; Fixed |
| randomAlgorithm | Random / Sobol; Random |
| seed | integer 1..2147483647; 1 |
| tipChannel | Alpha / Luminance / InvertedLuminance / Color; Alpha |
| tipSdf | boolean; false |
| mode | Hardness / Gradient; Hardness; Standard without a texture only |
| tipGradient, tintGradient | [Shared gradient format](README.md#shape-color-and-gradient), also fully described by brush.schema.json |
| blend | Color blend names from the schema, default Normal; None and Overwrite are not allowed |
| blendApplication | Stroke / Stamp; Stroke |

Standard texture tips and HLSL tips share all channel, SDF/Gradient, rotation, flip and painting behavior.
HLSL only changes how the tip image is obtained. Changing its parameters does not reset
these settings. Use opaque grayscale RGB plus Luminance for a distance field; with SDF enabled,
the selected value is inverted before looking up tipGradient, as for a texture tip.

## Full HLSL specification

Optional `// @header(Shape)` before a `// @param` declaration adds a bold, non-collapsible heading above that control. Titles are literal non-empty text without quotes. Headers do not create uniforms and are preserved when saving a preset; a header without a following parameter is ignored.

Implement `float4 BrushTip(float2 uv)`: UV is 0..1 across the cached square tip, with
Y increasing upward. Return straight (not premultiplied) RGBA. Alpha is clamped to 0..1.
RGB can carry HDR values and is stored as linear data. Color mode multiplies this RGB
by the painting color and randomized Tint; channel and SDF settings can reinterpret it.
Use a white palette to see unmodified RGB.

Supported parameters: **float**, **float4**, **color**. Defaults in the declarations are the
values used on JSON paste. No separate parameter-value object is necessary.
Maximum 32 parameters; maximum code size 64 KiB UTF-8.
Float4/color defaults use four comma-separated components in parentheses.

Float controls support independent soft boundaries: `// @param float _Width = 0.1 [0 .. ~1]`.
The slider stays within 0..1, while numeric input and label dragging may exceed 1 but cannot go below 0.
`[~0 .. 1]` makes only the lower boundary soft; `[~0 .. ~1]` makes both soft.
Both finite boundary values are required, with `min < max`. The initializer may be omitted.
Ordinary `[0 .. 1]` remains a hard range; the old `~[0 .. 1]` syntax is rejected.
HLSL preset export preserves both boundary flags and the current value. Limits explicitly written
in shader code still apply.

Helper functions and ordinary HLSL math, including `fwidth`, are allowed.
[FastNoiseLite noise functions](README.md#built-in-noise-library) are built in:
use `fnlCreateState`, `fnlGetNoise2D/3D` and Domain Warp directly without an include.
Otherwise code must be self-contained: no preprocessor directives, includes, texture2D or transform2D parameters.
Names beginning `_WhimTex_` are reserved. There are no input-image sampling helpers.
Unity shader syntax is not a security sandbox: only use trusted code. Excessively expensive
or non-terminating shader code can stall the GPU.

The editor compiles when source code changes. Parameter or resolution changes redraw the
cached tip without recompiling unchanged source. Drawing uses the resulting texture;
Size, Flow, Opacity, Scatter and other stroke settings do not rerun HLSL.

Save HLSL Preset writes the current parameter values as declaration defaults.
User files live in the configured presets folder under **Brushes/HLSL**. Project .hlsl
files are discovered through Unity's asset database. Both sources appear in one catalog,
grouped as User / Project. Selecting a catalog item copies its source into the brush;
it is not a live link to the file. Save As brush presets embed code, values and the baked tip.
