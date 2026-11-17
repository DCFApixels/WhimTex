# Node catalog thumbnails

Internal static catalog artwork, separate from the [operation icon pool](../Icons/README.md).
`Catalog.png` and `Catalog.json` are generated together. Both catalogs share the same
texture; the manifest maps node kind names to atlas UV rectangles. Missing entries use
operation icons. No graph preparation, compilation, materials or RenderTextures are
created by catalog browsing. The only persistent change is the user display preference;
it has a separate notification so switching display does not invalidate graph previews.

## Rebuilding

With `WHIMTEX_DEEP_DEBUG`, use **Window > WhimTex > Debug > Rebuild Node Thumbnails**.
The explicit editor operation evaluates one program at a time on the Editor thread.
Procedural examples use SVM; image examples use the native HLSL preview renderer,
including the node's actual intermediate passes. Compilation is polled across updates.
It starts from `ShaderGraphNodes.Create`, then applies the catalog-only recipes below,
with 2D coordinates, a 64x64 canvas, pixel scale one, seed 1337 and time zero.
Blue Noise tables are bound to the owned material. Warp outputs feed a Grid UV input.
Unsupported programs, invalid pixels or constant examples fail
generation; they are never replaced by approximate drawings. No document is changed.

## Noise examples

Recipes emphasize features at the actual 32px display size. They do not change factory
defaults, runtime algorithms or graph previews. All noise examples use Value; Warp uses
Position to deform a five-cell grid, with line width 0.15. The grid shows coordinate
deformation instead of treating a vector as a grayscale value.

| Kind | Catalog settings | Fixed display black / white |
| --- | --- | --- |
| Perlin | Scale 4 | 0.15 / 0.85 |
| Value | Scale 5 | 0 / 1 |
| Value Cubic | Scale 5 | 0.25 / 0.75 |
| Open Simplex 2, Open Simplex 2S | Scale 3 | 0.05 / 0.95 |
| Flow | Scale 3, Phase 1.4 | 0.1 / 0.9 |
| Cellular | Scale 4, default F1 | 0 / 0.6 |
| Gabor | Scale 4, Frequency 1, Direction (0.8, 0.6) | 0.05 / 0.95 |
| Spots | Scale 4 | 0 / 1 |
| Shard | Scale 4 | 0.1 / 0.9 |
| Triangle Voronoi | Scale 4, Evolution 0.7 | 0 / 0.35 |
| Caustics Noise | Scale 5, default Simplex | 0 / 1 |
| White, Blue | Grain Size 4 | 0 / 1 |
| All three Warp kinds | Scale 3, Strength 0.07 | 0 / 1 |

Fixed levels retain gradients without per-image auto-normalization. The smaller range
of Cellular and Triangle Voronoi is expanded from zero; applying centered contrast to
these distance outputs would erase much of their pattern. Flow uses a nonzero phase
because its phase-zero example coincides with Open Simplex 2S. Related gradient-noise
families can still resemble one another in a static image.

Images are 64x64, displayed at 32x32; two extruded edge pixels prevent atlas bleeding.
Linear output RGB is remapped by the fixed levels, clamped and encoded to sRGB; alpha is opaque. The atlas
uses no mipmaps, rescaling, compression or readable CPU copy. Recipe changes or algorithm
changes require an explicit rebuild. Browsing never checks source timestamps or bakes
missing previews. Coverage includes built-in noise kinds, Caustics Noise, Shape,
Checkerboard, Stripes, Grid, Rings, Gradient Factor, Truchet, all current Filter and UV
categories, and Sample Texture. External HLSL and Sub Graphs use icons. This is one
representative example per kind, not every mode or output.

## Filter and UV examples

`ShaderGraphCatalogThumbnailBaker.Images.cs` constructs one 256x256 linear reference:
a cyan shaded circle, a warm asymmetric wedge, a white ring, a grayscale ramp, fine
checks and a faint grid. **Sample Texture** shows it unchanged. Filters process this
same image; UV nodes feed the coordinates of another lookup of it. Height, normal,
slope and flow examples derive their auxiliary data from that same reference. They
do not substitute a hand-drawn approximation for the operation.

Catalog-only settings make the effect visible at 32px:

| Kind | Example settings |
| --- | --- |
| Blur | Radius 3 |
| Sharpen | Radius 2, Strength 2, ordinary mode |
| Dilate, Erode, Median | Radius 2 |
| Directional Blur | Radius 7, Angle 35 |
| Radial Blur | Spin, Angle 30 |
| RGB Shift | Directional, Amount 3, Angle 15 |
| Slope Blur | Reference R as slope, Strength 5 |
| Bilateral Blur | Radius 3, Tolerance 0.2 |
| Kuwahara | Classic, Radius 3 |
| Supersample | 4x4 |
| Make Tileable | Blend Width 0.45 |
| Fade Transition | Reference R, Fade Value 0.6, Fade Contrast 4 |
| Caustics | Reference R as height; Strength 0.015, Dispersion 0, 128x128 buffer, Value output |
| Transform UV | Rotation 25, Scale 0.85 |
| Apply Transform2D | Position (0.5, 0.5), Size (0.8, 0.65), Rotation 20; Local output |
| Pixelate UV | Cell Size 5 |
| Repeat, Tile UV | Two repeats per axis; Tile UV uses Staggered |
| Mirror, Kaleidoscope | Both axes; five sectors, respectively |
| Twirl, Bulge / Pinch | Strength 140 and 0.7, respectively |
| Wave, Ripple | Strength 0.08 / 0.07, Frequency 3 / 4 |
| Radial Shear | Strength 3 |
| Perspective UV | Top Left (0.23, 0.92), Top Right (0.77, 0.8) |
| Flipbook | 2x2, Frame 2 |
| Parallax Mapping, Parallax Occlusion Mapping | Reference height, Amplitude 0.3 |
| Height / Normal to Distortion, Vector Displacement, Flow Warp | Reference height derivative at Strength 5; normal displacement Strength 0.12, Flow Warp Strength 20 |

Other controls retain creation defaults. Texture lookups repeat. Image examples use
fixed display levels 0/1. Some related operations remain visually similar at small sizes;
the image indicates their action, while the name identifies the node.

The 62-entry atlas is 544x544 (about 1.13 MiB of uncompressed GPU texels). Browsing
still loads one texture and creates no render targets. The baker releases its temporary
reference, document, programs and render targets on success, failure or assembly reload,
and restores the caller's render target and sRGB-write state.
