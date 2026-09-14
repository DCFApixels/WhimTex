---
title: "Shader authoring"
parent: "Technical reference"
nav_order: 7
lang: en
permalink: /reference/shader-fx/
search_exclude: true
---

# Shader authoring

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

`SampleInput(uv)` reads the layer after earlier modifiers. Return straight RGBA; opacity/blending
come later. Built-in inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`,
`_CanvasSize` (width, height, 1/width, 1/height) and `_PreviewScale`. Do not redeclare generated uniforms.

Standard `#include` supports project/package paths and relative paths. Relative paths start in
the document/FX asset folder, or Assets before the first save. After library edits, click Apply again;
after Save As to another folder, check relative paths. Libraries must suit the fragment-shader environment.

**+ Reference** links an external FX shared by its users; **Embed** makes an independent document-owned
copy. Save As and layer duplication copy embedded FX independently. FX run in order after Transform;
changing parameter values does not regenerate shaders.

## HLSL catalog

Add a `.hlsl` file anywhere in Assets or an installed package. Its **first physical line** must be
`// @whimtex-effect Category/Name`. UTF-8 BOM is allowed, but no preceding blank line, indentation,
license comment or other text. Unmarked HLSL files are not catalog effects. Discovery does not compile shaders.

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
Manual parameters are emitted as declarations too. Custom includes are expanded for portability
(cyclic or oversized include trees are rejected). Engine includes remain external. Files can be saved
under user `ShaderFX` or project `Assets`. Existing effects are not detached or switched to the saved file.

### Parameter declarations

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Offset = 0 [.. 10]
// @param float _Amount = 10
// @param float4 _Channels = (0, 0, 0.5, 1)
// @param color _Tint = (1, 1, 1, 1)
// @param texture2D _Mask
// @param transform2D _Area
// @param transform2D _PlacedArea = (0.5, 0.5, 0.75, 0.75, 30)
```

No semicolons on metadata lines. Float/vector/color declarations require a finite default; defaults
outside the declared range are errors. With no initializer, Texture defaults to white and Transform2D
to the whole input. Transform2D accepts `(x, y, width, height, angleDegrees)` in normalized input units.
Texture2D accepts `= "guid:<32-digit asset GUID>:<local file ID>"`; the exporter uses this form for
assigned textures, including texture subassets. It requires persistent texture assets. A texture
reference absent from the current project falls back to white; the image is not embedded in HLSL.
Two distinct range boundaries produce a slider with numeric input; one boundary produces a limited
numeric field. Equal boundaries fix the number. Ranges apply only to floats.
Labels are derived from names: `_NoiseScale` becomes **Noise Scale**. `float4` is four raw components;
`color` is a color picker using the editor's HDR/Standard input setting and existing linear conversion.

These declarations also work in the inline code editor without a catalog header. Once declarations
are used, they define the parameter schema instead of the manual list. Existing matching name/type
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
layer transform's move/scale/rotate and snapping behavior, but have no pivot. Only one FX frame is edited at once.
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
