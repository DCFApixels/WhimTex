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
3. 将计算结果连接到 **Output**，为图像提供红、绿、蓝和 Alpha 通道。
4. 为需要在图层 **Properties** 中调整的值声明参数。
   使用 **Parameter** 节点读取参数；**Constant** 将数值保留在节点图内部。
5. 回到图层的 **Properties** 调整结果，无需重新搭建节点图。

新节点图已将一个 Color 参数连接到 Output。要尝试固定数值，
在 **Constant** 的类型菜单中选择 **Color**，用颜色选择器设置不透明的红色，再连接到 **Output**。
四分量 Constant 设为 `(1, 0, 0, 1)` 也会得到相同颜色。
如需在 Properties 中选择颜色，使用 **Color** 参数。

生成的图像使用普通图层的 **Transform**、**Rendering**、**FX** 和混合设置。
如需在计算中使用另一张图像，声明 **Texture** 参数并通过 **Sample Texture** 读取。
把 Shader 放在某张图像上方，不会自动将那张图像设为输入。

## 选择节点

| 节点 | 用途 |
| --- | --- |
| **Parameter**、**Constant** | 读取图层可调参数，或使用保留在节点图内部的数值。 |
| **UV**、**Image Size**、**Texture Size** | 获取图像内的位置、输出尺寸或输入纹理的尺寸。 |
| **Sample Texture** | 按 UV 读取图像；更改坐标可产生扭曲。 |
| **Sample Gradient**、**Sample Curve** | 将数值映射为渐变颜色，或用曲线调整数值。 |
| **Split**、**Combine** | 拆分或组合向量与颜色通道。 |
| **Add**、**Subtract**、**Multiply**、**Divide** | 偏移、缩放和组合数值。 |
| **Min**、**Max**、**Clamp**、**Abs**、**One Minus**、**Negate** | 限制数值、取绝对值或反转数值。One Minus 计算 `1 − x`。 |
| **Power**、**Square Root**、**Floor**、**Ceil**、**Round**、**Fraction** | 调整过渡形状、创建阶梯或重复数值区间。 |
| **Sine**、**Cosine** | 创建波形和重复图案。 |
| **Lerp**、**Remap**、**Step**、**Smoothstep** | 混合数值、转换范围并生成硬边或柔和阈值。 |
| **Length**、**Distance**、**Dot**、**Normalize** | 计算距离与方向。 |
| **Compare**、**Select** | 根据条件在两个值之间选择。 |
| **Transform UV** | 平移、缩放或旋转供后续节点使用的坐标。 |
| **Noise** | 根据坐标与 Seed 生成程序化图案。 |
| **Shape** | 创建带平滑边缘的椭圆、矩形或圆角矩形遮罩。 |
| **Blend** | 按混合模式与 Alpha 合成两个颜色。Lerp 仅进行普通数值插值。 |
| **Output** | 输出最终 RGBA 图像。 |

要创建彩色噪声，连接 **UV → Noise → Sample Gradient → Output**，并向 Sample Gradient
提供 **Gradient** 参数。之后在 Properties 中调整渐变。
要创建阈值遮罩，在 Noise 后接 **Smoothstep**，再通过 **Combine** 将其结果设为 Alpha。

未连接的数值输入字段显示在节点主体外、端口左侧，可直接编辑，且不会遮挡端口连线。
连接后字段隐藏，断开后重新显示。未连接的 UV 端口保留隐式的当前像素坐标（`PixelUV`）：
通过端口旁的 **Custom / UV** 选项，可输入自定义坐标或使用 UV。

从输出端口拖出连线并放到图中的空白处，即可搜索兼容节点。菜单在光标右侧打开，左边缘与光标对齐。每个选项都标明节点和具体输入，
例如 `Multiply(A)` 与 `Multiply(B)`。选择后会在放下位置创建节点并连接指定输入，
一次 Undo 即可撤销这两个操作。取消搜索不会改变图。

## 移动与删除节点

按 **Del** 删除选中的节点，包括 **Reroute**。**Output** 是必需节点，不能删除。
编辑文本或数值字段时，**Del** 继续编辑字段值，不会删除节点。

拖动时，已连接的输入与输出端口中心会轻微吸附到同一高度。
端口吸附不改变 X；节点矩形的常规 Y 轴吸附优先。
按住 **Shift** 可禁用吸附。一起移动的多个节点保持相对位置。

## 用 Reroute 整理连线

双击已有连线即可插入 **Reroute**，也可以在该连线的右键菜单中选择 **Add Reroute**。
Reroute 只能在已有连线上创建，不会出现在 **Add Node** 或节点搜索中。

这个迷你节点没有标题、预览或参数控件。**In → Out** 原样传递来源的类型和值，
支持数值、向量、Color、Boolean、Texture、Gradient 和 Curve，不增加计算，也不改变图像。
拖动迷你节点可以调整连线路径；将 **Out** 连接到多个输入，即可让同一个值分支。

删除 Reroute 会将其上游来源直接连接到下游输入，多个 Reroute 串联时也适用。
普通的连线删除操作会断开该连接。Reroute 的位置和连接随文档保存；
插入、移动、更改连接和删除均支持 **Undo/Redo**。

## 预览节点

除 **Output** 外，计算节点内部都有独立、可折叠的 **128 × 128** 预览。
**Output**、**Parameter** 和 **Reroute** 没有节点内预览；侧栏也没有跟随所选节点的共用预览。
节点有多个输出时，可通过该预览的输出选择器选择端口，例如 Split 的通道或 Image Size 的 Size/Texel。
预览在结果变化后刷新，且仅在展开并位于节点图可视区域内时更新。
折叠或移出可视区域的预览会等到再次展开且可见时更新。

数值显示为灰度；二维向量使用红、绿通道，三维向量使用 RGB，四维向量和 Color 还包含 Alpha。
Texture、Gradient 和 Curve 资源不能直接显示：连接对应的 Sample 节点，再展开其预览。

**Output** 通过轻微的外观区别便于查找。图层结果请查看 **Layer Preview**；
节点预览显示图层 Transform 和 FX 之前的数值。
节点预览发生错误时会清空图像并显示提示，不会把旧图像当作当前计算的结果。

## 参数与颜色

参数支持数值、向量、Boolean、颜色、纹理、渐变和曲线。
多个 Parameter 节点可读取同一个声明的参数，因此一个 Properties 字段能控制图像的多个部分。

**Parameter** 节点仅占一行，显示参数名和输出端口，不显示 **Out** 标签，内部也没有预览或下拉框。
通过节点的右键菜单选择其他已声明的参数；参数值在图层的 **Properties** 中调整。

数值和 **Color** 端口为圆形，**Boolean** 端口为菱形，**Texture**、**Gradient** 和 **Curve** 端口为方形。

数值向量可以连接到分量数不同的输入。`float4` 连接到 `float2` 输入时保留 X、Y，舍弃 Z、W。
较短的向量用零补齐缺少的分量，例如 `float2(x, y)` 转为 `float4(x, y, 0, 0)`。
向量连接到标量输入时保留 X。标量会重复填充数值向量的所有分量：`x` 转为 `float4` 时得到 `(x, x, x, x)`。

**Color** 是独立的端口类型，携带与 `float4` 相同的 RGBA 分量。Output 的 RGBA 输入、Blend 的颜色输入及结果、
Sample Texture 和 Sample Gradient 的颜色输出，以及 Color 参数的输出，均使用固定的 Color 类型。
Sample Curve 输出数值。Constant 的类型菜单也包含 Color，并提供颜色选择器。

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

向上拖动底部的 **Generated HLSL** 横条，即可查看节点图生成的代码。
横条带有标题和水平抓握线，操作方式与 Layer Preview 的拖动手柄相同。
面板初始为收起状态；向上拖动超过 14 个逻辑像素才会打开，不受节点图缩放影响。
单击不会改变状态。继续拖动可调整高度，向下拖动可隐藏代码。
面板高度受窗口可用空间限制，并为节点图保留空间；窗口会记住面板的展开或收起状态及高度。

这里复用 FX Code 的同一个代码字段，设为只读模式：文本仍可选择和复制，
并保留当前 Editor 版本中 FX Code 所支持的语法高亮、行号以及水平和垂直滚动。
重新打开时显示当前生成的代码；如果代码没有变化，原有文本选择会保留。
如需更改生成的代码，请修改节点图。

## 首版范围

节点图属于 Shader 图层，随文档保持可编辑。更改参数会调整图像；更改运算或连接会改变计算本身。
连接必须朝向 Output，不能形成循环。

未完成的节点图也可以保存，包括未连接的资源输入或尚未指定参数的 Parameter 节点。
如果无法编译，编辑器会显示错误，并在已有可用结果时保留上一次正常结果。
该图像不代表尚未完成的修改。文档保存的是可编辑节点图，之后可以继续完成它。

首版不包含自定义 HLSL 节点、子图、Blur 或 Time 节点。
需要额外处理时，使用图层的普通 FX 列表。端口类型与执行规则见 [Shader 图层契约](../ShaderLayer.md)。
