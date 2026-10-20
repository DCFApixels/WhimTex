# Running WhimTex tests

Start with the [test map](README.md): `Cases/` is organized by subsystem, `Framework/`
contains shared support, and `Fixtures/` contains required inputs. The explicit
`scripts/test-catalog.json` owns IDs, invocation metadata and limited profiles; directory
contents are not automatically discovered as runnable tests.

Tests are self-contained in the package: they do not require a Legacy archive, migration
maps, Git objects or the old project's `output` directory. Frozen reference inputs and
their provenance are in [Fixtures/Oracles](Fixtures/Oracles/README.md).
Ordinary cases use ephemeral test assemblies. Opt-in native-fixture/Player workflows
temporarily install GUID-owned native scripts under the authorized test subtree.
Explicit human permission covers test-only internal bindings for eyedropper, dock/tab,
profiler, and ReadScreenPixel on test-created windows. It does not authorize other internals.

## Inspect and select

`normal-specular-v1` checks the catalog-linked Normal Lighting preset against an independent
CPU diffuse/specular oracle with Packed Color and Linear Data, original disabled behavior,
all output modes, transparent/partial/filled coverage, HDR, tint alpha, Roughness endpoints,
Flip Y and frontal/opposite/degenerate directions. Owned documents cover group/Target input,
clipping, thumbnails, group export, external disabled-layer normals and JSON render parity.
It retains a small actual-render preview under GUID-owned `Temp/WhimTex/normal-specular`.
No user windows/documents or project settings are changed. Native pointer interaction and
every graphics backend are not covered.

`gradient-debug-curves-v1` creates an unshown gradient editor and checks graph/code presence
against the active target's `WHIMTEX_DEEP_DEBUG` symbol. With the symbol enabled it checks
RGBA button presence and visibility callbacks, unpremultiplied signed/HDR values, exposure independence, reused
sampling buffers, interpolation updates, Fixed discontinuities and wrap endpoints. It never
shows or focuses a window, changes a user document, or toggles project defines. Actual on-screen
appearance and native pointer dispatch remain manual checks. Run once without the symbol and again with it for both branches.

`height-to-distortion-v1` compiles the catalog preset and compares GPU output against an
independent CPU derivative/bilinear oracle: all three filters, five channels, three edge
modes, fractional radii and a differently sized Clamp texture. It checks exact flat/zero
neutral maps, signed/large unclamped vectors, affine packing, axis/sign controls and
Displacement Map parity. Owned documents cover Self/external disabled-layer inputs,
groups, Target, clipping, thumbnails, group export and JSON. A small actual-render preview
is retained in a GUID folder under `Temp/WhimTex/height-to-distortion`. No user windows or
documents are changed. Float arithmetic checks are stricter than existing Half-stage
rounding tolerances. Point filtering, mipmapped imports and every graphics backend are
not covered by this case.

`shader-fx-code-lines-v1` requires Unity 6000.7+. An owned window checks actual gutter draw
submissions against public native cursor baselines, font matching, fixed horizontal position,
fractional vertical scrolling, a 1102-line file, blank/trailing CRLF lines, native Enter,
Undo/Redo, silent field updates and code focus. It also checks actual current-row fill/tint
submissions, background ordering behind native selection, caret-only and focus changes,
and row alignment/width under scrolling. It does not capture the user's screen or
validate every font/skin visually; the ordinary code-reference case checks editing behavior.

`shader-fx-code-syntax-v1` requires Unity 6000.7+. It checks lexical categories, malformed
comments/quotes, CRLF and cache reuse, then observes actual generated glyph RGB, positions,
UVs and alpha through the public callback. An owned window checks UTF-16 offsets after a
surrogate pair, caret and reversed selection preservation. Code is not compiled. The
scrolling check exposes previously uncolored tail glyphs without changing their source.
The ordinary code-reference case separately checks editing and Undo/Redo with highlighting installed.

Opt-in `shader-fx-code-typing-v1` measures real textEdition KeyDown selection replacement
in an owned 640x480 field: 100/1000 lines, 8 warmups + 32 samples, repeated reverse order.
It types near the start of the source and compares the same native generator with the callback disabled/enabled, preserving user
captures and baselines. Input dispatch includes draft/Undo handlers; callback time includes
tokenization. Neither is the full deferred UI Toolkit layout/render time or GPU time.
Retain a pre-change run before implementation; raw arrays and generator/callback counts are
GUID-scoped under `Temp/WhimTex/code-typing`. A PASS means the workload ran and changed exact
source/caret, not that it met a performance threshold. No physical input or user document edits.

`shader-fx-code-reference-v1` checks Shader Inputs double-click callbacks through public
UI Toolkit pointer/link/click events in an owned bound inspector: caret/selection insertion,
signature-only replacement, argument-name preservation, focus, disabled/read-only guards
and owned Undo/Redo. Two native down/up cycles exercise rich-text hit testing and Unity's
ClickEvent detector with mouse-up click counts fixed at one. Native hover checks fragment
highlighting, hand-cursor class, unchanged text, no rebuild within one link and tooltip
suppression. It does not compile edited snippets, change user documents or inject physical
input. Manual interaction, the displayed OS cursor and every skin remain separate checks.
The same owned inspector checks four-space Tab stops, selected-line indentation and Shift+Tab
outdent, forward/reversed selections, excluded trailing lines, existing tabs, CRLF, empty and
read-only input, focus and single-step Undo/Redo. The default ApplyFX body uses the same indent.
Keyboard events target the public textEdition element, not the retargeted field wrapper.
Enter/KeypadEnter and `}` cases cover carried/nested indentation, existing brace pairs,
matching openers, comments/quoted text and escapes, tabs/CRLF, replaced selections, AltGr,
read-only input and Undo/Redo. A physical down + translated CR/LF + key-up sequence must
insert only one newline; repeated key-downs and standalone newline characters remain usable.

`shader-fx-code-info-v1` checks the compiled Pixel Local row above the code area, changed drafts,
failed/recovered compilation, disabled FX and Compile button naming on unattached controls.
It checks the sectioned/bulleted Shader Inputs reference and rich-text identifiers, not on-screen geometry
or every skin. The case owns its transient document, shaders and serialized object; it does not
open a window or change a user document. One deliberate malformed signature produces a diagnostic.

`shader-fx-pixel-local-v1` checks all three ApplyFX signatures, immutable PinnedUV values and helper/include
arguments, deliberate arithmetic/write/cast/signature failures, primary SampleInput/SampleTexture reads,
including pinned `_MainTex` helper reads versus direct sampling bypasses and texture-size metadata,
Texture/Self/None/Layer masks, unequal source resolutions, active-chain queries, pending/failed Apply,
native/JSON/preset restoration, preview compilation and bypass warnings. It also compiles all built-in FX,
checks their pixel-local classification and compares migrated presets against their original spatial forms.
It uses detached documents and
owned float textures without persistent assets or user windows. Deliberate negative cases emit compiler errors.
The case verifies authoring/render behavior, not a sandbox against intentionally obscured HLSL.

The `freeform-gradient` profile checks point interpolation against an independent CPU oracle,
signed HDR and alpha, shared-material point-count changes, thumbnails, group/Target rendering,
export, current TIFF/JSON roundtrips and Undo/Redo. Its temporary owned window exercises point
creation, dragging, influence, Escape, property fields and transformed parents through public
UI Toolkit events. The included viewport case compares full and cropped rendering, including
Freeform. These cases do not manipulate user documents or physical mouse/keyboard input.

`profiler-gpu-result-v1` checks supported actual threading modes, positive/zero/negative/multiple-invocation
timestamps, Graphics Jobs confidence tagging, open/ended readback invalidation, immutable completion and guarded production readback sites.
It uses managed synthetic slots without native
recorders and never replaces the retained user capture. `profiler-fx-v1` separately checks live delivery,
render parity and pending/stop/reload behavior with owned documents and a temporary window.
Its parity readback uses AsyncGPUReadback so it does not invalidate the measured render intervals;
the production encoded-copy path still exercises ReadPixels and must report invalidation explicitly.
`profiler-analysis-v1` also checks unconfirmed numeric aggregation/comparison, compact agent warnings and
recorded threading metadata through baseline/reload snapshots. `profiler-comparison-ui-v1` checks yellow
GPU-only numeric backgrounds, compact values, recycled row cleanup and historical capture/baseline warnings.
These synthetic confidence checks do not validate timestamp accuracy with active Graphics Jobs.

`hsv-master-timing-v1` and the opt-in `profiler-gpu-audit-*-v1` diagnostics compare two catalog-linked
Hue & Saturation instances at 512/1024/2048, active and neutral. One forces the full range path with
an enabled empty interval; the other disables every Range and uses the Master fast path.
The outputs must match. Older HSV timing reports/retained fixtures describe the former math,
not the current weighted-gray Saturation and Lightness implementation.
They alternate 16 AB/BA pairs after two warmups, using 64 independent same-input ARGBFloat blits
per interval. CPU preparation uses 128 warmed direct-delegate calls. Native intervals divided
by 64 are batch-normalized elapsed device times, not isolated shader instructions or FPS.
The audit keeps fresh single-use reference recorders alongside the ordinary bounded pool:
`split` brackets immediate blits; `single` puts reference boundaries and blits in one diagnostic
command buffer; `same` renders exactly the same HSV material under both labels; `repaint` submits
from an owned window Repaint instead of Editor update. None changes the production renderer.
Each audit owns 216 reference marker identities until script reload and disposes their recorders.
Raw profiler exports and timing arrays are retained under GUID-owned `Temp/WhimTex/hsv-master-timing`
folders. Reports are never overwritten. The retained capture, user documents and focus are preserved.
An audit PASS means the diagnostic completed, not that all native timestamps are trustworthy.
Keep invalid results in the report. For comparisons retain only complete pairs whose two native
intervals are positive/single-invocation and agree with their independent references within 5%;
this is a diagnostic cross-check, not a production timing floor or proof against shared driver errors.
Repeated positive timestamp anomalies still require independent GPU-level verification.

The bounded `profiler-gpu-probe-*` diagnostics add drained-queue wall timing (submission plus completion
wait/readback, not pure GPU time), format/size sweeps, 0/1/8/64/128-pass scaling and never-reset reference
recorders. `sync` isolates no readback, pooled-only ReadPixels, AsyncGPUReadback completion, pre-only,
post-only and single-buffer readback. Their raw ReadPixels calls intentionally bypass the production
guard to reproduce native counter failure; an audit PASS does not certify those timestamps. They own
180–252 fresh reference identities depending on profile; recorders are disposed, identities last until reload.
The legacy Recorder control uses the same Unity backend and is not an independent hardware clock.
Normal timing refuses unsupported actual Graphics Jobs modes rather than treating delivered counters as valid.
These diagnostics never change project settings or restart the Editor.

`shader-fx-group-menu` checks group and tab Copy/Paste/Reset, nested/hidden/disabled
values, linked tab checkboxes, destination bounds/identity, clipboard rejection and
owned Undo/Redo on transient documents and unattached controls. `shader-fx-containers-v1`
also checks tab-checkbox geometry, native keyboard independence, export and JSON layout.
Neither fixture simulates physical mouse input. Shared metadata cases reject missing,
repeated, conditional, misplaced and non-bool tab header bindings in Unity and Node.

`fx-enableif` checks availability metadata and strict condition nesting, independent
visibility, disabled parameter rows/headers/previews, inherited blocking, preset/native/JSON
roundtrips, the real Selective HSV preset and external-validator parity. The dedicated case
uses transient models and unattached controls; it does not verify manual pointer input or
on-screen dimming. Existing rendered FX tests verify unchanged color processing separately.

`fx-containers` checks Group/Foldout/Tabs/Tab metadata and malformed nesting, optional
tab headings, inline and standalone previews, preset export and independently owned
layout arrays. Its bounded utility window checks content-width tab headers, native
keyboard submission, independent linked checkboxes and restored session presentation.
It leaves user documents/assets untouched and clears only the owned FX Undo/session keys.
Manual pointer interaction, every skin and script-reload continuity remain separate checks.

`fx-preparation-regression` checks direct parameter/list/name/type/ID edits, applied fallbacks,
duplicate priority, mutable gradient/curve/projective data, nested texture leases, Self ownership,
render-sized None inputs, current context dimensions and owned Undo/Redo. It uses only detached
objects, clears Undo only on its own FX and does not edit user documents or assets.
The opt-in `fx-preparation-before` and `fx-preparation-after` diagnostics run the same four warmed
CPU workloads (8/64 scalars, 32 mixed parameters, 12 Self textures), each with 11 timing batches.
They save retained float pixels and primitive timing arrays under `Temp/WhimTex/fx-preparation`;
existing reports are never overwritten. After requires the genuine pre-change `before-v2.json`
and compares seven float renders bit-for-bit, including Self/None/Layer/Texture input modes.
Stage medians/p95 are microseconds per call, not GPU execution, total render latency or FPS.
Allocation counting is separate and includes the reflection harness at material/input boundaries;
it must not be presented as exact production allocation counts. Fingerprints use direct delegates.
Compilation/JIT warm-up and output readback are outside timing. Existing profiler captures are retained.

`fx-empty-input-timing` is an opt-in diagnostic for the empty texture-input path only.
It compares a frozen pre-change implementation with the current production method,
using both typed delegates and the original reflective harness. It alternates 32 pairs
of 2048 calls for 0/8/64 nontexture parameters with an active 32px RT, writes GUID-owned
primitive arrays under `Temp/WhimTex/fx-preparation` and never edits production or assets.
Frozen nonempty dependencies reject execution; this is not a full old-renderer oracle.
Repeat separate runs to assess timing variability; no GPU or FPS claims follow from it.

`hlsl-only-fx-v1` checks the typed HLSL stack, HLSL-only catalog/clipboard signatures,
removal of Reference controls, group/clipping/Target/preview/export consumers, JSON/native
roundtrips and explicit rejection of external FX references before asset resolution.
It uses detached documents and in-memory payloads; no Unity assets, files, user windows or
clipboard content are changed. Render-consumer comparisons use primary-color endpoints,
not a general HDR/export color-space equivalence oracle.

`shader-fx-preview-v1` checks standalone/inline metadata, exact optional value types and
UV-first ordering, explicit positional @param bindings (including forward/hidden references,
all mapped value types, local diagnostics, native/portable/preset roundtrips and program sharing),
candidate/uniform separation, all 20 Hue & Saturation preview shader entries,
independent pixel expectations for every Saturation/Lightness track and the shared float Hue LUT with negative/repeated hue sampling.
It uses transient objects and writes a small generated image under Temp/WhimTex/fx-preview.
`shader-fx-preview-ui-v1` owns a temporary utility window, checks rendered hard/soft Slider
backgrounds, fixed track/triangle dimensions, matching track endpoints and numeric columns
at wide/narrow widths and with long values, shared program/binding reuse,
unchanged redraw suppression and compact right-side fallbacks, then closes it and restores focus.
These bounded tests do not verify manual dragging, complex live group/Target inputs, every
resource-valued signature, script-reload continuity or every graphics backend.

`shader-fx-preview-updates-v1` pauses all periodic preview schedules, submits 32 notified edits
and checks the next actual Editor update: one redraw per surface and one shared parameter rebind.
It mixes implicit and explicitly bound callbacks, and also checks same-update suppression, latest uniform pixels, event-driven Undo/Redo, unchanged
notification suppression, resumed polling for an unnotified edit and detaching a queued surface.
The fixture owns its utility window and FX, clears Undo only for that FX and restores focus.
These are scheduling/correctness checks, not a system-load benchmark or a measured millisecond latency guarantee.

`shader-fx-interval-v1` checks bounded float2 parsing and invalid defaults, API bounds/order,
preset export, native MinMaxSlider/Vector2Field selection, synchronized endpoint fields,
owned Undo/Redo, wide/narrow track/triangle geometry and actual float2 values in GPU previews.
It also checks public panel picking across both half-handles at each limit and full/empty ranges,
rejecting ancestor clipping or numeric-field overlap at wide and narrow widths.
It owns a temporary utility window and transient FX; cleanup restores focus and clears Undo
only for that FX. It does not claim manual pointer-drag or every backend coverage.

`shader-fx-cyclic-range-v1` checks percent-range flags, preserved wrapped order, full/empty
semantics, scalar numeric repetition, preset/native roundtrips, embedded previews, paired
numeric fields, synthetic public pointer dragging across both edges and whole-range moves,
zero/full-width stops at the original opposite endpoint (no global min/max reset), wrapped numeric
and marker positions, anchored-full preset/reapply/native roundtrips, immediate reversal after large overshoots, Escape cancellation,
and visibility/picking of both handles at full-range endpoints,
keyboard movement, owned Undo/Redo, one-turn Selective HSV preview colors and wide/narrow layout.
It owns a utility window and transient FX. It does not simulate physical mouse input or claim
all input-device/backend coverage. Ordinary scalar track/numeric widths remain unchanged;
range tracks remove the extra left/right inset rather than widening other controls.
The `fx-cyclic-range` profile combines this case with ordinary intervals, scalar preview layout
and Selective HSV rendering/persistence.

`canvas-viewport` compares regional composition with crops of complete renders for raster/HDR,
idle and active masked Drawing, Text, Shape (including Step), all gradient types, Noise, UV fill,
Vector, Boolean and transformed clipping groups. It checks full-frame isolation, zoom planning,
per-FX full-input prefixes/regional suffixes and a native-detail 64px text render with HSV on an owned 8K document. The PNG is saved under
`Temp/WhimTex/viewport`. GPU filtering tolerances are explicit; procedural edges compare visible
premultiplied values, raster cases also compare hidden straight RGB.

`canvas-viewport-8k` runs the ordinary 81px and 2048px brush curve at fit zoom and native zoom in
owned temporary windows. It requires window permission. Per-frame canvas dimensions accompany
CPU/GPU timings; the legacy 512px request is not the actual viewport size. Native-zoom cases also
check pan/rotation alignment, avoiding redundant requests and full-frame fallbacks after timing.
On 2026-10-18 D3D12, the test window used 512² at fit and 576² at native zoom, not 8192².
CPU median paint submission remained about 0.25–0.53 ms and preview submission changed from
1.18–1.28 ms to 1.28–1.38 ms; these are stage times, not presented FPS. Intermittent Editor/Pipeline
gaps and GPU interval variability remain in both controls. At this earlier stage, Half source/preview surfaces and sparse
stroke pages retained their previous sizes. This does not verify every backend or the excluded FX path.
The separate `canvas-viewport-8k-large` profile repeats the native-zoom cases in a 1600×1000 window.
Its 2026-10-18 visible render was 1216×960: median CPU paint submission was 0.47/0.29 ms for
81/2048px brushes and preview submission was 1.09/1.22 ms (p95 1.57/1.75 ms). These results
show no sustained slowdown in this workload; isolated Editor/GPU waits remain possible.

The subsequent regional brush-cache implementation was checked on the same 8K workload:

| View / brush | Brush cache before → after | CPU preview p50 before → after | GPU layer generation p50 before → after |
| --- | --- | --- | --- |
| Fit 512² / 81 px | 512 → 4 MiB | 1.282 → 1.229 ms | 0.071 → 0.035 ms |
| Fit 512² / 2048 px | 512 → 4 MiB | 1.367 → 1.197 ms | 0.244 → 0.053 ms |
| Native 1216×960 / 81 px | 512 → 17.81 MiB | 1.094 → 1.229 ms | 0.091 → 0.082 ms |
| Native 1216×960 / 2048 px | 512 → 17.81 MiB | 1.226 → 1.189 ms | 0.196 → 0.098 ms |

The smaller native window used a 576², 5.06 MiB cache. Six 8K traces passed; CPU stamp
submission medians stayed 0.26–0.51 ms. The 81px large-window CPU render increased by 0.135 ms
in this sample, while GPU generation decreased. These single-run comparisons do not establish
noise-free speedups or presented FPS; Editor/Pipeline gaps remain. Memory above is the cache's
logical payload: Unity's temporary pool may retain released textures, and the 512 MiB Half paint
source, stored pixels, stroke atlas and Undo are excluded. FX/full-input consumers keep the old path.

`canvas-viewport` additionally compares the regional brush output against independently CPU-filtered
full native texels on a non-power-of-two 1031×773 source crossing atlas pages. It covers signed HDR,
low alpha, soft masks, multiply/stamp blending, erasure, channel locks, Point/Bilinear filtering and
transforms. Dirty-cache updates match forced rebuilds exactly. Raw full-render comparisons remain
for ordinary cases; transformed extreme-HDR sampling uses the independent oracle to avoid treating
hardware interpolation-weight quantization as a seam. Half/output rounding tolerances are explicit.

`text-character-conversion-v1` checks independent glyph paint frames, original source immutability,
API conversion, shared FX/transform identity, owned Undo/Redo, JSON/native roundtrips, clipping,
empty/single-glyph groups, Boolean operand preservation and atomic layer-limit failure.
It uses detached models and owned transient FX, with a before/after preview under Temp/WhimTex.

The opt-in [codec and spill-policy comparison](Cases/Painting/CodecComparison.md) captures real
brush history blocks and compares lossless compression and bounded background history eviction.
It requires explicit native-library and temporary-asset approval; it never changes production painting.

`text-gradients` checks Text/Character coordinate oracles for every fill type and edge mode,
unchanged opaque coverage, paint-only cache reuse, the canvas-handle coordinate adapter,
API/JSON/native persistence, owned Undo/Redo, group/Target/thumbnail/export consumers and
missing-font paint snapshots. It also runs the shared figure-fill shader regressions.
The font catalog is temporarily isolated and restored; no user windows or assets are edited.

`source-tiling` checks intrinsic Source policies, explicit-mode priority and JSON roundtrips,
Reset defaults, File's independent U/V wraps, procedural Unbounded equivalence and Vector
cache-wrap/rebuild independence. It also runs geometric Clip, Feather, Shape/Vector parity
and persistence regressions. Models are detached; the current user document is not edited.

`brush-preview-cache` compares incremental live conversion against full-source conversion on
non-square, non-power-of-two images across disjoint dabs and partial edge tiles. It covers
nearly transparent paint, HDR, soft/mutable selection, stamp blending, erasure, channel locks,
wrapping, parameter changes, provisional reset and commit/cancel ownership. The existing
`brush-compute-correctness` profile also exercises the cached preview across all blend modes,
analytic checks, a frozen shader reference and document consumers, with compute and raster commits.

`brush-live-512-81-ui-current`, `brush-live-8192-81-ui-current` and
`brush-live-8192-2048-ui-current` diagnose movement **during** an ordinary stroke.
They own one detached document and temporary WhimTex window, submit all 360 points of a
near-full-canvas curve in order, and use a 60 Hz input target / 30 Hz canvas-update ceiling.
The explicit brush snapshot is hardness 0, spacing 0.225, flow/opacity 1, no pressure;
it does not read or overwrite saved brush preferences. Other controls use hardness 0.65,
spacing 0.15 and flow 0.35. Half storage and production commit-compute support are unchanged.
No stroke finalization, Undo extraction or full-size readback occurs inside the measured region.
The final proof image reads only a 512px preview after timing. Every window is closed on cleanup;
running an owned window still requires the user's permission where computer-control restrictions apply.

Controls include `brush-live-8192-81-full` (simple preview window), `-ui-stamps` (input/stamps
without preview updates), `-ui-direct` (direct document rendering), `-ui-raw` (direct segment input),
`-ui-convert` (persistent conversion target), `-ui-copy` (plain copy instead of preview assembly),
`-ui-unprofiled`, `-ui-warm` (15-second opening warm-up), `-ui-hidden`, `-ui-clear`, and `-ui-nouav`
(only the owned zero-filled control surface omits UAV support). These are diagnostic isolations,
not alternative production algorithms or visual-equivalence tests. `brush-live-state`
only reads open-document/brush metadata. Reports, raw captures and bounded PNGs live under
`Temp/WhimTex/brush-live-trace`; an existing stopped WhimTex capture is saved before replacement.

`brush-live-8192-81-ui-native` additionally uses documented native Editor profiler APIs, preserves
an existing stopped native capture, and restores CPU-recording settings. It records bounded
main/render-thread samples of at least 1 ms; this selection is not a complete call tree, and
inclusive entries overlap. Native capture changes overhead, so compare with the unprofiled control.
GPU samples are continuously drained per-Unity-frame totals with invocation counts; do not interpret
them as pure shader execution, sum nested intervals, or equate update cadence with presented FPS.
Editor idle throttling and Pipeline poll/compile pauses are included in raw intervals. In the
2026-10-17 D3D12 run, native samples identified roughly 98 ms `Application.Idle` periods and
264–540 ms Pipeline dispatch pauses; neither measures brush computation. Only native frame data
was used to classify those waits: optional raw native-marker recorder arrays can contain unavailable
or invalid cross-thread samples. The 8K display path and `WaitForLastPresentation` remain distinct
from the cheap CPU stamp path. Backend/driver causes of variable large-texture intervals are not
resolved by these tests. An in-stroke diagnostic PASS means the owned workload and cleanup completed,
not that frame-rate targets were achieved or a performance regression was fixed.

`brush-live-512-81-ui-post` and `brush-live-8192-81-ui-post` extend that fixture with
production finalization of the first stroke, then start a second after 100 ms. The second
stroke is cancelled after timing. `-ui-post-wait` waits for the first history transfer/writer
to finish before starting the second; it is a diagnostic control, not a production throttle.
The 8K `-ui-post-native` / `-ui-post-wait-native` variants retain public native CPU frames
of at least 8 ms through both strokes. `-ui-post-wait-noempty-native` additionally disposes
only the fixture's empty-layer detector before timing; user documents retain normal detection.
All post-stroke controls clear Undo only on their owned objects and drain their transfers before
destroying them. They require the same window permission and exclude any active user stroke.

Reports include transfer/staging/write queues, ordered frame timestamps, process CPU totals,
GC counts, and completed page worker timings. Working set is null when the runtime cannot report
it; allocated graphics bytes are not physical VRAM residency. Encoding time includes stream writes,
not just compression; overlapping workers must not be summed into elapsed UI latency.
On 2026-10-17, the 8K/81px curve touched 346 blocks: 435 MiB of GPU readback, 519 MiB of raw
history and about 28 MiB of files. Measured commit return was 619–842 ms, including nine staging
capacity waits. Native data identified a 230 ms readback-resource allocation during the second
stroke. Waiting for history alone did not eliminate the delay: the fixture performed five
full-resolution empty-layer checks, with 153–180 ms forced readback waits and a 1187 ms
presentation wait near second-stroke start. The single no-detector control reduced the observed
history tail from 3.82 s to 1.24 s and had no >20 ms presentation wait in the first 30 inputs of
the second stroke. These describe the diagnostic baseline before block alpha coverage.

After block alpha coverage and persistent stroke deferral were implemented, the same wait-history
control performed no full empty-content renders or forced readback waits: one map initialization
took 1.85 ms of measured GPU time. History tail was 1.14 s versus the earlier 3.82 s; commit return
was 613 ms. A separate immediate-second-stroke run started with 13 pending pages after 129 ms,
returned from commit in 684 ms and finished history 1.53 s later. No presentation wait above 20 ms
was observed in its first 30 second-stroke inputs. Both PNG previews were byte-identical to the
baseline. The wait-history run still had presentation waits up to 380 ms, and Pipeline polling/idle
pauses remain separate. These are individual 2026-10-17 RTX 5060/D3D12 observations, not a claim
that all 8K stalls or all backends are fixed.

`brush-alpha-coverage` compares each block flag and Empty/Present answer with independently rendered
full-resolution alpha on an odd 513 × 257 image. It covers separated occupied blocks, erasing the
last block, zero/soft selection, cancellation, clear/source replacement, Half cutoff boundaries,
non-finite alpha cleanup, repeated near-transparent strokes and storage-only dirty notifications.
`brush-buffered-compute-v1` initializes tracking before its blend/mask/HDR/reference comparisons;
the raster variant retains the unsupported-surface path. `layer-content-state-v1` additionally
checks reuse of exact FX output and rejection of reduced previews. `alpha-presence-v1` checks
asynchronous completion while idle, cancellation and resource ownership. The detector submits
pending GPU work through public `GL.Flush` without waiting for completion.

`paint-undo-disk-lifecycle` uses a GUID Temp store and separate session envelopes to check shared
limits, concurrent reservations, reload reopening, foreign-owner protection and scoped cleanup.
An owned headless Node child is terminated to verify cleanup after a real process exit; the user
Editor is never terminated. Unknown envelopes and reused process IDs are tested separately.
`paint-undo-retention` uses an isolated store and owned 513px SDR/HDR documents to check count
pruning, precise checkpoints, low-flow masked strokes, stale cursor rejection and new Undo after
forced clear. A foreign reservation fills the quota to prove rejection before any Half mutation.
Both restore the original store and metadata; no user settings or history are cleared.
They require `temp-files,user-state` and belong to the `paint-tile-history` profile.

`paint-undo-boundary` interleaves owned parameter changes and masked HDR strokes, including a
native group containing both. It verifies each Undo/Redo across the retained boundary without
empty pixel steps or skipped parameters, forced clear, and exact Half reconstruction/Undo while
a background checkpoint is being prepared.
`paint-undo-maintenance-8k` owns an isolated 8K document and store, makes 51 strokes (the first four
span the canvas perimeter), and lowers the count limit from 50 to 5. It records total completion
time separately from the longest main-thread maintenance call and its slowest measured stage;
`(GC)` marks a collection during that call. Reports belong to `Temp/WhimTex/paint-undo-maintenance`.
This is a synchronous diagnostic harness yielding between maintenance calls, not an interactive
frame-rate benchmark. Neither case opens a window or changes the user's document/settings.

The 2026-10-17 pre-change diagnostic measured 4,198 ms of blocking maintenance after stroke 51 and
40,363 ms for 50 → 5. Its CLI timed out, but the completed measurements and owned-fixture cleanup
were independently verified; it is timing evidence, not a runner PASS. After page-sized worker
compaction, incremental disk accounting and reuse of decode buffers/loaded paths, two final diagnostic
runs passed with longest main-thread calls of 14.3–15.1 ms at stroke 51 and 25.0–29.4 ms for 50 → 5.
Total background completion was 1.03–1.09 s / 8.66–8.73 s; writing the new limit took 6.57–6.85 ms. These are individual runs on
Unity 6000.7.0a6, RTX 5060, D3D12, not guaranteed maxima. Intermediate variants still incurred
150–285 ms collection-related spikes; that prompted buffer/path reuse before final checks.
The boundary, retention, disk lifecycle, small exact-history and 8K HDR perimeter suites passed;
the separate reload pair also passed after a confirmed native assembly reload (event observed and
old AppDomain marker gone). This does not claim Editor-process restart or other-platform coverage.

The `paint-undo-continuation` diagnostic profile checks drawing while count-limit cleanup is
already running: a small preflight, then paired 8K controls/cleanup with 81px and 2048px brushes.
It seeds 51 strokes across real Editor updates (four perimeter strokes), starts a checkpoint
worker, then draws strokes 52–55 along a near-full-canvas curve, rendering all 24 inputs per
stroke. A 50 ms inter-stroke target deliberately overlaps pending history transfers. The
control changes only its fixture's count-group identity after stroke 50; the pixel revision
chain, global limits and user settings remain unchanged. The shared transfer update callback
is temporarily timed and restored, and only this fixture's history files are removed on exit.
No window, user document or input device is controlled. Keep the Editor otherwise idle.

Reports under `Temp/WhimTex/paint-undo-continuation` separate preparation, painting, rendering,
1×1 GPU completion waits, transfer/maintenance updates and commit capacity waits. These are
per-input work measurements, not presented FPS. Raw intervals include Editor idle and Pipeline
poll compilation; polling is limited to once per five seconds to reduce that interference.
Main-thread housekeeping totals between inputs can include multiple update calls; the separate
maximum reports one call. Actual worker overlap is asserted. Three Undo/Redo operations verify
revision order and exact Half bytes in a bounded probe on the last stroke, with a check that
Undo really changes that probe. This does not verify every pixel of the 8K image.

On 2026-10-18, Unity 6000.7.0a6 / RTX 5060 / D3D12 completed the small case and paired 8K runs
in both orders. Movement medians were 5.8–6.1 ms for 81px and 6.7–6.9 ms for 2048px; paired
p95 values were 10.5–11.9 ms and 12.8–13.4 ms respectively. No sustained movement regression
from cleanup was observed, including the inputs that actually overlapped its worker. However,
the first measured input after starting cleanup waited about 48–53 ms for GPU completion versus
24–27 ms in the control. Later starts varied in both branches; this experiment does not resolve
the initial wait or prove all startup stalls absent. Preparation itself stayed below 0.3 ms.
The broad stroke still spent 435–636 ms in commit, also without cleanup. A repeat counted the
same 29/31/33/34 staging-capacity waits in both branches while queuing 830/860/900/935 MiB;
the bounded staging queue was near its 256 MiB cap. That commit backpressure is separate from
old-history compaction. Production drawing/retention code was not changed by this diagnostic.

The subsequent `paint-undo-startup-*` diagnostics isolate the first-input difference.
They use the same detached textured 8K fixture and 81px brush. `control` starts immediately;
`idle` defers the first measured input for at least 2.6 seconds without retention; `cleanup`
starts a real checkpoint worker; `cleanup-wait` waits for that worker before drawing.
The `*-pool` controls hold two initialized, matching Half temporary textures through priming
and release them immediately before the ordinary brush code borrows its targets. This adds
1 GiB of held 8K targets during test setup, not a production memory-policy change.
The `*-fenced` controls split completion before input, after the first dab, and after preview;
extra fences change scheduling, so compare the stages rather than treating them as normal FPS.

On 2026-10-18 / the same RTX 5060 and D3D12, observed first-input completion waits were:

| Condition | Wait (ms) |
| --- | ---: |
| Immediate control | 22.8 |
| Cleanup worker started after priming | 50.5 |
| No cleanup, 2.69 s idle | 57.5 |
| Cleanup worker already finished | 48.8 |
| Cleanup, two prepared targets retained | 12.0 |
| No cleanup, 2.85 s idle, two prepared targets retained | 11.1 |

With split fences, waiting before the first dab was only 0.24/0.30 ms; waiting after the dab
was 0.60/19.87 ms (control/cleanup), and waiting after the preview was 25.91/27.62 ms.
Main-thread submission remained below 0.4 ms per paint/render stage. This localizes the
additional startup cost to preparation/first use of the full-size stroke target, with an
additional first-preview cost in both paths. The idle and retained-target controls show that
an active checkpoint worker is not necessary to reproduce the large wait. Unity's temporary
pool may discard unused targets after a few frames ([GetTemporary contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.GetTemporary.html)).
Reallocation/initialization is the supported explanation; the test does not distinguish native
allocation, residency and clear execution inside the graphics driver. It does not prove zero
worker contention in every workload. Holding a gigabyte indefinitely is not the proposed fix.

All six final controls passed their 96 rendered inputs, three Undo/Redo revisions and exact
Half probe. The retained-target first wait also repeated at 11.9 ms in an earlier run whose
final drain timed out; that earlier run alone is not a correctness PASS. Two initial 512px
preflights stalled before measurements because detached asynchronous graphics commands did
not progress without a repaint. Setup and final drain now use a 1×1 completion fence while
still yielding between Editor updates; the measured input path is unchanged. The small
preflight and original control/cleanup passed after the setup fix. Reports remain under
`Temp/WhimTex/test-runs` and `paint-undo-continuation`; no production rendering code changed.
Sparse storage/pool candidates and precision constraints are recorded in
[F15.14](../Context~/Backlog/BRUSH_IMPROVEMENTS.md#sparse-paint-storage).

An earlier, discarded single-call synchronous probe crashed the Editor during the 8K control:
the D3D12 log reported failure to allocate an 8 MiB readback buffer (`8007000e`). Accumulation
of native resources without real frame boundaries was suspected, not proven as the complete
driver cause. The replacement above yields between inputs/strokes, limits process-memory
growth and duration, and persists phase progress. Do not restore the dense synchronous probe.

`paint-tile-history-small` uses detached documents to check sparse coverage, queued low-flow strokes,
exact Half/storage Undo/Redo, disk-only restoration, mixed native/model Undo, history branching,
surface reconstruction, non-power-of-two edge blocks and unchanged hidden RGB. It checks the same
sync boundary as Save and a TIFF container roundtrip in Temp, without importing an asset.
Coverage tracking is enabled; Undo/Redo must restore the correct Empty/Present answer as well.
`paint-tile-history-8k-small`, `paint-tile-history-8k-large`, `paint-tile-history-8k-frame` and
`paint-tile-history-8k-frame-hdr` each own one 8K document. They check fractional selection and
opacity, HDR where applicable, precise bounded Undo/Redo probes, and report transferred bytes and
wall timings under `Temp/WhimTex/paint-tile-history`. Run large cases separately. The only full-size
upload is fixture initialization; subsequent uploads/readbacks are bounded pages or tiles.
The HDR perimeter case passed again with tracked alpha coverage, fractional selection and exact
Half Undo/Redo; the ring still touched 124 of 1024 blocks.
The perimeter cases also clear their own history and verify exact Half reconstruction from the
remaining checkpoint, using only bounded probes on the 8K surface.
These cases require `temp-files,user-state` for their owned files and Undo objects; no windows/input.

`paint-tile-history-raw` adds full-image exact SDR/HDR checks on detailed 513px sources, including
1×1 edge blocks and corruption rejection before any pixel restoration. The paired explicit
`paint-tile-history-async-prepare` / `paint-tile-history-async-verify` checks Editor-update completion
without an explicit drain across three wide HDR strokes. Run prepare and verify as separate commands,
allowing several seconds of Editor updates between them; a single update no longer drains all pages.
`paint-tile-history-queued` checks six
overlapping 1536px strokes in SDR/HDR: the first three complete without CPU extraction or capacity
waits, the queued revisions stay within the shared staging budget, and immediate Undo/Redo restores
every complete Half/storage revision exactly, including masks, eraser and tiny flow.
`paint-tile-history-8k-textured` owns an opaque noise source to expose
compression cost hidden by blank-canvas benchmarks. `paint-history-codecs` compares the available
portable compression streams on bounded synthetic buffers. Large cases preserve a previous stopped
profiler capture to Temp before recording their own detailed phase profiles; an active capture is rejected.

The reload fixture includes a cleared-history checkpoint followed by a new stroke, checking both
the shared session path and exact Half/Undo/Redo after real assembly reload. A `recompile` reply of
`up_to_date` is not a reload: require an actual compilation/reload event before verification.

The two explicit `paint-tile-history-reload-prepare` / `paint-tile-history-reload-verify` entries
bracket an actual normal Unity script recompile. Preparation intentionally leaves one unique owned
document; verification checks exact stored/Half pixels and Undo/Redo, then cleans it up. Do not
perform other edits between them. The verify entry refuses Undo if the current group changed.
On 2026-10-17 these checks and the compute/raster suites passed on Unity 6000.7.0a6, RTX 5060,
D3D12, Linear. Other backends, asynchronous-readback-unavailable devices, disk-full behavior and
Editor-process restart are not verified by these runs; restart does not preserve the Undo session.

At 8K, the 32px perimeter uses 124/1024 blocks (12.1% of source pixels); a local 32px stroke uses
4 blocks, and the tested 2048px brush uses 100. Readback includes before/after Half plus straight
storage, not just the final 8-bit pixels: 5/155/125 MiB respectively, or 186 MiB for the HDR frame.
The 2026-10-17 phase-profiled optimization run measured stroke submission plus forced drain:
Standard frame 883 → 497 ms, HDR frame 1222 → 607 ms, and the 2048px brush 1054 → 774 ms.
Undo changed 1016 → 277 ms, 1362 → 422 ms and 927 → 254 ms respectively. These are individual
diagnostic runs on the hardware above, not steady-state medians or interactive frame latency.
GPU/GC/queue variance remains: earlier optimized runs measured 476/533/614 ms for the same
three completion scenarios. Local small-stroke values from the earlier pass are not remeasured here.

On the detailed 8K source, always compressing pages still took 4616 ms for submission plus drain;
adaptive raw storage reduced this to 396 ms, with Undo 707 → 175 ms. That comparison isolates the
adaptive storage change after the other optimizations. The raw journal used 150 MiB for 100 blocks;
the empty-source broad stroke used 7.3 MiB and the two perimeter journals 0.50/0.60 MiB. Stage
profiles identify readback waits, CPU extraction/patching, write backpressure, validation, decode,
and bounded texture uploads separately. They measure main-thread wall time, including queued GPU
work; they are not GPU timestamp measurements. Those measurements predate deferred staging.
Integrity-failure checks, exact stored/Half restoration, both compute and raster suites passed.

The subsequent deferred-staging run measured return from a stroke separately from forced drain:
2048px SDR brush 640 → 12 ms; HDR perimeter 505 → 40 ms. Both queued all their pages without CPU
extraction or capacity waits (125/186 MiB owned GPU staging). Immediate forced drains still took
1023/907 ms, so this improves responsiveness, not total transfer throughput. These remain individual
diagnostic runs, not interactive FPS or guaranteed worst-case bounds. A single-block page now uses
256×256 instead of 1024×256; exact tests cover one-, two- and three-column pages. Full completion
can still block on queue capacity, GPU pressure, fallback reads, Undo, saving or resource release.

`drawing-readback-format-accuracy` compares the retained Half-staging readback with
matching storage staging: alpha/color ramps, a float conversion reference, signed HDR,
and 240 low-flow masked strokes/erasures. It checks exact working-surface preservation,
production/diagnostic agreement, at most one 8-bit storage step of difference, and GPU state.
`drawing-readback-format-512` is a bounded paired timing smoke. The separate
`drawing-readback-format-8192`, `drawing-readback-half-8192` and
`drawing-readback-format-hdr-8192` perform **one** transfer/upload per Editor command,
with detached fixtures and unique reports in `Temp/WhimTex/drawing-readback-format`.
Allow Editor updates between large samples. Never batch repeated full-size `Apply` calls
in one synchronous command: the first 8K series on D3D12 retained 4 GiB in the frame's
release queue and failed allocating another 256 MiB upload scratch resource. That series
is invalid; the revised one-transfer entries avoid that test-only accumulation.
All these cases require `temp-files`, and never access user documents, Undo or windows.
On 2026-10-17, Unity 6000.7.0a6 / RTX 5060 / D3D12 / Linear completed the revised
cases: three independent 8K standard samples per path gave median ReadPixels wall
latency 723.17 → 209.68 ms (ranges 667.76–734.27 / 183.37–337.98 ms). These include
queued GPU work and fresh fixture preparation dependencies; they are not interactive
latency guarantees. The 743-check accuracy case kept every working Half surface
bit-identical; stored Standard bytes differed by at most one code value, including
alpha crossing 0↔1/255. HDR bytes were exact. Compute/raster regressions passed
857784 / 857783 checks. Other backends and Gamma color space remain unverified.

`brush-buffered-stroke-v1` checks the immutable paint source, soft selection applied once,
all 29 blend modes in Stamp/Stroke with Standard/HDR, independent blend arithmetic,
Flow/Opacity/pressure, erasing, cancellation/commit, 8-bit and signed HDR storage,
projective/wrapped canvas-space masks and caller GPU state. Full-image reference comparisons cover
dirty regions, tiled commit seams, non-square partial tiles and the portable shader-copy fallback.
It owns only transient GPU/model
fixtures. `brush-dynamics-v2`, `brush-hlsl-v2` and `brush-write-protection` add pressure curves,
tip variants, first-stamp correction, group/Target/clipping/thumbnail/export and channel locks.
These brush cases do not open windows, drive input or restore window focus.

The separate opt-in `brush-pipeline-benchmark-smoke` and
`brush-pipeline-benchmark` profiles compare the previous conditional-buffer route
with the current unified route at 512 through 8192 pixels, including large brushes,
selection, opacity, blend modes, pressure, channel locks and HDR. Read the
[baseline provenance and measurement limits](Framework/Painting/BrushBaseline/README.md)
before interpreting the output. They require `temp-files`, own only detached GPU
fixtures, and write unique raw timing/memory reports under project
`Temp/WhimTex/brush-pipeline-benchmark`.

The initial 2026-10-17 check, before region optimization, used RTX 5060 / D3D12 / Unity 6000.7.0a6 / Linear.
The corrected diagnostic passed eight smoke cases, two full 20-case series and
four separate 8K repeats. At 4K with a 2048-pixel brush, the second series gave
these whole-stroke medians (16 preview updates, milliseconds): Normal 5.78 → 7.75,
fractional selection 6.06 → 11.02, HDR 5.92 → 9.49, Opacity 8.95 → 8.04,
pressure 9.92 → 8.76, Stamp blend 11.00 → 9.98. This is not a universal speedup.
One full-size RGBAHalf buffer costs 128 MiB at 4K and 512 MiB at 8K: the simple
path adds one while the former buffered path removes one during the stroke;
the commit scratch means peak savings depend on the mode.

8K Opacity/Stroke blend consistently improved to about 25/27 ms from 38/40 ms,
while HDR changed from about 20 to 31 ms. Normal was unstable across series:
an isolated repeat gave about 19.5 → 24.3 ms, but full runs showed new-path spikes
up to 929 ms. The role of resource reuse/residency is not established; neither
those spikes nor a universal warm-speed claim should be hidden in an average.
Interactive frame pacing, other GPUs/backends and full-document FX/Undo/storage
were not benchmarked. The initial full-size-source ReadPixels diagnostic caused
a native readback allocation failure; it was replaced with a real 1x1 readback RT,
and its earlier reports are excluded. See the baseline README for the incident
and the remaining limits.

The subsequent region implementation limits commit scratch to 1024×1024 RGBAHalf (8 MiB)
and copies compact padded stamp backdrops. `brush-region-benchmark-smoke` and
`brush-region-benchmark` compare full assembly with this path; both sides share the current
stamp renderer. See [reference provenance and methodology](Framework/Painting/BrushFullAssembly/README.md).
`brush-isolated-smoke` checks 512-pixel canvases; `brush-isolated-current` selects separate
8K invocations. Prefer individual IDs after the smoke test. These use synchronous calls,
two warmups and nine timed strokes, without async carriers or Editor-frame delays.

The resumed 2026-10-17 synchronous series passed all eight smoke modes, eight 4K/2048
cases, nine 8K cases (256/2048 brushes), two full-assembly reference cases and three
current-path repeats. Current 8K/2048 medians were 23.74–29.12 ms per 16-update stroke;
the largest sample was 55.64 ms in an HDR repeat. In the same Editor session, full assembly
gave Normal 118.08 ms (max 805.58) and Stamp 109.70 ms (max 960.30); current repeats after
those reference runs gave 23.48 ms (max 24.04) and 27.99 ms (max 30.96). Commit diagnostics
fell from about 97–105 ms to 1.7 ms for those two cases. Phase barriers change timing;
these values are not interactive frame rates or a universal speed ratio.

Commit scratch at 8K fell from 512 to at most 8 MiB. For this fixed 2048-pixel stamp,
backdrop capacity is conservatively 36.125 MiB instead of 512 MiB. Full-size source/stroke
and preview memory remain; neither figure is total physical VRAM. Full-assembly stalls
were reproduced while the current path completed without them, but exact driver residency
causality remains unmeasured. Earlier async timeouts are excluded from this accepted series;
their ownership was recovered before the restart and synchronous checks. Correctness and
source/scalar tests passed, cleanup completed, and the runner lock was released. Raw captures
and the accepted manifest are under project `Temp/WhimTex/brush-pipeline-benchmark`.

The compute-commit trial (2026-10-17) adds `brush-compute-correctness`: forced compute
and actual non-UAV raster fallback run the same suite, including all 16 write masks at
zero/tiny alpha, full-image non-square bounds, group/Target/clipping, thumbnails,
PNG roundtrip and EXR encoding. Final checks passed 857784 / 857783 assertions respectively.
The final shader compiled without new diagnostics. No windows or user documents are used.

Opt-in `brush-compute-512-128`, `brush-compute-4096-2048`, `brush-compute-8192-2048`
and the `*-256` profiles compare **current tiled raster versus compute final commit**.
They require `temp-files`; the 8K cases require at least 7000 MB reported graphics memory.
Use the small profile first. Both paths share current accumulation and preview code; two
warmups precede nine alternating whole-stroke pairs and nine separately fenced phase pairs.
The fixture retains one extra idle control surface for allocation-free A/B switching.
Completion uses a real 1×1 readback target, never a full-size staging readback.

On RTX 5060 / D3D12 / Linear, final Normal commit medians were 0.803 → 0.595 ms at
4K/2048 and 1.826 → 1.230 ms at 8K/2048. Whole 16-update strokes changed
7.620 → 7.409 and 22.650 → 22.060 ms respectively. Final 8K selection repeat:
2.101 → 1.365 ms commit, 26.077 → 25.292 ms whole stroke. Committed probes matched
exactly; the full correctness suite separately compares pixels with half-precision tolerances.
Compute removes up to 8 MiB of commit scratch. Full-size source/stroke and sequential-stamp
backdrops remain; their memory is not eliminated by compute.

Keep the outliers: an earlier 8K/256 compute run reached 258.332 ms, with a separate
phase sample spending 195.30 ms in drawing and 0.605 ms in commit. One 8K selection
raster series reached 228.411 ms. Subsequent repeats completed normally, but do not
establish the scheduling/residency cause or universally smooth input. Raw captures and
`compute-commit-report-2026-10-17.md` under project `Temp/WhimTex/brush-pipeline-benchmark`
retain all runs. Other GPUs/backends, actual unsupported hardware, Gamma mode and
interactive input latency remain unmeasured; unsupported-path behavior is tested with
non-UAV surfaces on the current device.

### Brush spike diagnosis

`brush-trace-smoke` and opt-in `brush-trace-8k` run 32 detached strokes per case.
Every setup, update, preview and commit records main-thread submission separately
from the completion wait, with GC collection counts and monotonic timestamps.
These are wall times, not GPU timestamps. Fenced traces serialize every stage;
streamed traces fence only stroke boundaries. Neither is interactive frame pacing.
The `brush-trace-control-*` cases isolate readback, preview, mesh updates and source
bindings; their output is a diagnostic control, not a pixel-equivalence reference.
`brush-trace-warm-*` retains two consecutive 32-stroke traces on the same resources.
Only the paired controls retain an extra idle source surface.

`brush-native-markers-start` / `brush-native-markers-stop` bracket a separately
selected trace with supported public CPU recorders. They leave `Profiler.enabled`
unchanged, refuse an existing Unity CPU capture, and release their own recorders
on Stop or after a 90-second deadline. Native summaries aggregate per frame;
trace deltas use available current-frame counters. Missing markers are not evidence
of zero work. No OS tracing or native plug-in is used by these cases.

The 2026-10-17 investigation on RTX 5060 / D3D12 reproduced 12–31 ms completion
waits after stamp submission, whose main-thread work was around 0.1 ms. They also
occurred without preview and with a different completion mechanism. Dropping the
extra A/B surface did not remove them. A fixed-mesh control and source-binding
controls motivated removing the unused full-size source binding from ordinary
stamp accumulation. Subsequent small-brush repeats were smoother, but this does
not prove that binding caused every previous outlier. Large-brush selection still
showed a first-series outlier; consecutive repeats on the same resources did not.
One separate 8–10 ms main-thread outlier coincided with GC in the diagnostic
session; it must not be attributed to the shader.

Public native markers distinguish cold texture creation/upload/scratch reservation
from the measured warm strokes. They do not establish the driver scheduling or
residency cause of the remaining intermittent waits. The historical 195–258 ms
events were not reproduced by these traces; their exact cause remains open.
Compute remains enabled. Both compute and raster correctness suites passed after
the binding change (857784 / 857783 checks). See the local
`Temp/WhimTex/brush-pipeline-benchmark/spike-investigation-2026-10-17.md` for raw
captures, instrumentation limitations and the unresolved native GPU diagnosis.

`vector-tools-brush-lines-v1` checks Circle/Square/Diamond cursor selection,
Shift axis locking at two canvas rotations/zooms, modifier transitions, endpoint
connection, per-stroke mask ownership and Undo/Redo. It owns a transient window
and requires `windows,user-state`. `vector-brush-v1` also compares tip coverage
against analytic shapes and sparse/dense tapered strokes, without a window.

`vector-tools-brush-editing-v1` checks repeated LMB additions, RMB holes/full erase,
Enter, toolbar/Properties Finish Editing and Edit Mask, selection changes, cancellation,
immutable source snapshots and native Undo/Redo. It captures its owned window under
`Temp/WhimTex/vector-mask-editing.png` and requires `windows,user-state,temp-files`.
`vector-brush-v1` additionally checks mask growth without resampling, bounded storage,
all three eraser tips and non-accumulating AA coverage across repeated samples.

The 2026-10-17 resumed check passed `vector-brush-v1`, `vector-tools-brush-editing-v1`,
`vector-tools-brush-lines-v1`, `vector-tools-brush-v1` and `vector-tools-cancel-v1`
in Unity 6000.7.0a6; owned cleanup completed. The empty-window fixture sets its
viewport directly before the first stroke because navigation commands require a
rendered image. This run does not establish tip/cursor agreement on manually
scaled or projectively deformed mask figures.

A later cursor regression check reproduced a hidden overlay despite a true cursor
visibility flag. `vector-tools-brush-v1` now checks the displayed overlay after canvas
refresh and during drawing; `vector-tools-brush-editing-v1` additionally compares
owned-window pixels with the cursor hidden, hovering, drawing and erasing. The drawing
comparison retains the identical held-stroke preview, so its fill cannot count as the
cursor. Both updated cases passed on 2026-10-17 in Unity 6000.7.0a6, including owned
cleanup, after the shared-overlay visibility fix. They use `windows,user-state,temp-files`;
captures under `Temp/WhimTex/vector-cursor-*.png` are bounded to the owned 1280x820
window, not the user's document texture. The earlier five-case pass predates these pixel
assertions. No test runner lock remains.

### Vector prototype

The `vector-feather` profile checks Inside/Outside/Centered coverage against an
independent edge profile, tiny widths, holes, dashed strokes, CPU/GPU parity,
transforms, frame rebasing, clipping, Target input, thumbnails and export-sized
rendering. It also checks native Undo/Redo, API patches and JSON/TIFF persistence.
It uses detached documents; declared effects are `temp-files,user-state` for a
comparison PNG and an owned Undo group. No user windows or assets are modified.

`vector-clipper-reuse-v1` compares scoped reuse with fresh Clipper instances,
including exact contour order/seams, mixed operations and fill rules, retained
outputs, nested scopes, reentrant isolation, workload exceptions and scope exit.
It owns detached polygon data and does not retain engines between builds.

`vector-prepared-geometry-v1` compares direct prepared contours with the original
normalized-anchor route, including float round trips, duplicate points, contour
seams, original input limits, snapshot ownership and warmed geometry replacement.
Extreme-coordinate cancellation retains the original flattening path and is
checked against both scalar and Jobs preparation.
`vector-prepared-fill-v1`, `vector-prepared-stroke-v1` and
`vector-prepared-dashes-v1` each cover 96 exact CPU/coverage comparisons and eight
GPU pixel comparisons. Cases include holes, fractional and negative coordinates,
degenerate contours, ordered figures, AA/Step, clipping, projective/reflected
transforms and every stroke position/join/cap combination. All four cases passed
on 2026-10-16; the five new regression IDs are included in the `vector` profile.

`vector-prepared-before-v1` / `vector-prepared-after-v1` measure the scoped-engine
and direct Boolean-contour changes. They use the same six cold fixtures and frozen
RGBAFloat references as below, writing immutable `prepared-before` / `prepared-after`
reports under `Temp/WhimTex/vector-clipper-v1`. Nine fresh uncached samples exclude
two engine warmups; compile/JIT time is not part of the cold geometry metric.

Measured 2026-10-16 in the same Editor, completed median milliseconds including
GPU readback (all six exact pixel deltas were zero):

| Fixture | Before | After |
| --- | ---: | ---: |
| Vector 512 | 74.15 | 72.89 |
| Boolean 4, styled/dashed outline, 512 | 196.40 | 190.52 |
| Boolean 16, fill, 512 | 188.54 | 158.23 |
| Vector 1024 | 113.49 | 119.80 |
| Boolean 4, styled/dashed outline, 1024 | 291.78 | 294.88 |
| Boolean 16, fill, 1024 | 253.83 | 228.96 |

The 16-operand cases improved about 10–16%; the small differences in the other
cases are inconclusive in this capture and must not be advertised as a universal
speedup. Resolver call counts remain 4/17/62; no additional Union passes were
removed. Separate phase measurements show input preparation for the 16-operand
cases dropping from 11.97/11.11 to 4.30/5.95 ms. Phase medians come from separate
renders and must not be summed as an exact decomposition of whole-render medians.

`vector-clipper-paired-1-v1` through `vector-clipper-paired-4-v1` investigate timing
variation without editing production code. Each independent block alternates AB/BA
renders of new Vector documents: 16 pairs at 1024 and eight at 512, plus separate
phase samples. The control uses the wrapper's existing reentrant-isolation path to
create a fresh Clipper per operation; it is not a historical binary, and current
validation/final Clear remain enabled. The direct Boolean-contour path is unused.
All temporary scope state is restored synchronously; profiler capture and user
documents are untouched. Raw samples, GC collection deltas and CPU/readback times
are retained in unique `Temp/WhimTex/diagnostics/vector-clipper-paired-<GUID>` folders.

The 2026-10-16 repeat produced 64 samples per mode at 1024: fresh-engine/scoped
medians were 122.77/117.97 ms completed and 104.78/99.09 ms CPU submission. Scoped
completed medians by block were 117.44, 117.64, 119.93 and 117.36 ms, versus fresh
117.87, 126.08, 116.56 and 126.49 ms. At 512, 32 samples per mode gave 77.81/75.51 ms.
No consistent slowdown was observed; the broad tails preclude a universal speedup
claim. The earlier 1024 CPU submission medians were already 103.12/102.76 ms:
the apparent +5.6% was in the GPU-completed metric, not an observed CPU regression.
All 32 frozen-reference pixel comparisons were exact. The initial diagnostic
attempt lost its nested sample array during serialization and is excluded from
these statistics; subsequent runs preserve all samples with managed serialization.

`vector-union-fill-v1`, `vector-union-stroke-v1` and `vector-union-dashes-v1`
separately compare rendered pixels against the original two-pass normalization:
48 fill and 216 cases for each stroke mode. Inputs cover holes, shared edges,
point touches, duplicate contours, self-intersections, acute stars and random
contours, plus compound inputs with mixed fill rules. Stroke variants cover all
positions, joins and caps, three widths, AA/Step, relative/absolute and odd dash
patterns with negative offset. The retained touching-partition counterexample
must have identical fill but differing centered stroke/dash pixels when Union is
blindly skipped. An isolated counter asserts that the exact fixed-point fast path
actually skips one Clipper call. Owned previews go to
`Temp/WhimTex/vector-union-appearance`; no user document or capture is changed.

`vector-union-before-v1` and `vector-union-after-v1` repeat the six cold fixtures
below as `union-before` / `union-after`, with the Burst budget guard already in
both versions. Original RGBAFloat references are reused read-only; timings are
new immutable reports. Vector-only fixtures are controls: their path is unchanged,
so their timing variation must not be attributed to the Boolean optimization.

`vector-clipper-regions-v1` retains the previous Boolean-normalization sequence as
an independent oracle over unchanged Clipper2. It compares exact oriented vertices
up to seam rotation and path ordering on 1,200 cases, including touching/self-crossing
contours, all four Boolean operations and mixed fill rules. It retains a counterexample
to skipping normalization of prior Clipper results. `vector-clipper-budgets-v1`
compares exact visit counts and rejections to the old active-list sweep, including both sides of
the four-million-visit limit. Both use test-owned geometry; the production budget
check uses short-lived native arrays and an integer-only Burst job.

`vector-clipper-before-v1`, `vector-clipper-before-timings-v1` and
`vector-clipper-after-v1` are opt-in diagnostics under
`Temp/WhimTex/vector-clipper-v1`. Before phases require the original wrapper and must
never be used to regenerate goldens after optimization. The first capture retained
whole-render timings and pixels; `before-timings` adds complete structured phase
data without replacing those captures. The after entry writes `after-burst` reports
(`after` and `after-final` retain intermediate managed candidates)
and compares to the frozen RGBAFloat pixels
exactly. Each phase uses the same six fixtures as the cold benchmark: nine fresh
uncached documents after two engine warmups. Three additional fresh renders measure
the stage breakdown separately, excluding timing-sink overhead from whole-render
medians. Counts must agree across those three renders. Parent timings are inclusive.
The scoped sink and instrumentation suppression preserve user profiler captures;
no open document, permanent asset or profiler baseline is replaced.

`vector-preparation-flatten-v1` compares Jobs/Burst cubic subdivision against an
independent scalar reference: exact points and order, seams, tolerances, degenerate
curves and repeated execution. `vector-preparation-coverage-aa-v1` and
`vector-preparation-coverage-step-v1` compare masks to scalar scan conversion,
including holes, narrow features, all joins/caps/positions, dashes, transforms,
clipping and overlapping translucent figures. Fill/stroke coverage is exact;
intersection reconstruction and GPU paint allow only their stated floating-point
rounding tolerances. These cases use owned transient data and restore render state.
`vector-preparation-coverage-budgets-v1` checks pre-dispatch work limits, accounting
for previous figures, fractional edge spans and crop bounds. Oversized synthetic
inputs cannot reach dispatch; exact-limit positives use only six pixels.

`vector-cold-benchmark-v1` is an opt-in diagnostic. It requires the six frozen
pre-change RGBAFloat references in `Temp/WhimTex/vector-cold-jobs-v1` and enabled
Burst. It never regenerates those references during comparison, and refuses to
overwrite `after-final` reports. Each of nine measured samples creates a fresh
document, so geometry, Boolean results and coverage start uncached. Two engine
warmups are excluded; this does not measure script/Burst compilation. It records
CPU submission separately from completion including GPU readback, not UI FPS.
Cases cover 512/1024 curved Vector, four-operand Boolean with a styled outline,
and sixteen-operand Boolean fill. Exact raw-pixel differences are reported against
the pre-change captures; inspect the retained PNGs as well.

`vector-paint-cache-v1` checks retained coverage across fill/stroke paint edits,
geometry invalidation, Boolean-result reuse, translucent overlap and recovery after
the owned paint RenderTexture loses storage. It uses transient objects and checks
colors against an independent CPU calculation, without changing the open document.
`vector-paint-benchmark-v1` is an opt-in diagnostic requiring the frozen pre-change
reference in `Temp/WhimTex/vector-paint-benchmark-v2`; it does not create a baseline
from the current renderer. Timings distinguish CPU submission from completion with
GPU readback and are not whole-editor interaction FPS.

`vector` selects geometry, native rasterization/FX/Target integration, mask-brush
rebuilds, JSON/native-TIFF persistence, Shape conversion and 94 Shape/Vector pixel
comparisons, including shared styled outlines. `shape-strokes-v1` also checks relative
dashes, Feather, transform width, clipping/Target, thumbnails and export.
It uses owned transient data only. `vector-tools` selects nine functional
UI scenarios: figure/node transforms, Pen, Brush, cancellation/capture loss, stale
layer removal, window focus loss, Position/Join/Cap icon controls and dash editing. It needs `--allow-effects windows,user-state`;
all gestures run in owned utility windows and restore preferences/focus afterward.
Native Undo/Redo is exercised; no user document is used. These are functional checks,
not pixel-perfect UI goldens or a restart/reload acceptance test.
`boolean-group-mode-v1` checks the five group Mode icon buttons, Combine appearance selection, uniform/mixed state,
empty groups, native Undo/Redo, repeated clicks and unchanged nested operations.
It requires `windows,user-state,temp-files` and captures the owned window to
`Temp/WhimTex/boolean-group-mode.png` for visual inspection.
`vector-stroke-style-v1` checks all nine icon buttons, selected-state synchronization,
Undo/Redo, repeated-choice no-op, disabled Stroke and retained brush source.
The separate `vector-stroke-style-preview-v1` also requires `temp-files` and writes
the actual owned inspector to `Temp/WhimTex/vector-stroke-style-preview.png` for visual review.
`vector-dashes-v1` checks text/pair/preset synchronization, odd cycles, Enter/blur/Escape,
invalid input, Undo/Redo, cap/width/offset preview updates and disabled Stroke.
`vector-dashes-preview-v1` requires `temp-files` and captures the controls and preset
gallery to `Temp/WhimTex/vector-dashes-editor.png` and `vector-dashes-presets.png`.
These are actual UI captures for inspection, not pixel-golden comparisons.
`shape-stroke-ui-v1` requires `windows,user-state,temp-files` and checks the same
shared controls on Shape, native Undo/Redo and preserved Feather/Transform settings.
Its actual inspector/canvas capture is `Temp/WhimTex/shape-strokes-editor.png`.
`stroke-seams-v1` requires `temp-files`: it checks 1,080 round capsules and 621
round joins for artificial interior boundaries, not just covered area. The shared
Shape/Vector star preview is written to `Temp/WhimTex/stroke-seams.png`.
`stroke-alignment-v1` requires `temp-files`: it checks full round dots/caps for both
offset sides, three widths, two dash lengths and all joins, plus holes/reversed winding,
collapsed insets, split narrow necks and unchanged open paths. Its actual render is
`Temp/WhimTex/stroke-alignment.png`: top Inside, bottom Outside; each pair is Shape
then Vector, with dots on the left and round-ended dashes on the right. The parity
matrix includes 24 offset-dot/dash comparisons on rectangles and stars.

The separate `vector-render-preview-v1` diagnostic requires `--allow-effects temp-files`
and writes actual composition pixels to project `Temp/WhimTex/vector-render-preview.png`.
Inspect that image; the existence of the PNG alone is not a visual-quality verdict.
`vector-brush-preview-v1` uses the same effects and renders two cubic fits of the
same saved mask, with control handles, to `Temp/WhimTex/vector-brush-curves-preview.png`.
Left: 0.25 reference-pixel tolerance; right: 4. `vector-brush-v1` checks smooth
closed joins, cubic density, reversible precision, unchanged mask bytes, holes and
narrow features. `vector-tools-brush-v1` also checks precision Undo/Redo through Properties.
`document-json-schema-v2` covers the additive frozen Vector defaults and source-mask
metadata. Schema validation does not decode/validate mask byte counts; native
`vector-persistence-v1` checks those rejection paths.

### Runner selection

`figure-gradient-canvas-v1` exercises shared handles on all four Shape/Vector/Boolean Group palette gradient fills:
anchor movement under parent transforms, unchanged figure transforms, angular range/Ctrl,
palette keys, Undo/Escape and Boolean appearance gating. It requires
`windows,user-state,temp-files`; actual Shape/Vector/Boolean Group canvas previews remain in a GUID diagnostic directory.
It also checks every Boolean mode and Combine Group/Layers handle availability, preserving child geometry and appearance.
`figure-fills-v1` checks CPU/GPU angular repetitions and palette wrapping;
`figure-fill-persistence-v1` checks repetitions in JSON, API, conversion, native payload and TIFF.

`gradient-angular-canvas-v1` checks split endpoint colors, both opening directions,
closing, rotation/translation, color keys/midpoints, Undo/Redo, Escape, mirrored hit testing
and JSON roundtrips in an owned window. It requires `windows,user-state,temp-files` and
retains closed/open canvas captures under `Temp/WhimTex/diagnostics/<GUID>/`.

From the package root:

```sh
node Tests~/scripts/run-tests.mjs --list
node Tests~/scripts/run-tests.mjs --review --profile core
```

Read selected sources and declared dependencies in full. Copy the current fingerprint, then run:

```sh
node Tests~/scripts/run-tests.mjs --run --profile core --reviewed "<fingerprint>" --project-path "<project-root>"
```

Choose **one** selector. None expands into unrelated scenarios or runs inferred dependencies:

| Selector | Scope |
| --- | --- |
| `--id canonical-reader-v2` | One scenario |
| `--ids canonical-reader-v2,display-channels-v2` | Exact list, in this order |
| `--group rendering` | Scenarios with this explicit group |
| `--profile quick` | Reviewed named subset |
| `--profile node` | Independent read-only Node tests; no Unity |
| `--profile documents` | Document/import cases and source guards, diagnostics excluded |
| `--profile regressions` | Only regression scenarios |
| `--profile source-guards` | Only source/contract guards |
| `--profile verify` | All non-diagnostic scenarios, including runner infrastructure |
| `--all` | Every **catalogued** scenario, including diagnostics and permissioned workflows |

`quick` checks the runner, 0.12.5 files and all registered references without Unity. `core` has two Unity regressions
and the frozen-file Node regression. `compatibility` is a small subset, **not** the full file
roundtrip suite. `async-protocol` checks Start/Poll/Cleanup using owned SessionState keys, not UI
scheduling.
Feature profiles such as `documents`, `layers`, `fx` and `painting` exclude diagnostics;
`diagnostics` selects them explicitly. The independent `runner-live-v2` tests actual Editor
verdicts and cleanup. Diagnostics are not silently counted as passing regressions.

Gradient reload is one paired lifecycle: select `--id whimtex-gradient-reload-v2` to execute
Begin → native Trigger → ReloadPoll → End → Cleanup with the same GUID. The two remaining
standalone `whimtex-gradient-reload-begin-v2` / `-end-v2` entries are historical helper mappings,
not independently runnable coverage: generic cleanup destroys Begin's fixture, and selecting
both entries does not insert a real reload. Use the complete paired workflow for coverage.

`comment-editing-v1` checks local draft transitions and reload snapshots, content conflicts
independent of author colors, atomic message edits, exact-link and text queries, feedback
revisions with mask replies, retained row ordering and connector contact geometry around
rounded card corners. It uses an owned in-memory document
and detached UI elements, without windows, input, focus changes or global Undo.

The `comments` profile checks optional TIFF comment storage and canvas geometry in memory,
then exercises the Comment tool in an owned temporary window. `comment-author-v1` uses injected
organization/email providers to check organization priority, email fallback, persistence, retries, manual overrides and the settings
field without changing the Unity account. It also checks deterministic hue, unchanged HSV saturation/value,
custom colors, profile ownership across nickname changes and manual overrides. It isolates and restores
author, profile and color preferences. The UI case checks post/reply authors, retained authorship during
edits, automatic draft/posted colors, manual colors and reset controls. Review all declared sources
and allow `assets,windows,user-state,temp-files` for the whole profile (the file case owns its GUID TIFF fixture). It tests only its own Undo records, restores tool
preferences and focus, and writes UI previews to `Temp/WhimTex/comment-tool-preview.png`,
`comment-list-preview.png` and `comment-list-narrow-preview.png` in the same directory.
The UI case also covers shared Open/Resolved/All filters and full-message search, preserved per-thread drafts,
permanent numbering, reply deletion/Undo, edited timestamps, floating card move/resize/connector geometry,
simultaneous cards with independent drafts/attachments/actions, focus stacking, per-card close/Escape,
outside-click disposal of untouched new drafts while retaining configured drafts and published discussions,
Close All in the list header with filtered cards, retained reply/new/edit drafts, masks, empty slots and manual color,
automatic placement after closing via the card, Escape or Close All while retaining the chosen size,
filter/tool restoration and individual connectors (`Temp/WhimTex/comment-multiple-preview.png`),
the shared comments drawer, colored row stripes, resolved rows,
virtualization, scrolling, short-window layout, attachment editing and drops, read-only posted references,
and asset/layer ping without selection changes. Attachment UI captures use Temp/WhimTex/comment-attachments-preview.png.
The Comment UI case polls every 5 seconds to reduce Pipeline compilation overhead.
Day separators cover same-day messages, a local-midnight boundary, later edits and repeated refreshes without document changes; the card render is saved to `Temp/WhimTex/comment-days-preview.png`.
Ping phase checks pause only the owned window's scheduled animation and advance its production
update with explicit elapsed times. Each phase is checked after UI layout/style updates: scale,
hold, outer border, stacking, fade and cleanup, including a frame delayed beyond the lifetime.
A separate ping uses the live scheduler and must finish automatically. No short wall-clock phase
needs to coincide with a test continuation; production animation timing is unchanged.
Selection-mask coverage includes lossless TIFF/JSON/Undo roundtrips, soft edges and holes,
crop/expand/scale/orientation, malformed inputs and decoded-pixel budgets. Agent cases check
selection/PNG/reference attachments, exact mask export, strict completion, unchanged working
selection and stale-mask guards. UI cases check attach, preview, Use, draft-only replacement,
cancellation and texture cleanup; the rendered card/overlay is saved to `Temp/WhimTex/comment-mask-preview.png`.
Show on Canvas and list double-click checks cover preserved zoom/rotation/drafts, marker highlighting,
off-canvas anchors and keeping the card clear of the marker beside a full-height list.
`comment-workflow-v1` checks combined context exports, independent application coverage, expansion
and feather, exact preservation of source pixels outside coverage, detached review images,
separate-mask/fork guards and serialized recovery conflicts/cancellation/corrupt data.
It also reproduces a completed masked edit followed by a reply with layer, asset, review-mask and
copied-mask attachments: feedbackRevision must survive refresh, Undo/Redo, retry and JSON roundtrip.
Editing the reply's mask or explicitly changing status must still invalidate feedback. Thread-wide
Unity managed-reference tables must not leak excluded agent masks into this revision.
`comment-navigation-v1` covers participant/date/sort controls, invalid ranges, zoom/overlap clustering,
native chooser selection, API query boundaries and temporary link navigation through excluding filters.
It also checks text/author substrings and full case-sensitive link search in the UI and API, surrounding
whitespace, rejected partial/altered/normalized/other-document links, retained filters and unchanged data.
Marker gestures cover closed-card dragging without opening, returning to the drag origin, click jitter,
Undo/Redo and preservation of an already open card and its draft.
Status choices cover capture on the initiating field and press/drag/release through the source window's mouse event path, ordinary
two-click selection, release outside options, Escape and keyboard submit; each selection commits once.
Its render is `Temp/WhimTex/comment-navigation-preview.png`.
`comment-file-v1` creates a GUID-owned TIFF fixture and exercises Headless preview/context/complete,
all attachment kinds, exact mask export, file dry-run and guarded write, reply provenance and feedback
revision after save/reopen, stale request rejection, Headless cancel/commit and persistent link navigation
after asset rename. It removes its own assets/window and restores tool preferences; user files are untouched.
The separately selected `agent-reload-v1` performs one real native script reload, then resumes the
same reservation with retained masks and completes it. It also cancels an interrupted edit lock and verifies
that multiple open cards, their focused discussion, independent drafts, an unposted message edit,
attached mask, empty attachment slot and separate floating layouts survive the real reload.
It requires an idle Editor with no borrowed pending/recoverable operations; all fixture windows,
documents and textures are transient. It never saves user documents. Use the same
`windows,user-state,temp-files` effects; do not run it concurrently with compilation or other tests.

`comment-agent-v1` checks unsaved discussion inspection, context PNGs/crops, guarded replies
and status changes, atomic batch Undo and changed/deleted feedback during reservation/fork/lock
workflows. It also checks attachment aliases/assets, idempotent work/request retries and conflicts,
receipt Undo/Redo, agent progress during generation and completed-work recovery without transient jobs.
Creation checks include required fields, text limits, attachments, returned IDs, dry-run, atomic rejection,
Undo/Redo and receipt replay. Color changes preserve feedback guards while full revisions stay strict.
The comments model case covers every InProgress / AwaitingReview / Resolved transition and TIFF/JSON
roundtrips, invalid combinations and replies preserving status. UI checks cover review badges, the global
badge surviving filters, reading without document changes, status filters and Undo/Redo. The review-state
render is saved to `Temp/WhimTex/comment-review-preview.png`; the tool badge has a transparent clearance.
The status picker is opened through its UI event and a choice is submitted from its panel overlay.
Choices use vector icons with hover names. The initiating field retains pointer capture for a held gesture;
desktop focus loss cancels it. Card/list icons appear in the review render.
Agent checks cover exact status strings, dry-run isolation, invalid/conflicting inputs, feedback revisions
and a result reply with its status in one Undo group.
It owns a temporary window and Drawing texture, writes previews under `Temp/WhimTex/Agent`,
and does not create or change Unity assets. Saved-path and Headless storage use the shared model/API
paths; this case targets the unsaved Assistant workflow.

## Compile and validate entry points

Use the same reviewed selector and explicit project for these separate actions:

```sh
node Tests~/scripts/run-tests.mjs --compile --profile documents --reviewed "<fingerprint>" --project-path "<project-root>"
node Tests~/scripts/run-tests.mjs --check-entries --id psd-writer-v2 --reviewed "<fingerprint-for-this-id>" --project-path "<project-root>"
```

`--compile` uses Unity's `run_script dry_run`: the assembly is not loaded and no assertions
run. Node uses syntax checking. `--check-entries` loads reviewed C# into an ephemeral assembly
and reflects only its own test types: public/static entries, unique method names (private
overloads also matter to Pipeline), and argument counts for every lifecycle phase. It invokes
no scenario body. Reports distinguish `compiled`, `entry-validated`, and executed `passed`.
Entry validation requires a C#-only selector; subsystem profiles containing Node reload
orchestrators must be narrowed with `--id` / `--ids`. Review each chosen selector separately.

## Common API

Cases return one structured result: `status`, nonnegative `checks`, `message`, `failures`;
Node can also include named `cases` and comparison `facts`. Statuses are `running`, `passed`,
`failed`, `skipped`, `cancelled`. Passed results cannot include failures. SKIP/cancellation
never count as an all-green run. Empty cases and Node case lists fail.

- C#: one static `Run` calls `TestContext.Run`; use `True`, `Equal`, `Near`. The body owns
  explicit using/finally cleanup. A cleanup exception prevents a passed verdict.
- Node: register named cases in `TestContext`, use its `assert`, finish once. Cases execute
  serially. stdout contains exactly one `WHIMTEX_TEST_RESULT <JSON>` marker; exit must agree.
- Async C#: explicit `Start`, `Poll`, optional `Cancel`, and `Cleanup`; pass a per-run GUID
  instead of sharing generic state keys. Running is not success. Cancellation must actually
  stop and drain owned work/finally before acknowledgement. UI batches use test-owned
  carriers/BCL bridges; Poll never relies on statics in a freshly compiled assembly.

Pipeline compiles one ephemeral input file. The runner concatenates the selected case and
explicit support sources under project `Temp/WhimTex/test-runs/`, with source line mappings
and hashes in the report. Global imports are deduplicated before types; source line mappings
remain intact. Ordinary cases do not install a production test bridge. Asset tests declare
their imports. Actual reload tests defer one supported
`CompilationPipeline.RequestScriptCompilation()` until after Trigger acknowledgement,
poll Editor status without running C# while compiling, then require the persisted real
before-reload marker and loss of the old AppDomain callback. A CLI `up_to_date` response
or rebuilding the UI alone is never accepted as domain-reload evidence.

All scenarios require the structured protocol, including cleanup. Text PASS/FAIL strings,
bare JSON success and exit codes cannot replace assertion results. Executable sources must
belong to Cases/Framework (except the explicitly declared current PSD writer); path aliases
cannot turn input data or scripts into executable test sources.

## Safety and results

Unity requires explicit `--project-path`; only an idle matching Editor is accepted. No tool
starts another Editor, installs dependencies or compiles via dotnet/MSBuild. A Player build
runs only through the explicitly selected `player-release-workflow-v2` after separate human
authorization. Selecting a profile or acknowledging effects is not build permission.
Read-only Node profiles also work in a standalone repository checkout, e.g. CI.

The runner checks reviewed source fingerprints between scenarios; no archival checks are
part of listing, reviewing or running current tests.
Reports separate transport, API, compile/execute, assertion, protocol, timeout and cleanup
failures. By default the first non-pass stops the profile; remaining scenarios are `notRun`.
Explicit `--keep-going` collects known failures, but still stops after uncertain execution,
cancellation or cleanup. Failed bodies and failed cleanup are recorded separately.
Only that selected set can be reported green. Production hashes cover package `src/`, not
every external project dependency or user action; coordinate Editor use separately.

Runs are serial under `Temp/WhimTex/test-runs/runner.lock`. Ambiguous Unity timeouts retain
the lock: killing the CLI does not stop Editor work. Cancel has a separate wait budget;
even successful cancellation/cleanup does not erase earlier uncertainty. Inspect completion
before removing the exact lock or retrying. The lock does not block other tools or the user.

An inner lifecycle may report `recoveryRequired:true` when its own journal or cleanup is
unresolved. This retains uncertainty and the outer lock even if the result is malformed or
contradictorily says `passed`. The dispatcher does not then replay generic Cancel/Cleanup:
inspect and recover the exact owned lifecycle first. A completed cleanup never hides the
original assertion failure.
If another beforeAssemblyReload subscriber throws, the native reload can finish without our
before-event marker. Verify still fails. Cleanup may release the owned fixture only after the
old AppDomain marker has disappeared and the Editor is idle; it never fabricates a passing marker.

`--allow-effects windows,user-state` acknowledges declared effects; it grants no permission.
Asset mutation/import/deletion still needs user authority. Review receipts include invocation, selected/support sources,
declared dependencies and dispatcher helpers, not a guarantee of sufficient coverage.

Node child-process restrictions (`spawn EPERM`) require the normal execution permission;
do not introduce OS-specific bypasses. Direct framework tests can run without children.

## Documentation CI

Documentation CI runs `node Tests~/scripts/ci-docs.mjs`: the existing eleven read-only Node
checks through independent `ci-docs`, with a programmatic receipt for the checked-in CI profile.
It rejects Unity and effectful metadata; it is not an interactive permission bypass.

Exit codes: 0 selected set passed; 1 run/preflight failed; 2 invalid invocation/catalog/receipt
or lock acquisition. PowerShell automation should forward `$LASTEXITCODE`.

## Optional prerequisites

`image-clipboard` selects only `image-clipboard-write-v1` and
`image-clipboard-native-v1`. The first checks DIBV5 encoding and internal HDR paste
placement without accessing the OS clipboard. The native case checks Windows
PNG/DIBV5 publication, selection alpha, HDR snapshot priority, merged copy and
replacement-image invalidation using owned transient documents and windows.
After source review, use `--allow-effects windows,user-state` for this profile.

Native checks return SKIP before mutation unless every original clipboard format
can be captured safely: at most 64 supported memory formats and 4 MiB total.
Unsupported handles, OLE data and concurrent changes prevent the run. Cleanup
verifies every restored format and byte; a subsequent external clipboard change
is left untouched. If `recoveryRequired` is true, retain the loaded fixture's
in-memory capture and resolve its owned recovery before another native run.

`fault-release-workflow-v2` owns installation, native reload verification, Faults/Deferred
assertions, removal and another verified native reload. Its generated postprocessor can
trigger only within its exact GUID folder. Its explicit `native-fixture` workflow has a
bounded 180-second lifecycle budget; ordinary scenarios remain limited to 60 seconds.
Installing/removing this fixture requires human asset authority. On uncertain completion,
retain the lock and fixture, inspect the exact recorded GUID, and never replay a mutation.

The HLSL grammar Node test uses an already installed TextMate/Oniguruma runtime specified by
`WHIMTEX_VSCODE_APP` (the installation's `resources/app` directory). No dependency is installed
automatically; an unavailable runtime is a non-green prerequisite skip. The optional PSD
reader source accepts an explicit preinstalled reader module and generated fixture path.
Select `--id psd-reader-roundtrip-v2` with `--allow-effects temp-files` and an explicit
matching project. Set `WHIMTEX_PSD_READER` to the installed module's absolute entry path;
the workflow creates its own GUID PSD through the native Editor before decoding it.
Installation remains a separate permissioned operation; the runner never installs a decoder itself.
Player validation remains a separately authorized workflow; never infer build permission.
The workflow targets the current supported Standalone platform without switching project
settings, builds only its owned scene to Temp/WhimTex, executes the built Player and verifies
runtime pixels, packed contents, saved document/meta preservation and native helper removal.

## Reference inputs and diagnostics

`text-outlines` covers original font curves and editable Text operands in Boolean Groups.
`text-outlines-v1` compares nine layouts with the native unhinted raster, including
SmallCaps, horizontal scaling, Frame/Clip/Ellipsis, justification and Auto Size.
`text-boolean-v1` checks all five operations, nested/reflected transforms, pixel scale,
editing, JSON render parity, Target, thumbnails and export. Its PNG stays in
`Temp/WhimTex/text-outlines`. `font-outlines-v1` compares TTF/composite, TTC and CFF
curve bounds with the public native font engine and rejects unsupported sources.
Its OFL fixtures and provenance are in `Fixtures/Fonts`; they are not installed or
imported. Missing installed TTC coverage returns SKIP.
`text-outline-persistence-v1` checks public API dry-run, atomic conversion failure,
layer-shell/FitFrame preservation, TIFF payload/carrier roundtrips in memory and
pure-model Unity serialization snapshots without transient FX references. The snapshot
check is not a live Undo/Redo test or an Editor restart; public EditorJsonUtility
snapshots are not an oracle for transient Unity object references. FX data/identity
are checked through the API and native TIFF cases. These cases leave user documents,
assets and the Undo stack alone.

`text-outline-undo-v1` separately exercises native Undo/Redo through the actual
conversion button, Properties fields and inline text editing. It requires
`--allow-effects windows,user-state`: six owned utility-window fixtures cover
Point, Frame/Clip/Ellipsis and nested Combine/Union/Subtract. Each of 21 edits
gets two Undo/Redo cycles, checking complete layout/curves, fitted transforms,
parent transforms/style, live native FX identity/data and RGBA restoration
(maximum channel error 1e-5). The 2026-10-16 run passed 1858 assertions and cleanup.
It is not a full Editor restart or a manual usability assessment. No user document
or saved asset is edited; only owned Undo records are cleared afterward.

`text-font-atlas` checks large native bitmap atlases against independently loaded
system-font glyphs, including a nonzero face in a TTC collection with distinct glyph
metrics. It prefers Arial and a monospaced/proportional collection when available.
A readable TTF is required, and missing TTC coverage returns SKIP. The result
retains the resolved faces and coverage facts. The case restores the initial public
FontEngine face and releases only owned resources. `text-edge-modes` also rejects
font-face load warnings across all three edge modes and large-font styles.

`--id reference-fixtures` authenticates all 255 registered files, their dimensions/layouts
and reference-reader safety contracts without Unity. `ReviewedOracle` authenticates bytes
again before native use. The reference paths are relative to package Fixtures, not absolute
machine paths. Missing or altered files are errors; no automatic generation or substitution.
Source PNG/live inputs are not algorithm output goldens. Never regenerate pre-change
snapshots with the current renderer to obtain a pass. Diagnostics, compilation, entry
validation and old reports do not prove current regression or visual equivalence.

`color-picker-eyedropper-native-inspection-v2` is an opt-in15-second diagnostic. The public
`InspectNativeSession(runId)` getter is repeatable between serial lifecycle calls; raw Poll
responses retain fresh-assembly observations. Do not inspect in parallel with another Unity
CLI invocation. Joined Cancel/Cleanup closes only this test's picker and releases its GUID.
Fixture/capture success is not a native sampling-duration or image-golden verdict.

Seamless Release `Status` is an explicit SKIP/redirect: actual synchronous Stress outcomes and
timings live in the selected Stress raw receipt after cleanup, not the old shared SessionState.
Historical Capture must never regenerate pre-change references with the current renderer.
Original tests and completed migration evidence remain only in Git history; recovery
references are in the [test map](README.md#история).

## Generated artifacts

`profiler-allocations-v1` checks per-stage main-thread allocation counts and nested self
counts, excludes an owned worker from stage counts while including its bytes in the
Editor-wide total, crosses real Editor frames, and verifies overflow/recovery and frozen
stopped totals. It joins its worker, disposes recorders and restores the stopped user
capture. Allow `user-state`; no assets or user documents are changed.

`profiler-diagnostics-v1` checks texture/native-cache ownership, duplicate release, actual runtime
GC-counter availability, suppression, observed source-property changes and dependent cache misses,
bounded cause explanations, filters and memory/cause snapshot roundtrips. It owns transient objects
and preserves the user's stopped capture/baseline. Allow `user-state`.

`profiler-diagnostics-ui-v1` uses an owned attached window to check unavailable/zero memory values,
cause evidence/limits, benchmark controls and progress, serialization/restore, selection/filter/scroll
preservation and repeated CreateGUI. It restores focus and preserves capture/session tokens.
Allow `windows,user-state`. This is a functional UI test, not a screenshot comparison.

`profiler-gpu-coverage-v1` checks instrumented non-FX boundary placement and on/off rendering parity;
it does not wait for native timestamp delivery. Allow `user-state`. Actual delayed native GPU
delivery for composition, layer generation, transforms and encoded copies is checked by `profiler-fx-v1`.

`profiler-benchmark-statistics-v1` checks deterministic median, interpolated p95, population deviation,
invalid samples and bounds. `profiler-benchmark-v1` additionally performs small real snapshot renders,
checks cold/warm ownership, frozen readable/unreadable textures and pending Drawing pixels, default
null brush references, unchanged source state/capture, cancel/timeout/failure/reload cleanup and
time-dependent FX rejection. Allow `user-state`. It invokes the reload callback, not a real reload;
its small render timings are functional evidence, not performance-comparison measurements.

`profiler-analysis-v1` checks baseline/current identity, immutable snapshots, workload versus mean
cost, null/zero/partial GPU and inclusive/self allocation deltas, integer precision, filters and paging, bounded agent summaries, cache/reason counts
and baseline persistence through the save/restore functions. It uses deterministic synthetic spans,
isolates and restores the user's capture, baseline and SessionState token, and deletes only its own
Temp snapshot. Allow `temp-files,user-state`. This tests the reload path but does not trigger a native
domain reload; it does not benchmark rendering or generate GPU samples.

`profiler-comparison-ui-v1` opens an owned temporary window and checks baseline actions, all eight
comparison metrics, shared filters, operation-filter warnings, 205-stage paging, nullable/partial GPU
rows, inclusive/self allocation units and coverage, recycled tooltips/styles, serialized view settings and repeated CreateGUI scheduling. It restores
the user's current/baseline references, reload tokens and focus. Allow `windows,user-state`.
It is a functional UI check, not a screenshot/golden-layout comparison.

`profiler-table-layout-v1` measures actual UI Toolkit header/realized-row column bounds across
Summary, Comparison and Events, 680/1000-point windows, light/dark styles, with/without vertical
scrollbars and after vertical/horizontal scrolling. It also checks both allocation headers, ascending/
descending numeric sorting, unavailable-last ordering, partial/zero/recycled cells and serialized sort settings.
Comparison delta colors are checked in both themes for all metrics, including recycled rows,
rounded zeros, missing percentages and incomplete measurements; call-count changes stay neutral.
It uses synthetic rows in an owned window across real Editor frames;
the user's capture is never replaced. Allow `windows,user-state`. No screenshot goldens or assets.

`profiler-coverage-v1` checks editor instrumentation with owned transient documents and a hidden window:
per-document change provenance, nested edits, root/filter behavior, coalesced render requests and
capture boundaries, real cache miss/hit/release reasons, selection-mask reuse, brush/composite pixel
parity, JSON roundtrip and the in-memory reload serialization codec. It preserves a stopped user
capture and its reload backup; requires no pending GPU queries. Allow `windows`; no Unity assets
or document files are created. This is not a native reload test or end-to-end file-import/dialog test.

`profiler-fx-v1` uses an isolated capture only during synchronous renders of owned transient
FX child/group/Target/material fixtures; it preserves any stopped user capture. It checks CPU
preparation/submission accounting, distinct FX summary rows, real delayed GPU results over three
batches (including recorder reuse and draining after Stop), pending export/reload handling, exact
off/on pixel parity, UI columns and snapshot roundtrip. It also exercises the automatic UI callback:
warmup/ready status, delayed completion, read throttling, unchanged-capture no-op, preserved selection,
filters/sort/folding/scroll, and scheduler cleanup when rebuilding or disabling the window.
It does not enable the Unity Profiler window or use internal Unity APIs. Allow `temp-files,windows`.
The GPU backend must produce samples when supported; unsupported hardware returns SKIP, not a GPU PASS.
The fixture pumps ordinary Editor frames while waiting; no user document or project asset is changed.
Its JSON results are under `Temp/WhimTex/profiler-fx-<run-id>.json`.

`profiler-reload-v1` uses one real native compilation/domain reload, with the shared GUID-owned
reload protocol. It verifies automatic retention of exact completed events/timings, interruption
of an open root, unchanged capture ID, and automatic UI population. It refuses an existing user
capture and requires `temp-files,windows,user-state`; it creates no project assets.
`profiler-v1` also checks stopped-capture snapshot roundtrips, interrupted scopes, invalid snapshot
rejection and Clear preventing resurrection, without claiming these substitute for a real reload.

`profiler-v1` checks the opt-in capture API on owned transient documents: nested
wall/self accounting, change and layer identities, document filters, cache hits and
invalidation, bounded capacity/deadline, pagination and full JSON export. It compares
rendered pixels with recording off/on and builds an owned unattached profiler UI
tree for control/list checks, including layer-name snapshots, filtered hierarchy/bar scale,
recycled row colors, split-pane structure and tree folding across pages with filtered ancestor paths.
It also opens a temporary profiler window for real summary-header submit events, checks all four
numeric sort keys in both directions, missing timings, stable ties, unchanged bar scale and serialized
sort state, then closes that window and restores focus.
Group checks cover all six classifications, multi-selection/solo/none, pre-pagination filtering,
context-only ancestors, unchanged timings, counts for hidden groups and serialized UI selection.
`profiler-groups-readonly-v1` runs the filter/UI checks against a stopped retained capture of at most
2000 events containing `document.render`, without starting, stopping, clearing or replacing it. It checks
event and metadata identity afterward and owns only temporary windows (`--allow-effects windows`).
Synthetic cross-group hierarchy cases remain in `profiler-v1`.
Allow `temp-files,windows`; exports use unique names
under `Temp/WhimTex`. An existing user capture causes SKIP without modification.
The test does not enable Unity Profiler or claim GPU/worker timing coverage.

`distance-rbf-opt-capture-*` / `distance-rbf-opt-compare-*` are paired, opt-in
session checks for the RBF cold-build optimization. Capture the `small`, `star512`,
`rings512` and `star1024` suites BEFORE modifying the kernel; capture refuses to
overwrite a snapshot. Compare authenticates its SHA256 and checks every raster
and output byte, or reports SKIP if the session snapshot is missing. The small
suite has 30 fields covering both sharp modes, thresholds, channels and tiny/odd
dimensions. Each large suite measures five fresh CPU field constructions with
no retained geometry, excluding raster generation and input/output allocation.
Phase CSVs and source hashes stay in `Temp/WhimTex/rbf-optimization-20261014`.
Timings share Editor/compiler state and are diagnostics, not speed assertions.

`distance-round-contour` is an opt-in performance comparison, not a regression
verdict. It measures seven fresh-resource builds per mode at 512/1024 on a star,
36 rings and native HintedAA text, alternating Round/Contour AB/BA pairs. The
source raster/upload is shared and untimed; no field result or geometry is reused.
The Editor/compiler state and Unity RT pool remain shared. Reports retain the
resolved font, CPU field phase, full Render and synchronous completion/readback.
Use `--allow-effects temp-files --keep-going` after review; successful diagnostic
sample collection returns SKIP. Files are under `Temp/WhimTex/distance-fields/performance`.

`distance-contour` checks the separate Contour AA mode: distances to raw finite
threshold segments, soft diagonal ramps, circles/rings, ambiguous cells, exact
threshold contacts, small islands, degenerate dimensions, settings and native
SDF/Outline render/UI. It does not replace Round AA or AAEDT. The text diagnostic
is `Temp/WhimTex/distance-fields/contour/text-source-round-aaedt-contour.png`:
columns are source, Round AA, AAEDT and Contour; row pairs are Hinted AA,
Antialiased and Pixel, each with identity then fractional layer translation.
These generated comparisons are not goldens or proof of universal superiority.
Use `--allow-effects temp-files,windows,user-state` after reviewing its sources.

`distance-aaedt` checks the additional AAEDT + 8SED mode: bounded independent
coverage/geometry tests, a native comparison with Round AA on the authenticated
fifth star at thresholds 77/128/192, existing six-mode numerical regressions, and
eight-mode settings/render/UI checks. The comparison sheet and CSV go to
`Temp/WhimTex/distance-fields/aaedt`: thresholds left to right, Round AA above,
AAEDT below. The general SDF/Outline sheet is `eight-modes.png`. These outputs are
diagnostics, not accuracy or speed rankings. Select this profile with
`--allow-effects temp-files,windows,user-state` after reviewing its sources.

`distance-merge` is the opt-in contour-merge regression profile. It compares merge
0/1/2 for Sharp/MTSDF: bounded geometry, seven analytic rasters (42 CPU calls), and
18 native renders of the authenticated fifth capture at thresholds 77/128/192.
It also runs the six-mode kernel/boundary, settings roundtrip and render/UI cases.
Contact sheets and delta CSV go to `Temp/WhimTex/distance-fields/merge`; columns are
0/1/2 pixels, top row Sharp, bottom row MTSDF. These are diagnostic comparisons,
not output goldens or evidence that merging restores sharpness. Caller documents
are untouched; the UI test uses an owned transient window. Select this profile
with `--allow-effects temp-files,windows,user-state` after reviewing its sources.

The `distance-fields` profile includes independent raster-angle and five captured-input
checks in addition to the kernel, UI and render cases. `DistanceRasterCornerTests`
checks source hashes and analytic geometry, never newly rendered output goldens.
`DistanceCornerFitTests` still compares fit-window arithmetic with the frozen original;
whole-contour recovery is no longer asserted bit-identical after the raster-quality changes.
The fifth originally failing input is retained in [DistanceRaster](Fixtures/DistanceRaster/README.md)
and participates in the captured suite and `distance-safety-v1`. The three `distance-safety-*`
entries add independent finite-contour bounds, full RGB interpolation before median,
common-angle tips, thin strokes and holes. Their oracle excludes only raw/independent analytic
near-boundary bands, never bands derived from the current output. Each body stays within 30s;
35/106/60 production calls respectively. Raster reconstruction may extend a sampled-off tip,
so a raw-mask-only sign oracle is not valid at that independently known analytic boundary.
Sharp resolves ownership per contour before combining fields; these tests do not demand
unlimited acute miters or treat the Euclidean-nearest island as the sole artistic contributor.
The 15-case profile passed on 2026-10-13, receipt `2026-10-13T08-18-49.604Z-82572.json`.
See F19 for the finite-distance compromise and measured cold costs.

Runner reports, native input bundles and recovery logs stay under project `Temp/WhimTex`.
Do not copy them into `Tests~` or commit expanded inventory snapshots. The package ignores
generated result files. Fixture manifests and provenance are required input data, not reports.
A compact human summary can link to the historical Git snapshot without embedding raw results.

Generated diagnostics must retain their actual samples/artifacts before owned cleanup.
The opt-in `distance-field-performance` profile measures actual SDF/Outline Render
calls on owned antialiased star/ring inputs, bypassing the outer document cache.
Review the profile, then run with its receipt, `--allow-effects temp-files` and
`--keep-going` to collect all diagnostic SKIPs. Raw JSON is retained in
`Temp/WhimTex/distance-fields/performance`; nested phase times overlap. Render time
includes input readback/output submission, not final GPU completion. The separate
completion readback includes full RGBAFloat transfer and is not GPU-only timing.

`distance-field-cold` selects six opt-in diagnostics with zero algorithm warmups:
SDF star 512/1024/2048, Outline star 512/1024 and SDF rings 512. Each sample creates
a new behaviour/material/LUT and rebuilds the field, geometry and scratch buffers;
it does not reuse a persistent result cache. The source fixture, running Editor,
compiler state and Unity's global temporary-RT pool remain shared: this measures
cold resource construction, not a cold process/JIT or first shader compilation.
The same review, `--allow-effects temp-files` and `--keep-going` requirements apply.
Raw reports retain `creationMs`, `renderMs`, separate `completionReadbackMs`,
`firstSampleForMode` and every sample, so compare creation + Render consistently.
Cleanup is outside this construction interval. Diagnostics return SKIP even when
all timing samples were collected; compare the matching production fingerprints
with a separately passed `distance-fields` quality run.

For a paired CPU-only comparison of the first six modes, explicitly select
`distance-classic-opt-capture-{small,star512,rings512,star1024}` **before** editing
production, then the corresponding `distance-classic-opt-compare-*` IDs afterward.
These are individual IDs, not a brace-expanding runner profile. Both need reviewed
sources and `--allow-effects temp-files`. The session snapshots under
`Temp/WhimTex/classic-sdf-optimization-20261015` cannot be overwritten by capture;
compare authenticates their SHA256 and requires identical input and output bytes,
including all four MTSDF channels. Missing snapshots produce SKIP, not PASS.
The small matrix covers 180 fields; each large fixture measures seven fresh builds
per mode in Guarded Sharp. CSVs retain all samples and nested phase timings, and
`.sources` files record production hashes, Editor version and CPU. The interval
includes geometry/field workspace construction and disposal, but excludes source
generation, input/output allocation, GPU transfer and rendering. No persistent
field cache or explicit warmup is used; Editor/compiler state remains shared.
PASS means exact field equivalence and finite/repeatable output, not a speed claim.
Use matching source hashes to identify runs when comparing a restored baseline.

`distance-reconstruction` checks the experimental Corner Fit AA and Spring Simulation
modes independently of the legacy Sharp/MTSDF guards. It includes exact pixel-area
oracles, an AA-capped triangle that must actually recover a corner, 10–90 degree
corners at 16–64 pixels, rotations/subpixel phases, holes, thin bars, tiny islands,
channel selection, binary/ambiguous fallbacks and the authenticated false-ray input.
`distance-spring-contribution-v1` compares 12 independently rasterized CornerFit/Spring
pairs and requires measurable Spring influence in the final field. The existing sign,
supported-side accuracy and remote-extension bounds also apply to these inputs.
The native comparison writes `distance-fields/reconstruction` under project Temp;
its single-render times are diagnostics, not a controlled performance benchmark.
The final native render check covers all ten modes and writes `ten-modes.png`.
Use the normal reviewed receipt and declared effect grants; no borrowed working
document is changed. A passed matrix is not a guarantee of recovering lost geometry.

`distance-preparation` covers retained CornerFit/Spring preparation and distance evaluation.
The native render-cache case compares 512px range/width and miter edits against fresh full
rebuilds, checks phase counts and all rendered channels, and writes `preparation/interaction.csv`
under project `Temp/WhimTex/distance-fields`. Render times exclude the subsequent verification
readback and are interaction diagnostics in the same Editor, not cold-process benchmarks.

`distance-corner-parameters` includes an opt-in comparison with this session's pre-change
native float snapshot. `distance-corner-parameters-capture-v1` is a diagnostic producer:
run it only BEFORE changing the production kernel; it refuses overwrite and returns SKIP.
`distance-corner-parameters-v1` authenticates the frozen SHA256 and checks the 24 CornerFit
fields bitwise. The 24 Spring fields now record historical differences, without asserting
equivalence: Spring deliberately stopped rejecting relaxation against source AA coverage
after that snapshot. It records prepared-kernel timings and differences in project
`Temp/WhimTex/distance-fields/corner-parameters`.
If the session snapshot is unavailable, comparison is SKIP; it must never be rebuilt using
the optimized kernel. Portable analytic/render/cache regressions remain independently runnable.

Successful timing/manual producers can still return SKIP; a saved PNG, CSV or timing array
does not establish historical equivalence or a performance acceptance threshold. Unavailable
process-memory counters must be labelled unavailable, not interpreted as zero memory usage.


<a id="stroke-tile-pool"></a>

### Stroke atlas pool, 2026-10-18

`brush-tile-buffer` compares unchanged full-target stamping with sparse accumulation using
identical generated stamps, every pixel of a 513×517 image, weak alpha, SDR/HDR, masks,
per-stamp blending, pressure, channel locks, erasing, wrapping and partial tile edges. It also
checks concurrent stroke ownership and the global 64 MiB idle-pool bound. Full-assembly oracle
shaders remain frozen/authenticated; a test-only adapter reconstructs their stroke input.
`brush-buffered-compute-v1`, `brush-buffered-raster-v1`, `brush-preview-cache` and the focused
`brush-first-stamp` pass assembly, downstream consumers and dynamic first-stamp correction.
`paint-tile-history-small` passes byte-exact Half/storage Undo, hidden RGB, queued weak strokes,
native history interleaving and TIFF synchronization. SDR/HDR `paint-tile-history-8k-frame[-hdr]`
pass exact Undo/Redo for a full-canvas perimeter, including restored alpha coverage.

`brush-tile-buffer-8192-{81,2048}-normal`, `...-8192-81-hdr` and `...-8192-2048-stamp`
run a 48-input curved path across almost the full canvas, yielding Editor updates. They compare
bounded 32² probes across tile corners and use only 1×1 timing fences, never full 8K readback.
They open no windows, inject no input and touch no user document. The paired baseline keeps a
full-size accumulator and old single-submission incremental preview. Stamp generation/reflection
is included only on the new-path CPU side. GPU values are fenced queue latency, not isolated
hardware timestamps or displayed frame rate.

RTX 5060 / D3D12 / Unity 6000.7.0a6, Linear, paired results:

| 8K workload | Stroke pages | Former accumulator | New / former accumulation wait, median | New / former first accumulation wait |
| --- | ---: | ---: | ---: | ---: |
| 81 px normal | 128 MiB | 512 MiB | 0.66 / 0.23 ms | 1.37 / 15.53 ms |
| 2048 px normal | 448 MiB | 512 MiB | 1.42 / 0.48 ms | 4.34 / 17.35 ms |
| 81 px HDR | 128 MiB | 512 MiB | 0.68 / 0.23 ms | 1.41 / 10.88 ms |
| 2048 px per-stamp Multiply | 448 MiB | 512 MiB | 3.82 / 2.07 ms | 4.99 / 179.86 ms |

These are individual runs, not a claim that every input is faster. A discarded prototype with
one RenderTexture per block took about 2.43 ms median for the 81 px path; atlas pages reduced
that overhead. Warm sparse submissions still cost more than a single full target.

Paired fixtures retain extra reference textures. `...-isolated` cases use production resources
only. Median accumulation waits were 0.63/0.94/0.67 ms for 81 px/2048 px/HDR; median preview waits
were 0.24/0.88/0.28 ms. Their first preview waits were 27.49/22.73/22.76 ms. A paired HDR first
preview reached 196.51 ms; it did not recur in the isolated run, so its cause is not attributed
to sparse accumulation. The isolated first wide-brush accumulation still took 19.01 ms.
Cold allocation variability remains; the Half source and straight preview are still 512 MiB each.

All tested strokes returned their active pages and retained at most 64 MiB idle. Active storage
is bounded by touched source blocks rounded to page capacity, not by the idle limit: a dense
8K stroke can need 512 MiB. Sequential-backdrop scratch is another 256² Half (0.5 MiB). No
source block is discarded based on alpha. These accumulation measurements exclude release and Undo;
see the dense-release diagnostics below for the subsequent history changes.
Local reports: `Temp/WhimTex/brush-tiles` and `Temp/WhimTex/test-runs`.

The older `BrushPipelineBenchmark` full-assembly control includes the sparse-to-full test adapter;
its timings must not be used as an accumulation A/B comparison. Full-target clear/replay controls
are explicitly unsupported for sparse storage; use the paired tile-buffer cases above.

## Dense stroke release and repeated input

`paint-history-codec` checks byte-exact block roundtrips, arbitrary Half bit patterns,
incompressible fallback, repeated runs, and malformed/truncated payload rejection.
`paint-undo-reservation-release` uses an isolated 1 GiB Temp store and twenty 1536px HDR strokes
without idle maintenance. Every writer releases its worst-case reservation; no history boundary
is retired, and all twenty Undo/Redo steps remain exact.

`paint-tile-history-8k-sweep[-hdr]` paints a serpentine 2048px stroke over all 1024 source blocks.
It checks snapshot limits, mask/opacity, HDR and exact Half Undo/Redo probes. Its input is submitted
synchronously: the following one-pixel fence can include queued drawing, so it is not a displayed
frame-rate or pure next-input latency measurement. No full 8K synchronous readback is used.

`paint-history-burst-512-paced` preflights the asynchronous fixture. The 8K `-paced` case makes
three dense strokes across Editor updates; `-saturation` makes six with no explicit history drain
between them. Both use an isolated Temp journal, no windows or OS input, and a one-pixel GPU fence
before release. Snapshot and writer queues stay bounded; final ordered Undo/Redo checks all states.
They require an idle Editor and `temp-files,user-state`; only owned objects/history are removed.
Reports and stage captures are under `Temp/WhimTex/paint-tile-history`.

Burst reports also include frame start/end ticks and per-page worker timestamps, compression,
input gathering, output copying, file writes and checksum times. Worker fields are read only after
their Tasks complete. Worker wall intervals overlap; do not sum them as UI latency. Restrict profile
spans to `brush.history.commit` when attributing release pauses, excluding later Undo/Redo.
Idle Editor cadence and Pipeline polling can lengthen the background tail; it is not disk latency.
The current writer allowance scales up to 256 MiB on larger CPUs; GPU staging and the two-page
readback limit remain unchanged. One completed FIFO head can additionally overlap the next read;
burst reports assert that bound and record `reading` / `cpuHeads`. Vertical transfer strips make
full tiles contiguous for direct codec input; partial blocks retain the row-gathering path.
`paint-history-native-pages` checks raw copy sentinels, bounds rejection and exact CPU/before/after
planes at both byte depths with Burst enabled and disabled. The queued, subtile, retention and
boundary cases cover overlapping revisions, soft/zero masks, tiny alpha, hidden RGB and parameter Undo.
A four-page readback control did not reduce release pauses on
the tested 8K series and was discarded. These detached tests do not establish interactive FPS.

Individual RTX 5060 / D3D12 / Unity 6000.7.0a6 observations, 2026-10-18:

- The original user release took 6849 ms, including 5986 ms waiting for Deflate writers.
  Portable Burst block packing reduced a subsequent similar user release to 1183 ms; those are
  two user strokes, not identical benchmark inputs. The new format trades some compression ratio
  for throughput and keeps the existing disk quota.
- A 1536 MiB shared snapshot allowance lets one standard dense 8K release return without CPU
  extraction. Repeated strokes can still saturate it: raising the bounded writer queue from
  64 to 128 MiB on this 32-thread CPU reduced the six-stroke release sequence from
  40/1462/2651/2855/2803/2824 ms to 36/863/1311/1387/1385/1254 ms. Peak owned snapshots were
  1520 MiB in both runs; queued writer payload peaked at 48/120 MiB. Both completed exact Undo/Redo.
- The rapid-series test exposed delayed disk-reservation accounting: earlier fifth/sixth releases
  took about 71 seconds while unnecessary synchronous retirement ran. Successful page writers now
  finalize their stroke reservation immediately; the quota is retried after draining before retirement.
  The final six-stroke test completed without those minute-long stalls.
- Background requests are submitted and updated without requiring a canvas repaint. A new snapshot
  does not remove transfer/encoding work: the final saturation run still had about 7 seconds of
  background tail while the Editor was idle. Actual quota exhaustion, saturation, Undo and save can
  still wait. These diagnostics do not establish displayed FPS or other-device performance.

The final dense HDR case also passed: 1536 MiB of snapshots, zero capacity waits, 55.6 ms for the
synchronous submission/commit call, 1582 ms for an explicitly forced drain, and exact Undo/Redo.
Its subsequent 141 ms one-pixel fence includes queued GPU work. The final small history and retention
regressions passed, covering weak alpha, hidden RGB, mask, HDR, TIFF and actual history pruning.

## Undo block-size comparison

`paint-undo-block-size-correctness` compares fresh 64/128/256px histories against the 256px
capture control on 518px SDR/HDR fixtures. It verifies every stored and Half byte through
three Undo/Redo revisions, low flow, eraser, fractional/zero selection, hidden RGB, partial
edge blocks, empty payloads and Half reconstruction. Each fixture has one fixed layout;
old in-memory/disk blocks are neither converted nor mixed into the experiment.

`paint-undo-block-512-64-skip-thin` preflights the asynchronous benchmark. The eight opt-in
`paint-undo-block-8192-{256-all,256-skip,128-skip,64-skip}-{thin,wide}` cases compare two
32px curved strokes or two 2048px serpentine strokes over most of a detached 8K canvas.
The queue drains between strokes: these are not saturation or interactive FPS tests.
Run serially with `temp-files,user-state`; no windows, OS input or user documents are used.
GPU paint tiles stay 256px, transfer pages stay at most 1024px, and `paintSurface` stays Half.
Only owned diagnostic layers select smaller history blocks; the UI default stays 256px.

The `skip` cases omit a block only when both its Half before/after bytes and its straight
stored before/after bytes match. Portable Burst compares raw words, retaining hidden RGB,
signed zero and Half bit patterns. When Burst is enabled the test asserts it actually ran;
a managed fallback cannot silently count as a Burst measurement. No alpha-only shortcut is used.

Reports under `Temp/WhimTex/undo-block-size` separate release submission, background tail,
comparison, extraction/CPU patching, worker compression/write and Undo/Redo. Compression
includes stream writes; sums of concurrent worker times are not elapsed drain time. Tail
depends on Editor update cadence as well as work. Raw history volume is cumulative data,
not resident RAM: writer-queue peaks, staging and GPU readback bytes are separate metrics.
The 8K checks use whole CPU-storage hashes and bounded Half probes; full byte-for-byte Half
coverage belongs to the small regression. No full 8K synchronous GPU readback is performed.

Final `burst-words-v2` measurements on 2026-10-18, Unity 6000.7.0a6 / RTX 5060 / D3D12,
two strokes per row (CPU times are per-stroke means):

| Brush / history | Raw MiB | Compare ms | Extract/patch ms | Writer queue peak MiB |
| --- | ---: | ---: | ---: | ---: |
| Thin / 256 control | 451.5 | 0 | 111.2 | 72 |
| Thin / 256 skip | 441.0 | 35.2 | 90.7 | 70.5 |
| Thin / 128 skip | 238.5 | 80.2 | 82.5 | 28.5 |
| Thin / 64 skip | 141.2 | 106.2 | 153.9 | 15.7 |
| Wide / 256 control | 1536 | 0 | 397.5 | 120 |
| Wide / 128 skip | 1536 | 4.9 | 1371.0 | 72 |
| Wide / 64 skip | 1536 | 13.4 | 2622.6 | 72 |

All eight cases, the small exactness regression and the 512px preflight passed; whole stored
hashes also matched between all layouts for each path. The initial `burst-words-v1` diagnostic
with a non-blittable instrumentation field fell back to managed execution; its timings are
excluded. The final guard rejects that failure. Earlier byte-span prototypes are excluded too.

The smaller layouts reduce thin-stroke payload and queued RAM, but barely change compressed
files (first stroke 16.1–16.9 MiB). GPU staging/readback remains 380 MiB thin / 1280 MiB wide.
Wide strokes keep all blocks and gain no payload reduction; splitting the existing managed
payload arrays greatly increases extraction cost. Small-block wide Undo/Redo also remained
slower (about 3.2 s for 128 and 3.6 s for 64 versus 3.0 s control on this run). Therefore the
experiment does not justify changing the normal 256px default yet. Shared page-backed payloads
and batched restoration are candidates before adopting a smaller layout. CPU comparisons could
instead move to GPU change tracking, but that is not implemented by this test.

## Native history pages

The subsequent native-page change retains 256px production blocks, Half source/preview,
compute assembly, Task workers and the existing memory limits. Completed readback planes
move to the writer; only the stored before-image is copied before FIFO CPU patching.
There are no managed payload arrays per captured block. Atlas rows still gather once into
reusable native codec scratch; compressed output still copies to a reusable file-write buffer.
The native idle pool is capped at 64 MiB; pending writers remain capped at 64–128 MiB and
charge resident planes including padding. Workspaces are exclusive and reused, with at most
four idle codecs (~6.3 MiB). Small-block omission now reduces file payload, not native page residency.

`paint-history-native-pages` checks reuse without reallocation, zero active bytes after
completion, bounded idle storage, an injected owned-file failure and exact HDR Undo/Redo
from retained native planes. Explicit deletion releases that retained payload exactly once.
`paint-history-native-no-burst` runs exact SDR/HDR raw-page recovery with the Burst option
temporarily disabled and restored. `paint-history-codec` checks native contiguous/strided
input, partial rows, raw fallback, malformed data and separate/reused workspace leases.

The existing small history, queued history, raw history, subtile/mask exactness and retention
regressions passed. `paint-history-burst-8192-saturation` and its `-hdr` variant perform six
full-canvas strokes without draining between strokes, followed by ordered Undo/Redo.
HDR uses nine Half probes across the canvas; these 8K probes are not a full GPU-byte comparison.
Full Half/storage comparisons run separately on smaller fixtures. The native reload pair
verifies a real domain reload, zero active/idle native planes after shutdown drain, and exact
restored Half/storage and Undo/Redo. An Editor-process restart was not performed.

Local comparison on 2026-10-18, Unity 6000.7.0a6 / RTX 5060 / D3D12 (two strokes per path):

| Measurement | Before | Native pages |
| --- | ---: | ---: |
| Thin 8K CPU extraction/patch | 98–110 ms | 39 ms |
| Wide 8K CPU extraction/patch | 376–377 ms | 132–133 ms |
| Wide single release | 25–42 ms | 19–20 ms |
| Saturated SDR releases 2–6 | 879–1517 ms | 532–969 ms |
| Managed allocations during two wide strokes, all Editor threads | 134.1 MiB | 23.2 MiB |

Whole stored CPU hashes matched the pre-change capture for both paths and both revisions.
GPU staging/readback byte counts and compressed-file sizes were unchanged. Idle background
tails fell from 2.0–2.1 s / 7.2–7.3 s to 0.25–0.29 s / 0.92–1.28 s for thin/wide paths, but
these include Editor scheduling; the baseline was unusually slow relative to earlier runs.
Do not interpret those ratios as an isolated codec speedup or presented frame rate.

The six-stroke HDR run still paused for 1.05–1.59 s at saturated releases. Active native-page
samples peaked at 160 MiB for SDR and 176 MiB for HDR; idle buffers stayed within 64 MiB.
Managed heap samples fell, while Unity-native allocation samples increased as ownership moved
to native pages. This does not establish a reduction of total process RAM. The direct per-thread
GC counter returned zero on this Editor; allocation figures above instead use the available
all-Editor profiler counter. Background worker sums still show compression dominating file
Write calls; sums from concurrent Tasks must not be added to UI latency. Reports and raw paths:
project `Temp/WhimTex/page-buffer-comparison/report.md` and `cases.json`.

### Per-layer / per-FX viewport checks (2026-10-19)

`canvas-viewport-render-v1` now includes color FX with canvas UV/input dimensions, spatial
helper reads, mixed FX order, group/clipping/processor input, cache retention across frames
and pan, and generated-wrapper restoration without applying pending source. Active brush
comparisons include a color FX, masks, low alpha, signed HDR, blending and erasure.
572 assertions passed. `hlsl-only-fx-v1` passed 73 assertions covering group/Target,
thumbnail/export and document round trips. These are detached fixtures, not user documents.

`canvas-viewport-fx-8k` runs four automatic brush curves: 81/2048 px, fit/native view,
with HSV (Hue 35) on Drawing and separate 64px Text. All four passed on D3D12.
Native output was 1216×960, fit output 512²; brush caches were 17.81/4 MiB ARGBFloat.
The Half paint surface remains 8192² (512 MiB logical payload), and source/atlas/Undo
memory is separate. CPU paint medians were 0.26–0.51 ms; canvas submission 1.68–1.80 ms
(p95 2.21–2.74 ms). Native no-FX, single-layer controls were 1.12–1.16 ms canvas submission:
the extra FX and text have a measurable cost, while ordinary stamp submission remains similar.
The initial regional implementation unnecessarily serialized/cached the changing Drawing
layer (5.53–6.26 ms canvas medians); bypassing this cache removed that overhead.
HSV GPU intervals were about 0.09 ms at native view and 0.025–0.027 ms at fit view.
These are stage timings, not presented FPS; graphics-memory figures are texture payloads,
not physical VRAM or proof that every spatial effect has bounded native-zoom cost.

`freeform-figure-v1` checks Shape/Vector Freeform fill and stroke CPU/GPU agreement, HDR/alpha,
coverage reuse, Undo, TIFF document blocks/JSON, grouped/Target/clipping consumers and native 8K viewport regions.
`freeform-gradient-canvas-v1` also exercises the shared figure point inspector and transformed fill/stroke gestures.
