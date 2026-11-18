---
title: "Shader layer contract"
parent: "Technical reference"
nav_order: 7.1
lang: en
permalink: /reference/shader-layer/
search_exclude: true
---

# Shader layer contract

The Shader layer generates its own image in either **Graph** or **Code** mode. Its behavior owns
a serializable `ShaderProgram`, also used by Shader FX. The artist workflow is in the
[Shader layer guide](en/shader-layer.md).

## Shared ShaderProgram

Open graph windows retain their document, target and view settings across script
reload. Graph views, debug-layout staging and preview compilation state are
transient and are rebuilt from the document-owned program after reload.

The shader system uses one vocabulary for both hosts:

| Term | Meaning and code |
| --- | --- |
| Shader Program | Stored editable content: `ShaderProgram`. |
| Code / Graph | Independent editing modes: `ShaderProgramMode`. |
| Draft | Editable source and values: `draftCode`, `draftGraph`, `draftCodeParameters`. It may be incomplete. |
| Working | Last successfully compiled source and values: `workingCode`, `workingGraph`, `workingCodeParameters`. Persisted source, not a GPU binary. |
| Runtime | Session resources and bindings: `ShaderProgramRuntime`. |
| Shader layer / FX | Hosts: `ShaderLayerBehaviour` / `ShaderFX`. |
| Shader preset | Reusable program: `ShaderPresetCatalog`, `ShaderPresetIO`, `ShaderPresetLink`. |
| Compile / Apply | Compile a program / bake an FX result. HLSL still uses `ApplyFX`. |

Shared declarations use `ShaderParameter`, parsing uses `ShaderMetadata`, HLSL generation uses
`ShaderSourceBuilder`, and diagnostics use `ShaderDiagnostics`. Document, preset and API data
use these same names. Previous names are unsupported; there are no migration aliases.

`ShaderLayerBehaviour.program` and `ShaderFX.program` use the same stored shape:
`mode`, `draftGraph`, `workingGraph`, `draftCode`, `workingCode`, `draftCodeParameters`,
`workingCodeParameters`, `presetId` and `presetVersion`.
The shared mode enum is `ShaderProgramMode`; `Graph` is 0 and `Code` is 1. `workingCode` is
the saved working Code text, not a GPU binary
or the generated Graph source. An FX with no applied Code can store null here.
`presetId`/`presetVersion` identify an exact catalog source; an unlinked program uses null/0.
`WhimTexDocumentJson.ShaderProgramSnapshot` and `ReadShaderProgram` provide the shared codec
for document, preset and API data, including inactive content.

`ShaderProgramRuntime.CompileDraft` and `RestoreWorking` select the source explicitly and share
candidate preparation and compilation. Hosts commit a successful candidate, register user Undo
and notify the document; restoration does not register an edit. Code declaration parsing and
value retention use `ShaderProgram.PrepareCodeParameters`. Prepared parameters are installed only
after successful compilation. A failed draft keeps its diagnostics and the working executable.
`ShaderProgramRuntime` owns Material and parameter/texture bindings for both hosts and modes;
`ShaderExecutableCache` shares the compiled Shader by exact executable source and include revision.
FX does not serialize a Shader or retain a second working-source/parameter snapshot. Native Undo
restores program data; the next explicit preparation rebuilds its resources. Status queries such as
`IsUnavailable`, `IsPixelLocal` and `UsesTime` never compile. Preparation runs before render-cache
dependency analysis, layer/FX rendering and visible inspector previews.
`ShaderSourceAnalysis` carries locality, regional-input capability, context dependencies and Unity-time
warnings from source generation to Runtime. Generated HLSL comments are not read back as runtime state.
Compiler messages, control-metadata warnings and Unity-time warnings are collected once per program
compilation for both hosts and modes. Failed compilation retains the same diagnostic snapshot,
including warnings and source locations; native graph errors retain their node IDs. Both editors
display Info/Warning/Error, and Console reporting uses shared suppression of repeated messages.
Node previews share executable reuse and scheduling but retain their own runtime bindings and
diagnostics, without repeating program-level metadata checks.
`ShaderParameterBindings.BindContext`
supplies common image dimensions, scale and evaluation context, with explicit FX viewport handling.

`ShaderParameterBindings.Plan` also shares parameter matching and texture-input lists. Code FX
match by name/type (first match); layer Code and Graph match by ID (last match, with storage-type
validation). The plan caches references and checks list structure, so direct edits and Undo remain
visible without rebuilding the plan on value-only changes. Both hosts and modes retain their last bound values
for draft fallback. Runtime reconstruction uses Working and leaves Draft and its parameter list intact.
`ShaderPresetLink.ApplyCandidate` validates the inactive saved program, installs and compiles a preset,
and restores the previous program through the same host callback on failure. Hosts still own
their resources, diagnostics, locks, source paths and Undo registration.

`ShaderCompilationState` shares attempt fingerprints, success/failure transitions and graph diagnostics.
Both hosts keep separate Code/Graph status; the graph editor reads the same status contract.
FX retains structured diagnostic snapshots for native Undo. Display text is formatted lazily; consumers
receive a read-only list, without parsing formatted messages back into diagnostics.
Restoring saved Code does not clear an existing failed-draft diagnostic in that mode. Repainting an
unchanged failed graph does not retry compilation. A failed compilation retains a usable previous
program in the same mode for both layers and FX; an FX without one is skipped. Error indicators and
explicit Compile failures remain visible even when the previous program can render.
`workingGraph` stores the last successful graph, including functions, Sub Graphs, include backups,
parameters and bound node-input values. `workingCodeParameters` retains the working Code values.
The common compile path replaces these snapshots only on success. Queued compilation takes a stable
candidate and accepts current compatible values, resources and controls before publishing Working.
Snapshot copying shares immutable strings and never serializes through a JSON scratch buffer.
Binding updates retained values without cloning a graph per frame. Loading restores the saved
program without replacing the editable draft; GPU resources are rebuilt locally.
Shader layers and FX share texture-reference remapping and the JSON/TIFF exclusion policy for
native execution fields. FX shaders are transient resources, never saved as separate assets.
FX native diagnostic fields still participate in Undo. TIFF accepts pending or failed drafts when
the selected mode has a restorable working program; it refuses saving without one before replacing
the destination. Structurally invalid data and unresolved unsafe references remain rejected.

Automatic graph requests share a captured graph, validation, subgraph expansion and adjacency.
HLSL generation, include expansion and source analysis run on one CPU worker. Paths and labels
are captured on the editor thread; texture setup, native compilation and material binding stay there.
Working graphs and resource bindings remain separate from the worker snapshot.

Automatic main-result and node-preview requests use one priority policy. Main results have priority;
queued previews are ordered by distance to the graph viewport center, updated when the view moves.
New preview compilations wait while a main result is still being prepared. Already running native
compilations are not interrupted. The native concurrency limit is unchanged:
at most two native compilations run automatically, with one new native submission per Editor update.
Pending requests for the same executable share the work. New edits replace stale queued requests,
and stale completed candidates cannot replace the current graph. Rendering retains the last working
result while compilation is pending. Explicit Compile, saving and export complete required programs
synchronously; these boundaries do not save a pending candidate as Working.

Executable reuse survives switching outputs and returning to a previous graph revision within the
session. Unused entries are evicted by recency, targeting 48 entries and 24 MiB of source text;
live owners keep their shaders until release. A separate prepared-source cache has the same limits.
Neither limit estimates native compiler/GPU memory. Include changes invalidate reuse, and domain reload
releases transient resources. Code and external-function dependencies are resolved again on requested
builds. First compilation of a new complex executable can still take time.

Generated local variables, uniform slots and diagnostic locations use deterministic names in
evaluation order, independent of stored node/parameter IDs and list ordering. Equivalent copies
can therefore reuse the same executable. Each runtime retains its own ID-to-slot mapping,
Material, resources, working values and diagnostic-to-node mapping. Stored IDs and public
parameter names are unchanged; authored Code and external source paths retain their identity.

Before HLSL compilation, a conservative allowlist shares identical typed pure expressions with
the same inputs within one evaluation scope. Combine/Split/Swizzle and numeric casts reuse
known component sources. Editable defaults remain independent uniforms even when equal;
Constant nodes retain float declarations and native arithmetic. Loops and sampled-UV branches
have separate scopes. Custom code, noise and other context-dependent operations are barriers,
not candidates for expression merging. No CPU approximation of shader arithmetic is performed.
This reduces redundant generated code; it does not guarantee faster first compilation for a
single complex noise branch. The public graph model and editing behavior are unchanged.

Graph code fingerprints exclude authoring labels/ranges and uniform values. Preview value fingerprints
follow evaluated inputs and ignore defaults overridden by connections; executable validation still sees
the relevant topology and inactive connections. Authoring directives remain in Generated HLSL, while
native shader source receives declarations independently. Changing controls or values therefore need
not create a new native shader, and unchanged branches keep their preview images.

The program does not own a layer's Input, Transform or blending. The Shader behavior keeps
`inputMode`, `inputTexture` and `inputLayerId`; the host supplies its evaluation context.
In an FX, Sample Input reads the previous chain image, and graph parameters use the ordinary
FX controls. Both hosts reuse the existing graph editor window. Moving the same program
between a Shader layer and an FX changes its input and coordinate context; pixel-identical
results are not guaranteed under all transforms. FX PixelLocal classification remains a
conservative analysis of generated HLSL, not a property guaranteed by Graph mode.

## Independent Graph and Code modes

`program.mode` selects `Graph` (the Shader layer default, enum value 0) or `Code` (value 1). The UI calls Graph
**Shader Graph**. Switching mode never converts a graph to HLSL, builds a graph from HLSL,
applies a code draft, or clears either mode's parameters. Both states survive save/reopen,
JSON in every writing mode, clipboard transfer, duplication and Undo/Redo.

| Stored field | Meaning |
| --- | --- |
| `draftGraph` | Editable graph, including its own parameter declarations and values. |
| `workingGraph` | Optional last successful graph and working values; null before the first successful Graph compilation. |
| `draftCode` | Editable WhimTex HLSL draft; it may be incomplete or contain errors. |
| `workingCode` | Last successfully compiled HLSL text used by Code rendering, not a GPU binary. |
| `draftCodeParameters` | Accepted Code declarations and current values, including controls and texture identities. |
| `workingCodeParameters` | Saved working Code declarations and bound values. Empty/omitted data uses `draftCodeParameters` when restoring the saved source. |

New Code state has an opaque white `ApplyFX` in both text fields and an empty parameter list.
Explicit **Compile** parses the draft's `@param` declarations, preserves compatible parameter
values through the shared WhimTex HLSL rules, and compiles the GPU program. Only success replaces
`workingCode` and `draftCodeParameters`. A failed UI compile retains the draft and the previous applied
state. Loading or rendering recompiles the applied text when needed; it never applies the draft.

Code uses the [WhimTex HLSL interface](AI/README.md#hlsl-interface--shader-only-or-inside-json),
including `@param`, shared controls and `@preview`. Its entry point is
`float4 ApplyFX(PinnedUV uv, float4 color)`. Here `uv` is local procedural UV under the same
Unbounded layer transform as Graph. `color` and `SampleInput` read the layer's shared **Input**
described below. Declare `@param texture2D` and use `SampleTexture` for additional image inputs.
Self is unavailable for texture parameters; Texture, Layer and None remain supported.
The result is straight RGBA and follows the ordinary layer pipeline.

Properties shows parameters for the selected mode. The graph window is read-only while Code
is selected; switch back to Shader Graph to edit its retained graph.

Stored-reference validation and copy/paste ID remapping inspect texture parameters from **both**
modes, including inactive references and cycle checks. Runtime dependencies and render stamps
inspect only the selected mode. Draft `draftCode` never participates in a render stamp; Graph stamps
exclude `workingCode`/`draftCodeParameters`/`workingCodeParameters`, and Code stamps exclude both graphs. Working snapshots participate in stored-reference validation and remapping. Graph annotations and
node positions remain excluded. These rules also apply to graphs nested inside layer groups.

## Shared Input

`inputMode`, `inputTexture` and `inputLayerId` belong to the layer and are shared by Graph and
Code. Changing mode retains both source selections, including inactive and missing references.

| `inputMode` | Image supplied to the shader |
| --- | --- |
| `None` (default) | Transparent black, `float4(0,0,0,0)`. |
| `Texture` | The Texture2D asset in `inputTexture`; a missing asset reads transparent black. |
| `Layer` | The rendered layer or group identified by `inputLayerId`, using the existing layer-source rendering rules. A missing source reads transparent black. |
| `CompositeBelow` | The lower composition at this stack position, including external backdrops through Pass Through groups. Isolated/clipped groups start transparent. This layer and layers above it are excluded. |

Code receives the selected image through `SampleInput(uv)` and the incoming `ApplyFX` `color`
at the current pixel. Graph uses **Sampling/Sample Input** (`kind: "SampleInput"`): one Float2
`UV` input defaults to local procedural UV; Float4 `Out` is labeled **RGBA**, with Float
outputs **R**, **G**, **B**, **A** sharing that sample. Its `wrap` and `filter` settings
follow Sample Texture. UV can be connected to distortion nodes for displaced reads.
`_InputSize` describes the selected input texture; `_CanvasSize` remains the document size.

Input changes reading; blending remains a separate layer setting. For a stack effect use
`Code`, `inputMode: CompositeBelow`, `blendMode: Crossfade`, and
`float4 ApplyFX(float4 color) { return color; }`, followed by the layer's existing FX.
Crossfade mixes the original and processed premultiplied RGBA by Opacity without accumulating alpha.
Composite Below includes the external backdrop through Pass Through ancestors, stopping at isolated
or clipped groups. Standalone previews, texture inputs and baking use the same stack-position input.
The built-in passthrough source forwards that input directly to Transform and FX without an extra draw.
Other Code and Graph programs keep their existing execution path.

Validation rejects unresolved nonempty selected layer IDs, self/cyclic dependencies and the
document's own output asset. Empty selections are valid and read transparent black; a source
that disappears during rendering also reads transparent black. Inactive
source selections are retained without adding a render dependency. Copy/paste remaps
`inputLayerId` even when Layer is not selected; unresolved asset identities remain available
for later restoration. TIFF and every JSON writing mode retain these fields.

## Layer boundary

- **Shader:** evaluate the selected graph or applied Code program for each output pixel and produce straight RGBA.
- **Shader FX:** process the owning layer's image with an effect.
- **Shader + Composite Below + Crossfade:** process the lower composition, then mix the processed result by Opacity.

Shader reads the lower stack only when **Composite Below** is selected for Input. Additional
images enter through declared Texture parameters, read by Sample Texture/height-processing
nodes in Graph or `SampleTexture` in Code. The generated image participates in the ordinary layer
transform, FX and composition pipeline; Output is not the final document composite.

Create nodes through the graph context menu's **Create Node**, **Space**, or by dropping an input or output
connection on empty graph space. Notes, groups and edge reroutes have separate context actions.

## Graph parameter authoring through Transform2D

Graph and Code share parameter values, control metadata and binding rules. This parity does
not convert one mode into the other. The Graph parameter picker adds these controls:

| Graph selection | Stored value and metadata | `Parameter.Out` |
| --- | --- | --- |
| `Enum` | `valueType: "float"`, `floatValue`; each Enum control's `optionNames` and `optionValues` form a parallel name/value table. Values may be fractional and are not option indices. | `Float` |
| `Seed` | Parameter `type: "Float"` with a `controls[]` entry of `type: "Seed"`, `seed: true`, `valueType: "float"`, both control boundary flags always `true`, and explicit `valueMinimum`/`valueMaximum`. Integral `floatValue` in **−16777216…16777216**, inclusive; endpoints and randomized values obey the same domain. | `Float` |
| `Point` | `vectorValue.xy`, default `(0.5,0.5)` in normalized UV; positions outside 0…1 are valid. No range. | `Float2` |
| `Normal` | `vectorValue.xyz`, normalized when supplied to HLSL; default and zero-vector fallback `(0,0,1)`. No range. | `Float3` |
| `Transform2D` | `transformValue`, the existing `ShaderTransform` with `position`, `size`, `rotation`, `storage` and `matrix`. | `Transform2D` |

Other rows use the selected name as parameter `type`. An omitted/null numeric `valueType`
uses the type's native float width; Seed's parameter storage is Float even though its control
is Seed. Preserve the stored control instead of replacing it with a plain Float row.
Seed uses the shared numeric control and **Random** action. Its control must always store
`hasMinimum: true` and `hasMaximum: true`: `ShaderSeed.Validate` and Random require both,
even when the Graph **Range** toggle is off. The parameter's own `hasMinimum`/`hasMaximum`
remain `false` with Range off; the control's `valueMinimum`/`valueMaximum` are then explicitly
`-16777216`/`16777216`. These control flags describe the valid Random domain, not the UI toggle.
With Range on, the parameter flags are `true`, its `minimum`/`maximum` hold the UI endpoints,
and the control's `valueMinimum`/`valueMaximum` hold those same endpoints. Preserve the
control's `minimum`/`maximum` and soft flags as well; do not use them in place of its numeric
`valueMinimum`/`valueMaximum`. See the [Seed JSON examples](AI/README.md#graph-parameter-parity-through-transform2d).
Random samples the control's inclusive domain. Soft limits constrain
random generation while allowing manual values beyond the suggested bounds, within that
domain. Rendering, copying, saving and reopening do not randomize the stored seed.

For `Float`, `hasMinimum` and `hasMaximum` enable limits independently; `softMinimum` and
`softMaximum` select hard or soft behavior for each enabled endpoint. Hard limits clamp
editing; soft limits give slider bounds while allowing manual values outside them. A single
enabled endpoint must be hard; soft Float boundaries require both finite endpoints with
`minimum < maximum`, with softness selected independently for each.
`cyclicRange` requires two finite hard endpoints with `minimum < maximum` and repeats
numeric input around that interval. `Vector2` without limits is an ordinary vector; a
range requires two finite hard endpoints with `minimum < maximum`. Its ordinary interval
orders X ≤ Y; cyclic mode retains start/end order across the seam and full-cycle winding.
It has no soft interval mode. These are the same [range rules as Code](ShaderFX.md#float2-intervals).

The common metadata path retains `controls`, including each `label`, `optionNames`,
`optionValues`, range values and flags, together with parameter-level limits and `pixelUnits`.
`pixelUnits` is the stored form of `px`: retain logical values and apply document pixel scale
once during binding. It applies to ordinary supported numeric lengths, not Enum, Seed,
Point, Normal or Transform2D. A full document/program/graph copy must retain these fields,
stable IDs, current values and inactive branches; do not reconstruct declarations from the
visible Properties rows. There are no new `options`, `range`, `random` or `px` JSON fields.

Graph parameter grouping and order use the recursive [parameter layout](ShaderGraph.md#parameter-layout),
including Group, Foldout, Tabs and Tab. This parameter-control scope still excludes new typed
integer ports, expanded color/toggle vectors, condition (`if`) and preview authoring.
Existing metadata for those Code features survives complete copies.

### Apply Transform 2D

`kind: "ApplyTransform2D"` has enum ID **164** and belongs to **UV**. Its exact port keys are:

| Direction | Key | Type | Meaning |
| --- | --- | --- | --- |
| Input | `Transform` | `Transform2D` | Transform parameter or inline transform resource. |
| Input | `UV` | `Float2` | Coordinates to transform; unconnected defaults to current-pixel UV. |
| Output | `Local` | `Float2` | `_Name_ToLocal(UV)`: input coordinates to the transform's local frame. |
| Output | `Input` | `Float2` | `_Name_ToInput(UV)`: local coordinates to the input frame. |

There is no `Out` output. Both outputs apply their direction to the same `UV` input;
`Input` does not implicitly consume `Local`.
Local `(0,0)` and `(1,1)` are frame corners. Transform values retain the shared TRS/projective
mapping, image aspect handling and guarded homogeneous divide described in
[Transform 2D](ShaderFX.md#transform-2d). Coordinates are not clamped or wrapped, and the
node produces no mask. It does not replace the layer's own Transform.

`Transform2D` is a resource-like port, not a `float3x3`, numeric vector or Color. It can be
passed through Reroute and matching Sub Graph pins, but cannot enter dynamic numeric math.
An inline default uses existing input storage:

```json
{
  "port": "Transform",
  "type": "Transform2D",
  "resource": {
    "type": "Transform2D",
    "transformValue": { "position": [0.5, 0.5], "size": [1, 1], "rotation": 0, "storage": "TRS" }
  }
}
```

An omitted resource uses identity (`ShaderTransform.Default`). The inline **Transform**
button opens a `PopupWindow` containing the shared `ShaderParameterView` with
Position/Size/Rotation fields and Reset, without a canvas button; canvas manipulators belong
to the parameter. Connecting a transform overrides the literal without deleting it.
Do not encode the transform in `inputs[].value`.

## Parameters and ports

Parameter headers can be dragged to reorder `graph.parameters` or dropped on the graph to
create a Parameter node at the transformed canvas position. Header context actions are
Use in Graph, Duplicate and Remove. Duplicate deep-copies parameter settings and values
(retaining texture/layer references), assigns a new ID and unique name, and inserts after
the original. Remove deletes its Parameter nodes and incident edges. These operations support
Undo/Redo. Header indicators reuse output-port types, colors and shapes.

Parameter declarations define the fields shown in the layer's Properties. A Parameter node reads
a declaration, and multiple nodes can read the same declaration. Constant selects PI, TAU,
PHI, E or SQRT2 and always outputs Float. Texture, Gradient, Curve and Transform2D parameters use the shared controls and bindings.

In the graph editor, Parameter nodes use one compact row containing the parameter name and
output connector. There is no visible `Out` label, internal preview or dropdown. Choose another
declared parameter through the node's context menu. This presentation does not rename the
serialized `Out` port or change parameter declarations and values.

| Port type | Meaning |
| --- | --- |
| `float`, `float2`, `float3`, `float4` | Numeric scalar/vector values. |
| Color | RGBA with the same four components as `float4`, with alpha-one defaults when converting from a scalar or shorter vector. |
| Boolean | Conditions for Compare and Select. |
| Texture | An image resource; Sample Texture produces its color. |
| Gradient | A gradient resource; Sample Gradient evaluates it at a scalar position. |
| Curve | A curve resource; Sample Curve evaluates it at a scalar position. |
| Transform2D | A transform resource; Apply Transform 2D maps coordinates in either direction and produces Float2. |

Unconnected Texture, Gradient and Curve inputs have 108 px inline fields to the left
of the port: a Texture2D asset picker, the gradient editor and the native curve editor.
Connecting a wire hides the field without deleting its value; disconnecting restores it.
An unassigned texture samples white; gradient and curve defaults match graph parameters.
This also works on Sub Graph calls and Color Curves. Edits update rendering without
recompiling after the input has been declared. Values are retained in the editable graph
and its working copy for Shader layers and FX.

In JSON, a resource literal is a node `inputs` entry with `port`, its graph `type`
(`Texture`, `Gradient`, `Curve` or `Transform2D`) and `resource`, a `ShaderParameter` with matching
`type` (`Texture2D`, `Gradient`, `Curve` or `Transform2D`) and `textureValue`, `gradientValue`,
`curveValue` or `transformValue`. Inline textures use `textureSource: "Texture"`; layer/composite sources
remain available through graph parameters. Resource values are not exposed as extra
Properties parameters. An omitted resource uses the default value.

Sample Gradient and Sample Curve default their scalar **T** to the current **UV.x**.
Sample Curve accepts T and returns values outside 0..1. It samples the full key-time
span and applies the curve's pre/post Wrap outside that span; native and SVM paths agree.
The unconnected scalar input offers **UV.x / UV.y / Custom**; Custom stores a numeric value. Explicit
values in existing graphs and connected inputs are retained. API discovery reports
`defaultUV: true` and `defaultExpression: "UV.x"` for these ports. Resource references
preserve null through native Unity serialization, clipboard and domain reload.
An explicit input's `source` selects `Value` (default), `UVX` or `UVY`. The component
selection applies to scalar UV inputs, including 1D noise Position; vector UV inputs
use full UV and retain the scalar choice when changing noise dimensions. A Float3 UV input
uses `float3(UV, value.z)`: the UV selector occupies X/Y and the native Z component remains
editable (default 0). Z is stored in the same input value, updates as a uniform and survives
UV/Custom switching, connections, serialization and dimension changes. Discovery reports
`editableUVComponents: ["z"]` and `defaultExpression: "float3(UV, value.z)"`.
Sample Input, Sample Texture and Sample Gradient expose **RGBA** (Color), **R**, **G**, **B**, **A**
(Float). RGBA keeps the stored port key `Out`; discovery maps it via `outputLabels`.
Connected channel outputs reuse the full-color sample instead of sampling again.

Numeric and Color ports use circular connectors, Boolean ports use diamonds, and Texture,
Gradient, Curve and Transform2D ports use squares. The shape follows the port type, including Parameter
outputs and Reroutes.

Connectors on ordinary nodes, Parameter and Output are centered on the inner background's
left/right boundary and draw above the selection outline. External input fields follow the
input connector; overflow picking includes its exposed half. Connections shorten their end
reaches by the connector outset, retain the native turn diameter, then lengthen the shortened
reach by 15%, capped at the native reach. Drawing, picking and drag previews share the native
edge control with adjusted control points. Reroute retains its inset connectors, size and native
end reaches. The 24-pixel rows and 8-pixel connector sizes retain the installed native port metrics.
The extension area behind previews and options matches the native output-port column background.

Visible port labels append the inferred type in parentheses: `1`, `2`, `3`, `4` for numeric
widths, `T2` for Texture, `G` for Gradient, `C` for Curve, `TR2` for Transform2D, `col` for Color and `bool` for Boolean.
Parameter names and custom HLSL labels use the same notation. Reroutes remain label-free;
the suffix is display-only and never changes a stored port name or connection.

Numeric connections allow different widths. Narrowing keeps the leading components and drops
the rest: `float4(x, y, z, w)` → `float2(x, y)`. Widening a vector fills missing components with
zero: `float2(x, y)` → `float4(x, y, 0, 0)`. A scalar still splats to every component of a numeric
vector: `float(x)` → `float4(x, x, x, x)`. Converting a vector to `float` keeps its first component.

### Angle unit contract

Angle units belong to each node/control; wires carry numeric values without implicit unit
conversion. One turn is `360° = 2π rad` (`TAU` radians). Negative angles and values above one
turn are allowed. Numerical trigonometry evaluates in radians internally. Editor labels use
the concise suffix `°`, `rad` or `turn` for the applicable unit, including the selected unit
on Rotate About Axis and the From/To units on Convert Angle.

| Node or control | Unit and default |
| --- | --- |
| `Sine`, `Cosine`, `Tangent` input; `Arcsine`, `Arccosine`, `Arctangent`, `Arctangent2` output | Radians. Hyperbolic functions are numeric functions, not angle controls. |
| `RotateAboutAxis.Rotation` | **Unit**: Radians (`option: 0`, default), Degrees (`1`), Turns (`2`); Rotation=0. |
| `PolarCoordinates.Angle` | Turns, default 0, in both To Polar and From Polar. The polar angular coordinate also uses turns. |
| `Scatter.Rotation`, `RotationRandom`, `RotationMap` | Turns; defaults 0, 0.5 and 0. RotationRandom=0.5 gives a symmetric half-turn range. |
| `TileUV.RandomRotation` | Turns, default 0. |
| `Twirl.Strength` | Turns, default 0.25. Apply the full signed strength to radial falloff before trigonometry; never take `frac(Strength)` first. Whole turns still affect the interior of the falloff. |
| `Hue.Offset` | Turns, default 0; replaces the `Degrees` port. |
| `HueRange.Range`, `Feather` | Turns; defaults `(11/12,1/12)` and `1/24`. `abs(Range.y-Range.x) >= 1` selects all hues before saturation gating. Equal endpoints retain the existing behavior: no selection unless Feather is positive. |
| `NoiseSineWave.In` | Cycles, default 0; multiply by `TAU` internally for the sine calculation. Sawtooth/Square/Triangle Wave retain period 1. |

Manual orientation controls remain in degrees: `TransformUV`, `Transform2D`, `Shape`,
`Stripes`, `Wave.Direction`, `Kaleidoscope`, `GradientFactor`, `DirectionalBlur`, `RGBShift`,
grain `grainDirection`, and `RadialBlur` in Spin mode (`RadialBlurSpin.Angle`). Existing
phase controls remain in cycles; `Evolution` is not normalized to a common cycle. Vector
directions, including `vectorDirection`, retain their vector meaning and values.

**Convert Angle** (`ConvertAngle`, **Math/Trigonometry**) replaces `DegreesToRadians`
and `RadiansToDegrees`. It has numeric `In=0` and `Out`, preserving Float1–Float4 width
(Float1 is `Float` in the graph). Its node fields are `inputAngleUnit` (default `Degrees`,
UI **From**) and `outputAngleUnit` (default `Radians`, UI **To**). Both accept `Radians`,
`Degrees` or `Turns`; they are settings, not input ports. Conversion is component-wise
scaling: degrees↔turns by 360, turns↔radians by `TAU`, and degrees↔radians by `π/180`.
There is no wrap or clamp. Identical From/To units bypass exactly, including out-of-range values.

Existing authored values are not migrated or rescaled. Reauthor affected numeric values
in the documented units and replace removed conversion nodes/ports explicitly; the new
defaults apply to newly created nodes.

### Color value contract

Color is Float4 in calculations, output ports, HLSL and SVM. `col` only describes an
input's conversion policy; it is not a semantic tag propagated through a branch.
Conversion into a Color input follows these rules:

| Source | Color result |
| --- | --- |
| `float(x)` | `(x, x, x, 1)` — opaque grayscale. |
| `float2(x, y)` | `(x, y, 0, 1)`. |
| `float3(x, y, z)` | `(x, y, z, 1)`. |
| `float4(x, y, z, w)` | `(x, y, z, w)` — all components, including alpha, are retained. |
| Color | All RGBA components are retained. |

Color values follow ordinary `float4` rules at numeric inputs. Output's `RGBA` input and
Blend's color inputs use the `col` conversion policy. Blend, color samples and Color
Parameter values produce Float4. Sample Curve remains scalar.

Generic math and sampled filters process all active components, including A, without
implicit alpha weighting, alpha preservation or luminance selection based on the source.
Inserting Multiply by 1 or a Reroute cannot change a downstream algorithm. Color-specific
nodes may define fixed RGB/alpha behavior explicitly; the incoming value's origin never
selects it. Resource decoding and final layer compositing retain their own contracts.

**Premultiply** and **Unpremultiply**, under Color/Alpha, make alpha weighting explicit:
`Premultiply(c) = float4(c.rgb*c.a, c.a)`; Unpremultiply divides RGB by nonzero A and
returns RGB=0 at exactly A=0, retaining A in both cases. There is no epsilon or range clamp.
For alpha-weighted averaging use `Premultiply → Blur → Unpremultiply`. Final Output expects
straight RGBA. `col` performs no Linear/Gamma/Perceptual conversion.

Dynamic math normalizes Color to `float4` before choosing the widest numeric operand width.
Scalars splat to that width; shorter vectors are zero-extended. The Color destination's
alpha-one defaults do not apply in math. For example, Add of Color `(r, g, b, a)` and `float(x)`
uses `(x, x, x, x)`, and Add with `float2(x, y)` uses `(x, y, 0, 0)`. Dynamic numeric results use
numeric types, not Color; operations with fixed scalar or Boolean results retain those results.

Unconnected Color values store linear RGBA; their picker displays encoded RGB
and converts edits back to linear RGB without changing alpha. Color parameters retain their
existing encoded storage and color preparation into linear working values.
Connecting Color and numeric ports does not insert a color-space conversion, and numeric
vectors do not receive color preparation merely because they have four components. Use Split
and Combine for explicit component selection and assembly. Texture, Gradient and Curve require
their Sample operation before numeric use; Transform2D uses Apply Transform 2D. Boolean conditions
remain distinct from numeric and resource ports.

Unconnected numeric input fields sit outside the node body, to the left of their ports, leaving
the port dots unobstructed for wiring. These fields already existed but were hidden by `Node`'s
built-in inline clipping; they are now visible and editable. Connecting an input hides its field; disconnecting it
restores the field. Single Float fields use an X component label; dragging it adjusts the value
through Unity's native numeric control. Unconnected UV ports retain implicit `PixelUV` coordinates. Their local
**Custom / UV** option selects an explicit numeric value or the implicit current-pixel UV;
Custom fields follow the same external placement and connection visibility rules. These are
editor controls over the existing input semantics, not changes to graph math or storage.

Visible inputs that do not apply to the selected mode are disabled together with their fields:
warp Octaves, Lacunarity and Gain with Fractal None; mode-specific Fractal ports
are hidden when unconnected and retained inactive when connected;
Shape Radius outside Rounded Rectangle; Stripes Width outside Square; GradientFactor
Angle in Radial mode. A tooltip explains the dependency. Split outputs beyond the input width
are disabled. Connection candidates and create-connected-node search exclude inactive ports.
Changing modes preserves values and wires for ports that remain present; reactivating a port
restores editing. This editor availability state adds no serialized fields or evaluation rules.

## Notes, groups and node messages

`ShaderGraph.notes` and `groups` are optional arrays (empty by default, at most 512 each).
A note stores `id`, `title`, `contents`, `position`, `size`, `theme` (0 Classic, 1 Black) and
`fontSize` (0 Small, 1 Medium, 2 Large, 3 Huge), and `hueShift` (−180…180 degrees, default 0).
The Hue context action opens a rainbow slider without a label. It rotates the native background,
outline and text palette in HSV, retaining each color’s saturation, value and alpha plus non-color
styles. Zero removes overrides entirely; changing the native note theme retains the shift and
applies it to the new theme’s palette. Selection and resize affordances keep their standard colors. Note selection uses the native node’s
2-pixel outward gap and 8-pixel corner radius, measured in Unity 6000.7. A group stores `id`, `title`, `position` and
`elements`: IDs of nodes or notes. IDs are unique across all three element kinds. Each element
belongs to at most one group; groups cannot contain groups. Deleting a member prunes its IDs.
Deleting the group only removes its frame. Notes use native StickyNote sizing and editing;
groups use native GraphView Group layout. Note text editors copy the visible label’s measured
rectangle and typography into the native TextField and nested input, preserving the text area and
font size when entering edit mode or typing longer content. Both are created through separate context-menu actions,
not node search. They support document Undo/Redo and clipboard; copying a group expands its
members, remaps their IDs and preserves internal edges. Notes can be copied independently.
Annotations are excluded from shader and render fingerprints and do not request compilation or
rendering when edited. TIFF and document JSON retain their layout and text.

Shader diagnostics retain node ID, text and Info/Warning/Error level, including Unity compiler
warnings. Emitted node expressions and HLSL modules map source locations to node IDs. A node's
compile, preview and function-draft messages share a badge at its right, using the highest severity.
Badge shapes and their symbols use Painter2D paths instead of raster icons, scaling with graph zoom.
Only hovering that badge opens the message panel to its right. Preview errors no longer add text
below the preview, and node errors do not repeat in the bottom message area. Global compilation
errors remain in the graph message area. An unsuccessful compile retains the last valid layer
program; an unsuccessful preview clears its image.

Diagnostic snapshots belong to their graph fingerprint and source revision. Rewiring or
changing a preview output invalidates obsolete messages even when that preview is collapsed
or offscreen; superseded requests cannot publish into the new branch. Function-operation and
Height-presentation messages expire independently. Restoring a working executable records its
own fingerprint, while retaining an already verified failed draft and its retry suppression.
Undo restores serialized diagnostics without retaining the later draft's transient node mapping.

## Node clipboard

Native GraphView Copy/Paste/Cut/Duplicate commands use a session-scoped JSON fragment.
The canvas also handles Ctrl/Cmd+C/V/X/D key events through those same native callbacks,
so fresh clipboard operations do not depend on Editor command forwarding after script reload.
Handled keys are consumed once; text fields and active pointer gestures keep their own input.
The fragment includes selected nodes, edges whose endpoints are both selected, required
parameter values, functions (including drafts and editor sizes), and their include closure.
Sub Graph instances also carry their referenced definitions and nested definition closure,
including required parameters and HLSL sources. Same-graph paste reuses existing definitions;
cross-graph paste imports definitions and remaps node/reference IDs while retaining interface IDs.
Output and definition-only interface nodes are not copyable. Text-field commands retain normal text editing behavior.

Paste assigns new node/function IDs, remaps internal edges, and selects the inserted nodes.
In the same graph, compatible Parameter IDs are reused; otherwise parameters are copied
with new IDs and unique display names. Unusable layer texture references are cleared, including
references across documents. Unity texture object references are retained within the Editor session.
Includes are deduplicated by ID and matching content/bindings; conflicting content is rejected.
The merged graph is validated before one document Undo mutation, so invalid data or capacity
limits do not partially insert nodes. Copy records the combined laid-out rectangles of nodes,
notes and group frames in graph coordinates. Paste and Duplicate align that rectangle's center
with the current viewport center, accounting for pan and zoom. Repeated paste uses the same
centering rule without an accumulating offset. Duplicate leaves the system clipboard unchanged.
The clipboard survives script reload: its Editor-session marker is retained in SessionState.
An Editor restart invalidates it; this is not a document format.

## Panel workspace

Fresh layouts and Reset Panel Layout create one left/top/bottom-anchored host, 282px wide.
Its vertical split allocates 38% to Parameters/Inspector tabs (Parameters selected) and
62% to Nodes. Existing saved layouts remain authoritative.

Parameters contains declarations and control metadata; the former Values foldout is removed.
Inspector uses the same `ShaderParameterView` and `ShaderGraphParameterViewSource` as
Layer Settings, with the same inspector content styling and label/value columns.
Vector components stay on one row at every panel width. The graph Inspector uses a
88px label column instead of the native 120px column and 4px side padding; Layer Settings and FX keep their
existing label widths and native component-label sizing.
Linked presets keep declaration editing disabled while their parameter values remain editable.
Parameters, Nodes and Inspector retain their content in floating hosts, grouped as native
TabView tabs or vertically/horizontally split sections. All headings use the same native
Tab presentation and inline ellipsis menu, without a second group title. The menu hit area
is square, sized from the native tab row height. Drag free header space to move hosts, drag tab/section
headers to detach, and drop onto another header/body to combine tabs/sections. Layer
Preview and tool panels share `ShaderGraphFloatingPanel` placement, resize, bounds
clamping and edge snapping. Resizing to an edge also anchors it; opposing
anchors stretch to the viewport height/width. Section split ratios persist independently of
viewport size. Pane rearrangement does not rebuild the canvas or mutate the graph.
Layout nodes are either tab stacks or horizontal/vertical groups with an arbitrary-length
child list and normalized size weights. Children can be either kind, enabling mixed nesting.
Native TwoPaneSplitViews render an n-way group without imposing binary grouping on its model.
Nodes are serialized in a flat ID table; host roots select independent floating windows.
Contents are registered by panel ID, so docking does not depend on three specific panels.
Validation removes cycles, duplicate references and empty groups, and restores missing registered panels.
Drop targets distinguish a leaf pane from an enclosing group's outer edge. Menu actions are
scoped to that group; detaching one tab preserves siblings, while Detach Group preserves its subtree.
Escape restores the layout captured before detaching. The workspace is retained
in Editor-window state and user preferences, outside TIFF/JSON.
The creation drop target rejects floating panels, including preview surfaces.

## Node movement and deletion

Select at least two nodes and choose **Arrange Selected** from the context menu.
The command orders connected nodes left to right, uses their laid-out bounds
including external input controls, and avoids unselected nodes and notes. Nodes
from different groups are placed separately. Only the selected node positions
are written, with one Undo step and no shader compilation request.

The same arrangement runs on new nodes with omitted positions in agent-authored
Shader and FX `draftGraph` edits, including `program.draftGraph` and each Sub Graph.
The API estimates node and port bounds from data and can run with the editor
window closed. Explicit coordinates, including `[0, 0]`, are fixed. Omission for
an existing ID retains its current position in that scope. Raw document and
preset deserialization retain their normal position defaults.

The **Nodes** sidebar tab uses the same creation catalog and mutation dispatch as
**Create Node**, in a virtualized tree with native row/foldout styles, category/name search and the
`DDXY` alias. It includes built-ins, current parameters, discovered HLSL functions
and non-recursive Sub Graph definitions, respecting **SVM Nodes Only**. A leaf starts
an Editor drag after the existing five-point drag threshold. Dropping on the canvas
converts panel coordinates through `contentViewContainer.WorldToLocal` and uses the
ordinary creation/Undo path. A click or cancelled drag creates nothing. Stale drags
from another window, graph scope, removed choice or changed filter are rejected;
linked/locked graphs remain read-only. Selected tab, search and expanded categories
are Editor-window state, not shader program data. Browsing does not compile shaders.

A palette drag builds the shared node view against an isolated candidate type cache.
Its ports, controls and color mode follow the normal node presentation, with no live
NodePreview, graph registration or document mutation. The root overlay ignores
picking, follows the canvas scale, and uses the actual title center as its grip.
Drop coordinates preserve its displayed position; cancellation, invalidated drag
data, window rebuild and disposal remove it. Only an accepted canvas drop invokes
the existing creation dispatch and Undo operation.

Nodes and the search dropdown share `WhimTexVirtualList`: known row heights form
an exact cumulative layout before offscreen rows are visited. A public ScrollView
holds only a recycled viewport-sized set of row elements. Expanding, collapsing,
filtering and switching Compact/Thumbnails rebuild the height table; scrolling uses
binary search and never measures the full catalog. Category return and display-mode
changes restore the first visible item and its within-row fraction, rather than an
estimated pixel offset.

The shared static atlas also contains native-rendered examples for Fractal, Scatter,
Range Mask, Step, Smoothstep, One Minus, Power and Blackbody. Their catalog-only
inputs emphasize repeated scales, scattered stamps, range boundaries and value/color
transforms. Recipes are in `src/Editor/NodeThumbnails/README.md`; node creation defaults
and graph execution do not depend on them. Browsing only reads the shared atlas.
The same recipe source covers all four Math/Wave kinds, Sphere Mask, Combine
Distance, Sample Gradient and Sample Curve. Wave examples use three periods and an
explicit signed-to-0..1 Remap for display; the distance union displays its Mask
output. Curve and gradient resources belong to the temporary example graph.
Sample Curve, Power, Smoothstep, Triangle Wave, Range Mask, Step, Sawtooth Wave,
Square Wave, Absolute and Saturate bake a green curve overlay from 256 scalar
samples of the same native HLSL, before display gamma. Absolute samples -1..1;
Clamp and Saturate sample `0.5 + 0.75 sin(2πx)`, clipping at 0.22/0.78 and 0/1 respectively.
Saturate's stroke stays half a display pixel inside the image, retaining a full green edge pixel at 32×32.
Discontinuous curves mark jumps vertically, without
interpolated slopes. These ranges only describe the catalog examples. Smoothstep's
catalog-only range is 1/32..31/32, retaining one horizontal display pixel per edge.
The overlay and its extruded padding belong to the atlas; browsing adds no curve
sampling, rendering or compilation.
Square Root, Exponential, Log, Soft Sign and Hyperbolic Tangent use the same curve
baker. The two signed examples mark zero output. Fresnel Effect uses sphere normals,
Distance uses a centered UV point, and each derivative evaluates the same angled shape.
The alpha examples display RGB over a checker that becomes visible near zero coverage.
Set Alpha shows the actual replaced alpha over the same checker. Convert Color pairs
the original table with raw HSV channels displayed as RGB. The Sine, Cosine and
Inverse Soft Sign curves share the zero guide. Reflection and Refract draw their
output vectors against the same incident ray and medium boundary.
The atlas includes coordinate, sampling, scatter, lighting, vector and logic examples
plus mathematically sampled transfer curves. The native bake binds a temporary
point light or a rotated layer when a recipe needs that context.
The signed rounding examples share a scale and an identity guide. The Tangent
stroke breaks across its pole. See the recipe table in the atlas README.
The And, Or, Not, All and Any recipes evaluate circle masks through the matching
logic nodes. All and Any use three components, with a negative nonzero third mask.
Constant, Value, Is NaN and Is Infinite bake π, 1.0, NaN and ∞ directly into the atlas.
UV displays red/green coordinates inside the rotated layer rectangle over a muted canvas
grid; Canvas Position fills the tile with normalized canvas coordinates in red/green.
The atlas also includes static diagrams for channels, matrices, dimensions, lighting,
and loop endpoints. The built-in Custom Function gets a code thumbnail. External
HLSL function entries retain their compact icons, including when creating from a port.
Sample Texture and Sample Input share a full-tile landscape based on Texture Size.
Lerp and Inverse Lerp use opposite funnel diagrams between a narrow grayscale
factor and a wide color ramp. Remap links two offset ranges on colored ramps.
Recycled catalog images explicitly invalidate their repaint when the texture or
UV rectangle changes; property assignment alone can leave old atlas geometry on screen.

**Create Node**, creation from a port and **Replace Node** use the shared UI Toolkit search
dropdown. Replacement opens the current category within the complete creation hierarchy;
Back returns to its parent. The current node has a subtle highlight, independent of the
selected row. Search covers all leaves, matches every word and prioritizes name prefixes.
Arrow keys navigate, Enter selects, and Escape clears search before closing. The node ID,
position, preview expansion, selection and group membership survive replacement. Node-specific
options start at their creation defaults. Input/output connections match direction, exact
ordinal port name and resolved value type; numeric conversions do not count as equal types.
Sub Graph pins match their declared names and types rather than their definition-specific IDs.
Ambiguous names are not mapped. Compatible inline inputs are copied; unmatched edges are removed.
The complete replacement is one native Undo operation. Required Output and Sub Graph interface
nodes are excluded, recursive subgraphs are rejected, and stale picker targets do not mutate
another graph or a newer node. This applies to Shader-layer and FX-hosted graphs.

Replacement by Reroute uses `ShaderGraphNodes.ReroutePorts` as a separate connection
mapping. A definition can declare its pair in the `reroutePorts` table. The default
is its first active non-option input and first current output, including custom
function and Sub Graph port order. Control-first layouts override this convention,
for example Step/Smoothstep use In → Out, sampling uses UV or T → Out, and
Apply Transform2D uses UV → Local. Time and other source-only definitions declare
no main input. The selected input edge maps to In and all compatible fan-out from
the selected output maps to Out. Other incident edges are removed. Actual upstream
type determines the Reroute type, and compatibility is checked through downstream
Reroute chains. Incoming open Reroutes stay unbound. Reroute has no inline literal,
so an unconnected main input leaves a visible break and consumers use their own
saved or default values. This mapping is node-definition metadata, outside the
document model and shader compilation fingerprint.

### Shared editor search dropdown

`WhimTexSearchDropdown` is an internal reusable UI Toolkit popup, independent of GraphView.
Build an `Item` tree (groups with `children`, leaves with `data`), then call `Show` with
the screen anchor, root and selection callback. A callback returning `true` closes the
popup; `false` keeps it open. Set `highlighted` for a subtle persistent marker and
`enabled` for availability. Icons are optional. The normal keyboard/hover selection
remains separate from persistent highlighting.

`initialPath` is a sequence of category names without the root. `OpenPath` also accepts
a slash-separated path; use the sequence overload for names containing a slash.
Opening a category preserves its ancestors. Invalid paths leave the current page intact;
an invalid initial path falls back to the root. `owner` and `valid` invalidate a popup
when its owning object or editing context disappears. Loss of focus or script reload
closes it. The tree is an opening-time snapshot and must not be mutated while visible.

The node adapter in `ShaderGraphWindow.Search.cs` uses the same creation catalog and
selection callbacks for normal creation, creation from a port and replacement. Native
theme GUIStyle metrics and textures supply its appearance, while layout, events and
the 250 ms category slide are UI Toolkit. It does not reflect into Unity internals.

**Del** deletes selected nodes, including Reroutes with the reconnect behavior described below.
Output remains required and cannot be deleted. When a text or numeric field is being edited,
**Del** edits the value without deleting the node.

During dragging, a weak Y-only snap aligns the centers of connected input/output connectors
within a radius of 4 logical screen pixels, accounting for graph zoom. Native Y snapping
between node rectangles takes priority; connector snapping never changes X. **Shift** disables
snapping. A multi-node drag applies the same adjustment to all moved nodes, preserving their
relative positions.

## Reroute connections

Double-clicking an existing edge inserts a Reroute at that location, replacing the edge with
source → `In` and `Out` → destination connections. The edge context menu offers **Add Reroute**
with the same behavior. In the editor, Reroute is created only on an existing edge and is
absent from node search.

Reroute is a movable mini-node with no header, preview or parameter controls. Its `In` → `Out`
path forwards the source's exact type and value: `float`, `float2`, `float3`, `float4` (including Color values), Boolean,
Texture, Gradient, Curve or Transform2D. It adds no HLSL calculations, casts or resource sampling.
The output can feed multiple inputs; a chain of Reroutes preserves the same source value.
Both connectors remain pickable without a label, so dragging a port starts a connection while
dragging the body moves the node. A chain without a source acts as a disconnected input without
a diagnostic; each consumer uses its own saved/default value. Nodes and wires remain stored.

Deleting a Reroute reconnects its incoming source directly to each downstream input, preserving
branches and the remaining Reroute chain. Ordinary edge deletion removes that connection
without reconnecting its endpoints. Node positions and connections persist with the graph;
insertion, movement, connection edits and deletion support Undo/Redo.

## Coordinate and output contract

UV supplies the current pixel's local layer coordinates, with the ordinary image domain at
`0..1`. Canvas Data describes document dimensions; Texture Size describes a selected texture.
Both expose `Size` (width, height) and `Texel` (reciprocal width, reciprocal height) as `float2`.
Canvas Data also exposes `PixelScale` (`float`), the document's pixel multiplier configured by
the Frame tool. These values do not change with graph zoom or preview resolution.
Canvas Position has no inputs and three `float2` outputs: `Normalized`, `AspectX` and `AspectY`.
Normalized is the current document position, with `(0,0)` at the lower-left canvas corner and
`(1,1)` at the upper-right. Aspect X scales only X by width/height; Aspect Y scales only Y by
height/width. For a 2:1 canvas their ranges are `(0..2,0..1)` and `(0..1,0..0.5)` respectively.
All outputs share the same origin and are unclamped for evaluations outside the canvas.
Layer/group transforms do not move this coordinate field. Connected Canvas Position branches
evaluate in the destination canvas with layer Clip/Repeat/Mirror/Clamp applied to local UV;
Canvas Position itself stays continuous across repeated tiles. Sampled branches reevaluate the
canvas position at the requested neighboring local UV. Node previews use an untransformed canvas.
Transform UV changes coordinates for subsequent calculations, independently of the layer's
ordinary Transform. Sampling uses the supplied coordinates.

Output receives RGBA through a fixed Color input. Its horizontal terminal layout places the
inputs on the left and the Output / Layer result title on the right, with a rounded end cap.
The Float `AlphaScale` input (displayed as **Alpha Scale**) defaults to 1 and multiplies only
RGBA.a: `float4(rgba.rgb, rgba.a * alphaScale)`. It accepts a literal or connected mask,
without clamping. Numeric RGB gains alpha 1 through the Color input. This multiplication
affects the final graph result, not upstream node previews. It has no per-node preview; Layer Preview shows the layer result.
RGB is straight, not premultiplied by alpha. The graph adds no blanket
`0..1` clamp to its numeric results: HDR and negative values remain available, subject to the
selected operation and the normal layer Rendering settings. Alpha retains the existing layer
coverage semantics. Lerp interpolates numeric components; Blend performs color compositing
with the chosen blend mode and alpha handling.

## Sampled branch filters

The **Sample Distance** input label identifies fixed-stencil spacing on Height to Normal,
Height to Distortion, Height Curvature, Normal Curvature, Edge Detection and Adaptive Sharpen.
It retains the `Radius` port ID in graph data and code; ordinary Sharpen displays Radius.
Only the label changes: defaults, units, calculations and connections remain the same.

`Blur`, `Sharpen`, `EdgeDetection`, `Dilate` and `Erode` take numeric or Color `In`,
Float2 `UV` (current-pixel UV by default) and Float `Radius` (default 1). Sharpen and
EdgeDetection also take Float `Strength` (default 1). Each has one `Out` with the same
numeric type as In (Color values are Float4). All components, including alpha, are processed
independently. Alpha-weighted averaging requires explicit Premultiply and Unpremultiply.
Resource and Boolean inputs
must be converted to a numeric/Color branch first.

The compiler emits an HLSL function for the upstream branch and calls it at `UV + offset`.
This reevaluates UV-dependent calculations, including procedural generation and texture
sampling. It does not allocate intermediate GPU textures or add rendering passes. Explicit
constant coordinates in the branch remain constant. Nested filters reevaluate their upstream
filters for each sample and can be expensive even when generated function code is shared.

Radius is measured in **document pixels**, using reciprocal document dimensions for UV offsets;
it is not measured in a bound texture's texels. Node previews and layer renders use the same
pixel metric; reducing preview resolution does not enlarge the filter footprint. Canvas Data
also reports document dimensions. `_InputSize` remains the actual render-target size for HLSL.
Ordinary filtering uses absolute Radius; at zero, Blur/Sharpen/Dilate/Erode return In and EdgeDetection
returns zero in every component.
UVs are not implicitly clipped; upstream texture samplers determine edge behavior.

| `kind` | Kernel and result |
| --- | --- |
| `Blur` | Normalized finite Gaussian kernel; constant inputs stay constant. |
| `Sharpen` | By default, `input + (input - blur) * Strength` using the same Gaussian kernel. `adaptiveSharpen:true` selects the local-contrast path below. Alpha is processed like any other component. |
| `Dilate` | Component-wise maximum over the kernel. |
| `Erode` | Component-wise minimum over the kernel. |
| `EdgeDetection` | Fixed 3 × 3 Sobel gradient magnitude, multiplied by Strength. Every active component is processed, including alpha. No implicit luminance conversion. |

For Blur, ordinary Sharpen, Dilate and Erode, **Quality** uses `option`: **0: Standard**
(default), **1: High**. Samples span `[-abs(Radius), abs(Radius)]` in each axis.
Standard's half-extent is `max(1, ceil(abs(Radius)))`, with at least **3 × 3** samples and a step
at most one document pixel. High doubles that half-extent, halving the step and inserting
samples between Standard positions without changing the area. At Radius 1 the grids are
3 × 3 and 5 × 5; at Radius 4 they are 9 × 9 and 17 × 17. Large radii increase sample count quadratically, and nested filters multiply
that cost. Radius 0 preserves the input. EdgeDetection always uses its 3 × 3 stencil.
None of these nodes clamps its result to `0..1`.

Sharpen's bottom **Adaptive** checkbox stores Boolean `adaptiveSharpen`, default false.
True uses a fixed 3 × 3 local-contrast calculation while retaining In, UV, Radius and Strength. Radius
sets the sample spacing in document pixels; Strength controls the correction. Quality is hidden
and ignored while Adaptive is enabled; its value is retained. Radius and Strength clamp
below at zero. The four axial neighbors supply the mean; all nine samples supply component
min/max. With `contrast=saturate((max-min)/max(abs(max)+abs(min),1e-6))`, the correction is
`(center-mean)*Strength/(1+Strength)*(1-contrast)`, then bounded to the local component
min/max. Every component, including alpha, uses the same calculation. False preserves the ordinary Gaussian
path and its kernel behavior. Adaptive Sharpen is a setting on Sharpen, not a separate kind
or an input port.

Built-in antialiasing in sampled Shape/Combine Distance masks, Checkerboard, Stripes, Grid
and Rings uses centered field differences over the render pixel's UV footprint. The entry
point captures that footprint before filter loops; connected UV transforms and field inputs
are reevaluated at the neighboring coordinates. Thus a spatially varying Radius does not
require hardware derivatives inside divergent loops. Direct, unfiltered evaluation keeps its
hardware derivatives. This adds field evaluations to sampled antialiasing, including nested
branches. Custom HLSL retains its own derivative semantics: `SampleTexture`/`SampleTextureLod`
can be sampled in these loops; raw implicit-LOD `tex2D`/`tex2Dbias` or explicit derivatives
inside varying control flow still need care in the function's implementation.

## Median and Kuwahara

`Median` (217) and `Kuwahara` (218) take dynamic numeric/Color `In`, Float2 `UV` (current
pixel by default), and Float `Radius` in document pixels, clamped below at zero. In defaults
to Float 0; their `Out` keeps In's type.
Both reevaluate the In branch at neighboring coordinates; constant upstream coordinates
remain fixed, and upstream texture samplers retain their own wrap/filter behavior.

Median defaults to Radius=1 and uses `r=max(0,floor(Radius))` document pixels.
Every point of the `(2*r+1)^2` square participates: r=0 preserves the input, r=1 uses
3 × 3 points, r=2 uses 5 × 5, and r=8 uses 17 × 17. Components, including alpha,
are ranked independently, retaining original signed float32/HDR values without quantization.
Component-wise medians can combine values from different samples and remove thin features.

At ordinary root evaluation with default UV, unconnected r>2 or a connected, proven
pixel-uniform Radius uses prepared float32 tiles with a real source halo. A cached 1 × 1
HLSL pass evaluates connected uniform controls using shader arithmetic; changes do not
require recompilation. Value, parameters, Canvas Data, Time and supported pure math can
qualify. Opaque user code, pixel-varying Radius, explicit UV and sampled/iterative contexts
retain direct full-area evaluation. Small direct neighborhoods use sorting; larger ones
use exact bounded-storage selection. Prepared evaluation uses sorted columns and selection
when compute/storage support permits, with exact direct selection as fallback. Scratch
storage is bounded; no fixed sparse grid replaces the requested neighborhood.
Prepared output uses bilinear interpolation on the document grid and Clamp outside the
canvas, so arbitrary off-grid procedural evaluation is not claimed identical. HLSL and
SVM share that prepared boundary. No extra density control is required.

Kuwahara uses node `option` **0: Classic** (default) or **1: Anisotropic**.
`In`, `UV`, `Radius` and `Out` are shared.

Classic defaults to Radius=2 document pixels and uses integer radius
`r = max(0, floor(Radius))`. It evaluates four overlapping dense quadrants, with
`(r+1)^2` points per quadrant, and returns the mean of the quadrant with the lowest mean
variance across active components, retaining the first quadrant on a tie. Float4 includes
all four components in that metric. Radius zero preserves the input. Increasing Radius
adds source points instead of spreading a fixed sample set farther apart.

**High Precision** is stored as Boolean `ShaderGraphNode.kuwaharaHighPrecision`, default
`false`. It applies only to Classic and is retained but ignored in Anisotropic. It is a
node setting, not an input port; `In`, `UV` and `Radius` are unchanged.
When `r > 5`, Radius is unconnected, UV uses its standard current-pixel source with no
connection (not a literal Value), and evaluation is outside sampled-branch, For, Fractal
and Scatter scopes, the filter prepares a source pass and a separate output pass.
Preparation is rasterized at document resolution. Source tiles include a real `r`-pixel
halo: the input is evaluated beyond the canvas rather than replaced with edge pixels.
Both precision settings use this same source grid. With High Precision off, summed-area
tables of values and squared values supply quadrant means and variances without
enumerating every source point for every output pixel. With High Precision on, the output
pass directly evaluates the dense quadrants from the prepared source instead. It avoids
summed-area accumulation error and can be substantially slower for large radii.

Small radii, connected Radius or UV, literal Value UV, and sampled/iterative contexts use direct evaluation
of the input branch. This fallback is not an error and preserves per-iteration procedural
dependencies; High Precision has no additional effect because calculation is already
direct. Its source work grows with quadrant area and multiplies upstream branch cost.
The prepared result is read with bilinear interpolation between document-pixel centers
and Clamp outside the canvas. The true source halo makes edge-pixel neighborhoods complete;
it does not extend the filtered output beyond the canvas. SAT and direct evaluation of
the prepared source implement the same four-quadrant statistics on that grid; compare
them with a float tolerance rather than bit equality. This does not promise equivalence
to an arbitrary procedural function at off-grid UV or beyond the canvas. Direct
input-branch evaluation retains procedural coordinates and the sampling footprint of its
calling context; preparing the source fixes that footprint to document resolution.

Anisotropic uses eight weighted sectors in an ellipse oriented by a smoothed structure
tensor. Sector means contribute according to their variance instead of selecting a single
axis-aligned quadrant. All active components, including alpha, participate in the filter.
Anisotropic keeps Radius as a nonnegative Float in document pixels, without rounding it
to an integer. It adds these Float inputs, active only in Anisotropic:

| Port | Default | Effective value | Role |
| --- | --- | --- | --- |
| `Sharpness` | 1 | `s=clamp(Sharpness,0,1)`, internal sharpness `16*s*s` | Controls the preference for low-variance sectors. |
| `Uniformity` | 4 | `u=max(0,floor(Uniformity))` document pixels | Structure-tensor Gaussian smoothing radius, with sigma `u/3`; zero leaves the tensor unsmoothed. |
| `Eccentricity` | 1 | `e=clamp(Eccentricity,0,2)` | Ellipse stretch `1+anisotropy*max(0.01,e)`; zero is nearly circular, not an exact isotropic switch. |

The three values use generic `inputs`/edges; the algorithm selector uses `option`.
High Precision does not select another Anisotropic calculation. No new serialization
fields are required for this mode.

At ordinary root evaluation with unconnected Radius, Sharpness, Uniformity and
Eccentricity and standard current-pixel UV (unconnected, not Value), Anisotropic uses a
prepared image at canvas/document resolution, rendered in tiles with the source halo
needed by tensor smoothing and the elliptical neighborhood. Tensor preparation uses
compute and a separable Gaussian. Without compute support, the prepared pixel shader
calculates the tensor directly; the result still follows the prepared-image path.
The prepared output uses the same interpolation and canvas Clamp rule described above.

Without CopyTexture support, or with connected controls/UV, literal Value UV or
sampled/iterative evaluation, the filter uses the native procedural branch. SVM consumes
the legal prepared-image boundary and continues through the existing VM; Anisotropic does
not add a VM opcode. Native fallback preserves per-iteration procedural dependencies.
Do not infer off-grid procedural equivalence or fixed sampling cost from the presence of
an SVM continuation.

Both filters process components directly, including alpha, without color conversion or
automatic alpha weighting. An explicit Buffer can reuse a costly source when its baked
resolution is suitable. Existing SVM work/nesting limits still allow native HLSL fallback.

Anisotropic validation used a 384 x 384 RGBAFloat texture with Sharpness=1, Uniformity=4
and Eccentricity=1. In the recorded run, the maximum absolute error against the CPU oracle
at the tested points (including tile boundaries) was 4.71e-6; sampled HLSL/SVM comparisons
were identical. These checks cover the tested document grid, not arbitrary procedural UV.

| Radius (pixels) | Native prepared render (ms) | SVM hybrid render (ms) |
| --- | --- | --- |
| 4 | 2.248 | 2.041 |
| 8 | 4.066 | 3.950 |
| 16 | 10.211 | 10.090 |
| 32 | 34.903 | 35.189 |

Timings are wall-clock medians of five rerenders after a warm-up, including a 1 x 1 GPU
readback wait for completion. Shaders are already compiled; each source texture update
invalidates the prepared image, so these measure rebuilt passes and the final draw rather
than a cached image read. Texture upload is outside the timer. Native timing starts at
Render after binding; hybrid timing includes RenderBoundaries binding and VM continuation.
They are not shader compilation or full editor-update latency measurements. On this fixture,
SVM continuation has similar cost to native prepared rendering; it does not remove the
elliptical filter's radius-dependent work. Source: shader-kuwahara-anisotropic-benchmark,
local receipt `kuwahara-anisotropic-bench-02.json`; results depend on the machine and graph.

## Supersample and Make Tileable

`Supersample` (214) and `MakeTileable` (215), in **Filter**, take dynamic numeric/Color `In`
(default Float 0) and Float2 `UV`, which
defaults to current-pixel UV. Both return `Out` with the same type as In. They reevaluate
the upstream branch at sample coordinates; explicit fixed coordinates in that branch
remain fixed. A Texture resource must first be sampled into a numeric or Color value.

Supersample averages subpixel evaluations. Its bottom **Quality** selector uses `option`
**0: 2x2** (default), **1: 3x3** and **2: 4x4**, for 4, 9 or 16 source evaluations per output
pixel. Quality is a node setting, not a numeric input port. Samples use centered grid cells
within the captured render-pixel UV footprint, including its X/Y directions; this footprint
can change with preview resolution. There is no neighboring-pixel Radius control.

Make Tileable evaluates four copies of the source, shifted by half a tile, and blends them
over unit UV using quintic weights. Let `q=frac(UV)`, `w(t)=6*t^5-15*t^4+10*t^3` and
`a=w(saturate(min(q,1-q)/BlendWidth))`, with min and w applied component-wise.
The Float input `BlendWidth=0.5` preserves full-tile blending; positive values clamp to 0.5.
Smaller values confine the blend to the edges and leave the center unchanged. Each unshifted axis uses its
component of a; each shifted axis uses one minus that component:

| Sample coordinate | Weight |
| --- | --- |
| `q` | `a.x*a.y` |
| `frac(q+(0.5,0))` | `(1-a.x)*a.y` |
| `frac(q+(0,0.5))` | `a.x*(1-a.y)` |
| `frac(q+(0.5,0.5))` | `(1-a.x)*(1-a.y)` |

All four source coordinates stay in `[0,1)`. On each axis the unshifted weight reaches
zero at the tile edges, suppressing the original seam; the shifted weight reaches zero
at q=0.5, suppressing the shifted copy's wrap cut. This works with both Clamp and Repeat
source textures. The weights sum to one. With `BlendWidth<=0`, the unshifted weight
is one: the source repeats without seam correction. UV outside `0..1` repeats the result. Out contains sampled values. Crossfading can
duplicate or fade recognizable shapes; inspect the tiled result for ghosting.

Both nodes process components independently, including alpha. Both have native HLSL and SVM paths.
Repeated branch evaluation, including nested sampled nodes, counts toward the existing
SVM branch work/nesting budget; exceeding it uses native HLSL fallback. Neither node
implicitly creates a Buffer. An explicit Buffer can reuse a costly source when baking
it at the chosen resolution is acceptable.

## Flow Warp and Truchet

| `kind` | Inputs and defaults | Output |
| --- | --- | --- |
| `FlowWarp` (216) | UV: Float2=current-pixel UV; Flow: Float2=(0,0); Strength=16 document pixels; Steps=8 | Out: Float2 UV |
| `Truchet` (220) | UV: Float2=current-pixel UV; Scale: Float2=(8,8); Width=0.1 cell units; Feather=0; Seed=1337 | Out: Float mask; Distance: signed Float |

Flow Warp integrates the unnormalized Flow branch using midpoint steps. Steps is rounded
and clamped to `1..32`; each step evaluates Flow twice. For the UV position p and
`h=Strength/Steps`, first evaluate `v=Flow(p)`, then evaluate Flow at the midpoint
`p+0.5*h*v/CanvasSize`, and advance p by that midpoint vector times `h/CanvasSize`.
This costs `2*Steps` field evaluations, up to 64. Strength is signed; negative values
reverse the integration direction. Vector magnitude affects travel distance. A zero Flow
or zero Strength preserves UV. Out is the integrated UV position, not a normalized vector.
Connect it to a downstream texture or procedural sampler. A fixed Flow is still a valid
constant displacement field; spatially varying fields must depend on the sampled UV.

Truchet generates a seeded cell pattern, with `option:0` selecting **Arcs** (default)
and `option:1` selecting **Lines**. Scale controls the cell density along each UV axis,
Width controls motif thickness in cell units, and Seed changes cell orientations.
The bottom selector is **Shape**. Each `floor(UV*Scale)` cell uses a hash with Seed to
choose orientation over the unbounded cell grid; there is no explicit Period setting or
guaranteed repetition over unit UV. Positive Width uses the shared distance-mask
antialiasing: fwidth at the root, explicit neighboring evaluations in replayed branches.
This accounts for the rendered pixel footprint and transformed UV; Width<=0 gives an empty mask.
Feather is a nonnegative full transition width in cell units, centered at Distance=0:
`edge=max(pixelFootprint/2, Feather/2, 1e-7)` and `Out=1-smoothstep(-edge,edge,Distance)`.
Negative Feather acts as zero. Distance is the signed motif field before smoothing,
negative inside, zero at the boundary and positive outside; it is independent of Feather.
Distance alone skips antialiasing evaluations. Both outputs share the same field calculation.
It generates the field directly and does not
sample an upstream image branch. Both nodes have native HLSL and SVM implementations;
Flow's repeated branch work is subject to the existing SVM work/nesting fallback.

## Pixelate UV, color adjustments and path blurs

| Category / kind | Inputs and defaults | Output |
| --- | --- | --- |
| UV / `PixelateUV` | UV: current pixel; CellSize: Float2=(8,8); Offset: Float2=(0,0) | Float2 |
| Color/Adjustments / `ChannelMixer` | Color; Red: Float3=(1,0,0); Green=(0,1,0); Blue=(0,0,1) | Float4 |
| Color/Adjustments / `WhiteBalance` | Color; Temperature=0; Tint=0 | Float4 |
| Filter / `DirectionalBlur` | In: dynamic numeric/Color; UV: current pixel; Radius=8; Angle=0 | Same as In |
| Filter / `RadialBlur` | In: dynamic numeric/Color; UV: current pixel; Center: Float2=(0.5,0.5); Angle=10; Strength=0.1 | Same as In |
| Filter / `RGBShift` | In: Color; UV: current pixel; Amount=2 document pixels; Blend=1; Center: Float2=(0.5,0.5); Falloff=1; Angle=0 degrees | Float4 |

Pixelate UV snaps to cell centers using `(floor((UV*CanvasSize-Offset)/size)+0.5)*size+Offset`,
then divides by CanvasSize. CellSize and Offset use document pixels, independently of preview
resolution. Each effective cell dimension is `max(abs(CellSize),1)`; coordinates are not clamped.
Channel Mixer computes three dot products with its coefficient rows; 1 is full contribution,
weights may be negative, and neither weights nor output are normalized or clamped. Alpha is retained.
White Balance applies Bradford chromatic adaptation in linear RGB. Temperature and Tint clamp
to -1..1; zero is neutral, positive Temperature warms, positive Tint shifts toward magenta.
The source white uses `x=0.3127-Temperature*0.05`,
`y=2.87*x-3*x*x-0.27509507+Tint*0.05`, adapted to D65. RGB remains signed/HDR and alpha is retained.

RGB Shift (`RGBShift=167`) reevaluates Color `In` at channel offsets. The `option` values are
0=Radial (default), 1=Directional and 2=Triangle. Radial uses Center and Falloff in
normalized UV space. Directional uses Angle and constant offsets. Both modes shift red
and blue in opposite directions and take green from the original coordinate. Triangle
samples red at Angle, green at Angle+120° and blue at Angle+240°, with Amount as the
radius in document pixels. The offsets are computed in pixel space before converting to UV.
Angle rotates the triangle. Center and Falloff are active only in Radial, and Angle is active
in Directional and Triangle. Unconnected inactive inputs are hidden. Connected inactive ports
stay visible and dimmed, retaining their values and connections. Discovery exposes input
activity for each mode in `modes[].inputs`.

Shifted coordinates clamp to the document's outer texel centers. Alpha comes from the
original coordinate in every mode. Amount<=0 or Blend<=0 is identity. Blend clamps to
0..1, while RGB values remain signed/HDR. SVM previews use native HLSL fallback without
an intermediate texture. Discovery exposes `sampledInputs:["In"]`, fixed Float4 output and
`amountUnits:DocumentPixels`. JSON writes `RGBShift` and also reads the previous node
kind spelling `ChromaticAberration`.

Directional Blur samples a symmetric line over ±Radius document pixels at Angle degrees.
Radial Blur's `option` is 0=Spin (default), 1=Zoom. Spin samples ±Angle degrees around Center
in document-pixel space, so rectangular documents retain circular rotation. Zoom samples radius
factors `1 ± abs(Strength)`. Angle is inactive in Zoom and Strength is inactive in Spin; inactive
branches do not affect the compiled output. A zero amount or the radial center returns the input.
Both use normalized Gaussian weights over a single path, reevaluate the connected branch without
an intermediate texture, and preserve its numeric type. All components, including alpha,
are processed independently. Node setting `filterQuality` is `Standard` (default) or `High`,
independent of Radial Blur's `option`; the old Samples input is unsupported.
Let L be the half-path length in document pixels: `abs(Radius)` for Directional,
`length((UV-Center)*CanvasSize)*abs(radians(Angle))` for Spin and
`length((UV-Center)*CanvasSize)*abs(Strength)` for Zoom. Standard uses
`extent=max(1,ceil(L))`; High doubles this extent. Both evaluate `2*extent+1` points
at t=i/extent, including both endpoints and the center, with weight `exp(-2*t*t)`.
High retains Standard's positions and halves spacing over the same path. Radial density
is computed per pixel. There is no fixed sample cap; longer paths increase work.
Spin visits symmetric pairs, shares their weight and rotates by recurrence, with exact
anchors every 32 steps to bound drift. This keeps the same sample set and weights.
An explicit Buffer can avoid reevaluating expensive upstream procedural branches;
its resolution, filtering and wrap still determine the sampled image. Neither blur
inserts one automatically. Sampling retains explicit AA footprint rules, HDR and
unclamped components. Native HLSL fallback and downstream SVM continuation remain available.

## Height blending, tiling and surface detail

These nodes use the exact output names below. In particular, Height Blend, Tile UV,
Perspective UV and Gabor Noise have no `Out` port. Unconnected UV inputs use current-pixel UV.
Scalar inputs are Float unless otherwise stated.

| Category / kind | Inputs and defaults | Outputs |
| --- | --- | --- |
| Height / `HeightBlend` | Bottom=0; Top=1; Offset=0; Smoothness=0.1; Opacity=1 | Height: Float; Mask: Float |
| UV / `TileUV` | UV: Float2; Count: Float2=(4,4); Offset: Float2=(0,0); RowOffset=0.5; RandomRotation=0; RandomScale=0; Seed=1337 | UV, Cell, Center: Float2; Random, Mask: Float |
| UV / `PerspectiveUV` | UV: Float2; BottomLeft=(0,0); BottomRight=(1,0); TopRight=(1,1); TopLeft=(0,1), all Float2 | UV: Float2; Mask: Float |
| Procedural/Noise / `GaborNoise` | Dimension-width Position, Scale, Period; Frequency=2, Bandwidth=1, Anisotropy=0.8, Direction along X; shared noise inputs | Value, Phase, Intensity, Phasor: Float; Gradient and 2D/3D Curl at the selected width. See [WT noise nodes](#wt-noise-nodes). |
| Math/Vector / `RotateAboutAxis` | In: Float3=(0,0,0), Axis: Float3=(0,0,0), Rotation=0. Supply a nonzero Axis. | Out: Float3 |
| Color/Generation / `Blackbody` | Temperature=6500 | Out: Float4 |
| Filter / `SlopeBlur` | In: dynamic numeric/Color; UV: Float2; Slope=0; Strength=8; Steps=16 (port key `Samples`) | Out: same type as In |
| Filter / `BilateralBlur` | In: dynamic numeric/Color; UV: Float2; Radius=2; Tolerance=0.1; Custom mode adds Guide: dynamic numeric/Color, default Float=0 | Out: same type as In |

| `kind` | Serialized `option` |
| --- | --- |
| `TileUV` | 0: Grid (default); 1: Staggered |
| `RotateAboutAxis` | 0: Radians (default); 1: Degrees; 2: Turns |
| `SlopeBlur` | 0: Average (default); 1: Min; 2: Max |
| `BilateralBlur` | 0: Source (default); 1: Custom |

### Height Blend

Height Blend compares Bottom with `top = Top + Offset`. With nonnegative Smoothness `s`,
`highest = max(Bottom, top)`, `a = max(top-highest+s, 0)` and `b = max(Bottom-highest+s, 0)`.
The top weight is `a/(a+b)`; at effectively zero Smoothness, the higher height wins and ties
use `0.5`. `Mask` is that weight multiplied by `saturate(Opacity)`; `Height` is
`lerp(Bottom, top, Mask)`. Use Mask for a matching color/material blend. Signed and HDR heights
remain available: Height is not clamped to `0..1`. Offset uses the same units as the heights.

### Tile UV and Perspective UV

Tile UV creates cell-local coordinates. Count uses rounded absolute components with a minimum
of 1. Offset is measured in cells, and RowOffset shifts alternating rows only in Staggered mode.
RandomRotation gives the symmetric random angle range in turns. RandomScale clamps to `0..1`,
interpolating from scale 1 at zero to per-cell scales in `[0.5,1.5)` at one.
Seed makes the per-cell variations repeatable. `Cell` identifies
the cell, `Center` gives its center in UV coordinates, and `Random` supplies a scalar per-cell
variation. `Mask` clips the transformed motif to its original cell: rotated/scaled motifs do
not overlap neighboring cells. Connect `UV` to a texture or procedural branch and use `Mask`
for coverage; Tile UV does not sample or composite images itself.

Perspective UV inversely maps the quadrilateral corners to unit-square source coordinates,
using a projective transform rather than bilinear interpolation. The default corners preserve UV.
`Mask` selects the quad; apply it to sampled alpha when the image should be confined to the quad.
Invalid, degenerate or nonconvex quads produce zero `UV` and zero `Mask`.

### Quaternion operations

`Math/Quaternion` uses ordinary Float4 `(x,y,z,w)` ports; no new wire type or
implicit quaternion arithmetic is introduced. Every node outputs `Out`.

| Kind | Inputs | Output |
| --- | --- | --- |
| `EulerToQuaternion` | Float3 Euler=(0,0,0) | Float4 unit quaternion |
| `QuaternionToEuler` | Float4 Quaternion=(0,0,0,1) | Float3 Euler angles |
| `RotateByQuaternion` | Float3 In=(0,0,0), Float4 Quaternion=(0,0,0,1) | Float3 rotated vector, preserving length |
| `QuaternionMultiply` | Float4 A and B, both identity | Float4 Hamilton product A*B: B applies first, then A; no normalization |
| `QuaternionInverse` | Float4 Quaternion, identity | Float4 conjugate divided by squared magnitude, including non-unit inputs |
| `QuaternionSlerp` | Float4 A and B, both identity; Float T=0.5 | Float4 unit quaternion; shortest-path spherical interpolation, near-coincident normalized linear interpolation |

Both converters use `option` for Unit: `0` Radians, `1` Degrees (default), `2` Turns.
`rotationOrder` selects `XYZ`, `XZY`, `YXZ`, `YZX`, `ZXY` (default), or `ZYX`.
The names describe extrinsic rotations about fixed axes, applied from left to right.
Vector components always remain X/Y/Z, independent of the selected order.
The inverse conversion returns the middle rotation angle in [-90,90] degrees and
the others in [-180,180], converted to the selected unit; near gimbal lock the first
rotation angle is zero. Equivalent rotations need not recover the original angles.

Rotation, Quaternion to Euler and Slerp normalize their quaternion inputs with
scale-safe arithmetic. Zero quaternion inputs are treated as identity there and
by Quaternion Inverse. Multiply preserves raw quaternion magnitude; ordinary
Multiply remains component-wise for Float4. Slerp does not clamp T, so values
outside 0..1 extrapolate. Native HLSL and SVM call the same helper functions.

### Gabor Noise, Rotate About Axis and Blackbody

Gabor Noise uses the WT family in 1D/2D/3D, with Frequency, Bandwidth, Anisotropy and
vector Direction. Value, Phase and Intensity describe the same single noise evaluation. Gradient/Curl retain their signed magnitude. See [WT noise nodes](#wt-noise-nodes)
for all settings and types. Sampled filters multiply its evaluation cost.

Rotate About Axis rotates Float3 In around the origin by Rotation, retaining vector magnitude.
Unit selects Radians (default, option 0), Degrees (option 1) or Turns (option 2). Float3 Axis is normalized;
provide a nonzero axis. In, Axis and Rotation default to zero. Out is Float3.
It does not implicitly subtract a UV center: subtract the desired center before rotation and
add it back afterward when rotating positions.

Blackbody clamps Temperature to `1000..40000` Kelvin and returns normalized linear RGB in
Float4 `Out`, with alpha 1. Negative RGB components are clipped, then RGB is divided by its
largest component. This describes temperature color; it does not encode physical radiance
or exposure. Adjust brightness separately with Multiply or Exposure.

The [coefficient generator](scripts/build-blackbody-coefficients.py) integrates Planck spectra over
380..780 nm with 2 nm trapezoidal steps, using the CIE 1931 analytic fit in Equation 4 and
Table 1 of [Wyman, Sloan and Shirley, *Simple Analytic Approximations to the CIE XYZ Color Matching Functions* (2013)](https://jcgt.org/published/0002/02/01/paper.pdf).
It implements the mathematical fit without copying the paper's sample implementation.
The shader approximates RGB with 16 cubic Hermite segments in logarithmic temperature,
selected by a binary tree with four branch decisions, before clipping and normalizing.
It evaluates generated polynomial coefficients without a lookup texture or per-pixel spectral
integration. The generator checks the approximation against direct spectral evaluation with
the same analytic CIE fit; this measures interpolation error, not the fit's error against
tabulated observer data.

### Slope Blur and Bilateral Blur

Slope Blur follows the **negative gradient** of the scalar Slope branch. The gradient is not
normalized: scaling Slope changes the travel distance. Negative Strength reverses the path;
zero Strength or a constant Slope preserves In. If no integration step moves the sample
position, the original evaluation is returned unchanged, including its antialiasing and
transparent RGB. The direct evaluation remains unconditional so neighboring pixels retain
valid derivatives. **Steps** (stored input key `Samples`) rounds/clamps to `1..64` integration
steps, with the starting sample included in the result (Steps + 1 In evaluations). At each
step, `g = 0.5 * (Slope(p+hx)-Slope(p-hx), Slope(p+hy)-Slope(p-hy))`, where hx/hy are one document
pixel in UV along each axis, then `p -= g * Strength / Steps`. Equivalently, Strength scales
document-pixel travel per unit of UV-height slope. Average, Min and Max operate per
component, including alpha. Both In and Slope are reevaluated along the
path. Strength is not a fixed-radius control; this node has no Radius input.

Bilateral Blur combines spatial Gaussian weights with soft similarity weights from the guide.
Radius uses absolute document pixels. The kernel spans ±Radius with half-extent
`max(1,ceil(abs(Radius)))` in **Quality = Standard** (default). **High** doubles the
half-extent, retaining Standard sample locations and halving spacing across the same area.
The square grid contains `(2*extent+1)^2` samples: Radius=8 uses 289 or 1089 respectively.
There is no fixed sample cap. High is useful for fine detail but approaches four times
the evaluations at large radii. Tolerance is nonnegative;
zero accepts only exact guide matches. Positive Tolerance weights the mean squared guide
difference `d2` by `exp(-0.5*d2/Tolerance^2)`, multiplied by the spatial Gaussian weight.
Source mode uses In as the guide. Custom adds a dynamic numeric/Color Guide input, defaulting
to scalar 0, that is reevaluated at each sample. Both modes compare all active components
directly, including hidden RGB and alpha for Float4. Guide's type is independent of In and
does not widen or change Out: only In determines its numeric width. Filtering processes
every component independently, including alpha.
Zero Radius preserves In, and Out retains In's type without a result clamp.

Both filters reevaluate upstream branches without intermediate textures. Slope Blur samples
the height gradient as well as In; Bilateral Blur evaluates its guide as well as In. More path
samples, larger neighborhoods, Gabor Noise and nested filters can make previews expensive.
Deeply nested sampled branches can also take minutes during the first shader compilation.

## Reusable Sub Graphs

`graph.subGraphs` is an embedded `List<ShaderGraphSubGraph>`, empty by default. Each definition
stores `id`, `name`, `inputs`, `outputs`, `nodes`, `edges`, `notes` and `groups`.
Each interface port stores `id`, `name`, `type`, `value` and `uvDefault`. `value` is the
unconnected default; `uvDefault` is valid only for Float2 and supplies the evaluation UV.
Resource ports require resource connections. Input and output IDs must be unique across
the definition's interface; display names can change without changing wiring keys.

- A `SubGraph` node references a definition through `subGraphId`. Its input/output port keys
  are the definition's stable interface IDs, not its display names.
- A definition's `SubGraphInput` node uses `parameterId` to reference one declared input
  and exposes `Out`. This does not reference `graph.parameters`.
- Exactly one `SubGraphOutput` terminates each definition. Its dynamic input keys are the
  definition's output interface IDs. It has no outputs. Root graphs use ordinary Output;
  definition-only interface nodes cannot be added to the root graph.

Definitions share root `parameters`, `functions` and `sources`; these are not copied into
each definition. Compilation expands instances into the enclosing calculation. Editing a
definition affects all its instances; it introduces no external file or asset dependency.
Snapshots, document JSON and native TIFF retain the embedded table with the graph.

Limits: at most **128 definitions**, **64 input pins** and **64 output pins** per definition
(at least one output), **16 nesting levels**, and **4096 expanded nodes**. Recursion is forbidden.
The ordinary limits of 512 nodes and 4096 edges apply within each scope; node IDs must be
unique across the root and every definition. Notes/groups retain their ordinary per-scope rules.

Select a connected calculation and use **Convert Selection to Sub Graph** in the context menu. Double-click
an instance to edit its definition, use **Back to Graph** to return to the root, and edit
**Inputs**/**Outputs** in the sidebar. Existing definitions are available in the creation menu.
Discovery marks SubGraph with `requiresDefinition`, `definitionField: "subGraphId"`,
`definitionTable: "subGraphs"`, `dynamicInputs` and `dynamicOutputs`. Its empty catalog port
lists are placeholders; inspect the referenced definition. SubGraphInput/SubGraphOutput are
not ordinary catalog creation entries.

## Compilation and invalidation

The connected calculation generates a WhimTex FX script (`@param` declarations, helper functions
and `ApplyFX`). The shared FX source builder then produces ShaderLab for a single
fragment-shader generation pass. Layer rendering and individual node previews use this same path.
Built-in helper functions are emitted only when referenced, together with their transitive
dependencies and overloads; their bodies are unchanged. The fixed helper library is indexed once
per script domain. Graphs containing user HLSL retain the complete enabled helper modules, so
macros and includes keep their existing environment. Source and sampling validation still run.
The builder receives parameter models directly, without reparsing generated `@param` comments;
those comments remain in Generated HLSL. Numeric values are checked against their declared type.
Intermediate numeric node values are shader expressions or variables, not an image allocated
for every node. Declared resource inputs retain their normal resource preparation requirements;
the subsequent layer FX and document composition remain separate from this generation pass.

Changing parameter values updates bindings and the rendered result without recompiling the
shader. Changes to operations, connections or parameter types change the generated program and
require compilation. Constant emits the selected mathematical value as a float literal rather
than a material uniform; changing `constant` or a Swizzle's `swizzle` mask changes the code fingerprint.
Moving nodes in the editor only changes layout. The computation must be acyclic and connections
must satisfy the port types to compile. If compilation fails, diagnostics remain explicit and
the runtime retains its last successfully compiled shader when one is available. That shader's
result is not proof that the current draft compiled or produced valid output.

### Generated HLSL

The displayed source is the intermediate FX script passed to the shared source builder, before
the ShaderLab wrapper. Only reachable exposed parameters and required hidden input uniforms are
declared. Library metadata inside HLSL nodes remains inert comments; it does not declare extra FX
parameters or a preset identity. Resource values and IDs remain bound from the graph document,
including multi-key/Custom gradients, curves and layer texture sources. The script is not a
resource export. Numeric declaration defaults are captured at compilation; subsequent value-only
edits update material bindings without regenerating the script.

Generated function bodies use the code editor's four-space indentation, including nested
branches and loops. Line breaks and diagnostic directives are preserved; embedded HLSL
functions and includes retain their original formatting.

**Generated HLSL** uses a draggable footer with the shared `VerticalRevealHandle` extracted from
`LayerPreviewPanel`. The handle keeps the same 20-logical-pixel height, caption and horizontal
grip lines. It responds to pointer dragging; a click alone does nothing. From the collapsed
20-pixel height, dragging upward reveals the code only when the requested height exceeds
34 logical pixels: more than 14 pixels of upward travel in normal window coordinates,
independent of graph zoom. Dragging downward hides the code again.

The footer starts collapsed. Dragging the handle resizes the code panel up to the available
window space while leaving 80 logical pixels for the rest of the window. The open/closed state
and panel height are serialized in the graph window and restored with its window state.

The code field is created lazily on the first opening and reuses the actual FX code field in
read-only mode. While closed, it is detached from the visual tree: no code drawing, syntax
highlighting or line-number callbacks run, and generated-source updates are not sent to the
field. The field instance is retained for reopening. Reopening displays the current generated
source; unchanged source is not reassigned, preserving the retained instance and text selection.

Text remains selectable and copyable, with the same syntax highlighting, line numbers and
horizontal/vertical scrolling as FX Code, subject to the same Editor-version support. The graph
remains the editable source; this view does not edit the generated program.

<a id="selected-node-preview"></a>

## For loops

`Utility/For Begin` and `For End` are created as a paired, initially connected set.
`ForBegin` (162) takes `Iterations` (Float, default 4) and `Initial` (dynamic numeric,
default 0), and exposes `Index` (Float) and `Value` (the type of Initial).
`ForEnd` (163) takes `Value` and exposes `Result`, also with Initial's type.
Each endpoint stores the other node's ID in `loopPartnerId`; both must belong to the
same graph scope. Agent-created JSON must declare both nodes and their reciprocal IDs.

Iterations are floored and bounded below by zero. Supply finite counts representable
as an integer; each additional iteration repeats the body for every pixel. Index runs
from 0 to count minus one. Value starts at Initial and receives the previous iteration's
For End value, converted using the usual numeric/Color rules. Result is the final state;
zero iterations return Initial. For example, Initial=1 and Value multiplied by 2 for
4 iterations yields 16. Explicit feedback wires to Initial/Iterations are invalid.

Nested pairs have independent indices and state. State supports Float–Float4 and Color;
resources cannot be loop state. Begin outputs are usable only within their paired loop
body; expose results through End. An internal node preview shows its first-iteration
value (Index=0, Value=Initial); End's preview evaluates the full loop. Begin has no image
preview. End's Value label carries the repeated-branch marker.

The compiler emits a single-pass HLSL loop. Sampled branches receive the active loop
state and index as function arguments; neighboring UV samples retain that per-pixel state.
This is not feedback between images. An independent Buffer can be read at changing UVs
inside the loop, and a completed loop can feed a Buffer. A Buffer or Caustics bake whose
contents depend on an active loop's Index/Value is rejected, including in previews.

Copying or deleting an endpoint includes its partner. Copy the body as well to retain
its connections; pasted pairs get fresh reciprocal IDs. Sub Graph expansion remaps pairs
per instance. Replacing one endpoint with another node kind is disabled. Saving uses the
common editable graph codec for Shader layers, FX and presets.

## Expression

`Utility/Expression` evaluates one HLSL expression. `expression` defaults to `"A"`;
`valueType` selects Float, Float2, Float3 or Float4 for every input and `Out`.
Variables become case-sensitive ports in first-appearance order. Repeated names share
one port; numeric literals, function names, type casts, comments and swizzles do not
create ports. Arithmetic, intrinsic calls, constructors, indexing and component access
use ordinary HLSL semantics. Empty/comment-only expressions return zero.

The expression field commits on Enter (including keypad Enter) or focus loss. Enter ends
editing; Ctrl+Enter inserts a newline into the pending draft without committing.
Named bindings survive reordering; connected
variables removed through the editor remain as inactive ports, with their values in
`inputs`, and reactivate when the name returns. Type changes keep values and connections.
Input names are ASCII HLSL identifiers. The expression is limited to 4096 characters and
64 active inputs. Statements, directives and string literals require Custom Function.
Syntax/type errors are node diagnostics; invalid drafts remain editable and serializable.
Expression data participates in the code fingerprint and the common graph codec, including
clipboard, Sub Graph, Shader/FX presets and the document's embedded copy.

## Constant and Swizzle

`Constant` uses `constant: "PI"` (default), `"TAU"` (2π), `"PHI"` (golden ratio),
`"E"` or `"SQRT2"`. `Out` is always Float; the selected value is emitted directly into HLSL.
Arbitrary numbers/colors belong to parameters or unconnected input values. The old node-level
`value` field is removed; JSON/TIFF graphs containing it are unsupported, without migration.

`Swizzle` has one dynamic numeric/Color input `In` and numeric output `Out`. Its `swizzle`
string defaults to `"xxxx"`. Valid masks have 1–4 characters: `xyzw`, `rgba` (case-insensitive,
aliases may be mixed) and `_` for literal zero. Components may repeat. Output is Float through
Float4 according to mask length, including when the input is Color; no color conversion occurs.
For Float2 `(x,y)`, `"_x_y"` produces `float4(0,x,0,y)`. `"_"` produces Float zero.
Input width is the maximum of the connected source width (or the stored unconnected input type)
and the highest component referenced by the mask: y/g requires Float2, z/b Float3 and w/a Float4.
`_` does not increase input width. Color sources count as four numeric components.
Connected sources use the usual numeric conversions: scalars repeat, shorter vectors gain zeros.
Unconnected fields expose all inferred components directly and retain their stored values.
An empty/oversized mask or unsupported character is a node-specific compilation error.
Invalid masks remain saveable drafts.
The Mask field commits on Enter or focus loss before rebuilding typed ports. Choice/mask edits,
Undo/Redo, clipboard and JSON/TIFF persistence use the ordinary graph mechanisms.

## Node previews

The toolbar's **Color Mode** is a user preference (`<None>`, `Category`, `Heatmap`,
`Precision`, `User Defined`). It only updates node-title strips. Heatmap is a static
local estimate, not GPU/compile timing or accumulated branch cost; cyan means unknown.
Precision describes graph-generated float arithmetic, not precision inside arbitrary user HLSL.
`ShaderGraphNode.userColor` optionally stores a custom HTML color (`#RRGGBB`) for
User Defined mode. It survives document/clipboard copies, node replacement and Undo;
null, empty or unrecognized values use the theme. This presentation field is excluded
from executable/render fingerprints. Parameter tokens and Reroute points are not colored.

Each previewable node has an Image/Height presentation choice. The hover overlay shares
native toolbar controls and the Height settings popup with Layer Preview; the output selector
remains at the right. Nodes store `previewHeight` (false = Image, true = Height) and
`previewOutput` (output port ID, null = first numeric output) in the document, clipboard and
Undo, including nested Sub Graph nodes. A temporarily unavailable output falls back to the first
numeric output without replacing the saved choice. These are layout edits: they do not affect
shader compilation or rendered-image cache keys. Node-local channel, scale and texture settings live in serialized graph-window
view state, keyed by Sub Graph scope and node ID. They do not change graph data, compile keys,
document dirtiness or Undo. Rebuilding the canvas retains them; they are not document parameters.

Height consumes the completed native or SVM node texture at its existing zoom-dependent resolution
(16–128 pixels), rather than requesting native-depth/full-canvas rendering. Parallel Floating Horizon
and display outputs are float32, matching that resolution; source values keep the existing raw node
preview convention without an additional gamma encoding. Texture is enabled by default. Presentation
updates use the existing bounded queue. Unchanged views reuse the result; collapse, offscreen visibility,
detach and Image mode release the extra GPU surfaces. No relief work occurs for nodes without previews.

Layer and node Height settings include `highlightOutOfRange`, off by default. The display pass
tests the selected height channel before scale against [0, 1], including both endpoints. Samples
outside the range render opaque error magenta before texture/channel display, illumination and gamma encoding.
The toggle changes presentation only: it reuses the source and horizon geometry, adds no texture
allocation or source sampling, and leaves normalized samples and transparent background unchanged.

Node previews use a fixed white directional light for Global Normal Lighting by default,
from the upper left at 45 degrees to the surface normal. **Accurate Lighting** in the graph
toolbar or User Settings → Shader Graph restores document lights, including Area and Cookie.
The preference persists across windows and Editor restarts. It affects downstream node previews
and their Buffer producers, not Local lighting, Layer Preview, Canvas, thumbnails or export.
Switching it preserves the document graph and final executable; unrelated previews keep their cache.
Custom HLSL and explicit light-query nodes retain their own calculations.

Eligible exact Global evaluations use a separate lighting pass. Its scope, float32
intermediates and execution constraints are described in the
[execution contract](ShaderGraph.md#separate-exact-global-lighting). SVM can resume
supported preview work after explicit image boundaries; see the
[native-image boundary contract](ShaderGraph.md#native-image-boundaries).

Multiple evaluated Global Normal Lighting nodes in the same generated pass receive an Info badge.
Disconnected nodes, fan-out of one evaluated node and separate Buffer passes are not counted as
duplicate lighting in one pass. Where suitable, mix surface inputs with a mask before one lighting
node. Mixing already-lit outputs still evaluates both; mixing inputs can change the appearance.

Layer Preview uses the shared `LayerPreviewPanel` in a floating overlay beside the graph canvas,
outside its content transform. It starts at the bottom-right, supports dragging the header or image and edge
or corner resizing, and keeps the existing R/G/B/A channel controls. Placement, size and channels
belong to window state, never graph/document data. Viewport resizing keeps the panel reachable.
Dragging within 16 logical pixels of an edge snaps each axis independently, allowing edge
and corner attachments. Attached edges sit flush with the viewport, follow viewport
resizing and show an accent border. Dragging away releases them. Attachment flags are window state;
resizing from a free edge preserves the opposite attachment, while resizing an attached edge releases it.
The docked Layer Settings preview retains its existing reveal/resize behavior.

Hovering a parameter declaration highlights its row and all matching Parameter nodes by ID.
This transient feedback does not alter selection, Undo history or rendering.

Preview availability is shared by node construction and cache cleanup. Constant, Canvas Data,
Texture Size, Split, Compare, Output, Parameter and Reroute do not create a preview or GPU target.
UV, sampling, generators and numeric image-processing operations retain previews.
Nodes with an expanded preview retain a 174-pixel minimum width. Collapsing the preview
removes that constraint, so the node sizes to its contents like an ordinary node without a preview;
expanding it restores the minimum. Rebuilding the node view respects the cached preview state.
Only numeric output ports are offered; Boolean/resource-only HLSL functions have no preview.
This is a node-kind/output-type policy, not an analysis of whether the current inputs vary with UV.
There is no shared Selected Node preview in the sidebar, and selecting a node does not retarget
another node's preview. An output selector appears only when multiple previewable outputs exist.
The selector overlays the bottom-right corner without a label or outer spacing and appears
on node hover. It uses a translucent dark background, a one-pixel dark border, square corners,
white text and a white dropdown arrow. Its native menu opens above the field, aligned to the
preview's right edge; available space above limits menu height, with scrolling for longer
lists. The selector stays visible while open. Menu width and item height are at least the
field's displayed dimensions. A uniform transform scales the entire menu with graph zoom,
including checkmarks, text, borders and spacing, with a minimum scale of 0.7x.
Placement and available height account for that scale.
The preview has no Foldout or heading. Its collapse button overlays the image at the top and
appears on node hover (or button focus). A collapsed preview leaves only an expand row.
Control heights and icon dimensions come from installed `com.unity.shadergraph` 17.7.0,
`Editor/Resources/Styles/MaterialNodeView.uss`: collapse 20 × 20 with a 4-pixel top inset,
expand 56 × 16 and icon 16 × 16 logical pixels. The expand button is centered without
margins or padding; its chevron is centered. Scoped button selectors override the native node's
button margins. Chevrons are drawn locally; there is no package
asset dependency. Each node stores `previewExpanded` (default true) in the document, including
Sub Graph contents. TIFF, all JSON modes and clipboard copies retain it; Undo/Redo and reopening
the graph restore it. Expansion is editor layout only and does not change shader compilation or
rendered-image cache fingerprints.
Resource ports require a Sample node; scalar values become opaque grayscale, `float2` becomes
`(x, y, 0, 1)`, `float3` gains alpha 1, and `float4` and Color are displayed as RGBA. These display
conversions do not change numeric wire conversions or math results.

Each preview uses a 16–128 pixel square linear ARGBFloat target with Bilinear filtering, sized
to its on-screen footprint (including display scaling). Zooming out reduces render resolution;
zooming in restores it, with hysteresis at size boundaries. Rendering happens before the owning
layer's Transform and FX. It is separate from Layer Preview and does not replace the graph's
Output connection. Preview rendering is dirty-driven: a preview renders only when invalidated,
expanded and visible in the graph viewport. Collapsed, hidden or off-screen previews do not
render; pending invalidation is processed when they are expanded and visible again. Program
reuse depends on the upstream branch fingerprint and chosen output port; unrelated node edits
do not recompile that branch. Shared function, parameter-layout and Sub Graph changes conservatively
invalidate previews. At most two preview passes compile asynchronously; rendering waits for completion.
A round-robin queue starts work within a 4 ms Editor-update budget (a single native call can exceed it),
with at most eight work items per update. Animated node previews run at up to 30 Hz in the focused
graph window and 10 Hz otherwise. Prepared bindings are shared across previews; only emitted numeric
inputs are uploaded. The profiler records `shadergraph.preview.tick`, `.compile` and `.render`.
The canvas retains one immutable preparation snapshot per edit for all preview requests.
External callers still validate live graph data; edits, Undo, source revisions and graph/view
changes invalidate the retained snapshot. Layout edits invalidate future snapshots without
recompiling existing previews. Camera, node layout and viewport changes invalidate priority
ordering; idle updates do not sort it again. The update budget includes fingerprint preparation
and sorting, though neither operation can be interrupted midway.
Resource-ready notifications update diagnostics and images without refreshing authoring fields
or invalidating graph/type caches. An owning program's ready image bypasses the ordinary edit
coalescing delay. External input-resource changes still invalidate preview images, and data
notifications retain the full editor refresh path for both Shader layers and FX.
The previous image remains visible while a replacement compiles;
parameter values rebind without recompilation. A preview compilation/render error clears that
preview's image and displays a diagnostic. This differs from retaining the last successful
shader for the layer itself. Layer rendering, graph math and storage are unchanged by this UI.

## Node set

Names below identify operations; grouped rows do not imply a single combined node.

| Operations | Purpose |
| --- | --- |
| Parameter, Constant | Shared parameter reads and named mathematical constants. |
| Instance Index, Time | Input nodes with one Float output, Out, and no own preview. Both have Float Start (default 0) and Scale (default 1) inputs. Instance Index returns `Start + (float)_WhimTexInstanceIndex * Scale`; the supplied index currently defaults to 0. Time returns `Start + _WhimTexTime * Scale`; Canvas uses controlled document seconds, graph previews keep independent Editor seconds, and Save/export/default renders use time 0. |
| Custom Function, cataloged HLSL functions | Inline function bodies or functions imported from an HLSL library. |
| Reroute | Forward a typed connection unchanged; insert on an existing edge, not through node search. |
| UV, Canvas Position, Canvas Data, Texture Size | Local/canvas coordinates, dimensions and document pixel scale. |
| Sample Texture, Sample Gradient, Sample Curve | Read typed resources. |
| Sample Input | Read the layer's shared Input at UV; RGBA and scalar R/G/B/A outputs, with Sample Texture's wrap/filter settings. |
| Split, Combine, Append, Swizzle | Component access, vector construction, concatenation, reordering and zero insertion. |
| Add, Subtract, Multiply, Divide | Basic arithmetic. |
| Math / Advanced | Absolute, Exponential, Length, Log, Modulo, Negate, Normalize, Posterize, Reciprocal, Reciprocal Square Root, Soft Sign and Inverse Soft Sign. |
| Math / Range | Clamp, Fraction, Maximum, Minimum, One Minus, Random Range, Remap and Saturate. |
| Power, Square Root | Powers and square roots. |
| Math / Round | Ceiling, Floor, Round, Sign, Step and Truncate. |
| Math / Trigonometry | Ordinary and inverse functions in radians, numeric hyperbolic functions, and Convert Angle for radians/degrees/turns. |
| Math / Wave | Noise Sine Wave, Sawtooth Wave, Square Wave and Triangle Wave. |
| Lerp, Smoothstep | Interpolation and smooth thresholds. |
| Inverse Lerp | Component-wise `(T-A)/(B-A)` without clamping. Dynamic numeric inputs A=0, B=1, T=0; Out uses the widest input width, with Color treated as Float4. Equal bounds are undefined. |
| Range Mask | Select a centered interval per component. Input defaults to Components: dynamic In=0 and Out are Float–Float4, with Color treated as Float4. `rangeLuminance:true` selects Input=Luminance: Color/vectors use Linear luminance before the mask, scalars pass through, and Out is Float. Scalar Center=0.5, Width=0.2, Feather=0. Width includes both inward transitions; Feather clamps to 0…Width/2. `option`: 0 Linear (default), 1 Smooth (cubic smoothstep). Width≤0 returns zero; Feather≤0 gives a hard mask including both endpoints. Signed/HDR inputs and Center are not clamped; Out is 0…1. |
| Distance, Dot Product, Cross Product, Reflection, Refract | Vector calculations. |
| Rotate About Axis | Rotate Float3 vectors around Axis, with Radians, Degrees or Turns units. |
| Compare, Select, Switch, And, Or, Not, All, Any, Is NaN, Is Infinite | Boolean conditions, ordered choices, component tests and scalar non-finite checks. |
| Transform UV, Polar Coordinates, Repeat, Mirror, Kaleidoscope, Flipbook | Coordinate transforms and atlas-frame UVs. |
| Tile UV, Perspective UV | Cell-local motif coordinates and inverse projective quad mapping, with masks. |
| Scatter, Scatter Instance | Place overlapping procedural motifs on a jittered grid and vary their source using per-instance coordinates and values. |
| OpenSimplex2 Warp Noise, Vector Displacement, Twirl, Bulge / Pinch, Wave, Ripple | Coordinate distortions for downstream sampling and procedural nodes. |
| WT noise families | Scalar, cellular, directional, spot, warp and grain fields; see [all 18 kinds](#wt-noise-nodes). |
| Random | Unchanged coordinate hashing utility under Math/Random. |
| Random Range | Float2 Seed = (0,0), Float Min = 0 and Max = 1. Float Out uses the same hash as Random with Coordinates = Seed and scalar Seed = 0, then interpolates from Min to Max. Equal bounds return that value. Reversed bounds reverse the interpolation. |
| Gabor Noise | Directional WT detail with Value, Phase, Intensity, Phasor and analytic Gradient/Curl outputs. |
| Checkerboard, Stripes, Grid, Rings, Gradient Factor, Dither | Procedural masks, gradient coordinates and dithering. |
| Shape, Combine Distance | Antialiased shape masks and signed-distance operations. |
| Hue, Saturation, Exposure, Lightness, Contrast, Replace Color, Levels, Color Balance, Posterize | Color adjustments that preserve source alpha. |
| Fade Transition | Turn a scalar noise or mask into a controlled 0–1 transition. |
| Convert Color, Luminance, Gradient Map, Color Curves | Explicit color conversion, luminance extraction and resource-based remapping. |
| Color Mask, Hue Range, Mix Color, Set Alpha, Premultiply, Unpremultiply | Color selection, interpolation, alpha control and alpha representation. |
| Height to Normal, Normal Blend, Height to Distortion, Normal to Distortion | Normals and displacement from textures or height branches. |
| Height Blend | Blend signed/HDR scalar heights and expose the top contribution as Mask. |
| Blackbody | Normalized linear temperature color; brightness is controlled separately. |
| Normal Encode, Normal Decode, Normal Reconstruct Z, Normal Unpack, Normal Strength, Normal Lighting | Normal representation, reconstruction, unpacking, detail strength and lighting with specular. |
| Blur, Sharpen, Edge Detection, Dilate, Erode | Sample an upstream numeric/Color branch at neighboring UVs. |
| Slope Blur, Bilateral Blur | Follow a height slope or soften an image while retaining guide edges. |
| Sub Graph | Reuse an embedded definition through its declared interface. |
| Blend | RGBA blending. The Mode menu uses the same names, order, and groups as the layer blend selector. Crossfade interpolates premultiplied linear RGBA by Opacity, preserving transparency without color contamination. Blend Range does not alter this mode. |
| Output | Final straight RGBA through a fixed Color input; modest visual distinction and no per-node preview. |

HLSL FX authoring continues to use the [separate effect contract](ShaderFX.md).

`Time` uses one shared Editor-time sample per update in visible node previews and the graph's
result preview, including active FX. It returns `Start + _WhimTexTime * Scale`; Instance Index returns
`Start + (float)_WhimTexInstanceIndex * Scale`. HLSL libraries and Custom Function can read the same
automatically declared `float _WhimTexTime` and `uint _WhimTexInstanceIndex` directly. The HLSL
uint keeps all 32 bits; the graph's Float output has ordinary floating-point precision.
Only compiled outputs that may depend on time update continuously; hidden/collapsed
previews do not render. Time is a uniform, so animation does not change the code fingerprint,
recompile shaders, or dirty the document. Graph result previews bypass the layer output/content
caches and retain transforms, FX and clipping. This graph-editor clock is independent of
Canvas Play/Pause. Canvas and its dependencies use the document's selected preview time;
Save, ordinary export, default renders and Live Update supply time 0, so Time.Out equals
Start in those images. Canvas Play/Pause and seeking are session controls; reopening/reloading starts at 0, paused.
Start and Scale are saved as ordinary node inputs; no clock value or playback state is saved.
See [live playback](LiveAgentAPI.md#preview-time-and-playback).
`ShaderLayerBehaviour.UsesTime` / `UsesInstanceIndex` describe the compiled output, including
reachable HLSL modules; node previews analyze their own reachable subgraph. Within a module,
analysis is conservative over its complete expanded source. See the [shared context contract](ShaderFX.md#evaluation-time-and-instance-index)
for FX, dependency flags and unresolved macros/includes.

### Extended math, normals and generators

Use these exact `kind` and port identifiers in JSON. Every node in this section has one
`Out`. Dynamic numeric nodes infer the widest numeric operand width, treating Color as Float4;
Boolean and resource values do not enter numeric math implicitly. Angles returned by inverse
trigonometric nodes are **radians**, unlike the degree controls on UV transforms.

| Category | `kind` | Inputs and defaults | Result |
| --- | --- | --- | --- |
| Math / Round | `Sign` | numeric In = 0 | Component-wise sign (-1, 0, 1). |
| Math / Round | `Truncate` | numeric In = 0 | Component-wise rounding toward zero, retaining numeric width. |
| Math / Wave | `SawtoothWave`, `SquareWave`, `TriangleWave` | numeric In = 0 | Component-wise signed wave with period 1. Out retains numeric width. |
| Math / Wave | `NoiseSineWave` | numeric In = 0, Float2 MinMax = (-0.5, 0.5) | In is cycles, multiplied by TAU internally for the sine calculation, with deterministic noise between MinMax.x and MinMax.y. Out retains the width of In. |
| Math / Range | `Saturate` | numeric In = 0 | Component-wise clamp to `0..1`. |
| Math / Advanced | `Modulo` | numeric A = 0, B = 1 | Truncating remainder `fmod(A,B)`. Nonzero results have A's sign; `fmod(-1,3)=-1`. B = 0 is undefined. |
| Math / Trigonometry | `Sine`, `Cosine`, `Tangent` | numeric In = 0 | Sine, cosine and tangent of In in radians. |
| Math / Trigonometry | `Arcsine`, `Arccosine`, `Arctangent` | numeric In = 0 | Inverse functions with output in radians. Arcsine and Arccosine require `[-1,1]` without implicit domain clipping. |
| Math / Trigonometry | `Arctangent2` | numeric Y = 0, X = 1 | `atan2(Y,X)` in radians. The angle at `(0,0)` is undefined. |
| Math / Trigonometry | `HyperbolicSine`, `HyperbolicCosine`, `HyperbolicTangent` | numeric In = 0 | Hyperbolic sine, cosine and tangent; these numeric functions do not use angle units. |
| Math / Trigonometry | `ConvertAngle` | numeric In = 0; From Degrees, To Radians | Component-wise Float–Float4 conversion among Radians, Degrees and Turns; no wrap/clamp and exact identity for matching units. See [angle units](#angle-unit-contract) for settings. |
| Math / Advanced | `Exponential` | numeric In = 0, Base E (`option: 0`) | Component-wise `exp(In)` in Base E or `exp2(In)` in Base 2 (`option: 1`). |
| Math / Advanced | `Log` | numeric In = 1 | Base E (`option: 0`, default), Base 2 (`1`) or Base 10 (`2`). Requires positive input without implicit domain clipping. |
| Math / Advanced | `Reciprocal` | numeric In = 1, Method Default (`option: 0`) | Component-wise `1/In`. Fast (`option: 1`) uses `rcp(In)`. |
| Math / Advanced | `ReciprocalSquareRoot` | numeric In = 1 | Component-wise `rsqrt(In)`, equivalent to `1/sqrt(In)` for positive inputs. |
| Math / Advanced | `SoftSign` | numeric In = 0 | Component-wise `In/(1+abs(In))`. Out retains the numeric width; Color is Float4 including alpha, without color conversion. |
| Math / Advanced | `InverseSoftSign` | numeric In = 0 | Component-wise `In/(1-abs(In))`, inverse of Soft Sign for `-1 < In < 1`. Singular at ±1; no implicit clamp. Outside the interval the formula still evaluates, but is not the inverse mapping. Same numeric-width and Color rules as Soft Sign. |
| Math / Vector | `CrossProduct` | Float3 A = (1,0,0), B = (0,1,0) | Float3 cross product, without normalization. |
| Math / Vector | `Reflection` | numeric Incident = -1, Normal = 1 | Reflection at their widest numeric width. Supply a unit Normal. |
| Math / Vector | `Refract` | Float3 Incident = (0,0,1), Normal = (0,0,-1). Float IORSource = 1, IORMedium = 1.5 | Refracted is Float3; Intensity is Float. IOR inputs clamp to at least 1. Supply unit vectors. Safe (`option: 1`, default) keeps the tangential direction beyond the critical angle with Intensity=0; Critical Angle (`0`) reflects with Intensity=1. |
| Math / Vector | `Projection`, `Rejection` | numeric A = 0, B = 1 | The part of A along B or perpendicular to B. Out uses the widest numeric input. For B=0, the results are zero and A respectively. |
| Math / Vector | `FresnelEffect` | Float3 Normal and ViewDir = (0,0,1), Float Power = 1 | Normalizes both directions. Float Out is `pow(1-saturate(dot(Normal, ViewDir)), Power)`. Power ≤ 0 gives 1. |
| Math / Vector | `SphereMask` | numeric Coords = 0, Center = 0.5. Float Radius = 0.1, Hardness = 0.8 | Float 0–1 mask. Distance uses the widest numeric input. Softness extends outward from Radius by `1-saturate(Hardness)`. Hardness 1 gives a hard edge. |
| Math / Vector | `Transform` | Float2 In defaults to the current pixel in the input space | Float2 position in normalized coordinates. `option: 0` is Layer to Canvas, `option: 1` is Canvas to Layer. |
| Logic | `And`, `Or` | Boolean A = false, B = false | Boolean conjunction/disjunction. |
| Logic | `Not` | Boolean In = false | Boolean negation. |
| Normal | `NormalEncode` | Float3 Normal = (0,0,1) | Float3 `Normal * 0.5 + 0.5`, signed XYZ to packed RGB. |
| Normal | `NormalDecode` | Float3 Normal = (0.5,0.5,1) | Float3 `Normal * 2 - 1`, packed RGB to signed XYZ. |
| Normal | `NormalStrength` | Float3 Normal = signed flat normal (0,0,1); Float Strength = 1 | Normalized Signed XYZ Float3; 0 is flat, 1 preserves the unit normal, negative values reverse XY. |
| Normal | `NormalLighting` | Color = white; Float3 Normal = signed flat normal (0,0,1); LightDirection: Float3 = (-0.5,0.5,1); LightColor: Color = white; Intensity = 1; Ambient = 0.15; SpecularColor: Color = white; SpecularIntensity = 0; Roughness = 0.25; SpecularOnly: Boolean = false | Color lit by ambient, diffuse and optional specular; surface alpha passes through. |
| Color/Alpha | `Premultiply`, `Unpremultiply` | Color = white | Float4 with RGB multiplied/divided by alpha; alpha is retained. |
| UV | `Flipbook` | UV; Columns = 4; Rows = 4; Frame = 0 | Float2 atlas coordinates for one frame. |
| Color/Adjustments | `Dither` | Color = white; UV; Levels = 8; PixelSize = 1 | Color with Bayer-quantized RGB and unchanged alpha. |

Unqualified scalar inputs are Float; UV is Float2 with current-pixel coordinates by default.
Cross, Reflect and Refract do not normalize automatically. NormalEncode/NormalDecode have
fixed directions and do not normalize, clamp or convert color spaces.
NormalStrength and NormalLighting take Signed XYZ, neutral `(0,0,1)`.
NormalStrength normalizes, scales XY by Strength, blends Z from 1 to the unit normal's
Z by `saturate(abs(Strength))`, then normalizes the result.

NormalLighting normalizes Normal and LightDirection. Its diffuse term is
`Color.rgb * (Ambient + LightColor.rgb * max(dot(N,L),0) * Intensity * LightColor.a)`.
Specular uses the fixed view direction `(0,0,1)` and the normalized halfway vector with L.
Roughness is bounded to `0.01..1`; smaller values sharpen the highlight. SpecularColor alpha
scales SpecularIntensity, independently of the diffuse light tint and intensity. SpecularOnly
omits ambient/diffuse while preserving Color alpha. No final RGB clamp is applied, including
for negative intensities or HDR inputs. SpecularIntensity defaults to zero, so enable it explicitly.

Unpremultiply returns zero RGB at exactly zero alpha; any other alpha, including tiny or negative
values, is used directly as the divisor. Premultiply/Unpremultiply do not clamp. Output expects
straight RGBA; restore straight color before connecting a premultiplied branch to Output.

Flipbook floors Columns/Rows and clamps them to `1..4096`; Frame is floored and wraps across
the frame count, including negative frames. `option: 0` is Bottom to Top (frame 0 at bottom-left),
`option: 1` is Top to Bottom. Row order does not flip local UV orientation. Flipbook returns UV,
without sampling or clipping it; connect Out to SampleTexture.UV.

Set positive per-axis Period on a [WT noise node](#wt-noise-nodes) to repeat it.
Cellular Noise → Distance to Edge returns normalized boundary distance; threshold Value for border lines.

Dither's `option` values are **0: Bayer 4x4**, **1: Bayer 2x2**, **2: Bayer 8x8**. Levels rounds
to at least 2; RGB quantization spacing is `1 / (Levels - 1)`, continuing beyond `0..1` without
clipping signed/HDR values. PixelSize uses its absolute value, at least one document pixel.
Its pattern follows document dimensions, independent of preview size; alpha is not quantized.

### Append, Switch and numeric predicates

`Append` (`kind: 196`, **Channel/Append**) concatenates numeric `A` then `B` into one
`Out`, using leading components in their original order. It takes at most three components
from A, then as many from B as fit in four total components. Both inputs default to scalar
zero, producing Float2; the result is always Float2, Float3 or Float4. Color supplies raw RGBA
components and the result is numeric, without color conversion. For example, Float2 `(1,2)`
and Float3 `(3,4,5)` produce Float4 `(1,2,3,4)`; Float4 A contributes only XYZ.

`Switch` (`kind: 197`, **Logic/Switch**) has scalar `Predicate = 0`, dynamic numeric/Color
`Fallback = 0` and one value input per ordered case. `Out` uses the common numeric width of
all case values and Fallback, with Color treated as Float4. The first matching case wins;
when none matches, the result is Fallback. `floorPredicate` defaults to `true`: comparisons
use `floor(Predicate)`, including for negative values. Disable **Floor Predicate** for
fractional thresholds. Comparisons are exact, without an equality tolerance.

`switchCases` is an ordered array of `{ port, comparison, threshold }`. `port` is a stable,
unique input identifier used by `inputs[].port` and incoming `edges[].inputPort`; it must
not be `Predicate` or `Fallback`. It contains 1–64 ASCII letters, digits or underscores.
Displayed letters follow list order; stored port identities remain stable when other cases
are removed. `comparison` is `Less`, `LessOrEqual`, `Greater`, `GreaterOrEqual`,
`Equal` or `NotEqual` (enum IDs 0–5); `threshold` is a finite scalar. At most 26 cases are
accepted. Omitted/null `switchCases` supplies `A == 0` and `B == 1`, each with a scalar-zero
value input. An explicit empty array leaves only Fallback. Removing a case removes its value
input and incoming connection. Keep the list and its stable port IDs when copying graph JSON.
The editor supports changing comparisons/thresholds and adding/removing cases. An Enum
parameter connects to Predicate as its scalar option value; it does not generate case names,
thresholds or inputs. Author the cases explicitly, including for non-index Enum values.

The remaining nodes are under **Logic**, each with one Boolean `Out`:

- `All` (`204`) and `Any` (`205`) accept dynamic numeric `In = 0`, including Float–Float4
  and Color as four raw components. All is true when every component is nonzero; Any is true
  when at least one is nonzero. Negative values count as nonzero; these are not `> 0` tests.
- `IsNaN` (`206`) and `IsInfinite` (`207`) take scalar Float `In = 0`. IsNaN detects NaN;
  IsInfinite detects positive or negative infinity, excluding NaN. Neither sanitizes its input.
  Split a vector first to test individual components.

Append, Switch and these four predicates have native HLSL and SVM implementations.
The same applies to FadeTransition, ReplaceColor, Contrast, NormalReconstructZ,
NormalUnpack, RadialShear and Shape's Rounded Polygon mode described below.

### Buffer and Fractal nodes

The graph editor marks repeated inputs, including unconnected ones, with an opaque `↻ ` prefix in the native
port label, sharing its font, spacing and baseline. The repeated-input list
remains explicit. Unconnected and inactive port labels retain `PickingMode.Ignore` so
their text does not activate the port's native connector hover. Hovering any connected input label, including the icon, traces its source
through compiler input reads and Sub Graph expansion. Connector dots do not trigger tracing.
Buffer source passes are excluded; Buffer UV expressions, parameters and constants remain
part of the source branch. Releasing a click without movement selects that trace's nodes;
Ctrl-click adds to the selection that preceded the press. On macOS, Command-click also adds.
Label presses use native node dragging and snapping; any movement cancels branch selection,
even if the pointer returns to the press position. Both actions share the cached traversal.
Connected output labels use the same hover/click/drag handling, tracing downstream consumers
of that specific output through active connections, including through Buffer nodes. Other outputs
of the starting node and unrelated inputs of consumers are excluded. The starting node is not
selected. Parameter labels participate; Reroute dots keep their connection behavior.
Input and output labels suppress tooltip bubbling, keeping native port hints from appearing over branch highlighting.
Buffered outputs (Buffer and Caustics) display a vector storage-cylinder suffix
after the native label, before the connector, including when unconnected. Its size follows the label's
font size and its gap uses a measured space. Text and decoration share a hit area with the existing label gestures.
Traces are editor-only, cached until the graph changes and require no shader compilation
or GPU draws. This visualizes evaluation structure, not an exact count of GPU instructions.

`Buffer` (kind ID 149, **Sampling**) materializes an expensive branch for later sampling.
`Fractal` (kind ID 150, **Procedural**) repeats a branch over the graph's 2D domain.
Both accept numeric or Color **In**, defaulting to scalar zero, and return **Out** with
the same type. Boolean and resource values are not numeric branches. Discovery exposes
`dynamicInputs`, `outputTypeFromInput: "In"` and `sampledInputs: ["In"]`.

| Node | Ports and settings | Behavior |
| --- | --- | --- |
| `Buffer` | In; UV: Float2 with implicit current-pixel UV; `bufferResolution`: Canvas (default) / Custom; `bufferSize`: [512,512]; existing `wrap` / `filter` | Evaluates In over the local 0..1 domain, then samples the buffer at UV. Out follows In's type. |
| `Fractal` | In; Octaves=3, Lacunarity=2, Gain=0.5, WeightedStrength=0, PingPongStrength=2, Bias=1, Feedback=2, SeedStep=1; `fractal`: FBm (default) / Ridged / PingPong / Multifractal / HybridMultifractal / HeteroTerrain / RidgedMultifractal; `noiseFiltering`: true | Repeats the upstream branch at scaled 2D graph coordinates and combines the octaves component by component. None is not a supported mode. |

Buffer **Canvas** resolution follows the current render target, including preview resolution
for node previews. **Custom** uses the exact positive integer width and height in bufferSize;
the stored size remains available when Canvas is selected. wrap/filter control sampling,
using the same enums as Sample Texture. The buffer is a linear RGBA texture with float32
components, retaining signed/HDR numeric values. Generated pixels and GPU resources are
transient and are never serialized into documents, presets or clipboard JSON.
Generated HLSL for Buffer depends on intermediate textures and passes managed by the
graph renderer. Copying the code alone does not allocate or populate those textures.

The renderer reuses its texture allocation across renders. Bound material values, time,
instance index, transforms and static texture changes invalidate cached contents. External
RenderTexture inputs or CustomFunction code conservatively refresh the source on every
owner render because GPU writes do not automatically update texture change counters.
Each needed buffer is evaluated once within a draw, with nested buffers processed in
dependency order; there are no allocations per pixel or octave.

Fractal evaluates `In(graphUV * frequency)`. FBm, Ridged and PingPong use normalized, component-wise weighted
accumulation; Color alpha participates as a component. **FBm** retains signed/HDR values.
**Ridged** transforms each value as `1 - abs(2*v - 1)`; **PingPong** uses
`1 - abs(frac(v * max(PingPongStrength, 0))*2 - 1)`. These folds also apply component-wise.
Octaves is converted to an integer and clamped to `1..8`; Lacunarity is clamped to
`0.01..16`, and Gain/WeightedStrength to `0..1`. Lacunarity scales coordinates between
octaves, Gain scales amplitude, and WeightedStrength modulates amplitude from prior values.
The four additional modes interpret the source as signed `s = 2*In - 1`, without clamping
the source or final height. They use raw cascades rather than the normalized mean above.
All calculations, including feedback, apply independently to float through float4/Color alpha.
For octave `i`, amplitude `a = Gain^i`:

| Mode | Unfiltered calculation | Additional active ports |
| --- | --- | --- |
| `Multifractal` | Start at 1; multiply by `1 + a*s` each octave. | None |
| `HybridMultifractal` | Start height=0, weight=1; clamp weight to 0..1; add `weight*(s+Bias)*a`, then multiply weight by `Feedback*(s+Bias)*a`. | Bias, Feedback |
| `HeteroTerrain` | First height=`s+Bias`; later add `height*(s+Bias)*a`. | Bias |
| `RidgedMultifractal` | First signal=`(Bias-abs(s))^2`; later multiply this ridge signal by `saturate(previousSignal*Feedback)`; add `signal*a`. | Bias, Feedback |

Bias defaults to 1 and is an additive signal/ridge-level control, not a coordinate offset.
Feedback defaults to 2 and is clamped only below zero. Gain keeps its existing 0..1 range
and controls octave amplitude independently of Feedback. WeightedStrength is available only
for FBm/Ridged/PingPong; PingPongStrength only for PingPong. Unused mode-specific ports are
hidden when unconnected and remain visible but inactive when connected. Stored values and
connections are retained. Existing Ridged is unchanged and distinct from RidgedMultifractal.
Filtering fades individual cascade contributions; it is not exact integration of the nonlinear
height field. New modes are currently graph-node modes; native WT noise and WT_DEFINE_FRACTAL
retain their existing modes.

Filtering attenuates octave contributions using the local UV pixel footprint. Filtering
inside upstream WT noise remains active independently. **SeedStep** (integer conversion,
default 1) offsets all upstream noise-node seeds, including White/Blue grain, per octave.
It does not automatically vary arbitrary seed parameters inside CustomFunction code.
This generic 2D graph operation is not exact per-noise WT fractal evaluation, even if the
input branch contains a 1D/3D noise node; the built-in WT noise fractal behavior is unchanged.
For a custom HLSL field, [`WT_DEFINE_FRACTAL`](NoiseLibrary.md#fractals-of-custom-functions)
provides the FBm/Ridged/PingPong component-wise accumulation math in Code/FX/brush/CustomFunction code.
Its position may be float/float2/float3 and its result float through float4. Graph Fractal
retains its existing 2D domain, settings and SeedStep; the macro uses WTNoiseSettings,
defaults to None and advances the source seed by one each octave. It performs no color
conversion and does not generate analytic derivatives or make arbitrary sources smoothly seamless.
Warp's None/Progressive/Independent modes remain a separate domain-warp operation.

For **Buffer → Fractal**, SeedStep must be an **unconnected literal 0**: a buffer cannot
vary with the per-octave seed context. A connected SeedStep is conservatively rejected,
even if its current value is zero; the check includes indirect source dependencies.
**Fractal → Buffer** is allowed. Use the latter to materialize the completed fractal, or
SeedStep=0 to repeat the same buffered pattern at different scales.

Buffer captures only the upstream field's local `0..1` image. When Fractal samples outside
that image, Buffer **Wrap** controls the result: **Clamp** is the default; use **Repeat**
for tiled octaves. Zooming into one fixed buffered image does not reveal new detail.

Storage retains settings, graph wiring and literal values, with no generated pixels.
Fractal JSON with an omitted `fractal` uses FBm unless an explicit type default supplies
it; explicit None is invalid. Ordinary scalar noise nodes no longer accept non-None
embedded fractals or the former octave inputs. Unsupported old data receives a diagnostic;
it is not migrated. Buffer settings use `bufferResolution`, `bufferSize`, `wrap`, `filter`;
Fractal mode uses `fractal`, not `option`. API settings include Buffer's size default and
`activeWhen: { "bufferResolution": "Custom" }`, and Fractal's `fractalModes`,
`defaultFractal`, per-mode input availability and filtering default.

### Scatter and Scatter Instance

`Scatter` and `ScatterInstance` are built-in kinds under **Procedural/Scatter**.
Scatter places one candidate per grid cell, jitters its center and reevaluates **Source**
inside each retained instance's local `0..1` square. Source can be a procedural branch
or a sampled image. Instances can overlap neighboring cells; the search bound adapts
to Size and rotation, and source evaluations outside the instance square are skipped.

| Scatter input | Type and default | Meaning |
| --- | --- | --- |
| `Source` | Dynamic Float–Float4/Color; Float `1` | Branch reevaluated in instance-local UV. Boolean and resource inputs require conversion or sampling first. |
| `UV` | Float2; current-pixel UV | Coordinates at which Scatter is evaluated. |
| `Count` | Float2 `(8,8)` | Cells per normalized UV unit, or per Period when periodic; absolute values rounded to whole cells and clamped to `1..1024` per axis. |
| `Size` | Float2 `(0.8,0.8)` | Instance dimensions in cell widths; absolute value, capped at `16` per component. |
| `SizeRandom` | Float `0.25` | Random size reduction, clamped to `0..1`; never enlarges Size. |
| `Rotation` | Float `0` | Base rotation in turns. |
| `RotationRandom` | Float `0.5` | Random rotation range of ± this many turns. |
| `Jitter` | Float `1` | Center displacement, clamped to `0..1`; 1 spans the whole cell. |
| `Density` | Float `1` | Probability of retaining a cell, clamped to `0..1`. |
| `Mask` | Float `1` | Contribution multiplier, clamped to `0..1`. |
| `ScaleMap` | Float `1` | Size multiplier, clamped to `0..1`; only shrinks instances. |
| `RotationMap` | Float `0` | Additional rotation in turns. |
| `Seed` | Float `1337` | Seed for repeatable placement and variation. |
| `Period` | Float2 `(1,1)` | UV repeat size when periodic; each component uses `max(abs(value), 0.0001)`. |

**Density**, **Mask**, **ScaleMap** and **RotationMap** are sampled at the canonical
instance center, wrapped into Period when periodic. Each therefore controls the whole
instance rather than varying across its source image. Other settings are evaluated at
Scatter's evaluation UV. Source is reevaluated at the local UV of each contributing instance.

The bottom **Mode** selector stores `option: 0` for **Max** (default), `1` for **Add**
and `2` for **Alpha Over**. The output key is `Out`. Max/Add preserve Source's type and HDR values; Max uses a
zero background, so negative-only contributions cannot lower it. Alpha Over returns
Float4 with straight-alpha RGB; Float–Float3 use the existing numeric-to-Color conversion
with alpha 1. No blanket `0..1` clamp is added to the result.

The bottom **Periodic** checkbox stores `scatterPeriodic`, default `false`.
It uses the grid Count over each Period and repeats instance placement and identity.
Period is hidden when disabled; a connected inactive Period port stays dimmed with its
edge and value retained. `noisePeriodic` does not configure Scatter.

**Scatter Instance** has no inputs. Its exact output keys are:

| Output | Type | Meaning |
| --- | --- | --- |
| `UV` | Float2 | Instance-local source sampling coordinates; `(0.5,0.5)` in center-sampled controls. |
| `Index` | Float | Stable hash-derived instance ID in `0..16777215`, exactly representable in Float; not a sequential cell number. |
| `Random` | Float | Per-instance random value in `0..1`. |
| `Center` | Float2 | Canonical instance center in Scatter UV, wrapped when periodic. |

Instance data is available in Source and the four center-sampled control branches.
Nested Scatter uses the innermost instance and restores the enclosing context afterward.
Connect Random or Index to a source seed or other input for independent procedural
variation; Scatter does not automatically change WT noise seeds. Evaluating Scatter
Instance outside an active Scatter context reports an explicit diagnostic. Node previews
of source branches use an example context: Index `0`, Random `0.5`, Center `(0.5,0.5)`.

An ordinary **Buffer → Scatter** source is allowed, but a buffer producer must not
depend on live Scatter Instance data. Buffering the source fixes its procedural contents,
so it cannot regenerate a different source from instance values. **Scatter → Buffer**
stores the completed scatter image. Scatter uses native HLSL; SVM previews fall back
for these nodes. Repeated source evaluations, larger instances and nested Scatter increase
evaluation cost.

Save, Undo and document JSON use the common graph model/codec. `scatterPeriodic` is
the only new serialized field; Mode uses the existing `option`, inputs use ordinary
node literals/edges, and instance values are evaluation context rather than saved data.

### WT noise nodes

Shader Graph evaluates the following noise kinds through the built-in [WT library](NoiseLibrary.md).
Noise Layer retains its separate implementation. This is a format break: retired `Noise`,
`Cellular`, `DomainWarp`, `TileableNoise` and `VoronoiEdge` nodes are unsupported, with no
migration or aliases. `GaborNoise` has a new numeric kind ID; old Gabor storage is explicitly
rejected rather than interpreted as the new node. Recreate affected nodes and their wiring.
`Random` keeps its existing coordinate hashing behavior under **Math/Random**.

| Category | Serialized `kind` | Family controls and outputs beyond Value |
| --- | --- | --- |
| Procedural/Noise | `PerlinNoise` | Perlin gradient noise; Gradient, Curl. |
| Procedural/Noise | `ValueNoise` | Interpolated lattice values; Gradient, Curl. |
| Procedural/Noise | `ValueCubicNoise` | Cubic-interpolated values; Gradient, Curl. |
| Procedural/Noise | `OpenSimplex2Noise` | OpenSimplex2; Gradient, Curl. |
| Procedural/Noise | `OpenSimplex2SNoise` | Smoother OpenSimplex2S; Gradient, Curl. |
| Procedural/Noise | `FlowNoise` | Phase=0 cycles rotates the gradients; 0 and 1 coincide. Gradient, Curl. |
| Procedural/Noise | `CausticsNoise` | 2D only. Algorithm: OpenSimplex2S or Simplex (default); Evolution=0, Strength=1, Dispersion=1; profiled Value and RGB Color in 0–1, alpha=1. See [Caustics nodes](#caustics-nodes). |
| Procedural/Noise | `CellularNoise` | Distribution, Metric, Output; Jitter=1, Evolution=0, Smoothness=0. F1, F2, CellValue, ToPoint. |
| Procedural/Noise | `TriangleVoronoiNoise` | 2D only. Distribution, Jitter=1, Evolution=0, Stretch=(1,1); Value is a normalized border field. Distance, CellValue, ToPoint and BorderNormal expose unfiltered geometry. |
| Procedural/Noise | `GaborNoise` | Frequency=2, Bandwidth=1, Anisotropy=0.8, Direction=(1,0,0) at the selected width; PhaseOffset=0 cycles, Width=0.5; Profile: Sine/Triangle/Pulse; Phase, Intensity, Phasor, Gradient, Curl. |
| Procedural/Noise | `SpotsNoise` | Density=1, Radius=0.35, Softness=0.5, Elongation=1, Direction=(1,0,0) at the selected width. |
| Procedural/Noise | `ShardNoise` | Sharpness=0.7 (0..1): soft slopes through sharp facets. Value only; native 1D/2D/3D, shared periodicity and filtering. Use Fractal for octave detail. |
| Procedural/Noise/Warp | `OpenSimplex2WarpNoise` | OpenSimplex2 displacement; Displacement, Position. |
| Procedural/Noise/Warp | `OpenSimplex2ReducedWarpNoise` | Reduced OpenSimplex2 displacement; Displacement, Position. |
| Procedural/Noise/Warp | `BasicGridWarpNoise` | Basic grid displacement; Displacement, Position. |
| Procedural/Noise/Grain | `WhiteNoise` | White grain; Value and independent-RGB Color. |
| Procedural/Noise/Grain | `BlueNoise` | Blue grain; Value and independent-RGB Color. |

#### Noise settings, inputs and output types

Use enum names in document JSON. The node settings are separate from numeric ports in
`node.inputs`. `CausticsNoise` uses `option` for Algorithm: `0=OpenSimplex2S`,
`1=Simplex` (default). Other noise algorithms have their own node kinds.

| Node field | Values and default | Effect |
| --- | --- | --- |
| `noiseDimensions` | `OneD`=0, `TwoD`=1 (default), `ThreeD`=2 | Scalar/warp widths are Float, Float2, Float3. Triangle Voronoi and Caustics Noise are fixed to TwoD, with no Dimensions selector. Grain allows OneD/TwoD and always takes Float2 positions. |
| `noiseDistribution` | `Uniform`=0 (default), `FixedRadius`=1 | Cellular and Triangle Voronoi point distribution. |
| `noiseMetric` | `Euclidean`=0 (default), `EuclideanSquared`=1, `Manhattan`=2, `Hybrid`=3 | Cellular distance measure; ignored for DistanceToEdge (Euclidean). Hybrid adds Manhattan and squared Euclidean distance. |
| `noiseOutput` | `CellValue`=0, `F1`=1 (default), `F2`=2, `Add`=3, `Subtract`=4, `Multiply`=5, `Divide`=6, `DistanceToEdge`=8 | Selects Cellular's normalized Value; auxiliary geometry stays hard. |
| `noiseFiltering` | `false` (default) / `true` | Scalar/warp approximate footprint filtering, using derivatives of unwrapped Position. |
| `noisePeriodic` | `false` (default) / `true` | Enables Period and Offset. Disabled keeps both at zero, excluding their uniforms and upstream calculations. |
| `noiseInverted` | `false` (default) / `true` | Inverts White/Blue grain outputs. |
| `fractal` (warp only) | `None`=0 (default), `Progressive`=4, `Independent`=5 | Progressive feeds earlier displacement into later octaves; Independent samples the original position. Scalar/grain noise has no embedded fractal; use the separate Fractal node. |

Scalar and warp nodes share **Position** (implicit `UV.x`, `UV`, `float3(UV,value.z)` for
supported 1D/2D/3D modes; Z defaults to 0), per-axis **Scale=8**, **Offset=0**, **Period=1**,
and scalar **Seed=1337**. Triangle Voronoi stays 2D with implicit `UV`. Enable **Periodic**
to use Offset and Period. Offset shifts Position in input units before Scale and periodic
reduction, using the WT compensated coordinate path. With Periodic off, translate Position
with Add. Positive Period repeats an axis in input-position units; nonpositive Period disables
that axis. Periodic is off by default: unconnected Offset/Period ports are hidden, connected
ones stay visible but inactive. Their values and edges are retained but not evaluated. Use Period on any scalar/warp family to repeat the field; there is no
separate tileable kind or Cells port. Dimensions change the literal types and retain values
and connections, including edges to temporarily unavailable ports such as Curl in 1D. Storage validates ports
across node modes; compilation uses only connections whose endpoints are available in the current
mode. An unavailable output uses the receiving input's saved/default value; through Reroutes,
the chain is treated as disconnected and each downstream consumer supplies its own fallback.
The editor keeps unavailable connected ports visible and disabled, and hides them after disconnection.

Scalar noise nodes evaluate one WT noise level and expose no fractal/Octaves/Lacunarity/
Gain/WeightedStrength/PingPongStrength controls. Connect their result to the separate
[Fractal node](#buffer-and-fractal-nodes) for generic 2D graph-domain repetition.
Warp retains **Octaves=3**, **Lacunarity=2**, **Gain=0.5**, and **Strength=0.1**;
it has no WeightedStrength or PingPongStrength. Octave controls are disabled under None.
Cellular Smoothness applies to all Value modes. Metric is hidden only for Distance to Edge.
Grain Direction applies only in 1D;
Gabor/Spots Direction, Anisotropy and Elongation apply only in 2D/3D. Disabled controls
retain their values and existing edges.

**Evolution=0** is a Float input on Cellular and Triangle Voronoi.
It accepts signed values and continuously moves their shared points within the Jitter
envelope; zero retains the static pattern. Connect Time for animation. Evolution has
no common 0..1 cycle. FixedRadius preserves length in 2D/3D; 1D uses a rotation projected
between the endpoints. Spatial periods hold for a constant or correspondingly periodic
Evolution input. See [Evolution](NoiseLibrary.md#evolution).

Cellular's Add averages the two distances, Subtract uses F2−F1, Multiply uses their product,
and Divide uses F1/F2 before the WT normalization. Smoothness=0 preserves hard values;
larger values smooth the distance pair or blend CellValue neighbors in the selected metric.
DistanceToEdge selects Euclidean boundary distance, with optional smoothing of interior ridges.
Jitter moves feature points; Uniform scatters them through the cell, FixedRadius uses a fixed radius.
Gabor Frequency controls its carrier, Bandwidth its envelope width, Anisotropy directional
coherence, and vector Direction its orientation. Spots Density controls point count, Radius
spot size, Softness the edge, Elongation stretching and vector Direction the stretch axis.
For family limits and normalization details, see the [WT contract](NoiseLibrary.md).

Triangle Voronoi shares Cellular's feature points with matching settings, selecting the
nearest with a triangular gauge. Its dominance regions need not be triangles; it does
not generate a triangular mesh. Coincident gauge faces can produce equality plateaus,
including at Jitter=0; deterministic ties choose a cell without treating plateau interiors
as borders. **Stretch: Float2=(1,1)** changes the gauge along X/Y:
each component uses its absolute value, clamped to `0.5..2`. **Jitter=1** clamps to `0..1`
and controls feature displacement; **Distribution** defaults to Uniform.
It has no Metric or Output selector; Position, Scale, Offset and Period stay Float2.
It uses the shared domain and filtering controls and evaluates one octave,
without Gradient/Curl or embedded octave controls.

Scalar kinds output Float **Value** first, normalized to `0..1` without gamma conversion.
Perlin, Value, Value Cubic, OpenSimplex2/2S, Flow and Gabor also output signed, unnormalized
**Gradient** at the selected width, and **Curl** only in 2D/3D at that width. Cellular's
Float **F1**, **F2**, **CellValue** and dimension-width **ToPoint** expose hard, unfiltered
geometry from the single evaluation, independent of its Value output selection. F1/F2 are raw
distances; CellValue is not a unique ID. There is no uint cell ID port in the numeric graph.
Triangle Voronoi's Float **Value** is `saturate(edgeDistance / sqrt(2))` before filtering,
where edgeDistance is the true nearest dominance-boundary distance. Float **Distance** is
raw Euclidean distance to the triangle-selected feature, rather than boundary distance. Float **CellValue**
and Float2 **ToPoint** identify its value and vector from the sample to that feature.
Float2 **BorderNormal** is a unit direction toward the nearest boundary, or zero at a
degenerate boundary. Distances and vectors use unstretched noise-lattice coordinates.
All four auxiliary outputs are unfiltered first-octave geometry; BorderNormal is not
a Gradient/Curl output. There is no raw edgeDistance or uint ID port.
Gabor's Float **Phase**, **Intensity** and **Phasor** come from the same single evaluation.
**PhaseOffset=0** shifts the common carrier in cycles, affecting Value, Phase, Phasor, Gradient
and Curl. Adding an integer repeats the pattern. Intensity measures the carrier's magnitude,
and Phase returns zero when that magnitude is negligible. Gradient and Curl use derivatives
of the shifted Value with respect to Position, holding all noise settings constant.
**Profile** uses `option` 0 for Sine (default), 1 for Triangle and 2 for Pulse.
**Width=0.5** is Pulse's duty cycle, clamped to 0..1. Width is hidden when unconnected
outside Pulse. Connected Width stays visible but inactive, retaining its value and wire.
Profile and Width affect Phasor only. Phasor uses analytic phase footprints, periodic box
integration and fading near signal zeros inside a pixel. Filtering assumes locally constant
settings, including PhaseOffset and Width, and retains true phase singularities. Point
sampling is used with Filtering off.
The [library contract](NoiseLibrary.md#gabor) defines profiles, filtering and shared traversal.
Warp's signed
**Displacement** and **Position** match the dimension width; Position is the original input
plus Displacement, so Strength=0 preserves it. Connect Position explicitly to another node
or sampler; none of these nodes inserts texture mapping.

White/Blue grain always takes **Position: Float2** (implicit UV), **GrainSize=1** pixel, **Direction=0** degrees, **Seed=1337**, and
**ImageSize: Float2** (implicit CanvasSize). OneD makes directional stripes and TwoD grain.
Float **Value** and **Color** (alpha 1) are evaluated independently using WT's monochrome
and color flag respectively; Color has independent RGB, not replicated Value.
Grain does not expose Scale, Offset, Period, filtering or fractal controls. Translate Position
with Add in UV units; divide a pixel displacement by ImageSize first. Caustics Noise has
a Float2 Offset=(0,0), also enabled only by Periodic, applied to its procedural input position.
Offset is shared by native HLSL and SVM paths. Filtering treats noise settings, including
Offset, as locally constant; use Position for spatial coordinate transformations.

Filtering passes unwrapped Position derivatives to WT before period reduction. Branches
reevaluated by sampled filters use the graph's captured sample-footprint mechanism.
This remains WT's approximate family filtering; Gradient/Curl are not normalized vectors.
Triangle uses a cell-family mean estimate rather than the exact mean of its tile.
Filtering does not guarantee alias-free output. See
[Triangle Voronoi](NoiseLibrary.md#triangle-voronoi) for its geometry and filtering details.

Discovery marks noise entries with `dynamicInputs`, `dynamicOutputs`, `defaultOutput` and
per-port `outputTypes`. `settings.noiseDimensions.values[]` lists each supported dimension's `inputs`,
`outputs` and `outputTypes` (only TwoD for Triangle Voronoi); warp `settings.fractal.values[]` and Cellular
`settings.noiseOutput.values[]` include input availability for that mode. Each noise input
has `active` and, when disabled, `inactiveReason`. `defaultExpression` records implicit
Position and ImageSize values. ImageSize additionally exposes `default: "CanvasSize"`
and omits the numeric `value`: its Canvas/Custom binding selects canvas dimensions or an
explicit literal. A literal Position `value` alone does not describe its implicit UV default.
Boolean settings have `{ type: "boolean", default }`; enum settings expose serialized
`value` and display `name`. Warp entries retain `fractalModes`/`defaultFractal`; scalar noise entries have neither.

### Procedural and UV nodes

Use the exact `kind` and output-port names below when authoring graph JSON. Spaces in editor
labels are not part of serialized kinds. WT noise kinds and their dimension-dependent
outputs are listed [above](#wt-noise-nodes).

| Category | `kind` | Outputs | Purpose |
| --- | --- | --- | --- |
| Math/Random | `Random` | `Out`: Float | Deterministic random values from coordinates and seed. |
| Procedural/Pattern | `Checkerboard`, `Stripes`, `Grid`, `Rings` | `Out`: Float | Antialiased pattern masks. |
| Procedural/Pattern | `GradientFactor` | `Out`: Float | Scalar position for `SampleGradient.T`. |
| Procedural/Shape | `Shape` | `Out`, `Distance`: Float | Smoothed mask and signed distance for Ellipse, Rectangle, Rounded Rectangle, Polygon, Star, Capsule, Line Segment, Rounded Polygon or Rounded Star. |
| Procedural/Shape | `CombineDistance` | `Out`, `Mask`: Float | Combine signed distances and produce a smoothed mask. |
| UV | `TransformUV`, `PolarCoordinates`, `Mirror`, `Kaleidoscope` | `Out`: Float2 | Transform, remap or fold coordinates. |
| UV | `ApplyTransform2D` | `Local`, `Input`: Float2 | Apply a Transform2D resource's input-to-local or local-to-input mapping to UV. |
| UV | `Repeat` | `Out`, `Cell`: Float2 | Repeated coordinates and the corresponding cell coordinates. |
| UV/Distortion | `VectorDisplacement`, `Twirl`, `BulgePinch`, `Wave`, `Ripple`, `RadialShear` | `Out`: Float2 | Offset or deform coordinates. |

Categories are nested branches in Create Node and the connection search. Discovery represents
each path with `/` separators. Noise Value, warp Displacement/Position and grain Value/Color
use explicit port names; there is no generic Out on these noise kinds.

The UV distortion nodes in this table return coordinates, not sampled colors or masks. Connect their `Out` to
the `UV` input of a pattern/sampling node or 2D noise `Position`. They do not clamp coordinates to `0..1`;
`SampleTexture` wrap settings determine how out-of-range image coordinates are sampled.
`Strength = 0` leaves the input UV unchanged; RadialShear additionally needs `Offset = (0,0)`.
Displacement vectors retain their magnitude;
they are not normalized. `TransformUV` belongs to **UV**, independently of the layer Transform.

`Wave`, `Ripple`, `Stripes` and `Rings` expose a separate Float `Phase` input. Supply a parameter
or another scalar expression to shift the pattern; `Time.Out` animates it inside graph previews.
Periodic patterns reduce spatial coordinates and Phase/Offset separately before combining them,
so a large uniform offset does not erase the spatial fraction. Antialiasing uses the signed
differences of the unwrapped components, including in sampled branches, rather than derivatives
across wrapped seams. Pulse and triangle coverage integrate local intervals plus complete periods
without subtracting large accumulated integrals. Phase repeats every cycle; Grid Offset repeats
every unit and Checkerboard every two units per axis. Sine's narrow-footprint attenuation uses
the near-zero sinc limit to avoid dividing an inaccurate tiny sine by its argument. This preserves the supplied float values;
it cannot recover precision already lost in upstream arithmetic or change Time's precision.
Mask edges are smoothed. A signed-distance output is negative inside, zero on the boundary
and positive outside; do not treat it as a `0..1` mask. `Shape.Distance` retains the existing
approximate ellipse distance, so an ellipse is not guaranteed to have an exact Euclidean SDF.
Polygon and Star measure the nearest actual edge in rotated UV space, including nonuniform Size;
their Distance and Feather no longer inherit the shorter axis's scale as an approximation.
Connect two `Shape.Distance` outputs to `CombineDistance` and use its `Mask` for coverage.

For a colored gradient, connect `GradientFactor.Out` to `SampleGradient.T`, a Gradient parameter
to `SampleGradient.Gradient`, and its color `Out` to `Output.RGBA`. `GradientFactor` supplies
the position; it does not hold or sample a gradient resource.

#### Serialized modes and inputs

`node.option` is a zero-based integer, default `0`. The following labels are listed in that
integer order; they are not string values to write into `option`.

| `kind` | `option`: label |
| --- | --- |
| `Stripes` | 0: Square; 1: Triangle; 2: Sine |
| `GradientFactor` | 0: Linear; 1: Radial; 2: Angular |
| `Shape` | 0: Ellipse; 1: Rectangle; 2: Rounded Rectangle; 3: Polygon; 4: Star; 5: Capsule; 6: Line Segment; 7: Rounded Polygon; 8: Rounded Star |
| `CombineDistance` | 0: Union; 1: Intersection; 2: Subtract |
| `PolarCoordinates` | 0: To Polar; 1: From Polar |
| `Mirror` | 0: X; 1: Y; 2: Both |
| `VectorDisplacement` | 0: Signed RG; 1: Packed RG |

`node.fractal` and the new `noise*` settings are separate from `option`; see the
[WT noise settings and ports](#noise-settings-inputs-and-output-types).

All inputs below are Float unless marked Float2. Every `UV` input is Float2 and defaults to
current-pixel UV; `Random.Coordinates` is Float2 and uses the same current-pixel UV default.
Input values belong in `node.inputs`, with exact case-sensitive port names. Discovery returns
their types and defaults; a mode's controls are not additional node-level JSON fields.

| `kind` | Inputs |
| --- | --- |
| `Random` | Coordinates (Float2), Seed |
| `Checkerboard` | UV, Scale (Float2), Offset (Float2) |
| `Stripes` | UV, Frequency, Width, Angle, Phase |
| `Grid` | UV, Scale (Float2), Width (Float2), Offset (Float2) |
| `Rings` | UV, Center (Float2), Frequency, Width, Phase |
| `GradientFactor` | UV, Center (Float2), Scale (Float2), Angle |
| `CombineDistance` | A, B, Smoothness, Feather |
| `TransformUV` | UV, Offset (Float2), Scale (Float2), Rotation, Center (Float2) |
| `ApplyTransform2D` | Transform (Transform2D), UV; outputs Local and Input (Float2) |
| `PolarCoordinates` | UV, Center (Float2), Angle |
| `Repeat` | UV, Scale (Float2), Offset (Float2) |
| `Mirror` | UV, Center (Float2) |
| `Kaleidoscope` | UV, Center (Float2), Sectors, Angle |
| `VectorDisplacement` | UV, Displacement (Float2), Strength (Float2) |
| `Twirl`, `BulgePinch` | UV, Center (Float2), Strength, Radius |
| `Wave` | UV, Strength, Frequency, Direction, Phase |
| `Ripple` | UV, Center (Float2), Strength, Frequency, Radius, Phase |
| `RadialShear` | UV, Center (Float2) = (0.5,0.5), Strength (Float2) = (10,10), Offset (Float2) = (0,0) |

Shape inputs depend on `option`. Use the active mode's port set when wiring or serializing
inputs. Changing Shape mode retains stored values and connections. Connected inactive inputs
remain visible but dimmed; newly introduced inputs receive their defaults.

| Shape `option` | Inputs |
| --- | --- |
| 0, 1, 2 | UV, Center (Float2), Size (Float2), Radius, Feather |
| 3: Polygon | UV, Center (Float2), Size (Float2), Sides, Rotation, Feather |
| 4: Star | UV, Center (Float2), Size (Float2), Sides, InnerRadius, Rotation, Feather |
| 5: Capsule | UV, Center (Float2), Size (Float2), Rotation, Feather |
| 6: Line Segment | UV, Start (Float2), End (Float2), Width, Feather |
| 7: Rounded Polygon | UV, Center (Float2), Size (Float2), Sides, Roundness, Rotation, Feather |
| 8: Rounded Star | UV, Center (Float2), Size (Float2), Sides, InnerRadius, Roundness, Rotation, Feather |

#### Units and evaluation

- Units follow the [angle unit contract](#angle-unit-contract). Manual orientation remains
  in degrees; `PolarCoordinates.Angle` and `Twirl.Strength` use turns. Gabor/Spots Direction is a vector.
  `Phase` is cycles: Stripes, Rings and Wave add it to their periodic coordinate; Ripple
  subtracts it. `Frequency` is cycles per input-coordinate unit. No implicit time is read.
- WT noise normalization, periods, dimensionality and family settings are described
  [above](#wt-noise-nodes); numeric limits follow the [WT library](NoiseLibrary.md).
- Random returns a deterministic scalar in `[0,1)`. Coordinates use current-pixel UV when no
  edge or explicit input literal is present. Custom sets constant coordinates; existing explicit coordinates remain Custom.
  Connect `Repeat.Cell` for one value per repeated cell, or another source to override the default.
  Repeat computes `p = UV * Scale + Offset`, `Out = frac(p)`, `Cell = floor(p)`, including
  negative cell coordinates. `Cell` is unrelated to the index supplied to `InstanceIndex` (currently zero).
- Stripes uses Width only in Square mode. Width is the filled fraction of a period for Square
  Stripes and Rings; Grid has independent X/Y widths. Pattern filtering smooths pixel coverage.
  Shape.Out and CombineDistance.Mask use derivative antialiasing with optional Feather.
  CombineDistance subtracts **B from A**; positive Smoothness blends the distance operation,
  while Feather only affects Mask. Sides rounds/clamps to `3..64` for Polygon and `2..64` for Star;
  Star.InnerRadius clamps to `0.001..1`. Shape.Radius affects Rounded Rectangle only.
  Line Segment's Width is its full thickness.
- GradientFactor first computes `p = (UV - Center) * Scale`. Linear returns its projection
  along Angle plus `0.5`; Radial returns `2 * length(p)`; Angular returns a wrapped turn minus
  `Angle / 360`. Linear and Radial can exceed `0..1`.
- To Polar returns `(wrapped angle in turns, 2 * distance from Center)`, subtracting Angle in turns
  from the angular coordinate. From Polar reads `UV.x` as turns, adds Angle, and uses
  `UV.y * 0.5` as radius around Center. Mirror folds selected axes onto the positive
  half-planes relative to Center. Kaleidoscope rounds/clamps Sectors to `1..128`.
- VectorDisplacement applies `UV + decoded Displacement * Strength` component-wise. Signed RG
  uses the vector directly; Packed RG decodes `2 * Displacement - 1`, making `(0.5,0.5)` neutral.
  Both Strength components must be zero for identity. No vector normalization is performed.
- Twirl, Bulge / Pinch and Ripple have smooth radial falloff and preserve UV when Radius is
  nonpositive. Twirl's Strength defaults to 0.25 turns and is not wrapped before applying falloff.
  Positive Bulge / Pinch Strength magnifies the sampled image; negative values pinch
  it. Wave displaces along Direction while its phase varies along the perpendicular axis.
- `RadialShear` (`kind: 203`) computes `d = UV - Center`, then
  `Out = UV + float2(d.y, -d.x) * dot(d,d) * Strength + Offset`, component-wise.
  Strength and Offset use coordinate units, not degrees or document pixels. There is no
  radius cutoff, normalization, implicit sampling or coordinate clamp.
- Rounded Polygon is `Shape.option: 7`, not a separate node kind. Defaults are Center =
  (0.5,0.5), Size = (0.5,0.5), Sides = 5, Roundness = 0.3, Rotation = 0 and Feather = 0.
  Sides rounds/clamps to `3..64`; Roundness clamps to `0..1`, from sharp corners to a circular
  profile (elliptical under nonuniform Size). Size, Center and degree Rotation place the shape;
  Feather and derivative antialiasing soften Out. Distance is a signed field with zero on
  the contour, negative inside and positive outside; rounded/nonuniform profiles do not
  guarantee an exact Euclidean distance or uniform-width distance bands.

- Rounded Star is `Shape.option: 8`; its menu entry follows Star, while Rounded Polygon
  follows Polygon without changing serialized option numbers. Defaults match Star plus
  Roundness = 0.3. Sides rounds/clamps to `2..64`, InnerRadius to `0.001..1`, and
  Roundness to `0..1`. At zero it evaluates the ordinary Star. Positive rounding trims
  up to half of each incident edge into circular fillets at tips and notches. Collinear
  intermediate vertices remain straight. The contour is evaluated in the normalized
  shape plane and scaled by the smaller half-size; nonuniform Size gives an approximate
  signed field. Both Out and Distance have native HLSL and SVM paths.

### Fade Transition

`FadeTransition` (`kind: 198`, **Filter/Fade Transition**) takes Float `NoiseValue = 0.5`,
`FadeValue = 0` and `FadeContrast = 1`, returning Float `Out` in `0..1`:
`saturate(FadeValue * (FadeContrast + 1) + (NoiseValue - 1) * FadeContrast)`.
Connect a noise or mask to NoiseValue; the node does not generate noise or resample a branch.
For NoiseValue in `0..1` and nonnegative FadeContrast, FadeValue = 0 gives zero and
FadeValue = 1 gives one. Higher FadeContrast narrows the transition; zero gives
`saturate(FadeValue)` independently of the mask. The transition is a clamped linear ramp,
without an additional smoothstep. Out can drive a blend factor or SetAlpha.

### Color and normal nodes

All native nodes below have a single `Out` port. Native `Contrast` (`kind: 200`) uses a
linear-RGB midpoint of `pow(0.5, 2.2)`. The separately cataloged Contrast HLSL function
retains its `Result` port, `Amount` input and midpoint of 0.5; it is a Custom Function,
not the native kind. Existing library nodes are not implicitly replaced.

| Category | Serialized `kind` | `Out` type |
| --- | --- | --- |
| Color/Adjustments | `Hue`, `Saturation`, `Exposure`, `Lightness`, `Contrast`, `ReplaceColor`, `Levels`, `ColorBalance` | Float4 |
| Math/Advanced | `Posterize` | Float4 |
| Color/Conversion | `ConvertColor` | Float4 |
| Color/Conversion | `Luminance` | Float |
| Color/Mapping | `GradientMap`, `ColorCurves` | Float4 |
| Color/Selection | `ColorMask`, `HueRange` | Float |
| Color/Mixing | `MixColor`, existing `Blend` | Float4 |
| Color/Alpha | `SetAlpha`, `Premultiply`, `Unpremultiply` | Float4 |
| Normal | `HeightToNormal`, `NormalBlend`, `NormalEncode`, `NormalDecode`, `NormalReconstructZ`, `NormalUnpack`, `NormalStrength` | Float3 |
| Normal | `NormalLighting` | Float4 |
| Normal | `NormalCurvature` | Convex, Concave, Curvature: Float |
| UV/Distortion | `HeightToDistortion`, `NormalToDistortion` | Float2 |

#### Color inputs and modes

Inputs belong in `node.inputs`. Unless a type is stated, a numeric input is Float. `Color`
is a Color input, default opaque white `(1,1,1,1)`. Resource inputs require a connection except
the explicitly optional ColorCurves inputs. The defaults below describe newly created nodes.

| `kind` | Inputs and defaults |
| --- | --- |
| `Hue` | Color; Offset = 0 turns |
| `Saturation` | Color; Amount = 1 |
| `Exposure` | Color; Stops = 0 |
| `Lightness` | Color; Amount = 0 |
| `Contrast` | Color; Contrast = 1 |
| `ReplaceColor` | Color; From: Color = (0,0,0,1); To: Color = (1,1,1,1); Range = 0.1; Fuzziness = 0.05 |
| `Levels` | Color; InputRange: Float2 = (0,1); Gamma = 1; OutputRange: Float2 = (0,1); Clamp: Boolean = true |
| `ColorBalance` | Color; Shadows, Midtones, Highlights: Float3 = (0,0,0); ShadowRange, HighlightRange = 0.5; PreserveLuminance: Boolean = true |
| `Posterize` | Color; Levels = 8 |
| `ConvertColor`, `Luminance` | Float4 |
| `GradientMap` | Color; Gradient: required Gradient resource |
| `ColorCurves` | Color; Master, Red, Green, Blue, Alpha: optional Curve resources, each identity unless connected or assigned inline |
| `ColorMask` | Color; KeyColor: Color = (0,1,0,1); Range = 0.1; Fuzziness = 0.05 |
| `HueRange` | Color; Range: Float2 = (11/12,1/12) turns; Feather = 1/24 turns; SaturationRange: Float2 = (0.05,0.1) |
| `MixColor` | A: Color = (0,0,0,1); B: Color = (1,1,1,1); T = 0.5 |
| `SetAlpha` | Color; Alpha = 1 |
| `HeightToNormal`, `HeightToDistortion` | Height: required Texture for `heightSource: "Texture"`, Float = 0 for `"Value"`; UV: Float2 = current-pixel UV; Strength = 8; Radius = 1; FlipX, FlipY: Boolean = false |
| `NormalBlend` | Base, Detail: Float3 = (0,0,1); Strength = 1 |
| `NormalToDistortion` | Normal: Float3 = (0,0,1); Strength: Float2 = (1,1); FlipX, FlipY: Boolean = false |
| `NormalReconstructZ` | In: Float2 = (0,0) |
| `NormalUnpack` | In: Float4 = (0.5,0.5,1,1) |

`option` remains a zero-based integer. Mode labels are not serialized strings.

| `kind` | `option`: label | New-node default |
| --- | --- | --- |
| `Levels` | 0: Luminance; 1: RGB | 0 |
| `ColorCurves` | 0: RGB; 1: Luminance | 0 |
| `ColorMask`, `ReplaceColor` | 0: Linear; 1: Perceptual; 2: Chroma | 1 |
| `PaletteQuantize` | 0: Linear; 1: Perceptual | 1 |
| `MixColor` | 0: Perceptual; 1: Linear; 2: Gamma | 0 |
| `GradientMap` | 0: Preserve; 1: Multiply; 2: Replace | 0 |
| `SetAlpha` | 0: Replace; 1: Multiply | 0 |
| `NormalBlend` | 0: Reoriented; 1: Whiteout | 0 |
| `NormalUnpack` | 0: Tangent; 1: Object | 0 |
| `HeightToDistortion`, `NormalToDistortion` | 0: Signed RG; 1: Packed RG | 0 |

An omitted `MixColor.option` is `0` (Perceptual), matching the editor's default.
ColorMask, ReplaceColor and PaletteQuantize default to `option: 1` (Perceptual).

The following are node-level enum fields, separate from `inputs` and `option`. Write enum
identifiers, not their spaced UI labels. Discovery's `settings` object supplies both spellings.

| Field | Applies to | Values in enum order | Default |
| --- | --- | --- | --- |
| `inputColorSpace` | ConvertColor | `LinearRGB`, `SRGB`, `HSV`, `HSL`, `Oklab` | `LinearRGB` |
| `outputColorSpace` | ConvertColor | same as inputColorSpace | `SRGB` |
| `channel` | GradientMap, HeightToNormal, HeightToDistortion | `Luminance`, `Red`, `Green`, `Blue`, `Alpha` | `Luminance` |
| `heightSource` | HeightToNormal, HeightToDistortion | `Texture`, `Value` | `Texture` |
| `derivative` | HeightToNormal, HeightToDistortion | `Scharr`, `Sobel`, `CentralDifference` | `Scharr` |
| `wrap` | HeightToNormal, HeightToDistortion | `Clamp`, `Repeat`, `Mirror`, `Transparent` | `Clamp` |
| `filter` | HeightToNormal, HeightToDistortion | `Point`, `Bilinear` | `Bilinear` |

#### Color, alpha and numeric range

`Contrast` (`200`) computes `(Color.rgb - midpoint) * Contrast + midpoint`, where
`midpoint = pow(0.5,2.2)` (approximately 0.217638). Contrast = 1 preserves RGB;
Contrast = 0 gives the midpoint. It retains Color alpha and signed/HDR RGB without a final clamp.

`ReplaceColor` (`199`) exposes Color `Out` and Float `Mask`. Its shared selection is
identical to ColorMask for the same source/key, Distance, Range and Fuzziness. Out is
`float4(lerp(Color.rgb, To.rgb, Mask), Color.a)`; From/To alpha is ignored. Both outputs
reuse one mask evaluation. The replacement preserves signed/HDR RGB without clipping.

Connections do not convert color spaces. Color ports carry RGBA components without a space
tag; `ConvertColor` performs an explicit conversion, preserving alpha. Ordinary color
operations expect linear RGB. When working with encoded RGB, HSV, HSL or Oklab components,
convert them back to LinearRGB before those operations or Output.
ConvertColor's unconnected input uses a color picker for LinearRGB and raw Float4 numeric
components for every other input space.

ConvertColor uses sRGB-encoded RGB for HSV/HSL, with hue in turns (`1` is a full revolution),
not degrees. HSV conversion clamps negative encoded RGB to zero, but V retains values above 1;
conversion from HSV clamps S to `0..1` and V to nonnegative values. HSL is an SDR representation:
conversion to HSL clamps encoded RGB to `0..1`, and conversion from it clamps L and S to `0..1`.
HSL cannot roundtrip HDR or negative RGB, and HSV cannot retain negative RGB. LinearRGB, SRGB
and Oklab conversions retain signed/HDR values through signed transfer functions and signed
cube roots. Converting a space to itself is an exact bypass, including its out-of-range values.

Hue, Saturation and Lightness use sRGB-encoded calculations internally and return linear RGB.
Hue rotates by Offset in turns; Saturation clamps Amount to nonnegative values. Lightness clamps its
Amount to `-100..100`. These operations retain signed/HDR results without a final RGB `0..1`
clamp. Exposure multiplies linear RGB by `exp2(Stops)`. Color Balance applies the three Float3
adjustments according to tonal weights and optionally preserves luminance. Posterize uses
`max(2, floor(Levels))` steps including the 0 and 1 endpoints, without clipping RGB to that range.
Luminance computes `dot(rgb, (0.2126,0.7152,0.0722))` from linear RGB, without alpha or a `0..1` clamp.

Levels maps InputRange through Gamma into OutputRange. In Luminance mode it adjusts the
brightness of RGB together; RGB mode maps channels independently. Its bottom **Clamp Input** checkbox
defaults to true and bounds values to InputRange. With Clamp false, signed-gamma extrapolation
allows negative values and values above the range. OutputRange is a remapping range, not a
general output clamp. Gamma is bounded below by `0.0001`; an equal or reversed InputRange
becomes a step at its first endpoint. Luminance mapping scales RGB by the new/old luminance
ratio; near zero luminance it adds the luminance difference instead.
The checkbox retains the Boolean `inputs` entry named `Clamp`. Existing connections to
Clamp remain visible and take precedence; the checkbox is disabled while connected.

Gradient Map samples the chosen channel through the connected Gradient, whose wrap settings
govern coordinates outside `0..1`. Preserve keeps source alpha; Multiply multiplies it by the
gradient alpha; Replace uses gradient alpha. Color Curves applies Master in RGB or Luminance
mode, then the individual channel curves. An assigned or connected curve uses
`x + curve(saturate(x)) - saturate(x)`: the input to the curve is limited to `0..1`, but values
outside that interval retain their excess. Identity curves therefore preserve HDR and negative
values. Unassigned curves are identity; alpha changes only through its own curve.

Color Mask outputs 1 for a matching KeyColor. It shares its distance calculation with
Replace Color and Palette Quantize: Linear compares linear RGB; Perceptual compares Oklab.
Chroma, available for Color Mask and Replace Color, compares Oklab a/b while ignoring L;
it remains sensitive to saturation. Alpha is ignored. No metric clips RGB or normalizes distance.
Range = 0.1 is the fully selected radius and Fuzziness = 0.05 the outer linear fade:
`mask = saturate(1 - (distance - max(Range,0)) / Fuzziness)` for positive Fuzziness.
Nonpositive Fuzziness gives the exact inclusive test `distance <= max(Range,0)`.
Neither control has an upper limit. The former Tolerance/Softness ports are not accepted.
Hue Range selects a cyclic interval in turns: the default `(11/12,1/12)` crosses red.
An absolute span `abs(Range.y-Range.x) >= 1` selects the full cycle before wrapping endpoints;
equal endpoints select nothing unless Feather is positive. Feather is in turns, default `1/24`;
SaturationRange fades selection for low-saturation colors, even with a full hue interval.
Feather clamps to `0..0.5` and cannot
extend past the opposite hue; SaturationRange endpoints clamp to `0..1`. Neither mask multiplies
by input alpha.

Mix Color interpolates RGB in Linear, Gamma or Perceptual space and interpolates alpha
linearly. T is unbounded, so values outside `0..1` extrapolate. This differs from Blend's
alpha compositing and Lerp's raw component interpolation. Set Alpha replaces or multiplies
alpha without changing RGB or clamping the result. Other color adjustments preserve source
alpha. Color ports do not add a general clamp; use an explicit Clamp and the layer's Rendering
color-range controls when a limited result is required.

The Convert Color UI uses **Linear**, **Gamma** and **Perceptual** for the
`LinearRGB`, `SRGB` and `Oklab` enum values; the stored identifiers are unchanged.

#### Height, normals and displacement

`NormalReconstructZ` (`201`) treats `In` as signed XY and returns
`normalize(float3(In, sqrt(1 - saturate(dot(In,In)))))`. Its Float3 `Out` is signed XYZ
with nonnegative Z, including `(0,0,1)` for zero XY; XY outside the unit disk gives a
normalized XY direction with Z = 0. It does not decode packed RG.

`NormalUnpack` (`202`) returns signed Float3 XYZ. Tangent (`option: 0`) calls Unity's
`UnpackNormal` from `UnityCG.cginc`, following the active platform's imported normal-texture
packing. Supply the sampled Float4 so its alpha channel remains available to the decoder.
Object (`option: 1`) returns `In.rgb * 2 - 1`, ignoring alpha, without normalization.
Object mode decodes RGB; it does not transform between coordinate spaces. Neither mode
has an Encoding selector. Ordinary packed RGB data can use Normal Decode or Object mode;
an imported tangent normal map needs the Tangent decoder.

Channel extraction for height/mask consumers uses `channel` (Luminance by default).
It runs before Float conversion and affects only the node inputs listed by API discovery
`settings.channel.inputs`. Scalar values remain unchanged. Numeric vectors and Color use
Linear RGB luminance or the chosen component; missing RGB components are zero, missing
alpha is one. No implicit clamp or Gamma conversion is added. One setting applies to all
listed inputs; explicit upstream Split/Luminance can supply independently chosen scalars.
Ordinary numeric casts continue to select X when narrowing to Float. Sampling-function
reuse includes the selected channel, keeping different consumers of one branch independent.

Height to Normal and Height to Distortion use node-level `heightSource`, default `Texture`.
In Texture mode, connect a **Texture** resource to Height. Channel selects its height component;
Radius measures bound-source texels, independent of graph-preview size or document Pixel Scale.
Wrap/Filter control those texture reads. In Value mode, Height is **Float** after channel selection: connect a noise,
mask, color or numeric branch. The compiler reevaluates that branch at neighboring UVs;
Radius measures document pixels, independent of node-preview resolution.
Channel applies equally to Texture and Value. Value may receive Color or a numeric vector;
Channel extracts its scalar height before neighboring evaluation. Scalar inputs pass through
unchanged. Wrap/Filter still belong to the texture sampler; upstream sampling nodes keep their own settings.
UV defaults to the current pixel in both modes. Strength, FlipX/FlipY and the Scharr, Sobel or
Central Difference derivative remain available. The finite difference is divided by the
sampling distance, so Radius changes the neighborhood without directly multiplying the slope.
An upstream branch with fixed coordinates remains constant when reevaluated at another UV.

### Signed normal contract

Normal processing uses **Signed XYZ** throughout the graph, with neutral `(0,0,1)`.
Height to Normal returns `normalize(-dx,-dy,1)`. Normal Blend combines signed Base and Detail
using Reoriented or Whiteout, with Strength controlling Detail, and normalizes its result.
Normal Strength, Normal Lighting and Normal Curvature also take signed XYZ. There is no
per-node Encoding selector or implicit decoding on wires, in math, or at lighting boundaries.

Previews display the actual XYZ components as RGB, without mapping `[-1,1]` to `[0,1]`.
The flat normal therefore looks blue. Negative channels cannot appear as negative display
brightness, but retain their signed values in calculations and floating-point preview targets.
Use Normal Encode (`n*0.5+0.5`) explicitly to obtain packed RGB for export or visualization;
use Normal Decode (`rgb*2-1`) to read ordinary packed RGB. Normal Unpack remains the explicit
conversion for imported texture packing. These vector operations perform no color-space conversion.
HLSL, SVM and separate lighting preparation share this contract.

Stored nodes no longer have `normalEncoding`. JSON with that removed field is rejected as an
unknown field; there is no compatibility conversion. Saved literals remain literal values.

Height to Distortion keeps the finite-difference gradient's magnitude. Normal to Distortion
takes signed Normal XY without normalization and scales its components by Strength. FlipX
and FlipY reverse the corresponding displacement components. Both return **displacement**,
not UV: connect Out to `VectorDisplacement.Displacement`, match its Signed RG/Packed RG mode,
then connect `VectorDisplacement.Out` to a sampling node's UV. Signed neutral is `(0,0)`;
packed neutral is `(0.5,0.5)`. Packing does not clamp strong or negative displacements to `0..1`.
For a slope `(dx,dy)`, Height to Normal builds `normalize(-dx,-dy,1)`, while Height to Distortion
uses `(dx,dy)`. Routing a generated normal through Normal to Distortion therefore changes both
the sign and magnitude compared with direct height displacement. Reverse Strength or FlipX/Y
when the desired distortion direction requires it.

## Height analysis nodes

`HeightCurvature` (210), `HeightToAO` (211) and `HeightShadow` (212), in **Height**, sample
a Float `Height` branch, default 0.
Their Float2 `UV` defaults to current-pixel UV. To use a height texture, connect a channel
from Sample Texture to Height; these nodes do not take a Texture resource directly.
The connected Height calculation is reevaluated at sample UV, while explicit fixed
coordinates remain fixed. Upstream texture sampling retains its own wrap/filter behavior.

| `kind` | Inputs in addition to Height and UV | Outputs |
| --- | --- | --- |
| `HeightCurvature` | Radius=2 pixels; Strength=8 | Convex, Concave, Curvature: Float |
| `HeightToAO` | Radius=16 pixels; HeightScale=16 pixels; Strength=1; Bias=0.05 pixels; Directions=8 (1..16); Steps=4 (1..16) | Out: Float visibility |
| `HeightShadow` | LightDirection: Float3=(0.5,0.5,1); HeightScale=16 pixels; Distance=64 pixels; Steps=32 (1..64); Bias=0.05 pixels; Softness=0.1 (angular) | Out: Float visibility |

All listed numeric controls are scalar unless marked Float3. Pixel units are document pixels,
independent of preview resolution. HeightScale converts one unit of input height into that
pixel height; Radius and Distance measure horizontal search extent, while Bias measures a
height tolerance. Directions and Steps round to the nearest integer and clamp to the listed
ranges. Radius, Strength, HeightScale, Distance, Bias and Softness use nonnegative values.
Softness is angular, not a pixel blur radius.

Height Curvature compares the center with four axial neighbors separated by Radius:
`difference = center - (left + right + down + up) / 4`, then `Curvature=difference*Strength`,
`Convex=saturate(Curvature)` and `Concave=saturate(-Curvature)`. Positive response denotes a
local convex feature, negative response a concavity. Curvature retains the signed, unclamped
response; zero Radius or Strength returns zero on all three outputs. This approximation depends on
sampling radius and source scale, not a geometric mesh-curvature measurement.

Height to AO estimates local occlusion from sampled horizons in Directions directions
and Steps distances per direction. Horizons are measured relative to the local tangent
plane, so a flat ramp remains unoccluded. `Out=1` means fully visible; `Out=0` means fully
occluded. The node first reads the center and four one-pixel neighbors to estimate the
tangent, then Directions × Steps horizon samples: at most 261 Height evaluations. Zero
Radius or Strength returns 1. Strength controls the darkening and Bias offsets the height comparison.

Height Shadow approximately ray-marches the height field toward the light along
LightDirection in the local UV/height frame, bounded by Distance and Steps. Steps counts
samples beyond the center, for at most 65 Height evaluations. `Out=1` is lit and `Out=0`
is shadowed; Softness permits intermediate visibility near a shadow boundary.
It produces a lighting mask and does not displace UV,
write depth or alter geometry. Both AO and shadow sampling can miss narrow blockers.

All three nodes have native HLSL and SVM implementations. Height is the sampled branch;
larger sample counts and nested sampled nodes multiply its cost. The existing SVM branch
work/nesting budget applies, with native HLSL fallback beyond the budget. An explicit
Buffer may be placed before Height to reuse a costly height calculation; none is inserted
automatically. Baking can lose subpixel height detail, which also changes these results.

## Normal Curvature

`NormalCurvature` (219) takes Float3 `Normal`, Float2 `UV` (current pixel by default),
Float `Radius=1` document pixel and Float `Strength=1`. It reevaluates the Normal branch
at nearby sample UV. A Texture resource must be sampled into a normal vector first.
Normal uses Signed XYZ, neutral `(0,0,1)`. Decode packed texture values explicitly before connecting them.

The node normalizes four signed samples at left/right/down/up. For positive Radius,
`Curvature=Strength*((right.x-left.x)+(up.y-down.y))/(2*Radius)`, with a guarded denominator.
Radius and Strength clamp below at zero; Radius=0 returns zero. Float `Convex` and `Concave`
are `saturate(Curvature)` and `saturate(-Curvature)`; Curvature stays signed and unclamped.
There is no Out port. This estimates the XY divergence of the normal field; a constant
normal gives no curvature detail. Radius changes
the sampled detail scale. It does not reconstruct a height map or geometric mesh curvature.
Repeated normal sampling adds the cost of the upstream Normal calculation at each point.
Native HLSL and SVM use the same four-sample operation, with HLSL fallback beyond the SVM
sampled-branch work/nesting limits.

## Palette Quantize

`PaletteQuantize` (213) takes `Color` (default opaque white), `Palette` (Gradient resource)
and Float `Colors=8`, rounded and clamped to `1..64`, and returns `Out: Color`. It constructs
candidate colors by sampling the gradient evenly across `0..1`, at `t=i/max(count-1,1)`.
With Colors=1 it samples t=0. The bottom **Distance** selector uses `option:0`, **Linear**,
to choose the smallest squared distance in linear RGB. `option:1`, **Perceptual** (default),
converts source and candidate RGB to Oklab for the distance comparison. The selected
candidate's original RGB is returned in either mode; the output is not Oklab-encoded.
The bottom **Palette** selector is **Samples** by default (`paletteKeys:false`), using
the candidate count above. **Gradient Keys** (`paletteKeys:true`) instead uses every authored
color stop in time order, without interpolation, midpoint shaping or alpha stops. Key colors
convert to Linear and retain HDR. Custom gradients use the combined RGB marker colors.
`Colors` becomes inactive; its value and connected edge remain stored. Equal distances
retain the first candidate. Gradient key edits update bound data without recompiling.
All authored keys participate (the gradient model allows 1–64 color positions); cost grows
with the number of keys. This is color-distance quantization, not
luminance-to-gradient mapping or per-channel posterization.

The source alpha is preserved. Gradient alpha does not replace it or participate in either
distance metric. The node has native HLSL and SVM paths; the existing branch budget/fallback
rules still apply when it is part of a repeatedly sampled branch. It does not sample a
spatial neighborhood or introduce its own UV input.

Gradient parameters also expose `<name>_KeyCount()` and `<name>_Key(int index)` in Code;
keys share the gradient resource and return Linear RGB with alpha 1. Indices clamp to the
available range. `<name>_Sample(t)` keeps its interpolation, wrapping and alpha behavior.

## Parallax UV nodes

`ParallaxMapping` (`kind: 208`) and `ParallaxOcclusionMapping` (`kind: 209`) belong to
**UV/Distortion**. Their only output, `Out: Float2`, is displaced UV: connect it to sampling
nodes for the color, normal and other surface maps. They do not write depth or change geometry.
Search aliases are **Bump Offset** / **Parallax Offset** for Parallax Mapping and **POM**
for Parallax Occlusion Mapping.

| Input | Type and default | Meaning |
| --- | --- | --- |
| `Height` | Texture in Texture mode; Float = 0.5 in Value mode | Height source, with 0.5 as the unchanged reference plane. |
| `UV` | Float2, current-pixel UV | Coordinates before displacement. |
| `ViewDirection` | Float3 = (0.5,0.5,1) | Surface-to-viewer direction in the same local frame as UV; positive Z faces the viewer. Normalized internally. |
| `Amplitude` | Float = 0.05 | Relief displacement scale in UV units, independent of document resolution. |
| `Steps` | Float = 16, POM only | Rounded and clamped to 1..64 search steps. |

Node-level `heightSource` selects `Texture` (default) or `Value`. Texture mode reads the
selected `channel` (`Luminance` by default) with `wrap: Clamp` and `filter: Bilinear` defaults.
These three settings are inactive in Value mode. Value mode reevaluates the connected Height
branch at each sample UV; its sampling nodes retain their own settings. A source with fixed
coordinates remains constant. Neither mode creates an implicit Buffer.

For normalized direction `v`, Parallax Mapping samples height once at UV, then returns
`UV + (height - 0.5) * Amplitude * v.xy / (v.z + 0.42)`. It does not clamp height.
Parallax Occlusion Mapping clamps sampled height to `0..1`, uses
`ray = Amplitude * v.xy / max(v.z, 0.05)`, and searches from height 1 to 0 around the
0.5 reference plane. `Steps` sets the minimum coarse count. The actual count is
`max(round(clamp(Steps,1,64)), ceil(64*saturate(length(ray))))`: long displacements receive
more samples without depending on canvas resolution. This targets a coarse stride of
1/64 UV until the 64-step cap, not a bound on arbitrary source frequencies. When adaptation
adds coarse steps, the crossing uses two secant refinements instead of three. The maximum
is still 68 Height evaluations, with no extra SVM frame storage. Both return unchanged
UV for zero ViewDirection or `v.z <= 0`; a perpendicular direction also has no XY displacement.
Coordinates are not clamped to `0..1`; final texture samplers control their own wrap behavior.
The shared HLSL/SVM guard applies to every Height source. Runtime NaN/Infinity in Height
uses the neutral height 0.5; invalid ViewDirection/Amplitude or overflowing displacement
leaves UV unchanged. Invalid UV falls back to (0,0), and invalid Steps uses a base count of
one before adaptation.
Finite large view vectors normalize without overflow. Secant denominators remain safe
even when a compiler evaluates both sides of a conditional. Texture reads use explicit
level zero; built-in derivatives in sampled branches use the captured pixel footprint.
These guards do not suppress compiler errors or diagnostics from user HLSL.
SVM uses finite differences for derivatives inside sampled Height. A branch requiring hardware
derivatives outside that sampled source uses native HLSL, as does a branch exceeding the VM
nesting or replay budget.

POM can occlude farther parts of the height field and costs repeated source evaluations.
Use the existing Buffer explicitly before a costly Value source when its baked sampling is
appropriate. Both nodes have HLSL and SVM paths with the same bounded search; graphs beyond
the existing SVM nesting/work limits use HLSL fallback.

## Erosion Noise

**Procedural → Noise → Erosion Noise** is a built-in node with kind `ErosionNoise`.
It combines a three-octave WT Perlin base with directional WT Gabor detail whose
successive octaves follow the base slope and accumulated detail. This is procedural
erosion styling, not a water/soil simulation. It needs no input image or buffer.
Inputs: `float2 Position` (default UV), `float2 Scale = (3, 3)`, `Seed = 1337`,
`Strength = 0.15`, `Detail = 4`, `SlopeStrength = 2`, `Branching = 0.1`,
`Octaves = 5`, `Lacunarity = 2`, `Gain = 0.45`, `float2 Period = (1, 1)`,
and `float2 Offset = (0, 0)`. The bottom **Periodic** checkbox writes
`noisePeriodic` (default false). Offset shifts Position before Scale. Period and
Offset are inactive while Periodic is off. Existing connections remain visible and dimmed.
Scale affects both spatial axes;
Detail sets the initial detail frequency, SlopeStrength increases carrier frequency
with guide magnitude, and Branching controls feedback into finer detail directions.
Octaves is floored and clamped to 0–8, Gain to 0–1; absolute Detail has a minimum
of 0.001 and absolute Lacunarity a minimum of 1. Negative SlopeStrength/Branching
act as zero. Outputs are scalar `Base` (unmodified terrain), `Erosion` (signed,
Strength-scaled detail) and unclamped `Value = Base + Erosion`. Zero Strength or
zero octaves returns Base. Large Branching can introduce fine irregular patterns.
Internal partial derivatives guide the detail, not exact final normals; use
Height to Normal on Value for shading. Periodic applies the WT lattice-period
fitting to the base and every Gabor octave, including their slope guides. Period
is measured in incoming UV units, before Scale: (1, 1) tiles the UV square.
Nonpositive Period components disable that axis. Fractional Scale, Detail and
Lacunarity are supported; fitting may slightly change feature frequencies.
Other inputs must stay constant or repeat over the same tile for seamless output.
This fixed 2D node uses native HLSL, including in previews. It has no filtering
or animation control. Existing HLSL placements continue to run from their
embedded source. Create the built-in node to use the shared noise controls.

## HLSL functions

### File declarations and ports

Declare a catalog entry with `// @whimtex-node Category/Name`, followed by optional existing
`@param` metadata, `@if`/`@endif` visibility blocks and an optional `@preset("GUID", version)` pair, then the HLSL function
definition. Metadata belongs to that function; multiple marked functions may share one file.
The preset pair uses a nonempty GUID and a positive integer version. Without it, a saved file
path and exact function variant identify an explicit refresh candidate.

Inputs accept `float`/`half` scalars and vectors of two to four components, `bool`, and
`WhimTexTexture`. `float1`/`half1` are scalar aliases. The return value becomes **Result**;
`out` arguments become named outputs and `inout` arguments expose both input and output ports.
`void` functions need at least one output argument. Texture values are input-only. Arrays,
matrices, arbitrary structs and default argument expressions are not supported port declarations.

`@param` names must match actual argument names. For a non-void function, `@param ... Result`
may describe the return port; an argument also named Result makes that metadata ambiguous.
Metadata and HLSL must have the same component count and numeric/Boolean/resource type;
float and half precision aliases normalize to the same port width. `color(float4)` or
`color(half4)` selects Color semantics for a four-component argument or return value.
Metadata supplies defaults, labels and numeric bounds to the portable port definition.
Boolean inputs declared with `@param toggle(bool) Name = false` appear as bottom
checkboxes, using the saved input value without changing the HLSL signature.
Plain Boolean arguments remain ordinary ports. `@if Name == true`/`@endif` can
conditionally hide other inputs; the driver must be an unconditional Boolean option.
Hidden inputs keep values and connections. Connected hidden ports stay visible but
dimmed. These are presentation rules only: the HLSL function must ignore inactive
arguments itself. Options are excluded from connection-search choices. Existing
option connections remain usable and keep their port; the checkbox is then disabled
and dependent ports stay visible because the driver is not statically known.
Texture inputs require graph
connections; texture metadata cannot bind an external image.

Input-only `float2`/`half2` arguments named exactly `UV` or `uv` default to the current pixel UV.
The port's **Custom / UV** control can select a numeric value instead. Other input names have
ordinary defaults; there is no automatic UV binding for `out` or `inout` arguments.

The bundled **Color/Adjustments/Contrast** node is
[`src/ShaderGraphFunctions.hlsl`](https://github.com/DCFApixels/WhimTex/blob/main/src/ShaderGraphFunctions.hlsl),
at `Packages/com.dcfapixels.whimtex/src/ShaderGraphFunctions.hlsl` in the project. It changes
contrast around 0.5, retains alpha and leaves HDR values unclamped. Its declaration follows
this pattern (the library also declares its own preset identity):

```hlsl
// @whimtex-node Color/Adjustments/Contrast
// @param color(float4) Color = (1, 1, 1, 1)
// @param float Amount = 1 [0 .. 4]
// @param color(float4) Result
void WhimTexNodeContrast(float4 Color, float Amount, out float4 Result)
{
    Result = float4((Color.rgb - 0.5) * Amount + 0.5, Color.a);
}
```

### Custom Function editing

**Utility/Custom Function** creates an inline node. Configure its **Ports**, edit the function
body in **Code**, then choose **Compile**. The node reuses the existing HLSL code editor.
Code fills the node width without a bottom gap. It can be expanded or collapsed; the resize
handle in its lower-right corner changes node width and code height,
with the ordinary node width as the minimum. The displayed signature and active graph ports
continue to describe the applied function while body or port edits remain drafts.
Collapsing Code restores the normal node width; expanding restores the resized dimensions.
Both the expansion state and custom dimensions are saved per node in the document.

`draft` and optional `draftPorts` are saved independently from `body` and `ports`. Compile
validates a standalone invocation even when the node is unconnected, then applies body and
signature together on success. An error leaves the last successfully compiled definition intact.
**File…** selects a supported function from an `.hlsl` file, including unmarked functions.
Catalog entries use ordinary node styling and preview, without constructing a code field,
function toolbar or resize control. **Refresh HLSL**, **Choose HLSL File…** and **Convert to
Custom Function** are available in their context menu.

Conversion clones the applied definition into an independent local function (`inline: true`,
`fullSource: true`), clears preset/path/fingerprint identity and preserves node ID, values,
ports, connections and include closure. Shared definitions stay unchanged for other nodes.
`body` and `draft` initially contain the archived main module, not an expanded include bundle.
The code editor edits the full source; its entry point remains `entryPoint`. On Compile the
portable parser resolves that declaration and applies signature changes with the code.
Module declarations replace the separate Ports editor. The compiler emits the full module
inside its own namespace, without wrapping it as a function body. Other local Custom Functions
keep `fullSource: false` and their existing body/Ports workflow. Conversion is one Undo operation.

An inline body may contain `#include` snippets. Compile captures the applied body and its
complete dependency closure in `graph.sources`, assigning the inline function a `sourceId`;
without includes, `sourceId` is null. The archived root's `code` must exactly equal the applied
`body`, otherwise validation fails. Compilation expands the archived body. API/JSON input
with inline includes but no archive is rejected; loading never fetches dependencies from disk.
New inline functions resolve relative includes from `Assets`; prefer explicit `Assets/...` or
`Packages/...` paths. Source cleanup follows every function's `sourceId`, including inline ones.
For converted full-source functions, Compile retains archived bindings for unchanged include
names, even if the original files changed or disappeared. Newly added includes are captured
from disk using the same path rules. Source changes are staged and validated before applying.

### Discovery and explicit refresh

`ShaderGraphFunctionCatalog.Entries` caches project/package and user-library discovery.
**Refresh HLSL** rescans it explicitly. Ordinary discovery parses only marked files; it does
not capture include archives. File picking uses `LoadFile(path)` with unmarked functions
enabled. Duplicate menu paths receive signature/path suffixes so every entry remains selectable.
`Errors` exposes catalog diagnostics for the editor.

`Entry.Import(graph)` clones the definition, captures the full source dependency closure and
adds a new function to the graph. Each import has its own definition and values; equal source
modules may share the archive. `sourceFingerprint` records the captured root content key,
including dependency bindings. Link checks cache closure fingerprints until the next refresh.

`Find(definition)` requires a unique identity/version and matching function variant plus an
exact backup fingerprint. `FindForRefresh(definition)` permits changed code for the same unique
identity/version and variant; it also checks the saved path for unmarked or external functions.
The library node's **Refresh HLSL** action validates the candidate before applying it. Rendering always uses
the document archive. Missing, changed, incompatible or ambiguous sources do not automatically
replace it; failed refresh retains the backup. Use **Choose HLSL File…** for an intentional replacement.

On Unity 6.5+, the optional adapter reads only public `ShaderInclude.Reflection.ReflectedFunctions`.
Functions marked with `UNITY_EXPORT_REFLECTION` can be converted into the same portable model;
WhimTex declarations take precedence. Automatic native discovery excludes `Packages/com.unity.*`
engine libraries. Explicit file import still reports unsupported signatures or missing includes.
The adapter maps native display/search names, category, provider key, Color, default, range and
UV hints. A native provider key is retained as an opaque identity with version 0. Unsupported
native functions report individual diagnostics. Unity 6.3 uses the portable declarations and
archived source without depending on the native reflection API.

### Archived module compilation

`ShaderGraphModuleSource.Prepare` prepares archived code for an isolated namespace per root.
It preserves macro names and token pasting (`##`), using ownership markers and conditional
cleanup. An active `#define` or `#undef` that collides with an already global macro produces a
diagnostic instead of changing that macro. This preparation does not rewrite the saved source.

Unity expands engine includes at module scope outside the wrapper namespace, retaining their
preprocessor conditions. Engine includes inside nested namespaces or functions are rejected,
as are scope-changing macros used with engine includes. Include placement and macro scope
must therefore remain explicit in an imported module.

## Authoring and persistence

The editable graph and parameter data belong to the shared program. Preserve its node identities,
connections, parameter declarations and values when storing or copying the layer; generated
HLSL is a compilation product, not a substitute for the editable graph. The graph is the saved
source of truth; retaining a working runtime shader does not replace the saved draft with it.

`ShaderGraph.Validate()` validates storage structure, not compilation readiness. Structurally
valid incomplete drafts can be stored, including a Parameter node with no declaration assigned.
Unconnected resource inputs use inline values or defaults. TIFF additionally requires a working version of the selected mode;
JSON can retain a draft before its first successful compile. Passing storage validation does not establish that
the draft can compile. Complete the missing inputs and resolve compilation diagnostics before
treating the current graph as a valid image generator.

Use the [document format](JSON_FORMAT.md) and [generated model schema](AI/document.schema.json)
for exact serialized fields, and the [Agent API](AgentAPI.md) for supported operations. This
document model stores the shared `program` object; it does not introduce new `@param` syntax.
Effect directives describe Code and must not replace Graph parameter declarations.
Standalone presets use the separate [shader preset envelope](ShaderFX.md#portable-shader-presets).

The embedded program retains both modes, functions, Sub Graphs and captured include sources
for offline reopening and editing. Missing preset source must not erase the editable graph.
Saving retains unapplied drafts and uses the document's atomic-save contract; saving is not
an implicit Compile. A portable preset cannot retain document-layer texture references,
including inactive selections: clear those references before export.

### Embedded HLSL source archive

Custom Function definitions are stored in `graph.functions`; nodes refer to them by
`functionId`. A file-backed definition refers to its embedded backup through `sourceId`.
The optional `draftPorts` list stores pending signature edits alongside `draft`; the existing
`ports` and compiled body remain separate from those pending edits. A nonempty `draftPorts`
implies an active draft; `hasDraftPorts: true` also represents an intentionally empty draft.
An empty/null list without the flag is not a draft. This distinction survives Unity domain reload,
Undo and JsonUtility copies, which can materialize null lists as empty lists. The UI displays the
applied ports in that case. Successful Compile clears the draft list and its flag.

`graph.sources` is a list of `{ id, code, includes }` modules. `code` is the original text,
including its include directives. Each `includes` entry is `{ requested, sourceId }`, where
`requested` is the exact include string without its quotes or angle brackets. Bindings belong
to the including module: two files may resolve `"common.hlsl"` to different dependencies.
Repeated directives with the same requested spelling use one binding and still expand at
each original directive location. The archive service does not prune sources, branches or
unreferenced entries; editor structural cleanup may remove obsolete function versions separately.

`ShaderGraphSources.Capture(source, path, destination)` accepts an absolute source-file path
(including the user node library), or an `Assets/...` or `Packages/...` path. Package paths
resolve through the public package API. The source string may contain unsaved edits; the
supplied path establishes the relative-include base. A null path permits inline source with
explicit project/package includes. Capture returns an opaque `src_<sha256>` ID and merges
modules only when their code and dependency bindings match. It publishes additions only after
the entire capture succeeds; existing entries remain unchanged on failure.

`Expand(sourceId, sources)` validates and expands only the saved archive, without reading any
source files. Preprocessor order and conditions are retained, including includes in inactive
branches. Capture therefore requires every non-engine literal include to resolve, even inside
`#if 0`. Macro-generated includes and unknown missing filenames fail with diagnostics.
Only exact names in the engine whitelist may remain unbundled, including `UnityCG.cginc` and
`ShaderApiReflectionSupport.hlsl`; arbitrary path prefixes or matching filename suffixes do
not qualify. Unity 6.3 compilation removes the optional reflection support include and export
marker in the compiler adapter, without changing the archived text.

Explicit include guards are preserved. Top-level `#pragma once` is represented by a stable
module guard during expansion; the stored directive remains unchanged. Conditional
`#pragma once` and all include cycles, including guarded cycles, are rejected explicitly.

Limits are measured in UTF-8 bytes: 8 MiB per source, 32 MiB per archive and expanded result,
4,096 modules, 32,768 include directives and 64 nested modules. Expansion also limits include
visits to 65,536. Capture limits inspected input to 256 MiB and 65,536 file visits, even when
duplicate modules reduce the final archive size. Source IDs contain 1–128 ASCII letters,
digits, underscores or hyphens; saved IDs are opaque and can remain stable during explicit edits.

JSON, clipboard and Unity Undo retain plain source strings. Native TIFF encodes program
`draftCode`/`workingCode`, graph source `code` and function `body`/`draft` strings using tagged value 33 and container blocks named
`shader-source:sha256:<text digest>`; null drafts remain null.
Compression uses the existing container codec; identical text is stored once across every
program in the TIFF, including layer and FX programs, even when modules have different include bindings. Bindings and source
IDs stay in the document model. Reads check the block size and SHA-256 before reconstructing
ordinary strings, with a 32 MiB limit on the unique shared text table. Older readers fail on
the unknown tag instead of interpreting the block reference as source code.
Compilation and node-preview fingerprints include module text and include bindings, even
when source IDs remain unchanged. Text hashes are cached until the string changes.

### Create from a connection

Dropping a new wire from an input or output on empty graph space opens the existing search window,
filtered with the same numeric/color conversions and resource-type rules as ordinary wiring.
The menu opens to the right of the cursor with its left edge anchored there; native screen-edge fitting still applies.
Dragging from an output lists compatible inputs as `NodeName(InputName)`; dragging from an input
lists compatible outputs as `NodeName(OutputName)`, including declared parameters and HLSL functions.
Each compatible port is a separate choice. Input searches respect downstream Reroute constraints
and exclude disabled outputs. Creation and connection
share one document Undo operation; cancellation changes no model data. The node uses the drop
position in graph coordinates. Selection revalidates the source and current type, graph identity,
editability and capacity limits. Choosing a source for an occupied input replaces its old connection
in the same Undo operation; cancelling keeps it. Parameter choices create a node using the existing
parameter, without duplicating its declaration. Output and Reroute are excluded from this creation
menu; the existing Output remains unique and Reroute still inserts into an existing connection.

### Reroute storage and compilation

A Reroute node uses JSON `kind: "Reroute"`, with ports named `In` and `Out`. Store its node ID,
`position` and ordinary graph edges alongside the other nodes. Its `valueType` stores the type
used to display the node while disconnected. When connected, the incoming source determines
the forwarded type and value; `valueType` does not cast, override or supply a default value.

A chain without an available source is a logical disconnect, without a compilation error.
HLSL, SVM, type inference and preview invalidation use each receiving input's saved or default
value, including UV and resource defaults. Reroute's display type supplies no value or cast.
Nodes and edges remain stored and editable; reconnecting restores normal propagation. Cycles
and invalid connections still undergo ordinary validation. A connected chain forwards the
source expression or resource through `Out` without adding HLSL calculations.
Discovery marks this input with `passthrough:true`; it accepts the source's numeric, Color,
Boolean or resource type. Dynamic math inputs accept numeric and Color values and normalize
Color to `float4` for computation.

### API and document JSON

`settings.shader` accepts a complete `program`, a catalog `presetId`, or independent partial
updates to `mode`, `inputMode`, `inputTexture`,
`inputLayerId`, `draftGraph`, `draftCode`, `workingCode` and `draftCodeParameters`. Omitted fields retain their current values. `draftGraph` and `draftCodeParameters`
replace their complete objects/arrays; no implicit conversion or declaration parsing occurs.
`program` cannot be combined with the flat program fields; `presetId` supplies the whole
program and cannot be combined with program edits. Host Input fields remain separate.
Inspection and stored JSON nest the program fields under `program`, retaining both modes
and inactive selections. Discovery exposes `shaderModes`, `shaderInputModes`, `shaderDefaults`
and `fxProgramDefaults`. FX edits accept the same program shape; see [FX operations](AgentAPI.md#fx-edits-and-presets).

`inputTexture` accepts the document format's Texture2D `$asset` descriptor or `null` to clear it.
`inputLayerId` accepts a layer/group ID, `null` to clear, or an `@alias` from an earlier operation
in the same batch. The API resolves aliases to persistent IDs; stored documents use IDs only.
Omitting either field preserves its current selection, even when another `inputMode` is active.

For example, to read the lower composition without changing the current Graph/Code mode:

```json
{
  "op": "set", "layer": "@shader",
  "settings": { "shader": { "inputMode": "CompositeBelow" } }
}
```

To use a source created earlier in the batch instead, supply
`"inputMode":"Layer", "inputLayerId":"@source"` in the same `shader` object.

The API-only `compile` flag defaults to false and is omitted from snapshots and documents.
`compile:true` compiles the selected Graph or Code after assigning supplied fields;
it does not change `mode`. Code and workingCode accept up to 2 Mi characters each,
within the API's overall request limit. A compile failure reports `shader_compile_failed` and
uses the ordinary batch rollback. To retain an invalid draft, submit it without `compile:true`
in a separate successful request. A dry run checks compilation on the detached probe.

For example, this operation selects Code and explicitly applies a tint generator:

```json
{
  "op": "add", "type": "shader", "as": "codeTint",
  "settings": {
    "shader": {
      "mode": "Code",
      "draftCode": "// @param color _Tint = (0.15, 0.5, 1, 1)\nfloat4 ApplyFX(PinnedUV uv, float4 color) { return _Tint; }",
      "compile": true
    }
  }
}
```

Preserve the full `program` for exact roundtrip restoration, including `workingGraph` and
`workingCodeParameters` alongside the editable branches. A generated Code recipe without a
separate working-value snapshot can use `workingCode` plus matching `draftCodeParameters`. Direct
assignment is a stored-state operation and does not promise GPU validation; `compile:true`
instead applies `draftCode` and replaces the applied pair on success in Code mode. A program with
only `draftCode` in document JSON has no saved applied source. To author a Code document without
an API compile operation, provide the applied HLSL and matching parameter declarations/values
explicitly under `program`. The document behavior uses `"$type":"ShaderLayerBehaviour"`; `compile` is never
a stored behavior field. Missing texture asset identities are retained in either mode.

This complete `add` operation creates a Shader layer with one exposed Color parameter connected
to Output. Put it in the request's `operations` array:

```json
{
  "op": "add", "type": "shader", "as": "tint",
  "settings": {
    "name": "Tint", "shader": {
      "draftGraph": {
        "version": 1,
        "parameters": [
          { "id": "11111111111111111111111111111111", "name": "_Tint", "type": "Color", "colorValue": [0.15, 0.5, 1, 1] }
        ],
        "nodes": [
          { "id": "22222222222222222222222222222222", "kind": "Parameter", "position": [40, 80], "parameterId": "11111111111111111111111111111111" },
          { "id": "33333333333333333333333333333333", "kind": "Output", "position": [360, 80] }
        ],
        "edges": [
          { "outputNode": "22222222222222222222222222222222", "outputPort": "Out", "inputNode": "33333333333333333333333333333333", "inputPort": "RGBA" }
        ]
      }
    }
  }
}
```

For an existing layer, use `op:"set"`, its `layer` ID or alias, and the same
`settings.shader.draftGraph` object. The graph is replaced completely, not patched by array index;
preserve existing IDs and include every retained node, parameter, edge, Sub Graph definition,
function, source and annotation.
Missing texture `$asset` identities supplied by this operation survive native save/reopen;
an explicit null replacement clears the old identity for that parameter.
Inspection returns the graph at `settings.shader.draftGraph` alongside the independent Code state.
Discovery supplies `shaderDefaults`, `shaderModes`, `shaderNodes`, `shaderValueTypes`,
`shaderWrapModes` and `shaderFilterModes`.
Each node catalog entry includes `category`, exact `inputs` and the existing string-array
`outputs`. Nodes with fixed procedural, normal, color, vector or Boolean outputs additionally
expose `outputTypes`, an output-name-to-type map; dynamic numeric types remain inferred from inputs.
`ApplyTransform2D` reports `outputTypes: {"Local":"Float2","Input":"Float2"}`; its transform
input reports `type: "Transform2D"`, `valueField: "resource"`, `resourceType: "Transform2D"`.
`shaderValueTypes` includes `Transform2D` from the model enum. Parameter entries mark
`dynamicOutputs:true`: resolve `Out` through `parameterId` and the retained declaration,
including Enum/Seed → Float, Point → Float2, Normal → Float3 and Transform2D → Transform2D.
Reroute marks dynamic inputs/outputs and `outputTypeFromInput:"In"`; resource types pass
through unchanged. A dynamic numeric input still excludes Transform2D and other resources.
Nodes with shared mode metadata provide `modeLabel`, `defaultOption` and `modes` entries
containing `{ option, name }`. Shape and BilateralBlur mark `dynamicInputs:true`;
each mode contains its own `inputs` array, while top-level inputs describe the default mode.
Shape includes Rounded Polygon (`option: 7`). NormalUnpack exposes Tangent (`0`, default)
and Object (`1`) through the same mode metadata. Fixed `outputTypes` for the new nodes are
Float for FadeTransition, Color Out plus Float Mask for ReplaceColor, Color for Contrast, Float3 for
NormalReconstructZ/NormalUnpack, Float2 for RadialShear and Boolean for All/Any/IsNaN/IsInfinite.
Append and Switch mark dynamic inputs/outputs. Append exposes `maxAComponents:3` and
`maxOutputComponents:4`; infer its output from the concatenation rule. Switch exposes
`caseInputsField:"switchCases"`, `settings.floorPredicate` (default true) and
`settings.switchCases` with the effective default list, `nullable:true`, `maxItems:26` and
case field descriptions/comparison values. Its top-level inputs describe default A/B cases;
read each actual node's stored cases for its current input IDs. All/Any mark dynamic inputs
while retaining Boolean output. Discovery creates no Enum-derived Switch cases.
RotateAboutAxis always outputs Float3; its modes change rotation units only.
Mixed outputs use their actual per-port types: TileUV's UV/Cell/Center are Float2 and its
Random/Mask are Float; PerspectiveUV's UV is Float2 and Mask is Float. HeightBlend uses
Height/Mask as Float. Blackbody has Float4 Out. WT noise types, per-dimension ports,
fractal availability and implicit defaults are described in [noise discovery](#noise-settings-inputs-and-output-types).
Color/normal nodes also expose applicable node-level `settings`: each field maps to
`{ default, values: [{ value, name }] }`, where `value` is the serialized enum identifier and
`name` is its UI label. Normal inputs always use signed XYZ with neutral `(0,0,1)`;
NormalEncode/NormalDecode and NormalUnpack expose explicit packing conversions.
Height nodes mark `dynamicInputs:true`; `settings.heightSource.values[]` supplies the input layout
and `radiusUnits` (SourceTexels or DocumentPixels) for each source mode on HeightToNormal/HeightToDistortion.
ParallaxMapping/ParallaxOcclusionMapping instead expose `amplitudeUnits:"UV"`, fixed Float2 `Out`,
and per-source `sampledInputs` (`["Height"]` for Value, empty for Texture). Their channel/wrap/filter
settings carry `activeWhen: { heightSource: "Texture" }`; they have no derivative or Radius.
Sampled filters expose
`outputTypeFromInput:"In"`, except RGBShift has fixed Float4 output and
`amountUnits:"DocumentPixels"`. They expose `sampledInputs:["In"]`, except SlopeBlur adds `"Slope"` and
BilateralBlur's Custom mode adds `"Guide"`, a dynamic numeric/Color input with scalar-zero default.
BilateralBlur lists `sampledInputs` in each mode;
its top-level list describes Source mode. `radiusUnits:"DocumentPixels"` is present only for
filters with Radius. RadialBlur, SlopeBlur, Buffer, Fractal, RGBShift, Supersample and
MakeTileable have no Radius. Supersample uses the render-pixel footprint; MakeTileable uses
unit UV. Buffer/Fractal
also expose dynamic numeric/Color In and Out; see [their settings and storage](#buffer-and-fractal-nodes).
ColorCurves resource inputs carry `optional:true` and evaluate as identity when neither connected nor assigned an inline curve.
Scatter uses `kind: "Scatter"`, `option: 0/1/2` for Max/Add/Alpha Over and
`scatterPeriodic: false` by default. Its Source is dynamic numeric/Color; Max/Add preserve
that type, while Alpha Over returns Color. `kind: "ScatterInstance"` has no inputs and
fixed UV/Center Float2 and Index/Random Float outputs. See the [Scatter contract](#scatter-and-scatter-instance)
for all input defaults, center sampling, context and Buffer restrictions.
Scatter discovery lists all five repeated branches in `sampledInputs` and its four
center-sampled controls in `centerSampling`. Top-level `outputTypeFromInput:"Source"`
describes the default Max mode; use the selected `modes[]` output rule: Max/Add infer
from Source, while Alpha Over supplies `outputTypes:{"Out":"Color"}` instead.
`settings.scatterPeriodic` reports Boolean default false, and the Period input carries
`activeWhen:{"scatterPeriodic":true}` plus its current `active`/`inactiveReason` state.
Names, categories, mode labels, port sets, defaults and these fixed output types come from the shared
node model; descriptions explain units and evaluation. These discovery fields are not extra
fields to copy into a serialized node.

In stored `whimtex.document` JSON, place this same graph at `layers[].behaviour.program.draftGraph`, with
`behaviour.$type` set to `ShaderLayerBehaviour`; the API's `op`, `as` and `settings` wrapper is
not stored. Keep `graph.version:1`. Graph parameter `controls` are saved declarations, not regenerated
HLSL metadata. Texture parameters use the existing `textureSource`, `textureLayerId` and
`textureValue` asset-reference fields. Native TIFF can embed non-asset textures; JSON requires
persisted asset references. The [generated schema](AI/document.schema.json) describes those fields.

### Caustics nodes

Both nodes expose optional Float `Dispersion=1`, nonnegative with no upper limit, and two outputs:
`Value` (Float) and `Color` (Color/`col`, alpha always 1). Dispersion affects only Color;
Value retains its scalar calculation and range. At zero dispersion,
`Color = float4(Value, Value, Value, 1)` with the default balanced spectrum.

An omitted Spectrum uses CIE 1931 / D65. An explicit Spectrum is a Gradient input,
editable inline or connected through a parameter. The inline Custom ramp starts at
red and runs through orange, yellow-green, green-cyan, blue and violet. Six samples at
`t=0,0.2,0.4,0.6,0.8,1` color the six contributions. The gradient path clamps negative
RGB and ignores alpha. Its `causticsWhiteBalance` setting defaults to true and divides
each band by the sum of its channel weights (epsilon 1e-6). Channels absent from the
palette stay zero. With balance off, each band is divided by six, retaining overall
tint and HDR. Spectrum uses generic resource inputs or edges. HLSL and SVM use the
same weights.

The public WT Color API retains its original fixed spectrum.
At nonzero dispersion, Color's green channel is not the unchanged scalar Value.

A Value-only evaluation does not evaluate the Dispersion input branch or extra channel
computations. Only Caustics Noise shortcuts Color when dispersion evaluates to zero:
one noise trace supplies RGB, including when zero comes from a dynamic input.
Physical Caustics always performs six Color projections, including at zero dispersion;
it has no compile-time zero shortcut.
Inputs and edges use generic graph storage and discovery. The node setting
`causticsWhiteBalance` defaults to true. Omitted Dispersion inputs use 1, so existing
Value connections keep their result.

`CausticsNoise` (158, **Procedural/Noise**) is one built-in, fixed 2D procedural node.
The bottom **Algorithm** selector stores `option`: `0=OpenSimplex2S`,
`1=Simplex` (default). Both algorithms share the inputs below and the Value/Color outputs.

| Input | Type and default |
| --- | --- |
| `Position` | Float2, implicit current UV |
| `Scale` | Float2, `(8,8)` |
| `Offset` | Float2, `(0,0)`, active only with `noisePeriodic` |
| `Period` | Float2, `(1,1)`, active only with `noisePeriodic` |
| `Seed` | Float, `1337` |
| `Evolution` | Float, `0`; connect Time for animation |
| `Strength` | Float, `1` |
| `Dispersion` | Float, `1`, nonnegative with no upper limit; affects Color only |

Each graph sample includes the complete profile, ending in `smoothstep(0,1,...)`:

- **OpenSimplex2S:** evaluate the raw OpenSimplex2S caustics calculation at
  `Scale * 1.125` and `Strength * 2`, with Evolution unchanged, then return
  `smoothstep(0,1,0.5 * exp(oldValue * 3 - 1.5))`. Here `oldValue` is the former main
  node's raw `0.5 + 0.5 * signedResult`, also returned by the public `WTNoiseCaustics`
  call at those adjusted inputs with one level and Filtering off. Its Evolution loop
  remains 578 units.
- **Simplex:** evaluate the former temporary Simplex calculation at `Evolution * 2`,
  with Scale and Strength unchanged, then return `smoothstep(0,1,pow(oldValue,1/1.7))`.
  Here `oldValue` includes that calculation's exponential mapping. The supplied
  unnormalized gradients and fixed `-6` direction-field coefficient remain intact;
  this direction field is not the exact scalar derivative.

Thus either algorithm produces a finished 0–1 profile without requiring downstream
Exponential, Power or Smoothstep nodes. These mappings are graph calibration, not color-space
conversion. Stronger distortion uses short steps and path averaging; extreme Strength
can still fold the domain and costs more to evaluate.

For nonzero Color dispersion, six spectral samples use the same Strength, profile,
Seed, Scale, Offset, Period and Evolution. Their XY positions shift along a smooth,
bounded local direction evaluated independently from the same noise field at a fixed
offset. This direction follows the field's periodicity. Dispersion scales each band's
signed noise-space displacement; it does not change Strength between samples.
The displacement is `direction * (0.2 * Dispersion * spectralShift)`, shared by HLSL and SVM.
CIE/D65 uses the same signed response weights as Caustics. After accumulation,
colors outside `0..1` are desaturated toward equal-luminance gray until they fit
that range. A custom spectrum uses its gradient weights. With balance off,
HDR gradient values can exceed that range.
Both algorithms support this Color path with Periodic, Filtering and native HLSL/SVM
evaluation. Scalar Value keeps its original position and calculation.

`noisePeriodic` and `noiseFiltering` default to false. Offset shifts Position in input
units before Scale; disabled Offset/Period inputs retain their values and connections.
Both algorithms use Seed; nonpositive Period components disable the corresponding axis.
Simplex repeats corner hash identities without blending copied fields. Periodic rotates the
lattice orthogonally to admit integer spatial periods without rescaling it. Each enabled axis
fits `max(1, floor(abs(Scale) * Period + 0.5))` noise-space units into the requested period;
Scale=8 and Period=1 therefore retain density 8. The periodic pattern changes orientation;
nonperiodic Simplex is unchanged. Kernel distances, unnormalized gradients, the -6 direction
field and Evolution retain their definitions; the direction field rotates back to input space. The retained radius-0.6 simplex kernel has small discontinuities on some simplex boundaries, also present without Periodic; lattice repetition does not smooth these intrinsic transitions.
OpenSimplex2S repeats corner hashes on both halves of an axis-aligned BCC lattice, with
local distances and analytic derivatives, without translated-field blending. The periodic
pattern changes orientation; nonperiodic ImproveXY is unchanged. Its Scale*1.125 calibration
remains, and the 578-unit Evolution loop fits 501 temporal lattice units (internal phase
speed changes by less than 0.1%). The public raw WT call shares this periodic core.
Filtering uses unwrapped Position derivatives and sample footprints for
both algorithms, attenuating the profiled sample toward 0.5; it is approximate and nonlinear
warp aliasing can remain. Use Fractal for
graph octaves with ordinary SeedStep behavior.

There is no Dimensions selector, separate `UV` resampling port, `Depth`, `IOR`,
`LightDirection`, `Softness`, `Intensity`, Resolution, Wrap or generated buffer.
Native HLSL and SVM evaluate the same internal core directly; generated shaders specialize
for the selected algorithm. The graph calibration/profile has a separate internal entry.
The public `WTNoiseCaustics(float2, WTNoiseSettings[, WTNoiseCausticsSettings])`
signatures, raw OpenSimplex2S result and `WTNoiseCausticsDefaults()` remain unchanged:
`strength=0.625`, `evolution=0`, with generic WT fractals available. The graph selector
does not add a public WT algorithm option. The additional
`WTNoiseCausticsColor(float2 p, WTNoiseSettings n, WTNoiseCausticsSettings s, float dispersion)`
and defaults overload `WTNoiseCausticsColor(float2 p, WTNoiseSettings n, float dispersion)`
combine six spatially shifted samples of the public raw OpenSimplex2S result with
shared Strength and spectrum weights, without the graph profile. Their signatures
are unchanged. See the [HLSL contract](NoiseLibrary.md#caustics-noise).

The temporary `Caustics Noise Simplex (Test)` catalog entry is removed. Existing Custom
Functions retain their embedded source; no user documents or saved inputs are rewritten
or migrated. Its MIT attribution is retained in `src/Shaders/WTNoiseCaustics.hlsl`.

`Caustics` (159, **Filter**) accepts scalar `Height` from any graph branch, including a
texture sample or Fractal. It projects refracted light onto a flat receiver and accumulates
finite elliptical light footprints. It returns scalar `Value`, nonnegative and unclamped
above one, and `Color` with nonnegative HDR RGB and alpha 1. A flat height field produces
uniform illumination, not black.

Caustics inputs: `UV` (implicit current UV, samples the completed result), `Strength=0.02`
(height multiplier), `Depth=1` (receiver distance), `IOR=1.333` (air-to-medium index,
minimum 1), `LightDirection=(0,0,-1)` (direction of travel, normalized; zero uses downward),
`Softness=1` (receiver-texture pixels), `Intensity=0.25` and `Dispersion=1` (nonnegative with no upper limit, Color only). Light directed away from the surface contributes zero.
Depth, softness and intensity use nonnegative values. The flat
surface's lateral light shift is removed, so changing direction changes the pattern without
translating an otherwise flat source. This is a planar thin-surface approximation; no
self-shadowing, multiple refraction or scene geometry is evaluated.

Color always uses six spectral refraction/projection samples, including at zero dispersion.
IOR is anchored at 550 nm: `n(lambda) = n550 + B550 * ((550/lambda)^2 - 1)`, with
`d=max(Dispersion,0)` and `B550=(max(IOR,1)-1)*f(d)`. For `d<=1`,
`f(d)=d*(0.3+0.7*d*d)`; above one, `f(d)=1+2.4*(d-1)`. The linear extension
retains both value and slope at one; the existing 0–1 mapping is unchanged.
Each spectral refractive index remains at least one when tracing rays.
At IOR=1.333 and d=0.1 this corresponds to Cauchy B approximately 0.0031 square
micrometers; separation grows faster toward d=1 and linearly beyond it.
The sample/pass count stays at six; large dispersion can separate visible bands and
increase projection overdraw without increasing ray count.
With no connected or stored Spectrum, Caustics uses six CIE 1931 / D65 weights
preconverted to linear sRGB and normalized to neutral white. The signed weights
accumulate in the RGB receiver. When the completed receiver is sampled, colors
with a negative channel are desaturated at constant luminance into nonnegative HDR RGB.
The projection footprints still use the regular Softness, with no spectral stretching.
A stored or connected Spectrum uses six CPU samples of the readable gradient LUT
and respects White Balance. Both paths share the existing receiver and ray texture.
No GPU readback, temporary XYZ texture or extra resolve pass is needed.
The height buffer and temporary ray texture are shared by all bands. Value retains the original IOR. When both Value and Color are used,
they have distinct output buffers and share the same height pass. Requesting Color does
not upgrade or repurpose the Value buffer, keeping output demand and cached results separate.
At IOR=1 all spectral samples have no refraction, regardless of Dispersion.

Only Caustics stores `bufferResolution` / `bufferSize` / `wrap` fields. Defaults are
**Custom**, `[512,512]`, **Repeat**. Output sampling is bilinear. Canvas resolution follows
the target; the internal height/ray grid is bounded to 16..512 per axis independently of
the receiver size. Higher output resolution does not add surface detail above that grid.
Height is baked once; its neighboring texels supply slopes. The refraction pass and
projection reuse the shared graph buffer invalidation and resource ownership. Inputs,
Time, instance index, external textures and host transforms follow the Buffer cache rules.
Generated images are transient and never saved in TIFF/JSON or presets. Generated HLSL
requires these graph-managed passes; copying it alone into Code cannot reproduce them.

Repeat projects neighboring tile images and samples periodically; supply a smoothly
periodic height field for a seamless result. Other Wrap modes project one finite surface
and apply the chosen addressing when sampling the result. Large distortions remain
limited by the height grid and finite footprint approximation; very broad periodic
footprints spanning multiple tiles can lose energy. Softness is not a contrast or per-frame
normalization control. Caustics is a buffer boundary: Fractal after it requires literal
`SeedStep=0`, as it resamples the baked result. Fractal before it changes the actual surface.
