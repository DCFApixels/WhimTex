# UV island overlay regression

Run through the connected Unity Editor Pipeline, always passing the intended project path:

- `UvIslandsSmoke.cs`: transient mesh geometry, selection rasterization, channel/submesh filtering, non-readable meshes, seams, hard-normal vertex splits, overlapping surfaces, folded and non-manifold edges, holes, clipping, memory limit and cloned document references.
- `UvUiSetup.cs`, then `UvUiSmoke.cs`: opens a temporary window and closes it after checking layout, panel exclusivity, topology reuse, rotated picking, selection operations and preservation across tool/overlay changes. It never writes a scene or asset. If interrupted between calls, run the second file to close the fixture.
- Optionally run `UvUiCapture.cs` between those two calls to capture the fixture to `Temp/WhimTexUvPreview.png`.
- `node Tests~/UvIslands.test.mjs`: source contracts, including passive overlay picking, bounded drawing batches and no rendering-time mesh rebuilds.
- Also run the existing `CanvasSelectionSmoke.cs`, `AreaSelectionPaintSmoke.cs`, `ShapePicker.test.mjs` and `MarqueeConstraints.test.mjs` regressions.

Local Unity 6000.7.0a6 checks: 38,274 geometry assertions and 262,161 UI/selection assertions passed. A 96×96 quad grid (18,432 triangles) produced one island and 384 boundary edges; one build took about 34 ms and 1,000 reflection-invoked picks about 4 ms. These are local CPU measurements, not a GPU frame-time guarantee.

The overlay handles the primary 0–1 tile, not UDIMs or preview repeats. Selection uses unioned triangle coverage so holes and self-overlap do not require simple-polygon contour reconstruction. Coincident disconnected geometry cannot always be distinguished after exact position/UV welding; ambiguous non-manifold edges are conservatively retained. No mesh UV edits, importer writes, new packages or output-texture rendering paths are involved.

Manual checks: assign an imported mesh via ObjectField/drag-drop; reopen a saved document; undo mesh/channel changes; toggle UV while using Brush/Pencil/Fill; exercise the held tool dropdown and MMB navigation; inspect a very dense fragmented unwrap at several zoom levels. Standard- and light-theme visual checks remain useful.
