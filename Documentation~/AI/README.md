---
title: "AI authoring: JSON layers and HLSL effects"
nav_order: 4
lang: en
permalink: /ai-authoring/
description: "Generate WhimTex procedural texture layers, groups and HLSL Shader FX with a browser AI, then paste JSON into the Unity texture editor."
---

# WhimTex AI authoring: JSON layers and HLSL effects

For **brushes**, use the separate [brush JSON/HLSL contract](BRUSHES.md) and
[brush examples](../Examples/Brushes/README.md). Brush JSON replaces the current brush;
it is not a layer document. HLSL brushes implement `BrushTip`, not `ApplyFX`.

This page is the complete starting contract for a browser AI generating editable textures for
**WhimTex, the Unity sprite and texture editor**. No Unity connection or file generation is required.
Return a JSON object for the user to copy and paste, or HLSL for a shader-only request.
This feature is available in WhimTex 0.9.6 and later. Drawing layers that download an image from a
link require WhimTex 0.10.1 or later.

## Where to start

**Ready-to-copy JSON files live in `Documentation~/Examples/Clipboard/`.**
Open the [example index](../Examples/Clipboard/README.md), choose a recipe for the requested task,
and read its actual `.json` file before adapting it. These are clipboard examples, not the live API
examples in the parent directory.

For an image URL followed by a shader effect, start with
[Stone wall: Drawing + Shader Processor](../Examples/Clipboard/stone-wall-retro.json).
The [recipe list below](#complete-examples-and-validation) covers procedural shapes, VFX and targeted effects.

Reading order: instructions → example files → [common mistakes](#common-mistakes) →
[full specification](#full-specification). The specification is at the bottom of this page;
examples illustrate it, but do not define extra fields.

## Instructions for an AI assistant

1. For a composition, return **one valid JSON object** in a `json` code block. No comments, trailing
   commas, Markdown or prose inside JSON. Use the field names and enum strings below exactly.
2. Compose with procedural layers, and bring a bitmap in with a Drawing layer that has a `url`
   (a direct `http(s)` link to a PNG or JPEG). Do not invent a File layer, Base64 payload, asset GUID,
   Unity type name, asset path, live-agent request or filesystem operation.
3. Keep useful parts editable: prefer Shape, Gradient, Noise and targeted effects over one huge shader.
   Use a named group for a multi-layer composition. Avoid excessive layers, blur radii or shader loops.
4. Layer arrays are **top to bottom**, exactly like the Layers panel. FX arrays run **first to last**.
   Give referenced layers short, unique local IDs. These are not real document GUIDs.
5. For reusable HLSL, declare controls with `// @param`. The default values become the initial UI values. Use `label(...)` inline to override the generated UI label without renaming the shader uniform, for example `// @param label(Tint Strength) float _Strength = 1`. The `hidden` and `label(...)` modifiers may appear in either order; quote labels containing parentheses. Optional `// @header(Lighting)` adds a bold heading; `// @helpbox(Your hint text.)` adds an informational help box before the next parameter. `// @formerlyserializedas(_OldName)` immediately before a parameter preserves compatible values when an FX uniform is renamed; repeat it for multiple previous names. Use `// @group(Title; _Parameter)` through `// @endgroup` to visually group controls and place a linked parameter in the header. A bool becomes an unlabeled left checkbox that only edits the value; use `@if` to show/hide dependent controls. Compact enum, float, color, float2, float3, and float4 controls appear on the right without a separate label: the group title labels the value. Dragging a float's title adjusts its value. These controls are omitted from the body. The linked parameter must be declared unconditionally inside its group. `hidden` suppresses the normal body row but does not suppress an explicitly linked supported header control; unlinked hidden parameters stay invisible. Unsupported/multi-row types remain in the body. When all body rows are hidden by `@if`, the body collapses and only the header remains. Use `// @group(Title)` for a titled box without a linked header control, or plain `// @group` for an untitled group. Groups cannot be nested. These directives are UI/serialization metadata; parameters remain shader uniforms.
   FX controls may be conditionally shown with `// @if _Mode == 1` or `// @if _Mode != 1`, closed by `// @endif`. Only numeric comparisons are supported; the condition must reference an unconditional scalar `float`, `bool` or `enum` parameter. This only hides editor controls—hidden values remain serialized and active in the shader. Do not nest these blocks.
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
A Drawing layer with `url` downloads its image before anything is pasted, and those links require a
confirmation that names the hosts. If a download fails, nothing is inserted.

## Complete examples and validation

These are reference recipes for AI authors, not a user-guide gallery or built-in presets.
See the [example index](../Examples/Clipboard/README.md) for what each recipe demonstrates.

- [Neon ring: Shape + Blur in a group](../Examples/Clipboard/neon-ring.json)
- [Shock wave: radial streaks from Gradient, Blue Noise and inline FX](../Examples/Clipboard/shock-wave.json)
- [Car wheel: layered primitive shapes](../Examples/Clipboard/car-wheel.json)
- [Forked lightning: procedural particle sprite and glow](../Examples/Clipboard/forked-lightning.json)
- [Heart: minimal properties, clipping, SDF, Outline and primitive highlights](../Examples/Clipboard/heart.json)
- [Mystic fog: Noise, hidden source, Blur and Gradient](../Examples/Clipboard/mystic-fog.json)
- [Stone wall: linked Drawing image + pixelation, posterization and Bayer dithering](../Examples/Clipboard/stone-wall-retro.json)
- [Retro posterization Processor](../Examples/Clipboard/retro-processor.json)
- [Local distortion with editable Transform 2D](../Examples/Clipboard/local-distortion.json)
- [JSON Schema: exact field names, types and enum values](layers.schema.json)

The schema checks structure; Unity additionally checks references, increasing gradient times,
nonzero scales, total limits, Normal Map cross-field constraints and shader compilation.
For Normal Map, `whiteLevel > blackLevel` and `largeRadius >= mediumRadius` are required.

## A prompt users can copy

> Read the WhimTex JSON/HLSL authoring guide at https://dcfapixels.github.io/WhimTex/ai-authoring/.
> Create a 512 × 512 magical ring texture using editable procedural layers, grouped and named in English.
> Return one complete clipboard JSON code block. Use only documented fields; only direct http(s) image
> links are allowed as external references, and no local files or asset paths.

If that page is not published yet, provide the guide from the repository's current development branch
or paste its contents into the chat. Search indexing and raw-README comments are discovery aids,
not requirements and not guarantees that an AI has read the specification.

## Common mistakes

Check these before returning JSON. This is a reading checklist, not proof of validation or compilation.

| Mistake | Use instead |
| --- | --- |
| `"type": "fx"` | `"type": "shaderProcessor"` for the lower stack; `fx` is an array on a non-group layer. |
| `properties.url` | Put `url` directly on the Drawing layer, alongside `type` and `properties`. |
| A Markdown link such as `"[image](https://…)"`, or an HTML image page | A plain absolute HTTP(S) URL that returns PNG/JPEG image data. |
| A full-image Drawing layer above its Processor | Place the Processor first: layer arrays run top to bottom. |
| `opacity: 80` | `opacity: 0.8`; opacity ranges from 0 to 1. |
| `// @param Strength (Range 0 1) = 0.5` | `// @param float _Strength = 0.5 [0 .. 1]`. |
| Extra or reordered arguments in `ApplyFX` | Exactly `float4 ApplyFX(float2 uv, float4 color)`. |
| `SampleTexture(uv)` or an invented `texelSize` argument | `SampleInput(uv)`; documented sizes are `_InputSize` and `_CanvasSize`. |
| GLSL `mix(a, b, t)` | HLSL `lerp(a, b, t)`. |
| Redeclaring a uniform already declared by `@param` | Let WhimTex generate that uniform. |
| Markdown escapes such as `\_`, `\*` or `\&` inside JSON strings | Plain `_`, `*`, `&`. Use JSON escapes such as `\n` only where needed. |
| Expecting a linked Drawing layer to auto-fit with an explicit transform | Omit both scale and matrix for automatic fitting. Explicit scale/matrix preserves the supplied placement. |
| Putting `fx` directly on a group | Put a Shader Processor inside an isolated group. |
| Using real document GUIDs, `@id`, or targets outside the pasted tree | Use a unique local `id` and the same plain string in `target`. |

If a field or function is not documented, do not guess it from another editor or shader language.
If the user reports an error, correct the complete JSON using the exact error path/message.
A schema check does not verify shader compilation, image availability or the visual result.

## Full specification

The sections below specify the clipboard JSON and embedded HLSL contract, not live-agent requests.
For every accepted property and exact structural constraints, including advanced Normal Map settings,
see [`Documentation~/AI/layers.schema.json`](layers.schema.json). The additional semantic constraints
described here are checked by WhimTex; the schema alone is not a complete runtime validator.

## JSON envelope

```json
{
  "format": "whimtex.layers",
  "version": 1,
  "canvas": { "width": 512, "height": 512, "filter": "Point" },
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

Optional `canvas.filter` sets the final canvas/output filtering: `Point`, `Bilinear` or `Trilinear`
(no `Source`). Omit it to keep the current document's filtering. Width and height remain required
when `canvas` is present. An explicit filter applies on successful paste even with **Keep Current**,
which keeps only the size; Undo restores both the layers and the previous canvas settings.
This is separate from each layer's `properties.filter`.

Limits: 1 MiB of JSON text, 128 layers total, 8 nested groups, 16 custom shaders total,
65,536 characters and 32 parameters per shader, 16 linked images. Each linked image is at most 64 MB
and 16 megapixels, and must be a PNG or JPEG. These limits are not performance guarantees.
Do not embed a `$schema` property: the envelope accepts only the fields shown above.

## Layer fields

Every layer requires `type`. All other fields are optional; omitted settings use the editor's defaults.

| Field | Meaning |
| --- | --- |
| `type` | `color`, `gradient`, `noise`, `shape`, `outline`, `sdf`, `normalMap`, `blur`, `sharpen`, `makeSeamless`, `shaderProcessor`, `drawing`, `file`, `group` |
| `name` | Display name, at most 128 characters |
| `id` | Unique local string, 1..64 characters; only needed for references |
| `properties` | Common settings and the type-specific settings below |
| `transform` | Non-group layer placement; see below |
| `children` | Groups only; if present, a nonempty array in top-to-bottom order |
| `target` | Local ID, without `@`, for Outline/SDF/Normal Map/Blur/Make Seamless |
| `fx` | Any layer, including groups: array of `{ "name": "Optional name", "code": "HLSL source" }`. Optional `enabled` (default true), `gradients` mapping declared parameter names to gradient values, and `textures` mapping texture parameter names to `{ "layer": "clipboard-id" }`. |
| `url` | Drawing layers only: absolute `http`/`https` link to a PNG or JPEG, downloaded on paste |
| `asset` | File only: `{ "guid": "32 hex characters", "localId": "2800000" }`. Restores an existing Texture2D; localId is an optional signed 64-bit decimal string identifying a subasset. Missing assets produce an empty layer and a warning. Never invent GUIDs. |
| `contentOmitted` | Drawing/File only: `true` marks omitted image content and warns on paste. Cannot accompany `url` or `asset`. Layer settings and references remain intact. |

Targets may refer forward or backward in the JSON. Hidden sources still work. With no `target`,
a targeted effect uses the next sibling below it. Prefer explicit targets for predictable portable results.
With no next sibling in the pasted tree it has no source; it does not attach to an existing document layer.
No targets outside this pasted tree. No cycles, self-targeting or targets that make a group depend on itself.
Groups support `fx`: effects process the combined children before group opacity and blending. FX automatically isolate a Pass Through group using Normal blending, without affecting layers outside it. Removing all FX restores Pass Through unless clipping or Swizzle still requires isolation.

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
and `tiling: "Source" | "Clip" | "Repeat" | "Mirror" | "Clamp" | "Unbounded"`.
`Clamp` extends edge pixels. `Unbounded` evaluates Noise, Gradient, Color Fill and Shape beyond 0–1 UV;
other raster layers fall back to Clip. Gradient keys still bound the available colors.
Position is an offset in canvas pixels: default `[0,0]`, positive X right, positive Y up.
Pivot is normalized bottom-left UV, default `[0.5,0.5]`; scale `[1,1]` covers the canvas.
Rotation is counterclockwise. Changing pivot does not compensate position. Negative scale mirrors an axis;
absolute scale components must be at least 0.00001. Groups also accept transform. Child transforms are local to their parent group; the canvas matrix is parent canvas matrix multiplied by the local matrix. Group frames use the group's own unit rectangle, not child bounds.

Alternatively, use `matrix: [m00,m01,m02,m10,m11,m12,m20,m21,m22]` for skew or perspective.
The row-major 3×3 matrix maps normalized source UV to normalized canvas UV (bottom-left origin):
`x'=(m00*x+m01*y+m02)/w`, `y'=(m10*x+m11*y+m12)/w`, `w=m20*x+m21*y+m22`.
It must be invertible, with finite values and no zero/sign change of `w` inside the source rectangle.
Do not combine `matrix` with `position`, `scale` or `rotation`; `pivot` and `tiling` remain allowed.
TRS and matrix values are stored as doubles; GPU evaluation uses floats. A matrix is not automatically simplified.
Example: `"transform":{"matrix":[0.8,0.1,0.05,0,0.8,0.1,0,0.25,1]}`.

### Drawing layers and linked images

A Drawing layer owns its pixels. `{ "type": "drawing" }` adds an empty layer the user can paint on,
and adding `url` downloads that link before the paste and fills the layer with the image:

- the texture keeps its **source resolution**; the image is never resampled to the canvas,
- omit both `transform.scale` and `transform.matrix` to fit the image to the canvas; explicit scale or matrix preserves its placement instead,
- the link is fetched **once, at paste time**. Pixels are stored locally, so opening the saved document does not require the network. The original URL is retained for **Copy as Portable** while the pixels remain unmodified; transforms and FX do not invalidate it,
- a confirmation lists the hosts before any download starts. If one download fails, nothing is pasted,
- the whole tree, images included, lands as a single Undo step.

PNG and JPEG only. `properties.brush` is not available from the clipboard: pasted Drawing layers start
empty unless they carry a `url`.

**Copy as Portable** in the Layers context menu exports selected layers/groups to this same format, including canvas size/filter, current FX values and internal references. The receiving user pastes the JSON with Ctrl+V. Drawing with an unchanged URL source retains that URL; otherwise it becomes an empty Drawing layer with a warning. File retains its asset GUID and local ID, not its pixels: the recipient needs the same asset and `.meta` file. If unavailable, it remains an empty File layer with a warning; its asset identity survives another portable copy. File without an asset is copied empty. No raster bytes are embedded. Material FX remain unsupported. Custom HLSL includes are expanded into separate function definitions (calls are not inlined), with at most 8 nested files and 64 KiB of UTF-8 code per FX, including declarations. Oversized files, missing include files and cycles abort copying. Include referenced layers and clipping bases in the selection. Unsupported dependencies or format limits stop the copy without replacing the clipboard. A URL may change or expire: it is not an immutable copy of the image. Review links before sharing, especially private or signed URLs. Color Fill also accepts `properties.fillMode` (`Color` by default, or `UV`).

### Shape, Color and Gradient

- **color:** `properties.color` is RGBA.
- **shape:** `properties.shape` accepts `kind` (`Rectangle`, `Ellipse`, `Polygon`, `Star`, `Line`),
  `fill`/`stroke` booleans, `fillColor`/`strokeColor`, `strokeWidth` (0..8192 pixels),
  `roundness` (0..1), `cornerRoundness` (four 0..1 values: top-left, top-right, bottom-right, bottom-left),
  `linkCorners` boolean, `sides` (integer 3..32), `innerRadius` (0.01..1).
- **gradient:** `properties.gradient` is 1..64 `{ "time": 0, "color": [1,1,1,1] }` stops,
  with strictly increasing times in 0..1. `properties.gradientOptions` optionally sets
  `type` (`Vertical`, `Horizontal`, `Radial`, `Circular`, `Diamond`, `Square`),
  `repetitions` (0.00001..1000), `wrap` (`Repeat`, `PingPong`),
  `mode` (`Classic`, `Linear`, `Perceptual`, `Fixed`) and `smoothness` (0..1).
  Geometry uses the layer's top-level `transform`: position, scale and rotation. There are no `center` or `radius` options.
  Radial, Diamond, Square and Circular are centered at local UV [0.5,0.5]; the first three reach the final stop at distance 0.5.
  Stops optionally include `midpoint` and `alphaMidpoint` (0.01..0.99, default 0.5).
  For independent tracks, use an object instead of an array:
  `{ "colors": [...], "alphas": [{ "time": 0, "alpha": 1, "midpoint": 0.5 }], "mode": "Linear", "smoothness": 1 }`.
  Each track supports 1..64 keys; times must increase. `colorSpace` is optionally `Gamma` (default) or `Linear`.
  SDF uses `Linear` interpolation by default; other gradients use `Classic`. `Fixed` ignores midpoint and smoothness.

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
- **sharpen:** `properties.sharpen`: `algorithm` (`Gaussian`, `Adaptive`), `strength` 0..4
  (default 1), `radius` 0..32 canvas pixels (default 1), `threshold`, `noiseReduction` and
  `haloSuppression` 0..1, `channelMode` (`RGB`, `Luminance`), and `edges` (`Transparent`,
  `Clamp`, `Repeat`, `Mirror`). `noiseReduction` applies only to `Adaptive`, where it further
  suppresses weak, directionless detail; it does not remove noise from the source. Zero strength
  or radius is an identity; HDR RGB values are preserved.
- **outline:** directly in `properties`: `color`, `outlineWidth`/`outlineSoftness` 0..16384 pixels,
  `outlineOffset` -16384..16384 pixels, `outlinePosition` (`Outside`, `Inside`, `Center`),
  `fillCenter` boolean, `fillColor`, `metric`.
- **sdf:** directly in `properties`: `sourceChannel` (`Alpha`, `Red`, `Green`, `Blue`, `Luminance`),
  `threshold` integer 0..255, `distancePosition` (`Outside`, `Inside`, `Center`, `Signed`),
  `inverted` boolean, `maxDistance` 0..16384 (0 = automatic), `gradient` stops, `metric`.
  Also `sourceOffset: [x,y]` in document pixels (each -16384..16384), `sourceEdges: "Transparent"|"Clamp"|"Repeat"|"Mirror"` (default Transparent), and `contourOffset` -16384..16384 (positive expands). Signed mode accepts `insideDistance` and `outsideDistance` 0..16384; 0 inherits maxDistance/auto. The contour maps to 0.5, interior limit to 0, exterior limit to 1. Optional `profile` uses the curve string syntax (`"linear"`, `"easeInOut"`, `"easeIn"`, `"easeOut"`, `"one"`, or `"keys((...))"` with the same seven values per key as FX curves). Inversion precedes Profile, then gradient sampling; profile output clamps to 0..1. Source Offset shifts the available input image without cropping the shifted contour before distance computation; it does not recover content already clipped by the upstream layer. Repeat considers contours across tile seams, Mirror reflects the field. Extended distance domains are limited to 64 million pixels; excessive offsets/resolutions report an error rather than silently cropping.
- **metric** for Outline/SDF: `EuclideanExact`, `EuclideanApproximate`, `EuclideanAntialiased`,
  `Manhattan`, `Chebyshev`.
- **makeSeamless:** `properties.makeSeamless`: `horizontal` (`Off`, `LeftToRight`, `RightToLeft`),
  `vertical` (`Off`, `BottomToTop`, `TopToBottom`), `blendWidth` 0.001..0.5, `falloff` 0.25..4.
- **normalMap:** `properties.normalMap`: commonly `mode: "HeightMap"`, `strength` 0..128,
  `sourceChannel` (`Luminance`, `Red`, `Green`, `Blue`, `Alpha`, `Maximum`), `smoothing` 0..64,
  `flipX`/`flipY` booleans. All advanced options and exact ranges are in the [schema](layers.schema.json).

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
`Classic`, `Linear`, `Perceptual`, `Fixed`; default `Classic`. `colorSpace`: `Gamma` (default)
or `Linear`. `smoothness`: 0..1, default 1. `midpoint`: 0.01..0.99, default 0.5;
the last key's midpoint has no following segment. Fixed ignores smoothness and midpoints.
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

`float4` is a raw vector; `color` is a color picker. Texture parameters without an explicit default keep Texture mode and sample white when empty. Use `= none` for transparent black, or `= self` to sample the current layer immediately before this FX (including earlier FX, excluding current/later FX). Both are unquoted declaration keywords and are preserved in exported presets. These are FX parameter defaults, not layer IDs. Users can change the texture source in the editor. Clipboard JSON cannot bind an asset texture to a shader parameter: a
Drawing layer with a `url` is the way to bring an image into the pasted tree.
Names generate labels: `_NoiseScale` → Noise Scale. No need for a second uniform declaration.

Use `// @param curve _Profile = one` for a constant 1 curve with keys (0,1) and (1,1).
Curve defaults also accept `easeIn` (`t²`) and `easeOut` (`1-(1-t)²`), for example `// @param curve _Profile = easeIn`. Both span (0,0) to (1,1).

FX-only `curve` declares a scalar mapping: `// @param curve _Profile`, sampled with
`_Profile_Sample(t)`. Default: linear (0,0) to (1,1). Input clamps to 0..1; output is unrestricted.
Named defaults: `// @param curve _Profile = linear` or `// @param curve _Profile = easeInOut`.
The latter smoothly eases between the same endpoints with horizontal endpoint tangents.
For clipboard FX, an optional default can be included directly in `fx[].code`:
`// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))`.
Each tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
Use strictly increasing finite times, finite values, weights 0..1 and mode 0/1/2/3
(none/in/out/both). Tangents additionally allow `inf` or `-inf` for steps. Maximum 256 keys;
`keys()` evaluates to zero. No range. See [curve reference](../ShaderFX.md#curve-parameters)
for sampling precision and preset persistence. This is not a layer property or a brush parameter.

FX-only `gradient` is declared as `// @param gradient _Ramp`, optionally with two endpoint colors:
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint may be `#RRGGBB` (opaque),
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Without an initializer it starts opaque
black-to-white (Classic). The user can edit colors, HDR, alpha and interpolation in the gradient field.
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
is decoded before compilation. Clipboard HLSL accepts `#define`, `#undef`, `#if`, `#ifdef`, `#ifndef`, `#elif`, `#else`, `#endif`, and `#error`, including continued macro lines. Literal `#include` is allowed only for `UnityCG.cginc`, `Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl`, and `Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc`. Copy as Portable expands other files on the sender's machine; a recipient never resolves arbitrary project includes. Macro include paths, `#include_with_pragmas`, other directives and texture asset GUIDs are rejected. The expanded FX source must fit 64 KiB in UTF-8; the overall clipboard limit is unchanged. Ordinary HLSL presets outside clipboard retain their existing rules.

For additional engine-specific authoring details, see [Shader authoring](../ShaderFX.md).
