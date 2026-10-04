# Patch Quilting verification

Run through the connected Unity Editor, targeting the intended project explicitly:

```sh
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingSmoke.cs" --entry PatchQuiltingSmoke.Main --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingSmoke.cs" --entry PatchQuiltingSmoke.Integration --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingSmoke.cs" --entry PatchQuiltingSmoke.Cache --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingSmoke.cs" --entry PatchQuiltingSmoke.Workspace --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingSmoke.cs" --entry PatchQuiltingSmoke.Candidates --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingFeatherSmoke.cs" --entry PatchQuiltingFeatherSmoke.Main --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingFeatherSmoke.cs" --entry PatchQuiltingFeatherSmoke.Rendering --project-path "D:/DCFA/Projects/Test6.6" --format json
unity command run_script --file "Packages/com.dcfapixels.whimtex/Tests~/PatchQuiltingEquivalence.cs" --entry DCFApixels.WhimTex.QuiltingEquivalence.Main --project-path "D:/DCFA/Projects/Test6.6" --format json
```

Inspect inner `result.success`, not just transport success. The tests use temporary objects/windows and restore focus, Undo group, render state and temporary resources. They do not edit user documents.

## Checks

- Shader compilation; exact open-cut DP and fixed-anchor closed DP against exhaustive tiny cases.
- Determinism, seed variation, finite results, convex input range, protected center, disabled independent channels, HDR constants and hidden transparent RGB.
- Sizes 1×1, 2×7, 17×13, 64×48; all edge pairs; Linked/Independent; Feather 0/25/50/100%.
- Affine fixtures verify that opposite border samples follow donor adjacency, including corners after the second axis. Adjacent samples need not be identical.
- Composite, target preview, thumbnail, full-resolution export render, group target and clipping; generic cache reuse and invalidation by source/settings.
- All 16 RGBA masks, portable clipboard roundtrip, retained controls, paired selectors, independent Poisson direction, Random, capture/release deferred commit and Undo/Redo.
- Core float comparisons use tight tolerances; integration comparisons allow 0.005 for existing intermediate render precision. They do not assert that arbitrary reduced-resolution previews select the full-resolution path.
- NativeArray/Burst search vs the frozen managed search reference: 324 combinations of size, quality, direction, matching, seeds and fixtures (including nearly tied candidates), exact GPU pixel equality on the tested editor. Both paths use the current percentage blend shader; this is not a comparison with the retired pixel-Feather appearance. The reference with managed arrays lives only in Tests~, never in the product assembly.
- Pre-Poisson FP32 entry reuse for correction edits; invalidation for every quilting control and source edits, including a Pending layer between the effect and source. Mode-switch eviction, budget enforcement and disposal. An uninitialized thumbnail cache safely bypasses the stage cache.
- Scratch reuse (native allocations, readback and both rectangular path textures), cold/warm pixel identity, channel-mask clearing, isolated nested rents, idle/budget eviction and complete native/texture disposal.
- Unique donor task pairs versus original candidate generation for dimensions 3/17/64/256, all quality budgets, narrow/wide bands, integer seed extremes, transposition, matching and channel masks. Every original candidate retains an alias and multiplicity; only redundant computation is removed.
- Controlled GPU blend weights against the scalar percentage profile, independent cut widths, both axes, narrow-band fallback, one/two/three-pixel output bands and wide bands, percentages 0/25/50/100. API bounds/export/default and unchanged old numeric values without migration.
- Full 512² affine render at 2%/45% Patch Width, all qualities, directions and matching modes: changing Feather 0→50→100 changes the rendered output, retains the protected center and opposite-edge donor adjacency. Integration checks cover percentage UI bounds/label, deferred commit, cache invalidation, thumbnails and export.

Run `OffsetOptionsSmoke.Main`, `MakeSeamlessIntegration.Histogram`, and Node `MakeSeamless.test.mjs`, `ProceduralClipboard.test.mjs`, `AgentDocumentation.test.mjs` for adjacent regression coverage. Check the clipboard schema generator and documentation source checker.

## Algorithm boundaries

Boundary-strip quilting is a local adaptation: donors translate along the repaired axis, without rotations. Optional Along-Seam Search (0–25%, default 0) adds a tapered along-strip displacement, not a rigid cyclic offset. Half the existing candidate budget remains straight and half uses signed shifted strips. Search uses squared selected-component differences plus longitudinal derivative error. A seeded choice is made among candidates within 10% of the best score.

Along-strip mapping is `row + shift*(rows-5)*16*t²*(1-t)²`, where `t=clamp((row-2)/(rows-5),0,1)`; rows <=5 bypass displacement. The first/last three analysis sample positions remain unchanged. Within the allowed range the mapping stays monotone and in bounds. GPU mapping uses the same normalized guard; matching gradients and compensation covariance sample the shifted donor. This may stretch details and is not guaranteed to improve a texture.

`QuiltingAlongSearchSmoke.Main` checks mapping, API bounds, determinism, finite HDR output, protected centers, active displacement, both channel modes, compensation, all directions, tiny/rectangular sizes and first-axis donor adjacency after the second pass. `PatchQuiltingSmoke.IntegrationShifted` covers UI, clipboard, composite/preview/thumbnail/export and correction integration; `Cache` checks invalidation. On the tested Editor: 801421 core checks, 294954 integration checks, 147487 cache checks passed; 252 archived seam fixtures remained pixel-identical at the default zero range.

`QuiltingAlongSearchSmoke.Capture` writes `output/quilting-along-search/comparison.png` from the archived noise fixture: columns 0/12.5/25%, Normal, Linked, width 20%, Feather 100%, seed 17, without compensation/Poisson, joins centered. Seven-run warm medians including readback were 8.354/10.280/11.451 ms on the tested Editor; these are local observations, not a frame-time guarantee.

The closed second-axis cut chooses a shared low-cost endpoint guard, then minimizes the interior path subject to it. It is not an unconstrained global cyclic minimum. Analysis is capped at 96/160/256 per axis; candidate budgets are 8/24/48. Blending uses original full-resolution pixels. Bands with fewer than four analysis samples use centered cuts with full-resolution Feather, not an unconditional blend bypass. Only one-pixel output bands copy directly. Center values are retained apart from premultiplication roundoff/zero-alpha hidden RGB in Linked; optional global Poisson changes this guarantee.

Feather is 0–100% (default 50) of the maximum centered transition width for each cut separately. With band B and strip coordinates L/R, widths are `2*p*max(0,min(L,B-1-L))` and `2*p*max(0,min(R-B,2*B-1-R))`, where p is the percentage divided by 100. Tiny-analysis cuts use `(B-1)/2` and `B+(B-1)/2`. A centered smoothstep uses each width independently; p=0 uses a hard step. This protects both band boundaries and fully copied tile-border samples. It is not an isotropic image blur. At very few output pixels, changing width need not change sampled weights.

The old field/API name is retained, but values now mean percentages without migration: old 16 means 16%, not an approximate conversion of 16 pixels. New default is 50%; rendering no longer scales Feather by preview resolution. Old appearance is intentionally not preserved. Cut lines, repeated features and contrast changes can remain. Higher search quality is not a perceptual quality guarantee. Reduced previews can choose different donors/cuts; final evaluation needs full-resolution Tiled output.

`PatchQuiltingFeatherSmoke.Capture` optionally writes a real-noise GPU comparison PNG under `output/quilting-feather-percent/` using the archived fixture. Columns are 0/50/100%, top row Patch Width 2%, bottom 45%, Normal/Linked, no Poisson. Joins are centered and RGB is encoded for display.

## Measured experiment, 2026-09-28

Actual GPU output from a saved 512×512 packed noise input, G/B selected, All Edges, width 20%, seed 0, no Poisson. Median of three warm runs, including synchronous readback: Linked Draft/Normal/High about 16/102/477 ms; Independent about 23/145/744 ms. This is a local observation, not a hardware-independent budget; four independent channels cost more. Cache hits were verified separately.

These historical measurements used the retired pixel Feather. The visual comparison showed obvious cut lines at 3 px; 16 px reduced them and was the initial default. The existing Offset Blend + Poisson example remained smoother on this soft-noise fixture. The later percentage change supersedes that default and interpretation.

## NativeArray/Burst optimization measurements

Paired warm old/new medians, same 512×512 G/B fixture, width 20%, Feather 16, no Poisson, including output readback. Linked Draft/Normal/High: 16.22→5.69 / 97.23→7.40 / 467.84→15.02 ms. Independent: 22.67→5.84 / 154.87→8.21 / 748.49→15.17 ms. All six comparisons had zero pixel difference. Results vary by hardware/editor load and exclude first-time Burst compilation.

`QuiltingEquivalence.Benchmark` runs the optional comparison on that fixture using the current percentage blend, so it does not recreate the historical pixel-Feather image; `Main` is self-contained. The optimized path has no managed image, cost, predecessor or path arrays. Texture pixel data is borrowed, not independently disposed. Jobs own disjoint scratch slices. Strict floating-point mode and double intermediate score normalization avoid changing near-tied donor ordering.

Pre-Poisson entries use the window's existing LRU budget, at 16 bytes/pixel. Readback/job completion remain synchronous; these optimizations do not introduce a background render pipeline.

## Batched search and retained scratch

One search batch covers every unique (channel, rounded donor, cut side) task for an axis. Candidates alias those tasks, then expand back into the unchanged original candidate list before sorting/seeded selection. The second axis still depends on the rendered first axis; it is not parallelized with it.

One process-wide idle scratch workspace reuses Persistent NativeArrays and readback/path Texture2Ds. It stores no reusable rendered result. All jobs complete before a workspace can be returned; a nested rent cannot borrow active storage. At most 64 MiB of native buffers plus estimated CPU/GPU RGBAFloat texture storage is retained, separately from the window result-cache budget. Oversized workspaces are disposed on return; retained resources expire after 30 seconds without a search and are released before domain reload or editor quit. Unused map channels are cleared on every evaluation. No settings, defaults or file format changed.

`QuiltingEquivalence.CurrentTiming` measures 15 warm current-only samples per setting, including output readback, on the optional archived 512×512 fixture. Two consecutive runs were taken before and after this second optimization. Second-run medians (milliseconds, G/B, All Edges, Feather 16, no Poisson):

| Quality / matching | Width | Before | After |
| --- | --- | ---: | ---: |
| Normal / Linked | 20% | 6.30 | 5.12 |
| Normal / Independent | 20% | 7.00 | 5.82 |
| High / Linked | 20% | 9.39 | 8.04 |
| High / Independent | 20% | 11.93 | 9.69 |
| High / Linked | 45% | 13.54 | 8.82 |
| High / Independent | 45% | 18.55 | 11.02 |

These are sequential editor runs, not simultaneous paired samples. Tail latency fluctuated substantially (High p90 roughly 26–40 ms in most samples); the synchronous pipeline can still stall the UI. This is a local measurement, not a frame-time guarantee. The first repeat showed the same High improvement. Wider bands have more duplicate donors, so deduplication helps them more.
