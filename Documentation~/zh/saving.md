---
title: "保存与导出"
parent: "简体中文"
nav_order: 13
lang: "zh"
permalink: "/zh/saving/"
translations: "en/saving.md,ru/saving.md,zh/saving.md"
previous_page: "zh/post-fx.md"
next_page: "zh/shortcuts.md"
---

# 保存与导出

保存文档可以让它的图层保持可编辑，并直接在 Unity 中使用结果。
只有当你需要单独的图像文件时才进行导出。

## 保存文档

按 `Ctrl+S`。首次保存会询问位置；之后的保存会更新同一个文件。
**Save As** 会创建一份单独的副本。如果你在文档有更改时关闭它，
可以选择保存、放弃更改或取消关闭。
未编辑的新文档关闭时不会提示保存。删除最后一个图层仍属于可保存的更改。

保存后的资源可以直接当作**纹理**使用。双击可继续编辑；
如果文档已打开，WhimTex 会切换到现有窗口。

Unity 通常显示的是**最后保存的图像**。启用 [Live Update](preview.md#在模型上查看你的绘制)
即可在保存前于模型上查看编辑效果。如果关联纹理发生变化，请再次保存文档以更新输出图像。
文件图层会保留它们与源纹理的链接；请把这些源文件保留在项目中。

## TIFF 文档（实验分支）

新文档保存为 **Name.whimtex.tiff**：一个可编辑文件，由 Unity 作为纹理导入。
在 Project 中选中它，或点击 **Output**，即可在 Unity 标准 Inspector 中设置压缩、mipmap、精灵及平台覆盖。TIFF 不使用单独的设置窗口；新文档需要先保存。
需要精灵时选择 **Sprite (2D and UI)**，再使用标准 Sprite Editor。
请保留 TIFF 及其 `.meta`；在 Unity 内移动资源不会断开与已打开文档的关联。
不要用外部图像编辑器重新保存 TIFF，否则可能丢失可编辑图层。

**Live Update** 也支持压缩输出：编辑期间临时使用未压缩图像。
需要时会临时启用 Read/Write，结束会话后恢复；在此期间 `.meta` 会有修改。
关闭或切换文档、脚本重载及外部重新导入都会停止会话。保存另一文档不会影响当前会话。
构建 Player 也会停止 Live Update，并使用**最后保存的 TIFF**，不会保存或丢弃尚未保存的编辑。构建后请手动重新启用 Live Update。如果无法恢复纹理，构建将中止。
如果保存后 Unity 报告导入错误，已写入的 TIFF 会保留。修复导入问题后再次保存；即使文件内容没有变化，WhimTex 也会重新尝试导入。
保存当前文档时会短暂暂停 Live Update，并在导入后恢复，不会反复关闭和开启 Read/Write。
Live Update 支持 2D **Default** 和 **Sprite** 导入；Crunch 压缩及其他纹理类型通过保存更新。
实验性 TIFF 路径目前同时只支持一个 Live Update 会话。最终导入处理可能与实时预览不同。

缺失类型、字段或引用资源时会显示警告并阻止保存，以免丢失数据。
恢复所需版本的软件包或资源后重新打开文档。LDR 保存遵循纹理的 sRGB 设置；
HDR TIFF 使用线性数据并关闭 sRGB。Alpha 始终不进行 sRGB 编码。

保存前请先 Apply 修改后的 Shader FX 代码。如果 TIFF 在当前编辑会话之外发生更改，请重新打开或使用 **Save As**；WhimTex 不会覆盖外部版本。
将旧 `.asset` 保存为 TIFF 会复制可编辑的 Drawing 像素，保留原资源不变。原有输出纹理的引用不会自动重新分配。
PNG/EXR 导出仍是普通图像导出，不包含可编辑图层。

下方的关联图像与内嵌输出设置仅适用于**旧版 `.asset` 工作流**，
可通过 **Export → Compositor Asset, legacy (.asset)** 使用，不适用于 TIFF 导入设置。

## 关联输出图像

点击画布 W/H 字段旁的 **Output**，在 **Linked Output** 中指定 **Assets** 内已有的 PNG、TGA、JPG/JPEG 或 EXR 图像。确认关联后，每次保存文档都会用完整画布分辨率的合成结果覆盖该图像。不要指定需要保留原样的源图像。PNG/TGA 保留透明度；JPG 将透明区域合成到白色背景上；EXR 保留线性 HDR。LDR 图像的 RGB 编码遵循目标文件的 sRGB 设置，法线贴图使用线性数据。

图像保留 GUID 和导入设置，包括平台覆盖设置及精灵切片。点击 **Texture Settings** 可选中图像并在 Unity 标准 Inspector 中修改设置。下方的内嵌输出设置不会缩小或压缩关联源文件。原有内嵌纹理及其 Live Update 保持不变；关联图像仅在 Save 时更新。

**Clear** 解除关联，但不删除图像。在 Unity 内移动或重命名图像不会断开关联。目标丢失、只读或格式不受支持时会阻止保存，需要重新指定或清空该字段。对已保存的文档执行 **Save As** 时，新副本会清除关联，避免覆盖原文档的输出；新文档首次保存时保留已指定的关联。

## 内嵌输出设置

**Alpha Is Transparency** 将边缘 RGB 扩展到透明像素，减少过滤产生的边缘杂色，不会移除 alpha。此处理在保存时应用，不用于 Live Update。**sRGB (Color Texture)** 是 RGBA32 的独立复选框；HDR 保持线性。

**Max Size** 限制输出尺寸而不改变画布；**Resize Algorithm** 可选择 Mitchell 或 Bilinear。精灵矩形和边框随输出缩放，元数据仍使用画布像素。**Advanced** 提供 Box/Kaiser mipmap 过滤、**Preserve Coverage** 和 **Alpha Cutoff**。关闭 **Read/Write** 可移除 CPU 副本，但需要重新启用并保存后才能使用 Live Update 和 Sprite Editor。Live Update 使用快速预览路径；最终缩放、alpha 处理和 mipmap 过滤在保存时应用。

底部固定预览区在棋盘格上显示最后保存的结果。拖动 **Preview** 标题栏可调整高度，不受设置区域滚动影响。达到最小高度后继续向下拖动即可完全隐藏预览；向上拖动保留的标题栏即可重新展开。图像底部叠加显示尺寸、格式、色彩空间、mip 层数、GPU/CPU 内存估算和实际资源文件大小。内存估算不包括驱动对齐和 Unity 对象开销。文件大小包括文档与图层，不包括 `.meta`，不等于纹理运行时内存占用。

底部 **Compression** 面板提供 **Format: Automatic**，以及 **Compression: None / Low Quality / Normal Quality / High Quality**。对于 LDR，Low/Normal 为不透明图像选择 BC1，为透明图像选择 BC3，分别使用 Fast/Normal 编码质量；High 使用 BC7 和 Best 质量。不透明且 RGB 非负的 HDR 使用 BC6H；带透明度或负 RGB 的 HDR 保持未压缩以保留数据。None 禁用压缩。这些是在保存时应用的 BC 预设，而不是 Unity 的平台相关导入模式。手动格式仍可单独设置编码质量。

**Output Type** 可选择 **Texture**（不生成精灵子资源）或 **Sprite**（Single/Multiple）。为保持兼容，默认仍为 Sprite。Texture 模式隐藏精灵设置，保存时不校验精灵参数。应用 Texture 会删除已生成的精灵并使其引用失效，但保留切片和设置以便切回 Sprite。输出纹理的引用保持不变。

点击 WhimTex 中的 **Output**，或在 Project 中选择已保存资源，然后点击 Inspector 中的 **WhimTex Output Settings…**。两种方式都会打开该文档的同一个设置窗口。点击 **Apply & Save Output** 或在 WhimTex 中保存，可更新内嵌纹理和单个 **Output Sprite**。文档存在未保存的更改（包括图层编辑）时，按钮会高亮显示。新文档可先配置参数，再在 WhimTex 中首次保存；编辑字段不会在每次输入时重新生成输出。

- **Texture：**Filter Mode、Wrap U/V、Aniso Level 和 Generate Mip Maps。Wrap 控制纹理采样，而非图层平铺。
- **Storage：**HDR Half（默认）、HDR Float、Linear RGBA32 或 sRGB RGBA32。RGBA32 将数值限制在 0–1；sRGB 对 RGB 进行颜色编码。HDR Float 仅改变存储精度，不提高合成器的半精度计算精度。
- **Sprite：**Pixels Per Unit、带 **Pivot Alignment** 固定位置与手动坐标的归一化 Pivot、以像素为单位的 **Left / Bottom / Right / Top** Border 字段、Full Rect/Tight Mesh Type、Extrude 和 Generate Physics Shape。九宫格使用 Full Rect；边框必须位于画布尺寸内。

无效设置会高亮显示，并在字段旁说明原因；修正前 **Apply & Save Output** 不可用。只有文档确实含有已保存的精灵时，才显示删除精灵的警告。

**Revert** 恢复上次应用的输出设置和 Filter Mode，不改变图层、画布尺寸或精灵切片，并支持 Undo。旧文档没有设置快照时，下次保存前以初次加载的设置作为恢复起点。

**Preview** 标题栏提供 **RGBA / RGB / Alpha** 和 mip 层级选择。这些只影响预览，关闭 Read/Write 时也可使用，不会修改保存的图像。**Preview requires Apply** 提醒当前显示的仍是上次保存的结果。通道和 mip 下拉列表是独立控件；拖动标题栏的其余区域可调整高度或隐藏预览。

两种输出类型均支持 **Generate Mip Maps**。**Compression** 提供 None、BC1、BC3、BC7 和 BC6H，以及 Fast/Normal/Best 质量。BC1/BC3/BC7 需要 RGBA32；BC6H 需要 HDR，仅存储非负 RGB，不保留 alpha。BC1 不保留完整 alpha，透明图像请使用 BC3 或 BC7。画布宽高必须能被四整除。压缩仅在保存时应用，不影响编辑中的画布。压缩结果通过 Save 更新；使用 Live Update 前请选择 Compression None 并再次保存。BC 格式需要兼容设备，不会自动按目标平台转换。

默认保持未压缩 HDR Half、无 mipmap、100 PPU 和居中轴心的矩形精灵。Read/Write 默认启用。不支持平台覆盖。设置仅影响内嵌输出，不影响单独导出的图像。

### 精灵切片（可选）

通过 Unity Package Manager 安装 **2D Sprite**（`com.unity.2d.sprite`）以启用切片编辑。WhimTex 不会自动安装该包。

1. 保存文档，打开 **Output → Sprite**。
2. 将 **Sprite Mode** 设为 **Multiple**，点击 **Sprite Editor**。打开 Unity 编辑器前会保存当前输出。
3. 使用 **Slice** 或手动绘制矩形，设置各精灵的名称、轴心和边框，然后点击 **Apply**。
4. 在 Project 中展开文档，即可使用各个精灵。

**Single** 使用整个画布。切换到 Single 会隐藏并保留切片，切回 Multiple 不会破坏引用。重命名或修改现有切片的矩形也会保留引用；删除切片会移除对应精灵，可能导致已有引用失效。缩小画布后，若切片超出边界，请先修改矩形再保存。

未安装 2D Sprite 时，切片编辑不可用，但保存文档时仍会生成已保存的切片。本集成不支持自定义轮廓、蒙皮或辅助纹理。

## 选择导出格式

使用窗口标题栏中的 **Export**：

| 格式 | 最适合 |
| :--- | :--- |
| **PNG / TGA** | 带透明通道的彩色图像。 |
| **JPEG** | 不需要透明通道的图像；透明区域会变成白色。 |
| **EXR** | HDR 图像。 |
| **PSD** | 交换分层图像。 |
| **Texture2D (.asset)** | 不含可编辑图层的独立 Unity 纹理。 |

EXR 和 Texture2D 会保留 HDR 亮度。PNG、JPEG、TGA 和 PSD 使用普通的颜色范围。
导出不会改变原始文档的颜色范围。

缩放、EV 和 Post FX 等预览设置不会包含在保存或导出的图像中。

## PSD 中还有哪些内容可编辑？

PSD 会保留图层名称、顺序、组、可见性、不透明度以及受支持的混合模式。
兼容的颜色填充、渐变和描边保持可编辑。
其他效果会变成像素，某些混合模式看起来可能不同。

如果结果有差异，请阅读导出说明。请保留原始的 WhimTex 文档，
这样你之后仍然可以更改所有效果及其来源。
