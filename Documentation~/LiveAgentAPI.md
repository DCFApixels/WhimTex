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
Edit Mode. They never trigger compilation, refresh the AssetDatabase, or save the document.

## Connect and discover

For an authorized content-generation request, reserve and capture context in one call:

```powershell
unity command whimtex_begin --requestId 'NEW-UUID' --name 'Balcony' --source merged --area selection --project-path 'D:/Projects/MyGame' --format json
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
unity command whimtex_sessions --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_live --requestPath 'D:/Projects/MyGame/Temp/WhimTex/request.json' --project-path 'D:/Projects/MyGame' --format json
```

Direct equivalents, without Pipeline:

```csharp
WhimTexApi.LiveSessions();
WhimTexApi.LiveFile(absoluteRequestPath);
WhimTexApi.LiveJson(requestJson);
```

Use the `DCFApixels.WhimTex` namespace. Check the returned JSON `success` as well as transport
success. Read-only discovery returns `sessions`, each with `sessionId`, name, assetPath, dimensions
and focused status, plus `focusOrder` (0 means no recorded focus). A blank assetPath is an unsaved document. If multiple documents are open, use
the user's requested document; ask when ambiguous. Do not guess from a layer name.

```json
{"apiVersion":1,"op":"inspect","sessionId":"SESSION"}
```

Inspection returns the regular document snapshot, `activeLayerId`, `selectedLayerIds` and the current
canvas `selection`. Pending entries expose `jobId`, `contentLocked` and status text. The session is
pinned to that window/document pair: switching documents, closing the window or reloading scripts
invalidates delivery. Discovery does not open windows or change selection.

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
| `area` | `canvas` (default) or `selection`; selection must be active and nonempty |
| `selectionMode` | Selection only: `strict` (default) enforces the mask; `guide` treats it as a placement reference |
| `padding` | Context around selection bounds, 0..4096 pixels; defaults to 32 for strict, 128 for guide. Selection only. JSON does not accept the CLI's -1 sentinel |
| `destination` | `newLayer` (default) or `replacePixels` |
| `targetLayerId` | Explicit Drawing ID required for `replacePixels` |

The source and destination are independent. Choose them from the user's request. For example,
removing an object from the visible composition usually needs `source:merged`; fixing the content
of a particular layer may need `source:layer`. Do not require all tasks to use Copy Merged.

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
canvas captures have null selectionMode and false maskEnforced. Unknown modes are errors.

For example, reserve a new object with room for its shadow:

```json
{
  "apiVersion":1,"op":"begin","requestId":"new-object-uuid","name":"Balcony",
  "source":"merged","area":"selection","selectionMode":"guide","padding":160
}
```

Choose padding before capture; both modes remain bounded by the canvas. In guide mode the generator
must not use the selection as a hard editing mask. With `replacePixels`, **the entire crop is replaced**,
including black-mask context and any transparent pixels in the result. Prefer newLayer for additions
unless replacing this wider area is part of the user's request. Preview before delivery when useful.

`capture.region` is `[x,y,width,height]` in **bottom-left canvas pixels**. PNGs have ordinary image
orientation; do not manually flip them. Generate the entire context crop, not just its white mask area.
Capture pixels are sRGB 8-bit PNG, excluding display channels, EV and Post FX. HDR is clamped for the
generation input. The original HDR document is not changed by capture.
Single-layer capture includes that layer's transform, Swizzle and FX before its outer opacity/blend,
even if it is hidden. Groups are isolated color sources; child visibility is respected.

Selection, its interpretation, crop bounds and source pixels are frozen at begin. Later selection changes do not change the task.
Context files live under `Temp/WhimTex/Agent/<jobId>/`, not Assets. Returned paths are absolute.

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
source can still be sampled for a new-layer result. Existing modifiers and swizzles still apply after
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

`layer` accepts `type`, `settings`, `transform`, `fx`, and effect-only `input`/`target`. Types and settings
are the same as regular API `add`; discover defaults with `whimtex_describe` and consult the
[API reference](AgentAPI.md). `settings.name` and `settings.enabled` are forbidden: those belong to
the reservation and may already have been changed by the user. Groups start empty. Shader Processor
uses the same inline `fx` contract as other nongroup layers.

Supply exactly one of `layer` or `imagePath`. Parameter completion requires `area:canvas` and
`destination:newLayer`. For selection-masked or pixel-replacement jobs, complete with an image.

## Inline Shader FX

Use a `shaderProcessor` for an effect on the composed lower stack; use `fx` on a normal layer to process
just that layer. Reserve with `begin` first, then preview or complete with code embedded in the request:

```json
{
  "apiVersion":1,"op":"complete","jobId":"RESERVATION",
  "layer":{"type":"shaderProcessor","fx":[{
    "code":"float4 ApplyFX(float2 uv, float4 color) { color.rgb *= _Gain; return color; }",
    "parameters":[{"name":"_Gain","type":"Float","value":1.25}]
  }]}
}
```

For generated Drawing content, `imagePath` and `fx` can be supplied together at the request root.
For parameter layers, put `fx` inside `layer` or at the root, never both. Pixel-replacement jobs do
not accept FX changes; lock the existing layer for a separate FX edit instead.

Each `fx` entry has `op` (`add` by default, `replace`, `remove`) and an optional modifier `index`.
Add appends by default, or inserts at the specified index. Replace/remove require an explicit index
from the layer's `fx` snapshot. Indices refer to the entire modifier list, including Material references,
and each operation uses the list after the preceding operation. Unmentioned entries stay unchanged.
Replace copies the new code into a fresh embedded FX; it never edits a shared external asset.
Remove accepts only op/index. Add/replace accept `code` and optional `parameters`.

- Code: 1..65,536 characters of HLSL with `float4 ApplyFX(float2 uv, float4 color)`.
- `color` and `SampleInput(uv)` are straight RGBA in linear working space. Return straight RGBA;
  preserve `color.a` unless changing transparency is intentional. Layer opacity/blend run afterwards.
- `_MainTex`, `_MainTex_TexelSize`, `_InputSize`, `_CanvasSize`, `_PreviewScale` are supplied by the
  editor. `_InputSize`/`_CanvasSize` hold width, height, reciprocal width, reciprocal height.
  `_PreviewScale` is full-size pixels per preview pixel. `UnityCG.cginc` is already included.
- `#include` works with existing Assets/Packages paths and paths relative to the document (Assets
  before its first save). Prefer explicit project paths. An FX does not require its own shader file.
- Parameters: at most 32, each with `name`, `type` and `value`. Types: `Bool` (JSON `true`/`false`, sent to HLSL as float `0`/`1`), `Float` (number), `Color`
  (encoded RGB + alpha, converted to linear for the shader), `Vector` (four raw values), `Texture2D`
  (existing Assets/Packages texture path). Texture uniforms include `<name>_TexelSize`.
  Use valid unique HLSL identifiers; do not redeclare the generated uniforms in code.
- `Transform2D` accepts `value: {"position":[0.5,0.5],"size":[1,1],"rotation":0}`; fields are optional.
  Position/size are normalized to the input image, rotation is in degrees. Size components must have
  magnitude at least `0.00001`. Generates `<name>_ToLocal(uv)` and `<name>_ToInput(uv)` helpers.
- `Gradient` accepts the same gradient value (color-stop array or object with `colors`, `alphas`,
  `mode`, `smoothness`, `colorSpace`) as layer gradients. Generates `<name>_Sample(t)` with clamped
  0..1 input and straight linear RGBA output. Code declarations use `// @param gradient _Ramp`
  without an initializer; omitted API overrides leave the opaque black-to-white default.
- Inline code may instead declare parameters using [HLSL metadata](ShaderFX.md#parameter-declarations).
  If JSON values are supplied as well, every entry must match a code declaration by name and type;
  those values override defaults. The first-line catalog marker is required only for catalog files.
- At most 16 FX operations per request and 32 resulting modifier entries. Keep GPU work bounded:
  no unbounded loops or enormous per-pixel sampling loops. Successful compilation does not prove
  that a shader is fast or numerically stable; inspect a small preview and use HDR Debug as needed.

The API compiles a temporary GPU shader before delivery. This is not a project/C# recompilation,
AssetDatabase refresh or an exported `.shader` file. Compile errors return `shader_compile_failed`
with compiler diagnostics; the live content stays unchanged, and the pending job can be corrected.
Previews do not publish candidate effects or add Undo entries. Completion is one Undo action.
Embedded code, parameters and compiled shader follow the document's usual save lifecycle.
Inspection exposes each FX's code, parameters, diagnostics and pending-change state.
Parameters also expose stable `id`, nullable `minimum`/`maximum`, and structured Transform2D values.
`catalogPath` identifies a linked HLSL source when present; inline replacement does not modify that source.
Groups do not directly render FX; place a Shader Processor inside a group for that workflow.

## Edit and lock an existing layer

Do not create a replacement placeholder for an existing layer's FX. Reserve its own content instead:

```powershell
unity command whimtex_lock --requestId 'NEW-UUID' --layerId 'LAYER-GUID' --project-path 'D:/Projects/MyGame' --format json
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
Locks are session-local, not saved: close/switch, deletion, script reload and Undo/Redo cancel them;
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

Status returns `pending`, `completed` or `cancelled`. On a generation failure, report it to the user
and optionally attach a message without discarding the reservation:

```json
{"apiVersion":1,"op":"fail","jobId":"JOB","message":"Image generation failed; waiting for retry."}
```

The user can cancel from Layer Settings or delete the row. Agents can cancel active or interrupted
reservations by session and layer ID:

```json
{"apiVersion":1,"op":"cancel","sessionId":"SESSION","layerId":"RESERVATION"}
```

- Completion is one Undo action. Its repeated identical request returns the completed status without
  inserting again. A different completion request for an already-completed job is rejected.
- Removing/undoing a reservation cancels its delivery. Redo does not restart generation. Undoing a
  completed result restores an inactive reservation; cancel it or start a new job, rather than replaying
  an old completion. A `completed` status acknowledges that the command ran, not that its result has
  never subsequently been undone or deleted.
- Closing/switching the source window or reloading scripts interrupts delivery. Serialized reservations
  remain recognizable and removable, but jobs/masks are session-local and are not resumed automatically.
  Saving normally keeps jobs alive; Save As transfers active jobs to the newly saved copy in that window.
- `document_busy` means wait for the current paint/transform/selection/drag gesture or delayed text edit
  to end, then retry the same request. No command ends the user's gesture or discards typed text.
  Canvas resize produces a conflict; cancel and capture again.
- After timeout, inspect status before retrying. Do not create a second reservation merely because the
  first response was lost. After an unknown job/session error, rediscover and inspect; never silently
  route a late result into another document.
- `rollback_failed` reports `applied:true` conservatively because the document may be partially changed.
  Stop automatic retries and inspect the document before attempting recovery.
- Limits: 16 pending jobs, 64 MiB total retained selection coverage, 512 job receipts per script session,
  1024 layers, 16,777,216 canvas pixels and 67,108,864 owned Drawing pixels. Preview/capture pixels are
  transient and are not stored in Undo. Temporary PNG files remain available for inspection until Temp
  is cleaned; the API does not delete user-provided images.

## Agent guidance

The portable skill is [Skills~/whimtex-live/SKILL.md](https://github.com/DCFApixels/WhimTex/blob/main/Skills~/whimtex-live/SKILL.md).
It describes intent-based source selection, reservation ownership and recovery without requiring
an embedded chat UI or a separate model integration inside Unity.
