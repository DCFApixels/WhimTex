---
title: "AI brush authoring: JSON and HLSL"
nav_order: 6
lang: en
permalink: /ai-brushes/
description: "Generate Standard brushes with optional image tips and Static or Dynamic HLSL tips for WhimTex, then paste JSON into the editor."
---

# WhimTex brush authoring

Use this contract when the user requests a **brush**, not a texture composition.
Return one complete JSON block with `format: "whimtex.brush"`. The user copies it and
pastes with Ctrl+V in an active WhimTex window, outside text fields. This replaces the
current brush; it does not create layers, resize the canvas or change palette colors.
Painting still requires a Drawing layer. Save As in Brushes saves the result as a brush preset.

## Start with an example

Ready-to-copy files are in **Documentation~/Examples/Brushes/**:

- [Standard scattered brush](../Examples/Brushes/standard-scatter.json)
- [Texture brush from a direct image URL](../Examples/Brushes/texture-url.json)
- [HLSL ring brush](../Examples/Brushes/hlsl-ring.json)
- [HLSL sparkle brush](../Examples/Brushes/hlsl-sparkle.json)
- [Dynamic HLSL brush](../Examples/Brushes/hlsl-dynamic.json)

Validate the structure against [brush.schema.json](brush.schema.json).
These are **not** `whimtex.document` layer recipes or Unity serialized settings.

## Common mistakes

- Use exact source names: **Standard**, **HLSL**. Texture is not a separate source.
- Standard optionally accepts a plain HTTP(S) URL returning PNG/JPEG, not a Markdown link or a webpage.
  No local asset paths, base64 or image-generation API requests in JSON.
- HLSL uses `BrushTip`, not `ApplyFX`, `SampleInput` or ShaderLab. Its signature selects Static or Dynamic evaluation.
- [FastNoiseLite](README.md#built-in-noise-library) is already available in both modes without an include.
  Prefer it for spatial noise when suitable; custom algorithms remain valid. Use `BrushRandom` for indexed stroke/stamp randomness.
- Begin code with `// @whimtex-brush Category/Name` on **line 1**.
- Parameter declarations include a type: `// @param float _Width = 0.1 [0 .. 1]`.
  No semicolon; one-sided bounds use `[0 ..]` or `[.. 1]`.
- JSON units are fractions: opacity 0.5 = 50%, spacing 0.16 = 16% of the brush diameter.
- Static HLSL does not run while painting. Dynamic HLSL receives the documented stroke context;
  neither mode provides background sampling, tablet pressure or live time.
- Use `tipChannel: "Color"` to retain RGB from a colored tip.
  Alpha is the default, so RGB alone does not color the brush.
- Only copy one JSON object (optionally inside one json code fence), without surrounding prose.

## Full JSON specification

Required root fields: `format: "whimtex.brush"`, `version: 1`, `source`.
Optional `name` is descriptive metadata, not a saved preset name. Optional `settings`
starts from fresh brush defaults; omitted properties do not inherit the previous brush.
Unknown fields and duplicate JSON keys are rejected. Maximum JSON size: **1 MiB UTF-8**.

Source-specific root fields:

| Source | Fields |
| :--- | :--- |
| Standard | Without `url`, a round procedural tip. Optional `url`: direct HTTP(S) PNG/JPEG for a texture tip. No code or resolution. Download requires confirmation; max 64 MB and 16 megapixels. Native image resolution and aspect ratio are retained. |
| HLSL | Required `code`; optional integer `resolution` 32..2048, default 512. Resolution is used only by Static tips; Dynamic tips evaluate directly while painting. No url or evaluation metadata. |

`settings` fields:

| Field | Values / default |
| :--- | :--- |
| size | 1..4096 canvas pixels; 32 |
| hardness | 0..1; 0.8; Standard without a texture only. Round tips retain a pixel-sized antialiased edge at 1; subpixel dabs fade with their area. |
| spacing | 0.01..4 current brush diameters after Pressure Size; 0.16; minimum 0.5 canvas pixels. Size Jitter does not change spacing. |
| opacity, flow | 0..1; 1 |
| pressure | boolean; true. Master switch for the three pressure bindings. Off bypasses their curves. Mouse input is 1; API strokes default to 1 or use explicit `pressures`. |
| pressureSize, pressureOpacity, pressureFlow | Objects with optional `enabled` and `curve`. Defaults: Size/Opacity disabled, Flow enabled; all curves linear. Size multiplies diameter; Flow multiplies stamp alpha and accumulates; Opacity caps local stroke coverage without reducing previously painted coverage. |
| writeChannels | integer 0..15; 15. Add enabled bits R=1, G=2, B=4, A=8. Disabled channels retain previous values; 0 writes nothing. Disabling A preserves alpha and fully transparent pixels, including when erasing. |
| scatter | 0..4; 0 |
| scatterBias | −1..1; 0 |
| sizeJitter | 0..1; 0 |
| angleJitter | 0..180 degrees; 0 |
| angleOffset | −180..180 degrees; 0 |
| flipX, flipY | 0..1 probability; 0 |
| rotationMode | Fixed / StrokeDirection; Fixed |
| randomAlgorithm | Random / Sobol; Random |
| seed | integer 1..2147483647; 1 |
| tipChannel | Alpha / Luminance / InvertedLuminance / Color; Alpha |
| tipSdf | boolean; false |
| mode | Hardness / Gradient; Hardness; Standard without a texture only |
| tipGradient, tintGradient | [Gradient value format](README.md#standalone-gradient-json), with brush-specific limits below; also fully described by brush.schema.json |
| blend | Color blend names from the schema, default Normal; None and Overwrite are not allowed |
| blendApplication | Stroke / Stamp; Stroke |

Both gradient fields accept a stop array or an object with `colors`, optional `alphas`, `mode`,
`wrapMode`, `smoothness` and `colorSpace`. If `mode` is omitted, both `tipGradient` and
`tintGradient` use **Perceptual**, also the default for new brushes; explicit modes are retained. Color-picker RGB/HSV, HDR input,
Channels and Preview EV preferences do not transform pasted values, and pasting does not populate
document History. RGB limits remain -107..107 and alpha 0..1, not the wider standalone gradient range.

Pressure `curve` uses the shared string syntax: `linear`, `easeIn`, `easeOut`, `easeInOut`,
`one`, or `keys((time,value,inTangent,outTangent,inWeight,outWeight,weightedMode), ...)`.
Provide at least two keys with times and values within 0–1; weightedMode is 0..3.
Curve output is clamped to 0–1; a mouse still uses the value at the right endpoint.
Bindings and curves are included in presets and layer brush settings. The global tablet curve
and pressure-smoothing preference are user input settings, not preset data or API filters.

Standard texture tips and HLSL tips share all channel, SDF/Gradient, rotation, flip and painting behavior.
HLSL changes how the tip is obtained. Changing its parameters does not reset
these settings. Use opaque grayscale RGB plus Luminance for a distance field; with SDF enabled,
the selected value is inverted before looking up tipGradient, as for a texture tip.

## Stroke assembly and selection

Flow and pressure control stamp accumulation. Stroke Blend applies to the isolated accumulated
paint; Stamp Blend evaluates each stamp against the accumulated backdrop. Brush Opacity applies
once to the stroke result. The canvas-space selection then clips the change from the immutable
source in premultiplied linear RGBA: `result = lerp(source, strokeResult, selection)`.
Selection is not multiplied into each stamp. Repeated dabs within one stroke therefore retain
a soft selection edge; separate committed strokes can still build coverage. Write RGBA protection
is applied to that assembled change, retaining locked channels and hidden source RGB.

Live assembly runs at the Drawing source resolution before its transform, layer/group opacity,
blend, clipping and FX. Canvas, Target inputs, previews and thumbnails use this same source.
Commit uses the same assembly function. Standard painting remains bounded; HDR keeps signed and
above-one RGB through accumulation and storage. Blur/Smudge retain their separate retouching paths.

The immutable `paintSurface` stays full-resolution RGBAHalf. Stroke accumulation uses logical
256 × 256 blocks packed into private RGBAHalf atlas pages: at most 1024 × 1024 (8 MiB,
16 blocks) per page, with 256/512 pages for small sources. Slots are allocated on first touch,
without growing/resampling a texture. An absent slot means no pending change, not a proof
that the source is empty. No alpha-based RGB deletion or approximate compression is used.
Ordinary pages start clear; sequential blend/pressure blocks copy their source region.
Stamp geometry retains global source/canvas coordinates and uses a per-slot viewport. Brush-tip
filtering is unchanged. Assembly samples texel centers with Point filtering; it never filters
between unrelated atlas slots. Source size, transforms, wrap, dynamics and selection coordinates
remain logical, not atlas coordinates.

Pages are exclusive to their active stroke. The shared Editor-process idle pool retains at most
64 MiB across all windows and page sizes. An active stroke may use more: an 8K dense stroke can
still need 512 MiB. Commit/cancel returns pages; excess pages are destroyed and the idle pool is
cleared on assembly reload/quit. Pool reuse, compute, preview and immutable Undo snapshot copies
remain ordered on the graphics queue. History never retains an atlas page or a mutable slot.
The `brush.stroke_page_bytes` and `brush.stroke_pool_bytes` profiler counters distinguish the
current stroke's page payload from retained idle payload.

Live rendering retains the existing source-resolution Half straight image. A separate set of
256 × 256 damage tiles accumulates since its last refresh; each update recomputes those tiles
from the immutable base plus the corresponding stroke slot. Previously converted pixels are
reused, never blended again. First use, a provisional-stroke reset or changed composite settings
requires a full base conversion followed by touched blocks. The target is released on
commit/cancel. At 8K this preview and the Half source still each occupy 512 MiB; their initial
allocation/conversion cost is not removed by stroke tiling. FX processing remains unchanged.
Input points still advance in order; segments producing no paint do not request another canvas
composition, and multiple pending requests remain coalesced.
Commit visits distinct 256 × 256 source blocks touched by stamp geometry, clipped to the stamp
bounds. A perimeter stroke does not include its empty center. On devices with a supported compute kernel and
RGBAHalf LoadStore access, it reads and replaces each source texel in place, without commit
scratch. Each invocation owns one pixel; accumulation and preview still use the raster path.
The compute kernel and raster shaders share `BrushComposite.cginc`, including signed HDR,
selection, opacity, blend and channel protection. Dispatch stays on the graphics queue.

Empty-content detection for an ordinary Drawing with unchanged alpha mapping keeps one uint
per 256 × 256 source block (4 KiB of metadata at 8192²). It initializes lazily from the Half
surface once; subsequent compute commits clear and recompute only touched blocks while assembling
the stroke. The test follows Half quantization and the existing straight-conversion/HDR cleanup,
so it describes rendered content rather than raw storage bits. Undo/Redo refresh restored blocks;
source replacement and retouching paths invalidate the map for a fresh initialization. The map
is transient, does not change pixels or history payloads, and has no full-size texture of its own.
Only this small buffer is read asynchronously. Pending or stale results are Unknown, never an
assumed empty layer. CPU journal-page synchronization does not invalidate the authoritative Half
image. Queries remain deferred throughout an active stroke, with no timed forced GPU wait.

With FX, alpha remapping or unsupported coverage compute, the badge uses finished layer output.
An already required full-resolution render can supply that output before clipping, without another
FX render. Reduced previews, transformed or interactive approximations cannot substitute for exact
intrinsic content; those cases retain the existing full-resolution fallback. Own layer Opacity and
visibility remain excluded, while FX and alpha mapping remain included.

Unsupported devices, unavailable kernels, failed UAV allocation and existing non-UAV paint
surfaces retain the raster path: one 256 × 256 RGBAHalf scratch block (0.5 MiB) for tracked strokes,
including partial edge tiles. Fallback is chosen before dispatch, never after a potentially
partial write. Both paths apply selection once. The scratch bound does not include the
full-resolution source/preview, stroke pages, stamp backdrops or downstream document rendering.

Interactive Brush/Pencil and API paint use a session pixel journal when readable, single-mip
RGBA32/RGBAHalf storage and the required texture copies/render format are supported. Unity Undo
records one small object per pixel operation; a shared per-user disk store holds each
block's before/after Half and straight-storage bytes. Ctrl+Z/Redo share the normal action order.
History survives script reload within the Editor session, but is not included in TIFF or restored
after restarting the Editor. Normal Editor exit removes only that process's session journal.
With Burst enabled, new pages use independent LZ4 blocks without byte shuffling,
with raw storage when packing would expand a block. The C#/Burst implementation uses portable
memory operations, a bounded 16 KiB match table and a checked decoder; no native library is required.
There is no color conversion or quantization in the codec. Without Burst, a sample of up to 64 KiB
selects Deflate for compressible pages and raw storage otherwise. The faster block format generally
uses more disk space than Deflate; the existing disk quota still applies. Payload checksums and
encoding headers are verified before restoring any block, then each page is decoded once into
reusable buffers. Existing pages in the current Editor session remain readable.

The stored GPU image is updated immediately. Immutable packed pages feed ordered asynchronous
readbacks into the existing CPU texture without a full-image `Apply`. Readbacks write directly
into owned native buffers, avoiding an intermediate full-page managed copy. Each page packs up to
sixteen blocks. Devices supporting 4096px textures pack them in a 256px-wide vertical strip,
so full 256px blocks are contiguous after readback and encode directly without gathering rows.
The strip height follows the block count, avoiding unused tail slots. Devices with smaller limits
retain up to four columns. This is only the immutable transfer layout; stroke tiles, pixels and
the on-disk block format are unchanged. Immutable GPU staging
has one shared burst budget across all documents/windows in this Editor: at most 1536 MiB,
further limited to one quarter of reported graphics memory and one eighth of reported system memory,
with a 256 MiB floor. Unknown memory sizes or unavailable async readback retain the 256 MiB budget.
This is a ceiling, not a preallocation; pages are released as they are consumed. It fits a full
8K snapshot (1280 MiB standard, 1536 MiB HDR) on sufficiently large devices. Only two pages at a time
perform native readback (at most 48 MiB). A completed FIFO head releases its readback slot before
CPU patching/writer admission, overlapping the next read with that work. At most one additional
ready head remains resident (20 MiB standard / 24 MiB HDR). Completed native planes transfer ownership to the writer;
they are not copied into separate managed arrays for every block. Stroke completion keeps the next stroke available without waiting
for readback or payload compression while staging fits that budget. GPU submission/allocation and a
small revision manifest still run on completion; this is not a zero-cost or zero-I/O operation.
Editor updates process the FIFO in approximately 4 ms slices, yielding between blocks, and never
wait for pending journal writes. A page reserves space under a 64–256 MiB pending-write budget before
CPU patching, charging the full resident planes including unused atlas slots; background compression/file writes release that space. GPU saturation, readback errors
and devices without asynchronous readback can still stall. Exceeding the staging budget waits for
the oldest page to finish, preserving exact revisions instead of dropping history. The same FIFO
captures each stored before-image before patching the after-image, including overlapping strokes.
The synchronous CPU copy validates all bounds, then captures and replaces each row in one Burst
kernel while its cache lines are hot. The portable non-Burst path performs the same raw-byte copies;
neither path converts colors or changes Half bits.
A shared pool retains at most 64 MiB of idle native planes for subsequent readbacks and stored
before-images. Active writer planes are charged to the pending-write budget; a page being patched
also owns its stored before-image (at most 8 MiB). No additional full-canvas CPU/GPU snapshot is introduced.
Compression and disk writes remain background Tasks. Their exclusive codec workspaces are reused
(at most four idle workspaces, approximately 6.1 MiB together). Contiguous native blocks are encoded
directly; partial-width blocks and square fallback pages gather rows native-to-native into reusable block scratch. A reusable managed
output buffer feeds the file API. Lossless bytes, raw fallback, checksums and the journal format are unchanged.
Idle pools are released after all consumers join at script reload/quit. Raw journal pages trade disk
space for lower latency.
The writer budget is `clamp(logicalProcessorCount / 2, 2, 8) × 32 MiB`; larger CPUs can encode
more independent pages concurrently without increasing the number of GPU readback pages.
This raises the maximum resident writer allowance by 128 MiB on sufficiently large CPUs;
it is demand-allocated and shared, not a new buffer per stroke. Exact page/revision identity and
FIFO CPU patching preserve Undo order even when workers finish out of order. Sustained input can
still exhaust the snapshot queue: more concurrency reduces writer waits, not GPU transfer volume.
New readback batches are submitted without waiting, and their request state is updated while the
Editor is idle. The last successful page writer converts its stroke's disk reservation to actual
file usage immediately, without waiting for idle maintenance. If disk reservation initially fails,
completion is drained and the quota is retried before any history is retired.
Disk errors retain affected payloads in RAM and report the failure; this exceptional retention can
exceed that budget. Further journaled strokes are refused until the failed history is cleared.
Undo, CPU pixel consumers, save, resource release and script reload drain pending work. API paint
returns after synchronization. Unchanged source pixels keep their original storage bytes, including
hidden RGB. Devices without asynchronous readback use bounded synchronous page reads. Unsupported
storage/copy configurations, Blur/Smudge/Healing and other native pixel operations retain their
previous storage/Undo path. The source and preview surfaces and document rendering remain full-size; only stroke accumulation uses sparse storage.

### Shared paint-history storage and retention

The disk home is `DCFApixels/WhimTex/PaintUndo` under Windows LocalApplicationData,
macOS `~/Library/Caches`, or Linux's absolute `XDG_CACHE_HOME` (otherwise `~/.cache`).
No administrator access or native platform library is required. All projects and Editor versions
use the same home. A process-owned GUID session contains separate step directories and per-drawing
checkpoints; multiple windows sharing one document share its history. This is disposable Undo,
not document recovery or a substitute for saving TIFF.

`limits.json` holds a global 8 GiB default quota and a 50-operation default per-document limit
(Brush/Pencil pixel Undo plus Redo). A stroke counts once regardless of how many texture blocks
it stores. Parameter-only edits remain subject to Unity Undo and do not consume this limit.
Other pixel tools still using native texture Undo are outside this journal's count.
Atomic per-field updates use a short cross-process file lock.
Other User Settings already use Unity's per-user [EditorPrefs](https://docs.unity3d.com/ScriptReference/EditorPrefs.html);
loaded preference caches and open controls refresh every two seconds. Project/workspace state is
not promoted to a global document setting.

Before modifying the Half source, commit reserves worst-case page bytes under the common quota,
including pending writes. It reclaims its own oldest steps when necessary; active foreign sessions
are never pruned. The store retains 512 MiB of free disk space and a shared 32 MiB compaction budget.
If reservation still fails, the stroke is not applied and reports a storage error. Clearing history
or increasing the limit can allow a retry; no stroke is silently committed without Undo.

Retiring an applied step first records its new boundary, then streams exact compressed after-Half
and after-storage blocks into a checkpoint. Pending source pages remain readable until replacement
is complete. This preserves low-alpha precision after clearing history and rebuilding a surface;
checkpoints count toward disk usage. Retention clears only the retired operation's native Undo
object, leaving parameter changes intact even inside the same native group. Ordinary Undo/Redo
therefore has no empty retired-stroke steps. Invalid stale cursors are still rejected atomically.
A new branch deletes its own
discarded future states. Idle maintenance enforces the operation count, trims excess usage and
reclaims histories whose texture no longer exists.

Routine checkpoint validation, decoding, compression and file publication run one page at a time
on a worker. Original source pages remain readable until the main thread retires them; Half
rebuilds prefer pending source tiles over checkpoint files being replaced. Editor maintenance
uses a 4 ms scheduling budget between work units (not a hard real-time I/O guarantee) and does not
wait for the worker. One reusable set of four tile arrays retains at most 2 MiB for checkpoint
decoding, avoiding repeated large allocations during pruning. Loaded revision chains and session
paths are reused. Disk accounting adjusts changed allocations instead of rescanning the entire
session per file. Explicit clear, quota exhaustion and assembly shutdown may synchronize pending
checkpoint work. Clearing and compaction preserve exact pixels; they do not replay brush commands.

Reducing the quota below existing usage schedules reclamation; it cannot erase precise current
images or another running Editor's history. Bounded compaction may temporarily exceed the new
limit. If checkpoints and other active sessions alone exceed it, usage remains visible and new
reservations fail until space is available. **Clear Paint Undo…** confirms removal of this Editor's
paint history and abandoned sessions; it retains current-image checkpoints and other native Undo.

Session manifests identify the machine, process ID and process start time. Cleanup at startup and
periodically while idle removes only recognized, proven-dead sessions, including reused process
IDs; access failures and unknown envelopes are preserved. Domain reload keeps the same process
session. File operations reject symbolic links and paths outside the owned session. Limits and
other preferences survive cleanup. Older package revisions' project-local journals are outside
this store and quota; they are not swept as unowned directories.

Sequential Stamp blending and pressure Opacity copy only the current stamp's padded backdrop
region, remapping sampling coordinates to that scratch texture. Capacity grows in 128-pixel
buckets within a draw batch. Large, wrapped or projectively expanded stamps can still require a
large region. Region copies use `Graphics.CopyTexture` where supported and a shader-copy fallback
otherwise. Neither route changes blend arithmetic, source resolution or the selection contract.

## Full HLSL specification

Optional `// @header(Shape)` adds a bold, non-collapsible heading, while `// @helpbox(Your hint text.)` adds an informational help box above the next parameter. These are UI metadata, not uniforms, and are preserved when saving a preset. Headings and help boxes without a following parameter declaration are ignored. Former-name aliases are not supported; declaration edits preserve matching values/identity using the same rules as FX.

Define exactly one entry point:

```hlsl
// Static: baked into a square texture on Apply.
float4 BrushTip(float2 uv)

// Dynamic: evaluated for covered pixels of each stamp while painting.
float4 BrushTip(float2 uv, BrushSequence sequence)
float4 BrushTip(BrushMotion motion, float2 uv)
float4 BrushTip(float2 uv, BrushMotion motion, BrushSequence sequence)
```

The actual function definition selects the mode; no metadata flag is needed.
Comments, helper arguments and forward declarations do not select it. Defining both
entry points is an error. Require exactly one `float2`, with each optional context at most once,
in any order and with any parameter names. `in` is accepted; `out`/`inout`, unknown types and duplicates
are rejected. Adding either context makes the tip Dynamic; only `BrushMotion` declares a movement
dependency. The argument order is resolved on Apply, not while painting.
UV is 0..1 across the square tip, with Y increasing upward.
Return straight (not premultiplied) RGBA. Alpha is clamped to 0..1.
RGB can carry HDR values and is treated as linear data. Color mode multiplies this RGB
by the painting color and randomized Tint; channel and SDF settings can reinterpret it.
Use a white palette to see unmodified RGB.

### Dynamic context and randomization

`BrushSequence` supplies progression and stateless randomization:

| Field | Type | Meaning |
| :--- | :--- | :--- |
| seed | uint | Stable root seed of this brush sequence, from settings.seed. Not replaced on each click. |
| strokeIndex | uint | Zero-based stroke index; advances on the next painted stroke. |
| stampIndex | uint | Zero-based logical stamp index; resets each stroke. Symmetry/repetition copies share it. Clipped and budget-skipped logical stamps still advance it. |
| distance | float | Path length in canvas pixels within this stroke, before Scatter or canvas wrapping. Interactive input smoothing has already been applied. |
| totalDistance | float | Path length accumulated across this brush's strokes, including strokes on different Drawing layers. Pointer travel between strokes is excluded. |

`BrushMotion` supplies movement independently of the sequence:

| Field | Type | Meaning |
| :--- | :--- | :--- |
| deltaPixels | float2 | Displacement from the previous logical stamp in canvas pixels: X right, Y up. The first stamp uses a phantom predecessor, one initial Spacing step behind its direction, or `(0,0)` without any known direction. |
| strokeDirection | float | `0` for Fixed Rotation, `1` for Stroke Direction. Tip UV already includes Rotation/Offset/Jitter/Flip; this flag lets the shader suppress a second direction-based turn. It does not describe or undo Offset/Jitter/Flip. |

Before an interactive Brush stroke, Canvas View filters pointer direction even without
painting. The first nonzero movement initializes it immediately. Later movement uses a
distance-weighted exponential filter, with decay `exp(-travel / L)` and
`L = clamp(visibleBrushSize * 0.08, 3, 6)` in Canvas View pixels. The unnormalized filter
state is retained between events; subdividing the same straight segment does not change
the result. Opposing directions near cancellation retain the last valid direction.
A stationary press preserves the filtered direction. This initializes both
**Stroke Direction** rotation and the first stamp's spacing-length `deltaPixels`; **Fixed** does not
rotate the footprint, but HLSL still receives the delta. It is converted with the current
view zoom/rotation into canvas pixels, without the layer transform, Scatter or tip rotation.
The first image appears immediately. For a tip with `BrushMotion` or Stroke Direction,
the first filtered movement of at least 0.25 canvas pixels corrects the first stamp once,
before any continuation stamp is added. With hover history the actual-direction weight grows
with the shortest angular difference: `w = abs(deltaDegrees) / (abs(deltaDegrees) + 45/19)`.
This positive SoftSign curve gives 0.95 at 45 degrees and approaches, but never reaches, 1.
Without history the corrected direction is entirely actual.
The corrected delta retains the initial Spacing length. Original pressure, random variation,
indices and copied stamps are preserved: correction replaces the provisional stamp, not an extra dab.
Small jitter that emits a continuation stamp before establishing direction commits the provisional
start instead of keeping an unbounded replay prefix. A click without movement keeps its first stamp.
Leaving Canvas View, panning, another tool, Alt picking, a different document or pointer
device clears the history. A Shift-connected line uses its known line displacement instead.
Hover never paints or advances `distance`, `totalDistance`, indices or randomization.
This is a short history of observed movement, not a prediction of the next one. It does not
change the painted path's Smoothing setting or delay the first stamp. Preview and API
strokes use the first known nonzero path direction upfront, without hover interpolation;
a one-point API stroke without direction starts with `(0,0)`.

All Brush and Pencil strokes accumulate in a separate linear ARGBHalf texture. The layer's
`paintSurface` remains the immutable source until commit; there is no copied `advancedStrokeBase`.
First-stamp correction reuses this source and accumulation texture without a CPU readback.
Storage sync or EndStroke commits the result and releases the accumulation texture; cancellation
discards it. Settings, preset/seed/tool changes finish the stroke before applying new values.

After the first stamp, `deltaPixels` is measured after input smoothing, before Scatter and canvas wrapping.
It carries across input events, including events too short to emit a stamp. Clipped
and budget-skipped stamps still define the previous logical position. Symmetry and
repetition copies share the original stroke's delta; tip rotation and flips do not
transform it. At a corner it is the straight displacement between stamps, not the
travelled path length. It is not velocity: no time interval is supplied.

`BrushRandom(uint seed, uint index)` and `BrushRandom(uint seed, uint strokeIndex, uint stampIndex)`
return deterministic values in `[0,1)`. They do not advance a mutable random generator.

```hlsl
float perStroke = BrushRandom(sequence.seed, sequence.strokeIndex);
float perStamp = BrushRandom(sequence.seed, sequence.strokeIndex, sequence.stampIndex);
float alongPath = sequence.totalDistance;
float stepLength = length(motion.deltaPixels);
float2 direction = stepLength > 1e-6 ? motion.deltaPixels / stepLength : float2(1, 0);
float hairAngle = atan2(direction.y, direction.x) * (1 - motion.strokeDirection);
```

Seed changes, a different preset/source code, or **Reset Sequence** restart indices and
totalDistance. Editing only declared parameter values does not. Previews use a separate
sequence. Progression is session state, not saved document/preset data; reopening starts
a new sequence. This is tip evaluation, not simulation: context is constant within a
stamp, and no previous stamp image or canvas pixels are supplied. Dynamic tips require
shader target 3.5; avoid expensive per-pixel loops or noise stacks.

### Declared parameters

Brush tips use the same [parameter syntax and fields as FX](../ShaderFX.md#parameter-declarations):
**float**, **bool**, **enum**, **float2**, **float3**, **float4**, **point**, **normal**,
**color**, **texture2D**, **transform2D**, **gradient** and **curve**. Defaults in the
declarations are used on JSON paste; there is no separate parameter-value object.
Maximum 32 distinct parameters, 128 controls and 64 KiB UTF-8 of code.

`label(...)`, tooltips, `hidden`, linked controls, `@group`, `@header`, `@helpbox` and
`@if`/`@endif` work as in FX. Optional `// @control(_Parameter)` belongs on line 2,
immediately after the brush header, and shows the referenced parameter as a compact field,
with the same supported types and warning rules as FX.

```hlsl
// @whimtex-brush Pattern/Striped Tip
// @control(_Frequency)
// @group(Pattern; _Enabled)
// @param bool _Enabled = true
// @param float _Frequency = 4 [1 .. ~16]
// @if _Enabled == 1
// @param gradient _Ramp = #FFFFFFFF -> #000000FF
// @endif
// @endgroup
float4 BrushTip(float2 uv)
{
    return _Enabled > 0.5 ? _Ramp_Sample(frac(uv.x * _Frequency)) : 1;
}
```

`point` uses tip UV rather than canvas UV. `transform2D` helpers operate on the square
tip, with an aspect ratio of 1. `normal` is normalized as in FX. These fields have no
Edit on Canvas buttons: brush parameters do not manipulate document handles.

`texture2D` accepts **Texture** (an optional Unity Texture2D asset; white when unassigned)
or **None** (transparent black). FX's Layer/Self sources and `SampleInput` are unavailable.
Gradient/curve sampling uses the same cached LUTs and helpers as FX: `_Ramp_Sample(t)`
returns straight linear RGBA; `_Profile_Sample(t)` returns a float. `color` defaults
are encoded RGB and are converted to linear once on upload, as in FX; raw float vectors
are not converted.

Float controls support independent soft boundaries: `// @param float _Width = 0.1 [0 .. ~1]`.
The slider stays within 0..1, while numeric input and label dragging may exceed 1 but cannot go below 0.
`[~0 .. 1]` makes only the lower boundary soft; `[~0 .. ~1]` makes both soft.
A range with a soft boundary requires two finite boundary values, with `min < max`. The initializer may be omitted.
Ordinary `[0 .. 1]` remains a hard range; the old `~[0 .. 1]` syntax is rejected.
HLSL preset export preserves both boundary flags and the current value. Limits explicitly written
in shader code still apply.

Helper functions and ordinary HLSL math, including `fwidth`, are allowed.
[FastNoiseLite noise functions](README.md#built-in-noise-library) are built in:
use `fnlCreateState`, `fnlGetNoise2D/3D` and Domain Warp directly without an include.
Otherwise code must be self-contained: no preprocessor directives or includes.
Names beginning `_WhimTex_` are reserved. There are no input-image sampling helpers.
Unity shader syntax is not a security sandbox: only use trusted code. Excessively expensive
or non-terminating shader code can stall the GPU.

The editor compiles the active tip on Apply/first preparation when source code changes,
not once per stamp. Static parameter/resolution edits redraw the cached texture without
recompiling unchanged source. Dynamic parameter edits update uniforms and resource bindings without baking;
gradient/curve LUTs rebuild only when their values change, not per stamp.
For Dynamic tips, Tip Resolution is hidden because it is unused. Size, Flow, Opacity and Scatter retain
their usual painting behavior. Dynamic tips use the same batched stamp mesh.

Save HLSL Preset writes the current parameter values as declaration defaults and retains
their groups, conditions and linked controls. It shares FX's export restrictions:
gradient defaults support only two-endpoint Classic/Gamma/Clamp with Smoothness 1 and
midpoint .5; any other gradient, including an implicit Perceptual default, causes an explicit refusal. Texture asset references
must resolve to a GUID in the destination project. A JSON brush contains code defaults,
not embedded parameter textures or separate edited values.
User files live in the configured presets folder under **Brushes/HLSL**. Project .hlsl
files are discovered through Unity's asset database. Both sources appear in one catalog,
grouped as User / Project. Selecting a catalog item copies its source into the brush;
it is not a live link to the file. Save As embeds code and values; only Static tips include
a baked image. Dynamic tips do not save a frozen preview as their source.
Brush settings and `.sebrush` presets retain parameter texture GUID/local IDs, not copies
of those texture images. Moving a preset to another project requires the referenced assets
with the same identities. Missing saved parameter textures produce an error instead of
silently replacing the image with white.
