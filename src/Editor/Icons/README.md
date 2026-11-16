# Internal icon pool

Start here before drawing or adding an icon. This folder contains the existing
SVG/PNG files and procedural icon sources. Keep an icon in its current format;
moving it here does not require rasterizing it or adding a public API.

## Find an icon

| Purpose | Source |
| --- | --- |
| Math symbols, noise, pattern, shape, color, filter, normal, channels, logic, texture, code, graph, buffer, time, loop and light | [Drawn/WhimTexIcons.cs](Drawn/WhimTexIcons.cs) |
| Add, delete, group, eye, alpha, settings, transform, pivot, flip, rotation, text alignment, stroke joins/caps and Boolean operations | [Drawn/LayerActionIcon.cs](Drawn/LayerActionIcon.cs) |
| Canvas tools, shapes, selection, crop, vector tools, comments, light types and canvas panel buttons | [Drawn/WhimTexWindow.Icons.cs](Drawn/WhimTexWindow.Icons.cs) |
| Error, warning, info and the buffer cylinder on graph ports | [Drawn/ShaderGraphWindow.Icons.cs](Drawn/ShaderGraphWindow.Icons.cs) |
| Linked/unlinked fields | [Drawn/WhimTexLinkIcon.cs](Drawn/WhimTexLinkIcon.cs) |
| Empty layer indicator | [Drawn/WhimTexLayerEmptyIcon.cs](Drawn/WhimTexLayerEmptyIcon.cs) |
| Reverse gradient | [Drawn/WhimTexGradientWindow.Icons.cs](Drawn/WhimTexGradientWindow.Icons.cs) |
| Round/bevel corners | [Drawn/ShapeCornerSettingsView.Icons.cs](Drawn/ShapeCornerSettingsView.Icons.cs) |
| Image silhouette in edge selection | [Drawn/WhimTexEdgeSelector.Icons.cs](Drawn/WhimTexEdgeSelector.Icons.cs) |
| FX color, distortion, gradient, lighting, mask, normal, pattern, tone and transform | [FX/](FX/) SVG files |
| Pencil, fill and healing brush | [Pencil.svg](Pencil.svg), [Fill.svg](Fill.svg), [HealingBrush.svg](HealingBrush.svg) |
| Tool cursors | `*Cursor.png` files in this folder |
| Window/application icon | [WhimTexIcon.png](WhimTexIcon.png); generated from the existing [brand master](../../../Documentation~/Images/whimtex-logo.svg) |

## Reuse

- Use the existing SVG/PNG or drawing implementation. Do not copy its geometry
  into another window. New icon resources belong in this folder.
- Shared drawn icons use `WhimTexIcons.Get(kind).Create()`; existing action icons
  also remain available through `LayerActionIcon`. The shared [WhimTexIcon](Drawn/WhimTexIcon.cs)
  element owns its own UI state; icon definitions can be shared.
- Some drawing sources remain private members of their original window, in
  partial files here. They keep their original state and callbacks. If another
  owner needs one, extract that specific drawing into a shared element here.
- [ShaderGraphCatalogIcon](Drawn/ShaderGraphCatalogIcon.cs) maps graph categories
  and node kinds to icons and category colors. The palette and dropdown draw
  full-height color strips on their rows, outside the reusable icon artwork.
- Keep existing `.meta` files and GUIDs when moving resources. Layout and hover
  styles that belong to a control stay with that control.

Preset-supplied icon parsing and rendering are separate infrastructure:
[ShaderIconMetadata](../../ShaderIconMetadata.cs) and [ShaderIconCache](../ShaderIconCache.cs).
