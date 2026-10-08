---
title: "对称与无缝绘制"
parent: "简体中文"
nav_order: 6
lang: "zh"
description: "在 Unity 中用 WhimTex 绘制无缝纹理与重复图案。使用平铺预览、环绕笔触、镜像和径向对称。"
permalink: "/zh/symmetry/"
translations: "en/symmetry.md,ru/symmetry.md,zh/symmetry.md"
previous_page: "zh/selection.md"
next_page: "zh/effects.md"
---

# 对称与无缝绘制

使用对称来绘制相互匹配的细节、装饰或重复的形状。
使用 Tiled 视图来绘制边缘能够拼接的纹理。

## 重复笔触

选择一个 Drawing 图层，并在 Layer Settings 中打开 **Symmetry & Repeat**。
每个 Drawing 图层都可以有自己的设置。

| 模式 | 用途 |
| :--- | :--- |
| Mirror | **Mirror X** 和 **Mirror Y** 是反射开关，不是轴线。X 沿经过 **Center** 的垂直线反射，Y 沿水平线反射。**Angle** 旋转这两条线。 |
| Horizontal / Vertical | 一行或一列副本。 |
| Grid | 按行和列排列的副本。 |
| Radial | 围绕 Center 排列的副本。选择扇区数量并旋转 Start Angle 来放置它们。 |
| None | 不带副本的普通绘制。 |

**Count** 设置副本数量（2–64）；Grid 用 **Count X** 设置列数、**Count Y** 设置行数。

在重复模式下，选择 **Copy** 获得完全相同的副本，或选择 **Alternate Mirror**
来反射每第二个副本。

## 让笔触留在分段内

**Edges → Clip** 会让每个笔触留在你开始绘制它的分段内。
当你希望笔触延伸到相邻分段时，选择 **Continue**。

## 绘制无缝边缘

1. 在画布视图底栏启用 **Tiled**。
2. 选择画笔或铅笔，并在任意可见副本上绘制。
3. 跨过边界绘制：被裁剪的部分会在对侧边缘继续。
4. 缩小视图以检查重复的图案。

Tiled 显示重复图像并让笔触跨越边缘，但不会扩大画布。图层变换和导出尺寸保持不变。
效果的 **Edges** 设置见[效果图层](effects.md)。

## 我需要哪种重复设置？

| 设置 | 重复的内容 |
| :--- | :--- |
| Drawing：**Symmetry & Repeat** | 该图层中的新笔触。 |
| 图层：**Transform → Tiling** | 超出图层边界的已有图像。 |
| Canvas View：**Tiled** | 整个画布，用于检查和跨边缘绘制。已有接缝不会自动消失。 |
| Noise：**Seamless** | 让噪声在相对边缘处衔接。见[噪声](noise.md)。 |
| Effect Layer：**Edges / Source Edges → Repeat** | 跨越边界读取输入图像，例如用于模糊。 |
| Distortion FX：**Input Tiling / Tiling** | 扭曲后的坐标超出边界时读取的输入图像。见 [FX](shader-fx.md)。 |
| Displacement Map：**Map Wrap** | 位移贴图，与输入图像分开设置。 |
| 渐变：**Wrap** | 超出 0–1 范围的调色板，不是画布图像。 |

绘制无缝纹理时，可先创建启用 **Seamless** 的 Noise，在上方添加 Drawing 图层，
再开启 **Tiled** 跨边缘绘制。Noise 仍可编辑。
若要直接修改噪声的像素，先[转换为 Drawing](transform.md)。
