---
layout: default
search_exclude: true
title: Motion Blur internals
parent: Technical reference
nav_order: 5
lang: en
permalink: /reference/motionblur/
---

# Motion Blur

Linear and Circular are modes of the unified Blur layer, using the same source selection, hidden-source handling,
group isolation and bounded GPU cache as [Gaussian Blur](GaussianBlur.md). It does not change
source pixels or convert groups to isolated mode in the main composition.

## Controls

- **Mode:** choose Linear or Circular in Blur Properties (a new Blur defaults to Gaussian). Settings for other modes are retained, not reset.
- **Strength:** 0–400%, default 100%. Below 100%, mixes the original with the blurred result
  in premultiplied RGBA. Above 100%, makes translucent trails denser without changing their
  length or straight RGB/HDR brightness. Fully opaque pixels are unchanged above 100%.
- **Linear:** Distance is the total exposure length in original canvas pixels, 0–512, default 16.
  Angle is −180–180 degrees, default 0 (right); positive angles turn counterclockwise.
- **Circular:** Arc is the total swept angle, 0–360 degrees, default 15. Center is normalized
  canvas position, `[0,0]` bottom-left to `[1,1]` top-right, default `[0.5,0.5]`.
  Rotation is computed in pixel space, so nonsquare canvases do not turn circles into ellipses.
  This is rotational blur, not zoom blur. Center is independent of the layer's output Transform pivot.
- **Direction:** Centered distributes exposure equally to both sides; Forward trails along Angle
  or counterclockwise around Center; Backward reverses the trail. Symmetric rotation has no
  clockwise/counterclockwise distinction, which is why the directional modes are separate.
- **Edges:** Transparent, Clamp, Repeat, Mirror; default Transparent. These apply at the source
  canvas boundary, independently of preview tiling and output Transform tiling.

Zero Strength, zero Distance in Linear mode, or zero Arc in Circular mode bypasses filtering while still
applying the effect's output Transform and modifiers. Both Layer Settings and separate Properties
windows use the same stable controls and existing parameter Undo path.

## Filtering and quality

Source RGBA is premultiplied before sampling, integrated with uniform exposure weights and
unpremultiplied afterwards. Transparent RGB does not bleed into visible edges. Intermediate
buffers use linear ARGBFloat; the effect's existing Color Range controls output saturation.
Set Color Range to HDR to preserve values above 1. Input data and canvas size are never rewritten.

For Strength `s > 1`, output alpha is `a * s / (1 + a * (s - 1))`, where `a` is blurred alpha.
This smoothly increases coverage, preserves zero/one alpha and avoids subtracting the original
image or producing negative alpha. RGB stays unchanged. Strength does not extend Distance/Arc
or add more blur passes. Mixing and density adjustment share the final conversion pass; the
original source is sampled at full preview resolution for Strength below 100%.

Full quality integrates along the line or rotation arc at approximately one working pixel per
interval, with trapezoidal endpoint weights and at most 1024 samples per output pixel. Circular
sample counts depend on each pixel's distance from Center. The sampling cap limits cost but very
large arcs can show separated traces on small high-contrast features. Full quality remains
synchronous GPU work; a cold render on a very large canvas can be expensive.

While painting, transforming or changing settings in the main window, sampling is capped at 32.
Long paths additionally use filtered reductions up to 4x per axis where dimensions permit.
The angular/pixel extent and center stay unchanged. Reduced previews can soften detail and show
coarse trails; the existing settling timer requests full-quality refinement after interaction.
Properties previews, API renders, saves, exports and conversion to Drawing use full quality.
PSD stores the rasterized result, preserving the ordinary layer structure.

## Cache and memory

Motion Blur participates in the shared 256 MiB effect-cache budget, rather than allocating a
separate retained cache. As an RGBA consumer it promotes group sources when required; Outline
and SDF can continue reading alpha from that same entry. A small source-requirement property
is shared by Normal Map, Gaussian Blur and Motion Blur so caching and source rendering agree.

Output settings, targets, source changes, dimensions and interactive quality invalidate the
existing fingerprints. Deterministic Shader FX inputs use the same cache; arbitrary Material
modifiers and FX with unsupported time inputs still bypass caching.
Diagnostic masks follow existing cache behavior. Undo and document changes use existing invalidation.

Working buffers are temporary GPU textures, released as soon as each stage no longer needs them
and also on exceptions. The blur retains at most two working buffers at once, excluding its input,
the returned output, transforms, source rendering and cache entries. Two full-size ARGBFloat
buffers alone require about 763 MiB at 5000 × 5000; the cache budget does not cap transient GPU
memory. No CPU pixel readback, serialized derived textures or new pixel Undo storage is introduced.

## Automation and validation

- [Agent parameters](AgentAPI.md#motion-blur-settings)
- [Tests and manual checks](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/MotionBlur.md)
