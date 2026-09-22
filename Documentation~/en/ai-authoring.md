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
No connection to Unity is needed. Available in WhimTex 0.9.6 and later.

1. Give the AI the [authoring guide](../AI/README.md) and describe your texture. Ask for **WhimTex clipboard JSON**, with useful parts on named layers.
2. Copy the returned JSON code block.
3. Focus the preview or Layers panel, leave text editing and press **Ctrl+V** (Cmd+V on macOS).
4. Adjust the new layers normally. **Ctrl+Z** undoes the whole insertion.

For example: “Create a blue magical ring on a transparent background, with an editable rim and a
separate glow. Use a 512 × 512 canvas and return WhimTex clipboard JSON.”

The result can contain shapes, gradients, noise, groups, outlines, blur, sharpen, normal maps, custom
Shader FX, and a Drawing layer that downloads an image from a direct HTTP(S) link
(`"type": "drawing"` with `url`).
The format does not support embedded pixel data, Base64 or local file paths.
Add local images through ordinary image paste, drag and drop, or an [agent connected to Unity](automation.md).

The layers appear above the existing composition. A canvas selection does not crop them; see [Area selection](selection.md).
If JSON supplies a size, an empty document adopts it. For an existing composition, choose **Apply Size**
or **Keep Current**; keeping the size still inserts the layers. Custom HLSL asks for confirmation before
compilation: only paste code you trust, as a heavy shader can stall rendering.
Before downloading images, a confirmation lists the source websites. If any download fails,
the entire insertion is cancelled. Each image keeps its resolution and fits the canvas through
the layer transform. Undo restores the document to its previous state.

If the JSON or shader is invalid, nothing is inserted. Open **Window → General → Console**,
copy the WhimTex error and ask the AI to fix it. This also applies to [brush JSON](painting.md#customize-the-brush).
Re-pasting creates new layers rather than updating the previous result.

For a single shader, request HLSL instead and paste it into **FX → + Shader FX**, then click **Apply**.
The same [AI guide](../AI/README.md) explains the syntax and editable parameters.
