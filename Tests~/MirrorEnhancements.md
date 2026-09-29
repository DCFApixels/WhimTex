# Optional Mirror processing

Run `MirrorEnhancementsSmoke.Main`, `.Transition`, `.Integration` and optionally `.Preview` through the
connected Editor's `run_script`. Main compares zero-strength histogram blending against an
independent CPU Mirror implementation for all nine direction combinations; checks reflected
RGBA/HDR output, alpha bounds, exterior preservation, premultiplied strength interpolation,
tiny/odd/large sizes and default values of older JSON.

Transition checks -100/-25/0/75/95% against an independent CPU reference, all reflection directions,
falloff extremes, tiny/odd sizes, compensation, API bounds and serialization. Integration checks those
five transition values, all four option combinations and all three independent Poisson directions through layer/thumbnail/export/cached renders,
groups, clipping, bypass, portable serialization, actual UI callbacks, Undo/Redo and caller render
state. Only temporary in-memory documents/windows are used. Preview uses the optional existing
offline 600x600 noise fixture and writes four PNGs outside Assets to
`output/mirror-enhancements-2026-09-28`: 0 original Mirror, 1 compensation, 2 correction, 3 both.

Compensation reuses histogram tables but samples reflection-paired locations and measures covariance
against reflected rather than half-period-shifted samples. Zero Transition Start preserves the original
Mirror weights. Other values use q=saturate((1-distance/width)/(1-start)) before the existing cubic fade
and falloff, in both shader paths. Negative starts may expose a seam. Mirror and Offset values are independent.
Global correction uses mirrorPoissonEdges (AllEdges/TopAndBottom/LeftAndRight, default AllEdges)
independently of reflection directions. It still runs with both reflection axes Off and can change
pixels outside the reflection bands. It aligns the immediate tile seam,
not all image features. Reflection remains, histogram preservation is approximate, and clipping
is possible. Off/zero compensation keeps the original cheap render path. No migration or baking.
