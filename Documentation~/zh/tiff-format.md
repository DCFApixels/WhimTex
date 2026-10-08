---
title: "TIFF 文档"
parent: "简体中文"
nav_order: 13.1
lang: "zh"
permalink: "/zh/tiff-format/"
translations: "en/tiff-format.md,ru/tiff-format.md,zh/tiff-format.md"
previous_page: "zh/saving.md"
next_page: "zh/shortcuts.md"
---

# TIFF 文档

需要可编辑的 WhimTex 文档和 Unity 纹理时，保存为 TIFF。
双击同一个文件可以编辑图层，将它指定给材质则使用已保存的图像。
这两种用途都不需要另行导出。

## 文件包含什么

一个 `.tiff` 包含最后保存的合成图像、可编辑图层与 FX，以及 Drawing 像素。
File 图层仍引用源纹理，请将这些资源保留在项目中。

**Precision** 控制保存图像的 8-bit 或 Float32 精度。Mipmaps、压缩、精灵切片和平台覆盖
仍使用 Unity 的标准导入设置。具体选择请参阅[保存与纹理设置](saving.md)。

## 加载与保存

按 **Ctrl+S** 更新文档，使用 **Save As** 创建另一个 TIFF。Unity 通常显示最后保存的图像；
[Live Update](preview.md#在模型上查看你的绘制) 可以显示未保存的修改。

大型 Drawing 图层按需加载。保存时先准备并检查临时文件，再替换旧 TIFF。
如果源文件在外部发生变化，请重新打开或使用 **Save As**，避免覆盖不同的版本。
出现未读取数据警告或保存中断时，请参阅[文档保护与恢复](saving.md#保留文档的可编辑性)。

## 兼容性

{: .warning }
不要在其他图像编辑器中重新保存 WhimTex TIFF：它可能只保留图像，删除可编辑图层。
需要在其他编辑器中处理时，导出单独的图像，并保留原 TIFF 及其 `.meta`。

旧 `.asset` 文档和版本 1 文档需要使用支持它们的 WhimTex 版本；当前版本不会自动转换。

[JSON](saving.md#json-文档) 保留可编辑设置，但没有 Drawing 像素或 Unity 纹理。
PNG、JPEG、TGA 和 EXR 只保留导出图像。PSD 保留部分图层与效果；需要全部 WhimTex 设置时，保留原 TIFF。

二进制布局、校验和、限制及加载规则请参阅 [TIFF 文档格式技术参考]({{ '/reference/tiff-format/' | relative_url }})。
