---
name: whimtex-live
description: Create generated images or procedural layers, add linked FX presets, edit FX parameters or custom code, and repair selected regions in an open WhimTex document. Use the live API while the user continues editing; not for modifying plugin source code.
---

# Live WhimTex editing

The API namespace is `DCFApixels.WhimTex`. WhimTex retains the `whimtex_*` command names.
The skill ID remains `whimtex-live` so existing installations keep working.

Use the installed `Packages/com.dcfapixels.whimtex` package. This skill contains the complete
fast-start contract below. Read `Documentation~/LiveAgentAPI.md` for completion, previews, advanced
begin options and recovery; read `Documentation~/AgentAPI.md` for layer parameters and shared
editing operations. Batch, Headless Live and Assistant have different persistence/retry contracts.

## Choose the workflow before reserving

- Known layer settings, linked preset insertion and parameter-only FX changes: use the shared
  `whimtex_assistant_execute` operations after inspection, without a reservation or edit lock.
  This includes adding a procedural layer and its linked FX together in one batch.
- Lengthy image/procedural generation or new custom shader code: reserve the output early as below.
- Custom code or layer-settings edits requiring a detached candidate: lock the existing layer.
  Lock completion does not support linked `presetId` insertion or parameter-only FX `set`.

Parameter-layer completion requires `area:canvas` and `destination:newLayer`. Selection jobs,
including `guide`, and pixel replacement require `imagePath`. If the user requires both procedural
editability and an exact selection mask, do not silently rasterize or substitute the selection's
bounding rectangle; explain this completion limitation and agree on a supported alternative first.

## Reserve early for generation

When the workflow above calls for generation with a reservation, reserve before prompt polishing,
image-tool preparation, full layer inspection or reading unrelated API sections. Follow required
project/tool instructions first. Known settings/preset batches use the immediate workflow above;
do not reserve for questions, inspection-only requests or plugin source changes.

For custom FX code or layer-settings edits requiring a detached candidate, use `whimtex_assistant_lock` instead of inserting a placeholder
(see Inline Shader FX below). Reserve only the layer actually being edited, not the whole document.

Once the connected command is known to be available, use one call (no request file needed):

```powershell
unity command whimtex_assistant_begin --requestId 'NEW-UUID' --name 'Balcony' --source merged --area selection --project-path 'D:/Projects/MyGame' --format json
```

Generate a unique requestId before calling. Reuse the exact arguments on a transport retry.
The command selects the only open document, or the currently focused WhimTex window when
several are open. Only if none is focused, it falls back to the most recently focused open window.
Absent or tied focus history fails without writing; then list sessions and ask which document.
An explicit `--sessionId` pins a known document. Never substitute a different project or document.

- Independent new artwork or a parameter layer: `--source none --area canvas` (the defaults).
- New content matching the visible image inside the user's selection: `--source merged --area selection`.
- A specifically requested layer source: `--source layer --sourceLayerId GUID`; omit sourceLayerId
  only when the user means the active layer. Choose canvas/selection from the request, not automatically.
- With a selection, choose `--selectionMode strict` (default) for a hard edit boundary, or
  `--selectionMode guide` for approximate placement with shadows or details outside it. Explicit
  instructions to preserve everything outside the selection take priority. Choose from the task
  before reserving; this does not require a separate inspection. See the image guidance below.
- `--padding` reserves context around the selection in canvas pixels (0..4096). Defaults: 32 for
  strict, 128 for guide. Allow enough space for the intended shadow/overhang; output cannot extend
  beyond the captured crop, which is bounded by the canvas.
- This shortcut always creates a new layer at the root top. For replacement of existing pixels or
  explicit parent/index, use the documented JSON `begin` instead.

The compact result returns jobId, sessionId, layerId, frozen context (active/selected layer IDs and
selection bounds), and capture image/mask paths when requested. Check nested API `success` as well
as transport success. Keep these IDs; an ordinary generation does not need a full document snapshot.
Read/inspect additional details after reserving only when they matter to the requested result.

If more output layers are needed, `whimtex_assistant_live` accepts this JSON in a request file:

```json
{"apiVersion":1,"op":"fork","jobId":"PENDING-JOB","requestId":"NEW-UUID","name":"Second result"}
```

Fork before completing the source reservation. It inserts a new independent reservation immediately
below that reservation in its current group, sharing the frozen capture/selection without rendering
again. It also retains selectionMode and crop bounds. Each result has its own jobId/layerId and lifecycle.
Do not use ordinary layer duplication for placeholders. To sample a different source, begin a new job.

## Connect to the user's work

- Respect project compilation and asset rules. Missing commands do not authorize recompilation,
  installing Pipeline or starting a different Editor. Use the existing CLI adapter or a C# eval bridge.
- Discover `whimtex_assistant_begin` when commands are not already known. Use `whimtex_assistant_sessions`
  and full inspection when disambiguation or specific existing-layer details are needed, not as a
  mandatory prelude to every generation. Always specify the intended Unity project path.
- A session can be unsaved. Do not save it or select another document as a workaround. The live API
  handles its in-memory model. Check JSON `success` independently of transport success.

## Reserve, prepare, deliver

The reserved name can be provisional; do not delay insertion to invent a perfect name. Preserve any
subsequent user rename. Retain jobId/sessionId/layerId rather than locating a row by name or index.

Choose source, area and destination from the request, not a universal recipe:

- New independent artwork: usually no source, full canvas, new layer.
- Artwork that must fit the existing scene: capture merged or relevant-layer context as appropriate.
- Parameter-based content such as noise: use a layer specification. Optionally preview a candidate
  and inspect the returned PNG before refining parameters. No image generator is necessary.
- Regional repair: capture the selection with context. Use merged sampling for visible-composition
  changes, or a particular layer when the request concerns its contents. New-layer output preserves
  the original. Replace existing Drawing pixels only when the requested edit calls for that.

For image generation/editing, use an available image tool with the captured image/mask and request a
local PNG. Selection interpretation is task-dependent:

- **Strict:** precise masked repair, or an explicit request not to change pixels outside the selection.
  White means editable, black means preserve; adapt to the tool's mask convention. The editor also
  clips the returned result to the frozen mask.
- **Guide:** the selection indicates the main object's approximate position/size, while shadows,
  soft edges or protruding details may extend outside it. Supplying a guide as an enforced editing
  mask would defeat this mode: use it as a visual reference and explain that black is context, not
  protected pixels. Filling the selected area with an object does not imply clipping its shadow.

Return the entire padded context crop with its framing intact, not a tightly cropped object. Keep
unrelated surroundings unchanged. For a new-layer cutout, request transparency outside the object
and its shadow; the API retains the returned alpha in guide mode and does not remove the background
automatically. Guide pixel replacement replaces the whole crop, so use it only when that matches
the requested edit; a separate layer is usually suitable for object additions.
Do not simulate complex image generation with thousands of brush commands. If image generation is
unavailable or fails, explain that and report `fail` or cancel; do not fabricate a finished layer.

Complete with imagePath or layer settings. Never overwrite the reservation's name, visibility or
placement: the user may have changed them. Reservation/lock previews are detached: do not substitute
temporary trial layers in the user's document. Render and visually inspect the final result before declaring success.

## Prefer linked FX presets

When an installed built-in or project FX already provides the requested effect, discover its exact ID
with `whimtex_fx_catalog` and add it through the shared FX operations using `presetId` and parameter
overrides. Preserve the source link; do not read a preset's HLSL and submit it as `code` just to reuse it.
For parameter-only changes, use `set`, not an inline replacement. User-library presets are embedded
by the API by design. Use raw `code` for new/custom algorithms or an intentionally independent,
modified version; explain when doing so detaches an existing preset. The reservation `layer.fx` and
`changes.fx` schemas are for inline code, not an alternative way to insert linked presets.

For a reserved procedural layer that also needs a linked preset, preview/complete the procedural
content first. Then inspect again and add the preset to the returned layerId via shared operations
once the document has no pending jobs. Preserve any user edits between these two Undo steps; do not
cancel someone else's jobs to proceed. The reservation preview alone has not tested the final FX result.

To tune an existing linked FX, use fresh inspect → `set` → render/probe without locking it. Each `set`
is a real live edit with Undo, not a private preview. If the user requires unpublished candidate trials,
explain that this linked-FX workflow has no detached parameter preview; do not copy the preset's code
into a lock replacement to simulate one. A render probe inspects current state, not proposed overrides.

## Inline Shader FX

For immediate, fully specified edits, `whimtex_assistant_execute` accepts the common layer/FX
operations with an explicit sessionId and freshly inspected document expectedRevision. It has
one Undo step, no save, and refuses pending jobs/locks. Use this for parameter-only edits,
catalog presets, reorder/copy/apply FX, duplicate/delete/merge/convert, blur and healing strokes.
Read `Documentation~/AgentAPI.md#shared-editing-operations` first; its FX `parameters` is a
name/value object, unlike the reservation workflow's array. `whimtex_fx_catalog` discovers
installed presets; `whimtex_render_probe` inspects FX input/output and channels. Do not cancel
another job to run a batch, bypass revision conflicts or replay a timed-out edit blindly.

For custom code, use the existing inline editor mechanism, not a generated `.shader` file or separate ShaderFX asset.
Read `Documentation~/LiveAgentAPI.md#inline-shader-fx` for the code/parameter and completion schema.
For a new stack effect, begin a `source:none`, `area:canvas` reservation and complete with
`layer.type:shaderProcessor` plus `layer.fx`. For a generated normal layer, include fx in its completion;
it processes that layer only. Groups also support FX directly on their combined children.

For an existing layer with a known GUID, acquire its content before writing code:

```powershell
unity command whimtex_assistant_lock --requestId 'NEW-UUID' --layerId 'LAYER-GUID' --project-path 'D:/Projects/MyGame' --format json
```

This returns jobId/sessionId and `context.layer` including existing FX code and parameters. If the
layer is not yet identified, inspect first; never choose an arbitrary layer. A lock is a mutation for
an authorized edit, not for merely reading code. A competing job returns layer_locked; do not cancel
someone else's job to take over. Keep the same requestId/arguments on transport retries.

Complete this job with `changes.fx` (add/replace/remove by FX index), optionally settings or
transform. Read the existing code before replacing an FX and preserve unrelated entries and uniforms.
Replacing an external reference creates a local embedded FX, leaving the shared asset untouched.
Implement `float4 ApplyFX(float2 uv, float4 color)` in linear straight RGBA; use SampleInput for neighbours.
Keep GPU loops/samples bounded. Use #include for existing libraries if needed; do not create files unless
requested. A preview trial compiles and renders without publishing the candidate. Inspect it before
completion; shader_compile_failed leaves working content untouched and supplies diagnostics for correction.

While locked, the user may rename, hide or move the layer; those changes belong to them. If generation
fails terminally or work stops, unlock or fail the job. The user can cancel the edit in Layer Settings.
Completion releases the lock; deletion, close/switch, reload and Undo/Redo cancel it. Do not revive a
cancelled task or bypass revision_conflict. A lock on a group covers its own settings, not its children.

## Concurrent changes and recovery

- Keep Unity calls short; the external image tool does the long work. A busy document is retryable
  after the user's current gesture ends. Do not finish gestures or block all editing yourself.
- If the target's pixels/settings changed, do not bypass conflict protection. Offer the generated
  result as a new layer or recapture for a newly agreed edit.
- Deleted/cancelled reservations must stay deleted. Do not recreate them automatically.
- After timeout, check the job's status before retrying its documented idempotent request with the
  exact same arguments. Immediate Assistant batches are not idempotent: inspect before replaying.
  Do not restart the whole workflow.
- Closing/switching the window or reloading scripts interrupts the job. Rediscover, inspect and explain
  the interrupted reservation; do not deliver into whichever document is now active.
- Live completion does not save. Leave saving to the user unless explicitly requested, and never treat
  a rendered PNG or a live output update as proof that the document file has been saved.
- A path-based TIFF batch is not a save command for an open window. Its `save:false` edits are
  discarded after the request; use Assistant for the open document and Headless Live for a retained
  window-independent candidate. Save an open document through its window.
