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
2. Click **+** at the bottom of Layers and choose **Drawing Layer**. If you already have a texture, you can drag it from Project onto the preview instead.
3. Choose **Transform** (`T`) to arrange the image, or **Brush** (`B`) to paint.
4. Press `Ctrl+S` and choose where to save the document.
5. Use the saved TIFF as a texture in Unity. For a sprite, select **Texture Type → Sprite (2D and UI)** in its Inspector and click **Apply**, then expand the TIFF in Project and use its sprite.

Double-click the saved TIFF to continue editing. You do not need to export it first.
It opens in its own WhimTex window without replacing your current document.
If it is already open, Unity focuses that window instead of opening a duplicate.

Each WhimTex tab shows its document's name. New documents start as **Untitled**; after saving, the tab uses the filename. An asterisk marks unsaved changes.

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

In **User Settings → Open Images**, **Double Click** chooses between **Tiff Documents Only** and **All Supported Images** (default) in the Project window. The latter also opens PNG, JPEG, BMP, TGA, EXR, ordinary TIFF and Texture2D `.asset` files, but not PSD. **Open As** chooses **Drawing** (editable copy of imported pixels) or **File** (reference to the imported texture). For PNG, JPEG, BMP, TGA and EXR, canvas and source sizing use the original file dimensions; other formats follow the imported texture. File layers backed by PNG, JPEG, BMP, TGA or EXR use a cached decode of the original source bytes for rendering, so Unity's import resizing and compression do not reduce their working resolution. Layered WhimTex TIFFs always open with their layers. Ordinary images open as new documents: with one layer, **Save** updates PNG, JPEG, TGA, EXR or Texture2D `.asset` sources; **Save As** creates a TIFF without changing the source import settings. Other formats use Save As. Opening PNG, JPEG, BMP, EXR or TGA as Drawing and converting these File layers to Drawing also reads the source bytes directly. Unity's EXR byte decoder is available in the Editor on Windows, macOS and Linux.

The **gear button to the right of Export** opens User Settings, where you can change the transparency checkerboard's colors
and size, or enable **Clean Preview Background** to hide the background logo and shadow. At the bottom of this settings window,
**Reset WhimTex Settings…** restores the workspace preferences after confirmation, without deleting your documents or preset files.

To keep a layer's settings in a separate window, use **layer ⋮ → Properties**.
