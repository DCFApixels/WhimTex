---
layout: default
title: Convert legacy layer JSON
parent: Technical reference
lang: en
permalink: /reference/legacy-layer-json/
search_exclude: true
---

# Convert legacy layer JSON before upgrading

The old `whimtex.layers` clipboard format is no longer supported. Before upgrading,
paste it in WhimTex **0.12.5**, then save the resulting document as TIFF or export
`whimtex.document` JSON. Use TIFF when the layers contain Drawing pixels.

The current [authoring guide](README.md) and [document schema](document.schema.json)
describe `whimtex.document`, version 1. Its JSON does not download images or store
Drawing pixels. A plain HTTP(S) image URL can still be pasted as a Drawing layer;
see [image paste](../en/selection.md). Brush and gradient preset formats are unchanged.
