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

Use **Add FX → Custom Shader FX** in a layer's settings, write `ApplyFX`, and click **Compile**.
Code and parameters belong to the document. For an effect on the already-composited
stack below a position, add a **Shader Processor** layer instead.

[FastNoiseLite](AI/README.md#built-in-noise-library) is automatically included in FX and
HLSL brush tips. Prefer its spatial-noise functions when suitable; custom algorithms remain valid.
No explicit include or copied library source is needed.

## Canvas viewport evaluation

Canvas View chooses evaluation per layer and FX chain. It does not reduce every layer
to a 512px preview when one effect is present. Native zoom with 100% Live Quality uses
source-pixel spacing; the visible region has a small overscan margin.

Verified pixel-local FX can read regional main, Self and Layer inputs through
`WhimTexTexture`. Ordinary Texture parameters retain their own complete images.
The earlier conservative analysis also permits effects using only the supplied color,
coordinates and ordinary parameters, including Hue & Saturation. Unconfirmed image
reads, derivatives or compiler dependencies keep the complete-input path.
This is a capability check, not a general HLSL optimizer or a neighborhood-radius inference.

For a complete-input stage, only its layer/dependency branch renders at the current
sampling step. The prefix through the last spatial FX is cropped before subsequent
local FX and normal composition. A local FX before a spatial FX still belongs to that
complete prefix. Each texture-layer dependency makes the same decision independently:
a local mask consumer does not make a spatial mask generator local. At native zoom that prefix is full resolution and
can consume substantial memory/time for large spatial effects. Bounded halo rendering
is not implemented. Groups, clipping and stack processors retain their input semantics.

A Shader Processor with an identity local transform and Clip/Unbounded source tiling
can reuse the already composed regional backdrop. Its FX use the same per-stage plan:
spatial stages or a non-identity processor transform require the complete backdrop,
then local suffix stages return to the viewport. Parent group transforms do not move
an identity processor's input a second time; FX-local coordinates still include the parent.
Pass-through external backdrops participate in rendering, dependency-cycle validation
and cache invalidation. Isolated or clipping-isolated groups stop that inheritance.

`ApplyFX` receives canvas UV, `_CanvasSize` remains the document size, and `_InputSize`
describes the complete input at `_RenderScale`, even when the supplied color comes from
a region. Layer-local coordinates and `_PixelScale` retain their meanings.
`SampleInput(PinnedUV)` can use a region; spatial `SampleInput(float2)` keeps complete input.

Inspector callbacks receive a separate complete input, reduced to at most 512 pixels
on its longest side. They may sample arbitrary UVs, including positions outside the
current canvas view. Canvas rendering invalidates their input but never supplies its
crop as the whole image. Refreshes are coalesced, input-only refreshes are limited to
10 Hz, and temporary input textures are not retained. Stack-processor backdrops use
the same reduced preview dimensions. Preview size does not change the canvas or export.
For Drawing layers with Clip/Unbounded tiling and a valid projected quad, an active
stroke is assembled directly into a separate inspector-sized cache (at most 4 MiB
ARGBFloat for 512²). It updates only newly touched tiles; transform, filter, mask and
compositing-setting changes invalidate it. This cache never replaces the canvas brush
buffer or consumes its pending damage, and is released when the stroke ends or is
cancelled. Without an active stroke, the inspector samples the Half source directly.
Repeat/Mirror/Clamp and unsupported projected quads retain source-resolution assembly
during a stroke, using a temporary buffer without evicting the canvas cache. Thus the
callback limit is not a universal limit on intermediate allocations. The
`shader-fx-regional-v1` and `shader-fx-workflow-8k` cases cover accuracy and cache isolation.

Region cache entries include the view transform and cannot replace full-image entries,
thumbnails or alpha-content checks. Active strokes and dependent branches bypass the
reusable FX cache. Generated shader resources refresh after script reload without
applying pending code edits or marking the document changed. Save/export and agent
renders remain complete-image operations; Post FX/Tiled/Crop view modes keep their
existing complete preview.

## Shader FX: a first snippet, parameters and reusable code

Parameters are authored only with `// @param` in HLSL. Saved manual uniforms are not
converted into declarations; there is no manual parameter authoring mode or migration.

Declare the parameter in the code, then click **Compile**:

```hlsl
// @param float _Amount = 1 [0 .. 1]

float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, 1.0 - color.rgb, saturate(_Amount)), color.a);
}
```

Parameter declarations generate uniforms and editor controls automatically; the supported types are described below.
The FX block's **Reset** menu action copies defaults from its current `@param` declarations into existing values, including hidden controls, textures, gradients, curves and transforms. Parameter IDs, controls, code, catalog linkage and enabled state are retained; no shader compilation is needed. It records one FX Undo and uses the normal value-change notification. Reset is disabled for locked FX, missing declarations or a draft whose declaration schema no longer matches the current parameters. It affects only this document-owned FX instance.
Code and declarations stay drafts until **Compile**, including a compilation request from saving the working file in the bundled VS Code integration. A failed compilation skips the FX, including any previously compiled version, until successful recompilation. Code, values and enabled state remain editable. Yellow indicators in Layers, the FX section and the effect header expose errors and warnings even when collapsed. Warnings do not disable a successfully compiled FX. JSON loading/saving retains broken effects and reports diagnostics without aborting the document; TIFF save validation is unchanged.

FX compilation, declaration validation, source/include failures and catalog reloads share one
diagnostic list with `Error`, `Warning` and `Info` severity, message and optional source location.
Normal compilation and transient agent compilation use the same collector. Diagnostics, UI indicators,
agent responses and WhimTex Console entries use those records; the API does not reclassify display text.
Console uses Error for errors and Warning for warnings, deduplicated by source path and formatted
diagnostic until scripts reload. Suppression affects only Console, never UI/API diagnostics.
Fixing all issues clears the indicators; a successful compilation with warnings keeps them visible.

`LayerToLocal(uv)` converts canvas UV to local layer UV, including parent transforms and perspective. Use it for procedural shapes that should follow the layer. It does not clamp or wrap UV; `SampleInput` still expects canvas UV.

`SampleInput(uv)` reads the layer after earlier FX. Return straight RGBA; opacity/blending
come later. Built-in inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`,
`_CanvasSize` (width, height, 1/width, 1/height) and `_RenderScale`. Do not redeclare generated uniforms.

`SampleInput(uv, tiling)` selects spatial input addressing: `0` Clamp, `1` Repeat, `2` Mirror,
`3` Clip (transparent outside UV 0–1). Repeat interpolates across both image seams with
the input's Point/Bilinear filter; intermediate FX textures have no mipmaps, so Trilinear
uses the same base-level interpolation. This helper does not change the source texture.
Declare an ordinary enum to expose the choice:

```hlsl
// @param enum _InputTiling = Clamp {Clamp: 0, Repeat: 1, Mirror: 2, Clip: 3}
```

Distortion presets use `_Tiling`; UV Transform uses `_InputTiling`. Previous parameter
names and old linked-preset layouts are not migrated. Detached source is not rewritten.
The original one-argument helper is unchanged. `Unbounded` is not a raster addressing mode.
Unity time inputs such as `_Time`, `_SinTime`, `_CosTime`, `_TimeParameters` and
`unity_DeltaTime` are allowed: their use does not block compilation. WhimTex does not
control their updates, so results may differ between Canvas, thumbnails and export.
Compilation adds a warning to Diagnostics and disables reusable result caching for the FX.
Agent compilation returns the same warning in `diagnostics` and `warnings`, not `errors`.
The warning uses the same diagnostic and Console mechanism as other FX warnings; there is no
time-specific reporting path. Repeated compilation, agent preflight and rendering do not repeat it;
Diagnostics and API warnings remain available every time. Use an explicit parameter for
predictable time-dependent behavior. This is the current warning-only policy, not a legacy exception.

Standard `#include` supports project/package paths and relative paths. Relative paths start in
the document or linked HLSL source folder, or Assets before the first save. After library edits, click Compile again;
after Save As to another folder, check relative paths. Libraries must suit the fragment-shader environment.

### Pixel-local entry points and texture reads

The Pixel Local row above the code text area, inside Code, reflects the compiled shader, not pending edits, with
Yes/No/Not compiled/Unavailable states. Compile compiles and uses the FX; it does not bake pixels.
Apply and Apply All in the FX stack remain baking commands. Shader Inputs is a sectioned,
highlighted reference for entry points, texture reads, coordinates, parameters and libraries.
Enumerated declarations and API entries use bullets with aligned wrapped descriptions.
Double-click a highlighted code fragment to insert it at the code field's last caret,
replacing any saved selection; this includes `SampleInput` and `SampleTexture` snippets.
Editable fragments show a stronger background and a hand cursor on hover, without tooltips.
Double-click a full `float4 ApplyFX(...)` declaration to replace the single definition's
header, keeping its body and existing argument names. Without a definition it inserts
the declaration at the caret; multiple definitions are left unchanged. The field keeps
focus and selection history, and each action supports Undo/Redo. These are draft edits,
not compilation: click Compile when ready. Linked preset code remains read-only until
Embed Copy; Shader Inputs explains this when the source is linked. Changing a signature
may require adjusting its body before compiling.

The code field uses four-space soft tabs. Tab inserts spaces to the next four-column stop
at a caret, or indents every selected line by one level. Shift+Tab removes one leading tab
or up to four leading spaces from the current/selected lines. A selection ending at the
start of the next line does not include that line. Focus stays in the field; each action
is one Undo/Redo step. New ApplyFX bodies use the same four-space indentation. Existing
source is not reformatted automatically, and line endings are preserved.
Long code lines do not wrap visually; a native horizontal scrollbar appears when needed.
Vertical scrolling remains available, and scrolling does not insert line breaks into source.
On Unity 6000.7+, a left-hand line-number gutter follows native text coordinates vertically
and stays fixed during horizontal scrolling. It includes empty/trailing lines, counts CRLF
once, updates on silent source changes and Undo/Redo, and draws only visible numbers.
The gutter is non-focusable and does not intercept text editing; `ui.code.lines` profiles
its drawing under UI & Input.
While the code field has focus, the caret's logical line has a subtle background and a
higher-contrast line number. The background sits behind native text selection, spans the
visible width and follows vertical scrolling. Caret/selection/focus events invalidate only
the decorations; moving the caret does not retokenize or rewrite the source.
Enter carries the current indentation to the new line and adds one level after an opening
brace. Between an existing brace pair it creates an indented interior line and aligns the
closing brace. Typing `}` after only leading whitespace aligns it with its matching opener,
or removes one indentation level when no opener is available. Inline closing braces do not
reindent their line. Braces inside comments and quoted strings are ignored; this lightweight
editing rule does not evaluate preprocessor branches. Enter/brace edits replace selections
and remain single Undo/Redo steps. A physical Enter and its separate translated newline
character produce one newline, while held-key repeats still insert subsequent lines.

On Unity 6000.7+, the inline FX field highlights HLSL keywords/types, numeric and quoted
literals, comments, preprocessor directives, function calls, `_` parameter names and
WhimTex `// @...` metadata. This is lexical coloring, not validation or symbol resolution;
Compile remains authoritative. Older supported Editors retain the plain code field.
`TextElement.PostProcessTextVertices` changes only glyph RGB, preserving positions, UVs,
alpha and native selected-text tint. No rich-text tags, overlay, generator switch or
source rewrite is used. A reusable UTF-16 token buffer is rebuilt only when contents
change, including Undo/Redo and external draft updates; caret-only repaints reuse it.
Glyph coloring is limited to the native field's visible vertical viewport plus a
conservative font-size margin. Scrolling/resizing invalidates glyph coloring without
rewriting the source or retokenizing unchanged contents; newly exposed lines are recolored.
Tokenization still scans the source for correct multiline-comment state, and enumeration
walks the prefix when scrolled down. This is not a virtualized full IDE.
WhimTex Profiler's **UI & Input** group exposes `ui.code.change` (native draft change
handler), `ui.code.edit` (atomic editor commands), `ui.code.tokenize` (changed contents)
and `ui.code.color` (glyph callback, inclusive of tokenization). These are main-thread
wall-time stages, not GPU timings or the complete deferred UI Toolkit layout/render cost.

An effect defines exactly one of these `float4` entry points. Argument names are arbitrary;
`in` and `const` qualifiers are accepted, but `out`, `inout`, extra arguments and overloaded
`ApplyFX` definitions are not supported.

```hlsl
float4 ApplyFX(float4 color)                        // No coordinate parameter.
float4 ApplyFX(PinnedUV uv, float4 color)            // Current pixel coordinate.
```

Locality is inferred from reachable image reads, including helper functions and expanded
includes. The entry signature alone does not determine it. The old `ApplyFX(float2, float4)`
signature is rejected with a diagnostic; document-owned code is not silently rewritten.

`PinnedUV` is immutable. Copies, parentheses and helper parameters/returns of the same
type retain the current coordinate. Arithmetic, swizzles, indexing, numeric constructors,
implicit `float2 p = uv` and explicit `(float2)uv` conversion produce ordinary numeric
values. Numeric values cannot become `PinnedUV`, even when they happen to equal the current
coordinate. Mutation, component writes, uninitialized pinned locals, construction, arrays,
struct fields, aliases and `out`/`inout` pinned arguments are rejected.

The authoring pass inserts portable HLSL conversions before Unity compiles the shader;
this does not require HLSL operator-overloading support or add a GPU pass.

`SampleInput(uv)` and `SampleTexture(_Mask, uv)` with pinned UV are local.
`SampleInput(uv + offset)`, `SampleInput((float2)uv)` and reads inside helpers accepting
`float2` are spatial. Even `uv + 0` loses the pin. UV arithmetic used only to compute
color, procedural noise or a gradient lookup does not make an effect spatial.
Numeric overload resolution is conservative: possible helper overloads all participate.
Macros invoking helpers/sampling or ambiguous preprocessor coordinate types remain unverified.

**`SampleInput` and `SampleTexture` are WhimTex's primary texture-reading API.**
`_MainTex` is the incoming `WhimTexTexture`, not a bypass by itself. `SampleTexture(_MainTex, uv)`
is equivalent to `SampleInput(uv)` and retains the pixel-local contract when `uv` is `PinnedUV`.
`_MainTex_TexelSize` is size metadata, not an image read. Direct texture sampling operations
remain bypass paths, including when mixed with approved helper calls.
`SampleInput(uv)` reads the current FX input, including previous FX. `SampleTexture(_Mask, uv)`
reads a declared `texture2D` parameter at the corresponding normalized coordinate, including
Texture, Layer, Self and None sources. Different resolutions do not change coordinate meaning.
Both entry forms may use numeric sampling. Explicit `SampleInput(uv, tiling)` addressing
is spatial even when UV is pinned. Preview callbacks retain their `float2` interface and
complete reduced inputs; their reads do not change the main function's typed sampling status.
Direct low-level bypass detection remains conservative across the complete source.
Its warning describes a full-input fallback, not a failed compilation or an invalid
`PinnedUV` entry point.

For explicit mip levels, use `SampleInputLod(float2 uv, float lod)` or
`SampleTextureLod(WhimTexTexture source, float2 uv, float lod)`. Both return `float4`
straight RGBA, preserve HDR and apply the input's own UV mapping. `PinnedUV` arguments
convert to numeric coordinates automatically. These reads are **spatial**, even with
unchanged `PinnedUV` and LOD 0, because a mip level can cover neighboring pixels.
They produce a verified `spatial` result with the `spatial_sample` reason, rather than
a bypass warning, unless the source contains another unverified operation.

```hlsl
float4 ApplyFX(PinnedUV uv, float4 color)
{
    return SampleInputLod(uv, 0);
}
// A declared texture is read with SampleTextureLod(_Mask, uv, 0).
```

LOD 0 means the base level of the **supplied texture**, including reduced inspector
inputs; it does not request the document's native resolution. Available mip levels,
fractional-LOD filtering and wrap behavior come from that texture and its sampler.
The helpers do not generate mipmaps. They avoid automatic-LOD derivatives and can
be used in data-dependent loops. Edge Outline uses `SampleInputLod(..., 0)` for its
neighbor search and remains spatial without a bypass warning. Inspector callbacks
may also use these functions with their complete reduced inputs.

`_MainTex` and declared `texture2D` parameters are read-only **`WhimTexTexture`** values.
Each value carries its sampler, its own mapping from whole-image UV to the stored region,
and the complete input's texel size. `SampleTexture` applies that mapping automatically;
UV 0–1 still addresses the whole input, even when its physical texture stores only a crop.
Different inputs may have different regions and resolutions. A regular Texture source
uses the full 0–1 region.

Use `WhimTexTexture`, rather than `sampler2D`, for helper and preview arguments that receive
these inputs. Copies, helper parameters and return values retain the mapping:

```hlsl
float4 ReadMask(WhimTexTexture source, PinnedUV uv)
{
    return SampleTexture(source, uv);
}
```

`source.texelSize` and `_Name_TexelSize` are `(1/W, 1/H, W, H)` for the **complete input
at its render scale**, not the physical crop dimensions. `_InputSize` remains
`(W, H, 1/W, 1/H)` for the main input; `_CanvasSize` describes the document.
Do not construct, cast, mutate or pass a texture view as `out`/`inout`. Accessing its
internal `.pixels` sampler or `.uvMap` mapping prevents pixel-local confirmation.
Direct `tex2D`/`tex2Dlod`/`tex2Dbias`/`tex2Dgrad`/`tex2Dproj` calls accept a texture view
and apply its mapping, but remain bypass paths for classification.

The renderer supplies regional inputs to verified pixel-local stages and complete
inputs to spatial stages. Inspector callbacks have their own complete reduced inputs.
A cropped input must cover every sampled coordinate,
including filtering support; the view does not supply missing neighboring pixels.

```hlsl
// @param texture2D _Mask = self
float4 ApplyFX(PinnedUV uv, float4 color)
{
    color.a *= SampleTexture(_Mask, uv).r;
    return color;
}
```

Direct `tex2D`, resource `.Sample`/`.Load` and similar low-level reads are bypass paths,
not the primary API. Detected bypass reads, derivatives and unresolved compiler dependencies
leave the shader usable but prevent confirmation of pixel locality and produce a shared warning.
This is an authoring contract, not a sandbox against deliberately hidden HLSL bypasses.
Generated curve/gradient lookups and the reserved hue lookup are independent of image-neighbor reads.
The transformable Mask preset remains spatial because it samples displaced mask coordinates.

Built-in Brightness Contrast, Color Balance, Color Filter, Gain, Gradient Map, Hue & Saturation,
Levels, Negative, Normalize, Step and Threshold use the color-only entry point. Posterize uses
`PinnedUV` for its procedural dither position, and Normal Lighting uses it with `SampleTexture`
for the normal input. Their calculations and output are unchanged. New inline FX start with
the color-only tint template; existing document-owned code is not rewritten.

`ShaderFX.IsPixelLocal` is a public read-only bool describing the applied, successfully compiled
shader, not pending code edits. It is false before compilation, after failed compilation, for spatial reads
and for unconfirmed declarations. It does not depend on Active. `Layer.AreActiveFxPixelLocal()`
checks this layer's active non-null FX; an empty chain returns true, disabled FX are ignored,
and an active unavailable/unconfirmed FX returns false. Child layers and source generation are not checked.
The value is derived from the compiled source contract and is reconstructed with the shader;
it is not an editable user setting or a serialized promise independent of the source.

### Document-owned HLSL effects

`Layer.fx` is a `List<ShaderFX>`. Every FX instance is owned by the document, with independent parameter
values and enabled state. Source modes are a linked, marked project/package `.hlsl` preset, an
embedded snapshot of a marked user-library `.hlsl` preset, or inline HLSL. A shared HLSL source
does not share mutable FX values. Save As, layer duplication and FX copying preserve independent
instances. FX run in order after Transform; changing values does not regenerate shaders.

There is no **+ Reference**, external Material/Shader/ShaderFX `.asset` ObjectField or shared-asset
editing. The **Embed Copy** menu action on an external ShaderFX asset card is removed; the
**Code → Embed Copy** button for a linked `.hlsl` preset remains. It detaches the HLSL source
and makes the code editable inside the document, preserving values and the original include base.
ShaderFX `.asset` catalog entries and its `CreateAssetMenu` workflow are removed.
External Material and Shader objects cannot serve as FX entries; the separate Material
render branch is removed. Unity Shader compilation and internal Materials remain execution resources
for HLSL effects, not a user-facing FX source mode. Texture asset parameters remain supported.

JSON and TIFF containing old external FX references are rejected, including unresolved references,
with `External FX references are no longer supported. Use a marked .hlsl preset or inline HLSL code.`
They are not skipped as missing assets, migrated, converted to
inline HLSL or rewritten. Keep originals and use a matching older checkout when needed. This does
not prohibit current HLSL source links or external texture parameters.

### CPU preparation and cache identity

Parameter preparation caches the draft-to-applied matching plan and shader property IDs, not mutable
values. On use it checks declaration references, order, names and types; incompatible draft parameters
still fall back to their applied values, and duplicate draft declarations retain first-match priority.
Dimensions, pixel scale and all current values are uploaded for each render context.

Render fingerprints read numeric data directly, including double-precision transforms, gradient keys,
midpoints, sampling settings and curve tangents/weights/wraps. Immutable strings reuse their content
hash until replaced. Direct edits and Undo/Redo do not require an explicit revision notification;
texture update counts and layer dependencies remain part of the enclosing render-cache stamp.

Texture inputs reuse the matching plan and bounded, reentrant binding leases. Repeated layer requests
are validated once within that lease only; graph validity is not retained across renders. Temporary
textures are unbound before release, Self is borrowed, and None retains the render-sized clear input
(including its texel-size semantics). LUT formats, sampling, shader formulas and FX pass order are unchanged.

The bounded `fx-preparation-before` / `fx-preparation-after` diagnostics report warmed CPU-stage
median/p95 timings and separate main-thread allocation counts, with retained pre-change float pixels.
They are not cold shader compilation, GPU timing or presented FPS. The timing harness uses reflection
at internal API boundaries; those calls contribute a common overhead/allocation to preparation and
texture-input samples. Fingerprint uses a direct delegate. Tests preserve the existing profiler capture;
allocation counting uses the same public GC.Alloc recorder semantics as WhimTex Profiler.

## Distortion header controls

All built-in Distortion presets expose a scalar through `@control`:

| Preset | Header parameter | Behavior |
| --- | --- | --- |
| Spherize | `_Strength` | Existing signed bulge/pinch strength; Sphere clipping remains at zero. |
| Twirl | `_Angle` | Existing signed rotation in degrees; zero is identity. |
| Radial Shear | `_Strength` | Existing signed shear strength; Offset remains at zero. |
| Polar Coordinates | `_Amount` | Hidden float, default 1, hard range 0–1; sample `lerp(uv, mappedUV, _Amount)`. |
| Displacement Map | `_Amount` | Hidden float, default 1, range `[0 .. ~2]`; multiply the final strength mask before displacement and parallax tracing. |
| Height to Distortion | `_Strength` | Raw height-gradient multiplier, default 8; zero generates a neutral field rather than returning the input. |

New Amount parameters return the original RGBA at zero and preserve the previous full
mapping at their default 1. They change coordinates, not output opacity. Displacement Map
keeps `_Mix` as an independent output crossfade. Existing signed controls and their defaults
are unchanged. Header bindings and values survive document and HLSL preset export.

Polar Coordinates uses two `transform2D` frames: `_Input` selects the source and
`_Output` places the output in both modes. Each defaults to position `(0.5, 0.5)`,
size `(1, 1)`, rotation `0`, and uses the shared Edit on Canvas/reset UI.
The sampling map is `_Input_ToInput(P_or_Q(_Output_ToLocal(uv)))`: To Polar converts
the output circle into source-strip coordinates; From Polar converts the output
strip into source-circle coordinates. Reversing Mode and swapping the frames
inverts the coordinate map away from the polar singularity and angular seam.
One radial unit is half a circle-frame axis: Output in To Polar, Input in From Polar.
Out-of-frame coordinates follow Tiling, which defaults to Clamp. UV Transform's Input Tiling defaults
to Clip; the other built-in distortion presets default to Clamp. The field follows the
effect's parameters, outside Transform 2D foldouts, and is independent of layer Tiling/map wrapping.

Linked preset refresh matches parameters by their current names and types. Old Area,
Source Center and one-frame layouts are not converted to Input/Output. Detached presets
keep their embedded source.

## Chroma Key preset

`Color/Chroma Key` is a color-only, pixel-local HLSL preset. `_KeyColor` is uploaded by the
ordinary color-parameter path; both it and input RGB are display-encoded with `SpriteEncode`
and clamped only for matching. `_Distance=0` compares Cb/Cr, where
`Y=dot(rgb,(0.2126,0.7152,0.0722))`, `Cb=(B-Y)/1.8556`, `Cr=(R-Y)/1.5748`;
the Euclidean distance is divided by sqrt(2). `_Distance=1` uses RGB distance / sqrt(3).
Use RGB to distinguish neutral background colors by brightness.

`keep=smoothstep(_Tolerance,_Tolerance+_Softness,distance)` multiplies original alpha.
Zero softness explicitly removes `distance <= tolerance`; tolerance 1 removes everything.
Default tolerance/softness are 0.1/0.05. `_Despill=0` preserves linear RGB; nonzero despill
subtracts positive chroma projected onto the zero-luma key direction, weighted by similarity
up to `_Tolerance+_Softness+_SpillRange`. Subtraction is limited before any RGB becomes
negative, without clamping HDR. Neutral keys, negative input RGB and fully transparent
input skip despill. Despill never changes matte.

`_Output=0` returns Result; 1 returns opaque grayscale of final alpha. `_Opacity=1` mixes
the entire selected RGBA output with the input; zero returns input immediately. This is
single-color keying, not foreground recovery or spatial edge filtering. No renderer or
layer-specific support is involved.

## Domain Warp preset

`Distortion/Domain Warp` is an ordinary spatial HLSL preset using the bundled FastNoiseLite
2D `fnlDomainWarp2D`. It generates data independently of input color and alpha. Coordinates
are `uv + _Offset` in canvas UV, with `_Scale` as frequency (default 4), `_Seed=1337` and
`_Strength=1` as domain-warp amplitude. There is no aspect correction or periodic wrapping.
`_WarpType` is OpenSimplex2 (0), OpenSimplex2Reduced (1) or BasicGrid (2).
`_Fractal` is None (0), Progressive (4) or Independent (5); active fractals use rounded
`_Octaves` (1–8, default 3), `_Lacunarity=2` and `_Gain=0.5`. None uses one octave regardless
of hidden values. Native fractal amplitude weighting is retained; the output vector is
never normalized to unit length. In Progressive, changing signed amplitude can also
change the positions sampled by later octaves.

The field is `(warpedPosition-originalPosition)*axisSigns`. `_FlipX` and `_FlipY` only change
the final signs. `_Output=0` (PackedRG, default) stores `0.5+0.5*field`; 1 (SignedRG) stores
`field`. B=0, A=1; zero Strength gives exact neutral values. Neither output clamps values.
Use HDR Color Range for signed/out-of-range data and linear EXR for external storage.
Displacement Map VectorRG uses Neutral 0.5 for Packed, or 0 with half the X/Y strengths for
Signed, because the existing consumer multiplies `(RG-Neutral)` by two. No changes to the
Noise layer, Displacement Map or renderer are required. Ordinary FX catalog IDs, parameters,
JSON and agent operations apply to both presets.

## Height to Distortion preset

`Distortion/Height to Distortion` generates a field; it does not distort the source image.
`_HeightMap` defaults to Self and accepts the ordinary texture/layer inputs. `_Channel`
selects luminance (0), R (1), G (2), B (3) or Alpha (4). Height is read as linear data,
without height clamping, color conversion or multiplication by source alpha.
`_Filter` selects Scharr (0, default), Sobel (1) or CentralDifference (2).
`_Radius` is a pixel-unit sampling distance (default 1, minimum 0.5 before Pixel Scale);
the stencil is divided by its coefficient sum and effective radius to compute height
change per canvas pixel, not normalized to unit vector length. The positive gradient
points toward increasing height in canvas +X/+Y. `_Strength` scales it, including negative
values; `_FlipX`/`_FlipY` independently reverse components.

`_Edges` is Clamp (0), Repeat (1) or Mirror (2). Repeat interpolates across the map seam,
including when the assigned texture uses Clamp. Stencil reads intentionally use explicit
LOD 0 through `SampleTextureLod(_HeightMap, uv, 0)`: wrapped texel-centre coordinates
must not influence automatic mip/anisotropic footprint selection. This is a verified
spatial FX, not pixel-local; it does not produce a bypass-read warning.

For `g = gradient(height) * _Strength * axisSigns`, `_Output` has two values:

| Output | RG | Displacement Map / VectorRG |
| --- | --- | --- |
| PackedRG (0, default) | `0.5 + 0.5 * g` | Neutral 0.5; X/Y strengths S. |
| SignedRG (1) | `g` | Neutral 0; X/Y strengths S/2 for the same offset. |

B is 0 and alpha is 1. Neither output clamps RG, so PackedRG may also exceed 0–1.
Keep the map layer's Color Range at HDR for signed/out-of-range values. Flat heights and
zero Strength output exact neutral values. The factor of two above comes from the existing
consumer: `(RG - Neutral) * 2 * float2(StrengthX, StrengthY) * Amount / CanvasSize`.
The generator does not change that consumer's behavior. Assign the map layer as the
Displacement Map input; it can remain hidden. The source and map can have different sizes.
Both modes are linear numeric data. For external signed/HDR storage use linear EXR;
PNG/TGA/PSD retain their normal color-encoding/clamping rules, not a new raw-data export mode.

## Edge Outline preset

The metadata groups controls into Detection, Contour and Output, linking `_Detection`,
`_Shape` and `_Output` to their headers. Labels show canvas-pixel units for thickness/softness
and degrees for the hue threshold; UI grouping does not change parameter names or values.

`Stylization/Edge Outline` has `_Method = 0` (Boundary, default) and `_Method = 1` (Scharr).
`@control(_Opacity)` exposes a hidden float parameter in the FX header, default 1 with hard
bounds 0–1. The shader blends the complete selected output with the original input RGBA;
zero returns the input unchanged, also in Outline Only. Tint alpha remains independent.
Boundary compares adjacent input texel centers in linear RGB.
`_Detection = 0` (Color) uses the maximum absolute RGB channel difference; `_Detection = 1`
(Luminance) uses the absolute difference of luminance with weights `(0.2126, 0.7152, 0.0722)`.
A horizontal/vertical pair seeds a boundary when the difference reaches `_Threshold`
(floored to 0.00001) and both pixels have alpha above 0.00001. Invisible RGB and alpha-only
silhouettes do not seed boundaries.

`_Detection = 2` (Hue) uses HSV hue in display-encoded, nonnegative RGB, matching the Hue & Saturation
preset and retaining HDR values. The shortest circular hue difference (0–180 degrees)
must reach `_HueThreshold` (default 15, floored to 0.001 degrees). Both endpoints must
reach `_MinSaturation` (default 0.1, range 0–1); undefined hues are always excluded,
including display-space chroma/value at or below 0.00001 and saturation at or below
0.00001. `_Threshold` is shown and used only for Color/Luminance; `_HueThreshold` and
`_MinSaturation` only for Hue. Switching detection keeps the hidden values stored.

Scharr uses the true 3×3 derivative kernels with weights 3/10/3, divided by 16 so a unit
horizontal/vertical step has magnitude one. Color takes the largest per-channel gradient
magnitude; Luminance differentiates the weighted linear signal. Hue unwraps each valid
neighbor around the center via the shortest signed angle, normalized by 180 degrees;
its threshold is `_HueThreshold / 180`. Undefined/low-saturation centers are excluded,
and invalid neighbors contribute the center signal, as do invisible neighbors in every
detection mode. At image edges, samples clamp to the nearest input texel center.
Below the selected threshold the response is zero; otherwise it is
`saturate(gradient * max(_Strength, 0))`. Strength is visible/used only in Scharr.

The preset expands binary boundary segments or weighted Scharr pixel seeds using
Euclidean (`Round`), Chebyshev (`Square`) or Manhattan (`Diamond`) distance in a dense
local neighborhood, taking maximum coverage rather than accumulating overlapping seeds.
Scharr's expansion radius subtracts half the largest input-texel pitch to account for its
base derivative footprint. Its tap spacing remains one input texel, independent of Thickness.
Broad transitions still produce wider support; this is not edge thinning. `_Thickness` is total
centered width in canvas pixels (0–32); `_Softness` is edge transition width (0–8), with at
least one input texel of antialiasing. Thickness does not change detection sensitivity.
Reduced-resolution renders detect boundaries in their own raster and cannot retain details
smaller than that raster. Neighborhood work grows with thickness/softness; this is a bounded
single-pass FX, not a cached distance-field pass. Scharr costs more because it evaluates
the 3×3 derivative at candidate seeds during expansion; eight neighbor reads describe
one detector evaluation, not the complete thick-contour pass.

Before the overall `_Opacity` blend, `_Output = 0` overlays `_OutlineColor.rgb`, weighted
by contour coverage and tint alpha, while preserving input alpha. `_Output = 1` emits
the tint with input alpha times that weight.
Zero Thickness or zero tint alpha removes the contour. Sampling stops at the canvas edges:
it does not add a frame or wrap to the opposite edge. Earlier FX, group input and Shader
Processor input follow the ordinary effect input contract.

<a id="hsv-preset"></a>

## Hue & Saturation preset

The inspector has an untitled tabs block: Master and Range 1–4 each have their own page.
Fields sit directly inside each page without nested groups. Each Range has an Enabled checkbox before its tab label.
Source Saturation (HSV) and Output remain shared below the tabs; changing pages does not enable or disable ranges.

`Color/Hue & Saturation` replaces the HSV preset. It opens on Master with all four ranges disabled; Master exposes
`_MasterHue`, `_MasterSaturation` and `_MasterLightness` for the whole image, including gray pixels.
Four ranges each expose Enabled, float2 Hue Range endpoints and Feather in degrees, signed Hue shift and
Saturation and Lightness. Neutral values are Hue 0, Saturation 1 and Lightness 0.
All ranges start disabled. Disabled ranges keep their controls visible
but gray and unavailable for editing, retaining their values. The four range bodies use `@enableif`;
their Enabled checkbox fields remain editable. Hue uses one 0°…360° turn and cyclic `[0 % 360]` endpoints: `(330, 30)` crosses red.
The forward cyclic endpoint distance is the full-strength span, and Feather adds a smooth transition on each side,
limited at the opposite hue. `(0, 360)` selects all hues; coincident endpoints
with zero Feather select nothing.

Every range measures the original display-encoded hue, before Master or any range correction.
Range settings describe corrections relative to the source, not additional changes after Master.
For each Hue/Saturation/Lightness correction offset, let `wi` be source hue weights, `g` the shared
`_SaturationRange` cutoff/full-strength gate, `Z = max(1, sum(wi))`, and `q = g * saturate(sum(wi))`.
The effective offset is `(1 - q) * masterOffset + g * sum(wi * rangeOffset_i) / Z`;
Hue offsets are degrees, Saturation offsets are `multiplier - 1`, and Lightness offsets are signed percentages.
Master never participates
in the overlap average. A neutral full-weight range therefore preserves original colors even
when Master desaturates everything. Feather/gating blend back toward Master. Two enabled ranges
still average signed offsets; a neutral overlapping range can weaken the other range.
Gray, black and near-undefined hues never select a range, but still receive Master correction.
Transparent Result pixels retain their hidden RGB exactly. An effectively neutral Result returns
the exact incoming RGBA; active correction supports HDR and preserves alpha.

Correction order is Hue shift, Saturation, then Lightness, in display-encoded nonnegative RGB.
Hue retains the previous HSV rotation. Saturation instead scales distance from weighted gray:
`Y = dot(rgb, (0.2126, 0.7152, 0.0722)); rgb = max(Y + (rgb - Y) * max(S, 0), 0)`.
Saturation has default 1 and a soft upper slider limit of 4; it is not the HSV S multiplier.
The weighted-gray operation is not a perceptually uniform or strictly luminance-preserving color model.
Lightness has hard limits -100…100 and default 0. With `L = Lightness / 100`, negative values
use `rgb *= 1 + L`; positive values use `lerp(rgb, 1, L)`. Thus -100 reaches black and +100
reaches reference white, including HDR input; positive Lightness can reduce highlights above white.
Decode back to linear and cap corrected RGB at 65504. Neutral/transparent bypasses remain exact.
The shared Source Saturation (HSV) control still measures the unmodified source HSV S for selection only.

Output offers Result, All Ranges Mask and one mask per range. Mask RGB is coverage times source
alpha; Master never changes these source selection masks. At full Opacity mask alpha is 1.
The hidden `_Opacity` header control blends the selected
output with the original RGBA; zero preserves the input even in mask modes. Settings use
ordinary uniforms and never trigger compilation variants.
With all Enabled controls off, a uniform branch bypasses hue-range weights, saturation selection
and overlap normalization, using only the same Hue/Saturation/Lightness correction for Master. Neutral and transparent
Result pixels retain the input exactly; disabled-range mask outputs stay black with opaque mask alpha
at full Opacity. The renamed `HueSaturation.hlsl` retains its source asset GUID; no second HSV preset is provided.
The former `_MasterValue` / `_RangeNValue` controls are replaced by `_MasterLightness` /
`_RangeNLightness`, not reinterpreted as percentages or migrated through aliases.
Linked presets receive the requested new Saturation behavior when refreshed; independent embedded code is unchanged.

## Texture inputs and lighting

### Height-based lighting

Normal Lighting and Bevel Emboss share `SurfaceLighting.cginc`. `_BaseColor.a` blends transparent lighting (0) into a filled surface (1), using premultiplied interpolation and returning straight alpha. Surface RGB uses `_BaseColor.rgb`, Lambert lighting, `_LightColor`, `_ShadowColor`, `_Intensity` and `_Ambient`; its coverage is the host input alpha, without another Base Color alpha multiplication. Transparent lighting subtracts flat-normal lighting and ignores host alpha/Base Color RGB/Ambient. `_Output` selects Both (0), HighlightOnly (1), ShadowOnly (2) for that component only; tint alpha scales its strength. Both effects default to Base Color alpha 0. There is no Render Mode parameter. Identical normals and common parameters produce identical output. The common include is expanded by Copy as JSON.

Lighting/Bevel Emboss is a regular FX over a `texture2D _HeightMap = self` input. It works on any layer, without raw SDF access or layer-specific outputs. Height Channel selects luminance, R, G, B or alpha; RGB channels are multiplied by image alpha before the 0–1 input is mapped through Profile. Depth controls relief strength/sign; Smoothing is the normal sampling radius in document pixels. Output selects Both, Highlight Only or Shadow Only. Output RGB is the light/shadow tint; straight alpha is lighting strength times tint alpha, independent of host alpha. Flat areas are transparent. Choose compositing through the layer blend mode; use separate light/shadow layers for independent modes. SDF bevel width comes from the visible height gradient and Max Distance, not an FX width parameter. The former raw-distance helpers are no longer provided; re-add the preset to replace an older embedded version.

Normal Lighting alone adds `_SpecularEnabled=false`, `_SpecularColor=(1,1,1,1)`,
`_SpecularIntensity=1`, `_Roughness=0.25` and `_Output=3` (SpecularOnly). The original
shared lighting function and Bevel Emboss are unchanged. With Specular disabled, modes
0–2 return the original result; mode 3 returns transparent black, regardless of Base Color.
With Specular enabled, modes 0/1 add the highlight, mode 2 excludes it and mode 3 outputs
only the highlight. `ApplyFX(PinnedUV, color)` remains pixel-local with both normal encodings.

The highlight uses Blinn–Phong with fixed orthographic `V=(0,0,1)`, normalized N/L,
`H=normalize(L+V)` and `p=max(2/max(saturate(Roughness),0.01)^2-2,1)`.
Its strength is `pow(saturate(dot(N,H)),p)*saturate(dot(N,L))*max(SpecularIntensity,0)*saturate(SpecularColor.a)`;
back-facing N and a degenerate H return zero. Intensity is independent of diffuse
Intensity, Ambient and Light Color; no energy-conservation or environment-light model is implied.
Let `s` be this strength and `c=saturate(input.a)`. Specular premultiplied RGB is
`SpecularColor.rgb*s*c`, alpha is `saturate(s)*c`. Add its RGB to original premultiplied
lighting RGB, combine alpha as `a+specularAlpha*(1-a)` and return straight RGBA.
This preserves filled-surface coverage and HDR energy without darkening diffuse lighting,
while transparent output has usable highlight coverage. The specular lobe does not subtract
flat-normal lighting: a front-lit planar surface may reflect across its entire area.

### Texture sources

`// @param texture2D _Source = self` samples the image immediately before this FX, including earlier effects but excluding this and later effects. It reuses the existing input texture without recursively rendering the layer. On groups it reads the composed group input. `// @param texture2D _Source = none` samples transparent black. Both defaults survive HLSL preset export and copying; Self stores no layer ID. Without a default, the existing Texture mode uses white when no asset is assigned. The UI offers Texture, Layer, None and Self. Live FX parameter values also accept the strings `"self"` and `"none"`.

### Layer-backed texture parameters

Declare `texture2D` and read it through `SampleTexture(_Source, uv)`. Direct `tex2D` is a bypass path, not the primary authoring API. In the editor, choose Texture or Layer. Layer references store a same-document layer ID and resolve the standalone rendered result, including transforms and FX, without its lower backdrop. Disabled sources are allowed as with SDF Target; a disabled Shader Processor retains its bypass semantics. Groups supply full-color contents. Missing or cyclic sources bind transparent pixels.

Layer inputs use the shared effect-render cache for deterministic sources. Shader FX and Shader Processor
results are cached when their inputs and serialized parameters are unchanged; the cache also tracks
external texture updates and referenced layer stamps. Switching sources does not recompile HLSL.
HLSL preset export omits document-local layer bindings. Unified document/clipboard JSON stores them
in `fx[].parameters[]` as `textureSource: "Layer"` and `textureLayerId`; include the source layer
in the exported tree. Copy as JSON rejects missing required dependencies, and insertion remaps their IDs.
The live FX API instead accepts a texture parameter value `{ "layer": "layer-id" }`.
Ordinary Ctrl+C and cross-window dragging remap copied sources and clear uncopied external sources
when pasting into another document; they are not the JSON export path.

## External code editors

For document-owned code, **Open Code** opens a working `.hlsl` file through Unity's selected external script editor. Saving synchronizes the draft; **Compile** in WhimTex compiles it. **Open in VS Code** uses a project-local isolated profile and installs the bundled extension automatically. Detection checks Unity's registered editors, then PATH, then **User Settings → External Code Editor → VS Code Command**.

The extension augments the existing **HLSL** language mode with highlighting for directives, types, modifiers, parameter names, values, enum options and ranges. Validation checks declaration defaults, finite numbers, hard/soft bounds, tuples, hex colors, two-color gradients, curve keys, transform matrices, texture-reference syntax, compatible repeated controls, conditions, groups, foldouts, tabs and seed modifiers; trailing tooltips and block-commented examples are handled separately. Layout diagnostics include title/header rules, page placement, matching ends, nested tabs and the 16-level limit. It supports Restricted Mode without disabling Workspace Trust. It does not resolve Unity assets/includes or compile HLSL; Unity remains the authority for generated shader identifiers and compilation.

WhimTex checks the bundled VSIX content when opening code, so a changed bundle can be installed without changing the package version. After reinstalling into an already-running VS Code window, use **Developer: Reload Window** to activate the update.

The bundled HLSL snippets expand `// @if`, `// @enableif`, `// @group`, `// @foldout`, `// @tabs` and `// @tab` with **Tab**, adding matching end directives. Compact spellings such as `//@foldout` also work. Tab visits each placeholder, then the body. Group/foldout titles may include `; _Parameter` for a header control. The tabs snippet starts with no overall title: optionally enter `(Title)` in its first placeholder, then name the first page. Add further pages with the tab snippet inside that tabs block. The extension contributes the HLSL default `editor.tabCompletion: onlySnippets`; it does not write user settings. Explicit overrides can disable direct Tab expansion; the snippets remain available through **Ctrl+Space** or **Insert Snippet** after `// `.

Working files and their last synchronized baselines live in `Library/WhimTex/ExternalCode`. The VS Code save handler writes a neighboring `.apply` request for these files. Unity polls active sessions every 0.35 seconds, imports the saved draft and calls the internal `Apply` method, including when the code is unchanged. Ordinary external writes synchronize after two stable observations and do not request compilation. FX locks defer synchronization; conflicting document/file changes ask which version to keep. A failed compilation preserves source and parameter values but skips the FX, including any older compiled version, and reports diagnostics until a successful compilation.

Working copies are disposable cache, not backups, standalone presets or the saved document. Applying code does **not** save the TIFF or JSON document; save it in WhimTex separately. Sessions are in-memory: after a script reload, reopen the code from WhimTex to reconnect.

At Editor startup/domain reload and then hourly, cleanup removes inactive working-file sets unused for more than **24 hours**, including their `.baseline` and `.hlsl.apply` companions. Reopening code refreshes its baseline timestamp without changing its content; a newer timestamp on any member preserves the whole set. Connected sessions are excluded and periodically refreshed, including before reload/quit. Only generated 32-character lowercase-hex names with the known suffixes are eligible; cleanup is non-recursive and skips linked files/directories. The VS Code profile/extensions and unrelated files are untouched. Saved-document state is deliberately not checked: code absent from the saved document may be lost after expiry. A surviving working copy can still be reused when reconnecting, but recovery is not guaranteed.

Catalog-linked code uses **Open HLSL Source** to edit its shared HLSL source; values stay local to each FX. To edit the existing effect's code independently, choose **Code → Embed Copy**. This stops following the `.hlsl` file and enables document-local code editing while preserving values and the original include base. Custom algorithms can also be authored as a new inline **Add FX → Custom Shader FX**. Relative includes resolve from the document/source context, not the working-copy directory.

## HLSL catalog

Add a `.hlsl` file anywhere in Assets or an installed package. Its **first physical line** must be
`// @whimtex-effect Category/Name`. UTF-8 BOM is allowed, but no preceding blank line, indentation,
license comment or other text. Unmarked HLSL files are not catalog effects. Discovery reads headers only,
without parsing parameters, loading FX instances or hashing shader dependencies. The menu uses a
ready, sorted header snapshot; opening it performs no file scan or shader compilation. After startup
or script reload, reconciliation is scheduled outside the click: Unity captures asset paths and resolves
each package once, a worker reads headers, and the main thread publishes the completed snapshot.
Session headers remain available during reconciliation; superseded scans cannot overwrite newer data.
Import notifications update affected project headers. User files refresh in the background on Editor
focus, preset saves, library-location changes and menu requests. A menu already open retains its snapshot;
reopen it to see a refresh that finished later. With no initial snapshot it displays **Loading effect presets…**.
Explicit agent/API catalog discovery remains synchronous and fresh, independent of menu readiness.
Full validation and loading happen when a preset is selected; invalid source reports an error without adding an FX.

```hlsl
// @whimtex-effect Color/Invert
// @param float _Amount = 1 [0 .. 1]

float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, 1 - color.rgb, _Amount), color.a);
}
```

Use **FX → Add FX** to add an independent document-owned instance. The HLSL source is referenced by asset GUID;
keep its `.meta` when moving files. Source/include changes refresh loaded, unlocked instances.
Missing or invalid source retains the last applied shader and reports diagnostics. **Compile** retries/reloads.
**Code → Embed Copy** detaches the source link and enables local code editing; later source-file
changes no longer update this FX. Values and the original include base are retained.
Included files remain external dependencies after detaching unless expanded into a snapshot/export.
Only marked `.hlsl` files enter the catalog; ShaderFX `.asset`, Material and standalone ShaderLab
sources are unsupported. No assets are created merely by adding an effect.

The user library's `ShaderFX` subfolder is also scanned recursively when opening the catalog.
The same first-line marker is required. These external presets are embedded copies, not GUID-linked
sources. Custom includes are expanded when adding a user preset; Unity includes stay external.
Relative includes in a user preset must stay within the configured `ShaderFX` folder.
Project/catalog discovery still uses AssetDatabase and import notifications.

**Save Preset…** exports the current code with parameter declarations rewritten to current
values, retaining float bounds and existing categories; the file name supplies the last category segment.
Custom includes are expanded for portability (cyclic or oversized include trees are rejected).
Engine includes remain external. Files can be saved under user `ShaderFX` or project `Assets`.
Existing effects are not detached or switched to the saved file.

### FX block control

If the parameter name contains `Opacity` or `Alpha` (case-insensitive), the numeric drag handle uses the same alpha icon as the Layers header instead of arrows; dragging behaves identically.

Float controls have a **↔** handle before the field: drag horizontally to adjust the value, hold Shift for finer changes or Ctrl for faster changes. The gesture respects hard/soft limits and forms one Undo step.

Use `// @control(_Opacity)` immediately after the catalog marker, or on the first line when there is no marker, to expose one existing parameter in the FX block header before **⋮**:

```hlsl
// @whimtex-effect Color/My Effect
// @control(_Opacity)
// @param hidden float _Opacity = 1 [0 .. 1]

float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(color.rgb, color.a * _Opacity);
}
```

The compact, unlabeled field supports bool, enum, float, color, float2, float3 and float4. Its tooltip identifies the parameter. Unsupported types leave the header unchanged. The parameter must have a single declaration. `hidden` suppresses its normal body row, not this header field; omit `hidden` to display both controls editing the same value. This is UI metadata only: it neither declares a uniform nor adds automatic opacity blending. Use the parameter in your HLSL to define its behavior.

Only one control is selected. Multiple directives produce soft warnings and the last declaration wins, even if invalid (there is no fallback to an earlier declaration). Malformed syntax, unknown/ambiguous parameter references and misplaced directives also produce warnings rather than shader errors. Documents, preset exports and portable code preserve the binding; exports write one canonical directive after the catalog marker.

### Parameter declarations

Use `// @if _Mode == 1` or `// @if _Mode != 1` before one or more `// @param` lines, then close the block with `// @endif`, to show controls conditionally. Use `// @enableif _Enabled == 1` or `// @enableif _Enabled != 1`, closed by `// @endenableif`, to keep rows visible while disabling editing when the condition is false. Fields, their headers/help boxes and standalone previews follow the enclosing conditions. All enclosing enable conditions must be true; a true inner condition cannot re-enable a disabled ancestor, including a read-only view.

Both kinds compare a `float`, `bool` or `enum` parameter with a finite numeric literal using `==`/`!=`; bool conditions use 0/1, not true/false. Drivers may be declared later or inside `@enableif`, but cannot themselves have any declaration under `@if`. Unknown and vector drivers are invalid when a block contains an actual parameter or standalone preview, including nested rows and hidden declarations. Empty blocks create no driver dependencies; headings/help boxes alone are not rows. Comparison syntax and finite literals are still validated in empty blocks.

`@enableif` may nest within itself and with `@if` in either order, to a combined maximum of 16 condition levels. A second active `@if` remains invalid even through an intervening `@enableif`. Each end directive takes no arguments and must close the innermost condition of the same kind. These directives change only the editor UI: values remain stored and uploaded, and rendering behavior comes from HLSL. Preset export preserves both kinds of condition blocks.

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

```hlsl
// @param bool _Enabled = false
// @enableif _Enabled == 1
// @header(Details)
// @helpbox(Enable this section to edit its settings.)
// @param float _Strength = 0.5 [0 .. 1]
// @enableif _Strength != 0
// @preview(PreviewStrength, _Strength)
// @endenableif
// @endenableif
```

Each `ShaderFXParameterControl`, including a standalone preview, carries an outer-to-inner
`ShaderFXEnableCondition[] enableIf` array, empty outside enabled blocks. Each public
`ShaderFXEnableCondition` struct has `string parameter`, `bool notEqual` and `float value`
fields. Visibility continues to use the existing `visibleIfParameter`, `visibleIfNotEqual`
and `visibleIfValue` fields; preview-only rows carry the same condition metadata as fields.

Use `// @header(Lighting)` before a `// @param` declaration to add a bold, non-collapsible heading above that control. Use `// @helpbox(Your hint text.)` to show an informational help box above the parameter instead. Both are UI metadata, not uniforms, and are preserved when exporting FX and brush presets. Headings and help boxes without a following parameter are ignored. Previous-name migration directives are not supported.

Built-in Color Filter, Negative, Mask, Gradient Map and Hue & Saturation use `_Opacity`, without old-name aliases.

```hlsl
// @header(Lighting)
// @helpbox(Keep this value subtle to preserve the input colors.)
// @param color _LightColor = (1, 1, 1, 1)
// @param float _Intensity = 1 [0 .. ~4]
```

### Layout containers

Use `@group` and `@endgroup` to visually contain several controls in a non-collapsible bordered block. An optional title appears in its header. `@foldout(Title)` / `@endfoldout` adds a collapsible body and requires a title. Both accept `; _Parameter` to bind a header field. Declare that parameter exactly once across the source, outside both `@if` and `@enableif` and directly inside its own group/foldout, rather than inside a descendant container.

A bool is drawn as an unlabeled checkbox to the left of the title. It edits only the bool value, independently of collapse or tab selection. Use `@if` to hide dependent rows or `@enableif` to leave them visible and disabled; use the uniform in HLSL to enable or disable the effect. Supported compact values (enum, float, color, float2, float3, and float4) are drawn on the right without a separate label: the container title labels the value. Dragging the title of a float field changes its value and respects its hard/soft bounds. The linked control is omitted from the body even when declared `hidden`; a supported hidden header field is promoted into the header. Unlinked hidden controls remain invisible, while unsupported or multi-row controls stay in the body and do not alter the header. If all body rows are hidden by `@if`, the body occupies no space and only the header remains; `@enableif` retains the body's space.

```hlsl
// @group(Tint; _EnableTint)
// @param bool _EnableTint = true
// @param color _Tint = (1, 1, 1, 1)
// @param float _TintStrength = 1 [0 .. 1]
// @endgroup
```

For example, `// @group(Quality; _Quality)` with `// @param hidden enum _Quality = 1 {Low: 0, High: 1}` puts the dropdown next to the group title without a duplicate row. Use `// @group(Advanced)` for a titled group without a linked field, or plain `// @group` for a box without a header.

Right-click a named group/foldout header or an individual tab for **Copy**, **Paste**, a separator and **Reset**. Bodies, unnamed groups and the overall tabs heading have no parameter-set menu. Actions include the linked header value, hidden/conditional/disabled parameters and nested containers of that group or page; repeated controls for one uniform are handled once. Copy stores a session-local snapshot of values, not code or metadata. Paste requires the same number of parameters, matching names (or unique UI labels), compatible types and identical enum options, including when transferring between a group and a tab. Destination float hard bounds still apply; layer texture references can only be pasted within their source document. Reset restores only that group's or page's defaults from the current HLSL declarations, not sibling pages or shared fields outside the page. Paste and Reset each form one Undo operation and retain destination parameter identities, code, compiled shader and current tab selection. These actions use the shared FX/brush parameter view.

Use `@tabs` or `@tabs(Title)` for pages, each opened by `@tab(Page title)` and closed with `@endtab`; close the whole block with `@endtabs`. The overall title is optional; page titles must be non-empty and unique within that tabs block (case-sensitive, after trimming). Empty parentheses are invalid; omit them for untitled tabs. An individual page can use `@tab(Page title; _Enabled)` to place a bool checkbox before its label. Declare that bool exactly once, unconditionally and directly inside the page, not inside a nested group/foldout; hidden bools are promoted, and no duplicate body row is drawn. Other parameter types are rejected for tab headers. Editing the checkbox does not select its page, and selecting a page does not change the bool. It does not automatically disable the page or its fields; use `@enableif` or `@if` for dependent rows and the uniform in HLSL for the actual effect. The overall `@tabs` heading accepts no header binding. Parameters and standalone previews must be inside pages, never directly under `@tabs`; groups/foldouts also belong inside pages.

```hlsl
// @tabs
// @tab(Color)
// @foldout(Tint; _EnableTint)
// @param hidden bool _EnableTint = true
// @enableif _EnableTint == 1
// @param color _Tint = (1, 1, 1, 1)
// @endenableif
// @endfoldout
// @endtab
// @tab(Shape)
// @group(Size)
// @param float _Radius = 0.5 [0 .. 1]
// @endgroup
// @endtab
// @endtabs
```

Groups and foldouts may nest in pages and in each other, with one existing restriction: opening a group is rejected when the nearest active group/foldout is a group. Thus `group → group` is invalid, while `group → foldout → group` is valid. Tabs may be placed inside a group/foldout, but no tabs block may contain another tabs block, even through intervening pages or containers. The maximum layout depth is 16, counting groups, foldouts, tabs blocks and pages.

Every container/page must contain at least one non-hidden parameter declaration or standalone preview, directly or in its descendants. Hidden declarations alone are insufficient, including a hidden header binding; `@header` and `@helpbox` do not count as rows. A row with `@if` or `@enableif` still counts even when its condition currently hides or disables it. All container/page starts and ends must be outside both condition kinds. Conditions may control rows inside groups, foldouts and pages, but must end before their container closes. End directives take no arguments and must match the innermost open container/page.

Tabs are content-width window-style tabs, with the active tab joined to the content panel. Untitled `@tabs` reserve no blank heading row. They have no close, new or reorder actions. Folding and page selection change layout only: all parameter values are still uploaded and the effect still runs regardless of the visible page or collapsed body. `@if` controls row visibility; `@enableif` controls whether visible rows can be edited. Per-FX foldout/tab choices are UI-local `SessionState`, keyed by the FX's stable `ShaderKey` and container kind/id/title. Choices survive UI rebuilds and script reloads during the current Editor session, but not a full Editor relaunch. They are not shader values, document settings or Undo operations.

Each C# `ShaderFXParameterControl`, including a standalone preview, exposes a `ShaderFXLayoutContainer[] containers` path ordered from outermost to innermost, empty outside layout containers. Each entry contains:

| Field | Meaning |
| --- | --- |
| `kind` | `ShaderFXContainerKind.Group`, `Foldout`, `Tabs` or `Tab`. |
| `id` | Source-order container identity shared by controls in that container. |
| `title` | Container/page title, absent for untitled groups/tabs. |
| `headerParameter` | Linked header name for a group/foldout or bool checkbox for a tab page; absent for an overall tabs block. |

The older flat fields `inGroup`, `groupId`, `groupTitle` and `groupHeaderParameter` remain a summary of the nearest group/foldout. They do not describe the complete layout or tab ancestry; use `containers` to reconstruct that path. These declarations describe UI layout without introducing additional parameter uniforms.

Use `label(...)` inline to override a parameter's generated UI label without changing its shader identifier: `// @param label(Tint Strength) float _Strength = 1`. The `hidden` and `label(...)` modifiers can appear in either order. Quote labels that contain parentheses; labels are preserved on preset export.

```hlsl
// @param float _Strength = 0.02 [0 .. 0.1]
// @param float _Scale = 1 [0 ..]
// @param float _Bias = 0 [.. 10]
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

### Inspector previews

Use `preview(FunctionName)` inside a parameter declaration to draw a preview within its control.
For Slider (including soft-range sliders), it fills the track beneath the thumb. Other fields show
a small rectangle to their right; their native controls are not replaced. Use a standalone
`// @preview(FunctionName)` for a separate image at that position in the inspector.
Both forms respect groups and conditions; neither creates another shader parameter.
Standalone previews use a centered 96×96 logical-pixel square; inline field and slider previews retain their own dimensions.

Built-in Mask, Chroma Key and Edge Outline use standalone previews in collapsible sections.
They render opaque grayscale: Mask evaluates the selected channel after Transform/Profile/Invert,
Chroma Key evaluates retained alpha, and Edge Outline evaluates contour coverage including source
and line alpha. They use the production mask helpers and ignore header Opacity and output mode.
No additional parameters or rendering contract are introduced; the existing bounded inspector
input path is retained, including its reduced-resolution approximation and fallback behavior.

Hard- and soft-range Slider previews use the native Slider's fixed 50-pixel numeric field size and matching
horizontal spacing. Track endpoints stay aligned when numeric text or inspector width changes.
Ordinary/cyclic float2 endpoint fields share those same fixed dimensions, independent of content.

```hlsl
// @param preview(PreviewAmount) float _Amount = 0.5 [0 .. 1]
// @param preview(PreviewTint) color _Tint = (1, 0.5, 0, 1)
// @preview(PreviewMask)

#ifdef WHIMTEX_PREVIEW
float4 PreviewAmount(float2 uv, float value) { return float4(value, _Amount, uv.y, 1); }
float4 PreviewTint(float2 uv, float4 value) { return value; }
float4 PreviewMask(float2 uv) { float a = SampleInput(uv).a; return float4(a, a, a, 1); }
#endif
```

Every entry returns straight linear `float4` RGBA and takes `float2 uv` first, bottom-left
`(0,0)` to top-right `(1,1)`. Inline entries may optionally take a second argument matching
the parameter's shader representation: `float` for float/enum, `bool` for bool, `float2`
for float2/point, `float3` for float3/normal, and `float4` for float4/color. Texture
parameters use `WhimTexTexture`; gradients and curves use `sampler2D`; transform2D uses its input-to-local `float3x3`.
For gradients and curves, prefer UV-only entries with the normal named `_Name_Sample` helper
to retain the library's sampling/wrap behavior. Entries must not be overloaded; argument
names are arbitrary, but UV must precede value. Without explicit bindings, standalone entries take UV only.

Both forms also accept an explicit ordered list of parameter names:

```hlsl
// @param preview(PreviewRange, _HueRange, _Feather) float2 _HueRange = (330, 30) [0 % 360]
// @param float _Feather = 15 [0 .. 180]
// @preview(PreviewRange, _HueRange, _Feather)

#ifdef WHIMTEX_PREVIEW
float4 PreviewRange(float2 uv, float2 range, float feather)
{
    float edge = saturate(feather / 180.0);
    return float4(uv.x, range.x / 360.0, edge, 1);
}
#endif
```

After `float2 uv`, the function must take exactly the listed arguments, in that order and
with their exact shader types from the table above. No implicit own `value` is appended.
Names refer only to `// @param` declarations, not arbitrary uniforms, literals or expressions.
Forward references, hidden/conditional parameters and repeated names are supported.
The attached control's type does not constrain explicitly listed arguments. Without a list,
the existing UV-only/optional-own-value signatures remain unchanged.

For an inline scalar Slider, a listed reference to its own float parameter receives the
track candidate, in whichever argument position it appears. Other references receive actual
current values. Standalone previews receive actual values for every reference. Both forms
retain ordinary global uniform/helper access. Unknown names or signature mismatches are local
preview diagnostics, not production-FX failures. Metadata syntax itself must still be valid.
Preset exports and native/portable roundtrips retain the ordered bindings; serialized controls
store them as `previewParameters`, separate from `previewFunction`.

For scalar Slider, `value` varies over the displayed track range, independently of the actual
uniform. For a bounded float2 interval it is the actual selected pair; `uv.x` spans the whole
track from its lower to upper limit. At other fields it is the selected value. Numeric `px` values use the same pixel
multiplier as the shader. All ordinary FX uniforms, texture parameters, gradient/curve
helpers and transform helpers remain available by name. Rendering never substitutes
candidate values into uniforms or edits the document, so native editing and Undo are unchanged.

Each preview has a small display texture. Attached surfaces of the same FX share expanded
source, compiled programs with identical callback expressions, and prepared parameter bindings
(including gradient/curve lookups). Typed callbacks referring to different uniforms remain separate.
The parameter fingerprint is computed at most once per Editor update; unchanged, nonanimated
surfaces do not redraw. Canvas dimensions and pixel scale also invalidate prepared bindings.
FX change notifications and Undo/Redo queue a refresh for the next Editor update, without
waiting for the 100 ms polling interval. Bursts coalesce per FX; every surface draws at most
once per Editor update, including periodic polls and live input captures. Changes during a
refresh remain queued for the next update. The 100 ms poll stays as a fallback for unnotified
direct edits and other external changes; it is not the normal interactive update path.
Shared resources are released when the last surface detaches or before script reload/quitting;
live incoming textures are never retained. `WHIMTEX_PREVIEW` is
defined only there: guard preview-only code as above to keep its compilation errors separate
from the production FX. A missing or incompatible entry shows a diagnostic on that preview;
it does not disable the effect. Syntax errors in shared, unguarded HLSL still affect the FX.

In an attached layer's FX inspector, image-dependent previews use the incoming image before
that exact FX, including earlier effects and resolved layer/self texture inputs. The initial
on-demand source is limited to 512 pixels on its longest axis; this is an inspector preview,
not a full-resolution classification oracle or a histogram analysis stage. Without a layer
context (including a standalone asset inspector), incoming/self/layer images are transparent;
assigned texture assets and ordinary parameters remain available. Hidden/offscreen previews
release their resources on detach and do not render while hidden. Values and dimensions
invalidate the display without recompiling; HLSL or reapplied includes rebuild its shader.

Preview sliders retain native dragging, keyboard control and numeric input. Their track is
10 logical pixels high in a fixed 20-pixel Slider; a 12×10 triangular marker straddles its
bottom edge. Preview texture dimensions cannot change the track or marker layout.

The built-in Hue & Saturation Master uses these previews with a red reference sample. Hue
previews a shift, Saturation previews weighted-gray scaling, and Lightness previews darkening/lightening
with the other current settings. Preview and image correction share the same Saturation/Lightness helper.
Hue & Saturation shares explicitly bound master/range correction functions across its controls,
and its Hue Range tracks share one selection function bound to that range and its Feather.
Its Hue Range tracks show the current selection on the source hue
spectrum, including Feather and a small coverage band. Feather itself has no preview. Source Saturation (HSV) shows the current
saturation gate. These are UV-only current-state previews, not candidate-value ramps.
Range correction tracks use that range's center hue as their reference; they describe its
own correction, not the final average of overlapping ranges. Display
colors are limited to the inspector's SDR range; numeric soft limits and HDR effect output
are not changed. Histogram data preparation and new interactive controls are outside this feature.

### Shared hue lookup

`_WhimTexHueGradient` is a global 1536×1, RGBAFloat, bilinearly filtered, repeating texture,
baked once in memory per Editor domain and restored after script reload. The payload is 24 KiB;
there are no mipmaps or saved texture assets. `_WhimTexHueGradient_TexelSize` follows Unity's
`(1/width, 1/height, width, height)` convention. Do not put either name in shader Properties:
a material property would shadow the global binding. The names and `WhimTexHue` helpers are
reserved, not FX parameters.

FX and Static/Dynamic HLSL brush wrappers automatically include the helpers. Other shaders
can include `Packages/com.dcfapixels.whimtex/src/Shaders/HdrColor.cginc`, or declare the global
sampler explicitly. `WhimTexHue(hue)` accepts turns (0..1, repeating outside this range) and
returns the RGB components of HSV `(hue, 1, 1)` without sRGB decoding; use it for HSV math.
`WhimTexHueLinear(hue)` decodes this result for direct linear-color output.

```hlsl
float4 PreviewRainbow(float2 uv) { return float4(WhimTexHueLinear(uv.x), 1); }
```

The half-texel offset in the helper preserves hue 0 at the first texel and interpolation
across the repeating seam. Hue & Saturation inspector previews use this lookup. Production hue rotation remains analytical;
the shared lookup is not an alternate image-processing algorithm.

### Seed parameters

```hlsl
// @param seed label(Seed) float _Seed = 0 [0 .. ~10000]
```

`seed` adds a Random button beside a numeric field; it does not change the HLSL `float`
or infer anything from the uniform name. A click chooses an integer from `ceil(min)` through
`floor(max)`, inclusive. Both bounds must be finite and within −16777216…16777216, with
at least one integer between them. Equal hard bounds are valid. Soft bounds constrain random
generation too, but manual entry/label dragging retains normal hard/soft rules and accepts fractions.
Invalid declarations use the shared FX diagnostics.

The modifier works in FX and HLSL brush tips, with labels, conditions, groups, repeated declarations
and `hidden`. A hidden body row has no button; an explicitly linked group/header control still does.
Repeated seed controls use their own ranges. Preset export and copy/paste retain the modifier,
range and chosen value. Random uses normal value editing, Undo and render invalidation; it does
not recompile HLSL. Preview, save, load and bake never choose a new seed.
Agent FX edits accept [`randomizeSeeds`](AgentAPI.md#fx-edits-and-presets); the first seed declaration
of each named uniform supplies its range. Validation does not randomize. Multi-editing and bound
document parameters remain separate deferred features.

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

Built-in Levels declares `_Curve` (shared RGB) and `_RedCurve`, `_GreenCurve`, `_BlueCurve`,
`_AlphaCurve`. `_CurveChannel` only selects the editor field; every curve remains active and
serialized. The shared curve runs after input normalization/Gamma, before output levels;
`_PreserveColor` chooses luminance versus independent RGB. Individual RGB curves then apply
`value + curve(saturate(value)) - saturate(value)`, preserving HDR excess beyond 0–1.
Alpha uses its own curve, clamped to 0–1, without the shared levels/Gamma. `_Opacity` blends RGBA.
Built-in Threshold uses `_SourceChannel`: Luminance=0, R=1, G=2, B=3, Alpha=4.
The source value is not clamped; `_Smooth` is the half-width of the smoothstep interval around
`_Threshold`, or a hard step when zero. Low/high colors affect RGB only; source alpha is preserved.

**Save Preset…** writes the current curve as an optional default:

```hlsl
// @param curve _Profile = keys((0, 0, 1, 1, 0, 0, 0), (1, 1, 1, 1, 0, 0, 0))
float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(color.rgb * _Profile_Sample(uv.x), color.a);
}
```

Each key tuple is `(time, value, inTangent, outTangent, inWeight, outWeight, weightedMode)`.
There are at most 256 keys, with strictly increasing finite times. Values are finite; tangents
also accept `inf` and `-inf` for steps. Weights are 0..1; weightedMode is 0 (none), 1 (in),
2 (out), or 3 (both). Only the sampled 0..1 interval is visible to the shader.
No range follows a curve declaration. Repeated declarations share one curve and the last explicit
default wins. Curve parameters also work in HLSL brush tips.

### Gradient parameters

`gradient` without a default creates an opaque black-to-white gradient with Perceptual color mode and Balanced interpolation.
Declare `// @param gradient _Ramp`, optionally with two endpoint colors such as
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint accepts `#RRGGBB` (opaque) or
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Explicit endpoint defaults use Gamma color mode,
Blend interpolation, Gamma key encoding, Clamp, Smoothness 0, Chroma Correction 0 and midpoint .5.
The parameter remains editable after creation.
Use `_Ramp_Sample(t)` to obtain straight linear RGBA. The helper uses the gradient's **Wrap** setting: **Clamp** holds endpoint colors, **Repeat** repeats every unit, and **Mirror** alternates forward and backward every unit, including negative inputs.
Do not redeclare a sampler or reference internal `_WhimTex_` uniforms.
Colors, HDR, alpha, color mode, interpolation, hue path, chroma correction, smoothness and midpoints are edited in the gradient field.
Repeated declarations share a gradient value; copying an effect creates independent gradient data.

The effect lazily caches a 512×2 RGBAHalf LUT without mipmaps. Unchanged renders reuse it;
edits upload new pixels without recompiling the shader. Fixed uses Point filtering, other modes
use Bilinear. LUT sampling is an approximation: transitions finer than one LUT interval may be lost.
Custom uses that same Bilinear LUT even for Constant tangents. Constant follows the CPU
right-key convention, but its rendered steps retain LUT discretization and filtering;
they do not use the Fixed GPU branch. See [Custom curves](AI/README.md#custom-channel-curves).
GPU caches are released with the material and recreated after reload. Edited keys are serialized
in the effect/document. The code default initializes new instances; applying code preserves the current edited value.
**Save Preset…** writes a two-endpoint Gamma/Blend gradient with Gamma key encoding, Clamp, Smoothness 0, Chroma Correction 0 and
midpoint .5 as a default. Active or retained Custom curves are rejected by endpoint-only HLSL
export even when the active automatic gradient has two stops; save the complete palette as
a gradient preset or retain it in the document. Other gradients must be simplified before this endpoint-only export;
the exporter rejects them rather than silently changing their settings.
Gradient parameters also work in HLSL brush tips, with the same sampling and export rules.

```hlsl
// @param gradient _Ramp // Map input brightness to colors.
float4 ApplyFX(PinnedUV uv, float4 color)
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

`enum` is a float displayed as a dropdown. Every option needs an unquoted identifier and
an explicit finite numeric value; fractional values are allowed. Names and values must be unique.
Option names are editor-only labels (for example, `SoftLight` displays as **Soft Light**), not HLSL
constants. A default may be an option name or a number. An unmatched value displays **Custom**
without changing the number.

Repeated compatible declarations create linked controls in source order, but only one stored value
and one uniform. `float`, `bool` and `enum` share scalar storage; other types must match exactly.
The last declaration **with an initializer** supplies the default. Defaultless declarations do not
overwrite it. Ranges constrain edits through that control, not the shared value or its default.
Saving a preset writes the current value into one declaration and omits other initializers.
HLSL brush tips share these declarations, metadata and fields. Their texture sources are limited
to Texture/None, point coordinates use tip UV, and Edit on Canvas is unavailable. See the
[brush parameter contract](AI/BRUSHES.md#declared-parameters) for context-specific rules.

Unbounded `float2` and `float3` expose two and three raw components. `point` is a `float2` in normalized canvas UV (bottom-left `(0, 0)`, top-right `(1, 1)`) and defaults to `(0.5, 0.5)`. Coordinates and tuple defaults may lie outside the canvas. The hand button (**Edit on Canvas**) beside the numeric field activates a draggable point handle. Point dragging uses the shared screen-space snapping radius for canvas edges and enabled, visible guides (including their intersections). Ctrl, or Command on macOS, bypasses snapping without restricting coordinates. `normal` generates a normalized `float3`, defaults to `(0, 0, 1)`, and uses that direction when given a zero vector. These types accept optional tuple defaults; only `float2` also supports a range. Live API values are arrays with the corresponding component count.

### Float2 intervals

```hlsl
// @param preview(PreviewRange) float2 _Range = (0.2, 0.8) [0 .. 1]
#ifdef WHIMTEX_PREVIEW
float4 PreviewRange(float2 uv, float2 value)
{
    float selected = step(value.x, uv.x) * step(uv.x, value.y);
    return float4(selected.xxx, 1);
}
#endif
```

Two finite hard limits with `min < max` select the native MinMaxSlider. X is the lower
endpoint, Y the upper; drag either handle or move the whole interval, or edit the synchronized
unlabeled numeric fields (lower X on the left, upper Y on the right). Removed label space is
given to the slider. Endpoints may coincide but cannot cross or leave the limits. Omitted defaults
select the whole interval. Soft (`~`) or missing limits are rejected for float2, as are
out-of-range or reversed defaults/API values. Ordinary unbounded float2 remains a vector field.
Limits and preview survive preset export; retained values clamp to newly edited hard limits.
Inline preview decorates the native track and its two handles without replacing native input.
Interval markers are enlarged 8×12 half triangles extending outward from the selected interval,
with vertical edges anchored to the endpoints; scalar sliders retain 12×10 full triangles.
Bounded interval handles remain visible and pickable at both hard limits, including full/empty ranges.
Only preview containers allow handle overflow; the rounded preview track remains clipped. Endpoint gutters
keep the outward halves clear of the unchanged numeric fields.

Replacing `..` with `%` declares a cyclic range:

```hlsl
// @param preview(PreviewHueRange) float2 _HueRange = (330, 30) [0 % 360]
// @param float _Angle = 20 [0 % 360]
```

For float2, X is the start and Y the end of the forward arc; X > Y crosses the seam.
The track shows one period, with both selected pieces highlighted for a wrapped arc.
Captured dragging continues beyond either track edge without warping the OS pointer;
handles retain their start/end identity. Dragging the selection moves both endpoints and
preserves its width. When narrowing, a handle stops at the other endpoint (zero width).
When widening, it stops at a full cycle at the other endpoint, without resetting either marker to the track limits. Dragging can wrap at
the track seam but cannot cross either zero/full-width stop. After hitting either stop,
reversing direction moves immediately. Both half-markers remain visible and pickable, including at coincident endpoints
and the outer track edges; only the preview track clips its image.
Full-cycle values store endpoints one positive period apart, with at least one endpoint inside
the limits: `(-330, 30)` and `(330, 690)` both cover a full `[0 % 360]` cycle and retain the
fixed endpoint. The UI wraps these coordinates for markers and numeric fields, so both show
the meeting angle while the whole track stays selected. This winding is part of the value,
not transient UI state; refresh, preset export and Undo retain it. `(min, max)` is also full;
equal stored endpoints and `(max, min)` are zero-width. Moving a full cycle moves its anchor
while retaining full width; moving an empty arc retains zero width.
Arrow keys move the focused endpoint; Ctrl/Command moves the whole arc, Shift increases the
step and Alt reduces it. Escape cancels an active drag.
Numeric endpoint edits wrap outside the limits without sorting. In-range endpoints, including
the exact maximum, are retained. Editing a numeric endpoint constructs a new arc from the
displayed angles. API/default endpoints must be within the limits except for the anchored
full-cycle representation above; reversed endpoints are valid only for `%`. Defaults omitted
from a cyclic declaration select the full cycle.
The shader receives the actual start/end float2, not a hidden center/width conversion.

For float, `%` keeps ordinary clamped slider dragging, but manual numeric entry repeats:
`370` becomes `10`, `-10` becomes `350` in `[0 % 360]`. Both `%` types require two finite
hard limits with min < max; missing/soft limits are rejected. Exact limits remain selectable.
Scalar default/API values remain bounded. Percent syntax and cyclic flags survive preset/native roundtrips;
JSON derives the flags from HLSL metadata. Linked declarations must agree on `%` versus `..`.
Live FX parameter snapshots expose `cyclicRange` for both types.

Hue & Saturation uses `_SaturationRange` in `[0 .. 1]` (cutoff/full strength) and `_Range1` through
`_Range4` in `[0 % 360]` (cyclic hue endpoints in degrees). `(330, 30)` crosses red;
`(0, 360)` selects all hues. Preview tracks show one hue turn and correction references use
the cyclic midpoint. Feather remains a separate per-side falloff. Previous Center/Width and saturation
cutoff/feather uniforms are removed without aliases or migration. Levels retains independent
soft-bound controls because its HDR values and reversible output are not ordered bounded intervals.

For `normal`, **Edit on Canvas** shows a fixed-screen-radius handle at the canvas center. The center points toward the camera; the radius edge points along the canvas. Dragging outside the radius clamps the projected direction. Clicking the handle without dragging switches the Z hemisphere: **+** faces the camera, **−** faces away. X points right and Y up in canvas coordinates; rotating the preview rotates the handle without changing the value. Changing values does not recompile the shader.

Labels are derived from names: `_NoiseScale` becomes **Noise Scale**. `float4` is four raw components;
`color` is a color picker using the editor's HDR/Standard input setting and existing linear conversion.

These declarations also work in the inline code editor without a catalog header. They define the
parameter schema for the effect. Existing matching name/type
values and IDs survive compilation; removed declarations disappear. Renaming in place without changing
the type or layout retains identity. When simultaneously restructuring and renaming declarations,
unmatched parameters are treated as new rather than guessing their correspondence.
Do not separately declare generated uniforms/helpers. `_WhimTex_` is reserved for generated data.
The limit is 128 declarations per effect; the live API supports up to 128 parameters per FX.

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

float4 ApplyFX(PinnedUV uv, float4 color)
{
    float2 p = _Area_ToLocal(uv);
    if (any(p < 0) || any(p > 1)) return 0;
    return SampleInput(p);
}
```

Canvas editing uses one window-local temporary tool slot for `transform2D`, `point` and `normal` parameters. Activating another parameter replaces the slot while preserving the original return tool. Leaving returns to the previous valid context tool or base tool; losing the target invalidates the slot. Escape cancels an active gesture first, then exits on a separate press. Tool selection is not serialized in TIFF or recorded in Undo; parameter edits still support Undo. Inspector/color-picker focus does not end the tool.

## Shader Processor: process the lower stack instead of one layer

The Processor uses the same ApplyFX/SampleInput interface, with the lower composite as input.
Normal blending uses Opacity to mix original and processed RGBA; 100% replaces the input.
Other blends combine it with the result. Both ranges default to HDR; hiding the Processor bypasses it.

In Pass Through groups it also sees the external backdrop; isolated groups restrict it to their
children. Standalone previews and rasterization evaluate lower siblings against transparency.
Processors are clipping-chain boundaries, not clipping layers or bases. PSD bakes the composite
and retains the original layers in a hidden Source Layers folder.

## Baking implementation

The stack's **Apply All** and **⋮ → Apply** bake rendered pixels; they are distinct from compiling code with **Code → Compile**. A per-FX bake consumes the inclusive prefix, preserving the remaining stack. The result is Drawing with unchanged logical Transform; serialized pixel-frame compensation prevents double-transforming the snapshot, and converted groups retain their FX coordinate frame. Painting uses the pixel frame, while Transform editing uses the logical frame. The snapshot is canvas-sized linear half-float, before layer opacity/blending/channelMapping/clipping.

Shader Processor baking reconstructs its stack-position backdrop, inheriting external input through Pass Through ancestors and starting transparent inside isolated/clipped groups. Its Normal becomes Overwrite; Drawing stores `processorSnapshot` and `processorNormalBlend` to retain premultiplied before/after opacity interpolation instead of ordinary straight-RGBA Overwrite. Other blend modes retain their ordinary behavior. Processor snapshots remain clipping boundaries so existing orphan clipping layers do not acquire a new base; explicitly enabling clipping on the Drawing opts into ordinary clipping semantics. The lower layers are unchanged, but their future edits no longer regenerate the snapshot. Flags survive repeated Apply, native clipboard, TIFF and Undo/Redo.

## Preset list groups

Use one quoted group name in the leading comments to arrange presets into flat blocks in
**Add FX**:

```hlsl
// @whimtex-effect Color/Hue & Saturation
// @control(_Opacity)
// @group("Color")
// @icon("../Icons/color.svg")
```

The 31 built-in presets use the Color, Distortion, Lighting, Normal Map and Stylization
category submenus. UV Transform is in Distortion. Inside each category, related presets
use the same group (for example Color, Mask or Tone). Gradient Map shares the Color group
while retaining its gradient icon. Separators divide these
blocks without adding another navigation level. Grouping does not change catalog paths or asset GUIDs.

Groups and the effects inside each group sort alphabetically, ignoring case. Group names
are trimmed and matched without case sensitivity. Blocks are separated by a line; they
do not create submenus or group-title rows. No numeric order field is used.
Ungrouped effects and existing submenus occupy the first alphabetically sorted block.
`Category/Name` in the effect marker still creates submenus; grouping is local to each
menu, including the User library. A slash inside the quoted group name is literal.
Duplicate effect names retain their source-path suffix.

Keep the marker on line 1 and optional `@control` immediately after it. Put `@group("Name")`
after these and before HLSL declarations/includes. Empty, malformed or duplicate preset
groups are errors. Double quotes are required and cannot occur inside the group name.
This quoted declaration has no `@endgroup`; the existing unquoted `@group(Title)` /
`@endgroup` still controls parameter layout.

Grouping is read with cached catalog headers, without compiling effects. Preset export
retains it. `whimtex_fx_catalog` includes `group` (null when absent) and its `query`
also matches the group name.

## FX icons

Place icon metadata in the leading comment block, before HLSL declarations or includes.
Keep `@whimtex-effect` on line 1 and an optional `@control` on line 2; put the icon after them.
Declare one icon using a relative SVG/PNG file, an imported asset GUID, or inline SVG:

```hlsl
// @whimtex-effect Color/My Effect
// @icon("../Icons/color.svg")
```

```hlsl
// @icon("guid:0123456789abcdef0123456789abcdef")
```

```hlsl
/* @icon-svg
<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
  <circle cx="8" cy="8" r="5" fill="currentColor"/>
</svg>
*/
```

Relative paths resolve from the HLSL source directory and support `../` and `../../`.
GUIDs resolve through the current project's AssetDatabase, including package assets.
For inline document code, relative paths use the existing source/include base or document path.
An unsaved document without a source uses `Assets/Untitled.hlsl` as its base; prefer inline SVG there.

SVG icons use paths, shapes, transforms, clipping and flat fills/strokes; `currentColor` follows
the Editor theme. SVG gradients, image fills, external resources and scripts are unsupported;
use PNG for artwork needing these features. SVG rendering requires Unity 6.3+; older Editors
show the general icon for SVG and continue to support PNG. SVG is limited to 64 KiB; PNG to
256 KiB and 1024 × 1024 pixels. Keep the leading metadata within 512 KiB.

The catalog reads icon metadata without compiling effects. Preparation is queued outside popup
construction, with one request per Editor update. Shared image content reuses cached textures;
SVG is rendered for the current display scale. File icons refresh after project changes or
returning to the Editor. The same image is used in the searchable Add FX picker and FX header.
A missing or invalid icon does not disable the effect; the header icon tooltip reports its error.

Embed Copy, FX clipboard copies, user-library imports, preset export and document JSON/TIFF
serialization embed available external icons. SVG becomes `@icon-svg`; PNG uses the generated
`// @icon("data:image/png;base64,...")` representation. Saving does not mutate the source effect.
Unavailable files retain their original declaration and use the fallback icon; no missing image
can be recovered from a broken link. Linked project presets continue to follow their HLSL source.

Catalog and document FX inspection include `icon.kind` (`default`, `file`, `guid`, `inlineSvg`,
`embeddedPng`), an external `reference` when present, and `warning`. The image payload is not
repeated in inspection metadata; it remains in the HLSL code. A loader warning may appear after
queued icon preparation completes.
