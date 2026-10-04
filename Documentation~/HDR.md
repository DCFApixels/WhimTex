---
layout: default
search_exclude: true
title: HDR and groups
parent: Technical reference
nav_order: 3
lang: en
permalink: /reference/hdr/
---

# Color range, HDR and groups
{: .no_toc }

<details markdown="1">
<summary>On this page</summary>

- Contents
{:toc}

</details>

## Layer controls

The compositor uses **linear floating-point working pixels** and straight alpha. Alpha stays in 0–1.
Each layer has two independent controls:

| Control | Standard (default) | HDR |
| :--- | :--- | :--- |
| Color Range | Clamp the layer's own output after transform, all FX and Swizzle, before blending. | Preserve signed RGB outside 0–1. |
| Blend Range | Bounded blend functions in the legacy sRGB blend space. | Extended blend functions and compositing in linear light. |

Standard blending bounds the overlap function, **not the entire accumulated image**. A transparent
or zero-opacity layer cannot clip HDR underneath. Non-overlap keeps the original layer color.
Overwrite still replaces RGBA, including transparent pixels; None does nothing.
Old documents default to Standard, with pass-through groups at opacity 1.

FX receive linear, straight RGBA. Their intermediate results are not saturated just because the
owning layer is Standard. Existing custom code that assumes encoded RGB may need explicit color conversion.
Color pickers and API color arrays retain the encoded RGB convention; source textures honor their
imported sRGB setting. Source import settings are never changed by the compositor.

## Color compatibility

Swizzle remaps straight linear RGBA after FX and before Color Range. The four selectors accept
`R`, `G`, `B`, `A`, `1-R`, `1-G`, `1-B`, `1-A`, `0`, `1`, `R * A`, `G * A`, `B * A`;
products use the original input alpha, and inversion means literal `1 - channel`
in linear space. Alpha is clamped to 0–1 after remapping. Identity is serialized as zero, so old
documents keep `R G B A`. A nonidentity group Swizzle forces isolation; a Pass Through group
temporarily uses Normal blending and resumes Pass Through when Swizzle returns to identity and clipping is inactive.
Source pixels are not rewritten. Conversion keeps a regular layer's Swizzle as an editable setting,
while group conversion and merging bake it once. PSD group baking retains original children in a hidden folder.

Brush uniforms and newly applied Shader FX color uniforms use vector properties carrying linear RGBA.
Color-to-linear conversion occurs once, not both in C# and Unity's material upload. Previously applied
FX with native Color properties remain supported without reapplying their code.

Color Fill stores encoded RGB in `storedColor` and decodes it once for the linear render.
The picker, API and PSD solid-color metadata use that encoded value. The C# `color` property
accesses the same field. Files saved by 0.12.5 use this canonical representation; earlier
Color Fill storage must be normalized in 0.12.5 before upgrading.

These compatibility rules do not rewrite Drawing pixels or imported textures. Strokes already painted
with incorrect color conversion have that color baked into their pixels; they cannot be automatically
distinguished from intentional colors. Restoring those strokes requires Undo or an earlier document.

CPU writes encode RGB only for sRGB destinations. Linear 8-bit textures remain linear; float textures
retain signed HDR. Alpha is never gamma-converted.

## TIFF output encoding

The canvas **sRGB** toggle changes output encoding only, not working space, FX buffers, Drawing storage,
picker conventions or layer parameters. For 8-bit TIFF, on encodes composed linear RGB to sRGB and sets
`TextureImporter.sRGBTexture=true`; off writes linear RGB and sets the flag false. Alpha is unchanged.
Float32 output is always linear. New ordinary documents default to sRGB; opening a TIFF initializes
the pending setting from its importer. The canvas toggle participates in document Undo and enables Save;
neither TIFF nor importer changes until Save. Save uses the pending setting even when only encoding changed.

Changing encoding via Inspector Apply renders a detached copy of the saved model and atomically replaces
only the TIFF composite and carrier flags. Editable model/pixel blocks are preserved, including unknown
container blocks. The open document's unsaved edits and dirty state are retained and its disk revision is
advanced. An incomplete model or invalid FX blocks conversion. Cancellation/failure restores the importer
flag to the encoding actually on disk. No repeated conversion of an already quantized output is used.

Inspector reimport detects a mismatch between importer and carrier flags and queues conversion after
import completes; a guard prevents recursive reimports. This applies only to WhimTex TIFFs in Assets,
not ordinary source images. Reimport stops Live Update. A changed external dependency can affect the newly
rendered saved output, just as it can on Save. Inspector Apply is a disk/import operation, not layer Undo;
it synchronizes the open document's pending encoding while retaining other unsaved edits.

## Color picker and history

HDR describes the stored floating-point color range, not physical HDR monitor output. The current editor UI presents SDR previews: the picker clamps display RGB to [0,1] after exposure, and the gradient editor bakes its exposed strip into RGBA32. Native color-field intensity gradients are visual indicators, not HDR display output. An HDR-capable monitor alone does not change this behavior. Preview EV helps inspect values above display white without changing them; it does not enable an HDR swap chain or display transfer function.

### History storage and filtering

History filtering uses the input's active HDR mode, not Preview EV or channel masks. Standard mode shows only entries with every raw RGB component in [0,1]; HDR mode shows all entries. Alpha is not part of this test. Gradient History follows the selected key's `color.hdr`, rebuilding when that mode changes while retaining a full unfiltered snapshot. Hidden entries stay in document history. Tiles retain their original document indices for selection, drag/reorder and deletion; stale selection callbacks cannot apply a now-hidden HDR value. Gradient History shows an empty-state label if all entries are filtered out.

### Viewing exposure and numeric entry

The picker's window-local `Preview EV` footer uses the gradient editor's −10..10 range and linear-light display calculation: decode RGB to linear, multiply by `2^EV`, encode, then clamp for display. Swatches use raw stored HDR before clipping, and HSV controls include the current HDR intensity. Alpha, numeric fields, callbacks and History data are unchanged. Channel adaptation follows the display transform, so alpha-only remains independent of EV. Hue ring, native eyedropper, checkerboard, alpha bars and the add tile are unchanged. A new picker starts at EV 0; UI rebuilding preserves its local value. No render texture or internal API access is added.

Numeric entry mode (RGB 0–255, RGB 0–1 or HSV) is stored immediately in the user-wide `WhimTex.ColorPicker.ColorMode` EditorPrefs key and restored when building the picker UI. Missing or invalid values fall back to HSV. Closing or canceling does not revert this preference; switching modes only refreshes controls and does not modify RGBA, HDR state, callbacks or document history.

### Gradient History

The gradient editor's document-local History is a shared swatch/drag implementation without the add tile. Applying a swatch uses StoreDisplayColor through the normal gradient Edit/Commit path: encoded RGB converts to the gradient's working color space, stored HDR is retained, per-key intensity/HDR overrides are reset and the independent alpha track/key position are unchanged. Alpha-key and midpoint selections disable the palette. History/owner changes and channel-mask changes refresh the palette while attached. RememberColor promotes an existing exact RGBA entry instead of duplicating it; ordinary picker edits call it only on confirmation (the explicit add button remains immediate).

### Channel-adapted display

`WhimTexColorField.UseCanvasChannels` explicitly opts document-color inputs into channel display; service colors remain ordinary. `WhimTexColorInputs.Bind` opts in layer/brush/FX bindings. An ancestor channel provider identifies the originating compositor window; detached Properties/gradient inputs fall back only to a unique open window for that document. Ambiguous or unavailable ownership uses ordinary display, never the focused unrelated document. Gradient sessions carry the originating provider into their detached editor and key picker. Layer Preview masks are not sources.

The persistent `Channels` preference changes rendering only. Two/three active RGB channels zero excluded components; one RGB component is grayscale; alpha-only is opaque grayscale alpha; no channels is black. Numeric RGB/HSV/HEX, HDR intensity, callbacks, History and serialization remain unmasked. Existing painting-channel semantics are unchanged. `Channels` is hidden for inputs without a channel source. Source changes refresh visible controls without changing their values.

The native `ColorField` remains the value/event/binding/focus container. A non-pickable Painter2D overlay in its public USS color container draws original upper-left / adapted lower-right RGB, below the native HDR/mixed labels and single actual-alpha ProgressBar. No new internal reflection is used. The custom overlay is absent for ordinary inputs and bypassed for mixed values, disabled channel adaptation or full RGBA. If the expected public visual structure is unavailable, native rendering remains. Picker swatches use the same diagonal comparison with one actual-alpha bar; ring/plane/ramps adapt their color visuals. The native screen magnifier remains an accurate, unmasked screen capture.

### Picker lifecycle and input

`WhimTexColorField` opens the UI Toolkit `WhimTexColorPicker`; gradient keys use the same window.
The gradient editor tracks its own key picker, suspends focus-loss dismissal while that picker is open, and clears the pending dismissal and restores focus when it closes (accept or Escape). Unrelated pickers do not hold the gradient editor open.
For HDR fields the native upper-left rendering is left intact. The lower-right triangle reuses the native field's resolved color/gradient geometry, colors and alpha-ramp textures through public visual-tree/style and MeshGenerationContext APIs, masking RGB without normalizing away HDR intensity. Alpha-only remains a constant alpha sample. The borrowed native textures are neither modified nor destroyed; no render texture or new internal reflection is involved. Changing HDR mode invalidates the overlay even when the channel mask is unchanged.
Its range policy is `Switchable`, `StandardOnly` or `HdrOnly`. HDR toggling changes the entry mode,
not the stored RGBA. Explicit edits use nonnegative RGB up to 65504 in HDR or 1 in Standard;
hidden alpha is preserved. Closing the picker confirms the color; Escape restores the opening value, including its original HDR intensity. There are no OK/Cancel buttons.
Hex entry accepts case-insensitive RGB/RGBA hexadecimal with an optional leading `#`, in 3/4-digit shorthand or 6/8-digit form. A separate right-aligned `#` label precedes the input; normalized display is uppercase six-digit RGB. RGB input preserves current alpha; RGBA applies parsed alpha when editable, retaining the fixed original alpha otherwise. Current HDR exposure multiplies RGB only, never alpha. Invalid input restores the current display without changing the color. Native TextField clipboard paste and delayed commit (Enter/focus loss) are preserved.
### Eyedropper integration

The eyedropper uses Unity's public ColorField control. Picker opening no longer reflects into Unity internals.
The optional pixel magnifier has a specifically authorized, isolated reflection adapter to `UnityEditor.EyeDropper.IsOpened`, `DrawPreview(Rect)` and `End()` only. Delegates are bound once per domain; no reflected start, capture or selected-color access is used. A public ColorField still launches sampling and delivers the result. The magnifier uses an IMGUIContainer in the fixed wheel area and requests repaints at most 30 times per second while this picker owns sampling. Local ownership is established only after an idle eyedropper button click; unrelated active sampling is not adopted or canceled. Selection/cancellation restores the wheel; Escape cancels sampling first, and window close/reload cancels owned sampling. Missing members or drawing failure disable the magnifier, not ordinary color selection. This narrow exception does not authorize other internal Unity reflection.
Alt screen sampling routes through the open primary brush color picker's ordinary edit callback when its source context matches the originating WhimTex window and its field remains valid. Other pickers (including secondary brush color, layer and gradient inputs) are not synchronized. The sampler retains brush alpha; picker channel/Hex/marker state is decoded from the sample. Closing confirms it, Escape restores the opening color, and intermediates are not recorded in history. Without a matching valid picker the existing direct brush-color path is unchanged.

### History interaction and persistence

History headers in the picker and gradient editor share the dark foldout style with gradient Presets. Expansion states default to open and persist independently in EditorPrefs under `DCFApixels.WhimTex.ColorPicker.HistoryExpanded`, `DCFApixels.WhimTex.Gradient.HistoryExpanded` and `DCFApixels.WhimTex.Gradient.PresetsExpanded`. These editor-local preferences do not modify document history or presets.

Confirmed colors are prepended uniquely to the owning document's serialized `colorHistory` list.
The leading gray plus button explicitly remembers the current RGBA without closing the picker; it is not a draggable swatch or a reorder target. It is disabled for non-document inputs.
Slider intermediates and canceled edits are not automatically recorded. Explicit additions remain after Escape, like history reordering/removal. Selecting a history swatch applies it and moves that exact stored RGBA entry to index zero, preserving the order of all other entries. This explicit reorder also remains after Escape. Confirming a manually entered exact existing RGBA color also promotes it, without duplication; alpha and HDR intensity participate in equality. History edits use document Undo and
dirty tracking, with no changes to layer colors. History stores encoded RGBA with HDR intensity;
it uses the existing tagged model serializer, not a new TIFF block or sidecar file. Non-document
inputs have no history. API/clipboard color writes do not populate history and their contract is unchanged.

## Input mode

The **HDR** button beside **EV** in the main preview footer sets the shared input preference
for bound WhimTex color fields, including brush/fill colors, layer properties and Shader FX color parameters.
It defaults to off (Standard) and persists between sessions. Standard shows a bounded color representation and
uses that same representation for new brush/fill operations. RGB above 1 is divided by its largest
component, preserving encoded RGB proportions; negative components are displayed as zero. Alpha is
unchanged within 0–1. Gradient editing does not inherit this shared preference: its HDR switch controls
the selected color key, initially inferred from that key's intensity or negative RGB unless overridden
in the current gradient session. The key's picker and History filter follow that per-key mode.

Switching modes never rewrites stored colors: returning to HDR restores their full intensity.
Explicitly editing a color or gradient in Standard replaces that value with the edited bounded value.
Existing layer rendering, pixels, layer ranges and exports are untouched by the picker preference.
The agent API uses its explicit colors independently of this UI preference. The preference is outside
document Undo and is restored by **Reset WhimTex Settings**. Open fields update without rebuilding
their UI or sending value-change events.

## Drawing storage and editing

New Standard Drawing layers store RGBA32 (4 bytes/pixel). Selecting HDR promotes owned pixels to
linear RGBAHalf (8 bytes/pixel). Switching back to Standard **does not compact or discard** that data.
The storage label shows the actual format. **Convert to 8-bit** explicitly clamps and quantizes source
pixels; its confirmation explains the loss. Undo records both format and pixels.

Painting on a Standard layer with retained HDR storage reads a clamped destination under the stroke.
Pixels outside the footprint keep their hidden HDR values. The eraser changes alpha without clamping
RGB. Tool settings are still outside document Undo; pixel edits and range changes participate in Undo.

Brush and fill colors exceeding half-float capacity are limited by intensity: after linear conversion,
the largest absolute RGB component is capped at 65504 and the other components are scaled equally.
This preserves the linear RGB ratios instead of turning a bright component black. The selected picker
value is not rewritten. Standard layers additionally clamp paint to 0–1. Working drawing surfaces and
HDR storage remain half-float; no automatic float32 storage promotion is performed.

Fill uses floating-point source and reference buffers. All Layers samples the actual composition,
before preview exposure/channel/debug visualization. Current-layer sampling respects its Color Range.
Tolerance remains 0–255, representing a premultiplied component difference of 0–1; it is not normalized
to the brightest HDR pixel. SDF thresholds likewise retain their existing bounded, encoded 0–255 convention.
The distance mask may be 8-bit; SDF gradients and Outline colors are generated in floating point.

## Groups

**Pass Through** lets children see the external backdrop. At reduced group opacity, the compositor
interpolates the complete before/after result in premultiplied linear RGB and alpha. It does not multiply
every child's opacity. Color Range and Blend Range are inactive in this mode.

Choosing any other group blend mode isolates its children on a transparent buffer. The group then
applies its own Color Range, blend mode, Blend Range and opacity to the parent stack.
Nested groups follow the same rules. Group FX process the combined children before Swizzle, Color Range and outer opacity/blending. FX force isolation with Normal blending when the saved mode is Pass Through. Group transforms remain unsupported.

Outline/SDF group targets use only the group's own content against transparency, including nested
opacity and alpha-replacing modes. They never include the external backdrop.

Clipping chains preserve their base's alpha; clipped members blend colors without accumulating
opacity into that alpha. The base's opacity and blend are applied to the complete chain once.
Groups participating in clipping (as base or clipped member) are isolated even when configured
as Pass Through, using Normal in that case. Their ranges become active while isolated.
Swizzle and clipping are independent reasons for isolation: Pass Through resumes only after
both restrictions are removed. Clipped Overwrite replaces source-covered RGB, not base alpha.

## Preview and numeric diagnostics

**EV** changes preview exposure in stops, without changing pixels, fill sampling or output.
The bug button beside RGBA toggles an error overlay (magenta by default, configurable in **User Settings…**). It starts off; a tinted icon indicates errors
reported by a small asynchronous GPU readback when that feature is available.

NaN and infinity are replaced with zero. Finite render-stage components outside the half-float range
(±65504) are clamped to that range rather than replaced with black.
Detection runs before layer saturation and half-float writes at render-stage boundaries. Alpha is then
clamped to 0–1. Negative finite RGB within that range is valid, even if a blend produces unusual results.
The mask accumulates participating stages within the current composition, including subsequently covered
pixels; render-stage errors are recalculated when the composition renders again. Disabled/no-op/zero-opacity layers are skipped.
It reports stage output locations, not an instruction-level trace inside custom shader code.
Preview quality determines the diagnostic sampling resolution. Error overlays are never baked into files.

The mask describes rendered data, not a separate history of input colors. Paint intensity limiting
does not add diagnostic marks. Disabled channels explicitly zero their values rather than multiply
NaN by zero.

<a id="layer-mini-preview"></a>

### Layer Preview

When available, Layer Preview uses the cached layer result from Canvas View, before
blending with other layers. This avoids differences caused by a separate lower-resolution render.
Otherwise it renders independently; clipped layers and pass-through groups use this fallback.
A group shows only its own colored content against transparency.

## Save and export

Saved compositor output and standalone Texture2D assets use linear RGBAHalf. Owned Drawing formats,
range settings and group settings survive saving, reopening and duplication. EXR preserves HDR.
PNG/JPEG/TGA and the current 8-bit PSD exporter receive a separate clamped, encoded copy. Converting
that copy never changes the source document. PSD retains folder blend modes and opacity where supported;
extended HDR blending can differ when another application recomposites its editable 8-bit stack.

C# callers: `TextureCompositor.ComposeCanvas()` now returns an owned, readable **RGBAHalf** Texture2D.
Do not reinterpret its raw bytes as Color32. Use `GetPixelData<Unity.Mathematics.half4>(0)` for native
access, or the format-independent pixel APIs. The caller must destroy the returned temporary texture.
The JSON agent API retains its existing PNG render output and adds explicit range/group settings.

The composite HDR texture output is linear, including in projects configured for gamma rendering.
Materials using it must treat it as linear data; the editor preview handles display conversion itself.
This texture output is distinct from HDR monitor output.

## Verification

`Tests~/HdrGroupSmoke.cs` is an opt-in live-Editor regression script, to run **after manual compilation**.
It uses only transient in-memory documents. Save/reopen and native Texture2D Undo require an Editor run;
source parsing and the standalone PSD writer tests do not replace that validation.

`Tests~/ColorPipelineSmoke.cs` adds checks after manual C# compilation and shader import: brush colors
and alpha in Standard/HDR storage, signed HDR, legacy/new Color Fill JSON round-trips, File textures,
gradients, SDF/Outline, Standard opacity, cached/new FX color uniforms, preview, PNG, flood fill and CPU
destination encoding, excessive paint intensity, retained half-float storage and RGB ratios.
It uses transient resources only and does not record Undo or save/import assets.
Run it in the project's existing color space; it does not change project settings. In-memory JSON/PNG
round-trips are not a substitute for a separate persistent-asset save/reopen test.

`Tests~/ColorInputModeSmoke.cs` checks Standard/HDR display conversion, retained source values, field
refreshes, color swapping and gradient copies after manual compilation. It does not write preferences
or assets. Attached picker interaction and serialized Shader FX field Undo still need an Editor check.
