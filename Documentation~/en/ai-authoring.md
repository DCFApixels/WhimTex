---
title: "Create layers with browser AI"
parent: "English"
nav_order: 15.1
lang: en
permalink: /en/ai-authoring/
alternate: ru/ai-authoring.md
---

# Create layers with browser AI

Ask a browser AI for an editable texture, copy its JSON and paste it into WhimTex.
No connection to Unity is needed. Available in development builds after the original 0.9.5 release.

1. Give the AI the [authoring guide](../AI/README.md) and describe your texture. Ask for **WhimTex clipboard JSON**, with useful parts on named layers.
2. Copy the returned JSON code block.
3. Focus the preview or Layers panel, leave text editing and press **Ctrl+V** (Cmd+V on macOS).
4. Adjust the new layers normally. **Ctrl+Z** undoes the whole insertion.

For example: “Create a blue magical ring on a transparent background, with an editable rim and a
separate glow. Use a 512 × 512 canvas and return WhimTex clipboard JSON.”

The result can contain shapes, gradients, noise, groups, outlines, blur, normal maps and custom
Shader FX. It cannot include imported images or painted pixels. Those still use ordinary image paste
or a [connected agent](automation.md).

The layers appear above the existing composition. A canvas selection does not crop them.
If JSON supplies a size, an empty document adopts it. For an existing composition, choose **Apply Size**
or **Keep Current**; keeping the size still inserts the layers. Custom HLSL asks for confirmation before
compilation: only paste code you trust, as a heavy shader can stall rendering.

If the JSON or shader is invalid, nothing is inserted. Send the error back to the AI and ask for
corrected JSON. Re-pasting is a new insertion, not an update to the previous result.

For a single shader, request HLSL instead and paste it into **FX → + Shader FX**, then click **Apply**.
The same [AI guide](../AI/README.md) explains the syntax and editable parameters.
