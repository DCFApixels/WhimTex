---
title: "简体中文"
description: "了解 WhimTex：用于快速绘制、VFX 遮罩、程序化噪声、分层纹理和场景实时更新的 Unity 精灵与纹理编辑器。"
nav_order: 1
lang: "zh"
permalink: "/zh/"
translations: "en/index.md,ru/index.md,zh/index.md"
has_children: true
has_toc: false
next_page: "zh/getting-started.md"
---

# WhimTex 用户指南

从第一份文档读到最终导出，或直接跳到你需要的工具。

将 WhimTex 用作 Unity 纹理编辑器，进行快速修复和制作 VFX 纹理；或用作精灵编辑器，
处理小型游戏图形。从一张图像开始，排列其图层，绘制细节并保存结果。
本指南中的控件名称与编辑器中的标签一致。

- [从这里开始](getting-started.md)
- [图层和组](layers.md)
- [变换和栅格化](transform.md)
- [画笔、铅笔和填充](painting.md)
- [区域选区](selection.md)
- [对称和无缝绘制](symmetry.md)
- [效果图层](effects.md)
- [混合和剪贴](blending.md)
- [Shader FX 和 Processor](shader-fx.md)
- [预览和导航](preview.md)
- [颜色、HDR 和通道](color.md)
- [游戏后处理](post-fx.md)
- [保存和导出](saving.md)
- [键盘快捷键](shortcuts.md)
- [自动化](automation.md)
- [疑难解答](troubleshooting.md)

对于生成的纹理，另请参见[噪声](noise.md)和[法线贴图](normal-map.md)。

## 为 VFX 创建纹理

组合渐变、[噪声](noise.md)和 [Shader Processor](shader-fx.md)，为能量环、爆发和其他效果创建遮罩。此圆环将明亮的边缘与细微的径向条纹结合在一起；它的各个图层让你可以分别调整形状和细节。

<a href="{{ '/Images/vfx-energy-ring.png' | relative_url }}"><img src="{{ '/Images/vfx-energy-ring.png' | relative_url }}" alt="WhimTex 中的 VFX 能量环纹理，包含渐变和噪声图层、一个 Shader Processor 以及圆形渐变的预览" width="720"></a>
