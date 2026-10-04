# GPU preview regression checks

Run `GpuPreviewSmoke.cs` as an opt-in Pipeline eval body after Unity Editor
compilation, with a graphics device available. It creates no saved assets and does not
modify Undo. It checks empty, Drawing, SDF and Outline compositions at multiple
resolutions, including 1 pixel and a return to the original size. It compares GPU
preview readback with the CPU preview API, compares readback with/without upload,
and verifies restoration of the active render target. These are correctness
checks, not performance measurements; the test deliberately reads pixels back.

The window and Properties previews own pooled render textures returned by
`RenderCanvas` / `RenderLayerPreview` until replacement or close, then detach
their UI images and call `RenderTexture.ReleaseTemporary`. CPU consumers retain
`ComposeCanvas()`, `ComposeCanvas(maxSize)` and the default uploading `CopyToTexture2D` path.
SDF/Outline input readback opts out of upload; their output upload is unchanged.

After compilation, check in the Editor:

1. Open an asymmetric image with transparency. Verify orientation, colors and
   alpha, then zoom, pan and switch RGBA channels in the main preview.
2. Paint with different Live Quality values; verify refresh during and after
   strokes. Repeat with SDF/Outline and in separate Properties windows.
3. Switch documents, resize the canvas, and close/reopen both window types.
   Check for stale images, invalid texture warnings and growing retained texture
   counts after repeated cycles.
4. Verify save/reopen, export, API preview export and All Layers fill still work.

For profiling, compare identical documents and edits before/after the change.
Main-window and Properties display should no longer perform final `ReadPixels`
or allocate a readable `Texture2D` on every refresh. SDF/Outline still require
input readback for CPU processing. No speedup factor is assumed without timings.
