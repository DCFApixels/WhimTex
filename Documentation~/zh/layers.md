---
title: "图层与组"
parent: "简体中文"
has_children: true
nav_order: 2
lang: "zh"
permalink: "/zh/layers/"
translations: "en/layers.md,ru/layers.md,zh/layers.md"
previous_page: "zh/getting-started.md"
next_page: "zh/transform.md"
---

# 图层与组

用多个独立的图层来构建图像，这样你就能单独移动或调整每个部分。
列表顶部是图像的前面。

## 添加图层

使用 Layers 底部的 **+** 来选择类型：

| 图层 | 用途 |
| :--- | :--- |
| File | Project 中已有的纹理。 |
| Drawing Layer | 绘制、擦除和填充。 |
| Color Fill | 纯色、UV 坐标或几何距离场图案。 |
| Gradient | 平滑的颜色过渡。 |
| Noise | 生成的图案。参见 [Noise](noise.md)。 |
| Shape | 可编辑的矩形、椭圆、多边形、星形、直线、圆弧或扇形。 |
| Text | 使用操作系统已安装字体的可编辑文字。 |

你也可以将 Project 中的纹理拖到画布视图中，把它添加到顶部，
或将它放到行与行之间来选择位置。新指定的图像会保持其原始比例。
将 HDR 纹理指定给 File 图层会把 **Color Range** 和 **Blend Range** 设置为 **HDR**。
之后你可以在 **Rendering** 中更改这两项。

引用其他 WhimTex 文档的 File 图层在行左侧显示细橙线。
双击缩略图或行背景可打开或切换到该文档，不替换当前文档。

效果图层也会显示结果缩略图，动画 Shader FX 使用静态图像。

## 选择与排列

点击某一行即可选中它。按住 `Ctrl` 可选择多个图层，按住 `Shift` 可选择连续范围。
**最后选择的图层是活动图层**：你就在这个图层上绘制，并在 Layer Settings 中编辑它。

拖动行可移动所选图层；保持在列表顶部或底部边缘附近可滚动。
从字段拖动前先结束文本编辑。从字段开始拖动会取消未确认的输入。
按 `Enter` 或离开字段可确认名称和不透明度的编辑。
可直接在行中编辑名称。**Opacity** 控制显示程度，**Blend** 控制与下方图像的混合；更改任一项会应用到所有选中图层。

使用眼睛图标隐藏图层。列标题中的眼睛图标会显示所有图层。

在 **Layers** 中选择多个图层，然后使用 **Transform (T)** 一起编辑。初始操作框与画布坐标轴对齐，仅包围所选图层的操作框；选中的组使用自身操作框，不遍历子图层。移动、旋转、缩放、倾斜和透视均相对于此公共操作框进行。同时选择组和子图层不会重复应用变换。**Esc** 取消当前拖动，Undo 撤销整个操作。多选变换期间，工具栏中的单图层设置不可用。

## 编辑图层设置

所选图层的设置分为四个折叠区：

- **Transform:** 位置、尺寸、旋转和平铺。
- **Rendering:** Opacity、Blend Mode、颜色范围和 Mapping。Opacity 和 Blend Mode 也可在 Layers 列表中使用。Standard/HDR 选择器仍保留在标题栏中。
- **Properties (layer type):** 该图层特有的设置，例如其源纹理、效果目标或绘制对称。
- **FX:** 添加和调整着色器效果。

通过 **图层菜单 ⋮ → Properties** 可在独立窗口中打开相同的设置。
不适用于所选图层的部分会变灰。

## 添加文字

选择带 **T** 图标的 **Text** 工具。单击创建无框文字；拖出矩形创建框内文字。
直接在 Canvas View 中输入，`Enter` 开始新段落。点击输入框外、按 `Ctrl+Enter` 或
选择 **Done** 可结束编辑。这次点击只结束输入，不会创建另一图层。
**Cancel** 或 `Esc` 恢复编辑开始前的文字。点击已有文字或工具栏的 **Edit Text** 可重新编辑。
按住 `Ctrl` 可创建新文字，而不是选择已有文字。

Text 工具栏的参数用于新建文字，不会修改已有图层。
在 **Properties (Text)** 中修改图层的格式；这些修改不会影响工具的默认设置。

也可选择 **+ → Text**，在 Layer Settings 的 **Properties (Text)** 中输入。
工具栏和图层设置中的 **Font** 提供已安装字体的搜索和字符预览。
所选字体名称和列表中可见的条目使用各自的字体显示；字体不可用或不支持名称中的字符时，
使用普通界面字体。工具栏中的选择框宽度固定。
无需将字体导入 Project。点击 **Refresh** 可刷新字体列表。

创建手势决定布局，工具栏不再提供 Layout 选择。
Frame Size、Wrapping、Overflow 和 Auto Size 仅属于图层 Properties，不属于工具设置。
在 Properties 中，**Layout → Point** 不使用文本框，只按显式换行分行。
**Layout → Frame** 按 **Frame Size (px)** 排版。**Wrapping** 可选
**Manual**（仅显式换行）、**Words**（按词换行）或 **Characters**（按字符换行）。
长于文本框的词仍会拆分。**Justify** 始终可用，扩大词间距以填满换行后的行，每段最后一行保持左对齐。
Point 文本或 Manual 换行会将其重置为左对齐，不改变垂直对齐。
用 Text 工具拖动文本框角点可调整换行。启用 **Auto Size** 后，Properties 中的 **Size**
将 **Min** 和 **Max** 并排显示，单位为画布像素。在此范围内选择能容纳文字的最大整数像素字号；
**Overflow** 控制超出文本框的内容：**None**（默认）保留框外文字，**Clip** 裁剪，
**Ellipsis** 缩短显示内容并在末尾添加「…」。Ellipsis 同时考虑宽度和高度，不修改原文。
如果一行或省略号本身也无法放入，则不显示该部分。Auto Size 达到 Min 后仍按 Overflow
处理。结束画布内输入后可查看最终效果。

**Style**、**Size**、**Alignment** 和 **Spacing Options (em)** 作用于整个文字块。
**Style** 行中的 **B** 和 **I** 按钮分别切换粗体和斜体；
**aa**、**AA** 和小型大写字母按钮分别选择 Lowercase、Uppercase 或 Small Caps。
**Alignment** 行将左对齐、居中、右对齐、Justify 与顶部、居中、底部对齐组合在一起。
再次点击已选按钮恢复原始大小写，不改变源文字。Small Caps 将原本的小写字母显示为
75% 大小的大写字形，并保持共同基线。
**Character**、**Word**、**Line** 和 **Paragraph** 分别增加字间距、词间距、行距和段间距。
零保留字体默认间距，正值增大间距，负值缩小间距。1 em 等于当前字体大小。
Paragraph 只作用于显式换行，不作用于自动换行；四个值均参与换行与 Auto Size 计算。

**Horizontal Scale** 只改变字符宽度，不改变高度：`1` 保持原宽度，`0.5` 缩窄一半，
`2` 加宽一倍。换行、Auto Size 和 Ellipsis 使用调整后的宽度；附加的 em 间距保持不变。

**Color** 包含透明度。使用 **Transform**、**Rendering** 和 **FX**
放置和处理文字。输入时显示普通文字；结束编辑后恢复带指定换行、两端对齐和效果的渲染结果。

TIFF 和 JSON 保存可编辑文字、字体名称和外观的栅格备份，不嵌入字体本身。
字体缺失时，WhimTex 显示警告并保留已保存的外观。选择已安装的替代字体后才能编辑
文字排版；Color、Transform 和 FX 仍可使用。没有匹配的备份时，文字保持透明，直到选择字体。
备份最长边不超过 2048 像素，必要时会进一步缩小以满足存储限制。

当前每层使用一种字体和样式，最多 8192 个字符。不提供逐字符格式和高级复杂文字排版。

## 将相关部分放在一个组中

选择图层并点击 **Folder**，或将图层拖入已有的组。
使用组的箭头展开或折叠它。

组默认处于 **Pass Through**，因此其图层可以与组外的图层混合。
选择其他混合模式可将组作为一张图像进行混合。组不透明度会让整个组淡出。
为组添加 **FX** 可处理其合成图像。FX 会自动将 Pass Through 组隔离，并使用 Normal 混合，不影响组外图像。移除全部 FX 后，若剪贴或 Mapping 不再需要隔离，将恢复 Pass Through。**Properties → Compositing** 以只读方式显示实际模式。组也支持 **Transform**：移动、旋转、缩放、倾斜和透视会作用于所有子图层。子图层的变换相对于所属组；组的操作框表示自身变换，不自动包围子图层。移动图层到其他组或取消分组时，画布上的位置保持不变。

## 复制、合并或删除

右键点击某一行，或打开 **⋮** 对所选内容执行操作。
**Duplicate** 会创建一个可以单独编辑的副本。File 图层仍使用相同的源纹理。

| 底部图标 | 点击 | 放置所选图层 |
| :--- | :--- | :--- |
| **+**（类型菜单） | 选择图层类型。 | 复制。 |
| **Page +**（带加号的纸张） | 添加绘制图层。 | 创建合并后的绘制副本。 |
| **Folder** | 将所选内容成组。 | 成组。 |
| **Trash** | 删除所选内容。 | 删除。 |

当你想在合并后的结果上绘制时，请参见[合并与转换](transform.md#合并图层或将它们转换为-drawing)。

## 分享图层

将一个或多个所选图层拖入另一个已打开的 WhimTex 窗口即可复制。在 Layers 列表外放置时插入顶部；在列表内按插入指示位置放置，或拖到组上以插入组内。源图层和剪贴板保持不变。组和 Drawing 像素会独立复制，一次 Undo 即可撤销整个操作。在源窗口内拖动仍然是移动图层。

选择图层，在右键菜单中点击 **Copy as JSON**。发送文本或 `.json` 文件；
接收者复制内容并按 **Ctrl+V**。普通 Ctrl+C 保持不变。

复制采用 FullOptimized：保留活动设置及其默认值，省略未启用功能的设置。
禁用的图层和 FX 仍会保留。需要 Full 或 Compact 时，在 **Export** 中选择 JSON。

同时选择 Target、FX 纹理和剪贴蒙版引用的源图层。Drawing 像素不会嵌入；
即使图片最初来自 URL，也会复制为空占位图层并显示警告。
使用 TIFF 或跨窗口拖放保留绘制内容。File 引用项目资源，缺失时会显示警告，
恢复资源前输入为空。

FX 源码会保留，include 尽可能展开；缺失依赖可能导致接收方无法编译 FX。
插入后检查警告和预览。技术细节见[统一 JSON 格式](../JSON_FORMAT.md)。

## 几何填充

在 **Color Fill** 中选择 **Mode → Pattern**。**Shape** 提供 Triangles、Squares、
Hexagons 和 Circles。圆形支持 **Square** 和 **Dense** 排列；Dense 将交替行偏移半个
间距并缩小行距。距离直接从几何计算，包括图形间的空隙，无需额外 SDF 图层。

- **Size (px)** 设置变换前的 X/Y 网格缩放。链条按钮按比例联动两个轴；断开后可独立修改。启用联动不会改变当前比例。**Offset (px)** 和 **Rotation** 控制偏移及旋转。
- **Gap** 缩小图形（0–0.99）；**Roundness** 向内圆化多边形顶角。零值保留尖角；即使 Gap 为零，圆角也会在公共顶点处产生空隙。
- **Bulge** 使内部距离轮廓呈圆顶形，不移动边界。零值保留几何距离，非零值是艺术化重映射。
- **Distance** 支持 Signed、Inside、Outside 和 Center（绝对距离）。**Distance Range** 以内切圆半径为单位。Signed 将边界映射至渐变中点。
- **Inverted**、**Profile** 和 **Gradient** 设置最终颜色和透明度。
- **Cell Color** 为各图形着色：**Uniform** 保留距离渐变，**Random** 根据 **Seed** 从 **Palette** 取色，**Pattern** 为正方形/三角形交替使用两种颜色，为六边形使用三种颜色。圆形根据排列方式选择配色。
- **Variation** 控制 Random 的取色范围：零使用渐变中点，一使用整个渐变。Fixed 模式可提供离散色板。**Color Blend → Multiply** 保留距离明暗，**Replace RGB** 提供平面色彩。两者保留距离渐变的透明度，忽略色板的透明度。空隙使用最近图形的颜色。
- 颜色随图形一起移动和旋转。Seamless 使 Random 在画布边缘重复，并将 Pattern 的单元数量适配到配色周期：棋盘格采用偶数，六边形和密集圆形的水平数量为三的倍数。

**Seamless** 将完整矩形周期适配到画布，包括成对的交错行。图层与组的组合旋转
吸附到 90°，两个轴的缩放分别调整。斜切和透视被替换为在画布中心估算的轴对齐网格。
原始设置保持不变；渲染后检查器显示实际网格。关闭 Seamless 后恢复自由变换。
多边形可能略有拉伸；圆仍保持圆形，因此某个方向可能增加空隙。
Seamless 优先于图层 Tiling，只保证生成图案的周期性；后续任意 FX 或其他图层
仍可能产生接缝。

## 绘制形状

选择 **Shape**（`U`），在画布视图顶部工具设置中选择一个形状，然后拖动即可创建它。
新图层会按图形命名，例如 **Rectangle 1**、**Line 2** 或 **Star 3**，所有形状共用一个编号序列。
每次拖动都会添加一个独立的 Shape 图层，在空文档中也是如此。
按住 `Shift` 可获得等比例或 45° 步进角度的直线；`Ctrl` 会绕过参考线吸附。
在松开前按 `Escape` 可取消。

使用 **Transform** 来移动、缩放或旋转已有的图形。在 **Properties (Shape)** 中，
可以更改其类型、颜色、多边形边数或星形角数和内半径。

- **Corners (%)** 显示图形示意图，各角旁的数值字段控制大小；零表示尖角。
  字段旁的图标选择 **Round**（圆角）或 **Bevel**（斜角）。多边形超过八个顶点时，点击示意图中的顶点来编辑它。
  矩形的四个角可独立设置；相邻角留有空间时，单角可达到短边长度的 100%。多边形可逐顶点设置。
  Star 和 Sector 分为 Outer 和 Inner 两组；Sector 的 Inner 控制中心角，Outer 控制与圆弧相接的两个角。
  链条按钮按比例链接矩形/多边形的大小，不链接角类型。
  独立角增大到无法容纳时，邻角的大小会减小；之后缩小该角不会自动恢复邻角。
  链接编辑和几何变化会按比例限制大小，使各角能容纳在边上。
- **Stroke Position** 选择 Inside、Center 或 Outside。描边宽度以画布像素为单位，缩放图形不会改变它。
- **Line Caps** 为 Line 和 Arc 选择 Butt、Round 或 Square。Transform 控制 Line 的长度和粗细；
  Round/Square 会延伸到端点之外。
- **Thickness (px)** 设置 Arc 主体的粗细；Transform 控制其中心线所在的椭圆。
  **Fill** 填充主体，**Stroke** 添加独立描边，包括两端周围的轮廓。
  **Stroke Width** 和 **Stroke Position** 控制描边，而不是圆弧主体的粗细。
- **Start Angle** 和 **Sweep Angle** 设置 Arc/Sector 的范围：0° 指向右侧，角度增加时逆时针旋转。
  Sweep 0 为空，360 为完整圆环或椭圆。

**Feather (px)** 柔化轮廓，而不模糊整张图像。**Feather Position** 可选择
Inside（向内）、Outside（向外）或 Centered（两侧）；空心描边的两条边界也会柔化。
数值是以画布像素为单位的完整过渡宽度，0 保留原有边缘。
较大的 Inside/Centered 羽化可能使细描边或小细节消失；Outside 会扩展到间隙中。
**Edge Mode → Step** 关闭抗锯齿和 Feather，使轮廓在画布像素网格上保持硬边，移动或旋转后也一样。
要得到源图像中只有 0/1 的 alpha，请使用不透明的 Fill/Stroke；颜色 alpha、图层 Opacity、FX 和后续过滤仍可柔化最终结果。
你也可以通过 **+ → Shape** 创建一个居中的形状。

Shape 保持可编辑：它能像其他图层一样使用剪贴蒙版、混合和 FX。
例如，在它上面放一个 Gradient 并启用该渐变的剪贴蒙版，即可为图形上色。
只有在你想直接在它上面绘制时，才将它转换为 Drawing。

## 为 VFX 创建纹理

程序化图层适合制作能量环、爆发效果和粒子遮罩。
这个示例将明亮的边缘与细微的径向条纹结合在一起。

<a href="{{ '/Images/vfx-energy-ring.png' | relative_url }}"><img src="{{ '/Images/vfx-energy-ring.png' | relative_url }}" alt="WhimTex 中的 VFX 能量环纹理，包含渐变和噪声图层、一个 Shader Processor 以及圆形渐变的预览" width="720"></a>

用 **Gradient** 图层确定主体形状，用[噪声](noise.md)添加细节，再用
[Shader FX](shader-fx.md)进行扭曲或最终调整。将形状与纹理细节放在不同图层中，
便于独立修改。在[渐变编辑器](color.md#编辑渐变)中调整颜色与衰减，
再用粒子效果实际使用的背景检查结果。

## 修复缺失的图层

类型不可用的图层会保留其名称、位置、可见性和通用设置。
组还会保留其子级。选择该行即可在 Layer Settings 中看到警告。

若要恢复原图层，请先恢复其包或脚本，并在替换类型前重新打开原文档。

选择 **Replace with**，然后点击 **Replace Behaviour**。**Transfer saved settings** 会转移旧图层类型的兼容设置。
面板会列出无法转移的内容；保存前请检查结果。包含子图层的组只能恢复为组。

不可用的图层仍可移动、隐藏或删除，恢复前不会显示在画布上。

{: .warning }
**Replace Behaviour** 会改用另一种类型，无法转移的设置可能在保存时丢失。
见[文档保护](saving.md#保留文档的可编辑性)；不支持的旧格式见 [TIFF 文档](tiff-format.md)。
