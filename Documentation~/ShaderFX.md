---
title: "Shader authoring"
parent: "Technical reference"
nav_order: 7
lang: en
permalink: /reference/shader-fx/
search_exclude: true
---

# Shader authoring

### Height-based lighting

Normal Lighting and Bevel Emboss share `SurfaceLighting.cginc`. `_BaseColor.a` blends transparent lighting (0) into a filled surface (1), using premultiplied interpolation and returning straight alpha. Surface RGB uses `_BaseColor.rgb`, Lambert lighting, `_LightColor`, `_ShadowColor`, `_Intensity` and `_Ambient`; its coverage is the host input alpha, without another Base Color alpha multiplication. Transparent lighting subtracts flat-normal lighting and ignores host alpha/Base Color RGB/Ambient. `_Output` selects Both (0), HighlightOnly (1), ShadowOnly (2) for that component only; tint alpha scales its strength. Both effects default to Base Color alpha 0. There is no Render Mode parameter. Identical normals and common parameters produce identical output. The common include is expanded by portable copying.

Lighting/Bevel Emboss is a regular FX over a `texture2D _HeightMap = self` input. It works on any layer, without raw SDF access or layer-specific outputs. Height Channel selects luminance, R, G, B or alpha; RGB channels are multiplied by image alpha before the 0–1 input is mapped through Profile. Depth controls relief strength/sign; Smoothing is the normal sampling radius in document pixels. Output selects Both, Highlight Only or Shadow Only. Output RGB is the light/shadow tint; straight alpha is lighting strength times tint alpha, independent of host alpha. Flat areas are transparent. Choose compositing through the layer blend mode; use separate light/shadow layers for independent modes. SDF bevel width comes from the visible height gradient and Max Distance, not an FX width parameter. The former raw-distance helpers are no longer provided; re-add the preset to replace an older embedded version.

### Texture sources

`// @param texture2D _Source = self` samples the image immediately before this FX, including earlier effects but excluding this and later effects. It reuses the existing input texture without recursively rendering the layer. On groups it reads the composed group input. `// @param texture2D _Source = none` samples transparent black. Both defaults survive HLSL preset export and copying; Self stores no layer ID. Without a default, the existing Texture mode uses white when no asset is assigned. The UI offers Texture, Layer, None and Self. Live FX parameter values also accept the strings `"self"` and `"none"`.

### Layer-backed texture parameters

The `texture2D` declaration and `tex2D` sampling syntax are unchanged. In the editor, choose Texture or Layer. Layer references store a same-document layer ID and resolve the standalone rendered result, including transforms and FX, without its lower backdrop. Disabled sources are allowed as with SDF Target; a disabled Shader Processor retains its bypass semantics. Groups supply full-color contents. Missing or cyclic sources bind transparent pixels.

Layer inputs use the shared effect-render cache for deterministic sources. Shader FX and Shader Processor
results are cached when their inputs and serialized parameters are unchanged; the cache also tracks
external texture updates and referenced layer stamps. Switching sources does not recompile HLSL.
HLSL preset export omits document-local layer bindings. Clipboard JSON does not expose these bindings;
the live FX API accepts a texture parameter value `{ "layer": "layer-id" }` instead of an asset path.
Copying layers remaps references to copied sources and clears uncopied external sources when pasting into
another document.

For browser AI generation, start with the [JSON layers and HLSL authoring guide](AI/README.md).
It is self-contained and includes clipboard-ready examples.

Use **+ Shader FX** in a layer's settings, write `ApplyFX`, and click **Apply**.
Code and parameters can live inside the document. For an effect on the already-composited
stack below a position, add a **Shader Processor** layer instead.

## Shader FX: a first snippet, parameters and reusable code

Add a **Float** parameter named `_Amount`, then apply this example:

```hlsl
float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, 1.0 - color.rgb, saturate(_Amount)), color.a);
}
```

Parameters support Float, Color, Vector, Texture2D and Transform2D. Their uniforms are generated automatically.
Code and declarations stay drafts until Apply; a compile error keeps the last working effect.

`LayerToLocal(uv)` converts canvas UV to local layer UV, including parent transforms and perspective. Use it for procedural shapes that should follow the layer. It does not clamp or wrap UV; `SampleInput` still expects canvas UV.

`SampleInput(uv)` reads the layer after earlier modifiers. Return straight RGBA; opacity/blending
come later. Built-in inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`,
`_CanvasSize` (width, height, 1/width, 1/height) and `_PreviewScale`. Do not redeclare generated uniforms.
WhimTex FX are deterministic: Unity time inputs such as `_Time`, `_SinTime`, `_CosTime`,
`_TimeParameters` and `unity_DeltaTime` are not supported and are not updated by the preview cache.
Their use is allowed for compatibility, but Apply adds a warning to Diagnostics and the result
is treated as non-cacheable. Use an explicit parameter when a value must change the effect.

Standard `#include` supports project/package paths and relative paths. Relative paths start in
the document/FX asset folder, or Assets before the first save. After library edits, click Apply again;
after Save As to another folder, check relative paths. Libraries must suit the fragment-shader environment.

**+ Reference** links an external FX shared by its users; **Embed** makes an independent document-owned
copy. Save As and layer duplication copy embedded FX independently. FX run in order after Transform;
changing parameter values does not regenerate shaders.

## HLSL catalog

Add a `.hlsl` file anywhere in Assets or an installed package. Its **first physical line** must be
`// @whimtex-effect Category/Name`. UTF-8 BOM is allowed, but no preceding blank line, indentation,
license comment or other text. Unmarked HLSL files are not catalog effects. Discovery reads headers only,
without parsing parameters, loading ShaderFX assets or hashing shader dependencies. Project headers
are cached across script reloads within the Editor session and updated by import notifications.
Full validation and loading happen when a preset is selected; invalid source reports an error without adding an FX.

```hlsl
// @whimtex-effect Color/Invert
// @param float _Amount = 1 [0 .. 1]

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, 1 - color.rgb, _Amount), color.a);
}
```

Use **FX → + Preset ▾** to add an independent instance. The source is referenced by asset GUID;
keep its `.meta` when moving files. Source/include changes refresh loaded, unlocked instances.
Missing or invalid source retains the last applied shader and reports diagnostics. **Apply** retries/reloads;
**Embed Copy** disconnects the source and enables local code editing, retaining the original include base.
Included files remain external dependencies even after embedding.
Standalone Shader FX assets also appear in the catalog and are copied, not shared.
The legacy **+ Reference** workflow is unchanged. ShaderLab shaders are not auto-enrolled by this HLSL catalog.

The user library's `ShaderFX` subfolder is also scanned recursively when opening the catalog.
The same first-line marker is required. These external presets are embedded copies, not GUID-linked
sources. Custom includes are expanded when adding a user preset; Unity includes stay external.
Relative includes in a user preset must stay within the configured `ShaderFX` folder.
Project/catalog discovery still uses AssetDatabase and import notifications.

**Save HLSL Preset…** exports the current code with parameter declarations rewritten to current
values, retaining float bounds and existing categories; the file name supplies the last category segment.
Custom includes are expanded for portability (cyclic or oversized include trees are rejected).
Engine includes remain external. Files can be saved under user `ShaderFX` or project `Assets`.
Existing effects are not detached or switched to the saved file.

### Parameter declarations

Use `// @if _Mode == 1` or `// @if _Mode != 1` before one or more `// @param` lines, then close the block with `// @endif`, to show controls conditionally. Conditions accept only numeric values and `==`/`!=`; the referenced parameter must be an unconditional `float`, `bool` or `enum`. Nested blocks are not supported. This changes the editor UI only: hidden values remain stored and continue to affect the shader. Preset export preserves the condition blocks.

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

Use `// @header(Lighting)` before a `// @param` declaration to add a bold, non-collapsible heading above that control. Use `// @helpbox(Your hint text.)` to show an informational help box above the parameter instead. `// @formerlyserializedas(_OldName)` declares an old parameter name for the next declaration; when the new name is applied, compatible saved values and parameter identity migrate from the old name. Repeat the directive to support multiple previous names. This is useful when renaming a uniform: update the HLSL code to use the new name and leave the old name as migration metadata. All three directives are UI/serialization metadata, not uniforms; they are preserved when saving or exporting presets. Directives without a following parameter are ignored. HLSL brushes support these decorations and rename aliases too.

```hlsl
// @header(Lighting)
// @helpbox(Keep this value subtle to preserve the input colors.)
// @formerlyserializedas(_OldLightTint)
// @param color _LightColor = (1, 1, 1, 1)
// @param float _Intensity = 1 [0 .. ~4]
```

Use `@group` and `@endgroup` to visually contain several controls in a bordered block. An optional title appears in its header. Add `; _Parameter` to link a parameter declared unconditionally inside that group to the header. A bool is drawn as an unlabeled checkbox to the left of the title; it only edits the bool value and does not itself show or hide the group body. Use `@if` to control dependent rows and use the bool uniform in HLSL to enable or disable the effect. Supported compact values (enum, float, color, float2, float3, and float4) are drawn as their usual labeled field on the right. The linked control is omitted from the group body even when declared `hidden`; `hidden` does not prevent an explicitly linked, supported control from appearing in the header. Unlinked hidden controls remain invisible, while unsupported or multi-row controls stay in the body and do not alter the header. If all body rows are hidden by `@if`, the body collapses and the group is displayed as a header only. Groups cannot be nested; `@if` blocks may be used inside a group.

```hlsl
// @group(Tint; _EnableTint)
// @param bool _EnableTint = true
// @param color _Tint = (1, 1, 1, 1)
// @param float _TintStrength = 1 [0 .. 1]
// @endgroup
```

For example, `// @group(Quality; _Quality)` with `// @param hidden enum _Quality = 1 {Low: 0, High: 1}` puts the labeled dropdown in the header without a duplicate row. Use `// @group(Advanced)` for a titled group without a linked field, or plain `// @group` for a box without a header.

Use `label(...)` inline to override a parameter's generated UI label without changing its shader identifier: `// @param label(Tint Strength) float _Strength = 1`. The `hidden` and `label(...)` modifiers can appear in either order. Quote labels that contain parentheses; labels are preserved on preset export.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Offset = 0 [.. 10]
// @param float _Amount = 10
// @param float2 _Offset = (0, 0)
// @param float3 _Direction = (1, 0, 0)
// @param normal _Normal = (0, 0, 1)
// @param float4 _Channels = (0, 0, 0.5, 1)
// @param bool _IncludeAlpha = false
// @param color _Tint = (1, 1, 1, 1)
// @param texture2D _Input = self
// @param texture2D _Optional = none
// @param texture2D _Mask
// @param gradient _Ramp = #FF0000FF -> #0000FF
// @param transform2D _Area
// @param transform2D _PlacedArea = (0.5, 0.5, 0.75, 0.75, 30)
```

No semicolons on metadata lines. Initializers are optional. A `color` value can use four numeric components or `#RRGGBB` / `#RRGGBBAA` hex; six digits mean opaque (`A = 1`), and eight digits are RGBA. Without any explicit
default, numeric/vector/color values start at zero. Texture defaults to white and Transform2D
to the whole input. Transform2D accepts `(x, y, width, height, angleDegrees)` in normalized input units.
Texture2D accepts `= "guid:<32-digit asset GUID>:<local file ID>"`; the exporter uses this form for
assigned textures, including texture subassets. It requires persistent texture assets. A texture
reference absent from the current project falls back to white; the image is not embedded in HLSL.
Two distinct range boundaries produce a slider with numeric input; one boundary produces a limited
numeric field. Equal boundaries fix the number. Ranges apply only to floats.

Put `~` before a boundary value to make **that boundary soft** in FX or HLSL brush declarations:
```hlsl
// @param float _Strength = 1 [0 .. 2]
// @param float _Strength [0 .. ~2]
// @param float _Other = 1 [~0 .. 2]
// @param float _Both [~0 .. ~2]
```
The first control clamps edits to 0..2. The second allows numeric values above 2, but not below 0.
`[~0 .. 2]` allows values below 0, but not above 2; `[~0 .. ~2]` allows both directions.
Numeric entry and label dragging respect each boundary independently. Outside the slider range,
the thumb stays at the nearest endpoint while the field and shader retain the actual number.
A range with a soft boundary requires two finite values with `min < max`; `[~0 ..]`, `[.. ~2]`
and `[0 .. ~0]` are errors. The old prefix syntax `~[0 .. 2]` is not accepted.
Initializers remain optional. Repeated controls keep their own range behavior.
Preset export preserves both boundary flags and the current value.
This is editor metadata only: explicit `clamp`, `saturate` or other bounds in HLSL still apply.

### Curve parameters

`// @param curve _Profile = one` creates a flat curve with exactly two keys: (0,1) and (1,1).

Additional named defaults: `// @param curve _Profile = easeIn` uses `t²` (slow start), and `// @param curve _Profile = easeOut` uses `1-(1-t)²` (slow finish). These are quadratic curves from (0,0) to (1,1).

Declare `// @param curve _Profile` and call `_Profile_Sample(t)` for a scalar.
The default is linear from (0, 0) to (1, 1). The standard Unity curve field edits keys and tangents.
Named defaults are `// @param curve _Profile = linear` and
`// @param curve _Profile = easeInOut` (Unity's `AnimationCurve.EaseInOut(0, 0, 1, 1)`).
Both span (0,0) to (1,1); easeInOut has horizontal endpoint tangents. Without an initializer, the curve is linear.
Sampling clamps the input to 0..1; Y is not clamped. Outside the key span the nearest key value
is used, regardless of the curve's wrap modes. Empty curves evaluate to zero.

A cached linear RFloat 512×2 LUT is rebuilt only when curve data changes, without recompiling
the shader. GPU sampling is bilinear: very narrow details and step transitions are approximate
at this resolution. Copies own independent curves; documents preserve keys and tangents.

Save HLSL Preset writes the current curve as an optional default:

```hlsl
// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))
float4 ApplyFX(float2 uv, float4 color)
{
    return float4(color.rgb * _Profile_Sample(uv.x), color.a);
}
```

Each key tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
There are at most 256 keys, with strictly increasing finite times. Values are finite; tangents
also accept `inf` and `-inf` for steps. Weights are 0..1; weightedMode is 0 (none), 1 (in),
2 (out), or 3 (both). Only the sampled 0..1 interval is visible to the shader.
No range follows a curve declaration. Repeated declarations share one curve and the last explicit
default wins. Curve parameters are FX-only, not HLSL brush parameters.

### Gradient parameters

`gradient` creates an editable WhimTex gradient, initially opaque black to white (Classic mode).
Declare `// @param gradient _Ramp`, optionally with two endpoint colors such as
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint accepts `#RRGGBB` (opaque) or
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. The parameter remains editable after creation.
Use `_Ramp_Sample(t)` to obtain straight linear RGBA. The helper clamps `t` to 0..1; use `frac(t)` yourself for repetition.
Do not redeclare a sampler or reference internal `_WhimTex_` uniforms.
Colors, HDR, alpha, interpolation, smoothness and midpoints are edited in the gradient field.
Repeated declarations share a gradient value; copying an effect creates independent gradient data.

The effect lazily caches a 512×2 RGBAHalf LUT without mipmaps. Unchanged renders reuse it;
edits upload new pixels without recompiling the shader. Fixed uses Point filtering, other modes
use Bilinear. LUT sampling is an approximation: transitions finer than one LUT interval may be lost.
GPU caches are released with the material and recreated after reload. Edited keys are serialized
in the effect/document. The code default initializes new instances; applying code preserves the current edited value.
**Save HLSL Preset…** writes a compatible two-endpoint gradient as a default; gradients with extra stops or
non-default interpolation, smoothness or wrapping must be simplified before export.
Gradient parameters are FX-only, not HLSL brush parameters.

```hlsl
// @param gradient _Ramp // Map input brightness to colors.
float4 ApplyFX(float2 uv, float4 color)
{
    float4 mapped = _Ramp_Sample(dot(color.rgb, float3(0.2126, 0.7152, 0.0722)));
    return float4(mapped.rgb, mapped.a * color.a);
}
```

Append `// tooltip text` after a parameter declaration to show a hover tooltip on its generated
control. Each repeated declaration can have its own tooltip. The text is trimmed, otherwise literal
(including further `//`, punctuation and non-English text), and survives preset export.
```hlsl
// @param float _Strength = 0.65 [0 .. 1] // Controls how strongly the effect changes the image.
// @param enum _Strength { Subtle: 0.25, Full: 1 } // Choose a predefined strength.
```

`bool` displays a toggle, stored in `floatValue` and sent as a float uniform (`0` or `1`), without shader keywords or recompilation on value changes. Optional defaults are `true`/`false` or `1`/`0`; ranges are not supported.

```hlsl
// @param float _Strength = 0.63 [0 .. 1]
// @param enum _Strength { Low: 0.2, Medium: 0.5, High: 1.5 }
// @param enum _Mode = SoftLight { SoftLight: 0, HardLight: 1, CustomBlend: 0.5 }
```

For FX, `enum` is a float displayed as a dropdown. Every option needs an unquoted identifier and
an explicit finite numeric value; fractional values are allowed. Names and values must be unique.
Option names are editor-only labels (for example, `SoftLight` displays as **Soft Light**), not HLSL
constants. A default may be an option name or a number. An unmatched value displays **Custom**
without changing the number.

Repeated compatible declarations create linked controls in source order, but only one stored value
and one uniform. `float`, `bool` and `enum` share scalar storage; other types must match exactly.
The last declaration **with an initializer** supplies the default. Defaultless declarations do not
overwrite it. Ranges constrain edits through that control, not the shared value or its default.
Saving a preset writes the current value into one declaration and omits other initializers.
Enum and linked controls are FX features; HLSL brushes currently use their existing parameter UI.

`float2` and `float3` expose two and three raw components. `point` is a `float2` in normalized canvas UV (bottom-left `(0, 0)` to top-right `(1, 1)`), defaults to `(0.5, 0.5)`, and provides a draggable **Edit on Canvas** handle. `normal` generates a normalized `float3`, defaults to `(0, 0, 1)`, and uses that direction when given a zero vector. These types accept optional tuple defaults without ranges. Live API values are arrays with the corresponding component count.

For `normal`, **Edit on Canvas** shows a fixed-screen-radius handle at the canvas center. The center points toward the camera; the radius edge points along the canvas. Dragging outside the radius clamps the projected direction. Clicking the handle without dragging switches the Z hemisphere: **+** faces the camera, **−** faces away. X points right and Y up in canvas coordinates; rotating the preview rotates the handle without changing the value. Changing values does not recompile the shader.

Labels are derived from names: `_NoiseScale` becomes **Noise Scale**. `float4` is four raw components;
`color` is a color picker using the editor's HDR/Standard input setting and existing linear conversion.

These declarations also work in the inline code editor without a catalog header. They define the
parameter schema for the effect. Existing matching name/type
values and IDs survive Apply; removed declarations disappear. Renaming in place without changing
the type or layout retains identity. When simultaneously restructuring and renaming declarations,
unmatched parameters are treated as new rather than guessing their correspondence.
Do not separately declare generated uniforms/helpers. `_WhimTex_` is reserved for generated data.
The limit is 128 declarations per effect; the live API limits authoring to 32 parameters.

### Transform 2D

For `// @param transform2D _Area`, the wrapper generates:

```hlsl
float2 _Area_ToLocal(float2 inputUV);
float2 _Area_ToInput(float2 localUV);
```

Local `(0,0)` and `(1,1)` are opposite corners, `(0.5,0.5)` is the center. Coordinates outside the
frame remain valid. Position and size are normalized to the input dimensions, and rotation is in
degrees around the center, with the image aspect ratio taken into account. Nonzero negative sizes
mirror axes; UI edits keep magnitude at least `0.00001` to avoid a singular inverse.
Internal uniforms use `_WhimTex_<parameter>_<stable ID>_ToLocalRow0` and corresponding rows.
Changing values updates material uniforms, not shader source. The green canvas handles share the
layer transform's move/scale/rotate and free-transform gestures, but have no pivot. Ctrl/Cmd + corner
deforms a corner; Ctrl/Cmd + edge skews; Alt adds opposite-corner symmetry, and Ctrl/Cmd + Alt + Shift
moves a pair of corners for perspective. Only one FX frame is edited at once.
Position/Size/Rotation edits preserve existing skew and perspective. Reset Transform restores TRS.
Transforms store double-precision TRS or a projective 3×3 matrix; GPU uniforms use float rows 0–2
with a signed, guarded homogeneous divide. Saving a deformed HLSL preset writes its default as
`matrix(m00, m01, m02, m10, m11, m12, m20, m21, m22)`, mapping local UV to input UV.
The matrix must be invertible and its horizon must not cross the unit rectangle.
Old saved affine helpers are upgraded once into a transient shader when first rendered; pending code
and saved shader assets are left untouched.
The frame refers to the input coordinate space of that FX, not the inverse of later distortions.
Define any region mask/falloff in the effect itself; Transform2D does not automatically clip or mask.

```hlsl
// @whimtex-effect Transform/Place Image
// @param transform2D _Area

float4 ApplyFX(float2 uv, float4 color)
{
    float2 p = _Area_ToLocal(uv);
    if (any(p < 0) || any(p > 1)) return 0;
    return SampleInput(p);
}
```

## Shader Processor: process the lower stack instead of one layer

The Processor uses the same ApplyFX/SampleInput interface, with the lower composite as input.
Normal blending uses Opacity to mix original and processed RGBA; 100% replaces the input.
Other blends combine it with the result. Both ranges default to HDR; hiding the Processor bypasses it.

In Pass Through groups it also sees the external backdrop; isolated groups restrict it to their
children. Standalone previews and rasterization evaluate lower siblings against transparency.
Processors are clipping-chain boundaries, not clipping layers or bases. PSD bakes the composite
and retains the original layers in a hidden Source Layers folder.
