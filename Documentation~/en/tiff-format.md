---
title: "TIFF document"
parent: "English"
nav_order: 13.1
lang: "en"
permalink: "/en/tiff-format/"
translations: "en/tiff-format.md,ru/tiff-format.md,zh/tiff-format.md"
previous_page: "en/saving.md"
next_page: "en/shortcuts.md"
---

# TIFF document

Save as TIFF when you need both an editable WhimTex document and a texture for Unity.
Double-click the same file to edit its layers; assign it to a material to use the saved image.
You do not need a separate export for either action.

## What the file contains

One `.tiff` contains the last saved composition, editable layers and FX, and Drawing pixels.
File layers still reference their source textures: keep those assets in the project.

**Precision** controls the saved image's 8-bit or Float32 precision. Mipmaps, compression,
sprite slicing and platform overrides are ordinary Unity import settings.
See [saving and texture settings](saving.md) for choosing them.

## Loading and saving

Press **Ctrl+S** to update the document; **Save As** creates another TIFF. Unity normally
displays the last saved image. [Live Update](preview.md#see-your-paint-on-a-model) shows unsaved edits.

Large Drawing layers load as they are needed. Saving prepares and checks a temporary file
before replacing the previous TIFF. If the source changed externally, reopen it or use
**Save As** rather than overwriting a different revision.
For warnings about unread data or an interrupted save, see [document protection and recovery](saving.md#protect-the-editable-document).

## Compatibility

{: .warning }
Do not resave a WhimTex TIFF in another image editor: it may leave only the image and remove
the editable layers. Export a separate image for that workflow, and keep the original TIFF and its `.meta`.

Old `.asset` and version-1 documents require the WhimTex version that supports them;
the current version does not convert them automatically.

[JSON](saving.md#json-documents) keeps editable settings but no Drawing pixels or Unity texture.
PNG, JPEG, TGA and EXR keep only the exported image. PSD keeps some layers and effects;
keep the original TIFF when you need all WhimTex settings.

For the binary layout, checksums, limits and loading rules, see the
[TIFF document format technical reference]({{ '/reference/tiff-format/' | relative_url }}).
