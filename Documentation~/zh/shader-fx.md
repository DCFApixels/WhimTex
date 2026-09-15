---
title: "Shader FX 与处理器"
parent: "简体中文"
nav_order: 9
lang: "zh"
permalink: "/zh/shader-fx/"
translations: "en/shader-fx.md,ru/shader-fx.md,zh/shader-fx.md"
previous_page: "zh/blending.md"
next_page: "zh/preview.md"
---

# Shader FX 与处理器

当你需要内置图层无法提供的效果时，可以使用自定义着色器效果。
你可以使用现有效果并调整其参数，无需编写代码。

## 应用现有效果

1. 选择要更改的图层。
2. 在其 FX 区域中使用 **+ Preset ▾**，按类别选择效果。
3. 调整效果公开的滑块、颜色或纹理。
4. 项目中的 HLSL 效果会跟随其源文件。如果你想在文档内独立编辑它们的代码，请选择 **Embed Copy**。

每个目录效果都有自己的设置。内置的 **Color → Gain** 用于调整亮度和色调；
**Transform → UV Transform** 用于重新定位传入的图像。
添加到项目中的效果会自动可用；无需设置预设文件夹。

**+ Reference** 仍可用于手动选择资源。其设置与使用该资源的其他位置共享；**Embed** 会创建独立副本。
当存在多个效果时，它们的顺序很重要。

## 保存你自己的预设

在效果的代码编辑器中，点击 **Save HLSL Preset…**。这会创建一个 `.hlsl` 文件，
其中以当前参数值作为默认值，包括颜色和 Transform 2D。
文件名会成为预设名称。你可以保存到用户库的 **ShaderFX**
子文件夹中，或项目 **Assets** 文件夹下的任意位置。覆盖保存会保留一份 `.bak` 副本。

在 **User Settings → Presets Folder** 中设置共享库位置。其 **Brushes**
和 **ShaderFX** 子文件夹分别存放两类预设。你也可以将现有的
HLSL 预设放入 ShaderFX 或其子文件夹；重新打开 **+ Preset** 即可在 **User** 下看到它们。
用户预设会被复制到文档中；之后对其文件的更改不会改变你已经添加的效果。项目 HLSL 效果仍然跟随其源文件。

纹理默认值是引用，而不是内嵌图像。要在另一个项目中使用它们，
还需一并转移被引用的纹理资源及其 `.meta` 文件，或指定替换资源。

## 在画布上调整效果

带有 Transform 2D 参数的效果提供 **Edit on Canvas**。选择它会显示绿色边框，
然后可以移动、缩放或旋转该边框。旋转围绕其中心进行；没有轴心控制。
再次点击该按钮或切换工具即可退出此模式。按 Escape 取消当前拖拽。

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

该边框编辑的是效果，而不是图层的变换。其用途取决于具体效果：
它可能用于放置图像、更改图案的缩放，或定义一个局部区域。它并不自动成为遮罩。

## 扭曲预设

选择 **FX → + Preset → Distortion → Spherize** 或 **Twirl**。

- **Spherize / Strength：** 正值会扩张中心；负值会收缩它。零则保持图像不变。
- **Twirl / Angle：** 围绕中心扭曲；符号会反转方向。角度以边框局部半径 1 处的度数计量，并随距离增长。
- **Area / Edit on Canvas：** 移动、缩放或旋转绿色坐标框。拉伸它可使扭曲变为椭圆形。

这些效果不会在边框边缘处遮罩或淡出。它们会继续延伸到边框之外，
包括超出局部坐标 0–1。较强的设置可能会采样输入图像之外的区域；这些采样使用
输入纹理的边缘寻址。RGB 和透明通道会一起采样。

### 极坐标

**Distortion → Polar Coordinates** 包含两个效果：

- **To Polar** 将条带卷成圆形：水平方向绕中心环绕，垂直方向向外延伸。
- **From Polar** 将圆形展开为条带：从左到右覆盖一整圈，从下到上覆盖距中心的距离。

**Area / Edit on Canvas** 定位并塑造圆形：To Polar 时是输出圆形，From Polar 时是源圆形。
**Angle Offset** 以度数移动环绕的起点；零表示从中心右侧开始并逆时针延伸。
**Radial Offset** 移动起始半径：正值在 To Polar 中将图案向外移动，
或在 From Polar 中从距中心更远处开始采样。一个单位沿着坐标轴从中心延伸到边框边缘。

半径会继续延伸到边框之外而不淡出。请匹配源条带的左右边缘，
以避免圆形周围出现可见接缝。整条条带的宽度会在中心汇聚，
因此展开无法恢复在该点丢失的细节。

## 影响一个图层还是下方图像？

**Shader FX** 属于单个图层，并更改该图层的图像。

**Shader Processor** 是一个单独的图层，用于更改其下方合成后的图像。
将它放在你要处理的图层之上。
使用 **Normal** 混合并降低 Opacity 可将效果与原始图像混合；
隐藏 Processor 即可对比处理前后。

在 Pass Through 组中，Processor 也可以影响该组下方的背景。
如果效果应限制在该组内容之内，请使用隔离组。

与 [Post FX 预览](post-fx.md) 不同，两者都会被包含在保存的图像中。

## 创建你自己的效果

如果你有着色器代码，请使用 **+ Shader FX**，将其粘贴到编辑器中并点击 **Apply**。
代码和设置会随文档一起保存；不需要单独的文件。
如果代码包含错误，先前可用的版本会保持可见。

编写效果是可选的。[着色器编写参考](../ShaderFX.md)
用于创建代码和可复用库。
