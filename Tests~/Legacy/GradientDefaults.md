# Gradient defaults

Run `GradientDefaultsSmoke.cs` with entry `GradientDefaultsSmoke.Main` through the connected Unity Editor.
Uses temporary managed values only, without modifying documents or opening windows. Reflection inspects WhimTex types and resolves the JSON type from the API signature to avoid duplicate JSON assemblies in the script runner; no Unity internals are accessed.

Checks Perceptual defaults for construction, shared ramps, null fallback, Gradient layers, brush tip/tint, FX metadata and clipboard/API input without a mode. Verifies explicit Linear defaults for SDF and Pattern, plus every explicit mode through cloning, serialization and portable clipboard compaction/roundtrip. Exercises the shared API SetBrush parser used by settings/strokes and compares both gradient fields with brush clipboard import: bare arrays, objects without a mode, every explicit mode, nondefault smoothness and preservation on omitted fields.

Before the brush default was changed to Perceptual, the added parser regression reproduced the API/clipboard mismatch (Linear expected, Perceptual returned). The regression now expects Perceptual consistently for both brush gradients; explicit Linear remains covered.

Connected Editor result: all 60 checks passed with the final Perceptual brush defaults; Editor recompilation completed without errors. Documentation, command inventory, Make Seamless contract and layer/brush clipboard schema checks also passed.
