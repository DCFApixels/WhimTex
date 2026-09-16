---
title: "变换与栅格化"
parent: "简体中文"
nav_order: 3
lang: "zh"
permalink: "/zh/transform/"
translations: "en/transform.md,ru/transform.md,zh/transform.md"
previous_page: "zh/layers.md"
next_page: "zh/painting.md"
---

# 变换与栅格化

选择 **Transform**（`T`）来移动、缩放或旋转图层。
拖动边框可移动它，拖动边或角可缩放，拖动圆形控制柄可旋转。
如需精确数值，请在 Layer Settings 中展开 **Transform**。

**Original Aspect** 会恢复图像的原始比例。

Transform 工具栏中的 **Original Size** 还会恢复其像素尺寸：一个源像素等于一个画布像素。
它会保留图像中心、旋转、轴心和翻转。生成的图层使用画布尺寸。
**Reset** 会将变换恢复到其初始状态。

## 位置与轴心

金色的轴心是图层围绕其旋转和缩放的点。
拖动它到新位置，而不会移动图像。

轴心和变换会吸附到附近的参考点。按住 `Ctrl` 可自由移动。
按住 `Shift` 可沿单轴移动、按比例缩放或以 15° 步进旋转。
`Escape` 取消拖动；`T` 或 `Enter` 退出该工具。

## 斜切与透视

使用 **Transform** 时，按住以下修饰键拖动控制柄：

- `Ctrl` / `Cmd` + 角点：独立移动该角。
- `Ctrl` / `Cmd` + 边：沿该边斜切。
- `Ctrl` / `Cmd` + `Shift` + 角点：沿画布的一个轴移动。
- `Ctrl` / `Cmd` + `Alt` / `Option` + `Shift` + 角点：让成对角点反向移动，调整透视。
- `Alt` / `Option` + 普通缩放：围绕轴心缩放。

不允许角点交叉或使图层塌缩。**Reset** 清除斜切和透视。
**Original Aspect** 和 **Original Size** 需要普通变换，请先重置已变形的图层。
Position、Rotation 和 Scale 数值控件会保留现有变形；Rotation 和 Scale 表示轴心处的局部坐标轴。
即使 Drawing 图层已变形，绘画时的笔刷印记仍保持画布空间中的形状和大小。

## 重复图像或保持锐利边缘

**Tiling** 控制图像原始边界之外显示的内容：

- **Source** 跟随源纹理的设置。
- **Clip** 让外侧保持透明。
- **Repeat** 平铺图像。
- **Mirror** 交替使用镜像副本。
- **Clamp** 将最外侧的像素延伸到图像边界之外。
- **Unbounded** 会在画面之外继续计算 Noise、Gradient、Color Fill 和 Shape。
  File、Drawing 和栅格效果则改用 Clip。渐变色仍遵循其色标；
  这不会在第一个或最后一个色标之外外推新的颜色。

**Filter** 控制边缘的平滑程度。选择 **Point** 用于像素画，或选择 **Bilinear** 用于平滑缩放。
**Source** 使用源纹理的过滤方式；**Trilinear** 还会平滑 mip 层级之间的过渡（如果存在）。

如果你想改为跨越画布边缘绘制，请使用[平铺预览](symmetry.md)。

## 合并图层或将它们转换为 Drawing

使用行菜单中的 **Merge** 或 `Ctrl+E`，可将所选图层替换为一个 Drawing 图层。
`Ctrl+Alt+E` 会创建合并后的副本并保留原始图层。
合并结果包含可见图层、它们的变换和效果。

只合并图像的一部分可能会改变它与其余图层的混合方式。
如果你想进行对比，请先创建副本。

**Convert to Drawing** 让图层变得可绘制。它也可用于已有的 Drawing 图层：

| 模式 | 会发生什么 |
| :--- | :--- |
| **Keep Transform** | 将源转换为像素并保留其变换。 |
| **Apply Transform** | 在像素中保留其当前外观并重置变换。 |

转换组会合并其可见内容。针对该组内部单个图层的效果
将需要新的目标。
