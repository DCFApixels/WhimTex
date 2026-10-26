---
layout: default
title: WhimTex JSON format
parent: Technical reference
lang: en
permalink: /reference/json-format/
---

# WhimTex JSON documents

`whimtex.document`, version **3**, is the shared editable format for `.json` files,
layer clipboard data and agent serialization. It stores settings, not rendered pixels.
TIFF remains the image-backed format; its tagged binary encoding is version 3.
New saves and exports use `.json`; existing `.whimtex.json` names remain readable without renaming.
Previous JSON, layer clipboard and TIFF document versions are unsupported by this checkout.
They are rejected explicitly, not silently interpreted as version 3. No migration or old
`swizzle` alias is provided. Work with a matching older checkout if those files need editing.
New exports and Copy as JSON use this format. Brush and gradient preset formats remain separate.

Backward compatibility with package **0.12.5** or **0.13.0** is not a development requirement.
Retired fields and previous-name aliases are not accepted. The version-3 default
snapshot defines omitted values for current Compact files, independently of UI defaults.

The layer FX list is `fx` in UI, code and TIFF/JSON. JSON rejects `modifiers`;
the TIFF reader diagnoses it as an unknown field and protects against unconfirmed loss.

Channel routing is `Layer.channelMapping`, a `LayerChannelMapping` object with its existing
packed representation. Live API patches use `settings.channelMapping`, an array of four
source labels in output RGBA order; `describe` returns `channelMappingSources`.
The source label `1 - Luminance` evaluates `1 - (0.2126 R + 0.7152 G + 0.0722 B)` from the original linear RGB, independently of alpha.
Its public C# enum is `ChannelMappingSource.OneMinusLuminance`, value 15; existing source values 0–14 are unchanged.
In stored document JSON, `"channelMapping":{"$type":"LayerChannelMapping","packed":49152}` preserves RGB and routes this source to output alpha. Output alpha remains bounded to 0–1.

## Canvas comments

Full-document JSON preserves an optional `document.comments` list. Empty lists and
layer-only clipboard fragments omit it. Each `WhimTexComment` stores `id`, `anchor`
(bottom-left canvas UV), opaque `color` (RGBA in 0..1, alpha 1), `resolved` and a nonempty `messages` list; messages store `id`,
`author`, `text` and `createdUtcTicks`. IDs are unique across all threads and messages.
Comments do not contribute to image pixels. See [TIFF comments](TIFF_FORMAT.md#optional-comments)
for validation limits and anchor behavior. JSON insertion/paste imports layers only;
opening a full document retains its comments.

## Custom gradients

`WhimTexGradient` stores automatic and manual data together. `colors`, `alphas`,
`colorMode`, midpoint and automatic interpolation controls retain their automatic values.
`interpolation:"Custom"` selects `customKeys` and `customColorMode`; all JSON write modes
keep both datasets, including inactive manual keys. The additive fields do not change
the document version (3) or standalone gradient envelope version (2).

`customKeys` is a flat array of `WhimTexGradientCurveKey` values: `id`, `channel`,
`time`, `value`, `inTangent`, `outTangent`, `tangentMode`. IDs, tangents and
channel values survive a roundtrip. `customColorMode` is required with nonempty curves;
active Custom requires saved curves and is never reconstructed silently on load.
The [shared curve contract](AI/README.md#custom-channel-curves) describes limits, channel
meaning, automatic time-cluster markers and the right-key Constant convention. Native Compact
JSON may omit default-valued key fields; the standalone/API contract requires the fields
listed in that reference. Unknown fields and invalid dormant data are rejected too.
There is no persistent marker/group field. Explicit obsolete `link` fields (including zero)
are rejected without migration. Marker clustering sorts all channels by time/ID, includes
keys within 0.00001 of each earliest anchor without transitive chaining, then filters the
cluster for a color/alpha track. It does not snap stored times or affect evaluation.

`customApproximationError` is a nonnegative, finite diagnostic saved with document data.
It is the maximum observed relative sample deviation from conversion, not a bound on
the entire curve or a render setting. Standalone presets and agent palette writes do not
restore this diagnostic. GPU caches and sampling remain unchanged.

## Freeform gradients

`GradientLayerBehaviour.gradientType` accepts `Freeform` (enum ID 6). Its independent
`freeform.points` list contains 1–64 `{position:[x,y], color:[r,g,b,a], influence:number}` entries.
Positions are local layer UV coordinates, with (0,0) at the bottom left; finite values
outside 0–1 are permitted within -10000..10000. Influence is 0.01–4 (default 0.35),
displayed as 1–400%. Its guide is circular in local UV space and follows the layer transform.
It controls relative spread, not a hard clipping radius; scaling every influence equally leaves the blend unchanged.
RGB is straight, encoded color with signed HDR support; alpha is 0–1. `freeform.colorMode`
accepts `Linear`, `Gamma`, `Perceptual` (OKLab, default) or `Hue`. Point colors remain encoded when
switching models. RGB is mixed in the selected space with normalized inverse fourth-power
distance weights and converted to linear output; alpha uses the same weights independently.
CPU evaluation and GPU painting share this contract. A small squared-distance floor keeps coincident points finite; equal-position
points mix at their common anchor. Transparent points retain their RGB.

Hue accumulates OKLab and chroma independently with the same weights. The mean a/b vector
gives a chroma-weighted circular direction, preferring the minor arc without a `huePath`.
Its length divided by mean chroma measures agreement: at or below 0.25 the result is
Perceptual; from 0.25 to 0.5 a quintic smoothstep restores the mean chroma, fully restored
at 0.5 or above. Neutral colors contribute no arbitrary direction. Zero-length means are
finite and remain neutral. This two-dimensional rule does not reproduce the exact angular
speed of one-dimensional Shortest interpolation. RGB remains signed HDR; alpha is unchanged.

All JSON modes and native TIFF retain Freeform points even while a different gradient type
is selected, and retain the ordinary `gradient` palette while Freeform is selected.
Points are evaluated directly at requested source coordinates, including a viewport region;
there is no per-point canvas-sized texture.

Shape and Vector/Boolean Group styles also accept `fillMode:"Freeform"` or
`strokeMode:"Freeform"` (`VectorFillMode` ID 7). The independent fields `freeform`
and `strokeFreeform` have independent `colorMode` values and the same point contract in normalized layer-local coordinates,
before the layer/parent transform. Both are retained in inactive modes; active Freeform
data must be non-null even when its painting gate is disabled. Shape-to-Vector conversion
clones both point sets. Palette anchors and repetitions do not affect Freeform.
Text excludes this mode; SVG export is unchanged.
The live API accepts `settings.gradientType` and `settings.freeform` (see [AgentAPI](AgentAPI.md#freeform-gradient-settings)).

## Mesh gradients

Only `GradientLayerBehaviour` accepts `gradientType:"Mesh"` (enum ID 7). Its independent
`mesh` object contains the complete tensor-product cubic Bézier control lattice:

| Field | Contract |
| --- | --- |
| `columns`, `rows` | Cell counts, integers 1–16. These are not node counts. |
| `colorMode` | `Linear`, `Gamma`, `Perceptual` or `Hue`. |
| `positions` | Finite row-major `[x,y]` controls in local coordinates, including values outside 0–1. Exactly `(3*columns+1)*(3*rows+1)` entries. |
| `colors` | Row-major four-component working-space coefficients, with the same length as `positions`. |
| `hueChroma` | Required non-null for Hue: finite scalar chroma coefficients, same length/order as `colors`. Other models use `null`. |
| `nodeModes` | Row-major `Smooth`/`Corner` entries for `(columns+1)*(rows+1)` nodes, or `null` for all Smooth; new nodes default to `Smooth`. |
| `patchPriorities` | Optional row-major `[constant,uCoefficient,vCoefficient]` per cell. `null` or omission uses equal parameter intervals. |

For lattice width `w=3*columns+1`, element `(x,y)` has index `y*w+x`. User nodes are
at `(3*c,3*r)`; the other entries are Bézier controls and must not be omitted or
reconstructed from the nodes. `colors` stores linear RGB for `Linear`, encoded RGB
for `Gamma`, and OKLab for `Perceptual`/`Hue`. Hue evaluates the independent `hueChroma`
cubic field and applies the same circular-mean/conflict rule as Freeform. Both fields
are subdivided with de Casteljau, preserving the image and shared-cell continuity.
Do not reconstruct hueChroma as the magnitude of subdivided a/b controls. Chroma controls
can be negative; the evaluated correction never reduces chroma below the Perceptual result.
The fourth component of `colors` is a straight alpha
coefficient. It is not the ordinary encoded RGBA color contract: signed HDR and control
overshoot are retained, including alpha coefficients outside 0–1. Evaluation clamps
alpha to 0–1. C# callers use `NodeColor`/`SetNodeColor` for encoded display colors.

The six core fields through `nodeModes` are required when `mesh` is supplied.
`patchPriorities` is optional. Numeric components must be
finite; array-size mismatches and unsupported color/node modes are rejected. Positions
are not restricted to 0–1, and folded or overlapping cells are allowed. A missing
`nodeModes` field is rejected; explicit `null` means all nodes use Smooth without
rewriting geometry.
Internal Unity deserialization can turn null arrays into empty arrays. The model treats
empty node modes/priorities as implicit defaults; JSON export and Inspect write them
as `null`. External JSON still rejects `[]`. JSON/API reads normalize implicit defaults
through `Clone()` without changing the field's appearance or aligning handles.
An active Mesh requires non-null `mesh` data. TIFF and all JSON modes retain the full
lattice and node modes while another type is selected; the ordinary palette and Freeform points are
also retained. Copy/clipboard use the same data, without color-space conversion or
coefficient clamping. Selection and render caches are not serialized.

Vertex preparation uses Jobs/Burst with strict floating-point evaluation and the same
Bernstein sum order as `EvaluatePosition`. Each renderer reuses one native workspace
across its cells; UVs and triangle indices rebuild only when their subdivision counts
change. Resolution changes retain the existing tessellation criterion. The workspace
is released with transient layer resources; profiler stage `render.mesh.prepare`
and native-cache accounting expose its cost. GPU shading and overlap reconstruction
are unchanged, including during dragging.

Adding a row or column subdivides positions and color coefficients together using
de Casteljau, preserving the continuous result apart from floating-point rounding.
Existing node modes are carried to their corresponding nodes; inserted nodes use
`Smooth`. `SetNodeMode` changes editing behavior and can align geometry, but does not
change color coefficients. Switching Corner to Smooth aligns each opposite handle
pair to its average direction, preserving each handle's length. Subsequent handle
movement in Smooth aligns its opposite while preserving the opposite handle's length;
Corner leaves it independent. Raw `nodeModes` assignment records the modes without
rewriting coefficients, so file reads preserve geometry exactly.

`MoveEdge(node,direction,delta)` translates the edge's two handles and adjoining
tensor controls without moving its endpoint nodes. Smooth endpoint modes can also
realign the opposite handles. Directions are 0 left, 1 right, 2 down, 3 up.
Overlaps blend surface colors and opacity with normalized weights, not source-over alpha.
Weights fade near folds and exposed mesh edges; regions with one surface retain their
original color. MSAA samples are normalized separately to preserve edge coverage.
A GPU reconstruction then redistributes premultiplied linear RGBA across the whole
overlap using surrounding colors and boundary variation. A narrow transition band
around the overlap fades smoothly into the original image; pixels outside both regions
stay unchanged. The stored mesh is untouched, HDR is retained and alpha does not accumulate.
`patchPriorities` retains the row/column parameter intervals through subdivision.
A supplied array has `columns*rows` triples with finite, nonnegative components
summing to at most 1. The u slopes in the first row define column widths; v slopes
in the first column define row heights. Each axis is normalized to 0–1 (all-zero
slopes use equal intervals). Effective priority planes are rebuilt as a continuous
field across shared edges; independent stored constants do not control overlap order.
Null/omission uses equal intervals. Clone, subdivision and API reads canonicalize
the planes; preserve them when replacing a mesh instead of recreating equal cells.
These are technical data, not a blend-mode control.
Outside the mesh boundary the source is transparent; Mesh resolves `Source` tiling to
`Clip`. Ordinary palette interpolation
and wrapping do not control Mesh. Shape, Vector, Text, Boolean Group styles and
standalone gradient-value clipboard formats do not accept this mode.
See [Mesh editing settings](AgentAPI.md#mesh-gradient-settings) for the Agent API.
`SetColorMode` converts retained coefficients when changing models; because those
conversions are nonlinear, the interpolated appearance can change. `SetNodeColor`
updates the adjacent coefficient block as well as the node to retain its local
color derivatives. `EvaluateColor` returns straight linear color.

## Figure fills

The ordinary `GradientLayerBehaviour` stores `circularStartAngle` (default 0), in
counterclockwise degrees from the local left ray. For Angular, one period spans
`360/circularRepetitions` degrees; Repeat/PingPong wrapping remains unchanged.
The angle rotates the field without changing transform or palette keys; it is
retained by native storage/Undo and included in JSON when Angular is active.
Shifted angular fields rasterize on PSD export. This is separate from figure-fill geometry.

Gradient layer `gradientType` is required in every JSON mode, including Compact: `Linear`, `Radial`, `Angular`, `Square`, `Freeform` or `Mesh`. Removed type names and omitted types are rejected rather than converted. The Linear direction comes from its transform and canvas handles.

Shape and Vector share `VectorFillMode`: `None=0`, `Solid=1`, `LinearGradient=2`,
`RadialGradient=3`, `SquareGradient=4`, `AngularGradient=6`, `Freeform=7`.
JSON uses these exact names, never numeric enum values. `ShapeLayerBehaviour` and
each `VectorShape` store `fillMode` (default `Solid`), `fillColor` (white),
`gradient` (a new black-to-white `WhimTexGradient`, Perceptual/Balanced/Clamp),
`gradientStart` (`[0,0]`), `gradientEnd` (`[1,0]`) and `gradientRepetitions` (`1`). Shape also retains its
`fill` boolean (default true) as the overall body-fill gate. None suppresses fill;
Solid uses `fillColor`; the four palette gradient modes use `gradient`; Freeform uses
`freeform` as described above. Stroke is independent.

Anchors are finite normalized layer-local coordinates, Y up, and can lie outside
0..1. Geometry is evaluated in reference pixels: multiply local coordinates by
the current canvas width/height for Shape, **before** the layer Transform, or by
`referenceSize` for Vector. Thus a non-square reference frame affects distances
and angles before the layer transform is applied. `gradientStart` is the start
of a linear gradient and the center of the other palette gradients; `gradientEnd` sets
the radius and orientation.

Let `d = end - start`, `r = length(d)` in reference pixels, and let `q` be the
point's offset from start, rotated so the start-to-end ray is +X, then divided by
`r`. The palette parameter is:

| Mode | Parameter `t` |
| --- | --- |
| `LinearGradient` | `q.x` |
| `RadialGradient` | `length(q)` |
| `SquareGradient` | `max(abs(q.x), abs(q.y))` |
| `AngularGradient` | Positive counterclockwise angle from +X divided by one turn, multiplied by `gradientRepetitions`; seam on the start-to-end ray |

Equal anchors produce `t=0` everywhere for every gradient type. The palette's
existing `wrapMode` handles clamping/repetition after the angular multiplier.
`gradientRepetitions` is a finite float of at least `0.0001`, default `1`; one
angular period spans `360 / gradientRepetitions` degrees. It affects only
AngularGradient and is retained and validated in every mode, including disabled
fills. The existing anchor ray sets the start angle; there is no separate angle field.
All modes retain color and alpha stops, HDR values, midpoints, interpolation,
smoothness and color space. A gradient mode requires a non-null palette, even when
Shape's `fill` gate is off. Invalid modes or non-finite anchors are rejected.

Shape, Vector and Boolean Group (`style`) also store an independent stroke paint:

| Field | Default and behavior |
| --- | --- |
| `stroke` | `false`; existing stroke enable gate |
| `strokeMode` | `Solid`; the same seven `VectorFillMode` names as fill. `None` disables stroke paint while retaining its settings |
| `strokeColor` | Opaque black; used by Solid |
| `strokeGradient` | A separate black-to-white Perceptual/Balanced/Clamp `WhimTexGradient`; the same HDR, alpha, midpoint, interpolation, smoothness, color-space and wrap behavior as fill |
| `strokeGradientStart`, `strokeGradientEnd` | `[0,0]`, `[1,0]`; finite normalized layer-local anchors using the same geometry and reference-pixel metric as fill |
| `strokeGradientRepetitions` | `1`; finite float of at least `0.0001`. AngularGradient only, before palette wrapping; one period spans `360 / strokeGradientRepetitions` degrees |

Stroke never borrows the fill palette, anchors or repetitions. Its gradient modes
require a non-null `strokeGradient` even with `stroke:false`; modes, anchors and
repetitions are validated while inactive. Changing stroke paint does not alter
stroke geometry, fill settings or layer transforms. Boolean Group stores both
paints in `style`, using its `referenceSize`; Combine with `appearance:Layers`
uses each child's own paints and retains the inactive group style.

Full JSON, TIFF and Unity serialization retain both complete paint styles, including
inactive values. FullOptimized and Compact also preserve inactive figure fill/stroke
settings; Compact may omit defaults and restores them on read. This applies to
layer clipboard fragments as well. Other inactive branches still follow the
[write-mode rules](#write-modes). See [Shape API settings](AgentAPI.md#shape-settings)
and [Vector API settings](AgentAPI.md#vector-settings) for patch semantics.

### Vector paint caching

Vector separates geometric coverage from paint. Vector and Boolean operand contours
are flattened by portable Jobs/Burst workers over data-only snapshots, preserving
the curve tolerance, depth-first point order and near-duplicate/closing-seam handling.
Clipper2 Boolean operations and stroke construction retain their geometry. Coverage preparation
for GPU paint runs in independent bands of up to 32 rows, scheduled in batches of up
to eight that complete before the next batch, retaining double-precision
scan intersections and the original per-row accumulation order: Antialiased integrates
horizontal spans at eight vertical samples; Step samples the destination pixel center.
Jobs complete before the main thread uploads each figure's cropped RGBAFloat texture
containing fill, stroke and their intersection coverage. Both job kernels use strict
floating-point mode; this alone is not a universal cross-platform bitwise guarantee.
The scalar CPU fallback retains its original flattening and scan conversion and serves
as the coverage regression oracle. Preparation and paint edits do not reduce raster
resolution, relax curve tolerances or simplify the resolved contours.

Changing fill/stroke gradient anchors, active paint type, angular repetitions,
palette colors, alpha, interpolation or wrapping reuses that coverage. GPU paint
evaluates the local gradient coordinates and samples the shared interval-based
`WhimTexGradientPalette`, with independent fill/stroke palettes. Solid colors and
alpha also repaint without rebuilding coverage. Geometry, fill rule, stroke width,
position, joins, caps, dashes, reference size, rasterized transform, output dimensions,
clipping and Edge Mode invalidate coverage. Switching to/from None or changing the
stroke enable gate can also change the required coverage. Repeat/Mirror/Source
extension retains its existing source-image sampling path.

Paint uses the stored fill/stroke intersection to preserve partially covered edges;
it does not multiply independently averaged coverage values. Combine paints each
figure's fill and then stroke in bottom-to-top order. Multiple figures accumulate
premultiplied linear color in float precision, then convert to straight alpha for
the ordinary layer/FX pipeline. Translucent overlap and interior outlines remain
visible. This optimization preserves the rendering contract; GPU palette interpolation
and floating-point arithmetic do not imply bitwise equality with CPU shading.

The retained coverage budget is 128 MiB per Vector model, estimated across CPU and
GPU mask storage. A separate 128 MiB scratch estimate bounds each scheduled mask batch:
512 bytes per fill/stroke edge per band includes list growth allocations, plus three
float row buffers and 4 KiB per band. Before native allocation or dispatch, a conservative
work estimate includes prior figures' work, the crop's pixel count and each edge's
crop-clipped row count multiplied by eight AA samples or one Step sample. Exceeding
either memory estimate or the 100 million work estimate selects the original scalar
CPU path, as does missing float shader/render-target support. The scalar path retains
its original quality and actual-work rejection limits; a conservative estimate alone
does not reject geometry. Contour output is separately batched in groups of eight,
bounding native point payload to about 16 MiB before total-point validation. These
budgets are not a total renderer memory cap: painted output, palettes, copied edges
and any multi-figure accumulation target are additional storage. Caches and build
counters are transient implementation details, absent from JSON/TIFF.

Boolean Group caches its prepared result separately from paint. Group paint edits
retain that result; group stroke-geometry edits rebuild coverage without repeating
Boolean operations. Ignored child fill/stroke settings do not rebuild the Boolean
result. Child geometry, fill rule, visibility, transforms, order or operations can
invalidate it. Combine with `appearance:Layers` continues to render each child's
own appearance through its existing path. See [Boolean groups](#boolean-groups).

For cold builds, the render-only Boolean result passes prepared closed paths to
the rasterizer instead of allocating editable points/IDs and flattening them again.
The previous normalized-float coordinate round trip, duplicate-point filtering,
path order, seams, bounds and geometry limits are retained. At extreme coordinates,
float cancellation can make even zero-handle straight cubics subdivide; those
figures keep the original flattening path. This does not remove normalization passes
or change the editable/serialized vector model.

Within one synchronous geometry build, sequential resolutions reuse one lazy,
thread-local Clipper64 instance. Input state is cleared in `finally` after every
operation; result paths remain independently owned. Nested build scopes borrow
the outer lifetime, in-flight reentrant calls use an isolated engine, and the
outer scope drops the engine on exit. No engine or its buffers are retained
between builds. Clipper2 itself is unchanged.

Polygon-resolution profiling separates `vector.clip.budget`, `vector.clip.input`
(Clipper construction and input ingestion) and `vector.clip.execute`, nested under
`vector.clip.resolve`; `vector.boolean.filled_region` includes its child resolves.
Do not add parent and child durations together. The intersection-work guard keeps
its original four-million-visit acceptance rule, but counts active edges with two
sorted endpoint arrays in an integer-only Burst job instead of repeatedly scanning all overlapping edges.
An empty subject and clip bypass execution. Boolean accumulation also omits the
Union against an empty accumulator when its preceding NonZero normalization
returned exactly the input paths: identical vertices, path order, orientation and
seams. This observed fixed point avoids guessing from the fact that paths came
from Clipper. Other passes remain: touching contour partitions can change on a
subsequent Union even when the filled area is identical. That partition can affect
centered outlines and dash phase, so fill-only equivalence is not sufficient.
No curve tolerance, coordinate precision, winding rule or operand order changes.

## Shape strokes

`ShapeLayerBehaviour` stores `strokeJoin` (`Miter`, `Round`, `Bevel`; default `Round`),
`strokeCap` (`Butt`, `Round`, `Square`; default `Butt`), `miterLimit` (default 4),
`dashPattern` (default `[]`) and `dashOffset` (default 0). The stroke engine is shared
with Vector. Styled Shape outlines use the shared stroke geometry with a cached
polygon-BVH distance query in the Shape shader. Centered undashed Round outlines retain the
analytic path for their existing appearance and performance. Shape retains its
procedural Feather and document-pixel stroke metrics under layer transforms;
Vector transforms its local stroke geometry.

For closed Shape/Vector paths, Inside/Outside offsets the resolved fill boundary
by half the stroke width, then strokes the resulting trajectory at full width.
Joins and miter limits participate in the offset construction; holes use the opposite
filled side. Dashes and caps are generated after offsetting, without clipping the
finished stroke against the original fill. This preserves whole round dots/caps,
but is not a strict containment mask at tight corners. Collapsed insets remain empty
and narrow necks can split. Offset trajectories use a stable minimum-X/minimum-Y
seam and resolved winding; their changed length/seam can change dash placement.
Center and open paths keep their original trajectories. No source points or saved
settings are rewritten. Explicit layer clipping remains a separate operation.

Shape dash values are always multiples of `strokeWidth`: `[2,1]` means a dash twice
the width and a gap once the width, independent of inside/center/outside alignment.
Empty/null means solid; an odd list repeats twice. `dashOffset` remains document
pixels. Shape has no `dashPatternRelative` flag. Dash lists allow at most 256 finite
positive values, each at most 10000000; `miterLimit` is finite 1..10000 and `dashOffset`
is finite -10000000..10000000. The inspector's pattern editor limits individual
multipliers to 8192.

`strokePosition` keeps the Shape enum ordering `Inside=0`, `Center=1`, `Outside=2`;
do not cast it to Vector's position enum. JSON uses the names. `lineCap` remains
the independent body end cap for Line/Arc; `strokeCap` controls outline dash ends.
Full, FullOptimized and Compact retain inactive stroke settings, including when
`stroke` is false or `strokeMode` is None. Compact may omit defaults and restores
them on read.
See [Shape settings](AgentAPI.md#shape-settings) for partial API updates and the full
field contract.

## Vector layers

Vector layers use `$type: "VectorLayerBehaviour"`, with `shapes`, `referenceSize`,
`edgeMode`, `feather`, `featherPosition` and optional `brushDraft`. Each authored layer has at most one figure.
Multiple contours, including holes and disconnected parts, may form that one figure.
The layer owns its transform; whole-figure transforms do not rewrite its control points.
Shape, contour and point IDs are persistent;
point positions and tangent offsets use normalized layer-local coordinates, Y up,
and may extend outside 0..1. `referenceSize` is the physical document pixel size at
creation. Stroke widths and dash offsets use reference pixels and follow
the layer transform. For width-relative dashes set `dashPatternRelative:true`:
`dashPattern:[2,1]` means a dash of `2 * strokeWidth` and a gap of `strokeWidth`,
independent of inside/center/outside alignment. `dashOffset` stays in reference pixels.
False/omitted retains pixel-pattern interpretation for existing data. The inspector
always displays multiples of width and converts older values on pattern or width
edits, using the old width (or 1 when it is zero), in the same Undo operation.
Each shape uses the shared [figure-fill contract](#figure-fills) and retains its fill rule, fill/gradient settings,
stroke alignment, join, cap, miter limit and dash settings, including inactive values
in every write mode.

`feather` is a finite total soft transition width in vector reference pixels,
0..8192 inclusive, default `0`. `featherPosition` is `Inside`, `Outside` or
`Centered` (default). Inside fades inward from each painted boundary, Outside
fades outward, and Centered distributes the transition equally across it.
These are root Vector-layer settings, not per-figure fields or Boolean Group
style settings. Feather scales with vector geometry like the stroke; it is not
a fixed canvas-pixel blur. It evaluates smoothstep of signed distance to the fill
and the actual stroked region, including dash ends, joins and both outline
boundaries. It changes coverage before the layer's FX, opacity and composition,
without blurring paint colors. `feather:0` preserves the existing rendering path.
`edgeMode:"Step"` ignores Feather without clearing either stored setting.
All JSON write modes, clipboard fragments and native TIFF retain both values
while inactive; optimized modes may omit defaults, restored as `0` / `Centered`.
Native TIFF includes these public fields through the automatic tagged-field
serializer, without a separate Vector payload or migration.

`brushDraft` retains `width`, `height`, `coverage`, `shapeId`, `tolerance`,
`expansion` and `frame`. `frame` is `[x,y,width,height]` in normalized layer-local
Y-up coordinates, defaults to `[0,0,1,1]`, and requires finite values and positive
dimensions. Reframing the figure changes this mask mapping, not the original bytes.
Coverage is base64 of the original bottom-up row-major byte mask,
exactly `width * height` bytes, at most 1024 per axis. It is never replaced with a
re-rasterized contour during saving. Tolerance is 0.25..64 reference pixels;
expansion is -256..256. The draft survives JSON/clipboard, TIFF and Unity
serialization. A new brush figure gets its own layer and mask. Within an active
editing session, left strokes union coverage and right strokes subtract it using
max/min coverage, retaining the shape ID, styles, tolerance and expansion. Each
committed stroke owns new coverage storage for Undo; frame growth copies existing
pixels without resampling and rejects dimensions above the limit. Enter / Finish
Editing retains the mask and makes the next stroke create a new layer; Edit Mask
resumes the selected saved mask. Editing-session state is window-local and is not
serialized. Escape discards only the pending stroke. Figures without a saved mask
are not silently rasterized for editing;
manual edits to its figure or Release Mask discard the draft explicitly. Native tagged storage encodes
byte arrays as a bounded byte block (tag 32), avoiding the per-value graph limit.

The window's mask brush clips new paint/erase contributions to document canvas UV
using the figure's full render transform; this transient clip is not serialized.
Existing off-canvas source samples are retained. The builder bounds the swept tip
before buffer growth, skips outside rows/cells, and evaluates canvas-edge pixel
coverage without resampling the source. An empty figure starts with one grid cell;
only affected storage is allocated. Completing a stroke trims zero-only borders
and shifts `frame` on the same pixel grid. A stroke with no changed coverage creates
neither an Undo entry nor a background rebuild. Procedural Expand and subsequent
layer transforms are not restricted by this painting clip.

`VectorBrushGeometry.Rebuild` traces the unchanged coverage (or its signed-distance
offset), then fits closed cubic Bezier contours with editable tangent offsets.
Tolerance bounds the fit against the interpolated mask boundary in reference pixels;
it is not an alpha threshold or a node-count target. Tangents are shared at smooth
joins, including the closed seam. The fitter subdivides spans that exceed its error
bound and tightens tolerance if flattened curves cross or change hole nesting.
An unresolved shortest span retains its exact straight segment. Work/geometry limits
fail before replacing the figure; no contour is silently dropped. Rebuilding does
not mutate coverage, and callers replacing a figure retain its existing style.
Window brush commits and Original Mask adjustments run this data-only calculation on
a detached mask snapshot in a cancellable worker. Only the completed, still-current
result is applied on the main thread in a separate Undo group. Figure commits that
insert, replace or remove an original mask use a complete document Undo snapshot
instead of per-element diffing of the replaced coverage array. Snapshot capture and
Undo finalization are profiled separately as `undo.snapshot` and `undo.finalize`.
This leaves mask samples and curve fitting unchanged but can use more Undo memory.
A pending rebuild
blocks further vector gestures and window saving, not the Editor event loop. Escape,
Cancel, tool/layer/document changes, Undo or window disposal discard pending results.
The direct C# method remains synchronous and accepts an optional cancellation token.
Curve fitting reuses source tangents across retries; topology validation uses a 2D
bounding-volume tree with the same inclusive segment-intersection predicate and
cached source nesting. Tolerance, mask sampling and between-sample error checks are
unchanged. Worker time appears under `WhimTex.VectorMask.RebuildWorker` in the Unity
Profiler; the editor statistics' `vector.brush_trace.commit` and
`vector.brush_rebuild.commit` cover main-thread application only, not worker time.
The fitting approach uses least-squares cubic handles, chord-length parameters and
Newton refinement as described by [Schneider, Graphics Gems (1990)](https://github.com/erich666/GraphicsGems/blob/master/gems/FitCurves.c),
with an additional between-sample error bound and compound-path validation.

The data API exposes the same fields through `settings.vector`; supplied top-level
fields replace their values, and supplied arrays replace whole arrays. Unspecified
fields remain unchanged. API authoring rejects `shapes` with more than one figure.
Document readers still accept older multi-figure Vector data: `NormalizeModel`
splits it into an ordinary Group retaining the old layer ID, transform and FX,
with one independently styled figure per child. It does not save or overwrite the
source file. The resulting document must still satisfy the normal layer budget.
`convertToVector` converts a Shape on the same layer shell,
preserving its ID, FX and transform tiling. Every layer defaults to `Source`, including
omitted `transform` or `tiling` and Reset. The source declares its policy independently
of preview/intermediate/cache textures: Shape, Vector, Boolean Group, Noise, Gradient
and Color Fill use `Unbounded`; Drawing, Text, ordinary groups and image effects use
`Clip`; File uses the bound source texture's U/V wrap modes. An explicit mode overrides
that policy. Explicit `Clip` cuts geometry, strokes and Feather at the local frame.
Every JSON writing mode retains explicit modes and stored `Source`. The geometry frame is fitted while
preserving canvas placement and style; stored local coordinates/transform values
may change together. The C# conversion helper is
`ShapeToVector.Convert(ShapeLayerBehaviour, WhimTexDocument)` and returns a detached
replacement behaviour; callers swapping it into a layer keep its tiling and call
`VectorLayerStructure.FitFrame(layer, width, height)` (which retains Clip frames).
Straight edges remain straight; analytic arcs become cubic
curves, with elliptic offset arcs subdivided to a 0.01 canvas-pixel sampling tolerance.
Repeat/Mirror sample Vector's raster source and may differ at frame boundaries.
Active feather, strokes under nonuniform/projective transforms, overlapping
translucent fill/stroke replacement, and arc offsets that develop cusps or overlapping end caps produce an
explicit unsupported diagnostic before replacement. Curve approximation and the
different rasterizers do not promise pixel-identical antialiasing.

### Text outline conversion

`convertToVector` also accepts Text. The C# helper
`TextToVector.Convert(TextLayerBehaviour source, WhimTexDocument document)` returns a
detached `VectorLayerBehaviour`. For solid and whole-Text gradient fills, callers replace
the behaviour on the existing layer, retain the stored tiling (including `Source` and
explicit `Clip`), then call `VectorLayerStructure.FitFrame(layer, width, height)`.
The layer ID, name, FX, shared rendering settings and Boolean operation stay intact.
Fitting preserves canvas placement; stored local geometry and transform values may
change together. Text Frame Clip is already baked into the geometry before fitting,
so it remains effective.

For a Character gradient, the C# helper returns one independently painted figure per
visible glyph; do not put that multi-figure payload directly on a persistent layer.
The UI/API conversion prepares separate Vector child layers in an isolated group,
retaining the original wrapper ID, transform and FX on the group. Inside a Boolean
Group it uses a compatible nested Combine group with `appearance:"Layers"`. Each
glyph keeps its original gradient frame before clipping; only its geometry is clipped.
Preparation validates the document layer/depth limits before replacing the source.
The group sets serialized `Layer.groupFxInLayerSpace:true` so `LayerToLocal` in the
retained FX still uses the original layer transform. Ordinary groups default to false
(canvas-space FX); non-group layers ignore this flag. The flag participates in the
normal model/cache key, save and Undo paths and does not depend on cached textures.

Other fills produce one compound figure with `fillRule:"NonZero"` for all glyphs and
holes, preserving the solid color or whole-Text gradient. Layout comes from the shared
`TextLayerRenderer`, including effective Auto Size, alignment, wrapping, spacing,
casing, Horizontal Scale, Layout Offset and Frame overflow. Glyph indices and the
actual resolved font face select the original outlines through Typography.OpenFont:
TrueType outlines in TTF, CFF outlines in OTF and faces in TTC are supported sources.
Variable axes, color glyphs and bitmap fonts are not supported by this contract.

Original curves are unhinted. Quadratic segments become equivalent cubic segments;
cubic outlines retain their curves. Frame Ellipsis uses the displayed layout, including
the ellipsis glyph. Frame Clip clips the geometry; glyphs crossing the frame may be
flattened for geometric clipping with a 0.01-pixel tolerance. Synthetic italic is
supported; synthetic bold requires a real bold face and otherwise produces an explicit
unsupported diagnostic. Missing, unreadable or unsupported source-font outlines fail
before replacing the layer. A saved raster mask is never traced or used as geometry.
Grid fitting from Hinted AA/Pixel is not baked into outlines, so edge coverage can differ
from the original text rasterization.

The Vector stores the resulting contours, not editable text or a font dependency.
TIFF/JSON roundtrips therefore use ordinary Vector data. Keep a Text duplicate when
later wording or font changes are needed. Direct Text children of Boolean Groups retain
their editable source instead, using the same outline and layout rules at evaluation.

## Boolean groups

Use `$type:"BooleanGroupLayerBehaviour"` on the group layer's `behaviour`, and put
operands in the layer's ordinary `children` array. Common appearance is `style`
(a `VectorShape` with empty `contours`), `referenceSize` and `edgeMode`.
The group always uses `compositing:"Isolated"`; omitting this field also selects
Isolated for this type. Explicit PassThrough is rejected.

The bottom enabled child is the initial region; walk upward and combine each next
operand using its layer-level `booleanOperation`: `Union` (default), `Subtract`,
`Intersect`, `Xor` or `Combine`. The bottom operand's operation is retained but ignored.
Children are stored top-to-bottom, so evaluation walks the array in reverse.
Supported operands are Shape, single-figure Vector, editable Text and nested Boolean Group.
Unsupported raster layers and open contours produce a diagnostic instead of
silently disappearing. Compound contours and holes remain part of their figure.

Text children keep `TextLayerBehaviour` and their original text/font/layout data in
TIFF/JSON. They contribute source-font outlines without conversion; edits rebuild their
geometry. The font restrictions and overflow behavior in [Text outline conversion](#text-outline-conversion)
also apply here. A missing or unsupported font produces an explicit diagnostic; a
saved text raster backup cannot replace Boolean geometry, and font files are not embedded.

Child transforms remain independent and local to the parent. Source geometry is
combined before applying the group's common fill, stroke and FX. Child fill/stroke,
FX and other appearance settings remain stored, but are not rendered into the Boolean
result; source strokes do not enlarge the Boolean region. Moving a child out of the
group reactivates its own appearance. Nested Boolean Groups contribute their geometry,
not their painted result. Ordinary Group keeps its existing compositing behavior.

`Combine` appends separate figures rather than resolving their filled regions, preserving
each outline and bottom-to-top fill overlap. A later Boolean operation resolves the accumulated
filled regions. `BooleanGroupLayerBehaviour.appearance` is `Group` (default) or `Layers`.
Only a uniformly Combine group uses `Layers`: children render their own fill/gradient,
stroke and edge treatment, including Shape Feather, with normal linear-light compositing.
Their FX, opacity, blend settings, channel mapping and clipping flags remain ignored.
Nested groups render according to their own mode and appearance. All other group modes,
including mixed operations, always use the group style. The inactive appearance preference is retained.

Creating a Boolean Group from figures in the UI copies the bottom figure's style
once. Later child-style edits do not change the group style. The data API creates
the group with defaults or explicit `settings.booleanGroup`; it does not infer a
style from children added later. See [Boolean Group API](AgentAPI.md#boolean-group-settings).

## Write modes

| Mode | Stored values |
| --- | --- |
| `Full` | All persistent settings, including inactive values and defaults. |
| `FullOptimized` (default) | Active settings, including their defaults; inactive branches are omitted except retained Shape/Vector fill and stroke styles, Boolean Group styles and Vector source data. |
| `Compact` | Same inactive-branch rules, additionally omitting version-3 default values, except source canvas dimensions. |

The optional envelope field `writeMode` uses these exact names; absence means `FullOptimized`.
Full and Compact exports include it; FullOptimized may omit it. Opening restores the mode for
subsequent Save/Ctrl+S. `SaveJson` without options uses the document's `JsonWriteMode`.
Explicit export/serialization options do not change the source document's mode.
Disabled layers and disabled FX remain in all three modes. Collapsed UI panels and visibility-only
shader controls do not determine whether a value is active. Optimizations include unused Noise
warp/fractal/gradient settings, unused Make Seamless modes, disabled correction options, and unused
FX parameter value slots. Unknown/custom shader behavior is not guessed from UI visibility.
Compiled shaders, rendering caches, catalog refresh state and code-derived control descriptions
are not document settings and are never saved. HLSL source and parameter values are separate;
changing a value does not rewrite its declaration into the source code.

Omitted values use the frozen defaults in `WhimTexJsonDefaultsV3`, not the current UI factory defaults.
For insertion/paste only, omitted canvas dimensions instead inherit the corresponding destination axes.
Re-enabling an omitted feature restores those defaults, not the previously inactive user value.

## Envelope and model

Only `format`, `version` and `layers` are required at the root. `document` and every setting inside
it are optional. If present, `document` must be an object, not null; `{}` is valid.

- Opening or writing a document fills missing settings from version-3 defaults (canvas **512 × 512**).
- Insertion/paste fills each missing source dimension from the current destination canvas. Without
  either dimension, no canvas-size prompt is requested; unrelated document settings do not request a resize.
- With only `width` or only `height`, the other axis is independent: format default on open/write,
  destination axis on insert/paste. Explicit values still undergo normal validation.
- API insert/replace never resizes the destination. Clipboard paste can offer to adopt explicitly
  supplied dimensions; accepting keeps any unspecified axis at its current size.

### Canvas context on export

All writer modes keep `document.width` and `document.height` explicit, even when they equal defaults.
This preserves source-canvas context when the same export is pasted elsewhere. Other settings continue
to follow Full/FullOptimized/Compact rules. Omission is a reading convenience, not a request to discard
output settings during export.

```json
{
  "format": "whimtex.document",
  "version": 3,
  "document": {
    "width": 256,
    "height": 256,
    "outputFilter": "Bilinear",
    "outputPrecision": "EightBit",
    "outputSrgb": true
  },
  "layers": [
    {
      "id": "noise-example",
      "layerName": "Clouds",
      "behaviour": {
        "$type": "NoiseLayerBehaviour",
        "noiseType": "OpenSimplex2",
        "scale": 8,
        "fractal": "FBm",
        "octaves": 3
      }
    }
  ]
}
```

Use the exact enum spellings in the [generated schema](AI/document.schema.json).
Fields match the persistent model: `behaviour` contains type-specific settings; `transform`,
`fx` and `children` belong to the layer. Layer order is top to bottom. `$type` selects an
allowlisted model type, not an arbitrary assembly-qualified runtime type. Unity vectors and colors
are fixed-length numeric arrays; transform `Double2` values use objects with `x` and `y`.
Writers use the field's current component count. JSON permits shorter float-vector arrays,
filling added components with zero; tagged TIFF values must exactly match the current field type.
Scalars, integer-vector conversions, narrowing and shortened colors/quaternions remain invalid.
`Layer.fx` is a `List<ShaderFX>`. A model entry contains document-owned HLSL effect data with
`$type: "ShaderFX"`, source `code`, `parameters`, `active` and optional `$name`. Each effect
has independent values and enabled state; a shared preset source does not share mutable settings.

JSON `fx` items may only be `ShaderFX` model objects, `null`, or internal `{"$ref":"..."}`
references resolving to a `ShaderFX` model object in the same document. Optional `$id` on a
model object supplies that internal serialization identity; it is separate from a layer ID.
An internal `$ref` refers to that model instance, not an external preset asset. An item with
`$asset` is invalid even when its expected type is ShaderFX and the asset exists.

### Canvas frame changes

`WhimTexDocument.ApplyCanvasFrame(RectInt)` changes the output boundary in current canvas
pixels, with X pointing right and Y up from the bottom-left corner. Negative origins expand
that side. It preserves source images and the layer hierarchy; content beyond the new boundary
is retained and can be revealed by expanding the canvas again. The caller owns Undo and change
notification. The method returns false for the unchanged frame and validates every candidate
transform before changing the model. Dimensions are 1..16384 per axis, at most 16,777,216 pixels.

`WhimTexDocument.ApplyCanvasOrientation(CanvasOrientation)` applies `FlipHorizontal`,
`FlipVertical`, `RotateLeft` or `RotateRight` to the whole document with the same atomic
transform and source-preservation contract. Left is counterclockwise by 90 degrees; right is
clockwise. Quarter turns exchange width and height. The caller owns Undo and change notification.
The Crop toolbar applies these operations immediately and carries its pending frame, selection
coverage and guides into the new coordinates. FX remain live and retain their parameters;
canvas-dependent effects are reevaluated rather than baking and rotating the composite image.

`WhimTexDocument.ApplyCanvasScale(width, height)` stretches canvas geometry through the same
atomic preparation, preserving layer identities, hierarchy and owned source-image resolution.
Stored pixel measurements remain logical values. The document's `pixelScale` (default `1`,
finite and greater than zero, at most `16384`) converts them to physical canvas pixels during
rendering and painting: Text font/layout metrics, Shape strokes, procedural pixel spacing,
brush dimensions/distances and built-in FX radii. Render-quality reduction is a separate scale.
Custom Shader FX mark pixel measurements with `// @param px float ...` (or float vectors);
those uniforms are converted at upload. `_PixelScale` is available for explicit shader logic.

`ApplyCanvasScale(width, height, pixelScale)` and `ApplyCanvasFrame(frame, pixelScale)` commit
dimensions and the pixel multiplier together; `ApplyPixelScale(pixelScale)` changes the latter
alone. Two-argument Scale and one-argument Frame retain the current multiplier. Each operation
validates its full candidate before mutation and returns false only when all affected values
are unchanged. Text layout follows logical pixel metrics, so its physical size scales with
`pixelScale`; its stored settings and live FX parameters are not rewritten. The caller owns
Undo and change notification.

The Crop tool's `Scale` and `Resize` are subtypes selected with the shared toolbar flyout or
header dropdown. Frame dimensions and Pixel Scale remain drafts until Apply. Scale's Auto
uses the committed multiplier times `min(newWidth / width, newHeight / height)`, including
nonuniform changes. Disabling Auto retains its current result for manual editing. Reset returns
both drafts to the last committed dimensions and multiplier. The stretch preview displays
geometry; pixel-dependent rendering is recalculated on Apply.

`Layer.sourceCanvasSize` is a persistent `[width,height]` reference used by Noise, ordinary
Pattern and uninitialized Drawing sources. The default `[0,0]` follows the current canvas;
otherwise both dimensions must be positive, at most 16384, with a product at most 16,777,216.
The first canvas or pixel-scale operation records the previous canvas size for existing layers.
Later frame and orientation changes retain that reference. `sourcePixelScale` records the
reference multiplier (`0` initially means the current document multiplier). `sourceCanvasScale`
starts at `[1,1]` and accumulates the physical length ratios of the source's two local axes
under Scale (measured at its centre for projective transforms); procedural pixel spacing
uses this transformed source metric without applying the canvas stretch twice.
These references are separate from an image's stored resolution and are not caches.
Text keeps fixed local-pixel layout through transform compensation; Shape keeps its physical
geometry and stroke metrics. Source images are neither resampled nor truncated.

FX remain editable and run against the resized canvas. Effects that depend on canvas dimensions,
canvas UV, input boundaries or time can produce a different result; arbitrary FX are not guaranteed
to be pixel-identical after a frame change. Seamless Pattern recalculates its lattice for the new
boundary, preserving its seamless constraint rather than freezing the previous lattice.
Procedural antialiasing is also reevaluated on the new viewport's pixel-derivative grid;
an odd-pixel frame shift can slightly change soft edge coverage while retaining the geometry.

### Open, insert and replace

There is no document/fragment discriminator. Exporting selected layers produces a document containing
only those layers, with source-canvas settings. The caller's operation determines how the content is used:

- Open restores the document, retaining layer IDs, output settings and History.
- Insert/paste adds layers with remapped IDs; it does not replace existing layers or apply source output settings.
- Replace changes only the explicitly selected layer through the API; content never requests replacement itself.

Root `kind` is rejected. The caller chooses the operation.
The required `format` and `version` fields still identify the format and its version.
This does not remove type-specific fields such as Shape's `behaviour.kind`.

Clipboard may ask whether to adopt canvas dimensions; either choice preserves the destination's output
filter, encoding and other output settings. Exporting selected nested layers converts
their root transforms to canvas space. Include their Specific, Previous, All Below, clipping and
texture-layer dependencies; missing required selections fail explicitly.
Selected-layer JSON can be opened as its own document or written with the API, just like whole-document JSON.
This does not clone a TIFF TextureImporter's compression or platform overrides.

## Drawing and external assets

Drawing nodes retain their ID, name, transform and FX, but have `contentOmitted: true` inside their
behaviour instead of pixels. Nonempty Drawing requires explicit omission permission; the UI lists
affected layers before saving. Export does not empty, rebind or mark the source TIFF saved.
Saving from the window switches to the saved version without pixels only after confirmation.
The low-level `SaveJson` API does not erase live pixels: when omission is explicitly allowed,
it leaves the source marked dirty. Prefer `ExportJson` to retain a pixel-bearing source unchanged.
Empty placeholders can subsequently be saved normally. No URL download substitutes for saved pixels.

### Text and system fonts

`TextLayerBehaviour` also stores `fillMode` (shared `VectorFillMode`, default `Solid`), `gradientScope`
(`Text` default or `Character`), `gradient`, normalized `gradientStart:[0,0]`, `gradientEnd:[1,0]`,
and `gradientRepetitions:1` (finite and at least 0.0001). `color` remains the inactive Solid color.
Text uses the union of visible glyph rectangles; Character repeats the same relative style in each
nonempty glyph rectangle, not its advance or whitespace. Glyph coverage/hinting and layout keys remain
independent of paint. Every write mode retains inactive palette, anchors and scope. Missing-font Text
scope repaints the saved mask with retained `fallbackInkBounds`. Character also saves optional
`fallbackPaint`: base64 Deflate-compressed little-endian RGBA float32, bottom-up, with dimensions
`fallbackPaintWidth/Height` in 1..2048 and exactly width*height*16 decoded bytes. Each retained image
has a 768 KiB compressed budget; resolution can be reduced as with the existing mask fallback.
`fallbackPaintKey` binds that snapshot to its style; it retains signed/HDR RGB without color quantization.
Without glyphs, Character paint cannot be edited; a missing/mismatched snapshot gives a diagnostic,
never a silently substituted Text-wide gradient. Text-to-Vector conversion retains Text-scope paint in one
compound Vector. Active Character paint instead creates an isolated group on the original layer shell,
with one Vector child per nonempty visible glyph and independent cloned palettes. The parent retains its
ID, transform, rendering settings and FX; child FX are empty and child opacity is one. Glyph gradient anchors
use the original raster-layout paint rectangle before Frame clipping, then rebase with the child's fitted
geometry frame. Whitespace and fully clipped glyphs are skipped. Inside a Boolean Group the new container
uses nested Boolean Combine with Layers appearance to remain a valid operand. The 1024-layer budget is
checked before replacement. Boolean operations continue to ignore child paint.

`TextLayerBehaviour` stores editable `text`, `fontFamily`, `fontStyle`, `fontSize`, `maxFontSize`, `spacing`, `characterHorizontalScale`,
`alignment`, `casing`, `layoutMode`, `layoutOffset`, `frameSize`, `wrapping`, `overflow`, `justify`, `autoSize` and `color`. `layoutMode` is
`Point` or `Frame`; `wrapping` is `Manual`, `Words` or `Characters`. Frame dimensions are
1..32768 canvas pixels per axis. Point text ignores the retained frame settings.
`layoutOffset` is `[x,y]` in local document pixels, with finite components -1000000..1000000;
X points right and Y up. It moves the layout before rasterization without changing Transform,
is retained across Point/Frame switches, and participates in the saved-appearance key.
`overflow` is `None` (default, unclipped), `Clip`, or `Ellipsis`. It participates in the layout
key and saved appearance. Ellipsis shortens displayed text at text-element boundaries to fit both
frame axes, after Auto Size, without changing the editable `text`.
`autoSize` fits Frame text using the largest whole-pixel size within `fontSize` (minimum) and
`maxFontSize` (maximum, default 256), without overwriting either bound. Both are 1..2048 px;
the range must contain a whole-pixel size. With Auto Size off, `fontSize` is the fixed size.
The fitted size is derived, not a separate serialized field.
`casing` is `Normal`, `Lowercase`, `Uppercase` or `SmallCaps`; it affects the saved appearance,
not the editable `text`. SmallCaps synthesizes smaller uppercase glyphs for lowercase letters.
`spacing` is a `TextSpacing` object with `character`, `word`, `line` and `paragraph`, each -1..10 em,
defaulting to zero. One em is the effective font size. The former `lineSpacing` multiplier is a removed
field, not an alternative spelling or unit for `spacing.line`; old data must receive an unresolved-field diagnostic.
No font asset or machine-specific font path is embedded.
Omitted Text settings use frozen version-3 defaults: `text: "Text"`, empty `fontFamily`,
`fontStyle: "Normal"`, `fontSize: 64`, `maxFontSize: 256`, `alignment: "MiddleCenter"`,
`layoutMode: "Point"`, `layoutOffset: [0,0]`, `frameSize: [256,128]`, `wrapping: "Words"`, `overflow: "None"`,
`casing: "Normal"`, zero spacing, `characterHorizontalScale: 1`, white `color`,
and `justify: false` / `autoSize: false`. An empty font family resolves to the installed default;
specify a known installed family when a particular font is required.
`characterHorizontalScale` is a 0.01..10 width/advance multiplier, default 1. It preserves glyph
height and added em spacing, participates in wrapping/Auto Size/Ellipsis and the layout key,
and applies equally to Point and Frame text.
Saving prepares `fallbackPng` (base64 coverage PNG), `fallbackKey`, `fallbackFont`, `fallbackWidth`
and `fallbackHeight`, plus `fallbackRect` (local-pixel source bounds as `[x,y,width,height]`).
The layout key excludes tint, Transform and FX, so the saved mask can still
be recolored and processed when the font is missing. A source layout change invalidates that mask.
The PNG is limited to 768 KiB and 2048 pixels per axis; dimension checks precede image decoding.
Missing fonts are warnings, not automatic substitutions. A matching backup renders normally;
without one, the Text layer is transparent until a font is selected.

### SDF and Outline distance modes

Both `SDFLayerBehaviour.metric` and `OutlineLayerBehaviour.metric` accept the twelve names
`EuclideanAntialiased`, `Sharp`, `Bevel`, `Diamond`, `Square`, `MTSDF`, `AAEDT`, `Contour`,
`CornerFit`, `Spring`, `Rbf` and `RbfDual`.
JSON stores these exact names, not numeric IDs. `EuclideanAntialiased` (UI **Round AA**, ID 4)
remains the omitted/default value. `AAEDT` remains ID 10; `Contour` is ID 11, UI **Contour AA**.
`CornerFit` is ID 12, UI **Corner Fit AA**; `Spring` is ID 13, UI **Spring Simulation**.
`Rbf` is ID 14, UI **RBF Your SDF**; `RbfDual` is ID 15, UI **RBF Dual Contouring**.
The new `RbfDual` leaves the previous modes unchanged.

`metric:"RbfDual"` selects experimental continuous RBF root searches, analytic blended normals
and bounded 2D QEF vertex placement with half-pixel cell connectivity. It shares Rbf fitting
and the scalar sharp-distance stage. `sharpMode` and `miterLimit` have the same meaning as in
`Rbf`; `contourMergeDistance` is retained but ignored. The metric participates in the preparation
cache key; Max Distance only remaps the retained field. `Gradient`/`LinearData` are supported,
and `MultiChannelData` remains MTSDF-only. This is not a faithful implementation of
Occupancy-Based Dual Contouring (2024), and contains no copied upstream code.

`metric:"CornerFit"` and `metric:"Spring"` are experimental raster-only scalar modes.
CornerFit fits sides from raw Contour geometry and verifies proposed corners by local AA
re-rasterization. Spring first relaxes the raw contour with springs, then uses the same checks.
Ambiguous cases retain raw geometry; intersecting or unconfirmed extensions are rejected.
Only AA-verified corners receive bounded sharp joins with continuous, local endpoint influence. Both use `miterLimit`
(finite 1..64, default 4) to bevel excessive validated joins, and ignore the validated
`contourMergeDistance`. Source thresholds use coverage remapping; soft/noncoverage masks are
approximate inputs. SDF supports `Gradient`/`LinearData`; `MultiChannelData` remains MTSDF-only.
Neither mode guarantees sharp recovery. Spring adapts ideas from a depixelization paper and
does not reproduce its full interactive tool or C++ implementation; see the shared contract below.

`metric:"Contour"` measures Euclidean distance to finite straight segments extracted from raw
marching-squares alpha/channel threshold crossings, with linearly interpolated subpixel endpoints.
There is no contour fitting, smoothing, simplification, corner recovery, merge/union, miter extension
or MSDF channel construction. Expanded corners round; the field measures the current raster
threshold boundary without restoring lost detail. This is a separate algorithm, not an AAEDT
modification or a sharp-miter mode. Quality comparison is experimental; improvement is not
guaranteed for every input.

Source-channel, threshold, `SourceEdges`, offset and rendering behavior use the existing wrapper.
SDF exposes `threshold`; Outline uses fixed byte threshold 128. `miterLimit` and SDF
`contourMergeDistance` are still validated but do not affect Contour. SDF supports `Gradient`
and `LinearData`; `MultiChannelData` still requires `MTSDF`.
See the [shared field contract](AgentAPI.md#layer-settings) for all mode semantics and controls.

### Asset references

An external texture or mesh reference is an object containing `$asset` with `guid`, `path`,
string `localId` and expected `type`. Resolve the GUID first; if the GUID is absent from AssetDatabase,
try the path. The local ID and type must still match. If unresolved, open with warnings and a null
reference; retain its identity when saving again, including across Unity serialization.

This missing-asset behavior applies only to supported asset fields, such as texture parameters.
External Material, Shader and ShaderFX `.asset` references in `fx` are unsupported input, even
if their assets still exist or their identities cannot be resolved. Reject the document or clipboard
payload before accepting it, with
`External FX references are no longer supported. Use a marked .hlsl preset or inline HLSL code.`
This is a structural error, not a missing-asset warning. Do not silently drop the entry,
convert it to inline HLSL, migrate it or overwrite the source document. Marked HLSL source links
and external texture parameters remain supported; see [FX ownership](ShaderFX.md#document-owned-hlsl-effects).

### Failed or unavailable Shader FX

Shader FX source is embedded with project includes expanded. Engine includes remain engine dependencies.
If includes cannot be expanded, the original source is preserved instead; those external dependencies
must be restored before the FX can compile. Failed HLSL or parameter-declaration compilation does not
abort JSON loading: keep source, parameter values, order and enabled state, return a warning and skip
the FX during rendering. A failed Apply also skips an older compiled version. Successful Apply resumes
rendering; indicators clear only when no diagnostic issues remain. Warnings without errors do not mark
an FX unavailable, but are also returned when preparing JSON effects and remain visible in the headers.
FX diagnostics are marked beside their layer and in the FX section and effect headers, including
collapsed headers. All FX errors/warnings use the shared reporter, deduplicated by source path and diagnostic;
rendering does not retry compilation. JSON saving preserves broken code; TIFF save validation is unchanged.
Only open/compile shaders you trust: expensive GPU code can stall the editor.
Clipboard insertion uses the same soft compilation path after trust confirmation. Failed FX are
included in the paste warning; continuing preserves code, values and enabled state while skipping
their rendering. Canceling leaves the destination untouched. Structural/reference validation remains
strict; a failed shader does not excuse an invalid layer or texture dependency.

## Validation and limits

Unknown fields, incompatible types, unsupported versions and broken internal layer IDs fail explicitly.
Numbers and booleans must use JSON numbers and booleans, not quoted strings. Enum values must be exact,
case-sensitive declared names, never integer IDs or numeric strings. Numeric values must be finite and
representable by their stored type; integer fields reject fractions. Value types cannot be null.
Errors identify the field or component path. Curve tangents alone also accept `"Infinity"` and
`"-Infinity"` for stepped keys. Vector/color components follow their numeric types.
These are storage checks, not the stricter agent-property patch/UI slider bounds: finite persisted values
are retained, with the model's rendering clamps. Noise and Pattern axes and Shape corners
use explicit current values; missing components use the version-3 defaults, not sentinel inheritance.
FX declarations come only from `@param`; readers do not append declarations for saved values.
`declaredInCode` and Shape's former `roundness`/`cornerRoundness` fields are unknown input.
Shape corners use `{style:"Round"|"Bevel",amount:0..1}` objects: four `rectangleCorners`,
`polygonCorners` matching `sides`, or Star/Sector `outerCorner`/`innerCorner` groups.
See [Shape settings](AgentAPI.md#shape-settings) for edge, cap and angle controls.
The generated schema describes per-field constraints; graph dependencies, total layer count and the
combined canvas pixel budget additionally require the reader/API validator.
The reader does not silently discard invalid data. JSON limits are 64 MiB characters, depth 128,
one million model values and 1024 layers including group children; canvas limits are 16384 per axis
and 16,777,216 pixels. Drawing pixels are not in that budget.

## Saving and exporting from the editor

The window's Export button opens a shared export dialog: choose WhimTex JSON, set Mode, then Export…
to select a project path. Canceling path selection retains the dialog; only a successful export closes it.
Drawing omission is warned before path selection and still requires confirmation. Save As always selects a TIFF path;
new JSON copies are created through Export. Ctrl+S on an already open JSON document still saves its JSON file.

For the artist workflow, see [Save and export](en/saving.md).

## C# and agents

`WhimTexDocumentJson.Write(document, options)` and `WriteLayers(document, layers, options)` return
JSON, warnings and `DrawingPixelsOmitted`. Warnings can also describe unavailable system fonts;
only `DrawingPixelsOmitted` signals that live Drawing pixels were excluded and requires omission
confirmation or leaves the source dirty after `SaveJson`. Options use `Mode`, `AllowDrawingOmission` and `AllowDataLoss`
(both false by default). `AllowDataLoss` explicitly permits writing the loaded part of an incomplete
document; it does not recover unread data. Writing or exporting JSON keeps the source load warning.
An accepted `SaveJson` clears it only after successful saving. Interactive Save asks for confirmation,
with a recovery-copy option; C#/agent defaults remain protected.
`Read(json, prepareEffects)` returns an owned, disposable detached document. Call `TakeDocument()`
only when taking over its lifetime. `WhimTexDocumentFile.Load/Save` support TIFF and JSON;
`ExportJson` writes a separate file without changing source identity. Saves are atomic and reject
external file changes or a conflicting open document.

Agent `write`, `insert` and `replace` support `save:false`: return the prospective document snapshot
and `saved:false`, without creating or modifying a file or importer metadata. Existing-file revision
checks still apply. Revision fingerprints include live Drawing pixels (including pending GPU paint),
although JSON storage itself still omits those pixels.

`whimtex_document_json` / `WhimTexApi.DocumentJsonFile(requestPath)` uses a command envelope,
not another content format. See [agent API](AgentAPI.md#unified-json-documents).

Generate the schema through the connected Editor with `Documentation~/scripts/DocumentJsonSchema.cs`,
entry `DocumentJsonSchema.Run`. Do not edit the generated schema alone.
