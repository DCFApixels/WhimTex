---
layout: default
search_exclude: true
title: Agent API
parent: Technical reference
nav_order: 1
lang: en
permalink: /reference/agentapi/
---

# WhimTex: agent API v1

Use this reference for document files and batch operations. For a document already open in WhimTex,
use [LiveAgentAPI](LiveAgentAPI.md) so edits participate in that window's revision and Undo workflow.
For clipboard content without a connected Editor, use the self-contained [authoring contract](AI/README.md).

| Task | Start here |
| :--- | :--- |
| Connect and discover commands | [Connecting](#connecting) |
| Capture render performance | [Profiler captures](#profiler-captures) |
| Read or write document/layer JSON | [Unified JSON documents](#unified-json-documents) |
| Change settings or build a layer stack | [Batch contract](#batch-contract) |
| Validate, recover or retry | [Validation, Undo and recovery](#validation-undo-and-recovery) |
| Work directly with C# layer objects | [Layer identity and behaviour](#layer-identity-and-behaviour) |

{: .no_toc }

<details markdown="1">
<summary>On this page</summary>

- Contents
{:toc}

</details>

## Connecting

Use a running Unity Editor in Edit Mode. The plugin's optional adapter supports
`com.unity.pipeline` 0.5.0-exp.1 or later and registers these commands after scripts compile.
The core API has no dependency on Pipeline. JSON parsing uses Unity's Newtonsoft Json package.
When copying the plugin into `Assets` instead of using UPM, ensure its declared package dependencies
are already installed; an `Assets`-local `package.json` does not install dependencies automatically.

```powershell
unity status --project-path 'D:/Projects/MyGame' --format json
unity command --query whimtex --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_describe --project-path 'D:/Projects/MyGame' --format json
```

If commands are absent, check whether Pipeline is installed and the plugin is compiled. Follow the
project's rules for compilation or installation; do not trigger them automatically when prohibited.
No API command calls `AssetDatabase.Refresh`, requests script compilation, enters Play Mode or opens a scene.
Import/save commands do import the specific image or document asset they write.

| Command | Parameters | Result |
|---|---|---|
| `whimtex_describe` | none | Protocol, operations, enums, limits |
| `whimtex_assistant_sessions` | none | Discover open document sessions and focus history |
| `whimtex_assistant_begin` | `requestId`; optional capture/placement arguments | Reserve a new layer and capture context in an open document; [Assistant contract](LiveAgentAPI.md#connect-and-discover) |
| `whimtex_assistant_lock` | `requestId`, `layerId`; optional `sessionId`, `expectedRevision` | Lock existing-layer content for generation; [edit contract](LiveAgentAPI.md#inline-shader-fx) |
| `whimtex_assistant_live` | Absolute `requestPath` | Inspect, capture, preview, deliver, render or cancel an open-document job; [request schemas](LiveAgentAPI.md) |
| `whimtex_document_inspect` | `assetPath` | Document revision, stable IDs, hierarchy and settings |
| `whimtex_image_import` | `sourcePath`, `assetPath` | Imported texture path, GUID, dimensions |
| `whimtex_batch_execute` | `requestPath` | Batch result, created IDs, updated document |
| `whimtex_document_render` | `assetPath`, `outputPath`, optional `maxSize=1024`, `overwrite=false` | Absolute PNG path and dimensions |
| `whimtex_storage_inspect` | `assetPath` (`.tiff`) | Metadata-only block catalog, sizes and disk revision |
| `whimtex_document_validate` | `assetPath`, optional `render=false` | Structure, limits, references and Shader FX validation; no save |
| `whimtex_fx_compile` | Exactly one of `presetPath` or `source`; optional `includeBasePath` | Compile a preset or raw HLSL in Unity; return diagnostics and verified locality without editing a document |
| `whimtex_fx_catalog` | Optional `query`, `presetId` | Discover installed presets; inspect defaults, bounds and enum choices |
| `whimtex_assistant_execute` | Absolute `requestPath` | Revision-checked shared operations in an open window; one Undo step, no save |
| `whimtex_render_probe` | Absolute `requestPath` | Render composite/layer/FX stages and channels; report linear ranges |
| `whimtex_profile` | Absolute `requestPath` | Record, summarize, compare with a pinned baseline or export bounded profiler captures; [contract](#profiler-captures) |
| `whimtex_comments` | Absolute `requestPath` | Query discussions, export context/masks, inspect work receipts or explicitly open a discussion link |
| `whimtex_document_status` | `assetPath` | Disk revision, GUID, importer, dirty/live/lock and staged recovery state |
| `whimtex_document_compare` | `leftPath`, `rightPath`, optional `render`, `maxSize` | Compare model, TIFF storage and optional rendered pixels |
| `whimtex_document_recover` | `sourcePath`, `destinationPath` | Recover a staged TIFF into a new document |
| `whimtex_document_export` | `assetPath`, `outputPath`, optional `maxSize`, `overwrite` | Export a flattened document to PNG/JPEG/TGA/EXR |
| `whimtex_headless_live` | `requestPath` | In-memory TIFF preview/commit session without a window; active state does not survive reload |

Pass `--project-path` and `--format json` on every command. The API object is nested inside the
CLI/Pipeline response: check its `apiVersion` and `success` as well as transport success/exit code.
An API validation error can arrive through a successful transport. `errorCode` and `error` describe it;
`failedOperation`, when present, is zero-based (`-1` means batch/save level).
For `whimtex_document_validate`, a readable document may still return `success:true` with `valid:false` and
an `errors` array; check both fields before using it as an input for another batch.

### Compile an FX preset without applying it

Use `whimtex_fx_compile` to check code through Unity's shader compiler before adding it to a
document. Supply exactly one input:

- `presetPath` for a `.hlsl` file inside `Assets`, `Packages` or the configured Shader FX user-preset
  folder. Presets must start with a valid `// @whimtex-effect Category/Name` header.
- `source` for raw HLSL defining exactly one supported entry point: `float4 ApplyFX(float4 color)`,
  or `float4 ApplyFX(PinnedUV uv, float4 color)`.
  It may include `// @param` declarations and does not need the preset header. Raw source
  with relative `#include` paths must also supply `includeBasePath`, pointing to a base directory in
  `Assets` or `Packages`, or to an existing HLSL file whose directory is used as the base. Absolute
  directories inside `Assets`, a package, or the configured user-preset folder are also accepted. If
  the source has no relative includes, omit `includeBasePath`. Unity, `Assets/...` and `Packages/...`
  includes need no base path.

Compilation creates only a temporary shader and material; it does not create an asset, modify source,
change a document or require the WhimTex window. Unity Editor must be connected to the agent command
bridge.

The API call's `success` means the diagnostic request completed. Check `compiled` separately:
compiler warnings are returned in `warnings` and do not make `compiled` false; compilation and source
validation failures return `compiled:false` with entries in `errors`. `diagnostics` contains all
messages, with `file` and `line` when Unity provides a source location.

Compiler messages, declaration/source/include errors, catalog reload failures and Unity time warnings
use the same FX diagnostic records as the UI and Console. Console suppression never removes API records.
Document `Validate` exposes these records per FX as `diagnosticMessages`, `warnings` and `errors`,
alongside the human-readable `diagnostics` text, and includes FX warnings in the document warning list.

#### Applied shader locality

`whimtex_fx_compile` also returns `locality`, captured after successful compilation. The same object
is returned for each Shader FX in document inspection (file, Headless Live and Assistant), FX operation
snapshots, and document validation. Its status describes the **applied, usable shader**, not the current
code draft. `pendingChanges:true` means the displayed source/settings may differ from that shader.
A failed Apply makes the FX unavailable: `locality` is then `null`, even if an older shader is retained
internally. Uncompiled FX and `compiled:false` results also have `locality:null`. Disabling an otherwise
usable FX does not erase its classification; check `enabled` separately.

```json
{
  "compiled": true,
  "locality": {
    "status": "unverified",
    "reasons": [
      {
        "code": "direct_texture_read",
        "message": "Direct texture access 'tex2D' bypasses the PinnedUV sampling contract.",
        "file": "Assets/WhimTexAgentInput.hlsl",
        "line": 4
      }
    ]
  }
}
```

| `locality.status` | Meaning |
| --- | --- |
| `pixelLocal` | The authoring contract is verified; `reasons` is empty. |
| `spatial` | The entry point accepts arbitrary coordinates; it does not guarantee locality. |
| `unverified` | A restricted entry point uses constructs outside the verified contract; rendering takes the conservative full-input path. |

`reasons` contains stable `code` values and readable `message` text. `file` and one-based `line` are
included when known, including expanded project/package includes. Raw source without a base path uses
the diagnostic label `Assets/WhimTexAgentInput.hlsl`; this is not a created file. Locations belong to
the applied source snapshot and can differ from a subsequently edited draft or include.

Current reason codes are `spatial_sample` (numeric sampling coordinates or explicit addressing), `unverified_sampling` (macro/conditional sampling that cannot be proven local), `direct_texture_read`
(direct sampling/load/gather), `derivative` (neighbor-fragment derivatives), `texture_internals`
(direct access to texture-view storage/mapping), and `unverified_dependency` (compiler/engine dependencies,
buffer access or preprocessor constructs outside the checked contract). Reasons explain the conservative
analysis; they are not GPU compiler errors or proof that every reported construct executes at runtime.
The analysis shares the renderer's `IsPixelLocal` contract, rather than inferring locality from shader
compiler optimization. A `spatial` or `unverified` shader can compile and render successfully.

This reports the FX's own contract, not its entire dependency/render plan. A pixel-local FX may use an
input produced by a spatial layer or effect. Inspector preview functions have a separate render path;
the locality result does not promise that their arbitrary-coordinate reads can use cropped inputs.

```powershell
unity command whimtex_fx_compile --presetPath 'Packages/com.dcfapixels.whimtex/src/FXPresets/Halftone.hlsl' --project-path 'D:/Projects/MyGame' --format json

$fx = @'
// @param color _Tint = (1, 1, 1, 1)
// @param color _Accent = #FF0000FF
// @param gradient _Ramp = #FF0000FF -> #0000FF
float4 ApplyFX(PinnedUV uv, float4 color) { return color * _Tint * _Accent * _Ramp_Sample(uv.x); }
'@
unity command whimtex_fx_compile --source $fx --project-path 'D:/Projects/MyGame' --format json
```

`whimtex_describe` also returns `agentModes` and `storagePolicy`. New documents use TIFF or unified JSON:
`whimtex_batch_execute` creates and saves `*.tiff` or `*.json`. Document paths ending in `.asset` return `invalid_path`, including read and dry-run requests. Old document formats require a matching older checkout; there is no migration here.

Direct C# entry points, all on Unity's main thread, return a JSON string:

```csharp
WhimTexApi.Describe();
WhimTexApi.Inspect("Assets/Art/Icon.tiff");
WhimTexApi.ExecuteJson(requestJson);
WhimTexApi.ExecuteFile(absoluteRequestPath);
WhimTexApi.ImportImage(absolutePngPath, "Assets/Art/Source.png");
WhimTexApi.Render("Assets/Art/Icon.tiff", "Temp/WhimTex/icon.png", 1024, false);
WhimTexApi.InspectStorage("Assets/Art/Icon.tiff");
WhimTexApi.Validate("Assets/Art/Icon.tiff", false);
WhimTexApi.Status("Assets/Art/Icon.tiff");
WhimTexApi.Compare("Assets/Art/Old.tiff", "Assets/Art/New.tiff", true, 1024);
WhimTexApi.Recover("Assets/Art/Wall.tiff.whimtex-tmp", "Assets/Art/Wall-recovered.tiff");
WhimTexApi.Export("Assets/Art/Icon.tiff", "Temp/WhimTex/icon.jpg", 0, true);
WhimTexApi.TiffLiveFile(absoluteRequestPath);
WhimTexApi.AssistantExecuteFile(absoluteRequestPath);
WhimTexApi.FxCatalog("Color");
WhimTexApi.RenderProbeFile(absoluteRequestPath);
```

The full namespace is `DCFApixels.WhimTex`. With an existing C# eval bridge, call these methods
instead of installing Pipeline solely for this tool. Send a request file to avoid shell-escaping JSON.

### Independent TIFF Live Update

`whimtex_headless_live` is the persistent, window-independent variant of Live Update. It keeps a
transient model between requests and supports `begin`, `status`, `preview`, `render`, `complete`
and `cancel`:

```json
{"apiVersion":1,"op":"begin","sessionId":"wall-live","assetPath":"Assets/Art/Wall.tiff","expectedRevision":"<inspect revision>"}
{"apiVersion":1,"op":"preview","sessionId":"wall-live","requestId":"preview-1","operations":[{"op":"add","type":"color","as":"overlay","settings":{"name":"Overlay","color":[1,0.2,0.1,1]}}]}
{"apiVersion":1,"op":"render","sessionId":"wall-live","requestId":"render-1","outputPath":"Temp/WhimTex/wall-preview.png","overwrite":true}
{"apiVersion":1,"op":"complete","sessionId":"wall-live","operations":[]}
```

Nonempty `operations` in `preview`, `render` or `complete` rebuild the candidate from the snapshot
captured at `begin`; they do not accumulate edits from earlier previews. Send the full candidate list.
Omitted operations or `operations:[]` keep the current working model; they do **not** reset it.
`render` without operations is read-only, but with nonempty operations it also replaces the candidate.
`complete` verifies the original disk revision before one atomic save. If another writer changed the
TIFF, it returns `revision_conflict` and the session remains available for `status` or `cancel`.
The existing `whimtex_assistant_live` remains the open-window API.

### Headless session lifecycle and retries

- Operations are `begin`, `list`, `status`, `preview`, `render`, `complete`, `cancel`.
  `list` accepts an optional TIFF `assetPath` filter; other calls address an explicit `sessionId`.
- `begin` requires a unique 1..128-character `sessionId`. At most eight sessions may be active;
  a TIFF already open in a WhimTex window or another Headless session is rejected.
  Existing documents require the document revision from `inspect`, not the disk SHA-256 from `status`.
  For creation use `create:true`, optional width/height (default 512, each 1..16384,
  at most 16,777,216 pixels), and omit `expectedRevision`. The destination must not exist.
- `begin` is not idempotent: after a timeout, use `list`/`status`, not another blind `begin`.
  Preview replay remembers only the last nonempty operation request with a `requestId`.
  Repeating that ID requires identical arguments; changing them returns `request_conflict`.
  Replayed additions may receive new IDs. Use `@aliases` inside the complete operation list,
  not IDs of layers created by an earlier candidate.
- Active models exist only in memory and are lost on domain reload or Editor exit. No user Undo
  history is provided. Only the last 32 successful `complete`/`cancel` receipts are persisted for
  identical terminal retries; these receipts do not restore an active session. Reusing an ID in a
  new `begin` invalidates its old receipt. `status` addresses active sessions, not terminal receipts.
- `complete` without new operations saves the current candidate and closes the session;
  `cancel` discards it without saving. On failed completion inspect disk/status and the still-active
  session before retrying: a post-commit import error is not proof that the TIFF was unchanged.
  Headless sessions do not publish the window's GPU Live Update output.
  A create session also rechecks that its destination and `.meta` are absent before saving;
  if another writer created them after `begin`, completion returns `already_exists` and keeps the session.


## Profiler captures

Captures now include separate `worker` and `latency` events. Worker `durationMs` excludes
`queueMs`; `originId` links to the scheduling span and `inputId` links to the originating input
when available. `threadId` identifies the recorded thread. Journal writers also provide
`workerEncodeMs`, `workerWriteMs` and `workerHashMs`; unavailable submeasurements are null.
`workerAllocatedBytes` is optional: some Editor runtimes do not support a meaningful per-thread
byte counter, and disabling allocations also makes it unavailable. Worker time is never added
to `rootWallMs` or subtracted from main-thread self time. `summary.brief` exposes separate
`workerHotspots` and `inputLatencies`.

`input.canvas_ready` starts at an accepted brush batch/release, or at the first canvas render
request for other edits. It ends after the updated canvas is assigned for presentation.
`input.gpu_observed` uses a public graphics fence and reports an observed completion upper bound,
not monitor presentation or a GPU execution duration. Check `asyncStatus` and
`completionPollIntervalMs`; unsupported/timeout/interrupted observations remain incomplete.
`coalescedInputs` counts accepted inputs sharing an update, and `latestInputDurationMs` measures
from the most recent one. No forced render, readback or GPU wait is introduced by recording.
Worker and fence completions may finish after Stop; wait for `openSpans:0` before exporting.
Pending workers are bounded to 256, input documents and GPU completion probes to 64 each.

`memory.paintBuffers` samples all WhimTex documents, including pools created before recording:
native active/idle bytes, pending writer payload, GPU transfer pages and active/pooled Half
atlases. These are overlapping payload views, not physical VRAM. Writer pending bytes are
already included in active native buffers. Process and pool totals are sampled at most every
100 ms plus capture start/stop; short-lived peaks may be missed. Texture resource events retain
their existing immediate accounting. Use `gpu:false` and/or `allocations:false` on Start to
compare recording overhead without changing rendering.

Open **Window → Analysis → WhimTex Profiler**, or call `DCFApixels.WhimTex.WhimTexApi.ProfileJson(requestJson)`
on the Editor main thread. The optional Pipeline command accepts the same JSON in a file:

```powershell
unity command whimtex_profile --requestPath 'D:/Projects/MyGame/Temp/WhimTex/profile-request.json' --project-path 'D:/Projects/MyGame' --format json
```

Every request requires `apiVersion:1` and `action`. Check the returned envelope's `success`,
`errorCode` and `error`. Profiling is off by default. Capture/analysis calls do not render, edit scenes or
documents, clear render caches, refresh assets or compile scripts. The explicit `benchmarkStart`
action renders an isolated snapshot as described below; it does not alter the source document.

The profiler window's automatic event/summary reads are cooperative main-thread work, with
a 4 ms target per scheduled callback and an adaptive delay of at least 0.5 seconds between
completed reads. Chunks, GC and final UI binding can exceed that target; it is not a frame-time
guarantee. Expensive reads back off to target roughly 5% read CPU duty. Each read fixes its event
count at the start; newly appended events and late completions are picked up by subsequent
refreshes. The previous complete table remains visible until the next result is ready.
Filters, clear/replacement and window disable cancel pending work. Public JSON `read`/`export`,
manual Refresh, filtering and initial window population remain synchronous. Their fields,
filtering, aggregation and paging contracts are unchanged.
Reload backups have a separate 256 MiB serialized-size limit; a capture within the
event-count limit can still exceed it. Export important large captures before script reload.

| Action | Additional request fields | Behavior |
| --- | --- | --- |
| `start` | Optional `maxEvents` (256–262144, default 32768), `durationSeconds` (1–600, default 120), `documentId` (opaque string; omitted or null means all documents), `gpu` and `allocations` (both default true) | Starts a new capture, replacing the retained stopped capture; refuses an active capture or unfinished spans |
| `status` | Optional `captureId` | Returns current capture status; validates the ID when supplied |
| `stop` | Required `captureId` | Stops recording; already open spans may still finish |
| `read` | Optional `captureId`, `offset` (default 0), `limit` (1–2000, default 200), `operationId`, `documentId`, `layerId`, `layer`, `phase`, `groups`, `tree` (default false), `collapsedIds` | Pages matching events (or visible tree rows) and summarizes all matching retained events; validates the ID when supplied |
| `clear` | Required `captureId` | Discards the stopped capture; refuses recording or unfinished spans |
| `export` | Required `captureId`, `outputPath` | Writes the entire stopped capture, without filters, to a new project-relative `Temp/WhimTex/*.json`; refuses unfinished spans and existing files |
| `baseline` | Required `captureId` | Copies a stopped capture into the one retained baseline; replaces the previous baseline. Requires zero open scopes and pending GPU queries |
| `clearBaseline` | Required `baselineId` | Discards only the baseline, even when there is no current capture |
| `summary` | Optional `captureId`, `top` (1–20, default 5), `operationId`, `documentId`, `layerId`, `layer`, `phase`, `groups` | Returns a compact `brief`, without raw events or the full legacy summary; active/incomplete captures carry warnings |
| `compare` | Optional `captureId`, `baselineId`, `offset` (default 0), `limit` (1–2000, default 20), `sort`, `documentId`, `layerId`, `layer`, `phase`, `groups` | Compares the stopped current capture against the baseline; requires zero open scopes and pending GPU queries. Same filters apply to both captures |
| `explain` | Required `captureId`, `eventId`; optional `limit` (1–256, default 64) | Traverses recorded property/dependency causes backward from one retained event; bounded and read-only |
| `benchmarkStart` | Required `documentId`; optional `mode` (`cold`, `warm`, `both`, default `both`), `repetitions` (1–30, default 7), `warmups` (0–5, default 1), `maxSize` (1–2048, default 512) | Freezes an owned snapshot, then renders one sample/warmup per Editor update; refuses concurrent runs or busy/playing Editor |
| `benchmarkStatus` | Optional `benchmarkId` | Reads the latest benchmark; validates identity when supplied |
| `benchmarkRead` | Required `benchmarkId` | Reads the identified benchmark, including partial samples |
| `benchmarkCancel` | Required `benchmarkId` | Stops between operations and releases only owned resources; retains partial results |

### Compact analysis and comparison

Prefer `summary` for a first agent inspection, then `read` for the relevant events:

```json
{"apiVersion":1,"action":"summary","top":5}
```

`brief` contains root-only `rootWallMs`/`rootCount`, `matchedEvents`, `spanCount`, `completedSpanCount`,
bounded `hotspots` ranked by self wall time, `gpuHotspots` ranked by measured GPU time, `slowOperations`
ranked by root wall time, `reasons`, cache lookup counts/rate, `omitted` counts and `warnings`.
Stages include phase/group, document/layer/FX identity and captured names, representative `eventId`,
calls and completed calls, total/self/mean/max times. FX CPU preparation/submission and GPU coverage
are separate. GPU hotspots include measured sample counts and `partial`; absent GPU samples are not zero.
`reasons.events` counts recorded reason rows; `occurrences` sums their batched counts, not user actions.
Cache `missRate` is misses/(hits+misses), null without lookups; bypasses and releases are separate.
Representative event IDs are capture-local: use `read` with the same capture and phase/layer filters;
`read` has no exact event-ID filter. The lists are independently capped by `top`, so a CPU hotspot
does not hide a GPU hotspot. Inclusive CPU/GPU and nested timings overlap; do not sum them.
The brief also includes whole-capture `memory`, filtered bounded `allocationHotspots` ranked by
self managed allocation **count**, and `causes` for matching cache misses with recorded property/dependency links.
Stage metrics include nullable `gcAllocationCount` and `gcSelfAllocationCount`: main-thread allocation
counts, including instrumentation, with no per-stage byte amounts. Inclusive counts overlap across
nested stages. Aggregate counts sum measured calls only and include `measuredCalls`, `eligibleCalls`
and `partial`; missing measurements never contribute a fabricated zero. `allocationHotspots` includes
stages with at least one measured call, is independently capped by `top`, and reports its omitted rows
in `omitted.allocationHotspots`. Comparison also supports these two allocation-count metrics;
see [Memory accounting](#memory-accounting) for measurement coverage.

For a before/after comparison, stop and pin the first capture, then record the second:

```json
{"apiVersion":1,"action":"baseline","captureId":"BEFORE_CAPTURE"}
```

```json
{"apiVersion":1,"action":"compare","captureId":"AFTER_CAPTURE","baselineId":"BEFORE_CAPTURE","sort":"selfMs","limit":20}
```

Successful responses include `baseline` metadata (or null), using the same fields as `capture`.
The baseline identity is `baseline.captureId`. Record/Clear affects only the current capture;
the independent baseline survives new recordings, current clearing and script reload in the same Editor
session. Pinning freezes all its events and recorded Unity version. It is not an archive across Editor
restarts, and importing previously exported reports is not supported. Export saves the current capture,
not the baseline; export it before replacing the current capture if an external report is needed.

`comparison` contains paged `rows`, `totalRows`, `offset`, `nextOffset`, before/after root/event totals
and `warnings`. Rows match by phase, captured document ID, layer ID and FX slot, never display name.
Renaming a layer does not split it; reopening a document with a different session-local ID does.
Replacing/reordering FX slots can change what is measured: compare equivalent workloads and inspect FX names.
Each row includes `before`/`after` metrics, `status` (`matched`, `added`, `removed`), `countChanged`,
`gpuComparable`, `allocationComparable`, `delta` and `percent`. Metrics: count/completedCount, totalMs/selfMs/meanMs/maxMs,
cpuPrepareMs/cpuSubmitMs, gpuMs/gpuMeanMs and GPU ready/pending/unavailable counts,
plus nullable `gcAllocationCount`/`gcSelfAllocationCount` with `measuredCalls`, `eligibleCalls` and `partial`.
Delta is after minus before. Percent is delta/before × 100, null if the baseline is zero or absent.
Missing rows have null timing deltas, not a claimed speedup to zero. GPU deltas require every call
in both rows to have a measured result; otherwise they are null. Call-count deltas remain available.
Allocation deltas require `allocationComparable:true`: both rows exist and every eligible call in each
has a measured allocation count. Partial sums remain visible in `before`/`after`, but neither a missing
row nor an unmeasured call is treated as zero. Both allocation deltas are integer counts, not bytes;
inclusive counts include nested stages, self counts exclude them. Zero baseline permits an absolute
delta but not a percentage. Different call counts still produce deltas, with the workload warning.
`sort` is `selfMs` (default), `totalMs`, `meanMs`, `maxMs`, `gpuMs`, `count`, `gcAllocationCount` or `gcSelfAllocationCount`, ordered by absolute
delta descending, unknown deltas last, with deterministic identity ties. `operationId` is deliberately
unsupported here because it belongs to one capture, not a repeatable operation identity.
Warnings cover different call counts/documents, added/removed stages, truncation and missing timings.
This is an observational comparison, not a benchmark: match input, quality, cache state and hardware.
Increasing call counts can increase total time while decreasing mean time. Legacy post-hoc `phase`
rows are excluded from both new analyses to avoid mixing overlapping accounting models.

### Capture filters and event details

`documentId` is an opaque string, exposed as `profilerDocumentId` by document snapshots and
`LiveSessions`. Copy the returned string unchanged; do not construct or guess numeric `EntityId`
values. Omit `documentId` or supply null for all documents in `start` and `read` requests.
Discover it again after reopening a document or reloading scripts; it is not a persistent asset
identity or a live editing `sessionId`. In the window, leave **Document ID** empty for all documents
or paste the returned `profilerDocumentId` to restrict the next capture.
`operationId` selects one correlated operation; `layerId` is an exact stable layer ID;
`layer` matches a captured layer name substring case-insensitively or an exact layer ID;
`phase` matches text case-insensitively. Supplied filters combine with AND. Omit filters to include all events.

`groups` accepts up to six string IDs: `ui` (UI & Input), `editing` (Tools & Editing),
`rendering` (Rendering), `resources` (Resources & Memory), `storage` (Load & Save),
`diagnostics` (Diagnostics). Selected groups combine with OR, then AND with the other filters.
Omit the field for all groups; `[]` selects none. Unknown IDs, null and non-string items are rejected.
Each event belongs to one group, classified from its phase/kind at read time, including restored captures.
SDF, text, shapes and FX are ordinary rendering work; caches, readback/upload and cache matching/copying
belong to resources. Change events belong to editing; otherwise counters, errors and unrecognized
phases belong to diagnostics. New instrumentation uses the centralized phase-family classification
in `WhimTexProfiler.Groups.cs`; the filter does not add measurement coverage to uninstrumented work.
The window offers group checkboxes, **All** and **None**, with serialized selection. To isolate one group,
click **None**, then check that group.
Groups filter both views before pagination and folding; context-only ancestors may belong to hidden
groups. Filtering never recalculates event wall/self times or changes the full export.

For example, start with the defaults:

```json
{"apiVersion":1,"action":"start","maxEvents":32768,"durationSeconds":120}
```

Retain `capture.captureId` from the response. Perform the authorized editing or rendering separately,
then stop and inspect the same capture; replace `CAPTURE` with that returned ID:

```json
{"apiVersion":1,"action":"stop","captureId":"CAPTURE"}
```

```json
{"apiVersion":1,"action":"read","captureId":"CAPTURE","offset":0,"limit":200,"phase":"SDF"}
```

```json
{"apiVersion":1,"action":"export","captureId":"CAPTURE","outputPath":"Temp/WhimTex/profile-unique-id.json"}
```

`capture` is null when no capture exists. Otherwise it contains `captureId`, `recording`, `stopReason`,
`capacity`, `eventCount`, `droppedEvents`, `elapsedMs`, `openSpans`, `pendingGpuSamples`, `gpuAvailability`, `documentId`, `truncated`,
`unityVersion`, `allocationCountUnit`, `allocationCountScope`, `memory` and `causeTrackingLimited`.
Recording stops at the event limit or deadline; document-change tracking also has
a bounded document limit. Inspect `stopReason`, `truncated` and `droppedEvents` before treating a
capture as complete. Captures are automatically saved before script reload and restored in the same
Editor session, with the same ID, events and completed timings. An active capture stops with
`stopReason:assembly_reload`; recording does not resume automatically or include the compilation gap.
Unfinished scopes become `interrupted:true`, stay `completed:false` with null timings, and no longer
count as active `openSpans`. The restored capture can be exported or cleared normally.
The reload snapshot is a versioned temporary file under `Temp/WhimTex`, referenced by Unity SessionState
only after writing finishes, then consumed after restoration. It is not an archive across Editor restarts;
export before closing the Editor. Invalid snapshots are rejected with a Console warning.
Starting again replaces the previous stopped capture. Export never overwrites an existing file.

`read` returns `events`, `summary`, `groups`, `totalMatched`, `totalVisible`, `offset`, `nextOffset` and `rootWallMs`.
`groups` lists all six IDs with `name`, `count` and `selected`; counts apply other filters but ignore
group selection and tree folding, so hidden groups remain discoverable. Event and summary rows include `group`.
Use `nextOffset` for the next request; null means the end. Each event contains `id`, `parentId`,
`operationId`, `changeId`, `documentId`, `layerId`, `layerName`, `depth`, `operationMs`, `fxIndex`, `fxName`, `phase`, `kind`, `detail`, `startedMs`,
`durationMs`, `selfMs`, `completed`, `interrupted` and `value`. IDs correlate events within this capture, not across
Editor sessions. Kinds are `span` (nested scopes), `cache`,
`change`, `reason`, `resource`, `counter` and `error`. Open events have null `durationMs` and `selfMs`, not zero;
`selfMs` is also null for non-span events. Preserve nulls when processing reports.
Timed stages reserve their event slot when they begin, then fill that same slot on completion.
New captures keep start order (earliest first), with parents before their children; events are not
sorted by duration or completion time. SDF/Outline stages use this same nested-scope mechanism.
Previously retained captures can still contain `phase` (post-hoc diagnostic intervals); their order is unchanged.
`layerName` is a recording-time string snapshot, not a live lookup; renaming or closing a document
does not change old events. `depth` is zero-based nesting in the complete capture, even when filters
or pagination omit parents. `operationMs` is the completed root span's wall time, or null when
the root is still open or is not a span. This provides a consistent duration-bar denominator across pages.

With `tree:true`, matching events include their ancestors as context rows. `collapsedIds` is an optional
array of current-capture event IDs (positive integers, at most `eventCount` entries); it requires tree mode.
All descendants of collapsed nodes are excluded before pagination, including those on other pages.
Tree events include `hasChildren` (in the filtered tree, even beyond the current page), `collapsed`
and `contextOnly`. In flat mode these flags are false. `totalVisible` counts rows after folding,
including context; `totalMatched`, summaries and root timings remain independent of folding and exclude
context-only ancestors. Tree pagination uses visible-row offsets and clamps an offset past the end
to the last page. Omit `tree` for the flat API behavior; exports always retain all events, unfolded.

Summary rows group by `phase` and `kind`; `fx.*` additionally groups by `documentId`, `layerId`,
`fxIndex` and captured `fxName`, so distinct FX slots/layers are not merged. Rows include `layerName`, `count`, `completedCount`, `totalMs`, `selfMs`
and `maxMs`. Counts include open events; timing aggregates include completed events only.
The window can sort Summary by clicking Count, Total ms, Self ms, Max ms, Prep ms or GPU ms. Selecting a new
column starts descending; clicking it again toggles direction. Unavailable timings stay last,
ties use phase/kind ordinal order, and duration bars always scale to the largest total time.
The window retains the column/direction across refresh and script reload. This is presentation-only:
it does not reorder API events, change summary values or affect exports.
The summary covers **all matches**, independently of pagination. `rootWallMs` sums only completed
root spans matching the filters; filtering out roots can make it zero even when child events remain.
Inclusive summary rows overlap and must not be summed. Legacy `phase` intervals are statistics only:
they may overlap spans, do not contribute to `rootWallMs`, and are not subtracted from span self time.

`durationMs` and `selfMs` measure main-thread wall elapsed, including waits and GPU command submission.
They are not GPU duration, pure CPU execution time, or summed worker time. Per-stage managed allocation
counts cover measured main-thread scopes; the separate allocated-byte total covers all Editor threads,
as detailed below. Instrumented stages include canvas updates, composition/readback, layer previews, groups, FX and SDF;
brush input batches, preparation, segment submission and pixel readback; selection edits/mask uploads;
transforms, canvas-frame edits, fill, model commits, Undo/Redo restoration, interface/hierarchy/inspector
refresh; TIFF/JSON load/save, serialization, file writing/import requests and image/PSD export.
Brush batches are instrumented rather than every stamp; `brush.stamp_count.value` counts stamps per submitted batch.
The in-stroke breakdown includes `brush.build_stamps` (CPU generation), `brush.mesh_upload`
(CPU mesh preparation), `brush.mesh_submit` (CPU submission and GPU interval), and
`brush.allocate_stroke` (first buffer allocation/initialization). `ui.window_update`,
`ui.canvas_presentation` and `ui.brush_cursor` distinguish window work from painting.
`layer.drawing_convert_blit` brackets only the conversion draw within `layer.drawing_convert`;
their inclusive GPU intervals overlap. `layer.drawing_convert_pixels.value` is the destination
pixel count actually converted for that invocation (the sum of damaged tiles for a partial update).
During a buffered stroke, a source-resolution Half straight preview is retained until commit/cancel;
256px damage tiles are independent of the stroke-wide history tiles. Every refresh recomputes
from the Half base and accumulated stroke, applying mask/blend/opacity once; it never blends into
the cached preview. Transform, FX, groups and export still consume a complete source image.
`layer.drawing_preview_full`, `layer.drawing_preview_partial` and `layer.drawing_preview_reuse`
count the chosen cache path. Initialization, stroke reset or changed composite settings/mask
invalidate the whole preview. Outside a buffered stroke the ordinary conversion path remains.
Input samples still advance spacing/pressure in order, but segments that produce no paint do not
request another canvas composition; existing scheduled requests remain coalesced.
`brush.dirty_bounds_pixels.value` is the accumulated stroke
bounding rectangle, **not** touched-tile coverage or the area changed since the previous preview.
These counters diagnose live display work separately from end-of-stroke/history work; they do
not measure OS input delivery or the time at which pixels become visible on the monitor.
`layer.content_check` brackets full-resolution intrinsic layer rendering and alpha reduction
for the empty-layer badge's fallback; its GPU interval includes the submitted work.
`layer.content_check.reuse` reduces an already available, exact full-resolution layer result,
including FX, without rendering it again. `layer.content_coverage.initialize` initializes the
ordinary Drawing's 256 × 256 alpha block map after creation or invalidation. Later compute
commits update touched flags inside the existing `brush.composite` / history submission scopes;
Undo refreshes restored blocks. The map payload is 4 KiB at 8192². Storage-page synchronization
does not invalidate the completed Half image. Pending detection is asynchronous and returns
Unknown; the former timed `layer.content_check.wait` path has been removed. Explicit resource
teardown may still drain an outstanding owned readback before releasing its resources.
`brush.history.store` measures main-thread enqueueing, not background compression/file writing.
The opt-in `brush-live-*-ui-post*` diagnostics separately export completed worker page timings
(sampling, encoding plus stream writes, checksum, total), transfer queues and consecutive strokes.
Worker durations overlap and are not pure disk time; see [the test guide](https://github.com/DCFApixels/WhimTex/blob/dev/Tests~/RUNNING_TESTS.md).
Deferred `storage.import` measures the import request, not background completion. A standalone load has no
document ID before decoding, so use an all-document capture to include it. File-dialog waiting is excluded
from the texture-export scope. GPU queries now also bracket selected composition, layer, brush and copy passes;
readback/upload spans remain CPU wall elapsed rather than fabricated GPU-only timings.
`changeId` links renders to the latest model change or Undo/Redo restoration; it does not
measure a whole input gesture through GPU presentation. Native Unity markers are independently
enabled by normal Unity Profiler recording and cover `Measure` scopes, including the SDF/Outline stages.
Worker execution remains in Unity's job profiler;
this capture does not add per-worker spans.

### Recalculation reasons

Events additionally expose `changeEventId`, `changeReason`, `changeOrigin`, `changedLayerId`,
`changedLayerName` and `resourceKey`. `changeEventId` points to the last recorded `document.changed`
event **for this document** (zero if absent); the other change fields are snapshots of its label,
originating measured phase and layer when known. The changed layer can differ from the currently
rendered layer. Nested work observes a change made after its parent scope began; it does not inherit
the parent's stale change ID. This is last-change context, not a full dependency-causality graph or
an assertion that every later miss was caused by that edit. An uninstrumented edit can have no origin/layer;
the generic `MarkChanged` label falls back to the current Undo group name.

`kind:reason` is an instantaneous event (`durationMs:0`, `selfMs:null`), not a measured task.
`render.trigger` records accumulated canvas requests: `brush.live`, `transform.live`, `document.changed`,
`document.resources_changed`, `layer.preview`, `undo.redo`, `quality.settled_refinement` and generic
`canvas.request`. Repeated requests of one kind coalesce until the next canvas update, with their count
in `value`; they do not imply that many renders. Pending reasons are bounded and discarded when the
capture or document changes. Generic and specific requests may both occur; do not sum them as user actions.

`cache.miss` includes the exact `resourceKey` and layer. Its detail is `miss:not_retained`,
`miss:content_or_settings`, `miss:quality`, `miss:scale`, `miss:resource_lost`, `miss:dimensions` or
`miss:color_required`. The combined content/settings stamp also covers dependencies; it does not
identify a specific changed property by itself. `cache.bypass` distinguishes `cache.not_attached` from
`dependency.uncacheable`. `cache.release` gives per-entry `unused`, `budget` or `replaced` reasons;
`cache.invalidate` records whole-cache release requests such as Undo/Redo or resource changes,
and `cache.disposed` reports the released entry count in `value`.
The existing `effect.cache` hit/miss events remain; a reason is explanatory metadata, not another miss.
The window shows these fields as **Latest change**, **Cache entry** and **Occurrences**; API reads,
full exports and reload snapshots retain them. Older captures have zero/null for absent provenance.

During recorded cache preparation, `edit.observed_change` records actual serialized property
differences (including embedded FX settings, source-pixel revisions and group child order).
`cache.dependency_changed` links the dependent layer to observed input changes. A content/settings
miss can carry `causeEventId`, `causeEventIds`, `causeLayerId`, `causeLayerName`, `causeProperty` and
`causeKind`; the array preserves multiple incoming causes, while singular fields describe the latest.
`explain` returns `nodes` with `eventId`, phase/layer/name/property/kind/detail and `causes`, plus
`truncated`, `trackingLimited` and an `evidence` caveat. All links are capture-local and backward.
First observation establishes a baseline, not an invented edit. Missing links do not prove absence
of a cause; external dependencies/unobserved changes can remain unattributed. Bounds: 1024 layers,
32 changed properties/incoming links per layer, 256 Ki input characters per layer and 2 Mi overall
(serialized-input budget, not managed-heap bytes). Limits or diagnostic read failures set
`causeTrackingLimited`. **Explain cause** in event details uses this same read-only API.

### Memory accounting

`capture.memory` is null for older/unavailable captures. Otherwise it exposes
`editorManagedAllocatedBytes`, `editorManagedAllocationAvailable`, `editorManagedAllocationLimited`,
`editorManagedAllocationScope`, `editorManagedAllocationUnit`, `allocationCountLimited`,
`trackedTextureBytes`, `peakTrackedTextureBytes`, `acquiredBytes`,
`releasedBytes`, `observedBytes`, `acquireCount`, `releaseCount`, `nativeCacheBytes`,
`peakNativeCacheBytes`, `processUsedBytes`, `processReservedBytes`, `peakProcessUsedBytes`,
`trackingLimited` and a `coverage` description. Values describe the whole retained capture, not display filters.

`editorManagedAllocatedBytes` is total managed allocation traffic during the capture across **all
Editor threads**, including unrelated Editor work and instrumentation. Neither the capture's
`documentId` nor any display/read/summary filters limit this total. Its metadata explicitly states
`editorManagedAllocationScope: "all_editor_threads"` and `editorManagedAllocationUnit: "bytes"`.
It is not live heap size, GC collection time, document-only memory or a sum of stage measurements.
The API samples current Editor allocations before reading memory totals. The byte value is null if
`editorManagedAllocationAvailable` is false or `editorManagedAllocationLimited` is true.

Each completed span with a complete, valid scoped measurement exposes nullable `gcAllocationCount`
and `gcSelfAllocationCount`. These are **counts** on the main thread, including instrumentation;
there are no per-stage managed byte measurements. Inclusive counts include nested work; self counts
subtract child counts. Do not sum inclusive counts across nested stages. Capture metadata states
`allocationCountUnit: "allocations"` and `allocationCountScope: "main_thread"`.
Open, interrupted, unavailable and incompletely measured spans expose null, not zero. Captures
without these scoped count measurements also expose null; retained byte measurements are not counts.

Stage count aggregates in `read.summary`, brief timing/allocation hotspots and comparison
`before`/`after` metrics include `measuredCalls`, `eligibleCalls` and `partial`. `eligibleCalls`
counts all retained matching spans, including open/interrupted spans; `measuredCalls` counts only
complete valid measurements. `partial` is true when `measuredCalls < eligibleCalls`. Numeric totals
sum the measured subset; both counts are null when no calls were measured. A measured zero remains
zero, including in a partial aggregate. Non-span summary rows have zero eligible/measured calls and
null counts. `gpuHotspots.partial` separately describes GPU coverage.

The core uses bounded Unity `ProfilerRecorder` measurements. Recorder overflow makes affected scoped
counts or the Editor byte total unavailable, never zero. `allocationCountLimited` reports a stage
count recorder limit; `editorManagedAllocationLimited` independently reports a limit affecting the
Editor byte total. Texture/native payload and sampled Unity-wide memory remain available independently.

Textures are estimated format/mip/sample payload for instrumented temporary RT checkouts, effect
caches, Drawing surfaces and distance-field uploads. `resource.acquire/observe/release` carry byte
counts in `value` and object identity in `resourceKey`. First observed pre-existing resources are
separate from new acquisitions. Returning a temporary RT ends our ownership; Unity may retain its
allocation in its pool. These figures are not physical VRAM: driver overhead, CPU-readable copies,
unobserved pre-existing resources and pool residency are excluded. Retained native distance-cache
payload is counted separately; native scratch arrays are not comprehensively tracked. Each tracker
is bounded to 8192 owners. Stop freezes ownership accounting, not future texture lifetimes.
Unity-wide allocation/reservation is sampled at start, completed root scopes and stop; the peak is
sampled, not continuous, and is neither document-only memory nor OS resident memory.
**Diagnostics & benchmarks** displays bytes on a separate **Managed allocated · all Editor threads**
line. Event details show main-thread inclusive/self allocation counts without byte formatting.
Unavailable measurements show **—**; recorder limits are marked explicitly.

### Repeatable snapshot benchmarks

Use `benchmarkStart` only for an authorized measurement, then poll `benchmarkStatus`/`benchmarkRead`.
The response has a separate `benchmark` object; it never replaces current capture or baseline.
It contains identity/source, `state`, `phase`, `running`, mode/settings, progress counts, `stopReason`,
`error`, `elapsedMs`, `partial`, `metadata`, and `cold`/`warm` series. Each series holds ordered
`samplesMs` and nullable `statistics`: count, medianMs, minMs, maxMs, meanMs, p95Ms, stddevMs.
P95 uses interpolation at `(n-1)*.95`; deviation is population standard deviation. Check execution
`state`/`error` even after a successful API envelope. Terminal states are completed, cancelled,
timedOut, interrupted and failed; partial statistics are retained.

The interval is **main-thread wall elapsed from render entry through synchronous HDR readback**,
including GPU/queue waits and transfer, excluding snapshot/clone setup and cleanup. It is not pure
CPU execution or GPU time. Cold samples get fresh owned document/layer-derived resources and effect
cache; warm samples reuse one owned document/cache after excluded warmups. This does not reset
Unity's RT/material/font caches, JIT, shaders or driver. `warmups:0` includes first-use work. Both mode
runs cold before warm; shared global caches can warm during the cold series.

Snapshot creation freezes model, Drawing pixels and texture dependencies without flushing the source's
Drawing surface or clearing its caches. Readable textures retain pixels/mips; non-readable textures
are copied to linear RGBAFloat with generated mips. Fonts/meshes remain shared asset references.
Known time-dependent HLSL FX are rejected. Other shader globals and
Editor/GPU load remain uncontrolled. Metadata records content SHA-256, dimensions, quality,
Unity/device/CPU and method caveats; compare equivalent inputs and environments.
The snapshot is bounded to 256 layers, nesting 32 and 128 MiB source-payload budget; this is not a
total process/VRAM cap (temporary copies/readbacks coexist). The 120-second deadline and cancellation
are checked between operations, so one synchronous operation can exceed them. Compilation/import/Play
interrupts the run. Reload releases owned resources and retains partial statistics in the same Editor
session. Only the latest run is retained; no recording archive or report-import feature is added.

### CPU and GPU timings

`fx.block` encloses one layer's FX stack, including group and input-dependency stacks.
Each active FX has `fx.render`, with zero-based `fxIndex` and captured `fxName`, plus child spans
`fx.prepare` (inputs, material/parameters, target allocation) and `fx.submit` (blit, output finalization
and target handoff). Inactive/unavailable FX are skipped. Null materials have preparation but no submitted GPU work.
Block and effect events expose `cpuPrepareMs` and `cpuSubmitMs`; other events and older captures expose null.
Block CPU components sum its immediate FX, not all recursively nested events. Preparation can include
rendering input layers and waits; it is wall elapsed on the calling CPU thread, not pure CPU busy time.

`gpuMs` is an asynchronous GPU timestamp interval from public Unity GPU recorders. The block brackets
the whole stack, including GPU work for input preparation. An individual FX brackets its own blit and
`FinishStage` output sanitization/diagnostics, excluding input preparation. These are elapsed device
intervals: queue gaps and GPU idle time can be included. CPU and GPU overlap, as do parent/child GPU
intervals: **never add CPU + GPU or sum nested GPU rows as total render latency**.

Additional GPU intervals cover common layer generation/transform, blend/finalization, accumulator
clears, backdrop/cache copies, selected group/diagnostic passes, Drawing-surface initialization and
straight-alpha conversion, brush draw batches/composites/clears, and output encoding blits.
They use the same bounded query pool and status contract as FX. Specialized paths outside these
boundaries remain unmeasured. Preparation/CPU jobs/readback are not reclassified as GPU work.
`layer.generate` is inclusive of its behavior's rendering, including nested transform/FX commands;
its device interval can include submission gaps while the CPU prepares those commands.

Before a capture's first GPU measurement, `gpuAvailability` is `warming_up`: empty markers check
that the Editor actually returns GPU timestamps. Wait for `ready` (or `unconfirmed` with Graphics Jobs) in `status` before a one-shot render
if its first GPU timing matters. No document is rendered/replayed to warm up instrumentation. Calls
during warmup retain CPU timings and `gpuStatus:warming_up`; they are not replayed later. Hardware/API
support alone does not guarantee the current Editor delivers counters. Native recording stays attached
during capture and stops after outstanding work drains; it does not enable the Unity Profiler window.
The actual `SystemInfo.renderingThreadingMode` is checked before warmup and before each query.
Unity does not support GPU profiling with Graphics Jobs. WhimTex still attempts collection in
`LegacyJobified` and all native Graphics Jobs modes; returned positive single-invocation intervals
have `gpuStatus:unconfirmed`, not `ready`. This is not an estimate with a known error bound.
Capture metadata retains actual `graphicsJobs` (boolean or null if unknown), `renderingThreadingMode`
and `gpuWarning`, independently of the current Editor and including baseline, export and reload snapshots.
The window shows actual Graphics Jobs On/Off even without a capture, plus a configured-setting/restart
notice if it differs. Unconfirmed numeric cells have a yellow background and explanatory tooltip in
Events, Summary and GPU Comparison, including derived deltas; there is no repeated word beside values.
Repeat captures with Graphics Jobs disabled and Unity restarted for a supported comparison.
Unknown future threading modes report `unsupported_threading`; absent device support remains `unsupported`.
CPU recording continues. The profiler never changes project settings.

`gpuStatus` is `pending` until the result arrives several Editor frames later, then `ready`, `unconfirmed` or an explicit
unavailable reason (`unsupported`, `unsupported_threading`, `invalidated_by_readback`, `warming_up`, `capacity`, `timeout`, `ambiguous`, `error`, `interrupted`).
`not_submitted` means no FX commands were submitted; `not_measured` covers other/older events.
`gpuMs` is null unless ready or unconfirmed; `gpuDetail` contains a diagnostic when available. Collection accepts
only positive single-invocation native intervals: zero/negative values or multiple populated samples
in the recorder snapshot are `ambiguous`, not zero-cost work. The marker is retired and the reason
is retained in `gpuDetail`. A positive interval is still an elapsed device interval, not independent
validation of driver timestamps or pure shader execution. Compare repeated paired runs, include an
identical-material control, and retain unavailable results rather than treating them as speedups.
Synchronous `Texture2D.ReadPixels` in WhimTex invalidates pending GPU queries before reading, including
queries from already-submitted operations and enclosing open GPU scopes. They report
`invalidated_by_readback`, never a truncated positive duration; completed earlier values are retained.
Open scopes still submit their matching EndSample before retiring. CPU wall/transfer timings remain
available. Pixel output, mipmap flags and render state are unchanged; no extra readback/wait is introduced.
Unrelated Editor/extensions can also affect native counters; these guards do not independently certify
every positive driver timestamp. The live diagnostic uses an explicit AsyncGPUReadback control.
A bounded pool permits
128 outstanding invocation markers. Each marker belongs to exactly one invocation until consumed;
frame totals are never divided among FX. Missing results time out after 10 seconds; their markers are
retired until script reload to prevent late-result misattribution. CPU recording continues if GPU capacity is exhausted.

Summary rows expose `gpuTotalMs`, `gpuMaxMs`, `gpuCount`, `gpuUnconfirmedCount`,
`gpuPendingCount`, `gpuUnavailableCount`. GPU aggregates include ready and unconfirmed samples;
`gpuUnconfirmedCount` is a subset of `gpuCount`, separate from missing coverage. No measured samples
means null, not zero. Compact hotspots and Comparison metrics carry the same unconfirmed count;
Comparison rows additionally expose `gpuUnconfirmed`. `gpuComparable` still describes complete numeric
coverage, not timing confidence. GPU deltas remain visible with a warning if either side is unconfirmed.
FX additionally exposes `cpuPrepareMs` and `cpuSubmitMs`.
The UI marks partial GPU totals with `*`, pending with `…`, unavailable with `—`.
The window automatically displays arriving GPU results. Stop does not discard pending queries; export requires
`pendingGpuSamples:0` (otherwise `capture_gpu_pending`). Clear/new capture abandons old queries safely.
Completed GPU values survive script reload; unfinished queries become `interrupted`, never fabricated timings.

The attached window checks status every 250 ms and refreshes changed rows at most twice per second,
including GPU results arriving after Stop. Unchanged stopped captures do not reread their tables.
Automatic updates preserve selected event identity, filters, sort order, folded branches and scroll position;
they do not render documents or clear caches. Closing/disabling the window stops its scheduled callback.
**Refresh** still updates immediately and discovers the current capture; committing a filter field or changing group selection also applies it. The virtualized event list retains one page
of 200 events; summary rows still cover all matches. Select an event to read its fields, use
**Only this operation** to filter by its operation, and **All operations** to remove that filter.
`—` denotes unavailable timing; open spans have an **Open** kind label. **Record**
discards the retained stopped capture; **Clear** only clears capture data. **Export Temp JSON**
exports all retained events regardless of the window's filters. Stop and wait for `openSpans:0` and `pendingGpuSamples:0`
before exporting; the controls update automatically after an automatic stop.

The three scrollable panels (summary, events and details) have draggable horizontal dividers;
their heights are retained by the window. **Capture settings** holds capacity, timeout and Document ID.
Category stripes distinguish general groups; cache hits/misses also have text labels.
Event rows show captured layer names and expandable branches. Click the arrow beside a stage or use
Left/Right on a selected parent; **Expand all** opens every branch and **Collapse all** leaves only
root events across all pages. Folding persists through Refresh
and pagination within a capture; a changed text filter opens matching paths, and a new capture resets
folding. Filter ancestors are muted/italic and identified in their tooltips. Nested indentation is
visually capped at eight levels; the full depth remains in details. IDs remain in tooltips and **Raw event data / IDs**.
Event duration bars compare wall time with that event's complete operation; instant events have no bar.
Summary rows are sorted by total duration, and their bars compare with the largest matching total,
not with a sum of overlapping rows. These bars are comparisons, not a timeline or GPU utilization.

## Unified JSON documents

`whimtex_document_json` takes an absolute `requestPath`. Equivalent C# methods are
`WhimTexApi.DocumentJsonFile(path)` and `WhimTexApi.DocumentJson(requestJson)`.
Content uses the [shared document format](JSON_FORMAT.md); commands are only an operation envelope.
There is no required `kind`: the same content can be opened, written, inserted or used for an explicit
layer replacement. Root `kind` is rejected; the caller chooses the operation.
The `document` object and its fields are optional. Open/write use version-3 defaults for missing
settings (512 × 512 canvas). Insert/replace use destination dimensions for each omitted source axis,
without resizing or changing destination output settings. Standalone validate uses format defaults.
Writers always include both canvas dimensions, including in Compact mode, to preserve placement context.

```json
{"apiVersion":1,"action":"serialize","assetPath":"Assets/Art/Icon.tiff","mode":"FullOptimized","allowDrawingOmission":false}
```

| Action | Inputs and result |
| --- | --- |
| `serialize` | `assetPath`, optional `layerIds` array, `mode`, `allowDrawingOmission`. Returns `json` containing all or selected layers plus `warnings`, in the same document format. |
| `export` | Same options without `layerIds`, plus a new `destinationPath` ending in `.json`. Does not modify the source. |
| `validate` | Exactly one `json` object or `sourcePath`; optional `compile` (default false). Detached parsing, no save. |
| `write` | `json` or `sourcePath`, destination TIFF or JSON `assetPath`, optional `save` (default true). Accepts whole-document or selected-layer exports; existing files require `expectedRevision`, including when `save:false`. |
| `insert` | `json` or `sourcePath`, existing `assetPath`, `expectedRevision`, optional `save` (default true). Adds remapped roots at the top without changing output settings. |
| `replace` | Same as insert plus `layerId`; exactly one incoming root. Keeps the target wrapper's ID, name, enabled state and placement in the stack. |
| `open` | `assetPath` of a JSON document. Opens/focuses its WhimTex window. |

`mode` is `Full`, `FullOptimized` (default), or `Compact`. Nonempty Drawing pixels require
`allowDrawingOmission: true` when serializing/exporting. Empty Drawing nodes remain. `write`, `insert`
and `replace` compile by default; `compile:false` only postpones compilation and does not make shader code trusted.
Failed FX compilation is reported in `warnings`; the JSON document still loads and retains the skipped
FX for repair. Structural JSON/reference errors still reject the request. `write` without `mode` preserves
the optional envelope `writeMode` (or uses FullOptimized when absent). Ordinary JSON saves retain that mode;
one-off exports do not change it. Insert/replace use the request's `mode` (FullOptimized if omitted)
when saving to JSON. A TIFF destination still uses TIFF save validation and may reject broken FX;
the soft JSON storage rule is not a bypass for TIFF validation.
Path edits refuse a conflicting open document. For `write`, `insert` and `replace`, `save:false` returns
`saved:false` and the prospective snapshot, then discards the transient model. It neither creates nor
overwrites the destination or its importer metadata; existing-file revision checks still apply.
Insert/replace do not overwrite the destination's dimensions, encoding, filtering or History.
Revisions include current Drawing pixels even for JSON-backed documents, including pending paint-surface
changes; this conflict-detection fingerprint does not imply that JSON can store Drawing pixels.
The regular batch, inspect and render APIs also support `.json` (including earlier `.whimtex.json` names). Independent Headless Live sessions
and TIFF storage diagnostics remain TIFF-specific. `json` is content, never an `ExecuteJson` request.
`Status` reports `format:"whimtex.document"` for JSON paths. JSON `open` reports `success:false` with
`errorCode:"open_failed"` and the load error when a recognized document cannot load; it does not show a modal
error dialog. An already open document is focused without reloading it.
Regular `Validate` returns JSON load warnings (including missing assets, omitted Drawing content and failed
FX) in `warnings`. Structural errors make `valid:false`; unavailable FX also fail the readiness check while
remaining loadable/editable. Check both the response `success` and `valid`, not just transport success.
JSON document fields use strict types, named enums and finite representable numbers, with field paths in errors.
They retain native storage semantics rather than imposing every UI/patch range; see the shared format reference.

For a browser AI without a Unity connection, use the separate [clipboard JSON/HLSL contract](AI/README.md).
Clipboard paste does not execute the operations described on this page.

The installed package also contains `Samples~/AgentTextures/manifest.json`: 38 procedural reference
recipes with individual PNG previews, descriptions and tags. The longest canvas axis is 256 pixels;
rectangular samples retain their aspect ratio. Read that folder's README before reuse. There are no
bundled TIFF duplicates or overview atlas. The `.whimtex.json` files are complete editable documents,
**not** requests for `ExecuteJson`: use the shared JSON API above, open a copy in an authorized folder,
or paste their layers. Do not modify bundled references in place.
Output encoding is stored in each JSON document. The three packed-data previews store raw linear values and
must be imported with sRGB disabled; generic diagnostic PNG renders instead use sRGB preview encoding.

WhimTex is installed as `com.dcfapixels.whimtex`, its namespace is `DCFApixels.WhimTex` and its
assemblies are `DCFApixels.WhimTex*`. Only current TIFF/JSON versions and current type/field names
are accepted. There is no old-version converter or historical-name scan through migration attributes.
Document `.asset` documents are unsupported. Public C#/agent aliases and old window layouts are not retained.
Use `WhimTexApi` and the `whimtex_*` commands; the JSON command contract remains v1.
Backward compatibility is not guaranteed for documents, presets, API or user settings during the redesign.
Canvas tool preferences use `DCFApixels.WhimTex.Canvas.*`; appearance preferences use
`DCFApixels.WhimTex.CanvasView.*`. The default library is the user's local application-data
`DCFApixels/WhimTex/Presets` folder; historical folders are not searched automatically.
Select an existing library explicitly in User Settings. Changing/resetting the path does not move,
rewrite or delete preset files; an existing custom folder setting is still read directly.


The API edits the same model and uses the same renderer, brush and save path as the window.
For reservations, generation and selected-region edits in an open (possibly unsaved) document,
use the [live editing API](LiveAgentAPI.md). The path-based batch contract below remains unchanged.
No WhimTex window or active selection is required. New agent documents may use a TIFF or JSON
`assetPath`, such as `Assets/Art/Icon.tiff` or `Assets/Art/Icon.json`. Old `.asset` documents are unsupported; edit them with a matching older checkout.
TIFF batches use a transient `WhimTexDocumentBuild` and the common TIFF writer; they do not create
or select a WhimTex window.

| Editing mode | Working state | Persistence and Undo |
| --- | --- | --- |
| Batch | Independent copy for one request | `save:true` writes TIFF or JSON according to the path; `save:false` discards edits after returning. No user Undo of the file |
| Headless Live | Independent candidate between requests | `complete` saves; `cancel`/reload discard. No user Undo |
| Assistant | The user's open document | Undo in the window; no automatic save |

### Output encoding (C#)

`WhimTexDocumentFile.GetOutputSrgb(document)` reads the pending output encoding;
`SetOutputSrgb(document, bool)` changes it on the Editor main thread and marks the document changed.
It does **not** write the TIFF or importer: call `Save` to apply it together with other pending edits.
`WhimTexDocument.outputSrgb` defaults to true. TIFF opening initializes it from the importer/carrier;
JSON restores `document.outputSrgb`, or the format default if omitted.
The UI supplies Undo; C# callers manage their own Undo records.
Inspector Apply instead queues a conversion of the **saved** model after import, without saving current
layer edits, then synchronizes the open document's encoding. This external reimport stops Live Update.
Float32 output always remains Linear; alpha is unchanged. This is not a working-space or source-pixel
conversion. These are C# methods, not batch operation names. The unified JSON storage field
`document.outputSrgb` is separate from this importer-conversion workflow.

Path-based inspection/rendering reads the disk document. To inspect unsaved window changes use
Assistant; to inspect an unsaved Headless candidate use its session. These states are not interchangeable.

The window's optional **Live Update** publishes preview pixels to the existing output texture on the GPU
without changing its asset reference or CPU pixel data. It is not an API autosave mode: save the open
document through its window, and use Assistant rendering to obtain current pixels rather than reading `OutputTexture.GetPixels()` during live
preview. Disabling live output restores the saved image; preview EV, channel display and Post FX are excluded.

## Shared editing operations

The operations below work in `whimtex_batch_execute`, the `operations` array of
`whimtex_headless_live` preview/render/complete requests, and `whimtex_assistant_execute`.
Headless preview retains its existing **replay from baseline** semantics: send the complete
candidate operation list, not an incremental patch to the previous preview.

For the open window, inspect with `whimtex_assistant_live` (`op:inspect`) first, then call
`whimtex_assistant_execute` with an absolute `requestPath` containing:

```json
{"apiVersion":1,"sessionId":"OPEN-SESSION","expectedRevision":"DOCUMENT-REVISION","dryRun":false,
 "operations":[{"op":"fx","layer":"LAYER-ID","edits":[
   {"op":"set","index":0,"parameters":{"_Opacity":0.5},"enabled":true}
 ]}]}
```

This synchronous batch forms one Undo step and does not save. It requires an idle document
without pending reservations/edit locks; it never cancels another job. After a timeout inspect
before retrying: operations such as duplicate and paint are not idempotent. A stale revision
is rejected. Use the reservation/lock workflow for longer generation tasks; its `changes.fx`
schema remains separate from this batch's `edits` schema. `dryRun` validates on an independent
copy; it does not compile new HLSL, render or calculate healing.

### Comment discussions

The UI snapshots its saved user nickname into each new message. Automatic detection first reads
the account's `UnityConnect.userInfo.primaryOrg` alias through an optional reflection lookup,
then falls back to the Unity email prefix before `@` through public `CloudProjectSettings.userName`.
The alias keeps the casing of its unambiguous ordinal case-insensitive match in `userInfo.organizationNames`.
Missing, unrelated or ambiguously cased display names retain the primary alias; names are not used to infer renamed organizations.
Missing, invalid or changed internal organization data cannot block the email fallback. The current
project's organization and the user's full name are never selected. Previously saved automatic full
names are treated as unassigned and resolved again; manually assigned names are preserved.
The name and its source are stored in EditorPrefs across projects. Unavailable account data
persists an unassigned state, displays `User` and retries on window opening or the next UI post.
Manual and successfully detected names remain stable until explicitly reset. Existing message
authors are never changed by this setting. Standalone model calls default to `User`; agent API
replies keep their independent `Assistant` default or explicit `author`.

Regular document and Assistant inspection include `document.comments`, including resolved threads.
Assistant reads published messages and committed edits in the open model even before the document is saved;
saved-path reads see only the saved version. Unposted replies and unsaved message-editor drafts are excluded.
Each entry contains `id`, permanent document-local display `number`, per-thread `revision`, `anchorUv`, `anchorPixels`,
`coordinates:"bottom-left canvas"`, RGB `color`, `status` (`InProgress`, `AwaitingReview`, `Resolved`),
`resolved` (true only for Resolved), and all `messages` with stable `id`,
`author`, plain `text`, ISO-8601 `createdUtc` and nullable `editedUtc` (null when never edited).
Deleting a discussion never renumbers the others or reuses its number; continue using IDs for operations.
Draft text, attachments and card layout stay in the local editor window, outside the model and API.

Each message also has `attachments`, `workId` and `agentMessage`. An attachment reports `kind`
(`layer`, `asset` or `mask`). Layer/asset entries report `layerId` or `assetGuid`/decimal-string `localId`, current `name`, `assetPath`
and `missing`. Layer IDs belong to this document; assets retain GUID/sub-asset identity across moves.
Missing targets remain references and never resolve by display name. Attachment content is context,
not permission to edit external assets. Empty editor fields are drafts and are not stored.

Mask entries report `maskId`, `revision`, original cropped `width`/`height`, `canvasBounds:[x,y,w,h]`
clipped to the current canvas and `localToCanvas` (a 3×3 affine matrix mapping bottom-left local
UV into bottom-left canvas UV). The stored snapshot retains linear 8-bit coverage, including soft
edges, holes and disconnected regions. Crop/Scale/Orientation update its placement without
resampling the original; off-canvas pixels remain available after expanding the canvas. Individual
layer transforms do not move masks. Sampling uses nearest source pixels at canvas pixel centres.

Read exact mask pixels without changing the user's working selection:

```json
{"apiVersion":1,"op":"mask","assistantSessionId":"SESSION","maskRef":{"maskId":"MASK-ID","expectedRevision":"MASK-REVISION"}}
```

`op:mask` accepts the same three document sources. It returns `mask` metadata, `maskPath` (the
current full-canvas mask) and `sourceMaskPath` (retained cropped source), unique PNG files under
`Temp/WhimTex/Agent`. Coverage is copied exactly into RGB, alpha is 255; use the **red** channel,
not alpha. PNG orientation is ordinary top-left; placement metadata remains bottom-left. No
display gamma is applied to coverage. For image context use `op:render`, or fetch the combined `op:context` bundle below. Mask revisions
include original coverage, placement and current canvas dimensions. Missing/stale references fail
with `mask_not_found`/`mask_revision_conflict`. Export needs graphics; listing does not decode PNGs.

Threads additionally expose `feedbackRevision`. Unlike the full `revision`, it excludes intact
API-authored replies and presentation metadata (`color`, `useAuthorColor`, `colorAuthorId`, message
`authorId`/`authorColor`). The first message always participates, including agent-created discussions.
Editing an agent reply makes it feedback again; new user messages, text, authorship text, attachments,
anchors and all status changes invalidate this revision. A check accepts exactly
one of `expectedRevision` or `expectedFeedbackRevision`. Use the latter for a long-running task,
so progress replies do not invalidate its guards. Full revision checks remain available.

`whimtex_comments` reads a JSON request file; C# equivalents are `WhimTexApi.CommentsFile(path)`
and `WhimTexApi.CommentsJson(json)`. Exactly one document source is required:
`assetPath` (saved TIFF/JSON), `assistantSessionId` (current unsaved window model) or
`headlessSessionId` (current Headless candidate). These sources are not interchangeable.

```json
{"apiVersion":1,"op":"list","assistantSessionId":"SESSION"}
```

Render the selected discussion:

```json
{"apiVersion":1,"op":"render","assistantSessionId":"SESSION","commentIds":["COMMENT-ID"],"maxSize":1024}
```

Render a focused crop:

```json
{"apiVersion":1,"op":"render","assistantSessionId":"SESSION","commentIds":["COMMENT-ID"],"focusCommentId":"COMMENT-ID","radius":128,"maxSize":1024}
```

`op` defaults to `list`. Without `commentIds`, the result includes unresolved threads; set
`includeResolved:true` to include all. Explicit unique `commentIds` (1..256) select exactly those
threads, including resolved ones; a missing ID fails with `comment_not_found`. Results default to
permanent-number order and include document `revision`, dimensions, `totalCount`, `matchedCount` and full selected discussions.

For `list`, `render` and `context`, optional `filter` accepts `status` (`Open`, `All`, `InProgress`,
`AwaitingReview`, `Resolved`), exact case-insensitive participant `author`, substring `search`,
`time` (`Created` or `Activity`, default Activity), and `fromUtc` / `toUtc` ISO-8601 timestamps with
an explicit timezone. The lower bound is inclusive, the upper bound exclusive. Filters intersect
explicit IDs; the status filter overrides the unresolved default. `sort` is `Number` (default),
`Created`, `Activity` or `Author`; `descending:true` reverses the selected key. Author sorting uses
the initial author; activity is the latest message creation/edit, not reading or marker/color changes.
Ties use permanent number and ID. Queries never reorder stored discussions or change revisions.
`filter.search` and the Comments search field also match a discussion's complete `link` using ordinal,
case-sensitive equality after trimming surrounding whitespace. No URI decoding or normalization is
applied for search. Message/author/number substring matching remains case-insensitive; all other filters
still intersect the result. Search does not open another document or navigate to the discussion.

```json
{"apiVersion":1,"op":"list","assistantSessionId":"SESSION","filter":{"author":"Artist","time":"Activity","fromUtc":"2026-10-01T00:00:00Z"},"sort":"Activity","descending":true}
```

Each snapshot additionally exposes thread `createdUtc`, `lastActivityUtc`, `link` and `persistentLink`.
Saved project documents use `whimtex://comment/asset/ASSET-GUID/COMMENT-ID` (URI-escaped ID), so renames
retain navigation. Save new discussions before sharing a persistent link. Unsaved windows expose
temporary `session` links valid only for that current window session; detached unsaved/headless candidates
may have `link:null`. No automatic save is performed. Canvas clustering is view-only: agent queries and
context exports retain every individual discussion and its exact anchor.

`op:open` is the explicit UI navigation exception to the one-source rule: supply only `apiVersion`,
`op` and `link`. It opens the matching discussion, activates Comment, clears excluding UI filters and
centres its anchor. It returns `sessionId`, `commentId`, `navigated:true`, `applied:false`; unavailable,
expired or deleted targets fail with `comment_link_unavailable`. It never guesses a target by name.

```json
{"apiVersion":1,"op":"open","link":"whimtex://comment/asset/0123456789abcdef0123456789abcdef/COMMENT-ID"}
```

Links open through canvas paste, clickable link buttons in messages or this API. The Comments search
field finds links in the current document. External applications do not launch Unity through this scheme;
no OS protocol is registered.

Each discussion includes `useAuthorColor` and its effective `color`. New UI discussions follow
their creator's user color; a manual thread color disables that link until reset. Existing comments
without the flag remain manual, including the former default blue. TIFF/document JSON also retain
an optional `colorAuthorId` local profile GUID and the latest `color` snapshot. Profile identity
survives nickname changes; it is not a Unity account ID. Other profiles retain the snapshot rather
than applying their own preference. A reply never changes the thread creator's color.
Each message also exposes effective `authorColor` as an RGB triple for the stripe beside its
author. It is independent of the discussion color. Stored messages can retain a local `authorId`
and an opaque RGBA `authorColor` snapshot; omitted/all-zero snapshots resolve by author name.
Author color metadata does not invalidate agent message provenance.

`render` accepts at most 128 selected comments. It returns a clean `imagePath` and separate
`annotatedPath`, both unique PNGs under `Temp/WhimTex/Agent`, plus `imageWidth`, `imageHeight` and
`cropBounds:[x,y,width,height]` in bottom-left canvas pixels. No document pixels are modified.
`maxSize` is 64..4096 (default 1024); images downscale to fit and never upscale. Optional
`focusCommentId` must be in the selected set. `radius` is 1..16384 canvas pixels, default 128:
the crop includes the anchor pixel and that many neighbours on each side, clipped to the canvas.
Off-canvas comments remain in overview metadata with `inView:false`; focusing one fails with
`comment_outside_canvas`. Each returned comment has `imagePoint:[x,y]` in top-left output-image
pixels and `inView`. Markers are navigation cues, not selection masks or layer bindings. Overlapping
markers can be separated by requesting individual IDs or focused crops. Rendering requires graphics.

### Comment context bundle

```json
{"apiVersion":1,"op":"context","assistantSessionId":"SESSION","commentIds":["COMMENT-ID"],"maxSize":1024}
```

`context` accepts the same sources, filters and render options as `render`. One response includes
full selected discussions, document revision, clean/numbered images, `checkerboardPath`, frozen
`commentChecks` and compact `layers` (id, name, parent, type, enabled, contentRevision). Read full
settings separately only for relevant layers. `masks` contains each attachment's `maskRef`, metadata,
exact full-canvas/source PNGs and a blue `overlayPath` on the rendered context, with `overlayBounds`.
Use the mask ID to match exports to message attachments. Coverage PNGs are data; overlays are visual aids.
No working selection or document content is changed. At most 32 masks and 67,108,864 full-canvas mask
pixels may be exported in one bundle; narrow `commentIds` for larger discussions.

Live preview's `review:true` additionally provides before/after images, a difference map and a
ready-to-use changed-area mask attachment. See [live comparisons](LiveAgentAPI.md#optional-trial-renders).

The shared `comment` operation is supported by Batch, Headless operations and Assistant Execute:

```json
{"apiVersion":1,"sessionId":"SESSION","expectedRevision":"DOCUMENT-REVISION","operations":[
  {"op":"comment","commentId":"COMMENT-ID","expectedFeedbackRevision":"FEEDBACK-REVISION"},
  {"op":"set","layer":"LAYER-ID","settings":{"opacity":0.8}}
]}
```

An operation with only `commentId` and either `expectedFeedbackRevision` or `expectedRevision`
checks that discussion without changing it. Put these checks before image edits to protect the feedback the edits were based on.
After rendering and verifying the result, post a reply with freshly inspected revisions:

```json
{"apiVersion":1,"sessionId":"SESSION","expectedRevision":"CURRENT-DOCUMENT-REVISION","workId":"WORK-UUID","requestId":"REPLY-UUID","operations":[
  {"op":"comment","commentId":"COMMENT-ID","expectedFeedbackRevision":"CURRENT-FEEDBACK-REVISION","reply":"Softened the edge on layer LAYER-ID and checked the rendered result.","author":"Assistant","status":"AwaitingReview"}
]}
```

`reply` is nonblank plain text up to 16,384 UTF-16 characters; `author` defaults to `Assistant`
and is nonblank up to 128 characters. `author` requires `reply`. Optional `status` accepts exactly
`InProgress`, `AwaitingReview` or `Resolved`; all transitions are allowed, with or without a reply.
Use `AwaitingReview` when a verified result is ready for user review. Optional `resolved:true/false`
maps to `Resolved` / `InProgress`; do not supply it together with `status`. Omission preserves status.
Reading, rendering and replying never advance status implicitly. Open queries include both In Progress
and Awaiting Review. Status changes participate in Undo, document/thread revisions and `feedbackRevision`.
TIFF/JSON retain this task state with the discussion: `resolved` and `awaitingReview` are mutually exclusive
flags; both false means In Progress. There is no per-user read/unread metadata.
Optional `attachments` requires `reply` and contains at most 32 entries. Each entry selects exactly
one source: `{ "layer":"ID-or-@alias" }`, `{ "assetPath":"Assets/Example.png" }`,
`{ "assetGuid":"GUID", "localId":"21300000" }`. Asset paths may also be under Packages.
Optional localId selects a sub-asset; it accepts a decimal string (recommended for exact 64-bit IDs)
or an integer. Targets must exist when attaching them; later deletion leaves a visible missing link.
Selection-mask entries use one of these alternatives:

- `{"selection":true}` snapshots the current nonempty working selection. This is available only
  through Assistant Execute; attach before a resize operation in the same batch.
- `{"maskRef":{"maskId":"MASK-ID","expectedRevision":"MASK-REVISION"}}` copies an existing
  attachment with a new ID and independent placement.
- `{"maskImagePath":"D:/Work/mask.png","channel":"red","bounds":[0,0,512,512]}` attaches an
  agent-prepared PNG. Use an absolute local PNG path, explicit `red` or `alpha` channel and
  `[x,y,width,height]` in bottom-left canvas pixels. Width/height are positive and may differ from
  image dimensions; placement may extend outside the canvas. Raw channel bytes are coverage,
  without gamma conversion. Empty masks are rejected; zero borders are cropped automatically.

PNG inputs are bounded to 64 MiB, 16384 per axis and 16,777,216 pixels. All attachment kinds
share the 32-entry message limit. Masks additionally share a 67,108,864 source-pixel budget per
document and the 16 MiB comments-block limit. File/mask-reference entries also work in Batch and
Headless operations. No attachment operation changes the working selection or imports a Unity asset.
The API never edits or deletes existing messages. Existing thread/message/storage limits apply.
The operation result has null `layerId`/`name`, the thread's `commentId`, and `messageId` when replying;
the returned snapshot contains updated messages.
Dry runs validate on a copy. Assistant changes participate in one batch Undo and do not save.

Create a new discussion with the shared `commentCreate` operation:

```json
{"apiVersion":1,"sessionId":"SESSION","expectedRevision":"DOCUMENT-REVISION",
 "workId":"review-edges","requestId":"note-1","operations":[
  {"op":"commentCreate","anchorUv":[0.3,0.7],"text":"This edge needs review.","author":"Assistant",
   "attachments":[{"layer":"LAYER-ID"}]}
 ]}
```

`anchorUv` is a required pair of finite coordinates in 0..1, measured from the bottom-left of the
canvas; `text` is required and has the same nonblank 16,384-character limit as `reply`.
`author` and `attachments` follow the reply contract, including aliases of layers created earlier
in the same batch. A new thread is unresolved and uses its author's automatic color. Its root
message retains agent provenance and the batch work ID. The operation returns stable `commentId`
and `messageId`; use these IDs for subsequent replies. Creation has no thread revision to check,
but Assistant Execute still requires the current document revision. Validation, dry-run and Undo
cover the whole batch, including its new layers and discussions. Creation-only batches may run
during a pending job. Use `workId`/`requestId` for retry protection: a replay returns the original
IDs in `receipt.operations`; without these IDs, inspect before retrying an ambiguous result.
Batch and Headless retain their usual save/replay contracts. A stale thread returns
`comment_revision_conflict`; missing threads return `comment_not_found`. This guard still applies
when a caller supplies a newer document revision. Reinspect after ambiguous timeouts before
retrying a reply without work/request IDs: those appends are not idempotent. A reply changes its thread revision, so combine reply
and status in one operation, or inspect again before the next operation on the same thread.

For long generation/edit jobs, JSON `begin` and `lock` accept `commentChecks`, an array of 1..256
checks with unique `commentId` values and exactly one of `expectedFeedbackRevision` (recommended)
or `expectedRevision`. The `context` bundle already returns checks with `expectedFeedbackRevision`.
They are checked before reservation/locking and again
before preview/completion; forks inherit and check them. Status returns the frozen checks.
Changed/deleted discussions block delivery without closing the job. Cancel/unlock remains available.
An identical retry of a completed job returns its original receipt rather than checking later edits.
The short CLI/C# begin and lock helpers do not expose `commentChecks`; use `whimtex_assistant_live`
with a JSON request. See [comment-driven editing](LiveAgentAPI.md#comment-driven-editing).

### Work IDs and safe retries

Assistant Execute accepts optional `workId` and `requestId`, supplied together, each nonblank and at
most 128 characters. Use one workId per assignment and a different requestId per edit/reply step.
An applied batch stores its compact operation results, including result layer and reply message IDs,
in the same Undo transaction. An identical retry returns `replayed:true`, `applied:false` and the
original `receipt`, even with a stale document revision. The returned document snapshot is current.
Request property order, sessionId, document expectedRevision and dryRun are excluded from request
identity; the operations and their thread guards must remain identical. Different operations under
the same IDs return `request_conflict`. Dry runs do not store receipts. Requests without IDs retain
the earlier non-idempotent behavior.

```json
{"apiVersion":1,"sessionId":"SESSION","workId":"WORK-UUID","requestId":"REPLY-UUID","expectedRevision":"DOCUMENT-REVISION","operations":[
  {"op":"comment","commentId":"COMMENT-ID","expectedFeedbackRevision":"FEEDBACK-REVISION","reply":"Added the requested heart and checked the image.","attachments":[{"layer":"RESULT-LAYER-ID"}]}
]}
```

Query completed work after a reconnect:

```json
{"apiVersion":1,"op":"work","assistantSessionId":"SESSION","workId":"WORK-UUID"}
```

The `work` response contains completed `receipts` for this workId. It supports the same saved,
Assistant and Headless read sources as list/render. Comment-only Assistant batches may run while
live jobs are pending, allowing progress replies; other batches still require jobs to finish.
JSON begin/lock accepts workId, and forks inherit it. Completion stores a receipt linked to its
begin/fork/lock requestId, comment checks and result layer. After loss of transient jobs, inspect the
receipts; retrying a completed begin/lock returns its receipt without creating another job.
Script reload interrupts pending Assistant jobs but retains checked recovery data in the same Editor process.
Rediscover the original open document's `recoverableJobs` and explicitly call `op:resume` with its current
session ID and original job ID. Resume validates the source, target, feedback and masks and reuses the
reservation; it does not repeat completed work. Recovery is unavailable after Editor restart, lost temporary
files or closing/switching the source document. See [live recovery](LiveAgentAPI.md#status-cancellation-and-recovery).

Receipts persist with ordinary TIFF/JSON saves, participate in Undo, and are omitted from layer
fragments. They do not autosave the document. A session journal detects an undone/removed receipt
and returns `request_undone` instead of silently reapplying it; the journal lasts until Editor exit.
Redo restores the receipt. After restarting from an older unsaved state, inspect the document before
starting again. Up to 2048 receipts and 4 MiB of receipt metadata are retained per document; reaching
the limit rejects new recorded work instead of evicting deduplication history. Historical receipts
do not prove that a result layer/message still exists or has not subsequently changed.

### FX edits and presets

`whimtex_fx_catalog` accepts an optional `query` category/name/group substring. It discovers only `.hlsl`
presets whose first physical line is `// @whimtex-effect Category/Name`, in Assets, installed
packages and the user preset library. ShaderFX `.asset`, Material and standalone ShaderLab
sources are not catalog entries. Each result has `id`, `path`, `name`,
`kind`, `group` (null when absent), `error` and `icon`. A leading `// @group("Name")`
organizes presets within each category submenu into alphabetical blocks separated by lines,
without adding navigation levels or numeric priorities. See [preset list groups](ShaderFX.md#preset-list-groups).
The icon descriptor reports `kind`, external `reference` and
`warning`; document FX inspection returns the same fields. See [FX icons](ShaderFX.md#fx-icons).
Pass an exact returned `presetId` to obtain `effect` with code and parameter
types, current defaults, hard/soft bounds and enum options. Use IDs, not display names; invalid
presets remain visible with an error. This command does not insert anything into a document.
Project/package HLSL presets retain a source link; user-library presets are embedded snapshots.
All resulting FX instances are document-owned, with independent values and enabled state.

`{"op":"fx","layer":"ID-or-@alias","edits":[...]}` supports:

| Edit | Fields and behavior |
|---|---|
| `add` | Exactly one of `code` or `presetId`; optional insertion `index` (default append), `parameters`, `enabled`. Raw code supports optional `includeBasePath`, required for relative includes |
| `replace` | Existing `index`, otherwise the same fields as add |
| `set` | Existing `index`, optional `parameters` and `enabled`; preserves code and does not recompile HLSL |
| `remove` | Existing `index` |
| `move` | Existing `index` and `toIndex`, the final index after removal |
| `copy` | `sourceLayer` ID or alias, `sourceIndex`; optional destination insertion `index`, `parameters`, `enabled`. Independent copy in the destination layer |
| `apply` | Existing `index`; bake that FX and all preceding entries, including disabled entries (removed without rendering). Later FX remain |
| `applyAll` | Bake the complete nonempty stack |

`add`, `replace`, `set` and `copy` also accept `randomizeSeeds:["_Seed"]`. Names must identify
explicit `seed(float)` declarations; the first seed control defines each inclusive integer range.
Do not include the same name in `parameters`. Each FX gets an independent choice when executed;
validation/dry runs do not randomize. This uses the ordinary copy-on-write, Undo and invalidation
path without recompiling a `set`. Catalog/live parameter snapshots expose `seed`, `seedMinimum`
and `seedMaximum`; bounds are null for other parameters. Values remain fixed until explicitly edited.

`parameters` is a **name/value object**, not a replacement list. Types are inferred from existing
declarations; unknown names, invalid enum values and hard-range violations fail. Use numbers for
enums, booleans only for declared bool types and the value formats below. Unmentioned values remain.
Indices refer to the latest inspected stack and change after each edit. Limits: 32 edits per
operation, 32 FX per layer, 128 parameters per effect, 65,536 characters of raw HLSL.

| Parameter type | Shared `fx.edits[].parameters` value |
| --- | --- |
| `toggle(type)` | Boolean for bool; numeric 0/1 for int/uint/half/float. Vectors use arrays of exactly the declared component count. |
| `Float` | Finite number within -1000000..1000000 (−16777216…16777216 for explicit `seed`) and the declaration's hard bounds; soft bounds may be exceeded |
| `enum(type)` | Number matching a declared option, within the chosen scalar type range; float/half permit fractions. |
| `seed(type)` | Integral number within its type and hard limits; float additionally stays within ±16777216. Soft limits may be exceeded. |
| `color(type)` | Scalar or exact-length array according to `valueType`; booleans for bool, numbers within the declared type range otherwise. Integer values remain exact; float/half RGB retains encoded-color semantics. |
| `Vector2`, `Point` | Two numeric components, each -1000000..1000000 |
| `Vector3`, `Normal` | Three numeric components, each -1000000..1000000; Normal is normalized by the API |
| `Vector` | Four numeric components, each -1000000..1000000; this is HLSL `float4` |
| `Texture2D` | Imported `Assets/...` or `Packages/...` Texture2D path, `"self"`, `"none"`, or `{"layer":"STABLE-LAYER-ID"}`. Layer bindings require actual IDs, not `@aliases`; missing sources, cycles and the document's own output are rejected |
| `Curve` | `"linear"`, `"easeIn"`, `"easeOut"`, `"easeInOut"`, `"one"` or a `"keys(...)"` string; [curve syntax](ShaderFX.md#curve-parameters) |
| `Gradient` | The shared ordered stop array or gradient object from [Color and gradient input](#color-and-gradient-input), with Perceptual as the omitted mode |
| `Transform2D` | Partial object with `position:[x,y]`, `size:[x,y]`, `rotation`, or nine row-major `matrix` values. Matrix excludes the other three fields. Components are finite, within ±1e15; size components must have absolute value at least 0.00001. Position and size use normalized input units (center `[0.5,0.5]`, full size `[1,1]`); rotation uses counterclockwise degrees |

The reservation/lock `fx` format in [LiveAgentAPI](LiveAgentAPI.md#inline-shader-fx)
accepts inline code with `@param` defaults. It does not accept this name/value object or
inspection's parameter records. Finish pending jobs before using `whimtex_assistant_execute`
for FX value edits. See [HLSL declarations](ShaderFX.md#parameter-declarations)
for the corresponding source syntax.

`Layer.fx` is a `List<ShaderFX>`; these edits operate on document-owned HLSL FX with independent
values. JSON list items are `ShaderFX` models, `null`, or internal `$ref` references to those
models, never `$asset`; see the [JSON model contract](JSON_FORMAT.md#envelope-and-model).
There is no external Material/Shader/ShaderFX `.asset` source mode or
Material render branch. Compiled Unity Shaders and internal Materials remain HLSL execution
resources. Old external FX references in JSON/TIFF are rejected with a clear diagnostic before
editing, without migration, silent omission or document rewrite. Supported HLSL source links
and texture asset parameters are unaffected. In the editor, **Code → Embed Copy** remains available
to detach a linked `.hlsl` source and edit its code in the document while keeping values.

Prefer `presetId` for existing built-in/project effects, with parameter overrides for the desired look.
Do not copy their unchanged HLSL into `code`: that creates an independent inline effect and loses
the catalog link. Use `set` for parameter-only changes. Raw `code` is for custom algorithms or
intentionally independent variants, not the default way to reuse a preset.

The built-in `Color/Gradient Map` samples full gradient RGBA from `_SourceChannel`: Luminance=0
(default, unchanged), R=1, G=2, B=3, Alpha=4. Luminance uses nonnegative linear RGB weights
0.2126/0.7152/0.0722; it and individual RGB sources clamp to 0..1 and use sRGB encoding before
lookup. Alpha clamps to 0..1 without sRGB conversion. `_Reverse` precedes `_Mapping`, then the
result samples the gradient. This selects one source, not independent per-channel remapping. Output alpha is
`sourceAlpha * lerp(1, gradientAlpha, saturate(_Opacity))`; an opaque gradient preserves source alpha,
zero strength bypasses the effect, and transparent input stays transparent. `_Mapping` and `_Reverse`
affect the lookup for both RGB and alpha.

Applying to a non-Drawing layer requires `allowRasterize:true` **inside the apply edit**.
This consents to rasterization, including flattening a group. Logical transforms remain editable.
Shader Processor captures its backdrop and maps Normal to Overwrite with its original opacity
blending; lower layers remain separate. See [baking details](ShaderFX.md#baking-implementation).

### Layer structure

- `{"op":"delete","layer":"ID"}` removes a layer or group subtree, not its external source assets.
- `{"op":"duplicate","layer":"ID","as":"copy"}` inserts an independent copy using normal layer duplication, including owned pixels/FX and internal reference remapping.
- `{"op":"merge","layer":"ID","others":["OTHER-ID"],"keepSources":false,"as":"merged"}` uses normal merge rendering and returns the new Drawing layer. `keepSources:true` retains originals. Merge is explicitly rasterizing; its composite placement follows the normal merge operation.
- `{"op":"convertToDrawing","layer":"ID","as":"drawing"}` uses Keep Transform conversion, preserving ordinary layer FX. Groups are flattened using the normal group conversion rules. Shader Processor must use FX apply/applyAll to capture its lower input instead.

`as` is optional for duplicate/merge/conversion; subsequent operations may use `@alias`.
Removed aliases cannot address detached layers. Existing dependency validation still applies:
do not delete an input while leaving an invalid explicit target reference.

### Blur, smudge and healing strokes

These tools require Drawing; convert explicitly when appropriate. Points are canvas pixels with a
**top-left origin**. They do not implicitly use the window's area selection or brush settings.
`tiled:true` wraps the canvas boundary; repeating layer transforms are not supported (Clip and
Unbounded are accepted). Size is 1..512 px, hardness 0..1, up to 4096 points.

```json
{"op":"blurStroke","layer":"DRAWING-ID","points":[[10,20],[40,20]],
 "size":24,"hardness":0.5,"strength":0.7,"flow":1,"source":"CurrentAndBelow","tiled":true}
```

Blur source is `CurrentLayer` (default), `CurrentAndBelow` (includes current), or `AllLayers`.
It is frozen at stroke start. Repair operations (`blurStroke`, `smudgeStroke`, `healStroke`)
also accept `writeChannels` (0..15, default 15; R=1, G=2, B=4, A=8), independent of source sampling.
Disabled channels retain previous straight values. Disabling A preserves transparency and fully
transparent pixels, including when erasing.
UI path/pressure smoothing is an input preference; API point lists are used exactly as supplied.
Strength and flow are each 0..1, default 1. Work is limited to
67,108,864 canvas-pixel × input-point passes; simplify a path rather than repeating thousands of points.

```json
{"op":"smudgeStroke","layer":"DRAWING-ID","points":[[10,20],[40,20]],
 "size":24,"hardness":0.8,"strength":0.8,"flow":1,"mixing":0.25,"source":"CurrentLayer","tiled":true}
```

Smudge carries existing RGBA pixels along the path, without adding a painting color.
One point alone does not paint. Strength (0..1, default 0.8) scales transport and controls
retention of the picked-up patch; flow (0..1, default 1) scales each dab independently.
Mixing (0..1, default 0.25) couples two mechanisms within each dab: deformation is
scaled by `1 - mixing`, while carried-color deposition is scaled by `mixing`.
At 0, only a coordinate map is advected; colors are reconstructed from a mipmapped,
immutable stroke-start image, so repeated movement does not repeatedly filter RGB.
Partial Mixing advects positive accumulated paint contributions and original-image
weights, then deposits and picks up from their shared result. It is not interpolation
between finished strokes. No contrast compensation or signed color residuals are used.
At 1, only the carried-color mechanism is active.
Below full Strength, the carry is refreshed after deposition from the painted result,
including mask, selection and Flow: carry = lerp(paintedPatch, previousCarry, strength).
At Strength 1, the initial captured patch remains unchanged throughout the stroke;
Hardness and Flow affect the dab, not this retention. No foreground color is added;
larger Mixing intentionally produces cumulative color blending.
Sources match Blur. CurrentLayer samples
raw Drawing pixels; visible-stack sources freeze other layers/FX at stroke start
and feed back this stroke's deformations and deposits. Writes affect only the selected Drawing.
Spacing is 2.5% of the tip diameter, at least one canvas pixel, and follows canvas distance
rather than input-point frequency. Hard tips have a pixel-wide antialiased boundary. Color pickup and feedback
stay on the source pixel grid, separately from the smoothly positioned round mask:
CurrentLayer uses native Drawing resolution; visible-stack sources use canvas resolution.
At full Strength, only deposition reconstructs the frozen patch at fractional pointer
positions; the unchanged carry is never replaced by that reconstruction.
The accumulator preserves individual pixels rather than reducing large tips to 1024×1024.
Transformed native footprints may be rectangular and grow during a projective stroke without
rescaling carried pixels. Up to 32,768 generated stamps are accepted, with a 268,435,456
native tip-pixel × stamp budget. Dry run checks the padded source-space footprint at path
vertices. Each carry buffer is limited to 18,874,368 pixels and the device's maximum texture
dimensions; a footprint crossing the inverse projection horizon is rejected rather than
downsampled. Use a smaller tip or adjust the layer transform if these limits are exceeded.
Deformation snapshots and the growing stroke-history region are also limited to
18,874,368 native pixels. History is cropped to the touched area, with tile-aligned
growth; rendering operates on dab bounds rather than running full-canvas passes per dab.

```json
{"op":"healStroke","layer":"DRAWING-ID","points":[[10,20],[40,20]],
 "size":24,"hardness":0.8,"source":"CurrentLayer","search":64,
 "quality":"Balanced","seed":1,"transparentOnly":false,"tiled":true}
```

Healing sources are `CurrentLayer` (raw pixels before FX) and `CurrentAndBelow`. Search is 8..512
canvas pixels; quality is Fast/Balanced/High. Instead of points, `maskPath` accepts an imported
project/package texture mapped over the canvas: linear red is repair coverage (white repairs).
Use linear-data import settings for an exact grayscale mask. Alpha is not used as coverage.
The API uses the same buffered stroke mask, tiled recentering and fill algorithm as the tool.
Healing currently limits the full canvas to 1,048,576 pixels and cancels computation after
20 seconds; cancellation fails the operation without applying a patch. It is synchronous,
not a background job. All three tools limit total generated stroke stamps to 32,768.

### Diagnostic rendering

`whimtex_render_probe` takes an absolute `requestPath`. C# equivalents are
`WhimTexApi.RenderProbeJson/File`, `AssistantExecuteJson/File` and `FxCatalog(query, presetId)`.

```json
{"apiVersion":1,"assistantSessionId":"OPEN-SESSION","layer":"LAYER-ID",
 "stage":"afterFx","index":0,"channel":"a","maxSize":1024}
```

Choose exactly one source: TIFF/JSON `assetPath`, `assistantSessionId`, or `headlessSessionId`.
Stages are `composite` (no layer/index), `layer` (all its FX), `beforeFx` and `afterFx` (layer + index).
Layer/FX stages capture the canvas-sized input pipeline before outer opacity, blending, channelMapping
and clipping; a Shader Processor uses its actual stack-position backdrop. They are not a solo
view of the final composited layer. Disabled FX have identical before/after images.
Channels: `rgba` (default), `r`, `g`, `b`, `a` (opaque grayscale). `maxSize` is 1..4096, default 1024.
Optional `outputPath` must be a new `Temp/WhimTex/*.png`; otherwise a unique path is generated.
The API response exposes the absolute PNG path as top-level `outputPath`, with `width`/`height`,
`sourceWidth`/`sourceHeight`, full-resolution `linearMinimum`/`linearMaximum` RGBA arrays and
`nonFiniteComponents`. PNG is a display preview, not lossless HDR data. Rendering uses an
independent document copy and never edits or saves the source. Read these fields from the inner
WhimTex API result, not the CLI transport wrapper.

## Layer identity and behaviour

`WhimTexDocument.layers` contains stable `Layer` objects. Common settings, GUIDs, FX references,
group compositing and `children` belong to `Layer`; only the type-specific `LayerBehaviour` is
polymorphic. Use `layer.Behaviour is DrawingLayerBehaviour drawing` to access owned pixels or
other behaviour-specific methods. `layer.SetBehaviour(new NoiseLayerBehaviour())` replaces the
behaviour without replacing the layer or changing its common settings. Document changes still
need the normal Undo/`MarkChanged` workflow; use the JSON API for agent authoring.

A behaviour can belong to only one layer. Do not share one behaviour between wrappers.
Swapping behaviours releases transient rendering resources but does not destroy the old
Drawing texture: Undo or the caller may still own it. Finish/synchronize an active stroke
before recording the structural Undo snapshot, and use the existing document removal or
conversion workflow when the old owned assets should also be deleted.
Replacing a populated group with a non-group is rejected; use the editor's explicit conversion
to Drawing or ungroup it first. Conversion and live-generation completion retain the layer wrapper.

Snapshots include `behaviourMissing`. A missing behaviour does not remove the layer's name, GUID,
common settings or group children. It does not render. Its `type` is `missing`, or `group` when the
saved node still contains a group. Type-specific commands require an available behaviour.
The Layer Settings recovery action can transfer compatible saved behaviour fields; common fields
are retained directly, not reconstructed from missing-type metadata. Recovery matches a separate
behaviour identifier rather than list positions or names.

Earlier inheritance-based documents are intentionally incompatible. Keep their originals and use
the earlier package revision to render/export them. No automatic colour, naming or repeat-mode
migrations are applied to the new document model.

## Generated image → document

1. Generate a PNG/JPEG using the agent's image tool, or use a user-provided local image.
2. Import it to a new asset path. Import never overwrites; for an existing imported texture, skip this step.

```powershell
unity command whimtex_image_import --sourcePath 'C:/Temp/generated.png' --assetPath 'Assets/Art/AgentIcon/source.png' --project-path 'D:/Projects/MyGame' --format json
```

The API copies only that file, preserves its dimensions up to the resource limit, disables texture
compression/mipmaps and keeps non-power-of-two dimensions. It does not change existing source importers.
PNG/JPEG only; the destination must use the same extension. No URLs or automatic image generation.

3. Write a JSON batch outside `Assets`, for example `Temp/WhimTex/create.json`:

```json
{
  "apiVersion": 1,
  "assetPath": "Assets/Art/AgentIcon/Icon.tiff",
  "create": true,
  "width": 1024,
  "height": 1024,
  "save": true,
  "operations": [
    {"op": "add", "type": "group", "as": "art", "settings": {"name": "Artwork"}},
    {
      "op": "add", "type": "file", "as": "image", "parent": "@art",
      "settings": {"source": "Assets/Art/AgentIcon/source.png", "filter": "Source"},
      "transform": {"position": [20, -12], "rotation": 8, "tiling": "Clip"}
    }
  ]
}
```

4. Optionally run the same request with `dryRun:true`, then change it to false to apply:

```powershell
unity command whimtex_batch_execute --requestPath 'D:/Projects/MyGame/Temp/WhimTex/create.json' --project-path 'D:/Projects/MyGame' --format json
unity command whimtex_document_render --assetPath 'Assets/Art/AgentIcon/Icon.tiff' --outputPath 'Temp/WhimTex/icon-v1.png' --project-path 'D:/Projects/MyGame' --format json
```

5. View the returned PNG. Revise the document if needed, using IDs/revision from the response or a new inspect.
The TIFF is imported by Unity as the composite texture/Sprite; this temporary PNG is only for inspection.

An initially empty File layer automatically gets Original Aspect when assigned its first texture.
Inspection reports the effective decoded `sourceSize` used for File rendering and, separately,
`importedSourceSize` from Unity's imported texture. For supported original-file formats these may
differ because of importer downscaling. Inspect may populate the original-file decode cache.
Assigning a different HDR-format source sets both `colorRange` and `blendRange` to `HDR`.
Explicit ranges in the same settings object override these defaults. Reassigning the same source,
assigning a non-HDR source or subsequent refreshes do not reset the ranges.
An explicit transform patch is applied **after** this fit. Setting `scale:[1,1]` explicitly therefore
stretches a non-square source to the full canvas; omit scale to preserve the initial aspect fit.

## Batch contract

| Request field | Meaning |
|---|---|
| `apiVersion` | Required integer `1` |
| `assetPath` | Required project-relative `Assets/.../*.tiff` or `Assets/.../*.json` for documents |
| `create` | Default false. True creates a new document |
| `width`, `height` | Create only; integers, default 512 each, 1..16384 and at most 16,777,216 total pixels |
| `expectedRevision` | Required for existing documents; copy the latest persisted document revision from inspect/successful save. Omit entirely on create; null is rejected. Do not use a discarded save:false candidate's revision |
| `save` | Default true. False executes on a temporary copy and returns a snapshot, then discards it. It does not change an open window or retain a session. New documents require true unless dryRun |
| `dryRun` | Default false. Validate the entire batch on a detached model, without strokes, rendering, saving or consuming real name counters |
| `operations` | Required array, at most 256; use `[]` to save/rebake without additional edits |

These fields describe an editing request, not a `whimtex.document` file. Batch JSON saves use
the document's write mode and do not offer Drawing-omission permission: nonempty Drawing content
cannot be saved to JSON through this command. Use TIFF for pixels, or explicitly authorize omission
through the [document JSON API](#unified-json-documents) when exporting a settings-only copy.

Unknown/duplicate fields, wrong JSON types, invalid enum names, non-finite numbers and out-of-range
values fail validation. Omitted patch fields are preserved; JSON null is not a general reset
instruction. Only explicitly documented fields such as brush `tip:null` accept it.
Property and enum names are case-sensitive. Do not pass Unity instance IDs, YAML file IDs or display names.

Each `add` can define `as:"image"`. Later operations refer to it as `layer:"@image"`, `parent:"@image"`
or `target:"@image"`. Aliases are local to one batch, must be unique and cannot forward-reference.
Persistent layer IDs are returned per operation and in `document.layers`.

`document.layers` is flat, with `parent` and sibling `index`. Index zero is visually topmost.
`settings` contains editable values; hierarchy, target and transform have separate fields/operations.
`gradientKeys` contains separate color/alpha keys, colorMode, interpolation, huePath,
chromaCorrection, smoothness, wrapMode, technical colorSpace and midpoints. Default color mode
is Perceptual and interpolation is Balanced. Alpha follows the interpolation independently;
color models and chroma/hue controls affect RGB only. The retired mode/transition fields are
rejected. Gradient input uses RGBA arrays and string enum names. See the
[gradient contract](AI/README.md#standalone-gradient-json) for controls, defaults and limits.
### Color and gradient input

Gradient inputs accept either an ordered stop array or the object form documented in
[the gradient contract](AI/README.md); SDF, Noise and general-purpose gradients default to Perceptual. Explicit modes are preserved.

Color-picker preferences are not API input transforms: RGB/HSV entry mode, HDR input toggles,
Channels and Preview EV do not modify supplied RGBA or gradient values. API/clipboard writes do not
add or promote colors in document History. Color and gradient JSON still use numeric RGBA, not the
picker's HEX text syntax. Editing-operation and brush RGB limits remain -107..107 with alpha 0..1;
the standalone gradient clipboard's -65504..65504 range does not extend these API limits.
Unified document JSON is a separate storage contract with native field types and representability
checks, not these editing-operation ranges; see [JSON format](JSON_FORMAT.md).
HDR texture data is distinct from physical HDR monitor output; rendered PNG previews are SDR.

### Operations

```json
{"op":"add", "type":"drawing", "as":"ink", "parent":"@art", "index":0, "settings":{"name":"Ink"}}
{"op":"set", "layer":"@ink", "settings":{"opacity":0.6, "blend":"Multiply"}}
{"op":"transform", "layer":"@image", "transform":{"position":[24,-10], "rotation":15}}
{"op":"move", "layer":"@ink", "parent":"", "index":0}
{"op":"target", "layer":"@outline", "input":"Specific", "target":"@art"}
{"op":"target", "layer":"@outline", "input":"Previous"}
```

- `add`: types `file`, `drawing`, `group`, `color`, `gradient`, `noise`, `shader`, `shape`, `text`, `outline`, `sdf`, `normalMap`, `blur`, `sharpen`, `makeSeamless`, `shaderProcessor`.
  Optional `parent` defaults to root, `index` to 0. `settings` and `transform` are optional patches.
- `set`: requires `layer` and `settings`.
- `transform`: requires `layer` and `transform`.
- `move`: `index` is the insertion index **after removal** from the old container; omitted parent
  or `parent:""` moves to root. A group cannot move into itself or its descendants.
- `target`: effect layers (SDF/Outline/Normal Map/Blur/Sharpen/Make Seamless); default input Specific. Previous means the next sibling below the effect. AllBelow means the composited visible lower siblings in the same group, starting on transparency, including opacity/blending/FX; even Pass Through does not include the external backdrop. Previous and AllBelow forbid a target; Specific requires one. Example: `{"op":"target","layer":"@effect","input":"AllBelow"}`. The discovery field `effectInputModes` lists all three modes. Empty AllBelow is transparent; cycles are rejected.
  Specific targets can be groups, but cannot create a dependency cycle.
- `stroke`: Drawing only, detailed below.

### Layer settings

Current sharp-mode prototype: SDF and Outline accept `sharpMode: "Unrestricted" | "GuardedSharp"` (default `Unrestricted`). It applies to Sharp, MTSDF, CornerFit, Spring, Rbf and RbfDual; other algorithms retain but ignore it. Mode changes invalidate the retained field; distance remapping does not.

Current Sharp/MTSDF quality prototype: closed raster contours use line fits over 2/4/8/16-pixel supports, requiring agreement across scales in the two wall-normal directions with bounded longitudinal uncertainty. Supported vertices receive sharp extensions; uncertain joins receive small tangent quadratic caps (at most 0.15px displacement) and finite endpoint distances. This replaces their older fixed 8/16-pixel fitting and 2-pixel endpoint eligibility rules. Contour Merge happens before adaptive fitting. Both Corner Mode policies retain these geometric checks and MTSDF per-contour ownership. GuardedSharp additionally retains scalar finite-edge ownership and MTSDF selective interpolation correction: proximity-aware edge coloring, protected corner texels, analytic channel-crossing candidates verified against the same pseudo-distance field, RGB median and alpha unchanged at integer texels. Unrestricted omits that interpolation pass and scalar finite ownership. Miter Limit bevels scalar joins only in GuardedSharp; MTSDF ignores it. Max Distance is display remapping only. CornerFit, Spring, Rbf and RbfDual are unchanged. Very acute low-resolution corners may still round or differ with threshold; no universal reconstruction guarantee. This supersedes earlier Sharp/MTSDF fitting and correction descriptions.

| Applies to | Supported keys |
|---|---|
| All | `name` (string), `enabled` / `clippingMask` (boolean), `opacity` (0..1), `blend`, `colorRange` / `blendRange` (`Standard`, `HDR`), `channelMapping` (four channel names in output RGBA order) |
| Non-group | `filter` (`Source`, `Point`, `Bilinear`, `Trilinear`) |
| Group | `compositing` (`PassThrough`, `Isolated`); ranges are active only when isolated |
| File | `source`: imported Texture2D path in Assets/Packages, or an absolute external image path. External PNG/JPEG/TGA/BMP/EXR and supported TIFF are decoded without importing. TIFF: little-endian straight RGBA, 8-bit or 32-bit float, None/Deflate compression, no predictor. Inspection returns the same path, `sourceSize` and `sourceError` when unavailable; `importedSourceSize` applies only to project textures. Assignment validates the decoder before changing the link. External links remain absolute and are not embedded; another machine must have the source at that path or replace it. |
| Color | `color` (`[r,g,b,a]`, encoded RGB -107..107, alpha 0..1), `fillMode` (`Color`, `UV`, `Pattern`), `fillPattern` (partial settings below) |
| Drawing | `brush` (partial brush settings below) |
| Shader | `shader.graph` (complete graph replacement, including parameter declarations and values); see the [exact JSON example and discovery fields](ShaderLayer.md#api-and-document-json). |
| Gradient | `gradientType` (`Linear`, `Radial`, `Angular`, `Square`, `Freeform`, `Mesh`), `gradient` (ordinary palette), `freeform` (point settings below), `mesh` (complete control lattice below) |
| Outline | `color`, `metric` (`EuclideanAntialiased` default; ten modes below), `miterLimit` (float, 1..64, default 4; Sharp/CornerFit/Spring), `sourceChannel` (`Alpha` default, `Red`, `Green`, `Blue`, `Luminance`), `outlineWidth`, `outlineSoftness` (0..16384), `outlinePosition` (`Outside`, `Inside`, `Center`), `outlineOffset` (-16384..16384), `fillCenter` (bool), `fillColor` (`[r,g,b,a]`) |
| SDF | `metric` (`EuclideanAntialiased` default; ten modes below), `miterLimit` (float, 1..64, default 4; Sharp/CornerFit/Spring), `contourMergeDistance` (finite float, 0..2 inclusive, default 0/off; experimental, Sharp/MTSDF only, current render px), `sourceChannel` (`Alpha`, `Red`, `Green`, `Blue`, `Luminance`), `threshold` (integer 0..255), `distancePosition` (`Outside`, `Inside`, `Center`, `Signed`), `encoding` (`Gradient` default, `LinearData`, `MultiChannelData`), `inverted` (bool; Gradient/LinearData), `maxDistance` (0..16384; zero = automatic), `sourceOffset` ([x,y], each -16384..16384 px), `sourceEdges` (`Transparent`, `Clamp`, `Repeat`, `Mirror`), `contourOffset` (-16384..16384 px; positive expands; Gradient/LinearData only), `insideDistance`/`outsideDistance` (Signed Gradient/LinearData only, 0..16384; 0 inherits maxDistance/auto), `profile` (FX curve string syntax, at most 4096 characters; default `linear`; Gradient/LinearData only) |
| Normal Map | `normalMap`: partial settings object described below |
| Noise | `noise`: partial procedural settings object described below |
| Shape | `shape`: partial settings object described below |
| Vector | `vector`: partial data settings; arrays replace whole arrays. See [Vector storage and API](JSON_FORMAT.md#vector-layers). |
| Boolean Group | `booleanGroup`: `style`, `referenceSize`, `edgeMode`, `appearance` (`Group` / `Layers`, Combine only); always Isolated. Children use `booleanOperation`: Union, Subtract, Intersect, Xor or Combine. |
| Text | `text`: partial settings object described below |
| Blur | `blur`: partial settings object; `mode`: Gaussian (default), Linear or Circular; [Gaussian](#gaussian-blur-settings), [motion](#motion-blur-settings) |
| Sharpen | `sharpen`: algorithm (`Gaussian`/`Adaptive`), strength 0..4, radius 0..32 px, threshold/noiseReduction/haloSuppression 0..1, channelMode (`RGB`/`Luminance`), edges |
| Make Seamless | `makeSeamless`: `{ "mode": "OffsetBlend", "edgeWidth": 0.2, "offsetTransitionStart": -0.25 }`; [all four methods and parameters](#make-seamless-settings) |
| Gradient, SDF | `gradient`: 1..64 `{"time":0.0,"color":[1,1,1,1]}` stops in strictly increasing time order, time 0..1 |

SDF accepts `encoding` directly in settings: `Gradient` (default), `LinearData` or `MultiChannelData`, reported by `sdfEncodings` in capabilities. Its default palette matches Noise: black at 0, white at 1, with Perceptual color mode and Balanced interpolation. For Gradient and LinearData, Inverted reverses normalized distance before Profile; Gradient then samples the palette. LinearData outputs the remapped distance as raw linear RGB 0–1 with alpha 1, without gamma decoding or palette sampling. With `metric:"MTSDF"`, both scalar outputs use the median of the three RGB distances before this remap. In Signed mode low values are inside and high values outside. Switching output preserves the palette, profile and inversion setting; existing explicit gradient modes are retained. SDF has no `useGradient` field or Gamma output. Document export encoding still applies normally; choose linear output when exporting numeric maps.

`encoding:"MultiChannelData"` requires `metric:"MTSDF"` in the resulting layer state; API settings reject a mismatched pair. It writes raw linear RGBA: RGB holds corner-preserving signed perpendicular/pseudo distances, and alpha holds the true signed Euclidean distance to the reconstructed subpixel contour. Alpha is distance data, not coverage. Each channel encodes `saturate(0.5 + 0.5 * d / maxDistance)`, with negative distances inside, positive outside and zero at 0.5; `maxDistance:0` uses the automatic symmetric range. This output bypasses `distancePosition`, `contourOffset`, `insideDistance`, `outsideDistance`, `inverted`, `profile` and the palette. It is not a copy of one scalar field into four channels, and its alpha is not distance to the binary bitmap. Preserve RGBA and linear encoding when exporting these data.

For raw RGBA document composition or export, set the SDF layer's `blend:"Overwrite"`
(Rendering → Blend Mode → Overwrite). Normal composition treats alpha as coverage and can alter
the channel values. An ordinary FX or effect-layer input reading the standalone SDF layer receives
its rendered RGBA data; group and AllBelow inputs receive their composed image.

Changing the UI algorithm away from MTSDF while Multi Channel Data is selected switches output to
Linear Data in the same Undo operation. API callers must supply a valid resulting metric/encoding pair;
they do not receive that UI fallback.

SDF operations write `settings.gradient`, not `settings.sdf`. Unified document/clipboard JSON
instead stores the native palette in `behaviour.gradient` on `SDFLayerBehaviour`.
Inspection reports the palette separately as `gradientKeys`; write that body's value using `gradient`.
Both stop arrays and the shared gradient object are accepted, including separate alpha keys, HDR colors,
`colorMode`, `interpolation`, `huePath`, `chromaCorrection`, `smoothness`, `wrapMode` and `colorSpace`. Supplying `gradient` replaces the whole palette but
does not select an output mode; set `encoding:"Gradient"` explicitly to enable it from LinearData.

SDF and Outline use the same twelve `DistanceMetric` values and field semantics:

| `metric` | Numeric ID | Distance Algorithm / field |
| --- | --- | --- |
| `EuclideanAntialiased` (default) | 4 | Round (Antialiased), or Round AA. Preserves the existing Euclidean antialiased implementation and rounded offsets. |
| `Sharp` | 5 | Contour-offset field with miter joins, limited by `miterLimit`. Not a Euclidean SDF. |
| `Bevel` | 6 | Contour-offset field with bevel joins. Not a Euclidean SDF. |
| `Diamond` | 7 | L1 distance, `abs(dx) + abs(dy)`; diamond-shaped offsets depend on the calculation axes. |
| `Square` | 8 | L∞ distance, `max(abs(dx), abs(dy))`; square-shaped offsets depend on the calculation axes. |
| `MTSDF` | 9 | Corner-preserving RGB perpendicular/pseudo distances plus true signed Euclidean distance in alpha to the subpixel contour. |
| `AAEDT` | 10 | **AAEDT + 8SED** (experimental): anti-aliased Euclidean raster-boundary estimate using sweep-and-update with 8-neighbor vector propagation; wide external offsets remain round. |
| `Contour` | 11 | **Contour AA** (experimental): Euclidean distance to finite straight segments from raw marching-squares threshold crossings with interpolated subpixel endpoints; expanded corners round. |
| `CornerFit` | 12 | **Corner Fit AA** (experimental): raw-contour initialization, multi-pixel side fitting and local AA verification of proposed corners; bounded sharp joins only at verified corners. |
| `Spring` | 13 | **Spring Simulation** (experimental): accepts geometrically safe spring relaxation without a global source-AA coverage gate, then uses the same local AA-tested corner proposals and scalar output path as CornerFit. |
| `Rbf` | 14 | **RBF Your SDF** (experimental): fits overlapping cubic RBF patches, extracts a half-pixel zero contour and uses the shared sharp-distance stage. |
| `RbfDual` | 15 | **RBF Dual Contouring** (experimental): shares the Rbf fit, extracts half-pixel cell connectivity using direct continuous RBF root searches, analytic blended normals and a bounded 2D QEF, then uses the shared sharp-distance stage. |

`RbfDual` is a separate raster-only scalar mode; existing `Rbf` is unchanged. It uses the same
source, threshold, `sharpMode`, `miterLimit` and scalar output controls. `contourMergeDistance`
is retained but ignored. Max Distance remaps the retained field; the metric participates in the
preparation cache key. `Gradient`/`LinearData` are supported; `MultiChannelData` remains MTSDF-only.
This is an experimental 2D dual-contouring adaptation, not a faithful implementation of
Occupancy-Based Dual Contouring (2024); no upstream code was copied. Sharp-corner recovery
and a quality advantage over `Rbf` are not guaranteed.

All twelve modes are antialiased. Round AA retains the original crossing-seed Euclidean distance
transform (EDT): it interpolates threshold crossings between horizontal/vertical neighboring samples
and propagates those seeds. It does not measure distance to continuous contour segments.

`CornerFit` and `Spring` are separate experimental raster-only modes. CornerFit starts with raw
Contour geometry and fits corner sides over multiple pixels. Candidate corners are locally
re-rasterized with AA and compared with source coverage; intersecting or unconfirmed extensions
are rejected, and ambiguous cases retain the raw contour. Only AA-verified corners receive bounded
sharp joins in continuous, local endpoint domains, rather than unrestricted line extensions. `miterLimit` bevels
excessive validated joins; it cannot make an unverified corner eligible. This is an experimental
hybrid, not a known algorithm with guaranteed sharp recovery.

Spring adds automatic spring relaxation of the raw contour before those same corner proposals.
Geometrically safe relaxation is accepted without the global match-to-source-AA coverage rejection;
`RasterCornerDistanceContour.AcceptRelaxation` and its call in
`ArtisticDistanceField.ReconstructRasterContours` have been removed. Spring forces, vertex travel limits and topology
protection remain in place. The subsequent per-corner AA fit still compares candidate coverage with
the source and is shared with CornerFit; removing the global gate does not bypass those local checks.
If corner fitting lacks evidence or exhausts its budget, it keeps the geometry entering that stage,
which can already be relaxed in Spring. The shape can shift locally relative to the source;
neither better reconstruction nor faster rendering is guaranteed.
It adapts ideas from [Matusovic, Parakkat and Eisemann, Interactive Depixelization of Pixel Art through Spring Simulation (2023)](https://doi.org/10.1111/cgf.14743).
The paper concerns interactive depixelization, not SDF generation. This mode is neither the full
interactive paper tool nor a replica of its C++ implementation.

Their renderer now separates contour preparation from distance evaluation and presentation.
SDF/Outline retain one current prepared contour and one current scalar field. An unchanged
input reuses preparation; only Miter Limit changes rerun distance evaluation. Max Distance,
Inside/Outside Distance, Contour Offset, profile/gradient and Outline shading reuse the field.
The exact input bytes, expanded dimensions, threshold, source channel and preparation mode
determine reuse. Changing these replaces the current entry. Derived native data is not saved
or kept in Undo. All retained entries share a 64 MiB budget with eviction; unused effect branches,
resource release, assembly reload and editor shutdown release their retained data. Requests
larger than the budget still render but do not retain preparation. This is local renderer reuse;
the future shared SDF request API for FX remains separate.

Round AA, Sharp AA, Bevel AA, Diamond AA, Square AA and MTSDF now use the same
per-renderer retention budget for their completed unnormalized fields. Max Distance,
Inside/Outside Distance, Contour Offset, profile/gradient and Outline width/shading
reuse that field. Scalar modes retain float distances; MTSDF retains all four float
channels, including true-distance alpha, so switching between Gradient, LinearData
and MultiChannelData does not require another field calculation. Exact expanded
input bytes, dimensions, threshold, source channel, algorithm and applicable
sharpMode/miterLimit/contourMergeDistance settings identify the current entry;
changing them replaces it. Inactive parameters are excluded. Each renderer retains
only its current result, not a history of edited values. Source readback, comparison,
sampling, texture upload and presentation still run; this avoids field construction,
not all rendering work. Fields exceeding the shared 64 MiB budget render uncached.
AAEDT and Contour are unchanged by this extension. No serialized settings were added.

CornerFit/Spring preparation now computes constant endpoint directions, normals, bisectors,
cosines and miter ratios once in a Burst job. Only eligible confirmed endpoints receive a
parameter record; the distance job uses compact indices and keeps the same cone/locality
checks. Parameter buffers are included in the retained-memory budget and disposal. That parameter
precomputation did not change the distance definition or contour fitting. Its frozen comparisons
and timings predate the separate Spring coverage-gate removal and do not establish current
bit-equivalence or performance for the changed Spring behavior; see the [historical evidence](https://github.com/DCFApixels/WhimTex/blob/dev/Context~/Backlog/SDF_OUTLINE_DESIGN.md#prepared-corner-parameters).

Both modes support the source threshold through coverage remapping of the selected channel;
soft masks and values that do not represent area coverage give only an approximate fit. SDF uses
its `threshold`; Outline uses fixed byte threshold 128. Source edges, offsets and the scalar
downstream controls use the existing wrapper. Gradient/LinearData are supported; MultiChannelData
remains exclusive to MTSDF. SDF `contourMergeDistance` remains validated but is ignored.
Neither mode promises universal sharp recovery. Round AA stays the default at ID 4;
the previous eight modes retain their behavior.

`Contour` extracts the current raster threshold boundary with marching squares on alpha or the
selected source channel. Threshold crossings are linearly interpolated to subpixel endpoints;
distance is the nearest Euclidean distance to the resulting finite straight segments. It applies
no contour fitting, smoothing, simplification, corner recovery, contour merge/union, miter extension
or MSDF channel construction. Expanded corners round. The field measures the current raster
threshold boundary; it does not restore detail lost during rasterization.

Contour AA is a separate algorithm, not an AAEDT modification or a sharp-miter mode. It uses the
same source-channel, threshold, `SourceEdges`, offset and rendering wrapper as the existing modes:
SDF exposes `threshold` and `sourceEdges`; Outline uses fixed byte threshold 128.
`miterLimit` and SDF `contourMergeDistance` remain validated but have no effect in Contour.
SDF supports Gradient/LinearData; MultiChannelData still requires MTSDF. Contour AA is provided
for experimental quality comparisons, with no guaranteed improvement across all inputs.
Round AA remains the default at ID 4; AAEDT remains ID 10 and all existing modes are unchanged.

`AAEDT` uses a portable Burst job based on Stefan Gustavson's `edtaa3` reference
([pinned source and MIT notice](https://github.com/DCFApixels/WhimTex/blob/main/ThirdPartyNotices.md#edtaa3)). The selected source channel is
treated as coverage: input alpha should measure the area of a crisp edge within each pixel
(box-filter sampling). Blur, feather and arbitrary opacity violate this assumption and do not
yield exact geometry. AAEDT estimates the raster boundary; wide external contours remain round.
It does not generate sharp/miter extensions and bypasses segment reconstruction, contour fitting,
simplification and union. `miterLimit` and SDF `contourMergeDistance` retain their validated values
but do not affect AAEDT. The default remains `EuclideanAntialiased` (Round AA, ID 4).

The single-seed sweep is approximate and may depend on scan orientation. Its local update
tolerance is 0.0001 pixel instead of the reference's 0.001; this is not a global error bound.

For AAEDT, normalize the selected source value to coverage `a` in 0..1 and the byte threshold to
`t = threshold / 255`. For `0 < t < 1`, the input coverage is remapped piecewise:

```text
c(a) = 0.5 * a / t                    if a <= t
c(a) = 0.5 + 0.5 * (a - t) / (1 - t)  if a > t
```

The remap is monotonic, maps `t` to 0.5 and preserves the 0/1 endpoints. Extreme byte thresholds
0 and 255 use binary classification: coverage is 1 only when the selected byte-range source value
exceeds the threshold, otherwise 0. Thus threshold 0 keeps nonzero samples and 255 leaves no
covered samples. SDF exposes `threshold`; Outline uses
fixed byte threshold 128 on its selected channel. AAEDT supports Gradient and LinearData output;
MultiChannelData remains MTSDF-only, and API patches reject an incompatible resulting pair.
This experimental mode is available for comparison; it carries no claim of verified accuracy
or performance superiority over Round AA.

Sharp, Bevel, Diamond, Square and MTSDF first fit straight flanks to multiple raw isocontour samples using
orthogonal least squares. Uniform arc-length samples are at most 0.5 pixels apart; each flank
has 8 pixels of support. Fits require maximum perpendicular error <= 0.35 pixels and mean
squared error <= 0.025 px². A recovered turn is at least 45 degrees, its cap half-arc at most
4 pixels and sampled flank deviation at most 0.6 pixels. Acute corners use
independently straight 16-pixel flanks when they agree with the base 8-pixel fits.
Their tiny AA cap alone has an engineering allowance of `sqrt(0.5)` pixels;
tip displacement stays within the measured base support, and only when
`sqrt(0.5) px / cosHalfTurn` fits in that support. Otherwise the original
`min(2 px, 0.6 px / cosHalfTurn)` bound applies. This is not an exact raster inverse.
Extended supported fits take priority over short cap-biased fits. Uncertain corners are preserved.
Unsampled details outside fitted regions remain intact. All five segment modes then use fixed
0.25-pixel contour simplification; paths with perimeter below 4 pixels bypass simplification.
Closed filled contours are normalized by NonZero union at 1e-6-pixel precision before joins and
edge colors are generated. Holes and genuinely separate islands remain; regions overlapping after
reconstruction share one filled boundary. Open and zero-area threshold contours bypass this union.
Enclosed holes are protected before union: a conflicting corner is rejected locally,
and simplification falls back to the protected contour. A negative ring outside all
filled outlines is a separate NonZero component, not an enclosed hole.
For a conflicting supported concave corner, a minimal outward wall shift can retain
the hole within the same 0.6-pixel envelope; finite-tip and hole checks still apply.
After a component merge, one bounded convex-cap enclosure pass checks both arc samples
and exact unsampled union vertices before the 0.05-pixel cleanup; it does not run another
0.25-pixel reduction. Closed components with perimeter below 4 pixels retain finite
geometry but do not generate distant Sharp/Bevel miter or MTSDF pseudo extensions.
Sharp/MTSDF endpoint extensions additionally require at least 2 pixels of straight
support on each flank after union, with successive directions agreeing within dot 0.995.
Angular amplification is capped at 4 (an ideal tip angle of about 29 degrees); unsupported
or above-cap endpoints retain finite segment distance, including short edges in a large
merged contour. Sharp resolves the nearest finite owner within each contour before
evaluating its angular distance, then combines the per-contour fields. This prevents a
remote supporting line from winning and avoids premature switching between separate islands.
Post-union cleanup uses a 0.05-pixel budget and rejects replacement chords crossing nonincident
original or replacement boundaries or changing contour containment/contact states. MTSDF resolves each contour's RGB median before combining
contours; the nearest valid signed pseudo-distance wins, while alpha remains the nearest true
Euclidean distance. RGB channels from unrelated contours are not mixed before median evaluation.
For cells certified away from the contour by all four true-distance alpha values (same sign,
absolute value > 1 pixel), a common-channel lower bound first certifies safe interpolation.
Suspect cells receive finite-contour distance checks at their corners, edge midpoints and centre.
Sign errors or angular amplification beyond 4 (with a 0.05-pixel numerical allowance) collapse
their RGB to true distance. Color-only incompatibility collapses RGB to its per-texel median.
True-distance alpha is unchanged; scalar MTSDF uses the corrected RGB median too. Texels with
absolute alpha distance <= 1 pixel are unchanged; neighbouring cells can interpolate those texels
with corrected far texels. This is a conservative local correction, not a full upstream error-correction implementation.
These steps can alter subpixel detail and do not reproduce the original matte exactly. MTSDF alpha
measures true Euclidean distance to the reconstructed contour, not an exact original continuous curve.
`miterLimit` is the same finite float field on SDF and Outline: API settings/inspect range 1..64,
default 4, dimensionless, and used by Sharp, CornerFit and Spring to limit miter length relative
to the contour offset and bevel excessive joins. CornerFit/Spring apply it only to AA-verified
corners with bounded local endpoint influence; it does not validate uncertain corners. In Sharp, values
above 4 remain stored but cannot override the separate raster safety cap. Very acute or
insufficiently supported Sharp corners can round even with a high requested Miter Limit.

SDF alone exposes experimental `contourMergeDistance` in settings/inspect and native document JSON.
It is a finite float in 0..2 inclusive, default 0 (off), measured in current raster/render pixels,
independent of the distance remap width and not divided by render scale. It applies only to Sharp
and MTSDF, including MultiChannelData. Other modes retain and ignore the value but still validate
its range and finiteness. The UI shows **Contour Merge (px)** only for Sharp/MTSDF. One pass merges
disjoint pairs of consecutive vertices within one contour, shortest eligible pairs first, to their
midpoints. Pair distance is bounded by the requested value and each original vertex is used at most
once; this does not build multi-vertex clusters or merge different loops. A topology conflict rolls
back the whole batch. Existing safety protections remain active. This may shift short details and
does not guarantee restored sharpness.

`threshold` selects the SDF contour; Outline uses the fixed midpoint threshold (128) on its selected
`sourceChannel`. A uniform source with no threshold crossing has no border.
The former enum values 0–3 and their names are removed; there are no aliases or numeric remappings.
Capabilities/inspect report the ten current names; Round AA retains ID 4, AAEDT ID 10 and Contour ID 11;
CornerFit is ID 12 and Spring is ID 13.
Outline filters both band edges, including at zero softness, and supports fractional widths. `outlineOffset`
translates the band in canvas pixels (negative inward, positive outward), without changing its width.
`fillCenter` defaults to false. When enabled, the region inside the band's inner edge uses `fillColor`
(default white; same encoded RGB/alpha contract as `color`). Fill and border share complementary coverage,
so opaque colors do not create a translucent seam. Zero width removes only the border, not the enabled fill.
The source's interior holes remain holes. This is an ordinary effect layer: place it below an explicitly targeted
source for a backing silhouette; above the source it can cover the original image.
PSD exports filled/offset/antialiased outlines as pixels rather than a native stroke style.
For SDF on a group, Alpha requests coverage only; Red/Green/Blue/Luminance request the group's RGBA result.
Changing SDF Source Channel therefore also changes the group's source-cache requirement.

Discover blend modes, ranges, group compositing and distance metrics with `whimtex_describe`.
Color-component blend names are `Hue`, `Saturation`, `Color`, and `Luminosity`, for layers,
isolated groups, and brushes. Standard uses non-separable luminosity/saturation blending in
sRGB blend space (luminosity weights 0.30/0.59/0.11, saturation max−min, luminosity-preserving
gamut clipping), not HSV channel replacement. HDR evaluates the same component substitutions
in linear light without gamut clipping. Alpha/opacity compositing is unchanged.
The UI groups choices independently of their stable enum values; JSON names do not change.
Ordinary Groups default to PassThrough; set `compositing:"Isolated"` to apply their own blend mode and ranges. Boolean Groups are always Isolated.
Group opacity applies to the complete result, not separately to every child. Groups support transforms and FX. Child transforms are parent-local; canvas matrices compose from parent to child. The group's frame is its own unit rectangle rather than the bounds of its children. The move operation preserves canvas placement when changing parents.
`channelMapping` accepts `R`, `G`, `B`, `A`, `1-R`, `1-G`, `1-B`, `1-A`, `0`, `1`, `R * A`, `G * A`, `B * A`,
`Luminance`, `Luminance * A`, `1 - Luminance` as strings. Luminance is `0.2126 R + 0.7152 G + 0.0722 B` in linear space;
the product uses the original alpha. `1 - Luminance` is one minus that same RGB luminance, independently of alpha.
`["1","1","1","Luminance"]` converts brightness to alpha with white RGB; `["1","1","1","1 - Luminance"]` reverses that brightness mask.
Labels include their exact spaces, matching `Describe`. All mappings read the original input RGBA:
`["R * A","G * A","B * A","1"]` multiplies RGB by the input alpha and sets output alpha to 1.
Selecting products does not change the document's blending convention or implicitly change output alpha.
For example, `"channelMapping":["B","G","R","A"]` exchanges red and blue;
`["A","A","A","1"]` displays alpha as opaque grayscale. The default is `["R","G","B","A"]`.
It runs after FX in linear working space and before Color Range and layer blending. Output alpha
remains bounded to 0..1. Source pixels and brush settings are unchanged.
A nonidentity group channelMapping forces isolated rendering. A saved Pass Through group uses Normal
blending while channelMapped, then resumes Pass Through when restored to identity, without FX and not participating in clipping. Explicitly isolated
groups retain their chosen blend mode. `Describe` lists `channelMappingSources`; `Inspect` includes each
layer's channelMapping, including groups.

Groups support FX on their combined children, before ChannelMapping and outer opacity/blending. A group with FX is automatically isolated; a saved Pass Through mode uses Normal blending until all FX are removed (unless ChannelMapping or clipping still requires isolation).

`clippingMask` defaults to `false`. Set it to `true` on a non-Processor layer or group to clip it to the
first non-clipping sibling below; consecutive clipped siblings share that base. The relationship
is positional, never crosses a parent group, and updates after moves. A hidden, transparent or
missing base hides the chain. The base's alpha is preserved and its opacity is applied once.
Participating groups are isolated temporarily (configured PassThrough uses Normal).
`Inspect` reports `settings.clippingMask`, resolved `clippingBaseId` (null without a base), and
`isolatedByClipping` on groups. Toggling clipping does not edit source pixels or drawing strokes.

For an existing layer, use `{"op":"set","layer":"<id>","settings":{"clippingMask":true}}`
in a batch with the current `expectedRevision`. Place its base first when constructing a new
document, then add clipping layers above it. Clipped Overwrite replaces source-covered color
without erasing base alpha; outside clipping it retains full RGBA overwrite behavior.

Setting Drawing `colorRange:"HDR"` promotes storage. Standard does not downgrade it. The explicit operation
`{"op":"compact","layer":"@drawing"}` clamps/quantizes to 8-bit and switches to Standard
(undoable in Assistant, not a filesystem Undo for Batch/Headless).
Only use compact when the user asks to discard HDR precision. Inspect reports `storageFormat`.
Colors retain the encoded RGB convention; rendering and EXR/Texture2D output are linear HDR.
The render command writes a clamped PNG copy. See [HDR behavior](HDR.md).
Unspecified settings and FX are preserved. The shared operations support Shader FX editing,
layer deletion, duplication, merging and conversion to Drawing; see [shared editing operations](#shared-editing-operations).
Changing gradient geometry is not exposed by these batch operations.
Use `enabled:false` when hiding a layer is preferable to deleting it.

### Custom gradient settings

Every shared gradient input accepts `interpolation:"Custom"`, `customColorMode` and
`customKeys` alongside retained automatic `colors`/`alphas`/`colorMode`. Inspect snapshots
use the same body and preserve inactive manual data. Replacing a palette replaces both
datasets; retain both when editing a snapshot. See the [complete JSON contract](AI/README.md#custom-channel-curves)
for channel key fields, links, finite-value limits and validation. There are no new batch
commands for individual keys; supply the complete gradient body through the existing field.

The public `WhimTexGradient` C# API adds:

- `CustomKeys` (a copy), `HasCustomCurves`, `CustomColorMode` and `CustomApproximationError`.
- `AutomaticColorMode`, `AutomaticColorKeys`, `AutomaticAlphaKeys` for retained automatic data.
- `SetCustomData(keys, mode)` validates and clones data. Null/empty clears manual data only
  when Custom is inactive. `ColorKeys`/`AlphaKeys` are projections while Custom is active;
  do not feed these projections back to `SetKeys`, which edits automatic data.
- `Interpolation = Custom` initially fits the current gradient and subsequently restores
  saved manual curves. `ConvertCustomColorMode(mode)` explicitly refits active Custom;
  changing `ColorMode` to a different mode directly while Custom is active is rejected.
- `AddCustomKey(channel,time)` samples the current channel curve to create one key, or
  returns the existing key within the minimum time spacing. `AddCustomColorKeys(time)`
  atomically adds/reuses three independent color-channel keys at the same time; it returns
  the channel-0 key ID. It creates no persistent association between them.
  These edit operations clamp the requested time to 0..1.
- `MoveCustomKey(id,time)` moves only that key, stopping at 0..1 and the nearest unselected
  same-channel neighbours. `UpdateCustomKey(key)` retains identity/channel, edits its
  values/tangents/mode and uses this independent movement for time changes.
- `MoveCustomKeys(int[] ids,float deltaTime)` atomically moves exactly the supplied IDs by
  one common delta. It clamps that delta so every selected key stays in 0..1 and at least
  0.000001 from its nearest unselected same-channel neighbours, preserving order and
  relative offsets subject to float storage rounding. It can shorten the delta further
  if rounding would violate spacing. Selected neighbours move together; nearby keys are
  never implicitly added. For a marker drag, freeze `GetCustomMarkerGroup(id)` at pointer
  down and reuse those IDs for the entire gesture; do not recompute membership after each move.
- `RemoveCustomKey(id)` removes only that key (an unknown ID is a no-op).
  `RemoveCustomKeys(int[] ids)` removes exactly the supplied set atomically; an operation
  that would leave any channel empty is rejected without changing the gradient.
  Both bulk methods reject null arrays or unknown IDs, deduplicate IDs, and accept an empty
  array as a no-op. Moves reject nonfinite times/deltas. All normal Custom validation,
  including the 64 distinct color-position limit, remains in force; failures do not commit
  partial changes. Editing a curve never implicitly moves or removes its marker neighbours.
- `EvaluateCustomChannel(channel,time,final=false)` samples the raw working-space channel.
  With `final:true`, it samples the bounded final color converted back into that channel's
  working space (or bounded alpha). Both clamp time to 0..1 without Repeat/Mirror wrapping.
  `CustomKeyTangent(id,incoming)` returns the effective prepared slope, including automatic
  tangents, or zero for an unknown ID.
- `TryGetCustomKey(id,out key)` and `CustomChannelName(channel)` expose key identity and names.
- `GetCustomMarkerGroup(int id)` returns a new array of all IDs in the key's automatic time
  cluster, including alpha and any same-channel keys; an unknown ID returns an empty array.
  All keys are sorted by time, then ID. Each cluster starts at the earliest remaining key
  and includes later keys at most `CustomMarkerTimeTolerance` (0.00001) from that anchor.
  Clustering is not transitive and does not change stored times: keys at 0, 0.000009 and
  0.000018 form two clusters, not one.
- `GetCustomMarkers(bool? alpha=false)` uses those same all-channel clusters, then selects
  the first matching key in time/ID order within each cluster: color channels for `false`,
  alpha for `true`, all channels for `null`. A cluster without a matching channel is omitted.
  Filtering never rebuilds clusters. Color and alpha representatives can therefore differ
  while `GetCustomMarkerGroup` returns the same membership for either. Representatives keep
  their own time/value; there is no averaged position or serialized group identity.
  Ordering, membership and representatives are cached internally until gradient data
  invalidation. Queries return copies, so callers cannot mutate the cache. Cloning and
  deserialization reset it; repeated per-marker queries do not re-sort the keys.

`WhimTexGradientCurveKey.link`, `LinkCustomKeys` and `UnlinkCustomKeys` are removed. Explicit
obsolete `link` fields are rejected by strict readers, including zero; no migration is provided.
Conversion uses bounded adaptive fitting and may change key count and tangents.
Its observed error is not a guaranteed maximum. The owning editor/API operation remains
responsible for its normal Undo/commit flow; the gradient model itself does not create Undo records.

### Freeform gradient settings

Gradient layers accept `gradientType:"Freeform"` with `freeform.points`. For example, use
the following `settings` in an `add` or `set` operation:

```json
{
  "gradientType": "Freeform",
  "freeform": {
    "colorMode": "Perceptual",
    "points": [
      { "position": [0.25, 0.3], "color": [1.5, 0.2, 0.1, 1], "influence": 0.35 },
      { "position": [0.75, 0.7], "color": [0.1, 0.3, 1.2, 0.4], "influence": 0.5 }
    ]
  }
}
```

There must be 1..64 points. Each point requires `position` (two finite local UV values,
bottom-left origin, each -10000..10000) and `color` (straight encoded RGB, each -107..107,
with alpha 0..1). Optional `influence` defaults to 0.35 and accepts 0.01..4. Larger influence
increases that point's relative weight; it does not impose a hard radius cutoff. Colors
are decoded and blended in linear space, retaining signed HDR and independent alpha.
Use the layer's HDR rendering range when the output must retain values outside 0..1.

Providing `freeform` replaces its complete settings; omitting it preserves the stored settings.
`colorMode` accepts `Linear`, `Gamma`, `Perceptual` (OKLab, default when omitted) or `Hue`.
Hue uses a chroma-weighted circular mean, favoring the minor arc and smoothly falling back
to Perceptual under directional conflict. It has no `huePath` option and is not an exact
angular-speed match for the one-dimensional palette's Shortest interpolation.
It selects RGB mixing; stored point colors and independently weighted alpha are unchanged.
The same field is supported independently by figure `freeform` and `strokeFreeform` settings.
Switching `gradientType` preserves both Freeform points and the ordinary `gradient` palette.
The canvas Gradient creation tool also offers Freeform: a drag creates three equilateral-triangle points sampled from the current palette at 0, 0.5 and 1, preserving encoded HDR RGB and alpha, then activates point editing. This UI initialization does not change the API default point set; API callers specify `freeform.points` explicitly when matching a particular palette.
Inspect returns `gradientType`, `freeform.colorMode` and `freeform.points` alongside `gradientKeys`, including
inactive settings. Live and file-based shared editing operations use this same contract.
For Shape, use `settings.shape` with `fillMode:"Freeform"` and `freeform`, or
`strokeMode:"Freeform"` and `strokeFreeform`. Each field replaces its complete settings;
omitted fields stay intact. Inspect includes both point fields under `settings.shape`.
Vector `settings.vector.shapes` and Boolean Group `settings.booleanGroup.style` use the
same paint fields, following their existing whole-array/whole-style replacement rules.
For example: `{"shape":{"fillMode":"Freeform","freeform":{"points":[{"position":[0.5,0.5],"color":[2,0.1,0.2,0.8],"influence":0.35}]}}}`.
Both inactive palettes and point sets are retained. Text does not accept Freeform.
SVG is unchanged. See
[document JSON storage](JSON_FORMAT.md#freeform-gradients) for serialization and interpolation.

### Mesh gradient settings

Only Gradient layers accept `settings.gradientType:"Mesh"` and `settings.mesh`.
`mesh` replaces the entire lattice; it must include `columns`, `rows`, `colorMode`,
`positions`, `colors` and `nodeModes`. Omission retains existing Mesh data. Changing `gradientType`
does not clear the ordinary palette, Freeform points or Mesh lattice. A newly created
Gradient layer has an initial mesh, so selecting Mesh without supplying `mesh` uses it.
Inspect returns `mesh` even while another type is active. The same settings apply to
live and file-based shared editing operations.

- `columns` and `rows` count cells, each 1–16. Both arrays contain exactly
  `(3*columns+1)*(3*rows+1)` entries in row-major order; a single cell has 16 controls.
- `positions` entries are finite `[x,y]` local Bézier controls, including outside 0–1. Nodes are at lattice indices
  `(3*c,3*r)`; all other controls remain part of the saved model.
- `colorMode` is `Linear`, `Gamma`, `Perceptual` or `Hue`.
- Hue requires `hueChroma`: a finite scalar coefficient for every color control, with
  the same lattice length as `colors`. Retain this independent lattice on replacement;
  do not recompute it from the OKLab coefficients after subdivision. Other modes use `null`.
  Hue uses the same circular-mean/conflict rule as Freeform, without `huePath`.
- `nodeModes` contains `(columns+1)*(rows+1)` row-major `Smooth`/`Corner` entries.
  This is a node array, not a control array. Explicit `null` means all Smooth;
  the field itself remains required. Newly created or inserted nodes use Smooth.
- Optional `patchPriorities` is `null` or a row-major array of `columns*rows`
  `[constant,uCoefficient,vCoefficient]` triples. Each component is finite and
  nonnegative; each triple sums to at most 1. Null/omission uses equal parameter
  intervals. Retained u slopes in the first row and v slopes in the first column
  describe subdivision widths/heights; effective priorities are reconstructed
  continuously, without independent cell offsets. Clone/API reads canonicalize
  the planes. Retain them when replacing an inspected mesh instead of rebuilding
  equal intervals; see the [mesh contract](JSON_FORMAT.md).
  Empty `nodeModes` or `patchPriorities` arrays are not valid input; use `null` for
  implicit defaults. Inspect also reports internal empty/default arrays as `null`,
  and API reads normalize implicit defaults to concrete arrays.
- `colors` entries contain working coefficients, **not encoded display colors**:
  linear RGB for Linear, encoded RGB for Gamma, OKLab for Perceptual/Hue, followed by
  straight alpha. Signed HDR and alpha-control overshoot are allowed. Do not apply
  the ordinary color-field alpha clamp to these arrays; evaluated alpha is clamped.

For a color or handle edit, read `mesh`, change the needed coefficients and submit
the complete payload. Changing `colorMode` in a raw payload also requires converting
its color coefficients; merely changing the name reinterprets them. In C#, use
`SetColorMode` for conversion; appearance can change across nonlinear models. Use
`NodeColor`/`SetNodeColor` when editing encoded node colors. `SetNodeColor` carries
the adjacent controls to preserve local color derivatives; `EvaluateColor` returns
straight linear output.
The model's `SplitColumn`/`SplitRow` subdivide both lattices without changing the
continuous result; an API client performing subdivision must likewise preserve all
coefficients and carry existing node modes; inserted nodes use Smooth.

Malformed arrays, missing `nodeModes`, nonfinite values and unsupported color/node
modes fail validation. Folded and overlapping geometry is allowed. Overlaps use
normalized color/opacity blending followed by whole-overlap reconstruction from
surrounding colors and boundary variation. This redistributes colors inside the overlap
without modifying the stored geometry or color coefficients.
A narrow, smoothly fading band around the overlap allows the transition to match its
surroundings. Alpha does not accumulate; pixels outside the overlap and this band retain
their color and opacity.
`mesh` on non-Gradient layers is rejected. The field schema is
[`$defs.mesh`](AI/agent-fields.schema.json); the [storage contract](JSON_FORMAT.md#mesh-gradients)
describes TIFF, JSON, clipboard and dormant data.

The shared `mesh` operation edits an existing grid without replacing all coefficients:

```json
{"op":"mesh","layer":"GRADIENT-ID","action":"nodeMode","node":0,"mode":"Corner"}
{"op":"mesh","layer":"GRADIENT-ID","action":"moveEdge","node":0,"direction":1,"delta":[0,0.05]}
```

`node` is zero-based and row-major: `row*(columns+1)+column`. `nodeMode` requires
`mode:"Smooth"` or `"Corner"`. Switching to Smooth aligns each opposite pair to an
average direction, keeping both handle lengths; subsequent handle edits align the
opposite handle while keeping its length. Corner keeps handles independent. Colors
do not change. In a raw payload, `nodeModes` stores modes as supplied and does not
realign controls; use this action when alignment is intended.

`moveEdge` requires `direction` (0 left, 1 right, 2 down, 3 up) and a finite local
`delta:[x,y]`. The edge must exist. It moves the two edge handles and adjoining tensor
controls, preserving endpoint positions; Smooth endpoints may also realign their
opposite handles. Both actions retain `gradientType`, so dormant Mesh data can also
be edited. They use ordinary shared-operation dry-run, revision and Undo handling;
invalid input fails before replacing the mesh. See [`$defs.meshOperation`](AI/agent-fields.schema.json).

### Resize a document

`{"op":"resize","width":256,"height":128,"preserveLayout":true}` is a document-level
operation shared by Batch, Headless Live and Assistant. Both integer dimensions are required
(1..16384; at most 16,777,216 pixels). It also works on an empty document; the operation result
has `layerId:null` and `name:null`, with the new dimensions in the document snapshot.

`preserveLayout` defaults to true: normalized layer placement is preserved throughout the group
hierarchy. Proportional resizing scales pixel-valued TRS positions; non-proportional resizing
stores equivalent normalized projective transforms to retain rotated/sheared placement.
Existing projective transforms remain unchanged. Set false to change only the canvas dimensions.
Layer IDs, source Drawing/File pixels, FX links, gradients and all other settings are retained;
this does not flatten or resample the editable source layers.

Pixel-valued effects are **not** scaled automatically: explicitly adjust blur radii, outline/SDF
distances, shape feather/strokes, pattern cell sizes and custom FX pixel parameters with `set`
or `fx` operations in the same batch. UV/normalized values should not be scaled. Compare rendered
previews: procedural sampling and antialiasing can differ at a smaller resolution.
Existing revision, dry-run, lock, save and Undo rules apply. Headless status reports the current
candidate size; a nonempty replay starts from its original baseline as usual.
Document revisions ignore rebuilt Shader FX compilation caches, but still include editable code,
parameters and activation state. Opening a detached TIFF restores its existing applied FX before
copying it; dry-run does not compile newly submitted shader code.

`shaderProcessor` processes the already-composited lower stack, with HDR ranges by default.
Normal + Opacity interpolates before/after without accumulating alpha twice. Pass Through includes
the external backdrop; isolated groups limit its scope. Processor is a clipping-chain boundary.
The batch API can create/reorder it, edit its settings and transform, and author its FX through `op:"fx"`.
The [live editing API](LiveAgentAPI.md#inline-shader-fx) can author inline Shader FX code and parameters
in open documents, including unsaved ones. No separate shader asset or special layer target is needed.
All WhimTex HLSL effects and brushes automatically include [FastNoiseLite](AI/README.md#built-in-noise-library);
its noise and domain-warp functions need no explicit include.
Post FX is window-local presentation state and never changes API rendering, sampling or export.

### Text settings

Text also accepts `fillMode` (`None`, `Solid` default, `LinearGradient`, `RadialGradient`,
`SquareGradient`, `AngularGradient`), `gradient`, `gradientStart` (default `[0,0]`),
`gradientEnd` (`[1,0]`), `gradientRepetitions` (finite, at least 0.0001), and `gradientScope`
(`Text` default, or `Character`). `color` is the retained Solid color, not a gradient multiplier.
The palette contract is shared with figure fills. Text coordinates normalize the union of visible
glyph rectangles; Character repeats shared anchors in each nonempty glyph rectangle, excluding advance/whitespace.
Layout, hinting, Pixel coverage, clipping and transform order are unchanged. Paint edits reuse glyph coverage.
Canvas handles operate on the selected letter's relative frame, without editing the layer transform.
Text-to-Vector conversion retains Text-scope fill in one compound Vector. An active Character gradient
converts the original shell to an isolated group with one independently styled Vector child per visible glyph.
The common transform, ID, name, rendering settings and FX stay on the group; FX are not duplicated per glyph.
The serialized `groupFxInLayerSpace` flag preserves the original `LayerToLocal` frame for those FX;
ordinary groups retain their default canvas-space behavior. This conversion flag has no separate UI control.
Inside a Boolean Group, the replacement is a nested Combine/Layers Boolean Group so it remains a valid operand.
Whitespace and fully clipped glyphs create no children. All children and layer-budget checks are prepared before
replacement. The document limit remains 1024 layers. Boolean operations still use their group styling.

Create a layer with type `text`; patch `settings.text` with any of: `text` (up to 8192 characters),
`fontFamily` (installed OS font name), `fontStyle` (`Normal`, `Bold`, `Italic`, `BoldAndItalic`),
`casing` (`Normal`, `Lowercase`, `Uppercase`, `SmallCaps`),
`edgeMode` (`HintedAA`, `Antialiased`, `Pixel`; default `HintedAA`),
`fontSize` and `maxFontSize` (1..2048 canvas pixels), `spacing` (object), `characterHorizontalScale`
(0.01..10 multiplier), `alignment` (Unity TextAnchor
name), `layoutMode` (`Point` or `Frame`), `layoutOffset` (two finite local-pixel components,
each -1000000..1000000, default `[0,0]`), `frameSize` (two canvas-pixel components, each
1..32768), `wrapping` (`Manual`, `Words` or `Characters`), `overflow` (`None`, `Clip`, `Ellipsis`), `justify` or `autoSize` (booleans), or `color`
(the common encoded RGBA input). Point text uses explicit newlines only and ignores the
stored frame settings. `layoutOffset` moves the layout before rasterization, independently
of the layer Transform; X points right and Y up. It is retained across Point/Frame switches
and is edited by Point/Frame layout handles. Frame handles change only `layoutOffset` and
`frameSize`, never the layer Transform. `overflow` defaults to `None` (visible outside the frame); `Clip` clips,
and `Ellipsis` shortens displayed text to fit both axes and adds «…», without rewriting `text`.
Ellipsis truncates at text-element boundaries and leaves no partial line/suffix when the frame is too small.
Words splits overlong words, Characters
breaks at text-element boundaries. Justification expands interword spaces on wrapped lines,
not paragraph-ending lines. Enabling Justify normalizes horizontal alignment to left while
preserving vertical alignment; Point or Manual wrapping resets `justify` to false.
With `autoSize:true`, Frame text uses the largest integer font size that fits its width and
height, between `fontSize` (Min Size) and `maxFontSize` (Max Size, default 256). Neither bound is
overwritten by fitting. The range must contain at least one whole-pixel size: minimum rounds up,
maximum rounds down. Invalid API ranges are rejected; editing either bound in the UI adjusts the
other when necessary. With Auto Size off, `fontSize` is the fixed size and `maxFontSize` is ignored. Wrapping and
all spacing fields participate in fitting; `overflow` is applied after fitting reaches its minimum. Point text ignores Auto Size.
`spacing` contains `character`, `word`, `line` and `paragraph`, each -1..10 em, default 0.
One em is the effective font size, including Auto Size. These are additive adjustments, not multipliers;
partial spacing patches retain omitted fields. Character gaps occur between text elements, not after the
last element of a line; Word adjusts ordinary and nonbreaking spaces. Line adjusts the font's natural
baseline step; Paragraph adds an extra step only after explicit breaks. Negative adjustments do not
move the text cursor backwards; baseline steps stay at least 1 px. The retired `lineSpacing` multiplier is not accepted.
Example: `"spacing":{"character":0.1,"word":0.2,"line":0.25,"paragraph":0.5}`.
`characterHorizontalScale` is a 0.01..10 multiplier, default 1 (UI: Horizontal Scale).
It scales glyph width and natural horizontal advance, including natural space widths, but not
height or added em spacing. Wrapping, Auto Size, Ellipsis, inline editing and saved appearance
use the same multiplier. This is a layer setting, not a Text tool creation default.
`casing` changes display without rewriting `text`; case conversion is culture-invariant.
SmallCaps uses smaller uppercase glyphs (75%, rounded to whole pixels) for lowercase letters,
with their baseline aligned to full-size capitals. It does not select a font's OpenType small-cap feature.
`edgeMode` selects glyph rasterization through public TextCore APIs, without a TMP dependency:
`HintedAA` uses `SMOOTH_HINTED` (antialiasing with grid fitting), `Antialiased` uses `SMOOTH`
(no hinting), and `Pixel` uses `RASTER_HINTED` with binary glyph coverage, snapped glyph placement
and nearest sampling by default. Explicit filters and FX may soften Pixel output; the mode does not
quantize the final layer alpha or override color opacity. Partial patches preserve an omitted mode.
The mode participates in saved-appearance matching; changing it with a missing font requires a
matching backup or an installed replacement. These modes do not guarantee AAEDT accuracy for
arbitrarily transformed or filtered inputs.
Capabilities expose `systemFonts`, `textDefaults`, `textFontStyles`, `textAlignments`,
`textLayoutModes`, `textWrappingRules`, `textOverflowModes`, `textCasingModes` and `textEdgeModes`.
Layer inspection exposes `settings.text`, `fontAvailable`, `resolvedFont` and missing-font `warnings`.
Missing fonts do not silently substitute another family: rendering uses a matching saved mask or
transparent output. Restoring/selecting a font enables source editing. Ordinary Transform, FX,
Mapping, blending and grouping apply to Text layers. Ordinary composition rasterizes Text
before those stages; Boolean Groups consume its source-font outlines while keeping it editable.

TIFF and unified JSON retain editable settings and a bounded appearance backup, not font files or
project font assets. The agent settings snapshot excludes this encoded mask; it is managed by save.
Current limitations: uniform style per layer and no advanced complex-script shaping.
Native coverage is rasterized independently of the canvas bounds, capped at 8192 pixels per
axis and 16 megapixels, before Transform and FX. Live Canvas View text editing reserves the
document from agent writes until editing ends; it uses the ordinary document transaction and Undo.

### Vector settings

Use `type:"vector"` to add a layer. Its `referenceSize` starts at the current document
pixel dimensions. `settings.vector` accepts `shapes`, `referenceSize`, `edgeMode`,
`feather`, `featherPosition` and `brushDraft`. It uses the [document data fields](JSON_FORMAT.md#vector-layers), including
stable shape/contour/point IDs, tangent offsets and named enums. Supplied top-level
fields replace their values; omitted fields stay unchanged. Arrays replace whole arrays.
The `shapes` array accepts at most one figure; its multiple contours and holes remain
part of that figure. Add another layer for another figure. Whole-figure placement uses
the layer's transform, independently of sibling figures.
All vector styles remain stored while inactive. `inspect` returns the same data,
including the figure's editable source-mask base64 coverage and local `frame` rectangle;
`describe` returns `vectorDefaults` and `vectorFeatherPositions`.

| Vector edge field | Contract |
| --- | --- |
| `feather` | Finite 0..8192 vector reference pixels; total soft transition width, default 0. Scales with geometry like the stroke, not fixed canvas pixels. |
| `featherPosition` | Inside, Outside, Centered (default); transition lies inward, outward or equally across each painted boundary. |

Both fields belong to `settings.vector`, not to `shapes[]` or `settings.booleanGroup`.
Feather uses smoothstep signed distance to fill and actual stroked/dashed boundaries,
including caps, joins and the two sides of an outline; it is not a blur pass. It
changes coverage before layer FX and composition without blurring paint colors.
Zero preserves the existing renderer. `edgeMode:"Step"` ignores Feather, retaining
both values for later reuse. All write modes retain nondefault values while inactive;
omitted fields in API patches remain unchanged. For example:

```json
{"op":"set","layer":"<layer-id-or-@alias>","settings":{"vector":{"feather":6,"featherPosition":"Outside"}}}
```

Each shape's `fillMode` accepts None, Solid, LinearGradient, RadialGradient,
SquareGradient, AngularGradient. `gradient`, `gradientStart`,
`gradientEnd` and `gradientRepetitions` use the shared [figure-fill geometry](JSON_FORMAT.md#figure-fills)
in `referenceSize` pixels. A supplied `shapes` array replaces every figure and its
style; include inactive settings when constructing that replacement.

Each figure also has independent `strokeMode`, `strokeGradient`,
`strokeGradientStart`, `strokeGradientEnd` and `strokeGradientRepetitions` fields.
They use the same modes, palette features and geometry as fill. `stroke` remains
the enable gate; `strokeMode:"None"` also suppresses stroke without clearing its
settings. Defaults are Solid, a separate black-to-white Perceptual/Balanced/Clamp palette,
`[0,0]`, `[1,0]` and `1`. Anchors must be finite; repetitions must be finite and at
least `0.0001`, including when inactive. Vector/Boolean palettes use the native
document gradient object, as for fill; Shape patches use the API gradient contract.

`{"op":"convertToVector","layer":"<layer-id-or-@alias>"}` converts a Shape or Text
geometrically on its existing layer shell, preserving the ID, name, FX and shared settings.
Shape conversion preserves transform tiling, including explicit `Clip`. Text conversion
retains the stored tiling, including `Source` and explicit `Clip`.
Fitting the figure's frame preserves canvas placement,
but can change the stored local geometry and transform values together.

Text conversion produces one compound Vector figure with `fillRule:"NonZero"`,
containing all laid-out glyphs and their holes.
It reads original unhinted font curves and preserves the current layout, including
Frame Clip/Ellipsis, applied before fitting the geometry frame. It does not retain
editable text; keep a duplicate if needed.
Missing or unsupported source fonts and synthetic bold without a real bold face fail
before replacement. Synthetic italic is supported. The operation never traces a raster
backup. See [Text outline conversion](JSON_FORMAT.md#text-outline-conversion) for supported
font sources, clipping tolerance and edge differences. The existing batch transaction
applies to both source types; this operation does not save by itself.

### Boolean Group settings

The canvas Gradient Handles tool edits the chosen paint's local anchors in the
group's `referenceSize` metric: `style.gradientStart`/`style.gradientEnd` for Fill,
or `style.strokeGradientStart`/`style.strokeGradientEnd` for Stroke. The canvas
toolbar's Fill/Stroke popup (`gradientHandleChannel`) selects the available gradient
automatically and is disabled when only one is valid; with both active it chooses
which paint to edit. Palettes and angular
repetitions belong to the chosen paint. Handles leave group/child transforms and
the other paint unchanged. Combine with `appearance:Layers` exposes child paint
handles while retaining the inactive group style. The shared inspector controls
use `figureFill*` and `figureStroke*` names for their respective paints.

Add `type:"booleanGroup"`. Its `referenceSize` starts at the current document size.
`settings.booleanGroup` accepts `style`, `referenceSize` and `edgeMode`. Omitted
top-level fields remain unchanged; a supplied `style` replaces the complete style,
so include retained inactive fields when constructing it. Style uses the
`VectorShape` fill/gradient/stroke fields, with empty `contours`; geometry comes from
the group's children. `describe` returns `booleanGroupDefaults` and `booleanOperations`;
`inspect` returns `settings.booleanGroup` and every layer's `settings.booleanOperation`.

The group is always Isolated; explicit PassThrough is rejected. Add or move Shape,
Vector, Text or another Boolean Group into it with the usual `parent` and `index` fields.
Index 0 is the top. Starting with the bottom enabled child, each next child upward
combines with the accumulated region using its `settings.booleanOperation`:
Union, Subtract, Intersect, Xor or Combine. The base child's operation is ignored. Raster
layers and open contours are rejected. The entire batch is preflighted before
application; a bad operand does not silently disappear.

Text remains `type:"text"` with editable `settings.text`; conversion is not required.
Changing its text or layout rebuilds the operand from the same source-font outlines
used by Text conversion, with the same font restrictions and Frame overflow rules.
Text's raster backup cannot supply Boolean geometry. The child retains its font and
layout settings in TIFF/JSON; the font file is not embedded. See
[Text outline conversion](JSON_FORMAT.md#text-outline-conversion).

Child transforms and source geometry remain editable. Their appearance and FX stay
stored but do not affect the Boolean result; the group supplies common styling and
FX after geometry combination. Extraction reactivates a child's own style. API
creation does not copy a later-added child's style; pass the desired common style
explicitly. This differs from the UI command that copies the bottom figure's style once.

`Combine` preserves separate figures and their complete outlines, drawn bottom to top.
Set `settings.booleanGroup.appearance` to `Group` (default) for shared styling, or `Layers`
for each child's fill, gradient, stroke and edge treatment. This choice applies only when
the group has a uniform Combine mode; Boolean and mixed modes always use group styling.
The preference is retained on switching. Child FX, opacity, blend settings, channel mapping
and clipping flags remain ignored; figures composite with normal linear-light blending.

```json
{"operations":[
  {"op":"add","type":"booleanGroup","as":"cutout"},
  {"op":"add","type":"shape","parent":"@cutout","as":"body",
   "settings":{"shape":{"kind":"Rectangle"}}},
  {"op":"add","type":"shape","parent":"@cutout","as":"hole",
   "settings":{"booleanOperation":"Subtract","shape":{"kind":"Ellipse"}},
   "transform":{"scale":[0.5,0.5]}}
]}
```

This operation fragment makes a rectangle with an elliptical hole. Supply the
usual path/session and revision fields required by the selected API entry point.
See the [storage contract](JSON_FORMAT.md#boolean-groups) for nested groups and order.

### Shape settings

Use `type:"shape"` and partial `settings.shape` updates. `describe` returns `shapeDefaults`
and `shapeKinds`; `inspect` includes all parameters. One layer contains one editable figure.

| Setting | Values |
| :--- | :--- |
| `kind` | Rectangle (default), Ellipse, Polygon, Star, Line, Arc, Sector |
| `fill`, `fillColor` | Boolean (default true), RGBA color (default white) |
| `fillMode` | None, Solid (default), LinearGradient, RadialGradient, SquareGradient, AngularGradient, Freeform; `fill` remains the overall gate |
| `gradient` | Complete gradient palette, using the existing gradient object or color-stop array contract; default black-to-white Perceptual/Balanced/Clamp. A supplied value replaces the palette |
| `freeform`, `strokeFreeform` | Independent Freeform point lists; see [point fields](#freeform-gradient-settings). Retained when inactive. |
| `gradientStart`, `gradientEnd` | Two finite normalized layer-local coordinates, Y up; defaults `[0,0]` / `[1,0]`; API components -1000000..1000000, allowed outside 0..1 |
| `gradientRepetitions` | Finite float, minimum `0.0001`, default `1`; AngularGradient only. One period spans `360 / gradientRepetitions` degrees from the start-to-end ray before palette wrapping. Retained and validated in every mode, including disabled fills |
| `stroke`, `strokeColor` | Boolean (default false), RGBA color (default black) |
| `strokeMode` | None, Solid (default), LinearGradient, RadialGradient, SquareGradient, AngularGradient, Freeform; `stroke` remains the enable gate; None suppresses paint without clearing settings |
| `strokeGradient` | Complete independent palette using the API gradient object or color-stop array; default black-to-white Perceptual/Balanced/Clamp. A supplied value replaces only this palette |
| `strokeGradientStart`, `strokeGradientEnd` | Two finite normalized layer-local coordinates, Y up; defaults `[0,0]` / `[1,0]`; allowed outside 0..1 within the finite float range |
| `strokeGradientRepetitions` | Finite float, minimum `0.0001`, default `1`; AngularGradient only, before palette wrapping. Retained and validated even with stroke disabled or another mode selected |
| `strokeWidth` | 0–8192 canvas pixels; default 2; outline width, independent of body thickness |
| `arcThickness` | 0–8192 canvas pixels; default 8; Arc body thickness; Transform sets the centerline ellipse |
| `strokePosition` | Inside (default), Center, Outside; relative to the body contour, including Arc. Shape's C# values remain Inside=0, Center=1, Outside=2; Vector uses different values |
| `strokeJoin` | Miter, Round (default), Bevel; outline joins, independent of body corner settings |
| `strokeCap` | Butt (default), Round, Square; ends of stroke dashes; independent of `lineCap` |
| `miterLimit` | Finite 1–10000; default 4; miter ratio above which a Miter join becomes Bevel; retained for other joins |
| `dashPattern` | Up to 256 finite positive numbers, each at most 10000000; alternating dash/gap lengths in multiples of `strokeWidth`. Default `[]`; empty or null is solid; odd lists repeat twice |
| `dashOffset` | Finite -10000000..10000000 document pixels; default 0; phase along the contour, independent of stroke width |
| `lineCap` | Butt, Round (default), Square; body ends of Line and Arc, independent of outline dashes |
| `edgeMode` | Antialiased (default), Step; Step ignores Feather and evaluates binary geometric coverage after Transform on the canvas pixel grid |
| `feather` | 0–8192 canvas pixels; total soft transition width; default 0 keeps the original antialiasing |
| `featherPosition` | Inside, Outside, Centered (default); fades inward, outward or equally across the contour, including both boundaries of a hollow stroke |
| `rectangleCorners` | Four `{style:"Round"\|"Bevel",amount:0..1}` objects: TL, TR, BR, BL in local orientation |
| `polygonCorners` | One corner object per vertex; count must equal `sides`; first vertex points up, subsequent vertices follow counterclockwise |
| `outerCorner`, `innerCorner` | Independent Star/Sector groups, each with its own style and amount. Sector's Inner is the center corner; Outer covers both arc joins. Unused for Sector at Sweep 360 |
| `linkCorners` | Boolean, default true; links rectangle/polygon amounts, never styles |
| `sides` | 3–32 polygon sides / star points; default 5 |
| `innerRadius` | 0.01–1 star inner/outer radius ratio; default 0.5 |
| `startAngle`, `sweepAngle` | Arc/Sector degrees: start -360000..360000 (default 0), sweep 0..360 (default 90); zero points right, positive turns counterclockwise |

Shape and Vector share [figure-fill geometry](JSON_FORMAT.md#figure-fills). Shape
measures the gradient in current canvas width/height before layer Transform;
Vector uses `referenceSize`. Start is the center for nonlinear gradients, and End
sets radius and orientation. Angular advances counterclockwise from the start-to-end
ray, with its seam on that ray. Equal anchors yield `t=0`. Palette `wrapMode` controls
repetition after the angular-only `gradientRepetitions` multiplier. Partial Shape updates preserve omitted
fill and stroke fields, including their independent inactive palettes, anchors and
repetitions; inspect/describe expose them. FullOptimized and Compact preserve both
inactive paint styles as well as Full JSON. Changing stroke settings does not
change fill settings or the layer transform.

Shape and Vector Inside/Outside strokes use a half-width trajectory offset before
dash/cap generation, not clipping of a finished double-width stroke. Round dots and
caps remain whole. Dash placement follows the new path; holes reverse the filled
side, narrow insets can split/disappear, and tight corners do not guarantee strict
containment of complete caps. Center/open paths retain their original trajectories.
See [stroke geometry semantics](JSON_FORMAT.md#shape-strokes).

UI displays corner amounts as 0–100%; data/API uses 0–1. Zero keeps a sharp corner.
For Rectangle, amount scales the shorter full side: an independent corner can exceed 50%, up to the
whole side if its neighbours leave enough room. Polygon/Star use the shorter transformed half-extent.
Round uses a circular fillet radius and Bevel uses the distance trimmed along each adjacent edge. Sector uses this fraction
of its normalized half-radius; its round joins follow the ellipse transform.
Limits use actual consumed edge lengths. A direct C# `SetCorner(index, value, halfSize)` or independent UI
edit gives that corner/group priority, reduces stored neighbours and then clamps the active amount.
For Star/Sector, index 0 is Outer and index 1 is Inner. Sector groups have separate styles and share the radial edge budget;
at Sweep 180 there is no center corner to round, and at Sweep 360 neither group changes the full ellipse.
Reducing it does not restore neighbours. Linked edits preserve ratios (editing a zero corner adds
the same amount to all), while style changes remain independent. Geometry changes and API assignments
restrict the whole affected set proportionally. Inspect reports the resulting stored values.
The former `roundness`/`cornerRoundness` keys are not accepted.

New shapes are centered with transform scale `[0.5,0.5]`. The untransformed bounds cover
the canvas: a 100×40 figure in a 512×256 document uses scale `[0.1953125,0.15625]`.
Set position/rotation with the existing `transform` operation. Line's endpoints span its transformed
width; transformed height sets its body thickness. Round/Square caps extend by half the thickness.
Arc has a body controlled by `arcThickness`, with the same independent Fill and Stroke controls
as Line. Its outline follows both sides and its caps; `strokeWidth` does not change body thickness.
Zero arc thickness or Sweep 0 is empty; Sweep 360 closes the arc into a ring without end caps,
or makes Sector a full ellipse. Arc thickness and stroke width stay in canvas pixels when resizing.
Colors follow the existing encoded-color contract; set layer color/blend ranges to HDR when needed.
Inactive type-specific settings are retained when `kind` changes. SVG import/export is not implied.

Shape and Vector share stroke join, cap and dash construction. Shape retains its existing
transform metric: ordinary resizing keeps stroke width, dash lengths and offset in document
pixels; perspective retains the existing local approximation. Vector transforms its local
stroke geometry along with the layer. Shape retains its Feather
controls. Shape's `dashPattern` is always width-relative: `[2,1]` gives a dash twice the
stroke width and a gap once that width, for every stroke position. Shape has no
`dashPatternRelative` field. Changing `strokeWidth` scales those lengths; ordinary resizing
does not. `dashOffset` remains a pixel distance. Supplied patterns replace the entire list;
omitted fields retain their current values. `inspect` and `shapeDefaults` expose the stroke
fields, including inactive settings. A null pattern is normalized to `[]` by the API.
All document JSON modes retain inactive stroke settings, including when `stroke`
is false or `strokeMode` is None. Compact may omit defaults and restores them on read.

Feather uses the procedural contour distance, not a blur pass. At large widths, thin strokes and
small shapes can become faint or disappear with Inside/Centered. Outside can expand into nearby
gaps. Feather is applied before layer FX, opacity and compositing; colors retain their RGB/HDR values.
Step only makes geometric coverage binary: color alpha, opacity, FX and later sampling retain their
normal behavior. With opaque colors and no subsequent softening, full-resolution source alpha is 0/1.

```json
{"op":"add","type":"shape","as":"badge","settings":{"shape":{
  "kind":"Star","sides":5,"innerRadius":0.45,
  "fillColor":[1,0.65,0.1,1],"stroke":true,"strokeWidth":3
}}}
```

To add a dashed outline to that Shape:

```json
{"op":"set","layer":"@badge","settings":{"shape":{
  "strokeJoin":"Bevel","strokeCap":"Round","miterLimit":4,
  "dashPattern":[2,1],"dashOffset":3
}}}
```

### Noise settings

Use `type:"noise"` with partial `settings.noise` updates. `describe` exposes `noiseDefaults`,
`noiseFields`, `noiseFieldTypes`, `noiseTypes`, `noiseFractals`, `noiseCellularDistances`, `noiseCellularReturns`, `noiseWarps`,
`noiseEncodings`, `noiseVectorOutputs`, `noiseDimensions`, `noisePeriodicAxes`, `noiseGrainColors`. `inspect` returns stored generator parameters, including inactive settings.

```json
{"op":"add","type":"noise","as":"height","settings":{"noise":{
  "noiseType":"OpenSimplex2","seed":1337,"scale":8,"offset":[0,0],
  "fractal":"FBm","octaves":3,"lacunarity":2,"gain":0.5,"encoding":"Linear"
}}}
```

| Setting | Values / limits |
| :--- | :--- |
| `field` | Value (default), Curl, GradientVector, CellDirection |
| `noiseType` | OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic, Value, WhiteNoise, BlueNoise |
| `grainColor` | Monochrome (default), Color (independent RGB); shared by WhiteNoise and BlueNoise |
| `grainSize` | 1–1024 canvas pixels per grain, default 1; shared by WhiteNoise and BlueNoise |
| `dimensions` | TwoD (default), OneD (straight stripes), ThreeD (2D slice at Offset Z; not White/Blue) |
| `periodic` | UI **Seamless**: None (default), X (left/right), Y (top/bottom), XY (both pairs); six non-grain noise types in TwoD/ThreeD only |
| `periodic1D` | Boolean, default false; UI **Seamless** checkbox in OneD, repeats along the noise axis; ignored outside OneD and for White/Blue |
| `direction` | −180–180 degrees, default 0; OneD only; 0 varies horizontally (vertical stripes), 90 varies vertically |
| `seed` | Signed 32-bit integer; passed to the shader as an integer, not a float |
| `scale` | Scalar sets XY, or XYZ in active ThreeD; `[x,y]` preserves Z, `[x,y,z]` sets all axes. Each 0.01–1000; default `[8,8,1]`. XY spans the shorter canvas side; Z multiplies Offset Z. Inspect returns XYZ including inactive Z |
| `linkScale` | Boolean, default true; proportional inspector edits, explicit API values are applied literally |
| `offset` | `[x,y]` preserves Z, `[x,y,z]` sets all axes; each −10000–10000 noise-space units (XY canvas pixels for WhiteNoise/BlueNoise). Inspect returns three values |
| `fractal` | None, FBm, Ridged, PingPong |
| `octaves`, `lacunarity`, `gain` | Integer 1–8; 1–4; 0–1 |
| `weightedStrength`, `pingPongStrength` | 0–1; 0.01–8 |
| `cellularDistance` | Euclidean, EuclideanSquared, Manhattan, Hybrid |
| `cellularReturn` | CellValue, Distance, Distance2, Distance2Add, Distance2Sub, Distance2Mul, Distance2Div |
| `cellularJitter` | 0–1 |
| `warp`, `warpStrength` | None, OpenSimplex2, OpenSimplex2Reduced, BasicGrid; 0–100 noise-space units |
| `warpSeed` | Signed 32-bit integer, default 1337; independent of the noise `seed` |
| `warpScale` | Scalar sets XY, or XYZ in active ThreeD; `[x,y]` preserves Z, `[x,y,z]` sets all axes. Each 0.01–1000; default `[1,1,1]`. Multipliers of Noise Scale; inspection returns XYZ |
| `linkWarpScale` | boolean, default true; UI/Random All chain, explicit API values apply literally |
| `encoding` | Value only: Linear (default; raw normalized values), Gamma (display colors), Gradient (monochrome noise mapped through a palette) |
| `vectorOutput` | Vector fields only: PackedVector (default; `XYZ * 0.5 + 0.5`), SignedVector (raw XYZ). Linear data, alpha 1, no implicit clamping |
| `normalize`, `strength` | Vector fields only: boolean (default false), 0–100 (default 1). Normalize first, then multiply by Strength; zero stays zero |
| `inverted` | Boolean; Value reverses 0–1 before palette sampling; vector fields negate the vector before encoding |
| `gradient` | Shared stops/object, default black-to-white, Perceptual color mode and Balanced interpolation when omitted; RGBA/HDR palette, retained while disabled |

With Field Value and without gradient mapping, RGB repeats the normalized scalar, except WhiteNoise/BlueNoise with Color which generates independent RGB; alpha is 1. Gradient output supplies RGB and alpha through the same encoded LUT and linear decode as SDF. Inverted remains available and reverses values before palette sampling. Color WhiteNoise/BlueNoise supports only Gamma/Linear: a retained Gradient setting temporarily renders/displays Gamma, then returns to Gradient on switching to monochrome. Changing Output preserves the palette. Random All preserves `gradient` and keeps `encoding:Gradient`; otherwise it randomizes encoding only between Gamma and Linear. It can vary `inverted`; for grain noise with stored Gradient output it also preserves the Color setting so effective Output cannot change. Noise operations put the palette inside `settings.noise`; unified document/clipboard JSON uses `behaviour.gradient` on `NoiseLayerBehaviour`. Noise has no `useGradient` field.
UI **Output** maps to `encoding` for Value or `vectorOutput` for vectors, **Seamless** to `periodic` (TwoD/ThreeD) or `periodic1D` (OneD), and the Scale chain to `linkScale`.
There is no Noise `seamless` boolean, `scaleY`, `scaleZ` or `offsetZ` API field: use `periodic`/`periodic1D`, `scale:[x,y,z]`
and `offset:[x,y,z]`. Supplying `gradient` alone does not enable it: also set `encoding:"Gradient"`.
Gradient updates replace the whole palette, not individual keys. Omitted fields in settings retain
their existing values. Inspection reports stored settings, even when a type/dimension temporarily
ignores them. Unified JSON export uses its selected write mode and frozen version defaults,
not the live editing-operation shapes or current new-layer factory defaults.

Curl supports OpenSimplex2, OpenSimplex2S and Perlin. GradientVector also supports
ValueCubic and Value. An incompatible stored `noiseType` temporarily evaluates OpenSimplex2;
switching back to Value restores the stored choice. CellDirection always evaluates Cellular.
Vector fields use TwoD or ThreeD; a stored OneD temporarily evaluates TwoD.
Value, Curl and GradientVector share all Fractal settings, including Weighted Strength.
CellDirection ignores Fractal and Cellular Return, retaining Distance, Jitter and Domain Warp.

GradientVector differentiates the complete scalar potential, including Scale ratios,
Fractal and Domain Warp. Curl uses its rotated gradient in 2D or the curl of three seeded
potentials in 3D. Finite differences use one common scale unit; this is not three unrelated
RGB noises. Normalize changes the mathematical properties of the field. CellDirection
points to the nearest center in the warped lattice, without solving an inverse warp.

Signed output and packed values outside 0–1 require `colorRange:"HDR"`. Use
`blendRange:"HDR"` for data blending, including on isolated parent groups. An 8-bit color
export cannot retain negative values. For Displacement Map's VectorRG use PackedVector
and Neutral 0.5; with SignedVector, Neutral 0 requires half the consumer Strength because
that FX decodes `(RG - Neutral) * 2`.

For example, this partial update combines a 3D seamless source with an explicit palette:

```json
{"op":"set","layer":"LAYER-ID","settings":{"noise":{
  "noiseType":"Perlin","dimensions":"ThreeD","periodic":"XY",
  "scale":[6,10,1],"linkScale":false,"offset":[0,0,0.5],"encoding":"Gradient",
  "gradient":{"colors":[{"time":0,"color":[0,0,0,1]},{"time":1,"color":[1,1,1,1]}],"mode":"Perceptual"}
}}}
```

With Field Value, FastNoiseLite output is remapped from signed noise to 0–1 and clamped.
WhiteNoise hashes discrete canvas-space cells with the signed integer seed. It ignores `scale`, fractal,
cellular and warp settings without resetting them; `grainSize` controls its grain size instead.
It supports inversion, encoding and OneD direction, and keeps its grid independent of preview resolution.
BlueNoise uses WhimTex-generated periodic void-and-cluster rank tables: 128×128 RGB in 2D and
a separate 256-sample RGB sequence in 1D. Seed hashes select translations/reflections (plus axis swaps in 2D),
not an expensive runtime rebake. Each channel has a separately generated rank table. Grain coordinates,
ignored settings, encoding and inversion match WhiteNoise. Both grain types use
`grainColor` and `grainSize`; old `whiteNoise*` aliases are not accepted. Offset Y in 1D selects a seeded variation of the sequence.
The tables use 8-bit uniform ranks; use Linear for raw dither thresholds.
For masks/channel packing, prefer Linear and apply the existing ChannelMapping/blend settings.
For a Normal Map or SDF source, add the effect above Noise and assign `Previous` or a specific target as usual.
Domain Warp uses a single warp pass; noise fractal settings affect the subsequent noise evaluation.
Warp Seed is independent of Seed. Warp Scale multiplies Noise Scale separately on
X/Y and, in ThreeD, Z. For example, Scale [0.5,2,3] and Warp Scale [6,0.5,2] request final
warp scales [3,1,6]. A scalar API value sets active-axis multipliers.
The chain only affects UI editing and Random All; enabling it retains the current ratio.
Random All chooses X logarithmically from 0.25–4 and scales linked active axes proportionally
within the legal range, or samples them independently when unlinked. It retains the chain state and inactive Z.
Seamless fits the resulting warp lattice periods and compensates displacement only for
period fitting. Z remains non-periodic. Missing multipliers default independently to 1;
no migration or legacy alias lookup is performed. Unified document JSON stores
`warpScale`, `warpScaleY` and `warpScaleZ`; the editing API uses the `warpScale` vector.
One-cell XY BasicGrid can still give uniform displacement; increase Warp Scale to obtain
a varying warp field at small Noise Scale. White/Blue Noise ignore these controls.
OneD projects aspect-correct centered coordinates onto the direction axis before offset and warp.
Offset X moves along the slice and Y selects the slice. Thus warp and fractals preserve stripe invariance.
ThreeD exposes Z in Scale and Offset and evaluates the native 3D kernel, including 3D Fractal and Domain Warp.
The sampled slice coordinate is `Offset Z × Scale Z`. Missing Scale Z defaults to 1;
1D/2D editing retains inactive Z. With Offset Z = 0, changing Scale Z alone does not move the slice.
Z is never periodic and never advances automatically. Cellular 3D slices differ visibly from 2D cells.
White/Blue retain their 1D/2D behavior: a stored ThreeD temporarily uses TwoD; Periodic is ignored.
OneD ignores `periodic`, retaining it for a return to TwoD/ThreeD; its separate `periodic1D`
flag enables the same periodic lattice kernels along the scalar axis before Fractal/Warp.
For canvas W/H and short side S, the period extent is
`abs(W/S * ScaleX * cos(Direction)) + abs(H/S * ScaleY * sin(Direction))`.
Each octave and warp fits that extent independently; the transverse coordinate stays fixed
before warp. Offset X moves along the slice, Y selects it, Z is ignored. Direction 0 joins
left/right and 90 joins top/bottom. Arbitrarily angled stripes repeat along their axis but
need not join rectangular canvas edges. Both seamless settings are retained independently.

Periodic wraps lattice hashes, not output colors. It fits integer periods independently on selected axes
for every octave and the warp. Each period rounds `extent * octaveFrequency / latticeUnit`
directly, with a minimum of one cell; 2D simplex no longer rounds the base extent before applying
octave frequency. Sub-unit Scale therefore affects later fractal octaves, but does not remove
the minimum-cell limit of Fractal None. This changes some existing 2D periodic simplex patterns
at small and large scales, without changing stored parameters or non-periodic/3D rendering.
Non-integral lacunarity remains periodic. Square/cubic lattices round
to whole cells; 2D simplex uses an orthogonal basis of its triangular lattice, and 3D simplex fits
external periods in multiples of three lattice units without repeating Z. Small scales can change
in visible steps. Source-rectangle periodicity does not guarantee canvas tiling after arbitrary
layer/group transforms or FX. Extreme aspect/scale/octave combinations exceeding 100 million lattice
units, or base coordinates beyond 500 million lattice units, report an error instead of overflowing
integer indices. High-frequency detail can still alias.
Random All preserves Field, vector Output, Normalize, Strength, Dimensions, Direction, both Seamless settings (`periodic`, `periodic1D`), the linked scale ratio and all Offset components (X/Y/Z). It randomizes Seed and Warp Seed independently and chooses only compatible Noise Types.

Linked main Scale samples the arithmetic mean M logarithmically from 1–64, then derives the active axes (XY, or XYZ in ThreeD)
from the retained ratio; swapping axes does not change the distribution of M. Intersect this
mean range with the means allowed by the per-axis 0.01–1000 bounds. If the intersection is empty
for an extreme ratio, use its feasible mean range instead (8 may be unreachable without changing
that ratio). Sampling the feasible interval directly avoids accumulating candidates at a clamp.
Unlinked active axes sample independently, logarithmically from 1–64. Inactive Scale Z is retained in 1D/2D.
Candidates are accepted with probability `(1 + exp(-0.5 * log2(M / 8)^2)) / 2`,
where `M = (X + Y) / 2` in 1D/2D and `(X + Y + Z) / 3` in ThreeD. This reweights the candidate distribution
by a factor in [1,2], without extra truncation or clamping at 8. For unlinked
axes the baseline distribution of M is not log-uniform: the cap is on the relative weighting,
not on absolute probabilities of arbitrary numeric bins. Warp Scale sampling is unchanged.
Generation runs on GPU at the requested resolution.
Seed and normalized coordinates are stable across preview/export sizes, but different GPUs may produce small
floating-point differences. Saving stores the procedural parameters through existing document serialization;
the usual baked output texture is still generated when required.

### Gaussian Blur settings

`strength` is 0–4 (default 1; UI 0–400%). Zero bypasses the blur; 0–1 mixes the source and blur in
premultiplied linear RGBA. Above 1, RGB stays unchanged and alpha becomes `a*s/(1+a*(s-1))`, with
`a` clamped to 0–1. This matches Motion Blur: denser translucent coverage, not a larger radius or RGB gain.
Radius zero remains an identity operation at every strength. Transform and FX still apply after bypass.

Use `type:"blur"` and partial `settings.blur` updates, with `mode:"Gaussian"` (default).
`describe` exposes `blurDefaults`, `blurModes`, `blurDirections` and `blurEdges`;
`inspect` returns settings for all modes, including inactive ones.
The former `gaussianBlur`/`motionBlur` API types and serialized layer classes are removed without migration.
Recreate old blur layers with the new type; request envelope version remains 1.

```json
{"op":"add","type":"blur","as":"blur","settings":{
  "colorRange":"HDR","blur":{"mode":"Gaussian","radius":32,"edges":"Repeat"}
}}
```

Assign a stable source ID (or batch alias) with
`{"op":"target","layer":"@blur","input":"Specific","target":"@source"}`.
Radius is the finite kernel extent (three standard deviations) in original canvas pixels;
0 bypasses filtering. Sources may be hidden. Groups are sampled against transparency without
changing their Pass Through setting. Layer Transform, channelMapping, clipping, opacity and blend settings
apply normally to the effect. API rendering/saving uses the full-quality algorithm, never the main
window's interactive approximation. Export to PSD rasterizes this effect.
See [Gaussian Blur](GaussianBlur.md) for transparency, HDR and cache behavior.

### Sharpen settings

Use `type:"sharpen"` and partial `settings.sharpen` updates. `algorithm` is `Gaussian`
(smooth unsharp mask) or `Adaptive` (a local edge-coherence mask that favors coherent edges
over weak, directionless detail). `strength` is 0..4
(UI 0–400%); `radius` is 0..32 original canvas pixels. `threshold`, `noiseReduction` and
`haloSuppression` are 0..1. `channelMode` is `RGB` or `Luminance`. `edges` selects
`Transparent`, `Clamp`, `Repeat` or `Mirror` sampling outside the source. Zero strength or
radius leaves the source unchanged. `noiseReduction` further suppresses irregular detail in
Adaptive mode and has no effect in Gaussian mode. It does not remove noise from the source.
Sharpen preserves alpha and does not clamp HDR RGB values.

```json
{"op":"add","type":"sharpen","as":"crisp","settings":{
  "sharpen":{"algorithm":"Adaptive","strength":1.25,"radius":1.5,
    "threshold":0.05,"noiseReduction":0.15,"haloSuppression":0.4,
    "channelMode":"RGB","edges":"Clamp"}
}}
```

Assign a source with `{"op":"target","layer":"@crisp","input":"Specific","target":"@source"}`.
The effect supports hidden sources, groups, transforms, ranges, channelMapping, clipping and FX
like other targeted effects.

### Motion Blur settings

Use the same `type:"blur"` and partial `settings.blur` updates with `mode:"Linear"` or `"Circular"`.
All modes share strength, edges and target; changing mode preserves radius, distance, angle, arc, center and direction.

| Field | Values / meaning |
| --- | --- |
| `mode` | `Gaussian` (default), `Linear`, `Circular` |
| `radius` | 0–256 canvas pixels; default 8; Gaussian only |
| `strength` | 0–4, default 1 (UI 0–400%); below 1 mixes with the original; above 1 increases translucent trail density without changing RGB brightness or length; fully opaque pixels stay unchanged |
| `distance` | 0–512 original canvas pixels; default 16; used by Linear |
| `angle` | −180–180 degrees; default 0; 0 points right, positive turns counterclockwise; Linear |
| `arc` | 0–360 degrees of rotation; default 15; Circular |
| `center` | `[x,y]`, each 0–1; bottom-left origin, default `[0.5,0.5]`; Circular |
| `direction` | `Centered` (default), `Forward`, `Backward`; Forward follows Angle or rotates counterclockwise |
| `edges` | `Transparent` (default), `Clamp`, `Repeat`, `Mirror` |

```json
{"op":"add","type":"blur","as":"motion","settings":{
  "colorRange":"HDR","blur":{"mode":"Circular","arc":25,"center":[0.4,0.6],"direction":"Centered","edges":"Transparent"}
}}
```

Assign the source with `{"op":"target","layer":"@motion","input":"Specific","target":"@source"}`.
Previous uses the sibling below. Hidden sources and isolated group color are supported, just as
for Gaussian Blur. Zero Distance (Linear) or Arc (Circular) bypasses filtering. Inactive-mode
settings are retained when switching modes. Transform, channelMapping, clipping, opacity and blend
settings use the normal effect-layer paths. API rendering uses full quality; PSD rasterizes the effect.
See [Motion Blur](MotionBlur.md) for sampling, alpha, quality and memory details.

### Make Seamless settings

Use `type:"makeSeamless"` and partial `settings.makeSeamless` updates in shared operations,
including live batches. Unified document/clipboard JSON puts the native fields directly inside
`behaviour` with `$type: "MakeSeamlessLayerBehaviour"`, without a `properties` or `settings` wrapper;
see the [clipboard contract](AI/README.md) and [complete recipe](Examples/Clipboard/seamless-noise.json).
This is a targeted effect layer, not an FX preset or an input wrap mode.

The following operations create a hidden source and an explicitly targeted effect. Place this array
inside the `operations` of the chosen [batch workflow](#shared-editing-operations):

```json
[
  {"op":"add","type":"noise","as":"source","settings":{"enabled":false,"noise":{"seed":42,"scale":6}}},
  {"op":"add","type":"makeSeamless","as":"tile","settings":{"makeSeamless":{
    "mode":"OffsetBlend","edgeWidth":0.2,"offsetTransitionStart":-0.25,
    "offsetSeamCorrection":true,"offsetPoissonEdges":"AllEdges"
  }}},
  {"op":"target","layer":"@tile","input":"Specific","target":"@source"}
]
```

For an existing effect, use its inspected layer ID in `set`:

```json
{"op":"set","layer":"LAYER-ID","settings":{"makeSeamless":{
  "mode":"PatchQuilting","quiltingEdges":"LeftAndRight","quiltingWidth":0.2,
  "quiltingAlongSearch":0.15,"quiltingFeather":50,"quiltingQuality":"Normal",
  "quiltingChannels":"Independent","processAlpha":false,
  "quiltingSeamCorrection":true,"quiltingPoissonEdges":"LeftAndRight"
}}}
```

This second example treats checked RGB channels as independent packed maps, preserves input alpha,
and joins left/right edges for horizontal repetition. Prefer `Linked` for ordinary colored images.

`whimtex_describe` / `WhimTexApi.Describe()` exposes `makeSeamlessDefaults`,
`makeSeamlessModes`, `makeSeamlessPoissonEdges`, `makeSeamlessHorizontal`,
`makeSeamlessVertical`, `makeSeamlessQuiltingQuality` and `makeSeamlessQuiltingChannels`.
Inspection returns every field below under `settings.makeSeamless`, including inactive-mode settings.
Omitted fields retain their values; switching mode neither resets nor transfers another mode's settings.
Defaults below apply to **new layers created by the registry/API**, not raw C# field initializers or
already saved layers. Use `MakeSeamlessLayerBehaviour.CreateDefault()` for equivalent C# defaults.
Enum names are case-sensitive strings; unknown fields, removed mode names and out-of-range values are rejected.

#### Shared controls and edge selection

| API field | Allowed values / new-layer default | Meaning |
| --- | --- | --- |
| `mode` | `OffsetBlend` (default), `Mirror`, `ScreenedPoisson`, `PatchQuilting` | UI **Method**; choose the base algorithm explicitly in reusable recipes. |
| `processRed`, `processGreen`, `processBlue`, `processAlpha` | Booleans, all `true` | UI **Channels**; unchecked channels are restored from the input after seam processing, before ordinary layer FX/compositing. All false skips seam processing. |

All paired-edge fields below accept `AllEdges` (default), `TopAndBottom`, `LeftAndRight`, `None`.
`TopAndBottom` joins the top/bottom borders for vertical tiling; `LeftAndRight` joins left/right
borders for horizontal tiling. `None` skips **only that pass**. The four copy-edge booleans and
Mirror directions do not limit an enabled Poisson pass. Clearing main-pass edges is therefore
not a full bypass unless correction is also disabled or its edges are `None`.

Poisson correction is global: selected axes wrap, unselected axes use natural boundaries, but
interior and unselected edge values can still change. A smaller radius localizes influence more,
without making a hard protected center. Tiny images relax seam-slope constraints.

#### Offset Blend

Blends half-period-shifted copies in boundary bands; histogram compensation reduces contrast loss.

| API field | Range / new-layer default | UI / behavior |
| --- | --- | --- |
| `leftEdge`, `rightEdge`, `bottomEdge`, `topEdge` | Booleans, all `true` | **Copy Edges**, independent destination bands. All false skips copying only. |
| `edgeWidth` | 0.02–0.5 / 0.2 | **Blend Width (%)**: fraction of each corresponding dimension; bands have at least two pixels. |
| `offsetTransitionStart` | -1–0.95 / -0.25 | **Transition Start (%)**, relative to Blend Width, not the canvas. |
| `offsetContrastCompensation` | Boolean / `true` | **Contrast Compensation**. |
| `histogramContrast` | 0–1 / 1 | **Strength (%)**; zero skips histogram analysis. |
| `offsetSeamCorrection` | Boolean / `true` | **Poisson Correction**, after blending. |
| `offsetPoissonEdges` | Paired-edge enum / `AllEdges` | **Poisson Edges**, independent of Copy Edges. |
| `offsetAutoRadius` | Boolean / `true` | **Automatic Radius**: max(0.005, edgeWidth / 4). |
| `offsetCorrectionRadius` | 0.005–0.25 / 0.05 | Manual **Radius (%)**, fraction of the smaller dimension; stored but unused while automatic. |

#### Mirror

Reflects the opposite edge into the selected destination and blends it back into the original.

| API field | Range / new-layer default | UI / behavior |
| --- | --- | --- |
| `horizontal` | `Off`, `LeftToRight` (default), `RightToLeft` | **Mirror Direction**: left source → right destination, or the reverse. |
| `vertical` | `Off`, `BottomToTop` (default), `TopToBottom` | **Mirror Direction**: bottom source → top destination, or the reverse. |
| `blendWidth` | 0.001–0.5 / 0.2 | **Blend Width (%)**, fraction of each corresponding dimension. |
| `mirrorTransitionStart` | -1–0.95 / -0.25 | **Transition Start (%)**, relative to Blend Width. |
| `falloff` | 0.25–4 / 1 | **Falloff**; larger values concentrate reflection near the destination edge. |
| `mirrorContrastCompensation` | Boolean / `false` | **Contrast Compensation**, histogram mixing with reflected donors. |
| `mirrorContrast` | 0–1 / 1 | **Strength (%)**; zero uses ordinary Mirror. |
| `mirrorSeamCorrection` | Boolean / `true` | **Poisson Correction**, also runs when both directions are Off. |
| `mirrorPoissonEdges` | Paired-edge enum / `AllEdges` | **Poisson Edges**, independent of Mirror Direction. |
| `mirrorAutoRadius` | Boolean / `true` | **Automatic Radius**: max(0.005, blendWidth / 4). |
| `mirrorCorrectionRadius` | 0.005–0.25 / 0.05 | Manual **Radius (%)**, fraction of the smaller dimension; retained while automatic. |

For both blend methods, zero Transition Start begins fading at the edge; positive values delay/narrow
the fade, negative values start it outside the canvas, leaving some original at the border. This may
reintroduce a seam. New layers already enable Poisson Correction, but **changing Transition Start does
not toggle it**. Check the correction's enabled state and edge pairs explicitly. With correction off,
pixels outside modified bands retain the input apart from floating-point/alpha roundoff.

#### Screened Poisson

| API field | Range / new-layer default | UI / behavior |
| --- | --- | --- |
| `poissonEdges` | Paired-edge enum / `AllEdges` | **Poisson Edges**, selecting the standalone solve. `None` skips it. |
| `screeningRadius` | 0.005–0.25 / 0.05 | **Radius (%)**, fraction of the smaller dimension. |

Uses source-value fidelity and discrete seam-slope constraints. Does not use copy bands, Mirror
directions, contrast-compensation flags or the other modes' correction radii.

#### Patch Quilting

Searches source boundary-strip donors and joins them using low-error dynamic-programming cuts;
not whole-image texture synthesis. Optional Along-Seam Search smoothly displaces donor samples
along the strip, tapering to zero at its ends without wrapping; it is not a rigid translation.

| API field | Range / new-layer default | UI / behavior |
| --- | --- | --- |
| `quiltingEdges` | Paired-edge enum / `AllEdges` | **Patch Edges**; `None` skips quilting, not independent correction. |
| `quiltingWidth` | 0.02–0.45 / 0.2 | **Patch Width (%)**, fraction of each corresponding dimension. |
| `quiltingAlongSearch` | 0–0.25 / 0 | **Along-Seam Search (%)**, relative to usable strip length. Nonzero divides the same candidate budget between straight and displaced strips. |
| `quiltingQuality` | `Draft`, `Normal` (default), `High` | **Search Quality**; analysis limits 96/160/256 per axis and 8/24/48 candidates. |
| `quiltingChannels` | `Linked` (default), `Independent` | **Channel Matching**; one donor/cut using checked premultiplied channels, or separate straight-value searches per checked channel for packed maps. |
| `quiltingSeed` | Signed 32-bit integer / 0 | **Seed**; repeatable for the same input, resolution and settings. Different seeds can still select the same donor. |
| `quiltingFeather` | 0–100 / 50 | **Feather (%)**, already in percentage units; share of the safe transition width of each cut, not pixels or a fraction of Patch Width. |
| `quiltingContrastCompensation` | Boolean / `false` | **Contrast Compensation** within Feather transitions. |
| `quiltingContrast` | 0–1 / 1 | **Strength (%)**; no extra compensation at zero strength or zero Feather. |
| `quiltingSeamCorrection` | Boolean / `false` | Independent global **Poisson Correction**, after quilting. |
| `quiltingPoissonEdges` | Paired-edge enum / `AllEdges` | **Poisson Edges**, independent of Patch Edges. |
| `quiltingCorrectionRadius` | 0.005–0.25 / 0.05 | **Radius (%)**, fraction of the smaller dimension; no automatic radius. |

Feather blends across each cut, not a blur of the texture. A one-pixel band cannot feather; on small
images several percentages may look identical. Compensation in the first axis can affect donor
selection in the second. Higher Search Quality costs more and does not guarantee a better visual match.

#### Units, alpha and verification

- UI percentages are API fractions (`20%` → `0.2`, `-25%` → `-0.25`, `5%` radius → `0.05`),
  **except `quiltingFeather`**, where `50%` → `50`.
- Disabled channels retain straight linear input values before subsequent layer processing.
  Disabling A preserves input alpha, not RGB independence from alpha: premultiplied methods still
  use input alpha. Independent quilting matching uses straight values; an enabled Poisson correction
  remains premultiplied. Independent matching is not a color-preserving mode for transparent artwork.
- Histogram compensation can alter colors; Poisson can affect contrast and overshoot RGB. Alpha is
  bounded; HDR RGB remains available until normal layer/output range handling. No method guarantees
  invisible joins for arbitrary structured images. Inspect both the tile join and the interior.
- Previous input uses the next sibling below; Specific can target a hidden source or isolated group
  color. Normal transforms, ranges, ChannelMapping, FX, clipping and composition still apply and can break
  the final join. These methods remain editable; raster export evaluates the effect.
- Reduced-size Quilting previews can choose different donors/cuts. Ordinary window preview is capped
  at 512 px; Live Quality does not remove that limit. Use Pencil without painting plus Tiled for a
  full-resolution UI check, or render/export at the actual intended resolution through the API.
- Poisson-only changes can reuse the pre-correction quilting cache. Source/search settings and
  resolution changes require a new search; synchronous readback/job completion can still block the editor.
- Quilting evaluates duplicate candidate strips only once, without changing the same-seed result or
  selected Search Quality; the benefit can be larger for wider strips. With Contrast Compensation, the first calculation
  after a long idle period or a resolution change can take longer than subsequent updates.

### Normal Map settings

Use `type:"normalMap"` and put generator settings inside `settings.normalMap`. Both `add` and
`set` accept partial updates. `describe` exposes `normalMapDefaults`; `inspect` returns every
generator setting under `settings.normalMap`. Regular layer settings, targets, groups, channelMapping,
clipping masks, duplication, conversion and raster export use the existing paths.

```json
{"op":"add","type":"normalMap","as":"normal","settings":{"normalMap":{
  "mode":"Texture","strength":6,"smoothing":1,"mediumRadius":4,"largeRadius":32,
  "fineDetail":1.5,"mediumDetail":1,"largeDetail":0.5,"lightRemoval":0.75,
  "edges":"Repeat","encoding":"Gamma"
}}}
{"op":"target","layer":"@normal","input":"Specific","target":"@art"}
```

| Key | Values / limits |
|---|---|
| `mode` | `HeightMap` (default), `Texture` |
| `sourceChannel` | `Luminance` (default), `Red`, `Green`, `Blue`, `Alpha`, `Maximum` |
| `inputSpace` | `Gamma` (default; encoded/display RGB), `Linear` (working values) |
| `strength` | 0..128, default 4 |
| `blackLevel`, `whiteLevel` | 0..1 / 0.0001..16; defaults 0/1; white must exceed black |
| `gamma` | 0.05..8, default 1 |
| `smoothing` | 0..64 full-resolution pixels, default 1 |
| `mediumRadius`, `largeRadius` | 0.5..128 / 0.5..512 pixels; defaults 4/32; large must be at least medium |
| `fineDetail`, `mediumDetail`, `largeDetail` | 0..8; defaults 1/1/0.5; Texture mode only |
| `lightRemoval` | 0..1, default 0.75; Texture mode only |
| `edges` | `Clamp` (default), `Repeat`, `Mirror`; independent of Transform tiling |
| `derivative` | `Sobel` (default), `Scharr`, `CentralDifference` |
| `inverted`, `flipX`, `flipY` | Boolean, default false |
| `ignoreTransparent` | Boolean, default true; alpha-normalized smoothing, except when Alpha is height |
| `alphaMode` | `Opaque` (default), `Source` |
| `output` | `Normal` (default), `Height` (reconstructed height for tuning) |
| `encoding` | `Gamma` (default; PNG/TGA/PSD/display), `Linear` (raw linear EXR/Texture2D data) |

Normals are tangent-space vectors packed into 0..1 RGB. Alpha is coverage, not a packed X channel.
Positive height gradients tilt the normal toward negative X/Y; flips reverse each respective axis.
Texture mode uses differences between smoothed height bands, not geometry or material recognition.
Its Light Removal attenuates the broad band and may remove real relief too.

A group source is rendered against transparency with its own descendants, opacity, channelMapping and
clipping, without the external backdrop. Outline and Alpha-source SDF use group coverage;
SDF with Red/Green/Blue/Luminance uses the group's color result.
Like other effect layers, Normal Map processes hidden sources: `enabled:false` hides a layer's
own contribution, not its availability to Previous/Specific consumers. Hidden groups still
respect their children's visibility. This also applies to chains of hidden effect layers.
Opacity and clipping semantics are unchanged; missing and cyclic targets remain invalid.

Keep the resulting normal layer at full opacity with Normal blend, identity channelMapping and no color
FX when exporting a normal texture. Color blending does not renormalize normals. Transform moves
the output image without rotating its vectors. Gamma compensates for the document's LDR
gamma encoding; Linear is the appropriate choice for raw linear output, not ordinary PNG export.
Import exported packed PNG/TGA as Normal Map with grayscale conversion disabled; see the
[Unity normal-map import reference](https://docs.unity.cn/6000.1/Documentation/Manual/texture-type-normal-map.html).
The API does not change source or exported texture import settings automatically.

### Transforms and coordinates

Transform patches support `position:[x,y]`, `scale:[x,y]`, `pivot:[u,v]`, `rotation`, `tiling`,
`reset:true`, `originalAspect:true`.

- Position is in canvas pixels: positive X goes right, positive Y goes up.
- Rotation is counterclockwise degrees.
- Pivot is bottom-left UV: `[0.5,0.5]` is the center.
- Scale `[1,1]` means the full canvas-sized source rectangle; negative values mirror, zero is rejected.
- Tiling is `Source`, `Clip`, `Repeat`, `Mirror`, `Clamp` or `Unbounded`. New layers, omitted JSON and Reset store `Source`. `LayerBehaviour.SourceTiling` selects the intrinsic policy, never a preview/intermediate/cache texture's wrap metadata: Shape/Vector/Boolean Group/Noise/Gradient/Color Fill use `Unbounded`; Drawing/Text/ordinary groups/image effects use `Clip`; File uses its bound source texture's U/V wrap modes. Explicit modes override this policy and survive saving and Shape/Text-to-Vector conversion. `Inspect` exposes stored `transform.tiling` and resolved `effectiveTiling` separately; File also exposes `sourceWrap` U/V. `Clamp` extends edge pixels. `Unbounded` continues procedural/geometric sources; raster layers use Clip. Explicit Clip crops geometry, stroke and Feather at the local unit frame.
- `reset` is applied before the other fields, Original Aspect after them.
- Changing pivot through this API uses raw TRS semantics; it does not compensate position. In matrix mode it changes only the pivot.
- `matrix:[m00,m01,m02,m10,m11,m12,m20,m21,m22]` sets a double-precision projective transform from source UV to canvas UV; divide the first two output coordinates by the third. It must be invertible with no horizon crossing the unit source rectangle.
- Do not combine `matrix` with `position`, `scale`, `rotation` or `originalAspect:true` in the same patch. `originalAspect:false` is a no-op; pivot and tiling are independent.
- Scalar edits on an existing matrix preserve its distortion. Rotation/Scale describe the local axes at the pivot; Reset returns TRS. Original Aspect requires TRS.
- Position, scale, pivot and matrix components are finite and within ±1e15; scale components must have absolute value at least 0.00001. Rotation is within ±360000 degrees.
- Brush footprints are defined in canvas pixels and inverse-compensated when written to a transformed Drawing source.

### Drawing strokes

```json
{
  "op":"stroke", "layer":"@ink", "space":"canvasPixels", "erase":false,
  "brush":{"color":[1,0.2,0.1,1], "size":12, "hardness":0.8, "spacing":0.16},
  "points":[[100,100],[180,140],[240,110]]
}
```

Each stroke has 1..4096 points: one point is a dab, multiple points form a polyline. Use sparse points
for straight segments; the brush interpolates stamps. Curves can be sampled as a polyline.
The brush uses the same renderer and source-over alpha as manual painting. `erase:true` uses the eraser.
No layer selection, canvas-area selection or RGBA Preview mask is inherited from the window:
specify the desired RGBA explicitly. Rectangle/lasso coverage and the internal pixel clipboard
are temporary window tools, not serialized document data or API stroke parameters.
Polygonal Lasso's Straight/Smooth choice and per-point Corner flags are also window
tool state. Smooth uses closed centripetal Catmull–Rom interpolation (alpha 0.5);
corner points use separate one-sided tangents. A shared cached polyline, flattened
to a 1/16-canvas-pixel tolerance, drives the boundary preview, curve insertion hit
testing and final even-odd selection rasterization. During editing, the final closing
segment is hidden and excluded from insertion hit testing. Completion uses the full
closed contour without changing the visible curves. It supports 256 placed nodes
and at most 16,384 rasterized contour points, reporting excessive detail before
applying a selection. The existing Aliased/Antialiased and combine/wrap rules apply.

For pixel-aligned pencil strokes, add `"pencil":"Circle"`, `"Square"`, or `"Diamond"` to the
`stroke` operation (not inside `brush`). Omit it for the regular soft brush. Pencil uses `brush.size`
rounded to whole pixels (1–4096), a hard edge and contiguous pixel steps; hardness and spacing do not
affect pencil strokes. Colors, HDR, `erase`, layer transforms, symmetry and repeat clipping still apply.
The pencil choice is per operation and does not change the window's selected tool.

```json
{"op":"stroke","layer":"@ink","pencil":"Square","brush":{"size":3,"color":[1,0,0,1]},"points":[[8,8],[24,16]]}
```
Color alpha zero leaves no mark, including for the eraser; eraser strength otherwise follows alpha.

- `space:"canvasPixels"` (default): top-left origin, X right, Y down. The API inverts the layer's
  transform to place ink under that canvas position. This mode requires Clip tiling.
- `space:"layerUv"`: bottom-left origin in the untransformed source tile; `[0,0]` bottom-left,
  `[1,1]` top-right. Useful for repeating transforms, where multiple visible copies share one source.
- Brush size is a diameter in canvas pixels. The footprint is inverse-compensated when written
  into a transformed Drawing source; a nonuniform layer scale does not stretch the visible brush.
- `brush` is optional; supplied fields update the layer's saved brush settings. Missing fields retain
  their current values, including symmetry/repeat. Set `repeat:"None"`
  explicitly when a one-off unmirrored stroke is intended.
- API brush parameters remain independent of the window's shared interactive brush/color/fill
  preferences. API strokes do not read or change those preferences. Symmetry/repeat and transforms
  are layer-local and are shared by API and interactive painting.
- Optional `pressures` supplies one 0..1 number per point, with the same length as `points`.
  Omitted values mean full pressure. Stamp pressure is linearly interpolated along segments;
  the global tablet curve and interactive smoothing do not apply. Not supported by Pencil.
- Each paint/erase operation is one committed stroke. A selection clips the assembled change
  once, preserving its soft edge across overlapping stamps; separate operations can build coverage.
  The interactive brush and API share [stroke assembly, blend and HDR semantics](AI/BRUSHES.md#stroke-assembly-and-selection).

| Brush field | Values |
|---|---|
| `color` | RGBA array: encoded RGB -107..107, alpha 0..1; Standard clamps the painting color |
| `size` | 1..4096 |
| `hardness` | 0..1 |
| `spacing` | 0.01..4, fraction of current diameter after Pressure Size (0.16 = 16%); minimum 0.5 canvas pixels. Size Jitter does not change spacing |
| `opacity` | 0..1, default 1; caps the complete stroke, not individual stamps |
| `flow` | 0..1, default 1; multiplies each stamp's alpha before accumulation |
| `pressure` | Boolean, default true; master switch for Size/Opacity/Flow pressure bindings. Off bypasses their curves. Mouse input is 1; API uses 1 or explicit `pressures` |
| `pressureSize`, `pressureOpacity`, `pressureFlow` | Objects with optional `enabled` and `curve`; defaults: Size/Opacity off, Flow on, linear curves. Size scales diameter; Flow scales stamp alpha with accumulation; Opacity caps local coverage without fading earlier paint. Curve string syntax: `linear`, `easeIn`, `easeOut`, `easeInOut`, `one`, or shared `keys(...)`; at least two keys, times/values in 0..1, clamped output |
| `writeChannels` | Integer 0..15, default 15. Add enabled bits R=1, G=2, B=4, A=8. Disabled straight channels retain previous values; 0 writes nothing. Disabling A preserves alpha and fully transparent pixels, including when erasing. Also applies to Pencil strokes |
| `scatter` | 0..4, default 0; random disk radius in brush diameters |
| `scatterBias` | −1..1, default 0 (UI −100..100). Negative concentrates centers near the stroke; positive near the scatter disk edge. With uniform sample `u`, normalized radius is `u^(0.5 * 2^(-4 * scatterBias))`; zero preserves `sqrt(u)`, uniform by area. Applies to Random and Sobol without consuming extra random values; ignored when scatter is zero |
| `sizeJitter` | 0..1, default 0; size multiplier sampled from 1−jitter to 1+jitter after pressure scaling. Zero-size stamps do not paint |
| `rotationMode` | `Fixed` (default) or `StrokeDirection`. For texture tips, adds the source-pixel-space segment angle before jitter; +X is zero, +Y is +90°. Stationary points retain the last direction; a new stroke starts at zero. Scatter does not affect direction. Ignored without a tip texture |
| `angleOffset` | −180..180 counterclockwise degrees, default 0; constant offset added to `rotationMode` before `angleJitter`. Ignored without a tip texture |
| `angleJitter` | 0..180 degrees, default 0; offset sampled from −jitter to +jitter per stamp, added after `rotationMode` and `angleOffset`. Ignored without a tip texture |
| `flipX`, `flipY` | 0..1, default 0; per-stamp horizontal/vertical reflection probabilities in tip-local axes, before rotation. 0 never flips, 1 always flips; intermediate probabilities sample each axis separately. Ignored without a texture tip. Symmetry copies share the chosen flips |
| `randomAlgorithm` | `Random` (default) or `Sobol`; controls scatter, size, angle, tint and flip sampling. Sobol uses seven fixed dimensions (scatter angle/radius, size, angle, tint, flip X/Y) and a seeded digital shift; enabling tint or flips does not perturb scatter. The sample index continues across stroke segments, including clipped stamps, and resets per stroke; symmetry copies share a sample |
| `tintGradient` | 1..64 ordered `{time, color}` stops, like a Gradient layer; default opaque white. Differing RGB or alpha keys trigger random sampling per stamp; identical keys give a constant multiplier without consuming random samples. Reset by supplying two opaque-white stops |
| `tip` | Imported Texture2D asset path, or null for a procedural brush. The document's own output is rejected. Does not change texture import settings |
| `tipChannel` | `Alpha` (default), `Luminance`, `InvertedLuminance`, `Color`. Ordinary tips use alpha for coverage, optionally multiplied by luminance/inverted luminance. Color also multiplies painting RGB by the tip RGB |
| `proceduralMode` | `Hardness` (default) or `SdfGradient`. Used only when `tip` is null; independent of textured `tipSdf`. Procedural gradient coordinate is `radius`, where radius is distance from stamp center divided by half the brush Size: center 0, edge 1. Pixels outside the circular tip are discarded. Uses the shared `tipGradient`, including its RGB and alpha; Pencil ignores this mode |
| `tipSdf` | Boolean, default false. For a textured brush, sample `tipGradient` at `1 − selected field`. Alpha and Color use alpha as the field, without multiplying the original alpha again; luminance modes use brightness then multiply coverage by original alpha. Higher field values are inside, corresponding to lower gradient coordinates. Ignored without a texture tip |
| `tipGradient` | 1..64 ordered `{time, color}` stops. Default white with alpha 1 at 0.4 and alpha 0 at 0.6. Shared by procedural SdfGradient and textured SDF modes. Coordinates run from interior 0 to outer edge 1: RGB multiplies brush/tint RGB, alpha supplies coverage. Replaces `tipThreshold`; hardness no longer affects SDF. Inspect returns `tipGradientKeys` (separate colors/alphas). A cached 1024×2 linear RGBAHalf premultiplied LUT, clamped and mip-filtered, approximates the gradient on GPU. Editor Standard input mode removes key intensity without changing stored values; API strokes always use supplied values |
| `blend` | Layer BlendMode names except `Overwrite` and `None`; default `Normal`. Application is controlled by `blendApplication`; ignored for erase |
| `blendApplication` | `Stroke` (default) or `Stamp`. Stroke blends accumulated source-over stamps against the pre-stroke layer. Stamp blends each stamp against the evolving layer, so earlier stamps participate. Flow affects each stamp; Opacity interpolates the pre-stroke and fully accumulated results once in premultiplied linear space, without feeding that interpolation into subsequent stamps. Normal and erase retain the existing equivalent fast paths. Symmetry stamps are applied in their generated order; periodic copies of one stamp share its backdrop |
| `seed` | Integer 1..2147483647, default 1; repeatable random sequence, restarted per stroke |
| `mirrorX`, `mirrorY` | Vertical/horizontal axis reflection respectively; used only with `repeat:"Mirror"` |
| `center` | Bottom-left UV, each component 0..1 |
| `repeat` | `None`, `Mirror`, `Horizontal`, `Vertical`, `Grid`, `Radial` (mutually exclusive) |
| `repeatCount`, `repeatSecondaryCount` | Integers 2..64; secondary is the grid Y count |
| `radialStartAngle` | 0..360 degrees counterclockwise from the left; Radial only, default 0 preserves the original layout |
| `mirrorAngle` | 0..360 degrees counterclockwise around `center`; Mirror only. Rotates both axes in source-pixel space. Default 0 keeps the original vertical/horizontal axes |
| `elements` | `Copy`, `AlternateMirror` |
| `boundary` | `Continue`, `Clip` |

For simple symmetry, set `repeat:"Mirror"` and at least one of `mirrorX`/`mirrorY` to true.

Stamp spacing carries across pointer segments; adding more points on the same straight path
does not add more stamps. Random variations are shared by symmetry copies of each stamp.
Each logical stamp sets the next interval from its pressure-sized diameter. The remaining
distance carries across input events, including events that emit no stamp. The endpoint
does not force an extra stamp. Pressure Size off and constant pressure use uniform spacing.
Opacity applies once per API stroke; separate strokes can build past that limit.
Texture tips preserve their aspect ratio; size is the longest side. Hardness controls procedural brushes in Hardness mode only;
100% Hardness retains a pixel-sized antialiased edge, while wider soft falloff keeps its smoothstep profile.
SDF uses its gradient. SDF fields use the normalized 0–1 range. Luminance is measured
in source-encoded RGB for sRGB textures, preserving the threshold between Gamma and Linear projects.
Pencil ignores all advanced dynamics and texture-tip settings.
Both `tipGradient` and `tintGradient` accept a stop array or the shared gradient object with `colors`,
optional `alphas`, `colorMode`, `interpolation`, `huePath`, `chromaCorrection`, `wrapMode`, `smoothness`, `colorSpace`, `customKeys` and `customColorMode`. Supplying either field replaces
that entire gradient; omitting it preserves the existing gradient. For supplied gradients without
`colorMode`, both `tipGradient` and `tintGradient` default to **Perceptual**, matching brush
clipboard import. Explicit modes are preserved. This applies to `settings.brush` and stroke `brush`.
Inspect returns `tipGradientKeys` / `tintGradientKeys`, including separate color/alpha tracks and
interpolation settings; write their gradient bodies using `tipGradient` / `tintGradient` respectively.

For example, `"brush":{"size":40,"spacing":0.8,"opacity":0.6,"flow":0.2,"scatter":0.5,"sizeJitter":0.3,"seed":123}`
creates repeatable scattered stamps with a 60% stroke-opacity cap.
Mirror axis choices are retained but ignored in other modes. `center` affects Mirror and Radial;
`elements` applies only to Horizontal, Vertical, Grid and Radial. `boundary` also applies to Mirror.
For reflected radial sectors, use `repeat:"Radial", elements:"AlternateMirror"`.
Old symmetry settings are not migrated; use the current mode and fields above.

Clip anchors to the stroke's first cell/sector and terminates the polyline at its first exit.
In Mirror mode it anchors to the starting side of each enabled, rotated axis; each
reflected brush footprint is clipped to its own half-plane or quadrant. Continue
allows crossing axes and overlapping brush footprints. Mirror without enabled axes is unrestricted.
Start a new stroke to draw in another segment. Continue permits crossing segment boundaries.
The validator caps estimated replicated stamps at 100,000 per stroke, covered brush pixels at
250,000,000 per stroke and source UV at -4..5. Documents support up to 1024 layers and
67,108,864 total owned Drawing pixels through the API.

### Color Fill patterns

Use `settings.fillMode:"Pattern"` and partial `settings.fillPattern` updates.
`describe` returns `fillModes` and `fillPatternDefaults`; `inspect` includes the full
pattern. Unified document/clipboard JSON uses `behaviour.mode` and `behaviour.pattern`
on `ColorFillLayerBehaviour`; use the document schema for their native field shapes.

| Field | Values |
|---|---|
| `shape` | Triangles, Squares, Hexagons (default), Circles |
| `circleLayout` | Square, Dense (default) |
| `size` | `[x,y]`, each 1..16384 px, default `[64,64]`; grid scale on each axis before transforms. A scalar sets both axes |
| `linkSize` | Default `true`; inspector edits scale both axes proportionally. Explicit API sizes are applied as given |
| `rotation`, `offset` | ±360000 degrees; [x,y] in pixels, each ±1000000 |
| `seamless` | false by default; fit complete rectangular periods to canvas |
| `gap`, `roundness`, `bulge` | 0..0.99, 0..1, 0..1; all default zero |
| `distanceRange` | .001..16 in figure inradii, default 1 |
| `position` | Outside, Inside, Center, Signed (default) |
| `inverted` | Boolean, default false |
| `profile` | FX curve string syntax, at most 4096 characters; default linear |
| `gradient` | Standard gradient stops/object; default white to black |
| `cellColor` | Uniform (default), Random, Pattern |
| `colorBlend` | Multiply (default), ReplaceRGB; both preserve SDF alpha |
| `palette` | Standard gradient stops/object, default white to black; RGB only. Fixed mode provides discrete colors |
| `seed` | Signed 32-bit integer, default 0; Random only |
| `variation` | 0..1, default 1; Random palette sampling from midpoint (0) to full range (1) |

Signed distance is negative inside and positive in gaps; zero maps to the middle
of the gradient. Bulge remaps only the interior, so nonzero Bulge is not an exact distance.
Seamless snaps combined layer/group rotation to quarter turns and fits each axis.
Shear/perspective become an axis-aligned approximation at canvas center; layer tiling
is bypassed. Stored settings stay unchanged. Polygons may stretch; circles remain circular
with extra space on one axis. Later FX can break periodicity.

Cell colors use the nearest contour, including gaps, and follow the procedural coordinates.
Pattern uses palette positions 0/1 for checkerboards and triangle orientations, 0/.5/1 for
hexagons and dense circles. Seamless also wraps Random cell IDs and fits Pattern color periods:
even X/Y counts for square grids, X multiples of three for hexagons/dense circles;
staggered grids retain complete row pairs. Changing palette colors does not change the geometry.

## Validation, Undo and recovery


- Every batch is preflighted on a detached model. `dryRun` does not prove GPU availability,
  successful image decoding, successful new HLSL compilation or writable disk space.
- **TIFF Batch:** opens an independent model, applies operations, optionally saves, then disposes
  the model. `save:false` returns a snapshot but discards the edits. It cannot edit or save the
  user's open document. Saving uses an atomic TIFF replacement, but import and other I/O are not
  one filesystem transaction. An error can occur after the file has committed.
- **Headless Live:** retains a candidate between requests until completion/cancellation or reload;
  see [its lifecycle](#headless-session-lifecycle-and-retries). It does not provide user Undo.
- **Assistant:** edits the open document with Undo, never autosaves. After Undo/Redo, save through
  the window if persistence is wanted. An empty TIFF Batch does not save unsaved Assistant edits.
- Internal rollback of a failed Batch is not a user Undo contract for the written TIFF.
- A revision hashes serialized model state, materialized Drawing pixels and FX state;
  it is not a complete
  guarantee against changes to every external texture or include file.
  It is an opaque optimistic-concurrency token, not a portable version-control ID. Re-inspect after
  Undo, save, import or domain reload. Do not cache it across sessions.
- On `revision_conflict`, inspect and reconsider the patch. On `already_exists`, inspect/reuse the
  asset or choose a new path; do not delete it to make the request succeed.
- On a lost response/timeout, the command may already have executed. Inspect layer IDs/names and
  render before deciding what remains. Do not automatically retry additions/strokes.
- On `saveMayBePartial:true`, Batch reached the save stage, but its temporary model will be discarded.
  Inspect the on-disk document, `status` and staged recovery files first. The old TIFF may still be
  present, or the new TIFF may already have committed. An empty batch cannot recover discarded edits.
  Reconstruct only changes confirmed missing, using a fresh revision; never blindly replay additions
  or strokes. Recover a valid staged TIFF into a new destination when appropriate.
- If an import fails after copying, the response reports `fileCreated:true` and the retained path.
- If reverting a failed edit also fails, `rollbackFailed:true` reports that explicitly. Stop and inspect;
  neither the old state nor a fully applied batch can be assumed.

Destinations stay under Assets (not StreamingAssets); path traversal and write-through symlinks are
rejected. Preview PNGs are limited to `Temp/WhimTex/` and do not overwrite unless requested.
Import accepts at most 64 MiB and 16,777,216 source pixels. Resource limits protect the editor from
accidental huge requests, but effect-heavy documents can still take time: use preview resolution
appropriately and keep batches focused. The API executes on the main thread; it is not an async job queue.

## Verification

Select `agent-api-v2` through the [independent test runner](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/RUNNING_TESTS.md), with a reviewed fingerprint and explicit intended project.
It uses only GUID-owned fixtures under `Assets/WhimTexTestMigration` and cleans up its own fixtures and previews in
`finally`; it does not edit existing documents. It verifies TIFF create/inspect, aspect/transform,
preflight rejection, revision conflicts, painting, transient `save:false` isolation and save/reopen.
`agent-editing-v2` covers the shared editing operations and Assistant Undo separately.
Do not run it when the project's rules prohibit creating test assets.

`drawing-pattern-smoke-v2` is a separate opt-in structured regression
test for mutually exclusive Mirror/Repeat modes, file-baseline normalization, movable mirror centers,
source stamps under the cursor and JSON round-trips. It creates no assets or GPU resources; run only
after the updated plugin is compiled. Acknowledge each selected scenario's declared effects;
this does not grant asset or user-state authority. It does not replace visual painting checks.

### Typed numeric shader parameters

The field syntax is `toggle(type)`, `color(type)`, `enum(type)` or `seed(type)`; defaults are float, float4, float and int. Toggle/color accept bool/int/uint/half/float scalars and vectors2/3/4; enum accepts numeric scalars; seed accepts int/uint/float. `rgba` changes component labels only. Use `rgba toggle(float4)` for channel switches and explicit vector components in conditions. `gradient`, `curve` and `transform2D` are unchanged. API parameter values use JSON booleans for bool, numbers for other scalar types and arrays of the exact component count for vectors. Integer endpoints remain exact. FX reads expose `valueType` and each control’s `field`, `valueType` and `componentLabels`. Float/half color values retain encoded-color semantics; integer/bool colors are raw numeric values, without 0–255 scaling. Seed values must be integral; float seeds stay within ±16777216. See [the shader parameter contract](ShaderFX.md#field-presentation-and-numeric-type).
