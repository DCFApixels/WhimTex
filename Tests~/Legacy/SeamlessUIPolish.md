# Make Seamless UI polish — 2026-09-28

No processing formulas, defaults or stored fields changed. No migration was added.

- Main-pass squares have Copy Edges, Mirror Direction and Patch Edges labels, distinct from Poisson Edges.
- Quilting orders donor search before Feather/compensation and independent Poisson correction.
- Compact separators and contextual Strength (%) / Radius (%) labels replace repeated long field labels.
- Empty selections disable only dependent fields. Selectors and the other pass stay interactive.
- Feather 0 disables compensation without resetting the saved toggle or strength.
- Help text is shorter; full-resolution preview warning remains visible for Quilting.

## Verification

Use the connected Editor, explicitly targeted to the project:

```sh
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/SeamlessUIPolishSmoke.cs --entry SeamlessUIPolishSmoke.Start --project-path D:/DCFA/Projects/Test6.6 --format json
unity command run_script --file Packages/com.dcfapixels.whimtex/Tests~/SeamlessUIPolishSmoke.cs --entry SeamlessUIPolishSmoke.Result --project-path D:/DCFA/Projects/Test6.6 --format json
```

Start schedules layout checks; Result must say Passed, not Running. The test creates only an in-memory document and temporary EditorWindow, then closes it, restores focus and reverts its Undo group. No saved user document is changed.

Results: Unity compilation completed with no errors; 281 UI checks passed, including labels, search order, dependent controls, selector reactivation, preserved values, Undo/Redo and horizontal field bounds in all four modes at 320 and 620 pixels. EmptyEdges passed 43211 checks; the numerical MakeSeamless suite passed 485916 checks plus UI/source contracts. Bounds checks are not a screenshot-based visual review.

Seed layout retained 110/410-pixel inputs at 320/620-pixel window widths, with a fixed 64-pixel Random button and no width change for long values. Defaults passed 64 checks. Documentation source checks passed for all 79 pages.

## Alignment follow-up

Mirror and Offset Blend share one correction HelpBox, shown only when the current method's Poisson Correction is enabled. The existing Quilting warning remains unchanged. The UI test checks all 16 mode/toggle combinations and the original Quilting text.

The original bounds checks did not detect inconsistent label indents. A coordinate assertion reproduced the bug before the fix: Strength text began at x=11 versus Method at x=4. Removed the detail indent, configured the Channels label after adding it, and matched edge-heading text in USS. Expanded the fixture to build the real Input/Target view, enable all compensation controls, and compare label text origins, input origins and vertical centers at both widths. All 597 checks passed after the fix; Unity compilation completed without errors and the 485916-check numerical suite also passed. No screenshot-based visual inspection was performed.
