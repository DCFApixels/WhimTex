---
title: "Procedural Noise"
parent: "Layers and groups"
grand_parent: "English"
nav_order: 1
lang: "en"
description: "Generate procedural noise textures inside Unity with WhimTex. Adjust Perlin, OpenSimplex2, Cellular, fractals and domain warp for clouds, VFX and masks."
permalink: "/en/noise/"
translations: "en/noise.md,ru/noise.md,zh/noise.md"
---

# Procedural Noise

Use Noise for clouds, grain, stone-like patterns or a starting point for a height map.
Add **Noise** from the layer menu and adjust the settings while watching the image.

## Start with the pattern

| Setting | What to try |
| :--- | :--- |
| Noise Type | OpenSimplex2 or Perlin for smooth variation; Cellular for cells; Value for a simpler random pattern. |
| Seed | Change the pattern without changing its character. |
| Scale | Increase for finer detail, decrease for larger shapes. |
| Offset | Move the pattern. |
| Fractal | FBm adds detail; Ridged emphasizes ridges; PingPong creates repeated bands. |
| Octaves | Add more levels of detail. |
| Domain Warp | Bend and distort the pattern; Strength controls how far. |

For finer control, **Lacunarity** changes the spacing between detail scales and **Gain**
changes how strongly the smaller details show.
With Cellular, try **Distance**, **Return** and **Jitter** to change the shape and regularity of the cells.

## White noise

Choose **Noise Type → White Noise** for random grain without smooth transitions.
**Color → Monochrome** produces grayscale grain; **Color → Color** gives independent red, green and blue values.
**Grain Size (px)** starts at one canvas pixel; increase it for larger square grains.
Use **Seed** to get a different pattern and **Offset** to move it in pixels.
Fractal and Domain Warp do not apply to White Noise; their settings are retained when switching types.
White Noise also supports **Dimensions → 1D** for random bands with adjustable direction.

## Blue noise

Choose **Noise Type → Blue Noise** for more evenly distributed grain with fewer random clumps.
It is useful for dither masks and fine speckles. **Monochrome / Color**, **Grain Size (px)**,
**Offset**, **Inverted** and **Output** work as they do for White Noise; **1D** creates bands.
Choose **Linear Data** for a dither mask, or **Color Values** for a visible texture.

Blue Noise repeats every **128 grains** horizontally and vertically, or every **256 grains** in 1D.
Large grains can make this repetition visible. Seed rearranges the pattern while preserving its distribution.
Fractal and Domain Warp do not apply to Blue Noise.

## Striped noise
Choose **Dimensions → 1D** to create straight noise stripes instead of a two-dimensional pattern.
**Direction (deg)** rotates the direction of variation: 0 gives vertical stripes, 90 gives horizontal stripes.
Scale controls their width. Offset X moves through the pattern; Offset Y chooses a different slice of noise.
For other noise types, Seed, Noise Type, Fractal and Domain Warp remain available; Warp changes the variation but keeps the stripes straight.
Switch back to **2D** for the usual pattern without losing the direction setting.

## Color texture or height map?

Choose **Output → Color Values** when using the noise as a visible image.
Choose **Linear Data** when using it as a height map or packing it into texture channels.

To create surface relief:

1. Add Noise and choose **Linear Data**.
2. Choose **Fractal → FBm**, start with three octaves and adjust Scale.
3. Add **Normal Map** above it and choose **Generation → Height Map**.
4. In Normal Map's Advanced settings, choose **Input Space → Linear**.
5. Adjust Strength and Smoothing. Change Noise Seed to try another surface.

**Tiled** preview helps you inspect seams, but does not make Noise itself seamless.
For the next step, see [Normal Map](normal-map.md).
