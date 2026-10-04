---
title: "保存与导出"
parent: "简体中文"
nav_order: 13
lang: "zh"
permalink: "/zh/saving/"
translations: "en/saving.md,ru/saving.md,zh/saving.md"
previous_page: "zh/post-fx.md"
next_page: "zh/tiff-format.md"
---

# 保存与导出

保存文档可以让它的图层保持可编辑，并直接在 Unity 中使用结果。
需要单独的图像文件或 JSON 副本时，可以进行导出。

## 保存文档

按 `Ctrl+S`。首次保存会询问位置；之后的保存会更新同一个文件。
**Save As** 会创建一份单独的副本。如果你在文档有更改时关闭它，
可以选择保存、放弃更改或取消关闭。
未编辑的新文档关闭时不会提示保存。删除最后一个图层仍属于可保存的更改。

保存后的 TIFF 可以直接当作**纹理**使用。双击可继续编辑；
如果文档已打开，WhimTex 会切换到现有窗口。

Unity 通常显示的是**最后保存的图像**。启用 [Live Update](preview.md#在模型上查看你的绘制)
即可在保存前于模型上查看编辑效果。如果关联纹理发生变化，请再次保存文档以更新输出图像。
文件图层会保留它们与源纹理的链接；请把这些源文件保留在项目中。

## 选择导出格式

点击窗口标题栏的 **Export**，选择 **Format** 和设置，再点击 **Export…** 选择路径。
取消不会导出文件；出错时保留设置以便重试。**Save As** 始终保存 TIFF。

| 格式 | 最适合 |
| :--- | :--- |
| **PNG / TGA** | 带透明通道的彩色图像。 |
| **JPEG** | 不需要透明通道的图像；透明区域会变成白色。 |
| **EXR** | HDR 图像。 |
| **PSD** | 交换分层图像。 |
| **Texture2D (.asset)** | 不含可编辑图层的独立 Unity 纹理。 |
| **WhimTex JSON (.json)** | 可编辑的设置与图层，不包含 Drawing 像素。 |

JPEG 提供 **Quality**（1–100，默认 95）。EXR 提供 **Precision**（16-bit Half 或 32-bit Float）
及无损 **Compression**（默认 ZIP，也可选择 RLE、PIZ 或 None）。JSON 提供 **Mode**，并在选择路径前提示
将省略 Drawing 像素；仍需另行确认。JSON 和 Unity Texture2D 保存在项目的 Assets 文件夹内。

EXR 和 Texture2D 会保留 HDR 亮度。PNG、JPEG、TGA 和 PSD 使用普通的颜色范围。
导出不会改变原始文档的颜色范围。

缩放、EV 和 Post FX 等预览设置不会包含在保存或导出的图像中。

## JSON 文档

JSON 适合分享可编辑的程序化图层，或以文本查看设置。
它不是 Unity 纹理，没有 Texture Importer 和 Live Update。**它不保存 Drawing 像素。**

1. 打开 **Export**，选择 **Format → WhimTex JSON**，再选择 **Mode**。
2. Drawing 含像素时，仅在可接受空图层的情况下确认省略；其设置和 FX 会保留。
3. 导出副本。源文档不改变，也不会标记为已保存。

| 模式 | 保留内容 |
| --- | --- |
| **Full Optimized (Default)** | 当前启用功能的设置，包括默认值。 |
| **Full** | 另外保留未启用功能的设置。 |
| **Compact** | 活动设置中省略默认值，以减小文件。 |

所有模式都保留关闭的图层和 FX。缺失资源会警告，但引用保留。
有错误的 FX 保留代码和设置，修复前跳过渲染并显示警告标记。

### 打开或粘贴

| 操作 | 结果 |
| --- | --- |
| 双击 `.json` | 恢复画布尺寸、sRGB、过滤、精度及 History；Ctrl+S 继续保存 JSON。 |
| 粘贴内容 | 添加图层，不替换目标 sRGB 或过滤设置，即使接受源画布尺寸。 |
| **Save As** | 创建 TIFF，用于 Unity 纹理或保留 Drawing 像素。 |

所选图层导出的 JSON 也能作为文档打开，内容格式相同。
重新打开和保存会保留写入模式；一次性使用其他模式导出不会改变它。

### 手写 JSON

缺少某一尺寸时，打开采用该轴默认值 512，粘贴沿用目标值。
两轴都省略时不提示调整画布；常规导出会写入两个尺寸。

数字和布尔值不要加引号，枚举使用准确名称。类型或数值错误会指出字段路径；
缺失资源和有错误的 FX 是警告，而非结构错误。
字段规则和限制见 [JSON 参考](../JSON_FORMAT.md)。

## PSD 中还有哪些内容可编辑？

PSD 会保留图层名称、顺序、组、可见性、不透明度以及受支持的混合模式。
兼容的颜色填充、渐变和描边保持可编辑。
其他效果会变成像素，某些混合模式看起来可能不同。

如果结果有差异，请阅读 Console 中的导出说明。请保留原始的 WhimTex 文档，
这样你之后仍然可以更改所有效果及其来源。

## 纹理与精灵设置

TIFF 是 WhimTex 的主要文档格式：一个文件同时保存合成图像和可编辑图层。
在 Project 中选中已保存的 TIFF，或点击 W/H 旁的 **Output**，即可打开 Unity 标准
Texture Importer。新文档需要先保存。

- **Texture Type：**纹理选择 **Default**，精灵选择 **Sprite (2D and UI)**。
- **sRGB (Color Texture)：**用于彩色图像；数据贴图需要对应的线性设置。
- **Alpha Is Transparency：**减少透明边缘的杂色，不会移除 alpha。
- **Generate Mip Maps**、**Filter Mode**、**Wrap Mode** 和 **Aniso Level：**控制导入纹理的采样。
- **Max Size**、格式和压缩：先设置默认值，再按需要启用平台覆盖。

点击 Inspector 中的 **Apply** 应用导入设置。这些设置影响 Unity 使用的纹理，
不会改变可编辑图层或画布尺寸。HDR TIFF 使用线性数据；WhimTex 会为其关闭 sRGB。

### 输出颜色编码

**Precision** 旁的 **sRGB** 选择 TIFF 输出编码：开启为 sRGB，关闭为 Linear。
新建的普通文档默认使用 sRGB。已有 TIFF 使用 Texture Importer 的设置；
在 Inspector 中修改 **sRGB (Color Texture)** 并点击 **Apply** 也会更新输出。
Float32 输出（包括 Auto 选择的 HDR）始终使用 Linear。

切换会保持图像亮度和颜色，仅可能存在 8 位输出的舍入差异。
图层颜色、画笔颜色、渐变和工作预览不变，alpha 也不变。
这是编码转换，不是用另一种颜色空间重新解释相同的通道数值。

在 WhimTex 中切换复选框会启用 **Save**，并支持 Undo/Redo。保存之前，TIFF 和导入设置不变；
放弃保存也会放弃待保存的编码设置。反复切换不会逐步降低源图层质量。
通过 Inspector 的 **Apply** 修改设置则更新**上次保存的图像**，不会保存当前图层编辑，
并会同步复选框。这种外部重新导入会停止 Live Update，需要时请重新启用。其他源纹理不会改变。

### 精灵切片

如果 Sprite Editor 不可用，请通过 Package Manager 安装 **2D Sprite**（`com.unity.2d.sprite`）。

1. 选中已保存的 TIFF，将 **Texture Type** 设为 **Sprite (2D and UI)**。
2. 单个精灵选择 **Sprite Mode → Single**，精灵表选择 **Multiple**，然后点击 **Apply**。
3. 打开 **Sprite Editor**，使用 **Slice** 或绘制矩形，设置名称、轴心和边框。
4. 点击 **Apply**，在 Project 中展开 TIFF，即可使用其精灵。

九宫格需要设置精灵边框和 **Mesh Type → Full Rect**，并将 uGUI Image 的
**Image Type** 设为 **Sliced**。修改画布尺寸后，检查精灵矩形是否仍在图像范围内。

## 保存图像的精度

画布尺寸旁的 **Precision** 控制 TIFF 内保存图像的精度：

- **Auto：**0–1 范围内使用每通道 8 位；结果需要 HDR 时使用 Float32。
- **8-bit：**始终使用每通道 8 位；超出 0–1 的值在保存的合成图像中被截断。
- **Float32：**保留渐变和高度图的细微差别，包括 0–1 范围内的差别；文件可能更大。

这与 Inspector 中的 GPU 压缩是独立设置。工作渲染使用 half-float，
因此 Float32 无法恢复之前已丢失的精度。Drawing 图层保留自己的像素格式。

## Live Update 与保存

[Live Update](preview.md#在模型上查看你的绘制) 可在使用该 TIFF 的对象上显示尚未保存的编辑。
同时只能有一个文档发布实时更新。会话期间纹理使用未压缩格式，必要时临时启用 Read/Write；
结束后恢复原设置。临时 Read/Write 更改会反映在 `.meta` 中。

保存时会短暂暂停 Live Update，并在导入后恢复。关闭或切换文档、脚本重载及外部重新导入
会结束会话。构建 Player 也会停止会话，并使用最后保存的 TIFF：构建前请保存编辑，
构建后重新启用 Live Update。如果无法恢复纹理，构建将报错并停止。

Live Update 支持 2D **Default** 和 **Sprite** 导入。Crunch 及其他纹理类型在保存时更新。
最终压缩和导入处理的效果可能与实时预览不同。

## 保留文档的可编辑性

请保留 TIFF 及其 `.meta`，并在 Unity 内移动或重命名资源，以保留引用。
不要在外部图像编辑器中重新保存 TIFF，否则可能丢失可编辑图层。
文件内容说明见 [TIFF 文档](tiff-format.md)。

保存前请先 Apply 修改后的 Shader FX 代码。如果文件被外部修改，请重新打开或使用
**Save As**。如果缺失类型、字段或引用资源导致保存被阻止，请恢复所需软件包或资源后
重新打开文档。如果文件写入后 Unity 报告导入错误，修复错误并再次保存以重试导入。

### 耗时操作与限制

较长的保存或打开操作会显示可取消的进度。取消保存会保留原 TIFF 和当前编辑；
请等待已启动的处理步骤结束。最终文件替换和 Unity 导入阶段无法取消。
这不是后台编辑：操作期间无法修改文档，渲染和导入仍可能短暂阻塞界面。

TIFF 文档限制：画布每边最多 **16384**，工作 half-float 缓冲区和解码后的输出图像各小于 **2 GiB**；
嵌入像素在文件压缩前，**每张纹理最多 256 MiB**，**合计最多 1 GiB**。
例如，一个 8192×8192 RGBAHalf Drawing 需要 512 MiB，因此无法保存；请减小源分辨率或拆分文档。
这些是实现限制，并不保证在最大尺寸下仍能流畅编辑。

### 恢复中断的保存

如果崩溃后留下 **.whimtex-tmp** 文件，选择 **Tools → WhimTex → Recovery → Recover Staged TIFF…**。
WhimTex 验证文件后可将其保存为**新的 TIFF**，原文档和临时文件都会保留。
不完整或损坏的写入无法用此方式恢复。恢复副本具有新 GUID 和默认导入设置；原有引用不会自动指向它。
这不是自动保存：未启动 Save 的编辑无法在这里恢复。

如果故障后 Live Update 的临时 Read/Write 设置未恢复，请先恢复缺失资源或修复导入错误，
再选择 **Tools → WhimTex → Recovery → Retry Live Update Recovery**。不要手动删除恢复日志。

## 迁移旧文档

不再支持 `.asset` 文档。升级前，请在 WhimTex 0.12.5 中打开旧文档，通过 **Save As** 保存为 TIFF。检查图层、Drawing 像素和导入设置，再按需要将新 TIFF 指定给材质和 File 图层。当前版本读取 TIFF/JSON，不读取旧版 `.asset` 文档。
