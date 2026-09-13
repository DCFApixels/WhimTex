---
title: "Game post-processing"
parent: "English"
nav_order: 12
lang: "en"
permalink: "/en/post-fx/"
alternate: "ru/post-fx.md"
previous_page: "en/color.md"
next_page: "en/saving.md"
---

# Game post-processing

Use **Post FX** to see how your image looks with the game's color grading, bloom and other effects.
It changes only the Preview, not your saved texture or export.
This feature currently needs **URP 17.x with Universal Renderer**; other editing tools work without it.

## Choose the look

Enable **Post FX** in the footer, then open the floating settings panel with the small arrow
on the right of the canvas. Close it with the same arrow to see the image unobstructed.

| Source | Use it to… |
| :--- | :--- |
| Scene View | Preview with the active Scene View's post-effects. Make sure they are enabled there too. |
| Game Camera | Use a chosen camera's settings. Leaving the field empty uses the main camera. |
| Profile | Try a Volume Profile and adjust the preview camera settings yourself. |

The original cameras and profiles are not changed.

## Set a background

Choose **Solid Color** or **Checkerboard**.
The background fills transparent areas before post-processing, so glow and similar effects
have a visible backdrop. You can adjust its appearance here or in **User Settings…**.
In **User Settings → Post FX Preview**, **Background Mode** and **Background** share the
same settings with this panel and apply to all WhimTex windows. Checkerboard colors and
cell size are under **Transparency Checkerboard**. Your choices are remembered between sessions.

## Try depth-based effects

**Depth** controls how the image behaves as a surface:

- **Solid:** a flat surface.
- **Alpha Height:** opaque areas are raised, with gradual slopes along soft transparent edges.
- **Alpha Mask:** separate the shape from empty space using **Threshold**, without gradual slopes.

Use **Link to Zoom** to change the simulated viewing distance as you zoom.
Enable **Animate** for effects that change over time.

## If it differs from the game

Post FX previews the image, not the whole scene. Scene lighting and surrounding objects are not included.
Features such as Full Screen Pass and SSAO can work, but not every game effect is supported.
If the selected setup is unsupported, the editor shows the original Preview with a notice.

Check the camera/profile choice and the game's post-processing switch first.
To add an effect that becomes part of the saved image, use [Shader FX or Processor](shader-fx.md).
