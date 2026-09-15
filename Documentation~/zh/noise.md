---
title: "程序化噪声"
parent: "图层与组"
grand_parent: "简体中文"
nav_order: 1
lang: "zh"
description: "在 Unity 中用 WhimTex 生成程序化噪声纹理。调整 Perlin、OpenSimplex2、Cellular、分形和域扭曲，用于云层、VFX 和遮罩。"
permalink: "/zh/noise/"
translations: "en/noise.md,ru/noise.md,zh/noise.md"
---

# 程序化噪声

使用噪声来制作云层、颗粒、类似石头的图案，或作为高度贴图的起点。
从图层菜单添加 **Noise**，并在观察图像的同时调整设置。

## 从图案开始

| 设置 | 可以尝试什么 |
| :--- | :--- |
| Noise Type | OpenSimplex2 或 Perlin 可获得平滑的变化；Cellular 用于细胞状图案；Value 用于更简单的随机图案。 |
| Seed | 在不改变图案特征的情况下改变图案。 |
| Scale | 增大可获得更精细的细节，减小可获得更大的形状。 |
| Offset | 移动图案。 |
| Fractal | FBm 添加细节；Ridged 强调脊线；PingPong 创建重复的条带。 |
| Octaves | 添加更多细节层次。 |
| Domain Warp | 弯曲并扭曲图案；Strength 控制扭曲的程度。 |

如需更精细的控制，**Lacunarity** 会改变细节层次之间的间距，**Gain**
会改变较小细节的显著程度。
使用 Cellular 时，可以尝试 **Distance**、**Return** 和 **Jitter** 来改变细胞的形状和规律性。

## 白噪声

选择 **Noise Type → White Noise** 可获得没有平滑过渡的随机颗粒。
**Color → Monochrome** 生成灰度颗粒；**Color → Color** 给出独立的红、绿、蓝数值。
**Grain Size (px)** 起始为一个画布像素；增大它可获得更大的方形颗粒。
使用 **Seed** 获得不同的图案，使用 **Offset** 以像素为单位移动它。
分形和域扭曲不适用于 White Noise；切换类型时它们的设置会保留。
White Noise 还支持 **Dimensions → 1D**，用于生成方向可调的随机条带。

## 蓝噪声

选择 **Noise Type → Blue Noise** 可获得分布更均匀、随机团块更少的颗粒。
它适合抖动遮罩和细密的斑点。**Monochrome / Color**、**Grain Size (px)**、
**Offset**、**Inverted** 和 **Output** 的用法与 White Noise 相同；**1D** 会创建条带。
选择 **Linear Data** 可用于抖动遮罩，选择 **Color Values** 可获得可见的纹理。

Blue Noise 在水平和垂直方向上每 **128 个颗粒**重复一次，在 1D 中每 **256 个颗粒**重复一次。
较大的颗粒会让这种重复变得可见。Seed 会重新排列图案，同时保持其分布。
分形和域扭曲不适用于 Blue Noise。

## 条纹噪声
选择 **Dimensions → 1D** 可创建直线噪声条纹，而不是二维图案。
**Direction (deg)** 旋转变化的方向：0 给出垂直条纹，90 给出水平条纹。
Scale 控制它们的宽度。Offset X 在图案中移动；Offset Y 选择噪声的另一个切片。
对于其他噪声类型，Seed、Noise Type、Fractal 和 Domain Warp 仍然可用；Warp 会改变变化方式，但保持条纹笔直。
切换回 **2D** 即可得到通常的图案，同时不会丢失方向设置。

## 彩色纹理还是高度贴图？

当把噪声用作可见图像时，选择 **Output → Color Values**。
当把它用作高度贴图或将其打包进纹理通道时，选择 **Linear Data**。

要创建表面起伏：

1. 添加 Noise 并选择 **Linear Data**。
2. 选择 **Fractal → FBm**，从三个八度开始并调整 Scale。
3. 在其上方添加 **Normal Map** 并选择 **Generation → Height Map**。
4. 在 Normal Map 的 Advanced 设置中，选择 **Input Space → Linear**。
5. 调整 Strength 和 Smoothing。更改 Noise Seed 可尝试另一种表面。

**Tiled** 预览有助于检查接缝，但不会让 Noise 本身变为无缝。
关于下一步，请参见 [Normal Map](normal-map.md)。
