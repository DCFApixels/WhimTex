---
title: "游戏后处理"
parent: "简体中文"
nav_order: 12
lang: "zh"
permalink: "/zh/post-fx/"
translations: "en/post-fx.md,ru/post-fx.md,zh/post-fx.md"
previous_page: "zh/color.md"
next_page: "zh/saving.md"
---

# 游戏后处理

使用 **Post FX** 可以查看图像在游戏的颜色分级、泛光以及其他效果下的样子。
它只改变预览，不会改变你保存的纹理或导出结果。
此功能目前需要 **URP 17.x with Universal Renderer**；其他编辑工具无需它也能工作。

## 选择外观

在页脚中启用 **Post FX**，然后用画布右侧的小箭头打开浮动设置面板。
再次点击同一个箭头即可关闭它，以便无遮挡地查看图像。

| 来源 | 用它来…… |
| :--- | :--- |
| Scene View | 使用当前 Scene View 的后处理效果进行预览。请确保在该视图中也已启用它们。 |
| Game Camera | 使用所选摄像机的设置。该字段留空则使用主摄像机。 |
| Profile | 试用某个 Volume Profile，并自行调整预览摄像机的设置。 |

原有的摄像机和配置文件不会被更改。

## 设置背景

选择 **Solid Color** 或 **Checkerboard**。
背景会在后处理之前填充透明区域，因此发光及类似效果
能有可见的衬托。你可以在这里或在 **User Settings…** 中调整它的外观。
在 **User Settings → Post FX Preview** 中，**Background Mode** 和 **Background** 与
此面板共用相同的设置，并应用到所有 WhimTex 窗口。棋盘格的颜色和
单元格大小位于 **Transparency Checkerboard** 下。你的选择会在会话之间保留。

## 试用基于深度的效果

**Depth** 控制图像作为表面时的表现：

- **Solid：** 平坦的表面。
- **Alpha Height：** 不透明区域被抬高，并沿着柔和的透明边缘逐渐过渡。
- **Alpha Mask：** 使用 **Threshold** 把形状与空白区域分开，没有渐变过渡。

使用 **Link to Zoom** 可在缩放时改变模拟的观察距离。
启用 **Animate** 可获得随时间变化的效果。

## 如果它与游戏中的表现不同

Post FX 预览的是图像本身，而不是整个场景。场景光照和周围物体不会被包含在内。
Full Screen Pass 和 SSAO 等功能可以工作，但并非每个游戏效果都受支持。
如果所选的设置不受支持，编辑器会显示原始预览并给出提示。

请先检查摄像机/配置文件的选择以及游戏的后处理开关。
若要添加会成为已保存图像一部分的效果，请使用 [Shader FX 或 Processor](shader-fx.md)。
