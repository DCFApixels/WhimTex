# Typography.OpenFont licenses and attribution

This directory incorporates source from [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography/tree/5877180c7c5271091379a0eaf9f03ab6ebd256b3),
commit `5877180c7c5271091379a0eaf9f03ab6ebd256b3`.
The [file manifest](SOURCE-MANIFEST.json) records the selection and hashes;
[README.md](README.md) describes omissions, local modifications and integration.
Original notices remain in every C# file and are collected verbatim in
[SOURCE-NOTICES.txt](SOURCE-NOTICES.txt). This index does not replace those notices
or relicense third-party code under WhimTex's own MIT license.

| Component / attribution in retained source headers | Included license / notice |
| --- | --- |
| WinterDev; Samuel Carlsson (NRasterizer); Sam Hocevar; Villu Ruusmann; Apache PDFBox/FontBox authors — Apache-2.0 portions | [Apache-2.0.txt](Licenses/Apache-2.0.txt) |
| WinterDev — MIT portions, dates retained in individual files | [MIT.txt](Licenses/MIT.txt), [source attributions](SOURCE-NOTICES.txt) |
| Michael Popoloski, 2015 (SharpFont); joint Michael Popoloski / WinterDev headers, 2015–2016 | [SharpFont-MIT.txt](Licenses/SharpFont-MIT.txt), [source attributions](SOURCE-NOTICES.txt) |
| FreeType Project, David Turner, Robert Wilhelm, Werner Lemberg and contributors; `IGlyphTranslator.cs` | [FTL.TXT](Licenses/FTL.TXT) |
| Adobe Systems Incorporated, Adobe Glyph List and Adobe Glyph List For New Fonts | [Adobe-AGL.txt](Licenses/Adobe-AGL.txt), [Adobe-AGLFN.txt](Licenses/Adobe-AGLFN.txt) |
| Inherited CFF headers: Apache Software Foundation (2014), PDFBox/FontBox (2002–2007), PaDaF / Atos Worldline SAS (2010), Adobe glyph lists, Apple OSXAdapter notice | [PDFBox-LICENSE.txt](Licenses/PDFBox-LICENSE.txt), [PDFBox-NOTICE.txt](Licenses/PDFBox-NOTICE.txt) |

WhimTex is based in part on the work of the FreeType Team through the managed
glyph translator. See [The FreeType Project](https://www.freetype.org/).
No native FreeType binary or OS adapter is included. The complete inherited
PDFBox notices and license bundle are preserved for attribution; mentions of
OSXAdapter, AFM files or CMaps in that bundle do not mean those components are
part of this OpenFont source selection.

The [upstream repository license overview](LICENSE.UPSTREAM.md) is included
unchanged. It describes projects beyond OpenFont, including AFDKO, text
processing, geometry, platform and decompression components. Those descriptions
do not expand this import. License selection here follows the retained source
headers, including the FreeType attribution that is easy to miss if only the
repository-level MIT description is consulted.

## License text provenance

The following files were copied without text changes. Their vendored hashes
are recorded in `SOURCE-MANIFEST.json`:

- `Apache-2.0.txt`: <https://www.apache.org/licenses/LICENSE-2.0.txt>
- `FTL.TXT`: <https://raw.githubusercontent.com/freetype/freetype/VER-2-6-5/docs/FTL.TXT>
  (FreeType 2.6.5 license text; this is provenance for the license, not a claim
  about the exact FreeType source revision originally ported by Typography).
- `SharpFont-MIT.txt`: <https://raw.githubusercontent.com/MikePopoloski/SharpFont/master/LICENSE>
  (license snapshot retrieved 2026-10-16; exact content recorded by hash).
- `PDFBox-LICENSE.txt`: <https://raw.githubusercontent.com/apache/pdfbox/2.0.12/LICENSE.txt>
- `PDFBox-NOTICE.txt`: <https://raw.githubusercontent.com/apache/pdfbox/2.0.12/NOTICE.txt>
  (the 2018 PDFBox license/notice bundle retains the notices copied into the
  upstream CFF headers; this is not a separate PDFBox runtime dependency).
- `Adobe-AGL.txt` and `Adobe-AGLFN.txt`: exact opening license blocks, including
  their original comment prefixes, extracted from the two `AdditionalInfo/Adobe*`
  sources at the Typography pin. Full source headers are also retained.

`MIT.txt` supplies the standard MIT permission and disclaimer for the files
identified as MIT by their original headers. Its attribution preamble is local;
the upstream author/date notices are not replaced or combined into a new
copyright claim.
