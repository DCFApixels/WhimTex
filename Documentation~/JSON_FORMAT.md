---
layout: default
title: WhimTex JSON format
parent: Technical reference
lang: en
permalink: /reference/json-format/
---

# WhimTex JSON documents

`whimtex.document`, version **2**, is the shared editable format for `.json` files,
layer clipboard data and agent serialization. It stores settings, not rendered pixels.
TIFF remains the image-backed format; its tagged binary encoding is version 2.
New saves and exports use `.json`; existing `.whimtex.json` names remain readable without renaming.
Previous JSON, layer clipboard and TIFF document versions are unsupported by this checkout.
They are rejected explicitly, not silently interpreted as version 2. No migration or old
`swizzle` alias is provided. Work with a matching older checkout if those files need editing.
New exports and Copy as JSON use this format. Brush and gradient preset formats remain separate.

Backward compatibility with package **0.12.5** or **0.13.0** is not a development requirement.
Retired fields and previous-name aliases are not accepted. The version-2 default
snapshot defines omitted values for current Compact files, independently of UI defaults.

The layer FX list is `fx` in UI, code and TIFF/JSON. JSON rejects `modifiers`;
the TIFF reader diagnoses it as an unknown field and protects against unconfirmed loss.

Channel routing is `Layer.channelMapping`, a `LayerChannelMapping` object with its existing
packed representation. Live API patches use `settings.channelMapping`, an array of four
source labels in output RGBA order; `describe` returns `channelMappingSources`.
The source label `1 - Luminance` evaluates `1 - (0.2126 R + 0.7152 G + 0.0722 B)` from the original linear RGB, independently of alpha.
Its public C# enum is `ChannelMappingSource.OneMinusLuminance`, value 15; existing source values 0–14 are unchanged.
In stored document JSON, `"channelMapping":{"$type":"LayerChannelMapping","packed":49152}` preserves RGB and routes this source to output alpha. Output alpha remains bounded to 0–1.

## Canvas comments

Full-document JSON preserves an optional `document.comments` list. Empty lists and
layer-only clipboard fragments omit it. Each `WhimTexComment` stores `id`, `anchor`
(bottom-left canvas UV), opaque `color` (RGBA in 0..1, alpha 1), `resolved` and a nonempty `messages` list; messages store `id`,
`author`, `text` and `createdUtcTicks`. IDs are unique across all threads and messages.
Comments do not contribute to image pixels. See [TIFF comments](TIFF_FORMAT.md#optional-comments)
for validation limits and anchor behavior. JSON insertion/paste imports layers only;
opening a full document retains its comments.

## Write modes

| Mode | Stored values |
| --- | --- |
| `Full` | All persistent settings, including inactive values and defaults. |
| `FullOptimized` (default) | Active settings, including their defaults; inactive branches are omitted. |
| `Compact` | Same inactive-branch rules, additionally omitting version-2 default values, except source canvas dimensions. |

The optional envelope field `writeMode` uses these exact names; absence means `FullOptimized`.
Full and Compact exports include it; FullOptimized may omit it. Opening restores the mode for
subsequent Save/Ctrl+S. `SaveJson` without options uses the document's `JsonWriteMode`.
Explicit export/serialization options do not change the source document's mode.
Disabled layers and disabled FX remain in all three modes. Collapsed UI panels and visibility-only
shader controls do not determine whether a value is active. Optimizations include unused Noise
warp/fractal/gradient settings, unused Make Seamless modes, disabled correction options, and unused
FX parameter value slots. Unknown/custom shader behavior is not guessed from UI visibility.
Compiled shaders, rendering caches, catalog refresh state and code-derived control descriptions
are not document settings and are never saved. HLSL source and parameter values are separate;
changing a value does not rewrite its declaration into the source code.

Omitted values use the frozen defaults in `WhimTexJsonDefaultsV2`, not the current UI factory defaults.
For insertion/paste only, omitted canvas dimensions instead inherit the corresponding destination axes.
Re-enabling an omitted feature restores those defaults, not the previously inactive user value.

## Envelope and model

Only `format`, `version` and `layers` are required at the root. `document` and every setting inside
it are optional. If present, `document` must be an object, not null; `{}` is valid.

- Opening or writing a document fills missing settings from version-2 defaults (canvas **512 × 512**).
- Insertion/paste fills each missing source dimension from the current destination canvas. Without
  either dimension, no canvas-size prompt is requested; unrelated document settings do not request a resize.
- With only `width` or only `height`, the other axis is independent: format default on open/write,
  destination axis on insert/paste. Explicit values still undergo normal validation.
- API insert/replace never resizes the destination. Clipboard paste can offer to adopt explicitly
  supplied dimensions; accepting keeps any unspecified axis at its current size.

### Canvas context on export

All writer modes keep `document.width` and `document.height` explicit, even when they equal defaults.
This preserves source-canvas context when the same export is pasted elsewhere. Other settings continue
to follow Full/FullOptimized/Compact rules. Omission is a reading convenience, not a request to discard
output settings during export.

```json
{
  "format": "whimtex.document",
  "version": 2,
  "document": {
    "width": 256,
    "height": 256,
    "outputFilter": "Bilinear",
    "outputPrecision": "EightBit",
    "outputSrgb": true
  },
  "layers": [
    {
      "id": "noise-example",
      "layerName": "Clouds",
      "behaviour": {
        "$type": "NoiseLayerBehaviour",
        "noiseType": "OpenSimplex2",
        "scale": 8,
        "fractal": "FBm",
        "octaves": 3
      }
    }
  ]
}
```

Use the exact enum spellings in the [generated schema](AI/document.schema.json).
Fields match the persistent model: `behaviour` contains type-specific settings; `transform`,
`fx` and `children` belong to the layer. Layer order is top to bottom. `$type` selects an
allowlisted model type, not an arbitrary assembly-qualified runtime type. Unity vectors and colors
are fixed-length numeric arrays; transform `Double2` values use objects with `x` and `y`.
Writers use the field's current component count. JSON permits shorter float-vector arrays,
filling added components with zero; tagged TIFF values must exactly match the current field type.
Scalars, integer-vector conversions, narrowing and shortened colors/quaternions remain invalid.
Shader FX have `$type: "ShaderFX"`, source `code`, `parameters`, `active` and optional
`$name`. Shared FX use `$id`/`$ref`; these IDs are distinct from layer IDs.

### Canvas frame changes

`WhimTexDocument.ApplyCanvasFrame(RectInt)` changes the output boundary in current canvas
pixels, with X pointing right and Y up from the bottom-left corner. Negative origins expand
that side. It preserves source images and the layer hierarchy; content beyond the new boundary
is retained and can be revealed by expanding the canvas again. The caller owns Undo and change
notification. The method returns false for the unchanged frame and validates every candidate
transform before changing the model. Dimensions are 1..16384 per axis, at most 16,777,216 pixels.

`WhimTexDocument.ApplyCanvasOrientation(CanvasOrientation)` applies `FlipHorizontal`,
`FlipVertical`, `RotateLeft` or `RotateRight` to the whole document with the same atomic
transform and source-preservation contract. Left is counterclockwise by 90 degrees; right is
clockwise. Quarter turns exchange width and height. The caller owns Undo and change notification.
The Crop toolbar applies these operations immediately and carries its pending frame, selection
coverage and guides into the new coordinates. FX remain live and retain their parameters;
canvas-dependent effects are reevaluated rather than baking and rotating the composite image.

`WhimTexDocument.ApplyCanvasScale(width, height)` stretches canvas geometry through the same
atomic preparation, preserving layer identities, hierarchy and owned source-image resolution.
Stored pixel measurements remain logical values. The document's `pixelScale` (default `1`,
finite and greater than zero, at most `16384`) converts them to physical canvas pixels during
rendering and painting: Text font/layout metrics, Shape strokes, procedural pixel spacing,
brush dimensions/distances and built-in FX radii. Render-quality reduction is a separate scale.
Custom Shader FX mark pixel measurements with `// @param px float ...` (or float vectors);
those uniforms are converted at upload. `_PixelScale` is available for explicit shader logic.

`ApplyCanvasScale(width, height, pixelScale)` and `ApplyCanvasFrame(frame, pixelScale)` commit
dimensions and the pixel multiplier together; `ApplyPixelScale(pixelScale)` changes the latter
alone. Two-argument Scale and one-argument Frame retain the current multiplier. Each operation
validates its full candidate before mutation and returns false only when all affected values
are unchanged. Text layout follows logical pixel metrics, so its physical size scales with
`pixelScale`; its stored settings and live FX parameters are not rewritten. The caller owns
Undo and change notification.

The Crop tool's `Scale` and `Resize` are subtypes selected with the shared toolbar flyout or
header dropdown. Frame dimensions and Pixel Scale remain drafts until Apply. Scale's Auto
uses the committed multiplier times `min(newWidth / width, newHeight / height)`, including
nonuniform changes. Disabling Auto retains its current result for manual editing. Reset returns
both drafts to the last committed dimensions and multiplier. The stretch preview displays
geometry; pixel-dependent rendering is recalculated on Apply.

`Layer.sourceCanvasSize` is a persistent `[width,height]` reference used by Noise, ordinary
Pattern and uninitialized Drawing sources. The default `[0,0]` follows the current canvas;
otherwise both dimensions must be positive, at most 16384, with a product at most 16,777,216.
The first canvas or pixel-scale operation records the previous canvas size for existing layers.
Later frame and orientation changes retain that reference. `sourcePixelScale` records the
reference multiplier (`0` initially means the current document multiplier). `sourceCanvasScale`
starts at `[1,1]` and accumulates the physical length ratios of the source's two local axes
under Scale (measured at its centre for projective transforms); procedural pixel spacing
uses this transformed source metric without applying the canvas stretch twice.
These references are separate from an image's stored resolution and are not caches.
Text keeps fixed local-pixel layout through transform compensation; Shape keeps its physical
geometry and stroke metrics. Source images are neither resampled nor truncated.

FX remain editable and run against the resized canvas. Effects that depend on canvas dimensions,
canvas UV, input boundaries or time can produce a different result; arbitrary FX are not guaranteed
to be pixel-identical after a frame change. Seamless Pattern recalculates its lattice for the new
boundary, preserving its seamless constraint rather than freezing the previous lattice.
Procedural antialiasing is also reevaluated on the new viewport's pixel-derivative grid;
an odd-pixel frame shift can slightly change soft edge coverage while retaining the geometry.

### Open, insert and replace

There is no document/fragment discriminator. Exporting selected layers produces a document containing
only those layers, with source-canvas settings. The caller's operation determines how the content is used:

- Open restores the document, retaining layer IDs, output settings and History.
- Insert/paste adds layers with remapped IDs; it does not replace existing layers or apply source output settings.
- Replace changes only the explicitly selected layer through the API; content never requests replacement itself.

Root `kind` is rejected. The caller chooses the operation.
The required `format` and `version` fields still identify the format and its version.
This does not remove type-specific fields such as Shape's `behaviour.kind`.

Clipboard may ask whether to adopt canvas dimensions; either choice preserves the destination's output
filter, encoding and other output settings. Exporting selected nested layers converts
their root transforms to canvas space. Include their Specific, Previous, All Below, clipping and
texture-layer dependencies; missing required selections fail explicitly.
Selected-layer JSON can be opened as its own document or written with the API, just like whole-document JSON.
This does not clone a TIFF TextureImporter's compression or platform overrides.

## Drawing and external assets

Drawing nodes retain their ID, name, transform and FX, but have `contentOmitted: true` inside their
behaviour instead of pixels. Nonempty Drawing requires explicit omission permission; the UI lists
affected layers before saving. Export does not empty, rebind or mark the source TIFF saved.
Saving from the window switches to the saved version without pixels only after confirmation.
The low-level `SaveJson` API does not erase live pixels: when omission is explicitly allowed,
it leaves the source marked dirty. Prefer `ExportJson` to retain a pixel-bearing source unchanged.
Empty placeholders can subsequently be saved normally. No URL download substitutes for saved pixels.

### Text and system fonts

`TextLayerBehaviour` stores editable `text`, `fontFamily`, `fontStyle`, `fontSize`, `maxFontSize`, `spacing`, `characterHorizontalScale`,
`alignment`, `casing`, `layoutMode`, `layoutOffset`, `frameSize`, `wrapping`, `overflow`, `justify`, `autoSize` and `color`. `layoutMode` is
`Point` or `Frame`; `wrapping` is `Manual`, `Words` or `Characters`. Frame dimensions are
1..32768 canvas pixels per axis. Point text ignores the retained frame settings.
`layoutOffset` is `[x,y]` in local document pixels, with finite components -1000000..1000000;
X points right and Y up. It moves the layout before rasterization without changing Transform,
is retained across Point/Frame switches, and participates in the saved-appearance key.
`overflow` is `None` (default, unclipped), `Clip`, or `Ellipsis`. It participates in the layout
key and saved appearance. Ellipsis shortens displayed text at text-element boundaries to fit both
frame axes, after Auto Size, without changing the editable `text`.
`autoSize` fits Frame text using the largest whole-pixel size within `fontSize` (minimum) and
`maxFontSize` (maximum, default 256), without overwriting either bound. Both are 1..2048 px;
the range must contain a whole-pixel size. With Auto Size off, `fontSize` is the fixed size.
The fitted size is derived, not a separate serialized field.
`casing` is `Normal`, `Lowercase`, `Uppercase` or `SmallCaps`; it affects the saved appearance,
not the editable `text`. SmallCaps synthesizes smaller uppercase glyphs for lowercase letters.
`spacing` is a `TextSpacing` object with `character`, `word`, `line` and `paragraph`, each -1..10 em,
defaulting to zero. One em is the effective font size. The former `lineSpacing` multiplier is a removed
field, not an alternative spelling or unit for `spacing.line`; old data must receive an unresolved-field diagnostic.
No font asset or machine-specific font path is embedded.
Omitted Text settings use frozen version-2 defaults: `text: "Text"`, empty `fontFamily`,
`fontStyle: "Normal"`, `fontSize: 64`, `maxFontSize: 256`, `alignment: "MiddleCenter"`,
`layoutMode: "Point"`, `layoutOffset: [0,0]`, `frameSize: [256,128]`, `wrapping: "Words"`, `overflow: "None"`,
`casing: "Normal"`, zero spacing, `characterHorizontalScale: 1`, white `color`,
and `justify: false` / `autoSize: false`. An empty font family resolves to the installed default;
specify a known installed family when a particular font is required.
`characterHorizontalScale` is a 0.01..10 width/advance multiplier, default 1. It preserves glyph
height and added em spacing, participates in wrapping/Auto Size/Ellipsis and the layout key,
and applies equally to Point and Frame text.
Saving prepares `fallbackPng` (base64 coverage PNG), `fallbackKey`, `fallbackFont`, `fallbackWidth`
and `fallbackHeight`, plus `fallbackRect` (local-pixel source bounds as `[x,y,width,height]`).
The layout key excludes tint, Transform and FX, so the saved mask can still
be recolored and processed when the font is missing. A source layout change invalidates that mask.
The PNG is limited to 768 KiB and 2048 pixels per axis; dimension checks precede image decoding.
Missing fonts are warnings, not automatic substitutions. A matching backup renders normally;
without one, the Text layer is transparent until a font is selected.

### SDF and Outline distance modes

Both `SDFLayerBehaviour.metric` and `OutlineLayerBehaviour.metric` accept the twelve names
`EuclideanAntialiased`, `Sharp`, `Bevel`, `Diamond`, `Square`, `MTSDF`, `AAEDT`, `Contour`,
`CornerFit`, `Spring`, `Rbf` and `RbfDual`.
JSON stores these exact names, not numeric IDs. `EuclideanAntialiased` (UI **Round AA**, ID 4)
remains the omitted/default value. `AAEDT` remains ID 10; `Contour` is ID 11, UI **Contour AA**.
`CornerFit` is ID 12, UI **Corner Fit AA**; `Spring` is ID 13, UI **Spring Simulation**.
`Rbf` is ID 14, UI **RBF Your SDF**; `RbfDual` is ID 15, UI **RBF Dual Contouring**.
The new `RbfDual` leaves the previous modes unchanged.

`metric:"RbfDual"` selects experimental continuous RBF root searches, analytic blended normals
and bounded 2D QEF vertex placement with half-pixel cell connectivity. It shares Rbf fitting
and the scalar sharp-distance stage. `sharpMode` and `miterLimit` have the same meaning as in
`Rbf`; `contourMergeDistance` is retained but ignored. The metric participates in the preparation
cache key; Max Distance only remaps the retained field. `Gradient`/`LinearData` are supported,
and `MultiChannelData` remains MTSDF-only. This is not a faithful implementation of
Occupancy-Based Dual Contouring (2024), and contains no copied upstream code.

`metric:"CornerFit"` and `metric:"Spring"` are experimental raster-only scalar modes.
CornerFit fits sides from raw Contour geometry and verifies proposed corners by local AA
re-rasterization. Spring first relaxes the raw contour with springs, then uses the same checks.
Ambiguous cases retain raw geometry; intersecting or unconfirmed extensions are rejected.
Only AA-verified corners receive bounded sharp joins with continuous, local endpoint influence. Both use `miterLimit`
(finite 1..64, default 4) to bevel excessive validated joins, and ignore the validated
`contourMergeDistance`. Source thresholds use coverage remapping; soft/noncoverage masks are
approximate inputs. SDF supports `Gradient`/`LinearData`; `MultiChannelData` remains MTSDF-only.
Neither mode guarantees sharp recovery. Spring adapts ideas from a depixelization paper and
does not reproduce its full interactive tool or C++ implementation; see the shared contract below.

`metric:"Contour"` measures Euclidean distance to finite straight segments extracted from raw
marching-squares alpha/channel threshold crossings, with linearly interpolated subpixel endpoints.
There is no contour fitting, smoothing, simplification, corner recovery, merge/union, miter extension
or MSDF channel construction. Expanded corners round; the field measures the current raster
threshold boundary without restoring lost detail. This is a separate algorithm, not an AAEDT
modification or a sharp-miter mode. Quality comparison is experimental; improvement is not
guaranteed for every input.

Source-channel, threshold, `SourceEdges`, offset and rendering behavior use the existing wrapper.
SDF exposes `threshold`; Outline uses fixed byte threshold 128. `miterLimit` and SDF
`contourMergeDistance` are still validated but do not affect Contour. SDF supports `Gradient`
and `LinearData`; `MultiChannelData` still requires `MTSDF`.
See the [shared field contract](AgentAPI.md#layer-settings) for all mode semantics and controls.

### Asset references

An external Texture, Material or mesh reference is an object containing `$asset` with `guid`, `path`,
string `localId` and expected `type`. Resolve the GUID first; if the GUID is absent from AssetDatabase,
try the path. The local ID and type must still match. If unresolved, open with warnings and a null
reference; retain its identity when saving again, including across Unity serialization.
### Failed or unavailable Shader FX

Shader FX source is embedded with project includes expanded. Engine includes remain engine dependencies.
If includes cannot be expanded, the original source is preserved instead; those external dependencies
must be restored before the FX can compile. Failed HLSL or parameter-declaration compilation does not
abort JSON loading: keep source, parameter values, order and enabled state, return a warning and skip
the FX during rendering. A failed Apply also skips an older compiled version. Successful Apply resumes
rendering; indicators clear only when no diagnostic issues remain. Warnings without errors do not mark
an FX unavailable, but are also returned when preparing JSON effects and remain visible in the headers.
FX diagnostics are marked beside their layer and in the FX section and effect headers, including
collapsed headers. All FX errors/warnings use the shared reporter, deduplicated by source path and diagnostic;
rendering does not retry compilation. JSON saving preserves broken code; TIFF save validation is unchanged.
Only open/compile shaders you trust: expensive GPU code can stall the editor.
Clipboard insertion uses the same soft compilation path after trust confirmation. Failed FX are
included in the paste warning; continuing preserves code, values and enabled state while skipping
their rendering. Canceling leaves the destination untouched. Structural/reference validation remains
strict; a failed shader does not excuse an invalid layer or texture dependency.

## Validation and limits

Unknown fields, incompatible types, unsupported versions and broken internal layer IDs fail explicitly.
Numbers and booleans must use JSON numbers and booleans, not quoted strings. Enum values must be exact,
case-sensitive declared names, never integer IDs or numeric strings. Numeric values must be finite and
representable by their stored type; integer fields reject fractions. Value types cannot be null.
Errors identify the field or component path. Curve tangents alone also accept `"Infinity"` and
`"-Infinity"` for stepped keys. Vector/color components follow their numeric types.
These are storage checks, not the stricter agent-property patch/UI slider bounds: finite persisted values
are retained, with the model's rendering clamps. Noise and Pattern axes and Shape corners
use explicit current values; missing components use the version-2 defaults, not sentinel inheritance.
FX declarations come only from `@param`; readers do not append declarations for saved values.
`declaredInCode` and Shape's former `roundness`/`cornerRoundness` fields are unknown input.
Shape corners use `{style:"Round"|"Bevel",amount:0..1}` objects: four `rectangleCorners`,
`polygonCorners` matching `sides`, or Star/Sector `outerCorner`/`innerCorner` groups.
See [Shape settings](AgentAPI.md#shape-settings) for edge, cap and angle controls.
The generated schema describes per-field constraints; graph dependencies, total layer count and the
combined canvas pixel budget additionally require the reader/API validator.
The reader does not silently discard invalid data. JSON limits are 64 MiB characters, depth 128,
one million model values and 1024 layers including group children; canvas limits are 16384 per axis
and 16,777,216 pixels. Drawing pixels are not in that budget.

## Saving and exporting from the editor

The window's Export button opens a shared export dialog: choose WhimTex JSON, set Mode, then Export…
to select a project path. Canceling path selection retains the dialog; only a successful export closes it.
Drawing omission is warned before path selection and still requires confirmation. Save As always selects a TIFF path;
new JSON copies are created through Export. Ctrl+S on an already open JSON document still saves its JSON file.

For the artist workflow, see [Save and export](en/saving.md).

## C# and agents

`WhimTexDocumentJson.Write(document, options)` and `WriteLayers(document, layers, options)` return
JSON, warnings and `DrawingPixelsOmitted`. Warnings can also describe unavailable system fonts;
only `DrawingPixelsOmitted` signals that live Drawing pixels were excluded and requires omission
confirmation or leaves the source dirty after `SaveJson`. Options use `Mode`, `AllowDrawingOmission` and `AllowDataLoss`
(both false by default). `AllowDataLoss` explicitly permits writing the loaded part of an incomplete
document; it does not recover unread data. Writing or exporting JSON keeps the source load warning.
An accepted `SaveJson` clears it only after successful saving. Interactive Save asks for confirmation,
with a recovery-copy option; C#/agent defaults remain protected.
`Read(json, prepareEffects)` returns an owned, disposable detached document. Call `TakeDocument()`
only when taking over its lifetime. `WhimTexDocumentFile.Load/Save` support TIFF and JSON;
`ExportJson` writes a separate file without changing source identity. Saves are atomic and reject
external file changes or a conflicting open document.

Agent `write`, `insert` and `replace` support `save:false`: return the prospective document snapshot
and `saved:false`, without creating or modifying a file or importer metadata. Existing-file revision
checks still apply. Revision fingerprints include live Drawing pixels (including pending GPU paint),
although JSON storage itself still omits those pixels.

`whimtex_document_json` / `WhimTexApi.DocumentJsonFile(requestPath)` uses a command envelope,
not another content format. See [agent API](AgentAPI.md#unified-json-documents).

Generate the schema through the connected Editor with `Documentation~/scripts/DocumentJsonSchema.cs`,
entry `DocumentJsonSchema.Run`. Do not edit the generated schema alone.
