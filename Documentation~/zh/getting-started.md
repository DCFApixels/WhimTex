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
2. 点击 Layers 底部的 **+**，选择 **Drawing Layer** 创建绘制图层。如果已有纹理，也可以直接从 Project 拖到预览上。
3. 选择 **Transform**（`T`）来调整图像位置，或选择 **Brush**（`B`）进行绘制。
4. 按 `Ctrl+S` 并选择文档的保存位置。
5. 将保存的 TIFF 用作 Unity 纹理。需要精灵时，在其 Inspector 中选择 **Texture Type → Sprite (2D and UI)** 并点击 **Apply**，然后在 Project 中展开 TIFF，使用其精灵。

双击保存的 TIFF 即可继续编辑。无需先导出它。
它会在自己的 WhimTex 窗口中打开，不会替换当前文档。
如果它已经打开，Unity 会聚焦该窗口，而不是打开一个重复的窗口。

每个 WhimTex 标签页都会显示其文档的名称。新文档以 **Untitled** 开始；保存后，标签页会使用文件名。星号标记未保存的更改。

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
和尺寸，或启用 **Clean Preview Background** 来隐藏背景标志和阴影。在此设置窗口的底部，
**Reset WhimTex Settings…** 会在确认后恢复工作区首选项，而不会删除你的文档或预设文件。

要将图层的设置保留在单独的窗口中，请使用 **layer ⋮ → Properties**。
