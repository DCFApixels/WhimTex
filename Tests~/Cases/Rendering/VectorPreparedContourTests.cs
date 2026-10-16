// Support: Tests~/Framework/TestApi.cs. Run in the explicitly selected Test6.6 Editor.
// Owned transient models/textures only; no files, assets, windows or global Undo.
// Reflection targets WhimTex internals only. Each appearance entry has 96 cases.
// This differential oracle preserves the OLD anchor construction, not an old rasterizer.
// Frozen cold-render references are checked separately; this file never regenerates them.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.ThirdParty.Clipper2Lib;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class VectorPreparedContourTests
{
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const int Width = 48, Height = 40;
    const double Precision = 4096d; // Frozen conversion contract, independent of production helpers.
    static Type Own(string name) => typeof(VectorLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static MethodInfo Method(Type type, string name, BindingFlags flags) => type.GetMethod(name, flags)
        ?? throw new MissingMethodException(type.FullName, name);
    static object Call(MethodInfo method, object owner, params object[] args)
    {
        try { return method.Invoke(owner, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { throw error.InnerException; }
    }
    static object Field(object owner, string name) => (owner.GetType().GetField(name, Instance)
        ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner);
    static void Set(VectorLayerBehaviour model, List<Paths64> paths) =>
        Call(Method(typeof(VectorLayerBehaviour), "SetPreparedContours", Instance), model, paths);
    static Array Prepared(VectorLayerBehaviour model) => (Array)typeof(VectorLayerBehaviour)
        .GetProperty("PreparedContours", Instance).GetValue(model);
    static void Release(VectorLayerBehaviour model) => Call(Method(typeof(VectorLayerBehaviour), "ReleaseTransientResources", Instance), model);
    static bool GpuAvailable()
    {
        var shader = Shader.Find("Hidden/WhimTex/VectorPaint");
        return shader != null && shader.isSupported && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);
    }

    public static string Fill() => Appearance(0);
    public static string Stroke() => Appearance(1);
    public static string Dashes() => Appearance(2);
    static string Appearance(int mode)
    {
        if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            return TestContext.Result("skipped", 0, "Prepared contour pixel comparisons require RGBAFloat textures.").ToJson();
        bool gpu = GpuAvailable();
        return TestContext.Run("Prepared contours " + new[] { "Fill", "Stroke", "Dashes" }[mode] +
            ": 96 exact CPU/Prepare comparisons; " + (gpu ? "8 exact GPU pair comparisons" : "GPU subset unavailable (shader/float target)"), t =>
        {
            int cases = 0, nonempty = 0;
            void Check(Data data, int variant, int edge, ProjectiveMatrix matrix, bool clip)
            {
                var old = Model(data, mode, variant, (VectorEdgeMode)edge, true);
                var current = Model(data, mode, variant, (VectorEdgeMode)edge, false);
                try
                {
                    Set(current, data.paths);
                    EmptyStyles(t, current);
                    if (Compare(t, old, current, matrix, clip, gpu && cases % 12 == 0,
                        "case=" + cases + " fixture=" + data.name + " variant=" + variant + " edge=" + edge + " clip=" + clip)) nonempty++;
                    cases++;
                }
                finally { Release(old); Release(current); }
            }
            if (mode == 0)
            {
                for (int fixture = 0; fixture < 6; fixture++)
                for (int matrix = 0; matrix < 4; matrix++)
                for (int edge = 0; edge < 2; edge++)
                foreach (bool clip in new[] { false, true })
                    Check(Input(fixture), matrix, edge, Transform(matrix), clip);
            }
            else
            {
                // Full Cartesian product: 3 positions x 3 joins x 3 caps x AA/Step.
                for (int variant = 0; variant < 27; variant++)
                for (int edge = 0; edge < 2; edge++)
                    Check(Input(0), variant, edge, Transform(variant % 4), variant % 2 != 0);
                for (int fixture = 0; fixture < 6; fixture++)
                for (int matrix = 0; matrix < 3; matrix++)
                for (int edge = 0; edge < 2; edge++)
                    Check(Input(fixture), fixture + matrix * 3, edge, Transform(matrix + 1), (fixture + matrix) % 2 != 0);
                for (int i = 0; i < 6; i++)
                    Check(Input(i % 2 == 0 ? 1 : 5), 26 - i, i % 2, Transform(i % 4), i >= 3);
            }
            t.Equal(96, cases, "bounded appearance matrix");
            t.True(nonempty > 24, "matrix must exercise visible pixels, not only empty equality");
        });
    }

    sealed class Data
    {
        internal string name;
        internal Vector2 reference = new Vector2(73.25f, 59.5f);
        internal List<Paths64> paths;
        internal Data(string name, params Paths64[] paths) { this.name = name; this.paths = new List<Paths64>(paths); }
    }
    static Point64 P(double x, double y) => new Point64((long)Math.Round(x * Precision), (long)Math.Round(y * Precision));
    static Path64 Rect(double x, double y, double w, double h) => new Path64 { P(x, y), P(x + w, y), P(x + w, y + h), P(x, y + h) };
    static Data Input(int index)
    {
        switch (index)
        {
            case 0:
                var hole = Rect(24.125, 19.375, 19.25, 17.5); hole.Reverse();
                return new Data("hole", new Paths64 { Rect(7.125, 6.375, 56.25, 44.5), hole });
            case 1:
                return new Data("negative-subpixel-clip", new Paths64 { new Path64 {
                    P(-13.125, 4.375), P(38.25, -7.625), P(88.75, 27.125), P(48.375, 71.25), P(8.125, 43.375) } });
            case 2:
                var duplicate = Rect(6.125, 8.25, 57.5, 39.125);
                duplicate.Insert(1, duplicate[0]); duplicate.Add(duplicate[0]); duplicate.Add(duplicate[0]);
                return new Data("duplicates-and-seam", new Paths64 { duplicate, new Path64(), new Path64 { P(30, 30) } });
            case 3:
                return new Data("empty-and-degenerate", new Paths64(), new Paths64 {
                    new Path64(), new Path64 { P(8, 9), P(8, 9) }, new Path64 { P(10, 12), P(31, 26) } });
            case 4:
                // +1 fixed-point unit becomes the same physical float after normalization.
                var rounded = new Path64 { new Point64(100000004, 30000000), new Point64(100000005, 30000000),
                    new Point64(350000003, 50000001), new Point64(310000007, 210000003), new Point64(90000001, 190000001) };
                return new Data("normalized-float-rounding", new Paths64 { rounded })
                    { reference = new Vector2(131071.3f, 65533.7f) };
            default:
                // Empty leading slot, overlapping figures, both fill rules, hidden final figure.
                return new Data("ordered-figures", new Paths64(), new Paths64 { Rect(4.25, 6.75, 36, 35) },
                    new Paths64 { Rect(22.125, 16.375, 41, 33), Rect(33.25, 25.5, 12, 10) },
                    new Paths64 { Rect(0, 0, 73, 59) });
        }
    }
    static Data Extreme(bool negative)
    {
        var path = new Path64 { P(9000000, 8999998), P(.3125, .1875), P(8.875, 2.4375), P(8999994, 8999987) };
        if (negative) for (int i = 0; i < path.Count; i++) path[i] = new Point64(-path[i].X, -path[i].Y);
        return new Data(negative ? "negative-cancellation" : "positive-cancellation", new Paths64 { path })
            { reference = new Vector2(1000003.5f, 999983.25f) };
    }
    static ProjectiveMatrix Transform(int index)
    {
        if (index == 0) return ProjectiveMatrix.Identity;
        if (index == 1) return ProjectiveMatrix.Translate(.5, .5) * ProjectiveMatrix.Rotate(23) *
            ProjectiveMatrix.Scale(.83, 1.07) * ProjectiveMatrix.Translate(-.5, -.5);
        if (index == 2) return ProjectiveMatrix.Translate(.98, .013) * ProjectiveMatrix.Scale(-.91, 1.03);
        return new ProjectiveMatrix { m00 = .93, m01 = .11, m02 = -.06, m10 = -.07,
            m11 = 1.02, m12 = .04, m20 = .24, m21 = -.13, m22 = 1 };
    }
    static List<VectorContour> OldContours(Paths64 paths, Vector2 reference)
    {
        var result = new List<VectorContour>();
        foreach (var path in paths)
        {
            var contour = new VectorContour { closed = true };
            foreach (var p in path)
                contour.points.Add(new VectorPoint(new Vector2((float)(p.X / 4096d / reference.x),
                    (float)(p.Y / 4096d / reference.y))) { inTangent = Vector2.zero, outTangent = Vector2.zero });
            result.Add(contour);
        }
        return result;
    }
    static VectorLayerBehaviour Model(Data data, int mode, int variant, VectorEdgeMode edge, bool old)
    {
        var result = new VectorLayerBehaviour { referenceSize = data.reference, edgeMode = edge };
        for (int i = 0; i < data.paths.Count; i++)
        {
            var shape = new VectorShape { fillMode = mode == 0 ? VectorFillMode.Solid : VectorFillMode.None,
                fillColor = new Color(.2f + i * .12f, .7f, .4f, .63f), stroke = mode != 0,
                strokeMode = VectorFillMode.Solid, strokeColor = new Color(.8f, .2f + i * .1f, .5f, .57f),
                strokeWidth = data.reference.x > 200000 ? 2.75f : 2.75f * data.reference.x / 73.25f,
                strokePosition = (VectorStrokePosition)(variant % 3), join = (VectorStrokeJoin)(variant / 3 % 3),
                cap = (VectorStrokeCap)(variant / 9 % 3), miterLimit = variant % 2 == 0 ? 1.25f : 4,
                fillRule = (VectorFillRule)((variant + i) % 2), dashPatternRelative = variant % 2 == 0,
                dashOffset = variant % 2 == 0 ? -.7f : 1.125f, visible = data.name != "ordered-figures" || i != 3 };
            if (mode == 2) shape.dashPattern = variant % 3 == 0 ? new List<float> { .01f, 1.5f } :
                variant % 3 == 1 ? new List<float> { 2, 1, .5f } : new List<float> { 2, 1 };
            // Keep absolute-unit cases bounded even for the large reference-size fixture.
            if (mode == 2 && !shape.dashPatternRelative)
                for (int d = 0; d < shape.dashPattern.Count; d++) shape.dashPattern[d] *= shape.strokeWidth;
            if (old) shape.contours = OldContours(data.paths[i], data.reference);
            result.shapes.Add(shape);
        }
        result.Validate(); return result;
    }
    static void EmptyStyles(TestContext t, VectorLayerBehaviour model)
    {
        foreach (var shape in model.shapes) t.Equal(0, shape.contours.Count, "public style contours stay empty");
    }
    static Texture2D Texture(int width = Width, int height = Height) => new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
    static void SamePixels(TestContext t, Texture2D expected, Texture2D actual, string label)
    {
        t.True(expected.width == actual.width && expected.height == actual.height, label + " dimensions");
        var a = expected.GetRawTextureData<float>(); var b = actual.GetRawTextureData<float>();
        for (int i = 0; i < a.Length; i++)
            if (!float.IsFinite(a[i]) || !float.IsFinite(b[i]) || BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                throw new InvalidOperationException(label + " float=" + i + " expected=" + a[i].ToString("R") + " actual=" + b[i].ToString("R"));
        t.True(true, label + " exact RGBA float bits");
    }
    static bool Compare(TestContext t, VectorLayerBehaviour old, VectorLayerBehaviour current,
        ProjectiveMatrix matrix, bool clip, bool gpu, string label)
    {
        Texture2D a = null, b = null; IList ma = null, mb = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            a = Texture(); b = Texture();
            var render = Method(Own("VectorRasterizer"), "Render", Static);
            Call(render, null, old, a, matrix, clip); Call(render, null, current, b, matrix, clip);
            SamePixels(t, a, b, label + " CPU");
            bool visible = false; var raw = a.GetRawTextureData<float>();
            for (int i = 3; i < raw.Length; i += 4) visible |= raw[i] > 0;
            var prepare = Method(Own("VectorRasterizer"), "Prepare", Static);
            ma = (IList)Call(prepare, null, old, Width, Height, matrix, clip);
            mb = (IList)Call(prepare, null, current, Width, Height, matrix, clip);
            t.Equal(ma.Count, mb.Count, label + " coverage count");
            for (int i = 0; i < ma.Count; i++)
            {
                foreach (string field in new[] { "shapeIndex", "x", "y" })
                    t.Equal((int)Field(ma[i], field), (int)Field(mb[i], field), label + " coverage " + i + " " + field);
                SamePixels(t, (Texture2D)Field(ma[i], "texture"), (Texture2D)Field(mb[i], "texture"), label + " coverage " + i);
            }
            if (gpu)
            {
                GpuPixels(old, ma, matrix, a); GpuPixels(current, mb, matrix, b);
                SamePixels(t, a, b, label + " GPU paint pair");
            }
            t.True(RenderTexture.active == previous && GL.sRGBWrite == srgb, label + " caller render state");
            return visible;
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (ma != null) foreach (IDisposable entry in ma) entry.Dispose();
            if (mb != null) foreach (IDisposable entry in mb) entry.Dispose();
            if (a != null) Object.DestroyImmediate(a); if (b != null) Object.DestroyImmediate(b);
        }
    }
    static void GpuPixels(VectorLayerBehaviour model, IList masks, ProjectiveMatrix matrix, Texture2D read)
    {
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var target = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
        try
        {
            if (!target.Create()) throw new InvalidOperationException("GPU float target allocation failed.");
            Call(Method(Own("VectorPaintRenderer"), "Render", Static), null, model, masks, matrix, target);
            if (RenderTexture.active != previous || GL.sRGBWrite != srgb) throw new InvalidOperationException("GPU paint changed caller render state.");
            RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false); read.Apply(false, false);
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; Object.DestroyImmediate(target); }
    }

    public static string Geometry() => TestContext.Run("Prepared geometry: float paths/fallback, input ownership, original limits and replacement; GPU cache check requires supported float rendering", t =>
    {
        bool roundingWitness = false, subdivisionWitness = false;
        var flatten = Method(Own("VectorContourPreparation"), "Flatten", Static);
        for (int fixture = 0; fixture < 8; fixture++)
        {
            var data = fixture < 6 ? Input(fixture) : Extreme(fixture == 7);
            var old = Model(data, 0, 0, VectorEdgeMode.Antialiased, true);
            var current = Model(data, 0, 0, VectorEdgeMode.Antialiased, false);
            try
            {
                Set(current, data.paths); EmptyStyles(t, current);
                var ready = Prepared(current);
                for (int s = 0; s < ready.Length; s++)
                {
                    var item = ready.GetValue(s);
                    var shortcut = (List<List<Vector2>>)Field(item, "closedPaths");
                    var fallback = (List<VectorContour>)Field(item, "fallbackContours");
                    t.Equal(data.reference, (Vector2)Field(item, "reference"), "prepared reference");
                    if (fixture >= 6) t.True(fallback != null, data.name + " keeps private zero-handle fallback contours");
                    foreach (float tolerance in new[] { .25f, .125f, .015625f, 1f / 4096 })
                    {
                        var expected = Flatten(old.shapes[s].contours, data.reference, tolerance);
                        var actual = fallback == null ? shortcut : Flatten(fallback, data.reference, tolerance);
                        SamePaths(t, expected, actual, data.name + " scalar tolerance=" + tolerance);
                        if (fallback != null)
                        {
                            SamePaths(t, expected, (List<List<Vector2>>)Call(flatten, null, fallback, data.reference, tolerance),
                                data.name + " prepared fallback tolerance=" + tolerance);
                            for (int p = 0; p < expected.Count; p++) subdivisionWitness |= expected[p].Count > shortcut[p].Count;
                        }
                    }
                    for (int p = 0; p < data.paths[s].Count; p++)
                    for (int v = 0; v < data.paths[s][p].Count; v++)
                    {
                        var raw = data.paths[s][p][v]; var point = old.shapes[s].contours[p].points[v];
                        Vector2 recovered = Vector2.Scale(point.position, data.reference);
                        roundingWitness |= recovered.x != (float)(raw.X / Precision) || recovered.y != (float)(raw.Y / Precision);
                    }
                }
            }
            finally { Release(old); Release(current); }
        }
        t.True(roundingWitness, "fixture distinguishes normalized-float roundtrip from direct pixel cast");
        t.True(subdivisionWitness, "extreme fixture really subdivides; duplicate filtering alone is insufficient");
        Limits(t);
        OwnershipAndReplacement(t);
    });
    static List<List<Vector2>> Flatten(List<VectorContour> contours, Vector2 reference, float tolerance)
    {
        var result = new List<List<Vector2>>();
        foreach (var contour in contours) { var path = new List<Vector2>(); VectorGeometry.FlattenContour(contour, reference, tolerance, path); result.Add(path); }
        return result;
    }
    static void SamePaths(TestContext t, List<List<Vector2>> expected, List<List<Vector2>> actual, string label)
    {
        t.Equal(expected.Count, actual.Count, label + " contours");
        for (int p = 0; p < expected.Count; p++)
        {
            t.Equal(expected[p].Count, actual[p].Count, label + " path=" + p + " vertices");
            for (int v = 0; v < expected[p].Count; v++)
                if (!expected[p][v].x.Equals(actual[p][v].x) || !expected[p][v].y.Equals(actual[p][v].y))
                    throw new InvalidOperationException(label + " exact coordinate mismatch path=" + p + " vertex=" + v);
        }
        t.True(true, label + " exact ordered vertices and seam");
    }
    static void Limits(TestContext t)
    {
        t.Equal(65536, VectorGeometry.MaxPoints, "original anchor limit");
        t.Equal(8192, VectorGeometry.MaxContours, "original contour limit");
        foreach (bool anchors in new[] { true, false })
        foreach (int extra in new[] { 0, 1 })
        {
            int limit = anchors ? VectorGeometry.MaxPoints : VectorGeometry.MaxContours;
            var a = new Paths64(); var b = new Paths64();
            if (anchors)
            {
                a.Add(new Path64()); b.Add(new Path64());
                // Deliberate duplicates: budgets count original anchors before deduplication.
                for (int i = 0; i < limit + extra; i++) (i < limit / 2 ? a[0] : b[0]).Add(P(1.25, 2.5));
            }
            else for (int i = 0; i < limit + extra; i++) (i < limit / 2 ? a : b).Add(new Path64());
            var data = new Data("limit", a, b);
            var old = new VectorLayerBehaviour { referenceSize = data.reference };
            var current = new VectorLayerBehaviour { referenceSize = data.reference };
            foreach (var paths in data.paths)
            {
                old.shapes.Add(new VectorShape { contours = OldContours(paths, data.reference) });
                current.shapes.Add(new VectorShape());
            }
            try
            {
                string resource = anchors ? "anchor count" : "contour count";
                string before = Rejection(() => old.Validate()), after = Rejection(() => Set(current, data.paths));
                if (extra == 0) t.True(before == null && after == null, resource + " exact aggregate boundary accepted: " + before + "/" + after);
                else t.True(before != null && after != null && before.Contains(resource) && after.Contains(resource),
                    resource + " aggregate boundary +1 rejected before duplicate/empty filtering: " + before + "/" + after);
            }
            finally { Release(old); Release(current); }
        }
    }
    static string Rejection(Action action)
    {
        try { action(); return null; }
        catch (InvalidOperationException error) { return error.Message; }
    }
    static ulong Hash(VectorLayerBehaviour model) => (ulong)Call(Method(typeof(VectorLayerBehaviour), "ComputeContentHash", Instance), model);
    static void OwnershipAndReplacement(TestContext t)
    {
        var data = Input(0); var current = Model(data, 0, 0, VectorEdgeMode.Antialiased, false);
        var old = Model(data, 0, 0, VectorEdgeMode.Antialiased, true);
        WhimTexDocument document = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            Set(current, data.paths); ulong original = Hash(current);
            data.paths[0][0][0] = P(-200, -200); data.paths[0][1].Clear(); data.paths[0].Clear(); data.paths.Clear();
            t.Equal(original, Hash(current), "caller mutation of all list levels cannot mutate prepared snapshot");
            var item = Prepared(current).GetValue(0);
            SamePaths(t, Flatten(old.shapes[0].contours, old.referenceSize, .125f),
                (List<List<Vector2>>)Field(item, "closedPaths"), "owned snapshot");
            bool pixels = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat);
            if (pixels) Compare(t, old, current, ProjectiveMatrix.Identity, false, false, "owned snapshot");
            bool cache = pixels && GpuAvailable(); int builds = 0;
            if (cache)
            {
                document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
                document.width = Width; document.height = Height; document.outputPrecision = WhimTexOutputPrecision.Float32;
                // ComputeContentHash reads the owner's filter mode and may already bind a shell.
                var layer = current.Owner; layer.transform.tiling = TransformTilingMode.Unbounded; document.layers.Add(layer);
                Warm(document); builds = (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Instance).GetValue(current);
                t.True((bool)Field(current, "rasterValid") && (bool)Field(current, "coverageValid"), "replacement starts with warm caches");
            }
            var replacement = new Data("replacement", new Paths64 { Rect(30.125, 8.25, 31, 27) });
            Set(current, replacement.paths);
            t.True(original != Hash(current), "replacing geometry changes content hash with unchanged empty styles");
            if (cache)
            {
                t.True(!(bool)Field(current, "rasterValid") && !(bool)Field(current, "coverageValid") && Field(current, "coverage") == null,
                    "Set invalidates raster and releases coverage cache");
                Warm(document);
                t.Equal(builds + 1, (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Instance).GetValue(current), "replacement rebuilds coverage once");
                Warm(document);
                t.Equal(builds + 1, (int)typeof(VectorLayerBehaviour).GetProperty("CoverageBuildCount", Instance).GetValue(current), "unchanged prepared geometry reuses coverage");
            }
            var expected = Model(replacement, 0, 0, VectorEdgeMode.Antialiased, true);
            try { if (pixels) Compare(t, expected, current, Transform(3), true, false, "replacement"); }
            finally { Release(expected); }
            var empty = new Data("empty replacement", new Paths64()); Set(current, empty.paths);
            t.Equal(0, ((List<List<Vector2>>)Field(Prepared(current).GetValue(0), "closedPaths")).Count, "replacement clears old paths");
            var emptyOld = Model(empty, 0, 0, VectorEdgeMode.Antialiased, true);
            try { if (pixels) t.True(!Compare(t, emptyOld, current, ProjectiveMatrix.Identity, false, false, "empty replacement"), "empty replacement clears old pixels"); }
            finally { Release(emptyOld); }
            EmptyStyles(t, current);
        }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            if (document != null) Object.DestroyImmediate(document);
            Release(old); Release(current);
        }
    }
    static void Warm(WhimTexDocument document)
    {
        Texture2D image = null;
        try { image = document.ComposeCanvas(); }
        finally { if (image != null) Object.DestroyImmediate(image); }
    }
}
