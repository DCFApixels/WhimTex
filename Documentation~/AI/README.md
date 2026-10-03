---
layout: default
title: AI authoring contract
lang: en
permalink: /ai-authoring/
description: "Generate editable WhimTex documents and layers using unified JSON and HLSL effects."
---

# WhimTex AI authoring: JSON documents and HLSL effects

Use **`whimtex.document`, version 1** for new documents, selected layers and clipboard recipes.
Files use **`.json`**. The operation (open, paste or API insert/replace), not a `kind` field,
decides what to do with the content. Brush and standalone gradient presets remain separate formats.

This guide targets the checked-out version, not older package releases. A browser AI can return
JSON for the user to paste without a Unity connection. Do not claim to have rendered, compiled or
inserted it unless you actually did so.

## Where to start

1. Read the [shared JSON contract](../JSON_FORMAT.md) and [generated model schema](document.schema.json).
2. Open an actual recipe from the [clipboard examples](../Examples/Clipboard/README.md) before adapting it.
3. For more involved textures, consult the [38 editable samples and individual previews](https://github.com/DCFApixels/WhimTex/tree/main/Samples~/AgentTextures).
4. For shader code, use the [HLSL interface below](#hlsl-interface--shader-only-or-inside-json)
   and [FX reference](../ShaderFX.md).

For brushes use the separate [brush contract](BRUSHES.md) and [brush examples](../Examples/Brushes/README.md).
Brush JSON replaces the brush rather than creating layers.

## Instructions for an AI assistant

- Return one complete JSON object in a `json` code block, without comments or trailing commas.
- Use editable layers for distinct parts. Prefer Shape, Gradient, Noise and targeted effects;
  use HLSL only where a custom algorithm is useful. Name layers and groups meaningfully.
- Use exact model fields and case-sensitive enum names from the schema. Do not put live API
  `settings`/operations or legacy `type`/`properties` objects into document JSON.
- Layer arrays are **top to bottom**; `modifiers` execute **first to last**.
  Give every layer a unique nonempty `id`. IDs may be descriptive local strings;
  paste remaps them and internal references to fresh document IDs.
- Omit values only when the version-1 default is intended. Do not infer storage defaults from
  a current UI factory or a slider range. Use FullOptimized by default; Full or Compact on request.
- Do not invent asset GUIDs or paths. For an existing project asset, use its verified `$asset`
  identity. Drawing pixels, Base64 images and remote image URLs are not stored in this format.
- Keep shaders self-contained and deterministic; avoid unnecessary layers, huge blur radii and
  expensive loops. Preserve alpha unless the requested effect intentionally changes coverage.
- If an error is reported, return a corrected complete object using its field path/message.
  Re-pasting inserts another copy; it does not update the previous paste.

## JSON envelope

```json
{
  "format": "whimtex.document",
  "version": 1,
  "document": {
    "width": 256,
    "height": 256,
    "outputFilter": "Bilinear",
    "outputSrgb": true
  },
  "layers": [
    {
      "id": "clouds",
      "layerName": "Clouds",
      "behaviour": {
        "$type": "NoiseLayerBehaviour",
        "noiseType": "Perlin",
        "scale": 8,
        "scaleY": 8,
        "fractal": "FBm",
        "octaves": 3,
        "encoding": "LinearData"
      }
    }
  ]
}
```

Only `format`, `version` and `layers` are required. `document` and all its settings are optional.
Opening uses version defaults for omitted settings (512 × 512 for the canvas).
Pasting inherits each omitted canvas axis from the destination. With neither axis supplied there
is no size prompt. Standard writers always include both dimensions, including Compact output.
Include them in generated recipes for predictable source-canvas context.

`writeMode` is optional and defaults to `FullOptimized`:

| Mode | Result |
| --- | --- |
| `Full` | All persistent settings, including defaults and inactive values. |
| `FullOptimized` | Active settings including their defaults; inactive branches omitted. |
| `Compact` | Same inactive-branch rules, also omitting frozen default values except canvas dimensions. |

Disabled layers and FX remain in all modes. Collapsed UI and hidden shader controls do not mean
their values are inactive. Omitted inactive settings restore version defaults when re-enabled.

## Layer fields

Every layer requires `id` and `behaviour`. Settings belong to their native model owner:

| Location | Examples |
| --- | --- |
| Layer | `layerName`, `enabled`, `opacity`, `blendMode`, `colorRange`, `blendRange`, `filterMode`, `clippingMask` |
| `behaviour` | `$type` and type-specific persistent fields |
| `transform` | Native `TextureTransform` fields; inspect the schema and an exported example |
| `modifiers` | FX with `$type: "ShaderFX"`, `code`, `parameters`, `active`, optional `$name` |
| `children` | Group children in top-to-bottom order |

`$type` is an allowlisted model name, not an arbitrary assembly-qualified type.
For instance, Color Fill uses `ColorFillLayerBehaviour.storedColor`, not `properties.color`.
Groups use `GroupLayerBehaviour`, `group: true`, `children` and layer-level `compositing`.
They can carry their own FX; FX process the combined children before group opacity/blending.

For targeted effects, use `behaviour.inputMode` and `behaviour.targetLayerId` with IDs from
this tree. `Previous`, `Specific` and `AllBelow` have their usual stack semantics;
include required sources and clipping bases when copying a selection. Do not use `@aliases`.
Shared FX `$id`/`$ref` identities are separate from layer IDs.

Native vector/color values follow the schema: Unity vectors and RGBA colors are numeric arrays,
while transform `Double2` values are objects with `x` and `y`.
Do not transfer the legacy array-based transform syntax or live API convenience setters unchanged.
Numbers and booleans are not quoted strings. Enum names are case-sensitive.

### Noise, gradients and SDF

Noise settings are directly in `behaviour`, not `properties.noise`.
`scale` and `scaleY` store the X/Y scale; `scaleY: 0` inherits X.
`warpScale` and `warpScaleY` are per-axis multipliers of the main scale.
`offset` is `[x,y,z]`; Z is used by `ThreeD` slices.
Seamless uses `periodic: "None" | "X" | "Y" | "XY"` for TwoD/ThreeD, and `periodic1D`
for OneD. White/Blue Noise ignore Seamless and do not have a true ThreeD variant.

Noise Output uses `encoding` (`LinearData`, `ColorValues`, `Gradient`), with
`gradient` storing the palette and `inverted` reversing the value before palette sampling.
Color White/Blue Noise does not use Gradient output.
SDF uses `SDFLayerBehaviour.encoding` with `LinearData` or `Gradient`, plus its own
`gradient` and `inverted`. It has no ColorValues mode.
For document gradients use the schema's native `WhimTexGradient` model;
the standalone clipboard format below is a separate value contract.
For algorithm behavior see [Noise](../AgentAPI.md#noise-settings) and
[Make Seamless](../AgentAPI.md#make-seamless-settings), but use document-schema field shapes,
not the live API patch wrappers from those pages.

## Paste, copy and export

Copy the JSON content, focus the preview or Layers panel and press **Ctrl+V** (Cmd+V on macOS)
outside text editing. One surrounding JSON code fence is accepted.
The tree is inserted above existing layers with remapped IDs; one Undo removes the insertion and
any accepted canvas resize. Existing layers are not replaced. A Processor can affect the lower stack.
Source output filtering/encoding does not replace destination settings during unified JSON paste.

Unknown fields, wrong types and broken internal references reject the paste without partial insertion.
Clipboard FX requires trust confirmation before compilation. A compilation failure does not reject
the tree: paste offers a warning, keeps the source, parameter values and enabled state, and skips
the unavailable effect during rendering, just like opening a JSON document. Canceling that warning
inserts nothing. Repair the code and Apply to resume the effect. Broken internal references still fail.

**Copy as JSON** exports selected layers using the same format and the FullOptimized default.
Include their dependencies in the selection. Ordinary Ctrl+C remains unchanged.
**Export** opens the shared export window; choose WhimTex JSON and a write mode.
**Save As** creates TIFF only; Ctrl+S on an already open JSON document still saves JSON.

Nonempty Drawing content cannot be represented in JSON. A confirmed omission keeps a layer
placeholder, settings and FX but loses its painted pixels in the exported copy.
Copy as JSON reports omitted content; JSON file export requests confirmation. The source remains intact.
Use TIFF when preserving Drawing pixels is required.
External assets retain GUID, path, local ID and type; resolution tries GUID, then path if the GUID
is absent. Missing assets warn rather than preventing opening, and their identity survives saving.

## Complete examples and validation

The [example index](../Examples/Clipboard/README.md) contains nine current procedural recipes:
neon ring, shock wave, car wheel, forked lightning, heart, mystic fog, seamless noise,
retro processor and local distortion. Schema checks cover their structures; Unity tests read,
compile and compare their rendered results with the original recipes.

A schema check alone does not test dependencies, shader compilation or visual quality.
Use the connected API's document JSON validation when available, then inspect an actual render.
Command envelopes are separate from content; see the [JSON agent API](../AgentAPI.md#unified-json-documents).

## A prompt users can copy

> Read https://dcfapixels.github.io/WhimTex/ai-authoring/ and its linked document schema and examples.
> Create a 512 × 512 magical ring using editable procedural layers, grouped and named in English.
> Return one complete whimtex.document JSON code block. Do not invent external asset identities.
> Keep HLSL self-contained and do not claim it was tested unless it was.

## Common mistakes

| Mistake | Use instead |
| --- | --- |
| `"format": "whimtex.layers"` for new output | `"format": "whimtex.document"` |
| Root `kind: "document"` or `kind: "layers"` | Omit it; the caller chooses the operation. |
| `canvas`, `type`, `name`, `properties`, `fx` from legacy recipes | `document`, `behaviour.$type`, `layerName`, native fields and `modifiers` |
| `scale: [8,12]` for Noise | `scale: 8, scaleY: 12` in its behaviour |
| A remote Drawing `url` or Base64 pixels | TIFF for Drawing pixels, or a verified File asset reference |
| `opacity: 80` for 80% | `opacity: 0.8` |
| ShaderLab, GLSL `mix`, invented helpers | HLSL `ApplyFX`, `lerp`, documented helpers below |
| `fx[].code` or live API operations inside stored content | `modifiers[].code` and native parameter values |

## Legacy input compatibility

Existing `whimtex.layers` data remains readable by the clipboard compatibility reader.
Its `type/properties`, Drawing `url`, limits and schema are described only in the
[legacy input reference](LEGACY_LAYERS.md). These are not an alternative format for new exports.
The linked-image example remains an explicitly labeled legacy fixture because unified JSON
does not download Drawing pixels. Brush and standalone gradient formats below are unaffected.

## Standalone gradient JSON

For a gradient field (not canvas layer paste), right-click the field or the gradient strip
in its editor and choose **Paste**. **Copy** produces this independent, reusable value:

```json
{
  "format": "whimtex.gradient",
  "version": 1,
  "gradient": {
    "mode": "Classic",
    "wrapMode": "Clamp",
    "colorSpace": "Gamma",
    "smoothness": 1,
    "colors": [
      { "time": 0, "color": [1, 0.1, 0, 1], "midpoint": 0.5 },
      { "time": 1, "color": [0.2, 0, 1, 1] }
    ],
    "alphas": [
      { "time": 0, "alpha": 1, "midpoint": 0.5 },
      { "time": 1, "alpha": 0 }
    ]
  }
}
```

The gradient body alone or a bare color-stop array is also accepted, optionally within one
JSON code fence. Each track requires 1..64 strictly increasing times in 0..1. Colors are
RGBA arrays; standalone RGB supports finite HDR values from -65504 to 65504, alpha is 0..1.
`wrapMode` is optional and defaults to `Clamp`; `Repeat` tiles values outside 0..1, while `Mirror`
reflects each repeated interval.
If `alphas` is omitted, color alpha components define the alpha track. Interpolation modes:
`Classic`, `Linear`, `Perceptual`, `Fixed`; default `Perceptual`. `colorSpace`: `Gamma` (default)
or `Linear`. `smoothness`: 0..1, default 1. `midpoint`: 0.01..0.99, default 0.5;
the last key's midpoint has no following segment. Rounded is the built-in algorithm, not a serialized setting.
The retired `transition` input field is ignored in old JSON/documents; it is not converted,
validated as a mode, exposed in UI, or written to new output. Old documents render through
Rounded directly without migration or resaving. Older artwork may therefore look different.
Other unknown fields are still rejected.
Rounded partitions the curve at complete equal-color intervals and uses monotone cubic
interpolation with adjacent-secant boundary slopes on each nonconstant block. In Perceptual,
opposing chroma is reduced by `0.5*(1-|a+b|/(|a|+|b|))`, where a/b are the neighboring
OKLab chroma vectors; a neutral endpoint disables the correction. Its envelope is
`[4u(1-u)]^2`, with u=0.5 at the midpoint, scaled by Smoothness. Lightness and alpha
are unaffected by this chroma adjustment. A shared RGB time map rounds the boundaries on both sides. Its radius
is the distance to the adjacent midpoint, limited to half the intervening constant
gap; redundant keys in an outer constant run do not alter it. The domain clips the support
to 0..1. With `u` normalized over that support and `q = supportLength / radius`,
use `e=0.2`, `v=min(u/e,1)`, and the integrated onset
`I(u)=e*(v^3-v^4/2)` for `u<e`, otherwise `I(u)=u-e/2`.
Then `F(u)=I(u)+(e/2)*u^3*(10-15u+6u^2)`, `S(u)=u^3*(2-u)`,
and `w=(2-q)*F(u)+(q-1)*S(u)`. The broad positive speed surplus in F reduces
domain-edge catch-up from 1.512 to 1.1875 times identity speed. The mapped coordinate is
`boundary + radius*w`, mirrored at the right edge. A color boundary already having zero
first and second output-linear-RGB derivatives is not eased again. Alpha uses an independent
scalar map. At full smoothness the constant joins and the time map are C2; the color curve
still inherits the base cubic's interior/midpoint joins, which need not be C2.
Partial smoothness retains a linear component. Stops at interior held boundaries
may have approximate evaluated colors/alpha; domain-edge stops, ordinary interior stops and
midpoint coordinates are preserved. This prioritizes smooth shoulders, not exact matching to
the reference or a guarantee of no plateau/rim. At zero smoothness interpolation is linear
in the selected working color space. Repeat does not make mismatched endpoints seamless.
Fixed ignores smoothness and midpoints.
The clipboard input is limited to 65536 characters. Unknown fields, duplicate fields and
unsupported versions are rejected. These standalone HDR limits do not change layer/brush JSON limits.

User presets store this envelope in `<user presets folder>/Gradients/<GUID>.json`.
Use a GUID without hyphens as the filename; files are limited to 64 KiB. No display name is needed.

## HLSL interface — shader-only or inside JSON

### Built-in noise library

FastNoiseLite v1.1.1, the same library used by Noise layers, is included automatically
in every WhimTex HLSL effect and brush. Do not paste the library or add an include.
Use the `fnl_` types, `fnl*` functions and `FNL_*` constants; avoid redefining those names.

```hlsl
// @param float _Scale = 8 [0.1 .. 64]
float4 ApplyFX(float2 uv, float4 color)
{
    fnl_state noise = fnlCreateState(123);
    noise.noise_type = FNL_NOISE_PERLIN;
    noise.frequency = 1.0;
    noise.fractal_type = FNL_FRACTAL_FBM;
    noise.octaves = 4;
    float n = fnlGetNoise2D(noise, uv.x * _Scale, uv.y * _Scale);
    return float4((n * 0.5 + 0.5).xxx, color.a);
}
```

Set `frequency = 1.0` when controlling scale through coordinates; the library default is 0.01.
`fnlGetNoise2D(state, x, y)` and `fnlGetNoise3D(state, x, y, z)` normally return −1..1
(some Cellular return modes exceed this range). Remap and clamp when a 0..1 mask is needed.
Noise types: `FNL_NOISE_OPENSIMPLEX2`, `FNL_NOISE_OPENSIMPLEX2S`, `FNL_NOISE_CELLULAR`,
`FNL_NOISE_PERLIN`, `FNL_NOISE_VALUE_CUBIC`, `FNL_NOISE_VALUE`.
Fractals: `FNL_FRACTAL_NONE`, `FNL_FRACTAL_FBM`, `FNL_FRACTAL_RIDGED`, `FNL_FRACTAL_PINGPONG`.
`fnlDomainWarp2D(state, x, y)` and `fnlDomainWarp3D(state, x, y, z)` modify coordinate variables in place;
configure `domain_warp_type` and `domain_warp_amp` on the state.
See the [bundled HLSL source](https://github.com/DCFApixels/WhimTex/blob/main/src/Shaders/ThirdParty/FastNoiseLite.hlsl) for the full state and constants.
White Noise and Blue Noise are separate Noise-layer implementations, not functions of this library.

The same noise calls work inside `float4 BrushTip(float2 uv)` for a brush;
its script still starts with `// @whimtex-brush Category/Name`.

### Effect entry point

Write a fragment function, **not a complete ShaderLab shader**:

```hlsl
// @whimtex-effect Color/Invert
// @param float _Amount = 1 [0 .. 1]
float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, 1 - color.rgb, _Amount), color.a);
}
```

`color` is the incoming straight (not premultiplied) RGBA. `SampleInput(uv)` reads that same
input at another UV, including earlier FX. Return straight RGBA in linear working space.
Shader Processor receives the lower composite; a regular layer's FX receives that layer's image.
To generate an image from scratch, use a Color layer with FX replacing its color.

`LayerToLocal(uv)` converts canvas UV to the owning layer's local UV, including parent group transforms and perspective. Use it for procedural shapes that must follow the layer transform. `ApplyFX` UV and `SampleInput` remain canvas-space; do not pass local UV to `SampleInput`. The helper does not wrap or clamp coordinates.

Available inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`
(width, height, reciprocal width, reciprocal height), `_PreviewScale`; `UnityCG.cginc` is already included.
Do not redeclare these or generated parameters/helpers. FX and Shader Processor code must be deterministic:
do not use Unity time inputs such as `_Time`, `_SinTime`, `_CosTime`, `_TimeParameters` or
`unity_DeltaTime`. They are not updated by the preview cache; their use only produces a warning and
disables caching for that result. Use an explicit parameter instead.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Offset = 0 [.. 10]
// @param float _Amount = 10
// @param bool _IncludeAlpha = false
// @param float2 _Offset = (0, 0)
// @param float3 _Direction = (1, 0, 0)
// @param normal _Normal = (0, 0, 1)
// @param point _Center = (0.5, 0.5)
// @param float4 _Channels = (0, 0, 0.5, 1)
// @param color _Tint = (1, 1, 1, 1)
// @param texture2D _Input = self
// @param texture2D _Optional = none
// @param texture2D _Mask
// @param gradient _Ramp
// @param transform2D _Area = (0.5, 0.5, 0.75, 0.75, 30)
```

No semicolon on metadata lines. Ranges apply to floats only. Defaults must be finite.
`[min .. max]` clamps edits through that control. Put `~` before a boundary value to allow crossing it:
`[0 .. ~2]` allows values above 2, `[~0 .. 2]` allows values below 0, and `[~0 .. ~2]` allows both.
The slider always stays within 0..2; numeric input and label dragging obey only the hard boundaries.
For example: `// @param float _Strength = 5 [0 .. ~2]` or `// @param float _Strength [~0 .. ~2]`.
Ranges with a soft boundary require both values and `min < max`; one-sided or equal soft bounds are errors.
The old `~[0 .. 2]` syntax is rejected. Boundary flags survive FX and HLSL brush preset export.
Bounds written inside HLSL still apply independently.
For FX, `bool` displays a toggle and generates a `float` uniform with value `0` or `1`, not a shader keyword. An optional default is `true`/`false` or `1`/`0`; ranges are not allowed. Use `if (_IncludeAlpha > 0.5)` or use it directly in arithmetic. This type is not supported by HLSL brush parameters yet.

FX also supports dropdown controls and repeated declarations of one variable:
```hlsl
// @param float _Strength = 0.63 [0 .. 1]
// @param enum _Strength { Low: 0.2, Medium: 0.5, High: 1 }
// @param enum _Mode = SoftLight { SoftLight: 0, HardLight: 1, CustomBlend: 0.5 }
```
Any FX or HLSL brush parameter declaration may end with `// tooltip text`, for example:
`// @param float _Strength = 0.65 [0 .. 1] // How strongly to apply the effect.`
This literal, single-line text appears on hover and is preserved when exporting presets.
Repeated declarations can have separate tooltips for their separate controls.
Enum option names are unquoted identifiers, used only as nicified UI labels, never as HLSL constants.
Each option requires an explicit finite value, including fractional values; duplicate names or values
are errors. Defaults may be option names or numbers. Unknown numeric values display as Custom.
All parameter types allow omitting `= value`. The last explicit default for a variable wins; if none
exists, scalar/vector/color defaults are zero. Repeated `float`/`bool`/`enum` controls share one float
uniform. Other repeated types must match exactly. Control ranges do not clamp values set through
another control. Preset export saves the current value once. These dropdown/linked controls are FX-only.
`float2`, `float3` and `float4` are raw vectors with two, three and four components. `point` is a `float2` position in normalized canvas UV, from bottom-left `(0, 0)` to top-right `(1, 1)`, and adds an **Edit on Canvas** handle that can be dragged across the canvas. Its default is `(0.5, 0.5)`; an explicit tuple is optional and ranges are not accepted. `normal` generates a normalized `float3`; its default and zero-vector fallback are `(0, 0, 1)`. It also offers an on-canvas direction handle; no range is accepted. Defaults are optional. Unknown parameter types are rejected.

FX use ordinary input images and explicit parameters, not hidden layer-specific data. Lighting/Bevel Emboss reads a height texture (Self by default) and shares lighting with Normal Map/Lighting. Base Color alpha blends transparent lighting (0) into the shaded surface (1); Output selects Both/Highlight Only/Shadow Only for the transparent part. SDF inputs use their visible gradient, not raw distances. See [shader reference](../ShaderFX.md) for the complete contract.

`float4` is a raw vector; `color` is a color picker. Texture parameters without an explicit default keep Texture mode and sample white when empty. Use `= none` for transparent black, or `= self` to sample the current layer immediately before this FX (including earlier FX, excluding current/later FX). Both are unquoted declaration keywords and are preserved in exported presets. These are FX parameter defaults, not layer IDs. Users can change the texture source in the editor. In unified JSON, existing texture assets use `$asset` identity references in the parameter value. Do not invent asset identities. Drawing pixels and URL downloads are not part of this format.
Names generate labels: `_NoiseScale` → Noise Scale. No need for a second uniform declaration.

Use `// @param curve _Profile = one` for a constant 1 curve with keys (0,1) and (1,1).
Curve defaults also accept `easeIn` (`t²`) and `easeOut` (`1-(1-t)²`), for example `// @param curve _Profile = easeIn`. Both span (0,0) to (1,1).

FX-only `curve` declares a scalar mapping: `// @param curve _Profile`, sampled with
`_Profile_Sample(t)`. Default: linear (0,0) to (1,1). Input clamps to 0..1; output is unrestricted.
Named defaults: `// @param curve _Profile = linear` or `// @param curve _Profile = easeInOut`.
The latter smoothly eases between the same endpoints with horizontal endpoint tangents.
For clipboard FX, an optional default can be included directly in `modifiers[].code`:
`// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))`.
Each tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
Use strictly increasing finite times, finite values, weights 0..1 and mode 0/1/2/3
(none/in/out/both). Tangents additionally allow `inf` or `-inf` for steps. Maximum 256 keys;
`keys()` evaluates to zero. No range. See [curve reference](../ShaderFX.md#curve-parameters)
for sampling precision and preset persistence. This is not a layer property or a brush parameter.

FX-only `gradient` is declared as `// @param gradient _Ramp`, optionally with two endpoint colors:
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint may be `#RRGGBB` (opaque),
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Without an initializer it starts opaque
black-to-white (Perceptual). Explicit two-endpoint defaults use Classic/Gamma/Clamp,
Smoothness 1 and midpoint .5 to preserve the endpoint-only HLSL export contract.
The user can edit colors, HDR, alpha and interpolation in the gradient field.
The same hex forms are accepted for `color` defaults; color defaults also accept numeric RGBA tuples.
Call `_Ramp_Sample(t)` for straight linear RGBA; `t` is clamped to 0..1.
For example, `return _Ramp_Sample(uv.x);`. Do not declare a sampler yourself. A cached 512×2
LUT supplies the samples; editing keys does not recompile the shader. HLSL brush parameters do not
support this type. Edited values persist in the document and two-endpoint defaults are exported with HLSL presets.

Transform2D uses `(centerX, centerY, width, height, angleDegrees)` in normalized input units.
Omitted default means the full image. For skew/perspective, use `// @param transform2D _Area = matrix(1, 0.2, 0, 0, 1, 0, 0.15, 0, 1)`: nine row-major values mapping local UV to input UV. The matrix must be invertible with no horizon crossing the unit rectangle. Do not combine matrix and TRS defaults.
Generated helpers are `_Area_ToLocal(uv)` and `_Area_ToInput(localUV)`; both support perspective.
Local `[0,0]`/`[1,1]` are corners and `[0.5,0.5]` is center. Out-of-range coordinates are valid.
There is **no automatic mask or falloff**: implement it in HLSL if desired. The user can edit the
frame with green canvas handles. Never reference reserved `_WhimTex_` internal uniforms.

For a `.hlsl` catalog preset the **first physical line** must be `// @whimtex-effect Category/Name`:
no blank line or license header before it. For inline/JSON FX this header is optional.
To encode HLSL inside JSON, use a string with `\n` for newlines; escape quotes normally. JSON escaping
is decoded before compilation. Keep generated FX self-contained and use the built-in helpers.
Copy as JSON expands project includes when possible; an expansion failure preserves the original
source for repair, so unresolved dependencies can still prevent compilation on another machine.
The restrictions of the [legacy clipboard reader](LEGACY_LAYERS.md#legacy-shader-restrictions)
are not the storage contract for unified JSON.

For additional engine-specific authoring details, see [Shader authoring](../ShaderFX.md).
