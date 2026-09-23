---
title: "Shader FX 与处理器"
parent: "简体中文"
nav_order: 9
lang: "zh"
permalink: "/zh/shader-fx/"
translations: "en/shader-fx.md,ru/shader-fx.md,zh/shader-fx.md"
previous_page: "zh/blending.md"
next_page: "zh/preview.md"
---

# Shader FX 与处理器

若要在编辑器中按条件显示控件，请将 `// @param` 声明放在 `// @if _Mode == 1`（或 `!=`）与 `// @endif` 之间。仅支持数值 `==` 和 `!=` 比较；条件必须引用无条件声明的 `float`、`bool` 或 `enum` 参数。不支持嵌套块。此功能只隐藏编辑器控件：数值仍会保存并继续影响着色器。导出预设时会保留条件块。

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

在 HLSL 参数声明前添加 `// @header(Lighting)` 可显示加粗的分节标题，不带折叠功能。标题无需引号，保存预设时会保留。

`one` 默认值创建恒为 1 的水平曲线，关键点时间分别为 0 和 1。

曲线默认值还支持 `easeIn`（缓慢起步）和 `easeOut`（缓慢结束），两者均为从 0 到 1 的二次曲线。

效果作者可在 `// @param` 声明中使用 `curve _Profile = linear` 或 `curve _Profile = easeInOut`。两者均从 (0,0) 到 (1,1)，Ease In Out 在两端平滑趋于水平。

**Color → Levels** 的 **Curve** 在输入黑白点和 Gamma 之后、输出黑白点之前应用，默认为线性。启用 **Preserve Color** 时调整亮度，否则分别调整 RGB 通道。透明度保持不变。

纹理来源 **Self** 读取当前 FX 之前的图像，包括前面的效果。**None** 返回透明像素。这两种模式无需指定资源或图层；复制到其他图层后 Self 仍会正确读取该图层的输入。

向量参数提供两个、三个或四个数值分量。`point` 参数是归一化画布 UV 中的 `float2` 坐标，左下角为 `(0, 0)`、右上角为 `(1, 1)`，默认值为 `(0.5, 0.5)`。点击 **Edit on Canvas** 后，可在画布上拖动该点。法线参数表示单位方向，也提供此按钮。拖动法线端点：靠近中心时朝向相机，到达最大半径时平行于画布。单击端点可切换 **+**（朝向相机）与 **−**（背向相机）。

纹理参数支持 **Texture**（资源）和 **Layer**（当前文档中的图层）。选择来源模式，或将图层拖到参数字段上。支持程序化图层和绘制图层，包括已隐藏的来源。组提供彩色内容，但隐藏的子图层仍不显示。来源缺失时输出透明，循环引用不可选。

在 FX 中编写的程序化形状可通过 `LayerToLocal(uv)` 跟随图层变换。坐标约定详见[着色器编写参考](../ShaderFX.md)。

当你需要内置图层无法提供的效果时，可以使用自定义着色器效果。
你可以使用现有效果并调整其参数，无需编写代码。

Bevel Emboss 的 **Profile** 将选定的高度通道映射为浮雕高度。Gradient Map 的 **Mapping** 在选择渐变颜色前重新分配亮度。两条曲线默认为线性。

Color Balance 使用三个有符号 RGB 分量。Gain、Levels、Threshold、环境光及扭曲偏移在适当位置提供软边界。Levels 和 Threshold 支持大于 1 的 HDR 数值；混合比例仍限制在 0–1。Pixelate 和 Posterize 可手动输入超过 64 的级数和大于 5 的 Gamma。

Shader FX 标题栏中的无标签复选框可启用或跳过效果，并保留其设置。外部 FX 引用共享此状态；使用 **Embed** 创建独立副本。

当输入和参数没有变化时，FX 与 Shader Processor 的结果会被缓存。Unity 时间输入
（`_Time`、`_SinTime`、`_CosTime`、`_TimeParameters`、`unity_DeltaTime`）不受支持：
允许使用它们，但点击 **Apply** 会显示警告，并为该结果停用缓存。

## 应用现有效果

1. 选择要更改的图层。
2. 在其 FX 区域中使用 **+ Preset ▾**，按类别选择效果。
3. 调整滑块、颜色、纹理、开关和下拉选项，图像会立即更新。

同一个参数可以同时提供滑块和下拉列表，两者修改同一个值。
**Custom** 表示当前数值不在下拉列表的预设选项中。

将鼠标悬停在参数上，可以查看效果作者提供的说明（如果有）。

某些滑块允许越过显示范围的一端或两端：在旁边的数字框中输入，或拖动参数标签即可。
每一端都可以独立设置为硬限制或软限制。
滑块停在最近的端点，但效果使用实际输入的数字。其他滑块会同时限制拖动和数字输入，
具体行为由效果作者决定。

效果可以提供曲线字段，用于调整数值轮廓。点击字段，在 Unity 标准曲线编辑器中修改关键点和切线。
默认是从 0 到 1 的直线，输出值可以低于 0 或高于 1。保存 HLSL 预设时会保留编辑后的曲线。

效果还可以提供渐变字段。点击色带即可编辑颜色、透明度和插值，包括 HDR 颜色。
新渐变默认为黑到白，修改会立即更新效果。编辑后的渐变关键点会保存在文档中，
但目前不会写入导出的 HLSL 预设；该预设的新实例仍从黑白渐变开始。

也可以将 WhimTex 效果 `.hlsl` 从 Project 拖到 **Layers** 的某一行。
拖到预览或列表空白处会在合成顶部创建 **Shader Processor**。
HLSL 画笔预设和没有效果标记的文件不会被接受。

如果预设的代码或参数无效，选择时会在 Console 中报告错误，图层保持不变。

每个目录效果都有自己的设置。内置的 **Color → Gain** 用于调整亮度和色调；
**Transform → UV Transform** 在可见边框内移动输入图像，边框外为透明，因此移动时会沿框裁剪图像。扭曲效果没有这种裁剪。
添加到项目中的效果会自动可用；无需设置预设文件夹。

**+ Reference** 用于选择 Shader FX 资源，所有使用它的位置共享设置。
效果标题栏中的 **⋮ → Embed Copy** 会在文档内创建独立副本，不修改外部资源。同一菜单还包含 **Move Up**、**Move Down** 和 **Remove**。
项目 HLSL 效果的代码会随源 `.hlsl` 文件更新。要在文档内独立编辑代码，
请点击 **Code** 中的 **Embed Copy**。参数直接显示在标题下方；**Code** 包含代码编辑器、**Apply**、**Save Preset…** 和嵌套的 **Shader inputs** 参考说明。只有存在消息时才显示诊断区域。

多个效果的顺序会影响结果：每行的 **↑** 和 **↓** 可将该效果提前或推后。

**光照与浮雕**

- **Normal Map → Lighting** 根据 RGB 法线贴图生成受光表面。**Normals** 默认为 Self。通过画布手柄调整 **Light Direction**，并设置 **Base Color**、明暗颜色、**Intensity** 和 **Ambient**。Normal Map 的默认输出应开启 **Packed Color**；Linear Data 应关闭。**Flip Y** 反转绿色轴方向。不支持平台专用压缩法线编码。
- **Lighting → Bevel Emboss** 从 **Height Map**（默认 Self，也可选择其他图层）计算法线。**Height Channel** 选择亮度、R、G、B 或 Alpha；**Profile** 映射高度，**Depth** 控制凸起或凹陷，**Smoothing** 设置以文档像素计的采样半径。SDF 的可见渐变与距离范围决定倒角宽度。

两个效果均通过 **Base Color 的透明度** 在透明光影（0）和完整受光表面（1）之间平滑混合，中间值采用考虑透明度的混合。RGB 设置表面颜色，**Ambient** 随填充逐渐显现。**Output** 为透明光影选择 Both、Highlight Only 或 Shadow Only；Base Color 透明度为 1 时无影响。Light Color/Shadow Color 的透明度控制该部分的强度。整体不透明度与混合模式由图层控制。两个效果的 Base Color 默认透明度均为 0。旧的嵌入版本需重新添加预设。

**法线贴图归一化**

**Normal Map → Normalize** 恢复法线的单位长度，同时保留透明度。
处理 RGB 法线贴图后，如果向量长度发生变化，可以使用此效果。
中性法线 `(0.5, 0.5, 1)` 保持不变；未定义的方向会变为中性法线。
对于 Normal Map 图层的默认编码，保持 **Packed Color** 开启；对于 **Linear Data**，关闭它。
该开关处理颜色编码，不会解包平台特定的法线贴图格式。

**HSV 校正**

**Color → HSV** 调整色相、饱和度和明度。**Hue** 以度数偏移色相；**Saturation** 和 **Value**
是倍率：1 不变，0 去除饱和度或使图像变黑。**Amount** 与原图混合。
调整色相或饱和度不会给灰色像素着色。保留透明度，支持 HDR 明度。
校正时将负 RGB 通道视为零；中性设置和 Amount 0 完全保留原图。

**渐变映射**

**Color → Gradient Map** 使用渐变为阴影、中间调和高光重新着色。
点击 **Gradient** 选择颜色，使用 **Amount** 与原图混合，使用 **Reverse** 反转映射方向。
输入亮度超出 0..1 时使用相应端点的颜色。保留原图透明度，忽略渐变的透明度。

**像素化与抖动**

**Pixel Art → Pixelate** 把每个 **Pixel Size** 个画布像素的方块替换为一个值。
**Average** 改为在方块内按 4×4 网格取样，而不是只取中心，细小的细节因此不易丢失。
**Levels** 决定每个通道保留多少个值，**Gamma** 调整各档在阴影与高光之间的分布。
**Dither** 选择在它们之间分散误差的图案：**Bayer2**、**Bayer4** 和 **Bayer8** 产生经典的规则图案，
**Interleaved** 是不规则噪声，**Checker** 是双色棋盘，**Halftone** 构成网点，**Hash** 是没有可见网格的稳定噪声。
图案按方块计算，因此在像素化之后依然可见。**Amount** 会削弱图案，直到变为普通四舍五入。
**One Bit** 按亮度把结果压缩为 **Low Color** 和 **High Color**，而不是逐通道量化。
透明度保持不变。同一组图案也可在 **Color → Posterize** 中逐像素使用。

## 保存你自己的预设

在效果的代码编辑器中，点击 **Save HLSL Preset…**。这会创建一个 `.hlsl` 文件，
其中以当前参数值作为默认值，包括颜色和 Transform 2D。
文件名会成为预设名称。你可以保存到用户库的 **ShaderFX**
子文件夹中，或项目 **Assets** 文件夹下的任意位置。覆盖保存会保留一份 `.bak` 副本。

在 **User Settings → Presets → Presets Folder** 中设置共享库位置。其 **Brushes**
和 **ShaderFX** 子文件夹分别存放两类预设。你也可以将现有的
HLSL 预设放入 ShaderFX 或其子文件夹；重新打开 **+ Preset** 即可在 **User** 下看到它们。
用户预设会被复制到文档中，后续文件修改不会影响已添加的效果。
保存在项目 **Assets** 中的预设则保持文件关联，修改文件会更新所有使用它的效果。

纹理默认值是引用，而不是内嵌图像。要在另一个项目中使用它们，
还需一并转移被引用的纹理资源及其 `.meta` 文件，或指定替换资源。

## 在画布上调整效果

带有 Transform 2D 参数的效果提供 **Edit on Canvas**。选择它会显示绿色边框，
然后可以移动、缩放或旋转该边框。旋转围绕其中心进行；没有轴心控制。
按住 Ctrl/Cmd 拖动角点可自由变形，拖动边缘可倾斜；Ctrl/Cmd + Alt + Shift 拖动角点可成对调整透视。
Ctrl/Cmd + Alt 会对称移动对角点。Position、Size 和 Rotation 保留已有变形；**Reset Transform** 清除变形。
再次点击该按钮或切换工具即可退出此模式。按 Escape 取消当前拖拽。

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

该边框编辑的是效果，而不是图层的变换。其用途取决于具体效果：
它可能用于放置图像、更改图案的缩放，或定义局部区域。边框本身不是遮罩，
但 **UV Transform** 的边框外没有像素。

## 扭曲预设

选择 **FX → + Preset → Distortion → Spherize** 或 **Twirl**。

- **Spherize / Strength：** 正值会扩张中心；负值会收缩它。零则保持图像不变。
- **Twirl / Angle：** 围绕中心扭曲；符号会反转方向。角度以边框局部半径 1 处的度数计量，并随距离增长。
- **Area / Edit on Canvas：** 移动、缩放或旋转绿色坐标框。拉伸它可使扭曲变为椭圆形。

Spherize、Twirl 和 Polar Coordinates 不会在边框处遮罩或淡出，扭曲会继续延伸到框外。
较强的设置可能显示输入图像外的区域，此时会延伸输入图像的边缘像素。RGB 和透明度一起扭曲。

### 极坐标

**Distortion → Polar Coordinates** 包含两个效果：

- **To Polar** 将条带卷成圆形：水平方向绕中心环绕，垂直方向向外延伸。
- **From Polar** 将圆形展开为条带：从左到右覆盖一整圈，从下到上覆盖距中心的距离。

**Area / Edit on Canvas** 定位并塑造圆形：To Polar 时是输出圆形，From Polar 时是源圆形。
**Angle Offset** 以度数移动环绕的起点；零表示从中心右侧开始并逆时针延伸。
**Radial Offset** 移动起始半径：正值在 To Polar 中将图案向外移动，
或在 From Polar 中从距中心更远处开始采样。一个单位沿着坐标轴从中心延伸到边框边缘。

半径会继续延伸到边框之外而不淡出。请匹配源条带的左右边缘，
以避免圆形周围出现可见接缝。整条条带的宽度会在中心汇聚，
因此展开无法恢复在该点丢失的细节。

## 影响一个图层还是下方图像？

**Shader FX** 属于单个图层，并更改该图层的图像。

**Shader Processor** 是一个单独的图层，用于更改其下方合成后的图像。
将它放在你要处理的图层之上。
使用 **Normal** 混合并降低 Opacity 可将效果与原始图像混合；
隐藏 Processor 即可对比处理前后。

在 Pass Through 组中，Processor 也可以影响该组下方的背景。
如果效果应限制在该组内容之内，请使用隔离组。

与 [Post FX 预览](post-fx.md) 不同，两者都会被包含在保存的图像中。

## 创建你自己的效果

如果你有着色器代码，请使用 **+ Shader FX**，将其粘贴到编辑器中并点击 **Apply**。
代码和设置会随文档一起保存；不需要单独的文件。
如果代码包含错误，先前可用的版本会保持可见。

HLSL 效果与画笔可以使用内置噪声库制作颗粒、有机遮罩和扭曲。
参见[噪声函数与示例](../AI/README.md#built-in-noise-library)。

编写效果是可选的。[着色器编写参考](../ShaderFX.md)
用于创建代码和可复用库。
