# Image / Height preview checks

Select the `height-preview` profile or individual IDs in `scripts/test-catalog.json`.
Review the case and support sources before running. Reports and images go to project
`Temp/WhimTex/height-preview-implementation`; runner results go to `Temp/WhimTex/test-runs`.

- `height-preview-render` and `height-preview-render-8k`: independent CPU bilinear
  samples verify projected surface UVs and textured output, including float HDR,
  alpha, signed heights and a five-source-pixel ridge. They also verify display-channel
  independence, target reuse and GPU state restoration. PNGs include a colored relief.
- `height-preview-inputs`: compare native rendering reduced across X with column
  rendering for fill/UV, clipping, groups, local/spatial FX and a targeted Blur layer.
  Graph-clock cases preserve the selected layer/group's time across a spatial prefix
  and local tail, while leaving child/dependency time independent of that clock.
- `height-preview-ui`: owned public-API Editor window, native controls at 200/300/400 px,
  mode switching, popup settings, no document dirtying/Undo, idle reuse and release.
- `height-preview-paint-smoke`, `height-preview-paint-8k-small`,
  `height-preview-paint-8k-large`: two reversed-order Image/Height rounds of a real
  tiled stroke following a path across most of the canvas, with soft selection,
  translucent HDR color and Half paintSurface. Every step updates the canvas and
  preview, then waits for GPU completion through a 1×1 readback. Four warm-up steps
  are excluded; median/p95 are written to CSV. This measures combined submission
  and completion, not editor frame rate, isolated GPU time or end-stroke Undo I/O.
  The Image comparison uses the normal standalone preview fallback, not a free
  snapshot from an already completed full-canvas render. Managed-allocation counts
  may be unavailable in the Editor runtime and are not memory-footprint evidence.

Height produces a column image up to 256 pixels wide at document-native height,
plus two 256² RGBAFloat outputs (2 MiB together). At 8K the column image uses 16 MiB
in Half or 32 MiB in float; live drawing also keeps its separate inspector stroke
cache. Height never evicts the canvas stroke preview. Spatial FX retain full input
resolution according to the normal dependency planner and can cost more; the brush
timings above exercise an ordinary Drawing layer without FX.

Sampling remains bounded across X by the visible columns. This is not a guarantee
that every subpixel-width detail survives in that direction. Depth is sampled twice
per source row. The heightfield has a single visible surface per output pixel;
alpha does not expose geometry behind a transparent foreground height.

## Initial measurements, 2026-11-09 (before Image cache isolation)

Unity 6000.7.0a6, RTX 5060, D3D12. Numerical 512/8192-depth, composition,
preview-reuse and owned-window cases passed. Measured 8K medians (two rounds):

| Brush diameter | Image fallback | Height |
| --- | --- | --- |
| 64 px | 4.23–4.55 ms | 2.23–2.24 ms |
| 2048 px | 11.68–11.74 ms | 4.59–4.89 ms |

These are combined brush/canvas/preview steps under the timing conditions above.
They do not predict the cost of arbitrary FX, other GPUs or an Image preview that
can reuse a previously completed full-resolution cache for free. In the 512 smoke
case the medians were about 1.22–1.24 ms (Image) and 1.26 ms (Height).

## Image cache isolation, 2026-11-09

Image fallback now selects the existing inspector drawing path. A live stroke uses
a separate 256² image instead of replacing the Canvas regional cache with an 8192²
source. FX ordering, composition and output resolution remain unchanged.

Paired 8K runs on the same device, adding a no-preview control; ranges are the two
reversed-order median measurements, with the same GPU fence and warm-up rules above:

| Brush | No preview, after | Image, before | Image, after | Height, after |
| --- | --- | --- | --- | --- |
| 64 px | 2.04–2.07 ms | 5.01–5.04 ms | 2.20–2.47 ms | 2.54–2.65 ms |
| 2048 px | 3.71–3.83 ms | 11.72–12.81 ms | 4.60–4.79 ms | 4.85–5.21 ms |

The bounded path does not promise bitwise parity with the former Half intermediate
and hardware-filtered image. Independent CPU filtering checks visible premultiplied
color/alpha within 0.003 normalized error, including HDR, masking, erase, channel
locks and transforms. Point samples exactly at texel boundaries allow either adjacent
texel within float coordinate precision. Temporary probes and raw data are under
project `Temp/WhimTex/image-preview-study`; no persistent test cases were added.
