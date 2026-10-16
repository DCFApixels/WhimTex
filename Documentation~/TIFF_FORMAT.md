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

TIFF has been WhimTex's main editable document format since version 0.11.0.
An image-backed document is stored as one `*.tiff` file. The file is both a normal
Unity-importable texture and a document container. The visible TIFF image is the last saved
composite; the editable model and Drawing data are stored after the image data in a WhimTex
container.

This page describes the current format implemented by the package. It is an implementation
reference, not a promise that arbitrary image editors will preserve the document payload.

## Optional comments

Canvas annotations are stored in the optional `comments` block, separate from the main
`document` model and composite pixels. The block is omitted when the thread list is empty;
resolved threads remain stored. Missing blocks load as an empty list. A malformed known
block fails loading with a comment-specific diagnostic rather than silently losing notes.

Its payload is a version-2 tagged `List<WhimTexComment>`. Each thread has a stable `id`, permanent `number`,
bottom-left normalized canvas `anchor`, opaque RGB `color`, mutually exclusive `resolved` / `awaitingReview`
flags and `messages`. Both flags false means In Progress; `awaitingReview` means Awaiting Review;
`resolved` means Resolved. Missing flags default to false. Conflicting flags are rejected. These are explicit
document task states, never per-user read receipts; opening or reading a discussion does not modify them.
Each message contains
`id`, `author`, plain `text`, `createdUtcTicks`, `editedUtcTicks` and optional `attachments`. Attachments retain
a local document layer ID or an asset GUID plus local file ID, and a fallback name. They
reference content rather than embedding it; missing targets remain identifiable. A message
may carry `agentWorkId` and `agentContentHash` for the agent feedback contract.
Thread and message IDs are globally unique
within the document. Coordinates must be finite but may fall outside 0..1 after a crop.
Crop, flips and quarter turns remap anchors; Scale preserves normalized positions.

Assigned numbers are positive and unique within the document. Omitted/zero numbers are assigned once
in list order after the highest existing number. The main document's `commentNumberSequence` stores
the high-water mark, including after deleting every discussion; it is omitted from layer fragments.
An exhausted Int32 sequence rejects new threads instead of reusing a number. `editedUtcTicks` defaults
to zero (never edited); nonzero values must be at or after creation and within DateTime's UTC range.
Editor drafts, search/filter settings and card layout are not document data and are not written to TIFF/JSON.

Limits: 4096 threads, 1..128 messages per thread, 16384 UTF-16 code units per message and
128 per author. IDs, authors and messages must be nonblank valid Unicode; timestamps are
valid UTC ticks. Marker color components are finite 0..1, with alpha exactly 1. The default
for an omitted stored color is RGB (48, 96, 133) / 255. Optional `useAuthorColor` defaults to
false, preserving every older color as manual. New UI threads default to automatic color:
a deterministic nickname hue with the original blue's HSV saturation/value, or the creator's
chosen preference. `colorAuthorId` is an optional local profile GUID (32 hexadecimal characters,
null/empty when unassigned), not a Unity account ID. It keeps local ownership across nickname
changes. `color` retains the latest portable snapshot: other profiles keep it; matching local
profiles refresh it on opening the document or changing preferences. Manual colors remain fixed.
Messages separately retain optional `authorId` (the same local profile GUID format) and
`authorColor` (opaque RGBA in 0..1). An omitted/all-zero `authorColor` means no saved snapshot;
the displayed color resolves from the matching local profile, then the saved snapshot or the
author-name hue. Older messages can match the root thread's profile or, without an ID, the current
nickname. This colors the stripe beside the author independently of manual thread colors.
New UI messages keep this identity and color across renaming, replies, edits, Undo and saves.
The encoded block is limited to 16 MiB and uses the normal
compression/integrity handling. Draft text is UI-local until Post/Save. Comments never
enter layer composition, thumbnails, image export or pixel clipboard data. Each message
allows at most 32 attachments; names are limited to 1024 UTF-16 code units.

Selection attachments contain a `WhimTexCommentMask` in the same optional `comments` block,
with no external texture asset. Version 1 stores nonzero-bounds-cropped R8 coverage in
`coverageDeflate` (Deflate bytes as base64), decoded-byte `sha256` (SHA-256/base64), width/height,
a document-unique GUID `id` and affine `localToCanvas`. Rows are bottom-first; values 0..255
retain exact coverage. The map places local UV in canvas UV and changes with Crop/Orientation;
Scale changes the canvas size while keeping the UV map. Original pixels, including those outside
a cropped canvas, survive. Restore/generation uses nearest source samples at canvas pixel centres.
Decoding validates size, checksum, nonempty cropped bounds and placement. Unsupported mask versions,
corrupt data and budgets fail explicitly. Limits: 16,777,216 source pixels per mask, 67,108,864 total,
16384 per axis, plus the existing 16 MiB encoded comments block. Layer/asset/mask are mutually
exclusive per attachment. JSON retains the same mask data; layer fragments omit it with comments.

Optional `agentReceipts` belong to the main document model. They record completed work/request
IDs, canonical request hashes, result JSON and UTC ticks. The limits are 2048 entries,
262144 characters per result and 4 MiB of combined metadata. Empty histories are omitted
from document JSON; layer fragments omit them. They do not force an empty comments block
to be written. See [agent work recovery](AgentAPI.md#comment-discussions).

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

The current container and tagged model both use version **2**. Version-1 documents are
unsupported and rejected explicitly; there is no automatic conversion in this checkout.
Channel routing is stored as `Layer.channelMapping` / `LayerChannelMapping`.

The container is a binary appendage, not a ZIP archive and not a second Unity asset. The TIFF
part remains a valid image for Unity. The footer gives WhimTex the exact start and length of the
appendage without scanning the whole file. A cheap document check reads the TIFF signature and
footer; the importer also keeps a `whimtex.document` marker in `.meta` after import.

The image strips use the TIFF carrier's normal encoding. WhimTex writes LDR data as 8-bit samples
and can write Float32 samples for HDR or explicit `Precision → Float32`. The RGB sRGB flag is stored
in the small `carrier` block; alpha is always linear. GPU compression, mipmaps, sprite slicing and
platform overrides belong to Unity's normal Texture Importer and are not part of this container.

## Blocks and model

The first logical block is `document`. It contains the tagged WhimTex model (version 2): layers,
groups, transforms, settings, gradients, document color history, FX source and references. Drawing layers use separate named
pixel blocks. Other embedded textures may use their own blocks. Block names and lengths are recorded
in the directory before the block payloads, so the reader can validate bounds before allocating data.

Each block is either stored raw or with raw Deflate. The directory records which encoding was used;
there is no assumption that compression makes a block smaller. The required `integrity:sha256` block
contains SHA-256 digests of the stored bytes. It detects truncation or accidental modification; it is
not a cryptographic signature and does not protect against deliberate replacement.

## Lazy Drawing loading

Opening a TIFF reads the footer, directory and model first. Drawing pixel blocks remain as stream-backed
metadata until a render, preview, edit or save actually needs them. Only the requested block is read;
its stored-byte checksum is verified before decompression. Unity `Texture2D` objects are created on the main thread;
container I/O and byte-level verification can run in the streaming operation scope.

This keeps a large document from materializing every Drawing layer at once, but it does not make a
large render free: a composite or a changed Drawing layer still needs its working pixels and GPU/CPU
resources. If the file changes after opening, deferred reads are rejected and the document must be
reopened so that the model and its blocks come from one revision.

## Save and compatibility rules

Saving is staged to a sibling temporary file, flushed, validated and atomically replaced. The previous
TIFF remains in place until the commit succeeds. An unchanged document can reuse verified compressed
blocks and skip composition/import work when there are no external inputs or time-dependent effects.

Unknown serialized fields, missing types or unresolved references appear in a persistent window warning
on open. Saving blocks data loss by default. Interactive Save/Save As offers a recovery copy, cancellation,
or explicitly accepted saving of the loaded data only; the latter can discard unread data. A copy preserves
the original for recovery, not unread data inside the new file. C# callers can opt in with
`Save(..., allowDataLoss:true)`; automatic saves remain protected. Serialized vector tags must match
their field types exactly; mismatches are diagnosed, not widened or narrowed.
Legacy `.asset` and version-1 TIFF documents are unsupported. A matching older checkout
is required to edit them; this checkout does not provide migration.
[Unified JSON](JSON_FORMAT.md) is also supported for editable settings without Drawing pixels or an image carrier.
Ordinary PNG, JPEG, TGA,
EXR and PSD export produces flattened/export files; those exports do not carry editable WhimTex layers.

Do not resave a WhimTex TIFF in an external image editor. Such an editor may rewrite the TIFF and drop
the trailing container, leaving only the composite image. Use **Save As** (TIFF) or the recovery command
to produce a new WhimTex document instead.

## Limits and tooling

The current reader limits the file to 4 GiB, the container to the documented block and model budgets,
and each canvas side to 16384 pixels. These limits are validated before allocation. Agents can inspect
the block directory without materializing the model with `whimtex_storage_inspect`, validate structure with
`whimtex_document_validate`, and compare model/storage/render output with
`whimtex_document_compare`.

For path-based authoring and the window-independent live session, see the [Agent API](AgentAPI.md).
