# Normal Map validation

Compile manually in Unity first. Do not trigger compilation, import, refresh or a project build
from these checks. Run editor scripts only with permission for transient test objects.

- `NormalMapSmoke.cs`: flat surfaces, RGB height ramps, all derivative filters, normalized vectors,
  strength, flips, inverted height, preview scale, packed encoding, group color, transparent RGB,
  alpha-as-height, output alpha and serialization. Transient textures/document only, no saves or Undo.
- `NormalMapApiSmoke.cs`: strict settings validation, enum handling, complete snapshot round-trip
  and discovery. No assets or rendering.
- Re-run existing `ClippingMaskSmoke.cs`, `SwizzleSmoke.cs` and `HdrGroupSmoke.cs` to confirm that
  legacy group-alpha sources are unchanged.

## Manual checks

1. Add Normal Map above a height texture; test Previous and Specific, including Target drag/drop.
   Hidden targets must still supply pixels; missing/self/cyclic targets remain invalid. Try a nested group with colored
   children: their RGB, not a uniform alpha union, must drive height.
2. Switch Height Map/Texture and drag sliders/type numbers without losing focus. Check Undo/Redo,
   separate Properties windows, duplicate, conversion to Drawing and saved-document reopen.
3. In Texture mode vary detail weights/radii and Light Removal. Use Output Height to inspect the
   reconstructed height; turn it back to Normal. Color edges and shadows can become relief.
4. Test Clamp/Repeat/Mirror with a tiled source, near all four borders, and a soft-alpha sprite.
   Repeat samples across the canvas seam; it does not make a nonseamless input seamless by itself.
5. Export a flat normal as PNG/TGA with Packed Color: expected RGB bytes approximately 128/128/255.
   Use Linear Data for EXR: expected raw RGB 0.5/0.5/1. Do not overwrite existing files.
   Import packed PNG/TGA as Normal Map with grayscale conversion disabled only when authorized.
6. Check preview/full-resolution output for comparable slope strength. Low-resolution preview
   necessarily loses fine features. Confirm no shader warnings/errors and no readback in generator.
7. Normal color blending, FX, swizzle and spatial transforms are available but do not automatically
   renormalize/reorient normal vectors. Check rasterized PSD export retains the generated image.

Run both Gamma and Linear project variants only in explicitly authorized test projects; do not
change the working project's color-space settings for this check.
