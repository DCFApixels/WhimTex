---
title: "Shader 图层"
parent: "图层与组"
grand_parent: "简体中文"
nav_order: 3
lang: "zh"
description: "在 WhimTex 的 Shader 图层中连接节点、公开参数并创建程序化纹理和遮罩，再通过普通图层变换、混合与 FX 处理结果。"
permalink: "/zh/shader-layer/"
translations: "en/shader-layer.md,ru/shader-layer.md,zh/shader-layer.md"
---

# Shader 图层

使用 **Shader** 将运算连接成节点图，生成纹理或遮罩。
节点图生成图层自身的图像。**Shader Processor** 处理下方的合成图像，
**Shader FX** 处理它所属图层的图像。参见 [Shader FX 与处理器](shader-fx.md)。

## 创建与调整图像

1. 在 Layers 底部通过 **+** 添加 **Shader** 图层。
2. 打开 **Properties**，点击 **Edit Graph**。
   通过节点图右键菜单中的 **Create Node** 或按 **Space** 添加节点。
3. 将计算结果连接到 **Output**，为图像提供红、绿、蓝和 Alpha 通道。
4. 为需要在图层 **Properties** 中调整的值声明参数。
   使用 **Parameter** 节点读取参数；**Constant** 提供数学常量。
5. 回到图层的 **Properties** 调整结果，无需重新搭建节点图。

新节点图已将一个 Color 参数连接到 Output。可在 Properties 中选择不透明的红色试用。
要在节点图内部设置数值，断开输入连接并编辑端口旁的字段。

生成的图像使用普通图层的 **Transform**、**Rendering**、**FX** 和混合设置。
如需在计算中使用另一张图像，声明 **Texture** 参数并通过 **Sample Texture** 读取。
把 Shader 放在某张图像上方，不会自动将那张图像设为输入。

## 数学常量与 Swizzle

**Input/Instance Index** 和 **Input/Time** 各提供一个名为 **Out** 的 Float 输出。
两者都没有自身预览，均提供 Float 输入 **Start**（默认 `0`）和 **Scale**（默认 `1`）。
**Instance Index** 返回 **Start + 索引 × Scale**；当前索引为 `0`，因此结果等于 Start。
**Time** 将编辑器运行时间（秒）传给相关节点的预览及图窗口中的结果预览。
输出为 **Start + 时间 × Scale**。两个输入均可手动设置或连接其他节点。
图层本身、Canvas、缩略图和导出始终使用时间 `0`，因此这些位置的 Time 输出等于 Start。
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

重复噪声使用 **Tileable Noise**：Period 设置 UV 中的重复周期，Cells 设置周期内的单元数，
Offset 移动图案。**Voronoi Edge** 输出到单元边界的距离，经过阈值处理即可画出边界线。
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

| 节点 | 用途 |
| --- | --- |
| **Parameter**、**Constant** | 读取图层可调参数，或使用 PI、TAU、PHI、E、SQRT2 数学常量。 |
| **UV**、**Image Size**、**Texture Size** | 获取图像内的位置、文档尺寸或输入纹理的尺寸。 |
| **Sample Texture** | 按 UV 读取图像；更改坐标可产生扭曲。 |
| **Sample Gradient**、**Sample Curve** | 将数值映射为渐变颜色，或用曲线调整数值。 |
| **Split**、**Combine**、**Swizzle** | 分离、组合或重排向量/颜色通道。 |
| **Add**、**Subtract**、**Multiply**、**Divide** | 偏移、缩放和组合数值。 |
| **Min**、**Max**、**Clamp**、**Abs**、**One Minus**、**Negate** | 限制数值、取绝对值或反转数值。One Minus 计算 `1 − x`。 |
| **Power**、**Square Root**、**Floor**、**Ceil**、**Round**、**Fraction** | 调整过渡形状、创建阶梯或重复数值区间。 |
| **Sine**、**Cosine** | 创建波形和重复图案。 |
| **Lerp**、**Remap**、**Step**、**Smoothstep** | 混合数值、转换范围并生成硬边或柔和阈值。 |
| **Unlerp** | 计算 T 在 A–B 范围中的位置：`(T-A)/(B-A)`，不限制输出范围。默认 A = 0、B = 1、T = 0。支持逐分量向量计算；两端相等时结果未定义。 |
| **Length**、**Distance**、**Dot**、**Normalize** | 计算距离与方向。 |
| **Compare**、**Select** | 根据条件在两个值之间选择。 |
| **Transform UV** | 平移、缩放或旋转供后续节点使用的坐标。 |
| **Pixelate UV** | 将坐标吸附到网格中心，再采样纹理或生成图案。 |
| **Noise**、**Cellular**、**Random** | 根据坐标与 Seed 生成噪声、单元距离或可重复的随机值。 |
| **Checkerboard**、**Stripes**、**Grid**、**Rings**、**Gradient Factor** | 创建边缘平滑的图案遮罩，或生成渐变采样位置。 |
| **Shape**、**Combine Distance** | 创建形状遮罩并组合有符号距离。 |
| **Polar Coordinates**、**Repeat**、**Mirror**、**Kaleidoscope** | 变换或折叠坐标，让图案重复或镜像。 |
| **Domain Warp**、**Vector Displacement**、**Twirl**、**Bulge / Pinch**、**Wave**、**Ripple** | 在生成图案或采样图像前扭曲坐标。 |
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

要创建彩色噪声，连接 **UV → Noise → Sample Gradient → Output**，并向 Sample Gradient
提供 **Gradient** 参数。之后在 Properties 中调整渐变。
要创建阈值遮罩，在 Noise 后接 **Smoothstep**，再通过 **Combine** 将其结果设为 Alpha。

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

未连接的数值输入字段显示在节点主体外、端口左侧，可直接编辑，且不会遮挡端口连线。
连接后字段隐藏，断开后重新显示。未连接的 UV 端口保留隐式的当前像素坐标（`PixelUV`）：
通过端口旁的 **Custom / UV** 选项，可输入自定义坐标或使用 UV。
单个 Float 字段也有 **X** 标签。水平拖动标签即可调整数值，与向量分量的操作一致。
当前模式不适用的输入仍然显示，但端口和字段会被禁用。例如，**Fractal = None** 时
Octaves、Lacunarity 和 Gain 不可用；PingPongStrength 仅在 Ping Pong 模式下可用。
悬停端口可查看原因。禁用的端口（包括 Split 中不存在的分量）不能建立新连接。
只要端口仍存在，其数值和已有连线都会保留；切换到适用模式后即可继续编辑。

从输入或输出端口拖出连线并放到图中的空白处，即可搜索兼容节点。菜单在光标右侧打开，左边缘与光标对齐。
从输出拖动时，列出兼容输入，例如 `Multiply(A)` 与 `Multiply(B)`；从输入拖动时，列出兼容输出，
例如 `Cellular(F1)` 与 `Cellular(F2)`，也包括匹配的参数和 HLSL 函数。
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

**Procedural/Noise** 中的 **Gabor Noise** 生成带近似抗锯齿处理的定向纹理细节。
**Scale** `(8,8)` 控制密度，**Frequency** `2` 控制细微振荡，**Anisotropy** `0.8` 控制方向一致性，
**Angle** `0` 以度数调整方向，**Seed** 默认为 `1337`。**Value**、**Phase** 和 **Intensity**
分别输出不同的 `0..1` 场，可用于遮罩或渐变着色。Gabor Noise 比简单噪声耗费更多计算；
将它接到需要反复采样的滤镜前，可能明显降低预览速度。

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
| **Procedural/Noise** | Noise、Cellular、Random。 |
| **Procedural/Pattern** | Checkerboard、Stripes、Grid、Rings、Gradient Factor。 |
| **Procedural/Shape** | Shape、Combine Distance。 |
| **UV** | Transform UV、Polar Coordinates、Repeat、Mirror、Kaleidoscope。 |
| **UV/Distortion** | Domain Warp、Vector Displacement、Twirl、Bulge / Pinch、Wave、Ripple。 |

**Noise** 提供 **None**、**FBm**、**Ridged** 和 **Ping Pong** 分形模式。Octaves 增加细节，
Lacunarity 控制各倍频层之间的频率变化，Gain 控制它们的相对强度。
**Domain Warp** 提供 **None**、**Progressive** 和 **Independent** 分形模式；None 仅使用一层噪声。
**Cellular** 按所选 Metric 输出非负的 **F1**、**F2** 距离，以及 `0..1` 内可重复的 **Cell** 值。
Cell 不是唯一标识符；Cellular 没有 Out 端口。
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
Angle、Rotation、Direction 和 Twirl 的 Strength 均以度为单位。

将 UV 节点的 **Out** 连接到下一个图案节点或 **Sample Texture.UV**。
**Repeat.Cell** 包含单元坐标，也可以为负。**Input/Instance Index** 使用独立索引（当前为零），与 Cell 无关。
**Domain Warp** 除了输出扭曲后的 UV，还通过
**Displacement** 输出偏移向量。**Strength = 0** 时，扭曲节点保持输入坐标不变；
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
Red、Green 和 Blue 再调整各通道。不连接某条曲线就不改变对应值。
只有连接 Alpha 曲线才会改变透明度。恒等曲线可保留高于白色和低于黑色的数值。

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

删除 Reroute 会将其上游来源直接连接到下游输入，多个 Reroute 串联时也适用。
普通的连线删除操作会断开该连接。Reroute 的位置和连接随文档保存；
插入、移动、更改连接和删除均支持 **Undo/Redo**。

## 预览节点

**Layer Preview** 悬浮在节点图上方，初始位于右下角。拖动标题或图像可移动它，拖动边缘或角落可调整大小。
靠近节点图视口边缘时会无间距贴边吸附，靠近角落时会同时固定到两条边，窗口大小变化时仍保持固定。
高亮边框表示固定方向；向远离边缘的方向拖动即可解除。窗口会记住位置、固定方向和尺寸。
节点图的平移与缩放不会移动预览。标题中的 **R/G/B/A** 按钮仅切换预览通道。

UV、采样、生成器和图像处理数学节点提供 最大 **128 × 128** 预览。

节点预览异步编译，并共享每次更新的时间预算。缩小时按屏幕尺寸降低分辨率，放大时恢复。修改独立分支不会重新编译其他分支。动画在当前图窗口中最高以 30 Hz 更新，其他情况下为 10 Hz。

**Constant**、**Image Size**、**Texture Size**、**Split**、**Compare**、**Output**、**Parameter**
和 **Reroute** 不显示预览；相关信息通过字段和端口查看。
无预览或预览已折叠的节点没有统一的最小宽度，宽度由内容决定。展开预览后会恢复其最小宽度。
HLSL 函数仅预览数值与 Color 输出，不显示 Boolean 或资源输出；有多个可预览输出时使用输出选择器。

不再显示 Preview 标题行。鼠标悬停在节点上时，图像顶部会出现向上箭头。
点击后预览折叠为窄行，中间显示紧凑的向下箭头按钮；点击该按钮即可再次展开。
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

## 参数与颜色

在 **Parameters** 中拖动参数标题可调整顺序；拖到图中可在放置位置创建 Parameter 节点。
右键单击标题可使用 **Use in Graph**、**Duplicate** 和 **Remove**。
Duplicate 创建独立参数并复制设置和当前值；Remove 同时删除该参数的节点和连接。
标题右侧的指示点与参数输出端口的形状和颜色一致。
悬停在参数上时，其所在行和所有对应的 Parameter 节点都会高亮，当前选择保持不变。

参数支持数值、向量、Boolean、颜色、纹理、渐变和曲线。
多个 Parameter 节点可读取同一个声明的参数，因此一个 Properties 字段能控制图像的多个部分。

**Parameter** 节点仅占一行，显示参数名和输出端口，不显示 **Out** 标签，内部也没有预览或下拉框。
通过节点的右键菜单选择其他已声明的参数；参数值在图层的 **Properties** 中调整。

端口标签在括号内显示类型：数值分量数为 `1`–`4`，纹理为 `T2`，渐变为 `G`，曲线为 `C`，
颜色为 `col`，布尔值为 `bool`。例如 `UV(2)`、`Color(col)`；标签会随端口类型更新。

数值和 **Color** 端口为圆形，**Boolean** 端口为菱形，**Texture**、**Gradient** 和 **Curve** 端口为方形。

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

未完成的节点图也可以保存，包括未连接的资源输入或尚未指定参数的 Parameter 节点。
如果无法编译，编辑器会显示错误，并在已有可用结果时保留上一次正常结果。
该图像不代表尚未完成的修改。文档保存的是可编辑节点图，之后可以继续完成它。

需要额外处理时，使用图层的普通 FX 列表。端口类型与执行规则见 [Shader 图层契约](../ShaderLayer.md)。
