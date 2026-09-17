# WhimTex — agent rules

## Start here

- Code task: read [context overview](Context~/HANDOFF.md); consult only relevant sections of [feature decisions](Context~/DECISIONS.md). These files supplement these rules; they are not a task queue.
- Browser AI / clipboard JSON / standalone HLSL: [AI_AUTHORING.md](AI_AUTHORING.md) → [contract](Documentation~/AI/README.md) → [examples](Documentation~/Examples/Clipboard/README.md).
- Edit an open compositor: read [whimtex-live skill](Skills~/whimtex-live/SKILL.md) first, then [LiveAgentAPI](Documentation~/LiveAgentAPI.md) as needed.
- Edit a compositor by path: [AgentAPI](Documentation~/AgentAPI.md).
- Do not load all references for an unrelated task. These files describe rules and decisions, not Git status or a task queue.

## Identity and compatibility

- Product: WhimTex. Package: `com.dcfapixels.whimtex`. Namespace: `DCFApixels.WhimTex`; assemblies: `DCFApixels.WhimTex*`.
- Preserve `whimtex_*` commands, `whimtex-live` skill ID, `Temp/WhimTex/` preview path, `whimtex-` USS prefix.
- Preserve existing `MovedFrom` rename markers. Past permission to break a specific format is not permission for future breaking changes.
- Repository: `DCFApixels/WhimTex`; site: https://dcfapixels.github.io/WhimTex/.
- Never increase version without an explicit request, including when committing/pushing.
- Do not introduce product-analogue names into code, UI or documentation.

## Execution and safety

- Follow root project AGENTS.md. Compile only through the connected Unity Editor/Pipeline; no standalone MSBuild or dotnet build. One compilation at a time; check completion/errors. Player builds require a separate request.
- Explicitly target the intended Unity project. Missing tools do not authorize installing packages, starting another Editor or changing unrelated projects/assets.
- For compositor authoring use `WhimTexApi`, not generated YAML or simulated clicks. Source-code work does not require the authoring API.
- Inspect existing documents before path-based edits; use IDs and `@aliases`, not display names. Validate unfamiliar batches with `dryRun:true`; check inner API success, not just CLI transport success.
- After ambiguous timeouts/save failures, inspect before retrying. Adds and strokes are not idempotent.
- Scope assets to the requested output. Do not overwrite unrelated files, change source import settings or delete work to recover from a failed command.
- Verify image-authoring results with an actual rendered preview; no `-nographics`.

## Live generation

- `whimtex_begin` reserves a named layer and captures context before lengthy generation. Preserve the user's subsequent placement/name/visibility changes.
- Choose source sampling and strict/approximate selection from the request; do not impose one policy on every generation.
- Existing-layer FX/settings edits require `whimtex_lock`; release on success or abandonment.
- Inline shader work uses live `fx` requests, not separate shader files by default.
- Live image completion inserts owned Drawing pixels. For path-based imported File workflows, generate the image externally, import with `whimtex_import_image`, reuse its returned path. Read the selected workflow's contract rather than mixing the two.
- Preserve source resolution; fit with transforms. Drawing strokes are for painting/masks/touch-ups, not a substitute for requested image generation.
- Clipboard Drawing `url` download support is distinct from live/local image import; do not infer URL support for arbitrary API operations.

## UI and validation

- FX are independent plugins over the shared effect contract. Adding a preset must not change layer generation/behavior, inspect its shader in layer code, or introduce layer-specific hidden outputs for that effect. Use ordinary input images and explicit parameters. Changes to layer behavior require an explicit user request; general FX infrastructure is not permission for preset-specific coupling.

- Prefer compact Unity-style controls, restrained decoration, minimal help text and meaningful changes. Avoid fragile internal APIs for cosmetic features.
- Preserve appearance during refactoring unless asked otherwise.
- Read applicable skills from the available `.agents` installation; do not assume an older `.codex` copy is current.
- Rendering changes: check composite, group/Target input, clipping, thumbnails and export; preserve caller render state and temporary-texture ownership.
- Run relevant tests in `Tests~`; inspect each file first (Node tests, C# snippets and entry-point classes use different runners). Report unverified behavior explicitly.

## Documentation and dependencies

- Update matching EN/RU/ZH user guides. Keep paths and reciprocal `translations` metadata, including the page itself; add new localized pages together.
- Artist guides: result → controls → visible effect. Keep API/storage/cache/Undo internals in technical references, not user guides. Warnings should concern results, compatibility or loss of work.
- README: short introduction, installation, quick start, guide links. Keep browser-AI entry points discoverable.
- Terminology: procedural brush / textured brush; RU процедурная кисть / текстурная кисть; ZH 程序化画笔 / 纹理画笔. ZH layer 图层, group 组, brush 画笔. Keep UI labels, JSON fields, CLI names and code in English.
- Update the English authoring/API contract with behavior changes. Change schema generators, then regenerate schemas; do not edit generated schemas alone.
- Validate docs via [building.md](Documentation~/building.md). Documentation builds do not authorize Unity builds/imports.
- Third-party additions/updates: preserve copyright, include license/notices, record source version in `ThirdPartyNotices.md`, acknowledge and link upstream in localized READMEs.
