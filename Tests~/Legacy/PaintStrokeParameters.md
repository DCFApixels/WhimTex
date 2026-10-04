# Shared stroke parameters

`PaintStrokeParameters` is a small immutable value passed explicitly to the
existing Drawing renderer. It contains color, diameter, hardness, pixel spacing
and erase mode. It does not own UI preferences, Undo, textures or pattern state.

The window creates parameters from shared tool preferences and the current color
mask. The API creates them from the layer's saved brush values after applying any
partial command. Retaining those saved values preserves existing commands and
documents; the API never depends on the user's interactive preferences.
Repeat, clipping, transforms, pointer handling and stroke lifetime are unchanged.

After manual compilation, run `PaintStrokeParametersSmoke.cs` through `eval_file`.
It checks parameter parity, value snapshots, color overrides, limits, legacy
spacing migration and partial API settings. It also compares the interactive
parameter path with API painting for hard/soft brushes, erasing and transparent
color, allowing one byte of channel error. It creates no saved assets or windows
and does not change preferences. GPU checks temporarily use Undo: do not edit
while the test runs.

Also rerun `PaintToolSettingsSmoke.cs` and `ScopedUndoSmoke.cs`. Manually check
RMB erasing, RGBA masks, Shift lines and Repeat Clip in the preview.
