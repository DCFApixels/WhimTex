# WhimTex AI authoring — start here

For noise in HLSL FX or brush tips, [FastNoiseLite is already available](Documentation~/AI/README.md#built-in-noise-library)
without an include. Prefer its functions when they fit the result; custom algorithms remain an option.

Use **`whimtex.document`, version 3** for new document files, layer fragments and clipboard JSON.
Read the [authoring guide](Documentation~/AI/README.md), the
[shared JSON contract](Documentation~/JSON_FORMAT.md), and the
[document schema](Documentation~/AI/document.schema.json).

Open the [clipboard example index](Documentation~/Examples/Clipboard/README.md), then read an
actual procedural recipe matching the task. These are stored documents, not live API requests.
Use `behaviour.$type`, native model fields and `fx`; do not use legacy `type/properties` or live FX operation envelopes.
`document` is optional, but specify width and height for predictable source-canvas context.
There is no root `kind` discriminator. Default export mode is `FullOptimized`; use Full/Compact on request.

For more involved VFX and surfaces, use the [internal texture samples](Samples~/AgentTextures/README.md).
`Samples~/AgentTextures/manifest.json` indexes 38 editable recipes and individual PNG previews,
with descriptions, tags and canvas dimensions (longest axis 256 pixels). Read only relevant examples.
These are reference documents, not automatically imported presets; no TIFF duplicates or atlas are bundled.

No Unity connection is needed to return JSON for the user to copy and paste with **Ctrl+V**.
**Copy as JSON** uses the same shared format. **Export** can create a `.json` file;
**Save As** creates TIFF. Never claim to have inserted, compiled or tested generated text.

Drawing pixels are not stored in JSON. Exported nonempty Drawing layers become warned placeholders.
Use verified `$asset` identities for existing project assets; do not invent GUIDs or paths.
Old `whimtex.layers` payloads and version-1 documents are unsupported. A matching older
checkout is needed to edit them; this checkout does not provide document migration.
Plain image URL paste remains available separately; URLs are not document JSON fields.

**Generating a brush?** Read the [brush contract](Documentation~/AI/BRUSHES.md),
[brush examples](Documentation~/Examples/Brushes/README.md) and
[brush schema](Documentation~/AI/brush.schema.json). `whimtex.brush` replaces the current brush.
Standalone gradients likewise retain their separate `whimtex.gradient` value format.

For shader-only requests return HLSL for **+ Shader FX**, not ShaderLab. Follow the
[HLSL interface](Documentation~/AI/README.md#hlsl-interface--shader-only-or-inside-json).
Omit `@preset` for independent document code. Reusable linked `.hlsl` presets declare one
`// @preset("GUID", 1)` in the initial comment header, preferably after `@whimtex-effect Category/Name`.
The GUID and positive integer version are required together; links use the exact version in project,
package or user-library sources. Missing, broken or differently versioned sources use the document
code while retaining the link for a later retry. See [preset identity](Documentation~/ShaderFX.md#preset-identity-and-version).
For connected operations use [AgentAPI](Documentation~/AgentAPI.md) or the live skill, not a
command envelope inside document JSON.

Artist workflow: [English](Documentation~/en/ai-authoring.md) · [Русский](Documentation~/ru/ai-authoring.md) · [简体中文](Documentation~/zh/ai-authoring.md).
