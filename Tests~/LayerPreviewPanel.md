# Layer preview panel checks

Run against the connected Editor (no saved assets are created or modified):

```sh
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/LayerPreviewPanelSmoke.cs --entry LayerPreviewPanelSmoke.Start --project-path D:/DCFA/Projects/Test6.6 --format json
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/LayerPreviewPanelSmoke.cs --entry LayerPreviewPanelSmoke.Result --project-path D:/DCFA/Projects/Test6.6 --format json
```

The first call schedules checks across UI layout frames. Wait for `Result` to report a verdict. Temporary test windows are closed, their documents discarded and the previous focused window restored. PNG captures are written under `Temp/WhimTex/LayerPreviewPanelSmoke`, outside assets.

## Verified 2026-09-28

- Unity compilation completed with no errors.
- Smoke passed 1,441,850 assertions, predominantly GPU pixel comparisons against `RenderLayerPreview` and RGBA/RGB/Alpha expectations.
- Actual pointer events expand/collapse the embedded preview. The channel control is excluded from resize capture.
- Both owners use the same component outside their settings scroll; there are no mip controls, texture-overlay labels or Properties Close buttons.
- Selection, edits, deletion and UI rebuild update the binding correctly. The independent Properties window continues to show its own layer.
- Landscape, portrait and tiny-image bounds are respected; resizing the image does not erase the preferred height. Serialized view state round-trips.
- Collapsed and detached panels do not render; rebuild, detach and deletion release owned preview resources and subscriptions.
- Captures inspected: 320-point-wide Properties and the embedded panel in a 1000×780 window. Checkerboard, image placement, footer alignment and controls are consistent.
- EN/RU/ZH documentation source validation passed (79 pages).

Manual follow-up: light theme, high-DPI/multi-monitor layouts and persistence across a full Editor restart were not visually tested. The layer-rendering algorithm is unchanged; this suite does not claim new coverage of every layer/FX implementation.

## Viewport regression — 2026-09-29

Added a live layout assertion that the settings content viewport ends at the Preview divider. It reproduced an 8-point gap before the USS change. Removing the bottom ScrollView padding fixes both Layer Settings and Properties; expanded and collapsed layouts pass with no gap. Full smoke rerun: 1,441,853 assertions passed.

## Main-render reuse and stable sizing — 2026-09-29

Current behavior supersedes the original image-dependent bounds: Mini Preview has a 256-point maximum block height including the divider, constrained only by available parent height. Preferred height survives changes of layer, image size and aspect ratio; `ScaleToFit` can enlarge small textures. Live layout smoke rerun passed 1,441,853 assertions with the new bounds, including 2×1 images, rebuild, detach, both owners and channel viewing. The updated Properties capture was inspected.

Additional test:

```sh
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/MiniPreviewReuseSmoke.cs --entry MiniPreviewReuseSmoke.Start --project-path D:/DCFA/Projects/Test6.6 --format json
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/MiniPreviewReuseSmoke.cs --entry MiniPreviewReuseSmoke.Result --project-path D:/DCFA/Projects/Test6.6 --format json
```

Passed 2,752,550 assertions:

- Captured Noise matches the downsampled main-resolution render and measurably differs from an independent lower-resolution Noise calculation.
- Reading an already available effect cache produces a hit without rerendering. Dependency changes reject stale entries; cache eviction cannot destroy the mini preview's owned copy.
- Observing a layer does not change the composite. The captured layer precedes outer opacity/blending. Colored group fallback matches the main group's own content; stack processors publish their processed result.
- Clipped layers use a separate render retaining clipping coverage instead of incorrectly showing the unmasked chain source. Pass-through groups also use an isolated-content fallback, not the external backdrop.
- Export and thumbnail rendering cannot publish mini-preview images. Collapsed/detached panels ignore main renders; owned textures and event subscriptions are released.
- Cache display preserves `RenderTexture.active` and `GL.sRGBWrite`.

The existing `EffectCacheSmoke.cs` regression passed 278,538 GPU assertions. Unity compilation completed without errors, and EN/RU/ZH documentation source checks passed.
