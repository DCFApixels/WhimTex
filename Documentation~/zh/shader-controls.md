---
title: "着色器参数控件"
parent: "Shader FX 与处理器"
grand_parent: "简体中文"
nav_order: 1
lang: "zh"
permalink: "/zh/shader-controls/"
translations: "en/shader-controls.md,ru/shader-controls.md,zh/shader-controls.md"
---

# 着色器参数控件

用 `// @param` 在 Layer Settings 中添加字段。WhimTex 自动创建 uniform 和控件；修改声明或代码后点击 **Apply**。

## 从数字和颜色开始

```hlsl
// @param float _Amount = 1 [0 .. 1]
// @param color _Tint = #FFAA66

float4 ApplyFX(float2 uv, float4 color)
{
    return float4(lerp(color.rgb, color.rgb * _Tint.rgb, _Amount), color.a);
}
```

此效果为 RGB 染色，保持 Alpha 不变。`_Amount` 初始为 1，滑块范围为 0–1。
输入纹理、坐标空间和全部参数类型见 [HLSL 参考](../ShaderFX.md#parameter-declarations)。

## 标签与标题栏控件

| 指令 | 用途 |
| --- | --- |
| `// @param label(Tint Strength) float _Strength = 1` | 设置易读的标签。包含括号时使用引号。 |
| 参数声明中的 `hidden` | 隐藏普通字段行，不改变数值或 HLSL 行为；可放在 `label(...)` 前或后。 |
| `// @control(_Opacity)` | 在 FX 标题栏 **⋮** 前显示一个已有参数。 |
| `// @header(Lighting)` | 在下一个参数前添加不可折叠的加粗标题。 |
| `// @helpbox(提示文字。)` | 在下一个参数前添加信息提示。 |

将 `@control` 放在 `// @whimtex-effect Category/Name` 后紧接的一行；没有目录标记时放在第一行。
支持 bool、enum、float、color 和 float2/3/4。不使用 `hidden` 时，标题与正文控件修改同一数值。
不支持的类型不会改变标题。重复指令会发出警告，以最后一个为准。

这些指令只描述界面：透明度控件不会自动添加混合。
没有后续参数的标题和提示会被忽略。预设和导出代码会保留这些元数据。

## 条件与分组

使用 `@if` 仅显示当前模式需要的选项：

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

条件支持数值 `==` 和 `!=` 比较，引用的 float、bool 或 enum 必须无条件声明。
条件不可嵌套。隐藏的值仍存在并影响 HLSL；若模式应改变计算，也要在着色器内分支。
预设导出保留条件。

用 `// @group(Tint; _Parameter)` 和 `// @endgroup` 组织相关字段。
关联参数必须在该组内无条件声明。

| 参数类型 | 标题栏行为 |
| --- | --- |
| bool | 标题左侧复选框，只改变数值；用 `@if` 隐藏依赖字段。 |
| enum、float、color、float2/3/4 | 标题右侧紧凑字段，以组名为标签；拖动 float 标题可调整数值。 |
| 不支持或多行类型 | 留在正文，标题保持普通样式。 |

关联的紧凑字段不再出现在正文中。`hidden` 不隐藏受支持的关联标题控件；未关联的隐藏字段仍不可见。
条件隐藏所有正文行后，只保留标题。
组内可以有 `@if`，但不能嵌套其他组。
`// @group(Advanced)` 创建普通标题组，`// @group` 创建无标题边框。

## 曲线默认值

例如，`// @param curve _Profile = easeInOut` 创建一个曲线字段。

| 默认值 | 形状 |
| --- | --- |
| `linear` | 从 (0,0) 到 (1,1) 的直线。 |
| `easeInOut` | 相同端点，两端平滑趋于水平。 |
| `easeIn` / `easeOut` | 0→1 二次曲线，缓慢起步 / 结束。 |
| `one` | 恒为 1，关键点时间为 0 和 1。 |

## 重命名参数

将 `// @formerlyserializedas(_OldName)` 放在新的 `// @param` 声明紧前面。
Apply 会转移兼容的已保存数值及参数标识。多个旧名称可重复声明；HLSL 中使用新的 uniform 名称。
预设导出保留这些别名。

## 坐标与时间

`LayerToLocal(uv)` 让程序化形状跟随图层变换。详见[坐标约定](../ShaderFX.md)。

输入和参数不变时，FX 与 Shader Processor 缓存结果。
依赖时间的效果应使用显式参数。Unity 时间输入（`_Time`、`_SinTime`、`_CosTime`、`_TimeParameters`、`unity_DeltaTime`）不受支持：
虽然允许声明，但 **Apply** 会警告并为该结果停用缓存。
