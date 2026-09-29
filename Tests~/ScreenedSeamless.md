# Screened Poisson verification

`ScreenedSeamlessSmoke.cs` is an opt-in snippet, not compiled into the package.
Use the connected Editor's Pipeline `run_script`, with this project explicitly
selected, to run entries `ScreenedSeamlessSmoke.Main`, `.Large`, `.Preview` and
`.Benchmark`. Preview requires the optional offline noise fixture in
`output/screened-poisson-lab-2026-09-28/00_original.png`; other entries are self-contained.
Run `MakeSeamlessIntegration.Main` from `MakeSeamlessIntegration.cs`
for pipeline/cache/UI/serialization and no-migration tests.

Main compares GPU results to independent double-precision unpreconditioned CG
on constant, random HDR, opaque, partial-alpha and transparent input, including
degenerate, rectangular, mixed-radix and prime dimensions, for all three Poisson directions. Large compares
separable ramps at 600x600, 257x511, 1024x512 and 4x1024 to independent 1D solves
with the same 2D screening coefficient. Both reject NaN and excessive error.

Implementation: minimize gradient mismatch plus lambda times source-value error,
lambda = 1/(.05*min(width,height))^2. The guide keeps interior input differences;
the wrap guide is the mean of the adjacent interior differences. Projection
matches three adjacent first differences across four seam samples on each selected axis.
AllEdges wraps both axes; TopAndBottom wraps Y; LeftAndRight wraps X. Unselected axes use
natural/clamped neighbor boundaries. An even extension on the unselected FFT axis gives the
exact unconstrained preconditioner without periodic coupling across that axis. The seam values
remain free during the global solve; there is no fixed-band seed or pinned seam ramp.
Axes of length 2/3 project to their mean; length 1 is unchanged. This is a local
discrete constraint, not an assertion of global C1 or visually continuous motifs.

The renderer uses FP32 linear temporary RTs, premultiplied RGBA, and GPU-only
projected FFT-preconditioned conjugate gradients (16 bounded iterations,
per-channel convergence guard). No CPU readback occurs in production. The test
reader deliberately synchronizes the GPU; Benchmark includes that readback,
temporary allocations and CPU dispatch, so it is not a pure kernel measurement.
Scratch storage is approximately 9 full-size RGBAFloat textures plus a 4x4
reduction pyramid and five 1x1 textures: about 145 bytes per pixel, excluding
the source, compositor caches and backend allocation overhead. Single-pair modes double
one dimension of two FFT textures (about 32 additional bytes per source pixel). A transform
exceeding the device texture-size limit reports an explicit error rather than silently changing
directions. Large prime FFT
factors remain slower than power-of-two sizes. Repeated unchanged renders use
the existing EffectRenderCache rather than rerunning the solver.

The all-edge defaults implement the unbounded experiment 05, NOT the slow
box-constrained experiment 07. RGB overshoot/undershoot is retained for normal
layer/output range handling. Alpha is clamped and unpremultiplied at the end,
which can change the straight-RGB seam for partial transparency. Source-value
fidelity is approximate; new layers default to OffsetBlend. Stored mode=0
remains Mirror; mode=3 selects OffsetBlend; other modes render as ScreenedPoisson without rewriting stored values. No migration or forced document changes occur.

Research formulation (independent implementation; no upstream code copied):
Bhat, Curless, Cohen, Zitnick, *Fourier Analysis of the 2D Screened Poisson
Equation for Gradient Domain Problems*, ECCV 2008:
https://grail.cs.washington.edu/projects/screenedPoissonEq/
The boundary guide/projection are our adaptation, not the paper's panorama demo.

Regression hazards covered: converged channels must stop updating their search
directions; tiny negative dot-product noise must not create enormous beta values.
Integer neighbour wrapping must not use floating division/modulo: N/N can round
below one at non-power-of-two sizes. Compare actual float input when checking
the solver against offline data; Unity texture decoding/sampling is separate
from solving and can differ from an offline sRGB decode of screenshot bytes.

Verified in the connected Editor: Main 213,230 checks (max error 1.20e-6), Large
4,078,852 checks (max error 8.94e-7), the shared integration and Node MakeSeamless/clipboard/docs checks also passed.
For the 600x600 noise fixture, comparing against the offline solver with the
actual decoded GPU input gives max error 8.73e-7, RMS 3.0e-8. Comparing encoded
PNGs to the original offline preview differs by at most one 8-bit level.
For the supplied 725x725 fixture, all three directions at radii .005/.05/.25 match the
independent float64 solver to a maximum error below 3.8e-7. The ordinary output remains
unbounded; this does not promise invisible motifs or no clipping.
Historical 12-iteration all-edge GPU render plus synchronous readback, median of three after warm-up on the test
machine: 256x256 11.74 ms, 512x512 23.28 ms, 1024x1024 104.81 ms. These are
device-specific diagnostic measurements, not guaranteed frame budgets.
