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

结果可以包含形状、渐变、噪声、组、描边、模糊、锐化、法线贴图、自定义
Shader FX，以及从直接 HTTP(S) 链接下载图像的绘制图层
（`"type": "drawing"`，带有 `url` 字段）。
此格式不支持内嵌像素数据、Base64 或本地文件路径。
本地图像请通过普通图像粘贴、拖放或[连接到 Unity 的智能体](automation.md)添加。

新图层会出现在现有合成之上，画布选区不会裁剪它们；详见[区域选择](selection.md)。
如果 JSON 提供了尺寸，空文档会采用该尺寸。对于现有合成，请选择 **Apply Size**
或 **Keep Current**；保留尺寸仍会插入图层。自定义 HLSL 在编译前会要求确认：
只粘贴你信任的代码，因为繁重的着色器可能使渲染卡住。
下载图像前会显示确认对话框，列出来源网站。任何一张图像下载失败，整个插入操作都会取消。
每张图像保留原始分辨率，通过图层变换适配画布。撤销会将文档恢复到插入前的状态。

如果 JSON 或着色器无效，不会插入任何内容。打开 **Window → General → Console**，
复制 WhimTex 错误并让 AI 修正。[画笔 JSON](painting.md#自定义画笔) 也适用。
重新粘贴会创建新图层，而不是更新之前的结果。

对于单个着色器，请改为索取 HLSL，并将其粘贴到 **FX → + Shader FX**，然后点击 **Apply**。
同一份 [AI 指南](../AI/README.md)解释了语法和可编辑参数。
