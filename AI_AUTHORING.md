# WhimTex AI authoring — start here

Want to generate **WhimTex procedural layers as clipboard JSON** or **HLSL Shader FX**?
Read the [self-contained authoring guide](Documentation~/AI/README.md).
It specifies the actual clipboard format, parameter names, HLSL interface and ready-to-copy examples.

- [JSON Schema](Documentation~/AI/layers.schema.json)
- [Complete JSON examples for AI authors](Documentation~/Examples/Clipboard/README.md): neon ring, car wheel, lightning, heart and more.
- [Artist workflow: English](Documentation~/en/ai-authoring.md) · [Русский](Documentation~/ru/ai-authoring.md)

No Unity connection, agent plugin or local files are needed: return JSON for the user to copy,
then paste with **Ctrl+V** in WhimTex. For shader-only requests return HLSL for **+ Shader FX**.
Do not substitute Unity serialized JSON, ShaderLab, or the live-agent operation protocol.
Do not claim to have inserted or tested anything when you only generated text.

The instructions in this repository describe the checked-out version. Procedural JSON clipboard
support requires WhimTex 0.9.6 or later; Drawing layers that download an image from a link require
0.10.0 or later.
