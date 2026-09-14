# Content-aware fill regression

Run with the connected Unity Editor only. Set the absolute path of the intended Unity project
explicitly; do not rely on automatic selection when several Editors are open:

```powershell
$testProjectPath = "D:/Projects/MyGame" # Replace with the intended Unity project root.
$testFolder = Join-Path $testProjectPath "Packages/com.dcfapixels.whimtex/Tests~"
unity command eval_file --file (Join-Path $testFolder "ContentAwareFillSmoke.cs") --project-path $testProjectPath --format json
unity command eval_file --file (Join-Path $testFolder "ContentFillUiSetup.cs") --project-path $testProjectPath --format json
unity command eval_file --file (Join-Path $testFolder "ContentFillUiSmoke.cs") --project-path $testProjectPath --format json
```

The setup creates a temporary unsaved window; the UI smoke closes it and restores previous focus, including on test failures. It doesn't create project assets or change scenes. Optional `ContentFillUiCapture.cs` captures the test settings window if it is actually visible on screen (do not mistake an occluding application for the fill UI).

The core test covers inward strips, canvas borders, holes, partial coverage, empty-only masks, finite HDR exemplars, outside immutability, deterministic seeds, odd-size pyramid levels, empty donor/target errors and cancellation. A before/after stripe fixture is written only to `Temp/WhimTexContentFillTest.png` for visual inspection.

The UI smoke verifies asynchronous calculation, no changes before Apply, native cropped HDR storage, canvas-space placement through the real GPU compositor, exact inner strip coverage, Undo/Redo, custom source capture without changing the target, cancellation, stale-result rejection and canvas resize guards.

Implementation is independent managed code, based on the propagation/random-search idea described in [Barnes et al., PatchMatch (2009)](https://gfx.cs.princeton.edu/pubs/Barnes_2009_PAR/index.php). No reference implementation or third-party source code is included. Matching is multiscale with weighted premultiplied patch costs and overlapping patch voting. Source patches cannot include fill pixels, invisible pixels or non-finite/out-of-half-range RGB. A chamfer distance field approximates Euclidean inward border width. Worker data is isolated from Unity objects; snapshots/readback and applying the result run on the main thread. Two working color buffers are reused per pyramid level; at most one job runs at a time.

Current deliberate limits: 4,194,304 pixels per working bounding rectangle; 2D image-space reconstruction, not 3D seam correspondence; no periodic/tiled donor search; no AI generation; no automatic per-keystroke synthesis. Preview is display-only LDR, final pixels remain native linear HDR. Selection is frozen when the window opens; content changes invalidate the result. Background work is cancelled on close/reload, not persisted across recompilation.
