---
title: "着色器参数控件"
parent: "Shader FX"
grand_parent: "简体中文"
nav_order: 1
lang: "zh"
permalink: "/zh/shader-controls/"
translations: "en/shader-controls.md,ru/shader-controls.md,zh/shader-controls.md"
---

# 着色器参数控件

用 `// @param` 在 Layer Settings 中添加字段。WhimTex 自动创建 uniform 和控件；修改声明或代码后点击 **Apply**。

## 控件类型与通道标签

`toggle(类型)`、`color(类型)`、`enum(类型)` 和 `seed(类型)` 将控件外观与 HLSL 数值类型分开。默认类型依次为 float、float4、float 和 int。Toggle/color 支持 bool、int、uint、half、float 及其 2/3/4 分量向量；enum 使用数值标量，seed 使用 int、uint 或 float。

`// @param rgba toggle(float4) _Channels = (1, 1, 1, 0)` 在一行中显示四个独立的 R/G/B/A 开关。`rgba float4` 保留标准向量字段，只更改分量标签。`color(float4)`, `color(float3)`, `color(float2)` 或 `color(float)` 分别提供 RGBA、RGB、RG、R 颜色编辑。整数颜色取整，bool 使用 0.5 阈值，不自动转换为 0–255。Float 颜色保留 HDR 和原有颜色解释。Seed 只接受整数，Random 在包含两端的范围内取值。条件可通过 `_Channels.a` 指定单个分量。

## 从数字和颜色开始

```hlsl
// @param float _Amount = 1 [0 .. 1]
// @param color _Tint = #FFAA66

float4 ApplyFX(PinnedUV uv, float4 color)
{
    return float4(lerp(color.rgb, color.rgb * _Tint.rgb, _Amount), color.a);
}
```

此效果为 RGB 染色，保持 Alpha 不变。`_Amount` 初始为 1，滑块范围为 0–1。
输入纹理、坐标空间和全部参数类型见 [HLSL 参考](../ShaderFX.md#parameter-declarations)。

## 代码中的程序化噪声

内置 [WT noise 库](../NoiseLibrary.md) 可生成噪声、细胞图案和颗粒，无需额外的 Noise
图层或显式 include。Scale 控制密度，Period 沿选定轴重复图案，分形增加细节。
White 和 Blue 颗粒使用独立的像素尺寸和 Color 设置。[图噪声节点](shader-layer.md#创建程序化图案与扭曲) 使用同一 WT 库。
独立 Noise 图层保留自己的设置和计算；相同名称与 Seed 不保证图案相同。HLSL 示例和单位见参考文档。
较大位移应通过噪声设置中的 Offset 传入，不要先与 UV 相加，以便保留微小位移和细节。
无缝噪声没有固定的密度上限。过细的图案可能出现混叠；极端设置的数值限制见参考文档。
Cellular 的 F1 和 F2 使用同一亮度比例：不启用分形和过滤时，F2 不会比 F1 暗。
Add 取两者平均值，Subtract 计算差值，Multiply 和 Divide 分别计算乘积和比值。
这些规则先作用于每个倍频层，再进行分形处理和过滤。
Smooth F1 用 Smoothness 柔化最近细胞之间的过渡；零恢复普通 F1。
Voronoise 混合细胞的随机值：Smoothness 为 0 时保留独立区域，为 1 时混合范围更宽。
细胞图案的过滤使用近似平均值，因此可能改变对比度。
噪声函数统一接收位置和公共设置，分形配置位于 `settings.fractal`，专用设置放在其后。
Cellular 和 Gabor 可通过额外的输出参数返回第一个倍频层的数据。
公共设置中的采样足迹会减弱小于像素的细节；两个足迹都为零时使用点采样。
过滤是近似计算，分形折叠仍可能改变对比度或出现混叠。旧 Gabor 脚本须将足迹移到公共设置。
Perlin、Value、Value Cubic、OpenSimplex2/2S、Gabor 和 Flow 提供解析 Gradient 和 Curl 场，
可用于定向扭曲。输出保留正负号，强度随噪声的变化率而变化，不归一化向量长度。
Flow 通过旋转梯度平滑改变图案。Phase 以周期为单位：0 和 1 对应同一图案，默认值为 0。
增大 Gabor 的 Bandwidth 会收窄包络，与载波 Frequency 分开。Spots 生成柔和亮斑：
Density 控制数量，Radius 控制范围，Softness 控制边缘，Elongation/Direction 控制形状和方向。
图中通过独立的噪声节点提供这些系列；Noise 图层控件保持不变。
WT Value Cubic 2D/3D 修正了晶格邻点采样，已有代码的图案可能因此改变。

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
// @param toggle(float) _UseExtra = false
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

同时修改 `@param` 声明和所有 HLSL 引用。原位置的重命名可保留数值和标识；
如果同时添加、删除或重排参数，请检查数值。
不支持预设别名或按旧名称自动迁移。

## 坐标与时间

`LayerToLocal(uv)` 让程序化形状跟随图层变换。详见[坐标约定](../ShaderFX.md)。

`_WhimTexTime` 提供时间（秒），`_WhimTexInstanceIndex` 提供整数实例索引，目前为 0。
直接读取即可，无需 `@param` 或重复声明。Canvas 及其依赖的图层和 FX 使用
[文档预览时间](preview.md#document-playback)。图编辑器保留独立时钟，包括图结果的 FX。
Save、普通导出和 Live Update 使用 0。索引不会自动改变 Seed，须由代码明确使用。

允许使用 Unity 时间输入（`_Time`、`_SinTime`、`_CosTime`、`_TimeParameters`、`unity_DeltaTime`），不会阻止 **Apply**。
使用它们会产生警告：WhimTex 不控制其更新，因此 Canvas、缩略图和导出结果可能不同。
需要可预测的结果时，使用 `_WhimTexTime` 或自己的数值参数代替 Unity 时钟。

通常，FX 在输入或参数变化前复用结果；时间输入会停用此缓存。
警告通过[统一的 FX 诊断](shader-fx.md#编译错误)显示，也能在 Console 和代理检查中读取。
输出规则见[技术参考](../ShaderFX.md#shader-fx-a-first-snippet-parameters-and-reusable-code)。
