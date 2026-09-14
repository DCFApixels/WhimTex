# Gaussian Blur validation

Compile the package manually in Unity first. Do not launch a build, force refresh or request project
compilation to run these tests. Execute the complete C# snippets with the connected Editor's existing
C# execution facility, explicitly passing the intended Unity project's absolute path through
`--project-path`. They create transient objects only.

- `GaussianBlurApiSmoke.cs`: defaults, partial updates, snapshot round trip and validation failures.
- `GaussianBlurSmoke.cs`: GPU vs CPU impulse kernel, transparent RGB rejection, HDR intensity,
  zero radius, Strength bypass/mixing/density, hidden input, odd canvas dimensions and all edge modes.
- `EffectCacheSmoke.cs`: warm/cold parity, alpha-to-RGBA promotion, Outline stability, Normal Map
  sharing, parameter/texture changes, fast-to-exact refinement, cycles, diagnostic masks,
  memory limits and removal of unused entries.
- Re-run existing `HiddenEffectInputSmoke.cs`, `NormalMapSmoke.cs`, `ClippingMaskSmoke.cs` and
  `HdrStageSmoke.cs` / `HdrGroupSmoke.cs` to catch changes in shared compositor behavior.

Outside Unity, `node Tests~/GaussianKernelMath.mjs` checks paired sampling against a direct CPU
convolution for every boundary mode, including tiny/odd dimensions and large/subpixel radii.
This verifies the mathematical construction, not shader compilation or GPU execution.
`node --test Tests~/GaussianStrength.test.mjs` checks Strength math and parity with the Motion Blur shader,
UI/API bindings, defaults, bypass and texture cleanup without Unity.

Manual checks:

1. Add Gaussian Blur above a Drawing layer; paint and drag Radius continuously. The preview should
   update during editing and refine after release, without rebuilding the settings fields.
2. Repeat with a hidden Pass Through group containing blended children. Changing the external
   background must not change the effect's isolated source; it can change the visible group's result.
3. Use Repeat edges with Tiled viewing and paint across a seam. Check alpha fringes and HDR colors.
4. Undo/Redo a source stroke and a Radius change. The image and Debug mask must not remain stale.
5. Export while a reduced-quality preview is present, then compare with a settled full-quality render
   at the same output resolution. The preview cache must not affect saved pixels.
6. Close/reopen the editor and switch documents. No cache textures should survive the owning window.
7. Try Strength at 0%, 50%, 100%, 200% and 400% on a translucent highlight. Radius should stay unchanged;
   above 100% the highlight becomes denser without brightening its RGB. Undo/Redo restores intensity.

The tests are opt-in and do not prove performance on a given GPU until executed there. Raw cache
byte accounting excludes temporary textures, driver allocations and the existing Undo history.
