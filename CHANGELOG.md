# Changelog

All notable changes to WhimTex are documented in this file.

## [Unreleased]

## [0.11.3] - 2026-09-24

### Added

- FX stack context-menu actions to copy a block and paste it as an independent Shader FX or a reused Material reference.
- Drag FX by their full header to reorder them or move them onto another layer, with Undo/Redo support.
- Stepped parallax occlusion mode for Displacement Map, with configurable view direction and sampling quality.
- Color/Mask FX with selectable mask channels, a profile curve, inversion, positioning and channel or color-weighted application.
- Step FX can threshold each color channel independently or use RGBA color components as per-channel effect strengths.
- Outline layers can detect their contour from Alpha, Red, Green, Blue or Luminance; Alpha remains the default.
- Shader FX color defaults accept `#RRGGBB`/`#RRGGBBAA`, and gradient parameters can define two endpoint colors in HLSL.
- FX parameter help boxes, groups with optional header controls, hidden parameters and custom inline labels.
- `@formerlyserializedas` parameter aliases preserve compatible values when shader uniforms are renamed; float, bool and enum values share scalar storage.
- Open document-owned FX code in Unity's selected script editor or VS Code. The VS Code integration installs bundled directive highlighting, completion and validation in an isolated profile, supports Restricted Mode and requests Apply when the working file is saved.
- A Random button beside the Noise layer's Seed field.

### Changed

- Digital Glitch's gradient blend options use Overlay instead of Override.
- Built-in FX controls use compact groups, conditional rows and shorter labels; Pixelate exposes Quantization and One Bit through its Color group.
- Simplified arithmetic channel masks and toggle calculations in built-in FX without adding extra rendering passes.
- Refined FX header menus, spacing and parameter refresh scheduling; refactored external-code synchronization without changing its workflow.
- Updated EN/RU/ZH guides and authoring references for FX controls, external editing, gradient wrapping and preset export.

### Fixed

- FX dragging no longer remains attached to the cursor or keeps autoscrolling after release or cancellation.
- Randomizing a Noise seed always produces a value different from the current seed.
- Corrected the overloaded catalog-method lookup in the color-preset regression test.
- Updated FX clipboard regression checks for context-menu pasting without the removed toolbar button.

### Upgrade notes

- Saving the external FX working file applies code but does not save the TIFF document. Save the document in WhimTex separately, and reopen its code after a Unity script reload to reconnect external editing.
- To update an independent embedded preset to the new controls and shader code, add it again from the catalog. Project-linked HLSL presets update from their source files.

## [0.11.2] - 2026-09-23

### Added

- Sharpen layer and Blur brush, including tablet-pressure controls and sampling from the current layer, layers below or the full result.
- Shader FX presets for Step, Halftone, CRT, VHS, Chromatic Aberration, Digital Glitch, displacement-map distortion and Negative, plus an additional Spherize mode.
- Conditional FX parameters, canvas point controls, draggable/foldable effect stacks and an agent command for compiling FX and returning diagnostics.
- Clamp, Repeat and Mirror gradient wrapping, with expanded CMYK/RGB plate controls for Halftone.

### Changed

- Deterministic settled FX and processor results are reused during interactive previews; Sharpen uses its fast approximation only when a valid high-quality cached result is unavailable.
- Supported File-layer sources can reuse cached original-resolution pixel data instead of repeatedly decoding the source during rendering.

## [0.11.1] - 2026-09-22

### Added

- Source-resolution Drawing conversion for PNG, JPG/JPEG, EXR and TGA files, including a built-in cross-platform TGA decoder with RLE and common pixel formats.

### Fixed

- File-layer conversion and layer merges now read supported source files directly instead of using Unity's potentially compressed/import-limited texture.
- Opening supported ordinary image files as Drawing layers preserves the original dimensions.

## [0.11.0] - 2026-09-21

### Added

- Editable TIFF documents: one file stores the saved composite, layer model and Drawing pixels, while Unity imports it as a normal texture.
- Native Texture Importer settings for mipmaps, texture compression, platform overrides and sprite slicing.
- Auto, 8-bit and Float32 output precision.
- Lazy Drawing loading, reusable compressed blocks and portable Burst SHA-256 hashing to reduce document loading and saving work.
- Staged saves, integrity checks, interrupted-save recovery and safeguards against overwriting externally modified documents.
- TIFF authoring APIs for batch creation, headless live editing and Assistant editing in an open window, with inspection, comparison, validation and recovery commands.
- Package-owned reference TIFF fixtures and regression coverage for persistence, migration, Live Update and agent workflows.

### Changed

- TIFF is now the main document format. Legacy WhimTex `.asset` documents can be opened and migrated, but cannot be created or saved in place.
- TIFF output settings use Unity's standard Inspector instead of the legacy output-settings window.
- Live Update temporarily uses an uncompressed texture, enables Read/Write when needed and restores the imported texture when the session ends.
- Updated EN/RU/ZH documentation and READMEs for the TIFF workflow, migration, sprite import and Live Update; moved the VFX example into the layer guide.
- Development-only diagnostic logs are gated behind `WHIMTEX_DEBUG`.

### Fixed

- Linked Shader FX presets survive save/reopen, with embedded source retained as a fallback if the preset is unavailable.
- Live Update recovery and reload handling, test asset cleanup and document-path validation.
- Unity 6.0 compatibility for object identifiers, assembly discovery and package dependencies.
- Broken localized TIFF reference links in GitHub Pages.

### Upgrade notes

- To migrate an old document, use **Save As** in WhimTex or **Assets → WhimTex → Migrate Legacy .asset to TIFF…**. The original asset is preserved; existing material and File-layer references must be reassigned to the new TIFF where needed.
- Keep TIFF files together with their `.meta` files. Resaving a WhimTex TIFF in an external image editor may discard its editable layers.
- One TIFF Live Update session can run at a time. Default and Sprite imports are supported; Crunch and other texture types update on Save.
- Agent integrations should use the current TIFF command names documented in the [Agent API](https://dcfapixels.github.io/WhimTex/reference/agentapi/).

## [0.10.25] - 2026-09-18

### Added

- Linked Output images: document saves update an assigned PNG, TGA, JPG or EXR at canvas resolution while preserving its GUID, import settings, platform overrides and sprite references. Save As of an existing document disconnects the new copy's output link.
- Texture-only output, sRGB and Alpha Is Transparency controls, maximum output size, resize algorithms, advanced mipmap settings and manual/automatic BC compression.
- Resizable and collapsible output preview with checkerboard, channel and mip selection, dimensions and memory/file-size information.
- Output-settings validation, Revert with Undo, pending-change highlighting and regression tests.

### Changed

- Refined Output Settings layout, aligned fields, sprite pivot choices and explicit border controls.
- Output now sits beside canvas dimensions; Tiled is a compact text-sized footer toggle beside UV and Guides.
- Updated EN/RU/ZH saving, preview and tiling documentation.

## [0.10.24] - 2026-09-18

### Added

- Shared Output Settings window accessible from WhimTex and the saved asset Inspector: texture storage, sampling, mipmaps and sprite settings including pivot and 9-slice borders.
- Optional Unity 2D Sprite integration for Single/Multiple sprite editing. Slice data and generated sprites remain usable without the package; existing sprite references survive renaming, reordering and mode changes.
- Regression coverage for output formats, Live Update restoration, sprite slicing and settings-window lifecycle.

### Changed

- UV mesh fields also accept model assets and resolve the first available mesh in their hierarchy.
- Updated EN/RU/ZH saving and UV documentation.

## [0.10.23] - 2026-09-17

### Added

- `// @header(Name)` parameter headings for FX and HLSL brushes, preserved in preset and portable exports.
- Logical parameter sections in seven built-in FX presets.

### Changed

- Compact FX headers with an actions menu, fewer persistent help boxes and Shader Inputs nested under Code.
- Unified Normal Lighting and Bevel Emboss shading. Base Color alpha blends transparent lighting into a filled surface and defaults to zero in both presets.
- Updated EN/RU/ZH guides, AI authoring contracts and regression coverage.

## [0.10.22] - 2026-09-17

### Added

- SDF source offset with Transparent, Clamp, Repeat and Mirror boundaries, contour offset, independent Signed inside/outside distances and a cached Profile curve.
- Clipboard/API support, localized documentation and CPU/GPU regression tests for the new SDF controls.

### Changed

- Bevel Emboss now uses an ordinary height-map input on any layer and outputs Both, Highlight Only or Shadow Only on transparency for layer-based blending.
- Removed the special raw-distance channel between SDF layers and FX; effects remain independent plugins over the shared image/parameter contract.
- Older embedded Bevel Emboss effects must be replaced by adding the updated Lighting/Bevel Emboss preset.
- Extended SDF calculation domains above 64 million pixels report an error instead of silently cropping.

## [0.10.21] - 2026-09-17

### Added

- Copy as Portable clipboard JSON with hierarchy, transforms, FX values and internal layer references; unchanged URL-backed Drawing layers retain their source links.
- Empty Drawing/File placeholders with warnings for omitted image content, and File texture restoration by asset GUID and local ID.
- Bounded expansion of custom HLSL includes for portable sharing, preserving separate function definitions.
- Curve FX parameters with cached GPU lookup textures and linear, easeInOut, easeIn, easeOut and one defaults.
- Self/None defaults for texture parameters, Normal Lighting and SDF Bevel FX presets, and per-effect activity toggles.
- Canvas-pixel keyboard nudging with gradual repeat acceleration and separate canvas/layer-list keyboard context.

### Changed

- Updated built-in FX parameter controls, curve defaults and soft ranges.
- Layer settings sections retain their expanded state when switching layers.
- Updated EN/RU/ZH guides, clipboard schema and regression tests.

## [0.10.20] - 2026-09-17

### Added

- Hierarchical group transforms and multi-layer transforms with shared TRS, skew and perspective editing.
- Projective Transform2D FX parameters and layer-local coordinates for procedural shader shapes.
- Layer-backed texture parameters, including disabled Drawing/procedural sources and full-color groups, with dependency validation and cache integration.
- Float2, float3 and normal FX parameters, including an on-canvas normal direction handle with hemisphere switching.
- Regression coverage for group transforms, multi-layer editing, shader coordinates, texture sources and vector parameters.

### Changed

- Updated EN/RU/ZH guides and authoring contracts; removed the standalone gradient test window.
- Updated the heart clipboard example to the current gradient format.

## [0.10.19] - 2026-09-16

### Added

- Pixel Art/Pixelate FX preset with block pixelation, optional 4x4 block averaging, level count, gamma, one-bit two-color output and eight dither patterns evaluated on the block grid.
- Dither pattern and amount controls in Color/Posterize, replacing the on/off toggle, backed by a shared `Dither.cginc` include for every preset that exposes dithering.
- GPU regression coverage for all dither patterns of both presets, one-bit output and the halftone threshold edge case.

### Changed

- Halftone dithering clamps its level step, so white pixels no longer exceed the output range or produce a third color in one-bit mode; FX metadata rejects an explicit float default outside a hard range boundary.
- Posterize documents that had dithering enabled now use Bayer2 instead of hash noise; Hash moved to the end of the shared pattern list.
- Pixelation and dithering sections in the English, Russian and Chinese guides, with Russian guide ordering fixes for the brush panel and the gradient sections.

## [0.10.18] - 2026-09-16

### Added

- Color Balance, Color Filter, Levels, Posterize and Threshold FX presets with parameter tooltips and preserved source alpha.
- GPU regression coverage for the five color presets, including zero-width thresholds, dithering, black-point lift and finite output.

### Changed

- Color correction presets handle degenerate ranges and near-zero luminance safely. Posterize uses an integer count of output levels and stable per-pixel dithering; Levels supports luminance-preserving or independent RGB adjustment.

## [0.10.17] - 2026-09-16

### Added

- Double-precision TRS/projective layer transforms with corner distortion, skew and perspective gestures, matrix clipboard/API input, and canvas-space brush footprints.
- Perspective-aware procedural rendering, gradient handles and fill sampling; distorted gradients are baked for layered export.

## [0.10.16] - 2026-09-16

### Added

- Groups now support FX with automatic isolation, a read-only Compositing status in Properties, clipboard/live authoring and baked FX results in layered export.
- Independent soft HLSL float boundaries (`[min .. ~max]`, `[~min .. max]`, `[~min .. ~max]`) allow numeric entry and label dragging past selected slider limits in FX and brush controls; preset export preserves each boundary flag.
- Color/HSV FX preset with hue shift, saturation/value multipliers and amount, preserving source alpha and supporting HDR colors.

### Changed

- Streamlined agent instructions and organized architectural context and feature decisions in Context~/.

## [0.10.15] - 2026-09-16

### Added

- Color/Gradient Map FX preset with editable gradient, amount and reverse controls, preserving source alpha.
- FX gradient parameters with defaultless declarations, editable black-to-white initial gradients, cached HDR LUT sampling and independent serialized values.
- Inline `// tooltip` comments on HLSL parameter declarations supply per-control hover descriptions in FX and brush editors, preserved by preset export.

### Changed

- FX preset menus discover headers without loading ShaderFX assets, parsing parameters or hashing shader dependencies. Project headers survive script reloads; unchanged user presets reuse cached entries. Full loading and validation happen when an effect is selected.

## [0.10.14] - 2026-09-15

### Added

- Enum FX dropdowns, optional parameter defaults and linked controls sharing one shader variable; preset export preserves their current value.
- Normal Map/Normalize HLSL effect preset for RGB-encoded normal directions, preserving alpha.
- Bool FX parameters use editor toggles backed by float uniforms, including HLSL preset defaults and live API values.
- Chinese README with navigation matching the English and Russian versions.

### Changed

- Polished Russian user guides and synchronized the English and Chinese documentation.

## [0.10.13] - 2026-09-15

### Added

- Drag project HLSL effect presets onto a layer to append FX, or onto the preview/layer-list background to create a Shader Processor.

## [0.10.12] - 2026-09-15

### Added

- Project brush presets import as brush assets with square, dark-background stroke thumbnails and an Inspector preview, without changing the portable `.sebrush` format. Drag a preset into WhimTex to choose the brush.

## [0.10.11] - 2026-09-15

### Fixed

- Layer footer grouping and deletion accept dragged layers even when no layer is selected.

### Changed

- New Color Fill layers default to Unbounded tiling; existing layers keep their settings.

## [0.10.10] - 2026-09-15

### Added

- User gradient presets in the gradient editor, saved without names in the configured presets folder. Presets can be applied, copied and removed.
- Gradient Copy/Paste uses JSON, including HDR colors, alpha keys, interpolation and midpoints; pasted values have independent storage.

## [0.10.9] - 2026-09-15

### Fixed

- Corrected the FastNoiseLite source link in AI documentation so GitHub Pages validation and deployment succeed.

### Changed

- Clipboard paste failures, including layer/brush JSON validation, HLSL compilation and image downloads, now report full exceptions to Unity Console as well as showing a short notification.

## [0.10.8] - 2026-09-15

### Added

- FastNoiseLite is available automatically in HLSL effects and brush scripts, without a user include.

## [0.10.7] - 2026-09-15

### Added

- Standard (with an optional texture) and cached HLSL brush tip sources, with shared textured-tip controls.
- HLSL brush presets from project files and the user Brushes/HLSL folder; an inline code editor and preset export.
- Clipboard brush JSON for browser AI, including URL texture tips, a schema and example recipes.

## [0.10.6] - 2026-09-15

### Changed

- Dragged guides no longer snap to other parallel guides.
- Guides snap to intersections only when no parallel guide passes through the intersection.
- Canvas edge/center and selected-layer snapping remain available.

## [0.10.5] - 2026-09-15

### Added

- On-canvas gradient controls: transform handles, color keys, midpoint diamonds and direct color picking.
- Delete selected color keys with Delete or by dragging away from the gradient line.

### Changed

- Gradient position, size and rotation use the layer transform; separate Center and Radius settings and JSON options have been removed without migration.
- New Gradient and Noise layers default to Unbounded tiling.
- Selected gradient controls draw above other keys with a blue highlight.
- Updated EN/RU/ZH guides and clipboard schema/examples.

### Known Issues

- GPU regression testing detects a color mismatch at a Fixed gradient key boundary.

## [0.10.4] - 2026-09-15

### Added

- Custom HDR gradient editor with Classic, Linear, Perceptual and Fixed interpolation, smoothness, independent color/opacity keys, midpoints and independent copy/paste.
- Chinese user documentation and reciprocal EN/RU/ZH language navigation.
- Optional canvas filtering in clipboard JSON and expanded gradient authoring settings and schema.

### Changed

- Layers and brushes now use WhimTex gradients. SDF defaults to Linear interpolation and uses GPU gradient coloring.
- PSD export rasterizes unsupported gradient interpolation to preserve its appearance.
- Existing serialized Unity gradients are not automatically migrated to the new type; keep originals before resaving older documents or brush presets.

### Fixed

- SDF shader input binding when applying the new gradient lookup texture.
- Gradient field preview refresh and independent editing/copying behavior.

## [0.10.3] - 2026-09-14

### Added

- Linked-image clipboard example with a stone wall source and editable pixelation, posterization and Bayer dithering.

### Changed

- Clearer AI authoring entry points, direct paths to example recipes, and a common-mistakes checklist before the full JSON/HLSL specification.

### Fixed

- Centered pixel-block sampling and unbiased Bayer thresholds in the stone wall example; its Drawing layer uses Point filtering.
- Corrected rename compatibility notes and the minimum version for linked Drawing images; removed machine-specific paths from test guidance and preview outputs.

## [0.10.2] - 2026-09-14

### Fixed

- Corrected the agent-facing documentation, which still told an AI assistant that clipboard JSON cannot include an image. The README comment for AI assistants no longer asks for `self-contained` JSON, the schema is titled `clipboard layer JSON` instead of `procedural clipboard layers`, and the authoring contract states the linked Drawing layer (`"type": "drawing"` with `url`) next to procedural layers instead of presenting procedural layers as the only option.

## [0.10.1] - 2026-09-14

### Added

- Clipboard JSON can add a Drawing layer that downloads its image from a direct HTTP(S) link: `{"type": "drawing", "url": "https://…"}`. WhimTex confirms the hosts first, fetches every linked image before inserting anything, keeps each image at its source resolution and fits the layer transform to the canvas. The whole tree, images included, lands as a single Undo step, and nothing is pasted if a download fails.

## [0.10.0] - 2026-09-14

### Added

- Drop shadow around the canvas in the preview, offset slightly downward so the canvas reads as lifted. It is hidden in Tiled preview, where the canvas already fills the whole area.

### Changed

- Changed the package ID from `com.dcfa_pixels.sprite-editor` to `com.dcfapixels.whimtex`. Embedded installations keep working; update the dependency entry wherever the package is referenced by ID.
- Renamed the code identity from `DCFApixels.SpriteEditor` to `DCFApixels.WhimTex`: the namespace, the `DCFApixels.WhimTex`, `.Pipeline` and `.URP` assembly definitions with their `WHIMTEX_*` defines, and the `SpriteEditor*` types and files (`WhimTexApi`, `WhimTexBranding`, `WhimTexColorInputs`, `WhimTexCommands`, `WhimTexMaterials`, `WhimTexPsdExporter`, `WhimTexUI`, `WhimTexUserSettings`, `WhimTexUserSettingsWindow`).
- Renamed the UI Toolkit surface for the same reason: `sprite-editor-*` CSS classes and the workspace element names became `whimtex-*`, `SpriteEditorSplitView.uss` became `WhimTexSplitView.uss`, and the `SpriteEditorNormalizeOutput` shader pass tag became `WhimTexNormalizeOutput`. The USS cascade baseline was regenerated for the renamed selectors; that also absorbs the drift which had already been failing that check.
- Renamed the agent-facing surface to match: the `sprite_editor_*` commands became `whimtex_*`, the `sprite-editor-live` skill ID and its `Skills~/whimtex-live/` folder became `whimtex-live`, the preview output folder moved to `Temp/WhimTex/`, and the persisted preference keys moved from `DCFApixels.SpriteEditor.*` to `DCFApixels.WhimTex.*`. Existing preference values are not migrated, so those settings fall back to their defaults.
- Every document-persisted type carries `MovedFrom` markers for the old namespace and assembly, so documents and effects saved under `DCFApixels.SpriteEditor` keep their layers and effects without re-saving.
- Moved the default preset folder from `%LocalAppData%/DCFApixels/SpriteEditor/Presets` to `%LocalAppData%/DCFApixels/WhimTex/Presets`. While the new folder does not exist the old one stays in use, so brushes and effects saved before the rename remain in the selector; move those files to the new folder to complete the switch.

## [0.9.6] - 2026-09-14

### Added

- Paste procedural layers, groups and embedded HLSL effects from browser-generated JSON with Ctrl+V. Local targets are remapped; paste and optional canvas resizing share one Undo. Existing documents can accept or decline the supplied size.
- Discoverable AI authoring guide, clipboard JSON Schema and complete examples, linked visibly and in raw README comments.
- Paste PNG/JPEG images from direct HTTP(S) links as independent Drawing layers, preserving source resolution and fitting their transform to the canvas.
- Original Size button in the Transform toolbar for restoring one source pixel per canvas pixel.
- Clamp and Unbounded tiling modes, with procedural continuation for Noise, Gradient, Color Fill and Shape; existing serialized tiling values are preserved.

### Fixed

- Preview guides are preserved when the window rebuilds after a script reload, even when its runtime document reference needs rebinding.
- Drawing layers retain source resolution until their transform is applied, preventing premature loss of detail.

## [0.9.5] - 2026-09-13

### Added

- Mesh UV outlines and UV-island selection for painting textures on models.
- Content-Aware Fill with sampling controls, inner-border filling and selection inversion.
- Editable shape layers, per-corner rectangle rounding and elliptical selections.
- Canvas rotation, exact zoom and angle controls, guides, intersection snapping and a Guides visibility button in the preview footer.
- Automatic HLSL effect catalogs, editable Transform 2D shader parameters and distortion presets.
- Shader presets saved from the code editor, shared user/project preset libraries and project brush presets.
- White and blue noise in monochrome or color, directional 1D noise and a Make Seamless effect.
- Windows clipboard image paste, cross-window layer copying with Drawing content, and alpha-aware layer picking.
- Document-named tabs, new documents in adjacent tabs and live updates between linked compositors.
- Documentation screenshots covering brushes, Shader Processor, VFX, UV, agent generation and Live Update.

### Changed

- Unified Gaussian and Motion Blur under a Blur layer with a mode selector.
- Added outline offset and center fill, improved antialiased contours, and exposed canvas output filtering.
- Refined brush controls, the preview footer, user settings and the empty-document workflow; Live Quality now defaults to 100%.
- Added WhimTex manta branding, optional preview decoration and cached layer thumbnails.
- Separated persistent layer identity from its behaviour, with missing-behaviour replacement and matching-parameter recovery.

### Fixed

- Desktop eyedropper positioning and sampled-color consistency, and Drawing content preservation across script reloads.
- Render-target restoration, gradient performance and gradient-mode persistence.
- Disabled Shader Processor visibility, layer dragging and dropping into empty list space.
- Documentation screenshot URLs and GitHub Pages publication.

### Compatibility

- Documents created before the Layer/Behaviour redesign are incompatible with the current layer format. No automatic migration is provided; keep a backup and export important results before updating.

## [0.9.0] - 2026-09-11

### Changed

- Renamed the product to WhimTex across the editor window, menus, settings, messages, package display name, PSD application metadata and EN/RU documentation.
- Focused the introduction on quick texture touch-ups, VFX masks and small sprite workflows inside Unity.
- Added descriptive search metadata, language links and a sitemap to the documentation website.
- Preserved package, API, command, shader and preference identifiers and document compatibility.
- Renamed the GitHub repository to `DCFApixels/WhimTex` and updated installation, documentation and badge links. The documentation website now uses `/WhimTex/`.

## [0.8.0] - 2026-09-11

### Added

- Live agent editing of open compositor documents, including unsaved work, with named placeholder layers and preserved user changes to their names, visibility and placement.
- Generated Drawing layers, parameter-based layers, selected-region edits, trial previews and shared-context reservations for multiple results.
- Inline Shader FX and Shader Processor authoring through the live API, with editable parameters and shader diagnostics, without separate shader files.
- Temporary content locks for agent edits to existing layers, with manual cancellation and conflict protection.
- A compact layer GUID copy button with tooltip and confirmation, a portable live-editing skill, bilingual guides and regression checks.

### Changed

- Generated images retain their source resolution and fit the requested region through Transform.
- Selection-based generation supports strict masks or placement guides that allow details beyond the selection.
- Automatic document targeting falls back to the most recently focused WhimTex window when no window currently has focus.

## [0.7.11] - 2026-09-11

### Added

- Live texture output updates for saved compositor assets, with a compact record-style toggle and stable material references.
- A Brushes drawer with texture tips, opacity, flow, spacing, scatter distribution, size and angle variation, direction-based rotation, random tint, Random/Sobol sampling and probabilistic texture flips.
- Procedural Hardness/SDF Gradient modes and gradient-mapped textured SDF brushes, with a live stroke preview and manual preview scale.
- Per Stroke and Per Stamp blend application, including interaction between overlapping stamps within one stroke.
- Portable brush presets with embedded tip textures, a shared configurable preset folder and a Brushes subfolder.
- Agent API support, bilingual guides and opt-in regression checks for the new brush controls.

### Fixed

- Layer-list auto-scroll while dragging layers.
- Brush tip references now survive editor reloads, including texture subassets and preset-loaded tips.
- Textured brush stamps use explicit mesh attributes for reliable GPU vertex transport.

## [0.7.10] - 2026-09-10

### Changed

- Unified layer settings into Transform, Color & Blending, Properties and FX sections with compact headers and icons. Only Properties starts expanded; unavailable sections are greyed out.
- Assigning an HDR texture to a File layer automatically enables HDR color and blend ranges, while preserving subsequent manual changes and explicit API overrides.
- Layers and groups can be dragged by their name, opacity field or visibility icon. Vertical field gestures move layers, horizontal gestures select text, and focused text fields stay in editing mode.
- Name and opacity edits commit on Enter or focus loss; starting a layer drag discards unconfirmed input without toggling visibility.

## [0.7.9] - 2026-09-10

### Changed

- Post FX settings now open over the canvas without shifting or resizing the preview.
- The settings panel fits its contents and scrolls when the window is too small to show all controls.

## [0.7.8] - 2026-09-10

### Changed

- Reworked the English and Russian guides around artists' tasks, visual results and practical examples.
- Simplified both READMEs and removed implementation details from the user guides.
- Separated shader authoring instructions from effect usage and kept developer references out of guide search results.

## [0.7.7] - 2026-09-10

### Added

- A Just the Docs website with English and Russian workflow guides, cross-language page links, keyboard-accessible search, technical references and troubleshooting.
- GitHub Pages deployment with pinned theme dependencies and automated checks for navigation, generated links, fragment targets and English/Cyrillic search.
- A documentation URL in Package Manager and the theme's source acknowledgement and MIT license.

### Changed

- Shortened both READMEs to installation, quick start and a guide map; detailed controls and caveats now live in the documentation.
- Preserved existing API and rendering reference paths, and documented the current alpha-product Swizzle options in the HDR reference.

## [0.7.6] - 2026-09-10

### Added

- Motion Blur effect layer with Linear and Circular modes, distance/angle or arc/center controls, centered or directional trails, and Transparent/Clamp/Repeat/Mirror edges.
- Motion Blur Strength (0–400%): alpha-correct original/blur mixing below 100% and denser translucent trails above 100%, preserving trail length and RGB brightness; exposed in Properties and the agent API.
- Alpha-correct linear HDR GPU filtering, reduced interactive previews, shared effect/group caching, Properties controls, and complete Motion Blur agent API settings and discovery.

## [0.7.5] - 2026-09-10

### Added

- A page-plus Layers footer button creates an empty Drawing layer on click or a merged Drawing copy of dropped layers/groups, keeping the originals and using existing merge Undo behavior.
- Procedural Noise layers powered by FastNoiseLite HLSL v1.1.1: six algorithms, seeded fractals, cellular controls, domain warp, and color/data output. Parameters update GPU previews during numeric dragging without rebuilding controls; generation uses the requested resolution directly, with no new refinement stage.
- Noise layer creation, partial settings, inspection and enum/default discovery through the agent API.

### Changed

- Shortened creation-menu labels by removing the Layer suffix, except for Drawing Layer.

## [0.7.4] - 2026-09-10

### Added

- Up/Down arrow keys navigate visible layer rows with automatic scrolling, without intercepting field editing.
- Swizzle offers R * A, G * A and B * A on every layer and group, using original input alpha with agent API support and compatible serialized channel IDs.
- A single Assign Channels context command routes each layer's R * A into successive output channels: RGB for one to three selected layers (opaque alpha and upper-layer Add) or RGBA for four (Swizzle only), in top-to-bottom order with one Undo step; disabled above four selections.

## [0.7.3] - 2026-09-10

### Added

- Shader Processor layers apply embedded Shader FX, transforms and Swizzle to the lower composite, with alpha-correct effect opacity, group scope and agent layer-type support.
- Optional URP 17.x Post FX preview with Scene View, Game Camera and Volume Profile sources, a collapsible drawer, synthetic Solid/Alpha Height/Alpha Mask depth and optional zoom-linked distance.
- Shared background color in User Settings and the Post FX drawer, applied as an opaque bottom fill before processing while retaining original alpha for depth. Post FX does not alter source cameras, profiles, document pixels or exports.
- Post FX background mode selects Solid Color or a shader-generated Checkerboard using shared user colors and cell size, stable across Live Quality changes and independent of the original alpha-derived depth.
- Post FX uses an isolated opaque game-camera surface for Renderer Features, including Full Screen Pass and SSAO, with demand-driven Depth/Depth Normals/Color passes and alpha-relief world normals. Renderer Feature/material changes invalidate the preview; temporal AO filtering is disabled on the owned stack.

### Changed

- PSDs containing stack processors show a baked composite and preserve the original hierarchy in a hidden Source Layers folder.

### Fixed

- Scene View Post FX no longer inherits an editor-stage culling mask that excludes the isolated preview surface.

## [0.7.2] - 2026-09-10

### Added

- Transform move and resize gestures lightly snap to canvas edges at a zoom-independent screen distance; hold Ctrl to bypass snapping while preserving Shift constraints.
- Gaussian Blur effect layer with Previous/Specific layer or isolated group sources, a pixel radius, Transparent/Clamp/Repeat/Mirror edges, alpha-correct linear HDR filtering, Properties and agent API support.
- Adaptive reduced-resolution Gaussian preview while editing, followed by full-quality preview refinement; saving, rasterization and exports use the full-quality algorithm.
- Window-local, budgeted effect/source caching with alpha-only group storage, shared RGBA promotion, dependency invalidation and cached numeric-error masks. Derived textures never enter document serialization or Undo.

### Changed

- Preview cursors now match the active tool: outline-only Brush/Pencil, standard arrows for Fill/selection, a zoom cursor for Zoom and a hand for Transform or middle-button panning. Cursor changes remain local to the preview.
- Transform handles show scale, rotation or pivot-move cursors in their existing hit areas and retain the action cursor throughout a drag; the rest of the preview keeps the hand cursor.
- Pending preview requests are throttled rather than postponed by every settings change, allowing continuous slider previews.
- Normal Map settings now offer Simple/Advanced views, semantic sections, generation guidance and an indication of modified hidden settings, without changing render parameters or Undo.

### Fixed

- Layer table headers stay above the scrolling list and remain aligned when the scrollbar appears.
- Clipping-mask indicators now point downward toward the base layer.
- Replaced obsolete UI Toolkit event cancellation calls with propagation and focus handling for consumed editor gestures, commands and shortcuts.

## [0.7.1] - 2026-09-10

### Fixed

- Effect layers can process hidden layers and groups in Previous and Specific modes, including hidden effect chains. Group child visibility, opacity, clipping and cycle guards remain unchanged.

### Changed

- Refined the Polygonal Lasso icon with a wider solid contour and a compact loop and tail.

## [0.7.0] - 2026-09-10

### Added

- Normal Map targeted layer with GPU height/texture generation, multiscale detail controls, transparent-aware smoothing, derivative/edge modes, export-aware encoding and complete agent API settings.
- Rectangle Select and Polygonal Lasso tools with canvas-space coverage, add/subtract/intersect operations, alpha selection from layer thumbnails and tiled-canvas support.
- Selection-masked brush, pencil, eraser and fill; an internal HDR clipboard with active-layer/merged copy and undoable paste to a new Drawing layer.

## [0.6.5] - 2026-09-09

### Added

- Non-destructive clipping masks for all layer types and groups, with shared-base chains that preserve soft alpha and base opacity.
- Selection-wide Clipping Mask command, row markers and Alt-click toggling at sibling boundaries; participating groups are automatically isolated.
- Clipping support in effect inputs, Drawing conversion, merging, native PSD records and the agent API, with documentation and opt-in regression checks.

## [0.6.4] - 2026-09-09

### Fixed

- Close warnings now cover edited saved documents as well as temporary ones. The native Save choice opens Save As; cancellation keeps the window open.
- Disable Save for unchanged saved documents; replace the temporary-document banner with a warning symbol on Save As. Clamp committed Canvas dimensions to the supported range, starting at one pixel.
- Keep brush/pencil cursors visible in Preview margins and allow strokes to start there. Only the overlapping tip paints canvas pixels; preview overlays remain clipped to Preview.

### Added

- User Settings window in the WhimTex window menu, with persistent checkerboard colors and cell size (1–128 UI pixels, default 16), numeric-error highlight color, live preview updates and an appearance reset. These preferences do not affect documents, exports or Undo.
- Selection-wide layer context-menu commands, including ordered movement, group operations, duplication, conversion and separate Properties/FX windows. Batch conversion resolves all source pixels before replacing layers and supports a single Undo step.
- Per-layer and group Swizzle with channel selection, inversion and zero/one constants, available in Layer Settings, Properties and the agent API.
- Automatic isolation for swizzled Pass Through groups; identity restores pass-through rendering. Effect targets, rasterization and exports respect remapped alpha and color; PSD preserves original group children in a hidden folder beside the baked result.

## [0.6.3] - 2026-09-09

### Fixed

- Reset preview exposure to 0 EV when opening the window or reloading scripts; exposure is no longer restored by window serialization or Undo.
- Highlight nonzero preview exposure with an amber label and tinted input background.
- Remember the selected preview tool across window reopening and script reloads, independently of Undo and layer availability. Leaving Transform restores the previous tool instead of forcing Brush or No Tool.

### Added

- Temporary eyedropper with Alt-click/drag in Brush, Pencil and Fill. Samples the full-resolution composition, including HDR and alpha, without preview display effects or changes to tool selection and Undo.
- Pixel-aligned Pencil tool with Circle, Square and Diamond tips, shared paint/erase colors,
  RMB erasing, straight lines, an independent integer size and the `P` shortcut.
- Pencil strokes through the agent API, with the same HDR, symmetry and layer painting path.
- Pencil preview uses Point filtering and full canvas resolution, temporarily bypassing Live Quality without changing its saved preference.
- Cached pixel-boundary cursors for Circle and Diamond tips, with screen-size and complexity LOD; moving the cursor does not rebuild its outline.

## [0.6.2] - 2026-09-09

### Added

- Multi-selection opacity and blend editing from inline fields without clearing the selection; numeric opacity shortcuts also affect selected layers and groups.
- Preview navigation with any tool: middle-button drag pans and the mouse wheel zooms around the cursor without modifying document data.
- Merge selected layers and groups into a Drawing layer with `Ctrl+E`, or keep originals with `Ctrl+Alt+E`; includes full-resolution FX/transform baking and single-step Undo.
- Global HDR / Standard color-picker preference for tools, layer colors/gradients and Shader FX parameters.
- Linear HDR rendering with independent Color Range and Blend Range per layer, HDR color controls,
  and half-float saved Texture2D/EXR output. LDR exports clamp a separate copy.
- Drawing storage promotion, explicit undoable conversion to 8-bit, HDR-aware painting and floating-point fill.
- Group opacity, pass-through before/after interpolation and isolated group blending, including nested groups and PSD metadata.
- Preview-only exposure and cumulative numeric-error diagnostics with a bug toggle and small asynchronous GPU readback.
- Agent API range/group controls, storage inspection and explicit `compact` operation; opt-in HDR/group regression checks.

### Changed

- Align layer rows and column headers in a shared table layout, with a show-all visibility action and refined eye icons.
- Allow layer dragging from non-input row areas, including group foldouts; keep column positions stable during drag feedback.
- Compact the layer menu, polish header spacing and simplify the Layers panel padding.
- Group Color Range and Blend Range controls in a compact foldout with a shared range selector.
- `TextureCompositor.Compose()` returns linear RGBAHalf instead of RGBA32. Raw-pixel consumers must account for its format.
- SDF and Outline keep their bounded mask thresholds while producing floating-point color output.

### Fixed

- Standard picker mode now displays and paints bounded colors without overwriting their stored HDR intensity when switching modes.
- Limited excessive brush/fill intensity to half-float capacity while preserving linear RGB ratios, instead of replacing bright components with black.
- Removed repeated color-space conversion in brush and Shader FX uniforms, including previously applied effects.
- Preserved legacy Color Fill appearance while keeping its picker, API colors and PSD metadata consistent.
- Respected linear versus sRGB destination encoding when writing 8-bit pixels.
- Added opt-in color-pipeline regression checks with non-primary colors and serialization round-trips.

## [0.6.1] - 2026-09-09

### Added

- Layered PSD export with nested pass-through groups, Unicode names, visibility, opacity,
  blend-mode mapping and a merged RGBA image. Uses per-row RLE and bounded raster memory.
- Editable solid fills with masks, compatible gradient fills and Outline stroke effects on target-alpha snapshots.
  SDF, shader effects and incompatible procedural settings use raster fallbacks with conversion notes.
- Editor-side `WhimTexPsdExporter.Export` API, cancellable export and atomic destination replacement.
- Standalone PSD writer checks, an optional independent-reader check and opt-in Editor export smoke tests.

## [0.6.0] - 2026-09-09

### Added

- Tiled Preview fills the viewport with repeated copies of the composition. Brush and eraser
  footprints wrap across canvas edges and corners while preserving editable layer transforms.
- Rotatable Mirror symmetry with Continue/Clip edges, including agent API support.
- Opt-in regression checks for tiled painting, channel previews, painting parity, and targeted UI refreshes.

### Changed

- Display a single RGB channel in grayscale, optionally retaining alpha; display alpha alone as opaque grayscale.
- Keep editor previews on the GPU, reuse a tiled checkerboard texture, and avoid redundant effect-input uploads.
- Share stroke parameter handling and scope preview-header refreshes to the affected controls.
- Support dragging the brush Size label and exclude shared tool settings from Undo/Redo history.
- Reorganize the English and Russian READMEs and add a compact editor screenshot.

### Fixed

- Improve Undo lifecycle, unsaved-document protection, and transient-resource cleanup.
- Preserve the active render target when restoring Drawing surfaces.
- Update agent API and drawing-pattern smoke tests for the current integration.

## [0.5.9] - 2026-09-09

### Added

- Opt-in shared paint settings regression checks in `Tests~/PaintToolSettingsSmoke.cs`.

### Changed

- Open with No Tool selected. Keep unavailable tool icons dimmed but selectable.
- Store interactive brush, color and fill settings separately from layers in window/user preferences;
  preserve per-layer transforms and repeat/symmetry settings, and retain legacy layer/API brush fields.
- Keep tool controls retained across layer selection changes. Show the brush cursor on non-Drawing layers.
- Use native HelpBox styling for the Transform section, stretching its contents and X/Y fields to the right edge.
- Offer cancellable Keep Transform / Apply Transform conversion on brush or fill clicks on non-Drawing
  layers. Consume the triggering click without painting or filling; preserve conversion Undo/Redo.

### Fixed

- Preserve the previous tool settings row as an empty toolbar in No Tool mode, including its wrapped height.
- Keep Fit preview padding at 36 UI units for every tool, reserving room for the default
  rotation handle without changing image scale when selecting Transform. Leave manual zoom unchanged.

## [0.5.8] - 2026-09-09

### Added

- Zoom tool (magnifier, Z) with anchored click steps, Alt-click zoom-out, area framing,
  middle-button panning, scale readout, Fit and 100% controls. Keep view state separate from
  document transforms and preserve it when switching tools/layers.
- Opt-in viewport geometry regression checks in `Tests~/PreviewZoomSmoke.cs`.

### Fixed

- Clip preview images, repeat guides, transform handles and zoom selection to the canvas viewport.
  Avoid painting outside that viewport during captured drags and only draw visible checkerboard cells.

## [0.5.7] - 2026-09-09

### Added

- Fill tool (bucket icon, G) for Drawing layers: sample the current layer's stored pixels or the
  full-resolution visible composition, writing only to the active Drawing layer with one Undo step.
- Per-layer fill settings in the preview header: color tolerance (0–255), antialiasing and edge
  expansion (0–32 source pixels). Share foreground/background colors, X swap and the RGBA paint mask.
- Contiguous toggle (enabled by default): disable to fill all matching pixels, including disconnected
  regions, with either sample source. Preserve per-layer settings and skip the flood queue in global mode.
- Burst/native-buffer connected-region search and approximate distance-based edge coverage;
  opt-in flood-fill regression checks in `Tests~/FloodFillSmoke.cs`.
- Reset button next to Original Aspect in the Transform header, with Undo/Redo support.

### Changed

- Order preview tools as No Tool, Transform, Brush and Fill.
- Use an All Layers checkbox for fill sampling; default it off and Contiguous on for new Drawing layers.
- Align fill option labels consistently and leave enough room for the full Expand label.

## [0.5.6] - 2026-09-08

### Added

- Neutral No Tool mode (cursor icon, V): disable preview painting and tool overlays, including on layer changes.
- Compact left tool strip with vector brush/hand icons, active-tool highlighting and layer-aware availability.
- Brush shortcut B; keep Brush/Eraser together and move Transform activation from the header to the tool strip.
  Show brush or transform options in the header without rebuilding controls on tool changes.

### Changed

- Refine the brush icon with a tapered handle, distinct ferrule and curved teardrop bristles.

## [0.5.5] - 2026-09-08

### Added

- Per-Drawing-layer Radial start angle slider (0–360°), shared by mirrored repeats, Clip and preview guides;
  expose the angle through the agent API and align guides with pixel-space sectors on non-square canvases.
- Versioned agent JSON API and optional Unity Pipeline CLI commands: inspect, image import,
  validated edit batches, transforms, groups, Outline/SDF targets, Drawing strokes and PNG previews.
- Stable layer IDs and local batch aliases, mandatory revision checks for edits, dry-run validation,
  whole-batch Undo and explicit partial-save recovery. Never overwrite existing compositor/image assets.
- Repository agent instructions, API reference, JSON recipes and opt-in CLI smoke tests.

### Changed

- Unify Mirror and Repeat into mutually exclusive drawing pattern modes; prevent extra X/Y reflections
  in Radial and other repeats. Migrate legacy mirror-only settings without changing stored pixels.
- Move per-Drawing-layer symmetry and repeat controls to layer settings and Properties.
- Keep Live Quality always visible on the left of the Preview footer; leave brush controls in the header.
- Brighter pane headers and an 80% default Live Quality.
- Refresh drawing surfaces on Undo/Redo even without a WhimTex window open.
- Declare Unity's Newtonsoft Json dependency for strict JSON request parsing.

## [0.5.4] - 2026-09-08

### Added

- Number-row/numpad opacity shortcuts for the active non-group layer, with 0.6-second two-digit
  input, 0 = 100%, 00 = 0%, and a single Undo step per quick pair. Leave text entry and modified
  shortcuts alone; reset pending digits on focus/selection changes, clicks and other commands.
- Assign SDF/Outline targets by dropping layer handles, using the active layer of a multi-selection.
  Keep inspector selection stable during handle drags, automatically choose Specific input, and reject
  cyclic or cross-document targets. Support both embedded and standalone settings with Undo.
- Independent, persisted name counters for each layer type; preserve existing names and continue after
  matching numbered names. Use Layer for Drawing, and File/Color Fill/Gradient/Outline/SDF/Group for
  the other types, migrating longer name counters. Duplicates append `Copy n` to the full source name,
  using one shared copy counter.
- Fixed Layers and selected-layer settings headers with thin dividers; show Layer Settings when empty.

### Changed

- More saturated RGB channel buttons, matching neutral alpha styling, and recessed off states.

### Fixed

- Preserve pending opacity digits across UI Toolkit's additional character events, so quick
  two-digit input combines correctly instead of replacing the first digit.

## [0.5.3] - 2026-09-08

### Added

- RGBA channel buttons in the Preview footer, with independent channel display and an associated
  brush-color mask: disabled components paint as zero, A off makes painting a no-op, and erasing
  remains unchanged. Inspect alpha alone in grayscale or RGB without transparency. Reuse a GPU
  preview buffer without recomposing layers or CPU readback on toggles; leave Save/Export unfiltered.
- Muted pastel red, green and blue active states for the corresponding channel buttons, with
  subtle hover highlights and neutral off states. Keep the alpha button's existing appearance.

## [0.5.2] - 2026-09-08

### Fixed

- Default and reset layouts reserve 400 UI pixels for the right pane and give Preview the
  remaining width, instead of fixing Preview to a narrow 340-pixel strip. Keep the upper-right
  settings section at 320 pixels and preserve manually adjusted pane sizes across reopening.
- Explicit code-editor Undo/Redo shortcuts and command handling when WhimTex suppresses
  Unity shortcuts. Record draft edits once in Unity's Undo history, group continuous typing,
  separate navigation/paste/cut/replacement/newline actions, and restore caret/selection without
  rebuilding the field or compiling shaders. Keep parameter and Apply operations out of typing groups.

### Added

- Reset WhimTex Settings from Unity's window tab menu, with confirmation. Restore layout,
  selection, scroll/foldout/tool state in open WhimTex windows and remove the saved Live
  Quality preference without replacing documents, changing layer data or clearing Unity preferences.
- Duplicate a layer or nested group from its row menu, or drop selected layers onto the footer
  plus button to duplicate the selection in one Undo step. Select copies above the originals;
  clone drawing pixels and embedded Shader FX independently, remap copied effect targets,
  and retain Previous inputs explicitly when duplication changes their adjacency.
- Inline Shader FX in the selected layer's settings: create, edit code/parameters, reorder and
  remove effects without opening another window or saving a separate asset. Apply works before
  the first document save. Save embeds effects and shaders in the compositor; Save As clones them
  independently, while external reusable FX remain references. Preserve code fields on value edits.
- Shader FX assets with an HLSL `ApplyFX` function, live Float/Color/Vector/Texture2D parameters,
  input-texture sampling and preview/full-canvas dimensions. Apply explicitly compiles the draft,
  reports shader diagnostics and saves a hidden embedded shader; invalid drafts retain the last
  successful program. No shader generation or CPU readback in the per-effect render path.
- Standard `#include` and `#include_with_pragmas` support for Assets, Packages and paths relative
  to the FX asset. Nested includes use Unity's preprocessor; no custom import language.
- Mix Shader FX and existing Materials in one ordered modifier list, create FX from that list,
  and open selected modifiers in the Inspector. Retained UI Toolkit code and parameter fields;
  refresh dependent open compositor previews when FX parameters or applied code change.

## [0.5.1] - 2026-09-08

### Fixed

- Draw saved compositor output in the small root-asset icon in Project list/tree layouts,
  including one-column mode. Keep native grid previews and sub-asset icons unchanged;
  cache document lookups and invalidate them on saves/project changes without composing layers.

### Added

- Save / Save As now stores a full-resolution Texture2D and an Output Sprite inside the
  layer document. Promote the texture to the main asset for texture-field assignment and
  native Project thumbnails without a separate export. Output Sprite uses a centered pivot,
  full-rectangle geometry, and 100 pixels per unit.
- Preserve embedded output object identities on subsequent saves and canvas resizing;
  create independent outputs for Save As and recover output references after Undo.
- Resolve documents from their generated textures/sprites for double-click opening, the
  document field, and Assets/Open in WhimTex. Keep the layer-document Inspector button,
  add Save & Update Output, and render its preview from saved pixels without recompositing.
- Upgrade existing documents on explicit WhimTex Save only. Outputs reflect the last
  save; source texture changes and Undo/Redo require another save to update the baked result.

## [0.5.0] - 2026-09-08

### Added

- Independent layer Filter modes: Source (default), Point, Bilinear, and Trilinear in retained
  Transform settings and the Preview toolbar. Inherit assigned texture filtering without changing
  its importer; preserve the choice through Transform Reset, rasterization, and Undo/Redo.
- Source tiling inherits texture wrap modes per axis, including Clamp and MirrorOnce.
  Keep Clip as the default and distinct from Source Clamp's edge extension. Repeat/Mirror no longer
  force bilinear sampling; all tiling modes honor Filter and existing source mipmaps.

- Drag Texture2D assets from Project into the layer list to create File layers, including
  multi-texture drops, insertion between rows, and drops into groups. Fit original proportions,
  select the created layers, and undo the entire drop in one step without modifying source assets.
- Drop Project textures onto Preview to create File layers at the top of the root stack,
  independently of the active layer/group, and scroll Layers to the newly inserted layers.
- Automatically fit original proportions when assigning a texture to an empty File layer;
  preserve Transform when replacing an existing source.

- Original Aspect in layer Transform settings and the Preview Transform toolbar. Fit source
  proportions by shrinking one axis while preserving image center, pivot, rotation, flips,
  and tiling; account for canvas dimensions and support Undo/Redo without rebuilding fields.

- Layer multi-selection with Ctrl/Cmd-click and Shift ranges, with the last selected layer
  active for inspector/painting/transform tools. Group, delete, and drag selected layers together
  in tree order, treating selected groups and descendants as a single subtree.

- Save button for existing documents and Ctrl+S/Cmd+S, falling back to Save As for temporary
  documents. Commit focused fields, active strokes, and transforms before saving the document
  and its drawing textures, without saving unrelated project assets.

- Split the right pane into a retained selected-layer inspector above the layer list, with a
  draggable horizontal divider, remembered inspector height, and independent vertical scrolling.
  Reuse type-specific Edit controls and Outline/SDF target selection without rebuilding on value edits.

- Export menu with PNG, JPEG (white background), TGA, linear EXR, and native Texture2D assets.
  Texture assets are distinct from layer documents, with guarded replacement preserving references.

- Snap the Preview pivot to nine frame anchors within 10 UI pixels, independent of canvas
  resolution. Holding Ctrl disables snapping immediately, including while the mouse is stationary.

- Drag the gold pivot handle in Preview Transform mode without moving the image: compensate
  Position for the current rotation and nonuniform/negative scale. Supports Shift axis lock,
  Escape cancellation, and a single Undo step; pointer movement repaints only the overlay.

- Convert any layer to Drawing through its context menu: keep the editable Transform or bake
  it (including tiling) into full-resolution pixels and reset it. Preserve layer identity,
  blend/opacity/FX, and existing Drawing brush settings; group conversion warns about isolation
  and descendant targets. Record texture creation/deletion with the replacement for Undo/Redo.

- Per-layer Transform tiling: Clip (default), Repeat, and Mirror, with retained controls in
  the layer settings and Preview Transform toolbar. Shared by Preview and export.

- Preview Transform tool (T): retained move/resize/rotate frame, Shift constraints, Escape
  cancellation, and one Undo step per drag. Available for non-group layers without painting conflicts.

- Hold Shift during a brush/eraser stroke to lock its direction horizontally or vertically on
  the canvas. Shift-click connects the previous painted endpoint to the clicked point, including
  consecutive Shift-clicks, with the existing brush spacing, symmetry, repeat clipping, and Undo.

### Changed

- Move canvas dimensions into a persistent Canvas toolbar above Preview, separate from the
  selected drawing layer controls. Anchor the main divider to the right pane so resizing the
  window preserves the settings/layers width instead of the preview width.

- Restyle the Layers footer as a compact 26-pixel toolbar with contiguous flat icon buttons,
  hover/pressed feedback, and a thin top border.

- Use narrow, borderless vertical-ellipsis layer menus with hover highlighting. Move FX and
  Properties (formerly Edit) into the menu, with Properties last; each invocation creates a new window.

- Simplify the Layers heading and move Add/Group into a pinned footer with square plus/folder
  icon buttons and a selected-layer delete button. Footer actions remain visible while scrolling.

- Remove divider direction tooltips, show the selected layer name as a retained inspector
  heading, and add a noninteractive 3-pixel divider beneath the document header.

- Extend each divider's invisible hit area by 1 pixel on both sides without changing its
  3-pixel appearance or pane spacing.

- Synchronize pane offsets when divider thickness changes during a stylesheet reload, avoiding
  stale gaps that expose the window background beside the drag handle.

- Use the #383838 window background for the right pane in the dark theme and simple 3-pixel
  dark dividers without light outlines. Retain centered grips, blue hover highlights on the full divider,
  resize cursors, light-theme support, and the native split-view resizer.

- Remove duplicate inline controls from the embedded inspector; put Transform controls in a
  retained foldout, collapsed by default, shared with standalone Edit windows.

- Move the document field, New, and Save As into a full-width header above both panes, with
  Export immediately after Save As; remove the old Export PNG button from the settings panel.

- Move implementation files and the editor assembly definition into `src/`, preserving all
  existing Unity asset GUIDs. Keep package metadata and documentation at the repository root.

- Replace the Drawing layer's Paint button with Edit, opening retained numeric Transform
  settings. Selecting a Drawing layer still enables painting in Preview.

- Name new groups `Group n` using a separate serialized counter from `Layer n`, including
  nested groups and grouping an existing layer. Preserve existing names.
- Keep output fields, layer rows, and drawing controls alive during value changes and selection.
  Rebuild only the affected subtree when the document, visible layer structure, or bound layer
  instance changes. Repeat modes, gradient modes, and effect inputs toggle existing controls.
- Synchronize fields through shared value bindings: update only changed values without emitting
  change events, preserve active text/caret and pointer interactions, and reconcile on focus or
  capture release. External notifications are coalesced instead of scheduling full UI rebuilds.
- Refresh modifier items only when their source list or contents change, and handle layer dragging
  with a pointer manipulator that cleans up capture on cancellation and detachment.

### Fixed

- Output width and height commit typed values on Enter or focus loss, so temporarily clearing
  a dimension while typing does not shrink the document and preview to a one-pixel strip.
- Selecting an unselected layer while editing its name or opacity no longer detaches the field.
- Brush Step no longer rewrites and clamps its text during typing; the model remains clamped and
  the field is reconciled when editing ends.
- External changes no longer discard UI refreshes while a field is focused. Effect and modifier
  windows also synchronize directly after Undo/Redo.
- Preview errors reuse one HelpBox, and cancelled layer drops restore the row's original margin.

## [0.4.0] - 2026-09-08

### Changed

- Rebuilt every WhimTex window and its `TextureCompositor` custom inspector with UI Toolkit.
- Replaced the main window with a resizable two-pane layout, retained-mode preview, recursive layer
  tree, drawing toolbar, and UI Toolkit drag-and-drop while preserving the existing editing workflow.
- Preserved direct Preview painting, brush and eraser hotkeys, groups, effect targets, modifiers,
  layer selection, Undo/Redo, and saved-compositor reopening across the migration.
- The Drawing `Step` value can now be scrubbed by dragging its label horizontally.

### Performance

- Settings panels and layer rows now rebuild only when editor state changes instead of every GUI
  event, and the Preview caches its image layout so the checkerboard is not repainted unnecessarily.

### Fixed

- Numeric label scrubbing no longer loses pointer capture after its first value change.
- Text and numeric field editing is no longer interrupted by a delayed external refresh while the
  active UI Toolkit field has keyboard focus.
- Drawing symmetry and repetition guides are hidden immediately after selecting a non-Drawing layer,
  preventing stale guide geometry from appearing offset after the Preview layout changes.

## [0.3.2] - 2026-09-07

### Changed

- Replaced the layer-selection radio button with a color-highlighted selected row.
- A layer or group can now be selected by clicking its free row area without interfering with
  visibility toggles, drag handles, or other row controls.

## [0.3.1] - 2026-09-07

### Changed

- Parallelized exact Euclidean distance-transform rows and columns with Burst jobs.
- Run the two approximate distance fields concurrently and parallelized their initialization and
  signed-output passes.
- SDF two-color gradients and Outline output now write directly into the destination Texture2D
  native buffer, removing intermediate pixel arrays and redundant `SetPixelData` copies.
- Reuse the signed-distance output buffer as the distance-to-object workspace and skip clearing
  temporary arrays that are fully overwritten.

## [0.3.0] - 2026-09-07

### Added

- Added every blend mode exposed by DataMath `DMBlend`: None, Add, Subtract, Multiply, Divide,
  Screen, Overlay, Darken, Lighten, Dodge, Burn, Linear Dodge, Linear Burn, Linear Light,
  Linear Light Add/Sub, Vivid Light, Pin Light, Hard Mix, Hard Light, Soft Light, Difference,
  Exclusion, Negation, and Overwrite.

### Changed

- Standard blend modes now use source-over alpha composition, so their blend
  function affects only overlapping coverage and non-overlapping pixels remain visible.
- RGB blend functions run in an sRGB blend space even when the Unity project uses
  Linear color space.
- Existing serialized Normal, Multiply, and Overwrite mode values remain compatible.

## [0.2.9] - 2026-09-07

### Fixed

- Clip repetition mode now confines an entire continuous stroke to the cell or radial sector where
  it started instead of continuing after the cursor crosses into another repeated shape.

## [0.2.8] - 2026-09-07

### Fixed

- Undo and Redo keyboard shortcuts remain available while WhimTex suppresses other Unity
  shortcuts.

## [0.2.7] - 2026-09-07

### Fixed

- Painting inside an alternately mirrored repeat cell or radial sector now keeps the active brush
  copy under the cursor instead of reflecting the input a second time.

## [0.2.6] - 2026-09-07

### Fixed

- Drawing hotkeys no longer remain disabled after Unity leaves its global text-editing flag set
  while no IMGUI text field actually owns keyboard focus.

## [0.2.5] - 2026-09-07

### Fixed

- Unity global and contextual shortcuts are suspended while the WhimTex window has focus,
  preventing Unity commands such as Local/Global toggle from consuming Drawing hotkeys.

## [0.2.4] - 2026-09-07

### Added

- RMB temporarily erases a Drawing layer without changing the selected Brush/Eraser tool.
- Foreground/background brush colors with an `X` swap shortcut.

## [0.2.3] - 2026-09-07

### Added

- A persistent **Live Quality** slider controls painting-preview resolution from 12.5% to 100%.
- Setting Live Quality to 100% disables painting-preview downscaling.

## [0.2.2] - 2026-09-07

### Added

- Configurable Drawing brush stamp spacing from 1% to 400% of the brush diameter.

### Changed

- Live painting previews are throttled to 30 updates per second and rendered at a lower working
  resolution; the full-resolution preview is restored when the stroke ends.
- Brush stamps for a mouse segment and pattern repetitions are submitted in one GPU batch.
- Repeated-stamp deduplication now uses constant-time lookups instead of a quadratic scan.

## [0.2.1] - 2026-09-07

### Added

- Saved `TextureCompositor` assets can now be opened by double-clicking them in the Project window.
- The `TextureCompositor` Inspector now includes an **Open in WhimTex** button.

## [0.2.0] - 2026-09-07

### Added

- GPU-backed Drawing layers painted directly on the composite Preview.
- Brush color, size, hardness, eraser, and `[` / `]` size shortcuts.
- Live reflection across configurable horizontal and vertical axes.
- Horizontal, vertical, grid, and radial repetition up to 64 copies.
- Regular and alternating-mirror repeat modes.
- Continue and per-cell/per-sector Clip boundary modes.
- Drawing textures stored as sub-assets of saved `TextureCompositor` assets.

### Fixed

- Exact Euclidean EDT now keeps the correct parabola envelope on both sides of a shape, fixing
  missing Outline/SDF pixels next to Drawing layers and other inputs.

## [0.1.0] - 2026-09-07

### Added

- Layered texture composition with file, color fill, gradient, outline, and SDF layers.
- Nested, non-isolated groups with drag-and-drop reordering.
- Outline and SDF inputs targeting either the previous item or a selected layer/group.
- Exact Euclidean, approximate Euclidean, Manhattan, and Chebyshev distance algorithms.
- Normal, Multiply, and true RGBA Overwrite blend modes.
- Per-layer material modifiers and editable layer transforms.
- Resizable side-by-side preview and settings layout.
- Sequential document-local `Layer n` names.
- PNG export and reusable `TextureCompositor` assets.
