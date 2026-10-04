# Brush header layout

Run `BrushHeaderLayoutSmoke.cs` through the connected Editor's `run_script` command.

- `Inspect` / `Verify`: read the visible Brush header's field and label geometry without editing tool settings or documents.
- `SetupGradient`, then `Verify`: open a temporary plain EditorWindow with the production header/gradient styles and compare the gradient swatch with native fields. No document is created.
- `Capture`, then `CaptureResult`: capture the row on repaint through a temporary ImmediateModeElement. The probe removes itself. Output: `Temp/WhimTex/brush-header-layout.png`.
- Always call `Cleanup` after `SetupGradient`; it closes only the named test window.

Verified in Unity 6000.7.0a6: the live Hardness header's Size, Hardness, Opacity, Flow and Pressure labels/inputs share the same vertical bounds; the temporary Gradient header's labels, swatch and native inputs are all 22 points high with matching centers. The rendered gradient header was inspected. Editor compilation passed. Narrow wrapped rows and other display scales have not been visually validated.
