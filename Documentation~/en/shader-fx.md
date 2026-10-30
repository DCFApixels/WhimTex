---
title: "Shader FX and Processor"
parent: "English"
nav_order: 9
has_children: true
lang: "en"
permalink: "/en/shader-fx/"
translations: "en/shader-fx.md,ru/shader-fx.md,zh/shader-fx.md"
previous_page: "en/blending.md"
next_page: "en/preview.md"
---

# Shader FX and Processor

Use **Shader FX** to change one layer or **Shader Processor** to process the image below it. Both support ready-made presets and your own HLSL. No code is needed to use a preset.

At native zoom and 100% **Live Quality**, pixel-local effects retain pixel detail
without processing the whole large canvas. Effects that read neighboring pixels can
still be more expensive. Previews in the FX settings show the complete input, even
when Canvas View shows only a small part of it.
Keeping a Shader Processor's **Transform** neutral also makes its pixel-local effects
cheaper on large canvases. Moving or scaling the Processor can require more work.

During ordinary brush strokes, open FX previews update at reduced resolution and preserve canvas detail. Repeated or mirrored layers can still cost more to preview.

## Affect one layer or the image below?

**Shader FX** belongs to one layer and changes that layer's image.

A **Shader Processor** is a separate layer that changes the combined image below it.
Place it above the layers you want to process.
Use **Normal** blending and lower Opacity to mix the effect with the original;
hide the Processor to compare before and after.

In a Pass Through group, a Processor can also affect the background beneath the group.
Use an isolated group if the effect should stay within that group's contents.

Unlike [Post FX preview](post-fx.md), both are included in the saved image.

## Apply an existing effect

1. Select the layer you want to change.
2. Click **Add FX** below the effect stack, or **+** beside **⋮** in the FX section header, including when the section is collapsed. Both open the same menu. Search by name or open a category to choose a preset; **Custom Shader FX** creates an effect for your own HLSL.
3. Adjust its sliders, colors, textures, toggles and dropdowns. Changes appear immediately.

In **Add FX**, built-in presets keep the Color, Distortion, Lighting, Normal Map and Stylization categories. Inside each category, related effects sit together, separated from other groups by a line. Groups and presets sort alphabetically. Add `// @group("Tone")` to an HLSL preset header to choose its group within its category. Presets without a group appear first. The quoted declaration does not need `@endgroup`. See [preset list groups](../ShaderFX.md#preset-list-groups).

Categories and nested categories use subdued filled folder icons. Preset icons appear beside names in **Add FX** and before the Enabled checkbox in each FX header. Related built-in effects share icons for color, tone, gradient, mask, distortion, lighting, normals, patterns and transforms. A missing or invalid icon uses the general FX symbol; hover over the header icon for details.

Alternatively, drag a WhimTex effect `.hlsl` from Project onto a row in **Layers**.
Dropping it onto Canvas View or empty space in the list creates a **Shader Processor** at the top of the composition.
HLSL brush presets and files without the effect marker are not accepted.

If a preset contains invalid code or parameters, selecting it reports an error in Console and leaves the layer unchanged.

Each catalog effect has its own settings. The included **Color → Gain** adjusts brightness and tint;
**Distortion → UV Transform** repositions the incoming image within a visible frame. Its default
**Input Tiling → Clip** makes the area outside the frame transparent; Repeat or Mirror fills it with tiles.
Effects added to the project become available automatically; no preset folder setup is needed.

Find settings for [color correction](#levels-contrast-and-color-balance), [masks](#mask),
[edge outlines](#edge-contours) or [distortion](#distortion-presets) in the preset guide below.

## Parameter controls

**Color → Color Space** converts channel values between **Linear**, **Gamma** and **HSV**.
Choose **Input Space** to match the source's output and **Output Space** for the desired result;
the default is Gamma → Linear. The names match Noise and Normal Map: Noise Linear followed by
Color Space Linear → Gamma matches Noise set to Gamma directly, and the reverse works too.
HSV stores hue, saturation and value in R, G and B, based on Linear RGB, without gamma-encoding
the HSV channels. Hue runs from 0 to 1. Alpha stays unchanged; equal spaces leave all channels unchanged.
Use HDR Color Range for values above 1. HSV Value can exceed 1; conversion to HSV clamps
negative RGB to zero. Linear ↔ Gamma retains negative values. This effect transforms
pixel values; it does not change export encoding. Use a linear export, such as EXR, to retain
Linear or HSV channel data without additional sRGB conversion.

Use **⋮ → Reset** or right-click the FX header and choose **Reset** to restore all parameter defaults, including hidden fields and transforms. Code, the preset link and enabled state stay unchanged. Reset affects only this effect and can be undone.

Right-click a named group/foldout header or an individual tab for **Copy**, **Paste** and **Reset**, with a separator before Reset. Paste transfers values between compatible groups or pages, including Hue & Saturation ranges. Hidden and disabled fields, nested settings and any header checkbox are included. Reset affects only that group or page, not neighboring tabs or shared fields; Paste and Reset can be undone. Bodies, unnamed groups and the overall tabs heading have no parameter-set menu.

The unlabeled checkbox in each Shader FX header enables or bypasses that effect without removing its settings. Each effect has its own values and enabled state, even when several effects use the same HLSL preset.

Hover over a parameter to read its description, if the effect's author supplied one.

### Groups, foldouts and tabs

An effect can organize settings into non-collapsible bordered groups, collapsible sections and tab pages. Click a foldout's arrow to show or hide its settings, or click a tab to view that page. These choices only change which controls you see: values in collapsed sections and other pages still affect the image. A checkbox beside a section title changes its parameter independently; the author can use it to show dependent settings or enable part of the effect.

For custom HLSL, use `@group` / `@endgroup`, `@foldout(Title)` / `@endfoldout`, and `@tabs` containing `@tab(Page title)` / `@endtab`, closed by `@endtabs`. Foldouts require a title. Tabs can have an overall title with `@tabs(Title)`; plain `@tabs` leaves no blank heading row. See the [layout syntax and examples](../ShaderFX.md#layout-containers).

A tab can have its own checkbox before the label: `@tab(Range 1; _Enabled)`. Declare the bool directly inside that page. Clicking the checkbox changes its value without selecting the tab; clicking the tab leaves the checkbox unchanged. The effect decides what that value enables.

Dependent fields and previews can stay visible but gray while their section is disabled. In custom HLSL, wrap those rows in `// @enableif _Enabled == 1` and `// @endenableif`; `@if` hides rows instead. Enabling the section restores editing with the saved values. These conditions only change controls; HLSL determines how values affect the image. See [condition syntax](../ShaderFX.md#parameter-declarations).

### Parameter previews

Standalone previews are square and centered; widening the inspector does not stretch them.

**Mask**, **Chroma Key** and **Edge Outline** include collapsible **Mask Preview**, **Matte Preview** and **Contour Preview** sections. White represents full mask value or coverage, black represents zero. Mask shows the selected channel after Transform, Profile and Invert. Chroma Key shows retained coverage including source alpha; Edge Outline shows contour coverage including source and line alpha. These previews show the mask before the FX header Opacity and do not switch the canvas output. Collapse the section to stop drawing its preview. Small previews can omit fine details; use the canvas to judge final edges.

Changing a parameter refreshes previews on the next Editor update, without a 100 ms wait.
Rapid changes are combined, and each preview redraws at most once per Editor update.
Periodic refresh remains a fallback for changes made outside normal controls.

An author can reuse one preview function with different parameter sets. In Hue & Saturation,
range previews stay linked to their own Hue Range and Feather, and correction tracks use
the current settings of that same range; switching another range does not redirect them.

Effects can show an author-defined preview: a colored Slider track, a small rectangle to the
right of another field, or a separate image between settings. Slider thumbs and numeric input
keep their normal behavior. Hue & Saturation previews use a red reference sample and the other current
settings; they are a guide, not the result for every image color. Inspector colors are limited
to SDR, without limiting HDR values in the effect. See the [HLSL preview syntax](../ShaderFX.md#inspector-previews).

Preview tracks use a compact triangular marker, half inside and half outside the lower edge.
Track and marker sizes stay the same for ordinary and soft-range sliders.
Numeric fields use the standard slider size, including both endpoints of range controls.

Hue & Saturation also shows the current hue range and feather on Hue Range tracks; Feather itself has no preview,
and the saturation gate on Source Saturation (HSV). Range correction tracks use the range's
center color as a reference, without the final averaging of overlaps. Both presets share one
baked rainbow lookup; the effect's image-processing formulas are unchanged.

### Numbers and limits

Interval sliders have two handles and synchronized, unlabeled numeric fields: left is the lower
endpoint (X), right the upper (Y). Drag a handle to resize the interval or drag the middle to move it as a whole.
Endpoints may coincide but cannot cross or leave the track. Shader previews sit beneath both handles.
The slightly larger half-triangle handles extend outward, with their vertical edges on the interval endpoints.
Both handles stay visible and draggable at the limits, including full and empty ranges, without overlapping numeric fields.

Numeric FX header fields have a **↔** handle: drag left or right to change the value. Shift gives finer adjustment; Ctrl makes it faster.

An effect can offer a slider and a dropdown for the same setting: changing either updates the shared
value. **Custom** means the current number is not one of the dropdown's predefined choices.

Some sliders allow numbers past one or both ends of their visible range: type in the adjacent field
or drag the parameter label. Each end can independently be a hard or soft limit.
The thumb stays at the nearest endpoint, but the effect uses your number. Other sliders keep
both dragging and numeric input limited to their range, as chosen by the effect's author.

Gain, Levels, Threshold, ambient lighting and distortion offsets offer soft limits where appropriate. Levels and Threshold can work above 1 for HDR; blend amounts remain limited to 0–1. Pixelate and Posterize allow more than 64 levels and Gamma above 5 through numeric input.

### Random seeds

Seed fields marked by the author have a **Random** button on the right. It chooses an integer
within the declared range, including its endpoints. You can still type or drag a fractional value
where the bounds allow it. The choice supports Undo/Redo and stays fixed during preview, save and load.

### Texture sources

A texture parameter can use **Texture** (an asset) or **Layer** (a layer in this document). Choose the source mode or drag a layer onto the parameter. Procedural and Drawing layers are supported, even when hidden. Groups provide their colored contents; hidden children remain hidden. Missing sources produce transparency, and circular references cannot be selected.

Texture source **Self** reads the image before the current FX, including earlier effects. **None** returns transparent pixels. These modes need no assigned asset or layer; Self continues to work when copied to another layer.

### Points and directions

Vector parameters provide two, three or four numeric components.

A `point` parameter sets a position: bottom-left `(0, 0)`, top-right `(1, 1)`, default `(0.5, 0.5)`.
Click the hand icon (**Edit on Canvas**) beside the field to drag the point, including outside the canvas.
Dragging snaps to canvas edges and visible guides; hold Ctrl to disable snapping.
Set the snap radius in **User Settings → Guides & Snapping → Snap Radius (px)**.

A normal parameter sets a unit direction and offers the same button.
Drag its endpoint: near the center it faces the camera; at the maximum radius it points along the canvas.
Click the endpoint to switch between **+** (toward the camera) and **−** (away).

### Curves and gradients

Effects can offer a curve field for adjusting a numeric profile. Click it to edit keys and tangents
with Unity's curve editor. It starts as a straight line from 0 to 1; values may go below 0 or above 1.
Saving an HLSL preset preserves the edited curve.

Effects can also offer a gradient field. Click its strip to edit colors, transparency and interpolation,
including HDR colors. New gradients start black-to-white unless the HLSL declaration supplies two endpoint colors,
for example `// @param gradient _Ramp = #FF0000FF -> #0000FF`. Hex colors use RGBA order; six digits imply full opacity.
Without an explicit default, Color Mode is Perceptual and Interpolation is Balanced; the two-color HLSL form uses Gamma/Blend with Smoothness 0.
Changes update the effect immediately.

In the gradient editor, **Wrap** chooses what happens outside 0–1: **Clamp** holds the endpoint colors, **Repeat** repeats the gradient, and **Mirror** alternates its direction. An effect that clamps its own input may never reach the repeated range.

For HLSL declarations, see [writing parameter controls](shader-controls.md).

## Adjust an effect on the canvas

Effects with a Transform 2D parameter offer **Edit on Canvas**. Select it to show a green frame,
then move, resize or rotate the frame. Rotation is around its center; there is no pivot control.
Use Ctrl/Cmd + corner to distort, Ctrl/Cmd + edge to skew, or Ctrl/Cmd + Alt + Shift + corner
for paired perspective adjustment. Alt with Ctrl/Cmd moves the opposite corner symmetrically.
Position, Size and Rotation preserve the deformation; **Reset Transform** removes it.
**Edit on Canvas** activates one temporary hand tool below the context tools, also used for Point and Normal parameters. Its tooltip identifies the parameter. Click its toolbar button, click **Edit on Canvas** again or press Escape to return to the previous tool. During a drag, the first Escape only cancels that drag. Editing another parameter replaces the temporary tool without changing the return tool. See [context tools](preview.md#context-tools).

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the canvas" width="720"></a>

The frame edits the effect, not the layer transform. Its purpose depends on the effect:
it may place an image, change a pattern's scale, or define a local area. It is not automatically a mask,
but **UV Transform** with **Input Tiling → Clip** leaves pixels outside the frame transparent.

<a id="order-copying-and-shared-effects"></a>

## Order, copying and HLSL sources

Right-click the **FX** section header in Layer Settings or click **⋮** at its right to open stack actions, even while collapsed. **Paste FX As New** appends the copied effect; **Apply All** bakes the stack; **Remove All** removes every effect without baking. Removal can be undone. Apply and Remove are disabled for an empty stack.

Effect order matters. Drag an FX header to reorder effects, or drop it onto another layer's row to move it there. **Move Up** and **Move Down** in the header context menu also change the order.

Every FX belongs to the document and has independent parameter values and enabled state. Use **⋮ → Copy FX**, then **Paste FX As New** on another row to insert an independent copy after it. Layer-texture links stay within the same document and are cleared when pasted into another document. The menu also contains **Move Up**, **Move Down** and **Remove**.

Presets with `// @preset("GUID", 1)` in their initial comment header receive code changes from their source `.hlsl` file only while the declared version matches the version saved in the document. This applies to project, package and user-library presets; their parameter values remain independent. Use **Open HLSL Source** to edit that source. To edit this effect independently, choose **Code → Embed Copy**: it disconnects the `.hlsl` link, keeps the current code and values in the document, and enables local code editing. Later source-file changes no longer update this effect. Presets without `@preset` are independent copies, wherever their files are stored. If a linked source is missing, broken or has a different version, the effect uses the code saved in the document and keeps the link so it can retry when a matching source returns. For custom code, add **Add FX → Custom Shader FX**, write inline HLSL and click **Compile**. Parameters appear below the header; **Code** provides the applicable code controls, **Save Preset…** and **Shader Inputs**. Diagnostics appear when there is something to report.

Only marked `.hlsl` presets and inline HLSL are supported. External Material, Shader and Shader FX `.asset` references cannot be added. Older JSON/TIFF documents containing external FX references are rejected with a diagnostic; there is no automatic migration or document rewrite. Keep the originals and use a matching older checkout to open them.

## Save your own preset

In the effect's code editor, click **Save Preset…**. This creates an `.hlsl` file
with the current parameter values as defaults, including colors and Transform 2D.
The file name becomes the preset name. You can save in the user library's **ShaderFX**
subfolder or anywhere under the project's **Assets** folder. Overwriting keeps a `.bak` copy.
A new file gets a fresh preset ID and version 1. Overwriting preserves the destination file’s
ID and version when present. Saving does not switch the current effect to the exported file.

Set the shared library location in **User Settings → Presets → Presets Folder**. Its **Brushes**
and **ShaderFX** subfolders hold the two kinds of presets. You can also place existing
HLSL presets in ShaderFX or its subfolders; they appear under **User** in **Add FX**.
The list refreshes in the background when you return to Unity. If you open it before the
refresh finishes, close and reopen it to see the new presets. On first startup it may briefly show **Loading effect presets…**.
Linking depends on the optional `@preset` header, not the folder. Write it once before HLSL,
preferably immediately after `@whimtex-effect Category/Name`, with both a GUID and a positive
integer version. Without it, later file edits do not change effects already added. With it, only
the saved version is followed. If copied files share an ID, writable duplicates receive new IDs;
read-only conflicts appear in Diagnostics. See [preset identity and version](../ShaderFX.md#preset-identity-and-version).

Texture defaults are references, not embedded images. To use them in another project,
also transfer the referenced texture assets with their `.meta` files, or assign replacements.

Edited gradients are saved in the document. HLSL defaults support two endpoints, Gamma color mode, Blend interpolation,
Gamma key encoding, Clamp wrapping, Smoothness 0%, Chroma Correction 0 and centered midpoints. Other gradients must be
simplified before this export; unsupported settings are not silently discarded.

## Bake effects into a layer

Baking turns the current result into Drawing pixels. Choose the command by scope:

| Command | Action |
| :--- | :--- |
| **Code → Compile** | Compile HLSL; keep the effect editable. Does not bake pixels. |
| **⋮ → Apply** in an FX header/menu | Bake that FX and every preceding FX, in stack order. Later effects remain editable. |
| **Apply All** in the FX section header menu | Bake the entire FX stack. |

Disabled FX in the baked range are removed without contributing to the image.

A non-Drawing layer asks for confirmation before becoming Drawing. Transform stays editable and keeps its values; opacity, blending, channel mapping and clipping remain separate. Baking captures the current canvas at full canvas resolution into floating-point pixels, not an infinite procedural source or off-canvas content. Undo restores the original layer and FX stack. Groups flatten their visible children and warn about lost child targets and possible pass-through changes.

Shader Processor also supports Apply. It captures its current input, including the external backdrop in Pass Through groups, without merging or deleting lower layers. **Normal** becomes **Overwrite** with the Processor's opacity blending retained, including partially transparent pixels; other blend modes stay unchanged. Later edits below no longer recalculate the baked effects. Remaining FX still operate on the snapshot, and Transform stays editable.

## Create your own effect

If you have shader code, use **Add FX → Custom Shader FX**, paste it into the editor and click **Compile**.
The code and settings stay with the document; no separate file is required.
If compilation reports a problem, see [FX errors and warnings](#compilation-warnings).

Custom HLSL effects and brushes can use the built-in noise library for grain,
organic masks and distortion. See the [noise functions and example](../AI/README.md#built-in-noise-library).

Writing an effect is optional. The [shader authoring reference](../ShaderFX.md)
is for creating code and reusable libraries.

### Pixel-local effects

Open **Code** to see **Pixel Local** above the code text area: **Yes**, **No**, **Not compiled** or **Unavailable** describe the compiled shader.
Draft edits do not change that answer until **Compile**; a note identifies a changed draft.
**Shader Inputs** groups the authoring reference into sections, uses bullets for declarations and API entries, and highlights function and parameter names.
Double-click highlighted code to insert it at the last code caret or replace the selected text,
including `SampleInput` and `SampleTexture` examples. Editable fragments brighten slightly
and show a hand cursor on hover, without tooltips. A full `ApplyFX` declaration instead
replaces the function header while keeping its body and argument names. The code field
keeps focus; Undo/Redo is available. Click **Compile** to use the edited draft. Linked
presets must first be made editable with **Embed Copy**.

In the code field, **Tab** inserts spaces to the next four-column stop; with a selection it
indents the selected lines. **Shift+Tab** removes one level of indentation. Both keep focus
in the code and support Undo/Redo. New `ApplyFX` bodies start with a four-space indent.
**Enter** keeps the current indent and adds a level after `{`. Between `{}` it opens an
indented interior line. Typing `}` at the start of a line aligns it with the opening brace;
braces in comments and quoted strings do not affect indentation. These edits support Undo/Redo.

In Unity 6000.7+, **Code** colors HLSL types, keywords, literals, comments, directives,
function names and WhimTex metadata. Coloring does not edit the source or compile it;
caret, text selection and Undo/Redo keep working normally. Selected text retains the
standard selection color. Older Editors show plain text.
Long code lines stay on one visual line; a horizontal scrollbar appears for overflow.
In Unity 6000.7+, line numbers on the left scroll vertically with the code but stay fixed
when scrolling horizontally. Blank lines are numbered too.
While editing, the current line has a subtle background and its number is slightly brighter;
text selection remains visible above the highlight.

Pixel-local FX read textures at the current coordinate. Use `ApplyFX(float4 color)` for
color operations, or `ApplyFX(PinnedUV uv, float4 color)` when UV is needed.
`PinnedUV` supports arithmetic and conversion to `float2`, but cannot be changed or
constructed from numeric coordinates. Copies retain the pin; arithmetic and swizzles lose it.
Sampling with pinned UV is local; sampling with numeric UV or tiling is spatial, including
inside helpers. UV math used only to calculate color does not remove locality.

`SampleInput(uv)` and `SampleTexture(_Mask, uv)` are WhimTex's primary texture-reading functions.
For an explicit mip level, use `SampleInputLod(uv, lod)` or `SampleTextureLod(_Mask, uv, lod)`.
These functions are spatial even at LOD 0 and can sample inside dynamic loops.
LOD 0 reads the supplied texture's base level; they do not create mipmaps.
`SampleTexture(_MainTex, uv)` is also pixel-local with `PinnedUV`, equivalent to `SampleInput(uv)`;
the sampler name itself does not make a read a bypass.
The latter also supports masks from Texture, Layer, Self or None; pixel-local reads require
`PinnedUV`. Direct HLSL texture reads are bypass paths, and detected bypasses or derivatives
produce a warning and prevent confirmation of pixel locality.
Texture inputs use the read-only `WhimTexTexture` type. Keep calls such as
`SampleTexture(_Mask, uv)`; use `WhimTexTexture` for texture arguments in your own
helper and preview functions. Sampling automatically accounts for the input's stored
region, while `_Mask.texelSize` and `_Mask_TexelSize` describe the complete input at
its render scale. **Shader Inputs** includes a helper example that you can insert into the code.
See the [signatures, restrictions and mask example](../ShaderFX.md#pixel-local-entry-points-and-texture-reads).

## Editing in an external editor

For a preset, **Code → Preset** shows its source file in a read-only object-style field. Click to ping a project/package asset; external library files have no Project selection. Double-click opens the original file in VS Code, or Unity's selected script editor when VS Code is unavailable. Right-click → **Show in Explorer** reveals the file (Finder on macOS). External library presets with `@preset` follow the linked source at the saved version; independent copies keep their document code.

- **Open Code** opens a working file in Unity's selected script editor. Saving updates the draft in WhimTex; click **Compile** to compile it.
- **Open in VS Code** also installs WhimTex directive highlighting, completions and checks automatically in an isolated profile. Saving this working file requests **Compile** in Unity, without returning to the WhimTex window. The language mode remains **HLSL**; the bundled extension supports Restricted Mode.
- If the VS Code button is missing, set **User Settings → External Code Editor → VS Code Command**. WhimTex checks Unity's registered editors and PATH before this fallback.
- The folder icon beside the VS Code path opens its containing folder. It sits between Browse and Reset and is available when the chosen file exists.

The VS Code extension highlights types, modifiers, parameter names, values and ranges as well as directive names. It checks defaults, tooltips, linked controls, conditions and groups; shader compilation errors still come from Unity. After an extension update, reopen code through **Open in VS Code**; an already-running window may need **Developer: Reload Window**.

Type `// @if`, `// @enableif`, `// @group`, `// @foldout`, `// @tabs` or `// @tab` and press **Tab** to insert a block with matching end directives. Tab moves through its placeholders and into the body. The tabs snippet includes a first page; enter `(Title)` in its first placeholder if you want an overall title. Compact spellings such as `//@enableif` work too. If your VS Code settings disable Tab completion, choose the snippet through **Ctrl+Space**.

External working files are temporary, not backups. Inactive copies unused for more than **24 hours** are automatically removed. Save your document in WhimTex to keep the code; saving in the code editor alone does not save the document. After restarting Unity or reloading scripts, reopen code from WhimTex to reconnect it.

<a id="compilation-warnings"></a>

## FX errors and warnings

A yellow **!** beside the layer and warning triangles in the **FX** section and individual effect
headers identify problems even with the panels collapsed. Hover for the reason; open **Code** for details.

| Message | What happens | What to do |
| :--- | :--- | :--- |
| Warning | The FX still runs. | Read the warning and decide whether the result is suitable. For example, [Unity time inputs](shader-controls.md#coordinates-and-time) can give inconsistent previews. |
| Error | The FX is skipped, not disabled or deleted. Its code and settings remain editable; no older compiled version is used. | Fix the problem and click **Compile**. |

Indicators clear when no issues remain. TIFF saving requires all FX drafts to be applied and errors fixed.
JSON can save and reopen broken FX without losing their code or parameter values; it does not store Drawing pixels.
WhimTex, Console and connected agents show the same messages. Repeated Console messages are suppressed;
diagnostics remain available in the UI and to the agent. See [the technical diagnostic rules](../ShaderFX.md#shader-fx-a-first-snippet-parameters-and-reusable-code).

## Preset guide

### Levels, contrast and color balance

**Color → Levels** has five curves. Choose **RGB**, **R**, **G**, **B** or **Alpha** in the **Curves** header to edit one; all configured curves remain active. They start linear.

The shared **RGB** curve runs after input black/white and Gamma, before output black/white. With **Preserve Color**, it remaps luminance; otherwise it remaps each RGB channel separately. The **R/G/B** curves then correct individual channels. Neutral curves keep HDR output; beyond 0–1, the correction at the nearest endpoint is added without clipping the excess. **Alpha** adjusts transparency independently of input/output levels and Gamma, with a 0–1 result. **Opacity** blends the entire correction, including alpha, with the original.

**Color → Brightness Contrast** adjusts midtone brightness and tonal separation. Both controls start at **0** (unchanged). Positive Contrast separates dark and light tones; negative Contrast brings them toward middle gray. The slider tracks cover −100…100, but numeric input and label dragging can go beyond either end. The effect uses smooth tone curves, not a uniform RGB offset: black and white remain fixed, alpha is unchanged, and RGB values outside 0–1 pass through. It operates in the incoming RGB space; it is not exposure control or a pixel-exact match to another application. Extreme settings can collapse details through floating-point precision.

Color Balance groups Shadows, Midtones and Highlights in one block. **RGB Offset** adjusts the three signed RGB components; **Range** controls shadow/highlight influence, and zero hides the corresponding offsets. **Preserve Luma** retains luminance. **Opacity** in the FX header blends the result with the original; alpha is unchanged.

**Profile** in Bevel Emboss maps the selected height channel to relief height. **Mapping** in Gradient Map redistributes brightness before choosing a gradient color. Both curves start linear.

### Chroma Key

**Color → Chroma Key** removes a background matching **Key Color** while keeping soft
edges. **Tolerance** removes close colors completely; **Softness** controls the transition
to untouched colors. Zero Softness gives a hard threshold. **Distance → Chroma** compares
color differences with less sensitivity to brightness; choose **RGB** for black, white or gray.

**Despill** suppresses the background tint near the selected color without changing alpha.
**Spill Range** extends its color range beyond the key transition. Despill defaults to zero;
it does not reconstruct foreground details or distinguish an object with the same color as
its background. **Output → Matte** shows final coverage as opaque grayscale, including the
original alpha. Header **Opacity** blends the selected output with the input; zero is unchanged.
Original HDR colors are retained, while color matching analyzes a clamped SDR copy.

<a id="hsv-correction"></a>

### Hue & Saturation correction

**Color → Hue & Saturation** adjusts hue, saturation and lightness. **Hue** shifts the hue in degrees.
**Saturation** scales color away from weighted gray: 0 removes color, 1 is unchanged, above 1 intensifies it.
**Lightness** goes from −100 (black) through 0 (unchanged) to +100 (white); it is not a brightness multiplier.
**Opacity** in the FX header mixes the correction with the original. Gray pixels stay gray when changing hue or saturation.
Alpha is preserved and HDR brightness is supported. Active correction treats negative RGB channels as zero;
neutral settings and Opacity 0 leave the original unchanged.

### Hue ranges

Master opens by default. Master and Range 1–4 have separate tabs without an overall heading or nested parameter groups.
Each Range has an Enabled checkbox before its tab label. Source Saturation (HSV) and
Output stay below the tabs and are available on every page. Switching tabs does not enable a Range.

**Color → Hue & Saturation** combines whole-image correction in **Master** with up to four hue
ranges that override it. Hue shifts the source hue; Saturation changes its distance from gray,
and Lightness moves it toward black or white. Their neutral values are 0, 1 and 0.

To remove all color except blue, set **Master → Saturation** to 0. Enable **Range 3**, keep
**Hue Range** at 210°–270° and **Saturation** at 1, then adjust the endpoints and **Feather**. The blue core
keeps its original color; Feather fades it into the desaturated surroundings.

All ranges start disabled. Enable a Range and set its Hue Range endpoints/Feather in degrees, then
adjust Hue/Saturation/Lightness. These settings replace Master inside the range; they are not
applied after it. Disabled ranges retain their values and show gray controls that cannot be edited;
their tab-header Enabled checkboxes remain available.
Ranges select the original hue, before any correction. Overlaps average range corrections;
Master is not part of that average. **Source Saturation (HSV)** sets the cutoff (X) and full-strength point (Y) for
range selection in weakly colored areas, which still receive Master correction. Gray pixels
also use Master. Hue Range shows one 0°–360° turn. Handles wrap across the edges:
330° → 30° selects through red. Drag the selected region to move the whole range.
Handles stop where they meet: at zero width when narrowing and at a full circle when widening, without jumping to 0°/360°. Reversing direction responds immediately, and both handles stay visible at the track edges.
At the full-circle stop, both fields show the meeting angle and the whole track stays selected. At the zero-width stop, no part of the track is selected; with zero Feather it selects nothing. Entering 0° → 360° also selects all hues.
In repeating numeric fields, values outside the range wrap: 370° becomes 10°, −10° becomes 350°.

Use **Output → All Ranges Mask** or an individual range mask to inspect the selection. Mask
brightness includes source transparency and does not change with Master. **Result** preserves alpha and HDR brightness;
the header **Opacity** blends any selected output with the original.
With all ranges disabled, the effect uses the faster whole-image calculation.
Lightness can lift black to white; positive values also draw HDR highlights toward reference white.

### Negative
**Color → Negative** blends the source RGB toward its inverse (`1 - RGB`) with **Amount**.
Alpha is preserved by default; enable **Invert Alpha** to apply the same blend to it.

### Mask
**Color → Mask** reads a chosen channel from **Mask** (Self by default). **Transform** positions the mask, **Profile** remaps its values, and **Invert** reverses the result. **Apply To → Channels** selects which channels to multiply; only alpha is enabled initially. In **Color** mode, each color component controls how much of the original channel to preserve: 1 leaves it unchanged, 0 applies the full mask. This is not a tint. **Amount** scales the overall effect.

### Gradient mapping
**Color → Gradient Map** recolors shadows, midtones and highlights using a gradient.
**Source Channel** selects Luminance (default), R, G, B or Alpha as the gradient position.
The selected value chooses the full gradient color, not a single output component. RGB sources
use sRGB values; Alpha is read directly. Reverse and Mapping apply after source selection.
Click **Gradient** to choose colors, use **Amount** to mix with the original image and
**Reverse** to swap the mapping direction. Input brightness outside 0..1 uses the endpoint colors.
Gradient alpha multiplies source alpha: transparent stops hide the corresponding tones,
while an opaque gradient preserves source transparency. FX strength controls both color and alpha;
fully transparent source pixels stay invisible.

### Pixelation and dithering
**Stylization → Pixelate** replaces every block of **Pixel Size** canvas pixels with a single value.
**Average** samples a 4×4 grid inside the block instead of its center, so thin details survive.
**Levels** sets how many values each channel keeps and **Gamma** moves the tonal steps between
shadows and highlights. **Dither** picks the pattern that spreads the error between levels:
**Bayer2**, **Bayer4** and **Bayer8** give the classic ordered look, **Interleaved** is irregular
noise, **Checker** is a two-tone grid, **Halftone** builds a clustered-dot screen and **Hash** is
stable noise without a visible grid. The pattern is evaluated per block, so it stays visible after
pixelation. **Dither Strength** weakens it down to plain rounding. **Color → Quantization** uses Levels and Gamma; **Color → One Bit** uses **Low Color** and **High Color** by luminance instead. **Offset** shifts the grid without moving the layer. Alpha is preserved unless **Alpha Clip** is enabled; **Alpha Cutoff** sets the transparent/opaque boundary.
The same pattern list works per pixel in **Stylization → Posterize**.

### Edge contours

Edge Outline reads neighboring pixels to find contours. Its **Pixel Local** status is
**No**; the built-in preset uses the supported explicit-LOD sampling functions and
compiles without a locality warning.

Choose **FX → Add FX → Edge Outline** to draw lines along sharp transitions.
**Method → Boundary** creates uniform contours; **Scharr** makes weaker transitions less opaque.
In Scharr, raise **Strength** to make these lines stronger. This control is hidden in Boundary.
Both methods share the controls below; switching methods keeps their values.
**Detection → Color** finds RGB changes, including between colors of equal brightness.
**Luminance** finds only brightness changes, ignoring color changes of equal brightness.
Raise **Threshold** to ignore weaker transitions, gentle gradients or small texture variations.
**Hue** outlines changes in color tone rather than brightness or saturation. **Hue Threshold (°)**
sets the minimum difference in degrees (0–180). Raise **Min Saturation** to ignore nearly gray
colors; a boundary is ignored if either side falls below it. Gray and black are always excluded.

**Thickness (px)** sets the total line width, centered on the boundary; **Softness (px)** softens its
edges. Both use canvas pixels. Scharr also responds to the width of the image's transitions:
broad transitions can form bands rather than a precisely uniform line. **Contour** chooses rounded (**Round**), square (**Square**) or
diamond-shaped (**Diamond**) joins. **Color** in Output sets the tint; its alpha controls opacity.
**Output → Overlay** keeps the image; **Outline Only** leaves just the lines on transparency.
The Opacity field in the FX header blends the full result with the original image: 0 keeps
the original, 1 applies the full effect. This also applies to Outline Only.
Zero Thickness removes the contour. Source transparency is preserved: this FX does not
outline an alpha-only silhouette or fill transparent areas. Wider lines take more time to render,
especially with Scharr.

### Other stylization effects
- **Step** thresholds the enabled color channels separately. Red, Green and Blue start enabled; Alpha starts disabled. Choose **Hard** for a two-value result or **Smoothstep** to soften the transition with **Hardness**. **Apply To → Color** uses RGBA as per-channel effect strengths: 0 preserves the original channel, 1 applies the full step. It does not replace the image with that color. **Threshold** instead compares **Source Channel → Luminance/R/G/B/Alpha** with a threshold and maps RGB between **Low Color** and **High Color**. **Transition Width** softens the boundary; zero gives a hard threshold. Source alpha is preserved, even when Alpha is selected; output color alpha is ignored.
- **Halftone** turns the image into monochrome, CMYK or RGB dot screens. Set dot size and shape; CMYK/RGB modes also expose screen angles and manual or automatic plate registration. Enable **Fixed Shape** to sample one color at each rotated cell's center instead of deforming the pattern at color transitions. In this mode, plate offsets move both the image and its screen; dot sizes can still differ between cells.
- **Chromatic Aberration** shifts red and blue in opposite directions, radially from a point or along an angle. **Amount** is in canvas pixels; green and alpha stay unchanged.
- **CRT** combines curved edges, scanlines, RGB phosphor stripes, vignette, color fringing, grain and flicker. **VHS** adds line wobble, chroma bleed, noise and a moving tracking band. **Seed** changes the deterministic pattern; **Effect Time** selects another frame.
- **Digital Glitch** combines line tears, independently segmented corruption blocks, channel shifts, color/noise/alpha artifacts and optional gradient tinting. **Seed**, **Effect Time** and **Frame Rate** control its repeatable animated variation; **Block Order** chooses independent row-first or column-first block layouts.

### Lighting and embossing
- **Normal Map → Lighting** turns an RGB normal map into a shaded surface. **Normals** defaults to Self. Set **Light Direction** with its canvas handle, then adjust **Base Color**, light/shadow colors, **Intensity** and **Ambient**. Set **Input Encoding → Gamma** for the Normal Map layer’s default output, or **Linear** for raw linear channels. **Flip Y** reverses the green-axis convention. Platform-packed normal textures are not supported.
- **Lighting → Bevel Emboss** derives normals from **Height Map** (Self by default, or another layer). **Height Channel** selects luminance, R, G, B or Alpha; **Profile** maps height, **Depth** raises or engraves, and **Smoothing** sets the sampling radius in document pixels. For SDF, the visible gradient and distance ranges define bevel width.

Both effects use **Base Color alpha** to blend transparent lighting (0) into the filled, shaded surface (1). Intermediate values crossfade with alpha-aware mixing. RGB tints the surface; **Ambient** fades in with it. **Output** selects Both, Highlight Only or Shadow Only for the transparent component and has no effect at Base Color alpha = 1. Light Color/Shadow Color alpha controls that component's strength. Overall opacity and blending belong to the layer. Both effects default to Base Color alpha 0. Re-add the preset to update an older embedded copy.

**Normal Map → Lighting** also has an optional **Specular** block, disabled by default. Enable it to add a reflective highlight using the same **Light Direction**. **Specular Color** sets its tint (alpha scales strength), **Specular Intensity** controls strength independently of the diffuse light, and **Roughness** widens the highlight as it increases. Zero Roughness uses a finite sharpness limit. Both and Highlight Only include the enabled highlight; Shadow Only excludes it. The additional **Specular Only** output omits the surface and diffuse light even when Base Color alpha is 1; with Specular disabled it is transparent. Specular follows input alpha, adds light without darkening the diffuse result and retains intensities above 1 in HDR. The orthographic viewer faces +Z, so frontal light can illuminate an entire flat region rather than only the rim. Bevel Emboss is unchanged.

Bevel Emboss reads neighboring heights, so **Pixel Local** is **No**. The built-in preset
compiles without a locality warning.

### Normal map normalization
**Normal Map → Normalize** restores unit-length normals while preserving alpha.
Use it after processing an RGB normal map if its vectors have changed length.
Neutral `(0.5, 0.5, 1)` stays unchanged; an undefined direction becomes neutral.
Set **Encoding → Gamma** for the Normal Map layer’s default encoding, or **Linear** for raw linear channels. This setting applies to both input and output.
This toggle accounts for color encoding, but does not unpack platform-specific normal-map formats.

## Distortion presets

Choose **FX → Add FX → Spherize**, **Twirl**, **Radial Shear** or **Displacement Map**.

The numeric field in each FX header controls distortion: **Strength** for Spherize and
Radial Shear, **Angle** for Twirl, and **Amount** for Displacement Map and Polar Coordinates.
It changes the mapping, not the opacity of a finished result.

- **Spherize / Mode:** `Classic` keeps the existing unbounded radial distortion; `Sphere` projects the image onto a sphere and clips it to a circular silhouette. The edge is antialiased by about one pixel.
- **Spherize / Strength:** positive values bulge the center; negative values pinch it. In `Classic`, zero leaves the image unchanged. In `Sphere`, zero keeps the circular shape but removes the texture distortion.
- **Twirl / Angle:** twists around the center; the sign reverses direction. The angle is measured in degrees at the frame's local radius 1 and grows with distance.
- **Radial Shear:** twists sampling coordinates progressively farther from **Center**; **Strength** controls the direction and amount, while **Offset** adds a base shift.
- **Area / Edit on Canvas:** move, resize or rotate the green coordinate frame. Stretch it to make the distortion elliptical.
- **Displacement Map / Mode:** `VectorRG` reads R/G as a signed direction field; the `Neutral` value (0.5 by default) means no offset. Use a linear/data texture for vector and height maps. X/Y strengths are in canvas pixels. `Grayscale` reads a selected channel and moves pixels horizontally, vertically, radially, tangentially, or along an angle. `ParallaxOcclusion` treats the selected map channel as height and shifts the sample along a virtual view ray.
- **Displacement Map / Amount:** multiplies X/Y strengths, Grayscale strength or Parallax depth. Zero keeps the original image; 1 uses the configured values, and values above 1 amplify them. **Output Mix** remains a separate image blend.
- **Parallax / Depth and View:** **Depth** sets the height range in canvas pixels; **View Angle** sets the ray direction, and a lower **View Elevation** increases the shift. **Invert Height** swaps raised and recessed areas. **Parallax Steps** selects the quality/cost tradeoff (4–32 height samples); the default is 8.
- **Map / Transform and Wrap:** position the map independently. `Clamp`, `Repeat`, and `Mirror` control map coordinates; they do not affect the displaced image's edges.
- **Strength Mask:** `Constant1` is the default, so one map is enough and no strength mask is required. `MapChannel` reuses a channel of that same map, `InputAlpha` follows the input image's alpha, and `SeparateTexture` adds an optional second mask texture. Invert the mask or remap it with **Mask Profile**.
- **Output / Mix:** blend the displaced sample with the original.

**Tiling**, below each distortion's parameters, controls samples beyond the input image:
`Clamp` extends edge pixels, `Repeat` tiles the image, `Mirror` reflects alternate tiles,
and `Clip` returns transparency. Distortions default to Clamp; UV Transform defaults to Clip.
This is separate from the layer's Tiling and Displacement Map's Map Wrap. Sphere keeps its
circular silhouette regardless of Tiling. These modes sample the existing image;
they do not extend a procedural source beyond the raster.

`Classic` Spherize, Twirl and Polar Coordinates do not mask at the frame edge, so distortion continues outside it.
`Sphere` is the exception: it creates a circular mask with a crisp, antialiased edge. Strong settings
in unmasked modes can reveal areas beyond the input image, where its edge pixels are extended. RGB and alpha are distorted together.

### Domain Warp

**Distortion → Domain Warp** generates an RG displacement map rather than distorting the
incoming image. **Warp Type** selects OpenSimplex2, OpenSimplex2Reduced or BasicGrid;
**Seed**, **Scale** and **Offset** change its pattern in canvas UV coordinates. Scale increases
pattern frequency, and Offset moves the pattern without adding a constant displacement.
There is no automatic aspect correction or seamless repeat.

**Fractal** selects None, Progressive or Independent. **Octaves** is rounded to an integer;
**Lacunarity** and **Gain** control frequency and amplitude per octave. Progressive uses
previously warped coordinates; Independent keeps the original sampling coordinates.
Header **Strength** controls warp amplitude: zero gives a neutral map, not the input image.
Negative Strength reverses amplitude; in Progressive it can also change the pattern.
**Flip X/Y** reverse the final components without changing the pattern.

**Packed RG** (default) encodes `0.5 + 0.5 * displacement`; **Signed RG** outputs the raw
vector. Neither mode normalizes or clamps it. Assign the map layer to **Displacement Map**,
select **VectorRG**, and use **Neutral** 0.5 for Packed or 0 for Signed. For example, Packed
Strength X/Y of 16 matches Signed Strength X/Y of 8. Keep the map layer at **Color Range → HDR**
for signed or strong vectors; use linear EXR for external storage. The map layer can be hidden.

### Height to Distortion

Add **Distortion → Height to Distortion** to a height-map layer to generate a displacement
field. **Height Map** defaults to Self; **Channel** selects luminance, R, G, B or Alpha.
Use linear height data. **Strength** in the header scales the field; negative values reverse
it and zero produces a neutral map. **Filter** offers Scharr (default), Sobel and Central
Difference. **Radius** sets the sampling distance in pixels; **Edges** offers Clamp, Repeat
and Mirror. **Flip X/Y** reverse individual directions. Positive vectors point toward
increasing height; vector length is not normalized, so steep slopes remain stronger.
Its **Pixel Local** status is **No** because the filters read neighboring heights;
the built-in preset compiles without a locality warning.

In **Output**, **Packed RG** (default) has neutral `(0.5, 0.5)`; **Signed RG** has neutral
`(0, 0)`. Assign this layer to **Displacement Map**, choose **VectorRG**, and set **Neutral**
to 0.5 or 0 respectively. For the same displacement, Signed needs half the X/Y strengths
used with Packed. The map layer may be hidden.

Choose **Color Range → HDR** on the map layer to retain negative and above-one values;
Packed does not clip strong vectors either. For external signed/HDR maps use linear EXR:
ordinary color-image exports do not preserve raw signed data.

### Polar coordinates

**Distortion → Polar Coordinates** is one effect with a **Mode** selector (To Polar by default):

- **Amount** in the FX header gradually changes sampling coordinates: 0 keeps the original image, 1 applies the full mapping below.
- **To Polar** wraps a strip into a circle: horizontal runs around the center, vertical runs outward.
- **From Polar** unwraps a circle into a strip: left to right covers one full turn, bottom to top covers distance from the center.

**Input** selects the source frame: a strip for To Polar, a circle for From Polar.
**Output** positions and shapes the output: a circle for To Polar, a strip for From Polar.
Both frames have position, size and rotation, plus **Edit on Canvas** and reset buttons.
Their default position `(0.5, 0.5)`, size `(1, 1)` and rotation `0` cover the whole image.
Move, scale or rotate Input to choose the source pattern independently of its placement in Output.
**Angle Offset** shifts the start of the turn in degrees; zero starts to the right of the center and runs counterclockwise.
**Radial Offset** shifts the starting radius: positive values move the pattern outward in To Polar,
or start sampling farther from the center in From Polar. One unit equals half the circle frame's width or height along its axes: Output for To Polar, Input for From Polar.

Radius continues beyond the frame without a fade. Match the left and right edges of the source strip
to avoid a visible seam around the circle. The entire strip width converges at the center,
so unwrapping cannot recover details lost at that point.
