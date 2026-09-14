---
title: "AI authoring: JSON layers and HLSL effects"
nav_order: 4
lang: en
permalink: /ai-authoring/
description: "Generate WhimTex procedural texture layers, groups and HLSL Shader FX with a browser AI, then paste JSON into the Unity texture editor."
---

# WhimTex AI authoring: JSON layers and HLSL effects

This page is the complete starting contract for a browser AI generating editable textures for
**WhimTex, the Unity sprite and texture editor**. No Unity connection or file generation is required.
Return a JSON object for the user to copy and paste, or HLSL for a shader-only request.
This feature is available in development builds after the original 0.9.5 release.

## Instructions for an AI assistant

1. For a composition, return **one valid JSON object** in a `json` code block. No comments, trailing
   commas, Markdown or prose inside JSON. Use the field names and enum strings below exactly.
2. Use only procedural layers. Do not invent a Drawing/File layer, image URL, Base64 payload,
   asset GUID, Unity type name, asset path, live-agent request or filesystem operation.
3. Keep useful parts editable: prefer Shape, Gradient, Noise and targeted effects over one huge shader.
   Use a named group for a multi-layer composition. Avoid excessive layers, blur radii or shader loops.
4. Layer arrays are **top to bottom**, exactly like the Layers panel. FX arrays run **first to last**.
   Give referenced layers short, unique local IDs. These are not real document GUIDs.
5. For reusable HLSL, declare controls with `// @param`. The default values become the initial UI values.
   Keep shader code self-contained; preserve alpha unless the requested effect changes coverage.
6. Do not claim successful compilation or insertion without actually testing in Unity. The user can
   send an error back; return a corrected complete JSON object. Re-pasting creates new layers, not an update.

## Paste workflow

Copy the JSON using the code block's Copy button. Focus WhimTex's preview or Layers panel, leave
text editing, and press **Ctrl+V** (Cmd+V on macOS). A single surrounding `json` code fence is accepted too.
Text fields retain ordinary text paste. Canvas selection does not clip these new layers.

The tree is inserted at the **top level, above existing layers**, with fresh GUIDs and internal
targets remapped. It does not edit or delete existing layers. A Processor may of course affect
the lower stack visually. One Undo removes the paste and reverts any accompanying canvas resize.
Existing image and cross-window layer clipboard workflows remain available for other clipboard contents.

Unknown fields, invalid values, duplicate IDs, missing/cyclic targets and invalid HLSL reject the
paste without adding a partial tree. Custom HLSL requires confirmation before compilation.
Only accept code you trust: valid HLSL can still be expensive enough to stall the GPU.

## JSON envelope

```json
{
  "format": "whimtex.layers",
  "version": 1,
  "canvas": { "width": 512, "height": 512 },
  "layers": [
    {
      "type": "shape",
      "name": "Soft Square",
      "properties": {
        "shape": {
          "kind": "Rectangle",
          "roundness": 0.3,
          "fillColor": [0.2, 0.6, 1, 1]
        }
      },
      "transform": { "scale": [0.7, 0.7] }
    }
  ]
}
```

`format`, `version`, and nonempty `layers` are required. `canvas` is optional, but if present
both integer dimensions are required: 1..16384 each, at most 16,777,216 pixels in total.
On an empty document the size applies immediately. On a nonempty document with a different size,
**Apply Size** resizes; **Keep Current** inserts the same layers without resizing.
Resize changes the canvas, not an instruction to bake/resample all existing layers.

Limits: 1 MiB of JSON text, 128 layers total, 8 nested groups, 16 custom shaders total,
65,536 characters and 32 parameters per shader. These limits are not performance guarantees.
Do not embed a `$schema` property: the envelope accepts only the fields shown above.

## Layer fields

Every layer requires `type`. All other fields are optional; omitted settings use the editor's defaults.

| Field | Meaning |
| --- | --- |
| `type` | `color`, `gradient`, `noise`, `shape`, `outline`, `sdf`, `normalMap`, `blur`, `makeSeamless`, `shaderProcessor`, `group` |
| `name` | Display name, at most 128 characters |
| `id` | Unique local string, 1..64 characters; only needed for references |
| `properties` | Common settings and the type-specific settings below |
| `transform` | Non-group layer placement; see below |
| `children` | Groups only; if present, a nonempty array in top-to-bottom order |
| `target` | Local ID, without `@`, for Outline/SDF/Normal Map/Blur/Make Seamless |
| `fx` | Non-group layers only: array of `{ "name": "Optional name", "code": "HLSL source" }` |

Targets may refer forward or backward in the JSON. Hidden sources still work. With no `target`,
a targeted effect uses the next sibling below it. Prefer explicit targets for predictable portable results.
With no next sibling in the pasted tree it has no source; it does not attach to an existing document layer.
No targets outside this pasted tree. No cycles, self-targeting or targets that make a group depend on itself.
For a group-wide shader use a Shader Processor inside an isolated group, not `fx` on the group.

### Common properties

| Property | Values |
| --- | --- |
| `enabled`, `clippingMask` | Boolean. A clipping layer uses the base below its clipping chain. Processor cannot be clipped. |
| `opacity` | 0..1, **not** 0..100 |
| `blend` | `Normal`, `Multiply`, `Overwrite`, `None`, `Add`, `Subtract`, `Divide`, `Screen`, `Overlay`, `Darken`, `Lighten`, `Dodge`, `Burn`, `LinearDodge`, `LinearBurn`, `LinearLight`, `LinearLightAddSub`, `VividLight`, `PinLight`, `HardMix`, `HardLight`, `SoftLight`, `Difference`, `Exclusion`, `Negation` |
| `colorRange`, `blendRange` | `Standard` or `HDR` |
| `filter` | Non-group only: `Source`, `Point`, `Bilinear`, `Trilinear` |
| `swizzle` | Four strings in output RGBA order; each is `R`, `G`, `B`, `A`, `1-R`, `1-G`, `1-B`, `1-A`, `0`, `1`, `R * A`, `G * A`, `B * A` |
| `compositing` | Group only: `PassThrough` or `Isolated` |

Colors are `[r,g,b,a]`, alpha 0..1; RGB may be HDR (-107..107). These are editor color values;
shader calculations use linear working space. Use `colorRange`/`blendRange: "HDR"` when HDR output matters.

### Transform

`transform` accepts `position: [x,y]`, `scale: [x,y]`, `pivot: [x,y]`, `rotation` in degrees,
and `tiling: "Clip" | "Repeat" | "Mirror" | "Source"`.
Position is an offset in canvas pixels: default `[0,0]`, positive X right, positive Y up.
Pivot is normalized bottom-left UV, default `[0.5,0.5]`; scale `[1,1]` covers the canvas.
Rotation is counterclockwise. Changing pivot does not compensate position. Negative scale mirrors an axis;
absolute scale components must be at least 0.00001. No group transform.

### Shape, Color and Gradient

- **color:** `properties.color` is RGBA.
- **shape:** `properties.shape` accepts `kind` (`Rectangle`, `Ellipse`, `Polygon`, `Star`, `Line`),
  `fill`/`stroke` booleans, `fillColor`/`strokeColor`, `strokeWidth` (0..8192 pixels),
  `roundness` (0..1), `cornerRoundness` (four 0..1 values: top-left, top-right, bottom-right, bottom-left),
  `linkCorners` boolean, `sides` (integer 3..32), `innerRadius` (0.01..1).
- **gradient:** `properties.gradient` is 2..8 `{ "time": 0, "color": [1,1,1,1] }` stops,
  with strictly increasing times in 0..1. `properties.gradientOptions` optionally sets
  `type` (`Vertical`, `Horizontal`, `Radial`, `Circular`, `Diamond`, `Square`), `center: [x,y]`,
  `radius` (0.00001..1000), `repetitions` (0.00001..1000), `wrap` (`Repeat`, `PingPong`),
  `mode` (`Blend`, `Fixed`, `PerceptualBlend`).

### Noise

Use `properties.noise`:

| Fields | Values |
| --- | --- |
| `noiseType` | `OpenSimplex2`, `OpenSimplex2S`, `Cellular`, `Perlin`, `ValueCubic`, `Value`, `WhiteNoise`, `BlueNoise` |
| `seed`, `scale`, `offset` | 32-bit integer; 0.01..1000; two values -10000..10000 |
| `dimensions`, `direction` | `TwoD` or `OneD` (stripes); -180..180 degrees |
| `fractal`, `octaves` | `None`, `FBm`, `Ridged`, `PingPong`; integer 1..8 |
| `lacunarity`, `gain`, `weightedStrength`, `pingPongStrength` | 1..4; 0..1; 0..1; 0.01..8 |
| `cellularDistance` | `Euclidean`, `EuclideanSquared`, `Manhattan`, `Hybrid` |
| `cellularReturn` | `CellValue`, `Distance`, `Distance2`, `Distance2Add`, `Distance2Sub`, `Distance2Mul`, `Distance2Div` |
| `cellularJitter` | 0..1 |
| `warp`, `warpStrength` | `None`, `OpenSimplex2`, `OpenSimplex2Reduced`, `BasicGrid`; 0..100 |
| `encoding`, `inverted` | `ColorValues` or `LinearData`; boolean |
| `whiteNoiseColor`, `whiteNoiseSize` | `Monochrome` or `Color`; 1..1024 pixel cell size, for White/Blue Noise |

### Targeted effects

- **blur:** `properties.blur`: `mode` (`Gaussian`, `Linear`, `Circular`), `strength` 0..4,
  `radius` 0..256 pixels, `distance` 0..512 pixels, `angle` -180..180 degrees, `arc` 0..360 degrees,
  `center` two 0..1 values, `direction` (`Centered`, `Forward`, `Backward`),
  `edges` (`Transparent`, `Clamp`, `Repeat`, `Mirror`). Mode selects which controls matter.
- **outline:** directly in `properties`: `color`, `outlineWidth`/`outlineSoftness` 0..16384 pixels,
  `outlineOffset` -16384..16384 pixels, `outlinePosition` (`Outside`, `Inside`, `Center`),
  `fillCenter` boolean, `fillColor`, `metric`.
- **sdf:** directly in `properties`: `sourceChannel` (`Alpha`, `Red`, `Green`, `Blue`, `Luminance`),
  `threshold` integer 0..255, `distancePosition` (`Outside`, `Inside`, `Center`, `Signed`),
  `inverted` boolean, `maxDistance` 0..16384 (0 = automatic), `gradient` stops, `metric`.
- **metric** for Outline/SDF: `EuclideanExact`, `EuclideanApproximate`, `EuclideanAntialiased`,
  `Manhattan`, `Chebyshev`.
- **makeSeamless:** `properties.makeSeamless`: `horizontal` (`Off`, `LeftToRight`, `RightToLeft`),
  `vertical` (`Off`, `BottomToTop`, `TopToBottom`), `blendWidth` 0.001..0.5, `falloff` 0.25..4.
- **normalMap:** `properties.normalMap`: commonly `mode: "HeightMap"`, `strength` 0..128,
  `sourceChannel` (`Luminance`, `Red`, `Green`, `Blue`, `Alpha`, `Maximum`), `smoothing` 0..64,
  `flipX`/`flipY` booleans. All advanced options and exact ranges are in the [schema](layers.schema.json).

## HLSL interface — shader-only or inside JSON

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

Available inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`
(width, height, reciprocal width, reciprocal height), `_PreviewScale`; `UnityCG.cginc` is already included.
Do not redeclare these or generated parameters/helpers. Do not use invented time, depth or scene inputs.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Offset = 0 [.. 10]
// @param float _Amount = 10
// @param float4 _Channels = (0, 0, 0.5, 1)
// @param color _Tint = (1, 1, 1, 1)
// @param texture2D _Mask
// @param transform2D _Area = (0.5, 0.5, 0.75, 0.75, 30)
```

No semicolon on metadata lines. Ranges apply to floats only. Defaults must be finite and inside bounds.
`float4` is a raw vector; `color` is a color picker. Texture parameters without a source default to white;
the user may assign them later, but clipboard JSON cannot supply texture assets.
Names generate labels: `_NoiseScale` → Noise Scale. No need for a second uniform declaration.

Transform2D uses `(centerX, centerY, width, height, angleDegrees)` in normalized input units.
Omitted default means the full image. Generated helpers are `_Area_ToLocal(uv)` and `_Area_ToInput(localUV)`.
Local `[0,0]`/`[1,1]` are corners and `[0.5,0.5]` is center. Out-of-range coordinates are valid.
There is **no automatic mask or falloff**: implement it in HLSL if desired. The user can edit the
frame with green canvas handles. Never reference reserved `_WhimTex_` internal uniforms.

For a `.hlsl` catalog preset the **first physical line** must be `// @whimtex-effect Category/Name`:
no blank line or license header before it. For inline/JSON FX this header is optional.
To encode HLSL inside JSON, use a string with `\n` for newlines; escape quotes normally. JSON escaping
is decoded before compilation. **Clipboard HLSL cannot use `#` directives, includes, backslashes
or asset GUIDs.** These restrictions do not change manually authored HLSL elsewhere in the editor.

For additional engine-specific authoring details, see [Shader authoring](../ShaderFX.md).

## Complete examples and validation

These are reference recipes for AI authors, not a user-guide gallery or built-in presets.
See the [example index](../Examples/Clipboard/README.md) for what each recipe demonstrates.

- [Neon ring: Shape + Blur in a group](../Examples/Clipboard/neon-ring.json)
- [Car wheel: layered primitive shapes](../Examples/Clipboard/car-wheel.json)
- [Forked lightning: procedural particle sprite and glow](../Examples/Clipboard/forked-lightning.json)
- [Heart: minimal properties, clipping, SDF, Outline and primitive highlights](../Examples/Clipboard/heart.json)
- [Mystic fog: Noise, hidden source, Blur and Gradient](../Examples/Clipboard/mystic-fog.json)
- [Retro posterization Processor](../Examples/Clipboard/retro-processor.json)
- [Local distortion with editable Transform 2D](../Examples/Clipboard/local-distortion.json)
- [JSON Schema: exact field names, types and enum values](layers.schema.json)

The schema checks structure; Unity additionally checks references, increasing gradient times,
nonzero scales, total limits, Normal Map cross-field constraints and shader compilation.
For Normal Map, `whiteLevel > blackLevel` and `largeRadius >= mediumRadius` are required.

## A prompt users can copy

> Read the WhimTex JSON/HLSL authoring guide at https://dcfapixels.github.io/WhimTex/ai-authoring/.
> Create a 512 × 512 magical ring texture using editable procedural layers, grouped and named in English.
> Return one complete clipboard JSON code block. Use only documented fields and no external assets.

If that page is not published yet, provide the guide from the repository's current development branch
or paste its contents into the chat. Search indexing and raw-README comments are discovery aids,
not requirements and not guarantees that an AI has read the specification.
