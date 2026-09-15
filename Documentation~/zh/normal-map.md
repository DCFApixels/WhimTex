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

1. 在 File、Drawing 或 Noise 之上添加 Normal Map。
2. 保留 **Input → Previous**，或指定另一个 Target。
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
如果源中的光照产生了不想要的斜面，请尝试 **Light Removal**。
当你对表面满意后，将 **Output** 改回 **Normal**。

## 实用的收尾控件

- **Height Levels：** 如果浮雕太平或太强烈，请调整 Black/White Level 和 Gamma。
- **Edges → Repeat：** 用于无缝源。
- **Alpha → Opaque：** 生成实心贴图；**Source** 保留源的透明通道。
- **Flip Y：** 如果目标材质把凸起显示为凹陷，请尝试此项。

## 为材质保存

如果只想单独输出法线贴图，请保持 **Normal** 混合、不透明度拉满以及通道重排（Swizzle）不变。
避免使用颜色效果，它们会扭曲浮雕。

为 PNG/TGA/PSD 选择 **Packed Color**。将导出的 PNG 或 TGA 以
**Normal Map** 导入 Unity，不要进行灰度转换。
当你的工作流需要线性 EXR 或 Texture2D 时，选择 **Linear Data**。

旋转已完成的法线贴图图层会旋转其图像，而不是其光照方向。
要获得方向正确的浮雕，请在生成贴图之前变换源。
