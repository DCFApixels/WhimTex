---
layout: default
search_exclude: true
title: TIFF document format
parent: Technical reference
nav_order: 2
lang: en
permalink: /reference/tiff-format/
---

# TIFF document format
{: .no_toc }

WhimTex stores a new editable document as one `*.tiff` file. The file is both a normal
Unity-importable texture and a document container. The visible TIFF image is the last saved
composite; the editable model and Drawing data are stored after the image data in a WhimTex
container.

This page describes the current format implemented by the package. It is an implementation
reference, not a promise that arbitrary image editors will preserve the document payload.

## Physical layout

```text
TIFF header, directory and image strips
  └─ native composite image used by Unity's TextureImporter
WHIMTEXD container
  ├─ magic + container version + block count
  ├─ block directory (name, compression, raw length, stored length)
  ├─ stored block bytes
  └─ integrity:sha256 manifest
footer: int64 container length + WHIMTEXD
```

The container is a binary appendage, not a ZIP archive and not a second Unity asset. The TIFF
part remains a valid image for Unity. The footer gives WhimTex the exact start and length of the
appendage without scanning the whole file. A cheap document check reads the TIFF signature and
footer; the importer also keeps a `whimtex.document` marker in `.meta` after import.

The image strips use the TIFF carrier's normal encoding. WhimTex writes LDR data as 8-bit samples
and can write Float32 samples for HDR or explicit `Precision → Float32`. The RGB sRGB flag is stored
in the small `carrier` block; alpha is always linear. GPU compression, mipmaps, sprite slicing and
platform overrides belong to Unity's normal Texture Importer and are not part of this container.

## Blocks and model

The first logical block is `document`. It contains the tagged WhimTex model (version 1): layers,
groups, transforms, settings, gradients, FX source and references. Drawing layers use separate named
pixel blocks. Other embedded textures may use their own blocks. Block names and lengths are recorded
in the directory before the block payloads, so the reader can validate bounds before allocating data.

Each block is either stored raw or with raw Deflate. The directory records which encoding was used;
there is no assumption that compression makes a block smaller. The optional `integrity:sha256` block
contains SHA-256 digests of the stored bytes. It detects truncation or accidental modification; it is
not a cryptographic signature and does not protect against deliberate replacement.

## Lazy Drawing loading

Opening a TIFF reads the footer, directory and model first. Drawing pixel blocks remain as stream-backed
metadata until a render, preview, edit or save actually needs them. Only the requested block is read and
inflated, then its checksum is verified. Unity `Texture2D` objects are created on the main thread;
container I/O and byte-level verification can run in the streaming operation scope.

This keeps a large document from materializing every Drawing layer at once, but it does not make a
large render free: a composite or a changed Drawing layer still needs its working pixels and GPU/CPU
resources. If the file changes after opening, deferred reads are rejected and the document must be
reopened so that the model and its blocks come from one revision.

## Save and compatibility rules

Saving is staged to a sibling temporary file, flushed, validated and atomically replaced. The previous
TIFF remains in place until the commit succeeds. An unchanged document can reuse verified compressed
blocks and skip composition/import work when there are no external inputs or time-dependent effects.

Unknown serialized fields, missing types or unresolved references block saving rather than silently
discarding data. Legacy `.asset` documents remain readable for migration and inspection, but new
documents are TIFF-only and the old asset is never overwritten by migration. Ordinary PNG, JPEG, TGA,
EXR and PSD export produces flattened/export files; those exports do not carry editable WhimTex layers.

Do not resave a WhimTex TIFF in an external image editor. Such an editor may rewrite the TIFF and drop
the trailing container, leaving only the composite image. Use **Save As TIFF** or the recovery command
to produce a new WhimTex document instead.

## Limits and tooling

The current reader limits the file to 4 GiB, the container to the documented block and model budgets,
and each canvas side to 16384 pixels. These limits are validated before allocation. Agents can inspect
the block directory without materializing the model with `whimtex_storage_inspect`, validate structure with
`whimtex_document_validate`, and compare model/storage/render output with
`whimtex_document_compare`.

For path-based authoring and the window-independent live session, see the [Agent API](AgentAPI.md).
