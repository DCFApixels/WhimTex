---
layout: default
title: AI authoring contract
lang: en
permalink: /ai-authoring/
description: "Generate editable WhimTex documents and layers using unified JSON and HLSL effects."
---

# WhimTex AI authoring: JSON documents and HLSL effects

Use **`whimtex.document`, version 3** for new documents, selected layers and clipboard recipes.
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
- Use editable layers for distinct parts. Prefer Shape, Text, Gradient, Noise and targeted effects;
  use HLSL only where a custom algorithm is useful. Name layers and groups meaningfully.
- Use exact model fields and case-sensitive enum names from the schema. Do not put live API
  `settings`/operations or legacy `type`/`properties` objects into document JSON.
- Layer arrays are **top to bottom**; `fx` execute **first to last**.
  Give every layer a unique nonempty `id`. IDs may be descriptive local strings;
  paste remaps them and internal references to fresh document IDs.
- Omit values only when the version-3 default is intended. Do not infer storage defaults from
  a current UI factory or a slider range. Use FullOptimized by default; Full or Compact on request.
- Do not invent asset GUIDs or paths. For an existing project asset, use its verified `$asset`
  identity. Drawing pixels and remote image URLs are not stored in this format. Text layers use
  `fontFamily` for an installed system font, not a project Font asset. Save manages their bounded
  appearance backup; do not invent `fallbackPng`/`fallbackKey` values or promise portability of font files.
- Keep shaders self-contained and deterministic; avoid unnecessary layers, huge blur radii and
  expensive loops. Preserve alpha unless the requested effect intentionally changes coverage.
- For spatial noise in FX or brush tips, consider [built-in FastNoiseLite](#built-in-noise-library)
  before writing a noise implementation. Prefer it when suitable; custom algorithms remain valid.
  Built-in helpers are available in self-contained code without an include.
- If an error is reported, return a corrected complete object using its field path/message.
  Re-pasting inserts another copy; it does not update the previous paste.

## JSON envelope

```json
{
  "format": "whimtex.document",
  "version": 3,
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
        "encoding": "Linear"
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
| `fx` | FX with `$type: "ShaderFX"`, `code`, `parameters`, `active`, optional `$name` |
| `children` | Group children in top-to-bottom order |

`$type` is an allowlisted model name, not an arbitrary assembly-qualified type.
For instance, Color Fill uses `ColorFillLayerBehaviour.storedColor`, not `properties.color`.
Groups use `GroupLayerBehaviour`, `group: true`, `children` and layer-level `compositing`.
They can carry their own FX; FX process the combined children before group opacity/blending.

For targeted effects, use `behaviour.inputMode` and `behaviour.targetLayerId` with IDs from
this tree. `Previous`, `Specific` and `AllBelow` have their usual stack semantics;
include required sources and clipping bases when copying a selection. Do not use `@aliases`.
Every FX model instance owns its parameter values and enabled state in the document. Reuse a marked
HLSL preset source or write inline HLSL; supply independent FX models for separate effects.
JSON `fx` items may only be `ShaderFX` model objects, `null`, or internal `$ref` references to
`ShaderFX` models in this document. `$id`/`$ref` are serialization identities, separate from layer
IDs; a `$ref` refers to the same model instance. `$asset` is not allowed as an FX list item.
External Material, Shader and ShaderFX `.asset` FX references are unsupported in JSON/TIFF
and are explicitly rejected, even when unresolved, with
`External FX references are no longer supported. Use a marked .hlsl preset or inline HLSL code.`
There is no migration or source-document rewrite.
This restriction does not affect texture asset parameters or same-document layer bindings.

Native vector/color values follow the schema: Unity vectors and RGBA colors are numeric arrays,
while transform `Double2` values are objects with `x` and `y`.
Do not transfer the legacy array-based transform syntax or live API convenience setters unchanged.
Numbers and booleans are not quoted strings. Enum names are case-sensitive.

### Noise, gradients and SDF

Noise settings are directly in `behaviour`, not `properties.noise`.
`scale`, `scaleY` and `scaleZ` store explicit axis scales; omitted fields use frozen version-3 defaults (`8`, `8`, `1`), not another axis.
`warpScale` and `warpScaleY` are per-axis multipliers of the main scale.
`offset` is `[x,y,z]`; Z is used by `ThreeD` slices.
Seamless uses `periodic: "None" | "X" | "Y" | "XY"` for TwoD/ThreeD, and `periodic1D`
for OneD. White/Blue Noise ignore Seamless and do not have a true ThreeD variant.

Noise Output uses `encoding` (`Linear`, `Gamma`, `Gradient`), with
`gradient` storing the palette and `inverted` reversing the value before palette sampling.
Color White/Blue Noise does not use Gradient output.
SDF uses `SDFLayerBehaviour.encoding`: `Gradient` (default), `LinearData` or `MultiChannelData`.
SDF and Outline share `metric`: `EuclideanAntialiased` (Round AA, default, numeric ID 4),
`Sharp` (5), `Bevel` (6), `Diamond` (7), `Square` (8), `MTSDF` (9), `AAEDT` (10,
UI **AAEDT + 8SED**, experimental), `Contour` (11, UI **Contour AA**, experimental),
`CornerFit` (12, UI **Corner Fit AA**, experimental) and `Spring` (13, UI **Spring Simulation**, experimental).
All ten are antialiased;
the old numeric values 0–3 are removed without aliases. Sharp and Bevel are contour-offset fields,
not Euclidean SDFs. Diamond uses L1 and Square uses L∞; both depend on the calculation axes.
The shared `miterLimit` float has range 1..64, defaults to 4, must be finite and affects Sharp,
CornerFit and Spring. For the new modes it bevels excessive AA-verified joins within their finite
region of ownership; it does not validate uncertain corners.
SDF alone adds experimental `contourMergeDistance`: a finite float in 0..2 inclusive, default 0 (off),
in current raster/render pixels, independent of distance remap width and not divided by render scale.
It applies only to Sharp/MTSDF, including MultiChannelData; other modes retain but ignore it and
still require a valid value. The UI shows **Contour Merge (px)** only for Sharp/MTSDF.
One pass merges disjoint pairs of consecutive vertices within one contour to their midpoints,
shortest eligible pairs first. Pair distance is bounded by the requested value; each original vertex
is used at most once. It does not build multi-vertex clusters or merge different loops. A topology
conflict rolls back the whole batch. Existing safety protections remain active. It may shift short
details and does not guarantee restored sharpness.
Round AA keeps its original crossing-seed Euclidean distance transform (EDT), rather than distance
to continuous contour segments. Sharp, Bevel, Diamond, Square and MTSDF share reconstructed,
normalized subpixel contours.

`CornerFit` starts with raw Contour geometry, fits sides over multiple pixels and verifies proposed
corners by local AA re-rasterization. `Spring` first applies automatic spring relaxation to that raw
contour, then uses the same AA-tested proposals. Both reject intersecting or unconfirmed extensions,
retain raw geometry where ambiguous and allow bounded sharp joins only at AA-verified corners.
These are raster-only scalar modes using the existing downstream controls: Gradient/LinearData
are supported; MultiChannelData remains MTSDF-only. `contourMergeDistance` is retained and validated
but ignored. SDF's threshold is supported through coverage remapping of the selected channel;
Outline uses fixed byte threshold 128. Fitting is approximate for soft or noncoverage masks.
CornerFit is an experimental hybrid, with no guaranteed sharp recovery. Spring adapts ideas from
[Matusovic, Parakkat and Eisemann (2023)](https://doi.org/10.1111/cgf.14743), whose paper concerns
interactive depixelization, not SDF generation. It is not the full interactive tool or a C++ replica.
The previous eight modes and the Round AA default are unchanged.

`Contour` uses raw marching-squares threshold crossings on alpha or the selected source channel,
with linear interpolation to subpixel endpoints, and measures Euclidean distance to the finite
straight segments. It performs no contour fitting, smoothing, simplification, corner recovery,
merge/union, miter extension or MSDF channel construction. Expanded corners round. It measures
the current raster threshold boundary and cannot restore detail lost during rasterization.
Contour AA is a separate algorithm, not an AAEDT modification or a sharp-miter mode.
The existing source-channel, threshold, `SourceEdges`, offset and rendering wrapper still apply;
SDF exposes the threshold and Outline uses fixed byte threshold 128. `miterLimit` and SDF
`contourMergeDistance` remain validated but are ignored. SDF supports Gradient/LinearData;
MultiChannelData remains MTSDF-only. Quality comparison is experimental, with no guaranteed
improvement across all inputs. Round AA remains the default at ID 4; AAEDT stays ID 10 and
the existing modes are unchanged.

`AAEDT` estimates the raster boundary with an anti-aliased Euclidean sweep-and-update transform
and 8-neighbor vector propagation, based on Stefan Gustavson's `edtaa3` reference. It uses a
portable Burst job. Input alpha is assumed to represent the area coverage of a crisp edge
(box-filter sampling); blur, feather and arbitrary opacity do not describe exact geometry.
The same assumption applies when selecting another source channel. Wide external contours
remain round. This mode does not extend sharp/miter corners or reconstruct contour segments;
it ignores `miterLimit` and SDF `contourMergeDistance`, while retaining their validated values.
Round AA remains the default, unchanged.

The sweep keeps one candidate seed per pixel and is an approximate transform, not an exact
or rotation-invariant solver. Its local update tolerance is 0.0001 pixel (tighter than the
reference's 0.001); this is not a global distance-error bound.

For `AAEDT`, SDF's byte `threshold` is adapted to coverage before distance estimation. With
source coverage `a` in 0..1 and `t = threshold / 255`, for `0 < t < 1` use
`c(a) = 0.5*a/t` when `a <= t`, otherwise `c(a) = 0.5 + 0.5*(a-t)/(1-t)`.
This monotonic piecewise remap sends `t` to 0.5 and preserves 0 and 1. Thresholds 0 and 255
use binary classification instead: coverage is 1 only when the selected byte-range source value
exceeds the threshold, otherwise 0. Threshold 0 keeps nonzero samples; 255 leaves no covered samples.
Outline uses fixed byte threshold 128. AAEDT supports
Gradient and LinearData; MultiChannelData still requires MTSDF. The experimental mode is
provided for comparison without a claim of verified accuracy or performance superiority.

For MTSDF, RGB stores corner-preserving signed perpendicular/pseudo distances; alpha stores
true signed Euclidean distance to the reconstructed subpixel contour, not to a binary bitmap.
Gradient and LinearData reconstruct the scalar using the RGB median, then apply Position,
normalization, Inverted and Profile; Gradient also samples `gradient`. LinearData has opaque alpha.
`MultiChannelData` requires `metric:"MTSDF"` and encodes each raw linear RGBA channel as
`saturate(0.5 + 0.5 * d / maxDistanceNormalization)` (negative inside, positive outside, zero at 0.5;
zero normalization selects the automatic range). Alpha is data, not transparency.
Position, Contour Offset, separate inside/outside ranges, Inverted, Profile and the palette do not remap these channels.
Sharp, Bevel, Diamond, Square and MTSDF first fit straight flanks to multiple raw isocontour samples using
orthogonal least squares. Recovery requires reliable flanks and bounded deviation: sampled
flank deviation is at most 0.6 pixels. Acute tips may use independently straight
16-pixel flanks agreeing with the base 8-pixel fits; these take priority over short fits.
Only their tiny AA cap has a `sqrt(0.5)`-pixel engineering allowance; tip displacement
stays within measured base support when `sqrt(0.5) px / cosHalfTurn` fits in it.
Otherwise `min(2 px, 0.6 px / cosHalfTurn)` applies. This is not an exact raster inverse.
Enclosed holes are protected before
union by rejecting a conflicting local corner or simplification. Negative rings outside
all filled outlines are separate NonZero components, not enclosed holes.
Supported concave fits may minimally shift one wall outward within the existing
0.6-pixel envelope to preserve a nearby hole, then recheck finite-tip/topology bounds.
A merged convex AA cap receives one bounded enclosure pass using arc samples and
exact union vertices. Tiny closed components (perimeter <4 pixels) keep their finite
geometry but do not generate remote miter/pseudo extensions. Sharp/MTSDF also require
2 pixels of straight support on both sides of an endpoint after union (direction dot >=0.995).
Angular amplification has a separate safety cap of 4, approximately a 29-degree ideal tip.
Unsupported or more acute endpoints use finite segment distance. Requested `miterLimit`
values above 4 remain stored but cannot override that cap. Such corners may round.
Sharp selects the nearest finite owner within each contour before applying an angular
join, then combines contour fields; unrelated islands cannot cause a premature owner switch.
Uncertain corners are preserved. All five segment modes then use fixed 0.25-pixel simplification;
paths with perimeter below 4 pixels bypass simplification. Closed filled contours are normalized
by NonZero union at 1e-6-pixel precision before generating joins or RGB edge colors.
Holes and genuinely separate islands remain; regions overlapping after reconstruction share one
filled boundary. Open and zero-area threshold contours bypass filled-region union.
Post-union cleanup uses a 0.05-pixel budget and rejects replacement chords crossing nonincident
original or replacement boundaries or changing contour containment/contact states. MTSDF resolves each contour's RGB median before combining
contours, avoiding cross-contour channel mixing; alpha remains nearest true Euclidean distance.
Away from the contour, a common-channel bound certifies safe RGB interpolation. Suspect
cells receive nine checks against finite contour distance. Geometric sign/amplification
errors fall back to true distance; color-only incompatibility retains the scalar median.
Alpha is unchanged. Scalar MTSDF uses the corrected RGB median. Near-boundary texels are unchanged;
their neighbouring cells may interpolate them with corrected far texels.
Recovery has an angle- and support-dependent tip allowance and can alter subpixel detail; the result is not an exact
original matte. The true distance in alpha refers to the
reconstructed contour, not an exact original continuous curve. API patches reject a mismatched metric/encoding pair.
The UI switches Multi Channel Data to Linear Data when choosing an algorithm other than MTSDF.
For raw RGBA composition/export, set the layer's native `blendMode:"Overwrite"`; Normal composition
treats alpha as coverage and can alter the data. Ordinary FX/effect-layer inputs reading the standalone
SDF layer receive its rendered RGBA channels; group/AllBelow inputs use the composed image.
Use linear document output and preserve RGBA when exporting. Live settings use `blend:"Overwrite"`.
SDF has no Gamma output mode.
Document JSON stores `maxDistanceNormalization`; live patches use `maxDistance`.
See the [shared SDF/Outline field contract](../AgentAPI.md#layer-settings) for mode semantics,
but use native behaviour fields here, not live patch wrappers.
For document gradients use the schema's native `WhimTexGradient` model;
the standalone clipboard format below is a separate value contract.
Gradient layers additionally support `gradientType:"Freeform"` and `freeform.points`
with local `position`, straight encoded HDR `color` and relative `influence`.
`freeform.colorMode` chooses `Linear`, `Gamma`, `Perceptual` (OKLab, default) or `Hue` RGB
mixing; alpha is weighted independently. Figure fills and strokes choose their own model.
See [Freeform document fields](../JSON_FORMAT.md#freeform-gradients) and
[shared editing settings](../AgentAPI.md#freeform-gradient-settings).
Gradient layers also accept `gradientType:"Mesh"` with a complete `mesh` Bézier
lattice with `nodeModes`: one `Smooth`/`Corner` entry per node, or `null` for all Smooth. Its `colors`
are working-space coefficients, not ordinary encoded RGBA; retain all controls,
node modes, signed HDR and alpha overshoot. Positions are finite local coordinates;
folds and overlaps are allowed. Normalized color/opacity blending is followed by
smooth reconstruction across the whole overlap using surrounding colors and boundary
variation, with a narrow fading transition band around the overlap. It retains HDR,
leaves pixels outside the overlap and its band unchanged and does not accumulate alpha. Mesh is not a Shape/Vector/Text
paint mode. Mesh also accepts `colorMode:"Hue"`, with OKLab/alpha `colors` and a required
`hueChroma` scalar lattice of the same length. Retain both lattices on subdivision or replacement.
Both Freeform and Mesh Hue favor short circular transitions and smoothly fall back to
Perceptual where hues conflict; neither exposes `huePath`.
See [Mesh document fields](../JSON_FORMAT.md#mesh-gradients) and
[Mesh editing settings](../AgentAPI.md#mesh-gradient-settings).
Shape and Vector/Boolean Group styles support `fillMode:"Freeform"` with `freeform`,
and `strokeMode:"Freeform"` with independent `strokeFreeform` points. Text excludes Freeform.
Shape and Vector figures share `fillMode`: None, Solid, LinearGradient, RadialGradient,
SquareGradient, AngularGradient. Use native `gradient`,
`gradientStart`, `gradientEnd` and `gradientRepetitions` fields; Shape's `fill` boolean remains the overall
gate. Anchors are normalized layer-local coordinates. Shape measures them in current
canvas pixels before Transform; Vector uses `referenceSize`. Angular advances
counterclockwise from the start-to-end ray, with a seam there. `gradientRepetitions`
is a finite float of at least `0.0001`, default `1`, applied only to AngularGradient
before palette wrapping; one period spans `360 / gradientRepetitions` degrees.
Equal anchors give `t=0`. See the complete
[figure-fill contract](../JSON_FORMAT.md#figure-fills). Independent stroke paint uses
`strokeMode`, `strokeGradient`, `strokeGradientStart`, `strokeGradientEnd` and
`strokeGradientRepetitions`, with the same modes and palette behavior. Defaults are
Solid, a separate black-to-white Perceptual/Balanced/Clamp palette, `[0,0]`, `[1,0]` and `1`.
`stroke:false` or `strokeMode:"None"` disables painting while preserving settings.
Anchors must be finite and repetitions finite and at least `0.0001` even while
inactive; a gradient mode requires a non-null palette. Boolean Group stores these
fields in `style` and uses its own `referenceSize`. Inactive fill and stroke
settings survive every JSON write mode.
For algorithm behavior see [Noise](../AgentAPI.md#noise-settings) and
[Make Seamless](../AgentAPI.md#make-seamless-settings), but use document-schema field shapes,
not the live API patch wrappers from those pages.

### Editable text

Text uses `TextLayerBehaviour`, with editable `text` and an installed `fontFamily`.
An omitted or empty family uses the machine's default font. Use a verified installed family for
a particular appearance; font files are not included. Save prepares the appearance backup.
For a fitted label, use Frame layout with `autoSize`, its minimum `fontSize` and maximum
`maxFontSize`. Spacing fields are in em; `characterHorizontalScale` changes width and advance.
`edgeMode` is `HintedAA` by default: antialiased glyphs with pixel-grid fitting for small text.
Choose `Antialiased` for smooth glyphs without hinting, or `Pixel` for binary glyph coverage,
snapped placement and nearest sampling by default. Explicit filters and FX can still soften Pixel
output; it does not quantize the final alpha. The mode is included in the saved-appearance key.

```json
{
  "format": "whimtex.document",
  "version": 3,
  "document": { "width": 256, "height": 128 },
  "layers": [
    {
      "id": "label",
      "layerName": "Label",
      "behaviour": {
        "$type": "TextLayerBehaviour",
        "text": "ENERGY",
        "fontStyle": "Bold",
        "edgeMode": "HintedAA",
        "layoutMode": "Frame",
        "frameSize": [192, 80],
        "fontSize": 24,
        "maxFontSize": 64,
        "autoSize": true,
        "wrapping": "Words",
        "overflow": "Ellipsis",
        "spacing": { "character": 0.03 },
        "color": [0.2, 0.8, 1, 1]
      }
    }
  ]
}
```

Point layout uses fixed `fontSize`; Frame settings are retained for later edits.
`layoutOffset: [x,y]` translates the layout in local document pixels before rasterization
without editing the layer Transform (X right, Y up; finite ±1000000 per axis, default `[0,0]`).
Point/Frame layout handles edit this offset; switching Point/Frame preserves it.
Frame resize changes `frameSize` and the local offset together, leaving the layer Transform unchanged.
For defaults, limits and missing-font behavior see [Text and system fonts](../JSON_FORMAT.md#text-and-system-fonts).

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
inserts nothing. Repair the code and Compile to resume the effect. Broken internal references still fail.

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
compile and compare their renders before and after saving/reopening.

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
| `canvas`, `type`, `name`, `properties` from legacy recipes | `document`, `behaviour.$type`, `layerName` and native fields |
| `scale: [8,12]` for Noise | `scale: 8, scaleY: 12` in its behaviour |
| A remote Drawing `url` or Base64 pixels | TIFF for Drawing pixels, or a verified File asset reference |
| `opacity: 80` for 80% | `opacity: 0.8` |
| ShaderLab, GLSL `mix`, invented helpers | HLSL `ApplyFX`, `lerp`, documented helpers below |
| Live FX operations (`op`, `presetId`, `set`) inside stored content | `fx[]` with `$type: "ShaderFX"`, `code` and native parameter values |

## Upgrading old clipboard data

The `whimtex.layers` reader is removed, and version-1 documents are unsupported.
Use a matching older checkout for old data; this checkout has no automatic migration.
Plain image URL paste, brush-tip URL downloads and standalone gradient presets remain available.
See the [current format and unsupported-input boundary](../JSON_FORMAT.md).

## Standalone gradient JSON

For a gradient field (not canvas layer paste), right-click the field or its editor strip
and choose **Paste**. **Copy** produces this independent value:

```json
{
  "format": "whimtex.gradient",
  "version": 2,
  "gradient": {
    "colorMode": "Perceptual",
    "interpolation": "Balanced",
    "huePath": "Shortest",
    "chromaCorrection": 0,
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

The body alone or a bare color-stop array is also accepted, optionally inside one JSON
code fence. Each track requires 1..64 keys with strictly increasing times in 0..1. Colors
are RGBA arrays; standalone RGB accepts finite values from -65504 to 65504, alpha 0..1.
If `alphas` is omitted, alpha components of the colors form the opacity track.
The live layer/brush API retains its own RGB limits; this clipboard range does not widen them.

| Field | Values | Default |
| --- | --- | --- |
| `colorMode` | `Linear`, `Gamma`, `Perceptual`, `Hue` | `Perceptual` |
| `interpolation` | `Blend`, `Balanced`, `Smooth`, `Adaptive`, `Fixed`, `Custom` | `Balanced` |
| `customColorMode` | Same values as `colorMode`; required with nonempty `customKeys` | No curves |
| `customKeys` | Independent channel keys, described below; retained while inactive | No curves |
| `huePath` | `Shortest`, `Longest`, `Increasing`, `Decreasing` | `Shortest` |
| `chromaCorrection` | Finite 0..1 | 0 |
| `smoothness` | Finite 0..1 | 1 |
| `wrapMode` | `Clamp`, `Repeat`, `Mirror` | `Clamp` |
| `colorSpace` | `Gamma`, `Linear`: encoding of stored key components only | `Gamma` |

Pattern palette inputs explicitly default to Linear color mode; other settings use the table.
All settings survive copying, serialization and switching to a mode that does not use them.
`colorSpace` describes storage, not the selected color model. Color modes operate in linear
RGB, encoded RGB, OKLab and OKLCH respectively. Each interval between color keys uses one
shared RGB range: `min(0, A.r, A.g, A.b, B.r, B.g, B.b)` to
`max(1, A.r, A.g, A.b, B.r, B.g, B.b)`. RGB is clamped after color conversion and chroma
correction, with both bounds and the result in the same encoded RGB space, before output
decoding. This preserves signed HDR keys while bounding intermediate overshoot; ordinary
0..1 keys produce 0..1 RGB. Bounds expand continuously with the keys and are not per-channel.
Exact keys and Fixed retain their values. Alpha is an independent scalar curve using the same interpolation;
color-mode conversions, hue direction and chroma correction never apply to alpha.

Blend interpolates each neighboring pair with its midpoint and blends linear versus eased
weights using Smoothness. Balanced inserts midpoint nodes and uses monotone cubic interpolation
with shaped boundary spans; Smoothness controls its contribution. Smooth uses a fixed 17-value
easing table. Adaptive prepares a curve using neighboring keys; its midpoint changes the curve
shape and is not a guarantee of a half-color sample. Smooth and Adaptive retain but ignore
Smoothness. Curve preparation is cached and shared by Evaluate/Bake consumers.

`midpoint` is stored in 0.01..0.99, default 0.5; the final key has no following interval.
Blend and Smooth evaluate it within 0.05..0.95. Hue Path affects only Hue mode; achromatic
regions avoid arbitrary hue spins. Chroma Correction reduces opposing chroma with the
physical-interval envelope `[4u(1-u)]^3`; it is independent of alpha and Smoothness.
Fixed ignores midpoint, Smoothness, Hue Path and Chroma Correction. It keeps the established
step convention: choose the first key whose time is greater than or equal to the wrapped
coordinate, using the last key beyond that track. Color and alpha tracks are selected separately.
Clamp/Repeat/Mirror apply before evaluation; Repeat does not make different endpoint colors seamless.

The current clipboard envelope is version 2. Version 1, retired `mode`/`transition`, old
prefixes, color objects, numeric enums, unknown/duplicate fields and unsupported versions
are rejected, without migration or rewriting source files. The input limit is 262,144 characters.
The standalone envelope differs from the internal gradient object in `whimtex.document` v3.
User presets store it in `<user presets folder>/Gradients/<GUID>.json` (256 KiB maximum).

### Custom channel curves

`Custom` stores four independent channel curves. `colors`, `alphas` and `colorMode` always
describe the retained automatic gradient, including when Custom is selected; never replace
them with sampled or projected Custom colors. `customColorMode` describes the manual curves:
Linear/Gamma use R/G/B, Perceptual uses OKLab L/a/b, Hue uses OKLCH L/C/H (hue in degrees).
Channel 3 is alpha in every mode. `colorSpace` remains the output/key encoding, not the
manual curve's color model. Custom does not apply midpoint, Smoothness, Hue Path or Chroma Correction.

Each `customKeys` entry is an object such as
`{"id":1,"channel":0,"time":0,"value":0,"inTangent":0,"outTangent":0,"tangentMode":"Auto"}`.
`id`, `channel`, `time`, `value` and `tangentMode` are required in API/clipboard input;
`inTangent` and `outTangent` default to zero. IDs are unique positive 32-bit integers.
`channel` is 0..3. Times are 0..1, strictly increasing within each channel with spacing
at least 0.000001. Each channel has 1..64 keys; the union of times of channels 0..2 has
at most 64 distinct positions, and alpha has at most 64. Values and tangents are finite
floats; alpha values stay in 0..1. Signed/HDR curve values are permitted.
Combinations whose encoded or linear RGB at a color-key position cannot fit a finite float
are rejected before replacing the previous manual data; there is no separate fixed cap on
the working-space channel values.

Keys are independent; there are no persistent links. The obsolete `link` field is rejected,
including `link:0`, without migration. Markers are derived by sorting all channels together
by time, then ID, and collecting keys within 0.00001 of the earliest remaining anchor.
This is not transitive: times 0, 0.000009 and 0.000018 produce two clusters. Color/alpha
tracks filter inside these same clusters and choose the first matching key, never regroup
filtered data. The combined track chooses the first key regardless of channel. A marker
does not merge or average the underlying values/times and may contain same-channel keys.
Curve edits move/remove only the addressed key; marker gestures freeze their ID set at
gesture start and move it with a common delta limited by 0..1 and unselected same-channel
neighbours. Deletion must keep every channel nonempty. See the exact atomic movement,
rounding and selection contract in the [C# API](../AgentAPI.md#custom-gradient-settings).
Tangent modes are `Auto`, `Smooth`,
`Broken`, `Linear`, `Constant`. On the CPU, Constant follows Fixed: between two keys it uses the
right key's value, while exact keys keep their own values.

Custom still uses the existing GPU palette resolutions and ordinary filtering; a Constant
tangent does not select the dedicated Fixed GPU path. Sharp Custom steps are therefore
limited by LUT sampling and may soften or shift within a sampling interval. The CPU
right-key convention does not promise the same rendered step behavior as GPU Fixed.

Active Custom requires explicit nonempty `customKeys` and `customColorMode`. Switching
or fitting automatic data is a UI/C# operation, not an implicit import conversion.
Other interpolation modes may retain these same fields; empty/null curves are allowed
only outside Custom. Invalid IDs, times, channels or nonfinite values are rejected.
Copy/inspect preserves both datasets and can be used as the body of a later gradient write.
The source RGB curves retain their entered values; final RGB uses the shared expanded
range at adjacent positions in the union of color-key times, and alpha is clamped to 0..1.

Changing `customColorMode` in a payload reinterprets its values. To preserve appearance,
use explicit **Convert Color Mode** or `ConvertCustomColorMode`: it approximately refits the curves,
may change keys/tangents, and retains the separate automatic gradient. The observed
conversion error is diagnostic rather than a certified bound.

GPU palette layout, dimensions, formats and filtering are unchanged in this CPU update.

## HLSL interface — shader-only or inside JSON

Preset headers may declare `// @group("Color")` after the effect marker and optional
`@control`, before HLSL declarations/includes. It creates a flat block within each Add FX
category submenu: groups and names sort alphabetically, with separators between groups.
The effect marker retains its `Category/Name` path. No `@order` is used.
Quoted preset groups have no end directive; unquoted `@group(Title)` / `@endgroup`
still controls parameter layout. The catalog returns `group` and searches it with `query`.
See [preset list groups](../ShaderFX.md#preset-list-groups).

FX may declare `// @icon("../Icons/name.svg")`, `// @icon("guid:...")` or a leading
`/* @icon-svg ... */` block. Keep these after the effect marker and optional `@control`,
before HLSL code. Icons do not change shader sampling or compilation. See the
[icon syntax, supported SVG features and transport rules](../ShaderFX.md#fx-icons).

FX inspector previews use `// @preview(Function)` for an independent image, or
`// @param preview(Function) float _Amount = 0.5 [0 .. 1]` for a Slider track.
Other inline fields show a small right-side rectangle. Functions return linear straight
`float4` RGBA, take `float2 uv` first and optionally the declared parameter's shader value
second (standalone previews take UV only). Alternatively, `preview(Function, _A, _B)` or
`// @preview(Function, _A, _B)` explicitly passes declared `@param` values after UV in that
order, with exact shader types and no extra own value. Forward/hidden references are allowed;
unknown names and signature mismatches produce a local preview diagnostic. A listed own float
on a Slider uses its track candidate; other references and standalone entries use actual values.
All normal uniforms/helpers remain available;
the Slider candidate never overwrites the selected uniform. Guard preview-only functions
with `#ifdef WHIMTEX_PREVIEW`. See [preview types, inputs and limits](../ShaderFX.md#inspector-previews).

All FX and HLSL brush wrappers also expose the global baked Hue LUT:
`WhimTexHue(h)` returns HSV `(h,1,1)` RGB components, while `WhimTexHueLinear(h)` returns
linear color. Hue is measured in turns and repeats. The 1536×1 float texture is globally bound
as `_WhimTexHueGradient`, with `_WhimTexHueGradient_TexelSize`; do not shadow these with
material Properties or `@param`. See [shared hue lookup](../ShaderFX.md#shared-hue-lookup).

### FX conditional controls

Wrap rows in `// @if _Mode == 1` / `// @endif` to hide them when false, or
`// @enableif _Enabled == 1` / `// @endenableif` to leave them visible but disabled.
Both accept `==` or `!=` against a finite numeric or true/false literal and a declared scalar
or an explicit vector component such as `_Channels.a`. Drivers may be forward references or declared under
`@enableif`, but no driver declaration may be under `@if`. Unknown/vector drivers are
invalid when an actual parameter or standalone preview uses the condition; empty blocks
and headings/help boxes alone add no driver dependencies. Literal syntax is always validated.

Enabled blocks can nest with themselves and with one active `@if` in either order;
combined condition depth is at most 16. Nested `@if` is still forbidden. Close the
innermost condition with its own end directive, without arguments. All enclosing enabled
conditions must pass; an inner condition cannot override a disabled or read-only ancestor.
Fields, attached headings/help boxes and previews follow the same conditions. Values,
uniform uploads and HLSL behavior do not change. Preset exports preserve the blocks.
`ShaderFXParameterControl.enableIf` is an outer-to-inner `ShaderFXEnableCondition[]`, empty
outside enabled blocks; each struct contains `string parameter`, `bool notEqual`, `double value`.
Standalone previews carry the same metadata. See [parameter declarations](../ShaderFX.md#parameter-declarations).

### FX layout metadata

Keep `// @group`, `// @group(Title)` or `// @group(Title; _HeaderParameter)` paired with `// @endgroup` for non-collapsible bordered groups. Add `// @foldout(Title)` or `// @foldout(Title; _HeaderParameter)`, paired with `// @endfoldout`, for collapsible content; the foldout title is required. A header parameter must be declared exactly once in the source, outside both `@if` and `@enableif` and directly inside its own group/foldout. Supported hidden header fields are promoted into the header. Header checkboxes edit values independently of folding and tab selection; use `@if` for dependent row visibility, `@enableif` for editing availability and HLSL for the effect behavior.

Use `// @tabs` or `// @tabs(Title)` around `// @tab(Page title)` / `// @endtab` pages, then `// @endtabs`. Page titles are required, non-empty and unique within one tabs block after trimming, with case-sensitive comparison. A page accepts `// @tab(Page title; _Enabled)` for a scalar toggle left of its label. Declare that toggle exactly once, unconditionally and directly in that page; hidden toggles are promoted and the body row is omitted. Other types are invalid. The checkbox changes only its value, without switching pages; page selection leaves it unchanged. Use `@enableif`/`@if` for dependent rows. The overall `@tabs` heading accepts no header binding. Put all parameters and standalone previews inside pages, never directly under `@tabs`. Empty `@tabs()` is invalid; omit parentheses to omit the overall title. Individual tab headers share group-header Copy/Paste/Reset actions for all values in that page, including hidden/disabled fields and descendants; sibling pages and shared fields are excluded.

```hlsl
// @tabs
// @tab(Color)
// @foldout(Tint; _Enabled)
// @param hidden toggle(float) _Enabled = true
// @enableif _Enabled == 1
// @param color _Tint = (1, 1, 1, 1)
// @endenableif
// @endfoldout
// @endtab
// @tab(Shape)
// @param float _Radius = 0.5 [0 .. 1]
// @endtab
// @endtabs
```

Groups/foldouts can nest inside pages and in one another, except that a group cannot open when the nearest active group/foldout is another group. `group → foldout → group` is allowed. Nested tabs are forbidden anywhere, including through a foldout. Maximum depth is 16 containers, counting pages. All container/page starts and ends must be outside both `@if` and `@enableif`; conditions may wrap rows inside containers/pages. Close the innermost container first; end directives take no arguments. Every container/page needs at least one non-hidden parameter declaration or standalone preview in its subtree; hidden declarations alone, even header bindings, do not satisfy this rule.

This is layout only: all values are still uploaded and the effect runs regardless of collapse or selected page; `@if` controls row visibility and `@enableif` controls editing availability. Tabs use content-width window-style tabs with the active tab joined to its content, no blank heading row for untitled `@tabs`, and no close/new/reorder controls. Per-FX choices live in UI-local `SessionState`, keyed by stable FX `ShaderKey` and container kind/id/title, outside shader values and Undo. They survive reloads in the current Editor session, not a full Editor relaunch. C# controls carry an outer-to-inner `ShaderFXLayoutContainer[] containers` path with `kind`, `id`, `title`, `headerParameter`. Flat `inGroup`, `groupId`, `groupTitle`, `groupHeaderParameter` retain only the nearest group/foldout summary. See the [full layout contract](../ShaderFX.md#layout-containers).

### Built-in noise library

FastNoiseLite v1.1.1, the same library used by Noise layers, is included automatically
in every WhimTex HLSL effect and both Static and Dynamic brush tips. No library source or
explicit include is needed.
Use the `fnl_` types, `fnl*` functions and `FNL_*` constants; avoid redefining those names.

For spatial noise, prefer this library when its algorithms fit the requested result rather
than reimplementing Perlin, Simplex-family, Value or Cellular noise from scratch. This is a
recommendation, not a requirement: custom noise or point distributions may suit the task better.

```hlsl
// @param float _Scale = 8 [0.1 .. 64]
float4 ApplyFX(PinnedUV uv, float4 color)
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

The same noise calls work inside Static or Dynamic `BrushTip` signatures;
the script still starts with `// @whimtex-brush Category/Name`.
For independent random values per stroke or stamp, use the brush's
[indexed `BrushRandom` helper](BRUSHES.md#dynamic-context-and-randomization).
It is not spatial noise; the FastNoiseLite recommendation does not replace it or rule out
custom hashes and specialized algorithms that the library does not provide.

### Built-in distortion controls

Distortion presets expose `_Strength` (Spherize, Radial Shear), `_Angle` (Twirl, degrees)
or `_Amount` (Polar Coordinates, Displacement Map) in the FX header. Amount defaults to 1,
preserving the full configured mapping; zero returns the original image. Polar Amount
interpolates sampling coordinates (hard range 0–1). Displacement Amount multiplies vector
X/Y strength, Grayscale strength or Parallax depth (range `[0 .. ~2]`); `_Mix` remains an
independent output blend. Spherize Sphere clipping and Radial Shear Offset remain when
their existing Strength is zero. See [the shader reference](../ShaderFX.md#distortion-header-controls).
Polar Coordinates has independent `transform2D` frames: `_Input` selects the source,
`_Output` places the output. Both default to position `(0.5, 0.5)`, size `(1, 1)`, rotation `0`.
To Polar maps Output's circle to Input's strip; From Polar maps Output's strip to Input's circle.
Sampling is `_Input_ToInput(P_or_Q(_Output_ToLocal(uv)))`; reverse Mode and swap frames
for the inverse coordinate map. Both use standard Edit on Canvas/reset controls.
Old one-frame layouts and previous parameter names are not converted.
Distortion presets expose **Tiling** (`_Tiling`); UV Transform uses **Input Tiling** (`_InputTiling`):
`Clamp: 0`, `Repeat: 1`, `Mirror: 2`, `Clip: 3`. Map wrapping is independent. UV Transform
defaults to Clip, distortions to Clamp. These modes read the existing input image, not
unbounded procedural noise outside its raster.

### Built-in edge contours

`Stylization/Edge Outline` offers Color (RGB differences), Luminance (brightness only)
and Hue (color tone) detection with two methods: Boundary (default) creates uniform
contours; Scharr weights opacity by a 3×3 gradient. Strength amplifies Scharr only and is
hidden in Boundary. Detection sensitivity, Thickness, Shape, Softness, tint and Output
are shared. Broad transitions may produce bands in Scharr; thick Scharr contours cost more.
Hue Threshold is the shortest color-wheel difference in
degrees (0–180); Min Saturation excludes a boundary if either side is too muted. Gray
and black never seed Hue boundaries. Threshold applies to Color/Luminance only.
Detection sensitivity filters transitions independently of Thickness
(0–32 canvas pixels). Shape selects Round, Square or Diamond joins; Softness controls the
line edges. Overlay preserves source alpha; Outline Only emits transparent contours within
source coverage. Neither mode outlines alpha-only silhouettes. Use the linked preset when
available rather than duplicating its code; see [the shader reference](../ShaderFX.md#edge-outline-preset).
`_Opacity` (0–1, default 1; FX header control) blends the complete selected output with
the original input. Zero leaves the image unchanged, including in Outline Only.

### HSV and seeds

`Color/Hue & Saturation` replaces HSV. `_MasterHue`, `_MasterSaturation` and `_MasterLightness`
correct the whole image; four `_Range1`…`_Range4` groups override Master inside selected hues.
Each range has Enabled, a float2 Hue Range and Feather (degrees), signed Hue, Saturation
and Lightness relative to the source, not to Master's output. Saturation scales distance from
weighted gray (0 removes color, 1 is unchanged, above 1 boosts it); Lightness uses -100…100
with neutral 0, reaching black/white at its endpoints. It is not HSV Value or a brightness multiplier.
Master and Range 1–4 retain five
tabs in an untitled block, with Enabled checkboxes in the tab headers. Master opens first. All ranges start
disabled; `@enableif` keeps their body controls visible and gray while preserving values.
Source Saturation (HSV) and Output remain shared below the tabs; selection still measures the original HSV saturation.
With no enabled ranges, Master uses only the correction without selection/overlap work.
Use the `_Master*` names for whole-image correction; there is no second HSV preset and no Value-to-Lightness alias.
Hue ranges use `[0 % 360]`: `[330, 30]` crosses red, `[0, 360]` selects the full circle,
and equal stored endpoints have zero width. Anchored full cycles such as `[-330, 30]` retain
one positive turn in storage while both handles display their meeting angle. The preview track shows one hue turn.
Ranges use source hue; overlaps average only range correction offsets. Feather and the shared
`_SaturationRange` cutoff/full-strength endpoints blend overrides back toward Master. Gray pixels use Master.
To desaturate everything except blue, set `_MasterSaturation` to 0, `_Range3Enabled` to true,
`_Range3` to `[210, 270]` and `_Range3Saturation` to 1, then adjust endpoints/Feather.
Output selects Result or source range masks (unaffected by Master), and `_Opacity` blends
that output with the input. See the
[preset contract](../ShaderFX.md#hsv-preset) for boundary and alpha rules.

Declare an explicitly randomizable seed as `// @param seed(float) _Seed = 0 [0 .. ~10000]`.
It remains an ordinary uniform. Random chooses an integer within both declared bounds;
the finite bounds must be within −16777216…16777216 and contain at least one integer.
Manual input keeps normal hard/soft rules. Preview/save/load never randomize. Labels, groups,
conditions, `hidden` and repeated controls work normally; FX edits can explicitly invoke
[`randomizeSeeds`](../AgentAPI.md#fx-edits-and-presets). The same modifier works in HLSL brush tips.

### Effect entry point

Write a fragment function, **not a complete ShaderLab shader**:

```hlsl
// @whimtex-effect Color/Invert
// @param float _Amount = 1 [0 .. 1]
float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, 1 - color.rgb, _Amount), color.a);
}
```

The supported signatures are `float4 ApplyFX(float4 color)` and
`float4 ApplyFX(PinnedUV uv, float4 color)`. Define exactly one; parameter names may vary,
and `const`/`in` are optional. `ApplyFX(float2, float4)` is no longer supported.
`PinnedUV` is immutable and converts to `float2` implicitly or explicitly. Arithmetic,
swizzles and numeric helpers lose the pin; conversion back, construction and mutation are
forbidden. Same-type copies and pinned helper arguments/returns retain it.
Locality follows the coordinate type in reachable `SampleInput`/`SampleTexture` calls:
pinned is local; numeric coordinates or explicit tiling are spatial. Procedural UV math
without image reads remains local. Unverifiable macro/bypass dependencies use a conservative path.

**`SampleInput` and `SampleTexture` are the primary WhimTex API for reading textures.**
`SampleTexture(_Mask, uv)` reads a declared `texture2D` parameter, including Texture, Layer,
Self and None sources. Both reading functions accept pinned or numeric coordinates;
only unchanged pinned coordinates confirm a local read.
`SampleInputLod(uv, lod)` and `SampleTextureLod(_Mask, uv, lod)` select an explicit mip
level through the same texture-view mapping. Both return straight RGBA, preserve HDR,
and accept `PinnedUV` through its numeric conversion. Both are always spatial, even
at LOD 0 with unchanged coordinates: compilation reports `spatial` / `spatial_sample`
without a bypass warning unless another operation is unverified. Use them for sampling
inside data-dependent loops, as in Edge Outline, or for base-level derivatives, as in
Height to Distortion. LOD 0 is the supplied texture's base
level, including reduced previews; these functions do not create mipmaps or request
native document resolution. Mip availability, filtering and wrapping follow the sampler.
`_MainTex` and declared `texture2D` parameters have the read-only HLSL type
`WhimTexTexture`. Use that type for helper/preview arguments and texture return values,
not `sampler2D`. `SampleTexture` maps whole-input UV to each texture's own stored region;
call sites such as `SampleTexture(_Mask, uv)` do not change. `.texelSize` and
`_Name_TexelSize` contain `(1/W, 1/H, W, H)` for the complete input at its render scale,
even if only a crop is stored. Copies retain that metadata. Construction, casts, mutation
and `out`/`inout` are invalid; `.pixels`/`.uvMap` access is an unconfirmed bypass.
Gradient/curve lookup samplers and brush-tip texture parameters keep their existing types.
Verified pixel-local FX use the visible canvas region. Each referenced layer independently
renders its spatial FX prefix completely before cropping; locality does not propagate
from a consumer to its dependencies. Inspector callbacks use a separate complete input
reduced to at most 512 pixels, so they can sample outside the current canvas view.
Save/export and agent renders still evaluate complete images.
An untransformed Shader Processor can reuse the regional stack backdrop. A processor
transform that moves/resamples the input or a spatial FX prefix requires the full
backdrop. Pass-through groups inherit their external backdrop; isolated groups do not.
Direct `tex2D`, resource `.Sample`/`.Load` and similar HLSL reads are bypass paths. Detected
bypasses or derivatives prevent pixel-local confirmation and produce a warning rather than
disabling an otherwise valid shader. This is an authoring contract, not a sandbox.
`ShaderFX.IsPixelLocal` describes the applied usable shader, not pending edits;
`Layer.AreActiveFxPixelLocal()` checks active non-null FX on that layer only, with an empty chain true.
Unavailable/unconfirmed active FX return false; disabled FX are ignored.
See [pixel-local rules and mask example](../ShaderFX.md#pixel-local-entry-points-and-texture-reads).

`color` is the incoming straight (not premultiplied) RGBA. `SampleInput(uv)` reads that same
input at another UV, including earlier FX. Return straight RGBA in linear working space.
Shader Processor receives the lower composite; a regular layer's FX receives that layer's image.
To generate an image from scratch, use a Color layer with FX replacing its color.

`SampleInput(uv, tiling)` uses spatial addressing: Clamp (0), Repeat (1), Mirror (2), or
Clip (3; transparent outside UV 0–1). Repeat filters across opposite edges and respects
the current input's Point/Bilinear filtering without changing texture import settings.
Expose tiling through a normal enum `@param`. The one-argument function is unchanged.

`LayerToLocal(uv)` converts canvas UV to the owning layer's local UV, including parent group transforms and perspective. Use it for procedural shapes that must follow the layer transform. `ApplyFX` UV and `SampleInput` remain canvas-space; do not pass local UV to `SampleInput`. The helper does not wrap or clamp coordinates.

Available inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`
(width, height, reciprocal width, reciprocal height), `_RenderScale`, and `_PixelScale`;
`UnityCG.cginc` is already included. `_PixelScale` is the document's logical-pixel multiplier,
default `1`; texture dimensions, texel sizes and `_RenderScale` remain physical pixels.
`_PixelScale` is a canvas-FX input, not an HLSL brush-tip input.
Do not redeclare these or generated parameters/helpers. Prefer explicit parameters for predictable
time-dependent FX. Unity time inputs such as `_Time`, `_SinTime`, `_CosTime`, `_TimeParameters` or
`unity_DeltaTime` are allowed and do not block compilation, but WhimTex does not control their updates:
Canvas, thumbnails and export may differ. Their use disables reusable result caching and produces
a warning in Diagnostics and agent compilation `diagnostics`/`warnings`, not `errors`.
Unity time warnings use the shared FX diagnostic list and Console reporter, just like compiler
and declaration messages. Console deduplicates each source path + severity/location/message until
scripts reload; repeated Apply, agent preflight and rendering do not repeat it. UI/API diagnostics
are never suppressed. Errors and warnings retain their respective severity even when both occur.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Bias = 0 [.. 10]
// @param float _Amount = 10
// @param px float _Radius = 3 [0 .. ~64]
// @param px float2 _PixelOffset = (0, 0)
// @param toggle(float) _IncludeAlpha = false
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
For canvas FX, the `px` modifier marks artistic lengths in logical document pixels. It supports `float`,
`float2`, `float3` and `float4`, combined with `hidden` and `label(...)`; it cannot be combined
with seed, enum, toggle, color, normal, point or resource parameters. Every declaration of
the same variable must agree on `px`. Stored values, UI values and declared ranges remain
logical pixels; FX upload multiplies these numeric values by `_PixelScale` exactly once.
Do not multiply marked parameters by `_PixelScale` again in HLSL. For unmarked custom
calculations, use `_PixelScale` explicitly for artistic pixel lengths or grid spacing;
keep physical texture indices, sampling precision and antialiasing separate.

HLSL brush tips render into their own fixed-resolution texture. Their numeric lengths,
including declarations marked `px` through the shared metadata parser, stay in that texture's
own units and do not receive the document multiplier. Pixel Scale already scales the physical
brush diameter when the texture is stamped; multiplying the tip's lengths again would scale
it twice. Changing document Pixel Scale does not rebake or resize the stored brush-tip texture.

`[min .. max]` clamps edits through that control. Put `~` before a boundary value to allow crossing it:
`[0 .. ~2]` allows values above 2, `[~0 .. 2]` allows values below 0, and `[~0 .. ~2]` allows both.
The slider always stays within 0..2; numeric input and label dragging obey only the hard boundaries.
For example: `// @param float _Strength = 5 [0 .. ~2]` or `// @param float _Strength [~0 .. ~2]`.
Ranges with a soft boundary require both values and `min < max`; one-sided or equal soft bounds are errors.
The old `~[0 .. 2]` syntax is rejected. Boundary flags survive FX and HLSL brush preset export.
Bounds written inside HLSL still apply independently.
For FX and HLSL brush tips, `toggle` displays a checkbox and defaults to a `float` value of `0` or `1`. `toggle(type)` chooses its exact scalar or vector type. An optional default is `true`/`false` or `1`/`0`; ranges are not allowed. Use `if (_IncludeAlpha > 0.5)` or use it directly in arithmetic.

FX and HLSL brush tips also support dropdown controls and repeated declarations of one variable:
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
exists, scalar/vector/color defaults are zero. Repeated `float`/`toggle(float)`/`enum(float)` controls share one float
value. Linked controls must have matching HLSL value types and compatible storage; color storage stays separate. Control ranges do not clamp values set through
another control. Preset export saves the current value once.
Unbounded `float2`, `float3` and `float4` are raw vectors with two, three and four components. `point` is a `float2` position in normalized canvas UV: bottom-left `(0, 0)`, top-right `(1, 1)`. Coordinates and tuple defaults may lie outside these bounds. Its hand button (**Edit on Canvas**) activates a handle that can also be dragged outside the canvas. Its default is `(0.5, 0.5)`; an explicit tuple is optional and ranges are not accepted. `normal` generates a normalized `float3`; its default and zero-vector fallback are `(0, 0, 1)`. It also offers an on-canvas direction handle; no range is accepted. Defaults are optional. Unknown parameter types are rejected.

`// @param preview(PreviewRange) float2 _Range = (0.2, 0.8) [0 .. 1]` is an ordered interval using a native MinMaxSlider and X/Y fields. It requires two hard finite limits with min < max and min <= X <= Y <= max; omitted defaults select the whole interval. `[0 % 360]` instead permits wrapped start/end pairs such as `(330, 30)` and wraps dragged endpoints across track edges. `(0, 360)` is full; equal stored endpoints are empty before Feather. Full cycles can also retain their meeting angle with endpoints one positive period apart: `(-330, 30)` or `(330, 690)`. At least one endpoint must be within the limits; all nonfull pairs must have both endpoints within them. The UI wraps full-cycle coordinates to the displayed angles but retains their winding in storage and shader values, so the handles stop where they meet rather than resetting to the limits. A scalar `float` also accepts `%`: ordinary slider dragging, repeating manual input (`370` → `10`, `-10` → `350`). Missing/soft `%` limits are rejected. Inline preview accepts `float2 uv` followed by optional `float2 value`: UV spans the whole track, value contains both actual endpoints. No extra JSON type is needed: values remain two-number arrays. Live FX snapshots expose `cyclicRange`. See [float2 intervals](../ShaderFX.md#float2-intervals).

FX use ordinary input images and explicit parameters, not hidden layer-specific data. Lighting/Bevel Emboss reads a height texture (Self by default) and shares lighting with Normal Map/Lighting. Base Color alpha blends transparent lighting (0) into the shaded surface (1); Output selects Both/Highlight Only/Shadow Only for the transparent part. SDF inputs use the selected layer output; use Gradient or LinearData for a scalar height map. MultiChannelData remains an ordinary RGBA texture input, with distance data in alpha; it does not introduce a typed SDF parameter or hidden field access. Normal Map/Lighting uses `_InputEncoding`; Normal Map/Normalize uses `_Encoding`. Both are enum parameters: `Linear:0`, `Gamma:1` (default Gamma). Gamma means sRGB-encoded channel values; Linear uses raw channels. See [shader reference](../ShaderFX.md) for the complete contract.

`float4` is a raw vector; `color` is a color picker. Texture parameters without an explicit default keep Texture mode and sample white when empty. Use `= none` for transparent black, or `= self` to sample the current layer immediately before this FX (including earlier FX, excluding current/later FX). Both are unquoted declaration keywords and are preserved in exported presets. These are FX parameter defaults, not layer IDs. Users can change the texture source in the editor. In unified JSON, existing texture assets use `$asset` identity references in the parameter value. Do not invent asset identities. Drawing pixels and URL downloads are not part of this format.
Names generate labels: `_NoiseScale` → Noise Scale. No need for a second uniform declaration.

Use `// @param curve _Profile = one` for a constant 1 curve with keys (0,1) and (1,1).
Curve defaults also accept `easeIn` (`t²`) and `easeOut` (`1-(1-t)²`), for example `// @param curve _Profile = easeIn`. Both span (0,0) to (1,1).

`curve` declares a scalar mapping for FX or HLSL tips: `// @param curve _Profile`, sampled with
`_Profile_Sample(t)`. Default: linear (0,0) to (1,1). Input clamps to 0..1; output is unrestricted.
Named defaults: `// @param curve _Profile = linear` or `// @param curve _Profile = easeInOut`.
The latter smoothly eases between the same endpoints with horizontal endpoint tangents.
For clipboard FX, an optional default can be included directly in `fx[].code`:
`// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))`.
Each tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
Use strictly increasing finite times, finite values, weights 0..1 and mode 0/1/2/3
(none/in/out/both). Tangents additionally allow `inf` or `-inf` for steps. Maximum 256 keys;
`keys()` evaluates to zero. No range. See [curve reference](../ShaderFX.md#curve-parameters)
for sampling precision and preset persistence. This is not a layer property.

`gradient` for FX or HLSL tips is declared as `// @param gradient _Ramp`, optionally with two endpoint colors:
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint may be `#RRGGBB` (opaque),
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Without an initializer it starts opaque
black-to-white (Perceptual color mode, Balanced interpolation). Explicit two-endpoint defaults use Gamma/Blend with Gamma key encoding, Clamp, Chroma Correction 0,
Smoothness 0 and midpoint .5 to preserve the endpoint-only HLSL export contract.
The user can edit colors, HDR, alpha and interpolation in the gradient field.
The same hex forms are accepted for `color` defaults; color defaults also accept numeric RGBA tuples.
Call `_Ramp_Sample(t)` for straight linear RGBA; the gradient's Wrap setting controls `t` (Clamp by default).
For example, `return _Ramp_Sample(uv.x);`. Do not declare a sampler yourself. A cached 512×2
LUT supplies the samples; editing keys does not recompile the shader. Edited values persist in the
owning effect/brush; two-endpoint defaults are exported with HLSL presets. Active or retained
Custom curves cannot be encoded by that endpoint-only syntax, so export rejects them instead
of discarding manual data. Save the gradient separately or keep it in the document. Brush-specific texture
sources and tip coordinates are described in the [brush contract](BRUSHES.md#declared-parameters).

Transform2D uses `(centerX, centerY, width, height, angleDegrees)` in normalized input units.
Omitted default means the full image. For skew/perspective, use `// @param transform2D _Area = matrix(1, 0.2, 0, 0, 1, 0, 0.15, 0, 1)`: nine row-major values mapping local UV to input UV. The matrix must be invertible with no horizon crossing the unit rectangle. Do not combine matrix and TRS defaults.
Generated helpers are `_Area_ToLocal(uv)` and `_Area_ToInput(localUV)`; both support perspective.
Local `[0,0]`/`[1,1]` are corners and `[0.5,0.5]` is center. Out-of-range coordinates are valid.
There is **no automatic mask or falloff**: implement it in HLSL if desired. The user can edit the
frame with green canvas handles. Never reference reserved `_WhimTex_` internal uniforms.

For a `.hlsl` catalog preset the **first physical line** must be `// @whimtex-effect Category/Name`:
no blank line or license header before it. For inline/JSON FX this header is optional.

Linked presets also declare `// @preset("12bc047d-f5b2-43e4-91fd-84e0c2276cde", 1)` once in
the initial comment header, preferably immediately after the marker. The ID and positive integer
version are required together; nonzero GUID D and N forms are accepted and stored as lowercase N.
Omit this directive for an independent embedded copy, including project/package presets; Unity
asset GUIDs do not supply a missing preset ID. Project/package and external user-library links
both require an exact version match. Missing, broken, conflicting or differently versioned sources
use the saved document code while retaining the link for later retry. TIFF/JSON store `presetId`
and `presetVersion` with the dependency-expanded fallback code; source paths and dependency hashes
are transient. `shaderKey` remains the separate FX instance identity. For connected operations,
use the ID returned by `whimtex_fx_catalog`: declared preset ID when available, otherwise source
path. Do not copy an example GUID when creating a new source; generate a new GUID or use
**Save Preset…**, which creates a new ID/version 1 and preserves an overwritten destination’s
valid ID/version. See [preset identity and version](../ShaderFX.md#preset-identity-and-version).
To encode HLSL inside JSON, use a string with `\n` for newlines; escape quotes normally. JSON escaping
is decoded before compilation. Keep generated FX self-contained and use the built-in helpers.
The render fallback expands custom project/package and user-library includes; engine includes and
external texture inputs remain dependencies. Code whose dependencies cannot be expanded still needs
repair before it can serve as a usable fallback on another machine.

For additional engine-specific authoring details, see [Shader authoring](../ShaderFX.md).

### Typed numeric shader parameters

The field syntax is `toggle(type)`, `color(type)`, `enum(type)` or `seed(type)`; defaults are float, float4, float and int. Toggle/color accept bool/int/uint/half/float scalars and vectors2/3/4; enum accepts numeric scalars; seed accepts int/uint/float. `rgba` changes component labels only. Use `rgba toggle(float4)` for channel switches and explicit vector components in conditions. `gradient`, `curve` and `transform2D` are unchanged. API parameter values use JSON booleans for bool, numbers for other scalar types and arrays of the exact component count for vectors. Integer endpoints remain exact. FX reads expose `valueType` and each control’s `field`, `valueType` and `componentLabels`. Float/half color values retain encoded-color semantics; integer/bool colors are raw numeric values, without 0–255 scaling. Seed values must be integral; float seeds stay within ±16777216. See [the shader parameter contract](../ShaderFX.md#field-presentation-and-numeric-type).
