# Tiled paint experiment record

The **Raster** backend now lives in `src/Layers/TiledPaint`, with shaders in
`src/Shaders`. Brush/Pencil use its Drawing adapters. This directory retains
the pre-integration contract and measurements; it has no second backend copy.
Gather/hybrid experiments are not production or test dependencies. Integration
verification is tracked separately in [the Brush contract](../../Documentation~/AI/BRUSHES.md#sparse-raster-source).

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

## Production integration

`DrawingLayerBehaviour.TiledSource.cs` and `.TiledBrush.cs` route native
stamp payloads to the sparse source, retaining the original UV/Transform frame.
`.TiledHistory.cs` connects tile versions to the existing pixel journal and
`.StorageAccess.cs` supplies document ownership, validation and byte accounting.
The existing tagged TIFF payload stores original pixels plus signed Half replacement tiles.
JSON keeps its explicit Drawing-omission policy. Rendering consumers use the
Drawing source adapter rather than importing this directory.

The retained backend checks do not replace actual Drawing tests. Native tip
payloads, editor history, persistence, rendering consumers and 8K editor-path
costs have separate integration cases in `Tests~/Cases/Painting/TiledDrawing*`.
Historical standalone passes below are not evidence that those cases passed.

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

Tests read the production shaders and loaded backend without importing assets.
Only the simple-tip test adapter from `Tests~/Framework/Painting` is bundled.
They do not open windows, simulate input or edit documents. Reports and bundles
go to `Temp/WhimTex/tiled-paint-readiness`. The native comparison oracle invokes
the retained dense `PaintStrokeBuffer` path, separately from Raster; it does not
compare two names for the new renderer and does not require old experiment files.
