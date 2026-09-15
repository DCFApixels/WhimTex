---
layout: default
search_exclude: true
title: Agent API
parent: Technical reference
nav_order: 1
lang: en
permalink: /reference/agentapi/
---

# WhimTex: agent API v1

For a browser AI without a Unity connection, use the separate [clipboard JSON/HLSL contract](AI/README.md).
Clipboard paste does not execute the operations described on this page.

WhimTex is installed as `com.dcfapixels.whimtex`, its namespace is `DCFApixels.WhimTex` and its
assemblies are `DCFApixels.WhimTex*` (previously `com.dcfa_pixels.sprite-editor` and
`DCFApixels.SpriteEditor`). The 0.10.0 rename preserves documents from the preceding
Layer/Behaviour format through `MovedFrom` markers. It does not migrate documents from before
that redesign. Update integrations to the `WhimTexApi` type and `whimtex_*` commands;
the JSON command contract remains v1.
Preference keys were renamed to `DCFApixels.WhimTex.*` without migrating old values, so user
settings revert to defaults. Presets in the old default folder remain discoverable while the
new default folder does not exist; a custom preset-folder path must be selected again.
{: .no_toc }

<details markdown="1">
<summary>On this page</summary>

- Contents
{:toc}

</details>

The API edits the same model and uses the same renderer, brush and save path as the window.
For reservations, generation and selected-region edits in an open (possibly unsaved) document,
use the [live editing API](LiveAgentAPI.md). The path-based batch contract below remains unchanged.
No WhimTex window or active selection is required. It creates ordinary compositor `.asset`
files, with their layers, owned Drawing textures, baked Texture2D and Sprite subassets, and Project preview.

The window's optional **Live Update** publishes preview pixels to the existing output texture on the GPU
without changing its asset reference or CPU pixel data. It is not an API autosave mode: use `save` to persist
changes, and `render` to obtain current pixels rather than reading `OutputTexture.GetPixels()` during live
preview. Disabling live output restores the saved image; preview EV, channel display and Post FX are excluded.

## Layer identity and behaviour

`TextureCompositor.layers` contains stable `Layer` objects. Common settings, GUIDs, FX references,
group compositing and `children` belong to `Layer`; only the type-specific `LayerBehaviour` is
polymorphic. Use `layer.Behaviour is DrawingLayerBehaviour drawing` to access owned pixels or
other behaviour-specific methods. `layer.SetBehaviour(new NoiseLayerBehaviour())` replaces the
behaviour without replacing the layer or changing its common settings. Document changes still
need the normal Undo/`MarkChanged` workflow; use the JSON API for agent authoring.

A behaviour can belong to only one layer. Do not share one behaviour between wrappers.
Swapping behaviours releases transient rendering resources but does not destroy the old
Drawing texture: Undo or the caller may still own it. Finish/synchronize an active stroke
before recording the structural Undo snapshot, and use the existing document removal or
conversion workflow when the old owned assets should also be deleted.
Replacing a populated group with a non-group is rejected; use the editor's explicit conversion
to Drawing or ungroup it first. Conversion and live-generation completion retain the layer wrapper.

Snapshots include `behaviourMissing`. A missing behaviour does not remove the layer's name, GUID,
common settings or group children. It does not render. Its `type` is `missing`, or `group` when the
saved node still contains a group. Type-specific commands require an available behaviour.
The Layer Settings recovery action can transfer compatible saved behaviour fields; common fields
are retained directly, not reconstructed from missing-type metadata. Recovery matches a separate
behaviour identifier rather than list positions or names.

Earlier inheritance-based documents are intentionally incompatible. Keep their originals and use
the earlier package revision to render/export them. No automatic colour, naming or repeat-mode
migrations are applied to the new document model.

## Connecting

Use a running Unity Editor in Edit Mode. The plugin's optional adapter supports
`com.unity.pipeline` 0.5.0-exp.1 or later and registers these commands after scripts compile.
The core API has no dependency on Pipeline. JSON parsing uses Unity's Newtonsoft Json package.
When copying the plugin into `Assets` instead of using UPM, ensure its declared package dependencies
are already installed; an `Assets`-local `package.json` does not install dependencies automatically.

```powershell
unity status --project-path 'D:/Projects/MyGame' --format json
unity command --query whimtex --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_describe --project-path 'D:/Projects/MyGame' --format json
```

If commands are absent, check whether Pipeline is installed and the plugin is compiled. Follow the
project's rules for compilation or installation; do not trigger them automatically when prohibited.
No API command calls `AssetDatabase.Refresh`, requests script compilation, enters Play Mode or opens a scene.
Import/save commands do import the specific image or compositor asset they write.

| Command | Parameters | Result |
|---|---|---|
| `whimtex_describe` | none | Protocol, operations, enums, limits |
| `whimtex_inspect` | `assetPath` | Document revision, stable IDs, hierarchy and settings |
| `whimtex_import_image` | `sourcePath`, `assetPath` | Imported texture path, GUID, dimensions |
| `whimtex_execute` | `requestPath` | Batch result, created IDs, updated document |
| `whimtex_render` | `assetPath`, `outputPath`, optional `maxSize=1024`, `overwrite=false` | Absolute PNG path and dimensions |

Pass `--project-path` and `--format json` on every command. The API object is nested inside the
CLI/Pipeline response: check its `apiVersion` and `success` as well as transport success/exit code.
An API validation error can arrive through a successful transport. `errorCode` and `error` describe it;
`failedOperation`, when present, is zero-based (`-1` means batch/save level).

Direct C# entry points, all on Unity's main thread, return a JSON string:

```csharp
WhimTexApi.Describe();
WhimTexApi.Inspect("Assets/Art/Icon.asset");
WhimTexApi.ExecuteJson(requestJson);
WhimTexApi.ExecuteFile(absoluteRequestPath);
WhimTexApi.ImportImage(absolutePngPath, "Assets/Art/Source.png");
WhimTexApi.Render("Assets/Art/Icon.asset", "Temp/WhimTex/icon.png", 1024, false);
```

The full namespace is `DCFApixels.WhimTex`. With an existing C# eval bridge, call these methods
instead of installing Pipeline solely for this tool. Send a request file to avoid shell-escaping JSON.

## Generated image → compositor

1. Generate a PNG/JPEG using the agent's image tool, or use a user-provided local image.
2. Import it to a new asset path. Import never overwrites; for an existing imported texture, skip this step.

```powershell
unity command whimtex_import_image --sourcePath 'C:/Temp/generated.png' --assetPath 'Assets/Art/AgentIcon/source.png' --project-path 'D:/Projects/MyGame' --format json
```

The API copies only that file, preserves its dimensions up to the resource limit, disables texture
compression/mipmaps and keeps non-power-of-two dimensions. It does not change existing source importers.
PNG/JPEG only; the destination must use the same extension. No URLs or automatic image generation.

3. Write a JSON batch outside `Assets`, for example `Temp/WhimTex/create.json`:

```json
{
  "apiVersion": 1,
  "assetPath": "Assets/Art/AgentIcon/Icon.asset",
  "create": true,
  "width": 1024,
  "height": 1024,
  "save": true,
  "operations": [
    {"op": "add", "type": "group", "as": "art", "settings": {"name": "Artwork"}},
    {
      "op": "add", "type": "file", "as": "image", "parent": "@art",
      "settings": {"source": "Assets/Art/AgentIcon/source.png", "filter": "Source"},
      "transform": {"position": [20, -12], "rotation": 8, "tiling": "Clip"}
    }
  ]
}
```

4. Optionally run the same request with `dryRun:true`, then change it to false to apply:

```powershell
unity command whimtex_execute --requestPath 'D:/Projects/MyGame/Temp/WhimTex/create.json' --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_render --assetPath 'Assets/Art/AgentIcon/Icon.asset' --outputPath 'Temp/WhimTex/icon-v1.png' --project-path 'D:/Projects/MyGame' --format json
```

5. View the returned PNG. Revise the document if needed, using IDs/revision from the response or a new inspect.
The `.asset` is already usable as a texture/Sprite in Unity; this temporary PNG is only for inspection.

An initially empty File layer automatically gets Original Aspect when assigned its first texture.
Assigning a different HDR-format source sets both `colorRange` and `blendRange` to `HDR`.
Explicit ranges in the same settings object override these defaults. Reassigning the same source,
assigning a non-HDR source or subsequent refreshes do not reset the ranges.
An explicit transform patch is applied **after** this fit. Setting `scale:[1,1]` explicitly therefore
stretches a non-square source to the full canvas; omit scale to preserve the initial aspect fit.

## Batch contract

| Request field | Meaning |
|---|---|
| `apiVersion` | Required integer `1` |
| `assetPath` | Required project-relative `Assets/.../*.asset`; no overwrite on create |
| `create` | Default false. True creates a new document |
| `width`, `height` | Create only; integers, default 512 each, 1..16384 and at most 16,777,216 total pixels |
| `expectedRevision` | Required for existing documents; copy the latest inspect/execute revision verbatim |
| `save` | Default true. False keeps edits in the loaded existing document without rebaking output |
| `dryRun` | Default false. Validate the entire batch on a detached model, without strokes, rendering, saving or consuming real name counters |
| `operations` | Required array, at most 256; use `[]` to save/rebake without additional edits |

Unknown/duplicate fields, wrong JSON types, invalid enum names, non-finite numbers and out-of-range
values fail validation. Omitted patch fields are preserved; JSON null is not a reset instruction.
Property and enum names are case-sensitive. Do not pass Unity instance IDs, YAML file IDs or display names.

Each `add` can define `as:"image"`. Later operations refer to it as `layer:"@image"`, `parent:"@image"`
or `target:"@image"`. Aliases are local to one batch, must be unique and cannot forward-reference.
Persistent layer IDs are returned per operation and in `document.layers`.

`document.layers` is flat, with `parent` and sibling `index`. Index zero is visually topmost.
`settings` contains editable values; hierarchy, target and transform have separate fields/operations.
`gradientKeys` contains separate color/alpha keys, interpolation, smoothness and midpoints.
Gradient inputs accept either an ordered stop array or the object form documented in
[the gradient contract](AI/README.md); SDF defaults to Linear, other gradients to Classic.

### Operations

```json
{"op":"add", "type":"drawing", "as":"ink", "parent":"@art", "index":0, "settings":{"name":"Ink"}}
{"op":"set", "layer":"@ink", "settings":{"opacity":0.6, "blend":"Multiply"}}
{"op":"transform", "layer":"@image", "transform":{"position":[24,-10], "rotation":15}}
{"op":"move", "layer":"@ink", "parent":"", "index":0}
{"op":"target", "layer":"@outline", "input":"Specific", "target":"@art"}
{"op":"target", "layer":"@outline", "input":"Previous"}
```

- `add`: types `file`, `drawing`, `group`, `color`, `gradient`, `noise`, `shape`, `outline`, `sdf`, `normalMap`, `blur`, `makeSeamless`, `shaderProcessor`.
  Optional `parent` defaults to root, `index` to 0. `settings` and `transform` are optional patches.
- `set`: requires `layer` and `settings`.
- `transform`: requires `layer` and `transform`.
- `move`: `index` is the insertion index **after removal** from the old container; omitted parent
  or `parent:""` moves to root. A group cannot move into itself or its descendants.
- `target`: effect layers (SDF/Outline/Normal Map/Blur/Make Seamless); default input Specific. Previous means the next sibling below the effect.
  Specific targets can be groups, but cannot create a dependency cycle.
- `stroke`: Drawing only, detailed below.

### Layer settings

| Applies to | Supported keys |
|---|---|
| All | `name` (string), `enabled` / `clippingMask` (boolean), `opacity` (0..1), `blend`, `colorRange` / `blendRange` (`Standard`, `HDR`), `swizzle` (four channel names in output RGBA order) |
| Non-group | `filter` (`Source`, `Point`, `Bilinear`, `Trilinear`) |
| Group | `compositing` (`PassThrough`, `Isolated`); ranges are active only when isolated |
| File | `source` (already imported Texture2D path in Assets or Packages) |
| Color | `color` (`[r,g,b,a]`, encoded RGB -107..107, alpha 0..1) |
| Drawing | `brush` (partial brush settings below) |
| Outline | `color`, `metric`, `outlineWidth`, `outlineSoftness` (0..16384), `outlinePosition` (`Outside`, `Inside`, `Center`), `outlineOffset` (-16384..16384), `fillCenter` (bool), `fillColor` (`[r,g,b,a]`) |
| SDF | `metric`, `sourceChannel` (`Alpha`, `Red`, `Green`, `Blue`, `Luminance`), `threshold` (integer 0..255), `distancePosition` (`Outside`, `Inside`, `Center`, `Signed`), `inverted` (bool), `maxDistance` (0..16384; zero = automatic) |
| Normal Map | `normalMap`: partial settings object described below |
| Noise | `noise`: partial procedural settings object described below |
| Shape | `shape`: partial settings object described below |
| Blur | `blur`: partial settings object; `mode`: Gaussian (default), Linear or Circular; [Gaussian](#gaussian-blur-settings), [motion](#motion-blur-settings) |
| Make Seamless | `makeSeamless`: `{ "horizontal": "LeftToRight", "vertical": "BottomToTop", "blendWidth": 0.2, "falloff": 1 }`; [parameters](#make-seamless-settings) |
| Gradient, SDF | `gradient`: 1..64 `{"time":0.0,"color":[1,1,1,1]}` stops in strictly increasing time order, time 0..1 |

SDF/Outline `metric` accepts `EuclideanExact` (default), `EuclideanApproximate`, `Manhattan`,
`Chebyshev` and `EuclideanAntialiased`. The latter interpolates threshold crossings between horizontal/vertical
neighboring samples and measures distance to the closest crossing point. It approximates the continuous contour
between those points; it does not treat a wide translucent transition as subpixel coverage.
`threshold` selects the SDF contour; Outline uses threshold 128. In this mode a uniform source with no crossing has no border.
Existing metric numeric IDs 0–3 remain stable; `EuclideanAntialiased` is 4 and is reported by capabilities/inspect.
Outline filters both band edges, including at zero softness, and supports fractional widths. `outlineOffset`
translates the band in canvas pixels (negative inward, positive outward), without changing its width.
`fillCenter` defaults to false. When enabled, the region inside the band's inner edge uses `fillColor`
(default white; same encoded RGB/alpha contract as `color`). Fill and border share complementary coverage,
so opaque colors do not create a translucent seam. Zero width removes only the border, not the enabled fill.
The source's interior holes remain holes. This is an ordinary effect layer: place it below an explicitly targeted
source for a backing silhouette; above the source it can cover the original image.
PSD exports filled/offset/antialiased outlines as pixels rather than a native stroke style.
For SDF on a group, Alpha requests coverage only; Red/Green/Blue/Luminance request the group's RGBA result.
Changing SDF Source Channel therefore also changes the group's source-cache requirement.

Discover blend modes, ranges, group compositing and distance metrics with `whimtex_describe`.
Groups default to PassThrough; set `compositing:"Isolated"` to apply their own blend mode and ranges.
Group opacity applies to the complete result, not separately to every child. Group transforms/FX are rejected.
`swizzle` accepts `R`, `G`, `B`, `A`, `1-R`, `1-G`, `1-B`, `1-A`, `0`, `1`, `R * A`, `G * A`, `B * A` as strings.
Product names include spaces, matching `Describe`. All mappings read the original input RGBA:
`["R * A","G * A","B * A","1"]` multiplies RGB by the input alpha and sets output alpha to 1.
Selecting products does not change the compositor's blending convention or implicitly change output alpha.
For example, `"swizzle":["B","G","R","A"]` exchanges red and blue;
`["A","A","A","1"]` displays alpha as opaque grayscale. The default is `["R","G","B","A"]`.
It runs after FX in linear working space and before Color Range and layer blending. Output alpha
remains bounded to 0..1. Source pixels and brush settings are unchanged.
A nonidentity group swizzle forces isolated rendering. A saved Pass Through group uses Normal
blending while swizzled, then resumes Pass Through when restored to identity and not participating in clipping. Explicitly isolated
groups retain their chosen blend mode. `Describe` lists `swizzleChannels`; `Inspect` includes each
layer's swizzle, including groups.

`clippingMask` defaults to `false`. Set it to `true` on a non-Processor layer or group to clip it to the
first non-clipping sibling below; consecutive clipped siblings share that base. The relationship
is positional, never crosses a parent group, and updates after moves. A hidden, transparent or
missing base hides the chain. The base's alpha is preserved and its opacity is applied once.
Participating groups are isolated temporarily (configured PassThrough uses Normal).
`Inspect` reports `settings.clippingMask`, resolved `clippingBaseId` (null without a base), and
`isolatedByClipping` on groups. Toggling clipping does not edit source pixels or drawing strokes.

For an existing layer, use `{"op":"set","layer":"<id>","settings":{"clippingMask":true}}`
in a batch with the current `expectedRevision`. Place its base first when constructing a new
document, then add clipping layers above it. Clipped Overwrite replaces source-covered color
without erasing base alpha; outside clipping it retains full RGBA overwrite behavior.

Setting Drawing `colorRange:"HDR"` promotes storage. Standard does not downgrade it. The explicit operation
`{"op":"compact","layer":"@drawing"}` clamps/quantizes to 8-bit and switches to Standard, with native Undo.
Only use compact when the user asks to discard HDR precision. Inspect reports `storageFormat`.
Colors retain the encoded RGB convention; rendering and EXR/Texture2D output are linear HDR.
The render command writes a clamped PNG copy. See [HDR behavior](HDR.md).
Other settings of existing layers and all existing FX are preserved. The path-based batch API does not author Shader FX,
delete layers, duplicate/rasterize layers, resize an existing canvas or change gradient geometry.
These remain available in the window. Use `enabled:false` to hide an unwanted layer non-destructively.

`shaderProcessor` processes the already-composited lower stack, with HDR ranges by default.
Normal + Opacity interpolates before/after without accumulating alpha twice. Pass Through includes
the external backdrop; isolated groups limit its scope. Processor is a clipping-chain boundary.
The batch API can create/reorder it and edit its common settings, transform and Swizzle.
The [live editing API](LiveAgentAPI.md#inline-shader-fx) can author inline Shader FX code and parameters
in open documents, including unsaved ones. No separate shader asset or special layer target is needed.
All WhimTex HLSL effects and brushes automatically include [FastNoiseLite](AI/README.md#built-in-noise-library);
its noise and domain-warp functions need no explicit include.
Post FX is window-local presentation state and never changes API rendering, sampling or export.

### Shape settings

Use `type:"shape"` and partial `settings.shape` updates. `describe` returns `shapeDefaults`
and `shapeKinds`; `inspect` includes all parameters. One layer contains one editable figure.

| Setting | Values |
| :--- | :--- |
| `kind` | Rectangle (default), Ellipse, Polygon, Star, Line |
| `fill`, `fillColor` | Boolean (default true), RGBA color (default white) |
| `stroke`, `strokeColor` | Boolean (default false), RGBA color (default black) |
| `strokeWidth` | 0–8192 canvas pixels, inside the edge; default 2 |
| `roundness` | 0–1 uniform rectangle rounding shortcut; setting it assigns all four corners |
| `cornerRoundness` | Four 0–1 values, clockwise from top-left: TL, TR, BR, BL. Radius relative to the shorter half-extent |
| `linkCorners` | Boolean, default true; enables proportional corner edits in Properties |
| `sides` | 3–32 polygon sides / star points; default 5 |
| `innerRadius` | 0.01–1 star inner/outer radius ratio; default 0.5 |

API corner assignments are exact, regardless of `linkCorners`; when both rounding keys are present,
`cornerRoundness` overrides `roundness`. Inspect reports the resolved corners and the top-left value
as the scalar shortcut. UI displays rounding as 0–100%; API uses 0–1.

New shapes are centered with transform scale `[0.5,0.5]`. The untransformed bounds cover
the canvas: a 100×40 figure in a 512×256 document uses scale `[0.1953125,0.15625]`.
Set position/rotation with the existing `transform` operation. Line is a capsule whose
length and thickness are its transformed width and height. Stroke width stays in canvas pixels.
Colors follow the existing encoded-color contract; set layer color/blend ranges to HDR when needed.
Inactive type-specific settings are retained when `kind` changes. SVG import/export is not implied.

```json
{"op":"add","type":"shape","as":"badge","settings":{"shape":{
  "kind":"Star","sides":5,"innerRadius":0.45,
  "fillColor":[1,0.65,0.1,1],"stroke":true,"strokeWidth":3
}}}
```

### Noise settings

Use `type:"noise"` with partial `settings.noise` updates. `describe` exposes `noiseDefaults`,
`noiseTypes`, `noiseFractals`, `noiseCellularDistances`, `noiseCellularReturns`, `noiseWarps`
and `noiseEncodings`, `noiseDimensions`, `noiseWhiteColors`. `inspect` returns all generator parameters. No new operation or protocol version is required.

```json
{"op":"add","type":"noise","as":"height","settings":{"noise":{
  "noiseType":"OpenSimplex2","seed":1337,"scale":8,"offset":[0,0],
  "fractal":"FBm","octaves":3,"lacunarity":2,"gain":0.5,"encoding":"LinearData"
}}}
```

| Setting | Values / limits |
| :--- | :--- |
| `noiseType` | OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic, Value, WhiteNoise, BlueNoise |
| `whiteNoiseColor` | Monochrome (default), Color (independent RGB); shared by WhiteNoise and BlueNoise |
| `whiteNoiseSize` | 1–1024 canvas pixels per grain, default 1; shared by WhiteNoise and BlueNoise |
| `dimensions` | TwoD (default), OneD (straight stripes, a 2D noise slice) |
| `direction` | −180–180 degrees, default 0; OneD only; 0 varies horizontally (vertical stripes), 90 varies vertically |
| `seed` | Signed 32-bit integer; passed to the shader as an integer, not a float |
| `scale` | 0.01–1000 noise-space units across the shorter canvas side |
| `offset` | `[x,y]`, each −10000–10000 noise-space units (canvas pixels for WhiteNoise/BlueNoise) |
| `fractal` | None, FBm, Ridged, PingPong |
| `octaves`, `lacunarity`, `gain` | Integer 1–8; 1–4; 0–1 |
| `weightedStrength`, `pingPongStrength` | 0–1; 0.01–8 |
| `cellularDistance` | Euclidean, EuclideanSquared, Manhattan, Hybrid |
| `cellularReturn` | CellValue, Distance, Distance2, Distance2Add, Distance2Sub, Distance2Mul, Distance2Div |
| `cellularJitter` | 0–1 |
| `warp`, `warpStrength` | None, OpenSimplex2, OpenSimplex2Reduced, BasicGrid; 0–100 noise-space units |
| `encoding` | ColorValues (display colors) or LinearData (raw normalized values) |
| `inverted` | Boolean |

RGB repeats the normalized scalar, except WhiteNoise/BlueNoise with Color which generates independent RGB; alpha is 1.
FastNoiseLite output is remapped from signed noise to 0–1 and clamped.
WhiteNoise hashes discrete canvas-space cells with the signed integer seed. It ignores `scale`, fractal,
cellular and warp settings without resetting them; `whiteNoiseSize` controls its grain size instead.
It supports inversion, encoding and OneD direction, and keeps its grid independent of preview resolution.
BlueNoise uses WhimTex-generated periodic void-and-cluster rank tables: 128×128 RGB in 2D and
a separate 256-sample RGB sequence in 1D. Seed hashes select translations/reflections (plus axis swaps in 2D),
not an expensive runtime rebake. Each channel has a separately generated rank table. Grain coordinates,
ignored settings, encoding and inversion match WhiteNoise. Legacy `whiteNoise*` field names are retained
for both grain types. Offset Y in 1D selects a seeded variation of the sequence.
The tables use 8-bit uniform ranks; use LinearData for raw dither thresholds.
For masks/channel packing, prefer LinearData and apply the existing Swizzle/blend settings.
For a Normal Map or SDF source, add the effect above Noise and assign `Previous` or a specific target as usual.
Domain Warp uses a single warp pass; noise fractal settings affect the subsequent noise evaluation.
OneD projects aspect-correct centered coordinates onto the direction axis before offset and warp.
Offset X moves along the slice and Y selects the slice. Thus warp and fractals preserve stripe invariance.
Generation runs on GPU at the requested resolution; it is not time-animated or automatically seamless.
Seed and normalized coordinates are stable across preview/export sizes, but different GPUs may produce small
floating-point differences. Saving stores the procedural parameters through existing document serialization;
the usual baked output texture is still generated when required.

### Gaussian Blur settings

`strength` is 0–4 (default 1; UI 0–400%). Zero bypasses the blur; 0–1 mixes the source and blur in
premultiplied linear RGBA. Above 1, RGB stays unchanged and alpha becomes `a*s/(1+a*(s-1))`, with
`a` clamped to 0–1. This matches Motion Blur: denser translucent coverage, not a larger radius or RGB gain.
Radius zero remains an identity operation at every strength. Transform and FX still apply after bypass.

Use `type:"blur"` and partial `settings.blur` updates, with `mode:"Gaussian"` (default).
`describe` exposes `blurDefaults`, `blurModes`, `blurDirections` and `blurEdges`;
`inspect` returns settings for all modes, including inactive ones.
The former `gaussianBlur`/`motionBlur` API types and serialized layer classes are removed without migration.
Recreate old blur layers with the new type; request envelope version remains 1.

```json
{"op":"add","type":"blur","as":"blur","settings":{
  "colorRange":"HDR","blur":{"mode":"Gaussian","radius":32,"edges":"Repeat"}
}}
```

Assign a stable source ID (or batch alias) with
`{"op":"target","layer":"@blur","input":"Specific","target":"@source"}`.
Radius is the finite kernel extent (three standard deviations) in original canvas pixels;
0 bypasses filtering. Sources may be hidden. Groups are sampled against transparency without
changing their Pass Through setting. Layer Transform, swizzle, clipping, opacity and blend settings
apply normally to the effect. API rendering/saving uses the full-quality algorithm, never the main
window's interactive approximation. Export to PSD rasterizes this effect.
See [Gaussian Blur](GaussianBlur.md) for transparency, HDR and cache behavior.

### Motion Blur settings

Use the same `type:"blur"` and partial `settings.blur` updates with `mode:"Linear"` or `"Circular"`.
All modes share strength, edges and target; changing mode preserves radius, distance, angle, arc, center and direction.

| Field | Values / meaning |
| --- | --- |
| `mode` | `Gaussian` (default), `Linear`, `Circular` |
| `radius` | 0–256 canvas pixels; default 8; Gaussian only |
| `strength` | 0–4, default 1 (UI 0–400%); below 1 mixes with the original; above 1 increases translucent trail density without changing RGB brightness or length; fully opaque pixels stay unchanged |
| `distance` | 0–512 original canvas pixels; default 16; used by Linear |
| `angle` | −180–180 degrees; default 0; 0 points right, positive turns counterclockwise; Linear |
| `arc` | 0–360 degrees of rotation; default 15; Circular |
| `center` | `[x,y]`, each 0–1; bottom-left origin, default `[0.5,0.5]`; Circular |
| `direction` | `Centered` (default), `Forward`, `Backward`; Forward follows Angle or rotates counterclockwise |
| `edges` | `Transparent` (default), `Clamp`, `Repeat`, `Mirror` |

```json
{"op":"add","type":"blur","as":"motion","settings":{
  "colorRange":"HDR","blur":{"mode":"Circular","arc":25,"center":[0.4,0.6],"direction":"Centered","edges":"Transparent"}
}}
```

Assign the source with `{"op":"target","layer":"@motion","input":"Specific","target":"@source"}`.
Previous uses the sibling below. Hidden sources and isolated group color are supported, just as
for Gaussian Blur. Zero Distance (Linear) or Arc (Circular) bypasses filtering. Inactive-mode
settings are retained when switching modes. Transform, swizzle, clipping, opacity and blend
settings use the normal effect-layer paths. API rendering uses full quality; PSD rasterizes the effect.
See [Motion Blur](MotionBlur.md) for sampling, alpha, quality and memory details.

### Make Seamless settings

Use `type:"makeSeamless"` and partial `settings.makeSeamless` updates:

```json
{"op":"add","type":"makeSeamless","as":"tile","settings":{
  "makeSeamless":{"horizontal":"LeftToRight","vertical":"BottomToTop","blendWidth":0.2,"falloff":1}
}}
```

`horizontal`: Off/LeftToRight/RightToLeft; `vertical`: Off/BottomToTop/TopToBottom.
`blendWidth` is a fraction of each canvas dimension, 0.001–0.5 (default 0.2);
`falloff` is 0.25–4 (default 1). Directions select the source edge and destination edge.
Both Off bypass the operation. Other settings are the common targeted-effect settings:
Previous/Specific input, transform, ranges, Swizzle and FX. Hidden sources and isolated group inputs work normally.
The one-pass operation mixes mirrored RGBA samples in premultiplied space, preserving HDR RGB.
The outermost pixel centers match on enabled axes; corners combine both axis weights.
Subsequent transforms, modifiers and composition can alter this match. PSD export rasterizes the effect.
`describe` exposes `makeSeamlessDefaults`, `makeSeamlessHorizontal`, `makeSeamlessVertical`;
`inspect` includes all four parameters. Live add/complete/settings use the same type and settings.

### Normal Map settings

Use `type:"normalMap"` and put generator settings inside `settings.normalMap`. Both `add` and
`set` accept partial updates. `describe` exposes `normalMapDefaults`; `inspect` returns every
generator setting under `settings.normalMap`. Regular layer settings, targets, groups, swizzle,
clipping masks, duplication, conversion and raster export use the existing paths.

```json
{"op":"add","type":"normalMap","as":"normal","settings":{"normalMap":{
  "mode":"Texture","strength":6,"smoothing":1,"mediumRadius":4,"largeRadius":32,
  "fineDetail":1.5,"mediumDetail":1,"largeDetail":0.5,"lightRemoval":0.75,
  "edges":"Repeat","encoding":"PackedColor"
}}}
{"op":"target","layer":"@normal","input":"Specific","target":"@art"}
```

| Key | Values / limits |
|---|---|
| `mode` | `HeightMap` (default), `Texture` |
| `sourceChannel` | `Luminance` (default), `Red`, `Green`, `Blue`, `Alpha`, `Maximum` |
| `inputSpace` | `ColorValues` (default; encoded/display RGB), `Linear` (working values) |
| `strength` | 0..128, default 4 |
| `blackLevel`, `whiteLevel` | 0..1 / 0.0001..16; defaults 0/1; white must exceed black |
| `gamma` | 0.05..8, default 1 |
| `smoothing` | 0..64 full-resolution pixels, default 1 |
| `mediumRadius`, `largeRadius` | 0.5..128 / 0.5..512 pixels; defaults 4/32; large must be at least medium |
| `fineDetail`, `mediumDetail`, `largeDetail` | 0..8; defaults 1/1/0.5; Texture mode only |
| `lightRemoval` | 0..1, default 0.75; Texture mode only |
| `edges` | `Clamp` (default), `Repeat`, `Mirror`; independent of Transform tiling |
| `derivative` | `Sobel` (default), `Scharr`, `CentralDifference` |
| `inverted`, `flipX`, `flipY` | Boolean, default false |
| `ignoreTransparent` | Boolean, default true; alpha-normalized smoothing, except when Alpha is height |
| `alphaMode` | `Opaque` (default), `Source` |
| `output` | `Normal` (default), `Height` (reconstructed height for tuning) |
| `encoding` | `PackedColor` (default; PNG/TGA/PSD/display), `LinearData` (raw linear EXR/Texture2D data) |

Normals are tangent-space vectors packed into 0..1 RGB. Alpha is coverage, not a packed X channel.
Positive height gradients tilt the normal toward negative X/Y; flips reverse each respective axis.
Texture mode uses differences between smoothed height bands, not geometry or material recognition.
Its Light Removal attenuates the broad band and may remove real relief too.

A group source is rendered against transparency with its own descendants, opacity, swizzle and
clipping, without the external backdrop. Outline and Alpha-source SDF use group coverage;
SDF with Red/Green/Blue/Luminance uses the group's color result.
Like other effect layers, Normal Map processes hidden sources: `enabled:false` hides a layer's
own contribution, not its availability to Previous/Specific consumers. Hidden groups still
respect their children's visibility. This also applies to chains of hidden effect layers.
Opacity and clipping semantics are unchanged; missing and cyclic targets remain invalid.

Keep the resulting normal layer at full opacity with Normal blend, identity swizzle and no color
FX when exporting a normal texture. Color blending does not renormalize normals. Transform moves
the output image without rotating its vectors. PackedColor compensates for the compositor's LDR
gamma encoding; LinearData is the appropriate choice for raw linear output, not ordinary PNG export.
Import exported packed PNG/TGA as Normal Map with grayscale conversion disabled; see the
[Unity normal-map import reference](https://docs.unity.cn/6000.1/Documentation/Manual/texture-type-normal-map.html).
The API does not change source or exported texture import settings automatically.

### Transforms and coordinates

Transform patches support `position:[x,y]`, `scale:[x,y]`, `pivot:[u,v]`, `rotation`, `tiling`,
`reset:true`, `originalAspect:true`.

- Position is in canvas pixels: positive X goes right, positive Y goes up.
- Rotation is counterclockwise degrees.
- Pivot is bottom-left UV: `[0.5,0.5]` is the center.
- Scale `[1,1]` means the full canvas-sized source rectangle; negative values mirror, zero is rejected.
- Tiling is `Source`, `Clip`, `Repeat`, `Mirror`, `Clamp` or `Unbounded`. `Source` reads the source texture's U/V wrap modes. `Clamp` extends edge pixels. `Unbounded` continues Noise, Gradient, Color Fill and Shape calculations outside 0–1 UV; raster layers use Clip.
- `reset` is applied before the other fields, Original Aspect after them.
- Changing pivot through this API uses raw transform semantics; it does not compensate position.

### Drawing strokes

```json
{
  "op":"stroke", "layer":"@ink", "space":"canvasPixels", "erase":false,
  "brush":{"color":[1,0.2,0.1,1], "size":12, "hardness":0.8, "spacing":0.16},
  "points":[[100,100],[180,140],[240,110]]
}
```

Each stroke has 1..4096 points: one point is a dab, multiple points form a polyline. Use sparse points
for straight segments; the brush interpolates stamps. Curves can be sampled as a polyline.
The brush uses the same renderer and source-over alpha as manual painting. `erase:true` uses the eraser.
No layer selection, canvas-area selection or RGBA Preview mask is inherited from the window:
specify the desired RGBA explicitly. Rectangle/lasso coverage and the internal pixel clipboard
are temporary window tools, not serialized document data or API stroke parameters.

For pixel-aligned pencil strokes, add `"pencil":"Circle"`, `"Square"`, or `"Diamond"` to the
`stroke` operation (not inside `brush`). Omit it for the regular soft brush. Pencil uses `brush.size`
rounded to whole pixels (1–4096), a hard edge and contiguous pixel steps; hardness and spacing do not
affect pencil strokes. Colors, HDR, `erase`, layer transforms, symmetry and repeat clipping still apply.
The pencil choice is per operation and does not change the window's selected tool.

```json
{"op":"stroke","layer":"@ink","pencil":"Square","brush":{"size":3,"color":[1,0,0,1]},"points":[[8,8],[24,16]]}
```
Color alpha zero leaves no mark, including for the eraser; eraser strength otherwise follows alpha.

- `space:"canvasPixels"` (default): top-left origin, X right, Y down. The API inverts the layer's
  transform to place ink under that canvas position. This mode requires Clip tiling.
- `space:"layerUv"`: bottom-left origin in the untransformed source tile; `[0,0]` bottom-left,
  `[1,1]` top-right. Useful for repeating transforms, where multiple visible copies share one source.
- Brush size is source-space diameter in canvas pixels, before layer transform. Nonuniform scale
  stretches it, just as when painting the layer manually.
- `brush` is optional; supplied fields update the layer's saved brush settings. Missing fields retain
  their current values, including symmetry/repeat. Set `repeat:"None"`
  explicitly when a one-off unmirrored stroke is intended.
- API brush parameters remain independent of the window's shared interactive brush/color/fill
  preferences. API strokes do not read or change those preferences. Symmetry/repeat and transforms
  are layer-local and are shared by API and interactive painting.

| Brush field | Values |
|---|---|
| `color` | RGBA array: encoded RGB -107..107, alpha 0..1; Standard clamps the painting color |
| `size` | 1..4096 |
| `hardness` | 0..1 |
| `spacing` | 0.01..4, fraction of brush size (0.16 = 16%) |
| `opacity` | 0..1, default 1; caps the complete stroke, not individual stamps |
| `flow` | 0..1, default 1; multiplies each stamp's alpha before accumulation |
| `scatter` | 0..4, default 0; random disk radius in brush diameters |
| `scatterBias` | −1..1, default 0 (UI −100..100). Negative concentrates centers near the stroke; positive near the scatter disk edge. With uniform sample `u`, normalized radius is `u^(0.5 * 2^(-4 * scatterBias))`; zero preserves `sqrt(u)`, uniform by area. Applies to Random and Sobol without consuming extra random values; ignored when scatter is zero |
| `sizeJitter` | 0..1, default 0; size multiplier sampled from 1−jitter to 1+jitter, minimum one pixel |
| `rotationMode` | `Fixed` (default) or `StrokeDirection`. For texture tips, adds the source-pixel-space segment angle before jitter; +X is zero, +Y is +90°. Stationary points retain the last direction; a new stroke starts at zero. Scatter does not affect direction. Ignored without a tip texture |
| `angleOffset` | −180..180 counterclockwise degrees, default 0; constant offset added to `rotationMode` before `angleJitter`. Ignored without a tip texture |
| `angleJitter` | 0..180 degrees, default 0; offset sampled from −jitter to +jitter per stamp, added after `rotationMode` and `angleOffset`. Ignored without a tip texture |
| `flipX`, `flipY` | 0..1, default 0; per-stamp horizontal/vertical reflection probabilities in tip-local axes, before rotation. 0 never flips, 1 always flips; intermediate probabilities sample each axis separately. Ignored without a texture tip. Symmetry copies share the chosen flips |
| `randomAlgorithm` | `Random` (default) or `Sobol`; controls scatter, size, angle, tint and flip sampling. Sobol uses seven fixed dimensions (scatter angle/radius, size, angle, tint, flip X/Y) and a seeded digital shift; enabling tint or flips does not perturb scatter. The sample index continues across stroke segments, including clipped stamps, and resets per stroke; symmetry copies share a sample |
| `tintGradient` | 1..64 ordered `{time, color}` stops, like a Gradient layer; default opaque white. Differing RGB or alpha keys trigger random sampling per stamp; identical keys give a constant multiplier without consuming random samples. Reset by supplying two opaque-white stops |
| `tip` | Imported Texture2D asset path, or null for a procedural brush. The document's own output is rejected. Does not change texture import settings |
| `tipChannel` | `Alpha` (default), `Luminance`, `InvertedLuminance`, `Color`. Ordinary tips use alpha for coverage, optionally multiplied by luminance/inverted luminance. Color also multiplies painting RGB by the tip RGB |
| `proceduralMode` | `Hardness` (default) or `SdfGradient`. Used only when `tip` is null; independent of textured `tipSdf`. Procedural gradient coordinate is `radius`, where radius is distance from stamp center divided by half the brush Size: center 0, edge 1. Pixels outside the circular tip are discarded. Uses the shared `tipGradient`, including its RGB and alpha; Pencil ignores this mode |
| `tipSdf` | Boolean, default false. For a textured brush, sample `tipGradient` at `1 − selected field`. Alpha and Color use alpha as the field, without multiplying the original alpha again; luminance modes use brightness then multiply coverage by original alpha. Higher field values are inside, corresponding to lower gradient coordinates. Ignored without a texture tip |
| `tipGradient` | 1..64 ordered `{time, color}` stops. Default white with alpha 1 at 0.4 and alpha 0 at 0.6. Shared by procedural SdfGradient and textured SDF modes. Coordinates run from interior 0 to outer edge 1: RGB multiplies brush/tint RGB, alpha supplies coverage. Replaces `tipThreshold`; hardness no longer affects SDF. Inspect returns `tipGradientKeys` (separate colors/alphas). A cached 1024×2 linear RGBAHalf premultiplied LUT, clamped and mip-filtered, approximates the gradient on GPU. Editor Standard input mode removes key intensity without changing stored values; API strokes always use supplied values |
| `blend` | Layer BlendMode names except `Overwrite` and `None`; default `Normal`. Application is controlled by `blendApplication`; ignored for erase |
| `blendApplication` | `Stroke` (default) or `Stamp`. Stroke blends accumulated source-over stamps against the pre-stroke layer. Stamp blends each stamp against the evolving layer, so earlier stamps participate. Flow affects each stamp; Opacity interpolates the pre-stroke and fully accumulated results once in premultiplied linear space, without feeding that interpolation into subsequent stamps. Normal and erase retain the existing equivalent fast paths. Symmetry stamps are applied in their generated order; periodic copies of one stamp share its backdrop |
| `seed` | Integer 1..2147483647, default 1; repeatable random sequence, restarted per stroke |
| `mirrorX`, `mirrorY` | Vertical/horizontal axis reflection respectively; used only with `repeat:"Mirror"` |
| `center` | Bottom-left UV, each component 0..1 |
| `repeat` | `None`, `Mirror`, `Horizontal`, `Vertical`, `Grid`, `Radial` (mutually exclusive) |
| `repeatCount`, `repeatSecondaryCount` | Integers 2..64; secondary is the grid Y count |
| `radialStartAngle` | 0..360 degrees counterclockwise from the left; Radial only, default 0 preserves the original layout |
| `mirrorAngle` | 0..360 degrees counterclockwise around `center`; Mirror only. Rotates both axes in source-pixel space. Default 0 keeps the original vertical/horizontal axes |
| `elements` | `Copy`, `AlternateMirror` |
| `boundary` | `Continue`, `Clip` |

For simple symmetry, set `repeat:"Mirror"` and at least one of `mirrorX`/`mirrorY` to true.

Stamp spacing carries across pointer segments; adding more points on the same straight path
does not add more stamps. Random variations are shared by symmetry copies of each stamp.
Opacity applies once per API stroke; separate strokes can build past that limit.
Texture tips preserve their aspect ratio; size is the longest side. Hardness controls procedural brushes in Hardness mode only;
SDF uses its gradient. SDF fields use the normalized 0–1 range. Luminance is measured
in source-encoded RGB for sRGB textures, preserving the threshold between Gamma and Linear projects.
Pencil ignores all advanced dynamics and texture-tip settings.
Inspect returns the brush's gradient as `tintGradientKeys.colors` / `tintGradientKeys.alphas`;
write it using the `tintGradient` stop array above.

For example, `"brush":{"size":40,"spacing":0.8,"opacity":0.6,"flow":0.2,"scatter":0.5,"sizeJitter":0.3,"seed":123}`
creates repeatable scattered stamps with a 60% stroke-opacity cap.
Mirror axis choices are retained but ignored in other modes. `center` affects Mirror and Radial;
`elements` and `boundary` apply only to Horizontal, Vertical, Grid and Radial.
For reflected radial sectors, use `repeat:"Radial", elements:"AlternateMirror"`.
Legacy mirror-only settings migrate to Mirror; legacy Repeat+Mirror uses Repeat without extra mirrors.

Clip anchors to the stroke's first cell/sector and terminates the polyline at its first exit.
In Mirror mode it anchors to the starting side of each enabled, rotated axis; each
reflected brush footprint is clipped to its own half-plane or quadrant. Continue
allows crossing axes and overlapping brush footprints. Mirror without enabled axes is unrestricted.
Start a new stroke to draw in another segment. Continue permits crossing segment boundaries.
The validator caps estimated replicated stamps at 100,000 per stroke, covered brush pixels at
250,000,000 per stroke and source UV at -4..5. Documents support up to 1024 layers and
67,108,864 total owned Drawing pixels through the API.

## Validation, Undo and recovery

- Every batch is preflighted on a detached model before the live document is touched. `dryRun`
  does not prove GPU availability, successful image decoding or writable disk space.
- Existing-document changes form one Undo step, including drawing pixels. Model/paint execution
  errors before saving attempt to revert that step. Asset saves/imports and new file creation are
  **not filesystem transactions**. Empty directories or copied files can remain after I/O failures.
- Saving rebakes output. Undo restores editing state, but an already saved/baked output must be
  saved again after Undo/Redo. Use a new revision and an empty operation list to save without dialogs.
- `save:false` edits are in memory and visible to an open WhimTex; they are not a persisted output.
- A revision includes serialized state, drawing pixels, modifier state and saved asset dependencies.
  It is an opaque optimistic-concurrency token, not a portable version-control ID. Re-inspect after
  Undo, save, import or domain reload. Do not cache it across sessions.
- On `revision_conflict`, inspect and reconsider the patch. On `already_exists`, inspect/reuse the
  asset or choose a new path; do not delete it to make the request succeed.
- On a lost response/timeout, the command may already have executed. Inspect layer IDs/names and
  render before deciding what remains. Do not automatically retry additions/strokes.
- On `saveMayBePartial:true`, editing has been applied but saving failed. Inspect first. If the asset
  exists, issue a save-only batch with its current revision; don't replay the edits.
- If an import fails after copying, the response reports `fileCreated:true` and the retained path.
- If reverting a failed edit also fails, `rollbackFailed:true` reports that explicitly. Stop and inspect;
  neither the old state nor a fully applied batch can be assumed.

Destinations stay under Assets (not StreamingAssets); path traversal and write-through symlinks are
rejected. Preview PNGs are limited to `Temp/WhimTex/` and do not overwrite unless requested.
Import accepts at most 64 MiB and 16,777,216 source pixels. Resource limits protect the editor from
accidental huge requests, but effect-heavy documents can still take time: use preview resolution
appropriately and keep batches focused. The API executes on the main thread; it is not an async job queue.

## Verification

[Tests~/AgentApiSmoke.cs](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/AgentApiSmoke.cs) is an opt-in C# eval-file smoke test. After the
user compiles the plugin, run it through an available `eval_file` bridge on the intended project.
It uses a new uniquely named folder under Assets and retains its fixtures for inspection; it does
not edit existing documents. It verifies create/inspect, aspect/transform, preflight rejection,
revision conflict, painting, Undo/Redo, save and output subassets. See the test's result for its path.
Do not run it when the project's rules prohibit creating test assets.

[Tests~/DrawingPatternSmoke.cs](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/DrawingPatternSmoke.cs) is a separate opt-in eval-file
regression test for mutually exclusive Mirror/Repeat modes, legacy migration, movable mirror centers,
source stamps under the cursor and JSON round-trips. It creates no assets or GPU resources; run only
after the user has compiled the updated plugin. It does not replace visual painting checks.
