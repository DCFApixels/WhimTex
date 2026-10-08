---
title: "从这里开始"
parent: "简体中文"
nav_order: 1
lang: "zh"
description: "在 Unity 6 中安装 WhimTex 并创建第一个分层纹理或精灵。绘制、添加图片，保存可直接用于游戏的可编辑 TIFF。"
permalink: "/zh/getting-started/"
translations: "en/getting-started.md,ru/getting-started.md,zh/getting-started.md"
next_page: "zh/layers.md"
---

# 从这里开始

## 安装

需要 **Unity 6 或更高版本**。打开 **Window → Package Management → Package Manager**，
选择 **Install package from git URL**，然后粘贴：

```text
https://github.com/DCFApixels/WhimTex.git
```

## 制作你的第一张图像

1. 打开 **Window → WhimTex** 并设置画布的 **W / H**。**New** 会在单独的标签页中创建另一个空文档，并保持当前文档处于打开状态。
2. 点击 Layers 底部的 **+**，选择 **Drawing Layer**。你可以直接在这个图层上绘制。
3. 选择 **Transform**（`T`）来调整图像位置，或选择 **Brush**（`B`）进行绘制。
4. 按 `Ctrl+S` 并选择文档的保存位置。
5. 文档保存为 TIFF，可像普通 Unity 纹理一样用于材质或作为精灵使用。

双击保存的 TIFF 即可继续编辑。无需先导出它。
它会在自己的 WhimTex 窗口中打开，不会替换当前文档。
如果它已经打开，Unity 会聚焦该窗口，而不是打开一个重复的窗口。

每个 WhimTex 标签页都会显示其文档的名称。新文档以 **Untitled** 开始；保存后，标签页会使用文件名。星号标记未保存的更改。

要使用已有图像，将它从 Project 拖入 Canvas View。这会添加一个引用源纹理的 **File** 图层。
需要修整时，在上方添加 Drawing 图层，或在尝试绘制时确认将 File 转换为 Drawing。

将纹理拖入空文档会把画布尺寸设置为该纹理在 Unity 中的尺寸。
当你把 **File** 作为唯一的图层添加并指定其第一个 **Source Texture** 时，也会发生同样的情况。
之后替换纹理不会改变画布尺寸。当同时拖入多个纹理时，
第一个纹理决定尺寸。

## 熟悉界面

画布位于左侧。**Layers** 是右侧的列表；其上方的 **Layer Settings**
显示所选图层的控件。拖动分隔条即可在需要的地方腾出更多空间。

在左侧工具栏上选择一个工具；其选项会显示在画布上方。
按住鼠标滚轮并拖动可以平移。滚轮滚动可以缩放；**Fit** 显示整个画布。

| 工具 | 按键 | 用途 |
| :--- | :---: | :--- |
| Layer Select | `V` | 点击可见像素以选择图层。 |
| Transform | `T` | 移动、缩放和旋转图层。 |
| Area Select | `M` | 选择矩形或椭圆。按住工具按钮可选择形状。 |
| Polygonal Lasso | `L` | 通过沿轮廓点击来选择区域。 |
| Brush / Pencil | `B` / `P` | 绘制柔和的笔触或锐利的像素。 |
| Fill | `G` | 用颜色填充区域。 |
| Zoom | `Z` | 放大或框选某个区域。 |

## 让工作区更顺手

**Export 右侧的齿轮按钮**会打开 User Settings，你可以在其中更改透明棋盘格的颜色
和尺寸，或启用 **Clean Canvas View Background** 来隐藏背景标志和阴影。在此设置窗口的底部，
**Reset WhimTex Settings…** 会在确认后恢复工作区首选项，而不会删除你的文档或预设文件。

要将图层的设置保留在单独的窗口中，请使用 **layer ⋮ → Properties**。

## 打开已有图像

在 **User Settings → Open Images** 中选择双击 Project 资源时的行为：

- **Tiff Documents Only：**打开 WhimTex TIFF 文档。
- **All Supported Images**（默认）：还支持 PNG、JPEG、BMP、TGA、EXR、普通 TIFF 和 Texture2D `.asset`，不包括 PSD。

WhimTex TIFF 文档恢复原有图层。对于普通图像，**Open As** 决定来源类型：

| 模式 | 适用情况 |
| --- | --- |
| **Drawing** | 需要可直接绘画的独立像素副本。 |
| **File** | 需要链接到源纹理的图层。 |

{: .warning }
**Save 可能覆盖源文件。** 双击普通 PNG、JPEG、TGA、EXR 或 Texture2D `.asset` 会打开与源图像关联的新文档，即使选择 **Open As → Drawing** 也是如此。
只有一个顶层图层时（一个组也算一个图层），**Save** 更新源图像。
有多个顶层图层时，Save 询问 TIFF 保存位置；保存 TIFF 之前如果又只剩一个图层，仍可能更新源图像。
**Save As** 创建 TIFF，不修改源图像及其导入设置。其他图像格式需要使用 Save As。
将纹理拖入新文档只添加 File 图层，不会让 Save 指向源图像。

**源分辨率。** PNG、JPEG、BMP、TGA 和 EXR 使用原始文件的尺寸和像素，不受 Unity 导入缩小或压缩影响。
这适用于 File 渲染、作为 Drawing 打开，以及将 File 转为 Drawing。其他格式使用导入纹理。
EXR 解码支持 Windows、macOS 和 Linux Editor。
