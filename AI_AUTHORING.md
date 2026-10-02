# WhimTex AI authoring — start here

Use **`whimtex.document`, version 1** for new document files, layer fragments and clipboard JSON.
Read the [authoring guide](Documentation~/AI/README.md), the
[shared JSON contract](Documentation~/JSON_FORMAT.md), and the
[document schema](Documentation~/AI/document.schema.json).

Open the [clipboard example index](Documentation~/Examples/Clipboard/README.md), then read an
actual procedural recipe matching the task. These are stored documents, not live API requests.
Use `behaviour.$type`, native model fields and `modifiers`; do not use legacy `type/properties/fx`.
`document` is optional, but specify width and height for predictable source-canvas context.
There is no `kind` discriminator. Default export mode is `FullOptimized`; use Full/Compact on request.

For more involved VFX and surfaces, use the [internal texture samples](Samples~/AgentTextures/README.md).
`Samples~/AgentTextures/manifest.json` indexes 38 editable recipes and individual PNG previews,
with descriptions, tags and canvas dimensions (longest axis 256 pixels). Read only relevant examples.
These are reference documents, not automatically imported presets; no TIFF duplicates or atlas are bundled.

No Unity connection is needed to return JSON for the user to copy and paste with **Ctrl+V**.
**Copy as JSON** uses the same shared format. **Export** can create a `.json` file;
**Save As** creates TIFF. Never claim to have inserted, compiled or tested generated text.

Drawing pixels are not stored in JSON. Exported nonempty Drawing layers become warned placeholders.
Use verified `$asset` identities for existing project assets; do not invent GUIDs or paths.
Old `whimtex.layers` payloads, including Drawing `url` imports, remain accepted only through the
[compatibility clipboard reader](Documentation~/AI/LEGACY_LAYERS.md), not as the format for new output.

**Generating a brush?** Read the [brush contract](Documentation~/AI/BRUSHES.md),
[brush examples](Documentation~/Examples/Brushes/README.md) and
[brush schema](Documentation~/AI/brush.schema.json). `whimtex.brush` replaces the current brush.
Standalone gradients likewise retain their separate `whimtex.gradient` value format.

For shader-only requests return HLSL for **+ Shader FX**, not ShaderLab. Follow the
[HLSL interface](Documentation~/AI/README.md#hlsl-interface--shader-only-or-inside-json).
For connected operations use [AgentAPI](Documentation~/AgentAPI.md) or the live skill, not a
command envelope inside document JSON.

Artist workflow: [English](Documentation~/en/ai-authoring.md) · [Русский](Documentation~/ru/ai-authoring.md) · [简体中文](Documentation~/zh/ai-authoring.md).
