<!--
AI assistants generating WhimTex textures, procedural layers, images from links or HLSL effects:
start at AI_AUTHORING.md, then Documentation~/AI/README.md for the clipboard JSON
and shader contract. Schema: Documentation~/AI/layers.schema.json.
Clipboard example folder: Documentation~/Examples/Clipboard/.
Read Documentation~/Examples/Clipboard/README.md, then the matching .json file.
For a Drawing image URL + Shader Processor, use stone-wall-retro.json in that folder.
The contract ends with common mistakes followed by the full JSON/HLSL specification.
Return clipboard JSON for Ctrl+V. A Drawing layer with `url` may fetch a PNG or JPEG from a direct http(s) link.
Do not use Unity serialization or live API requests.
These relative paths refer to the same branch as this README.
For brushes use Documentation~/AI/BRUSHES.md and Documentation~/AI/brush.schema.json.
Brush JSON examples: Documentation~/Examples/Brushes/README.md. Format: whimtex.brush.
-->
<p align="center">
  <img src="Documentation~/Images/whimtex-logo.svg" alt="WhimTex 鳐鱼标志" width="160" height="160">
</p>

<h1 align="center">WhimTex</h1>

<p align="center">
  直接在 Unity 中创作纹理、精灵和 VFX 遮罩，无需外部图像编辑器。
</p>

<p align="center">
  <a href="package.json"><img alt="包版本" src="https://img.shields.io/github/package-json/v/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="LICENSE.md"><img alt="MIT 许可证" src="https://img.shields.io/github/license/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="#installation"><img alt="Unity 6 或更高版本" src="https://img.shields.io/badge/Unity-6%2B-383838?logo=unity&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
  <a href="https://dcfapixels.github.io/WhimTex/zh/"><img alt="阅读文档" src="https://img.shields.io/badge/DOCS-READ-3984c6?style=for-the-badge"></a>
  <a href="https://discord.gg/kqmJjExuCf"><img alt="加入 Discord" src="https://img.shields.io/badge/Discord-JOIN-6473c8?logo=discord&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README-RU.md">Русский</a> · <b>简体中文</b>
</p>

<p align="center">
  <a href="#installation">安装</a> ·
  <a href="#why">为什么选择 WhimTex</a> ·
  <a href="#quick-start">快速开始</a> ·
  <a href="Documentation~/zh/shortcuts.md">快捷键</a> ·
  <a href="CHANGELOG.md">更新日志</a> ·
  <a href="https://github.com/DCFApixels/WhimTex/issues">报告问题</a>
</p>

---

**WhimTex** 是一款免费、开源的 **Unity 精灵与纹理编辑器**。
修饰纹理、绘制粒子遮罩、生成 VFX 噪声，或将多个图层组合成精灵——
这些原本需要打开图像编辑器的小任务，都可以直接在 Unity 中完成。

可编辑的合成文档和可直接使用的纹理保存在同一个 TIFF 文档中。图层、效果和变换仍可调整，
该 TIFF 可以直接指定给材质。只有需要独立图像文件时才需要导出。

<p align="center">
  <a href="Documentation~/Images/whimtex-heart.png"><img src="Documentation~/Images/whimtex-heart.png" alt="WhimTex 中由渐变、描边、高光和 SDF 边缘光组成的分层爱心" width="720"></a>
</p>

<a id="why"></a>
## 为什么选择 WhimTex

- **无需离开 Unity。** 绘制并检查结果，不必在不同编辑器之间传递文件。
- **保留可编辑性。** 图层、渐变、噪声、描边和 Shader FX 都可以随时调整，而不是永久烘焙成像素。
- **融入项目。** 将保存的 TIFF 用作**纹理**，或以 **Sprite (2D and UI)** 类型导入以使用精灵。从 Project 拖入纹理、画笔预设和 HLSL 效果，预设也能与项目一起保存。
- **在模型上查看效果。** **Live Update** 在绘制过程中实时更新场景对象上的纹理。
- **按需使用程序化工具。** 通过参数创建噪声、渐变、形状、距离场和描边，无需逐笔绘制。
- **与 AI 协作。** 已连接的智能体可以在打开的文档中添加和编辑图层。浏览器 AI 则可以用 JSON 描述图层、画笔或 HLSL 效果，通过 `Ctrl+V` 即可粘贴。创作指南、格式规范和示例帮助生成有效内容，粘贴错误会显示在 Console 中。
- **无运行时依赖。** WhimTex 仅在编辑器中运行，游戏使用的是最终纹理和精灵。

## 可以创作什么

- VFX 和粒子纹理：柔和遮罩、渐变和程序化噪声。
- 由导入图像、绘制内容、填充、渐变和噪声组成的多图层精灵与图标。
- 使用画笔、铅笔、选区和对称功能制作的像素画与无缝图案。
- 描边、距离场、法线贴图、Gaussian/Motion Blur 和自定义 Shader FX。
- 通道打包纹理和 HDR 合成，并可预览游戏中的 Post FX。

<p align="center">
  <a href="Documentation~/Images/vfx-energy-ring.png"><img src="Documentation~/Images/vfx-energy-ring.png" alt="由渐变和噪声图层构成的 VFX 能量环" width="250"></a>
  <a href="Documentation~/Images/uv-rubik-cube.png"><img src="Documentation~/Images/uv-rubik-cube.png" alt="Live Update：纹理修改立即显示在 Scene view 的立方体上" width="250"></a>
  <a href="Documentation~/Images/brush-settings.png"><img src="Documentation~/Images/brush-settings.png" alt="WhimTex 的画布、图层面板和画笔设置" width="250"></a>
</p>

> [!NOTE]
> 需要 **Unity 6（`6000.0`）**或更高版本。

<a id="installation"></a>
## 安装

在 Package Manager 中选择 **Install package from git URL**，粘贴：

```text
https://github.com/DCFApixels/WhimTex.git
```

[安装详情](Documentation~/zh/getting-started.md)。

<a id="quick-start"></a>
## 制作第一张图像

1. 打开 **Window → WhimTex** 并设置画布尺寸。点击 **New** 可再创建一个文档。
2. 点击 Layers 底部的 **+**，选择 **Drawing Layer** 创建绘制图层。也可以将已有纹理从 Project 拖到预览上。
3. 使用 Transform（`T`）调整位置，或使用 Brush（`B`）/ Pencil（`P`）绘制。
4. 按 `Ctrl+S`。可编辑文档和完整分辨率纹理会保存在同一个 `.tiff` 文件中。
5. 将 TIFF 指定给纹理字段。需要精灵时，在其 Inspector 中选择 **Texture Type → Sprite (2D and UI)**，点击 **Apply**，然后在 Project 中展开资源。

双击保存的 TIFF 即可再次编辑。通过 **Export** 可导出 PNG、TGA、JPEG、EXR、多图层 PSD 或 Texture2D。

<a id="workspace"></a>
<a id="layers"></a>
<a id="transform"></a>
<a id="painting"></a>
<a id="symmetry"></a>
<a id="effects"></a>
<a id="preview"></a>
<a id="saving"></a>
<a id="export"></a>
<a id="shortcuts"></a>
<a id="automation"></a>
## 文档

**[用浏览器 AI 创建图层和 Shader FX →](AI_AUTHORING.md)** — 生成可编辑的程序化合成 JSON，
再粘贴到 WhimTex。[如何粘贴](Documentation~/zh/ai-authoring.md)。

**[阅读文档 →](https://dcfapixels.github.io/WhimTex/zh/)** ·
[English](https://dcfapixels.github.io/WhimTex/en/) ·
[Русский](https://dcfapixels.github.io/WhimTex/ru/)

指南按工作流程组织，从第一个画布到绘制、效果和导出：

| 下一步 | 指南 |
| :--- | :--- |
| 熟悉窗口并排列来源图像 | [入门](Documentation~/zh/getting-started.md) · [图层](Documentation~/zh/layers.md) · [变换](Documentation~/zh/transform.md) |
| 绘制、填充和选择 | [绘制](Documentation~/zh/painting.md) · [选区](Documentation~/zh/selection.md) · [无缝图案](Documentation~/zh/symmetry.md) |
| 创建程序化纹理 | [Noise](Documentation~/zh/noise.md) · [效果图层](Documentation~/zh/effects.md) · [Normal Map](Documentation~/zh/normal-map.md) |
| 控制合成结果 | [混合与剪贴蒙版](Documentation~/zh/blending.md) · [Shader FX](Documentation~/zh/shader-fx.md) · [HDR 与通道](Documentation~/zh/color.md) |
| 检查并使用结果 | [预览](Documentation~/zh/preview.md) · [Post FX](Documentation~/zh/post-fx.md) · [保存与导出](Documentation~/zh/saving.md) |
| 查找操作或解决问题 | [快捷键](Documentation~/zh/shortcuts.md) · [疑难解答](Documentation~/zh/troubleshooting.md) |
| 自动化创作 | [与智能体协作](Documentation~/zh/automation.md) |

## 致谢

感谢为 WhimTex 提供部分功能基础的库作者和维护者：

- **[Sobol 方向数](https://web.maths.unsw.edu.au/~fkuo/sobol/)** — Frances Kuo 和 Stephen Joe；
  用于均匀分布的画笔变化。[附带许可证](ThirdPartyNotices.md#sobol-direction-numbers)。
- **[FastNoiseLite](https://github.com/Auburn/FastNoiseLite)** — Jordan Peck 及贡献者；
  Noise 图层使用其 HLSL 实现。[附带 MIT 许可证](ThirdPartyNotices.md#fastnoiselite)。
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** — James Newton-King 及贡献者；
  通过 Unity 包提供合成文档和智能体 API 的 JSON 序列化。
  [附带第三方许可证](Documentation~/Licenses/Newtonsoft-ThirdPartyNotices.md)。
- **[Unity Burst](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html)** 和
  **[Unity Collections](https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/index.html)** —
  优化的 CPU 计算与原生集合。

- **[Just the Docs](https://github.com/just-the-docs/just-the-docs)** — 文档网站主题。
  [附带 MIT 许可证](Documentation~/Licenses/JustTheDocs-LICENSE.txt)。

源码版本和包许可证信息见 [Third-party notices](ThirdPartyNotices.md)。
第三方组件保留各自的许可证。

<a id="community"></a>
## 社区与许可证

有问题或想法？欢迎加入 **[Discord · RU / EN](https://discord.gg/kqmJjExuCf)**。
报告错误请创建 [GitHub issue](https://github.com/DCFApixels/WhimTex/issues)，
附上 Unity 版本和复现步骤。

以 **[MIT 许可证](LICENSE.md)**分发。
