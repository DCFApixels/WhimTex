# Scalar-jitter reference

These 72 GPU snapshots were captured before axis-specific jitter changed the WT
sources identified in manifest.json. Each file is 16×16 linear RGBA, little-endian
float32 without a header. Suites 0/1/2 cover Cellular 1D/2D/3D; suite 3 covers
Triangle Voronoi 2D. Both point distributions, static/evolved positions,
periodicity, filtering, geometry outputs and sampled Fractal branches are included.

CellularAxisJitterTests verifies each file's SHA-256 before comparing the current
equal-component result. The 0.00015 tolerance permits GPU arithmetic differences.
These are output references, not source textures. Do not regenerate them with the
current implementation to obtain a passing test. New run reports and images belong
in project Temp/WhimTex/CellularAxisJitter, outside this fixture.
