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

Parameters are authored only with `// @param` in HLSL. Saved manual uniforms are not
converted into declarations; there is no manual parameter authoring mode or migration.

Declare the parameter in the code, then click **Apply**:

```hlsl
// @param float _Amount = 1 [0 .. 1]

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, 1.0 - color.rgb, saturate(_Amount)), color.a);
}
```

Parameter declarations generate uniforms and editor controls automatically; the supported types are described below.
Code and declarations stay drafts until **Apply**, including an Apply request from saving the working file in the bundled VS Code integration. A failed Apply skips the FX, including any previously compiled version, until successful recompilation. Code, values and enabled state remain editable. Yellow indicators in Layers, the FX section and the effect header expose errors and warnings even when collapsed. Warnings do not disable a successfully compiled FX. JSON loading/saving retains broken effects and reports diagnostics without aborting the document; TIFF save validation is unchanged.

FX compilation, declaration validation, source/include failures and catalog reloads share one
diagnostic list with `Error`, `Warning` and `Info` severity, message and optional source location.
Normal Apply and transient agent compilation use the same collector. Diagnostics, UI indicators,
agent responses and WhimTex Console entries use those records; the API does not reclassify display text.
Console uses Error for errors and Warning for warnings, deduplicated by source path and formatted
diagnostic until scripts reload. Suppression affects only Console, never UI/API diagnostics.
Fixing all issues clears the indicators; a successful Apply with warnings keeps them visible.

`LayerToLocal(uv)` converts canvas UV to local layer UV, including parent transforms and perspective. Use it for procedural shapes that should follow the layer. It does not clamp or wrap UV; `SampleInput` still expects canvas UV.

`SampleInput(uv)` reads the layer after earlier FX. Return straight RGBA; opacity/blending
come later. Built-in inputs include `_MainTex`, `_MainTex_TexelSize`, `_InputSize`,
`_CanvasSize` (width, height, 1/width, 1/height) and `_RenderScale`. Do not redeclare generated uniforms.

`SampleInput(uv, tiling)` selects input addressing: `0` Clamp, `1` Repeat, `2` Mirror,
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
`unity_DeltaTime` are allowed: their use does not block compilation or Apply. WhimTex does not
control their updates, so results may differ between Canvas, thumbnails and export.
Apply adds a warning to Diagnostics and disables reusable result caching for the FX.
Agent compilation returns the same warning in `diagnostics` and `warnings`, not `errors`.
The warning uses the same diagnostic and Console mechanism as other FX warnings; there is no
time-specific reporting path. Repeated Apply, agent preflight and rendering do not repeat it;
Diagnostics and API warnings remain available every time. Use an explicit parameter for
predictable time-dependent behavior. This is the current warning-only policy, not a legacy exception.

Standard `#include` supports project/package paths and relative paths. Relative paths start in
the document/FX asset folder, or Assets before the first save. After library edits, click Apply again;
after Save As to another folder, check relative paths. Libraries must suit the fragment-shader environment.

**+ Reference** links an external FX shared by its users; **Embed** makes an independent document-owned
copy. Save As and layer duplication copy embedded FX independently. FX run in order after Transform;
changing parameter values does not regenerate shaders.

## Distortion header controls

All built-in Distortion presets expose a scalar through `@control`:

| Preset | Header parameter | Behavior |
| --- | --- | --- |
| Spherize | `_Strength` | Existing signed bulge/pinch strength; Sphere clipping remains at zero. |
| Twirl | `_Angle` | Existing signed rotation in degrees; zero is identity. |
| Radial Shear | `_Strength` | Existing signed shear strength; Offset remains at zero. |
| Polar Coordinates | `_Amount` | Hidden float, default 1, hard range 0–1; sample `lerp(uv, mappedUV, _Amount)`. |
| Displacement Map | `_Amount` | Hidden float, default 1, range `[0 .. ~2]`; multiply the final strength mask before displacement and parallax tracing. |

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

`_Detection = 2` (Hue) uses HSV hue in display-encoded, nonnegative RGB, matching the HSV
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

## Texture inputs and lighting

### Height-based lighting

Normal Lighting and Bevel Emboss share `SurfaceLighting.cginc`. `_BaseColor.a` blends transparent lighting (0) into a filled surface (1), using premultiplied interpolation and returning straight alpha. Surface RGB uses `_BaseColor.rgb`, Lambert lighting, `_LightColor`, `_ShadowColor`, `_Intensity` and `_Ambient`; its coverage is the host input alpha, without another Base Color alpha multiplication. Transparent lighting subtracts flat-normal lighting and ignores host alpha/Base Color RGB/Ambient. `_Output` selects Both (0), HighlightOnly (1), ShadowOnly (2) for that component only; tint alpha scales its strength. Both effects default to Base Color alpha 0. There is no Render Mode parameter. Identical normals and common parameters produce identical output. The common include is expanded by Copy as JSON.

Lighting/Bevel Emboss is a regular FX over a `texture2D _HeightMap = self` input. It works on any layer, without raw SDF access or layer-specific outputs. Height Channel selects luminance, R, G, B or alpha; RGB channels are multiplied by image alpha before the 0–1 input is mapped through Profile. Depth controls relief strength/sign; Smoothing is the normal sampling radius in document pixels. Output selects Both, Highlight Only or Shadow Only. Output RGB is the light/shadow tint; straight alpha is lighting strength times tint alpha, independent of host alpha. Flat areas are transparent. Choose compositing through the layer blend mode; use separate light/shadow layers for independent modes. SDF bevel width comes from the visible height gradient and Max Distance, not an FX width parameter. The former raw-distance helpers are no longer provided; re-add the preset to replace an older embedded version.

### Texture sources

`// @param texture2D _Source = self` samples the image immediately before this FX, including earlier effects but excluding this and later effects. It reuses the existing input texture without recursively rendering the layer. On groups it reads the composed group input. `// @param texture2D _Source = none` samples transparent black. Both defaults survive HLSL preset export and copying; Self stores no layer ID. Without a default, the existing Texture mode uses white when no asset is assigned. The UI offers Texture, Layer, None and Self. Live FX parameter values also accept the strings `"self"` and `"none"`.

### Layer-backed texture parameters

The `texture2D` declaration and `tex2D` sampling syntax are unchanged. In the editor, choose Texture or Layer. Layer references store a same-document layer ID and resolve the standalone rendered result, including transforms and FX, without its lower backdrop. Disabled sources are allowed as with SDF Target; a disabled Shader Processor retains its bypass semantics. Groups supply full-color contents. Missing or cyclic sources bind transparent pixels.

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

For document-owned code, **Open Code** opens a working `.hlsl` file through Unity's selected external script editor. Saving synchronizes the draft; **Apply** in WhimTex compiles it. **Open in VS Code** uses a project-local isolated profile and installs the bundled extension automatically. Detection checks Unity's registered editors, then PATH, then **User Settings → External Code Editor → VS Code Command**.

The extension augments the existing **HLSL** language mode with highlighting for directives, types, modifiers, parameter names, values, enum options and ranges. Validation checks declaration defaults, finite numbers, hard/soft bounds, tuples, hex colors, two-color gradients, curve keys, transform matrices, texture-reference syntax, compatible repeated controls, conditions, groups and former-name aliases; trailing tooltips and block-commented examples are handled separately. It supports Restricted Mode without disabling Workspace Trust. It does not resolve Unity assets/includes or compile HLSL; Unity remains the authority for generated shader identifiers and compilation.

WhimTex checks the bundled VSIX content when opening code, so a changed bundle can be installed without changing the package version. After reinstalling into an already-running VS Code window, use **Developer: Reload Window** to activate the update.

The bundled HLSL snippets expand `// @if` and `// @group` with **Tab**, adding `@endif` or `@endgroup`. The compact `//@if` / `//@group` spelling also works. Tab visits the parameter, comparison and value for a condition, or the title for a group, then the body. Group titles may include `; _Parameter` for a header control. The extension contributes the HLSL default `editor.tabCompletion: onlySnippets`; it does not write user settings. Explicit overrides can disable direct Tab expansion; the snippets remain available through **Ctrl+Space** or **Insert Snippet** after `// `.

Working files and their last synchronized baselines live in `Library/WhimTex/ExternalCode`. The VS Code save handler writes a neighboring `.apply` request for these files. Unity polls active sessions every 0.35 seconds, imports the saved draft and calls Apply, including when the code is unchanged. Ordinary external writes synchronize after two stable observations and do not request Apply. FX locks defer synchronization; conflicting document/file changes ask which version to keep. A failed Apply preserves source and parameter values but skips the FX, including any older compiled version, and reports diagnostics until a successful Apply.

Working copies are disposable cache, not backups, standalone presets or the saved document. Applying code does **not** save the TIFF or JSON document; save it in WhimTex separately. Sessions are in-memory: after a script reload, reopen the code from WhimTex to reconnect.

At Editor startup/domain reload and then hourly, cleanup removes inactive working-file sets unused for more than **24 hours**, including their `.baseline` and `.hlsl.apply` companions. Reopening code refreshes its baseline timestamp without changing its content; a newer timestamp on any member preserves the whole set. Connected sessions are excluded and periodically refreshed, including before reload/quit. Only generated 32-character lowercase-hex names with the known suffixes are eligible; cleanup is non-recursive and skips linked files/directories. The VS Code profile/extensions and unrelated files are untouched. Saved-document state is deliberately not checked: code absent from the saved document may be lost after expiry. A surviving working copy can still be reused when reconnecting, but recovery is not guaranteed.

Catalog-linked code uses **Open HLSL Source** to edit the shared source. Choose **Embed Copy** to edit an independent document-owned copy instead. Relative includes resolve from the document/source context, not the working-copy directory.

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
**+ Reference** also accepts existing Material and Shader FX assets. ShaderLab shaders are not auto-enrolled by this HLSL catalog.

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

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(color.rgb, color.a * _Opacity);
}
```

The compact, unlabeled field supports bool, enum, float, color, float2, float3 and float4. Its tooltip identifies the parameter. Unsupported types leave the header unchanged. The parameter must have a single declaration. `hidden` suppresses its normal body row, not this header field; omit `hidden` to display both controls editing the same value. This is UI metadata only: it neither declares a uniform nor adds automatic opacity blending. Use the parameter in your HLSL to define its behavior.

Only one control is selected. Multiple directives produce soft warnings and the last declaration wins, even if invalid (there is no fallback to an earlier declaration). Malformed syntax, unknown/ambiguous parameter references and misplaced directives also produce warnings rather than shader errors. Documents, preset exports and portable code preserve the binding; exports write one canonical directive after the catalog marker.

### Parameter declarations

Use `// @if _Mode == 1` or `// @if _Mode != 1` before one or more `// @param` lines, then close the block with `// @endif`, to show controls conditionally. Conditions accept only numeric values and `==`/`!=`; the referenced parameter must be an unconditional `float`, `bool` or `enum`. Nested blocks are not supported. This changes the editor UI only: hidden values remain stored and continue to affect the shader. Preset export preserves the condition blocks.

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

Use `// @header(Lighting)` before a `// @param` declaration to add a bold, non-collapsible heading above that control. Use `// @helpbox(Your hint text.)` to show an informational help box above the parameter instead. Both are UI metadata, not uniforms, and are preserved when exporting FX and brush presets. Headings and help boxes without a following parameter are ignored. Previous-name migration directives are not supported.

Built-in Color Filter, Negative, Mask, Gradient Map and HSV use `_Opacity`, without old-name aliases.

```hlsl
// @header(Lighting)
// @helpbox(Keep this value subtle to preserve the input colors.)
// @param color _LightColor = (1, 1, 1, 1)
// @param float _Intensity = 1 [0 .. ~4]
```

Use `@group` and `@endgroup` to visually contain several controls in a bordered block. An optional title appears in its header. Add `; _Parameter` to link a parameter declared unconditionally inside that group to the header. A bool is drawn as an unlabeled checkbox to the left of the title; it only edits the bool value and does not itself show or hide the group body. Use `@if` to control dependent rows and use the bool uniform in HLSL to enable or disable the effect. Supported compact values (enum, float, color, float2, float3, and float4) are drawn on the right without a separate label: the group title labels the value. Dragging the title of a float field changes its value and respects its hard/soft bounds. The linked control is omitted from the group body even when declared `hidden`; `hidden` does not prevent an explicitly linked, supported control from appearing in the header. Unlinked hidden controls remain invisible, while unsupported or multi-row controls stay in the body and do not alter the header. If all body rows are hidden by `@if`, the body collapses and the group is displayed as a header only. Groups cannot be nested; `@if` blocks may be used inside a group.

```hlsl
// @group(Tint; _EnableTint)
// @param bool _EnableTint = true
// @param color _Tint = (1, 1, 1, 1)
// @param float _TintStrength = 1 [0 .. 1]
// @endgroup
```

For example, `// @group(Quality; _Quality)` with `// @param hidden enum _Quality = 1 {Low: 0, High: 1}` puts the dropdown next to the group title without a duplicate row. Use `// @group(Advanced)` for a titled group without a linked field, or plain `// @group` for a box without a header.

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
default wins. Curve parameters also work in HLSL brush tips.

### Gradient parameters

`gradient` without a default creates an opaque black-to-white Perceptual gradient.
Declare `// @param gradient _Ramp`, optionally with two endpoint colors such as
`// @param gradient _Ramp = #FF0000FF -> #0000FF`. Each endpoint accepts `#RRGGBB` (opaque) or
`#RRGGBBAA` (RGBA), or a numeric `(r, g, b, a)` tuple. Explicit endpoint defaults use Classic,
Gamma, Clamp, Smoothness 1 and midpoint .5, matching their original HLSL export format.
The parameter remains editable after creation.
Use `_Ramp_Sample(t)` to obtain straight linear RGBA. The helper uses the gradient's **Wrap** setting: **Clamp** holds endpoint colors, **Repeat** repeats every unit, and **Mirror** alternates forward and backward every unit, including negative inputs.
Do not redeclare a sampler or reference internal `_WhimTex_` uniforms.
Colors, HDR, alpha, interpolation, smoothness and midpoints are edited in the gradient field.
Repeated declarations share a gradient value; copying an effect creates independent gradient data.

The effect lazily caches a 512×2 RGBAHalf LUT without mipmaps. Unchanged renders reuse it;
edits upload new pixels without recompiling the shader. Fixed uses Point filtering, other modes
use Bilinear. LUT sampling is an approximation: transitions finer than one LUT interval may be lost.
GPU caches are released with the material and recreated after reload. Edited keys are serialized
in the effect/document. The code default initializes new instances; applying code preserves the current edited value.
**Save Preset…** writes a two-endpoint Classic/Gamma/Clamp gradient with Smoothness 1 and
midpoint .5 as a default. Other gradients must be simplified before this endpoint-only export;
the exporter rejects them rather than silently changing their settings.
Gradient parameters also work in HLSL brush tips, with the same sampling and export rules.

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

`float2` and `float3` expose two and three raw components. `point` is a `float2` in normalized canvas UV (bottom-left `(0, 0)`, top-right `(1, 1)`) and defaults to `(0.5, 0.5)`. Coordinates and tuple defaults may lie outside the canvas. The hand button (**Edit on Canvas**) beside the numeric field activates a draggable point handle. Point dragging uses the shared screen-space snapping radius for canvas edges and enabled, visible guides (including their intersections). Ctrl, or Command on macOS, bypasses snapping without restricting coordinates. `normal` generates a normalized `float3`, defaults to `(0, 0, 1)`, and uses that direction when given a zero vector. These types accept optional tuple defaults without ranges. Live API values are arrays with the corresponding component count.

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

The stack's **Apply All** and **⋮ → Apply** bake rendered pixels; they are distinct from compiling code with **Code → Apply**. A per-FX bake consumes the inclusive prefix, preserving the remaining stack. The result is Drawing with unchanged logical Transform; serialized pixel-frame compensation prevents double-transforming the snapshot, and converted groups retain their FX coordinate frame. Painting uses the pixel frame, while Transform editing uses the logical frame. The snapshot is canvas-sized linear half-float, before layer opacity/blending/channelMapping/clipping.

Shader Processor baking reconstructs its stack-position backdrop, inheriting external input through Pass Through ancestors and starting transparent inside isolated/clipped groups. Its Normal becomes Overwrite; Drawing stores `processorSnapshot` and `processorNormalBlend` to retain premultiplied before/after opacity interpolation instead of ordinary straight-RGBA Overwrite. Other blend modes retain their ordinary behavior. Processor snapshots remain clipping boundaries so existing orphan clipping layers do not acquire a new base; explicitly enabling clipping on the Drawing opts into ordinary clipping semantics. The lower layers are unchanged, but their future edits no longer regenerate the snapshot. Flags survive repeated Apply, native clipboard, TIFF and Undo/Redo.
