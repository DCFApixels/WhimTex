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
Documents with no layers close or switch without a save prompt, but can still be saved manually.

The saved asset is ready to use as a **texture**.
Expand it in Project to use **Output Sprite**. Double-click it to continue editing.

Unity normally shows the **last saved image**. Enable [Live Update](preview.md#see-your-paint-on-a-model)
to see edits on a model before saving. If a linked texture changes, save the document again to update its output image.
File layers keep their links to source textures; keep those sources in the project.

## Embedded output settings

Click **Output** in WhimTex, or select the saved asset in Project and click **WhimTex Output Settings…** in its Inspector. Both open the same settings window for the document. Use **Apply & Save Output**, or save in WhimTex, to rebuild the embedded texture and single **Output Sprite**. For an unsaved document, configure the settings here and save it in WhimTex first. Editing these fields does not bake the output on every keystroke.

- **Texture:** Filter Mode, Wrap U/V, Aniso Level and Generate Mip Maps. Wrap affects texture sampling, not layer tiling.
- **Storage:** HDR Half (default), HDR Float, Linear RGBA32 or sRGB RGBA32. RGBA32 clamps values to 0–1; sRGB encodes RGB for color sampling. HDR Float changes storage precision, not the half-float working compositor's precision.
- **Sprite:** Pixels Per Unit, normalized Pivot, Border in pixels (X/Y/Z/W = left/bottom/right/top), Full Rect/Tight Mesh Type, Extrude and Generate Physics Shape. Use Full Rect for 9-slice. Borders must fit within the canvas.

Defaults preserve HDR Half without mipmaps and a centered, full-rect sprite at 100 PPU. Read/Write stays enabled for Live Update restoration. Compression, platform overrides, custom mip filters and alpha-coverage preservation are not provided here. These settings affect the saved document's embedded output, not separate image exports.

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
