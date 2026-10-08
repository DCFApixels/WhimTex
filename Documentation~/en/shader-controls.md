---
title: "Shader parameter controls"
parent: "Shader FX and Processor"
grand_parent: "English"
nav_order: 1
lang: "en"
permalink: "/en/shader-controls/"
translations: "en/shader-controls.md,ru/shader-controls.md,zh/shader-controls.md"
---

# Shader parameter controls

Use `// @param` to expose a value in Layer Settings. WhimTex creates the uniform and its control; click **Apply** after changing declarations or code.

## Start with a value and a color

```hlsl
// @param float _Amount = 1 [0 .. 1]
// @param color _Tint = #FFAA66

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, color.rgb * _Tint.rgb, _Amount), color.a);
}
```

This effect tints RGB without changing alpha. `_Amount` starts at 1 and its slider spans 0–1.
For input textures, coordinate spaces and all parameter types, see the [HLSL reference](../ShaderFX.md#parameter-declarations).

## Labels and header controls

| Directive | Purpose |
| --- | --- |
| `// @param label(Tint Strength) float _Strength = 1` | Give the field a readable label. Quote labels containing parentheses. |
| `hidden` on a parameter declaration | Hide its normal body row, without changing its value or HLSL behavior. It can precede or follow `label(...)`. |
| `// @control(_Opacity)` | Put one existing parameter before **⋮** in the FX header. |
| `// @header(Lighting)` | Add a bold, non-collapsible heading before the next parameter. |
| `// @helpbox(Your hint text.)` | Add an informational hint before the next parameter. |

Place `@control` immediately after `// @whimtex-effect Category/Name`, or on the first line if there is no catalog marker.
It supports bool, enum, float, color and float2/3/4. Without `hidden`, the header and body fields edit the same value.
Unsupported types leave the header unchanged. Repeated control directives warn and the last wins.

These directives describe the UI, not the effect: an opacity control does not add blending by itself.
Headings and hints without a following parameter are ignored. The metadata is retained in presets and exported code.

## Conditional fields and groups

Use `@if` to show options only for a selected mode:

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

Conditions accept numeric `==` and `!=` comparisons against an unconditional float, bool or enum parameter.
They cannot be nested. Hidden values still exist and affect HLSL; branch in the shader too if the mode should change its calculation.
Preset export retains the conditions.

Group related fields with `// @group(Tint; _Parameter)` and `// @endgroup`.
The linked parameter must be declared unconditionally inside that group.

| Linked type | Header behavior |
| --- | --- |
| bool | Checkbox to the left of the title. It changes the value; use `@if` to hide dependent fields. |
| enum, float, color, float2/3/4 | Compact field on the right, labeled by the group title; dragging a float title adjusts it. |
| Unsupported or multi-row type | Remains in the body; the header stays plain. |

Linked compact fields are removed from the body. `hidden` does not hide a supported linked header field; unlinked hidden fields stay invisible.
When conditions hide every body row, only the header remains.
Groups may contain `@if`, but cannot contain other groups.
Use `// @group(Advanced)` for a plain titled group, or `// @group` for an untitled border.

## Curve defaults

Declare a profile with, for example, `// @param curve _Profile = easeInOut`.

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

FX and Shader Processor cache results when their inputs and parameters are unchanged.
Unity time inputs (`_Time`, `_SinTime`, `_CosTime`, `_TimeParameters`, `unity_DeltaTime`) are allowed and do not block **Apply**.
However, Canvas, thumbnails and export may show different results because WhimTex does not control their updates.
**Apply** warns in Diagnostics and Unity Console and disables result caching. The agent also receives the warning.
This uses the same diagnostic mechanism as other FX errors and warnings. Console repeats are
suppressed for the same source and message until scripts reload;
Diagnostics and agent warnings remain visible. For predictable behavior, use an explicit parameter instead.
