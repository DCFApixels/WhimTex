---
title: "画笔、铅笔与填充"
parent: "简体中文"
nav_order: 4
lang: "zh"
description: "在 Unity 中用 WhimTex 的画笔、铅笔和填充绘制纹理。使用纹理画笔、渐变、散布和预设，快速修饰并制作 VFX 遮罩。"
permalink: "/zh/painting/"
translations: "en/painting.md,ru/painting.md,zh/painting.md"
previous_page: "zh/transform.md"
next_page: "zh/selection.md"
---

# 画笔、铅笔与填充

选择一个 **Drawing** 图层，直接在画布上绘制。
选择 **Brush**（`B`）绘制柔和的笔触，或选择 **Pencil**（`P`）绘制锐利的像素。
如果你尝试在其他类型的图层上绘制，编辑器会先提示转换。

## 塑造笔触

| 控件 | 它改变什么 |
| :--- | :--- |
| Size | 笔触宽度。你可以拖动标签或使用 `[` / `]`。 |
| Hardness / Gradient | 位于预览标题栏中 Size 的旁边。拖动 Hardness 或输入百分比；点击渐变条可编辑它。箭头可在 Hardness 和 Gradient 之间切换程序化画笔，并且不会丢失任一设置。 |
| Opacity | 整个笔触的最大强度。松开并再次绘制可叠加另一层。 |
| Flow | 每个画笔图章的强度。较低的值让重叠的图章逐渐叠加颜色。 |

例如，**Opacity 40%** 会让一个笔触最高不超过 40%，即使你反复
涂过它。**Flow 10%** 会随着绘制逐渐叠加，直到 Opacity 上限。
这些控件在用 Brush 擦除时同样有效。

对于纹理画笔，请在 **Brushes → Tip** 中启用 **SDF** 以使用 Gradient 控件。
不启用 SDF 时，纹理定义自己的边缘，此标题栏区块不可用。
相同的控件也可在 **Brushes → Tip** 中作为 **Mode**、**Hardness** 和
**Gradient** 使用。任一处的更改都会保持同步。

Pencil 没有硬度或间距控件。可为它的笔尖选择 **Circle**、**Square** 或 **Diamond**。
放大即可看到它精确的像素轮廓。

## 自定义画笔

在 **Brushes → Tip → Source** 中选择 **Standard** 或 **HLSL**。
Standard 在 **Texture** 为空时使用程序化圆形笔尖；指定图像后使用纹理笔尖。
HLSL 用脚本生成笔尖，与带纹理的 Standard 使用相同的 Tip Channel、SDF/Gradient、旋转和翻转设置。
选择 **HLSL Presets**，或打开 **Edit Code…** 编辑脚本并点击 **Apply**。
**Tip Resolution** 控制笔尖细节，**Size** 控制笔触大小。
代码窗口可将 HLSL 预设保存到项目或用户预设文件夹。
项目中的文件与用户预设 **Brushes/HLSL** 子文件夹中的文件会出现在菜单中。
普通画笔的 **Save As…** 也会保留 HLSL 代码和设置。

浏览器 AI 可以生成画笔 JSON。在文本输入框外按 **Ctrl+V** 粘贴，
即可替换当前画笔，不改变图层。参见[画笔格式与示例](../AI/BRUSHES.md)。

在选中 **Brush** 的情况下，打开预览右边缘上方的箭头以显示
**Brushes**。它与 Post FX 共用抽屉：打开一个会关闭另一个。

<a href="{{ '/Images/brush-settings.png' | relative_url }}"><img src="{{ '/Images/brush-settings.png' | relative_url }}" alt="WhimTex Brushes drawer with a neon red stroke, stamp controls, tint, blend mode and live brush preview" width="720"></a>

所有画笔预设控件都在这里：**Size** 在 **Tip** 中，**Opacity / Flow**
在 **Color** 中。预览标题栏提供了指向相同设置的快捷入口。

底部的波浪形笔触会在你调整时预览当前画笔。较大的笔尖
会被缩小以适应该预览；Eraser 会显示它在灰色颜料上的效果。该示例不包含
所选图层的效果或对称。
散布会分散图章，但不会缩小它们；较大的散布可能会超出示例的边缘。
使用示例下方的 **Preview Scale (%)** 手动缩小它以容纳更多散布。
这只改变示例，而不改变你绘制时使用的画笔大小。

**Tip**、**Stamps** 或 **Color** 标题栏中的 **↺** 会重置对应部分，
但保留 **Size、Hardness、Spacing、Opacity 和 Flow**。Hardness/Gradient 模式仍可能改变。
调色板颜色不会重置。**Tint** 旁的小按钮只将其渐变恢复为不透明白色，不改变画笔颜色。

| 设置 | 它改变什么 |
| :--- | :--- |
| Spacing (%) | 图章之间的距离，以 Size 为基准。100% 是一个直径的距离；较低的值形成连续的笔触，较高的值留下彼此分开的痕迹。 |
| Scatter | 围绕笔触的随机位移。100% 会让中心最多偏移一个画笔直径。 |
| Scatter Bias | 负值让图章集中在笔触附近；正值将它们移向散布圆盘的外缘。**0** 让分布在其整个区域内保持均匀。在 Scatter 大于零时可用；适用于 Random 和 Sobol。 |
| Randomization | **Random**（默认）给出普通的随机变化。**Sobol** 让变化在图章之间分布得更均匀。适用于 Scatter、Size Jitter、Angle Jitter、Flip X/Y 和 Tint。 |
| Size Jitter | 围绕 Size 的随机变化。50% 会给出所选尺寸的一半到一倍半之间的图章。 |
| Rotation | **Fixed** 保留纹理笔尖的原始方向。**Stroke Direction** 让它沿笔触转向，笔尖朝右的轴跟随移动方向。第一次点击使用原始方向，直到你移动为止。 |
| Angle Offset (°) | 在 Rotation 之后附加的固定旋转，从 −180° 到 180°。例如，在 Stroke Direction 下设置 90° 会让笔尖横跨笔触。需要纹理笔尖。 |
| Angle Jitter (°) | 在 Rotation 和 Angle Offset 之后附加的随机旋转。0° 不添加变化，180° 覆盖所有方向。需要纹理笔尖；程序化画笔不会因旋转而改变。 |
| Flip X / Flip Y | 每个纹理图章水平或垂直镜像的几率：**0** 从不，**0.5** 大约一半，**1** 总是。轴跟随笔尖的旋转。需要纹理笔尖；每个轴单独采样。 |
| Texture | 拖入一个纹理以将其形状用作画笔。清空该字段可返回程序化画笔。Size 测量其最长边。 |
| Tip Channel | Alpha 使用透明度；Luminance 将白色作为墨水；Inverted Luminance 将黑色作为墨水。Color 保留笔尖的颜色，并乘以绘制颜色。所有模式都遵循笔尖的透明通道。 |
| SDF | 将纹理视为距离场，并通过 Gradient 映射它。使用 Alpha 表示以透明度构成的场，使用 Luminance 表示灰度场，或在暗部为内部时使用 Inverted Luminance。Color 使用透明通道场，同时保留笔尖的 RGB 颜色。 |
| Gradient (preview header) | 左侧是内部（**0**），右侧是外缘（**1**）。透明通道色标控制边缘和覆盖范围；颜色色标会乘以画笔颜色和 Tint。将透明通道色标移近可获得锐利边缘，移远则变得柔和。将过渡右移可扩展形状，左移则收缩它。 |
| Tint | 不同的渐变色或透明通道色标会为每个图章产生随机着色，并乘以调色板颜色。相同的色标给出恒定着色。右侧的小 ↺ 按钮会重置为不透明白色，这不会改变调色板颜色。 |
| Blend | 绘制与活动图层上已有像素的结合方式。独立于图层的 Blend 设置；擦除时会被忽略。 |
| Apply Blend | **Per Stroke**（默认）将 Blend 应用于整个笔触。**Per Stamp** 将它应用于每个图章，包括同一笔触内图章重叠的地方。Opacity 控制整个结果；Flow 控制每个图章。在密集的 Spacing 下，Per Stamp 可能会更慢。 |

要获得散布的彩色痕迹，请增大 Spacing 和 Scatter，添加一点 Size Jitter，
然后向 Tint 渐变添加不同的颜色。开始时在调色板中使用白色，以便看到
渐变的颜色而不叠加额外的色调。

这些高级设置属于 **Brush**，而不是 Pencil 或 Fill。

程序化画笔的渐变从**圆形笔尖中心的 0** 延伸到**外缘的 1**。
指定纹理后，已选的程序化画笔模式会保留。

对于纹理 SDF 画笔，在 Tip 中启用 **SDF**，选择包含距离场的通道，
然后在观察示例的同时在预览标题栏中编辑 **Gradient**。开始时使用白色颜色色标以保留
画笔颜色，并使用透明通道色标塑造边缘。你可以添加透明色带
来制作中空形状，或添加颜色色带来制作多色笔尖。该场应使用 0–1 范围，
内部值更高（或选择 Inverted Luminance）。普通图像
笔尖请保持 SDF 关闭。保存画笔预设时会包含 Gradient。

### 画笔预设

项目中的 `.sebrush` 文件显示为画笔资源，并在 Project 窗口中带有笔触缩略图。
选中资源可在 Inspector 中查看大图。将资源拖入 WhimTex 窗口的任意位置即可选择画笔，也可以使用画笔预设菜单。拖入画笔不会创建图层或切换工具。

在 **Brushes** 顶部的选择器中选择一个已保存的画笔。使用 **Save As…** 为
新预设命名；选择器菜单还提供 **Overwrite Selected…** 和
**Open Brushes Folder**。星号表示自保存或
选择以来你已更改过该画笔。更改不会自动保存。

预设包含 Size、Hardness、程序化 Mode、Gradient、Spacing、Opacity、Flow、纹理笔尖、Stamps 和 Color
设置。你的调色板颜色、Brush/Eraser 模式、Pencil/Fill 设置和图层对称
保持不变。选择预设时预览会更新。

在 **Window tab ⋮ → User Settings… → Presets** 中，用 **…** 选择 **Presets Folder**
或输入绝对路径。该文件夹设置会在这台计算机上跨项目共享，
适用于你的用户账户。**↺** 会恢复默认位置而不删除文件。
画笔存储在其 **Brushes** 子文件夹中。每个 `.sebrush` 文件都包含其纹理
笔尖，因此你可以将它复制到另一台计算机的 Brushes 文件夹，而无需导入原始
纹理。覆盖会保留之前的文件为 `.sebrush.bak`；要恢复它，请将该
备份重命名为以 `.sebrush` 结尾的其他名称。

该选择器还会在项目的 **Assets** 或已安装
包中的任何位置查找 `.sebrush` 文件。使用选择器菜单中的 **Save to Project…**，或将预设复制到 Assets
文件夹以与项目共享。**User** 和 **Project** 让两个来源保持区分；
支持子文件夹。可以使用包提供的预设，但请将你的更改
保存为新的用户预设或项目预设。

## 颜色与擦除


第一个颜色样本是绘制颜色；`X` 交换这两个样本。
在 WhimTex 中按住 `Alt` 可使用屏幕取色器；其放大镜中带轮廓的中心像素就是你将拾取的颜色。
用鼠标左键点击或拖动即可拾取，然后松开 `Alt` 返回你的工具。
你可以跨屏幕取色，包括其他 Unity 窗口和其他应用程序。取色器拾取的是可见颜色，
包括 Post FX、EV 和透明棋盘格，而不是原始的 HDR 值。画笔透明通道保持不变。
使用 Brush 或 Pencil 时，按住鼠标右键即可擦除，或在工具选项中选择擦除。

画笔和填充设置会在图层之间跟随你。[对称](symmetry.md) 是为每个 Drawing 图层单独设置的。

## 绘制直线

点击并开始拖动，然后按住 `Shift` 以水平或垂直绘制。
这些方向跟随屏幕，即使画布视图被旋转。
如果笔触开始时吸附到 [参考线](preview.md#参考线)，`Shift` 会改为沿该参考线，任意角度且不受指针与它距离的影响。
要连接各点，请点击第一个点，然后按住 `Shift` 点击下一个点。
重复即可绘制一连串直线段。

## 填充区域

选择 **Fill**（`G`）并点击你想上色的区域。

| 选项 | 何时使用 |
| :--- | :--- |
| All Layers | 填充时沿整个可见图像的轮廓，但只填充活动的 Drawing 图层。关闭则仅使用该图层。 |
| Contiguous | 保持开启以只填充你点击的相连区域。关闭则替换整个图层中匹配的颜色。 |
| Tolerance | 增大它以包含更多相近的颜色；如果填充扩散太远则减小它。 |
| Antialias | 柔化填充的边缘。 |
| Expand (px) | 将填充稍微延伸到轮廓之下，以封闭细小的缝隙。 |

填充不会通过画笔对称重复。要填充图层原始区域之外的
变换副本，请先使用 **Convert to Drawing → Apply Transform**。

使用[区域选区](selection.md)让绘制或填充保持在选定的形状内。
