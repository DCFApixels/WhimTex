# Clipboard JSON examples for AI authors

Reference recipes for AI assistants generating WhimTex layer JSON. These are not built-in editor presets
or a user-guide gallery. Read the [authoring contract](../../AI/README.md) before adapting them.

| Example | What to learn |
| --- | --- |
| [Neon ring](neon-ring.json) | A Shape and a targeted Blur inside a group. |
| [Shock wave](shock-wave.json) | Radial and circular Gradients shaping one-dimensional Blue Noise into an uneven energy ring. |
| [Car wheel](car-wheel.json) | A layered illustration built entirely from ellipses and a five-point star. |
| [Forked lightning](forked-lightning.json) | A procedural particle sprite with embedded HLSL, editable parameters, transparency and separate glow. |
| [Heart](heart.json) | Parameterized heart silhouette, clipping Gradient, SDF rim light, Outline and two ellipse highlights feeding a Blur. |
| [Mystic fog](mystic-fog.json) | Noise, a hidden source and a coloring gradient. |
| [Retro processor](retro-processor.json) | A standalone Processor acting on an existing lower stack. |
| [Local distortion](local-distortion.json) | An editable Transform 2D shader parameter. |

Use only the parts needed for the requested result. Omit default properties such as identity Swizzle,
Normal blending and Standard ranges unless they differ from defaults. Keep local IDs only where referenced.
The heart is the cleaned-up recipe: no Shader Processor 1, no Drawing layer, and nothing external.
That describes this one recipe, not the format: a Drawing layer is allowed, and it may fetch a PNG or
JPEG from a direct http(s) link with `url`. See the [authoring contract](../../AI/README.md).
Do not infer that every recipe needs a shader: the wheel and neon ring need none.

Files are complete clipboard envelopes; copy their JSON content, not their filename or this table.
Schema checks cover every JSON file here. The Unity smoke test also loads, compiles and renders these examples.
