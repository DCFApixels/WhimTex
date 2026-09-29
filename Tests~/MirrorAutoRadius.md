# Mirror Automatic Radius — 2026-09-28

`mirrorAutoRadius` defaults true. The effective correction radius is max(0.005, clamped Blend Width / 4); manual mode uses the stored `mirrorCorrectionRadius`. The UI and renderer share the effective-radius property. No migration or changes to other algorithms.

Run through the connected Editor:

```sh
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/MirrorAutoRadiusSmoke.cs --entry MirrorAutoRadiusSmoke.Main --project-path D:/DCFA/Projects/Test6.6 --format json
```

147487 checks passed: automatic versus equivalent manual full rendering, cached preview and thumbnail; minimum/20%/40% widths; compensation on/off; API setter, Unity JSON and portable roundtrip for true/false. Only in-memory fixtures are created and disposed.

SeamlessUIPolishSmoke passed 619 checks, including auto default, displayed value following width, minimum, retained manual value, empty-edge disabling, hidden correction controls and alignment at 320/620px. Unity compilation completed with no errors. Numerical MakeSeamless, documentation source and regenerated schema checks passed.
