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
| Warp Scale | X/Y multipliers of Noise Scale: final warp scale is `Scale × Warp Scale` per axis. Default [1,1], range 0.01–1000 per multiplier. The chain preserves proportions; unlink to edit axes separately. Random All chooses 0.25–4, preserving linked proportions. |

For finer control, **Lacunarity** changes the spacing between detail scales and **Gain**
changes how strongly the smaller details show.
With Cellular, try **Distance**, **Return** and **Jitter** to change the shape and regularity of the cells.


## Scale, 3D slices and seamless noise

**Scale X/Y** controls detail independently on each axis. The chain button changes both
proportionally; linking retains the current proportions. Scale is measured across the shorter canvas side.

### 3D slices

For OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic and Value, choose **Dimensions → 3D**
to see a slice of a volume. **Scale** and **Offset** gain a **Z** component: Offset Z selects
the slice, and Scale Z controls how far through the volume that offset moves (`Offset Z × Scale Z`).
The Scale chain links all three axes proportionally. Scale Z defaults to 1 and is retained
when switching to 1D/2D. At Offset Z = 0, changing Scale Z alone does not move the slice.
Cellular slices have a different character from 2D cells. Fractal and Domain Warp work in both 2D and 3D.

### Repeat without a seam

Use the square **Seamless** control to make the source repeat without blending edges.
Left/right edges toggle together for X; top/bottom toggle together for Y. Select all four for XY,
or clear both pairs to disable periodicity.
Click the center image to invert the selected pairs.
Fractal octaves and Domain Warp remain periodic; Z never repeats. Complete lattice cells must fit the tile,
so Scale changes in steps, especially with small OpenSimplex scales. Arbitrary transforms and FX can
introduce canvas seams again. White/Blue Noise do not offer this option.

In **1D**, **Seamless** is a checkbox: it repeats the pattern along its direction of variation,
including Fractal and Warp. One period spans the canvas projected onto that axis; Scale controls
the detail within it and still changes in steps. Direction 0 joins left/right and 90 joins top/bottom.
At arbitrary angles, repetition along the noise axis does not guarantee matching canvas edges.
The checkbox and the 2D/3D edge selection are remembered independently; it starts disabled.

With 2D OpenSimplex2/2S, Scale below 1 can now produce coarser fractal detail: each octave
fits its own period from the requested Scale. Changes remain stepped, with at least one cell
per octave. With Fractal None, the minimum-cell limit remains. Existing 2D seamless simplex
patterns may change, including at Scale above 1; other noise types and 3D are unchanged.


Warp Scale applies in 1D, 2D and 3D; it adds no separate Z multiplier. With Seamless, each selected
warp axis fits complete cells from its final scale. BasicGrid with only one cell on both axes
becomes a uniform shift: increase the Warp Scale multipliers to get distortion without
increasing Noise Scale. For example, Scale 0.5 × Warp Scale 6 requests a warp scale of 3.
White/Blue Noise ignore Warp Scale.
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

**Output** determines how noise values become pixels:

| Output | Use |
| :--- | :--- |
| Linear Data (default) | Height maps and packed texture data. |
| Color Values | Display the original noise as colors. |
| Gradient | Map monochrome values through a palette, including its alpha and HDR colors. |

**Inverted** reverses values before gradient sampling and works in every output mode.
The default palette is black-to-white **Perceptual**; switching Output keeps it.

Color White/Blue Noise supports only Color Values and Linear Data.
A stored Gradient selection temporarily uses Color Values until you return to Monochrome.
For which settings change with Random All, see [Random variations](#random-variations).

To create surface relief:

1. Add Noise and choose **Output → Linear Data**.
2. Choose **Fractal → FBm**, start with three octaves and adjust Scale.
3. Add **Normal Map** above it and choose **Generation → Height Map**.
4. In Normal Map's Advanced settings, choose **Input Space → Linear**.
5. Adjust Strength and Smoothing. Change Noise Seed to try another surface.

**Tiled** preview helps inspect seams; use **Seamless** to make the source repeat.
For the next step, see [Normal Map](normal-map.md).

## Random variations

**Random All** at the top of the noise settings explores a new combination of generator parameters,
including inactive generator options and **Inverted**. Output varies between Color Values and Linear Data, but stays Gradient if selected. The gradient palette, **Dimensions**, **Direction**, **Seamless**, linked Scale and Warp Scale ratios, **Offset X/Y/Z**, layer transforms, blending and FX
stay unchanged. One Undo restores the previous combination. **Random** beside **Seed** changes only the seed.
Noise Type stays within the selected group: **White Noise / Blue Noise**, or all other noise types.

### How Random All distributes Scale

Random All gently favors an average Scale near **8** (X/Y, or X/Y/Z in 3D), without excluding small or large patterns.
Linked axes keep their proportions. See the [technical sampling rules](../AgentAPI.md#noise-settings) for the distribution and limits.
