---
title: "WhimTex"
description: "WhimTex is a free Unity sprite editor and texture editor. Paint VFX masks, generate noise, combine layers and touch up textures without leaving Unity."
nav_order: 0
permalink: "/"
has_toc: false
---

<img class="brand-hero" src="{{ site.logo | relative_url }}" alt="WhimTex manta logo" width="144" height="144">

# WhimTex

**Unity Sprite Editor & Texture Editor**

Quick texture ideas, right where you use them. WhimTex is a free, open-source editor for
touch-ups, simple sprites and VFX textures directly inside Unity. Paint a particle mask,
generate noise, combine images or try a new effect without interrupting work on your game.

Save an editable composition that works as a texture in materials. Enable **Live Update**
to see changes in your scene as you paint, or export when you need a separate image.

[English user guide](en/index.md){: .btn .btn-primary }
[Руководство на русском](ru/index.md){: .btn }
[简体中文用户指南](zh/index.md){: .btn }

<img class="hero-image" src="{{ '/Images/whimtex-heart.png' | relative_url }}" alt="WhimTex showing a layered heart with a gradient, outline, highlight and SDF rim light" width="720">

## Choose your starting point

| I want to… | English | Русский | 简体中文 |
| :--- | :--- | :--- | :--- |
| Install and create an image | [Start here](en/getting-started.md) | [Начало работы](ru/getting-started.md) | [从这里开始](zh/getting-started.md) |
| Paint or edit pixels | [Brush, Pencil and Fill](en/painting.md) | [Кисть, карандаш и заливка](ru/painting.md) | [画笔、铅笔与填充](zh/painting.md) |
| Build textures from sources | [Layers](en/layers.md) · [Effects](en/effects.md) | [Слои](ru/layers.md) · [Эффекты](ru/effects.md) | [图层](zh/layers.md) · [效果](zh/effects.md) |
| Create VFX masks and noise | [Noise](en/noise.md) · [Channels](en/color.md) | [Шум](ru/noise.md) · [Каналы](ru/color.md) | [噪声](zh/noise.md) · [通道](zh/color.md) |
| Use the result in Unity | [Save and export](en/saving.md) | [Сохранение и экспорт](ru/saving.md) | [保存与导出](zh/saving.md) |
| Automate authoring | [Automation](en/automation.md) | [Автоматизация](ru/automation.md) | [自动化](zh/automation.md) |

## Requirements and scope

Unity 6 (`6000.0`) or newer. WhimTex runs only in the Editor;
use the saved textures and sprites in your game.
Game Post FX preview is optional and currently needs URP 17.x with Universal Renderer.
Other editing tools do not require a render pipeline package.

Creating an integration or writing shader code? The separate [technical reference](reference.md) is for developers.
See the [changelog](https://github.com/DCFApixels/WhimTex/blob/main/CHANGELOG.md)
for changes and [acknowledgements](credits.md) for third-party sources and licenses.
