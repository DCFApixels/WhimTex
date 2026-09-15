---
title: "疑难解答"
parent: "简体中文"
nav_order: 16
lang: "zh"
permalink: "/zh/troubleshooting/"
translations: "en/troubleshooting.md,ru/troubleshooting.md,zh/troubleshooting.md"
previous_page: "zh/automation.md"
---

# 疑难解答

## 画笔没有留下痕迹

1. 选择一个绘制图层。
2. 确认已选择 **Brush** 或 **Pencil**，且笔尖尺寸没有过小；参见[绘制设置](painting.md)。
3. 打开预览底部的 **R / G / B / A**，检查绘制颜色的透明度；参见[颜色设置](color.md)。
4. 如果画布有选区，请[清除它](selection.md)：笔触不会出现在选区外。
5. 检查图层是否可见，且其不透明度不为零。
6. 如果启用了剪贴蒙版，请检查基础图层是否可见并包含图像。

在经过变换的图层上，请确保你绘制在图层图像之上。

## 颜色看起来不对

将 **EV** 重置为 0 并打开 **RGBA**。
禁用 **Post FX**，以便与未处理的图像进行比较。
检查图层的混合模式和不透明度，然后检查其[颜色设置](color.md)。

如果你正在制作法线贴图或其他数据纹理，请检查该图层设置中的
Encoding 选项。

## 效果为空或使用了错误的图像

检查 **Input / Target**，尤其是在移动图层之后。
**Previous** 使用同一组中紧邻效果下方的图层。
如果你希望在重新排列列表时保持同一来源，请使用 **Specific**。

隐藏的来源仍然有效。组内需要参与效果的子图层应保持可见；详见[效果来源](effects.md#选择效果使用的对象)。

## 重复图像中出现接缝

检查你正在使用哪种[重复模式](symmetry.md)。
对于模糊和 Normal Map，除了启用平铺预览外，还要将 **Edges → Repeat** 设置好。
Tiled 预览不会消除接缝。可以尝试 [Make Seamless](effects.md#make-seamless) 接合相对边缘。

## Unity 显示较旧的图像

编辑后（包括修改了关联纹理后），请在 WhimTex 中点击 **Save**；详见[保存说明](saving.md)。

## 绘制感觉很慢

降低预览页脚中的 **Live Quality**。
对于复杂的图像，在绘制时暂时隐藏你不需要的效果。
无论 **Live Quality** 如何设置，Pencil 都使用完整分辨率，保证像素绘制的精度。

## Post FX 看起来与游戏中不同

检查相机或配置文件的选择，并确保已启用后处理。
预览不包含周围场景，并且某些效果不受支持。
参见 [Post FX](post-fx.md)。

## 还是卡住了？

请在[错误报告](https://github.com/DCFApixels/WhimTex/issues)中附上包版本和 Unity 版本、
问题的重现步骤，以及（如果可能）一份可以分享的小型文档。
