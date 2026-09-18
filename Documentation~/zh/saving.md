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
没有图层的文档在关闭或切换时不会提示保存，但仍可手动保存。

保存后的资源可以直接当作**纹理**使用。
在 Project 中展开它即可使用 **Output Sprite**。双击它可继续编辑。

Unity 通常显示的是**最后保存的图像**。启用 [Live Update](preview.md#在模型上查看你的绘制)
即可在保存前于模型上查看编辑效果。如果关联纹理发生变化，请再次保存文档以更新输出图像。
文件图层会保留它们与源纹理的链接；请把这些源文件保留在项目中。

## 内嵌输出设置

点击 WhimTex 中的 **Output**，或在 Project 中选择已保存资源，然后点击 Inspector 中的 **WhimTex Output Settings…**。两种方式都会打开该文档的同一个设置窗口。点击 **Apply & Save Output** 或在 WhimTex 中保存，可更新内嵌纹理和单个 **Output Sprite**。新文档可先配置参数，再在 WhimTex 中首次保存；编辑字段不会在每次输入时重新生成输出。

- **Texture：**Filter Mode、Wrap U/V、Aniso Level 和 Generate Mip Maps。Wrap 控制纹理采样，而非图层平铺。
- **Storage：**HDR Half（默认）、HDR Float、Linear RGBA32 或 sRGB RGBA32。RGBA32 将数值限制在 0–1；sRGB 对 RGB 进行颜色编码。HDR Float 仅改变存储精度，不提高合成器的半精度计算精度。
- **Sprite：**Pixels Per Unit、归一化 Pivot、像素 Border（X/Y/Z/W 对应左/下/右/上）、Full Rect/Tight Mesh Type、Extrude 和 Generate Physics Shape。九宫格使用 Full Rect；边框必须位于画布尺寸内。

默认保持 HDR Half、无 mipmap、100 PPU 和居中轴心的矩形精灵。Read/Write 保持启用，以便 Live Update 恢复保存的纹理。此处暂不提供压缩、平台覆盖、自定义 mipmap 滤波或透明覆盖率保持。设置仅影响文档的内嵌输出，不影响单独导出的图像。

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
