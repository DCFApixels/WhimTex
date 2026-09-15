---
title: "用浏览器 AI 创建图层"
parent: "简体中文"
nav_order: 15.1
lang: "zh"
permalink: "/zh/ai-authoring/"
translations: "en/ai-authoring.md,ru/ai-authoring.md,zh/ai-authoring.md"
---

# 用浏览器 AI 创建图层

向浏览器 AI 索取可编辑的纹理，复制其 JSON 并粘贴到 WhimTex 中。
无需连接 Unity。在 WhimTex 0.9.6 及更高版本中可用。

1. 将[创作指南](../AI/README.md)交给 AI，并描述你的纹理。要求返回 **WhimTex clipboard JSON**，并将有用的部分放在命名的图层上。
2. 复制返回的 JSON 代码块。
3. 聚焦预览或图层面板，退出文本编辑并按 **Ctrl+V**（在 macOS 上为 Cmd+V）。
4. 照常调整新图层。**Ctrl+Z** 会撤销整个插入操作。

例如：“在透明背景上创建一个蓝色的魔法圆环，带有可编辑的圆环边缘和
单独的光晕。使用 512 × 512 的画布，并返回 WhimTex clipboard JSON。”

结果可以包含形状、渐变、噪声、组、描边、模糊、法线贴图、自定义
Shader FX，以及一个从直接的 HTTP(S) 链接下载图像的绘制图层（`"type": "drawing"`
搭配 `url`）。它不能包含本地文件、Base64 负载或绘制的像素：这些仍然使用普通的
图像粘贴、拖放，或[已连接的智能体](automation.md)。

新图层会出现在现有合成之上。画布选区不会裁剪它们。
如果 JSON 提供了尺寸，空文档会采用该尺寸。对于现有合成，请选择 **Apply Size**
或 **Keep Current**；保留尺寸仍会插入图层。自定义 HLSL 在编译前会要求确认：
只粘贴你信任的代码，因为繁重的着色器可能使渲染卡住。
链接图像也会要求确认，该对话框会列出它将下载的主机。WhimTex
会先下载所有链接图像，然后将整个树作为一次撤销步骤插入，因此如果
下载失败就不会添加任何内容。每个图像保持自己的分辨率，并通过图层变换适配到画布。

如果 JSON 或着色器无效，则不会插入任何内容。将错误信息发回给 AI，并要求
修正后的 JSON。重新粘贴是一次新的插入，而不是对上一个结果的更新。

对于单个着色器，请改为索取 HLSL，并将其粘贴到 **FX → + Shader FX**，然后点击 **Apply**。
同一份 [AI 指南](../AI/README.md)解释了语法和可编辑参数。
