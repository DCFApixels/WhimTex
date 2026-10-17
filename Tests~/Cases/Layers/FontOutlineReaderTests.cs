using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using WhimTex.Tests;

// Source-font parsing without installing fonts or creating project assets.
public static class FontOutlineReaderTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Size = 512;
    static void VerifyFixture(TestContext t, string path, string hash)
    {
        using var sha = SHA256.Create(); using var file = File.OpenRead(path);
        t.Equal(hash, BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant(), "Immutable OFL input " + Path.GetFileName(path));
    }
    static readonly Type Reader = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.FontOutlineReader", true);
    static readonly Type Placement = typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex.TextGlyphPlacement", true);
    static List<VectorContour> Read(object reader, string path, int face, uint glyph)
    {
        var placement = Activator.CreateInstance(Placement, Hidden, null,
            new object[] { path, face, glyph, Vector2.zero, (float)Size, 1f, false }, null);
        return (List<VectorContour>)Reader.GetMethod("Read", Hidden).Invoke(reader, new[] { placement, (object)(Vector2.one * Size) });
    }
    static Rect CurveBounds(List<VectorContour> contours, bool quadraticControlBox)
    {
        var lo = Vector2.one * float.PositiveInfinity; var hi = Vector2.one * float.NegativeInfinity;
        void Include(Vector2 p)
        {
            p = (p - Vector2.one * .5f) * Size;
            lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p);
        }
        foreach (var contour in contours) for (int i = 0; i < contour.points.Count; i++)
        {
            var a = contour.points[i]; var b = contour.points[(i + 1) % contour.points.Count];
            // TrueType's native glyph metrics use the glyf control box, which can be
            // wider than the curve's actual extrema. Recover the original quadratic
            // control from exact cubic degree elevation for that comparison only.
            if (quadraticControlBox)
            {
                Include(a.position); Include(b.position);
                Vector2 qa = a.position + a.outTangent * 1.5f, qb = b.position + b.inTangent * 1.5f;
                if ((qa - qb).sqrMagnitude < 1e-10f) Include((qa + qb) * .5f);
                continue;
            }
            for (int j = 0; j <= 128; j++)
            {
                float t = j / 128f, u = 1 - t;
                Vector2 p = u * u * u * a.position + 3 * u * u * t * (a.position + a.outTangent) +
                    3 * u * t * t * (b.position + b.inTangent) + t * t * t * b.position;
                Include(p);
            }
        }
        return Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
    }
    public static string Formats()
    {
        var initial = FontEngine.GetFaceInfo(); bool collection = false;
        var checkedFaces = new List<string>();
        string result = TestContext.Run("Original TTF composite, TTC face and CFF cubic outlines", t =>
        {
            try
            {
                var paths = Font.GetPathsToOSFonts() ?? Array.Empty<string>();
                string ttf = paths.FirstOrDefault(p => Path.GetFileName(p).Equals("arial.ttf", StringComparison.OrdinalIgnoreCase)) ??
                    paths.FirstOrDefault(p => Path.GetExtension(p).Equals(".ttf", StringComparison.OrdinalIgnoreCase));
                t.True(ttf != null, "Installed TTF available");
                var cases = new List<(string path, int face)> { (ttf, 0) };
                foreach (string path in paths.Where(p => Path.GetExtension(p).Equals(".ttc", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => Path.GetFileName(p).Equals("msgothic.ttc", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(8))
                {
                    if (FontEngine.LoadFontFace(path, Size, 0) != FontEngineError.Success || (FontEngine.GetFontFaces()?.Length ?? 0) < 2) continue;
                    cases.Add((path, 0)); cases.Add((path, 1)); collection = true; break;
                }
                string cff = Path.GetFullPath("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Fonts/SourceSerifPro-Regular.otf");
                t.True(File.Exists(cff), "Redistributable CFF fixture exists"); cases.Add((cff, 0));
                VerifyFixture(t, cff, "8570d750feabf4ddd4efd26836ba3f5b70188db784baa12aa32d70142c750455");
                var reader = Activator.CreateInstance(Reader, true);
                foreach (var item in cases)
                {
                    t.Equal(FontEngineError.Success, FontEngine.LoadFontFace(item.path, Size, item.face), "Public native reference loads selected face");
                    var info = FontEngine.GetFaceInfo(); checkedFaces.Add(Path.GetFileName(item.path) + "#" + item.face + " " + info.familyName);
                    foreach (uint character in new uint[] { 'O', 'B', 'é', 'g', 'W' })
                    {
                        t.True(FontEngine.TryGetGlyphWithUnicodeValue(character, GlyphLoadFlags.LOAD_NO_HINTING, out var glyph), "Reference glyph " + character);
                        var curves = Read(reader, item.path, item.face, glyph.index);
                        t.True(curves.Count > 0 && curves.TrueForAll(c => c.closed && c.points.Count >= 2), "Closed original contours " + character);
                        Rect bounds = CurveBounds(curves, item.path != cff); var metrics = glyph.metrics;
                        t.Near(metrics.horizontalBearingX, bounds.xMin, 1, "Font design X minimum " + character);
                        t.Near(metrics.horizontalBearingY, bounds.yMax, 1, "Font design Y maximum " + character);
                        t.Near(metrics.width, bounds.width, 1, "Font design width " + character);
                        t.Near(metrics.height, bounds.height, 1, "Font design height " + character);
                        if (character == 'O') t.Equal(2, curves.Count, "O includes its hole");
                        curves[0].points[0].position = Vector2.one * 123;
                        t.True(Read(reader, item.path, item.face, glyph.index)[0].points[0].position.x < 2, "Cached source contours are never exposed for mutation");
                    }
                }
                string svg = Path.GetFullPath("Packages/com.dcfapixels.whimtex/Tests~/Fixtures/Fonts/SourceCodePro-Regular.otf");
                VerifyFixture(t, svg, "1b87680aa1b825253e65d051755664ade67cd6b842c19cb669a88a0dc57ddae0");
                foreach (var input in new[] { (ttf, 0, uint.MaxValue), (ttf, 1000, 1u), (cff + ".missing", 0, 1u), (svg, 0, 1u) })
                {
                    bool rejected = false;
                    try { Read(reader, input.Item1, input.Item2, input.Item3); }
                    catch (TargetInvocationException error) when (error.InnerException is NotSupportedException) { rejected = true; }
                    t.True(rejected, "Invalid source/face/glyph or SVG font rejected instead of guessed outlines");
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(initial.familyName)) FontEngine.LoadFontFace(initial.familyName, initial.styleName, initial.pointSize);
                else FontEngine.UnloadFontFace();
            }
        });
        var report = JsonUtility.FromJson<TestResult>(result);
        if (report.status == "passed" && !collection) { report.status = "skipped"; report.message += "; installed TTC unavailable"; }
        report.message += "; faces: " + string.Join("; ", checkedFaces);
        return report.ToJson();
    }
}
