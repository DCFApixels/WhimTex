# Node catalog thumbnails

Internal static catalog artwork, separate from the [operation icon pool](../Icons/README.md).
`Catalog.png` and `Catalog.json` are generated together. Both catalogs share the same
texture; the manifest maps node kind names to atlas UV rectangles. Missing entries use
operation icons. No graph preparation, compilation, materials or RenderTextures are
created by catalog browsing. The only persistent change is the user display preference;
it has a separate notification so switching display does not invalidate graph previews.

## Rebuilding

With `WHIMTEX_DEEP_DEBUG`, use **Window > WhimTex > Debug > Rebuild Node Thumbnails**.
The explicit editor operation evaluates one SVM program at a time on the Editor thread.
It uses `ShaderGraphNodes.Create` defaults, 2D coordinates, a 64x64 canvas, pixel scale
one and time zero. Blue Noise tables are bound to the owned material. Warp outputs feed
a Checkerboard UV input. Unsupported programs, invalid pixels or constant examples fail
generation; they are never replaced by approximate drawings. No document is changed.

Images are 64x64, displayed at 32x32; two extruded edge pixels prevent atlas bleeding.
Linear output RGB is clamped for display and encoded to sRGB; alpha is opaque. The atlas
uses no mipmaps, rescaling, compression or readable CPU copy. Recipe changes or algorithm
changes require an explicit rebuild. Browsing never checks source timestamps or bakes
missing previews. The first set covers built-in noise kinds, Caustics Noise, Shape,
Checkerboard, Stripes, Grid, Rings, Gradient Factor and Truchet; external HLSL and Sub Graphs
use icons. This is a representative default example per kind, not every mode or output.
