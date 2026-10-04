# Motion Blur validation

CPU-only checks, with no Unity compilation or Editor access:

```text
node Tests~/MotionBlurMath.test.mjs
node Tests~/GaussianKernelMath.mjs
```

The motion test covers exposure weights, horizontal/vertical impulses, one-sided trails,
rotation centers and nonsquare geometry, HDR/transparent RGB, edge sampling, sample budgets,
rotation recurrence, and API/UI/cache source contracts. It is not a GPU benchmark or shader compilation.
Strength checks cover original/blur endpoints, premultiplied mixing, monotonic trail density,
HDR color preservation, API limits and cache invalidation.

After the user manually compiles the project, opt-in Editor scripts can be run on the intended
project through its existing test runner. They use transient objects, not imported/saved assets:

- `MotionBlurApiSmoke.cs`: discovery, validation, partial updates, add/set routing and JSON round trips.
- `MotionBlurSmoke.cs`: GPU impulse, HDR/alpha, hidden source, zero amount, direction, circular center,
  nonsquare canvas, constant image and edge behavior.
- `EffectCacheSmoke.cs`: shared group RGBA sources, cache hits, parameter invalidation and quality
  transitions for Motion Blur, alongside Gaussian/Outline/Normal Map regression checks.

Do not trigger compilation, import, asset creation or these Editor scripts without authorization.

Manual checks:

1. Add Motion Blur above Drawing. Hide Drawing and verify that its output still reaches the effect.
2. Drag Distance and Angle, switch modes, and edit Center/Arc in Layer Settings and Properties.
   Inputs should not lose focus/capture; hidden mode values should survive switching back.
3. Try Centered/Forward/Backward. In Circular, Forward sweeps a point counterclockwise.
   Try Strength at 0%, 50%, 100% and 200%: above 100% only translucent coverage is intensified,
   without brighter colors or a longer trail. Zero Distance/Arc still bypasses the effect.
4. Choose a nonsquare canvas and an off-center pivot; rotation must remain circular in pixel space.
5. Target a Pass Through group. The effect samples isolated color; the main group keeps its mode.
6. Paint and transform the source: the preview refines after release. Undo/Redo restores settings.
7. Try Transparent/Clamp/Repeat/Mirror, including across seams in tiled preview.
8. Save/reopen a test document, duplicate the effect, convert a copy to Drawing, and export PNG/PSD.
   Never overwrite existing work for this check. These steps require explicit asset-write permission.
