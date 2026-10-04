# Color picker validation

Scope: these checks cover real HDR color data and its SDR preview, not physical HDR monitor output. HDR display hardware/output has not been validated; Preview EV and intensity gradients do not enable that output path.

Run through the connected Unity Editor, with no active user color picker. These tests use transient
documents/windows and do not save assets. Reflection in the smoke scripts inspects WhimTex types only.

`HistoryHdrFilterSmoke.cs`, entry `HistoryHdrFilterSmoke.Main`: 19 checks passed for Standard/HDR filtering in the picker and gradient editor, negative/above-one RGB, alpha-independent boundary colors, selected-key changes, immediate toggle refresh, stable snapshots, retained document indices, selection/promotion/deletion, hidden-entry guards and non-destructive all-HDR empty state. Temporary windows/documents only; close user picker/gradient windows before running.

`ColorPickerPreviewSmoke.cs`: `Main` verifies display-only EV, the gradient editor's range/linear-light formula, unchanged numeric/HDR/history values, channel adaptation and alpha-only invariance, display-surface wiring, UI rebuilding and reset on a new window. `Setup`, `Layout`, then `Cleanup` check a temporary minimum-size HDR picker with History; `ColorPickerRingSmoke.Capture` / `CaptureResult` capture that window. Always clean up. Numeric checks and 240×520 layout passed; the rendered footer was inspected.

`ColorPickerModeSmoke.cs`, entry `ColorPickerModeSmoke.Main`: all three numeric modes persist across close/cancel/reopen and UI rebuild, correct labels, missing/invalid preference fallback, unchanged HDR/RGBA and no color-change callbacks. Restores the user's original preference in `finally`. Passed through the connected Editor.

`ColorPickerSmoke.cs` entry points:

- `Main`: range policies, non-mutating HDR switching, cancel, hidden alpha, HEX, exposure,
  unique document-local history, reorder/delete, invalid values, stale callbacks and tagged model serialization.
- `Fields`: pointer opening, raw HDR versus bounded field display, cancel, document switching and history ownership.
- `BrushSampling`: Alt sample routing to a matching primary-brush source context, RGB/HSV/Hex/marker updates, callback count, alpha, Escape, confirmed history, closed/disabled fields and isolation from another window or unrelated picker. Passed 16 checks through the connected Editor; OS Alt/capture interaction is not simulated by this test.
- `LayoutSetup`, then `LayoutVerify`: layout and captured-pointer history reorder.
- `LayoutVerify` also checks the shared Alt/history eyedropper cursor texture requirements and the unchanged default cursor on plus. Layout/reorder and texture cleanup passed in the Editor. `EyedropperCursorSmoke.cs` passed 18 checks for CPU/GPU copying, outline padding and render-state preservation. Native hover appearance remains a manual check.
- `HistorySelectionVerify` after layout: pointer selection applies the stored color and moves it immediately after the plus tile, preserving other entries and suppressing duplicates.
- `AddSwatchVerify` after layout: pointer activation of the plus tile; leaves an identical gray swatch beside it for framebuffer comparison. The plus uses the same color-surface renderer and shared swatch style, not a native Button.
- `WheelVerify` after layout: ring angle input, empty-center exclusion, saturation/value input,
  visible gradient tracks and upper-right original/new samples. Verified at default and minimum width.
- `DragRemovalVerify` after layout: red pending-removal state on each edge, return-to-history,
  Escape, capture loss, deletion only on release and unchanged selected color.
- `MinimumSize`, then `MinimumVerify`: controls remain inside the smallest allowed window.
- `LayoutCleanup`: close only the test picker and destroy its transient document. Always run after layout tests.

`GradientKeyPickerSmoke.cs`: run `Setup`, then `Verify`. Covers double-click selection, Gamma/Linear,
Standard/HDR, unchanged opening state, alpha preservation and stale callbacks; closes its test window.
`FocusLifecycle` separately checks native window Close, Escape, repeated key-picker opening, an expired pending focus check, focus restoration and unrelated-picker isolation. Run without user gradient/picker windows open. Passed through the connected Unity 6000.7.0a6 Editor; only temporary test windows are created and closed.

Validated on Unity 6000.7.0a6: clean Editor compilation; Main 34 checks and Fields 8 checks passed;
gradient, layout, drag, drag-out deletion/cancellation and minimum-size checks passed. Documentation source validation passed.
Screen eyedropper interaction and visual fidelity to the native picker require manual UI review;
layout bounds and synthetic pointer tests do not establish pixel-level visual equivalence.

Main additionally verifies that OK/Cancel buttons are absent, Escape closes and restores the exact opening HDR color, and ordinary window closing confirms the current color and adds it to document history.
History checks cover newest-first insertion, the leading plus button retaining exact HDR/alpha without closing, duplicate suppression and explicit additions surviving Escape. Layout verifies the plus matches swatch dimensions and is excluded from reordering; captured-pointer reorder and drag-out removal still pass with the leading button present. The rendered grid was inspected through a framebuffer capture.

## Gradient history palette and recency

`GradientHistorySmoke.Setup`, then `Verify` checks the History block above presets, no plus tile, direct selected-key application without a picker, HDR and Gamma/Linear conversion, retained alpha/time, alpha-key/midpoint exclusion, document-history synchronization and duplicate promotion. `Capture` writes the actual temporary window framebuffer to `Temp/WhimTex/gradient-history.png`; always run `Cleanup` afterwards. `PickerRecency` independently verifies that manually entering an existing color promotes it only on confirmation, not during editing or after canceling. Passed on Unity 6000.7.0a6; framebuffer layout inspected. Shared picker history regression checks (Main 34, LayoutVerify, HistorySelectionVerify and DragRemovalVerify) also passed after factoring shared swatch/drag behavior.

## Channel-aware color display

`ColorPickerChannelsSmoke.cs` runs with no user picker open. `Main` passed 87 checks for all 16 masks, original RGBA/HSV/HEX preservation, opt-in/service exclusion, originating-provider isolation, live mask/toggle changes and GUI recreation. `Setup`, then `Layout` creates a temporary picker with ordinary, channel-aware, HDR, mixed and disabled fields. `ColorPickerRingSmoke.Capture` captures its actual framebuffer; use `ColorPickerChannelsSmoke.Cleanup` afterwards to close only this test window and restore the channel-display preference.

Validated on Unity 6000.7.0a6: compilation, default/minimum-width layout, native single-alpha bar, HDR-label ordering and non-pickable overlay; actual framebuffer inspected for two-channel and single-channel grayscale rendering, diagonal swatches, service exclusion and mixed/disabled fields. Ring shader/material lifecycle checks passed. Existing Main (34), Fields (8), BrushSampling (16), HEX (92) and gradient-key checks passed. Tests create no persistent assets. Other Unity versions, graphics backends and display scaling remain unverified.

HDR channel-field regression: after `ColorPickerChannelsSmoke.Setup`, run `HdrSetup`, capture with `ColorPickerRingSmoke.Capture`, then run `HdrPixels`. Repeat capture/verification after `HdrSingleChannel`; `HdrModeSwitch` checks HDR invalidation with an unchanged mask. `Cleanup` closes the temporary window. On Unity 6000.7.0a6, framebuffer comparisons against a neighboring native HDR field passed for RG and R-only, including visible intensity variation and original-triangle preservation; mode-switch and 87 general channel checks also passed. Native ramp textures are borrowed through public resolved styles, not created, modified or accessed by reflection.

## HEX input

`ColorPickerHexSmoke.Main` passed 92 checks on Unity 6000.7.0a6: prefix placement in the hierarchy, mixed case, optional #, surrounding whitespace, RGB/RGBA, shorthand, HDR exposure, fixed/hidden alpha, normalized six-digit RGB and invalid input. `PasteSetup`, then `PasteVerify` checks resolved prefix bounds/right alignment and sends the public Paste command to the native editable TextElement, followed by focus-loss commit. The clipboard is restored and the temporary picker closed in finally; the clipboard/layout check passed. No user picker may be open when starting these tests.

## Eyedropper magnifier

`ColorPickerEyedropperSmoke.cs` runs through Pipeline `run_script` with no user picker open:

- `Main`: 19 checks covering native adapter binding, missing-member fallback, foreign-session isolation, idle/active/canceled/selected states, public ColorField result delivery, Escape and closing a pending session. Lifecycle tests use a test-owned fake with the same three-member contract; they do not read additional Unity internals.
- `NativeRender`: one-frame rendering through the real native `DrawPreview` in an IMGUIContainer, with startup through the public ColorField. Writes `Temp/WhimTex/color-picker-eyedropper-render.png`; inspect it for the pixel grid and picked-pixel frame. `NativeCleanup` closes only named temporary test windows if cleanup was interrupted.
- `NativeSetup`, then `NativeInspect`: optional short native-interaction probe, automatically closed after 15 seconds. Synthetic pointer dispatch can immediately terminate the native capture, so this is not a substitute for manually moving the physical pointer, selecting a pixel and pressing Escape.

Validated on Unity 6000.7.0a6: clean compilation, 19 adapter/lifecycle checks and native DrawPreview framebuffer capture showing the grid and center frame. Physical-pointer tracking/selection, multiple monitors, Editor UI scaling and Unity 6000.0 execution remain manual checks. The production reflection exception is restricted to IsOpened, DrawPreview(Rect) and End, as documented in the HDR contract.

## Immediate hue ring

`ColorPickerRingSmoke.cs` runs through Pipeline `run_script` after `ColorPickerSmoke.LayoutSetup`:

- `Verify`: public `ImmediateModeElement` renderer, supported/error-free shader, material destruction on detach and recreation on attach. Reflection inspects WhimTex fields only, never Unity internals.
- `Capture`, then `CaptureResult` after a repaint: read the temporary picker's actual framebuffer into `Temp/WhimTex/color-picker-ring.png`. No intermediate render texture is created. Inspect the PNG for hue direction, marker position and smooth inner/outer edges.
- `VerifyPlusCenter` after capture: compares the rendered plus and gray-fill centers in the framebuffer. The plus is drawn geometrically with the swatch, without a text label. Verified offset: (0, 0) physical pixels at the tested Editor scale.
- `ClipAndScale`, then `Capture` after layout: test 75% scaling and parent overflow clipping of the lower half. This modifies only the temporary test window; run `LayoutCleanup` afterwards.

Validated on Unity 6000.7.0a6: shader and script compilation, Main (25 checks), default/minimum wheel input, layout, detach/reattach and both framebuffer captures. No hue-ring materials remained after cleanup. The implementation uses public APIs available in Unity 6.0, but execution on a 6000.0 Editor and other graphics backends remains unverified. No forced repaint loop or render texture is used by the ring; `GL.sRGBWrite` is restored after drawing.
