---
layout: default
search_exclude: true
title: Gaussian Blur and caching
parent: Technical reference
nav_order: 4
lang: en
permalink: /reference/gaussianblur/
---

# Gaussian Blur and effect caching

Gaussian is the default mode of the nondestructive Blur layer. Its source is the next sibling below it
(Previous) or a specific layer/group. The source's visibility does not prevent sampling; hidden
children inside a group remain excluded. Cycles are rejected as with other effect layers.

## Parameters and rendering

- **Strength:** 0–4, default 1 (UI 0–400%). Zero bypasses filtering; below 1 mixes source and blur
  in premultiplied linear RGBA. Above 1 increases translucent coverage using `a*s/(1+a*(s-1))`,
  without changing straight RGB, radius or fully opaque coverage. The existing final pass handles this;
  no additional render target or blur pass is allocated. A zero Radius remains an identity at any Strength.
- **Radius:** 0–256 original canvas pixels, default 8. This is the finite kernel extent,
  with sigma equal to radius / 3 (a minimum sigma of 1/3 pixel for nonzero subpixel radii).
  Zero bypasses filtering. Preview scaling adjusts the radius, not the document setting.
- **Edges:** Transparent, Clamp, Repeat or Mirror; default Transparent. These address the source
  canvas boundary independently of tiled viewing and the effect's output Transform.
- **Color:** straight RGBA is premultiplied before filtering and unpremultiplied afterwards.
  Transparent RGB cannot contaminate visible edges. Filtering is linear and floating point;
  the layer's Standard/HDR range still controls final saturation.

Full quality uses horizontal and vertical normalized Gaussian convolutions. Adjacent weights are
paired through bilinear sampling, with explicit edge interpolation for seamless Repeat/Mirror.
The original data and source dimensions do not change.

The main window uses interactive quality during painting, on-canvas transforms and a short settling
period after parameter changes. Kernels above 24 preview pixels reduce the working image in powers
of two, up to 16x per axis where dimensions permit. Filtered reduction and a variance adjustment
keep the approximation close to the final blur. Quality refinement follows the end of interaction.
It is still synchronous GPU work, not a promise of a stall-free final render at every canvas size.
Preview resolution follows existing Live Quality rules. Full-quality preview is not necessarily
full canvas resolution; saves, exports and rasterization use the original output dimensions.

## Group sources

An effect sees the group's own isolated composition on transparency, including internal blends,
opacity, swizzle and clipping. The group's role in the main composition is unchanged: Pass Through
children can still interact with the external backdrop there. The effect does not sample that backdrop.
Group FX are included in this source and force isolation in the main composition too. Group transforms remain unsupported.

Outline and Alpha-source SDF request grayscale coverage. SDF with a non-alpha channel,
Normal Map, Gaussian Blur and Motion Blur request RGBA. Both source representations use the
same composite alpha, so adding a color consumer does not change an existing Outline/SDF result.

## Cache ownership and memory

The main window owns a 256 MiB LRU budget shared by effect outputs, group sources and their diagnostic
masks. Entries are created on demand. Groups with only coverage consumers retain an RFloat source
(ARGBHalf fallback); a reachable color consumer promotes the shared source to RGBAHalf. Coverage
consumers extract alpha from that RGBA entry. Intermediate color buffers can still be required to
compute coverage correctly, especially for color-to-alpha swizzle; the alpha cache saves retained
memory, not necessarily all temporary rendering memory.

After the last color consumer disappears, RGBA may remain until replacement/eviction. Entries with
no effect consumers are removed on the next preview. Oversized entries are rendered without storage.
The budget covers retained cache textures, not the canvas, preview, temporary blur buffers, CPU
readbacks, driver overhead or Unity Undo storage. No CPU image copies are retained by this cache.
Returning a cached source uses a temporary GPU copy to preserve existing renderer ownership rules.

Dependency fingerprints include serialized settings, group descendants, targeted/clipping inputs,
texture update/dirty counters and live painting frames. Fingerprints are memoized within a render.
Resolution, scale and interactive quality must match. Undo invalidates the window cache; changing
documents, closing the window or reloading scripts releases it. Source-less cycles cannot hit a
cached image. Deterministic Shader FX and Shader Processor results participate in the same cache.
Arbitrary Material modifiers, or FX that use unsupported time inputs, bypass caching because their
state is not represented safely by the layer model.

Numeric-error masks are captured locally with each entry and accumulated again on cache hits.
Neither the cache nor its masks are serialized, saved into compositor assets, or registered with
Undo. Gaussian changes record settings through the existing Undo path. Drawing pixel history is
unchanged; no changed-tile Undo storage is introduced.

Ordinary composite renders outside the main window share a short-lived cache for that render only.
Direct per-layer Properties/raster/PSD renders use full quality without the persistent window cache.
The cache is an optimization: cold, warm and evicted results must match at the same quality.

## Validation

After manually compiling in Unity, run the opt-in checks described in
[Gaussian Blur tests](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/GaussianBlur.md). No build or automatic project compilation is needed.
