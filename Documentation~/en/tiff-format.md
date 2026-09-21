---
title: "TIFF document"
parent: "English"
nav_order: 13.1
lang: "en"
permalink: "/en/tiff-format/"
translations: "en/tiff-format.md,ru/tiff-format.md,zh/tiff-format.md"
---

# TIFF document

WhimTex saves a new editable document as one `*.tiff` file. The file is a normal Unity texture
and an editable document at the same time: the visible TIFF image is the last saved composite,
while the layer model and Drawing data travel in an appended WhimTex container.

## What the file contains

The TIFF image comes first, so Unity can import it with the normal Texture Importer. After the
image, WhimTex writes a binary container with a `document` model block, separate Drawing pixel
blocks and a small carrier block. A directory records each block's name, compression and lengths;
the `integrity:sha256` manifest detects accidental truncation or modification. This is not a ZIP
file and it is not a second Unity asset.

The composite uses 8-bit samples for ordinary output and can use Float32 for HDR or explicit
`Precision → Float32`. Mipmaps, GPU compression, sprite slicing and platform overrides remain
normal Unity importer settings rather than document-layer data.

## Loading and saving

Opening reads the footer, block directory and model first. Drawing pixel blocks are loaded lazily
when rendering, previewing, editing or saving needs them; the whole document is not inflated at
once. Saving stages a sibling temporary file, validates it and atomically replaces the previous
TIFF. If the source changed externally, WhimTex refuses to combine revisions and asks you to
reopen or use **Save As**.

## Compatibility

Do not resave a WhimTex TIFF in an external image editor: it may discard the appended container and
leave only the composite image. Legacy `.asset` documents remain readable for inspection and
migration, but new documents are TIFF-only and migration never overwrites the legacy source.
PNG, JPEG, TGA, EXR and PSD are ordinary flattened exports and do not carry editable WhimTex layers.

For the complete byte layout, limits and block-level rules, see the [TIFF document format technical
reference]({{ '/reference/tiff-format/' | relative_url }}).
