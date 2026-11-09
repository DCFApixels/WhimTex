---
title: "Shader parameter controls"
parent: "Shader FX"
grand_parent: "English"
nav_order: 1
lang: "en"
permalink: "/en/shader-controls/"
translations: "en/shader-controls.md,ru/shader-controls.md,zh/shader-controls.md"
---

# Shader parameter controls

Use `// @param` to expose a value in Layer Settings. WhimTex creates the uniform and its control; click **Apply** after changing declarations or code.

## Typed controls and channel labels

Use `toggle(type)`, `color(type)`, `enum(type)` and `seed(type)` to choose the control and HLSL value independently. Defaults are float, float4, float and int respectively. Toggle/color accept bool, int, uint, half and float with one to four components; enum accepts numeric scalars; seed accepts int, uint or float.

`// @param rgba toggle(float4) _Channels = (1, 1, 1, 0)` shows four independent R/G/B/A switches on one row. `rgba float4` keeps the standard vector field and only changes its labels. `color(float4)`, `color(float3)`, `color(float2)` or `color(float)` opens the picker for RGBA, RGB, RG or R respectively. Integer colors round; bool colors use a 0.5 threshold, without a 0–255 scale conversion. Float colors retain HDR and the existing color interpretation. Seed entry is integral; Random respects the inclusive range. Use `_Channels.a` in conditions to select one component.

## Start with a value and a color

```hlsl
// @param float _Amount = 1 [0 .. 1]
// @param color _Tint = #FFAA66

float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, color.rgb * _Tint.rgb, _Amount), color.a);
}
```

This effect tints RGB without changing alpha. `_Amount` starts at 1 and its slider spans 0–1.
For input textures, coordinate spaces and all parameter types, see the [HLSL reference](../ShaderFX.md#parameter-declarations).

## Procedural noise in code

The built-in [WT noise library](../NoiseLibrary.md) creates noise, cell patterns and grain
without an extra Noise layer or explicit include. Scale controls density; Period repeats the
pattern along selected axes; fractals add detail. White and Blue grain keep their own pixel-size
and Color controls. [Graph noise nodes](shader-layer.md#build-procedural-patterns-and-distortions)
use the same WT library. The separate Noise layer retains its own controls and calculations,
so matching names and seeds need not match its pixels. See the linked reference for HLSL examples and units.
For large translations, keep Offset in the noise settings instead of adding it to UV first;
this lets the library preserve small changes and fine detail.
Periodic noise has no fixed upper density cap. Very fine patterns can alias; numerical limits
for extreme settings are described in the reference.
Cellular F1 and F2 use the same brightness scale: without fractals or filtering, F2 is never darker
than F1. Add averages them; Subtract shows their difference; Multiply and Divide combine
them as a product and ratio. These rules apply to each octave before fractal/filter processing.
Cellular Smoothness affects every scalar Output: zero keeps hard patterns, positive values
blend Cell Value neighbors or soften distance ridges. Distance to Edge uses Euclidean geometry;
the other outputs retain Metric. Filtered means are approximate, so contrast can change.
Noise calls share position and settings, with fractals in `settings.fractal`; family-specific
options follow those settings. Cellular and Gabor can also return first-octave details through
an optional output argument. Shared sampling footprints reduce unresolved fine detail;
zero keeps point sampling. Filtering is approximate, so folded fractals can still change
contrast or alias. Existing Gabor scripts must move footprint values to the common settings.
Perlin, Value, Value Cubic, OpenSimplex2/2S, Gabor and Flow provide analytic Gradient and
Curl fields for directional distortion. Their signed strength follows the rate of change;
vector length is not normalized.
Flow changes the pattern smoothly by rotating its gradients. Phase is in cycles: 0 and 1
give the same pattern, and the default is 0. Increasing Gabor Bandwidth narrows the envelope
independently of carrier Frequency. Spots creates positive soft marks; Density changes their count,
Radius their reach, Softness their edges, and Elongation/Direction their shape and orientation.
The graph exposes these families through dedicated noise nodes; Noise-layer controls are unchanged.
Existing WT Value Cubic 2D/3D code can produce a different image after the lattice-neighbor correction.

To layer a custom coded pattern at several scales, use [custom-function fractals](../NoiseLibrary.md#fractals-of-custom-functions).
FBm preserves signed and HDR values, including multi-channel patterns. Period repeats the
coordinates, but a custom pattern can still show seams; octave filtering does not prevent
all aliasing. Existing graph Fractal controls and built-in noise behavior stay the same.

## Labels and header controls

| Directive | Purpose |
| --- | --- |
| `// @param label(Tint Strength) float _Strength = 1` | Give the field a readable label. Quote labels containing parentheses. |
| `hidden` on a parameter declaration | Hide its normal body row, without changing its value or HLSL behavior. It can precede or follow `label(...)`. |
| `// @control(_Opacity)` | Put one existing parameter before **⋮** in the FX header. |
| `// @header(Lighting)` | Add a bold, non-collapsible heading before the next parameter. |
| `// @helpbox(Your hint text.)` | Add an informational hint before the next parameter. |

Place `@control` immediately after `// @whimtex-effect Category/Name`, or on the first line if there is no catalog marker.
It supports toggle, enum, float, color and float2/3/4. Without `hidden`, the header and body fields edit the same value.
Unsupported types leave the header unchanged. Repeated control directives warn and the last wins.

These directives describe the UI, not the effect: an opacity control does not add blending by itself.
Headings and hints without a following parameter are ignored. The metadata is retained in presets and exported code.

## Conditional fields and groups

Use `@if` to show options only for a selected mode:

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param toggle(float) _UseExtra = false
// @endif
```

Conditions accept numeric `==` and `!=` comparisons against an unconditional scalar or an explicit vector component.
They cannot be nested. Hidden values still exist and affect HLSL; branch in the shader too if the mode should change its calculation.
Preset export retains the conditions.

Group related fields with `// @group(Tint; _Parameter)` and `// @endgroup`.
The linked parameter must be declared unconditionally inside that group.

| Linked type | Header behavior |
| --- | --- |
| toggle | Checkbox to the left of the title. It changes the value; use `@if` to hide dependent fields. |
| enum, float, color, float2/3/4 | Compact field on the right, labeled by the group title; dragging a float title adjusts it. |
| Unsupported or multi-row type | Remains in the body; the header stays plain. |

Linked compact fields are removed from the body. `hidden` does not hide a supported linked header field; unlinked hidden fields stay invisible.
When conditions hide every body row, only the header remains.
Groups may contain `@if`, but cannot contain other groups.
Use `// @group(Advanced)` for a plain titled group, or `// @group` for an untitled border.

## Curve defaults

Declare a profile with, for example, `// @param curve _Profile = easeInOut`.

Curve input and output can be negative or greater than 1. `_Profile_Sample(t)` and
the **Sample Curve** node use all keys, including keys outside 0–1. Beyond the end
keys, **Wrap** determines whether the curve holds its endpoint, loops or reflects.

| Default | Shape |
| --- | --- |
| `linear` | Straight from (0,0) to (1,1). |
| `easeInOut` | Same endpoints, flattened at both ends. |
| `easeIn` / `easeOut` | Quadratic 0→1 curve with a slow start / finish. |
| `one` | Constant 1, with keys at time 0 and 1. |

## Renaming a parameter

Update the `@param` declaration and every HLSL reference together. An in-place edit can retain
the value and identity; verify values when also adding, removing or reordering parameters.
Preset aliases and automatic previous-name migration are not supported.

## Coordinates and time

`LayerToLocal(uv)` makes procedural shapes follow the layer transform. See the [coordinate contract](../ShaderFX.md).

`_WhimTexTime` supplies time in seconds; `_WhimTexInstanceIndex` supplies the integer instance
index, currently 0. They are built in: read them directly without `@param` or a new declaration.
Canvas and its dependent layers/FX use the document's [preview time](preview.md#document-playback).
The graph editor keeps an independent clock, including graph-result FX. Save, ordinary export
and Live Update use 0. Using the index does not change Seed unless your code does so.

Unity time inputs (`_Time`, `_SinTime`, `_CosTime`, `_TimeParameters`, `unity_DeltaTime`) are allowed and do not block **Apply**.
They produce a warning: WhimTex does not control their updates, so Canvas, thumbnails and export may differ.
For a predictable result, use `_WhimTexTime` or your own numeric parameter instead of Unity's clock.

FX normally reuse the result until an input or parameter changes; time inputs disable that caching.
The warning appears through the [shared FX diagnostics](shader-fx.md#compilation-warnings), including Console and agent inspection.
See the [technical time-input policy](../ShaderFX.md#shader-fx-a-first-snippet-parameters-and-reusable-code) for reporting rules.
