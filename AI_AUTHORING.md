# WhimTex AI authoring — start here

Want to generate **WhimTex layers as clipboard JSON** — procedural layers, or a Drawing layer with
`url` that fetches an image from a direct http(s) link — or **HLSL Shader FX**?
Start with the [authoring guide](Documentation~/AI/README.md): it points to example files first,
then lists common mistakes and the full JSON/HLSL specification at the bottom.

**The clipboard example folder is `Documentation~/Examples/Clipboard/`.**
Read the [example index](Documentation~/Examples/Clipboard/README.md), then open the actual
`.json` recipe that matches the task. For an image URL with a shader effect, use
[stone-wall-retro.json](Documentation~/Examples/Clipboard/stone-wall-retro.json);
for editable shapes, noise or VFX, choose a recipe from the index.
Do not use the live-agent examples in the parent folder for Ctrl+V.

- [JSON Schema](Documentation~/AI/layers.schema.json)
- [Complete JSON examples for AI authors](Documentation~/Examples/Clipboard/README.md): neon ring, car wheel, lightning, heart and more.
- [Artist workflow: English](Documentation~/en/ai-authoring.md) · [Русский](Documentation~/ru/ai-authoring.md) · [简体中文](Documentation~/zh/ai-authoring.md)

No Unity connection, agent plugin or local files are needed: return JSON for the user to copy,
then paste with **Ctrl+V** in WhimTex. For shader-only requests return HLSL for **+ Shader FX**.
Do not substitute Unity serialized JSON, ShaderLab, or the live-agent operation protocol.
Do not claim to have inserted or tested anything when you only generated text.

The instructions in this repository describe the checked-out version. Procedural JSON clipboard
support requires WhimTex 0.9.6 or later; Drawing layers that download an image from a link require
0.10.1 or later.
