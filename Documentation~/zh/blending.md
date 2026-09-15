---
title: "混合与剪贴蒙版"
parent: "简体中文"
nav_order: 8
lang: "zh"
permalink: "/zh/blending/"
translations: "en/blending.md,ru/blending.md,zh/blending.md"
previous_page: "zh/effects.md"
next_page: "zh/shader-fx.md"
---

# 混合与剪贴蒙版

使用图层行中的 **Blend** 可更改其颜色与下方图像的混合方式。
降低 **Opacity** 可使结果不那么明显。

## 选择混合模式

先从这些常用选项开始：

| 混合 | 典型用途 |
| :--- | :--- |
| Normal | 根据图层的透明度叠加到下方图像上。 |
| Multiply | 添加阴影或使纹理变暗。 |
| Screen | 提亮图像或添加柔和光晕。 |
| Add | 添加明亮的高光和发光细节。 |
| Overlay / Soft Light | **Overlay** 显著增强对比度；**Soft Light** 柔和地添加色调和光感，同时保留细节。 |
| Darken / Lighten | 保留两个图层中较暗或较亮的部分。 |
| Difference | 比较两个图像或创建对比图案。 |

**Overwrite** 完全替换下方图像，包括透明像素：图层的空白区域也会擦除下方图像。
**None** 则保持不变。
**Subtract** 对颜色值做减法，**Divide** 做除法，**Color Dodge** 提亮图像，**Linear Light** 产生强烈对比。

对于组，**Pass Through** 让组内的内容与组外的图层混合。
选择其他模式可将该组视为一个图像。

## 让绘制保持在另一个图层的形状内

剪贴蒙版非常适合为角色上色、为文字添加纹理或为剪影着色：

1. 将形状图层放在你要剪贴的图层下方。
2. 打开上方图层的 **⋮** 菜单并启用 **Clipping Mask**。
3. 在上方图层上绘制或添加效果：它们会保持在该基础形状内部。

你也可以按住 `Alt` 并点击基础图层上方的边界来切换剪贴蒙版。
多个连续的剪贴图层可以共用同一个基础图层。请将它们与基础图层放在同一组或列表顶层；剪贴蒙版不会在组外寻找来源。
如果基础图层被隐藏或为空，其剪贴图层也会消失。
如果下方没有基础图层，剪贴图层不会显示，提示中会出现 **Clipping Mask: no base below this layer**。
