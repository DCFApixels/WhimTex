---
title: "Shader source templates"
parent: "Technical reference"
nav_order: 18
lang: en
permalink: /reference/shader-source-templates/
search_exclude: true
---

# Shader source templates

`ShaderSourceTemplate` is an internal source-selection utility in
`DCFApixels.WhimTex`. It reads annotations in ordinary HLSL line comments,
selects named source blocks and feature branches, and substitutes exact
identifier tokens. The result is HLSL text for the existing shader source
builder. It does not compile shaders, resolve includes, evaluate the HLSL
preprocessor, infer function dependencies, or optimize expressions.

Annotations have no effect when the original file is included as ordinary
HLSL. Keep that original file valid independently: use ordinary HLSL guards
where necessary, and ensure unselected alternatives can coexist there.
This utility does not change the public WT API.

## Internal API

```csharp
internal sealed class ShaderSourceTemplate
{
    internal static ShaderSourceTemplate Load(string path);
    internal static ShaderSourceTemplate Parse(string source, string path);
    internal string Generate(IEnumerable<string> blocks,
        IEnumerable<string> features,
        IReadOnlyDictionary<string, string> replacements);
}
```

`Load` accepts filesystem paths and Unity `Assets/` or `Packages/` paths.
Virtual package paths resolve through Unity's package registry, including cached
packages; other relative paths resolve against the process working directory.
Callers with a separately resolved source can use `Parse` and supply the
original HLSL path for diagnostics. `Parse` performs no I/O and bypasses caching.
The path must be nonempty and contain no quotes, NULs, or line breaks;
backslashes are normalized to forward slashes.

`Generate` treats null block/feature enumerations as empty selections and
null replacements as no substitutions. An empty block selection returns
empty text after validating the requested features and replacements.
Repeated selections are harmless. Unknown requested names and invalid
replacement arguments throw `ArgumentException`, including `path:1` when
there is no corresponding declaration. Parse errors throw `FormatException`
with `path:line` and a one-based physical source line. I/O errors propagate.
There is no fallback to the unfiltered source.

## Annotation grammar

Place each directive on its own `//` comment line. Leading indentation and
whitespace after `//` are allowed. Directive names are case-sensitive and
must begin with `@wt.` immediately after that whitespace. Trailing prose is
not allowed. Ordinary comments, inline comments following HLSL, strings,
and the contents of multiline comments are not annotations.

| Directive | Meaning |
| --- | --- |
| `// @wt.feature(Name)` | Declare a feature outside all blocks. |
| `// @wt.feature(Name; Dep1, Dep2)` | Declare a feature with feature dependencies. |
| `// @wt.block(Name)` | Begin a top-level block. |
| `// @wt.block(Name; Dep1, Dep2)` | Begin a block with block dependencies. |
| `// @wt.end` | Close the current block. |
| `// @wt.if(Feature)` | Begin a conditional region inside a block. |
| `// @wt.if(!Feature)` | Select a region when the feature is absent. |
| `// @wt.else` | Select the opposite branch of the innermost condition. |
| `// @wt.endif` | Close the innermost condition. |

Names use `[A-Za-z_][A-Za-z0-9_]*`. Blocks and features have separate
namespaces. A semicolon is optional when there are no dependencies;
`Name;` is also accepted. Dependencies require the semicolon separator.
Whitespace around names and separators is ignored. Conditions accept one
feature name, optionally preceded by `!`; boolean expressions are rejected.

Blocks cannot nest. Conditions can nest, but must close before the enclosing
block ends. Feature declarations may appear before or after their uses,
outside blocks. Block dependencies may also refer forward. Annotations
must not split backslash-continued HLSL lines or themselves end in a
continuation backslash. Text inside a continued line comment is ordinary
comment content.

Parsing validates the whole file, including unselected blocks and inactive
branches: duplicate declarations or dependency entries, unknown dependency
or feature names, dependency cycles, malformed arguments, unknown `@wt.`
directives, misplaced directives, repeated `else`, and unmatched endings
are errors. Feature and block dependency cycles are checked separately.
HLSL syntax and the semantic validity of each specialization remain the
compiler's responsibility.

## Selection and ordering

Selecting a feature enables its complete transitive dependency closure.
Conditions evaluate against that closure, including negated conditions.
Selecting a block includes its complete block dependency closure. Each
selected block is emitted exactly once, independent of selection order.

Blocks are ordered by a stable topological sort: dependencies precede their
users, and the earliest source declaration wins among currently ready
blocks. Put dependencies before their users in the original file when exact
source order is desired. Content within each retained branch keeps its
original order. All annotation lines and all source outside blocks are
excluded; necessary shared declarations and includes must belong to a
selected block or be supplied by the caller.

## Identifier substitutions and source locations

Replacement keys are case-sensitive identifiers. A replacement applies to
an entire identifier token, including declarations and calls, never to a
substring of another identifier. Strings, character literals, line comments,
multiline comments, numeric literals, and quoted/angle-bracket include paths
are preserved. Substitution is one
pass: replacing `Helper` with `Helper2` does not then apply a `Helper2` rule.
Identifier tokens in ordinary preprocessing directives are also eligible.
This is a lexical operation, without symbol resolution or macro expansion.

Values are non-null single-line text with no NUL; empty values are allowed.
Callers are responsible for valid HLSL values and collision-free helper
names. Typical values are an integer literal for a dimensions placeholder
or a distinct identifier for a specialized helper. Values are inserted
verbatim, without parsing or rescanning; avoid introducing comments, quotes,
continuations, or token fragments that would change subsequent tokenization.

Generated output uses LF newlines and ends each emitted line with a newline.
It starts each retained span with `#line N "original/path.hlsl"`. Consecutive
source lines share one mapping; skipped metadata/branches and block boundaries
start new mappings. No directive is inserted into a retained multiline
comment, string, or continued line. Substitutions cannot add physical lines.
The caller should set its own `#line` mapping before appending unrelated
generated code. Includes and any authored preprocessing directives retain
their normal downstream behavior in `ShaderSourceBuilder`.

## Example

```hlsl
// @wt.feature(Derivatives; Value)
// @wt.feature(Value)

// @wt.block(Common)
float WTTemplateScale(float x) { return x * 0.5; }
// @wt.end

// @wt.block(Kernel; Common)
float WTTemplateKernel(float x)
{
    float result = WTTemplateScale(x) / WTTemplateDimensions;
// @wt.if(Derivatives)
    result += 1.0;
// @wt.if(!Value)
    result += 2.0;
// @wt.endif
// @wt.else
    result += 3.0;
// @wt.endif
    return result;
}
// @wt.end
```

```csharp
var template = ShaderSourceTemplate.Parse(source, "Templates/Kernel.hlsl");
string hlsl = template.Generate(
    new[] { "Kernel" },
    new[] { "Derivatives" },
    new Dictionary<string, string>
    {
        ["WTTemplateDimensions"] = "3",
        ["WTTemplateKernel"] = "WTTemplateKernel3D",
        ["WTTemplateScale"] = "WTTemplateScale3D"
    });
```

This emits `Common` once, followed by `Kernel`. `Derivatives` enables `Value`,
so the `1.0` addition survives; the `2.0` and `3.0` additions are omitted.
Both helper declarations and calls are renamed. For ordinary inclusion,
provide the dimensions placeholder through the surrounding HLSL contract.

## Gabor integration

`WTNoiseGabor.hlsl` is the shared algorithm source for ordinary WT calls and
specialized graph helpers. `WTNoiseGaborTypes.hlsl` holds the unchanged settings,
result type, constants and defaults. The graph requests blocks after discovering
the reachable outputs, selecting dimension, filtering and result features. Uniform
input values remain uniforms; changing them does not create another specialization.

Helpers with the same feature selection share one definition in a compiled graph.
Outputs of one node share its primary sample within the same evaluation context;
neighboring-UV branches keep independent samples. A 3D curl keeps its three original
seeds. Phasor retains both analytic derivatives needed for its footprint and
confidence even when Gradient is not an output. Fractal stays in the existing
graph Fractal node; ordinary library calls retain their fractal loop.

The template cache follows source import revisions. Generated helpers carry original
HLSL line mappings; bindings reset the mapping before unrelated generated code.
Manual code and public WT functions still use the ordinary library includes.

## Basis and Cellular integration

`WTNoiseBasis.hlsl` and `WTNoiseDerivatives.hlsl` select the graph's algorithm,
dimension, filtering setting and reachable Value/Gradient/Curl outputs. Grid
and simplex derivative blocks are selected independently. Scalar sampling calls
the original kernel directly without the generic state dispatch. The 1D grid
algorithms retain their dedicated interpolation; 1D simplex retains its existing
2D domain slice and derivative projection. Curl keeps the original independent seeds.

Value, Gradient and Curl of one basis node share its primary value/derivative
sample inside one evaluation body. A Value-only body retains the scalar kernel;
reachable derivative outputs select the combined kernel after traversal. In 2D,
Curl reuses the primary gradient; in 3D, it adds only the two independently seeded
gradients. All three fields share one prepared domain, period/origin context and
filter visibility. Each seed retains its own filtered accumulation and clamp.
The Curl profile is selected after traversal, so requesting Value or Gradient
first does not trigger a second preparation when Curl is also used. Separate UV
sampling functions and For bodies keep independent results and output requirements.
Helper definitions still share identical profiles. Flow uses this same path with
one phase rotation saved and restored around the combined sample. Phase remains
a dynamic port value. Value-only, 2D Curl and public WT calls keep their existing
sampling behavior; the shared 3D preparation applies to generated basis/Flow nodes.

Perlin/Value derivatives traverse one X-fastest lattice loop instead of nested axis
loops, preserving sample and accumulation order. Their generated 3D Curl profiles
share one loop body across three seeds. ValueCubic retains nested lattice rows and
direct seed calls to preserve GPU execution speed; simplex also retains direct calls.
Outer Fractal loops remain dynamic, with unchanged octave counts, filtering and
normalization. These changes reduce native compiler work without approximating the
noise or changing its public API.

The 3D gradient helper maps the exact 64-entry direction sequence to a 16-entry
vector table: twelve repeating directions and four exceptional entries. This
replaces three scalar lookups without changing the hash mapping. The complete
table is still available to kernels that use it directly. Ordinary includes and
selected graph kernels use the same helper, including phase rotation.

`WTNoiseBasisKernels.hlsl` remains the complete kernel/table include for ordinary
library calls. Its source file, `ThirdParty/FastNoiseLite.hlsl`, also carries
blocks for individual kernels, tables and their dependencies. Generated built-in
code requests roots by known symbol name; template selection emits their
transitive dependencies once, including the original license and source mappings.
The arithmetic and tables have one source shared by both paths. Grid derivative
templates select only the required dimension's helpers.

`WTNoiseFlowTypes.hlsl` contains Flow settings and phase rotation without pulling
in the full basis library. Flow graph helpers therefore use selected kernels too.
If a graph needs a generic family include,
kernel selection keeps the complete include to avoid mixing partial and full
declarations. Unknown symbols, custom expressions, authored preprocessing and
unsupported includes preserve the existing full-library fallback. This is a
selection mechanism for known generated code, not a general HLSL dependency parser.

`WTNoiseCellular.hlsl` selects feature distribution, dimension, metric, scalar
output and filtering. `WTNoiseCellularTypes.hlsl` contains the unchanged shared
types, constants and defaults. Requests using only F1, F2, CellValue or ToPoint
omit scalar smoothing and filter-mean calculations. Those geometry outputs still
describe the raw first octave, even when Value uses smoothing or filtering.
DistanceToEdge still forces the Euclidean metric. CellValue with Smoothness keeps
the existing weighted cell-value interpolation. The graph has one Cellular node;
these are its existing options, not additional node types.

Basis and Cellular graph calls select a single octave without an inner fractal loop. Ordinary public
WT functions retain their existing overloads and fractal behavior. Search bounds,
site tie ordering, evolution, hash tables, arithmetic and wide-coordinate domain
preparation are shared. Uniform-distribution graph profiles do not require the
fixed-radius random-vector tables. Triangle Voronoi uses the same selected site kernels.

Only node options and reachable outputs select templates. Scale, Period, Seed,
Jitter, Evolution, Smoothness and all other port values remain uniforms or linked
expressions. Zero smoothness/evolution at the current edit is not a specialization
condition. Helpers share definitions by profile; evaluation results remain scoped
to each sampling context. `NoiseTemplateSource()` emits the requested noise and
Fractal helpers after all reachable outputs have registered requests.
The source-selection allowlist is an include optimization only; user HLSL still
passes through the shared entry-point, coordinate and texture-access validation.

## Remaining noise families and Fractal

Built-in noise nodes select annotated WT source. Caustics Noise uses its own
OpenSimplex2S ImproveXY derivative kernel, with extra translated-kernel blending
only for periodic evaluation; it no longer requires a Flow height pass. Shard,
Spots, Triangle Voronoi and White/Blue grain keep their existing algorithms,
domains and outputs. Settings/default declarations live in small family headers;
the generated shader receives the requested profiles and reachable outputs.
Numeric port values remain uniforms. Shard retains its original nested traversal
and octave preparation: removing their constant branches increased GPU time in
the measured 2D profile. Its shared kernel is still selected from WT blocks; the
graph no longer needs the complete family include.
Triangle Voronoi retains its original octave preparation and eager first-octave
evaluation from the public sample overload. Removing the one-iteration loop or
introducing a visibility branch for Value-only requests slowed rendering.

The three Warp families select their WT accumulation block and the corresponding
kernel. None retains a single octave; Progressive and Independent retain their
existing dynamic loops, split-precision frequency and saved period/origin context.
Selected kernel dependencies provide the original hashes and direction tables.

Fractal selects its loop from `WTNoiseFractal.hlsl` and its fold/accumulation
helpers from `WTNoiseFractalValues.hlsl`. The latter also supplies the public WT
overloads, instantiated for float through float4. Generated helpers include only
the selected FBm, Ridged, PingPong or cascade formula and value width. Graph
bindings provide the source call and uniforms; the HLSL owns the arithmetic.
Nested sampling and For contexts retain their own seed and footprint restoration.
Loops stay rolled, octave limits and normalization are unchanged, and Buffer's
Seed Step restriction remains in force. Public `WT_DEFINE_FRACTAL` macros and
ordinary WT includes keep the same signatures and behavior.

## Cache and validation checks

`Load` keeps an LRU cache of at most 16 parsed templates and at most
4,194,304 original source characters in total. This is a source-size budget,
not an exact managed-memory measurement. Oversized templates are parsed but
not cached. Keys include the absolute filesystem path and the normalized
display path. Cache access is synchronized. When
`ShaderProgramSourceRevision.Current` changes, the next `Load` discards all
cached entries. Failed loads/parses are never cached. Existing template
references remain immutable snapshots; call `Load` again after a revision
change to obtain current source. File timestamps are not polled. There is
no generated-output cache.

Small validation inputs for a caller-owned check:

```csharp
// Unknown condition in an otherwise unused block: error at check.hlsl:2.
ShaderSourceTemplate.Parse(
    "// @wt.block(Unused)\n// @wt.if(Missing)\n// @wt.endif\n// @wt.end\n",
    "check.hlsl");

// Feature cycle: error even when no features or blocks are requested.
ShaderSourceTemplate.Parse(
    "// @wt.feature(A; B)\n// @wt.feature(B; A)\n", "cycle.hlsl");

// Token boundaries: only the standalone Size is substituted.
var tokens = ShaderSourceTemplate.Parse(
    "// @wt.block(B)\nfloat Size2 = Size; // Size\n\"Size\"; 1e3; 0.5f;\n// @wt.end\n",
    "tokens.hlsl");
string result = tokens.Generate(new[] { "B" }, null,
    new Dictionary<string, string> { ["Size"] = "3", ["e3"] = "bad", ["f"] = "bad" });
```

The last input is a lexical check, not a complete shader. Its output starts
with `#line 2 "tokens.hlsl"` and contains `float Size2 = 3; // Size`, followed
by the unchanged string and numeric literals, with no second mapping between
the consecutive source lines. Further integration checks should cover
forward/shared block dependencies, nested alternatives, unknown directives
in unused branches, missing endings, comments spanning physical lines,
and cache invalidation after a source revision.
