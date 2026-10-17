# Full-assembly brush reference

These shaders were captured on 2026-10-17 immediately before dirty-region optimization,
after the unified immutable paintSurface plus stroke-buffer path was implemented.
`provenance.json` authenticates LF-normalized contents; do not regenerate these files
from the optimized implementation. `Reference.cs` compiles transient shaders through
the public ShaderUtil API, with no assets or Unity-internal reflection.

`brush-buffered-stroke-v1` compares complete small images against this reference,
including transformed and wrapped selections, off-center stamps and a non-square
2053 × 1157 source crossing tile seams and partial edge tiles. A separate patterned
pixel check forces the portable partial-tile shader-copy path even on GPUs supporting
CopyTexture. All test fixtures are detached.

The opt-in `brush-region-benchmark-smoke` and `brush-region-benchmark` profiles compare
this full-image assembly and full-size commit scratch with current bounded assembly.
Both paths share the current stamp renderer, including compact stamp-backdrop copies.
They isolate assembly changes; they do not reconstruct historical stamp-scratch costs.
The older conditional-buffer comparison is documented in
[BrushBaseline](../BrushBaseline/README.md).

For bounded verification, start with `brush-isolated-smoke`, then select individual
`brush-isolated-*` cases. `RunIsolated` executes only the chosen path in one synchronous
Editor call: two warmup strokes, nine timed strokes and one phase diagnostic. It uses
no asynchronous carrier or inter-frame delays; disposal and render-state restoration
finish before the result returns. Path 1 is full assembly, path 2 is current assembly.
Paired profiles alternate both paths and can retain more allocations in the driver pool.

Every stroke includes 16 preview updates and a completion read from a dedicated 1 × 1
render target. No 8K/full-source ReadPixels is used. Timings are main-thread wall latency
with GPU completion, not isolated GPU timestamps, UI frame pacing or pointer latency.
Memory fields describe owned texture estimates and logical scratch lifetimes; driver
allocation totals do not establish physical VRAM residency or eviction.

Earlier paced diagnostics were retired after Editor responsiveness/cleanup timeouts.
Their recovered ownership does not turn partial runs into passes or establish the cause
of the delay. Keep their raw reports separately from synchronous captures. Review the
current catalog and sources before every run; generated reports belong only in project
`Temp/WhimTex`.
