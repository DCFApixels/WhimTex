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

用 WhimTex 直接在 Unity 中绘制纹理、制作程序化 VFX 遮罩，并将图层组合为精灵。
先[制作第一张图像](getting-started.md)，再选择下方需要的任务。
控件名称保留英文，与编辑器一致。[技术参考](../reference.md)介绍文件格式、集成和完整着色器语法；普通编辑无需阅读。

## 开始与组织

- [入门](getting-started.md)：安装、第一张图像及工作区。
- [图层与组](layers.md)：图像组成、来源和程序化形状。
- [变换与栅格化](transform.md)：放置内容，并决定何时转换为像素。

## 绘制与选择

- [画笔、铅笔与填充](painting.md)：笔触、笔尖和预设。
- [Smudge](painting.md#smudge-brush) 与[修复画笔](painting.md#修复画笔)：拉伸或修复已有像素。
- [区域选择](selection.md)
- [对称与无缝绘制](symmetry.md)

## 构建程序化效果

- [噪声](noise.md)：图案、扭曲和无缝重复。
- [效果图层](effects.md)：Outline、SDF、Blur 与 Make Seamless。
- [Normal Map](normal-map.md)：从图像创建表面起伏。
- [混合与剪贴](blending.md)：组合图层、约束覆盖范围。
- [Shader FX 与处理器](shader-fx.md)：使用预设或自定义 HLSL。
- [着色器参数控件](shader-controls.md)：自定义效果的可选语法参考。

## 检查与交付

- [画布视图与导航](preview.md)
- [颜色、HDR 与通道](color.md)
- [游戏后处理](post-fx.md)
- [保存与导出](saving.md)
- [TIFF 文档](tiff-format.md)

## 获取帮助或自动化

- [键盘快捷键](shortcuts.md)
- [自动化](automation.md)
- [故障排查](troubleshooting.md)
