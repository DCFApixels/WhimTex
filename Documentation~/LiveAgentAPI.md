---
layout: default
search_exclude: true
title: Live editing API
parent: Technical reference
nav_order: 2
lang: en
permalink: /reference/live-agent-api/
---

# Live editing API

This extends [Agent API v1](AgentAPI.md) with reservations in **open documents**, including unsaved
documents. The agent stays outside Unity. Image generation is performed by the agent's own tools;
these commands neither call a model nor download images. All calls run on Unity's main thread in
Edit Mode. They never trigger C# recompilation, refresh the AssetDatabase, or save the document.
FX delivery can compile transient GPU shaders; this is separate from script compilation.

## Immediate editing batches

For comment-driven work, [comment queries](AgentAPI.md#comment-discussions) support author, status,
text and creation/activity ranges, ordering, and links to exact discussions. Canvas clusters are
presentation only; freeze individual IDs and feedback revisions. `whimtex_comments` `op:open`
explicitly navigates a link and may open its saved document; ordinary list/context calls do not navigate.

For parameter-only FX edits, preset insertion, reordering/copying/baking FX, layer structure changes
and blur/healing strokes, use `whimtex_assistant_execute`. It takes the same operations as the
batch/headless backend, an explicit open `sessionId` and the document `expectedRevision` from
`op:inspect`. It forms one Undo step, never saves, and requires all pending jobs to finish first.
See [shared editing operations](AgentAPI.md#shared-editing-operations) for the complete contract.
This is an immediate edit, not a replacement for reserving an output while doing lengthy generation.
Use `whimtex_fx_catalog` to discover installed presets and `whimtex_render_probe` to inspect FX
input/output or isolated channels without changing the document.

Make Seamless is a targeted layer (`type:"makeSeamless"`), not an FX preset. Use shared `add`/`set`
operations with `settings.makeSeamless` and a `target` operation for its source. All four methods,
units, new-layer defaults and independent edge selectors are documented in
[Make Seamless settings](AgentAPI.md#make-seamless-settings). Use `whimtex_describe` for defaults/enums
and inspect the session before updating existing settings; changing the method retains its other parameters.
For packed maps, `quiltingChannels:"Independent"` affects patch matching, while `processRed` /
`processGreen` / `processBlue` / `processAlpha` control which channels receive the result in every method.

## Connect and discover

Shape and Vector fill edits use the same shared `set` operations: partial
`settings.shape` fields or `settings.vector.shapes` as a complete replacement array.
Both expose all seven fill modes and retain inactive palettes and anchors. See
[Shape settings](AgentAPI.md#shape-settings), [Vector settings](AgentAPI.md#vector-settings)
and the [shared fill/stroke geometry](JSON_FORMAT.md#figure-fills). Stroke has its own
`strokeMode`, `strokeGradient`, `strokeGradientStart`, `strokeGradientEnd` and
`strokeGradientRepetitions`; `stroke:false` or `strokeMode:"None"` retains inactive
settings. Inspect before replacing
a Vector array so the figure, compound contours and inactive settings remain present.
New Vector authoring accepts one figure per layer. Use `type:"booleanGroup"` and
`settings.booleanGroup` for common styling and Combine-only `appearance: Group / Layers`, with `settings.booleanOperation` on
Shape/Vector/editable Text/nested Boolean Group children. These are shared batch operations, not
image-generation reservations. See [Boolean Group settings](AgentAPI.md#boolean-group-settings).
The shared `convertToVector` operation accepts Shape or Text on the same layer shell.
Text conversion preserves its canvas placement and current layout as font-outline curves;
direct Text operands stay editable. See [Vector settings](AgentAPI.md#vector-settings)
for font restrictions and atomic failures.

For an authorized content-generation request, reserve and capture context in one call:

```powershell
unity command whimtex_assistant_begin --requestId 'NEW-UUID' --name 'Balcony' --source merged --area selection --project-path 'D:/Projects/MyGame' --format json
```

`requestId` is required; generate a unique value before invoking the command and retain the exact
arguments for retries. Optional arguments: `name` (Generating…), `source` (none), `area` (canvas),
`sessionId`, `sourceLayerId`, `selectionMode` (strict), `padding` (-1: mode default). This shortcut creates
a new layer at root index 0; use JSON `begin` for other placement or replacePixels. Direct equivalent:
`WhimTexApi.LiveBegin(requestId, name, source, area, sessionId, sourceLayerId, selectionMode, padding)`.

With no sessionId, begin uses the only open document or the currently focused WhimTex window.
Only when multiple windows are open and none currently has focus, it falls back to the open window
with the highest `focusOrder`. Each OnFocus advances a shared counter; window orders survive script
reload, and the counter resumes above restored orders. API reads do not change focus history.
Missing or tied history returns `session_ambiguous` without writing. Focus a window or specify its
session explicitly in that case. A retry resolves its original receipt before considering
the current focus, so switching windows cannot redirect a duplicate request. Use globally unique
request IDs. For `source:layer`, omitting sourceLayerId means the active layer at begin.

This is an explicit **mutation**, not a read with side effects. Reading sessions or inspecting a
document never creates a placeholder. No full snapshot is required before an ordinary begin.
The response includes a compact `context` with document name/path, activeLayerId, selectedLayerIds,
selectionActive and selectionBounds, all captured at begin. It does not enumerate layer settings.
Rendering and PNG encoding are still synchronous when a source image is requested; context and
reservation are captured in one main-thread call, not at the instant a chat message was sent.

The existing optional Pipeline adapter exposes:

```powershell
unity command whimtex_assistant_sessions --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_assistant_live --requestPath 'D:/Projects/MyGame/Temp/WhimTex/request.json' --project-path 'D:/Projects/MyGame' --format json
```

Direct equivalents, without Pipeline:

```csharp
WhimTexApi.LiveSessions();
WhimTexApi.LiveFile(absoluteRequestPath);
WhimTexApi.LiveJson(requestJson);
```

Use the `DCFApixels.WhimTex` namespace. Check the returned JSON `success` as well as transport
success. Read-only discovery returns `sessions`, each with `sessionId`, name, assetPath, dimensions
and focused status, plus `focusOrder` (0 means no recorded focus). `assetPath` identifies the bound TIFF
or JSON document, not its temporary in-memory document. A blank path means no saved document
binding; a source image opened for editing is not necessarily a saved layered document. If multiple documents are open, use
the user's requested document; ask when ambiguous. Do not guess from a layer name.

```json
{"apiVersion":1,"op":"inspect","sessionId":"SESSION"}
```

Inspection returns the regular document snapshot, `activeLayerId`, `selectedLayerIds` and the current
canvas `selection`. Pending entries expose `jobId`, `contentLocked` and status text. The session is
pinned to that window/document pair: switching documents, closing the window or reloading scripts
invalidates delivery. Discovery does not open windows or change selection.

## Profiling live renders

The profiler window is available at **Window → Analysis → WhimTex Profiler**.

Use `whimtex_profile --requestPath` or `WhimTexApi.ProfileJson(requestJson)` to capture render
performance around an authorized live edit. See [profiler captures](AgentAPI.md#profiler-captures)
for requests, limits, pagination, export and timing semantics. Profiling control itself neither
edits the document nor forces a render, clears caches or starts compilation.

`LiveSessions` and document snapshots expose `profilerDocumentId` as an opaque string. Pass that
returned string unchanged as `documentId` in a profiler `start` request to restrict the capture;
omit the field or supply null for all documents. Do not guess numeric `EntityId` values. In the
window, paste the returned string into **Document ID**, or leave it empty for all documents.
This ID is session-local, distinct from `sessionId`, and must be rediscovered after reopening the
document or reloading scripts. Retain the returned `captureId`, perform the live work separately,
then stop, read and export that capture before script reload.

The capture covers instrumented render paths. `changeId` associates renders with the latest model
change; it does not establish full gesture latency or GPU presentation time. Measurements include
main-thread waits and GPU submission, with null timings for unfinished spans. Summary rows overlap;
only completed matching root spans contribute to `rootWallMs`. Native worker execution is visible
through Unity's job profiler, without custom per-worker spans in this report.
FX blocks and individual FX additionally expose `cpuPrepareMs`, `cpuSubmitMs`, and delayed `gpuMs`
with `gpuStatus`. Wait for `capture.gpuAvailability:ready` before a one-shot GPU measurement and for
`pendingGpuSamples:0` before export. Read the FX timing semantics in the profiler contract: nested GPU
intervals overlap, and unavailable GPU timing is null rather than an estimate from CPU wall time.

## Comment-driven editing

The `inspect` snapshot includes all posted discussions in `document.comments`, including committed
messages/edits not yet saved to the document file and resolved threads. Text and attachments still being
composed or edited before **Post/Save** remain window-local and are invisible to the API.
Each discussion exposes `status`: `InProgress`, `AwaitingReview`, or
`Resolved`. Set it explicitly through a `comment` operation; opening or replying preserves it.
Use `AwaitingReview` for verified work handed back to the user; all status transitions are allowed.
To act on feedback, freeze the requested IDs and `feedbackRevision` values
at the start. Read complete discussions and use `whimtex_comments` with this session's
`assistantSessionId` for clean/numbered overviews and focused crops. New comments do not expand
the assignment automatically. A marker is a location cue; it does not identify a layer or an edit mask.

For immediate edits, put check-only `comment` operations before layer/FX operations in
`whimtex_assistant_execute`. For long jobs, use JSON begin/lock with the frozen checks:

```json
{"apiVersion":1,"op":"begin","sessionId":"SESSION","requestId":"NEW-UUID","workId":"WORK-UUID","name":"Edge correction","source":"merged","area":"canvas","commentChecks":[{"commentId":"COMMENT-ID","expectedFeedbackRevision":"FEEDBACK-REVISION"}]}
```

For an existing layer:

```json
{"apiVersion":1,"op":"lock","sessionId":"SESSION","requestId":"ANOTHER-UUID","workId":"WORK-UUID","layerId":"LAYER-ID","commentChecks":[{"commentId":"COMMENT-ID","expectedFeedbackRevision":"FEEDBACK-REVISION"}]}
```

Checks follow forks and prevent preview/completion if feedback changed or disappeared. Reread and
reassess the work on `comment_revision_conflict`/`comment_not_found`; cancellation/unlock remains
available. Do not bypass the conflict by replacing a revision without considering the new feedback.

After applying, render and inspect the result, then reply in each relevant thread with what changed
and any remaining limitations. Combine the verified result reply with `status:AwaitingReview`, or
`status:Resolved` when the user asked to close the discussion. Partial work stays `InProgress` with an explanation.
Reply/status operations use fresh thread and document revisions, share Undo and never save the
open document. The [comment contract](AgentAPI.md#comment-discussions) specifies filters, coordinates,
limits, reply fields and retry semantics. The `whimtex-live` skill describes the complete workflow.

Prefer `expectedFeedbackRevision` in task guards: colors and intact agent progress replies do not change it,
but user corrections do. Message attachments expose layer IDs, persistent asset references and selection masks.
Replies can attach the resulting layers/assets; the user can ping them from the comment card.
Use a workId across the assignment, with a distinct requestId for each Assistant batch or begin/lock.
The [work receipt contract](AgentAPI.md#work-ids-and-safe-retries) covers retrying completed edits,
recovering result IDs after reload, and Undo. Comment-only batches can post progress during pending
jobs; mixed image-edit batches remain blocked until pending jobs finish.
To leave a new note, use the shared `commentCreate` operation with bottom-left `anchorUv` in 0..1,
`text`, and optional `author`/`attachments`. Its result returns `commentId` and `messageId` for
follow-up replies; work/request receipts preserve these IDs on retry. New discussions stay open.

## Reserve first, generate later

```json
{
  "apiVersion":1,"op":"begin","sessionId":"SESSION",
  "requestId":"a-new-caller-generated-uuid","name":"Moonlit mist"
}
```

The result contains `jobId`, reserved `layerId`, `state`, optional `capture` and `saved:false`.
Use a fresh `requestId` for each task. Retrying the identical begin request returns the existing job;
reusing it for a different request fails. Keep the original JSON for transport retries.

| Begin field | Meaning |
|---|---|
| `sessionId`, `requestId` | requestId required; sessionId optional for unambiguous automatic document selection |
| `name` | Optional; defaults to `Generating…`. Normally give a useful name immediately |
| `parent`, `index` | Group ID and insertion index; default root, top (`0`) |
| `source` | `none` (default), `merged`, or `layer` |
| `sourceLayerId` | For `source:layer`; omitted means the active layer. An explicit ID may identify any valid source |
| `area` | `canvas` (default), `selection` (nonempty working selection), or `commentMask` (attached snapshot) |
| `maskRef` | Required for `commentMask`: `{maskId,expectedRevision}` from a message's mask attachment; unavailable on the short CLI begin helper, use JSON |
| `selectionMode` | For selection/commentMask: `strict` (default) enforces the mask; `guide` treats it as a placement reference |
| `padding` | Context around mask bounds, 0..4096 pixels; defaults to 32 for strict, 128 for guide. Selection/commentMask only. JSON does not accept the CLI's -1 sentinel |
| `destination` | `newLayer` (default) or `replacePixels` |
| `targetLayerId` | Explicit Drawing ID required for `replacePixels` |

The source and destination are independent. Choose them from the user's request. For example,
removing an object from the visible composition usually needs `source:merged`; fixing the content
of a particular layer may need `source:layer`. Do not require all tasks to use Copy Merged.

An attached selection can drive a job directly, without replacing the user's current selection:

```json
{"apiVersion":1,"op":"begin","sessionId":"SESSION","requestId":"mask-edit-1",
 "name":"Edge correction","source":"merged","area":"commentMask","selectionMode":"strict",
 "maskRef":{"maskId":"MASK-ID","expectedRevision":"MASK-REVISION"},"padding":32}
```

Read the mask metadata through `whimtex_comments` first. `capture.maskRef` records the frozen
reference; `capture.maskPath` follows the existing region-relative generation-mask contract.
Preview, completion and forks recheck the mask revision; replacement, deletion or canvas geometry
changes require rereading and recapturing. Changes to the working selection do not invalidate the
job. A mask with no selected pixels inside the canvas is rejected. Both strict and guide modes
require image completion, as with ordinary selection jobs. `replacePixels` is supported for a
Drawing target; global FX/settings edits do not become spatially masked by this field.

A reservation is an inert layer, ignored by compositing, effect inputs and clipping chains.
Users may rename, hide, move or group it immediately. Content settings and painting are unavailable.
Completion preserves its current ID, name, visibility and position. Duplicating, merging or converting
a reservation (including a group containing one) is unavailable until generation finishes.

### Several results from the same context

```json
{"apiVersion":1,"op":"fork","jobId":"PENDING-JOB","requestId":"NEW-UUID","name":"Second result"}
```

Fork inserts a separate named reservation directly below the source reservation in its current
container. It reuses the immutable capture files, frozen selection and selectionMode, without a new render or
sampling current pixels. Each fork has independent job/layer IDs, cancellation and completion.
Only pending newLayer jobs can be forked; fork before completing the source. Deleting the source
afterward does not cancel its forks. requestId is idempotent with identical JSON. Name defaults to
the source reservation's current name. Canvas resize and existing resource limits are checked.

## Capture a selected region

```json
{
  "apiVersion":1,"op":"begin","sessionId":"SESSION",
  "requestId":"another-uuid","name":"Background repair",
  "source":"merged","area":"selection","padding":48,
  "destination":"newLayer"
}
```

`capture.imagePath` is a cropped PNG with surrounding context. `capture.maskPath`, when present,
is a matching grayscale mask of the frozen selection. Its meaning depends on `selectionMode`:

- `strict` (default): white means editable, black means preserve. The API enforces this mask on
  completion as well as candidate previews; soft selection values become partial coverage.
- `guide`: white marks the preferred subject area; black is surrounding context, not protected pixels.
  The selection is a reference for generation, not an enforced editing mask. Result pixels and shadows
  can extend outside it, **within the padded capture crop**. The API does not automatically remove
  the returned background; request transparent surroundings when generating a cutout on a new layer.

Choose strict for precise masked edits or explicit outside-area protection. Choose guide for object
placement with shadows/soft edges that need extra room. A request to fill the selection with the main
object need not constrain its shadow. Keep unrelated surroundings unchanged in the generation prompt.
`capture.selectionMode`, `capture.maskEnforced` and `capture.maskMeaning` report the frozen policy;
Canvas captures have null selectionMode; maskEnforced is true when an application mask is supplied. Unknown modes are errors.

For example, reserve a new object with room for its shadow:

```json
{
  "apiVersion":1,"op":"begin","requestId":"new-object-uuid","name":"Balcony",
  "source":"merged","area":"selection","selectionMode":"guide","padding":160
}
```

Choose padding before capture; both modes remain bounded by the canvas. In guide mode the generator
must not use the selection as a hard editing mask. Without an application mask, `replacePixels` replaces **the entire crop**,
including black-mask context and any transparent pixels in the result. Prefer newLayer for additions
unless replacing this wider area is part of the user's request. Preview before delivery when useful.

`capture.region` is `[x,y,width,height]` in **bottom-left canvas pixels**. PNGs have ordinary image
orientation; do not manually flip them. Generate the entire context crop, not just its white mask area.
Capture pixels are sRGB 8-bit PNG, excluding display channels, EV and Post FX. HDR is clamped for the
generation input. The original HDR document is not changed by capture.
Single-layer capture includes that layer's transform, ChannelMapping and FX before its outer opacity/blend,
even if it is hidden. Groups are isolated color sources; child visibility is respected.

Selection, its interpretation, crop bounds and source pixels are frozen at begin. Later selection changes do not change the task.
Context files live under `Temp/WhimTex/Agent/<jobId>/`, not Assets. Returned paths are absolute.

### Separate guide and application masks

For approximate feedback, keep the guide for generation and explicitly limit where the result may
be applied. JSON `begin` accepts `applicationMask` alongside `selectionMode:"guide"`:

```json
{
  "apiVersion":1,"op":"begin","sessionId":"SESSION","requestId":"repair-uuid",
  "source":"layer","sourceLayerId":"DRAWING-ID","area":"commentMask",
  "maskRef":{"maskId":"MASK-ID","expectedRevision":"MASK-REVISION"},
  "selectionMode":"guide","padding":48,"destination":"replacePixels","targetLayerId":"DRAWING-ID",
  "applicationMask":{"fromGuide":true,"expandPixels":4,"featherPixels":3}
}
```

`applicationMask` requires exactly one of `fromGuide:true` or a separate
`maskRef:{maskId,expectedRevision}`. `expandPixels` and `featherPixels` are integers 0..512,
default 0, measured on the canvas. Expansion grows every nonzero mask pixel by Euclidean distance;
feather adds an outward linear transition after that radius. With both zero, soft coverage is
preserved exactly. Expansion promotes covered pixels to full coverage; feather alone retains
existing soft pixels. Holes can shrink as the mask grows. Inspect the exported coverage.

The capture crop includes the application mask even if it extends beyond the guide's padded bounds.
`capture.applicationMaskPath` is a PNG aligned with that crop: coverage is in RGB, alpha is opaque.
`capture.applicationMask` describes its settings, and `capture.applicationMaskRef` identifies a
separate attached mask when used. Both references are checked again on preview, fork and completion.
Application coverage clips new Drawing pixels or blends replacements with original pixels;
zero coverage preserves the target's source pixels. Existing layer FX may still spread visually
outside the mask. It does not mask global FX/settings edits. Forks inherit the frozen masks.
Strict mode already enforces its selection and rejects an additional application mask.

Guide mode without `applicationMask` retains its unrestricted padded-crop behavior. Do not use
large padding alone to protect surrounding pixels. Choose the smallest expansion/feather needed
for the requested edge and inspect the comparison before applying.

## Complete with an image

```json
{
  "apiVersion":1,"op":"complete","jobId":"JOB",
  "imagePath":"C:/Temp/generated.png","fit":"stretch"
}
```

PNG input is decoded directly into owned Drawing pixels at its original resolution; there is no
intermediate File layer or imported source asset. New images become Standard Drawing layers. The result is not automatically
saved. The user can inspect it, paint on it and save normally.

`fit` is `contain` or `stretch`. For new layers it sets Transform scale and position, never resizes
the stored image. Contain preserves aspect ratio and centers within the captured region; stretch
fits the complete generated image to that region. Defaults: contain for a full canvas, stretch for
a selection crop. For regional edits return the full captured crop with the same framing. In strict
mode the frozen selection is mapped into source pixels and multiplied into their alpha; RGB and source
dimensions are preserved. Selection edges have the precision of the generated source's pixel grid.
Guide mode preserves the returned RGBA without mask multiplication and uses the same crop transform.
For `replacePixels`, the existing target's resolution is retained and nonmatching input is resampled
in linear, premultiplied color. PNGs are interpreted as sRGB
color, not raw linear data textures. PNG input is limited to 64 MiB and 16,777,216 pixels.

For `replacePixels`, only the original Drawing's pixel content is updated. Its identity, transform,
blend, FX, name and visibility remain. The selection is mapped into Drawing coordinates; rotation
and scale are supported. Repeating/mirrored transforms are rejected because one stored pixel can
appear both inside and outside the selected region. Use a new layer for such edits. A transformed
source can still be sampled for a new-layer result. Existing FX and channel mappings still apply after
replacement; prefer a new layer when editing their already-processed appearance.

Pixel replacement checks the target's content/settings fingerprint. Independent edits to other layers,
renaming, visibility and reordering do not invalidate it. If the target changed, `revision_conflict`
leaves the reservation intact. Do not refresh the fingerprint and overwrite the user's newer work:
start a new-layer job, or recapture after agreeing on a new edit.

## Complete with layer parameters

```json
{
  "apiVersion":1,"op":"complete","jobId":"JOB",
  "layer":{
    "type":"noise",
    "settings":{"noise":{"scale":8,"seed":472}}
  }
}
```

`layer` accepts `type`, `settings`, `transform`, `fx`, and effect-only `input`/`target`. `input` accepts `Previous`, `Specific` (default), or `AllBelow`; only Specific accepts a target. AllBelow reads visible lower siblings composited on transparency within the current group, not an external backdrop, and remains stack-relative after placement changes. Types and settings
are the same as regular API `add`; discover defaults with `whimtex_describe` and consult the
[API reference](AgentAPI.md). `settings.name` and `settings.enabled` are forbidden: those belong to
the reservation and may already have been changed by the user. Groups start empty. Shader Processor
uses the same inline `fx` contract as other layers, including groups.

Supply exactly one of `layer` or `imagePath`. Parameter completion requires `area:canvas` and
`destination:newLayer`. For selection-masked or pixel-replacement jobs, complete with an image.

## Inline Shader FX

Use a `shaderProcessor` for an effect on the composed lower stack; use `fx` on a normal layer to process
just that layer. Reserve with `begin` first, then preview or complete with code embedded in the request:

```json
{
  "apiVersion":1,"op":"complete","jobId":"RESERVATION",
  "layer":{"type":"shaderProcessor","fx":[{
    "code":"// @param float _Gain = 1.25\nfloat4 ApplyFX(float2 uv, float4 color) { color.rgb *= _Gain; return color; }"
  }]}
}
```

For generated Drawing content, `imagePath` and `fx` can be supplied together at the request root.
For parameter layers, put `fx` inside `layer` or at the root, never both. Pixel-replacement jobs do
not accept FX changes; lock the existing layer for a separate FX edit instead.

Each `fx` entry has `op` (`add` by default, `replace`, `remove`) and an optional FX `index`.
Add appends by default, or inserts at the specified index. Replace/remove require an explicit index
from the layer's `fx` snapshot. Indices refer to the document-owned HLSL FX list,
and each operation uses the list after the preceding operation. Unmentioned entries stay unchanged.
Replace copies the new code into a fresh document-owned inline FX with independent values.
Remove accepts only op/index. Add/replace accept `code`; define parameters with HLSL `@param` declarations.

Non-null `Layer.fx` entries are `ShaderFX` instances; persistent JSON also permits internal `$ref`
references to these document models, but never `$asset` FX items. External Material, Shader and ShaderFX `.asset`
references are unsupported; old JSON/TIFF containing them is rejected with a diagnostic, without
migration or source-document rewrite. Internal Materials and compiled Unity Shaders still execute
the HLSL. Catalog discovery and linked project/package versus embedded user-library presets are
described in [FX edits and presets](AgentAPI.md#fx-edits-and-presets).

- Code: 1..65,536 characters of HLSL with `float4 ApplyFX(float2 uv, float4 color)`.
- `color` and `SampleInput(uv)` are straight RGBA in linear working space. Return straight RGBA;
  preserve `color.a` unless changing transparency is intentional. Layer opacity/blend run afterwards.
- `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`, `_RenderScale` are supplied by the
  editor. `_InputSize`/`_CanvasSize` hold width, height, reciprocal width, reciprocal height.
  `_RenderScale` is full-size pixels per preview pixel. `UnityCG.cginc` is already included.
- [FastNoiseLite](AI/README.md#built-in-noise-library) is also included automatically, with no explicit
  include needed. Prefer it for spatial noise when suitable; custom algorithms remain valid.
- `#include` works with existing Assets/Packages paths and paths relative to the document (Assets
  before its first save). Prefer explicit project paths. An FX does not require its own shader file.
- Declare at most 128 parameters in code using [HLSL metadata](ShaderFX.md#parameter-declarations),
  including their initial values. Do not redeclare the generated uniforms. The first-line catalog
  marker is required only for catalog files. This reservation/lock workflow does not accept a
  `parameters` array, value overrides, `presetId` or an FX `set` operation.
- To change values after completion, inspect the finished layer and use the shared
  [FX editing operations](AgentAPI.md#fx-edits-and-presets) through `whimtex_assistant_execute`.
  There, `parameters` is a name/value object. The shared contract defines supported value types,
  ranges, gradients and texture/layer references. Do not submit inspection's parameter records
  directly as an operation. Finish pending jobs/locks before starting this immediate batch.
- At most 16 FX operations per request and 32 resulting FX entries. Keep GPU work bounded:
  no unbounded loops or enormous per-pixel sampling loops. Successful compilation does not prove
  that a shader is fast or numerically stable; inspect a small preview and use HDR Debug as needed.

The API compiles a temporary GPU shader before delivery. This is not a project/C# recompilation,
AssetDatabase refresh or an exported `.shader` file. Compile errors return `shader_compile_failed`
with compiler diagnostics; the live content stays unchanged, and the pending job can be corrected.
Previews do not publish candidate effects or add Undo entries. Completion is one Undo action.
Embedded code, parameters and compiled shader follow the document's usual save lifecycle.
Inspection exposes each FX's code, parameters, diagnostics and pending-change state.
Each Shader FX also exposes structured `diagnosticMessages`, `warnings` and `errors` with the same
severity/message/location as normal Apply and Console. The `diagnostics` text remains available;
Console deduplication never hides records from inspection.
Each Shader FX includes `locality: {status, reasons}` for its **applied, usable shader**: `pixelLocal`,
`spatial` or `unverified`. Reasons have stable codes, readable messages and source locations when known.
Pending edits do not replace this result; a failed Apply or an uncompiled FX returns `locality:null`.
Read `pendingChanges` and `lastApplyFailed` alongside it. Disabled FX retain their classification.
The same result is available from `whimtex_fx_compile` before modifying a document. See the
[locality contract and reason codes](AgentAPI.md#applied-shader-locality); classification does not
describe the full render plan of dependent layers or inspector previews.
Parameters also expose stable `id`, nullable `minimum`/`maximum`, and structured Transform2D values.
`catalogPath` identifies a linked HLSL source when present; inline replacement does not modify that source.
Groups support FX directly. FX process the combined children before group opacity and blending and automatically isolate a Pass Through group using Normal blending.

## Edit and lock an existing layer

For custom FX code or layer-settings candidate edits, do not create a replacement placeholder:
lock the existing layer's content instead. Linked preset insertion and parameter-only FX `set`
use immediate editing batches **without a lock**. The lock's `changes.fx` schema does not accept
`presetId` or `set`, and an active lock blocks immediate batches. A sequence of immediate `set`
and render/probe calls edits the live document with Undo; it is not a detached candidate preview.

```powershell
unity command whimtex_assistant_lock --requestId 'NEW-UUID' --layerId 'LAYER-GUID' --project-path 'D:/Projects/MyGame' --format json
```

Direct equivalent: `WhimTexApi.LiveLock(requestId, layerId, sessionId, expectedRevision)`.
Or send `op:lock` with requestId/layerId, optional sessionId and expectedRevision through LiveJson/File.
The optional expectedRevision is the layer's **contentRevision**, not the document revision.
Automatic window choice and idempotent request retries follow begin's rules. The returned job owns
the lock, and `context.layer` contains the captured layer settings and FX. Nothing new is inserted;
the layer keeps rendering its previous result while the agent works.

```json
{
  "apiVersion":1,"op":"complete","jobId":"EDIT-JOB",
  "changes":{"fx":[{
    "op":"add","code":"float4 ApplyFX(float2 uv, float4 color) { return float4(1 - color.rgb, color.a); }"
  }]}
}
```

Use `op:preview` with the same changes and optional view/maxSize/outputPath for a detached trial
(default view is composite). `changes` accepts `settings`, `transform`, `fx`; name/visibility are
not editable through this job. The layer's ID, pixels, type, tree position and unrelated FX remain.
This lock workflow does not replace Drawing pixels: use begin/replacePixels for that operation.

While locked, painting, fill, transform, opacity/blend and settings/FX editing are disabled for this
layer. Users may still rename, hide, move or delete it. A group's lock covers its own settings, not
its children; children can continue changing. Destructive conversions/merges involving a locked layer
are blocked. Path-based batches targeting its saved document are rejected while the edit is pending.
Other live jobs may work on other layers, but cannot lock or replace pixels on this target concurrently.

Successful completion releases the lock. `{"apiVersion":1,"op":"unlock","jobId":"EDIT-JOB"}`
releases it without changes and is safe to repeat. The user can choose **Cancel Agent Edit** in Layer
Settings or the context menu; `cancel` with sessionId/layerId also works. A terminal `fail` releases
an edit lock. If pausing or abandoning work, unlock/fail rather than leaving the user waiting.
Locks are session-local, not saved: close/switch, deletion and Undo/Redo cancel them;
script reload interrupts them with explicit checked recovery as described below.
Redo never reacquires one. A late completion cannot revive a cancelled job. Save As transfers the
job to the new document copy. If content changed by another route, `revision_conflict` prevents
overwriting it: unlock, inspect and agree on a new edit rather than bypassing the check.

## Optional trial renders

Before completing, use the same candidate with `op:preview`:

```json
{
  "apiVersion":1,"op":"preview","jobId":"JOB","view":"composite","maxSize":768,
  "layer":{"type":"noise","settings":{"noise":{"scale":8,"seed":472}}}
}
```

`view` is `layer` (default) or `composite`. The candidate replaces the reservation only on a detached
copy. Its name/visibility/placement are current, and other layers reflect the document at preview time.
The live document and its Undo history are unchanged. The trial returns an absolute `outputPath`.
No trial is mandatory: use it when visual feedback helps choose parameters. If the user hid the
reservation, a composite preview also respects that visibility. A layer-only preview still shows
its content, so hiding a reservation does not prevent the agent from checking it.

Add `review:true` to a generation or edit-lock preview to also receive a `review` bundle:

- `beforePath`, `afterPath`: current and candidate composites as transparent PNGs.
- `comparisonPath`: before on the left, after on the right, over checkerboard.
- `differencePath`: amplified visual difference; `changedOverlayPath`: orange changed-area overlay.
- `changedMaskPath`: binary RGB coverage of changed samples; alpha is opaque.
- `changedPixels`, `outsideGuidePixels`, `outsideApplicationPixels`: sample counts; absent masks
  produce null outside counts. `sampled:true` means the images are smaller than the canvas.
- `attachment`: a ready-to-use `{maskImagePath,channel:"red",bounds}` for a comment reply, or null
  when no visible change was detected. Regenerate if the candidate or live document changes.

The comparison uses premultiplied sRGB and alpha differences greater than 1/255 at the preview
resolution. It is a visual review, not an exact HDR/source-pixel audit. Small changes may disappear
in a reduced preview. Outside counts compare pixel centres; filtering and downstream FX can extend
beyond application coverage. Inspect the images together with the counts. `maxSize` controls the
cost and detail; the original document and its Undo history remain unchanged.

To inspect a current document without a candidate:

```json
{"apiVersion":1,"op":"render","sessionId":"SESSION","maxSize":1024}
```

Optional `sourceLayerId` renders one layer. Both render commands accept `maxSize` 1..4096 and optional
project-relative `outputPath` under `Temp/WhimTex/*.png`. Files never overwrite; omit the path
for an automatically unique name. These renders require a graphics device.

## Status, cancellation and recovery

```json
{"apiVersion":1,"op":"status","jobId":"JOB"}
```

Status returns `pending`, `completed`, `cancelled`, or `interrupted` with `recoverable:true` after script reload. On a generation failure, report it to the user
and optionally attach a message without discarding the reservation:

```json
{"apiVersion":1,"op":"fail","jobId":"JOB","message":"Image generation failed; waiting for retry."}
```

The user can cancel from Layer Settings or delete the row. Agents can cancel active or interrupted
reservations by session and layer ID:

```json
{"apiVersion":1,"op":"cancel","sessionId":"SESSION","layerId":"RESERVATION"}
```

After script reload, rediscover `whimtex_assistant_sessions`: each original open document lists
`recoverableJobs`. Resume using its **current** session ID and the original job ID:

```json
{"apiVersion":1,"op":"resume","sessionId":"CURRENT-SESSION","jobId":"ORIGINAL-JOB"}
```

Resume reuses the original reservation/edit target and frozen capture/guide/application buffers.
It never creates a layer or recaptures pixels. It checks the original document instance, canvas size,
source layer content/order, target revision, feedback and attached-mask revisions before enabling
delivery. Changed source produces `revision_conflict`; changed masks produce `mask_revision_conflict`.
Keep the generated image, inspect the new state and start a new capture when appropriate. Cancellation,
deletion, closing/switching documents and Undo cannot be bypassed by resume. An interrupted edit lock
can also be cancelled with sessionId/layerId. Repeating successful resume is safe.

Recovery lasts only within the current Editor process and requires its temporary mask files.
It does not survive restarting Unity or cleaning Temp; it is not saved in TIFF. Missing/corrupt
recovery data returns `recovery_unavailable`. Closed documents are never reopened automatically.

- Completion is one Undo action. Its repeated identical request returns the completed status without
  inserting again. A different completion request for an already-completed job is rejected.
- Removing/undoing a reservation cancels its delivery. Redo does not restart generation. Undoing a
  completed result restores an inactive reservation; cancel it or start a new job, rather than replaying
  an old completion. A `completed` status acknowledges that the command ran, not that its result has
  never subsequently been undone or deleted.
- Closing/switching the source window cancels delivery. Script reload retains checked recovery data;
  the agent must explicitly resume. Serialized reservations remain recognizable and removable.
  Saving normally keeps jobs alive; Save As transfers active jobs to the newly saved copy in that window.
- `document_busy` means wait for the current paint/transform/selection/drag gesture or delayed text edit
  to end, then retry the same request. No command ends the user's gesture or discards typed text.
  Canvas resize produces a conflict; cancel and capture again.
- After timeout, inspect status before retrying. Do not create a second reservation merely because the
  first response was lost. After an unknown job/session error, rediscover and inspect; never silently
  route a late result into another document.
- `rollback_failed` reports `applied:true` conservatively because the document may be partially changed.
  Stop automatic retries and inspect the document before attempting recovery.
- Limits: 16 pending jobs, 64 MiB total retained guide/application coverage, 512 job receipts per script session,
  1024 layers, 16,777,216 canvas pixels and 67,108,864 owned Drawing pixels. Preview/capture pixels are
  transient and are not stored in Undo. Temporary PNG files remain available for inspection until Temp
  is cleaned; the API does not delete user-provided images.

## Agent guidance

`LayerToLocal(uv)` converts canvas UV to the owning layer's local UV, including parent group transforms and perspective. Use it for procedural shapes that must follow the layer transform. `ApplyFX` UV and `SampleInput` remain canvas-space; do not pass local UV to `SampleInput`. The helper does not wrap or clamp coordinates.

The portable skill is [Skills~/whimtex-live/SKILL.md](https://github.com/DCFApixels/WhimTex/blob/main/Skills~/whimtex-live/SKILL.md).
It describes intent-based source selection, reservation ownership and recovery without requiring
an embedded chat UI or a separate model integration inside Unity.
