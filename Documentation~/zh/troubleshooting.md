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
2. 打开预览页脚中的全部四个通道按钮，并检查绘制颜色的透明度。
3. 按 `Ctrl+D` 移除区域选区。
4. 检查图层是否可见，且其不透明度不为零。
5. 如果启用了剪贴蒙版，请检查其基础图层是否可见并包含图像。

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

被隐藏的来源仍然有效。对于组来源，请让你想要包含的子项保持可见。

## 重复图像中出现接缝

检查你正在使用哪种[重复模式](symmetry.md)。
对于模糊和 Normal Map，除了启用平铺预览外，还要将 **Edges → Repeat** 设置好。
平铺会显示接缝；它不会自动让每个来源都变为无缝。

## Unity 显示较旧的图像

编辑后，包括更改了链接纹理之后，请在 WhimTex 中点击 **Save**。
Unity 使用最后保存的结果。仅预览的 Post FX 不会出现在该纹理中。

## 绘制感觉很慢

降低预览页脚中的 **Live Quality**。
对于复杂的图像，在绘制时暂时隐藏你不需要的效果。
铅笔会为精确的像素工作保持完整质量。

## Post FX 看起来与游戏中不同

检查相机或配置文件的选择，并确保已启用后处理。
预览不包含周围场景，并且某些效果不受支持。
参见 [Post FX](post-fx.md)。

## 还是卡住了？

请在[错误报告](https://github.com/DCFApixels/WhimTex/issues)中附上包版本和 Unity 版本、
问题的重现步骤，以及（如果可能）一份可以分享的小型文档。
