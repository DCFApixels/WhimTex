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

当画笔轮廓相对于画布视图过大时，中心会显示一个小十字标记。

Brush、Pencil、Smudge、Blur、Healing、填充和 Clear Drawing Layer 与参数修改共用普通 Undo/Redo 顺序；这些像素操作共同计入 Paint Undo Steps 限制。像素历史可保留到脚本重新编译之后，
但不会写入保存的 TIFF 文档，也不会延续到新的 Unity 会话。
在支持的设备上，上一笔的历史记录仍在后台处理时，就可以开始下一笔。
Undo 和保存可能需要等待尚未完成的笔触；连续绘制大量复杂笔触时，队列满后也可能暂停。
像素精度和撤销顺序保持不变。画布视图静止时，历史记录仍会继续处理，无需再画一笔来完成处理。

文档的 [Pixel Scale](preview.md#裁剪扩展或缩放画布) 会放大画笔尺寸，同时保留输入值和预设。Size 为 3、Pixel Scale 为 2 时，会以 6 个画布像素绘制。修改倍率会影响新笔触；已有绘制像素仍保存在源纹理中，直到缩放整个图像。

## 塑造笔触

| 控件 | 它改变什么 |
| :--- | :--- |
| Size | 笔触宽度。你可以拖动标签或使用 `[` / `]`。 |
| Hardness / Gradient | 位于画布视图顶部工具设置中 Size 的旁边。拖动 Hardness 或输入百分比；点击渐变条可编辑它。箭头可在 Hardness 和 Gradient 之间切换程序化画笔，并且不会丢失任一设置。 |
| Opacity | 整个笔触的最大强度。松开并再次绘制可叠加另一层。 |
| Flow | 每个画笔图章的强度。较低的值让重叠的图章逐渐叠加颜色。 |
| Pressure | 启用在 Brushes 中配置的 Size、Opacity 和 Flow 压力绑定。鼠标提供完整输入压力。 |

例如，**Opacity 40%** 会让一个笔触最高不超过 40%，即使你反复
涂过它。**Flow 10%** 会随着绘制逐渐叠加，直到 Opacity 上限。
这些控件在用 Brush 擦除时同样有效。

在柔边选区内，同一笔触中反复叠加 Brush 或 Pencil 图章会保持选区边缘的柔和程度。
松开并开始新笔触后，边缘覆盖仍可继续累积。
结束笔触时会保留预览中的结果，包括选区柔边和受保护的通道。
WhimTex 会在兼容的 GPU 上自动加速笔触完成处理；其他设备保持相同的绘制行为。

Hardness 保持程序化圆形笔尖的中心不透明，并控制边缘的柔和程度。
在 100% 时，约一个像素宽的抗锯齿边缘使圆形保持平滑；Pencil 仍使用精确的像素边缘。

在 **Brushes** 中，可分别启用 **Pressure → Size**、**Opacity** 或 **Flow** 并编辑各自的响应曲线。
Size 改变直径；Flow 改变图章强度并逐渐累积；Opacity 限制笔触内的局部覆盖，
但不会降低已经涂上的颜料。默认只有 Flow 受压力控制。曲线输入和输出均为 0–1，
鼠标使用曲线右端的值。**WhimTex Settings → Tablet Input → Pressure Curve** 在画笔曲线
之前调整全局输入响应，此设置不保存在画笔预设中。

对于纹理画笔，请在 **Brushes → Tip** 中启用 **SDF** 以使用 Gradient 控件。
不启用 SDF 时，纹理定义自己的边缘，此标题栏区块不可用。
相同的控件也可在 **Brushes → Tip** 中作为 **Mode**、**Hardness** 和
**Gradient** 使用。任一处的更改都会保持同步。

Pencil 没有硬度或间距控件。可为它的笔尖选择 **Circle**、**Square** 或 **Diamond**。
放大即可看到它精确的像素轮廓。

## 稳定笔触并保护通道

画布视图顶部的共用设置适用于 **Brush、Pencil、Blur Brush 和 Smudge Brush**。
**Smoothing → None** 直接跟随指针（默认）。**Smooth** 减少细小抖动；**Stabilizer**
让画笔沿一条虚拟绳索跟随指针。**Distance (px)** 以画布像素设置滤波距离或绳索长度：
值越大，笔触越稳定，但延迟也越大。缩放不改变此距离。它不会模糊图像像素；Shift 直线和吸附点保持准确。

**Finish Stroke** 在松开时将延迟的笔触补到指针位置；关闭则停在滤波后的位置。
**Smooth Pressure** 独立平滑压力，即使 Smoothing 为 None 也可使用。工具的 **Pressure** 关闭时，此控件不可用，但会保留设置。
**Distance** 仅在路径平滑或已启用的压力平滑需要它时显示。Pencil 没有压力动态。
每个图章使用指针事件之间插值后的压力。Smooth Pressure 使用近期样本：按距离加权，
或在 Stabilizer 中使用滑动平均。每个新笔触重置历史；补完末端时使用最终输入压力，包括零。

**Write R/G/B/A** 只允许修改开启的通道，关闭的通道保留原值。
关闭 A 可在重新着色时保持透明度和全透明像素；擦除也不会改变透明度。
例如，关闭 G、B 和 A 可单独编辑打包纹理的红色通道。保护也适用于 **Fill 和 Healing Brush**。
底部 R/G/B/A 按钮只改变显示，不改变绘制结果。

平滑设置作为输入偏好保存；写入通道包含在画笔预设中。

## 自定义画笔

点击 Canvas View 右边缘面板工具栏中的画笔图标，即可打开 **Brushes**。
如果当前选择其他工具，设置会被禁用；点击上方的 **Select Brush** 即可启用 Brush。
再次点击图标可关闭面板；打开其他面板会关闭当前面板。

<a href="{{ '/Images/brush-settings.png' | relative_url }}"><img src="{{ '/Images/brush-settings.png' | relative_url }}" alt="WhimTex Brushes drawer with a neon red stroke, stamp controls, tint, blend mode and live brush preview" width="720"></a>

笔尖和图章设置都在这里：**Size** 在 **Tip** 中，**Opacity / Flow**
在 **Color** 中。画布视图顶部提供相同设置的快捷入口，以及共用的写入保护控件。

笔触示例预览当前笔尖，不包含图层 FX 或对称；Eraser 在灰色颜料上预览擦除。
模拟压力从零逐渐增加，在中间保持完整，然后在末端降至零，使用已启用的画笔绑定和曲线。
关闭 Pressure 后，示例使用均匀强度。
用 **Preview Scale (%)** 容纳大笔尖或宽散布；它仅改变示例，不改变实际绘制大小。

**Tip**、**Stamps** 或 **Color** 标题栏中的 **↺** 会重置对应部分，
但保留 **Size、Hardness、Spacing、Opacity 和 Flow**。Hardness/Gradient 模式仍可能改变。
调色板颜色不会重置。

| 设置 | 它改变什么 |
| :--- | :--- |
| Spacing (%) | 图章之间的距离，以 Pressure Size 调整后的当前直径为基准，最小为 0.5 画布像素。100% 是一个直径；较低的值形成连续笔触，较高的值留下独立痕迹。Size Jitter 不改变间距。 |
| Scatter | 围绕笔触的随机位移。100% 会让中心最多偏移一个画笔直径。 |
| Scatter Bias | 负值让图章集中在笔触附近；正值将它们移向散布圆盘的外缘。**0** 让分布在其整个区域内保持均匀。在 Scatter 大于零时可用；适用于 Random 和 Sobol。 |
| Randomization | **Random**（默认）给出普通的随机变化。**Sobol** 让变化在图章之间分布得更均匀。适用于 Scatter、Size Jitter、Angle Jitter、Flip X/Y 和 Tint。 |
| Size Jitter | 围绕 Size 的随机变化。50% 会给出所选尺寸的一半到一倍半之间的图章。 |
| Rotation | **Fixed** 保留笔尖的原始方向。**Stroke Direction** 让它沿笔触转向，朝右的轴跟随移动方向。第一次点击采用 Canvas View 中最近的移动方向，并轻微平滑以减弱指针抖动，开始绘制后再修正一次。仅点击而不移动时保留初始方向。 |
| Angle Offset (°) | 在 Rotation 之后附加的固定旋转，从 −180° 到 180°。例如，在 Stroke Direction 下设置 90° 会让笔尖横跨笔触。需要纹理笔尖。 |
| Angle Jitter (°) | 在 Rotation 和 Angle Offset 之后附加的随机旋转。0° 不添加变化，180° 覆盖所有方向。需要纹理笔尖；程序化画笔不会因旋转而改变。 |
| Flip X / Flip Y | 每个纹理图章水平或垂直镜像的几率：**0** 从不，**0.5** 大约一半，**1** 总是。轴跟随笔尖的旋转。需要纹理笔尖；每个轴单独采样。 |
| Texture | 拖入一个纹理以将其形状用作画笔。清空该字段可返回程序化画笔。Size 测量其最长边。 |
| Tip Channel | Alpha 使用透明度；Luminance 将白色作为墨水；Inverted Luminance 将黑色作为墨水。Color 保留笔尖的颜色，并乘以绘制颜色。所有模式都遵循笔尖的透明通道。 |
| SDF | 将纹理视为距离场，并通过 Gradient 映射它。使用 Alpha 表示以透明度构成的场，使用 Luminance 表示灰度场，或在暗部为内部时使用 Inverted Luminance。Color 使用透明通道场，同时保留笔尖的 RGB 颜色。 |
| Gradient (Canvas View header) | 左侧是内部（**0**），右侧是外缘（**1**）。透明通道色标控制边缘和覆盖范围；颜色色标会乘以画笔颜色和 Tint。将透明通道色标移近可获得锐利边缘，移远则变得柔和。将过渡右移可扩展形状，左移则收缩它。 |
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
然后在观察示例的同时在画布视图顶部工具设置中编辑 **Gradient**。开始时使用白色颜色色标以保留
画笔颜色，并使用透明通道色标塑造边缘。你可以添加透明色带
来制作中空形状，或添加颜色色带来制作多色笔尖。该场应使用 0–1 范围，
内部值更高（或选择 Inverted Luminance）。普通图像
笔尖请保持 SDF 关闭。保存画笔预设时会包含 Gradient。

### 脚本笔尖与画笔 JSON

在 **Brushes → Tip → Source** 中选择 **Standard** 或 **HLSL**。
Standard 在 **Texture** 为空时使用程序化圆形笔尖；指定图像后使用纹理笔尖。
HLSL 用脚本生成笔尖，与带纹理的 Standard 使用相同的 Tip Channel、SDF/Gradient、旋转和翻转设置。
选择 **HLSL Presets**，或打开 **Edit Code…** 编辑脚本并点击 **Apply**。
脚本参数与 FX 使用相同的字段、分组和条件显示，也支持渐变和曲线。
函数签名决定 **Static**（点击 Apply 时烘焙）或 **Dynamic**（绘制时计算）。
**Tip Resolution** 仅控制 Static 笔尖的细节；**Size** 在两种模式下都控制印章大小。
Dynamic 可按笔触、印章、累计路径长度或移动方向变化。**Seed** 设置稳定的序列，
**Reset Sequence** 重新开始；预览不会推进实际绘画的序列。
代码窗口可将 HLSL 预设保存到项目或用户预设文件夹。
项目中的文件与用户预设 **Brushes/HLSL** 子文件夹中的文件会出现在菜单中。
普通画笔的 **Save As…** 也会保留 HLSL 代码和设置。签名与上下文字段见
[HLSL 规范](../AI/BRUSHES.md#full-hlsl-specification)。

浏览器 AI 可以生成画笔 JSON。在文本输入框外按 **Ctrl+V** 粘贴，
即可替换当前画笔，不改变图层。参见[画笔格式与示例](../AI/BRUSHES.md)。

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
如果更新后看不到预设，请在这里选择其现有文件夹；文件不会被移动或删除。
工具和 Canvas View 设置可能在更新后重置。

画笔存储在其 **Brushes** 子文件夹中。`.sebrush` 包含设置和使用的图像/Static
笔尖；Dynamic 保存代码而不是冻结的纹理。可将预设复制到另一台计算机的 Brushes 文件夹，
无需导入原始图像。覆盖会保留之前的文件为 `.sebrush.bak`；要恢复它，请将该
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

## Smudge Brush

选择**手指图标**并拖动，将已有像素沿笔触拉开。
只点击而不移动不会绘制。工具使用圆形笔尖，不会添加当前绘画颜色。

- **Size / Hardness** 设置直径和边缘柔和程度；`[` / `]` 调整 Size。
- **Mixing** 调节拉伸与颜色混合。0% 时，细节通过变形拉伸，不累积颜色模糊。数值越高，颜色混合越强；100% 时只混合颜色。默认值为 25%。
- **Strength** 控制拉伸力度；启用 Mixing 时，还控制拾取图像的保留程度。100% 时，拾取的区域保持不变；低于 100% 时，逐渐拾取已绘制的结果。
- **Flow** 控制每一步的作用强度。**Pressure** 根据数位笔压力调整 Flow。
- **Current Layer** 采样 Drawing 的原始像素。**Below Layers** 包含当前图层及其组内下方的可见图层；**All Layers** 采样可见合成图像。

只修改当前 Drawing，选区限制写入范围。点击其他图层类型时会提示转换。
在原图上方添加空白 Drawing 并选择 Below Layers，可以保留原图。
采样的 FX 会成为笔触中的像素；在带有相同 FX 的图层上操作可能重复应用效果。

每次拖动是一个 Undo 步骤。**Tiled** 允许笔触跨越画布边界。
设置独立于 Blur Brush；画笔预设和对称不适用于此工具。

## 修复画笔

选择创可贴图标，涂过划痕、小孔或接缝后松开鼠标。彩色笔触标记修复区域，
松开后才开始重建。请覆盖整个缺陷并留少量余量；结果可一次撤销。

修复会从周围图像中借用相似的颜色与纹理，包括细节。
它会平滑相容片段的接缝，并尝试其他样本来减少重复。
无法重建独特的缺失细节；较大的形状和中断的线条可能需要手动修整。
对平滑噪声进行大范围修复时，可能丢失对比度并留下较平坦的条带；重复细节仍可能出现。
**Hardness** 控制笔触边缘的混合，不控制重建细节的锐度。

- **Size**：画布像素直径，也可用 `[` / `]` 调整。
- **Hardness**：控制蒙版边缘硬度；低值柔和混合修复结果，高值产生清晰边缘。
- **Search**：笔触周围寻找替代纹理的范围。
- **Current Layer**：采样当前 Drawing 的原始像素，不含 FX。
  **Current & Below**：采样所在组内当前及下方图层的可见结果，支持在源图像上方的
  空白 Drawing 图层中修复。采样中的 FX 会烘焙到结果；使用独立修复图层可避免重复应用相同 FX。
- **Fast / Balanced / High**：增加匹配迭代次数，但计算更慢。
- **Transparent Only**：只填充采样图像中的透明空白，保留可见像素。

只修改活动 Drawing 图层；点击其他类型图层会提示转换。选区限制写入范围，
但搜索可使用选区外的像素。按 **Escape** 或 **Cancel** 放弃笔触或后台计算。
修改文档、选区或工具会取消待处理结果；保存前请等待计算完成。

在 **User Settings → Healing Brush → Stroke Color** 中设置预览颜色与透明度。
该设置仅影响显示，不改变蒙版强度或修复结果。**Reset Canvas View Appearance** 恢复默认蓝色和 40% 不透明度。

彩色预览显示实际绘制的蒙版，包括柔边与选区。反复涂过不会增加覆盖强度。
在 **Tiled** 预览中可在任意副本上绘制，蒙版会跨越边缘和角落，修复搜索也会跨越接缝。
沿四边绘制时，搜索会将相对边缘一起处理。复杂或不明确的纹理仍可能出现片段间的过渡。
使用圆形笔尖，不使用画笔预设、对称、压感或 Flow。
计算采用画布分辨率，但保留 Drawing 原始纹理尺寸。笔触与搜索区域合计最多 100 万像素。
短笔触响应更快；大面积缺失和独特细节可能需要重试或手动修整。

## 填充区域

选择 **Fill**（`G`）并点击你想上色的区域。
油漆桶光标上的小箭头尖端就是点击位置，桶本身位于其右下方。

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
