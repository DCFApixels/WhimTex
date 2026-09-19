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

New documents are saved as **Name.whimtex.tiff**: one editable document that Unity imports as a texture.
Select it in Project, or click **Output**, to configure compression, mipmaps, sprites and platform overrides in Unity's standard Inspector. TIFF uses no separate settings window; an unsaved document must be saved first.
For sprites, select **Sprite (2D and UI)** there and use the standard Sprite Editor.
Keep the TIFF and its `.meta` together; moving the asset within Unity preserves its link to the open document.
Do not resave the TIFF in another image editor: that can remove the editable layers.

**Live Update** also works with compressed output: the working image is temporarily uncompressed.
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

The sections below about linked images and embedded output settings apply to the **legacy `.asset` workflow**,
available through **Export → Compositor Asset, legacy (.asset)**, not to TIFF import settings.

## Linked output image

Open **Output** beside the canvas W/H fields. In **Linked Output**, assign an existing PNG, TGA, JPG/JPEG or EXR image in **Assets**. Confirm the link: each document save replaces that image with the composition at full canvas resolution. Do not assign a source image you want to keep unchanged. PNG/TGA preserve alpha; JPG composites transparency over white; EXR preserves linear HDR. For LDR images, RGB is encoded according to the target's sRGB setting (normal maps use linear data).

The image keeps its GUID and import settings, including platform overrides and sprite slicing. Use **Texture Settings** to select it and edit its standard Unity Inspector. Embedded output settings below do not resize or compress the linked source image. The existing embedded texture and its Live Update are unchanged; the linked image updates only on Save.

**Clear** disconnects the output without deleting the image. Moving or renaming it inside Unity preserves the link. A missing, read-only or unsupported target blocks saving until corrected or cleared. **Save As** from an already saved document clears the link on the new copy so it does not overwrite the original document's output; the first save of a new document retains its assigned link.

## Embedded output settings

**Alpha Is Transparency** extends edge RGB into transparent pixels to reduce filtering fringes; it never removes alpha. This processing happens on Save, not Live Update. **sRGB (Color Texture)** is a separate checkbox for RGBA32; HDR remains linear.

**Max Size** limits saved dimensions without changing the canvas; **Resize Algorithm** selects Mitchell or Bilinear. Sprite rectangles and borders scale with the output while metadata remains in canvas pixels. **Advanced** includes Box/Kaiser mipmap filtering, **Preserve Coverage** and **Alpha Cutoff**. **Read/Write** keeps a CPU copy; disabling it prevents Live Update and Sprite Editor until enabled and saved again. Live Update uses a fast preview path, so final resizing, alpha processing and mip filtering are applied on Save.

The resizable preview footer shows the last saved output over a checkerboard. Drag the **Preview** header to resize it independently of the settings scroll area. Continue dragging down past the minimum height to hide the preview entirely; drag the remaining header upward to restore it. Information is overlaid at the bottom: dimensions, format, color space, mip count, estimated GPU/CPU pixel storage and actual asset file size. The memory estimate excludes driver alignment and Unity object overhead. Asset file size includes the document and its layers, but not `.meta`; it is not the texture's runtime memory usage.

The bottom **Compression** panel also offers **Format: Automatic**, with **Compression: None / Low Quality / Normal Quality / High Quality**. For LDR, Low/Normal choose BC1 for opaque images or BC3 for transparency, using Fast/Normal encoder quality; High uses BC7 with Best quality. Opaque non-negative HDR uses BC6H; HDR with alpha or negative RGB remains uncompressed to preserve those values. None disables compression. These are save-time BC presets, not Unity's platform-dependent import modes. Manual formats retain their separate encoder-quality control.

**Output Type** selects **Texture** (no sprite subassets) or **Sprite** (Single/Multiple sprites). Sprite remains the default for compatibility. In Texture mode sprite controls are hidden and sprite settings do not restrict saving. Applying Texture removes existing output sprites and breaks references to them; slicing and sprite settings are retained for switching back. The output texture keeps its reference.

Click **Output** in WhimTex, or select the saved asset in Project and click **WhimTex Output Settings…** in its Inspector. Both open the same settings window for the document. Use **Apply & Save Output**, or save in WhimTex, to rebuild the embedded texture and single **Output Sprite**. The button is highlighted when the document has unsaved changes, including layer edits. For an unsaved document, configure the settings here and save it in WhimTex first. Editing these fields does not bake the output on every keystroke.

- **Texture:** Filter Mode, Wrap U/V, Aniso Level and Generate Mip Maps. Wrap affects texture sampling, not layer tiling.
- **Storage:** HDR Half (default), HDR Float, Linear RGBA32 or sRGB RGBA32. RGBA32 clamps values to 0–1; sRGB encodes RGB for color sampling. HDR Float changes storage precision, not the half-float working compositor's precision.
- **Sprite:** Pixels Per Unit, normalized Pivot with **Pivot Alignment** positions and manual coordinates, **Left / Bottom / Right / Top** Border fields in pixels, Full Rect/Tight Mesh Type, Extrude and Generate Physics Shape. Use Full Rect for 9-slice. Borders must fit within the canvas.

Invalid settings are highlighted with an explanation beside the field; **Apply & Save Output** remains disabled until they are corrected. Warnings about removing sprites appear only when the document actually has saved sprites.

**Revert** restores the last applied output settings and Filter Mode without changing layers, canvas size or sprite slices. It supports Undo. For older documents without a saved settings snapshot, the initially loaded settings are the starting point until the next save.

The **Preview** header offers **RGBA / RGB / Alpha** and a mip-level selector. These affect only the preview, work with Read/Write disabled, and never alter the saved image. **Preview requires Apply** means the displayed image is still the last saved output. The channel and mip selectors are controls; drag the remaining header area to resize or hide the preview.

**Generate Mip Maps** is available for both output types. **Compression** offers None, BC1, BC3, BC7 and BC6H with Fast/Normal/Best quality. BC1/BC3/BC7 require RGBA32 storage; BC6H requires HDR and stores non-negative RGB without alpha. BC1 does not preserve full alpha; use BC3 or BC7 for transparency. Canvas dimensions must be divisible by four. Compression applies on Save, not to the editable canvas. Compressed output updates on Save; Live Update requires Compression None and another save. BC formats require a compatible target device; no automatic platform conversion is performed.

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
