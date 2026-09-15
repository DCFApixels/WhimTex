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
node scripts/build-clipboard-schema.mjs --check
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
  the same links remain usable when reading the sources on GitHub.
- Use stable ASCII permalinks; do not move the existing API/reference files without preserving links.
- Each page has one H1. User guides are for artists: introduce the visual task, then show steps and
  explain controls by their effect on the image. Keep only caveats that affect the result or risk losing
  editable work. Do not narrate UI layout mechanics, caching, storage internals, Undo implementation,
  or past fixes. Programming details belong in the separate technical reference.
- Exclude engineering references from site search so artist queries lead to practical guides.
- The self-contained `AI/README.md` is intentionally searchable and included in the sitemap: it is the public entry point for browser AI authoring. Its matching EN/RU/ZH workflow pages remain artist-facing.
- After changing clipboard fields, run `node scripts/build-clipboard-schema.mjs` and `node ../Tests~/ProceduralClipboard.test.mjs`. Run `Tests~/ProceduralClipboardSmoke.cs` through Unity Pipeline separately to validate parsing, rendering and Undo against the editor.
- Keep README as an introduction, installation, quick start and a map to these guides.
- Do not duplicate API tables into every language. Explain workflows bilingually; link the shared contract.
- Keep dependency sources and licenses in the repository notices when updating the theme.
