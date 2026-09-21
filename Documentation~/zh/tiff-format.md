---
title: "TIFF 文档"
parent: "简体中文"
nav_order: 13.1
lang: "zh"
permalink: "/zh/tiff-format/"
translations: "en/tiff-format.md,ru/tiff-format.md,zh/tiff-format.md"
---

# TIFF 文档

WhimTex 将新的可编辑文档保存为一个 `*.tiff` 文件。它同时是 Unity 可导入的普通纹理和可编辑
文档：可见的 TIFF 图像是最后保存的合成结果，而图层模型与 Drawing 数据位于附加的 WhimTex
容器中。

## 文件包含什么

文件首先写入 TIFF 图像，因此 Unity 可以使用标准 Texture Importer 导入它。图像之后是 WhimTex
二进制容器，其中包含 `document` 模型块、独立的 Drawing 像素块和小型 carrier 块。目录记录
每个块的名称、压缩方式和长度；`integrity:sha256` 清单用于检测意外截断或修改。这不是 ZIP
文件，也不是第二个 Unity 资源。

普通输出使用每通道 8 位；HDR 或明确选择 `Precision → Float32` 时使用 Float32。Mipmaps、GPU
压缩、精灵切片和平台覆盖仍由 Unity 标准 Texture Importer 控制，不属于图层数据。

## 加载与保存

打开文件时先读取 footer、块目录和模型。Drawing 像素块会在渲染、预览、编辑或保存需要时延迟
加载，不会一次性解压整个文档。保存会先写入旁边的临时文件、完成校验，再原子替换旧 TIFF。
如果源文件在外部发生变化，WhimTex 不会合并两个版本，而是要求重新打开或使用 **Save As**。

## 兼容性

不要使用外部图像编辑器重新保存 WhimTex TIFF：它可能删除附加容器，只留下合成图像。旧版
`.asset` 仍可打开查看或迁移，但新文档只保存为 TIFF，迁移不会覆盖旧资源。PNG、JPEG、TGA、
EXR 和 PSD 是普通的扁平导出，不包含可编辑的 WhimTex 图层。

完整的字节布局、限制和块规则请参阅 [TIFF 文档格式技术参考]({{ '/reference/tiff-format/' | relative_url }})。
