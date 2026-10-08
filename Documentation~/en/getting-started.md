---
title: "Start here"
parent: "English"
nav_order: 1
lang: "en"
description: "Install WhimTex in Unity 6 and create your first layered texture or sprite. Paint, add images and save an editable TIFF ready to use in your game."
permalink: "/en/getting-started/"
translations: "en/getting-started.md,ru/getting-started.md,zh/getting-started.md"
next_page: "en/layers.md"
---

# Start here

## Install

Requires **Unity 6 or newer**. Open **Window → Package Management → Package Manager**,
choose **Install package from git URL**, and paste:

```text
https://github.com/DCFApixels/WhimTex.git
```

## Make your first image

1. Open **Window → WhimTex** and set the canvas **W / H**. **New** creates another empty document in a separate tab, keeping the current document open.
2. Click **+** at the bottom of Layers and choose **Drawing Layer**. This is the layer you can paint on.
3. Choose **Transform** (`T`) to arrange the image, or **Brush** (`B`) to paint.
4. Press `Ctrl+S` and choose where to save the document.
5. The document is saved as a TIFF and can be used like any other Unity texture, in a material or as a sprite.

Double-click the saved TIFF to continue editing. You do not need to export it first.
It opens in its own WhimTex window without replacing your current document.
If it is already open, Unity focuses that window instead of opening a duplicate.

Each WhimTex tab shows its document's name. New documents start as **Untitled**; after saving, the tab uses the filename. An asterisk marks unsaved changes.

To use an existing image, drag it from Project onto Canvas View. This adds a **File** layer linked to the texture.
Add a Drawing layer above it for touch-ups, or accept conversion to Drawing when painting on the File layer.

Dropping a texture into an empty document sets the canvas size to that texture's dimensions in Unity.
The same happens when you add **File** as the only layer and assign its first **Source Texture**.
Later texture replacements leave the canvas size unchanged. When dropping several textures at once,
the first one sets the size.

## Find your way around

The canvas is on the left. **Layers** is the list on the right; **Layer Settings** above it
shows the selected layer's controls. Drag a divider to make more room where you need it.

Choose a tool on the left toolbar; its options appear above the canvas.
Pan by holding the mouse wheel and dragging. Scroll to zoom; **Fit** shows the whole canvas.

| Tool | Key | Use it to… |
| :--- | :---: | :--- |
| Layer Select | `V` | Click visible pixels to select a layer. |
| Transform | `T` | Move, resize and rotate a layer. |
| Area Select | `M` | Select a rectangle or ellipse. Hold the tool button to choose the shape. |
| Polygonal Lasso | `L` | Select an area by clicking around its outline. |
| Brush / Pencil | `B` / `P` | Paint soft strokes or crisp pixels. |
| Fill | `G` | Fill an area with color. |
| Zoom | `Z` | Zoom in or frame an area. |

## Make the workspace comfortable

The **gear button to the right of Export** opens User Settings, where you can change the transparency checkerboard's colors
and size, or enable **Clean Canvas View Background** to hide the background logo and shadow. At the bottom of this settings window,
**Reset WhimTex Settings…** restores the workspace preferences after confirmation, without deleting your documents or preset files.

To keep a layer's settings in a separate window, use **layer ⋮ → Properties**.

## Open an existing image

In **User Settings → Open Images**, choose what a double-click in Project opens:

- **Tiff Documents Only:** WhimTex TIFF documents.
- **All Supported Images** (default): also PNG, JPEG, BMP, TGA, EXR, ordinary TIFF and Texture2D `.asset`; not PSD.

WhimTex TIFF documents reopen with their layers. For ordinary images, **Open As** chooses the source:

| Mode | Use it when… |
| --- | --- |
| **Drawing** | You want an editable copy that you can paint on. |
| **File** | You want a layer linked to the source texture. |

{: .warning }
**Save can overwrite the source.** Double-clicking an ordinary PNG, JPEG, TGA, EXR or Texture2D `.asset` opens it as a new document linked to that image, even with **Open As → Drawing**.
While there is one top-level layer (a group also counts as one), **Save** updates the original image.
With several top-level layers, Save asks for a TIFF; returning to one layer before saving a TIFF makes source-saving possible again.
Use **Save As** to create a TIFF without changing the source or its import settings. Other image formats require Save As.
Dragging a texture into a new document instead adds a File layer and does not bind Save to that source image.

**Source resolution.** PNG, JPEG, BMP, TGA and EXR use the original file dimensions and pixels, even if Unity imports a smaller or compressed texture.
This applies to File rendering, opening as Drawing, and converting File to Drawing. Other formats use the imported texture.
EXR decoding is supported in the Windows, macOS and Linux Editor.
