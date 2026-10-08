---
layout: default
title: WhimTex JSON format
parent: Technical reference
lang: en
permalink: /reference/json-format/
---

# WhimTex JSON documents

`whimtex.document`, version **1**, is the shared editable format for `.json` files,
layer clipboard data and agent serialization. It stores settings, not rendered pixels.
TIFF remains the image-backed format; its tagged binary encoding remains version 1.
New saves and exports use `.json`; existing `.whimtex.json` names remain readable without renaming.
The old `whimtex.layers` clipboard envelope is unsupported. Paste it in 0.12.5 and save
as TIFF or export `whimtex.document` JSON before upgrading.
New exports and Copy as JSON use this format. Brush and gradient preset formats remain separate.

Compatibility during legacy cleanup covers files written by package **0.12.5**, not
its C# or agent APIs. Readers accept retired compositor output/slice metadata and
Drawing source-URL/revision bookkeeping at their original owner types; current writers
omit it. Unknown settings outside that explicit allowlist remain errors. The schema
marks `document.spriteSlices` as deprecated input-only metadata. The version-1 default
snapshot is unchanged, so omitted values in existing Compact files retain their meaning.

The layer FX list is `fx` in new JSON and TIFF writes. Readers also accept the old
`Layer.modifiers` field from existing files and convert it to `fx`, preserving list order
and shared references. Both names in one layer are rejected as ambiguous. This input
conversion is not a C# or agent API alias; new content must use `fx`.

## Write modes

| Mode | Stored values |
| --- | --- |
| `Full` | All persistent settings, including inactive values and defaults. |
| `FullOptimized` (default) | Active settings, including their defaults; inactive branches are omitted. |
| `Compact` | Same inactive-branch rules, additionally omitting version-1 default values, except source canvas dimensions. |

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

Omitted values use the frozen defaults in `WhimTexJsonDefaultsV1`, not the current UI factory defaults.
For insertion/paste only, omitted canvas dimensions instead inherit the corresponding destination axes.
Re-enabling an omitted feature restores those defaults, not the previously inactive user value.

## Envelope and model

Only `format`, `version` and `layers` are required at the root. `document` and every setting inside
it are optional. If present, `document` must be an object, not null; `{}` is valid.

- Opening or writing a document fills missing settings from version-1 defaults (canvas **512 × 512**).
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
  "version": 1,
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
Writers use the field's current component count. TIFF and JSON readers accept lossless float-vector
expansion: Vector2 to Vector3/Vector4 and Vector3 to Vector4, filling added components with zero.
Scalars, integer-vector conversions, narrowing and shortened colors/quaternions remain invalid.
Shader FX have `$type: "ShaderFX"`, source `code`, `parameters`, `active` and optional
`$name`. Shared FX use `$id`/`$ref`; these IDs are distinct from layer IDs.

### Open, insert and replace

There is no document/fragment discriminator. Exporting selected layers produces a document containing
only those layers, with source-canvas settings. The caller's operation determines how the content is used:

- Open restores the document, retaining layer IDs, output settings and History.
- Insert/paste adds layers with remapped IDs; it does not replace existing layers or apply source output settings.
- Replace changes only the explicitly selected layer through the API; content never requests replacement itself.

Root `kind` is rejected; 0.12.5 writers already omit it. The caller chooses the operation.
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
the FX during rendering. A failed Apply also skips an older compiled version. Successful Apply clears
the warning and resumes rendering. Compiler warnings without errors do not mark an FX unavailable.
Uncompiled/failed FX are marked beside their layer and in the FX section and effect headers, including
collapsed headers. Console failures are deduplicated per effect instance and source/diagnostic pair;
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
are retained, with the model's rendering clamps. The 0.12.5 reader normalizes zero Y axes and
negative Shape corner values into explicit coordinates; those sentinels are no longer runtime modes.
Saved manual FX parameters become `@param` declarations with their values and references preserved.
The read-only `declaredInCode` metadata distinguishes them from intentionally removed code declarations.
Manual scalar values outside their hard range become the effective clamped value used by 0.12.5.
Current writers do not emit `declaredInCode` or Shape's former uniform `roundness` field.
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
JSON and Drawing-omission warnings. Options use `Mode`, `AllowDrawingOmission` and `AllowDataLoss`
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
