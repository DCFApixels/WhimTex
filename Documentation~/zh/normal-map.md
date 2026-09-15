---
title: "法线贴图"
parent: "效果图层"
grand_parent: "简体中文"
nav_order: 1
lang: "zh"
permalink: "/zh/normal-map/"
translations: "en/normal-map.md,ru/normal-map.md,zh/normal-map.md"
---

# 法线贴图

法线贴图让表面在材质中受光照时看起来凸起或凹陷。
先从 **Simple** 开始使用常用控件；需要更精细的调整时再打开 **Advanced**。
返回 Simple 会保留你的高级设置。

## 从高度贴图生成

1. 在 **File**、**Drawing Layer** 或 **Noise** 之上添加 Normal Map。
2. 保留 **Input → Previous**，或指定另一个 **Target**。这些字段的用法见[效果来源](effects.md#选择效果使用的对象)。
3. 选择 **Generation → Height Map** 以及包含高度的通道。
4. 先用 **Strength** 4 和 **Smoothing** 1 px，然后调整浮雕效果。
5. 如果只想看到法线贴图，请隐藏源图层。

明亮区域为高，暗色区域为低。**Invert Height** 会将其反转。
**Strength** 让浮雕更强；**Smoothing** 柔化细小的凸起。
对于使用 Linear Data 编码的 Noise 源，请在 Advanced 中选择 **Input Space → Linear**。

## 从颜色纹理生成

选择 **Generation → Texture** 可从普通图像估算浮雕。
这只是一个起点，并非精确重建：阴影或绘制出的颜色变化可能看起来像凹陷。

在 Advanced 中，选择 **Output → Height** 以检查推断出的高度。
调整 **Fine / Medium / Large Detail** 以对应小纹理、中等特征和宽大形状。
**Light Removal** 默认为 0.75，可减弱大范围的亮度变化。
它有助于消除源图光照带来的多余斜面，但也可能去掉真实的大尺度起伏。
如果大块形状失去了体积感，请降低此值或设为零。
当你对表面满意后，将 **Output** 改回 **Normal**。

## 实用的收尾控件

- **Height Levels**（仅在 **Advanced** 中）：如果起伏太平或太强烈，请调整 Black/White Level 和 Gamma。
- **Edges → Repeat：** 用于无缝源。
- **Alpha → Opaque：** 生成实心贴图；**Source** 保留源的透明通道。
- **Flip Y：** 如果目标材质把凸起显示为凹陷，请尝试此项。

## 为材质保存

单独输出法线贴图时，请保持 **Normal** 混合、完全不透明和原始 [Swizzle](color.md)，不要重排通道。
避免使用颜色效果，它们会扭曲浮雕。

为 PNG/TGA/PSD 选择 **Advanced → Encoding → Packed Color**。将导出的 PNG 或 TGA 以
**Normal Map** 导入 Unity，不要进行灰度转换。
当你的工作流需要线性 EXR 或 Texture2D 时，选择 **Encoding → Linear Data**。

旋转已完成的 Normal Map 图层只会旋转图像；起伏的受光方向仍像旋转前一样。
要获得方向正确的浮雕，请在生成贴图之前变换源。
