# Procedural texture samples for agents

**38 curated examples**, each with an editable JSON recipe and an individual PNG preview.
The longest canvas axis is **256 pixels**; rectangular beams and streaks retain their aspect ratio.
These are package-internal references, not editor presets or an automatic asset import.

Start with [manifest.json](manifest.json). Search its descriptions and tags, inspect the chosen PNG,
then read only that example's recipe. There is no atlas to navigate or need to read all recipes.

## Files and reuse

Each descriptive filename has two matching files:

- `.whimtex.json` — a complete **`whimtex.document`** file with editable layers, document settings, History and embedded HLSL.
- `.png` — an individual visual preview, not its editable source.

The version-2 manifest records each example's ID, title, recipe/preview paths, dimensions, filter,
`outputSrgb`, category, description, tags and total layer count. JSON itself also stores output settings.
There are no bundled TIFF duplicates or overview PNG.

Documents use **Full Optimized**: active settings are explicit, inactive branches are omitted.
Their defaults are tied to format version 1. All samples are procedural; no external images,
downloads or project-specific asset references are required. Embedded HLSL and its parameter values
are separate data. Preserve both when adapting a sample.

Read a chosen document with `WhimTexDocumentJson.Read`, or copy it into an authorized Assets folder
and open it in WhimTex. Agents can use `whimtex_document_json` (`validate`, `write`, `insert`).
Opening restores sRGB/filter/precision. Pasting its JSON inserts layers and does not overwrite the
destination's output encoding; use the manifest's `outputSrgb` if reconstructing a complete sample by paste.
Save as TIFF when a Unity texture is needed. Do not edit the bundled references in place.

See the [shared format](../../Documentation~/JSON_FORMAT.md) and [schema](../../Documentation~/AI/document.schema.json).
These content files are not `ExecuteJson` command envelopes. Unity ignores `Samples~`.

## Finding examples

Use manifest tags to locate rings/portals, flares/bursts, beams/trails, smoke/clouds, liquid,
packed fields and surfaces. For example:

- [Arcane Rift](Arcane_Rift.whimtex.json): noise, twirl, gradient-colored plasma and targeted glow.
- [Magic Line Sharp](Magic_Line_Sharp.whimtex.json): sharp streak with inner/outer contour glow.
- [Smoke 2x2](Smoke_2x2.whimtex.json): procedural smoke cells with irregular silhouettes and edge fades.
- [Gas Particle](Gas_Particle.whimtex.json): gas fields packed into RGB channels.
- [Surveyed Archipelago](Surveyed_Archipelago.whimtex.json): warped terrain and contour lines.
- [Terrazzo Triangles](Terrazzo_Triangles.whimtex.json): seeded tile palette, chips and surface grain.

## Authoring notes

- Layer order is top to bottom. Preserve effect targets, clipping bases and layer-texture references.
- PNGs store straight RGBA8 without a baked checkerboard; alpha is never gamma-converted.
  For `outputSrgb:true`, RGB uses sRGB encoding. For `outputSrgb:false`, channel values are stored directly
  as linear data: import those PNGs with **sRGB disabled**. The PNG alone does not configure Unity's importer.
  Gas Particle, Ring Distortion and Sphere Distortion are data maps. Their raw channels are also useful
  for visual inspection, but are not sRGB display renders. HDR values are clamped for PNG only.
  Do not use the API's generic diagnostic PNG render for data export: it always produces an sRGB preview.
- Disabled study/background helpers are retained. Muzzle Flash retains its source background state;
  this refresh preserves the curated documents rather than imposing a new transparency policy.
- Smoke Billows uses white RGB and density in alpha for tinting. The 2x2 sheets contain four separate
  sprites, not an animation with generated in-betweens.
- Blur radii, pattern cell sizes and displacement strength/depth are pixel-based. Use the API `resize`
  operation with `preserveLayout:true` for proportional canvas changes; explicit pixel constants inside
  custom HLSL still require review. Do not scale normalized UV or gradient positions.
- Seamless behavior is recipe-specific; a seamless source does not guarantee seamless overlaid FX.

## Verification

From the package root:

```sh
node Tests~/AgentSamples.test.mjs
node Tests~/ProceduralClipboard.test.mjs
```

Run `Tests~/AgentSamplesSmoke.cs` through connected Unity Pipeline `run_script`, entry
`AgentSamplesSmoke.Run`, explicitly targeting the intended project. It compiles detached recipes,
checks layer counts, dimensions, finite output and FX diagnostics, and compares SDR renders against
decoded preview pixels. Optional `start`/`count` arguments allow short batches. It does not write project
assets or change open documents.

During this refresh all 38 JSON reconstructions were also compared against source TIFF composites
in linear RGBA: every pixel matched exactly on the authoring Editor. PNG compression was optimized
losslessly with decoded RGBA checked unchanged. Cross-device shader rounding can differ slightly.
Sphere Distortion was subsequently corrected to output raw vector data (neutral RG=0.5), without
decoding it as a display color; its source document and recipe were updated together. The three data-map
PNGs were re-exported without sRGB encoding, and the smoke test also checks the sphere's neutral value.
