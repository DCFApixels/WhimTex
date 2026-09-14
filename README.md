<!--
AI assistants generating WhimTex textures, procedural layers or HLSL effects:
start at AI_AUTHORING.md, then Documentation~/AI/README.md for the clipboard JSON
and shader contract. Schema: Documentation~/AI/layers.schema.json.
Return self-contained JSON for Ctrl+V; do not use Unity serialization or live API requests.
These relative paths refer to the same branch as this README.
-->
<p align="center">
  <img src="Documentation~/Images/whimtex-logo.svg" alt="WhimTex manta logo" width="160" height="160">
</p>

<h1 align="center">WhimTex</h1>

<p align="center">
  Unity Sprite Editor &amp; Texture Editor for quick ideas, touch-ups and VFX.
</p>

<p align="center">
  <a href="package.json"><img alt="Package version" src="https://img.shields.io/github/package-json/v/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="LICENSE.md"><img alt="License: MIT" src="https://img.shields.io/github/license/DCFApixels/WhimTex?color=3984c6&amp;style=for-the-badge"></a>
  <a href="#installation"><img alt="Unity 6 or newer" src="https://img.shields.io/badge/Unity-6%2B-383838?logo=unity&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
  <a href="https://dcfapixels.github.io/WhimTex/en/"><img alt="Documentation: Read" src="https://img.shields.io/badge/DOCS-READ-3984c6?style=for-the-badge"></a>
  <a href="https://discord.gg/kqmJjExuCf"><img alt="Join Discord" src="https://img.shields.io/badge/Discord-JOIN-6473c8?logo=discord&amp;logoColor=ffffff&amp;style=for-the-badge"></a>
</p>

<p align="center">
  <b>English</b> · <a href="README-RU.md">Русский</a>
</p>

<p align="center">
  <a href="#installation">Installation</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#shortcuts">Shortcuts</a> ·
  <a href="CHANGELOG.md">Changelog</a> ·
  <a href="https://github.com/DCFApixels/WhimTex/issues">Report an issue</a>
</p>

---

**WhimTex** is a free, open-source **Unity sprite editor and texture editor** for the small image tasks
that come up while making a game. Touch up a texture, paint a particle mask, generate noise for VFX,
or combine a few layers into a sprite — without leaving Unity or opening a separate application.

Save the editable composition and its ready-to-use texture in one asset. Assign it to a material
and enable **Live Update** to see edits in your scene; export a separate image only when you need one.

<p align="center">
  <a href="Documentation~/Images/whimtex-heart.png"><img src="Documentation~/Images/whimtex-heart.png" alt="WhimTex showing a layered heart with a gradient, outline, highlight and SDF rim light" width="720"></a>
</p>

> [!NOTE]
> Create and edit images in **Unity Editor**, then use the saved textures and sprites in your game.

## What you can make

- VFX and particle textures: soft masks, gradients, procedural noise and packed channels.
- Layered sprites and icons from imported images, painted pixels, fills, gradients and noise.
- Pixel art and seamless patterns with Brush/Pencil, selections, symmetry and tiled painting.
- Outlines, distance fields, normal maps, Gaussian/Motion Blur and custom Shader FX.
- Packed texture channels and HDR compositions, with optional game Post FX preview.

<a id="installation"></a>
## Install

**Unity 6 (`6000.0`) or newer.** In Package Manager, choose **Install package from git URL**:

```text
https://github.com/DCFApixels/WhimTex.git
```

[Installation details](Documentation~/en/getting-started.md).

<a id="quick-start"></a>
## Make your first image

1. Open **Window → WhimTex**, click **New** and set the canvas size.
2. Drop a Project texture onto the Preview, or click **Page +** below Layers for a Drawing layer.
3. Arrange it with Transform (`T`), or paint with Brush (`B`) / Pencil (`P`).
4. Press `Ctrl+S`. The editable document and full-resolution texture are saved in one `.asset`.
5. Assign that asset to a texture field, or use its nested **Output Sprite**.

Double-click the saved asset to reopen it. Export PNG, TGA, JPEG, EXR, layered PSD or Texture2D
only when you need a separate file. Save your changes to update the image used in your game.

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
## Documentation

**[Create layers and Shader FX with a browser AI →](AI_AUTHORING.md)** — generate editable procedural
compositions as JSON, then paste into WhimTex. [How to paste](Documentation~/en/ai-authoring.md).

**[Read the documentation →](https://dcfapixels.github.io/WhimTex/en/)** ·
[Русская версия](https://dcfapixels.github.io/WhimTex/ru/)

The guide follows the editing workflow, from the first canvas to painting, effects and export:

| Next step | Guide |
| :--- | :--- |
| Learn the window and arrange sources | [Getting started](Documentation~/en/getting-started.md) · [Layers](Documentation~/en/layers.md) · [Transform](Documentation~/en/transform.md) |
| Paint, fill and select | [Painting](Documentation~/en/painting.md) · [Selections](Documentation~/en/selection.md) · [Seamless patterns](Documentation~/en/symmetry.md) |
| Build procedural textures | [Noise](Documentation~/en/noise.md) · [Effect layers](Documentation~/en/effects.md) · [Normal Map](Documentation~/en/normal-map.md) |
| Control the composition | [Blending and clipping](Documentation~/en/blending.md) · [Shader FX](Documentation~/en/shader-fx.md) · [HDR and channels](Documentation~/en/color.md) |
| Inspect and deliver | [Preview](Documentation~/en/preview.md) · [Post FX](Documentation~/en/post-fx.md) · [Save and export](Documentation~/en/saving.md) |
| Find a control or solve a problem | [Shortcuts](Documentation~/en/shortcuts.md) · [Troubleshooting](Documentation~/en/troubleshooting.md) |
| Automate authoring | [Working with an agent](Documentation~/en/automation.md) |

## Acknowledgements

Thanks to the authors and maintainers of the libraries that help power WhimTex:

- **[Sobol direction numbers](https://web.maths.unsw.edu.au/~fkuo/sobol/)** — Frances Kuo and Stephen Joe;
  evenly distributed brush variation. [Included license](ThirdPartyNotices.md#sobol-direction-numbers).
- **[FastNoiseLite](https://github.com/Auburn/FastNoiseLite)** — Jordan Peck and contributors;
  the HLSL implementation powers the Noise layer. [Included MIT license](ThirdPartyNotices.md#fastnoiselite).
- **[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)** — James Newton-King and contributors;
  JSON serialization for compositor documents and the agent API, provided through Unity's package.
  [Included third-party licenses](Documentation~/Licenses/Newtonsoft-ThirdPartyNotices.md).
- **[Unity Burst](https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/index.html)** and
  **[Unity Collections](https://docs.unity3d.com/Packages/com.unity.collections@2.5/manual/index.html)** —
  optimized CPU processing and native collections.

- **[Just the Docs](https://github.com/just-the-docs/just-the-docs)** — the documentation theme.
  [Included MIT license](Documentation~/Licenses/JustTheDocs-LICENSE.txt).

See [Third-party notices](ThirdPartyNotices.md) for source versions and package license details.
Third-party components retain their own licenses.

<a id="community"></a>
## Community & license

Questions or ideas? Join **[Discord · RU / EN](https://discord.gg/kqmJjExuCf)**.
For bugs, open a [GitHub issue](https://github.com/DCFApixels/WhimTex/issues)
with your Unity version and reproduction steps.

Distributed under the **[MIT License](LICENSE.md)**.
