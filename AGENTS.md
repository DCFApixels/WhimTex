# WhimTex asset authoring

For browser/text-only AI generation of procedural layer clipboard JSON or HLSL effects,
start with [AI_AUTHORING.md](AI_AUTHORING.md) and [the self-contained contract](Documentation~/AI/README.md).
This is distinct from the connected live-editing API below.

WhimTex is the public product name. The package ID `com.dcfa_pixels.sprite-editor`,
`DCFApixels.SpriteEditor` namespace, `sprite_editor_*` commands and persisted identifiers
are compatibility contracts, not branding to rename. The canonical repository is
`DCFApixels/WhimTex`; documentation is hosted at `https://dcfapixels.github.io/WhimTex/`.

## Versioning

Script compilation is allowed only through the connected Unity Editor's own compilation
pipeline (including Unity CLI/Pipeline commands), targeting the explicit project path.
Never use standalone MSBuild or `dotnet build` for this Unity project. Run one compilation
at a time and check its result before requesting another. Player builds require a separate request.

Do not increment the package version when committing or pushing updates unless the user
explicitly requests a version change. Keep the current version otherwise.

When adding or updating third-party code or libraries, acknowledge the project and link its
upstream source in both READMEs. Preserve original copyright headers, include the applicable
license text and notices, and record the source version in `ThirdPartyNotices.md`.

For requests to create or edit **WhimTex compositor assets**, use the editor-side
`DCFApixels.SpriteEditor.SpriteEditorApi`, not generated Unity YAML or simulated mouse clicks.
This guidance is for authoring images; ordinary plugin source-code tasks do not require running it.

For path-based asset authoring, read [Documentation~/AgentAPI.md](Documentation~/AgentAPI.md) for command syntax, JSON operations,
coordinates, safety/Undo semantics and complete workflows. Examples live in
[Documentation~/Examples](Documentation~/Examples).

For work in an **already open window**, including unsaved documents and selected-region edits, first read
the portable [live-editing skill](Skills~/sprite-editor-live/SKILL.md). Its fast-start contract allows
`sprite_editor_begin` to reserve and capture context in one call, before full inspection or prompt preparation.
Read [Documentation~/LiveAgentAPI.md](Documentation~/LiveAgentAPI.md) for subsequent steps or advanced options.
Reserve a named layer before lengthy generation;
preserve user edits to its placement/name/visibility. Choose the sampled source from the user's request.
Live image completion inserts owned Drawing pixels directly, without importing a separate File source.
For inline shader authoring, use live `fx` requests; do not create a separate shader file by default.
Acquire `sprite_editor_lock` for FX/settings edits on an existing layer and release it on completion or abandonment.

- Discover `sprite_editor_*` commands on the intended running Editor. Always pass the explicit
  Unity project path. The Pipeline adapter is optional; direct C# API calls work without it.
- Respect the project's compilation and asset-editing rules. Missing commands are not permission
  to install packages, start another Editor, recompile, or modify an unrelated project.
- For a generated image, first use an available image-generation tool, then import the resulting
  local PNG/JPEG with `sprite_editor_import_image`. Reuse the returned asset path in a File layer.
  The API does not generate images or download URLs.
- Prefer File layers and nondestructive transforms. Drawing strokes are useful for touch-ups,
  masks and simple procedural marks, not a substitute for an image-generation tool.
- Inspect before a path-based batch edits an existing document. Live reservations capture the required
  context atomically and do not require a full inspection first. Use stable layer IDs and the returned revision;
  use `@aliases` for newly added layers within a batch. Do not identify layers by display names.
- Validate unfamiliar batches with `dryRun:true`. Check the API's `success`, not only the CLI
  process/transport result. Unknown fields and unsupported settings are errors.
- After a timeout or save failure, inspect before retrying: additions and strokes are not
  idempotent. Never replay a whole batch merely because its response was lost.
- Render a preview and inspect it visually before declaring an image task complete. Rendering
  uses the actual compositor and requires graphics; do not launch it with `-nographics`.
- Scope new assets to the user's requested output location. Do not overwrite unrelated files,
  alter existing source import settings, or delete working documents to retry a failed command.

## Documentation maintenance

- Brush terminology: **procedural brush** has no tip texture; **textured brush** uses a tip texture.
  In Russian use **процедурная кисть** and **текстурная кисть**. Use these names consistently in UI and documentation.

- Keep README files concise: introduction, installation, quick start and links to the guide.
- Write the EN/RU user guides for artists: lead with the desired visual result, where to click,
  and how controls change the image. Omit rendering/storage/cache internals, Undo implementation
  details, regression history and descriptions of incidental UI layout behavior. Keep warnings only
  when they affect the result, compatibility or loss of editable work. Ordinary Undo shortcuts belong
  in the shortcuts page. Keep programming contracts in the separate technical reference, not the guides.
- Update the matching `Documentation~/en/` and `Documentation~/ru/` user-guide pages for feature changes.
  Keep the shared English API contract in `Documentation~/AgentAPI.md` accurate; preserve page paths.
- Follow `Documentation~/building.md` for website validation. Jekyll builds are documentation-only;
  they do not authorize a Unity build, compilation or asset reimport.
