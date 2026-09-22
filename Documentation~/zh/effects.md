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
| 接合相对的纹理边缘 | Make Seamless | 源边缘、Fade Width 和 Falloff。 |

## 选择效果使用的对象

**Input → Previous** 使用同一组中效果正下方的图层。
选择 **Specific** 可指定另一个图层或组。点击 **Target** 从列表中选择，或将图层直接拖入字段。
**None (Layer)** 表示尚未选择来源。
拖动多个选中的图层时，活动图层会成为目标。

你可以隐藏源并仍然看到效果。对于组，请隐藏组本身，
而不是你想包含的子级。效果只使用该组的内容，而不使用其背后的背景。

## Outline 和 SDF

使用 **Outline** 在形状周围添加边框。调整其宽度和柔和度，
然后选择它位于边缘内侧、外侧还是跨越边缘。

当你需要基于到形状距离的渐变过渡时，使用 **SDF**。
**Threshold** 设置确定轮廓的阈值，**Max Distance (px, 0 = auto)** 设置过渡距离；0 表示自动选择距离。
**Position** 决定渐变覆盖轮廓的哪一侧：Outside 为外侧，Inside 为内侧，Center 为两侧。
默认的 **Signed** 覆盖轮廓两侧。**Inverted** 反转渐变方向。

**Source Offset (px)** 沿 X/Y 移动输入，保留移出画布的轮廓对距离的影响。**Source Edges** 可选 Transparent（默认，透明）、Clamp（延伸边缘像素）、Repeat（重复并计算跨接缝距离）或 Mirror（镜像）。无法恢复已被源图层裁掉的内容。

**Contour Offset (px)** 为正时扩张轮廓，为负时收缩。在 **Signed** 模式下，**Inside Distance** 和 **Outside Distance** 分别控制内外距离；0 使用 Max Distance 或自动范围。轮廓始终映射到渐变中点。**Profile** 在 Inverted 之后、渐变着色之前调整过渡形状；线性曲线保持原样。制作浮雕时，可从灰度渐变开始，在此调整高度轮廓。

大偏移和 Repeat 需要更多内存。扩展计算区域超过 6400 万像素时会报错而非静默裁切，请减小偏移或分辨率。
距离算法会改变转角和对角线的特征：
Euclidean 给出圆润的距离，而 Manhattan 和 Chebyshev 给出更棱角分明的结果。

对于具有平滑、部分透明边缘的源，请在 SDF 或 Outline 中选择 **Distance Algorithm → Euclidean Antialiased**。Outline 会跟随 50% 透明通道边缘，即使跨越宽泛的柔和过渡也是如此；
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
选择 **Gaussian** 可获得平滑锐化，选择 **Adaptive** 可减少强边缘附近的光晕。
**Strength (%)** 控制强度（0–400%），**Radius (px)** 控制以画布像素为单位的比较距离。
**Threshold**、**Noise Reduction** 和 **Halo Suppression** 控制细节与边缘伪影。
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

用 **Horizontal** 和 **Vertical** 选择将哪条边复制到其对侧边缘。
你也可以点击字段上方图像图标的一条边来选择目标。
再次点击高亮的那条边会关闭该轴；选择其对侧则会切换方向。
将任一轴设为 **Off** 可使其保持不变。复制的条带会被镜像并淡入原始图像；
两个轴都启用时，角也会被接合。

**Fade Width (%)** 会加宽过渡。**Falloff** 控制其形状：较高的值会让
反射更接近目标边缘。启用 **Tiled** 可在调整时检查接合处。
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
