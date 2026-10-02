# Clipboard JSON examples for AI authors

The nine procedural recipes below use **`whimtex.document`, version 1**, the same format as
document files, Copy as JSON and agent serialization. Read the [authoring guide](../../AI/README.md),
[shared contract](../../JSON_FORMAT.md) and [document schema](../../AI/document.schema.json).

| Example | What to learn |
| --- | --- |
| [Neon ring](neon-ring.json) | Shape and targeted Blur inside a group. |
| [Shock wave](shock-wave.json) | Radial/circular gradients, one-dimensional Blue Noise and inline FX. |
| [Car wheel](car-wheel.json) | Editable ellipses and a five-point star. |
| [Forked lightning](forked-lightning.json) | Procedural sprite with HLSL parameters, transparency and separate glow. |
| [Heart](heart.json) | Clipping, SDF rim light, Outline and blurred highlights. |
| [Mystic fog](mystic-fog.json) | Noise, a hidden source and a coloring gradient. |
| [Seamless noise](seamless-noise.json) | Offset Blend, negative Transition Start and independent Poisson edge selection. |
| [Retro processor](retro-processor.json) | Processor acting on the lower stack. |
| [Local distortion](local-distortion.json) | An editable Transform 2D shader parameter. |

These are complete FullOptimized documents: active defaults are deliberately explicit.
Keep disabled layers, IDs and dependencies. Full retains inactive values; Compact also omits
version defaults. Adapt the native model fields, not live API `settings` wrappers.
Copy the JSON content, not the filename. The same content can be opened as a document or pasted
as layers. These are reference examples, not built-in presets.

Schema checks validate all current recipes. Unity tests compile and compare their 64-pixel
renders with the original compatibility fixtures; no network or user document is required.
More examples with individual previews: [38 texture samples](../../../Samples~/AgentTextures/README.md).

## Legacy linked-image fixture

[Stone wall: linked Drawing + Processor](stone-wall-retro.json) is intentionally **not** a current
document example. It tests the input-only `whimtex.layers` compatibility reader.
See the [legacy reference](../../AI/LEGACY_LAYERS.md); do not copy its envelope or field structure
into new recipes. Unified JSON omits Drawing pixels and does not download a Drawing URL.

![Legacy source image](stone-wall.png)

This old fixture downloads a direct PNG URL after host confirmation. The image must be available
online; a failed download cancels insertion. It retains source resolution and uses a transform to
fit its 1024 × 1024 canvas. Only the Processor is procedural.
