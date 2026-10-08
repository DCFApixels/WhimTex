---
title: "Troubleshooting"
parent: "English"
nav_order: 16
lang: "en"
permalink: "/en/troubleshooting/"
translations: "en/troubleshooting.md,ru/troubleshooting.md,zh/troubleshooting.md"
previous_page: "en/automation.md"
---

# Troubleshooting

## The brush leaves no mark

1. Select a Drawing layer.
2. Check that **Brush** or **Pencil** is selected and the brush size is not too small; see [painting controls](painting.md).
3. Turn on **R / G / B / A** in the Canvas View footer and check the paint color's transparency; see [color settings](color.md).
4. If the canvas has a selection, [clear it](selection.md): strokes cannot reach outside it.
5. Check that the layer is visible and its opacity is not zero.
6. If Clipping Mask is enabled, check that its base layer is visible and contains an image.

On a transformed layer, make sure you are painting over the layer's image.

## The color looks wrong

Reset **EV** to 0 and turn on **RGBA**.
Disable **Post FX** to compare with the unprocessed image.
Check the layer's Blend and opacity, then its [color settings](color.md).

If you are making a normal map or another data texture, check the Encoding choice
in that layer's settings.

## An effect is empty or uses the wrong image

Check **Input / Target**, especially after moving layers.
**Previous** uses the layer immediately below the effect in the same group.
Use **Specific** if you want to keep the same source when rearranging the list.

A hidden source still works. For a group, keep the required children visible; see [Effect layers](effects.md#choose-what-the-effect-uses).

## FX shows a warning or error

Hover over the yellow marker beside the layer, or open the effect's **Code** section for details.
**Warning** leaves the FX running; **Error** skips it until you fix the problem and click **Apply**.
The same messages are available in Console and to a connected agent.

Before saving TIFF, apply pending FX drafts and fix FX errors. JSON can retain broken FX code and settings,
but not Drawing pixels. See [FX errors and warnings](shader-fx.md#compilation-warnings).

## Save warns about unread document data

Keep the original file. **Save a Copy…** writes the loaded parts and your edits to a new file;
it protects the original, but the copy may lose the unread parts. **Save Anyway** overwrites the original
with the same risk. **Cancel** returns to editing.

If you need the unread data, restore the required packages, types or assets and reopen the original.
Replacing a missing layer type is a substitution, not recovery. See [document protection](saving.md#protect-the-editable-document).

## There are seams in the repeated image

Check which [repeat mode](symmetry.md) you are using.
For blur and Normal Map, set **Edges → Repeat** as well as enabling Tiled preview.
Tiled preview does not remove seams. Try [Make Seamless](effects.md#make-seamless) to join opposite edges.

## Unity shows an older image

Click **Save** in WhimTex after editing, including after changing a linked texture; see [saving](saving.md).

## Painting feels slow

Lower **Live Quality** in the Canvas View footer.
For a complex image, temporarily hide effects you do not need while painting.
Pencil uses full resolution regardless of **Live Quality**, keeping pixel work precise.

## Post FX looks different from the game

Check the camera or profile selection and make sure post-processing is enabled.
The Preview does not include the surrounding scene, and some effects are not supported.
See [Post FX](post-fx.md).

## Still stuck?

Include the package and Unity versions, steps to reproduce the problem and, if possible,
a small document you can share in a [bug report](https://github.com/DCFApixels/WhimTex/issues).
