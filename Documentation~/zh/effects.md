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

**Input → Previous** 使用同一组中效果正下方的图层。
选择 **Specific** 可指定另一个图层或组。点击 **Target** 从列表中选择，或将图层直接拖入字段。
**None (Layer)** 表示尚未选择来源。
拖动多个选中的图层时，活动图层会成为目标。

**All Below** 使用效果下方所有可见图层的合成结果，包含透明度、混合和效果。范围仅限当前组，即使该组为 Pass Through；在文档顶层则使用下方的文档图层。空堆栈为透明，此模式隐藏 Target 字段。不透明背景会使合成 Alpha 完全不透明，因此基于 Alpha 的 Outline/SDF 无法识别背景上各对象的轮廓。需要这些轮廓时请选择 Specific，或将来源放在单独的组中。

使用 Previous 或 Specific 时，你可以隐藏源并仍然看到效果。对于组，请隐藏组本身，
而不是你想包含的子级。效果只使用该组的内容，而不使用其背后的背景。

## Outline 和 SDF

使用 **Outline** 在形状周围添加边框。调整其宽度和柔和度，
选择 **Source Channel**（默认 Alpha，也可选 Red、Green、Blue 或 Luminance），
然后选择它位于边缘内侧、外侧还是跨越边缘。

当你需要基于到形状距离的渐变过渡时，使用 **SDF**。
**Output → Gradient** 是默认模式，渐变从 0 处的黑色到 1 处的白色，插值为 **Perceptual**。**Inverted** 保持可用，在 Profile 和渐变采样前反转归一化距离。**Output → Linear Data** 输出归一化的原始 RGB 0–1 值和不透明 Alpha，不进行颜色伽马转换。Inverted 和 Profile 对两种模式均生效。切换 Output 会保留渐变和反相设置。Signed 内部值较低、外部值较高；使用默认渐变时，两种输出的亮度方向相同。
**Threshold** 设置确定轮廓的阈值，**Max Distance (px, 0 = auto)** 设置过渡距离；0 表示自动选择距离。
**Position** 决定渐变覆盖轮廓的哪一侧：Outside 为外侧，Inside 为内侧，Center 为两侧。
默认的 **Signed** 覆盖轮廓两侧。在两种输出模式中，**Inverted** 在 Profile 之前反转归一化距离。

**Source Offset (px)** 沿 X/Y 移动输入，保留移出画布的轮廓对距离的影响。**Source Edges** 可选 Transparent（默认，透明）、Clamp（延伸边缘像素）、Repeat（重复并计算跨接缝距离）或 Mirror（镜像）。无法恢复已被源图层裁掉的内容。

**Contour Offset (px)** 为正时扩张轮廓，为负时收缩。在 **Signed** 模式下，**Inside Distance** 和 **Outside Distance** 分别控制内外距离；0 使用 Max Distance 或自动范围。应用 Profile 前，轮廓映射到 0.5。**Profile** 在两种模式中均调整过渡形状：位于 Inverted 之后、渐变采样之前；线性曲线保持原样。制作浮雕时，可使用 Linear Data 或灰度渐变，在此调整高度轮廓。

大偏移和 Repeat 需要更多内存。扩展计算区域超过 6400 万像素时会报错而非静默裁切，请减小偏移或分辨率。
距离算法会改变转角和对角线的特征：
Euclidean 给出圆润的距离，而 Manhattan 和 Chebyshev 给出更棱角分明的结果。

对于具有平滑、部分透明边缘的源，请在 SDF 或 Outline 中选择 **Distance Algorithm → Euclidean Antialiased**。Outline 会跟随所选 **Source Channel** 的 50% 阈值边缘，即使跨越宽泛的柔和过渡也是如此；
SDF 则使用其 **Threshold** 设置。永远达不到该阈值的区域不会形成轮廓。
对于硬阈值轮廓或像素遮罩，请保留 **Euclidean Exact**。

Outline 的 **Width (px)** 支持小数。**Softness (px) = 0** 保持清晰而平滑的边缘；
增大 **Softness (px)** 会羽化两侧，而不改变 **Width (px)**。
**Offset (px)** 会移动边框而不改变其宽度：负值向内移动，正值向外移动。
如果边框看起来与柔和的源脱节，可以尝试一个小的负偏移。

启用 **Fill Center** 可获得实心形状而不是空心边框。**Color** 设置边框颜色；
**Fill Color** 独立设置中心颜色和不透明度。要用实心轮廓衬托柔和的绘图，
请将 Outline 放在绘图下方，并将其 Input 设为该特定图层。放在绘图上方时，填充会覆盖它。
填充会跟随轮廓，而不是源原本的柔和透明通道。源中的孔洞仍保持为孔洞。
对于 SDF，**Source Channel** 可以使用 Alpha、单个 RGB 通道或 Luminance，源是组时也是如此。

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

## Sharpen

添加 **Sharpen** 可增强局部边缘对比度，同时保持源图层的 Alpha 不变。
选择 **Gaussian** 可进行常规反锐化；**Adaptive** 使用局部边缘方向一致性遮罩，优先增强连贯边缘，减少对微弱、无方向细节的锐化。
**Strength (%)** 控制强度（0–400%），**Radius (px)** 控制以画布像素为单位的比较距离。
**Threshold** 设置锐化细节的最低对比度。**Noise Reduction** 仅在 Adaptive 模式下进一步抑制不规则细节；它不会移除源图像中已有的噪声。**Halo Suppression** 限制边缘过冲。
**Channels** 可处理 RGB 或亮度；**Edges** 决定源边界外的采样方式：Transparent、Clamp、Repeat 或 Mirror。
该操作会保留 HDR 颜色值。
编辑过程中 WhimTex 使用更快的近似计算；操作结束后会重新计算更高质量的 Gaussian 加权结果。

### Linear 和 Circular

选择 **Linear** 可获得直线拖尾。**Distance (px)** 设置长度，**Angle (deg)** 设置方向。
选择 **Circular** 可获得旋转拖尾，然后设置 **Center** 和 **Arc**。

**Direction** 决定拖尾相对于源的位置：围绕它、在它之前或在它之后。
**Strength** 低于 100% 会恢复更多清晰的原始图像。
高于 100% 会让半透明拖尾更浓密，但不会让它们更长。

## Make Seamless

在纹理或组上方添加 **+ → Make Seamless**。用 **Input** 选择源，
然后如果你想只看到处理后的结果，就隐藏源本身。

**Method** 提供 **Offset Blend**（新图层的默认值）、**Mirror**、**Screened Poisson** 和 **Patch Quilting**。现有图层保留已保存的模式。
新图层的 Offset Blend 和 Mirror 均使用 **Blend Width 20%**、**Transition Start −25%**，
并启用 **Poisson Correction → All Edges**。Offset 的所有复制边和 Mirror 的两个镜像轴均启用。
现有图层保留设置，切换方法不会重置参数。

**Copy Edges**、**Mirror Direction** 和 **Patch Edges** 选择主要处理的边，**Poisson Edges** 选择独立校正的边。取消某个处理的全部边时，仅禁用其相关参数，选择器与另一处理仍可使用，参数值保留。Quilting 在 **Feather 0%** 时无法使用对比度补偿；增大 Feather 即可恢复。

相连的对边会在悬停时一起高亮。点击中央图标可反转选择。
**Mirror Direction** 的中央按钮在任一轴开启时关闭两轴；再次点击按 Left To Right 和 Bottom To Top 开启两轴。

**Contrast Compensation** 会增加计算量。长时间闲置或尺寸变化后的首次计算可能比后续更新慢。

**Channels — R / G / B / A** 选择参与接缝处理的通道，适用于所有模式及 Mirror 的可选补偿与校正。
默认四个通道都启用。未勾选通道保留输入值；关闭 **A** 可保留原始透明度，全部关闭则跳过接缝处理。
后续图层 FX、Swizzle 和混合仍可改变最终外观。

**Screened Poisson** 全局平滑所选的成对边缘。**Poisson Edges** 提供 **All Edges**（默认）、
**Top & Bottom**（垂直平铺）和 **Left & Right**（水平平铺）。
点击方形的边可同时切换一对相对边。可以取消全部边以跳过对应处理，选择器仍可操作。Patch Quilting 和所有 Poisson Correction 的成对选择器均支持此行为，各自独立。
**Radius (%)**（0.5–25，默认 5）设置相对较短边的校正距离，没有硬性条带边界。
内部亮度仅近似保持，未选边缘也可能变化，但不会被接合。不使用 Mirror 控件，图层随源内容更新。
高光仍可能超出输出范围并被截断，细节连续性无法保证。请检查 **Tiled** 预览。
不会迁移文档或自动重写文件。

**Offset Blend** 混合偏移半个周期的副本，不使用镜像。
**Blend Width (%)**（2–50，默认 20）控制边带宽度。
**Transition Start (%)**（−100…95，新图层默认 −25）指定混合在边带内的起点，而非相对整个画布。
起点之前完全使用副本。较小值扩大平滑过渡，0 从边缘开始；较大值缩短过渡。
负值把起点放到画布外，可能重新出现接缝。必要时开启 **Poisson Correction** 并选择相应的边缘对。新图层默认开启校正，但修改 Transition Start 不会切换此开关。
可选校正：

- **Contrast Compensation** 默认开启；**Strength (%)**（0–100，默认 100）控制补偿强度。
  关闭或设为零可跳过直方图分析。
- **Poisson Correction** 默认开启，增加全局 Screened Poisson 求解，其 **Poisson Edges** 独立于复制边缘。
  校正可能改变混合条带之外的像素。
  **Automatic Radius** 默认开启，使用 Blend Width 的四分之一，最小 0.5%。
  关闭后可手动设置 **Radius (%)**（0.5–25），相对于较短边。
  此步骤增加计算成本，且可能超出颜色范围。

重复图案和颜色可能变化，尤其是非噪声图像。
分布保持只是近似，请检查 **Tiled**。图层随源内容更新，不会烘焙结果。

Offset Blend 可独立点击图标四边来选择复制条带，默认全部启用。Poisson Correction 关闭时，
条带外像素保持不变（浮点及透明度舍入除外）。条带最小为两像素，在角部重叠。
关闭全部复制边缘仅跳过复制，已启用的 Poisson Correction 仍会运行。

**Patch Quilting** 从源图像搜索平移条带，并沿误差较小的路径拼接，仅修补边界而不重新合成整张纹理。
它可以保留清晰细节，但也可能重复图案，或在柔和噪声中产生可见拼接线。请检查 **Tiled** 预览。

- **Patch Edges：** All Edges、Top & Bottom 或 Left & Right；方形图标的相对边一起切换。
- **Patch Width (%)**（2–45，默认 20）：每侧修补条带的宽度。加宽可提供更多切割空间，但改变更多像素。
- **Feather (%)**（0–100，默认 50）：分别使用每条切割路径可用过渡宽度的百分比。0 为硬切割；100 使用不越过条带边界、不重新暴露拼接缝的最宽对称过渡。这是条带混合，不是纹理模糊，也不是 Patch Width 的百分比。
- **Contrast Compensation**（默认关闭）减少 Feather 过渡中的对比度损失。**Strength (%)** 控制强度（0–100），并考虑原图与所选区块的相似程度；颜色可能改变。启用后会增加计算时间，但强度或 Feather 为 0% 时不计算补偿。处理两组边缘时，第二次处理使用第一次补偿后的结果，可能选择不同区块。
- **Search Quality：** Draft、Normal（默认）、High。较高质量搜索更多候选并提高分析分辨率，计算更慢，不保证视觉效果更好。
- **Along-Seam Search (%)：** 0–25%，默认 0（原有搜索）。沿接缝移动供体条带，百分比以可用条带长度为基准。位移在两端平滑归零，不循环回绕，但可能拉伸细节。非零值将原有候选数量分配给未移动和移动条带，计算更慢，不保证视觉改善。
- **Seed / Random：** 尝试另一种可重复的供体选择；某些种子可能产生相同结果。
- **Channel Matching：** Linked（默认）让选中颜色通道共用供体和路径；Independent 分别搜索每个选中通道，适合打包遮罩/噪声，不适合保持颜色关系或透明彩色边缘。
- **Poisson Correction**（默认关闭）：可选全局校正，具有独立的 **Poisson Edges** 和 **Radius (%)**（0.5–25，默认 5），可能改变中心、对比度及 HDR 范围。

关闭校正时，条带外像素除浮点舍入外保持原样。Patch Width 和 Feather 在松开滑块后更新。
分析图中的窄条带使用居中切割，Feather 仍然生效。仅一个输出像素宽的条带没有平滑空间；输出像素很少时，不同百分比可能看起来相同。
旧 Feather 数值不迁移，直接作为百分比读取：保存的 16 变为 16%，因此旧效果可能改变。低分辨率预览可能选择不同片段和路径。普通预览限制为 512 像素；**Live Quality 100%** 不会将非绘画预览切换为完整分辨率。选择 **Pencil**（无需绘画）并启用 **Tiled**，在保存或导出前检查完整分辨率结果。
搜索已加速而不降低选定的 Search Quality。调整 Poisson Correction 不会重新搜索供体；更改源图像或 Quilting 参数仍需重新搜索。
High 搜索还会避免重复计算相同候选条带，同时保持相同 seed 的结果不变。宽条带可能受益更多；重新计算仍会阻塞编辑器。

在 **Mirror** 模式下，点击方形图像控件的一条边，选择对侧边缘镜像复制的目标。
再次点击高亮的那条边会关闭该轴；选择其对侧则会切换方向。
没有高亮边缘的轴保持不变。复制的条带会被镜像并淡入原始图像；
两个轴都启用时，角也会被接合。

**Blend Width (%)** 会加宽过渡。**Falloff** 控制其形状：较高的值会让
反射更接近目标边缘。启用 **Tiled** 可在调整时检查接合处。
**Transition Start (%)**（−100…95，新图层默认 −25）设置镜像在 Blend Width 内开始淡出的地点。
0 保留原有过渡；正值延迟并缩短过渡，负值将过渡扩展到画布外，可能重新出现接缝。
必要时启用 **Poisson Correction**。开启或关闭 Contrast Compensation 时均有效，与 Offset Blend 的设置独立。

通过脚本或 AI 设置图层时，请参阅 [Make Seamless 参数参考](../AgentAPI.md#make-seamless-settings)
和[完整剪贴板示例](../Examples/Clipboard/seamless-noise.json)。

Mirror 有两个独立选项：新图层默认关闭 Contrast Compensation、开启 Poisson Correction：

- **Contrast Compensation** 使用直方图补偿混合反射像素。**Strength (%)**（0–100）控制强度，
  0 保留普通 Mirror。可减少过渡区的对比度损失，但可能改变颜色，不会消除镜像图案。
- **Poisson Correction** 执行全局 Screened Poisson 校正，带有独立的 **Poisson Edges** 选择。
  **Automatic Radius** 默认开启，使用 **Blend Width** 的四分之一，最小 0.5%。Radius 以只读方式显示计算值。
  关闭 Automatic Radius 即可恢复并编辑已保存的手动半径。
  **Radius (%)**（0.5–25，默认 5）相对较短边控制校正距离，不受 **Blend Width** 限制。
  内部和其他边缘可能变化；两个镜像方向都 Off 时仍运行。高光可能被截断，计算成本明显增加。

这对噪声和表面纹理很有用，但可识别的形状在接合处附近可能看起来被镜像了。
进一步的变换或效果可能会改变匹配的边缘，因此也要检查最终的平铺结果。
如果需要用周围细节修复一块区域，而不是接合相对边缘，请使用[内容识别填充](selection.md#根据现有纹理细节填充)。

## 正确保持边缘

模糊效果有一个 **Edges** 设置：

- **Transparent：** 淡入空白区域。
- **Clamp：** 延伸边缘颜色。
- **Repeat：** 环绕；对无缝纹理很有用。
- **Mirror：** 在边界处反射图像。

Normal Map 提供 **Clamp**、**Repeat** 和 **Mirror**，没有 **Transparent**。

对于无缝工作，既要设置效果的 Edges，也要启用 [Tiled preview](symmetry.md)。
