---
title: "Shader 图层"
parent: "图层与组"
grand_parent: "简体中文"
nav_order: 3
lang: "zh"
description: "通过 Shader Graph 或 WhimTex HLSL Code 创建纹理和遮罩。切换模式时保留两种内容及各自的参数。"
permalink: "/zh/shader-layer/"
translations: "en/shader-layer.md,ru/shader-layer.md,zh/shader-layer.md"
---

# Shader 图层

使用 **Shader** 通过节点图或 WhimTex HLSL 代码生成纹理或遮罩。
两种模式都可读取输入图像。**Composite Below** 读取下方合成，**Crossfade** 按 Opacity 混合处理前后的结果。
普通 **FX** 列表在节点图或 Code 之后执行。

**Shader Program** 是 Shader 图层和 FX 共用的程序。**Draft** 是可编辑草稿，**Working** 是最后一次成功编译的版本。**Compile** 更新工作版本，FX 的 **Apply** 则烘焙结果。

## 用 For 重复计算分支

添加 **Utility → Loop → For Begin** 或 **For End** 会创建一对已连接的节点。
在 Begin 设置 **Iterations** 和 **Initial**，在循环体中处理 **Value**，再将新值
连接到 End。**Index** 从 0 开始；**Result** 输出最终结果。例如 Initial=1，
每次将 Value 乘以 2，重复 4 次后得到 16。零次或负数次返回 Initial；小数次数向下取整。

循环支持嵌套，可传递数值、向量和颜色。循环体节点的预览显示第一次迭代，End
预览显示完整循环的结果。循环内可以读取预先烘焙的 Buffer，也可以在完整循环后
使用 Buffer，但不支持在每次迭代中写入整张图像。复制或删除一个端点会包含其
配对端点；复制完整循环时，还需选择循环体中的节点。

## 选择 Graph 或 Code

只要当前模式存在工作版本，就可以保存未完成或编译失败的草稿。
重新打开时会恢复之前的结果，并保留可编辑的草稿，即使原预设已不存在。
没有工作版本时，必须先成功编译才能保存 TIFF。

在 **Properties** 中，**Mode** 可选择 **Shader Graph**（默认）或 **Code**。
切换会保留节点图、代码及各自的参数值，不会在两种内容之间进行转换。
保存文档后，仍可返回任一版本继续编辑。

使用 Code 时，粘贴或编写包含 `ApplyFX` 和 `@param` 的 WhimTex HLSL，然后点击 **Compile**。
新的 Code 内容生成不透明的白色。只有编译成功后，文本修改才会改变图像。
编译失败时，之前应用的结果及参数值仍然保留；修正文本后再次点击 Compile。
已接受的参数可在 Properties 中通过共用的[着色器控件](shader-controls.md)调整，包括预览。

Code 在图层的局部坐标中生成图像。传入的颜色和 `SampleInput` 读取下文所述的 **Input**。
如需使用其他图像，声明 Texture 参数并通过 `SampleTexture` 读取。
接口细节参见 [HLSL 契约](../ShaderLayer.md#independent-graph-and-code-modes)。

切回 Shader Graph 后，节点图和参数值保持原样。选择 Code 时，已经打开的节点图窗口
为只读；切回 Shader Graph 即可继续编辑。

## 复用着色器预设

在 Properties 中点击 **Preset…**，从项目和用户库的共用目录选择 Code 或 Graph 预设。
同一预设也可用于 [Shader FX](shader-fx.md)。**Save Preset…** 将 Code 保存为 `.hlsl`，
将 Graph 保存为 `.whimtexshader`。Graph 预设保留两种模式、函数、Sub Graphs 和已收录的
include 源码，便于继续编辑。保存预设前，清除两种模式中 Texture 参数的 Layer 引用，包括未启用的引用。

关联预设只跟随已保存的 ID 和完全匹配的版本。源文件缺失时，文档仍保留可编辑的副本和节点图。
**Embed Copy** 可将其变为独立副本。保存文档可保留草稿和两种模式。
Input、Transform 和混合仍由图层设置决定；相同预设用于 FX 时，输入和坐标不同，像素结果可能不同。
改变应用位置或变换后，请检查图像。

## 选择输入图像

Properties 中的 **Input** 由 Shader Graph 和 Code 共用：

| 来源 | 结果 |
| --- | --- |
| **None** | 透明黑色；默认选项。 |
| **Texture** | 读取指定的纹理资源。 |
| **Layer** | 读取选定的图层或组。 |
| **Composite Below** | 读取下方合成，包括 Pass Through 组的外部背景，不包含当前 Shader 或其上方图层。 |

在节点图中使用 **Sampling/Sample Input** 读取图像，将 Color **Out** 接入计算。
**UV** 默认使用局部坐标，也可连接扭曲坐标。Wrap 和 Filter 与 Sample Texture 相同。
Code 使用 `SampleInput(uv)` 或 `ApplyFX` 传入的 `color`。来源缺失时返回透明黑色。
切换 Input 或 Mode 会保留已指定的来源。

处理下方图像时，选择 **Code**、**Composite Below** 和 **Crossfade**。编译 `float4 ApplyFX(float4 color) { return color; }`，然后在 **FX** 中添加效果。Opacity 混合处理前后结果，不会叠加 Alpha。节点图中可连接 **Sample Input → Output**。Pass Through 组包含外部背景；隔离或剪贴组仅使用内部内容。

## 创建与调整图像

1. 在 Layers 底部通过 **+** 添加 **Shader** 图层。
2. 打开 **Properties**，在 **Mode** 中选择 **Shader Graph**，然后点击 **Edit Graph**。
   通过节点图右键菜单中的 **Create Node** 或按 **Space** 添加节点。
3. 将计算结果连接到 **Output**，为图像提供红、绿、蓝和 Alpha 通道。
4. 为需要在图层 **Properties** 中调整的值声明参数。
   使用 **Parameter** 节点读取参数；**Constant** 提供数学常量。
5. 回到图层的 **Properties** 调整结果，无需重新搭建节点图。

新节点图已将一个 Color 参数连接到 Output。可在 Properties 中选择不透明的红色试用。
要在节点图内部设置数值，断开输入连接并编辑端口旁的字段。

端口中心位于节点内部背景的边缘，并显示在选中轮廓上方。
连线两端的引出段缩短，以适应紧凑布局。
Reroute 的端口位置保持不变。
预览和附加设置区域使用与输出端口列相同的背景。

生成的图像使用普通图层的 **Transform**、**Rendering**、**FX** 和混合设置。
通过 **Input** 和 **Sample Input** 使用共用图像；其他图像可声明为 **Texture** 参数，
再通过 **Sample Texture** 读取。Input 默认为 None。

## 数学常量与 Swizzle

**Utility/Expression** 用公式替代简短的计算链。输入 `a + b * c / 2` 后，
节点会生成 `a`、`b`、`c` 输入和 **Out** 输出。输入值为 1、2、10 时，结果为 11。
**Type** 为所有端口统一选择 Float–Float4。支持 HLSL 函数、括号、分量访问和
向量构造，例如 `saturate(a * b)`，或在 Float2 类型下使用 `float2(a.x, b.y)`。
离开文本字段后应用公式；空表达式返回零。连接按区分大小写的变量名称保留；
从公式中移除的已连接输入保持可见但不参与计算，名称恢复后重新启用。
错误显示在节点旁。包含多条语句的函数体请使用 Custom Function。

**Input/Instance Index** 和 **Input/Time** 各提供一个名为 **Out** 的 Float 输出。
两者都没有自身预览，均提供 Float 输入 **Start**（默认 `0`）和 **Scale**（默认 `1`）。
**Instance Index** 返回 **Start + 索引 × Scale**；当前索引为 `0`，因此结果等于 Start。
**Time** 将编辑器运行时间（秒）传给相关节点的预览及图窗口中的结果预览。
输出为 **Start + 时间 × Scale**。两个输入均可手动设置或连接其他节点。
Canvas 使用独立的[文档 Play/Pause 和时间跳转](preview.md#document-playback)，不会停止图的时钟。Save、普通导出和 Live Update 使用时间 `0`，因此这些图像中的 Time 等于 Start。
HLSL 节点和 Custom Function 可直接读取 `_WhimTexTime` 与 `_WhimTexInstanceIndex`。
Shader 图层的 FX 在图结果预览中使用相同的动画时间。

**Constant** 输出所选数字：**PI**、**TAU**（2π）、**PHI**（黄金比例）、**E** 或 **SQRT2**（√2），默认选择 PI。

**Swizzle** 重排或重复输入通道。在 **Mask** 中输入 1–4 个 `xyzw` 或 `rgba` 字符，不区分大小写；`_` 插入零。
例如，二分量输入使用 `_x_y` 会输出四分量向量 `(0, x, 0, y)`。
表达式长度决定输出分量数。按 Enter 或离开字段后应用修改。
输入宽度取来源宽度和表达式所需宽度的最大值：`y` 需要 Float2，`z` 需要 Float3，`w` 需要 Float4。
未连接的输入显示相应数量的可编辑分量。连接的标量会重复到各分量，较短向量以零补齐；`_` 始终插入零。

## 更多数学与图案控制

**Sign** 区分负值、零和正值；**Saturate** 将数值限制在 `0..1`。**Modulo** 用于重复数值
区间，**Atan2** 根据 Y 和 X 计算角度，**Asin** 和 **Acos** 执行反三角运算。这些角度
使用弧度。Asin/Acos 的输入须在 `-1..1` 内；**Log** 需要正值。**Exp** 和 Log 使用自然底数。
**Cross** 计算三维垂直向量。**Reflect** 和 **Refract** 根据法线反射或折射入射方向；
请提供单位向量，通过 Eta 设置折射率比。在 Select 前用 **And**、**Or** 和 **Not** 组合布尔条件。

重复噪声可在 **Perlin Noise** 等节点上启用 **Periodic** 并设置正 **Period**。**Scale** 控制密度，
在 **Position** 前连接 **Add** 来平移图案。**Cellular Noise → Distance to Edge** 输出归一化边界距离；对 Value 设置阈值可绘制单元边线。
**Flipbook** 根据 Columns、Rows 和 Frame 选择图集单元，将 Out 连接到 Sample Texture.UV。
帧号循环，包括负值；行顺序可从下到上或从上到下，单帧内的图像方向保持不变。

**Dither** 用 Bayer 2 × 2、4 × 4 或 8 × 8 图案减少颜色级数。Levels 设置级数，PixelSize
以文档像素设置图案大小。Alpha 保持不变；HDR 和负颜色值不会自动裁剪。

## 便签与分组

右键点击节点图，选择 **Add Note** 添加可调整大小的便签，或选择 **Group Selection**
将选中的节点与便签放入一个组。未选择元素时会创建空组。这两项是独立的右键菜单操作，
不在 **Create Node** 中。可编辑组标题，将元素拖入组内，并通过拖动组标题一起移动内容。
**Ungroup Selection** 将选中元素移出组；删除组会保留其中的节点和便签。不支持嵌套组。

便签右键菜单中的 **Hue…** 打开彩虹滑块，旋转原生背景、边框和文字的色相，保持各颜色的
饱和度、亮度及其他非颜色样式不变。切换便签主题时保留色相偏移。**Reset** 恢复标准颜色。
编辑标题或正文时，保持原有字号和文本区域大小。

便签和分组随文档保存，支持 **Undo/Redo**。复制组时会包含其内容和内部连接。
它们只用于整理节点图，不改变图像。

信息、警告和错误以图标显示在相关节点右侧。将鼠标悬停在图标上，消息面板会显示在图标右侧。
多个消息共用最高严重程度的图标。整个节点图的错误仍显示在公共消息区域。
矢量图标在放大时仍保持清晰。

## 复制节点

选择节点后，使用 **Ctrl+C / Ctrl+V** 复制和粘贴，或 **Ctrl+D** 直接创建副本
（macOS 使用 **Cmd**）。节点图的右键菜单也提供这些命令。
副本保留设置、相对位置和所选节点之间的连接，包括 Reroute 和 HLSL 函数。
Sub Graph 会携带嵌套定义；在同一节点图中复用原定义，其他节点图则获得独立副本。
Output 和 Sub Graph 的接口节点不会被复制。粘贴和创建副本支持 **Undo/Redo**。
副本位于当前节点图视图的中心。居中使用节点、便笺和分组框的整体边界矩形，并计入其尺寸。
重复粘贴仍然保持居中，不会逐次偏移。

在同一节点图中，Parameter 副本继续使用原参数；粘贴到其他节点图时，会复制所需参数及其值。
粘贴到其他文档后，需要重新指定来自图层的纹理输入。编辑字段时，复制命令处理字段文本。
脚本重新编译后，复制和粘贴快捷键仍然可用，包括重新开始复制节点。
节点剪贴板也会保留，直到关闭编辑器。

## 选择节点

**Canvas Position** 提供文档坐标：**Normalized(2)** 为 `0..1`，**Aspect X(2)** 将 X
乘以宽/高，**Aspect Y(2)** 将 Y 乘以高/宽。三者都以画布左下角为原点。
在 2:1 画布上，Aspect X 的范围为 `0..2 × 0..1`，Aspect Y 为 `0..1 × 0..0.5`。
图层变换不会移动这个坐标场。**Canvas Data** 提供 **Size(2)**、**Texel(2)** 和
**Pixel Scale(1)**；Pixel Scale 来自 Frame 工具的文档像素倍率，不受节点图缩放或预览分辨率影响。

| 节点 | 用途 |
| --- | --- |
| **Parameter**、**Constant** | 读取图层可调参数，或使用 PI、TAU、PHI、E、SQRT2 数学常量。 |
| **UV**、**Canvas Position**、**Canvas Data**、**Texture Size** | 获取局部或画布坐标、文档尺寸与像素倍率，或输入纹理尺寸。 |
| **Sample Texture** | 按 UV 读取图像；更改坐标可产生扭曲。 |
| **Sample Gradient**、**Sample Curve** | 将数值映射为渐变颜色，或用曲线调整数值。 |
| **Split**、**Combine**、**Swizzle** | 分离、组合或重排向量/颜色通道。 |
| **Add**、**Subtract**、**Multiply**、**Divide** | 偏移、缩放和组合数值。 |
| **Min**、**Max**、**Clamp**、**Abs**、**One Minus**、**Negate** | 限制数值、取绝对值或反转数值。One Minus 计算 `1 − x`。 |
| **Power**、**Square Root**、**Floor**、**Ceil**、**Round**、**Fraction** | 调整过渡形状、创建阶梯或重复数值区间。 |
| **Sine**、**Cosine** | 创建波形和重复图案。 |
| **Lerp**、**Remap**、**Step**、**Smoothstep** | 混合数值、转换范围并生成硬边或柔和阈值。 |
| **Unlerp** | 计算 T 在 A–B 范围中的位置：`(T-A)/(B-A)`，不限制输出范围。默认 A = 0、B = 1、T = 0。支持逐分量向量计算；两端相等时结果未定义。 |
| **Range Mask** | 选择 Center 周围的数值。Width 是包含两侧向内 Feather 过渡的完整宽度；Falloff 可选 Linear 或 Smooth。向量各分量独立处理；若需按颜色亮度选择，先使用 Luminance。 |
| **Length**、**Distance**、**Dot**、**Normalize** | 计算距离与方向。 |
| **Compare**、**Select** | 根据条件在两个值之间选择。 |
| **Transform UV** | 平移、缩放或旋转供后续节点使用的坐标。 |
| **Apply Transform 2D** | 通过 Transform2D 参数，将 UV 转入局部框架或映射回输入空间。 |
| **Pixelate UV** | 将坐标吸附到网格中心，再采样纹理或生成图案。 |
| **Perlin Noise**、**Cellular Noise**、**Random** | 根据坐标与 Seed 生成噪声、单元距离或可重复的随机值。 |
| **Checkerboard**、**Stripes**、**Grid**、**Rings**、**Gradient Factor** | 创建边缘平滑的图案遮罩，或生成渐变采样位置。 |
| **Shape**、**Combine Distance** | 创建形状遮罩并组合有符号距离。 |
| **Polar Coordinates**、**Repeat**、**Mirror**、**Kaleidoscope** | 变换或折叠坐标，让图案重复或镜像。 |
| **OpenSimplex2 Warp Noise**、**Vector Displacement**、**Twirl**、**Bulge / Pinch**、**Wave**、**Ripple** | 在生成图案或采样图像前扭曲坐标。 |
| **Hue**、**Saturation**、**Exposure**、**Lightness**、**Contrast**、**Levels**、**Color Balance**、**Posterize** | 调整颜色、亮度与色阶数量。 |
| **Convert Color**、**Luminance**、**Gradient Map**、**Color Curves** | 转换颜色分量或重新映射图像色调。 |
| **Color Mask**、**Hue Range**、**Mix Color**、**Set Alpha** | 选择颜色、混合颜色并控制透明度。 |
| **Channel Mixer**、**White Balance** | 混合 RGB 通道，或调整 Temperature 和 Tint，同时保留 Alpha 与 HDR。 |
| **Directional Blur**、**Radial Blur** | 沿直线、绕中心旋转或沿中心径向模糊连接的分支。 |
| **Height Blend** | 混合高度，并用同一遮罩混合对应颜色。 |
| **Tile UV**、**Perspective UV** | 在单元格中重复图案，或将图像映射到四边形。 |
| **Gabor Noise**、**Rotate Vector**、**Blackbody** | 生成定向纹理细节、旋转向量或按温度生成颜色。 |
| **Slope Blur**、**Bilateral Blur** | 沿高度坡面涂抹，或在保留引导边缘的同时柔化细节。 |
| **Height to Normal**、**Normal Blend**、**Height to Distortion**、**Normal to Distortion** | 将纹理或高度计算转为法线或位移，并叠加法线细节。 |
| **Blend** | 按混合模式与 Alpha 合成颜色。Mix Color 对颜色插值，Lerp 对数值分量插值。 |
| **Output** | 输出最终 RGBA 图像。 |

要创建彩色噪声，连接 **UV → Perlin Noise (Position → Value) → Sample Gradient → Output**，并向 Sample Gradient
提供 **Gradient** 参数。之后在 Properties 中调整渐变。
要创建阈值遮罩，在 Perlin Noise 后接 **Smoothstep**，再通过 **Combine** 将其结果设为 Alpha。

**Pixelate UV** 的 **CellSize** 默认 `(8,8)`，**Offset** 默认 `(0,0)`，单位均为文档像素。
每个轴的最小单元尺寸为一个像素。将 Out 连接到纹理或图案的 UV 输入。
**Channel Mixer** 的 Red、Green、Blue 向量指定各 RGB 通道的贡献；`1` 表示完整贡献。
默认设置保持原色，也支持负权重与 HDR。**White Balance** 的 Temperature 和 Tint 默认均为 `0`，
范围为 `-1..1`。正 Temperature 使颜色偏暖，负值偏冷；正 Tint 偏洋红，负值偏绿。

**Directional Blur** 的 Radius 使用文档像素，Angle 使用度数；路径向两侧各延伸 Radius。
**Radial Blur** 提供 **Spin**（绕 Center 旋转 ±Angle）和 **Zoom**（半径乘以 `1 ± abs(Strength)`）。
默认 Strength = `0.1`，对应半径的 `0.9..1.1` 倍。不适用的字段仍显示，但不可用。
两种模糊均正确处理 Color 的透明度，避免隐藏 RGB 形成色边。**Samples** 默认为 `17`，
向上取整为 `3..129` 之间的奇数。较长路径或细节需要更多 Samples，计算成本也随之增加。

未连接的 **Texture**、**Gradient** 和 **Curve** 输入也提供宽 108 像素的内嵌字段：
Texture2D 资源选择器、渐变编辑器和原生曲线编辑器。连接时隐藏字段并保留数值，断开后恢复。
这些数值保存在文档和工作节点图中，修改会更新预览和 FX。Sub Graph 输入和 Color Curves 同样支持。
未指定纹理时采样白色；渐变和曲线的默认值与节点图参数一致。

Sample Gradient 和 Sample Curve 的 **T** 输入可选择 **UV.x**、**UV.y** 或 **Custom** 数值。
1D 噪声的标量 Position 也支持这些选项。新节点默认使用 UV.x；已有数值和连接保持不变。
Sample Gradient 和 Sample Texture 提供颜色输出 **RGBA** 及标量通道 **R**、**G**、**B**、**A**。
已有完整颜色连接继续使用 RGBA。

未连接的数值输入字段显示在节点主体外、端口左侧，可直接编辑，且不会遮挡端口连线。
连接后字段隐藏，断开后重新显示。未连接的 UV 端口保留隐式的当前像素坐标（`PixelUV`）：
通过端口旁的 **Custom / UV** 选项，可输入自定义坐标或使用 UV。
单个 Float 字段也有 **X** 标签。水平拖动标签即可调整数值，与向量分量的操作一致。
根据节点，不适用于当前模式的输入会隐藏，或连同字段一起禁用。例如，Warp 的 **Fractal = None** 时
Octaves、Lacunarity 和 Gain 不可用；独立 Fractal 节点的 PingPongStrength 仅在 Ping Pong 模式下可用。
悬停端口可查看原因。禁用的端口（包括 Split 中不存在的分量）不能建立新连接。
设置隐藏已连接的端口时，端口仍显示为灰暗状态并保留连线；切回原设置后连接恢复生效。
未连接的隐藏端口不显示。输出暂时不可用时，接收输入使用已保存的数值或默认值。

从输入或输出端口拖出连线并放到图中的空白处，即可搜索兼容节点。菜单在光标右侧打开，左边缘与光标对齐。
从输出拖动时，列出兼容输入，例如 `Multiply(A)` 与 `Multiply(B)`；从输入拖动时，列出兼容输出，
例如 `Cellular Noise(F1)` 与 `Cellular Noise(F2)`，也包括匹配的参数和 HLSL 函数。
选择后会在放下位置创建节点并连接指定端口；已连接的输入会用新连线替换旧连线。
一次 Undo 即可撤销创建和连接操作。取消搜索不会改变图。

## 混合高度与放置表面细节

**Height Blend** 混合 **Bottom** 与 **Top** 高度，输出 **Height** 和 **Mask**。
将 Height 用于表面计算，Mask 用于混合对应颜色。Offset 抬高或降低 Top，Smoothness 柔化过渡，
Opacity 减少 Top 的贡献。默认值为 Bottom `0`、Top `1`、Offset `0`、Smoothness `0.1`、Opacity `1`。
负值和 HDR 高度会保留。

**Tile UV** 在 **Grid** 或 **Staggered** 单元格中重复图案。将其 **UV** 输出连接到图案的 UV 输入，
并用 **Mask** 乘以图案的 Alpha。**Count** 默认为 `(4,4)`，**Offset** 以单元格为单位。
**RowOffset** 默认为 `0.5`，仅在 Staggered 中偏移交替行。**RandomRotation** 设置随机旋转角度范围，
单位为度；**RandomScale** 在 `0..1` 范围内控制缩放变化。两者默认均为 `0`，**Seed** 默认为 `1337`，
使变化可重复。**Cell**、**Center** 和 **Random** 分别提供单元格坐标、图像 UV 中的单元格中心和
每格一个随机值。图案保持在原单元格内；旋转或放大后超出的部分会被裁切，不会覆盖相邻单元格。

**Perspective UV** 将图像放入四个角点：**BottomLeft**、**BottomRight**、**TopRight** 和 **TopLeft**，
默认分别为 `(0,0)`、`(1,0)`、`(1,1)` 和 `(0,1)`。将 **UV** 输出连接到图像采样器，
用 **Mask** 乘以采样结果的 Alpha，以隐藏四边形外部区域。映射保留透视关系。
退化、自相交或非凸四边形输出零 UV 和零 Mask。

**Procedural/Noise** 中的 **Gabor Noise** 在 1D/2D/3D 中创建定向细节。
**Frequency=2** 控制振荡频率，**Bandwidth=1** 控制包络宽度，**Anisotropy=0.8** 控制方向一致性，
向量 **Direction**（默认沿 X）控制朝向。**Value**、**Phase**、**Intensity**、**Phasor** 共用一次噪声计算。
**Phasor** 产生对比度更均匀的条带。**Profile: Sine / Triangle / Pulse** 选择轮廓；
**Phase Offset=0** 按周期移动条带，增加 1 后重复。**Width=0.5** 设置 Pulse 的亮部占比，
0 为全黑，1 为全白。其他轮廓隐藏未连接的 Width；已连接时保持可见但禁用，数值和连线保留。
这些设置只影响 Phasor。Filtering 将过细条带平滑至轮廓平均值；真实相位奇点仍存在，
像素内接近信号零点的位置会淡化。
**Gradient** 及 2D/3D 的 **Curl** 保留带符号强度。采样滤镜会增加计算开销。

**Vector** 中的 **Rotate Vector** 将 **In** 旋转 **Angle** 度，保留向量长度。
**2D** 使用两个分量；**3D** 使用三个分量，并增加默认 `(0,0,1)` 的 **Axis**。
In 初始沿 X 方向，Angle 初始为 `0`；零 Axis 保持 In 不变。
若要围绕指定中心旋转 UV，先减去中心坐标，旋转后再加回。

**Color/Generation** 中的 **Blackbody** 在 **Out** 输出近似的线性 RGB 温度颜色，Alpha 为 `1`。
**Temperature** 默认为 `6500` 开尔文，限制在 `1000..40000`：较低值呈暖红或橙色，较高值呈冷蓝色。
温度控制归一化颜色；亮度通过 Multiply 或 Exposure 单独调整。

## 创建程序化图案与扭曲

在 **Create Node** 的以下嵌套分类中查找节点。从连线创建节点时也使用相同的分类分支：

| 分类 | 节点 |
| --- | --- |
| **Procedural/Noise** | Perlin Noise, Value Noise, Value Cubic Noise, OpenSimplex2 Noise, OpenSimplex2S Noise, Flow Noise, Cellular Noise, Triangle Voronoi Noise, Gabor Noise, Spots Noise, Shard Noise. |
| **Procedural/Noise/Warp** | OpenSimplex2 Warp Noise, OpenSimplex2 Reduced Warp Noise, Basic Grid Warp Noise. |
| **Procedural/Noise/Grain** | White Noise, Blue Noise. |
| **Math/Random** | Random. |
| **Sampling** | Buffer. |
| **Procedural** | Fractal. |
| **Procedural/Pattern** | Checkerboard、Stripes、Grid、Rings、Gradient Factor。 |
| **Procedural/Shape** | Shape、Combine Distance。 |
| **UV** | Transform UV、Apply Transform 2D、Polar Coordinates、Repeat、Mirror、Kaleidoscope。 |
| **UV/Distortion** | Vector Displacement、Twirl、Bulge / Pinch、Wave、Ripple。 |

噪声节点使用共享的 WT 库。**Dimensions** 选择线状 **1D**、平面 **2D**（默认）或体积 **3D**。
未连接的 **Position** 使用像素 UV；1D 使用 X，3D 使用 UV 加可单独编辑的 **Z**（默认 0）。
3D 中 UV 选择器占据 X/Y 的位置，Z 保留原生数值字段并支持拖动标签调整。
切换 UV/Custom 或 Dimensions 会保留 Z。
**Scale=8** 控制每轴密度，在 **Position** 前连接 **Add** 可在缩放前平移图案。**Periodic** 默认关闭。启用后显示每轴默认为 1 的 **Period**；正值指定重复周期，非正值关闭该轴。关闭时省略周期计算；已连接的 Period 端口保留显示但停用，值和连接均保留。**Seed=1337** 选择可重现的变化。
切换 Dimensions 保留数值和所有连接；已连接的 Curl 在 1D 中仍显示，但处于禁用状态。
**Filtering** 默认关闭；开启可减少无法分辨的细节，在采样滤镜内部也生效。
**Triangle Voronoi Noise** 固定为 **2D**，没有 Dimensions 选择器，坐标和各轴设置始终为双分量。

标量噪声的首个输出 **Value** 是用于遮罩和渐变的 `0..1` 场。Perlin、Value、Value Cubic、
OpenSimplex2/2S、Flow 和 Gabor 还提供 **Gradient**，并在 2D/3D 中提供 **Curl**。
这些带符号向量保留场的强度。**Flow Noise** 的 **Phase=0** 平滑旋转梯度；
Phase 以周期计，0 和 1 产生相同图案。

**Shard Noise** 生成棱面图案。**Sharpness=0.7** 控制从 0 的柔和过渡到 1 的锐利碎片。
**Value** 输出支持共享的 1D/2D/3D、Period 和 Filtering 设置。要制作动画，选择 3D
并改变 Position Z。

**Triangle Voronoi Noise** 在 2D 中生成棱角分明的单元，并不生成三角网格；
单个单元也不一定是三角形。**Distribution** 可选
**Uniform**（默认）或 **Fixed Radius**，**Jitter=1** 移动特征点，**Stretch=(1,1)**
沿 X/Y 改变单元形状。Stretch 的每个轴分量取绝对值后限制在 `0.5..2`，Jitter 限制在 `0..1`。
Jitter=0 时可能出现三角规函数值相等的平台区域，其内部不视为边界。
对归一化边界场 **Value**（`0..1`）设置阈值可绘制单元边线。
**Distance** 是到所选特征点的未归一化欧几里得距离，**CellValue** 是该点可重现的值，
**ToPoint** 指向该点，**BorderNormal** 是指向最近边界的单位向量，退化时为零。
这些辅助输出不经过过滤；节点没有 Gradient/Curl 输出。

要叠加细节，将结果连接到 **Procedural** 中独立的 **Fractal** 节点。
它在图的 2D 坐标中重复数值或 Color 分支；即使输入包含 1D/3D 噪声，也不等于特定 WT
噪声系列的精确分形。**Out** 保留 **In** 的类型。标量噪声节点不再提供内嵌分形或倍频层输入。
**FBm**（默认）混合细节并保留负值/HDR。**Ridged** 用 `1-abs(2*v-1)` 生成脊线，
**Ping Pong** 用 `1-abs(frac(v*max(strength,0))*2-1)` 折叠信号。
归一化加权混合按分量进行，Color 的 Alpha 也参与。
**Octaves=3** 控制层数（转为整数后限制 `1..8`），**Lacunarity=2** 控制频率步进（`0.01..16`），
**Gain=0.5** 控制振幅步进（`0..1`），**Weighted Strength=0** 控制之前值对振幅的调制（`0..1`）。
**Ping Pong Strength=2** 控制折叠；其他模式下未连接时隐藏，但保留数值与连接。
**Filtering** 默认开启，依据局部 UV 像素足迹衰减倍频层；源噪声的过滤独立生效。
**Seed Step=1** 逐层偏移所有噪声节点的种子，包括 White/Blue 颗粒。
自定义 HLSL 函数中的任意 seed 参数不会自动变化。

**Multifractal** 通过乘法产生不均匀细节。**Hybrid Multifractal** 混合平滑区域与粗糙山峰；
**Hetero Terrain** 根据已形成的高度添加细节。**Ridged Multifractal** 对脊线平方并在层间反馈，
原有 **Ridged** 的行为不变。这些模式以 0..1 噪声为输入，在内部转换为以零为中心的信号，
输出未经归一化的高度，可能超出 0..1。**Bias=1** 调整 Hybrid/Hetero 的高度基准或
Ridged Multifractal 的脊线基准。**Feedback=2** 控制 Hybrid/Ridged Multifractal 的层间反馈；
**Gain** 仍控制振幅衰减。这四种模式不使用 Weighted Strength。不适用且未连接的控件隐藏；
已连接端口保持可见但不可用，数值与连接均保留。

使用 **Sampling** 中的 **Buffer** 可先计算昂贵分支，再采样结果。
**In** 接受数值/Color，**Out** 保留其类型。**UV** 默认使用当前像素 UV；**Wrap** 和 **Filter** 控制采样。
**Resolution → Canvas** 使用当前渲染目标尺寸，因此节点预览使用预览分辨率。
**Custom** 使用精确 **Size**（初始 `512 × 512`）。缓冲保留 float32 精度、负值和 HDR，
复用已分配的纹理并在源变化时更新，生成的像素不会保存到文档。
外部 RenderTexture 或自定义函数源会在每次所属对象渲染时刷新；嵌套缓冲按依赖顺序计算。
Buffer 的 Generated HLSL 依赖图管理的中间纹理和渲染通道；仅复制代码不会创建这些资源。

生成 Buffer 图像的渲染通道与读取其纹理的着色器分别编译。未改变的已编译程序会被复用，
因此只修改一侧不会自动要求另一侧重新编译。输入或时间变化仍会重绘受影响的缓冲纹理，
即使着色器无需重新编译。此行为适用于显式添加的 Buffer 节点；不会自动插入缓冲，
也不需要新的节点设置。

**Buffer → Fractal** 必须使用未连接的 **Seed Step=0**，因为缓冲图像无法逐层改变种子。
已连接的 Seed Step 即使当前输出零也会被拒绝。**Fractal → Buffer** 可以保存完成后的分形结果。
Buffer 只保存上游场在局部 `0..1` 范围内的图像。Fractal 超出此范围采样时使用 Buffer 的
**Wrap**：默认 **Clamp**；需要平铺各层时选择 **Repeat**。放大同一张固定缓冲图像不会产生新细节。
Warp 保留 **None**、**Progressive**、**Independent**，它们属于独立的域扭曲操作。

Cellular 和 Triangle Voronoi 的 **Evolution=0**
可让特征点在 Jitter 范围内连续移动。连接 Time 即可制作动画；负值沿相反方向推进，
零保留原始图案。它没有统一的 0..1 循环。Fixed Radius 在 2D/3D 中保持旋转半径，
在 1D 中则在两个端点之间移动。两种噪声使用相同 Evolution 和特征点设置时保持对应。
Evolution 在图像中恒定或具有相同空间周期时，无缝平铺仍然成立。

**Cellular Noise** 统一单元值、距离图案和单元边界。
**Output** 可选 **Cell Value**、**F1**（默认）、**F2**、**Add**、**Subtract**、
**Multiply**、**Divide** 或 **Distance to Edge**。**Smoothness=0** 保留硬边图案；
增大它可混合相邻单元值或柔化距离场的脊线。
**Metric** 可选 **Euclidean**、**Euclidean Squared**、**Manhattan**、**Hybrid**；
仅在 Distance to Edge 模式下隐藏，该模式使用欧氏边界。
**Distribution** 选择 **Uniform** 或 **Fixed Radius**，**Jitter=1** 控制点偏移。
独立的 **F1**、**F2**、**CellValue**、**ToPoint** 输出保留未平滑、未过滤的几何。
CellValue 可重现，但不是唯一 ID。

**Spots Noise** 创建柔和斑点。**Density=1** 控制数量，**Radius=0.35** 控制大小，
**Softness=0.5** 控制边缘，**Elongation=1** 控制拉伸，向量 **Direction**（初始沿 X）控制朝向。

三个 **Procedural/Noise/Warp** 节点共用 Position、Scale、Period、Seed，**Strength=0.1** 控制位移。
分形为 **None**、**Progressive**（每层弯曲下一层的坐标）或 **Independent**（每层采样原始位置），
使用 Octaves、Lacunarity 和 Gain。**Displacement** 是带符号偏移，**Position** 是输入加偏移。
将 Position 接到后续噪声或图像采样节点；Strength=0 保持原坐标。
Warp 没有 Weighted Strength 和 Ping Pong Strength。

**Procedural/Noise/Grain** 中的 **White Noise**、**Blue Noise** 支持 **1D** 条带和 **2D** 颗粒。
**Position** 始终为双分量 UV；使用 **Add** 平移。若位移以像素计，先除以 **ImageSize**。**Grain Size=1** 以像素计，
**Direction=0** 以度计，**Seed=1337** 选择变化。**Image Size** 默认用 **Canvas** 画布尺寸，可选 **Custom** 输入尺寸；**Inverted** 默认关闭。
**Value** 为单色，**Color** 为独立 RGB 且 Alpha 为 1。颗粒没有分形，Direction 仅用于 1D。Gabor/Spots Direction、Anisotropy、Elongation 仅用于 2D/3D。

旧 Noise、Cellular、Domain Warp、Tileable Noise、Voronoi Edge 和旧 Gabor 节点不受支持，
也不会自动转换；请使用当前节点重新创建。独立 Noise 图层的行为保持不变。

**Random** 未连接的 Coordinates 默认使用当前像素的 **UV**。选择 **Custom** 可设置常量坐标；
连接 **Repeat.Cell** 则让每个重复单元使用一个随机值。已保存的常量坐标保留为 Custom，可切换到 UV。

**Shape** 提供 Ellipse、Rectangle、Rounded Rectangle、Polygon、Star、Capsule 和 Line Segment。
**Out** 是平滑遮罩；**Distance** 在形状内部为负，外部为正。
Polygon 和 Star 沿单轴拉伸时，仍保持均匀的距离和 Feather 宽度。
将两个 Distance 输出连接到 **Combine Distance**，再使用其 **Mask** 作为覆盖度。
可选择 Union、Intersection 或 Subtract（从 A 中减去 B）；Smoothness 平滑形状接合处，
Feather 柔化遮罩边缘。Capsule 使用 Center、Size 和 Rotation；Line Segment 使用 Start、End
和 Width。切换 Shape 模式会更换控件，并删除连接到已移除输入端口的连线。
椭圆距离是近似值，因此根据它生成的描边宽度可能不均匀。

要创建彩色渐变，连接 **Gradient Factor.Out → Sample Gradient.T**，将 **Gradient** 参数
连接到 **Sample Gradient.Gradient**，再将采样颜色连接到 **Output**。
Checkerboard、Stripes、Grid、Rings 和形状遮罩的边缘均经过平滑处理。
**Stripes**、**Rings**、**Wave** 和 **Ripple** 提供独立的 **Phase** 输入，以周期为单位移动图案。
可以用参数控制 Phase，或连接 **Time** 来查看图预览中的动画。
Phase 每个周期重复，也支持负值。Grid 的 Offset 在每个轴上以 1 为周期，
Checkerboard 则以 2 为周期。较大的偏移仍可保持均匀的填充和清晰的图案边缘。
Stripes 提供 Square、Triangle 和 Sine 波形，Width 仅影响 Square。
Angle、Rotation、图案/颗粒 Direction 和 Twirl 的 Strength 均以度为单位；Gabor/Spots Direction 是向量。

将 UV 节点的 **Out** 连接到下一个图案节点或 **Sample Texture.UV**。
**Repeat.Cell** 包含单元坐标，也可以为负。**Input/Instance Index** 使用独立索引（当前为零），与 Cell 无关。
**OpenSimplex2 Warp Noise** 通过 **Displacement** 输出偏移，
通过 **Position** 输出偏移后的坐标。**Strength = 0** 时，扭曲节点保持输入坐标不变；
位移向量保留自身长度。UV 可以超出 `0..1`，图像边界外的采样方式由 Sample Texture
的环绕设置决定。Transform UV 改变节点图内部的坐标，与图层的 Transform 独立。

**Vector Displacement** 的 **Signed RG** 模式直接使用位移向量；**Packed RG** 从 `0..1`
解码，其中 `(0.5,0.5)` 表示零位移。Strength 分别缩放 X 和 Y。
**Bulge / Pinch** 的 Strength 为正时放大采样图案，为负时收缩图案。
**Polar Coordinates** 可将坐标转换为以圈数表示的角度和距 Center 的两倍距离，也可转换回 UV。
**Mirror** 将所选轴折叠到 Center 的正方向一侧；**Kaleidoscope** 重复径向扇区。
准确的端口与模式见[程序化节点契约](../ShaderLayer.md#procedural-and-uv-nodes)。

## 调整颜色与透明度

要改变纹理颜色，连接 **Sample Texture → Hue → Saturation → Output**。
这些调整节点位于 Create Node 的 **Color/Adjustments** 分类。先保留 **Degrees = 0**
和 **Amount = 1**，再调整色相角度，或将饱和度降至零以获得灰度图像。
这些调整会保留输入颜色的透明度。

| 控件 | 图像变化 |
| --- | --- |
| **Exposure / Stops** | 调亮或调暗线性 RGB；+1 将其加倍，−1 将其减半。 |
| **Lightness / Amount** | 在 −100 到 100 之间调整明度；0 保持原值。 |
| **Contrast / Amount** | 围绕 0.5 调整对比度；1 保持原色。 |
| **Levels / Input Range, Gamma, Output Range** | 设置黑白点和中间调。Luminance 整体调整亮度，RGB 分别调整通道。关闭 Clamp 可保留超出 Input Range 的数值。 |
| **Color Balance / Shadows, Midtones, Highlights** | 为暗部、中间调和亮部调色。Range 控制各区域的范围；Preserve Luminance 保留亮度。 |
| **Posterize / Levels** | 将平滑过渡变为阶梯，至少使用两个级别。 |

要用渐变重新着色，添加 **Color/Mapping/Gradient Map**，将图像连接到 Color，
将 **Gradient** 参数连接到 Gradient，再选择 Channel。Luminance 将暗到亮的色调映射到渐变。
默认 Preserve 模式保留图像 Alpha；Multiply 将它与渐变 Alpha 相乘；Replace 仅使用渐变 Alpha。

**Color Curves** 使用 Curve 参数调整色调。Master 先调整整体 RGB 或亮度，
Red、Green 和 Blue 再调整各通道。未指定某条曲线时不改变对应值，可使用内嵌编辑器或连接 Curve 参数。
透明度仅通过 Alpha 曲线改变。恒等曲线可保留高于白色和低于黑色的数值。

要选择颜色，添加 **Color/Selection/Color Mask**，设置 Key Color，并调整 Tolerance
和 Softness。Chroma 比较色度，RGB 也考虑亮度差异，Perceptual 比较 Oklab 差异。
**Hue Range** 按度数选择色相区间，例如 `(330,30)` 会跨过红色。Feather 柔化边缘，
Saturation Range 排除或淡化接近灰色的区域。两个节点均输出遮罩，白色表示选中；
图像 Alpha 不会影响遮罩。将它连接到 **Color/Alpha/Set Alpha.Alpha**：Replace 直接使用遮罩，
Multiply 同时保留源图透明度。Set Alpha 不改变 RGB。

**Color/Mixing/Mix Color** 按 T 混合 A 和 B，默认选择 Perceptual。Linear RGB 和 sRGB
会产生不同的中间颜色。T = 0 得到 A，T = 1 得到 B，超出范围则进行外推。
Alpha 也会插值。需要按 Alpha 合成时，使用 **Blend**。

颜色端口的连线不会自动转换颜色空间。**Color/Conversion/Convert Color** 可显式转换
Linear RGB、sRGB、HSV、HSL 和 Oklab；在普通颜色处理或 Output 前，应先转回 Linear RGB。
未连接的输入在 Linear RGB 模式下显示颜色选择器，其他空间则显示原始 Float4 数值字段。
HSV 和 HSL 基于 sRGB 编码的 RGB，色相以圈数表示。HSV 会丢失负 RGB，但保留 HDR 亮度；
HSL 限制在 `0..1`，会丢失 HDR 和负 RGB。Linear RGB/sRGB/Oklab 则可保留这些值。
**Luminance** 从线性 RGB 提取亮度。颜色调整通常保留 HDR 和负值；Levels 提供显式的
Clamp 开关，Set Alpha 不限制透明度。需要限制范围时添加 Clamp，并在图层 Rendering
中选择所需的颜色范围。

## 创建法线与位移

要生成法线贴图，添加 **Normal/Height to Normal**。使用默认的 **Source: Texture**，
将 Texture 参数连接到 Height，再选择 Channel。此时 Radius 以源纹理的纹素为单位；
Wrap 和 Filter 控制边缘采样。
选择 **Source: Value**，即可连接噪声、遮罩或其他标量高度计算。节点会在相邻坐标
重新计算该分支，此时 Radius 以文档像素为单位，不受预览尺寸影响。**Height to Distortion** 也支持这两种来源。
Strength 调整凹凸强度，Radius 调整采样距离。两种来源均支持 Scharr、Sobel 和
Central Difference；Flip X 和 Flip Y 反转对应的倾斜方向。使用固定坐标的分支会产生恒定高度。

**Packed RGB** 的平坦法线为 `(0.5,0.5,1)`，适合显示为法线贴图；**Signed XYZ** 则为
`(0,0,1)`。Height to Normal 的 **Output Encoding** 应与后续节点的 **Normal Encoding** 一致；
节点图不会对这些向量进行颜色转换。
**Normal Blend** 通过 Reoriented 或 Whiteout 合并 Base 和 Detail，Strength 控制细节强度。
输出经过归一化，并使用选定的编码。

要扭曲图像，使用 **UV/Distortion/Height to Distortion** 读取高度纹理，或用
**Normal to Distortion** 读取法线向量。将其输出连接到 **Vector Displacement.Displacement**，
在两个节点中选择相同的 Signed RG 或 Packed RG，再连接
**Vector Displacement.Out → Sample Texture.UV**。前两个节点输出偏移量，最终 UV 由
Vector Displacement 生成。位移向量保留强度，不会归一化。Normal to Distortion 可分别缩放 X 和 Y。
Signed RG 的零位移为 `(0,0)`，Packed RG 为 `(0.5,0.5)`。
Height to Normal 的 XY 指向高度梯度的反方向，Height to Distortion 则沿梯度方向。
如果法线产生的扭曲方向相反，可反转 Strength 的符号或使用 Flip X/Flip Y；
法线经过归一化，因此位移强度也会与直接使用高度时不同。
准确的输入与模式见[颜色与法线节点契约](../ShaderLayer.md#color-and-normal-nodes)。

**Normal Encode** 将有符号法线转为 Packed RGB，**Normal Decode** 执行反向转换；
这两个节点不进行归一化。**Normal Strength** 在 0 时将法线压平，在 1 时保留单位法线，
负值会反转 XY。需要着色时，将结果连接到 **Normal Lighting.Normal**，匹配 Normal Encoding，
再调整表面 Color、LightDirection、LightColor、Intensity 和 Ambient。
将 **SpecularIntensity** 从默认的 0 调高可添加高光；SpecularColor 控制颜色，Roughness
扩大高光，SpecularOnly 只显示高光部分。表面 Alpha 保持不变。

**Premultiply** 将 RGB 乘以 Alpha；**Unpremultiply** 在 Alpha 非零时恢复直通 RGB，
Alpha 为零时返回黑色 RGB。Alpha 本身不变。Output 需要直通颜色，因此使用预乘颜色
完成计算后，应通过 Unpremultiply 再连接到 Output。

## 对计算结果应用滤镜

会重复计算来源的输入即使未连接，也以 **↻** 和一个空格开头，与标签正文共用字体和基线。
Buffer、Caustics 和 Caustics Noise 的输出标签后方、连接点前方显示圆柱形存储图标，
未连接时也可见。它表示从中间纹理读取数值，并共享标签的悬停高亮、选择和拖动行为。
悬停在任意已连接输入的标签或图标上会高亮来源分支，不弹出工具提示。
输出标签则沿有效连接高亮使用该输出的下游节点，不包含起始节点、
它的其他输出分支或消费者的其他来源。输出标签（包括参数节点）同样支持点击选择和拖动。
按下后不移动鼠标并松开，即可选择该分支的节点；**Ctrl+点击**将节点添加到当前选择，
macOS 也支持 **Command+点击**。从标签拖动会移动节点，并取消分支选择。
端口连接点仍用于连线；悬停在未连接端口的标签上不会点亮连接点。
移开鼠标会取消悬停高亮，但保留选择。
Buffer 输出旁的短线表示缓存边界：不高亮缓存的来源分支，但仍高亮其 UV 计算。
连接的参数和数学常量也包含在来源分支中。

将图案或 **Sample Texture.Out** 连接到 **Blur.In**，再将 Blur.Out 连接到 Output，即可柔化
图像。**Sharpen** 增强局部对比度，**Edge Detection** 提取变化边缘，**Dilate** 扩大亮区，
**Erode** 缩小亮区。Blur 会考虑 Color 的 Alpha，避免透明像素中的隐藏颜色产生光晕；输入和输出均为直通 RGBA。
Sharpen 和 Edge Detection 保留 Color 的 Alpha。数值向量及 Dilate/Erode 仍逐分量处理；需要统一轮廓时，连接标量遮罩。

Radius 以文档像素为单位设置采样距离。Blur、Sharpen、Dilate 和 Erode 的 **Minimum Kernel**
可选 **3 × 3**、**5 × 5** 或 **9 × 9**。较大半径会自动增加采样，使间距不超过一个像素；
更密的最小核有助于捕捉亚像素细节。Edge Detection 使用固定的 3 × 3 Sobel 核。Strength 调整 Sharpen 和
Edge Detection 的强度。结果可以超出 `0..1`；需要限制遮罩范围时，添加 Clamp 或 Saturate。

滤镜在相邻坐标重新计算输入分支。上游图案需要随 UV 变化；固定坐标只会得到恒定结果。
大半径，尤其是多个滤镜串联时，可能使预览变慢。节点预览和完整图层渲染使用相同的文档像素尺度。
Radius 可以随图像位置变化；内置形状和图案在滤镜内仍保留抗锯齿。Radius 为零时，Blur、Sharpen、Dilate 和 Erode 保留原始输入。

**Slope Blur** 沿标量 **Slope** 场的下坡方向涂抹 **In**。将高度或噪声分支连接到 Slope；
默认的 `0` 保持图像不变。**Strength** 默认为 `8`，负值反转路径。梯度不归一化，因此坡越陡，
采样移动越远。**Samples** 默认为 `16`，取整并限制在 `1..64`；更多步数能更细致地跟随路径弯曲。
**Average** 沿路径取平均，**Min** 保留较暗值，**Max** 保留较亮值。Average 会考虑 Color 的 Alpha；
Min/Max 逐分量处理，包括 Alpha。零 Strength 保持 In 不变。此滤镜没有 Radius 控件。

**Bilateral Blur** 柔化细节，同时保留引导信号中的边缘。**Radius** 默认为 `2` 文档像素。
**Tolerance** 默认为 `0.1`：较小值保留更多引导差异，较大值允许跨越更多差异进行平滑。
负值按零处理；`0` 只接受完全相同的引导值。**Guide = Source** 使用 In 本身；**Custom** 增加
数值或 Color 类型的 **Guide** 输入，可连接独立图案，默认为标量 `0`。两种模式都使用预乘 RGBA
比较 Color 引导，忽略完全透明区域的隐藏 RGB。Guide 不会改变输出类型，输出类型只由 In 决定。
Color 滤波会考虑 Alpha；零 Radius 保持 In 不变。

两种滤镜都保留输入的数值类型或 Color 类型。Slope Blur 反复采样图像与坡面，Bilateral Blur
采样图像与引导。较大的 Samples、Radius 或多个滤镜串联可能减慢预览，输入含 Gabor Noise 时尤其明显。
深度嵌套的滤镜链在首次编译时也可能需要数分钟。

## 复用 Sub Graph

选择一组运算，在上下文菜单中选择 **Convert Selection to Sub Graph**。外部连线会成为
新节点的输入和输出。双击节点可编辑内部运算，在侧栏调整 **Inputs** 和 **Outputs**，
通过 **Back to Graph** 返回。在创建菜单的 **Sub Graphs** 中选择已有定义，即可再次使用。

编辑定义会更新当前 Shader 节点图中的所有实例。端口名称可以修改，连线使用的稳定标识
不会因此改变。定义随文档保存。Sub Graph 可以包含其他定义，但不能直接或间接调用自身。
所有实例共用的控制值使用普通参数；各实例需要不同值时，使用 Sub Graph 的接口输入。

## 移动与删除节点

右键节点并选择 **Replace Node**，从创建目录中选择替代节点。列表直接打开当前分类，
并以浅色突出当前节点。返回箭头可进入父级分类，搜索覆盖整个目录。名称与类型相同的端口保留连线和
内联输入值，其余连线会移除。新节点使用默认选项，但保留位置、选中状态和分组。
一次 Undo 可撤销整个替换。必需的 Output 和 Sub Graph 接口节点不能替换。

按 **Del** 删除选中的节点，包括 **Reroute**。**Output** 是必需节点，不能删除。
编辑文本或数值字段时，**Del** 继续编辑字段值，不会删除节点。

拖动时，已连接的输入与输出端口中心会轻微吸附到同一高度。
端口吸附不改变 X；节点矩形的常规 Y 轴吸附优先。
按住 **Shift** 可禁用吸附。一起移动的多个节点保持相对位置。

## 用 Reroute 整理连线

双击已有连线即可插入 **Reroute**，也可以在该连线的右键菜单中选择 **Add Reroute**。
Reroute 只能在已有连线上创建，不会出现在节点搜索中。

这个迷你节点没有标题、预览或参数控件。**In → Out** 原样传递来源的类型和值，
支持数值、向量、Color、Boolean、Texture、Gradient 和 Curve，不增加计算，也不改变图像。
拖动迷你节点可以调整连线路径；将 **Out** 连接到多个输入，即可让同一个值分支。
从任一端口拖动可创建连线，拖动节点主体则移动节点。没有来源的 Reroute 链视为断开的连接，不报错：每个下游输入使用自己保存的值或默认值。节点和连线保留在图中，重新连接来源即可恢复信号。

删除 Reroute 会将其上游来源直接连接到下游输入，多个 Reroute 串联时也适用。
普通的连线删除操作会断开该连接。Reroute 的位置和连接随文档保存；
插入、移动、更改连接和删除均支持 **Undo/Redo**。

## 预览节点

**Accurate Lighting** 控制节点预览中的光照。默认情况下，Global Normal Lighting 使用
一个从左上方以 45° 照射的固定白色平行光。启用工具栏或 User Settings → Shader Graph 中的
开关后，预览使用文档中的灯光，包括 Area 和 Cookie。关闭窗口或重启编辑器后仍保留此选择。
Local 保留自己的光照设置；Layer Preview、Canvas 和导出始终使用实际光照。

同一通道中执行多个 Global 光照计算时，节点会显示 Info 图标。在适合的情况下，可先用遮罩
混合表面输入，再连接一个 Normal Lighting 节点。混合已照明的结果仍会计算两条分支；
混合输入可能改变外观，尤其是镜面高光。

启用 **SVM Nodes Only** 后，Create Node、Replace Node 和拖放连线的菜单只显示
初始设置受 SVM 支持的节点。此筛选与 **SVM Preview (Experimental)** 相互独立。
修改设置或连接不受支持的上游分支后，预览仍可能使用 HLSL。

**Layer Preview** 在下一次编辑器更新时刷新，没有固定等待时间。
连续编辑会合并处理，并复用可用的 Canvas 结果；隐藏或折叠的预览不会渲染。

居中的旋转指示器表示当前预览图像正在等待更新。新图像绘制完成或报告错误后，
指示器停止。隐藏的预览不会播放指示器动画；普通的时间动画不会让它一直显示。
等待时图像会变暗，并有柔和的亮光从上向下扫过。节点标题中的青绿色闪电图标表示当前显示的预览由 SVM 渲染；
Output 标题中的标记对应结果预览所用的渲染方式。

在节点图工具栏启用 **SVM Preview (Experimental)**，可试用另一种预览渲染方式。
它用于受支持的节点预览，以及当前窗口中的 Shader 图层结果预览；图层的 FX 和剪贴效果仍会生效。
Shader FX 的结果预览、文档画布及导出继续使用原生渲染，
图的原生编译也会在后台继续进行。
首次使用时，共用 SVM 着色器的编译可能耗时较长。准备新结果时，预览保留已有图像。
此实验选项默认关闭。

原型支持数值与布尔运算、Swizzle、颜色调整、法线、UV 变换、Time，以及数值、Color
和 Boolean 参数。Perlin、Value、Value Cubic、OpenSimplex、Flow、Gabor、Cellular
和 Shard 支持 1D/2D/3D。Flow 的 Phase 可通过 Time 动画化。Gradient 保留噪声的维度，
Curl 在 2D/3D 中可用。这些噪声支持 Filtering 和 Periodic，包括 Fractal
内部的过滤源。Fractal 保留自身过滤，最多嵌套两层。
Sample Gradient 和 Sample Curve 支持端口内设置及参数输入。Blend、以纹理或数值分支为高度源的
Height to Normal/Distortion、Local Lighting 和简化 Global Lighting 也可使用 SVM。
Accurate Global Lighting 仍精确计算文档光源。若输入分支受支持，SVM 会一起准备全部
光照输入；不支持的输入仍使用 HLSL。光照结果保持不变。

还支持 Canvas Position/Data、Pixelate UV、Tile UV、Blackbody、Dither、Shape、
Combine Distance、Checkerboard、Stripes、Grid 和 Rings，包括 Fractal 内部的图案抗锯齿。
Gradient Map 和 Color Curves 保留 HDR 与 Alpha 设置。

Spots Noise 和全部三种 Warp Noise 支持 1D/2D/3D；Triangle Voronoi Noise 支持所有 2D 输出。
Filtering、Periodic、分布模式及 Warp 累积保留原生计算。White/Blue Noise 支持 1D/2D、
Value/Color、Invert 和自定义 Image Size。Edge Detection 保留 Color 输入的中心 Alpha。
Apply Transform2D 接受端口值或参数，支持 Local/Input 和投影变换。
在 SVM 限制内，这些节点也可作为 Fractal 的输入。

Texture Size、Sample Texture 和 Sample Input 也使用 SVM，保留 HDR、Alpha、独立通道、
Point/Bilinear 和边界模式。Sample Input 读取所选图层输入或当前 FX 之前的图像。
纹理参数可读取其他图层；Self 读取当前 FX 输入。不支持的组合仍回退到 HLSL。


SVM 也支持整数标量、整数向量和像素单位参数；Pixel Scale 在渲染时应用。
Sub Graph 复用原生 HLSL 的展开逻辑，保留嵌套调用、多输出和各实例的默认值。
编辑定义内部时，受支持的预览采用接口默认值；不支持的依赖仍使用原生 HLSL。

在 Shader 图层预览中，显式添加的 **Buffer** 之后，受支持的节点可以恢复使用 SVM，
即使 Buffer 的输入分支使用 HLSL。**Caustics**、**Caustics Noise** 和直接求值的
**Global Normal Lighting** 之后也支持这种继续计算。Buffer 的 Size、Wrap 和 Filter
仍决定采样结果。这不包括 Fractal 或模糊滤镜内部的 Global 光照。
其他分支、设置或超出原型限制的图会自动使用原生 HLSL。
关闭此选项可让整个窗口恢复原生预览。详见[支持范围与限制](../ShaderGraph.md#experimental-svm-preview)。

**Layer Preview** 悬浮在节点图上方，初始位于右下角。拖动标题或图像可移动它，拖动边缘或角落可调整大小。
靠近节点图视口边缘时会无间距贴边吸附，靠近角落时会同时固定到两条边，窗口大小变化时仍保持固定。
高亮边框表示固定方向；向远离边缘的方向拖动即可解除。窗口会记住位置、固定方向和尺寸。
节点图的平移与缩放不会移动预览。标题中的 **R/G/B/A** 按钮仅切换预览通道。

UV、采样、生成器和图像处理数学节点提供 最大 **128 × 128** 预览。

最终图像优先编译。随后更新直接编辑过的节点预览，包括输入连接或断开的节点，再按距离最近编辑位置的远近更新其余预览。平移和缩放视图不会移动这个优先位置。首次编辑前，预览按距视口中心的距离排序。图代码在后台准备；新程序编译完成前保留上一次可用图像。复杂着色器的首次编译仍可能需要时间。

包含大量展开预览的图会共享编辑后的准备工作。图像就绪后直接更新，无需刷新未改变的控件，也无需再等待一次编辑延迟。参数值、连接及预览输出选择的行为保持不变。

节点预览异步编译，并共享每次更新的时间预算。缩小时按屏幕尺寸降低分辨率，放大时恢复。修改独立分支不会重新编译其他分支。动画在当前图窗口中最高以 30 Hz 更新，其他情况下为 10 Hz。

图形修改正在编译时，图层或 FX 保留上一份可用图像。返回最近编译的版本时，如果缓存仍可用，就会直接复用。**Compile**、保存和导出会等待所需结果；草稿编译失败时，保留可用版本并显示错误信息。

仅使用内置节点的图，其初次预览准备更快，分辨率和计算结果保持不变。

**Constant**、**Canvas Data**、**Texture Size**、**Split**、**Compare**、**Output**、**Parameter**
和 **Reroute** 不显示预览；相关信息通过字段和端口查看。
无预览或预览已折叠的节点没有统一的最小宽度，宽度由内容决定。展开预览后会恢复其最小宽度。
HLSL 函数仅预览数值与 Color 输出，不显示 Boolean 或资源输出；有多个可预览输出时，
将鼠标悬停在节点上，通过预览右下角的选择器切换输出。列表向上展开，右边缘与预览右边缘对齐。
放大节点图时，整个菜单按比例缩放，包括勾选标记、文字和间距，确保选项不小于选择字段。

不再显示 Preview 标题行。鼠标悬停在节点上时，图像顶部会出现向上箭头。
点击后预览折叠为窄行，中间显示紧凑的向下箭头按钮；点击该按钮即可再次展开。
每个节点的预览展开或折叠状态随文档保存，复制节点时也会保留。
侧栏没有跟随所选节点的共用预览。
预览在结果变化后刷新，且仅在展开并位于节点图可视区域内时更新。
折叠或移出可视区域的预览会等到再次展开且可见时更新。

数值显示为灰度；二维向量使用红、绿通道，三维向量使用 RGB，四维向量和 Color 还包含 Alpha。
Texture、Gradient 和 Curve 资源不能直接显示：连接对应的 Sample 节点，再展开其预览。

**Output** 是横向终点块：左侧为输入，右侧为 **Output / Layer result** 标题，末端呈圆角。
**RGBA** 接受带 Alpha 的颜色，或 Alpha 为 1 的 RGB。**Alpha Scale** 默认为 **1**，
用于乘以输入 Alpha；可输入数值或连接遮罩。RGB 保持不变，乘法结果不限制在 0..1。
图层结果请查看 **Layer Preview**；
节点预览显示图层 Transform 和 FX 之前的数值。
节点预览发生错误时会清空图像并显示提示，不会把旧图像当作当前计算的结果。

## 选择参数控件

以下参数类型可让你直接在 **Properties** 中调整图像：

| 类型 | 控件与效果 |
| --- | --- |
| **Enum** | 带名称的数值选项。每个选项可指定自己的值，包括 `0.25`、`0.5` 等小数；选择后，节点图收到的是该值。 |
| **Seed** | 整数字段配有 **Random** 按钮，用于选择可重复的变化。数值与范围端点均在 −16777216…16777216 内。关闭 **Range** 时，Random 使用整个区间，包含两端。保存或重新打开文档会保留所选值。 |
| **Point** | 二维位置，默认 `(0.5,0.5)`。`(0,0)` 为左下角，`(1,1)` 为右上角；位置可以超出画布。 |
| **Normal** | 三分量方向，传入节点图时归一化为单位长度。默认值和零向量的回退值均为 `(0,0,1)`。 |
| **Transform2D** | 可复用的变换框架，支持位置、尺寸和旋转；可通过参数在画布上编辑。将其连接到 **Apply Transform 2D** 使用该框架。 |

**Float** 的最小值和最大值可分别启用。硬边界限制输入；软边界为滑块提供建议范围，
允许手动输入超出范围的值。循环范围需要两个硬边界：在 `0…360` 中输入 `370` 会得到 `10`。
Float 仅启用一侧边界时，该边界必须为硬边界；软边界要求两侧都启用，且最小值小于最大值。
**Vector2** 可以保持自由向量，也可以使用带两个硬边界的区间。循环区间可跨越接缝，
例如 `(330,30)`；完整一圈会保留两端相遇的角度。Vector2 区间不支持软边界。
Seed 的软边界限制 **Random** 的取值范围；手动输入仍可超出建议范围，但必须位于上述精确整数范围内。

要移动程序化图案的框架，添加 **Transform2D** 和 **Apply Transform 2D**。
将参数连接到 **Transform**，再把 **Local** 输出连接到图案或采样节点的坐标输入。
**Input** 输出将局部坐标映射回输入空间。两个输出都使用同一个 **UV** 输入；
未连接时默认使用当前像素的 UV。节点不会裁剪图像或生成遮罩，需要时请另加遮罩。
变换端口不能直接连接到数值运算。

未连接的 **Transform** 显示一个紧凑的 **Transform** 按钮，默认保持坐标不变。
点击按钮可打开弹窗，调整位置、尺寸和旋转，或使用 **Reset**。
弹窗没有画布编辑按钮；需要画布操作时请使用参数。
复制或重新打开文档会保留参数标签、Enum 选项、边界和像素单位设置。

## 参数与颜色

在 **Parameters** 中拖动参数标题可调整顺序；拖到图中可在放置位置创建 Parameter 节点。
右键单击标题可使用 **Use in Graph**、**Duplicate** 和 **Remove**。
Duplicate 创建独立参数并复制设置和当前值；Remove 同时删除该参数的节点和连接。
标题右侧的指示点与参数输出端口的形状和颜色一致。
悬停在参数上时，其所在行和所有对应的 Parameter 节点都会高亮，当前选择保持不变。

参数支持数值、向量、Boolean、颜色、纹理、渐变、曲线、Enum、Seed、Point、Normal 和 Transform2D。
多个 Parameter 节点可读取同一个声明的参数，因此一个 Properties 字段能控制图像的多个部分。

**Parameter** 节点仅占一行，显示参数名和输出端口，不显示 **Out** 标签，内部也没有预览或下拉框。
通过节点的右键菜单选择其他已声明的参数；参数值在图层的 **Properties** 中调整。

端口标签在括号内显示类型：数值分量数为 `1`–`4`，纹理为 `T2`，渐变为 `G`，曲线为 `C`，
Transform2D 为 `TR2`，颜色为 `col`，布尔值为 `bool`。例如 `UV(2)`、`Color(col)`；标签会随端口类型更新。

数值和 **Color** 端口为圆形，**Boolean** 端口为菱形，**Texture**、**Gradient**、**Curve** 和 **Transform2D** 端口为方形。

数值向量可以连接到分量数不同的输入。`float4` 连接到 `float2` 输入时保留 X、Y，舍弃 Z、W。
较短的向量用零补齐缺少的分量，例如 `float2(x, y)` 转为 `float4(x, y, 0, 0)`。
向量连接到标量输入时保留 X。标量会重复填充数值向量的所有分量：`x` 转为 `float4` 时得到 `(x, x, x, x)`。

**Color** 是独立的端口类型，携带与 `float4` 相同的 RGBA 分量。Output 的 RGBA 输入、Blend 的颜色输入及结果、
Sample Texture 和 Sample Gradient 的颜色输出，以及 Color 参数的输出，均使用固定的 Color 类型。
Sample Curve 输出数值。

将数值或向量连接到 Color 输入时，转换结果如下：

| 来源 | Color 结果 |
| --- | --- |
| `float(x)` | `(x, x, x, 1)`，不透明灰色。 |
| `float2(x, y)` | `(x, y, 0, 1)`。 |
| `float3(x, y, z)` | `(x, y, z, 1)`。 |
| `float4(x, y, z, w)` | `(x, y, z, w)`，保留 Alpha。 |

Color 连接到数值输入时遵循普通的 `float4` 转换规则。
Add、Multiply、Lerp 等动态类型运算先将 Color 视为 `float4`，再采用数值输入中最多的分量数。
标量重复填充所有分量，较短向量补零；运算不会将 Alpha 补为 1。
例如，Color 乘以 `0.5` 会将包括 Alpha 在内的四个通道都减半。
动态数值结果使用向量或标量类型，不保留 Color 类型。

颜色选择器中的值采用与 WhimTex 其他颜色字段相同的预处理；连接端口本身不会额外转换色彩空间。

节点图保留 HDR 和负数值。需要明确限制时使用 Clamp；需要 HDR 输出时，
检查图层 **Rendering** 中的颜色范围设置。
Alpha 控制透明度；在 Output 前无需将 RGB 乘以 Alpha。

## 查看 Generated HLSL

面板显示由节点图生成的 WhimTex FX 脚本，包括 `@param` 声明和 `ApplyFX`。
这是添加 Unity ShaderLab 包装之前的代码。
生成的函数、分支和循环采用四空格缩进，复制代码时会保留这些缩进。

向上拖动底部的 **Generated HLSL** 横条，即可查看节点图生成的代码。
横条带有标题和水平抓握线，操作方式与 Layer Settings 中预览的拖动手柄相同。
面板初始为收起状态；向上拖动超过 14 个逻辑像素才会打开，不受节点图缩放影响。
单击不会改变状态。继续拖动可调整高度，向下拖动可隐藏代码。
面板高度受窗口可用空间限制，并为节点图保留空间；窗口会记住面板的展开或收起状态及高度。

这里复用 FX Code 的同一个代码字段，设为只读模式：文本仍可选择和复制，
并保留当前 Editor 版本中 FX Code 所支持的语法高亮、行号以及水平和垂直滚动。
重新打开时显示当前生成的代码；如果代码没有变化，原有文本选择会保留。
如需更改生成的代码，请修改节点图。

## HLSL 与 Custom Function 节点

选择 **Utility/Custom Function**，即可在节点内编写计算。通过 **Ports** 设置输入和输出的名称、
类型与方向，然后在 **Code** 中编辑函数体并点击 **Compile**。代码字段使用与 FX 相同的 HLSL 编辑器。
可以折叠代码，也可以拖动右下角的缩放手柄调整节点宽度和代码区域高度；最小宽度与普通节点一致。
代码字段占满节点宽度，底部不留空隙。
折叠 Code 后，节点恢复普通宽度；展开后恢复之前调整的尺寸。
文档会分别保存每个节点的展开状态和尺寸。

函数体与端口修改会作为草稿保存，直到 Compile 成功才应用。脚本重新加载或复制节点后，
端口设置仍保持可见；重新编译未修改的代码会保留输入值。编译失败时继续使用之前的代码和端口；
未连接的节点也能检查。支持 float/half 向量、Boolean 和 Color；Texture 只能作为输入。
函数返回值对应 **Result**，输出参数保留原名，inout 参数同时具有输入和输出端口。
名为 **UV** 或 **uv** 的二维数值输入默认使用当前像素坐标，可通过 **Custom / UV** 改为固定值。

带标记的 HLSL 库按分类出现在节点搜索中。**Color/Adjustments/Contrast** 节点位于
`Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl`：Amount 为 1 时保持原色，
其他值调整对比度，同时保留 Alpha 和 HDR。**Refresh HLSL** 重新扫描目录。
Custom Function 的 **File…** 按钮也可选择 `.hlsl` 文件及其中受支持的函数，无需目录标记。

HLSL 库节点采用普通节点外观：标题、端口和预览，不显示代码面板。
右键菜单提供 **Refresh HLSL** 和 **Choose HLSL File…**。文档使用源代码备份，
直到手动刷新且编译成功才替换；文件缺失或不兼容时保留备份。修改库文件不会自动更新文档。

通过右键菜单的 **Convert to Custom Function** 创建独立、可编辑的副本。
转换保留端口、数值、连接及当前图像，并解除文件关联。**Code** 包含完整主源文件及其辅助声明，
include 备份保留在文档中。修改端口时，在 Code 中编辑函数声明，再点击 **Compile**。
已有 include 备份不会被重新读取，因此原文件缺失时仍可编辑。转换支持 **Undo/Redo**。
元数据与库文件编写规则见 [函数契约](../ShaderLayer.md#hlsl-functions)。

## 功能范围

节点图属于 Shader 图层，随文档保持可编辑。更改参数会调整图像；更改运算或连接会改变计算本身。
连接必须朝向 Output，不能形成循环。

未完成的节点图也可以保存，包括尚未指定参数的 Parameter 节点。
如果无法编译，编辑器会显示错误，并在已有可用结果时保留上一次正常结果。
该图像不代表尚未完成的修改。文档保存的是可编辑节点图，之后可以继续完成它。

需要额外处理时，使用图层的普通 FX 列表。端口类型与执行规则见 [Shader 图层契约](../ShaderLayer.md)。

## 焦散

**Procedural/Noise** 中的 **Caustics Noise** 可直接生成水下光纹。
**Filter** 中的 **Caustics** 根据连接到 **Height** 的高度生成同样的光纹，
例如 **Perlin Noise → Fractal → Caustics**，也可使用采样的高度纹理。两者均为 2D。

**Caustics Noise** 的 **Periodic** 和噪声 **Filtering** 也默认关闭，Period 每轴默认为 1。独立的 Wrap 设置仍控制已计算图像的采样。

- **Strength** 控制起伏，**Depth** 控制光传播的距离。增大任一值会收紧亮纹，
  进一步增大则会产生重叠。
- **IOR** 控制折射，**1** 表示无折射。**Light Direction** 指向接收平面，
  `(0,0,-1)` 表示从上方照射。
- **Softness** 柔化光斑，**Intensity** 控制亮度。**Value** 可以超过 1，
  需要保留亮部峰值时请使用 HDR。平坦的高度产生均匀照明。
- **Caustics Noise** 的 **Scale**、**Period** 和 **Seed** 控制表面；在 **Position** 前连接 **Add** 来平移。
  **Phase** 按周期变化，连接 Time 即可产生动画。
- **UV** 放置最终光纹。**Resolution** 默认为 Custom **512 × 512**；
  增大分辨率可提高清晰度，但表面细节仍有采样上限。
- **Wrap = Repeat** 重复光纹。对于 **Caustics**，输入高度也应平滑且无缝；
  重复模式无法修复原始高度的接缝。

平滑高度生成流动的亮纹，突变高度产生棱角。将 Fractal 放在 Caustics 前可丰富
表面；放在任一焦散节点之后则只重复结果图像，且要求 **Seed Step = 0**。
