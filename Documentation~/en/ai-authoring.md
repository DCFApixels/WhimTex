---
title: "Create layers with browser AI"
parent: "English"
nav_order: 15.1
lang: en
permalink: /en/ai-authoring/
translations: "en/ai-authoring.md,ru/ai-authoring.md,zh/ai-authoring.md"
---

# Create layers with browser AI

Ask a browser AI for an editable texture, copy its JSON and paste it into WhimTex.
No connection to Unity is needed. These instructions describe the current unified JSON format.

1. Give the AI the [authoring guide](../AI/README.md) and describe your texture. Ask for **WhimTex clipboard JSON**, with useful parts on named layers.
2. Copy the returned JSON code block.
3. Focus Canvas View or the Layers panel, leave text editing and press **Ctrl+V** (Cmd+V on macOS).
4. Adjust the new layers normally. **Ctrl+Z** undoes the whole insertion.

For example: “Create a blue magical ring on a transparent background, with an editable rim and a
separate glow. Use a 512 × 512 canvas and return WhimTex clipboard JSON.”

The result can contain shapes, gradients, noise, groups, targeted effects and custom Shader FX.
Ask for **whimtex.document** JSON. Existing project images can be referenced when their real asset
identity is known. Drawing pixels are not stored in JSON; add images through ordinary paste,
drag and drop, or an [agent connected to Unity](automation.md).

Old linked-image JSON remains accepted by the [compatibility reader](../AI/LEGACY_LAYERS.md);
its URL workflow is not part of newly generated document JSON.

The layers appear above the existing composition. A canvas selection does not crop them; see [Area selection](selection.md).
If JSON supplies a size, an empty document adopts it. For an existing composition, choose **Apply Size**
or **Keep Current**; keeping the size still inserts the layers. Custom HLSL asks for confirmation before
compilation: only paste code you trust, as a heavy shader can stall rendering.
Invalid JSON structure or layer references prevent insertion. A layer FX compilation failure instead
offers **Paste with warnings**: continue to retain its code and values while skipping that FX, or cancel
without inserting anything. Warning icons identify the effect; repair its code and click **Apply**.
Copy diagnostics from **Window → General → Console** when asking the AI for a fix.
[Brush JSON](painting.md#customize-the-brush) is separate and still requires a valid brush shader.
Re-pasting creates new layers rather than updating the previous result.

For a single shader, request HLSL instead and paste it into **FX → + Shader FX**, then click **Apply**.
The same [AI guide](../AI/README.md) explains the syntax and editable parameters.
