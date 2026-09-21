---
title: "Save and export"
parent: "English"
nav_order: 13
lang: "en"
permalink: "/en/saving/"
translations: "en/saving.md,ru/saving.md,zh/saving.md"
previous_page: "en/post-fx.md"
next_page: "en/shortcuts.md"
---

# Save and export

Save your document to keep its layers editable and use the result directly in Unity.
Export only when you need a separate image file.

## Save a document

Press `Ctrl+S`. The first save asks for a location; later saves update the same file.
**Save As** makes a separate copy. If you close a document with changes,
you can save, discard them or cancel closing.
An untouched new document closes without a prompt. Deleting the last layer is still a change and can be saved.

The saved asset is ready to use as a **texture**. Double-click it to continue editing;
if it is already open, WhimTex focuses that window.

Unity normally shows the **last saved image**. Enable [Live Update](preview.md#see-your-paint-on-a-model)
to see edits on a model before saving. If a linked texture changes, save the document again to update its output image.
File layers keep their links to source textures; keep those sources in the project.

## TIFF documents (experimental branch)

New documents are saved as **Name.tiff**: one editable document that Unity imports as a texture.
Select it in Project, or click **Output**, to configure mipmaps, compression, sprites and platform overrides in Unity's standard Inspector. TIFF uses no separate settings window; an unsaved document must be saved first.
For sprites, select **Sprite (2D and UI)** there and use the standard Sprite Editor.
Keep the TIFF and its `.meta` together; moving the asset within Unity preserves its link to the open document.
Do not resave the TIFF in another image editor: that can remove the editable layers.

For the byte-level layout, block directory, lazy Drawing loading and integrity checks, see the
[TIFF document format](../TIFF_FORMAT.md) technical reference.

**Precision**, next to the canvas size, selects the saved image precision:

- **Auto** — 8 bits per channel for the ordinary range; Float32 when the result needs HDR.
- **8-bit** — always 8 bits per channel; values outside 0–1 are clipped only in the output image.
- **Float32** — retains fine differences even within 0–1, for example in smooth gradients or height maps. Files may be considerably larger.

This controls the source TIFF, not GPU compression. Working rendering remains half-float;
Float32 cannot recover precision already lost. Drawing pixels keep their own storage format.
Unity's Texture Importer controls GPU compression, resizing and platform overrides for the imported TIFF.

**Live Update** uses the imported TIFF and requires a readable, uncompressed texture while the session is active.
Read/Write is enabled only when needed and restored when the session ends; the `.meta` is temporarily modified.
Closing/switching the document, script reload or external reimport stops the session. Saving another document does not affect it.
Building a Player also stops Live Update and uses the **last saved TIFF**, without saving or discarding your pending edits. Enable Live Update again manually afterward. If the texture cannot be restored, the build is stopped.
If Unity reports an import error after saving, the saved TIFF is retained. Fix the import error and save again; WhimTex retries the import even when the file contents are unchanged.
Saving the active document briefly pauses Live Update and resumes it after import, without switching Read/Write off and on.
Live Update supports 2D **Default** and **Sprite** imports; Crunch and other texture types update on Save instead.
This experimental TIFF path currently allows one Live Update session at a time. Final import processing may differ from the live preview.

Missing types, fields or referenced assets produce a warning and block saving to prevent data loss.
Restore the required package/assets and reopen the document. LDR saves follow the texture's sRGB setting;
HDR TIFF stores linear values and disables sRGB. Alpha is never sRGB-encoded.

Apply any pending Shader FX code before saving. If the TIFF changed outside the current editing session, reopen it or use **Save As**; WhimTex will not overwrite the external version.
Saving a legacy `.asset` as TIFF copies its editable Drawing pixels and leaves the original asset intact. References to its old output are not reassigned automatically.
PNG/EXR export remains ordinary image export, without editable layers.

### Long operations and limits

Long saves and opens show cancellable progress. Cancelling a save keeps the previous TIFF and your current edits;
allow the processing step already in progress to finish. The final file replacement and Unity import cannot be cancelled.
This is not background editing: the document cannot be edited during the operation, and rendering/import may briefly block the interface.

TIFF document limits: each canvas dimension up to **16384**, working half-float buffer and decoded output image each below **2 GiB**,
embedded pixels up to **256 MiB per texture** and **1 GiB total**, before file compression.
For example, one 8192×8192 RGBAHalf Drawing takes 512 MiB and cannot be saved; reduce its source resolution
or split the document. These are implementation limits, not a promise of smooth editing at the maximum sizes.

### Recovering an interrupted save

If a crash leaves a **.whimtex-tmp** file, choose **Tools → WhimTex → Recovery → Recover Staged TIFF…**.
WhimTex verifies it and offers to save a **new TIFF**. The original document and temporary file are kept.
Incomplete or damaged writes cannot be recovered this way. The recovered copy gets a new GUID and default import settings;
existing references are not reassigned to it. This is not autosave: edits made without starting Save cannot be recovered here.

If Live Update's temporary Read/Write setting was not restored after a failure, restore the missing asset or fix its import error,
then choose **Tools → WhimTex → Recovery → Retry Live Update Recovery**. Do not delete the recovery journal manually.

The sections below about linked images and embedded output settings apply to the **legacy `.asset` workflow**,
not to TIFF import settings. Legacy documents remain readable, but the window no longer creates new `.asset` documents.
To migrate one, select it in the Project window and choose **Assets → WhimTex → Migrate Legacy .asset to TIFF…**.
The original `.asset` and its GUID stay unchanged; migration creates a separate TIFF.

## Linked output image

Open **Output** beside the canvas W/H fields. In **Linked Output**, assign an existing PNG, TGA, JPG/JPEG or EXR image in **Assets**. Confirm the link: each document save replaces that image with the composition at full canvas resolution. Do not assign a source image you want to keep unchanged. PNG/TGA preserve alpha; JPG composites transparency over white; EXR preserves linear HDR. For LDR images, RGB is encoded according to the target's sRGB setting (normal maps use linear data).

The image keeps its GUID and import settings, including platform overrides and sprite slicing. Use **Texture Settings** to select it and edit its standard Unity Inspector. Embedded output settings below do not resize or compress the linked source image. The existing embedded texture and its Live Update are unchanged; the linked image updates only on Save.

**Clear** disconnects the output without deleting the image. Moving or renaming it inside Unity preserves the link. A missing, read-only or unsupported target blocks saving until corrected or cleared. **Save As** from an already saved document clears the link on the new copy so it does not overwrite the original document's output; the first save of a new document retains its assigned link.

## Embedded output settings

This section is compatibility documentation for legacy `.asset` files only. They can be opened and
inspected, but cannot be saved in place; use **Save As TIFF**. For new TIFF documents, configure
mipmaps, compression and platform overrides in Unity's standard Inspector.

**Alpha Is Transparency** extends edge RGB into transparent pixels to reduce filtering fringes; it never removes alpha. This processing happens on Save, not Live Update. **sRGB (Color Texture)** is a separate checkbox for RGBA32; HDR remains linear.

**Max Size** limits saved dimensions without changing the canvas; **Resize Algorithm** selects Mitchell or Bilinear. Sprite rectangles and borders scale with the output while metadata remains in canvas pixels. **Advanced** includes Box/Kaiser mipmap filtering, **Preserve Coverage** and **Alpha Cutoff**. **Read/Write** keeps a CPU copy; disabling it prevents Live Update and Sprite Editor until enabled and saved again. Live Update uses a fast preview path, so final resizing, alpha processing and mip filtering are applied on Save.

The resizable preview footer shows the last saved output over a checkerboard. Drag the **Preview** header to resize it independently of the settings scroll area. Continue dragging down past the minimum height to hide the preview entirely; drag the remaining header upward to restore it. Information is overlaid at the bottom: dimensions, format, color space, mip count, estimated GPU/CPU pixel storage and actual asset file size. The memory estimate excludes driver alignment and Unity object overhead. Asset file size includes the document and its layers, but not `.meta`; it is not the texture's runtime memory usage.

The obsolete custom output-compression panel is no longer shown. Configure compression and platform overrides in Unity's standard Texture Importer for the saved TIFF.

**Output Type** selects **Texture** (no sprite subassets) or **Sprite** (Single/Multiple sprites). Sprite remains the default for compatibility. In Texture mode sprite controls are hidden and sprite settings do not restrict saving. Applying Texture removes existing output sprites and breaks references to them; slicing and sprite settings are retained for switching back. The output texture keeps its reference.

For a legacy asset, click **Output** in WhimTex or **WhimTex Output Settings…** in its Inspector to inspect the compatibility settings. **Save As TIFF** creates the new document; the `.asset` is never overwritten. New TIFF documents use Unity's standard Inspector instead. Editing these fields does not bake a TIFF on every keystroke.

- **Texture:** Filter Mode, Wrap U/V, Aniso Level and Generate Mip Maps. Wrap affects texture sampling, not layer tiling.
- **Storage:** HDR Half (default), HDR Float, Linear RGBA32 or sRGB RGBA32. RGBA32 clamps values to 0–1; sRGB encodes RGB for color sampling. HDR Float changes storage precision, not the half-float working compositor's precision.
- **Sprite:** Pixels Per Unit, normalized Pivot with **Pivot Alignment** positions and manual coordinates, **Left / Bottom / Right / Top** Border fields in pixels, Full Rect/Tight Mesh Type, Extrude and Generate Physics Shape. Use Full Rect for 9-slice. Borders must fit within the canvas.

Invalid settings are highlighted with an explanation beside the field; **Apply & Save Output** remains disabled until they are corrected. Warnings about removing sprites appear only when the document actually has saved sprites.

**Revert** restores the last applied output settings and Filter Mode without changing layers, canvas size or sprite slices. It supports Undo. For older documents without a saved settings snapshot, the initially loaded settings are the starting point until the next save.

The **Preview** header offers **RGBA / RGB / Alpha** and a mip-level selector. These affect only the preview, work with Read/Write disabled, and never alter the saved image. **Preview requires Apply** means the displayed image is still the last saved output. The channel and mip selectors are controls; drag the remaining header area to resize or hide the preview.

**Generate Mip Maps** remains available for both output types. Compression is owned by Unity's Texture Importer and can be configured per platform; it does not alter the editable canvas.

Defaults preserve uncompressed HDR Half without mipmaps and a centered, full-rect sprite at 100 PPU. Read/Write is enabled by default. Platform overrides are not provided. These settings affect embedded output, not separate image exports.

### Sprite slicing (optional)

Install **2D Sprite** (`com.unity.2d.sprite`) through Unity Package Manager to enable slicing controls. WhimTex does not install it automatically.

1. Save the document, then open **Output → Sprite**.
2. Choose **Sprite Mode → Multiple**, then click **Sprite Editor**. This saves the current output before opening Unity's editor.
3. Use **Slice** or draw rectangles; edit each sprite's name, pivot and border, then click **Apply**.
4. Expand the document in Project to use its individual sprites.

**Single** uses the whole canvas. Switching to Single keeps the saved slices hidden so switching back preserves their references. Renaming or moving an existing slice also preserves its reference; deleting a slice removes its sprite and can break references to it. If resizing the canvas leaves a slice outside its bounds, fix the rectangles before saving.

Without 2D Sprite, slicing controls are disabled, but previously saved slices continue to be generated when saving. Custom outlines, skinning and secondary textures are not supported by this integration.

## Choose an export format

Use **Export** in the window header:

| Format | Best for |
| :--- | :--- |
| **PNG / TGA** | Color images with transparency. |
| **JPEG** | Images that do not need transparency; transparent areas become white. |
| **EXR** | HDR images. |
| **PSD** | Exchanging a layered image. |
| **Texture2D (.asset)** | A standalone Unity texture without the editable layers. |

EXR and Texture2D keep HDR brightness. PNG, JPEG, TGA and PSD use the ordinary color range.
Exporting does not change the original document's color range.

Preview settings such as zoom, EV and Post FX are not included in the saved or exported image.

## What remains editable in PSD?

PSD keeps the layer names, order, groups, visibility, opacity and supported blends.
Compatible color fills, gradients and outlines remain editable.
Other effects become pixels, and some blend modes can look different.

Read the export notes if the result differs. Keep the original WhimTex document
so you can still change all effects and their sources later.
