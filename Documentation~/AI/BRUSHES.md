---
title: "AI brush authoring: JSON and HLSL"
nav_order: 6
lang: en
permalink: /ai-brushes/
description: "Generate Standard brushes with optional image tips and Static or Dynamic HLSL tips for WhimTex, then paste JSON into the editor."
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
- [Dynamic HLSL brush](../Examples/Brushes/hlsl-dynamic.json)

Validate the structure against [brush.schema.json](brush.schema.json).
These are **not** `whimtex.document` layer recipes or Unity serialized settings.

## Common mistakes

- Use exact source names: **Standard**, **HLSL**. Texture is not a separate source.
- Standard optionally accepts a plain HTTP(S) URL returning PNG/JPEG, not a Markdown link or a webpage.
  No local asset paths, base64 or image-generation API requests in JSON.
- HLSL uses `BrushTip`, not `ApplyFX`, `SampleInput` or ShaderLab. Its signature selects Static or Dynamic evaluation.
- Begin code with `// @whimtex-brush Category/Name` on **line 1**.
- Parameter declarations include a type: `// @param float _Width = 0.1 [0 .. 1]`.
  No semicolon; one-sided bounds use `[0 ..]` or `[.. 1]`.
- JSON units are fractions: opacity 0.5 = 50%, spacing 0.16 = 16% of the brush diameter.
- Static HLSL does not run while painting. Dynamic HLSL receives the documented stroke context;
  neither mode provides background sampling, tablet pressure or live time.
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
| HLSL | Required `code`; optional integer `resolution` 32..2048, default 512. Resolution is used only by Static tips; Dynamic tips evaluate directly while painting. No url or evaluation metadata. |

`settings` fields:

| Field | Values / default |
| :--- | :--- |
| size | 1..4096 canvas pixels; 32 |
| hardness | 0..1; 0.8; Standard without a texture only |
| spacing | 0.01..4 brush diameters; 0.16 |
| opacity, flow | 0..1; 1 |
| pressure | boolean; true. Multiplies brush opacity by tablet pressure when painting interactively. |
| writeChannels | integer 0..15; 15. Add enabled bits R=1, G=2, B=4, A=8. Disabled channels retain previous values; 0 writes nothing. |
| lockAlpha | boolean; false. Preserve alpha and fully transparent pixels; overrides the A write bit. |
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
| tipGradient, tintGradient | [Gradient value format](README.md#standalone-gradient-json), with brush-specific limits below; also fully described by brush.schema.json |
| blend | Color blend names from the schema, default Normal; None and Overwrite are not allowed |
| blendApplication | Stroke / Stamp; Stroke |

Both gradient fields accept a stop array or an object with `colors`, optional `alphas`, `mode`,
`wrapMode`, `smoothness` and `colorSpace`. If `mode` is omitted, both `tipGradient` and
`tintGradient` use **Perceptual**, also the default for new brushes; explicit modes are retained. Color-picker RGB/HSV, HDR input,
Channels and Preview EV preferences do not transform pasted values, and pasting does not populate
document History. RGB limits remain -107..107 and alpha 0..1, not the wider standalone gradient range.

Standard texture tips and HLSL tips share all channel, SDF/Gradient, rotation, flip and painting behavior.
HLSL changes how the tip is obtained. Changing its parameters does not reset
these settings. Use opaque grayscale RGB plus Luminance for a distance field; with SDF enabled,
the selected value is inverted before looking up tipGradient, as for a texture tip.

## Full HLSL specification

Optional `// @header(Shape)` adds a bold, non-collapsible heading, while `// @helpbox(Your hint text.)` adds an informational help box above the next parameter. These are UI metadata, not uniforms, and are preserved when saving a preset. Headings and help boxes without a following parameter declaration are ignored. Former-name aliases are not supported; declaration edits preserve matching values/identity using the same rules as FX.

Define exactly one entry point:

```hlsl
// Static: baked into a square texture on Apply.
float4 BrushTip(float2 uv)

// Dynamic: evaluated for covered pixels of each stamp while painting.
float4 BrushTip(float2 uv, DynamicBrushContext brush)
```

The actual function definition selects the mode; no metadata flag is needed.
Comments, helper arguments and forward declarations do not select it. Defining both
entry points is an error. UV is 0..1 across the square tip, with Y increasing upward.
Return straight (not premultiplied) RGBA. Alpha is clamped to 0..1.
RGB can carry HDR values and is treated as linear data. Color mode multiplies this RGB
by the painting color and randomized Tint; channel and SDF settings can reinterpret it.
Use a white palette to see unmodified RGB.

### Dynamic context and randomization

| Field | Type | Meaning |
| :--- | :--- | :--- |
| seed | uint | Stable root seed of this brush sequence, from settings.seed. Not replaced on each click. |
| strokeIndex | uint | Zero-based stroke index; advances on the next painted stroke. |
| stampIndex | uint | Zero-based logical stamp index; resets each stroke. Symmetry/repetition copies share it. Clipped and budget-skipped logical stamps still advance it. |
| distance | float | Path length in canvas pixels within this stroke, before Scatter or canvas wrapping. Interactive input smoothing has already been applied. |
| totalDistance | float | Path length accumulated across this brush's strokes, including strokes on different Drawing layers. Pointer travel between strokes is excluded. |
| deltaPixels | float2 | Displacement from the previous logical stamp to this one in canvas pixels: X right, Y up. The first logical stamp of each stroke gets `(0,0)`. |

`deltaPixels` is measured after input smoothing, before Scatter and canvas wrapping.
It carries across input events, including events too short to emit a stamp. Clipped
and budget-skipped stamps still define the previous logical position. Symmetry and
repetition copies share the original stroke's delta; tip rotation and flips do not
transform it. At a corner it is the straight displacement between stamps, not the
travelled path length. It is not velocity: no time interval is supplied.

`BrushRandom(uint seed, uint index)` and `BrushRandom(uint seed, uint strokeIndex, uint stampIndex)`
return deterministic values in `[0,1)`. They do not advance a mutable random generator.

```hlsl
float perStroke = BrushRandom(brush.seed, brush.strokeIndex);
float perStamp = BrushRandom(brush.seed, brush.strokeIndex, brush.stampIndex);
float alongPath = brush.totalDistance;
float stepLength = length(brush.deltaPixels);
float2 direction = stepLength > 1e-6 ? brush.deltaPixels / stepLength : float2(0, 0);
```

Seed changes, a different preset/source code, or **Reset Sequence** restart indices and
totalDistance. Editing only declared parameter values does not. Previews use a separate
sequence. Progression is session state, not saved document/preset data; reopening starts
a new sequence. This is tip evaluation, not simulation: context is constant within a
stamp, and no previous stamp image or canvas pixels are supplied. Dynamic tips require
shader target 3.5; avoid expensive per-pixel loops or noise stacks.

### Declared parameters

Brush tips use the same [parameter syntax and fields as FX](../ShaderFX.md#parameter-declarations):
**float**, **bool**, **enum**, **float2**, **float3**, **float4**, **point**, **normal**,
**color**, **texture2D**, **transform2D**, **gradient** and **curve**. Defaults in the
declarations are used on JSON paste; there is no separate parameter-value object.
Maximum 32 distinct parameters, 128 controls and 64 KiB UTF-8 of code.

`label(...)`, tooltips, `hidden`, linked controls, `@group`, `@header`, `@helpbox` and
`@if`/`@endif` work as in FX. Optional `// @control(_Parameter)` belongs on line 2,
immediately after the brush header, and shows the referenced parameter as a compact field,
with the same supported types and warning rules as FX.

```hlsl
// @whimtex-brush Pattern/Striped Tip
// @control(_Frequency)
// @group(Pattern; _Enabled)
// @param bool _Enabled = true
// @param float _Frequency = 4 [1 .. ~16]
// @if _Enabled == 1
// @param gradient _Ramp = #FFFFFFFF -> #000000FF
// @endif
// @endgroup
float4 BrushTip(float2 uv)
{
    return _Enabled > 0.5 ? _Ramp_Sample(frac(uv.x * _Frequency)) : 1;
}
```

`point` uses tip UV rather than canvas UV. `transform2D` helpers operate on the square
tip, with an aspect ratio of 1. `normal` is normalized as in FX. These fields have no
Edit on Canvas buttons: brush parameters do not manipulate document handles.

`texture2D` accepts **Texture** (an optional Unity Texture2D asset; white when unassigned)
or **None** (transparent black). FX's Layer/Self sources and `SampleInput` are unavailable.
Gradient/curve sampling uses the same cached LUTs and helpers as FX: `_Ramp_Sample(t)`
returns straight linear RGBA; `_Profile_Sample(t)` returns a float. `color` defaults
are encoded RGB and are converted to linear once on upload, as in FX; raw float vectors
are not converted.

Float controls support independent soft boundaries: `// @param float _Width = 0.1 [0 .. ~1]`.
The slider stays within 0..1, while numeric input and label dragging may exceed 1 but cannot go below 0.
`[~0 .. 1]` makes only the lower boundary soft; `[~0 .. ~1]` makes both soft.
A range with a soft boundary requires two finite boundary values, with `min < max`. The initializer may be omitted.
Ordinary `[0 .. 1]` remains a hard range; the old `~[0 .. 1]` syntax is rejected.
HLSL preset export preserves both boundary flags and the current value. Limits explicitly written
in shader code still apply.

Helper functions and ordinary HLSL math, including `fwidth`, are allowed.
[FastNoiseLite noise functions](README.md#built-in-noise-library) are built in:
use `fnlCreateState`, `fnlGetNoise2D/3D` and Domain Warp directly without an include.
Otherwise code must be self-contained: no preprocessor directives or includes.
Names beginning `_WhimTex_` are reserved. There are no input-image sampling helpers.
Unity shader syntax is not a security sandbox: only use trusted code. Excessively expensive
or non-terminating shader code can stall the GPU.

The editor compiles the active tip on Apply/first preparation when source code changes,
not once per stamp. Static parameter/resolution edits redraw the cached texture without
recompiling unchanged source. Dynamic parameter edits update uniforms and resource bindings without baking;
gradient/curve LUTs rebuild only when their values change, not per stamp.
For Dynamic tips, Tip Resolution is hidden because it is unused. Size, Flow, Opacity and Scatter retain
their usual painting behavior. Dynamic tips use the same batched stamp mesh.

Save HLSL Preset writes the current parameter values as declaration defaults and retains
their groups, conditions and linked controls. It shares FX's export restrictions:
gradient defaults support only two-endpoint Classic/Gamma/Clamp with Smoothness 1 and
midpoint .5; any other gradient, including an implicit Perceptual default, causes an explicit refusal. Texture asset references
must resolve to a GUID in the destination project. A JSON brush contains code defaults,
not embedded parameter textures or separate edited values.
User files live in the configured presets folder under **Brushes/HLSL**. Project .hlsl
files are discovered through Unity's asset database. Both sources appear in one catalog,
grouped as User / Project. Selecting a catalog item copies its source into the brush;
it is not a live link to the file. Save As embeds code and values; only Static tips include
a baked image. Dynamic tips do not save a frozen preview as their source.
Brush settings and `.sebrush` presets retain parameter texture GUID/local IDs, not copies
of those texture images. Moving a preset to another project requires the referenced assets
with the same identities. Missing saved parameter textures produce an error instead of
silently replacing the image with white.
