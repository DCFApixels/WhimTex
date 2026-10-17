# Typography.OpenFont source integration

Source: [LayoutFarm/Typography](https://github.com/LayoutFarm/Typography/tree/5877180c7c5271091379a0eaf9f03ab6ebd256b3),
commit `5877180c7c5271091379a0eaf9f03ab6ebd256b3`.

`OpenFont/` contains the 84 C# files selected from the pinned
`Typography.OpenFont/Typography.OpenFont.projitems`, except these two entries:

- `TrueTypeInterperter/TrueTypeInterpreter.cs`
- `TrueTypeInterperter/InvalidFontException.cs`

Neither excluded type is referenced by the retained sources. The TrueType
interpreter and its unsafe code are not compiled. The `cvt `, `fpgm`, and `prep`
table readers remain because OpenFontReader references them; this integration
does not execute the font's TrueType hinting programs.

The original `.projitems`, OpenFont README, and repository license overview are
retained as provenance. The `.projitems` is not a Unity build input and still
lists the intentionally omitted entries. `SOURCE-MANIFEST.json` records every
imported file and its original SHA-256; modified C# files also have a vendored
SHA-256. No GlyphLayout, TextServices, renderer, rasterizer, native library,
font binary, Brotli, zlib, or platform integration is included. WOFF readers and
other OpenType table readers remain to preserve OpenFont's internal dependency
closure. Their presence does not make web fonts usable without separately
supplied decompression callbacks.

## Unity assembly

`DCFApixels.WhimTex.Typography.OpenFont` is Editor-only, auto-referenced, uses no
Unity engine references, and disallows unsafe code. Consumers with their own
asmdef must explicitly reference this assembly by name. The upstream namespaces
are unchanged. System.Numerics.Vector2 comes from the Editor's managed framework;
no NuGet package or extra binary is bundled.

## Local source changes

Original copyright/license headers remain intact. Changes are also marked in
the affected files:

- `Typeface.cs`: add `public bool ContainsTable(string tag)`, an allocation-free
  ordinal search of the original table directory. It includes unknown/unparsed
  tags, preserves case and trailing spaces, and returns false for null, a tag
  whose length is not four, or an absent directory. This lets callers reject
  unsupported `fvar`, `CFF2`, `COLR`, `SVG `, and bitmap representations without
  reflection or silently using a default/monochrome outline.
- `Tables.CFF/Type2CharStringParser.cs`: suppress the automatic DEBUG instruction
  dump on an invalid stem sequence. The existing exception is preserved.
- `Tables.CFF/CffEvaluationEngine.cs`: the 20 upstream Type 2 operator stubs now
  throw `NotSupportedException` naming the operator, instead of only writing a
  DEBUG diagnostic and continuing with incomplete contours. Implemented
  operators are unchanged. Callers must discard the entire glyph on failure.
- `Tables.Variations/GVar.cs` and `Typeface_TrimableExtensions.cs`: replace DEBUG
  debugger breaks with diagnostic messages so reading a font cannot request
  an interactive debugger. Parsing and return values are unchanged.

All other imported C# files are byte-for-byte copies of the pinned checkout.

## Integration details and limitations

- `ReadPreview(stream)` and `Read(stream, offset)` dispose their input stream.
  Open a fresh stream for each call. For collections, first check
  `IsFontCollection`, then use `GetMember(i).ActualStreamOffset` in `Read`.
  `MemberCount` is only valid for a collection. `Read` with offset zero does not
  automatically select a collection member.
- Obtain a glyph with `Typeface.GetGlyph(ushort)`. TrueType curves use
  `translator.Read(glyph.GlyphPoints, glyph.EndPoints)` in the
  `Typography.OpenFont` namespace. CFF1 curves use
  `CffEvaluationEngine.Run(translator, glyph.GetCff1GlyphData())` from
  `Typography.OpenFont.CFF`. Default scale 1 preserves font design units;
  pixel scaling belongs to the caller. No hinting is applied by these paths.
- `ContainsTable` reports directory presence, not supported interpretation.
  It is available after a successful read; an unsupported CFF2-only font can
  throw before returning a Typeface. Callers must handle failed/null reads as
  unsupported. Reject unsupported variation, color and bitmap formats in the
  adapter before using returned outlines. The full reader parses ancillary
  tables too; `ReadFlags` is not a reliable way to skip them in this revision.
- CFF2 and applying variable-font axis coordinates are not implemented here.
  The 20 unimplemented CFF Type 2 arithmetic, stack, storage and conditional
  operators throw `NotSupportedException` in both debug and release builds.
  This is an explicit refusal to produce a partial or guessed outline.
  Other upstream format limitations still apply; this is not a complete CFF
  conformance implementation.
- DEBUG builds retain upstream CFF bounds precomputation, which evaluates all
  glyphs during a full read. Cache successfully read typefaces. This reader is
  not a hardened parser for arbitrary hostile font files.

The integration compiled in Unity 6000.7.0a6. The `text-outlines` test profile covers
TTF/composite glyphs, nonzero TTC faces and the static Source Serif Pro CFF fixture,
with native metric comparisons and unsupported-source rejection. This is bounded
runtime coverage, not a complete font-format conformance claim.

## Licenses and notices

See [NOTICE.md](NOTICE.md), [the preserved source headers](SOURCE-NOTICES.txt),
and [the original repository overview](LICENSE.UPSTREAM.md). The overview
describes the entire upstream repository; its references to excluded projects
do not imply those projects are bundled here. The applicable source headers
specify Apache-2.0, MIT, the FreeType Project License, and Adobe BSD terms.
