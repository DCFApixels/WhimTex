using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Detached fixtures only. Reflection is limited to package-owned drawing methods.
public static class DrawingReadbackFormatTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetField(name, Flags)?.GetValue(target)
        ?? target.GetType().GetProperty(name, Flags)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Flags).SetValue(target, value);
    static object New(string name) => Activator.CreateInstance(typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name), true);

    [Serializable] public sealed class Difference
    {
        public string label;
        public long channels, changed;
        public int maximumByteDelta;
        public int maximumAlphaByteDelta;
        public long changedAlpha, alphaZeroCrossings;
        public double meanByteDelta;
        public float maximumOldOracleError, maximumNewOracleError;
        public double meanOldOracleError, meanNewOracleError;
    }
    [Serializable] public sealed class Sample
    {
        public string path, graphicsFormat;
        public bool warmup;
        public double reserveMs, submitMs, readMs, uploadMs, totalMs;
    }
    [Serializable] public sealed class Report
    {
        public string unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
            api = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString();
        public int size;
        public bool hdr;
        public string phase;
        public string sourceFormat, storageFormat;
        public List<Difference> differences = new List<Difference>();
        public List<Sample> samples = new List<Sample>();
        public List<double> productionSyncMs = new List<double>();
        public int accumulatedStrokes, exactSurfaceComparisons;
    }
    sealed class Fixture : IDisposable
    {
        public readonly DrawingLayerBehaviour layer;
        public readonly Texture2D pixels;
        public readonly RenderTexture surface;
        public readonly int size;
        public readonly bool hdr;
        public Fixture(int size, bool hdr)
        {
            this.size = size; this.hdr = hdr;
            layer = new DrawingLayerBehaviour { colorRange = hdr ? LayerColorRange.HDR : LayerColorRange.Standard,
                blendRange = hdr ? LayerBlendRange.HDR : LayerBlendRange.Standard };
            pixels = new Texture2D(size, size, hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false, hdr)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            Call(layer, "AdoptStoredTexture", pixels);
            surface = (RenderTexture)Call(layer, "EnsurePaintSurface", size, size);
            var old = RenderTexture.active;
            try { RenderTexture.active = surface; GL.Clear(false, true, Color.clear); }
            finally { RenderTexture.active = old; }
        }
        public void Dispose() => Call(layer, "ReleaseTransientResources");
    }
    static string Save(Report report)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/drawing-readback-format"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + report.size + "-" + (report.hdr ? "hdr" : "standard") + "-" + Guid.NewGuid().ToString("N") + ".json");
        Write(report, path);
        return path;
    }
    static void Write(Report report, string path)
    {
        // The Editor also loads a second public JsonConvert via Localization.
        var json = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        File.WriteAllText(path, (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report }));
    }
    static byte[] SurfaceBytes(RenderTexture surface)
    {
        var texture = new Texture2D(surface.width, surface.height, TextureFormat.RGBAHalf, false, true);
        var previous = RenderTexture.active;
        try
        {
            RenderTexture.active = surface;
            texture.ReadPixels(new Rect(0, 0, surface.width, surface.height), 0, 0, false);
            return texture.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
    }
    static void Exact(TestContext t, byte[] expected, byte[] actual, string label)
    {
        t.Equal(expected.Length, actual.Length, label + " length");
        for (int i = 0; i < expected.Length; i++)
            if (expected[i] != actual[i]) throw new InvalidOperationException(label + " differs at byte " + i);
        t.True(true, label + " matches bit for bit");
    }
    static Difference Compare(byte[] before, byte[] after, string label, Color[] oracle = null)
    {
        if (before.Length != after.Length) throw new InvalidOperationException("Readback length mismatch");
        var diff = new Difference { label = label, channels = before.Length };
        long sum = 0;
        for (int i = 0; i < before.Length; i++)
        {
            int delta = Math.Abs(before[i] - after[i]);
            if (delta != 0) diff.changed++;
            sum += delta; diff.maximumByteDelta = Math.Max(diff.maximumByteDelta, delta);
            if (i % 4 == 3)
            {
                diff.maximumAlphaByteDelta = Math.Max(diff.maximumAlphaByteDelta, delta);
                if (delta != 0) diff.changedAlpha++;
                if ((before[i] == 0) != (after[i] == 0)) diff.alphaZeroCrossings++;
            }
            if (oracle == null) continue;
            float ideal = Mathf.Clamp01(oracle[i / 4][i % 4]) * 255;
            float oldError = Mathf.Abs(before[i] - ideal), newError = Mathf.Abs(after[i] - ideal);
            diff.maximumOldOracleError = Mathf.Max(diff.maximumOldOracleError, oldError);
            diff.maximumNewOracleError = Mathf.Max(diff.maximumNewOracleError, newError);
            diff.meanOldOracleError += oldError; diff.meanNewOracleError += newError;
        }
        diff.meanByteDelta = (double)sum / before.Length;
        diff.meanOldOracleError /= before.Length; diff.meanNewOracleError /= before.Length;
        return diff;
    }
    // Retained pre-change sequence: identical shader and storage, always a Half staging RT.
    // The alternative changes only staging format; no async reads, full-image Color[] or Undo.
    static Sample Transfer(Fixture f, bool original, bool warmup = false)
    {
        var timer = Stopwatch.StartNew();
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(f.size, f.size, 0,
            original || f.hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var sample = new Sample { path = original ? "half-staging" : "matching-staging", warmup = warmup,
            graphicsFormat = rt.graphicsFormat.ToString(), reserveMs = timer.Elapsed.TotalMilliseconds };
        try
        {
            double start = timer.Elapsed.TotalMilliseconds;
            Call(f.layer, "BlitStraightSurface", rt, f.hdr ? 3f : 2f);
            sample.submitMs = timer.Elapsed.TotalMilliseconds - start;
            RenderTexture.active = rt;
            start = timer.Elapsed.TotalMilliseconds;
            f.pixels.ReadPixels(new Rect(0, 0, f.size, f.size), 0, 0, false);
            sample.readMs = timer.Elapsed.TotalMilliseconds - start;
            start = timer.Elapsed.TotalMilliseconds;
            f.pixels.Apply(false, false);
            sample.uploadMs = timer.Elapsed.TotalMilliseconds - start;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); }
        sample.totalMs = timer.Elapsed.TotalMilliseconds;
        if (sample.totalMs > 5000) throw new InvalidOperationException("Readback exceeded the bounded test budget; stop the series.");
        return sample;
    }
    static Color[] Oracle(Fixture f)
    {
        var previous = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(f.size, f.size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var texture = new Texture2D(f.size, f.size, TextureFormat.RGBAFloat, false, true);
        try
        {
            Call(f.layer, "BlitStraightSurface", rt, f.hdr ? 3f : 2f);
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, f.size, f.size), 0, 0, false);
            return texture.GetPixels();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); RenderTexture.ReleaseTemporary(rt); }
    }
    static void Seed(Fixture f)
    {
        // Small seed scaled on GPU, including almost-transparent and signed HDR values.
        var previous = RenderTexture.active;
        var seed = new Texture2D(128, 128, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point };
        try
        {
            float[] alphas = { 0, 1e-6f, 5e-6f, 1e-5f, 1.1e-5f, .0001f, .0005f, .001f, .003f, .01f, .1f, .25f, .5f, .75f, .99f, 1 };
            var values = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float a = alphas[y % alphas.Length];
                float r = x / 127f, g = ((x * 17 + y * 31) % 128) / 127f, b = y / 127f;
                if (f.hdr) { r = r * 12 - 2; g = g * 3 - .5f; b *= 50; }
                values[y * 128 + x] = new Color(r * a, g * a, b * a, a);
            }
            seed.SetPixels(values); seed.Apply(); Graphics.Blit(seed, f.surface);
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(seed); }
    }
    static void Accuracy(TestContext t, Report report, bool hdr)
    {
        using var f = new Fixture(128, hdr); Seed(f);
        t.Equal(RenderTextureFormat.ARGBHalf, f.surface.format, "Paint source remains Half");
        var sourceBefore = SurfaceBytes(f.surface);
        Transfer(f, true); var old = f.pixels.GetRawTextureData<byte>().ToArray();
        Transfer(f, false); var matched = f.pixels.GetRawTextureData<byte>().ToArray();
        Call(f.layer, "SyncSurfaceToTexture");
        Exact(t, matched, f.pixels.GetRawTextureData<byte>().ToArray(), "Diagnostic agrees with production sync");
        Exact(t, sourceBefore, SurfaceBytes(f.surface), "Readback leaves working pixels unchanged");
        if (hdr)
        {
            Exact(t, old, matched, "HDR readback unchanged");
            var colors = f.pixels.GetPixels();
            bool negative = false, bright = false;
            foreach (var c in colors) { negative |= c.r < -.1f; bright |= c.b > 10; }
            t.True(negative && bright, "Signed and bright HDR values survive");
            report.differences.Add(Compare(old, matched, "HDR raw bytes"));
        }
        else
        {
            var diff = Compare(old, matched, "SDR alpha/color ramps", Oracle(f));
            report.differences.Add(diff);
            t.True(diff.maximumByteDelta <= 1, "Storage changes by at most one 8-bit step");
            // Different target formats can use different shader arithmetic/conversion
            // precision. The float render is a higher precision reference, not an
            // assertion that every backend implements ideal round-to-nearest UNorm.
            t.True(diff.maximumNewOracleError <= 1f, "Storage stays within one 8-bit step of float conversion: " + diff.maximumNewOracleError);
            t.True(diff.meanNewOracleError <= diff.meanOldOracleError + .01, "Mean conversion accuracy does not materially regress");
            // Verify stored source can recreate a surface; this is the save/reopen boundary.
            Call(f.layer, "InvalidatePaintSurface");
            var restored = (RenderTexture)Call(f.layer, "EnsurePaintSurface", 128, 128);
            t.Equal(RenderTextureFormat.ARGBHalf, restored.format, "Restored drawing surface remains Half");
        }
    }
    static void RepeatedStrokes(TestContext t, Report report, float flow, bool erase)
    {
        const int size = 96;
        using var a = new Fixture(size, false); using var b = new Fixture(size, false);
        if (erase) { Seed(a); Seed(b); }
        var mask = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        try
        {
            var values = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                values[y * size + x] = new Color(x / (float)(size - 1), 0, 0, 1);
            mask.SetPixels(values); mask.Apply();
            var settings = New("PaintToolSettings"); var dynamics = Get(settings, "dynamics");
            Set(settings, "brushColor", new Color(.71f, .29f, .83f, 1));
            Set(settings, "brushSize", 80f); Set(settings, "brushHardness", .2f);
            Set(dynamics, "flow", flow); Set(dynamics, "opacity", .65f);
            var parameters = Call(settings, "GetStrokeParameters", erase, (Color?)null);
            parameters = Call(parameters, "WithSelectionMask", mask);
            for (int stroke = 0; stroke < 48; stroke++)
            {
                var point = new Vector2(.45f + .05f * (stroke % 3), .5f);
                foreach (var f in new[] { a, b })
                {
                    Call(f.layer, "BeginStroke", point);
                    for (int dab = 0; dab < 4; dab++) Call(f.layer, "PaintPoint", point, size, size, parameters);
                    Call(f.layer, "EndStroke");
                }
                Transfer(a, true); Call(b.layer, "SyncSurfaceToTexture");
                Exact(t, SurfaceBytes(a.surface), SurfaceBytes(b.surface), "Accumulation flow=" + flow + " stroke=" + stroke);
                report.exactSurfaceComparisons++;
                var diff = Compare(a.pixels.GetRawTextureData<byte>().ToArray(), b.pixels.GetRawTextureData<byte>().ToArray(),
                    "flow=" + flow + " erase=" + erase + " stroke=" + stroke);
                t.True(diff.maximumByteDelta <= 1, "Almost-transparent strokes do not accumulate storage error");
                report.differences.Add(diff); report.accumulatedStrokes++;
            }
            var alpha = Oracle(b);
            float maxAlpha = 0;
            foreach (var c in alpha) maxAlpha = Mathf.Max(maxAlpha, c.a);
            t.True(maxAlpha > 0, "Almost-transparent stroke fixture produced nonzero pixels");
        }
        finally { Object.DestroyImmediate(mask); }
    }
    public static string RunAccuracy()
    {
        var report = new Report { size = 128 };
        string result = TestContext.Run("Drawing readback format accuracy", t =>
        {
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            var sentinel = RenderTexture.GetTemporary(4, 4);
            try
            {
                RenderTexture.active = sentinel; GL.sRGBWrite = false;
                Accuracy(t, report, false); Accuracy(t, report, true);
                foreach (float flow in new[] { .00001f, .0001f, .001f, .01f }) RepeatedStrokes(t, report, flow, false);
                RepeatedStrokes(t, report, .001f, true);
                t.True(RenderTexture.active == sentinel && !GL.sRGBWrite, "Caller render state preserved");
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; RenderTexture.ReleaseTemporary(sentinel); }
        });
        string path = Save(report);
        return result.Substring(0, result.Length - 1) + ",\"reportPath\":\"" + path.Replace("\\", "/") + "\"}";
    }
    public static string RunTiming(int size, bool hdr)
    {
        var report = new Report { size = size, hdr = hdr };
        string result = TestContext.Run("Drawing readback format paired timing", t =>
        {
            // Large Apply calls retain driver upload scratch until the Editor frame ends.
            // Never run a repeated full-size upload series in one synchronous command.
            t.True(size == 512, "Repeated timing is limited to 512; use RunSingle for 8K");
            var profiler = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler");
            t.True(!(bool)profiler.GetProperty("Recording", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Profiler recording must be idle");
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                using var f = new Fixture(size, hdr); Seed(f);
                report.sourceFormat = f.surface.graphicsFormat.ToString(); report.storageFormat = f.pixels.graphicsFormat.ToString();
                for (int pair = -2; pair < 6; pair++)
                {
                    bool oldFirst = pair % 2 == 0;
                    report.samples.Add(Transfer(f, oldFirst, pair < 0));
                    report.samples.Add(Transfer(f, !oldFirst, pair < 0));
                }
                for (int i = 0; i < 2; i++)
                {
                    var timer = Stopwatch.StartNew(); Call(f.layer, "SyncSurfaceToTexture");
                    report.productionSyncMs.Add(timer.Elapsed.TotalMilliseconds);
                }
                t.Equal(RenderTextureFormat.ARGBHalf, f.surface.format, "8K working surface remains Half");
                t.Equal(hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, f.pixels.format, "Storage format unchanged");
                t.Equal(16, report.samples.Count, "Completed bounded alternating samples");
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        });
        string path = Save(report);
        return result.Substring(0, result.Length - 1) + ",\"reportPath\":\"" + path.Replace("\\", "/") + "\"}";
    }
    public static string RunSingle(int size, bool hdr, bool original)
    {
        var report = new Report { size = size, hdr = hdr, phase = "before-fixture" };
        string path = Save(report);
        string result = TestContext.Run("Single drawing readback: " + (original ? "Half staging" : "matching staging"), t =>
        {
            t.True(size == 8192, "Isolated large transfer size");
            t.True(SystemInfo.graphicsMemorySize >= 7000, "8K memory prerequisite");
            var profiler = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.WhimTexProfiler");
            t.True(!(bool)profiler.GetProperty("Recording", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), "Profiler must be idle");
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                using var f = new Fixture(size, hdr); Seed(f);
                report.sourceFormat = f.surface.graphicsFormat.ToString(); report.storageFormat = f.pixels.graphicsFormat.ToString();
                report.phase = "before-single-transfer"; Write(report, path);
                report.samples.Add(Transfer(f, original));
                report.phase = "transfer-completed"; Write(report, path);
                t.Equal(RenderTextureFormat.ARGBHalf, f.surface.format, "Working source stays Half");
                t.Equal(hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, f.pixels.format, "Storage format unchanged");
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        });
        report.phase = "finished"; Write(report, path);
        return result.Substring(0, result.Length - 1) + ",\"reportPath\":\"" + path.Replace("\\", "/") + "\"}";
    }
}
