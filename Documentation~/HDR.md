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

Legacy Color Fill layers used their stored RGB directly in linear projects. Those serialized values
retain that interpretation; the picker, API and PSD solid-color metadata expose the corresponding
encoded color. Editing a color switches that layer to explicit encoded storage. This is tracked in
the serialized layer, so reopening, copying and Undo do not repeatedly convert it. In C#, `color`
is now a property; existing serialized `color` fields are read through `FormerlySerializedAs`.

These compatibility rules do not rewrite Drawing pixels or imported textures. Strokes already painted
with incorrect color conversion have that color baked into their pixels; they cannot be automatically
distinguished from intentional colors. Restoring those strokes requires Undo or an earlier document.

CPU writes encode RGB only for sRGB destinations. Linear 8-bit textures remain linear; float textures
retain signed HDR. Alpha is never gamma-converted.

## Drawing storage and editing

The **HDR** button beside **EV** in the preview footer sets the color and gradient picker mode globally
for WhimTex, including brush/fill colors, layer properties and Shader FX color parameters.
It defaults to off (Standard) and persists between sessions. Standard shows a bounded color representation and
uses that same representation for new brush/fill operations. RGB above 1 is divided by its largest
component, preserving encoded RGB proportions; negative components are displayed as zero. Alpha is
unchanged within 0–1. Gradient keys use the same display conversion.

Switching modes never rewrites stored colors: returning to HDR restores their full intensity.
Explicitly editing a color or gradient in Standard replaces that value with the edited bounded value.
Existing layer rendering, pixels, layer ranges and exports are untouched by the picker preference.
The agent API uses its explicit colors independently of this UI preference. The preference is outside
document Undo and is restored by **Reset WhimTex Settings**. Open fields update without rebuilding
their UI or sending value-change events.

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

## Save and export

Saved compositor output and standalone Texture2D assets use linear RGBAHalf. Owned Drawing formats,
range settings and group settings survive saving, reopening and duplication. EXR preserves HDR.
PNG/JPEG/TGA and the current 8-bit PSD exporter receive a separate clamped, encoded copy. Converting
that copy never changes the source document. PSD retains folder blend modes and opacity where supported;
extended HDR blending can differ when another application recomposites its editable 8-bit stack.

C# callers: `TextureCompositor.Compose()` now returns an owned, readable **RGBAHalf** Texture2D.
Do not reinterpret its raw bytes as Color32. Use `GetPixelData<Unity.Mathematics.half4>(0)` for native
access, or the format-independent pixel APIs. The caller must destroy the returned temporary texture.
The JSON agent API retains its existing PNG render output and adds explicit range/group settings.

The main HDR output is a linear texture, including in projects configured for gamma rendering.
Materials using it must treat it as linear data; the editor preview handles display conversion itself.

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
