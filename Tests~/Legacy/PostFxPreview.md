# Processor and Post FX verification

Do not import scripts, compile Unity or change project assets automatically. Run these checks only
after the user has manually compiled the source. `ShaderProcessorSmoke.cs` is an opt-in live C# script;
it creates and destroys transient objects without saving assets. It covers identity HDR/alpha,
before/after opacity, bypass, group isolation, order and serialization.

`PostFxPresentationSmoke.cs` checks the GPU presentation pass independently of cameras and profiles:
registered blit input, explicit pass routing, varying RGB, alpha and gamma-project decoding. Run it
through `eval_file` only after manual shader import; it must not return a constant fallback texture.

`node Tests~/PostFxSurfaceMath.test.mjs` runs without Unity: source contracts, reference relief-normal
math at several resolutions/projections and 10,000 octahedral normal round trips. It does not validate
shader compilation, GPU orientation or actual Renderer Feature execution.

## Manual preview checks

1. Use an existing document and existing URP Universal Renderer. Enable Post FX, open/close the drawer,
   resize it indirectly by resizing the window, and switch layers while editing a delayed numeric field.
   No field should be rebuilt during typing. The right layer pane must retain its width.
2. With Scene View selected, toggle that view's post-processing switch. Select Game Camera and compare
   its post-processing enable state and Volume mask/trigger. Explicit Profile should work without any
   MainCamera. Preview-only settings must not mark the source document/camera/profile dirty.
   Regression: Scene View cameras carry an editor-stage `overrideSceneCullingMask`. After rendering,
   the owned camera's mask must match its own preview scene, not the source camera. Switch Scene View
   → Game Camera → Scene View → Profile: image geometry must remain visible, including when the source
   Scene View is in an isolated editing stage. Never reset the source camera's mask.
3. Use a profile with an obvious color adjustment or bloom. Toggle Post FX: source pixels, clipboard,
   eyedropper, fill All Layers and exported pixels must not change. Drawing should update the processed
   image, and finishing a stroke should use the refined full-quality image.
4. With a depth-of-field profile and soft alpha, compare Solid, Alpha Height and Alpha Mask. Adjust
   Distance, Depth Range, Threshold and Invert. Disabling alpha or selecting R in preview must not change
   the generated depth. Compare perspective and orthographic overrides.
5. Set Background to a distinct color in the drawer, then change it in User Settings. Both fields and
   all open Post FX previews must update without rebuilding their UI. Reset Preview Appearance must
   reset both fields. Fully transparent pixels use the background, opaque pixels keep their source
   color, and soft alpha blends the two in linear light before post-processing (input alpha becomes 1).
   Alpha Height/Mask must still follow the original alpha, not the opaque fill. Turning Post FX off
   restores the original transparent preview; saved/exported alpha is unchanged.
   Switch Background Mode to Checkerboard: cells appear behind transparent and soft-alpha pixels,
   go through the same post-effects, and do not alter alpha-derived Depth/Normals. Edit checker colors
   and size in User Settings: open previews update. Live Quality changes must not change cell count;
   zoom magnifies the cells with the image. Return to Solid Color: the previous solid color remains.
   Closing/reopening the drawer must preserve the mode, with no field rebuild on mode changes.
   Link to Zoom is off by default; enabling it should
   affect depth-dependent effects. Animate should refresh grain without document changes.
6. TAA and active Motion Blur should produce an explanatory notice and be bypassed. A 2D renderer,
   non-URP pipeline or missing camera/profile should show a notice and the unprocessed preview.
7. Disable Post FX, close the window, reopen, and reset editor settings. No preview scene, camera,
   material or render texture should be left behind. Multiple editor windows should not share a
   mutable camera or VolumeStack. Existing scene objects and Volume Profiles must remain unchanged.
8. Add a Shader Processor, edit its embedded FX and use Apply. Verify opacity, hiding, moving across
   groups, Undo/Redo and save/reopen. Target cycles must be unavailable in effect input pickers.
   PSD should show Processed Result with original hierarchy in a hidden Source Layers folder.

## Renderer Features and demand-driven inputs

Use existing feature setups, or ask the user to configure them; do not edit renderer/project assets
automatically. Recompile/import manually first. Test both Forward and Deferred where available.

1. Enable an existing Full Screen Pass with an obvious color change. Check before/after post-processing
   injection points and Color/Depth/Normal requirements. Edit its material and toggle feature activation:
   the preview must update without touching the document. Features filtering by camera/scene/layer may
   deliberately exclude the preview; it uses an isolated Game camera and a layer-0 opaque surface.
2. Use a white source with a soft-alpha relief and SSAO. Compare Solid, Alpha Height and Alpha Mask,
   perspective and orthographic, Depth and Depth Normals inputs, and SSAO Before/After Opaque modes.
   Before Opaque must darken the surface; After Opaque must not apply AO twice. Solid must be flat;
   relief slopes must change normals smoothly and hard-mask silhouettes must not wrap across the canvas.
3. Inspect a preview request in Frame Debugger / Render Graph Viewer. In Forward, with no depth/normal
   consumers or forced prepass, there must be no preview depth-normal pass or dedicated depth copy.
   Adding SSAO Depth Normals or a Normal requirement must schedule the surface DepthNormalsOnly pass;
   a Color requirement must request the native color input. Deferred/depth priming can require buffers
   independently of features, which is expected. No CPU ReadPixels, document cache or Undo allocation
   should be used to produce these maps.
4. Disable Post FX: no further preview camera/depth/normal requests. Re-enable and resize repeatedly:
   no retained editor-owned map textures; native pipeline resource pools may remain allocated.
5. In newer URP with volume-based AO, temporal filtering is disabled only in the temporary volume stack.
   With STP selected, expect an unsupported notice and the original preview, not a broken frame.
   Existing source camera, renderer features, materials and Volume Profiles must remain unchanged.

This path uses native URP post-processing, not a pixel-identical recreation of an entire Game View.
Camera stacks, scene geometry/lighting, complete material buffers and temporal history are outside
the synthetic surface model. Third-party Renderer Features are not universally compatible.
GPU/color/depth correctness requires the checks above in Unity.
