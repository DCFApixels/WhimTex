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
无需连接 Unity。此指南描述当前统一 JSON 格式。

1. 将[创作指南](../AI/README.md)交给 AI，并描述你的纹理。要求返回 **WhimTex clipboard JSON**，并将有用的部分放在命名的图层上。
2. 复制返回的 JSON 代码块。
3. 聚焦画布视图或图层面板，退出文本编辑并按 **Ctrl+V**（在 macOS 上为 Cmd+V）。
4. 照常调整新图层。**Ctrl+Z** 会撤销整个插入操作。

例如：“在透明背景上创建一个蓝色的魔法圆环，带有可编辑的圆环边缘和
单独的光晕。使用 512 × 512 的画布，并返回 WhimTex clipboard JSON。”

结果可以包含形状、渐变、噪声、组、目标效果和自定义 Shader FX。
请索取 **whimtex.document** JSON。知道真实资源标识时，可以引用项目中已有的图片。
JSON 不保存 Drawing 像素；请通过普通粘贴、拖放或[连接到 Unity 的智能体](automation.md)添加图片。

旧的链接图片 JSON 已不再支持，也不会迁移。请使用对应的旧版 checkout 编辑；
新配方请遵循[当前 JSON 契约](../JSON_FORMAT.md)。

新图层会出现在现有合成之上，画布选区不会裁剪它们；详见[区域选择](selection.md)。
如果 JSON 提供了尺寸，空文档会采用该尺寸。对于现有合成，请选择 **Apply Size**
或 **Keep Current**；保留尺寸仍会插入图层。自定义 HLSL 在编译前会要求确认：
只粘贴你信任的代码，因为繁重的着色器可能使渲染卡住。
JSON 结构或图层引用错误会阻止插入。图层 FX 编译失败时会显示 **Paste with warnings**：
继续会保留代码和参数，但跳过该 FX；取消则不插入任何内容。警告图标标出有问题的效果，
修复代码后点击 **Apply** 即可恢复。向 AI 请求修复时，可从 **Window → General → Console** 复制诊断。
[画笔 JSON](painting.md#自定义画笔) 是独立格式，仍要求画笔着色器有效。
重新粘贴会创建新图层，而不是更新之前的结果。

对于单个着色器，请改为索取 HLSL，并将其粘贴到 **FX → + Shader FX**，然后点击 **Apply**。
同一份 [AI 指南](../AI/README.md)解释了语法和可编辑参数。
