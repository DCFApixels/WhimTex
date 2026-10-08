---
layout: default
title: Convert legacy layer JSON
parent: Technical reference
lang: en
permalink: /reference/legacy-layer-json/
search_exclude: true
---

# Convert legacy layer JSON before upgrading

The old `whimtex.layers` clipboard format is no longer supported. WhimTex **0.12.5**
can paste that format and save TIFF or version-1 `whimtex.document` JSON, but those
documents are also unsupported by the current checkout. Use a matching older checkout
to edit old data; there is no automatic conversion to version 2 here.

The current [authoring guide](README.md) and [document schema](document.schema.json)
describe `whimtex.document`, version 2. Its JSON does not download images or store
Drawing pixels. A plain HTTP(S) image URL can still be pasted as a Drawing layer;
see [image paste](../en/selection.md). Brush and gradient preset formats are unchanged.
