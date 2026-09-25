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

## 将效果烘焙到图层

添加 FX 按钮旁的 **Apply All** 会烘焙整个效果栈。**⋮ → Apply**（也可从标题栏右键菜单进入）按顺序烘焙所选 FX 及其上方的所有 FX，后续效果仍可编辑。该范围内禁用的 FX 会被移除，但不参与图像计算。这不同于 **Code** 中用于编译 HLSL 的 **Apply**。

非 Drawing 图层会先请求确认转换。Transform 的数值不变且仍可编辑；透明度、混合、swizzle 和剪贴仍单独处理。烘焙以完整画布分辨率保存当前画布的浮点像素，不保留无限程序化源或画布外的内容。Undo 可恢复原图层和 FX 栈。组会合并可见子图层，并警告独立子图层目标丢失及 Pass Through 可能发生变化。

Shader Processor 也支持 Apply。它捕获当前输入，包括 Pass Through 组的外部背景，不合并或删除下方图层。**Normal** 转为 **Overwrite**，同时保留 Processor 的 Opacity 混合方式，包括半透明像素；其他混合模式不变。之后编辑下方图层不会重新计算已烘焙的效果。剩余 FX 继续处理快照，Transform 仍可编辑。

## 参数控件

参数名包含 `Opacity` 或 `Alpha` 时（不区分大小写），拖动手柄使用图层标题栏的透明度图标代替箭头，拖动行为不变。

FX 标题栏中的数值字段前有 **↔** 手柄：左右拖动可调整数值，按住 Shift 可精细调整，按住 Ctrl 可加快调整。

在 `// @whimtex-effect Category/Name` 后紧接着添加 `// @control(_Opacity)`；没有目录标记时，将它放在第一行。它会在 FX 标题栏的 **⋮** 前显示一个已有参数。支持 bool、enum、float、color 和 float2/3/4。`hidden` 只隐藏正文中的字段，不隐藏标题栏控件；否则两个字段编辑同一个值。只能选择一个声明：重复声明会产生警告，以最后一个为准。不支持的字段类型不会改变标题栏。这不会自动添加透明度混合，参数的作用仍由着色器定义。详见[语法参考](../ShaderFX.md#fx-block-control)。

若要在编辑器中按条件显示控件，请将 `// @param` 声明放在 `// @if _Mode == 1`（或 `!=`）与 `// @endif` 之间。仅支持数值 `==` 和 `!=` 比较；条件必须引用无条件声明的 `float`、`bool` 或 `enum` 参数。不支持嵌套块。此功能只隐藏编辑器控件：数值仍会保存并继续影响着色器。导出预设时会保留条件块。

```hlsl
// @param enum _Mode = 0 { Basic: 0, Advanced: 1 }
// @if _Mode == 1
// @param float _Detail = 0.5 [0 .. 1]
// @param bool _UseExtra = false
// @endif
```

在 HLSL 参数声明前添加 `// @header(Lighting)` 可显示加粗的分节标题，不带折叠功能。使用 `// @helpbox(提示文字。)` 可在下一个参数上方显示信息提示框。这些指令仅是 UI 元数据，不会声明 uniform，并会在保存预设和导出可移植代码时保留；后面没有参数的指令会被忽略。

使用 `// @group(Tint; _Parameter)` 与 `// @endgroup` 可将多个控件放入带边框的分组，并将组内无条件声明的参数链接到标题。bool 会显示为标题左侧无标签的复选框，只修改该参数值；使用 `@if` 控制相关控件的显示与隐藏，并可在着色器中用 bool 启用或禁用效果。紧凑的 enum、float、color、float2、float3 和 float4 显示在标题右侧，不再重复标签：组标题就是字段标签，拖动 float 的标题可以调整数值。这些控件不会在组内容中重复显示。`hidden` 仅隐藏参数在组内容中的普通行；显式链接且受支持的标题控件仍会显示，未链接的隐藏参数保持不可见。不支持或多行的控件仍留在组内容中，标题保持普通样式。当组内所有内容行都被 `@if` 隐藏时，内容区域会收起，只留下标题。分组不能嵌套，但组内可以使用 `@if`。`// @group(Advanced)` 创建普通标题组，单独的 `// @group` 创建没有标题的边框组。

可在参数声明中 inline 设置自定义 UI 标签，例如 `// @param label(Tint Strength) float _Strength = 1`。`hidden` 和 `label(...)` 可以任意顺序出现，例如 `// @param label(Optional Mode) hidden enum _Mode = Off {Off: 0, On: 1}`。若标签包含括号，请使用引号。标签只影响 UI，并会在预设导出时保留。

将 `// @formerlyserializedas(_OldName)` 放在 `// @param` 紧前面，可在应用 FX 时从旧参数名迁移兼容的已保存值和参数标识。可重复该指令来列出多个旧名称。HLSL 中应改用新的 uniform 名称；保存或导出预设时会保留这些别名。

`one` 默认值创建恒为 1 的水平曲线，关键点时间分别为 0 和 1。

曲线默认值还支持 `easeIn`（缓慢起步）和 `easeOut`（缓慢结束），两者均为从 0 到 1 的二次曲线。

效果作者可在 `// @param` 声明中使用 `curve _Profile = linear` 或 `curve _Profile = easeInOut`。两者均从 (0,0) 到 (1,1)，Ease In Out 在两端平滑趋于水平。

**Color → Levels** 的 **Curve** 在输入黑白点和 Gamma 之后、输出黑白点之前应用，默认为线性。启用 **Preserve Color** 时调整亮度，否则分别调整 RGB 通道。透明度保持不变。

**Color → Brightness Contrast** 调整中间调亮度与色调对比。两个控件默认均为 **0**，表示不变。正 Contrast 拉开明暗色调，负值使其趋向中灰。滑块轨道覆盖 −100…100，但可通过数字输入或拖动标签超出两端。效果采用平滑色调曲线，而非统一 RGB 偏移：黑白端点和透明度保持不变，0–1 之外的 RGB 原样通过。计算在输入 RGB 空间中进行；它不是曝光控制，也不与其他应用逐像素一致。极端设置可能因浮点精度而丢失细节。

纹理来源 **Self** 读取当前 FX 之前的图像，包括前面的效果。**None** 返回透明像素。这两种模式无需指定资源或图层；复制到其他图层后 Self 仍会正确读取该图层的输入。

向量参数提供两个、三个或四个数值分量。`point` 参数是归一化画布 UV 中的 `float2` 坐标，左下角为 `(0, 0)`、右上角为 `(1, 1)`，默认值为 `(0.5, 0.5)`。点击 **Edit on Canvas** 后，可在画布上拖动该点。法线参数表示单位方向，也提供此按钮。拖动法线端点：靠近中心时朝向相机，到达最大半径时平行于画布。单击端点可切换 **+**（朝向相机）与 **−**（背向相机）。

纹理参数支持 **Texture**（资源）和 **Layer**（当前文档中的图层）。选择来源模式，或将图层拖到参数字段上。支持程序化图层和绘制图层，包括已隐藏的来源。组提供彩色内容，但隐藏的子图层仍不显示。来源缺失时输出透明，循环引用不可选。

在 FX 中编写的程序化形状可通过 `LayerToLocal(uv)` 跟随图层变换。坐标约定详见[着色器编写参考](../ShaderFX.md)。

当你需要内置图层无法提供的效果时，可以使用自定义着色器效果。
你可以使用现有效果并调整其参数，无需编写代码。

Bevel Emboss 的 **Profile** 将选定的高度通道映射为浮雕高度。Gradient Map 的 **Mapping** 在选择渐变颜色前重新分配亮度。两条曲线默认为线性。

Color Balance 将 Shadows、Midtones 和 Highlights 放在同一块中。**RGB Offset** 调整三个有符号 RGB 偏移；**Range** 控制阴影和高光的影响范围，设为零会隐藏相应偏移。**Preserve Luma** 保持亮度。FX 标题栏中的 **Opacity** 将结果与原图混合，Alpha 保持不变。

Gain、Levels、Threshold、环境光及扭曲偏移在适当位置提供软边界。Levels 和 Threshold 支持大于 1 的 HDR 数值；混合比例仍限制在 0–1。Pixelate 和 Posterize 可手动输入超过 64 的级数和大于 5 的 Gamma。

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
默认渐变为黑到白，也可在 HLSL 声明中指定两个端点，例如 `// @param gradient _Ramp = #FF0000FF -> #0000FF`。
十六进制颜色按 RGBA 顺序书写；六位颜色默认完全不透明。更改会立即更新效果；简单双端点渐变可以保存为 HLSL 预设默认值。

渐变编辑器中的 **Wrap** 决定 0–1 以外的行为：**Clamp** 保持端点颜色，**Repeat** 重复渐变，**Mirror** 交替反转方向。若效果自身限制了输入范围，循环可能不会显现。

也可以将 WhimTex 效果 `.hlsl` 从 Project 拖到 **Layers** 的某一行。
拖到预览或列表空白处会在合成顶部创建 **Shader Processor**。
HLSL 画笔预设和没有效果标记的文件不会被接受。

如果预设的代码或参数无效，选择时会在 Console 中报告错误，图层保持不变。

每个目录效果都有自己的设置。内置的 **Color → Gain** 用于调整亮度和色调；
**Transform → UV Transform** 在可见边框内移动输入图像，边框外为透明，因此移动时会沿框裁剪图像。扭曲效果没有这种裁剪。
添加到项目中的效果会自动可用；无需设置预设文件夹。

**+ Reference** 用于选择 Shader FX 资源，所有使用它的位置共享设置。
效果标题栏中的 **⋮ → Embed Copy** 会在文档内创建独立副本，不修改外部资源。同一菜单还包含 **Move Up**、**Move Down** 和 **Remove**。
选择 **⋮ → Copy FX**，然后在另一行选择 **Paste FX As New**，即可在该行后插入独立副本。Shader FX 的代码和参数会一并复制；在同一文档内粘贴时保留图层纹理引用，粘贴到其他文档时会清除这些引用。Material 行复制的是同一个材质资源引用。
项目 HLSL 效果的代码会随源 `.hlsl` 文件更新。要在文档内独立编辑代码，
请点击 **Code** 中的 **Embed Copy**。参数显示在标题下方；**Code** 包含代码编辑器、**Apply**、**Save Preset…** 和 **Shader inputs** 参考。存在消息时才显示诊断。

**在外部编辑器中编辑**

- **Open Code** 在 Unity 所选的脚本编辑器中打开工作文件。保存会更新 WhimTex 中的草稿；点击 **Apply** 编译。
- **Open in VS Code** 使用独立配置，自动安装 WhimTex 指令高亮、补全和检查。保存工作文件会向 Unity 请求 **Apply**，无需返回 WhimTex 窗口。语言模式仍为 **HLSL**，内置扩展支持 Restricted Mode。
- 若没有 VS Code 按钮，可设置 **User Settings → External Code Editor → VS Code Command**。WhimTex 先检查 Unity 注册的编辑器和 PATH，再使用此备用路径。
- 编译失败时保留最后可用的结果，并显示诊断。请在 WhimTex 中保存文档，将修改写入 TIFF；仅保存代码不会保存 TIFF。Unity 脚本重新加载后，请从 WhimTex 再次打开代码以重新连接。

扩展不仅高亮指令名称，也区分类型、修饰符、参数名、数值和范围。它检查默认值、提示注释、关联控件、条件和分组；着色器编译错误仍由 Unity 检查。更新后通过 **Open in VS Code** 重新打开代码；已经运行的窗口可能需要执行 **Developer: Reload Window**。

输入 `// @if` 或 `// @group` 后按 **Tab**，即可插入带结束指令的代码块。Tab 依次跳转到条件各部分或分组标题，最后进入块内。无空格的 `//@if` / `//@group` 也支持。如果 VS Code 设置禁用了 Tab 补全，可通过 **Ctrl+Space** 选择代码片段。

外部编辑器的工作文件只是临时缓存，并非备份。超过 **24 小时**未使用且没有活动会话的副本会自动删除。请在 WhimTex 中保存 TIFF 以保留代码；仅在代码编辑器中保存不会保存文档。重启 Unity 或重新加载脚本后，请从 WhimTex 再次打开代码以恢复同步。

效果顺序会影响结果。拖动 FX 标题可以调整顺序，也可将其拖到其他图层行上以移动到该图层。标题的右键菜单也提供 **Move Up** 和 **Move Down**。

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

**反相**

**Color → Negative** 使用 **Amount** 将原始 RGB 与反相颜色（`1 - RGB`）混合。
默认保留 Alpha；启用 **Invert Alpha** 后，Alpha 也会按相同强度反相。

**遮罩**

**Color → Mask** 读取 **Mask**（默认 Self）的所选通道。**Transform** 定位遮罩，**Profile** 重映射数值，**Invert** 反转结果。**Apply To → Channels** 选择要乘以遮罩的通道；初始仅启用 Alpha。**Color** 模式的各分量控制保留原通道的程度：1 保持不变，0 完整应用遮罩，而不是染色。**Amount** 控制整体强度。

**渐变映射**

**Color → Gradient Map** 使用渐变为阴影、中间调和高光重新着色。
点击 **Gradient** 选择颜色，使用 **Amount** 与原图混合，使用 **Reverse** 反转映射方向。
输入亮度超出 0..1 时使用相应端点的颜色。保留原图透明度，忽略渐变的透明度。

**像素化与抖动**

**Stylization → Pixelate** 把每个 **Pixel Size** 个画布像素的方块替换为一个值。
**Average** 改为在方块内按 4×4 网格取样，而不是只取中心，细小的细节因此不易丢失。
**Levels** 决定每个通道保留多少个值，**Gamma** 调整各档在阴影与高光之间的分布。
**Dither** 选择在它们之间分散误差的图案：**Bayer2**、**Bayer4** 和 **Bayer8** 产生经典的规则图案，
**Interleaved** 是不规则噪声，**Checker** 是双色棋盘，**Halftone** 构成网点，**Hash** 是没有可见网格的稳定噪声。
图案按方块计算，因此在像素化之后依然可见。**Dither Strength** 会削弱图案，直到变为普通四舍五入。
**Color → Quantization** 使用 Levels 和 Gamma；**Color → One Bit** 则按亮度选择 **Low Color** 或 **High Color**。
**Offset** 移动网格而不移动图层。默认保留透明度；启用 **Alpha Clip** 后，**Alpha Cutoff** 决定透明与不透明的分界。同一组图案也可在 **Stylization → Posterize** 中逐像素使用。

**其他风格化效果**

- **Step** 分别对已启用的颜色通道进行阈值处理。Red、Green、Blue 默认启用，Alpha 默认关闭。选择 **Hard** 得到两级结果，或选择 **Smoothstep** 并用 **Hardness** 调整过渡柔和度。 **Apply To → Color** 使用 RGBA 分量控制各通道的效果强度，而非替换颜色：0 保留原通道，1 完整应用阈值效果。**Threshold** 则比较亮度（或 Alpha）与阈值，在两种颜色之间映射；可平滑边缘，并保留源 Alpha。
- **Halftone** 将图像转换为单色、CMYK 或 RGB 网点屏幕。可设置网点大小与形状；CMYK/RGB 模式还提供屏幕角度以及手动或自动色版套准。
- **Chromatic Aberration** 将红、蓝通道向相反方向偏移，可从某个点径向扩散或沿指定角度偏移。**Amount** 的单位是画布像素；绿色通道和 Alpha 保持不变。
- **CRT** 组合边缘弯曲、扫描线、RGB 荧光条纹、暗角、色差、颗粒和闪烁。**VHS** 加入逐行抖动、色彩拖影、噪声和移动的跟踪带。**Seed** 改变确定性图案，**Effect Time** 选择其他帧。
- **Digital Glitch** 组合行撕裂、按行或列独立分段的破损块、通道偏移、颜色/噪声/Alpha 瑕疵以及渐变着色。**Seed**、**Effect Time** 和 **Frame Rate** 控制可重复的动画变化；**Block Order** 选择先按行或先按列独立排布区块。

## 保存你自己的预设

在效果的代码编辑器中，点击 **Save Preset…**。这会创建一个 `.hlsl` 文件，
其中以当前参数值作为默认值，包括颜色和 Transform 2D。
文件名会成为预设名称。你可以保存到用户库的 **ShaderFX**
子文件夹中，或项目 **Assets** 文件夹下的任意位置。覆盖保存会保留一份 `.bak` 副本。

在 **User Settings → Presets → Presets Folder** 中设置共享库位置。其 **Brushes**
和 **ShaderFX** 子文件夹分别存放两类预设。你也可以将现有的
HLSL 预设放入 ShaderFX 或其子文件夹；重新打开 **+ Preset** 即可在 **User** 下看到它们。
用户预设会被复制到文档中，后续文件修改不会影响已添加的效果。
从项目 **Assets** 添加预设会创建关联实例，修改文件会更新与其关联的效果。导出预设本身不会将当前效果重新关联到新文件。

纹理默认值是引用，而不是内嵌图像。要在另一个项目中使用它们，
还需一并转移被引用的纹理资源及其 `.meta` 文件，或指定替换资源。

编辑后的渐变保存在文档中。简单双端点渐变也可导出为 HLSL 默认值；额外色标或非默认插值、平滑度、循环方式需要在导出前简化。

## 在画布上调整效果

带有 Transform 2D 参数的效果提供 **Edit on Canvas**。选择它会显示绿色边框，
然后可以移动、缩放或旋转该边框。旋转围绕其中心进行；没有轴心控制。
按住 Ctrl/Cmd 拖动角点可自由变形，拖动边缘可倾斜；Ctrl/Cmd + Alt + Shift 拖动角点可成对调整透视。
Ctrl/Cmd + Alt 会对称移动对角点。Position、Size 和 Rotation 保留已有变形；**Reset Transform** 清除变形。
**Edit on Canvas** 在上下文工具下方启用一个临时手形工具，Point 和 Normal 参数也使用此工具。按钮提示标明参数。点击工具按钮、再次点击 **Edit on Canvas** 或按 Escape 可返回之前的工具。拖动时第一次按 Escape 只取消拖动。编辑其他参数会替换临时工具，但不会改变返回目标。参见[上下文工具](preview.md#上下文工具)。

<a href="{{ '/Images/shader-processor-transform.png' | relative_url }}"><img src="{{ '/Images/shader-processor-transform.png' | relative_url }}" alt="WhimTex Shader Processor using a Spherize preset with a green Transform 2D frame on the preview" width="720"></a>

该边框编辑的是效果，而不是图层的变换。其用途取决于具体效果：
它可能用于放置图像、更改图案的缩放，或定义局部区域。边框本身不是遮罩，
但 **UV Transform** 的边框外没有像素。

## 扭曲预设

选择 **FX → + Preset → Distortion → Spherize**、**Twirl**、**Radial Shear** 或 **Displacement Map**。

- **Spherize / Mode：** `Classic` 保留当前不受边框限制的径向扭曲；`Sphere` 将图像投影到球面并裁切为圆形。边缘会以约一个像素进行抗锯齿。
- **Spherize / Strength：** 正值会扩张中心；负值会收缩它。`Classic` 模式下零表示不改变图像；`Sphere` 模式下零仍保留圆形，但不扭曲纹理。
- **Twirl / Angle：** 围绕中心扭曲；符号会反转方向。角度以边框局部半径 1 处的度数计量，并随距离增长。
- **Radial Shear：** 采样坐标会随离 **Center** 的距离增加而逐渐旋转；**Strength** 控制方向和强度，**Offset** 添加基础偏移。
- **Area / Edit on Canvas：** 移动、缩放或旋转绿色坐标框。拉伸它可使扭曲变为椭圆形。
- **Displacement Map / Mode：** `VectorRG` 将 R/G 作为有符号方向场；默认 `Neutral` 为 0.5，表示不偏移。矢量贴图和高度贴图应使用线性数据设置。X/Y 强度以画布像素为单位。`Grayscale` 读取所选通道，并按水平、垂直、径向、切向或指定角度移动像素。`ParallaxOcclusion` 将所选贴图通道作为高度，并沿虚拟视线移动采样位置。
- **Parallax / Depth 与 View：** **Depth** 设置以画布像素为单位的高度范围；**View Angle** 设置射线方向，降低 **View Elevation** 会增大位移。**Invert Height** 可交换凸起与凹陷区域。**Parallax Steps** 用于调整质量与开销（4–32 次高度采样），默认值为 8。
- **Map / Transform 与 Wrap：** 独立定位和缩放贴图。`Clamp`、`Repeat` 和 `Mirror` 只控制贴图坐标，不影响被扭曲图像的边缘。
- **Strength Mask：** 默认 `Constant1`，因此只需一张贴图，也可以不设置强度遮罩。`MapChannel` 重用同一贴图的一个通道，`InputAlpha` 使用输入图像的透明度，`SeparateTexture` 则额外提供一张遮罩贴图。可反转遮罩或用 **Mask Profile** 曲线重新映射。
- **Output / Mix 与 Input Edge：** 将扭曲采样与原图混合，并选择图像坐标超出边界时的处理方式：`Clamp`、`Repeat`、`Mirror` 或 `Transparent`。

`Classic` Spherize、Twirl 和 Polar Coordinates 在边框处不会遮罩，扭曲会继续延伸到框外。
`Sphere` 是例外：它会创建带清晰抗锯齿边缘的圆形遮罩。未遮罩模式下，较强设置可能显示输入图像外的区域，
此时会延伸输入图像的边缘像素。RGB 和透明度一起扭曲。

### 极坐标

**Distortion → Polar Coordinates** 是一个带有 **Mode** 切换的效果（默认为 To Polar）：

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
