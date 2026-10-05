---
layout: default
search_exclude: true
title: PSD export
parent: Technical reference
nav_order: 6
lang: en
permalink: /reference/psdexport/
---

# Layered PSD export
{: .no_toc }

<details markdown="1">
<summary>On this page</summary>

- Contents
{:toc}

</details>

Choose **Export → Layered PSD (.psd)**. The source document is not converted or saved by this operation.
Hidden layers and nested folders are included. A compatibility image is rendered from the original
composition, independently of the exported editable layer representation.

## Conversion policy

| Source | PSD representation |
| :--- | :--- |
| Groups | Nested pass-through or isolated folders; blend, opacity, visibility and names preserved. Empty groups are retained. |
| Drawing / File | Raster layer at canvas resolution, with transform, sampling, tiling and FX baked into pixels. |
| Color Fill without FX | Editable solid-color fill; rendered alpha becomes a layer mask, preserving color alpha and transformed boundaries. |
| Gradient without FX | Editable color/opacity stops, type, angle, scale and offset where compatible. Clip boundaries become a mask. |
| Compatible Outline | Separate layer in its original stack position, with Fill 0% and an editable stroke effect. Its pixels hold a snapshot of the input alpha. |
| SDF / other procedural cases | Raster layer with the original transform and FX applied. |
| Shader / Material FX | Baked into the owning layer. No shader code is embedded. |

Layer opacity remains a separate property. Names support Unicode; stacking order and hierarchy are retained.
Source File textures are embedded as rendered pixels, not external links or embedded source documents.
Raster transforms are applied only to the exported pixels; they do not remain editable transform matrices.

### Gradients

Linear gradients support position, pivot, rotation and nonzero positive/negative scale.
Radial, angular and diamond variants support positive uniform scale and rotation; Square maps to a rotated diamond.
Geometry and interpolation can differ, particularly on non-square canvases.
Fixed interpolation, tiled Repeat/Mirror transforms, repeated angular gradients, degenerate transforms,
anisotropic non-linear gradients and gradients with FX are rasterized.
Clip and Source/clamped transforms are supported. Native gradient alpha is kept in opacity stops;
the additional mask represents only canvas-shape coverage.

### Outline

An untransformed Euclidean Outline without FX, with width greater than zero and at most 250 pixels,
exports as a native stroke. Color, alpha, width and Inside/Outside/Center remain editable.
Distance metric details and softness have no exact equivalent and are approximated by the stroke renderer.
Other Outline settings are rasterized.

The separate effect layer preserves its name, folder, blend, opacity and order, even for a Specific target
elsewhere in the tree or a group target. This is a **snapshot**, not a live link: changing the source layer
in the PSD does not update this alpha automatically. Target resolution follows compositor rendering,
including disabled or missing inputs. No source layer is removed or merged into another layer.

### Blending and compatibility

Standard supported blend modes are mapped directly. Add maps to Linear Dodge.
Linear Light Add/Sub uses Linear Light, Negation uses Difference, and Overwrite uses Normal as approximations.
None is exported hidden because it is a no-op. Each affected layer is listed in export notes.
Background-dependent modes cannot always be baked independently while retaining an editable stack.

The merged image retains a clamped copy of the compositor result, subject to 8-bit merged-alpha matte rounding.
HDR values and extended blending cannot be fully represented in this 8-bit format; export notes identify affected layers.
Applications that recomposite the editable stack can produce a different result. Layer-aware Unity import
does not necessarily reproduce every blend or layer effect; ordinary texture import uses the merged result.
Export does not install a layered importer or change the selected importer type.

Limits: RGB, 8 bits per channel, dimensions 1–30000, at most 32767 records (a group uses two),
and files/sections below 2 GB. An empty document receives one transparent Canvas layer so merged alpha
remains explicit. The native WhimTex TIFF remains the authoritative, fully editable source. Convert old `.asset` documents to TIFF in WhimTex 0.12.5 before upgrading; the current version does not open that format.

## Editor-side API

```csharp
PsdExportReport report = WhimTexPsdExporter.Export(
    document,
    absolutePsdPath,
    overwrite: false,
    progress: (stage, fraction) => { /* Optional progress or cancellation. */ });

foreach (string note in report.notes)
    UnityEngine.Debug.Log(note);
```

Call on the Editor main thread, with graphics available and a compiled package. Finish any active
painting stroke before exporting. The window does this automatically. The API does not import files,
save or normalize the source asset, alter Undo, or select assets. `overwrite` defaults to false.
The destination directory must exist. Throw `OperationCanceledException` from the progress callback
to cancel; progress is reported between raster layers and before final replacement, not per pixel.
Writes use a unique temporary sibling and replace the destination only after successful completion.
This API is separate from the Pipeline command adapter; there is no new CLI command.

## Verification

Use the independent structured scenarios in the [test runner guide](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/RUNNING_TESTS.md):

```text
node Tests~/scripts/run-tests.mjs --review --id psd-writer-v2
```

Run the reviewed selection with its current fingerprint and explicit Unity project. The
format writer is compiled through the connected Editor, not an archived standalone project.
An optional independent reader check accepts a separately installed `ag-psd` module through
`WHIMTEX_PSD_READER`; it is a test-only tool, not a package dependency:

```text
node Tests~/scripts/run-tests.mjs --review --id psd-reader-roundtrip-v2
```

The opt-in `psd-export-v2` scenario creates temporary in-memory documents and PSDs only
under a unique `Temp/WhimTex/` folder, checking real rendering,
source preservation, overwrite protection, cancellation and cleanup. It does not save Unity assets.
The reader workflow generates its own GUID fixture through the native writer before decoding it.
The reader requires existing human authority and `--allow-effects temp-files`; the export
scenario additionally declares `user-state`. Acknowledge all effects of the selected scenario.
