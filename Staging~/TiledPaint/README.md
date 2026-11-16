# Tiled paint integration candidate

This directory is intentionally ignored by Unity's asset importer. Nothing in
`src` references it. It is a **Raster** source backend, not a second editor or
a replacement enabled in the current drawing tool. Gather/hybrid experiments
are not dependencies of this module or its tests.

## Contract

- Logical tiles are 256 × 256 premultiplied, linear RGBAHalf pixels. GPU pages
  are 256, 512 or 1024 square, chosen from the original source dimensions.
  Logical tile keys can be negative. Allocating tiles does not change the
  original source frame, its UV coordinates or its Transform.
- Source pixel dimensions and document pixel dimensions are independent.
  `TiledPaintFrame` holds their immutable projective mapping. UV outside the
  original frame remains outside 0…1. Drawing is not clipped to the canvas.
  An active selection still contributes zero outside the canvas unless its
  wrap flag is enabled.
- `Begin` copies the small settings structure and freezes selection/original
  straight textures by exact GPU copy. The caller may subsequently edit or
  destroy its original textures. Unsupported exact-copy formats/devices are
  rejected; no implicit format or color-space conversion is used.
- `Append` accepts a complete ordered raster batch. It validates the entire
  bounds list before changing pending storage, then records and submits it.
  Validation failure preserves the earlier pending stroke. Allocation,
  recording or submission failure cancels the pending stroke and preserves
  the committed source. Raster callbacks cannot re-enter the surface.
- `Commit` reserves output versions and constructs the new snapshot/delta
  before GPU submission. The source changes at one publication point.
  Consumed pending pages can be reused later in the same ordered command
  buffer. Existing source/history slots are never overwritten.
- Raster draws pending ink. Larger commits may use the equivalent compute
  composite; this is not the experimental gather renderer. Raster composition
  is available when compute/RW Half is unavailable or explicitly disabled.
- `CopyRegion(region, target, includePending: true)` composes only the requested
  source-pixel region directly into a caller-owned Half target. It neither
  commits the stroke nor allocates another image. This is the regional preview
  entry point; it does not downsample the whole canvas.
- `AcquireSnapshot` and `Commit` return disposable leases. The editor owns
  Undo retention/branching. There is no hidden four-step history. A lease can
  outlive the surface; tile views remain borrowed and read-only. `Restore`
  accepts snapshots of the same surface and immutable frame.
- `ImportTiles` accepts exact 256-square linear Half regions and preserves
  keys outside the original frame. `CopyRegion` zero-fills missing tiles.
  Enumeration of snapshot tiles supplies texture + physical rectangle for
  the existing asynchronous readback/history writer. Transfer their bytes
  before releasing the lease; reconstruct through `ImportTiles`.
- Lost unused pages are recreated. Lost source/history pixels cause an
  explicit error; they are never treated as transparent. A complete CPU/document
  restoration can replace the source through `ImportTiles(..., replace: true)`.
  A retained lost Undo snapshot remains invalid and must also be restored.

## Ownership and limits

Create/use/dispose the surface, snapshots and raster resources on the owning
Unity thread. `Dispose` is idempotent. `Prepare` allocates and clears a requested
number of free pages between strokes; it is not part of pointer-down handling.
`TrimIdle` releases excess empty pages at a safe point.

`GpuBudgetBytes`/`AllocatedBytes` cover owned texture payload: all prepared,
pending, source and retained-history pages, plus frozen inputs. They do **not**
claim physical VRAM or include the caller's textures, native Mesh/Material,
driver overhead or managed arrays. Batch stamp/reference limits independently
bound geometry allocations. A budget failure is explicit and transactional;
the editor must flush/evict retained history through its existing policy before
retrying. Increasing the budget is not an automatic fallback.

The native simple-tip adapter accepts already spaced/snapped stamps and decoded
linear shader colors; flow is already included in alpha. It covers Brush and
Pencil Circle/Square/Diamond, including partial edge tiles. It does not generate
pressure/dynamics, texture tips or pattern payloads. Those remain responsibilities
of the existing native stamp generator/mesh builder. A prepared native batch can
implement `ITiledPaintRasterBatch` with its existing attributes/material.
Backdrop/pressure accumulation requires the explicit `ITiledPaintBackdropBatch`
contract and is rejected for plain-ink batches before mutation.

Keep `TiledPaintBrushRasterizer` for the duration of a stroke and call
`CreateBatch(stamps)` for each update. Dispose that batch after `Append` returns;
its CPU inputs are copied and it holds the rasterizer's single active lease.
Material, Mesh and bounded geometry buffers are reused. The convenience
`TiledPaintBrushBatch` constructor is suitable for isolated single updates.

`StandardColor`, `HDRBlend` and color decoding are separate decisions. For
protected channels at zero alpha, provide the original straight texture and
its encoding explicitly. Premultiplied pages alone cannot reconstruct hidden
RGB. Mask and original-straight copies have the source texture's exact format
and sampling settings.

## Connecting the editor later

These are integration touchpoints, deliberately not enabled by this change:

1. Route the existing `DrawingLayerBehaviour.PaintStamp` and native mesh/material
   payload to a raster batch. Preserve dynamics/pressure/rotation/tips rather
   than rebuilding them in the storage layer.
2. Remove clipping at `BuildBrushSegment`, `BuildPencilSegment` and
   `DrawCanvasStamp` when using this source. Swapping only `PaintStrokeBuffer`
   leaves these earlier clipping gates active.
3. Retain the Drawing source frame (`sourceCanvasSize`, `sourceCanvasScale`,
   pixel Transform) separately from occupied tile bounds. Use the region API
   for visible rendering; add explicit adapters for consumers requiring a
   complete image, including spatial FX, export and thumbnails.
4. Connect delta leases/tile views to `PaintHistoryStore`/`PaintTileTransfer`.
   CPU/disk transfer, cancellation and eviction stay under the editor's history
   policy. Do not introduce a second Undo stack in the backend.
5. Persist logical tile keys, original source frame and exact Half tile data.
   Preserve existing atomic document saving. Verify the actual Drawing,
   group/Target, clipping, thumbnail and export paths after connecting them.

The standalone checks do not claim those production integration paths are
already active or tested. Texture tips/dynamics/backdrop integration and other
graphics backends require their own actual-render checks when connected.

## Validation

The measured results and audit scope are recorded in [VALIDATION](VALIDATION.md).

The opt-in `tiled-paint-readiness` profile lives in the normal test catalog.
The separate `tiled-paint-comparison` profile compares the native renderer.
Standalone bundle builders compile **only through the connected Editor**:

```powershell
& 'Packages/com.dcfapixels.whimtex/Tests~/scripts/build-tiled-paint-readiness.ps1'
unity command run_script --file 'Temp/WhimTex/tiled-paint-readiness/TiledPaintReadiness.Run.cs' --entry TiledPaintReadinessTests.Run --args '["all"]' --project-path 'D:/DCFA/Projects/Test6.6' --timeout_ms 50000 --timeout 55 --format json

& 'Packages/com.dcfapixels.whimtex/Tests~/scripts/build-tiled-paint-comparison.ps1'
unity command run_script --file 'Temp/WhimTex/tiled-paint-readiness/TiledPaintNativeComparison.Run.cs' --entry TiledPaintNativeComparisonTests.Run --args '[2048,0.005,3,true]' --project-path 'D:/DCFA/Projects/Test6.6' --timeout_ms 50000 --timeout 55 --format json
```

Tests temporarily import only their owned shader fixture folder, then remove it.
They do not open windows, simulate input or edit documents. Reports and generated
bundles go to `Temp/WhimTex/tiled-paint-readiness`. The native comparison adapter
contains only the actual current renderer invocation and frozen input path;
it has no runtime dependency on old polygon files.
