# Node Image/Height checks

Profile: `node-height-preview`; optional smaller case: `node-height-smoke`.
The reviewed case and support sources are listed in the test catalog. Effects:
`temp-files,windows,user-state`. Only owned utility windows and unsaved documents
are used; no scene, project asset or user graph is edited.

Each run opens an 8192² graph containing HDR/negative/alpha arithmetic outputs and
a Cellular Noise node with multiple outputs. It checks the actual native or SVM
backend, switches the native toolbar controls and opens the settings popup. Assertions
cover independent settings, unchanged source texture/program identities, numeric
HDR/alpha preservation, output switching, overlay bounds, the existing eight-item
queue limit, idle reuse, collapse/offscreen release and view-rebuild retention.
Graph JSON, document dirtiness and Undo group must remain unchanged by presentation.

The benchmark alternates Image/Height, measures complete source + presentation updates
for all visible nodes, and drains GPU work through a separate 1×1 readback. Four warm-up
rounds precede 20 measured rounds per mode. It reports CPU submission and completion
latency, not isolated GPU time or Editor frame rate. Reflection and fixture overhead
are included equally. The fixture updates the entire set in one measurement; production
uses its normal 4 ms/eight-item scheduling budget. Native compilation is outside timing.
Measurements remain under `Temp/WhimTex/node-height-preview`.

## Checked 2026-11-09

Unity 6000.7.0a6, D3D12, RTX 5060. All 32 node previews were visible; retained preview
textures were 128²; Height Scale was 0.5 at measurement time. Additional Height buffers: 16 MiB total (geometry + display,
512 KiB per node). No additional 8K surface was allocated for node relief.

| Renderer | Image median / p95, ms | Height median / p95, ms | CPU submission Image / Height, ms |
| --- | --- | --- | --- |
| Native | 3.39 / 4.01 | 4.95 / 6.87 | 3.00 / 4.07 |
| SVM | 1.94 / 2.26 | 3.12 / 4.20 | 1.49 / 2.32 |

Thirty repeated idle passes submitted no node renders. Each native/SVM integration
case passed 25,792 assertions; shared renderer and Layer Preview UI checks also passed.
Initial SVM runs fell back to native because Unity retained an earlier out-of-memory
compiler error on `GraphPreviewVM.shader`. A normal ForceUpdate reimport restored SVM;
the successful run verified that every test node actually used SVM. No shader-source
change was made to hide that error.

Reports: `2026-11-09T08-06-18.814Z-139212.json` (native case passed; initial SVM failed),
`2026-11-09T08-10-04.528Z-52192.json` (SVM, shared renderer, shared UI passed).
The smaller eight-node native smoke check also passed. These measurements describe
the bounded preview path, not arbitrary expensive upstream graphs or drawing/Undo I/O.
