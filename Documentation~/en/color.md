---
title: "Color, HDR and channels"
parent: "English"
nav_order: 11
lang: "en"
description: "Edit HDR textures and pack RGBA channels in Unity with WhimTex. Inspect individual channels, configure Mapping and control layer color and blend ranges."
permalink: "/en/color/"
translations: "en/color.md,ru/color.md,zh/color.md"
previous_page: "en/preview.md"
next_page: "en/post-fx.md"
---

# Color, HDR and channels

For ordinary painting, leave **HDR** off and all four **R / G / B / A** buttons on.
Use the controls below when you need extra brightness, channel masks or texture data.

## Choose and reuse colors

Click a color field or double-click a gradient color key to open the WhimTex Color Picker.
Choose a hue on the ring, then saturation and brightness in the central square.
Original and new colors appear at the top right; changes preview immediately.

Close the window to confirm. Press Escape to restore the opening color. There are no confirmation buttons.

### Enter a color

Use **RGB 0–255**, **RGB 0–1** or **HSV** for numeric entry, and **A** for transparency.
The selected numeric mode is remembered across pickers and Unity sessions; switching it does not change the color.
Channel sliders show gradients, with a checkerboard under alpha.

**Hexadecimal** accepts these formats, in either case and with or without `#`:

| Format | Example | Alpha |
| :--- | :--- | :--- |
| RGB | `FFAA66` or `FA6` | Preserved |
| RGBA | `FFAA6680` or `FA68` | Updated if editable |

Press Enter or leave the field to apply. The field always displays six RGB digits with a separate **#** prefix.

### HDR and viewing exposure

**HDR** unlocks RGB values above 1. Inputs restricted to Standard or HDR lock this switch.
Switching alone preserves the stored color; editing in Standard replaces it with a bounded color.

| Control | Effect |
| :--- | :--- |
| Exposure | Changes the actual HDR intensity in stops. RGB changes; alpha does not. |
| Preview EV | Changes display brightness only, from −10 to +10. Saved colors and numeric values do not change. |

Lower **Preview EV** to inspect bright colors. It affects the square, slider gradients and color/History swatches,
not the hue ring, screen eyedropper, checkerboards or alpha. Each new picker starts at 0.
Its footer matches the gradient editor; their exposure settings are independent.

### Sample with the eyedropper

The picker's eyedropper replaces the ring with a magnified pixel grid and a frame around the sampled pixel.
Choose a color or cancel to restore the ring. During sampling, Escape cancels only the eyedropper;
the picker stays open and retains its current color. If your Editor cannot show the magnifier, ordinary sampling still works.

Alt sampling in WhimTex also updates that window's open **primary brush color** picker, including numbers and markers.
Layer, gradient and secondary-color pickers are unaffected. Without a matching picker, Alt still changes the primary brush color.
Its white eyedropper cursor samples at the lower-left tip, without a separate arrow. History swatches use the same cursor.

### Reuse colors from History

**History** stores colors with the document. Confirmed colors go to the front; intermediate slider values are not recorded.
Confirming a color already in History moves it to the front without a duplicate, including when entered manually.
Matching includes alpha and HDR intensity. Fields outside a document have no History.

| Action | Result |
| :--- | :--- |
| Click a color | Apply it and move it to the first slot after **+**. |
| Click **+** | Add the current color without closing the picker. |
| Drag within History | Reorder colors. |
| Drag outside until the outline turns red, then release | Remove the color. Return inside or press Escape before release to cancel. |
| Right-click → Remove, or Delete/Backspace | Remove the selected swatch. |

Removing a swatch does not change layers that use that color. Explicit History actions remain even if you later
cancel color selection with Escape; canceled manual color edits are not recorded.

With **HDR off**, History hides colors with RGB outside 0–1 without deleting them.
In the gradient editor, filtering follows the selected color key's HDR mode; alpha does not affect filtering.

The gradient editor's History sits above **Presets**, without **+**.
Select a color key, then click a swatch to apply RGB and HDR intensity; opacity keys and the key's position stay unchanged.
The palette is unavailable for opacity keys and midpoints. Drag sorting and removal work as in the picker.

History and gradient Presets remember their expanded state between sessions.

## Inspect a channel

The footer channel buttons let you see parts of the image separately:

- **R, G or B alone:** show that channel in black and white. Keep A on to retain transparency.
- **A alone:** show transparency as black and white.
- **Several color channels:** keep their colors and hide the others.
- **A off:** view colors without transparency.

These buttons change only the view. For protected painting, use **Write R/G/B/A**
in the Canvas View header; see [painting](painting.md#steady-the-path-and-protect-channels).

### Color picker channel view

**Channels** in the color picker follows the R/G/B/A buttons in Canvas View for document colors (brushes, layers, gradient keys and FX). Turn it off for ordinary color display. The ring, square and slider gradients adapt; swatches compare the original color in the upper-left half with the channel view in the lower-right half. Their single alpha bar still shows actual transparency. RGB/HSV/HEX numbers and saved History colors remain original; editing is not restricted to visible channels.

This choice is remembered across color pickers. Interface colors such as guides, UV overlays and checkerboards do not adapt. The eyedropper magnifier always shows the actual screen pixels. Layer Preview channel buttons are independent and do not affect the picker.
HDR color fields keep their intensity gradients in both diagonal halves; the shared alpha bar still represents the stored alpha.

## Paint bright HDR colors

Enable **HDR** beside EV in the Canvas View footer to choose colors with RGB values above ordinary white (1).
This is useful for luminous details you want to use with bloom.

HDR values are real, but the current WhimTex editor previews are SDR, including the color picker and gradient editor. An HDR monitor alone does not make them display extra physical brightness. Lower **Preview EV** to inspect bright values without changing the color; intensity gradients in color fields are visual indicators, not HDR monitor output. The picker, gradient editor and Canvas View have independent exposure controls.

In **Layer Settings → Rendering**, choose **HDR** in the header dropdown
to let the layer retain that extra brightness. **Standard** is the usual choice for ordinary artwork.

The HDR button controls color picking; it does not convert your existing image.
Turning it off lets you paint with the color without its extra intensity.
Turning it back on restores that intensity unless you have edited the color in between.
The gradient editor has its own HDR switch for the selected color key; it does not follow the main footer's shared HDR preference.

## Fine-tune a layer's color range

You can leave **Rendering** closed for most work. Expand it to set the two ranges separately:

| Setting | Controls |
| :--- | :--- |
| Color Range | Whether the layer itself keeps extra brightness. |
| Blend Range | Whether its blend works with that extra brightness or within the ordinary range. |

The dropdown in the foldout header changes both settings together.
A blank value means the two are different.

Switching to Standard does not erase stored HDR colors.
Use **Convert to 8-bit** only if you want to permanently reduce the stored color range.

## Edit a gradient

New gradients use **Color Mode → Perceptual** and **Interpolation → Balanced**.
Pattern gradients explicitly start in Linear color mode. Each gradient keeps its own settings.

### Color Mode and Interpolation

The gradient field uses the standard Unity field height and aligns with adjacent controls.
Click it to edit its colors and opacity. These two controls are independent:

| Control | Result |
| :--- | :--- |
| Color Mode → Linear | Blends in linear RGB, useful for light mixtures. |
| Color Mode → Gamma | Blends the encoded RGB components. |
| Color Mode → Perceptual | Blends in OKLab for more even perceived changes. |
| Color Mode → Hue | Blends lightness, chroma and hue in OKLCH; choose the route around the hue circle. |
| Interpolation → Blend | Each pair of keys defines a separate transition. |
| Interpolation → Balanced | Neighboring keys help shape a monotone transition with midpoint control. |
| Interpolation → Smooth | Uses a fixed easing curve for each transition. |
| Interpolation → Adaptive | Adjusts the curve using neighboring keys; the midpoint changes its shape rather than guaranteeing a half-color sample. |
| Interpolation → Fixed | Produces hard color and opacity bands. |
| Interpolation → Custom | Edit independent channel curves and their tangents directly. |

**Smoothness** appears only for Blend and Balanced. **Chroma Correction** reduces chroma
between opposing colors; zero disables it. **Hue Path** appears in Hue mode: Shortest,
Longest, Increasing or Decreasing. Fixed disables Chroma Correction and Hue Path while retaining their values.
Opacity follows the selected interpolation with its own keys and midpoints; color mode,
hue direction and chroma correction do not change the opacity calculation.

Between two color keys, RGB stays within 0–1 unless either key extends that range.
HDR and negative components expand the shared range for all three channels; for example,
keys with a minimum component of −0.2 and a maximum of 4 allow −0.2…4.
The keys themselves stay unchanged. Intermediate values beyond these bounds are clipped,
which can flatten parts of a transition or change its hue.

The diamonds move transition midpoints. Fixed ignores them and Smoothness.
**Wrap** controls values outside 0–1: Clamp holds the nearest endpoint, Repeat tiles the
gradient, and Mirror reflects alternate repetitions. Repeating different endpoint colors
still creates a seam.

### Edit Custom curves

Choose **Interpolation → Custom**, then open **Edit Curves**. The first switch fits the
current transition; later switches restore your manual curves. Automatic settings stay
separate. The fit can slightly change the result, especially sharp transitions.

The curve window shows R/G/B in Linear or Gamma, L/a/b in Perceptual, and L/C/H in Hue,
with a separate alpha channel. Click an authored curve to make its channel active;
double-click near it to add a key on the curve.
Keep the second click held to drag the new key immediately; this also works on the curve-window strip.
The nearest visible channel at that time is used; coincident curves prefer the active channel.
Empty space and Wrap continuations do not add keys. Output remains a result preview.
Adjust the key's position, value and tangents; negative and HDR values remain available. Tangent modes are
**Auto**, **Smooth**, **Broken**, **Linear** and **Constant**. Constant uses the next key's
value between keys, matching Fixed; a key at its exact position keeps its own value.
Very sharp Custom steps can appear slightly softened in the rendered result.

Grid divisions stay anchored to curve coordinates when panning. The maximum main line step is
0.5 on both axes. Lines and labels use decimal 1–2–5 steps, such as 0.5, 0.2, 0.1, 0.05 and 0.02.
Finer lines are more transparent and gradually become stronger as you zoom in; overlapping
divisions are drawn only once. Numeric labels choose a separate, uncapped step to stay sparse
and omit unnecessary trailing zeros. They use a fixed light transparency; zoom only shows or hides them.
Zero lines appear slightly brighter.
The background outside 0–1 on either axis is slightly darker, with equal shading at the corners.
Outside time 0–1, translucent curves follow **Wrap**: Clamp extends the endpoint values,
Repeat repeats the curves, and Mirror alternates their direction. Repeat boundaries do
not connect different endpoint values with a diagonal. Keys remain in the original interval.
With **Output** enabled, continuations show only the final curve; the authored curve remains
visible for comparison inside 0–1. This avoids overlapping translucent lines outside the interval.

The graph and the gradient strip directly beneath it share the same side margins, with no gap
between them. The strip follows the visible horizontal interval and **Wrap**, including areas
outside 0–1. Vertical navigation does not change its colors. The graph includes the area behind axis labels and scrollbars.
Labels overlay the top and left edges. Scrollbar tracks are transparent and their thumbs
are translucent, becoming more visible on hover or drag. Middle-button panning shows a hand cursor. The bottom bar controls
time; the right bar controls channel values. Drag
the thumb body to pan, or either end to zoom that axis. Clicking the track centers the
view there. The navigation range stays fixed when panning; moving the view does not
change the thumb's size or sensitivity. Ctrl+wheel zooms vertically only. Escape cancels
the current drag, and **F** frames time 0–1 and the visible channels, extending their
value range in steps of 0.25 for negative or HDR values. Navigation does not change keys.
Wheel zoom and scrollbar grips have no preset minimum or maximum zoom. Only numerical
precision limits remain. At extreme zoom-out, subpixel Wrap repetitions appear as a faint
value-range band instead of drawing every repetition.

**Output** sits below the graph; channel buttons are on the right of the same row. They use
the Canvas View footer style and behavior: click to toggle a channel or drag across several
buttons to apply the first button's new state to all crossed buttons. Channel names follow
the selected color mode. There is no Fit button.

The curve-window strip combines keys whose **t** values differ by at most 0.00001 into one
marker, including alpha. Dragging it moves the represented keys together; dragging an individual
curve key edits it independently. The keys included in a drag are fixed when the drag starts.
There are no persistent key links. Double-click empty strip space inside 0–1 to add keys in all
four channels. Markers appear only at original key positions, not on Wrap repetitions.
Alpha-only markers show white over a checkerboard: opaque white at alpha 1, the checkerboard
at alpha 0. The body shows a centered 2×2 checkerboard; the same square-cell grid continues into
the triangular tip, clipped to the marker outline. Combined markers that also contain color keys keep their color swatch.
Keys snap to other key times and to time/value boundaries 0 and 1. In **Repeat**, a key moved
along the left or right boundary also snaps vertically to the opposite end of its channel curve.

The ordinary gradient editor and canvas handles also combine coincident channel keys. The editor
keeps separate color and opacity tracks. In this editor, dragging, editing Location or deleting a
Custom marker affects only its own track, even when color and alpha share the same time.
Canvas View shows only color markers; moving or deleting them preserves alpha keys. Edit alpha in the
gradient editor or curve window. Markers have no colored channel dots in any window.
Custom canvas markers sit directly on the gradient line or arc, with the same circles and outlines
as ordinary gradient handles. Angular endpoints share the usual two-color handle when the circle is closed.
Double-click a marker to edit its key in the curve window. Adding a color marker creates three
independent color keys at one position; the editor's opacity track adds only alpha. Moving a marker
stops before an unselected neighbor in any represented channel.

Drag a curve-window strip marker above or below the strip and release to delete its represented
keys. A faded marker with a red outline shows pending deletion; return to the strip before
releasing to keep it, or press Escape to cancel the drag. Each channel must retain a key.
Deleting a strip marker removes the represented keys; deleting a point in the curve window
removes that point only. Each channel keeps at least one key. Custom ignores midpoint,
Smoothness, Chroma Correction and Hue Path. Final color still uses the expanded RGB
bounds described above, so an entered curve value and the final color can differ.
Use **Convert Color Mode** to change Custom's color model while approximately preserving appearance;
conversion may replace keys and tangents. Undo restores the previous curves.

### Edit keys

The narrow color field to the left of the strip changes all color keys together. A gray swatch with a white diagonal and dark-gray shadow from the bottom-left to the top-right corner means their colors differ; the field remains editable. Key positions, midpoints and opacity are preserved. Escape in the picker restores the original colors. The equally sized circular-arrow button on the right reverses both key tracks and their midpoints. Both actions support Undo.

Outside Custom, double-click a color-key marker in the gradient editor to open its color picker. A single click selects it; dragging moves it.
Closing the color picker or canceling it with Escape returns to the gradient editor without closing that editor.
Use **HDR** beside the selected color when extra brightness is needed. Drag a key vertically
away from its track to delete it; each track keeps at least one key. Right-click a gradient field
to **Copy** or **Paste** an independent copy. SDF and Noise palettes start with **Perceptual** interpolation.

With `WHIMTEX_DEEP_DEBUG` enabled, an RGBA curve graph appears below the gradient strip.
**R / G / B / A** independently show or hide curves. The graph shows unpremultiplied samples
in the gradient's stored Linear/Gamma space; its vertical scale includes negative and HDR values.
**Preview EV** and channel buttons do not change these samples or the gradient.

### Save and reuse gradients


In **Presets**, click the **New** swatch to save the current gradient without naming it.
Click a swatch to apply it; right-click for **Copy** or **Delete**. Presets are
listed newest first, immediately after **New**. The folder is
chosen in User Settings; gradients use its **Gradients** subfolder. **↻** reloads the list.
Deleted presets can be recovered from **Gradients/.trash**. Unreadable preset files are skipped in the library. Built-in Unity gradient preset libraries are not imported.

Use **Import…** in Presets to read a `.grd` library. Imported gradients become independent user presets.
Source Classic/Linear/Perceptual methods map to Gamma/Linear/Perceptual color mode with Blend interpolation.
Stops, midpoints and Smoothness are retained; a warning explains that the source transition curve may differ.
Missing or unrecognized interpolation uses **Perceptual**; missing or unrecognized Smoothness uses **100%**.
Foreground/background stops become black/white. Noise gradients, unsupported color models and tracks
over 64 stops are skipped with a warning; they are not silently replaced with a different gradient.

**Paste** also accepts gradient JSON from an AI or another application. Copy a complete
[gradient JSON value](../AI/README.md#standalone-gradient-json), then right-click the gradient field
or the gradient strip in its editor and choose **Paste**.

Old gradient JSON is no longer accepted. Use the current gradient format;
retired fields and previous names are not migrated.

### Shape the gradient on canvas

Choose **Gradient** beside **Fill**, pick a type and edit the gradient in the Canvas View header, then drag.
Hold or drag the toolbar button to open its subtype list, then release over **Linear**, **Radial**, **Angular**, **Square**, **Freeform** or **Mesh**. Each has its own gradient icon; the button shows the current type and stays synchronized with **Type** in the header. A quick click activates the remembered type.
Point with the small arrow tip; the gradient symbol sits to its lower right.
The canvas cursor shows the selected gradient type and updates immediately when you change it. Mesh currently uses the standard gradient cursor.

The tool header's **Type** and the layer Properties' **Gradient Type** use the same row of six icons. Click one to switch type; the selected icon is highlighted, and hovering shows its name.

Linear replaces separate horizontal and vertical types: drag its endpoints or rotate the layer for any direction.

Linear uses the drag's start and end; Radial, Angular and Square use the start as their center.
**Freeform** creates three points in a triangle: the drag places two corners and previews the third. All three preview points show their palette colors while dragging. Their colors and opacity come from the start, middle and end of the current palette, including HDR. Releasing immediately activates point editing.
**Shift** constrains the angle to 45° increments, **Ctrl** disables snapping and **Escape** cancels.
Each drag creates a separate editable Gradient layer; a click without dragging creates nothing.
The palette types and Freeform start with a square transform in canvas pixels, so switching to Radial produces a circle even on a rectangular canvas. Mesh uses the rectangle drawn on the canvas.
For **Angular**, the creation line sets the center and direction only. Releasing restores a unit square, independent of the drag length: Scale is 1 × 1 on a square canvas; on a rectangular canvas the square fits its shorter side.
The creation settings do not change existing layers. Choose **Gradient Handles** to edit a created layer; Freeform and Mesh open these handles automatically after creation.
All canvas gradient handles show a hand over draggable points and handles, and an arrow with a plus where a click adds a color key. Fill and stroke gradients follow the same rule.

Select a Gradient layer to activate **Gradient Handles**, the contextual hand tool. Square handles change its geometry;
colored points move color keys. Click the line to add a key, or double-click a point to open WhimTex's
Color Picker. Alpha keys remain in the gradient editor.
Drag the small diamonds to move the midpoint between colors (except in Fixed mode).
Delete a selected color key with Delete while the preview has focus, or drag it away from the line and release.
At least one color key remains.
Position, size and rotation are controlled by the layer transform; there are no separate Center or Radius settings.
Radial, diamond and square gradients share position, scale and rotation controls.
For **Angular**, drag the center to move the gradient, or the start ray to rotate it without opening the circle.
When the arc is open, a thinner, fainter line completes the circle as a visual guide.
A full circle has one split-color handle: its halves are the independent 0% and 100% keys.
Double-click a half to edit its color. Drag clockwise to separate the end, counterclockwise to separate the start;
bringing the endpoints together joins the handle again. The arc edits one period through **Repetitions**;
**Repeat/PingPong** still controls the rest of the circle. **Start Angle** rotates the field independently of the layer transform.
Hold **Ctrl** while dragging an arc endpoint to snap Repetitions to integers (1, 2, 3… → 360°, 180°, 120°…). The opposite endpoint stays fixed. Press or release Ctrl during the drag to switch between snapped and continuous editing; rotation, center movement and intermediate keys are unaffected.
Click the arc to add a color key; intermediate keys and midpoint diamonds move along it. Escape cancels a drag.
With Repetitions below 1, only the visible portion of the period is shown; the start handle rotates it and the period length remains editable in Properties.
Handles have no tooltips. Select a basic tool to hide the handles, or press Escape when not dragging to return to your last basic tool. The contextual hand button restores the handles. Selecting another Gradient layer activates them automatically, except while the Gradient creation, Crop or Comment tool is active.

The same **Gradient Handles** tool edits Linear, Radial, Angular and Square fills on **Shape** and **Vector** layers. Select a gradient-filled figure, then click its contextual hand button; selecting the figure does not activate handles automatically. The handles change only the fill, not the figure's shape or transform. Linear endpoints move independently; moving the center of another fill moves its direction/radius handle with it. Color keys and midpoint diamonds work as above.

Angular fills use the same split endpoint handle, opening/closing gestures, faint completion arc and Ctrl snapping. The start ray rotates the fill; **Center**, **Direction** and **Repetitions** in Properties follow the handles. Outside the first period, the palette's **Clamp/Repeat/Mirror** setting applies. A child whose appearance is replaced by a Boolean Group's style does not show fill handles; Combine with layer appearance keeps them available.

**Boolean Group** also provides these handles for its common fill, including Angular. Select the group to edit its gradient without moving the group or its operands. In **Combine → Appearance: Layers**, the group handles are hidden; select a child to edit its own fill instead.

With **Source** tiling, Color Fill, Noise and non-Mesh Gradient sources are unbounded. Mesh clips to its layer frame.

### Freeform gradient points

On a **Gradient** layer, choose **Gradient Type → Freeform** in Properties.
For **Shape** and **Vector**, choose **Freeform** in **Fill → Type** or **Stroke → Type**.
Fill and stroke keep separate sets of points. Enable **Gradient Handles** manually for a figure;
the toolbar's **Fill / Stroke** selector chooses which set to edit.
Use **Gradient Handles** to place colors anywhere on the layer:

- Click an empty spot on the canvas to add a point. Keep the button held to move it immediately.
- Drag a colored point to move it; double-click it to edit its color and alpha.
- The cursor shows a plus where a new point can be added, and a hand over a point or its influence handle.
- Drag the selected point's square influence handle, or change **Influence (%)** in Properties.
  A larger value gives that color more space relative to the other points. The outline is a guide, not a hard cutoff.
- **Point** selects a point by number. **Position**, **Color** and **Opacity** edit that point.
- Press **Delete** with the canvas focused, or use **Delete Point**, to remove it. One point must remain; up to 64 are supported.
- **Escape** cancels a drag, including a point just added. Undo/Redo includes the whole gesture.

Colors blend smoothly in linear space and support HDR and transparency. HDR colors also need
the layer's **Rendering → Color Range** set to **HDR** to remain brighter than white in the result.
Changing Gradient Type preserves both the Freeform points and the ordinary gradient palette.
Freeform is available for Gradient layers and Shape/Vector paints, including Boolean Group styling.
Text and SVG export are unchanged.

<a id="mesh-gradients"></a>

### Shape colors with a Mesh gradient

On a **Gradient** layer, choose **Gradient Type → Mesh**, then **Gradient Handles**.
The grid lets you shape color transitions with connected curves. Each node has its
own color and transparency.

To create one directly, choose **Mesh** in the Gradient tool, set **Rows** and
**Columns** in the header, and drag a rectangle. The initial colors and alpha come
from the current palette's two ends. Creation settings start at 2 × 2 cells and are
remembered between sessions; they do not change existing meshes.

- Drag a node to move it. Select it to edit **Position**, **Color** and **Opacity**
  in Properties; double-click its color marker to open the color picker.
- Drag the selected node's Bézier handles to bend the adjoining edges. Nodes and
  handles move freely, including beyond the initial frame; the grid can fold and overlap.
- Choose **Node Type → Smooth** to align the opposite handle while retaining its
  length when editing a handle, or **Corner** to move each handle independently. The same choices
  are available from the node's context menu. Switching to Smooth aligns its handle
  pairs and can change the shape; colors stay unchanged. New nodes use Smooth.
- Drag an edge to bend it without moving its endpoint nodes. Both edge handles move
  together; Smooth endpoints keep their opposite handles aligned.
- **Rows** and **Columns** count cells. **Add Row** and **Add Column** split the
  selected adjacent cell in half. Double-click an edge to split the corresponding
  row or column. Subdivision preserves the colors and shape already drawn.
- Use ordinary **Transform** to move, rotate or scale the whole mesh.

**Color Model** offers Linear, Gamma and Perceptual. Changing it can alter transitions
between nodes. HDR colors are supported; choose **Rendering → Color Range → HDR**
to retain brightness beyond white. Areas outside the mesh are transparent. Where the
mesh overlaps itself, one surface section supplies the color and opacity; transparency
does not build up from repeated coverage. Visible output still follows **Rendering → Tiling**.
Switching Gradient Type preserves the mesh and the other gradient settings.

Up to 16 rows and 16 columns are supported. Removing individual nodes or rows is
not available. Mesh is currently only for Gradient layers; it is not a figure fill
or stroke type.

### Choose transition shape

Use Blend for separate eased transitions or Balanced when neighboring keys should influence the curve.
Smoothness 0% removes their smoothing contribution; 100% applies it fully. Smooth uses a fixed easing
curve, while Adaptive follows the surrounding key arrangement. Neither uses Smoothness.
Changing interpolation does not move keys. Narrow transitions can still be approximated by the
existing rendering palette; selecting a mode is not a guarantee of invisible bands in every result.

<a id="rearrange-channels-with-swizzle"></a>

## Rearrange channels with Channel Mapping

In **Layer Settings → Rendering → Mapping**, choose what goes into each output channel of a layer or group.
For example, choose R in the R, G and B fields to make a grayscale image from the red channel.

Each field offers the original channels, their inverses, black (`0`), white (`1`),
or a color channel multiplied by transparency (`R * A`, `G * A`, `B * A`).
`Luminance` combines RGB into grayscale brightness; `Luminance * A` also accounts for transparency.
`1 - Luminance` reverses that RGB brightness: black becomes white and white becomes black, independently of the input alpha.
The list groups original RGBA and Luminance first, then their inverses, alpha products, and the constants 0 and 1.

The arrow button on the right offers builtin presets:

- **Default:** restore `R G B A`.
- **Default without Alpha:** keep RGB and make the image opaque (`R G B 1`).
- **R**, **G**, **B:** put brightness in the selected color channel, set the other two to `0` and alpha to `1`.
- **Luminance to Alpha:** make RGB white and use brightness as alpha (`1 1 1 Luminance`).
- **1 - Luminance to Alpha:** make RGB white and use inverted brightness as alpha.
- **Alpha to Grayscale:** show alpha as an opaque grayscale image (`A A A 1`).

Changing a group's Mapping treats its contents as one image, so outside blending can look different.

### Turn a grayscale image into transparency

1. Select the grayscale layer. If you paint the mask yourself, keep all Canvas View channel buttons enabled and use opaque colors.
2. In Mapping's preset menu, choose **Luminance to Alpha**. Black becomes transparent, white becomes opaque, and gray becomes partly transparent. RGB is set to white.
3. To include the image's original transparency, set the A field to **Luminance * A** instead.
4. Check the result with **A** alone in [Layer Preview](preview.md#layer-preview). Its channel buttons only inspect the result; they do not restrict painting.

For the opposite mask, choose **1 - Luminance to Alpha** from Mapping's preset menu: black becomes opaque and white becomes transparent, with white RGB. This preset does not include the image's original transparency.

To use this layer as a mask for another layer, select the artwork layer and add **FX → + Preset → Color → Mask**.
Set the mask texture source to **Layer**, choose your mask layer, and set **Mask Channel → Alpha**.
Under **Apply To → Channels**, leave only **A** enabled to change transparency without changing RGB.
The mask layer can be hidden; the FX still reads it. See [texture sources](shader-fx.md#texture-sources).

## Pack several masks

Select layers and choose **Assign Channels** in the row menu.
The order is top to bottom, and each layer uses its red channel multiplied by its transparency.

- **1–3 layers:** assign them to R, G and B. The upper selected layers use Add to combine the masks.
- **4 layers:** assign them to R, G, B and A. This only sets Mapping: with ordinary blending,
  the RGB layers have no visible opacity, so it is not a ready-to-export four-channel combination.

The command is unavailable for more than four layers. It leaves the layers separate.
