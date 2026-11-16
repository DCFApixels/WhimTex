using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEngine;
using Object = UnityEngine.Object;

// Requires the native-only oracle and simple-tip test adapter in the generated
// comparison bundle. Reflection targets WhimTex/test types, never Unity internals.
public static class TiledPaintNativeComparisonTests
{
    const int Side = 8192, Chunk = 512;
    const string Output = "Temp/WhimTex/tiled-paint-readiness/";
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type Oracle = typeof(TiledPaintNativeOracle);
    static readonly Assembly Product = typeof(DrawingLayerBehaviour).Assembly;

    public sealed class Sample
    {
        public string path;
        public int repeat, preparedPages;
        public double setupCompletedMs, preparationCompletedMs, beginSubmitMs, firstResultMs;
        public double drawCompletedMs, drawSubmitMs, commitCompletedMs, commitSubmitMs;
        public double preparationPlusFirstResultMs;
        public double[] batchSubmitMs;
        public long readyTextureBytes, peakTextureBytes, nativeSourceBytes, nativeStrokeBytes;
    }
    public sealed class PixelProof
    {
        public string status = "not-run", firstMismatch;
        public int x, y, width, height, chunks;
        public long channels, mismatchedChannels;
        public float maximumAbsoluteDifference;
        public bool negativeHdrPreserved, greaterThanOneHdrPreserved;
        public double milliseconds;
    }
    public sealed class Report
    {
        public string status, error, unity, device, graphicsApi, timestampUtc;
        public string message;
        public int checks;
        public List<string> failures = new List<string>();
        public int diameter, repeats, batches, dabs, preparedPages, warmupsPerPathPerRepeat = 1;
        public float spacing;
        public bool comparePixels;
        public List<Sample> samples = new List<Sample>();
        public PixelProof pixels = new PixelProof();
        public object medians;
        public string scope = "Current is the real native PaintStrokeBuffer/PaintBrushRenderer/BrushStrokeCompute, invoked by the frozen test adapter. New is the loaded production Raster backend. Identical 8K simple-tip Brush/Pencil dabs, negative/HDR RGB, soft selection, flow and stroke opacity. This detached comparison does not exercise the Drawing editor adapters. Raster retains one rasterizer with immutable settings and reusable Material/Mesh/buffers; construction is in setup. Per-batch stamp copy/validation, geometry, uploads, submissions and lease lifecycle remain in Append timing. Stamp payload preparation is in setup on both paths.";
        public string timing = "Wall time with synchronous 1x1 GPU readback completion, not GPU timestamps. FirstResult includes Begin plus first Append and its completion wait. Draw includes all Appends, the first-result wait and the final completion wait; remaining batches do not fence individually. Commit has a separate completion wait. One fresh warmup precedes each measured path; measured path order alternates each repeat. Setup and prepared-pool cost are separate and preparationPlusFirstResult exposes just-in-time preparation. No correctness readbacks, UI, composition, export or point generation occur inside timing.";
        public string memory = "Owned GPU texture payload, not physical VRAM. New includes all idle/prepared, pending/source/history pages and frozen mask. Current is its full 512 MiB source plus pending atlas; native global idle pool and CPU/disk Undo are excluded. Driver, Mesh/Material and managed allocations are excluded on both paths. Candidate retains one exact GPU delta. Setup/fence textures and the shared 64-byte mask are test overhead outside reported payload.";
        public string pool = "New preparation knows only brush diameter: one conservative tile footprint packed into 1024 pages plus one spare page; it never inspects the future stroke path. Newly allocated pending pages still clear on first use, which remains inside first-result latency.";
    }

    public static string Run(int diameter = 2048, float spacing = .005f, int repeats = 3, bool comparePixels = true)
    {
        var report = new Report
        {
            diameter = diameter, spacing = spacing, repeats = repeats, comparePixels = comparePixels,
            unity = Application.unityVersion, device = SystemInfo.graphicsDeviceName,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(), timestampUtc = DateTime.UtcNow.ToString("O")
        };
        string path = Output + "native-comparison-" + diameter + "-" + spacing.ToString(CultureInfo.InvariantCulture) + ".json";
        Texture2D mask = null;
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        try
        {
            if (diameter < 1 || diameter > 4096 || !float.IsFinite(spacing) || spacing <= 0 || repeats < 1 || repeats > 9)
                throw new ArgumentOutOfRangeException("Choose diameter 1..4096, positive spacing and repeats 1..9.");
            if (SystemInfo.maxTextureSize < Side || !(bool)ProductType("BrushStrokeCompute").GetProperty("Supported", Static).GetValue(null))
                throw new NotSupportedException("Comparison requires the native 8K RGBAHalf compute commit path.");
            if ((bool)ProductType("WhimTexProfiler").GetProperty("Recording", Static).GetValue(null) ||
                (int)ProductType("PaintTileTransfer").GetProperty("PendingCount", Static).GetValue(null) != 0)
                throw new InvalidOperationException("Finish active paint transfers and profiling before measuring.");
            mask = (Texture2D)Oracle.GetMethod("SoftMask", Static).Invoke(null, null);
            var style = new TiledPaintNativeOracle.Style
            {
                Size = diameter, Hardness = .71f, Opacity = .76f, Flow = .62f,
                Color = new Color(-.48f, 2f, .9f, .72f), Hdr = true, Mask = mask, Pencil = diameter == 1
            };
            var centers = (Vector2[][])Oracle.GetMethod("MakePath", Static).Invoke(null, new object[] { diameter, spacing, style.Pencil, true });
            report.batches = centers.Length;
            foreach (var batch in centers) report.dabs = checked(report.dabs + batch.Length);
            int tileSpan = Mathf.CeilToInt((diameter + 4.02f) / 256f) + 1;
            report.preparedPages = (tileSpan * tileSpan + 15) / 16 + 1;
            GL.sRGBWrite = false;
            using (var fixture = new TiledPaintReadinessTests.ShaderFixture())
            {
                for (int repeat = 0; repeat < repeats; repeat++)
                {
                    for (int order = 0; order < 2; order++)
                    {
                        bool current = (repeat + order) % 2 == 0;
                        for (int pass = 0; pass < 2; pass++)
                        {
                            long started = Stopwatch.GetTimestamp();
                            using (IPath runner = current ? (IPath)new Native(style, centers) :
                                new Candidate(fixture.Shaders, style, centers, report.preparedPages))
                            {
                                runner.Fence(); double setup = Elapsed(started);
                                started = Stopwatch.GetTimestamp(); runner.Prepare(); runner.Fence(); double preparation = Elapsed(started);
                                var sample = Measure(runner, centers.Length);
                                sample.path = current ? "current" : "new-raster"; sample.repeat = repeat;
                                sample.setupCompletedMs = setup; sample.preparationCompletedMs = current ? 0 : preparation;
                                sample.preparedPages = current ? 0 : report.preparedPages;
                                sample.preparationPlusFirstResultMs = sample.preparationCompletedMs + sample.firstResultMs;
                                if (pass == 1) { report.samples.Add(sample); Save(); }
                            }
                        }
                    }
                }
                if (comparePixels) Compare(fixture.Shaders, style, centers, report);
                else report.pixels.status = "skipped-by-request";
            }
            report.medians = new { current = Medians(report.samples, "current"), newRaster = Medians(report.samples, "new-raster") };
            report.status = !comparePixels || report.pixels.status == "passed" ? "passed" : "failed";
            if (report.status == "failed") report.failures.Add(report.pixels.firstMismatch ?? "HDR preservation failed.");
        }
        catch (Exception exception) { report.status = "failed"; report.error = exception.ToString(); report.failures.Add(report.error); }
        finally { if (mask != null) Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        report.checks = report.samples.Count + report.pixels.chunks;
        report.message = report.status == "passed" ? (comparePixels
            ? "Current/new raster measurements and exact affected-region comparison passed."
            : "Current/new raster measurements completed; pixel comparison was explicitly skipped.") : "Current/new raster comparison failed; inspect failures.";
        Save(); return Encode(report);

        void Save() { Directory.CreateDirectory(Output); File.WriteAllText(path, Encode(report)); }
    }

    interface IPath : IDisposable
    {
        void Prepare(); void Begin(); void Add(int index); void Commit(); void Fence();
        void Memory(Sample sample); Color[] Read(RectInt region);
    }
    static Sample Measure(IPath path, int batches)
    {
        var sample = new Sample { batchSubmitMs = new double[batches] };
        path.Memory(sample); sample.readyTextureBytes = sample.peakTextureBytes;
        long first = Stopwatch.GetTimestamp(); path.Begin(); sample.beginSubmitMs = Elapsed(first);
        long draw = Stopwatch.GetTimestamp();
        for (int i = 0; i < batches; i++)
        {
            long submit = Stopwatch.GetTimestamp(); path.Add(i);
            sample.batchSubmitMs[i] = Elapsed(submit); sample.drawSubmitMs += sample.batchSubmitMs[i];
            if (i == 0) { path.Fence(); sample.firstResultMs = Elapsed(first); }
        }
        path.Fence(); sample.drawCompletedMs = Elapsed(draw); path.Memory(sample);
        long commit = Stopwatch.GetTimestamp(); path.Commit(); sample.commitSubmitMs = Elapsed(commit);
        path.Fence(); sample.commitCompletedMs = Elapsed(commit); path.Memory(sample);
        return sample;
    }

    sealed class Native : IPath
    {
        readonly IDisposable owner;
        readonly Action begin, commit, fence;
        readonly Action<int> add;
        readonly Action<TiledPaintNativeOracle.Sample> memory;
        readonly Func<RectInt, Color[]> read;
        bool began;
        public Native(TiledPaintNativeOracle.Style style, Vector2[][] centers)
        {
            Type dense = Oracle.GetNestedType("Dense", BindingFlags.NonPublic);
            owner = (IDisposable)Activator.CreateInstance(dense, Instance, null, new object[] { style, centers }, null);
            try
            {
                begin = Bind<Action>("Begin"); add = Bind<Action<int>>("Add"); commit = Bind<Action>("Commit"); fence = Bind<Action>("Fence");
                memory = Bind<Action<TiledPaintNativeOracle.Sample>>("Memory"); read = Bind<Func<RectInt, Color[]>>("Read");
            }
            catch { owner.Dispose(); throw; }
            T Bind<T>(string method) where T : Delegate => (T)dense.GetMethod(method, Instance).CreateDelegate(typeof(T), owner);
        }
        public void Prepare() { }
        public void Begin() { begin(); began = true; }
        public void Add(int index) => add(index);
        public void Commit() => commit();
        public void Fence() => fence();
        public Color[] Read(RectInt region) => read(region);
        public void Memory(Sample sample)
        {
            sample.nativeSourceBytes = 8192L * 8192 * 8;
            if (began)
            {
                var native = new TiledPaintNativeOracle.Sample(); memory(native);
                sample.nativeStrokeBytes = Math.Max(sample.nativeStrokeBytes, native.StrokeBytes);
            }
            sample.peakTextureBytes = Math.Max(sample.peakTextureBytes, sample.nativeSourceBytes + sample.nativeStrokeBytes);
        }
        public void Dispose() => owner.Dispose();
    }

    sealed class Candidate : IPath
    {
        readonly TiledPaintSurface surface;
        readonly TiledPaintFrame frame;
        readonly TiledPaintBrushSettings brush;
        readonly TiledStrokeSettings stroke;
        readonly TiledPaintBrushStamp[][] batches;
        readonly TiledPaintBrushRasterizer rasterizer;
        readonly int preparedPages;
        readonly RenderTexture fenceTarget;
        readonly Texture2D fenceReader;
        TiledPaintDelta delta;
        public Candidate(TiledPaintShaders shaders, TiledPaintNativeOracle.Style style, Vector2[][] centers, int preparedPages)
        {
            this.preparedPages = preparedPages;
            frame = new TiledPaintFrame(Side, Side, Side, Side, ProjectiveMatrix.Identity);
            Color color = style.Color; color.a *= style.Flow;
            color = (Color)ProductType("HdrUtility").GetMethod("DecodePaintColor", Static).Invoke(null, new object[] { color });
            brush = new TiledPaintBrushSettings(style.Size, style.Hardness, color,
                style.Pencil ? TiledPaintPencilShape.Circle : TiledPaintPencilShape.Brush, standardColor: false, hdrBlend: true);
            stroke = new TiledStrokeSettings(opacity: style.Opacity, mode: style.Mode, standardColor: false, hdrBlend: true, selection: style.Mask);
            batches = new TiledPaintBrushStamp[centers.Length][];
            for (int i = 0; i < centers.Length; i++)
            {
                batches[i] = new TiledPaintBrushStamp[centers[i].Length];
                for (int j = 0; j < centers[i].Length; j++) batches[i][j] = TiledPaintBrushStamp.FromSourcePixels(frame, centers[i][j], brush);
            }
            try
            {
                surface = new TiledPaintSurface(frame, shaders);
                rasterizer = new TiledPaintBrushRasterizer(shaders.Brush, frame, brush);
                fenceTarget = TiledPaintNativeOracle.Create(1, 1, false); TiledPaintNativeOracle.Clear(fenceTarget);
                fenceReader = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            }
            catch { Dispose(); throw; }
        }
        public void Prepare() => surface.Prepare(preparedPages);
        public void Begin() => surface.Begin(stroke);
        public void Add(int index)
        {
            using (var batch = rasterizer.CreateBatch(batches[index])) surface.Append(batch);
        }
        public void Commit() => delta = surface.Commit();
        public void Memory(Sample sample) => sample.peakTextureBytes = Math.Max(sample.peakTextureBytes, surface.AllocatedBytes);
        public void Fence()
        {
            TiledPaintTileView last = default; bool hasTile = false;
            if (surface.Pending) foreach (var tile in surface.PendingTiles) { last = tile; hasTile = true; }
            else if (delta != null) foreach (var tile in delta.After.Tiles) { last = tile; hasTile = true; }
            var previous = RenderTexture.active;
            try
            {
                if (hasTile) Graphics.CopyTexture(last.Texture, 0, 0, last.PhysicalRect.x, last.PhysicalRect.y, 1, 1, fenceTarget, 0, 0, 0, 0);
                RenderTexture.active = fenceTarget; fenceReader.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
        }
        public Color[] Read(RectInt region)
        {
            var target = TiledPaintNativeOracle.Create(region.width, region.height, false);
            var reader = new Texture2D(region.width, region.height, TextureFormat.RGBAHalf, false, true);
            var previous = RenderTexture.active;
            try
            {
                surface.CopyRegion(region, target); RenderTexture.active = target;
                reader.ReadPixels(new Rect(0, 0, region.width, region.height), 0, 0, false); return reader.GetPixels();
            }
            finally { RenderTexture.active = previous; TiledPaintNativeOracle.Destroy(target); Object.DestroyImmediate(reader); }
        }
        public void Dispose()
        {
            delta?.Dispose(); surface?.Dispose(); rasterizer?.Dispose(); TiledPaintNativeOracle.Destroy(fenceTarget);
            if (fenceReader != null) Object.DestroyImmediate(fenceReader);
        }
    }

    static void Compare(TiledPaintShaders shaders, TiledPaintNativeOracle.Style style, Vector2[][] centers, Report report)
    {
        var proof = report.pixels; long started = Stopwatch.GetTimestamp();
        float left = Side, bottom = Side, right = 0, top = 0;
        foreach (var batch in centers) foreach (var center in batch)
        {
            TiledPaintNativeOracle.Bounds(style, center / Side, out float x0, out float y0, out float x1, out float y1);
            left = Mathf.Min(left, x0 * Side); bottom = Mathf.Min(bottom, y0 * Side);
            right = Mathf.Max(right, x1 * Side); top = Mathf.Max(top, y1 * Side);
        }
        proof.x = Mathf.Clamp(Mathf.FloorToInt(left) - 2, 0, Side); proof.y = Mathf.Clamp(Mathf.FloorToInt(bottom) - 2, 0, Side);
        proof.width = Mathf.Clamp(Mathf.CeilToInt(right) + 2, 0, Side) - proof.x;
        proof.height = Mathf.Clamp(Mathf.CeilToInt(top) + 2, 0, Side) - proof.y;
        using (var current = new Native(style, centers))
        using (var candidate = new Candidate(shaders, style, centers, report.preparedPages))
        {
            candidate.Prepare(); current.Begin(); candidate.Begin();
            for (int i = 0; i < centers.Length; i++) { current.Add(i); candidate.Add(i); }
            current.Commit(); candidate.Commit(); current.Fence(); candidate.Fence();
            for (int y = proof.y; y < proof.y + proof.height; y += Chunk)
                for (int x = proof.x; x < proof.x + proof.width; x += Chunk)
                {
                    var region = new RectInt(x, y, Math.Min(Chunk, proof.x + proof.width - x), Math.Min(Chunk, proof.y + proof.height - y));
                    var expected = current.Read(region); var actual = candidate.Read(region); proof.chunks++;
                    if (expected.Length != actual.Length) throw new InvalidOperationException("Region read length differs.");
                    for (int i = 0; i < actual.Length; i++)
                    {
                        var pixel = actual[i];
                        proof.negativeHdrPreserved |= pixel.a > 0 && pixel.r < 0;
                        proof.greaterThanOneHdrPreserved |= pixel.a > 0 && pixel.g > pixel.a;
                        for (int channel = 0; channel < 4; channel++)
                        {
                            float a = expected[i][channel], b = pixel[channel]; proof.channels++;
                            if (!float.IsFinite(a) || !float.IsFinite(b)) throw new InvalidOperationException("Nonfinite comparison output.");
                            proof.maximumAbsoluteDifference = Mathf.Max(proof.maximumAbsoluteDifference, Mathf.Abs(a - b));
                            if (BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b)) continue;
                            proof.mismatchedChannels++;
                            if (proof.firstMismatch == null) proof.firstMismatch = "(" + (x + i % region.width) + "," +
                                (y + i / region.width) + ") channel " + channel + ": current=" + a.ToString("R", CultureInfo.InvariantCulture) +
                                ", new=" + b.ToString("R", CultureInfo.InvariantCulture);
                        }
                    }
                }
        }
        proof.milliseconds = Elapsed(started);
        proof.status = proof.mismatchedChannels == 0 && proof.negativeHdrPreserved && proof.greaterThanOneHdrPreserved ? "passed" : "failed";
    }

    static object Medians(List<Sample> samples, string path)
    {
        double Median(Func<Sample, double> select)
        {
            var values = new List<double>(); foreach (var sample in samples) if (sample.path == path) values.Add(select(sample));
            values.Sort(); int count = values.Count;
            return count % 2 == 0 ? (values[count / 2 - 1] + values[count / 2]) * .5 : values[count / 2];
        }
        return new { firstResultMs = Median(s => s.firstResultMs), drawCompletedMs = Median(s => s.drawCompletedMs),
            commitCompletedMs = Median(s => s.commitCompletedMs), peakTextureBytes = Median(s => s.peakTextureBytes),
            preparationPlusFirstResultMs = Median(s => s.preparationPlusFirstResultMs) };
    }
    static Type ProductType(string name) => Product.GetType("DCFApixels.WhimTex." + name, true);
    static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);
    static string Encode(object value) => (string)Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
}
