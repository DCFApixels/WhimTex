# Make Seamless controls

`SeamlessChannelsSmoke.Main` checks all sixteen RGBA masks across all modes and all four Mirror option
combinations. Disabled channels are compared to independently rendered input, selected channels to
the full effect, including independent alpha selection and all-off bypass. Also covers thumbnail/export,
cache invalidation, group targets, Portable roundtrip, actual checkbox callbacks, Undo/Redo and old
JSON defaults. The shared channel mask is applied before later layer modifiers.

Run `SeamlessControlsSmoke.Main` through the connected Editor's `run_script` command.
It covers all 16 copy masks and three Poisson directions, widths 2/20/50%, radius extremes,
histogram compensation 0/50/100%, 1x1, 2x3, 17x13 and 64x48 images. Checks include finite output,
unchanged copy exterior without Poisson, paired seam slopes, and independent finite-difference
residuals of the global screened solve. Copy-off bypass with correction off, correction with
copying off and independent direction cache invalidation are checked by `MakeSeamlessIntegration.Main` and `.Histogram`.
Those also exercise actual edge-button/slider callbacks, cache invalidation, UI refresh, Undo/Redo,
preview/thumbnail/export paths, and document/portable serialization. Tests create temporary objects
and do not edit the user's document.

`SeamlessControlsSmoke.Preview` optionally uses the existing offline 600x600 noise fixture to
write PNG and linear float results to `output/seamless-controls-lab-2026-09-28` outside Assets.
Copy indices: 0 all edges, 1 right only, 2 top only, 3 right+top. Poisson directions for those
diagnostic indices: AllEdges, LeftAndRight, TopAndBottom, AllEdges. The fixture is not shipped with the package.

Offset Blend runs an optional global screened solve after blending, using independent Poisson
directions. No hard-banded screened path remains. Sixteen projected PCG iterations use an exact
mixed-boundary preconditioner (periodic selected axes, even extension on natural axes).
The supplied full-size fixture is compared to an independent converged solver separately;
passing discrete seam constraints alone is not a guarantee of visual quality.
