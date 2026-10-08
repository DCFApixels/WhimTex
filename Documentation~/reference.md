---
title: "Technical reference"
nav_order: 3
permalink: "/reference/"
search_exclude: true
has_children: true
has_toc: false
---

# Technical reference

This section is for integration developers and shader authors, not required reading for using the editor.
For painting, layers and export, use the artist guides in [English](en/index.md), [Russian](ru/index.md) or [Simplified Chinese](zh/index.md).
Technical specifications are maintained in English.

## Connect or automate

- [Live agent connection](LiveAgentAPI.md): select an Editor and window, inspect the open document and send commands.
- [Agent API](AgentAPI.md): discovery, operations, layer settings, painting and safety.
- [JSON API request examples](Examples/index.md): commands for a connected agent. These are requests, not document files.

## Store or exchange documents

- [TIFF document format](TIFF_FORMAT.md): carrier layout, model/Drawing blocks, lazy loading and recovery rules.
- [JSON document format](JSON_FORMAT.md): editable files, clipboard layers, write modes and asset references.
- [Document JSON recipes](Examples/Clipboard/README.md): procedural documents to open or paste as layers.
- [Layered PSD export](PsdExport.md): editable representation, rasterization and limits.

## Write shaders or inspect rendering

- [Shader authoring](ShaderFX.md): code, parameters, inputs and shared libraries.
- [Color range, HDR and groups](HDR.md): color spaces, storage, isolation and diagnostics.
- [Gaussian Blur and caching](GaussianBlur.md): source representations, quality and memory.
- [Motion Blur](MotionBlur.md): sampling, density and working-buffer costs.

Start with [effects](en/effects.md) or [color controls](en/color.md) if you need a UI workflow first.
