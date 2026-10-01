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
Add **Noise** through **+ → Noise** at the bottom of Layers and adjust the settings while watching the image.

**Random All** at the top of the noise settings explores a new combination of generator parameters,
including inactive generator options and **Inverted**. Output varies between Color Values and Linear Data, but stays Gradient if selected. The gradient palette, **Dimensions**, **Seamless**, linked Scale ratio, Offset Z, layer transforms, blending and FX
stay unchanged. One Undo restores the previous combination. **Random** beside **Seed** changes only the seed.
Noise Type stays within the selected group: **White Noise / Blue Noise**, or all other noise types.

## Start with the pattern

| Setting | What to try |
| :--- | :--- |
| Noise Type | OpenSimplex2 or Perlin for smooth variation; OpenSimplex2S for a smoother OpenSimplex2; Cellular for cells; Value for a simpler random pattern; ValueCubic for smoothed Value. |
| Seed | Change the pattern without changing its character. |
| Scale | Increase for finer detail, decrease for larger shapes. |
| Offset | Move the pattern. |
| Fractal | FBm adds detail; Ridged emphasizes ridges; PingPong creates repeated bands; None disables fractals. |
| Octaves | Add more levels of detail. |
| Domain Warp | Bend and distort the pattern; **Warp Strength** controls the amount. None disables distortion. |

For finer control, **Lacunarity** changes the spacing between detail scales and **Gain**
changes how strongly the smaller details show.
With Cellular, try **Distance**, **Return** and **Jitter** to change the shape and regularity of the cells.

## Scale, 3D slices and seamless noise

**Scale X/Y** controls detail independently on each axis. The chain button changes both
proportionally; linking retains the current proportions. Scale is measured across the shorter canvas side.

For OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic and Value, choose **Dimensions → 3D**
to see a slice of a volume. **Offset** gains a **Z** component: move it to explore adjacent slices.
Cellular slices have a different character from 2D cells. Fractal and Domain Warp work in both 2D and 3D.

Use the square **Seamless** control to make the source repeat without blending edges.
Left/right edges toggle together for X; top/bottom toggle together for Y. Select all four for XY,
or clear both pairs to disable periodicity. Hovering either edge highlights its pair.
Click the center image to invert the selected pairs.
Fractal octaves and Domain Warp remain periodic; Z never repeats. Complete lattice cells must fit the tile,
so Scale changes in steps, especially with small OpenSimplex scales. Arbitrary transforms and FX can
introduce canvas seams again. White/Blue Noise and 1D do not offer this option.

## White noise

Choose **Noise Type → White Noise** for random grain without smooth transitions.
**Color → Monochrome** produces grayscale grain; **Color → Color** gives independent red, green and blue values.
**Grain Size (px)** starts at one canvas pixel; increase it for larger square grains.
Use **Seed** to get a different pattern and **Offset** to move it in pixels.
Fractal and Domain Warp do not apply to White Noise; their settings are retained when switching types.
White Noise also supports **Dimensions → 1D** for random bands; see [Striped noise](#striped-noise).

## Blue noise

Choose **Noise Type → Blue Noise** for more evenly distributed grain with fewer random clumps.
It is useful for dither masks and fine speckles. **Monochrome / Color**, **Grain Size (px)**,
**Offset**, **Inverted** and **Output** work as they do for White Noise; **Dimensions → 1D** creates bands.
Choose **Output → Linear Data** for a dither mask, or **Output → Color Values** for a visible texture.

In canvas pixels, Blue Noise repeats every 128 × grain size on each axis, or 256 × grain size along the direction of variation in 1D.
Large grains can make this repetition visible. Seed rearranges the pattern while preserving its distribution.
Fractal and Domain Warp do not apply to Blue Noise.

## Striped noise

Choose **Dimensions → 1D** to create straight noise stripes instead of a two-dimensional pattern.
**Direction (deg)** rotates the direction of variation: 0 gives vertical stripes, 90 gives horizontal stripes.
Direction is available for every noise type. **Scale** controls stripe width;
White Noise and Blue Noise use **Grain Size (px)** instead.
**Offset X** moves along the direction of variation; **Offset Y** selects another noise slice (for Blue Noise, another seeded sequence).
Fractal and Domain Warp remain available except for White Noise and Blue Noise.
Warp changes the pattern but keeps the stripes straight.
Switch back to **2D** for the usual pattern without losing the direction setting.

## Color texture or height map?

**Output → Gradient** maps monochrome noise through the selected palette, including its alpha and HDR colors. The default output is **Linear Data**; the default gradient is black-to-white **Perceptual**. **Inverted** is available in every output mode and reverses noise values before gradient sampling. Switching Output keeps the palette. Color White/Blue Noise offers only Color Values and Linear Data; a previously selected Gradient temporarily uses Color Values until you return to Monochrome. Random All keeps Gradient output and the palette; otherwise it randomizes Output between Color Values and Linear Data. Inverted can change.

Choose **Output → Color Values** to display the original noise as colors, with **Inverted** available.
Choose **Output → Linear Data** when using it as a height map or packing it into texture channels.

To create surface relief:

1. Add Noise and choose **Output → Linear Data**.
2. Choose **Fractal → FBm**, start with three octaves and adjust Scale.
3. Add **Normal Map** above it and choose **Generation → Height Map**.
4. In Normal Map's Advanced settings, choose **Input Space → Linear**.
5. Adjust Strength and Smoothing. Change Noise Seed to try another surface.

**Tiled** preview helps inspect seams; use **Seamless** to make the source repeat.
For the next step, see [Normal Map](normal-map.md).
