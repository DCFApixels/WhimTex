---
title: "Save and export"
parent: "English"
nav_order: 13
lang: "en"
permalink: "/en/saving/"
translations: "en/saving.md,ru/saving.md,zh/saving.md"
previous_page: "en/post-fx.md"
next_page: "en/tiff-format.md"
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

## Texture and sprite settings

TIFF is WhimTex's main document format: one file contains the saved image and editable layers.
Select the saved TIFF in Project, or click **Output** beside W/H, to open its standard Unity
Texture Importer. Save a new document first.

- **Texture Type:** choose **Default** for a texture or **Sprite (2D and UI)** for sprites.
- **sRGB (Color Texture):** use it for color images; data maps need the appropriate linear settings.
- **Alpha Is Transparency:** reduces colored fringes around transparent edges without removing alpha.
- **Generate Mip Maps**, **Filter Mode**, **Wrap Mode** and **Aniso Level:** control how Unity samples the imported texture.
- **Max Size**, format and compression: configure the default settings, then enable platform overrides where needed.

Click **Apply** in the Inspector to apply import settings. They affect the texture used by Unity,
not the document's editable layers or canvas size. For HDR TIFF, WhimTex uses linear data and disables sRGB.

### Sprite slicing

Install **2D Sprite** (`com.unity.2d.sprite`) through Package Manager if Sprite Editor is unavailable.

1. Select the saved TIFF and set **Texture Type → Sprite (2D and UI)**.
2. Choose **Sprite Mode → Single** for one sprite or **Multiple** for a sprite sheet, then **Apply**.
3. Open **Sprite Editor**. Use **Slice** or draw rectangles, then set names, pivots and borders.
4. Click **Apply**, expand the TIFF in Project and use its sprites.

For 9-slice, set the sprite borders and use **Mesh Type → Full Rect**; in a uGUI Image, choose
**Image Type → Sliced**. After changing the canvas size, check that the sprite rectangles still fit.

## Saved image precision

**Precision**, beside the canvas size, controls the image stored in the TIFF:

- **Auto:** 8 bits per channel for values within 0–1; Float32 when the result needs HDR.
- **8-bit:** always 8 bits per channel; values outside 0–1 are clipped in the saved composite.
- **Float32:** preserves fine differences in gradients and height maps, including within 0–1; files may be larger.

This is separate from GPU compression in the Inspector. Working rendering uses half-float,
so Float32 cannot restore precision already lost. Drawing layers retain their own pixel format.

## Live Update and saving

[Live Update](preview.md#see-your-paint-on-a-model) shows unsaved edits on objects using the TIFF.
One document can publish live updates at a time. During the session the texture is uncompressed,
and Read/Write is enabled if needed; its original settings are restored when the session ends.
The temporary Read/Write change appears in `.meta`.

Saving briefly pauses Live Update and resumes it after import. Closing or switching the document,
script reload and external reimport end the session. Building a Player also stops it and uses the
last saved TIFF; save your edits before building and enable Live Update again afterward.
If the texture cannot be restored, the build stops with an error.

Live Update supports 2D **Default** and **Sprite** imports. Crunch and other texture types update
when saved. Final compression and import processing can look different from the live preview.

## Protect the editable document

Keep the TIFF and its `.meta` together; move or rename the asset inside Unity to preserve references.
Do not resave the TIFF in another image editor: it may remove the editable layers.
See [TIFF document](tiff-format.md) for what the file contains.

Apply pending Shader FX code before saving. If the file changed externally, reopen it or use
**Save As**. If missing types, fields or referenced assets block saving, restore the required
package or assets and reopen the document. If Unity reports an import error after a successful
write, fix it and save again to retry the import.

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

## Migrate an old document

Legacy `.asset` documents can be opened, but cannot be saved back to that format.
Use **Save As** in WhimTex, or select the old asset and choose
**Assets → WhimTex → Migrate Legacy .asset to TIFF…**.

Migration copies editable layers and Drawing pixels into a new TIFF; the original asset and its GUID
remain unchanged. Existing materials and File layers still reference the old output: assign the
new TIFF where needed and review its import settings. Use the TIFF Inspector for future output settings.

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
