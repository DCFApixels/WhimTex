---
title: "效果图层"
parent: "简体中文"
has_children: true
nav_order: 7
lang: "zh"
description: "在 Unity 中用 WhimTex 构建纹理效果。为图层或组添加描边、SDF、法线贴图、高斯模糊和运动模糊，同时保持源可编辑。"
permalink: "/zh/effects/"
translations: "en/effects.md,ru/effects.md,zh/effects.md"
previous_page: "zh/symmetry.md"
next_page: "zh/blending.md"
---

# 效果图层

效果图层可以创建描边、柔化图像，或将纹理细节转换为表面起伏。
它们保持源可编辑，因此你无需重建效果即可调整它。

根据结果的作用位置选择：

| 类型 | 所在位置 | 修改对象 |
| --- | --- | --- |
| **Effect Layer** | Layers 中的独立行 | 所选来源，例如为隐藏的 Shape 创建 Outline。 |
| **FX** | 所选图层的 FX 区域 | 该图层的图像。预设和 HLSL 参见 [Shader FX](shader-fx.md)。 |
| **Shader Processor** | Layers 中的独立行 | 下方的合成图像。参见[作用范围](shader-fx.md)。 |
| **Post FX** | Canvas View 的预览控件 | 仅改变显示，不修改保存的像素。参见 [Post FX](post-fx.md)。 |

## 选择效果

使用 Layers 底部的 **+**：

| 目标 | 图层 | 初始设置 |
| :--- | :--- | :--- |
| 为形状描边 | Outline | 宽度、柔和度以及内侧/外侧位置。 |
| 基于到边缘的距离生成遮罩 | SDF | 源通道、Threshold 和 Max Distance。 |
| 创建表面起伏 | Normal Map | Height Map 或 Texture。参见 [Normal Map](normal-map.md)。 |
| 柔化图像 | Blur | Mode → Gaussian，然后是 Radius。 |
| 创建运动拖尾 | Blur | Mode → Linear 或 Circular。 |
| 恢复边缘清晰度 | Sharpen | Strength 和 Radius。 |
| 接合相对的纹理边缘 | Make Seamless | Method、Channels、各方法的边缘和混合设置，以及可选的 Poisson Correction。 |

## 选择效果使用的对象

| Input | 来源 |
| :--- | :--- |
| Previous | 同一组中效果正下方的图层。 |
| Specific | 在 **Target** 中指定的图层或组。 |
| All Below | 效果下方可见图层的合成结果，包含透明度、混合和 FX。 |

使用 **Specific** 时，从列表选择 Target 或将图层拖入字段。
拖动多个选中图层时使用活动图层。**None (Layer)** 表示尚未指定来源。

Previous 和 Specific 可使用隐藏来源。对于组，隐藏组本身，不要隐藏需要保留的子图层。
只读取组内容，不包含其后方背景。

**All Below** 限于当前组，即使组为 Pass Through；在顶层则使用文档下方图层。
空堆栈透明，Target 字段隐藏。
不透明背景也会让合成 Alpha 完全不透明，基于 Alpha 的 Outline/SDF 因此无法分离背景上的轮廓。
需要这些轮廓时使用 Specific 或单独的来源组。

## Outline 和 SDF

**Outline** 添加边框。**SDF** 将到轮廓的距离转为渐变，可用于遮罩、发光或高度轮廓。
两者都保持来源可编辑。

### Outline：描边与填充

选择 **Source Channel**（默认 Alpha，也可选 Red、Green、Blue 或 Luminance），
然后将描边放在边缘内侧、外侧或两侧。

| 设置 | 可见效果 |
| :--- | :--- |
| Width (px) | 描边宽度，支持小数。 |
| Softness (px) | 羽化两侧，不改变 Width。零保留清晰、抗锯齿的边缘。 |
| Offset (px) | 负值向内、正值向外移动描边，不改变宽度。 |
| Color | 描边颜色。 |
| Fill Center / Fill Color | 用独立颜色和透明度填充中心。 |

如果描边与柔和来源脱节，可尝试小幅负 Offset。
要用实心轮廓衬托绘图，将 Outline 放在下方并以 Specific 指定该绘图。
放在上方时填充会覆盖绘图。填充跟随轮廓，而非来源的柔和 Alpha；孔洞保持不变。

### SDF：距离渐变或数据

选择 **Source Channel**：Alpha、RGB 通道或 Luminance，组也支持这些选项。
**Threshold** 确定轮廓，**Max Distance (px)** 确定过渡距离。零表示自动选择距离。

| Output | 结果 |
| :--- | :--- |
| Gradient（默认） | 调色板提供 RGB、HDR 强度及 Alpha，不保留来源 Alpha。 |
| Linear Data | 原始 RGB 0–1 归一化距离，不透明 Alpha，不进行颜色伽马转换。 |

默认调色板在 0 为黑、1 为白，使用 **Perceptual** 插值。
**Inverted** 先反转归一化距离，**Profile** 随后调整其形状，再进行调色板采样。
两者适用于两种输出。线性 Profile 保持原样，切换 Output 保留调色板和反相设置。

**Position** 选择距离区域：Outside、Inside、Center（两侧）或 **Signed**（默认）。
Signed 内部值低、外部值高；Profile 前轮廓对应 0.5。
**Inside Distance** 与 **Outside Distance** 分别控制两侧范围，零继承 Max Distance 或自动范围。
**Contour Offset (px)** 为正时扩张形状，为负时收缩。

制作浮雕时使用 Linear Data 或灰度渐变，并用 Profile 调整高度。
默认调色板下，两种 Output 从内到外的亮度方向一致。

### 来源边缘与距离质量

**Source Offset (px)** 移动 SDF 来源，同时保留画布外轮廓的影响。
**Source Edges** 可选 Transparent（默认）、Clamp、Repeat（包括跨接缝距离）或 Mirror。
它无法恢复已被来源图层裁切的内容。

大偏移和 Repeat 需要更多内存。扩展计算区域超过 6400 万像素时，
请减小偏移或分辨率；WhimTex 会报错而非静默裁切。

距离算法影响拐角和对角线：

| 算法 | 结果 |
| :--- | :--- |
| Euclidean | 圆润的等距轮廓。 |
| Manhattan / Chebyshev | 更棱角分明的轮廓。 |
| Euclidean Antialiased | 考虑来源平滑、部分透明的边缘。 |
| Euclidean Exact | 硬阈值轮廓，适合像素遮罩。 |

Euclidean Antialiased 下，Outline 使用 Source Channel 的 50% 阈值，SDF 使用 Threshold。
未达到阈值的区域不会形成轮廓。

## Blur

添加 **Blur**，然后在 Properties 中选择 **Mode**：**Gaussian**、**Linear** 或 **Circular**。
只会显示相关的控件；切换模式会保留它们的设置。

### Gaussian

增大 **Radius** 可让图像更柔和。可从小值开始清理边缘；
对大而柔和的形状使用更大的半径。

**Strength (%)** 控制强度：0% 显示原始图像，100% 给出正常模糊，
最高 400% 会让半透明区域更浓密，而不改变半径或提亮颜色。
高于 100% 的值不会改变完全不透明的区域。

要一起模糊多个图层：

1. 将它们放入一个组，并在其上方添加 Blur，将 Mode 设为 Gaussian。
2. 让 Input 保持 Previous。
3. 隐藏组本身，只显示模糊结果。
4. 调整 Radius 和 Strength。

### Linear 和 Circular

选择 **Linear** 可获得直线拖尾。**Distance (px)** 设置长度，**Angle (deg)** 设置方向。
选择 **Circular** 可获得旋转拖尾，然后设置 **Center** 和 **Arc**。

**Direction** 决定拖尾相对于源的位置：围绕它、在它之前或在它之后。
**Strength** 低于 100% 会恢复更多清晰的原始图像。
高于 100% 会让半透明拖尾更浓密，但不会让它们更长。

## Sharpen

添加 **Sharpen** 可增强局部边缘对比度，同时保持源图层的 Alpha 不变。
选择 **Gaussian** 可进行常规反锐化；**Adaptive** 使用局部边缘方向一致性遮罩，优先增强连贯边缘，减少对微弱、无方向细节的锐化。
**Strength (%)** 控制强度（0–400%），**Radius (px)** 控制以画布像素为单位的比较距离。
**Threshold** 设置锐化细节的最低对比度。**Noise Reduction** 仅在 Adaptive 模式下进一步抑制不规则细节；它不会移除源图像中已有的噪声。**Halo Suppression** 限制边缘过冲。
**Channels** 可处理 RGB 或亮度；**Edges** 决定源边界外的采样方式：Transparent、Clamp、Repeat 或 Mirror。
该操作会保留 HDR 颜色值。
编辑过程中 WhimTex 使用更快的近似计算；操作结束后会重新计算更高质量的 Gaussian 加权结果。

## Make Seamless

在纹理或组上方添加 **+ → Make Seamless**，选择 **Input**；若只需要处理结果，可隐藏来源。
启用 **Tiled** 比较重复图像的边缘。

| 方法 | 连接方式 | 主要取舍 |
| --- | --- | --- |
| **Offset Blend** | 混合偏移半个周期的副本。 | 适合作为噪声和表面纹理的起点；颜色和重复细节可能改变。 |
| **Mirror** | 将对边反射到过渡条带中。 | 容易调整，但可辨认的细节可能显得镜像对称。 |
| **Screened Poisson** | 平滑校正对边的不匹配。 | 也可能改变中心和其他边缘；不负责重建连续细节。 |
| **Patch Quilting** | 从源图寻找条带，沿差异较小的路径拼接。 | 可能保留更清晰的细节，但会重复图案，平滑噪声上仍可能看到拼接线。 |

新图层使用 **Offset Blend**。Offset Blend 和 Mirror 初始为 **Blend Width 20%**、**Transition Start −25%**，并启用 **Poisson Correction → All Edges**。
所有复制边缘和两个镜像轴初始开启。切换方法保留各自设置；已有图层沿用保存值。

### 边缘与通道

**Channels → R / G / B / A** 选择接收结果的通道，初始全部开启。
关闭 A 可保留原透明度；关闭全部通道则跳过接缝处理。
后续 FX、Mapping 和图层混合仍可能改变图像。

方形控件选择当前步骤处理的边缘：

- **Copy Edges：**Offset Blend 的四条边独立选择。
- **Mirror Direction：**所选边接收对边的反射。再次点击关闭该轴，点击对边反转方向。两个轴都开启时也连接角落。
- **Patch Edges / Poisson Edges：**对边成对切换。**Top & Bottom** 垂直重复，**Left & Right** 水平重复，**All Edges** 启用两个方向。两对都可以关闭。

关联边一起高亮；点击中心反转选择。
Mirror 的中心在任一轴开启时关闭两轴，再次点击启用 Left To Right 和 Bottom To Top。
关闭某步骤会使其依赖字段变灰，但不禁用方形控件或另一处理步骤；数值保留。

**Poisson Correction 有独立的边缘选择。** 即使复制、镜像或条带匹配关闭，校正仍可运行。

### Offset Blend

| 控件 | 作用 |
| --- | --- |
| **Blend Width (%)** | 复制条带宽度：2–50%，默认 20。 |
| **Transition Start (%)** | 条带内的淡出起点，而非相对整个画布：−100…95%，默认 −25。此前副本完全生效。 |
| **Contrast Compensation / Strength (%)** | 减少混合造成的对比度损失。默认开启；Strength 0–100%，默认 100。 |

降低 Transition Start 会加宽过渡，0 从边缘开始，正值收窄过渡。
负值把过渡延伸到画布外，可能重新出现接缝。**Poisson Correction** 可减轻接缝，但改变 Transition Start 不会自动切换校正。

条带在角落重叠，最少宽两像素。关闭 Poisson Correction 时，条带外像素保持不变，仅有浮点与透明度舍入差异。
对比度补偿近似保留分布，并非完全一致。关闭它或设 Strength 为 0 会跳过直方图分析。

### Mirror

**Blend Width (%)** 设置过渡宽度。**Falloff** 越大，反射越集中于目标边缘。
镜像条带逐渐淡入原图；没有选中边缘的轴保持不变。

**Transition Start (%)** 相对 Blend Width，范围 −100…95%，默认 −25。
0 保留原淡出曲线；正值推迟并收窄过渡，负值把过渡扩展到画布外，可能重新产生接缝。
它与 Contrast Compensation 可独立使用，也独立于 Offset Blend 的同名设置。

**Contrast Compensation** 默认关闭。直方图补偿可减少镜像过渡的对比度损失，但可能改变颜色，且不能去除镜像图案。
**Strength (%)** 为 0–100；0 使用普通镜像。

### Screened Poisson 与 Poisson Correction

**Radius (%)** 设置校正范围，相对图像短边为 0.5–25%，默认 5。
校正没有硬条带边界。中心亮度仅近似保留；中心和未选择的边缘也可能改变，但只连接选中的边缘对。
亮值可能超出允许范围并被裁剪。Mirror 控件不影响此方法。

Offset Blend、Mirror 和 Patch Quilting 通过 **Poisson Correction** 提供同一种校正：

- 各自拥有独立的 **Poisson Edges**，作用范围不限于复制或匹配条带。
- Offset Blend 和 Mirror 默认开启校正，Patch Quilting 默认关闭。
- Offset Blend 和 Mirror 的 **Automatic Radius** 默认开启：Blend Width 的四分之一，最小 0.5%。Radius 显示计算值；关闭自动模式可恢复并编辑保存的手动半径。
- 校正增加计算时间；在 Tiled 中同时检查对比度与接缝。

### Patch Quilting

仅重建边缘条带，不重新生成整张纹理。

| 控件 | 作用 |
| --- | --- |
| **Patch Width (%)** | 每条处理区域的宽度：2–45%，默认 20。更宽允许更多切线位置，也会修改更多图像。 |
| **Feather (%)** | 每条切线两侧可用对称过渡的比例：0–100%，默认 50。它混合片段，不模糊纹理，也不是 Patch Width 的百分比。 |
| **Search Quality** | Draft、Normal（默认）、High。更高档增加候选数与分析分辨率，计算更慢，但不保证视觉改善。 |
| **Along-Seam Search (%)** | 沿接缝移动来源条带：0–25%，默认 0。0 不移动候选；位移在端点渐隐而不循环，细节可能拉伸。 |
| **Seed / Random** | 选择另一个可复现的匹配；不同种子可能得到相同结果。 |
| **Channel Matching** | **Linked**（默认）为所选通道共用片段与切线；**Independent** 分通道匹配，适合打包数据，不适合保留颜色和透明彩边。 |

Feather 0 保留硬切线，100 使用不会越出条带或打开接缝的最大对称过渡。
极窄条带中不同百分比可能看不出差别；只有一个输出像素时没有羽化空间，分析条带过窄时使用居中切线。
关闭 Poisson Correction 时，条带外像素保留，只有舍入差异。

**Contrast Compensation** 默认关闭。Feather 大于 0 时，它可减轻过渡中的对比度损失。
**Strength (%)** 为 0–100。校正考虑源与候选的相似性，可能改变颜色。
Strength 或 Feather 为 0 时不计算；Feather 为 0 时该控件不可用。
两轴都开启时，第二次搜索使用第一次校正后的结果，因此可能选择不同片段。

### 预览与计算

- 来源保持可编辑，修改会更新结果，不会被该图层烘焙。
- Patch Width 与 Feather 在松开滑块后应用。Quilting 计算可能暂时阻塞编辑；更高质量和更广搜索需要更多时间。
- 小尺寸预览可能选到不同片段和切线。普通预览限制为 512 像素，**Live Quality 100%** 不会解除非绘画预览的限制。导出前选择 **Pencil** 但不要绘画，并启用 **Tiled** 检查完整分辨率。

**旧文档：**Feather 保存值直接按百分比读取，不迁移（16 即 16%），外观可能改变。
打开不会自动重写文件。
后续变换与 FX 可能破坏无缝性，还应检查最终合成。

脚本与代理参见 [Make Seamless 参数参考](../AgentAPI.md#make-seamless-settings)和 [clipboard 示例](../Examples/Clipboard/seamless-noise.json)。
若要修复区域而非连接对边，使用[内容感知填充](selection.md#根据现有纹理细节填充)。

## 正确保持边缘

模糊效果有一个 **Edges** 设置：

- **Transparent：** 淡入空白区域。
- **Clamp：** 延伸边缘颜色。
- **Repeat：** 环绕；对无缝纹理很有用。
- **Mirror：** 在边界处反射图像。

Normal Map 提供 **Clamp**、**Repeat** 和 **Mirror**，没有 **Transparent**。

对于无缝工作，既要设置效果的 Edges，也要启用 [Tiled preview](symmetry.md)。
