---
title: "Building the documentation"
nav_order: 5
permalink: "/building/"
search_exclude: true
---

# Building the documentation

The Jekyll source is `Documentation~`. Unity ignores that directory; building this website does
not build, compile, import or open the Unity project. Just the Docs and Jekyll versions are pinned
in Gemfile, with transitive dependencies in Gemfile.lock.

## Local preview

Use Ruby 3.3 with a working native-extension toolchain, Bundler and Node.js 22 or newer.
On Windows, RubyInstaller + Devkit supplies the native toolchain. On Linux, install Ruby development
headers and a C/C++ compiler through the normal environment setup.

From the repository's `Documentation~` directory:

```sh
bundle install
node scripts/check-docs.mjs source
node scripts/build-agent-fields-schema.mjs --check
node scripts/build-brush-schema.mjs --check
node ../Tests~/AgentDocumentation.test.mjs
node ../Tests~/DocumentJsonSchema.test.mjs
node ../Tests~/Compatibility0125.test.mjs
node ../Tests~/RenameMarkerCleanup.test.mjs
node ../Tests~/UserSettingsCleanup.test.mjs
node ../Tests~/GradientClipboardCleanup.test.mjs
node ../Tests~/RemainingLegacyCleanup.test.mjs
node ../Tests~/AgentCommandInventory.test.mjs
node ../Tests~/MakeSeamlessContract.test.mjs
node ../Tests~/ProceduralClipboard.test.mjs
node ../Tests~/BrushClipboard.test.mjs
bundle exec just-the-docs rake search:init
bundle exec jekyll serve --baseurl /WhimTex --host 127.0.0.1
```

Open `http://127.0.0.1:4000/WhimTex/`. Search initialization creates a generated theme file;
it is intentionally ignored by Git. Build output, caches and installed gems are not Unity assets.

## Production checks

```sh
bundle exec jekyll build --strict_front_matter
node scripts/check-docs.mjs site
```

The checks cover local source links, language counterparts, navigation metadata, generated page and
asset links, fragment targets and an indexed page from each language. Inspect the actual site at
both desktop and mobile widths after layout changes; source validation alone cannot prove visual quality.
Agent documentation checks additionally compare command discovery and FX parameter limits with source,
and parse the JSON examples. Persistence, rendering and Undo semantics require Unity integration tests;
syntactically valid examples are not proof of runtime behavior.

Make Seamless's contract check compares all fields, enums and numeric bounds with the parser,
snapshot and generated live API field schema. `Tests~/MakeSeamlessContractSmoke.cs` (Unity Pipeline
`run_script`, entry `MakeSeamlessContractSmoke.Main`) also checks runtime defaults, exact endpoints,
partial updates, invalid values, documented operations and the procedural clipboard recipe using
temporary models only. See the [validation report](https://github.com/DCFApixels/WhimTex/blob/main/Tests~/MakeSeamlessContract.md).

The theme's SEO tag supplies titles, descriptions, canonical URLs and Open Graph metadata.
`head_custom.html` adds reciprocal language links and application metadata on the landing page;
`sitemap.xml` lists public guide pages without generated timestamps. Keep the metadata descriptive
of real editing workflows. Technical references remain accessible through links but are not in the sitemap.
The checks also validate these generated tags and sitemap targets. Search-engine indexing and ranking
are external to the deployment; do not promise a position or a date for search results.

## GitHub Pages

The Documentation workflow builds on relevant main-branch changes and pull requests. Only main
publishes. Set repository **Settings → Pages → Source → GitHub Actions** once.
The deployment environment is `github-pages`. No workflow step invokes Unity or installs Unity packages.
Published JS/CSS URLs include the deployment commit, so browsers revalidate theme assets after updates.

Published URL: [dcfapixels.github.io/WhimTex](https://dcfapixels.github.io/WhimTex/).
If the repository name or host changes, update `url` and `baseurl` in `_config.yml` and README links.

## Editing pages

### Brand assets

`Images/whimtex-logo.svg` is the approved vector master. Use it directly in README and page headers.
The PNG favicon, touch icon, link-preview image and Unity window icon are rasterizations of this
same SVG, not separately drawn variants. Do not replace document thumbnails or tool icons with the logo.

To regenerate the PNGs with the optional Node.js `sharp` tool installed:

```sh
node scripts/build-brand-assets.mjs
```

An existing Sharp module path can be passed as the first argument instead of installing it here.
This image-conversion helper does not start Unity or trigger a refresh.

### Page content

- Keep matching user-guide paths under `en/`, `ru/` and `zh/`; each page lists all three in a
  `translations` front-matter value, itself included. Add a new page to every language at once.
- Use relative Markdown links to source `.md` files. The relative-links plugin rewrites them for the site;
  the same links remain usable when reading the sources on GitHub. Keep each link's label and target
  on one source line: multiline labels can escape rewriting. For explicit site permalinks, use
  Liquid's `relative_url` filter so the repository base path is preserved.
- Use stable ASCII permalinks; do not move the existing API/reference files without preserving links.
- Each page has one H1. User guides are for artists: introduce the visual task, then show steps and
  explain controls by their effect on the image. Keep only caveats that affect the result or risk losing
  editable work. Do not narrate UI layout mechanics, caching, storage internals, Undo implementation,
  or past fixes. Programming details belong in the separate technical reference.
- Exclude engineering references from site search so artist queries lead to practical guides.
- The self-contained `AI/README.md` is intentionally searchable and included in the sitemap: it is the public entry point for browser AI authoring. Its matching EN/RU/ZH workflow pages remain artist-facing.
- After changing live API/brush fields, run `node scripts/build-agent-fields-schema.mjs` and the matching contract tests. Run `Tests~/ProceduralClipboardSmoke.cs` through Unity Pipeline separately to validate parsing, rendering and Undo against the editor.
- New clipboard recipes use `whimtex.document`; their schema is generated by `scripts/DocumentJsonSchema.cs` through Unity. `DocumentJsonSchema.test.mjs` checks current recipes and samples. `ClipboardExamplesSmoke.Run` checks read/save/reopen parity for the nine current recipes using detached documents and 64-pixel renders. `build-agent-fields-schema.mjs` emits reusable live API/brush field definitions, not a clipboard envelope.
- `ClipboardBrokenFxSmoke.Run` checks soft FX failures on open/paste, parameter and texture-reference preservation, render bypass, repair and Undo/Redo across writing modes. `DocumentJsonSmoke.Run(start, count)` checks TIFF-to-JSON export/open/save/render roundtrips from the package-owned `Tests~/Fixtures/Compatibility0125/procedural.tiff` and `Tests~/Fixtures/BASE_Gradient_128.tiff` fixtures without modifying them; run bounded batches through Pipeline. Export, Drawing omission, missing assets and cancellation/source isolation have separate `ExportWindowSmoke`, `DocumentJsonContractSmoke` and `DocumentJsonSafetySmoke` checks.
- Keep README as an introduction, installation, quick start and a map to these guides.
- Do not duplicate API tables into every language. Explain workflows in EN/RU/ZH; link the shared contract.
- Keep dependency sources and licenses in the repository notices when updating the theme.
