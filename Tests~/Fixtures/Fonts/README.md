# Font input fixtures

These are immutable test inputs, not generated output or installed fonts.
Files under `Tests~/` are read by path and are not imported as Unity Font assets;
no `.meta` files are required. Do not copy them into `Assets` or install them in
the operating system. Executable tests belong in `Tests~/Cases`, outside this
fixture directory.

## SourceSerifPro-Regular.otf

The positive static CFF1 adapter input is `SourceSerifPro-Regular.otf`.

- Adobe Source Serif Pro Regular, version
  `3.000;hotconv 1.0.109;makeotfexe 2.5.65596`.
- Unmodified original binary, 236,192 bytes. This is smaller than the inspected
  `SourceSansPro-Regular.otf` in the same checkout (256,288 bytes).
- SHA-256: `8570d750feabf4ddd4efd26836ba3f5b70188db784baa12aa32d70142c750455`.
- Copied from `Demo/Windows/TestFonts/SourceSerifPro-Regular.otf` in
  [LayoutFarm/Typography commit 5877180c7c5271091379a0eaf9f03ab6ebd256b3](https://github.com/LayoutFarm/Typography/blob/5877180c7c5271091379a0eaf9f03ab6ebd256b3/Demo/Windows/TestFonts/SourceSerifPro-Regular.otf).
- Verified byte-for-byte by SHA-256 against
  [Adobe's original OTF](https://github.com/adobe-fonts/source-serif/blob/fb33c384290783887810dcfcc25132817509cf9f/OTF/SourceSerifPro-Regular.otf),
  release `3.000R`, commit `fb33c384290783887810dcfcc25132817509cf9f`.
- [SourceSerifPro-LICENSE.md](SourceSerifPro-LICENSE.md) is an unchanged copy of
  [Adobe's license at that commit](https://github.com/adobe-fonts/source-serif/blob/fb33c384290783887810dcfcc25132817509cf9f/LICENSE.md),
  including the exact copyright/trademark notice and SIL OFL 1.1 terms.
- CFF1, 1,000 units per em, 1,450 glyphs. No `glyf`, `fvar`, `CFF2`, `SVG `,
  `COLR`, `sbix`, `CBDT`, `CBLC`, `EBDT`, or `EBLC` table.

The embedded copyright notice is:

> © 2014 - 2019 Adobe Systems Incorporated (http://www.adobe.com/), with Reserved Font Name ‘Source’.

The original name table credits Frank Grießhammer. The reserved font name
`Source` and all other metadata are unchanged. Runtime tests must still prove
the actual FontOutlineReader and CFF evaluation results; table inspection alone
does not establish runtime success.

## SourceCodePro-Regular.otf

- Adobe Source Code Pro Regular, version
  `2.030;PS 1.0;hotconv 16.6.51;makeotf.lib2.5.65220`.
- Unmodified original binary, 145,556 bytes.
- SHA-256: `1b87680aa1b825253e65d051755664ade67cd6b842c19cb669a88a0dc57ddae0`.
- Copied from `Demo/Windows/TestFonts/SourceCodePro-Regular.otf` in
  [LayoutFarm/Typography commit 5877180c7c5271091379a0eaf9f03ab6ebd256b3](https://github.com/LayoutFarm/Typography/blob/5877180c7c5271091379a0eaf9f03ab6ebd256b3/Demo/Windows/TestFonts/SourceCodePro-Regular.otf).
- Verified byte-for-byte by SHA-256 against
  [Adobe's original OTF](https://github.com/adobe-fonts/source-code-pro/blob/482adcaccf9777f2850974e08c60e706b1ad9169/OTF/SourceCodePro-Regular.otf),
  release `2.030R-ro/1.050R-it`, commit
  `482adcaccf9777f2850974e08c60e706b1ad9169`.
- [SourceCodePro-LICENSE.txt](SourceCodePro-LICENSE.txt) is an unchanged copy of
  [Adobe's license at the same commit](https://github.com/adobe-fonts/source-code-pro/blob/482adcaccf9777f2850974e08c60e706b1ad9169/LICENSE.txt).
  This font remains under SIL OFL 1.1, including the reserved font name `Source`;
  WhimTex's MIT license does not apply to the font.

The embedded copyright notice is:

> Copyright 2010, 2012 Adobe Systems Incorporated (http://www.adobe.com/), with Reserved Font Name ‘Source’.

The original name table credits Paul D. Hunt and Teo Tuominen. The exact license
file includes Adobe's copyright and trademark notices, the reserved font name,
and all OFL conditions. Redistribution verification relies on that Adobe
release and the identical binary, not the testing-only description in the
Typography demo directory. No tables, glyphs, names, signatures, or license
metadata have been removed or rewritten.

## Expected coverage of Source Code Pro

This is an `OTTO` font with CFF1 outlines and an original `SVG ` table. It has
no `glyf`, `fvar`, `CFF2`, `COLR`, or `sbix` table. This distinction matters:

- Direct OpenFont reader tests can check preview identity, table presence,
  glyph mapping and CFF1 evaluation using this fixed input.
- The conservative WhimTex outline adapter must reject this font because of
  `SVG `, even for characters that have ordinary CFF outlines. It is therefore
  an adapter rejection fixture, not evidence of a successful CFF adapter path.
- Use the verified Source Serif Pro fixture above for the successful CFF
  adapter test. Do not strip `SVG `, relax the production gate, or claim
  adapter success merely because the lower-level reader succeeds.

## Reading the fixtures in tests

Tests may load this absolute fixture path through the public
`FontEngine.LoadFontFace(path, ...)` overload and use the same file path with
the managed reader/adapter. They do not need `SystemFontCatalog` or an OS font
installation. Restore the caller's FontEngine state using the existing test
resource discipline. `OpenFontReader.ReadPreview` and `Read` each dispose their
input stream, so use separate streams.

[manifest.json](manifest.json) records source and license hashes, provenance,
the original name records, table directory, and independent character/glyph
mapping and advance-width facts for `A`, `O`, `S`, `g`, `0`, and `@`.
It describes the input bytes, not a runtime test result. `.gitattributes`
prevents font/license line-ending conversion so their original hashes remain
stable across platforms. Unity and runtime correctness tests were not run
when these fixtures were added.
