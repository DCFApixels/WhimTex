---
title: "Start here"
parent: "English"
nav_order: 1
lang: "en"
description: "Install WhimTex in Unity 6 and create your first layered texture or sprite. Paint, add images and save an editable asset ready to use in your game."
permalink: "/en/getting-started/"
alternate: "ru/getting-started.md"
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

1. Open **Window → WhimTex**, click **New**, and set the canvas **W / H**.
2. Drag a texture from Project onto the Preview, or click **Page +** at the bottom of Layers to create a Drawing layer.
3. Choose **Transform** (`T`) to arrange the image, or **Brush** (`B`) to paint.
4. Press `Ctrl+S` and choose where to save the document.
5. Use the saved asset as a texture in Unity, or expand it in Project and drag **Output Sprite** into a sprite field.

Double-click the saved asset to continue editing. You do not need to export it first.

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
| Rectangle Select | `M` | Select a rectangular area. |
| Polygonal Lasso | `L` | Select an area by clicking around its outline. |
| Brush / Pencil | `B` / `P` | Paint soft strokes or crisp pixels. |
| Fill | `G` | Fill an area with color. |
| Zoom | `Z` | Zoom in or frame an area. |

## Make the workspace comfortable

The **gear button to the right of Export** opens User Settings, where you can change the transparency checkerboard's colors
and size, or enable **Clean Preview Background** to hide the background logo. At the bottom of this settings window,
**Reset WhimTex Settings…** restores the workspace preferences after confirmation, without deleting your documents or preset files.

To keep a layer's settings in a separate window, use **layer ⋮ → Properties**.
