---
layout: default
title: Legacy layer JSON input
parent: Technical reference
lang: en
permalink: /reference/legacy-layer-json/
search_exclude: true
---

# Legacy layer JSON input

This reference describes only the `whimtex.layers` compatibility clipboard reader.
Do not generate new recipes from these tables: use the [current authoring guide](README.md)
and [document schema](document.schema.json). Existing legacy payloads still paste without conversion.
The [URL image fixture](../Examples/Clipboard/stone-wall-retro.json) intentionally remains legacy.
Procedural examples in the main clipboard index now use the shared format and are not examples of this contract.

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
| `input` | Targeted effects only: `Previous`, `Specific`, `AllBelow`. Defaults to Specific when target is supplied, otherwise Previous. |
| `target` | Local ID, without `@`, for Outline/SDF/Normal Map/Blur/Sharpen/Make Seamless; required for Specific, forbidden for Previous/AllBelow |
| `fx` | Any layer, including groups: array of `{ "name": "Optional name", "code": "HLSL source" }`. Optional `enabled` (default true), `gradients` mapping declared parameter names to gradient values, and `textures` mapping texture parameter names to `{ "layer": "clipboard-id" }`. |
| `url` | Drawing layers only: absolute `http`/`https` link to a PNG or JPEG, downloaded on paste |
| `asset` | File only: `{ "guid": "32 hex characters", "localId": "2800000" }`. Restores an existing Texture2D; localId is an optional signed 64-bit decimal string identifying a subasset. Missing assets produce an empty layer and a warning. Never invent GUIDs. |
| `contentOmitted` | Drawing/File only: `true` marks omitted image content and warns on paste. Cannot accompany `url` or `asset`. Layer settings and references remain intact. |

Targets may refer forward or backward in the JSON. Hidden sources still work. With no `input` or `target`,
a targeted effect uses the next sibling below it. Prefer explicit targets for predictable portable results.
With no next sibling in the pasted tree it has no source; it does not attach to an existing document layer.
No targets outside this pasted tree. No cycles, self-targeting or targets that make a group depend on itself.
`"input":"AllBelow"` reads the composited visible lower siblings on transparency, including their opacity, blend modes and effects, within the current group (also for Pass Through). An empty lower stack is transparent. It remains stack-relative when copied or pasted, so lower layers at the destination can contribute. It does not replace the effect's usual output blending. A lower dependency that refers back to this effect is rejected as a cycle.
Groups support `fx`: effects process the combined children before group opacity and blending. FX automatically isolate a Pass Through group using Normal blending, without affecting layers outside it. Removing all FX restores Pass Through unless clipping or Swizzle still requires isolation.

### Common properties

| Property | Values |
| --- | --- |
| `enabled`, `clippingMask` | Boolean. A clipping layer uses the base below its clipping chain. Processor cannot be clipped. |
| `opacity` | 0..1, **not** 0..100 |
| `blend` | `Normal`, `Multiply`, `Overwrite`, `None`, `Add`, `Subtract`, `Divide`, `Screen`, `Overlay`, `Darken`, `Lighten`, `Dodge`, `Burn`, `LinearDodge`, `LinearBurn`, `LinearLight`, `LinearLightAddSub`, `VividLight`, `PinLight`, `HardMix`, `HardLight`, `SoftLight`, `Difference`, `Exclusion`, `Negation`, `Hue`, `Saturation`, `Color`, `Luminosity` |
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
- the link is fetched **once, at paste time**; downloaded pixels are then stored in TIFF. New JSON exports omit Drawing pixels and do not retain a downloadable URL,
- a confirmation lists the hosts before any download starts. If one download fails, nothing is pasted,
- the whole tree, images included, lands as a single Undo step.

PNG and JPEG only. `properties.brush` is not available from the clipboard: pasted Drawing layers start
empty unless they carry a `url`.

**This is an input-only compatibility format.** The Layers command **Copy as JSON** writes
[the shared format](../JSON_FORMAT.md), not this envelope. Its write modes, Drawing omission,
asset references and include handling follow the current contract. No legacy writer is offered in the UI.

Legacy Color Fill accepts `properties.fillMode` (`Color`, `UV`, `Pattern`) and
`properties.fillPattern`; see [Color Fill patterns](../AgentAPI.md#color-fill-patterns).

### Shape, Color and Gradient

- **color:** `properties.color` is RGBA.
- **shape:** `properties.shape` accepts `kind` (`Rectangle`, `Ellipse`, `Polygon`, `Star`, `Line`),
  `fill`/`stroke` booleans, `fillColor`/`strokeColor`, `strokeWidth` (0..8192 pixels),
  `feather` (0..8192 canvas pixels, default 0), `featherPosition` (`Inside`, `Outside`, `Centered`, default `Centered`),
  `roundness` (0..1), `cornerRoundness` (four 0..1 values: top-left, top-right, bottom-right, bottom-left),
  `linkCorners` boolean, `sides` (integer 3..32), `innerRadius` (0.01..1).
- **gradient:** `properties.gradient` is 1..64 `{ "time": 0, "color": [1,1,1,1] }` stops,
  with strictly increasing times in 0..1. `properties.gradientOptions` optionally sets
  `type` (`Vertical`, `Horizontal`, `Radial`, `Circular`, `Diamond`, `Square`),
  `repetitions` (0.00001..1000), `wrap` (`Repeat`, `PingPong`),
  `mode` (`Classic`, `Linear`, `Perceptual`, `Fixed`) and `smoothness` (0..1). Rounded is always used.
  Geometry uses the layer's top-level `transform`: position, scale and rotation. There are no `center` or `radius` options.
  Radial, Diamond, Square and Circular are centered at local UV [0.5,0.5]; the first three reach the final stop at distance 0.5.
  Stops optionally include `midpoint` and `alphaMidpoint` (0.01..0.99, default 0.5).
  For independent tracks, use an object instead of an array:
  `{ "colors": [...], "alphas": [{ "time": 0, "alpha": 1, "midpoint": 0.5 }], "mode": "Linear", "smoothness": 1 }`.
  Each track supports 1..64 keys; times must increase. `colorSpace` is optionally `Gamma` (default) or `Linear`.
  SDF, Noise and general-purpose gradients use `Perceptual` interpolation by default. Explicit modes are preserved. `Fixed` ignores midpoint and smoothness.

### Noise

Use `properties.noise`:

| Fields | Values |
| --- | --- |
| `noiseType` | `OpenSimplex2`, `OpenSimplex2S`, `Cellular`, `Perlin`, `ValueCubic`, `Value`, `WhiteNoise`, `BlueNoise` |
| `seed`, `scale`, `offset` | 32-bit integer; scale scalar or `[x,y]`, each 0.01..1000; offset `[x,y]` or `[x,y,z]`, each -10000..10000 |
| `dimensions`, `direction` | `TwoD`, `OneD` (stripes), `ThreeD` (slice at Offset Z); -180..180 degrees in OneD |
| `periodic`, `linkScale` | `None` (default), `X`, `Y`, `XY`; linkScale boolean (default true, UI/Random All preserve the X:Y ratio; explicit API axes are applied literally) |
| `periodic1D` | boolean, default false; Seamless along the noise axis in OneD; independent of `periodic`, ignored for White/Blue and outside OneD |
| `fractal`, `octaves` | `None`, `FBm`, `Ridged`, `PingPong`; integer 1..8 |
| `lacunarity`, `gain`, `weightedStrength`, `pingPongStrength` | 1..4; 0..1; 0..1; 0.01..8 |
| `cellularDistance` | `Euclidean`, `EuclideanSquared`, `Manhattan`, `Hybrid` |
| `cellularReturn` | `CellValue`, `Distance`, `Distance2`, `Distance2Add`, `Distance2Sub`, `Distance2Mul`, `Distance2Div` |
| `cellularJitter` | 0..1 |
| `warp`, `warpStrength` | `None`, `OpenSimplex2`, `OpenSimplex2Reduced`, `BasicGrid`; 0..100 |
| `warpScale` | number or [x,y], each 0.01..1000, default [1,1]; multipliers of Noise Scale per axis, final scale = Scale × Warp Scale; Z frequency unchanged; Seamless fits the resulting periods |
| `linkWarpScale` | boolean, default true; UI/Random All preserve the X:Y multiplier ratio; explicit API axes are applied literally |
| `encoding`, `inverted` | `LinearData` (default), `ColorValues` or `Gradient`; boolean, reverses values before gradient sampling |
| `whiteNoiseColor`, `whiteNoiseSize` | `Monochrome` or `Color`; 1..1024 pixel cell size, for White/Blue Noise |
| `gradient` | Shared gradient stops/object, default black-to-white Perceptual |

Gradient output applies only to monochrome noise and supplies RGB/HDR and alpha using the same rendering path as SDF. Inverted remains available in every output mode and reverses values before palette sampling. Color White/Blue Noise temporarily treats stored Gradient output as ColorValues without losing the palette; the UI offers only Color Values and Linear Data there. Random All preserves the palette and keeps Gradient output; otherwise Output varies only between Color Values and Linear Data. Inverted can still vary. For raw masks, height maps or dither thresholds, set `encoding:"LinearData"`. Noise has no `useGradient` field; SDF also selects its output through encoding (Gradient or LinearData).

The **Seamless** UI control uses `periodic` in TwoD/ThreeD, not a `seamless` boolean: X joins left/right,
Y joins top/bottom, XY joins both. **Output** uses `encoding`. Gradient updates replace the complete
palette and do not change Output; supply `encoding:"Gradient"` to enable it. Noise has no root-level
`gradient`, `scaleY` or `offsetZ` property; use `properties.noise` with `scale:[x,y]` and `offset:[x,y,z]`.

Periodic and ThreeD support the six non-grain types, including every Fractal and Domain Warp mode.
White/Blue ignore both Seamless settings and temporarily use TwoD if ThreeD is stored.
OneD ignores `periodic` but offers `periodic1D:true`: repeat along the projected noise axis,
including Fractal and Warp. The period spans the projection of the source rectangle onto that axis;
Scale controls detail inside it. Direction 0 joins left/right, 90 joins top/bottom; other angles
need not tile at canvas edges. The OneD flag and TwoD/ThreeD edges are retained independently.
Offset Z is retained in all modes but used only in ThreeD and is never periodic. Two-value Offset
patches preserve Z. Scale scalar sets both axes; inspection returns Scale `[x,y]` and Offset `[x,y,z]`.
These shapes apply only to this compatibility reader; unified JSON uses native fields.
Periodic fits native lattice periods on selected source axes per octave/warp, with visible Scale steps
at low values (especially simplex). Transforms/FX can alter canvas seams. Random All keeps Dimensions,
Direction, both Seamless settings, both scale-chain states and linked ratios, and all Offset components (X/Y/Z).
Main Scale randomization softly favors the actual axis mean `M = (X + Y) / 2` near 8:
the previous logarithmic candidate distribution is weighted by
`1 + exp(-0.5 * log2(M / 8)^2)`, a factor between 1 and 2. Existing ranges and linked-axis
limits remain unchanged; this is a relative weighting, not a twofold cap on probabilities
of arbitrary numeric intervals. Warp Scale randomization is unchanged.
See [Noise details](../AgentAPI.md#noise-settings).

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
  `inverted` boolean, `maxDistance` 0..16384 (0 = automatic), `gradient` stops, `metric`, `encoding` (`Gradient` default or `LinearData`). SDF and Noise start with identical Perceptual palettes: black at 0, white at 1. Inverted remains available in both modes and reverses distance before Profile and palette sampling. LinearData outputs normalized distance after Inverted/Profile as raw linear RGB 0–1 with alpha 1, without gamma conversion or palette sampling. Profile applies in both modes. Switching output retains the palette, inversion and explicit gradient mode. SDF has no ColorValues mode or useGradient field.
  Also `sourceOffset: [x,y]` in document pixels (each -16384..16384), `sourceEdges: "Transparent"|"Clamp"|"Repeat"|"Mirror"` (default Transparent), and `contourOffset` -16384..16384 (positive expands). Signed mode accepts `insideDistance` and `outsideDistance` 0..16384; 0 inherits maxDistance/auto. The contour maps to 0.5, interior limit to 0, exterior limit to 1. Optional `profile` uses the curve string syntax (`"linear"`, `"easeInOut"`, `"easeIn"`, `"easeOut"`, `"one"`, or `"keys((...))"` with the same seven values per key as FX curves). In both modes, inversion precedes Profile; in Gradient, Profile precedes palette sampling. Profile output clamps to 0..1. Source Offset shifts the available input image without cropping the shifted contour before distance computation; it does not recover content already clipped by the upstream layer. Repeat considers contours across tile seams, Mirror reflects the field. Extended distance domains are limited to 64 million pixels; excessive offsets/resolutions report an error rather than silently cropping.
  SDF accepts the shared gradient object as well as stop arrays, including HDR colors and separate alpha keys.
  Supplying `properties.gradient` replaces the palette without changing `properties.encoding`.
  There is no `properties.sdf` wrapper. Inspection's `gradientKeys` is read-only naming; write its body using `gradient`.
- **metric** for Outline/SDF: `EuclideanExact`, `EuclideanApproximate`, `EuclideanAntialiased`,
  `Manhattan`, `Chebyshev`.
- **makeSeamless:** `properties.makeSeamless`: `mode` (`OffsetBlend`, default for new layers; `Mirror`; `ScreenedPoisson`; `PatchQuilting`).
  See the [complete parameter tables and UI mapping](../AgentAPI.md#make-seamless-settings) and
  [paste-ready noise recipe](../Examples/Clipboard/seamless-noise.json). Clipboard targets use a local
  layer ID without `@`; live/batch operations instead use `settings.makeSeamless` and a separate `target` operation.
  Percentage controls use fractions (`20%` = 0.2), except `quiltingFeather` (`50%` = 50).
  New layers created through the registry/API use 0.2 for both blend widths, -0.25 for both transition starts,
  and enable both Offset/Mirror Poisson corrections on AllEdges. All Offset copy edges and both Mirror
  reflection axes (LeftToRight, BottomToTop) are enabled. UI order matches the mode order above.
  UI labels distinguish Copy Edges, Mirror Direction, Patch Edges and Poisson Edges. Compensation
  amounts are labeled Strength (%); all Poisson radii are labeled Radius (%). Empty pass selections
  disable only dependent UI fields; zero Feather disables Quilting compensation controls without
  resetting values. These UI states do not restrict API setters or change the stored field names.
  Switching modes does not reset settings. Saved values are preserved; legacy document field initializers
  remain unchanged for missing fields (Mirror transition 0, Offset transition 0.325, Mirror correction false).
  Common RGBA mask: `processRed`, `processGreen`, `processBlue`, `processAlpha` booleans (all default true).
  Disabled channels are restored from the effect input in straight linear RGBA after seamless
  processing, including Mirror options, but before layer FX/transforms/swizzle/color range/blending.
  A disabled processAlpha keeps input alpha without rescaling the selected straight RGB result.
  The algorithms use input alpha for premultiplied calculations, except PatchQuilting Independent matching, which uses straight values. All four disabled bypasses
  seam processing; all enabled uses the existing path without an extra channel-selection pass.
  Mirror-only settings: `horizontal` (`Off`, `LeftToRight`, `RightToLeft`),
  `vertical` (`Off`, `BottomToTop`, `TopToBottom`), `blendWidth` 0.001..0.5, `falloff` 0.25..4.
  Mirror options: `mirrorTransitionStart` -1..0.95 (new-layer default -0.25), fade start as a fraction of Blend Width,
  independent of Offset Blend. Zero preserves the original Mirror fade; positive values delay/narrow it;
  negative values extend it beyond the canvas and may expose a seam. Poisson Correction is independent:
  enabled for new layers, but changing Transition Start does not toggle it. The same transition applies with or without contrast compensation.
  `mirrorContrastCompensation` boolean (default false), `mirrorContrast` 0..1
  (default 1), `mirrorSeamCorrection` boolean (new-layer default true), `mirrorCorrectionRadius` 0.005..0.25
  (manual default 0.05), `mirrorAutoRadius` boolean (default true). Auto uses max(0.005, blendWidth/4),
  with blendWidth clamped to its valid range, and leaves the manual radius stored for later reuse.
  Compensation reuses histogram mixing with reflected donors, reflection-paired
  stratified samples and reflection covariance, preserving Mirror's original width/falloff weights.
  Strength is a premultiplied blend of ordinary and compensated results; disabled or zero strength
  uses the original cheap shader without histogram analysis. Correction is independent and runs after
  mixing: a global ScreenedPoisson solve with independent `mirrorPoissonEdges`. Both reflection axes Off
  bypasses reflection, but enabled correction still runs. Compensation can change colors; correction can exceed output range. Neither removes
  mirrored motifs or guarantees artifact-free results. No migration or automatic rewrite of saved settings occurs.
  Offset-only copy settings: `leftEdge`, `rightEdge`, `bottomEdge`, `topEdge` booleans (all true by default).
  All false bypasses copying, not enabled Poisson correction. Without correction, multiple bands
  overlap in corners. `edgeWidth` 0.02..0.5 (default 0.2) controls each dimension's band width, minimum two pixels.
  ScreenedPoisson uses `screeningRadius` 0.005..0.25 (default 0.05) relative to the smaller dimension, source-value
  fidelity and discrete seam-slope constraints on selected pairs. `poissonEdges` (standalone),
  `mirrorPoissonEdges` and `offsetPoissonEdges` (post-corrections) are independent enums:
  `AllEdges` (default), `TopAndBottom` (vertical tiling), `LeftAndRight` (horizontal tiling), `None` (skip this pass).
  Every paired-edge selector allows clearing both pairs. `None` bypasses only its own pass, independently of other enabled passes.
  Selected axes wrap periodically; unselected axes use natural boundaries, not a second periodic seam.
  All three use global correction and can change the interior and unselected edge values.
  ScreenedPoisson ignores the four copy-edge flags and edgeWidth. It uses premultiplied linear RGBA:
  alpha is bounded on output; RGB retains HDR until normal layer/output range handling.
  Mirror settings are retained but ignored. Center preservation is approximate; highlights may clip.
  It cannot guarantee visually continuous features or remove all folds. Check Tiled preview.
  No document migration or automatic file rewrite occurs. Stored mode numbers remain unchanged:
  zero is Mirror, 3 is OffsetBlend, 4 is PatchQuilting; other values render as ScreenedPoisson. API discovery/export exposes only the
  four supported names; removed names are not accepted. For Mirror recipes specify
  `"makeSeamless": { "mode": "Mirror" }`.
  The restricted-band ScreenedPoisson path is removed without migration. Tiny sizes relax slope constraints.
  OffsetBlend copies half-period-shifted patches in selected bands. `offsetContrastCompensation`
  (default true) enables histogram compensation; `histogramContrast` 0..1 (default 1) sets its strength.
  Disabled or zero skips histogram analysis and uses ordinary copy blending.
  `offsetTransitionStart` -1..0.95 (new-layer default -0.25) is the start of the fade as a fraction of
  `edgeWidth`, not the canvas. Before it, the strip is fully copied; after it, a smooth quintic
  fade reaches the original at the end of the band. Zero starts fading at the edge. Only OffsetBlend uses it.
  Negative values place the start outside the canvas, leaving some original pixels at the edge.
  This can reintroduce a seam. Poisson Correction can join its selected edge pairs; it defaults on for new layers,
  but changing Transition Start does not toggle it.
  `offsetSeamCorrection` (default true, UI: Poisson Correction) adds global ScreenedPoisson with
  `offsetPoissonEdges`, regardless of the copy-edge mask. Disabling it can expose the seam. `offsetAutoRadius` (default true)
  uses max(0.005, edgeWidth/4). Otherwise `offsetCorrectionRadius` 0.005..0.25 (default 0.05)
  sets the radius relative to the smaller dimension. Correction increases cost and may cause HDR overshoot.
  Switching methods does not transfer their independent compensation/radius settings. No migration or old-name alias is provided.
  It uses per-channel Gaussian histogram transforms and covariance-aware blending in premultiplied
  linear RGBA; alpha is bounded and RGB is unpremultiplied on output. Pixels outside selected bands
  are unchanged apart from normal floating-point premultiplication roundoff only when correction is off. Mirror controls do not apply.
  Distribution/covariance tables are estimated from a stratified sample of at most 128x128 pixels,
  with synchronous readback, parallel NativeArray/Burst preparation and full-resolution GPU blending.
  One bounded idle workspace retains at most about 2.2 MiB of CPU pixel/table storage plus its textures,
  expires after 30 seconds, and is disposed before reload/quit. Nested evaluations use isolated owners.
  Quantile ranks are reused by sample count; a source change still rebuilds distributions/covariance.
  Unchanged results use EffectRenderCache. No persistent baked data or migration.
  Distribution preservation is approximate; colors, motifs and fine details can change.
  Use `"makeSeamless": { "mode": "OffsetBlend" }`.
  PatchQuilting uses translated boundary-strip donor search and dynamic-programming cuts, not whole-image resynthesis.
  `quiltingEdges`: `AllEdges` (default), `TopAndBottom`, `LeftAndRight`, `None` (skip quilting; independent correction can still run);
  `quiltingWidth` 0.02..0.45 (default 0.2), fraction of each corresponding dimension;
  `quiltingFeather` 0..100 percent (default 50), fraction of the maximum safe centered transition width
  calculated independently for each cut; 0 is hard, 100 uses all available width. Not a texture blur and
  not a percentage of the canvas or Patch Width. No preview-scale conversion. Existing numeric values
  are read as percentages without migration (old 16 means 16%); old appearance is not preserved;
  `quiltingContrastCompensation` bool (default false), `quiltingContrast` 0..1 (default 1, UI 0..100%):
  optional histogram-based contrast restoration within Feather transitions, using selected-donor covariance.
  Uses existing analysis readback, persistent NativeArray/Burst scratch with cached quantile ranks within the workspace budget, and
  one extra Gaussian GPU pass per processed axis. Disabled, zero strength and zero Feather skip the work.
  Pure source/donor samples are preserved per pass; with both axes, the compensated first pass feeds the
  second search and may change its donor. These settings invalidate the raw quilting cache. No migration;
  `quiltingQuality`: `Draft`, `Normal` (default), `High`, with analysis limits 96/160/256 per axis and 8/24/48 candidates;
  `quiltingAlongSearch`: 0..0.25 (default 0, UI 0..25%), maximum along-strip donor displacement
  relative to usable strip length. Displacement tapers to zero at strip ends without wrapping;
  this is a slight deformation, not a rigid cyclic translation. Nonzero splits the same candidate
  budget between straight and shifted strips and costs more to evaluate. Zero preserves original search.
  Matching derivatives and compensation covariance follow shifted samples; the setting invalidates the raw cache;
  `quiltingSeed`: signed 32-bit integer (default 0), repeatable at the same source/resolution/settings;
  `quiltingChannels`: `Linked` (default: common donor/cut in premultiplied linear RGBA) or `Independent`
  (separate straight-value searches for checked channels, intended for packed data, not transparent color).
  Search uses selected channels only. Linked premultiplication still depends on alpha.
  `quiltingSeamCorrection` boolean (default false), `quiltingPoissonEdges` (`AllEdges`, default; `TopAndBottom`, `LeftAndRight`, `None`),
  `quiltingCorrectionRadius` 0.005..0.25 (default 0.05, fraction of the smaller dimension).
  Correction is global and independent of quiltingEdges; it may alter the center and overshoot RGB range.
  Without correction, the center outside the bands is preserved up to premultiplication roundoff.
  Search batches all selected channels and cut sides into parallel Burst jobs with precomputed
  oriented values/derivatives. Duplicate rounded donors share calculations, but retain their original
  multiplicity/order for seeded selection. One scratch workspace reuses NativeArray buffers and readback/path
  textures across evaluations, with a 64 MiB retention cap and release after 30 seconds idle, domain reload or quit.
  This scratch pool is separate from the result-cache budget. GPU readback and job completion remain synchronous.
  EffectRenderCache also stores the pre-Poisson result in FP32 within its existing memory budget;
  correction toggle/radius/direction changes reuse it. Source, size, scale, edge, width, Feather,
  quality, seed, Along-Seam Search, compensation or selected-channel changes invalidate it.
  The second-axis cut fixes a common endpoint/guard to preserve the first join; this is a constrained,
  not globally optimal cyclic cut. Bands with fewer than four analysis samples use centered cuts and
  still apply percentage Feather at full resolution. A one-pixel output band copies directly; very few
  output samples can make different percentages look identical. Reduced previews may
  choose different donors/cuts. Ordinary window preview is capped at 512 pixels, independently of Live Quality;
  selecting Pencil without painting provides full-resolution preview. Verify Tiled output at that resolution
  before export. Higher quality is slower but does not guarantee fewer visible artifacts.
  No baking, migration or parameter transfer from other methods. Default method remains OffsetBlend.
- **normalMap:** `properties.normalMap`: commonly `mode: "HeightMap"`, `strength` 0..128,
  `sourceChannel` (`Luminance`, `Red`, `Green`, `Blue`, `Alpha`, `Maximum`), `smoothing` 0..64,
  `flipX`/`flipY` booleans. All advanced options and exact ranges are in the [schema](layers.schema.json).


## Legacy shader restrictions

Legacy clipboard HLSL allows conditional/define directives and literal built-in includes only:
`UnityCG.cginc`, `Packages/com.dcfapixels.whimtex/src/Shaders/ThirdParty/FastNoiseLite.hlsl`,
and `Packages/com.dcfapixels.whimtex/src/Shaders/Dither.cginc`.
Macro includes, `#include_with_pragmas` and texture asset GUID declarations are rejected.
Each FX is limited to 64 KiB UTF-8 and 32 parameters. Source restrictions and malformed legacy
parameter/binding declarations still reject the input. Once parsed, compilation errors are soft:
paste warns, retains the code and skips the failed FX, as with unified JSON.
For the shared shader language see [HLSL interface](README.md#hlsl-interface--shader-only-or-inside-json).
