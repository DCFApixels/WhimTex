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
通过 Layers 底部的 **+ → Noise** 添加噪声图层，边观察图像边调整设置。

## 从图案开始

| 设置 | 可以尝试什么 |
| :--- | :--- |
| Noise Type | OpenSimplex2 或 Perlin 适合平滑变化；OpenSimplex2S 是更平滑的 OpenSimplex2；Cellular 生成单元格；Value 生成简单随机图案；ValueCubic 是平滑的 Value。 |
| Seed | 在不改变图案特征的情况下改变图案。 |
| Scale | 增大可获得更精细的细节，减小可获得更大的形状。 |
| Offset | 移动图案。 |
| Fractal | FBm 添加细节；Ridged 强调脊线；PingPong 创建重复条带；None 关闭分形。 |
| Octaves | 添加更多细节层次。 |
| Domain Warp | 弯曲并扭曲图案；**Warp Strength** 控制程度，None 关闭扭曲。 |
| Warp Scale | Noise Scale 的 X/Y 倍率：每轴最终扭曲尺度为 `Scale × Warp Scale`。默认 [1,1]，每轴范围 0.01–1000。链条保持比例；断开后分别编辑。Random All 在 0.25–4 中选择并保持链接比例。 |

如需更精细的控制，**Lacunarity** 会改变细节层次之间的间距，**Gain**
会改变较小细节的显著程度。
使用 Cellular 时，可以尝试 **Distance**、**Return** 和 **Jitter** 来改变细胞的形状和规律性。


## 轴向缩放、3D 切片与无缝噪声

**Scale X/Y** 可分别调整两个方向的细节尺度。链条按钮按当前比例联动两个值；
启用链接不会改变已有比例。尺度以画布较短边为基准。

### 3D 切片

OpenSimplex2、OpenSimplex2S、Cellular、Perlin、ValueCubic 和 Value 支持 **Dimensions → 3D**。
此时 **Offset** 增加 **Z** 分量，用来选择体积噪声的二维切片。Cellular 的 3D 切片
与普通 2D 单元格外观不同。Fractal 和 Domain Warp 在 2D、3D 中均可使用。

### 无缝重复

使用方形 **Seamless** 控件让噪声本身重复，而不是混合边缘。
左右边缘一起切换 X 轴，上下边缘一起切换 Y 轴。选中四边启用 XY，取消两对边缘则关闭周期性。
点击中央图标可反转两对边缘的选择。
每个分形八度及 Warp 均保持周期性；Z 从不循环。周期必须容纳完整的晶格单元，
因此 Scale 会分段变化，尤其是在较小的 OpenSimplex 尺度下。任意变换和 FX 仍可能
在画布上产生接缝。White/Blue Noise 不提供该选项。

在 **1D** 中，**Seamless** 改为复选框：沿噪声变化轴重复，Fractal 和 Warp 也保持周期性。
一个周期覆盖画布在该轴上的投影；Scale 控制周期内的细节，仍按阶梯变化。
Direction 为 0 时连接左右边缘，为 90 时连接上下边缘。任意角度下，沿噪声轴重复
不保证画布边缘匹配。此复选框默认关闭，与 2D/3D 的边缘选择分别保存。

在 2D OpenSimplex2/2S 中，小于 1 的 Scale 现在可以生成更大的分形细节：每个八度
按所设 Scale 单独确定周期。变化仍然是阶梯式的，每个八度至少保留一个晶格单元。
Fractal None 仍受最小单元限制。现有 2D 无缝 Simplex 图案可能改变，包括 Scale 大于 1
的情况；其他噪声类型及 3D 保持不变。


Warp Scale 适用于 1D、2D 和 3D；Z 频率不变。启用 Seamless 时，根据相乘后的最终尺度
为所选轴匹配完整晶格单元。当 BasicGrid 两个轴都只有一个单元时，扭曲变成均匀平移：
增大 Warp Scale 倍率即可获得变化的扭曲，而无需增大噪声 Scale。
例如 Scale 0.5 × Warp Scale 6 得到扭曲尺度 3。White/Blue Noise 忽略 Warp Scale。
## 白噪声

选择 **Noise Type → White Noise** 可获得没有平滑过渡的随机颗粒。
**Color → Monochrome** 生成灰度颗粒；**Color → Color** 给出独立的红、绿、蓝数值。
**Grain Size (px)** 起始为一个画布像素；增大它可获得更大的方形颗粒。
使用 **Seed** 获得不同的图案，使用 **Offset** 以像素为单位移动它。
分形和域扭曲不适用于 White Noise；切换类型时它们的设置会保留。
White Noise 还支持 **Dimensions → 1D** 随机条带，详见[条纹噪声](#条纹噪声)。

## 蓝噪声

选择 **Noise Type → Blue Noise** 可获得分布更均匀、随机团块更少的颗粒。
它适合抖动遮罩和细密的斑点。**Monochrome / Color**、**Grain Size (px)**、
**Offset**、**Inverted** 和 **Output** 的用法与 White Noise 相同；**Dimensions → 1D** 会创建条带。
抖动遮罩选择 **Output → Linear Data**，可见纹理选择 **Output → Color Values**。

以画布像素计，Blue Noise 在每个轴上的重复周期为 128 × 颗粒大小；1D 模式沿变化方向的周期为 256 × 颗粒大小。
较大的颗粒会让这种重复变得可见。Seed 会重新排列图案，同时保持其分布。
分形和域扭曲不适用于 Blue Noise。

## 条纹噪声

选择 **Dimensions → 1D** 可创建直线噪声条纹，而不是二维图案。
**Direction (deg)** 旋转变化的方向：0 给出垂直条纹，90 给出水平条纹。
所有噪声类型都支持方向调整。**Scale** 控制条纹宽度，White Noise 和 Blue Noise 则使用 **Grain Size (px)**。
**Offset X** 沿变化方向移动图案，**Offset Y** 选择噪声场的另一个切片。
除 White Noise 和 Blue Noise 外，1D 模式仍支持 Fractal 和 Domain Warp。Warp 会改变图案，但条纹保持笔直。
切换回 **2D** 即可得到通常的图案，同时不会丢失方向设置。

## 彩色纹理还是高度贴图？

**Output** 决定如何将噪声值转换为像素：

| Output | 用途 |
| :--- | :--- |
| Linear Data（默认） | 高度贴图及打包到通道的纹理数据。 |
| Color Values | 将原始噪声显示为颜色。 |
| Gradient | 用调色板映射单色值，包括其透明度和 HDR 颜色。 |

**Inverted** 在渐变采样前反转数值，适用于所有输出模式。
默认调色板是黑到白的 **Perceptual** 渐变；切换 Output 会保留它。

彩色 White/Blue Noise 仅支持 Color Values 和 Linear Data。
已保存的 Gradient 选择会暂时使用 Color Values，直到切回 Monochrome。
Random All 会改变哪些设置，参见[随机变化](#随机变化)。

要创建表面起伏：

1. 添加 Noise 并选择 **Output → Linear Data**。
2. 选择 **Fractal → FBm**，从三个八度开始并调整 Scale。
3. 在其上方添加 **Normal Map** 并选择 **Generation → Height Map**。
4. 在 Normal Map 的 Advanced 设置中，选择 **Input Space → Linear**。
5. 调整 Strength 和 Smoothing。更改 Noise Seed 可尝试另一种表面。

**Tiled** 预览有助于检查接缝；使用 **Seamless** 使源噪声重复。
关于下一步，请参见 [Normal Map](normal-map.md)。

## 随机变化

噪声设置顶部的 **Random All** 会随机组合生成器参数，包括当前未启用的选项、
以及 **Inverted**。Output 仅在 Color Values 和 Linear Data 之间随机切换；选定的 Gradient 保持不变。渐变配色、**Dimensions**、**Direction**、**Seamless**、链接的 Scale 和 Warp Scale 比例、**Offset X/Y/Z**、图层变换、混合和 FX 保持不变。
一次撤销即可恢复上一个组合。**Seed** 旁的 **Random** 只改变种子。
Noise Type 只会在当前组内切换：**White Noise / Blue Noise** 为一组，其余噪声类型为另一组。

### Random All 的 Scale 分布

Random All 略微偏向 X/Y 平均 Scale 接近 **8** 的结果，但仍会生成大、小图案。
链接轴保持比例。分布公式与范围见[技术参考](../AgentAPI.md#noise-settings)。
