# Brush dynamics

Do not trigger Unity compilation automatically. After the user compiles manually, run
`BrushDynamicsSmoke.cs` with the existing editor-side test runner if authorized.
It uses transient layers/textures, with no saves, imports or Undo changes.

The smoke covers whole-stroke opacity, per-stamp flow, erasing, stroke-buffer release,
texture color/alpha, automatic tint variation from RGB/alpha keys and white reset, seeded rotation, Multiply and the actual C# spacing accumulator.
The separate `BrushVertexTransportSmoke.cs` compares immediate GL attributes with explicit mesh
attributes on the current graphics backend. On the reported DX12 session, GL returned alpha 0,
whereas explicit vertices returned alpha 1 for the same shader and white texture.

Without Unity, run `node Tests~/BrushDynamicsMath.test.mjs` for reference arithmetic
and source-wiring checks. This is not a shader compilation or GPU test.

Run `node Tests~/BrushStampBlend.test.mjs` for scalar lanes of the shared HLSL blend
functions, repeated Multiply/Add, transparent backdrop, whole-stroke opacity without
feedback, Normal equivalence, region-copy bounds and per-stamp renderer wiring.
After manual compilation, `BrushDynamicsSmoke.cs` also checks repeated Multiply in both
application modes and buffer release. Manually check Per Stamp with all tip types, SDF,
Tint, Flow, selection masks, transformed/tiled canvases and symmetry (including overlap).
Check Apply Blend persists in presets and resets to Per Stroke with Color reset.
The per-stamp path retains base/working surfaces and uses one temporary feedback surface
per segment. Only stamp bounds are copied when RT region copies are supported; wrapping
can widen them to the full canvas. The fallback is a full blit. No per-stamp readback or
new Undo snapshots are introduced. Performance at dense spacing still needs GPU profiling.

Run `node Tests~/BrushTipPersistence.test.mjs` for source-translated persistence checks
with a mock asset database: deferred restoration, preserving unresolved references,
exact texture subassets, legacy GUID-only settings, and explicit clearing.
This does not exercise an actual Unity domain reload.

Run `node Tests~/BrushStrokePreview.test.mjs` for preview path and source/lifecycle checks.
Run `node Tests~/BrushSdf.test.mjs` for SDF LUT addressing, reference filtering and wiring.
After manual compilation, test normalized grayscale and alpha distance fields: move alpha
keys, add an interior transparent band, use colored/HDR keys, switch Blend/Fixed gradient
mode and color input mode. Check all tip channels, erasing, Opacity/Flow, tiny and large
stamps, rotated tips, tinted stamps, sample preview and preset save/load. Verify
SDF Alpha/Color do not multiply the distance alpha twice, luminance modes preserve the
separate opacity, and ordinary tips are unchanged with SDF off. Check both project color spaces.
White SDF gradient RGB must preserve the brush color; erasing uses gradient alpha only.
For the procedural brush test Mode Hardness/SDF Gradient, including scatter, size jitter,
Tint, opacity/flow, erasing and the wave preview. The gradient reads 0 at the circle's edge
and 1 at its center, never drawing outside the circle. Hardness and gradient values must
survive switching modes and assigning/removing a tip texture. Presets preserve the mode.
Hardness must be hidden in gradient mode; Threshold is removed.
The static LUT is bounded to one texture, reused for identical gradients and released
before domain reload/Editor quit; changing keys, gradient mode or HDR input mode rebakes it.
Earlier experimental SDF presets without a gradient use the new default alpha ramp.
After manual compilation, open Brushes and check the wavy sample with a round and textured
tip, opacity/flow, spacing, both randomization algorithms, scatter, jitter, direction rotation,
tint, blend and Eraser. Switch HDR inputs, resize, close/reopen the drawer and window.
The sample remains below the scrolling controls, and must not change document pixels,
dirty state or Undo. Verify inactive drawers do not render the sample repeatedly.

The math checks execute translated Sobol recurrence/sampling bodies against published
Joe–Kuo reference points, dyadic-bin coverage in all seven dimensions, seed shifts,
unsigned index limits and fixed per-control dimension wiring. Random remains the default.
Scatter Bias checks cover the unchanged default radius, monotonic center/edge concentration,
bounded radii, expected mean radius and finite outputs with Sobol samples.

## Interactive checks

Use each section's ↺ button after changing its fields: Tip clears its texture and restores
Alpha/80% hardness; Stamps preserves spacing and restores Random and zero scatter/bias/jitter;
Stamps also restores Fixed rotation, zero Angle Offset and zero Flip X/Y probabilities.

`node Tests~/BrushFlip.test.mjs` checks probability boundaries, local-axis reflection
with rotation/non-square tips, and UI/API/mesh/shader wiring. The math suite also
checks Sobol flip reference points and the joint distribution of both flip axes.
It does not compile Unity or execute a GPU shader. After manual compilation, use an
asymmetric texture to compare Flip X/Y at 0, 0.5 and 1 with both Random and Sobol,
rotation, SDF, erasing, tiled canvas, the wave preview and preset save/load.
Color restores white Tint and Normal. Other sections, palette colors, size, Opacity and Flow
must remain unchanged. Confirm fields and dependent enabled states update immediately,
including a focused numeric input; each header and button share a subtle dark strip.

1. Select Brush: its drawer arrow appears above Post FX. Each drawer closes the other;
   closing the Post FX drawer leaves processing enabled. Select Pencil: Brushes disappears.
2. Change Size, Opacity and Flow in the header. Open Brushes and vary spacing, hardness,
   scatter, size jitter, angle jitter and Tint gradient. Check identical keys, differing RGB keys,
   alpha-only variation, and the small white-reset button. Settings remain shared across Drawing layers.
   Switch Randomization between Random and Sobol: scatter, size, rotation and tint all follow it.
   Sobol should give a more even spread across a long stroke; symmetry copies remain identical.
   With Scatter enabled, try Scatter Bias −100, 0 and 100: centers concentrate near the stroke,
   spread uniformly over the disk, then concentrate near its outer edge. Test both algorithms.
3. Draw a slow and fast straight stroke with 100% spacing. Stamp separation should agree.
4. Set Opacity 40%, Flow 10%; overlap repeatedly without releasing, then release and repaint.
   The first stroke stays below 40%; the second adds another coat. Repeat with RMB erasing.
5. Use a transparent, non-square texture tip; verify aspect ratio and each Tip Channel.
   Try Angle Jitter 0°, 45° and 180° with an asymmetric tip, including at canvas edges.
   Switch Rotation to Stroke Direction: test horizontal, vertical and diagonal strokes,
   turns, stationary clicks, non-square canvases and transformed layers. Jitter adds to
   movement rather than replacing it. A new stroke must not reuse the previous stroke's angle.
   With Jitter at zero, test Angle Offset −90°, 0° and 90° in both rotation modes, then
   add jitter. Offset alone must rotate a texture tip and preserve its edge coverage,
   including tiled painting. Check the same result in the sample preview and API strokes.
   Clear Texture to restore hardness. Try a Multiply stroke on existing color.
6. Repeat with an area selection, a transformed Drawing layer, tiled preview, and symmetry.
   Test stamps touching the canvas edge from outside. Default settings must still feel responsive.
7. Undo a stroke, not its tool-settings changes. Close/reopen the window: tip and settings remain.
   Assign a texture tip, manually recompile, and confirm it returns without reassignment.
   Repeat with a texture subasset and after moving the source asset. Changing another brush
   setting while the tip is unavailable must not permanently clear its saved reference.
   Explicitly clear Texture or use Reset Tip, then recompile: it must stay cleared.
8. API: set advanced fields, inspect, paint twice with the same seed into fresh equal layers,
   and compare results. Check dryRun and rejected fields/ranges; verify save/reopen retains settings.
