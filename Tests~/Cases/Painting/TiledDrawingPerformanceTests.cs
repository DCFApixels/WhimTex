using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Package-owned native oracle supplies the unchanged input path and old bounded
// renderer. New uses the promoted source and actual production native adapter.
public static class TiledDrawingPerformanceTests
{
    const int Side = 8192, PreviewSize = 1024;
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Oracle = typeof(TiledPaintNativeOracle);
    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object value, string name) => value.GetType().GetField(name, F)?.GetValue(value) ?? value.GetType().GetProperty(name, F)?.GetValue(value);
    static void Set(object value, string name, object field) => value.GetType().GetField(name, F).SetValue(value, field);
    static object Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, F).Invoke(value, args);
    public sealed class Sample
    {
        public string path; public int repeat;
        public double setupMs, prepareMs, firstResultMs, drawCompletedMs, commitCompletedMs, pendingRenderMs;
        public double beginSubmissionMs, drawSubmissionMs;
        public long peakTextureBytes;
    }
    public sealed class Report
    {
        public string status, error, unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName;
        public int diameter, repeats, batches, dabs, checks;
        public float spacing;
        public bool prepared;
        public bool reusedSurface;
        public double retainedCurrentSetupMs, retainedRasterSetupMs;
        public long channels, mismatches;
        public string firstMismatch;
        public List<Sample> samples = new List<Sample>();
        public object medians;
        public string scope = "8K native PaintStrokeBuffer versus promoted Raster/native adapter. Same HDR paint, flow, opacity and soft selection. PendingRender is a separate full 1024x1024 fitted bilinear preview, using the old production regional renderer versus the new sparse renderer. Draw, preview and commit are fenced separately; no document/disk history, UI, FX or stamp generation inside timings. Adapter GPU Undo and full editor history are tested separately.";
        public string timing = "Alternate measured path order. First result=Begin+first native input batch+1x1 GPU completion wait. Remaining batches submit without individual fences. Default reusedSurface=false creates fresh sources after a separate warmup per measured repeat. With reusedSurface=true, one warmup stroke and every measured stroke use the same source/pool; empty pixels are restored outside timing between strokes. Default prepared=false includes first-use page allocation. Optional prepared=true applies only to fresh sources and reports prewarming separately. Full affected-region pixels are compared outside timing.";
        public string memory = "Owned texture payload, not physical VRAM: source/pending/prepared pages/frozen mask plus the common 1024x1024 Float preview. Driver, native mesh/CPU/history payloads and the old global idle pool are excluded. Fences are 1x1.";
    }

    public static string Run(int diameter = 2048, float spacing = .005f, int repeats = 3, bool comparePixels = true, bool prepared = false, bool reusedSurface = false)
    {
        var report = new Report { diameter = diameter, spacing = spacing, repeats = repeats, prepared = prepared, reusedSurface = reusedSurface };
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite; Texture2D mask = null;
        Path retainedCurrent = null, retainedRaster = null;
        string path = "Temp/WhimTex/tiled-drawing-integration/performance-" + diameter + "-" + spacing.ToString(CultureInfo.InvariantCulture) + (prepared ? "-prepared" : "") + (reusedSurface ? "-reused" : "") + ".json";
        try
        {
            if (diameter < 1 || diameter > 4096 || spacing <= 0 || !float.IsFinite(spacing) || repeats < 1 || repeats > 7)
                throw new ArgumentOutOfRangeException("Choose size1..4096, positive spacing, repeats1..7.");
            if (SystemInfo.maxTextureSize < Side || !(bool)Product("BrushStrokeCompute").GetProperty("Supported", F).GetValue(null))
                throw new NotSupportedException("Native 8K Half compute reference unavailable.");
            if ((bool)Product("WhimTexProfiler").GetProperty("Recording", F).GetValue(null) ||
                (int)Product("PaintTileTransfer").GetProperty("PendingCount", F).GetValue(null) != 0)
                throw new InvalidOperationException("Finish profiling and pending paint transfers before measuring.");
            GL.sRGBWrite = false;
            mask = (Texture2D)Oracle.GetMethod("SoftMask", F).Invoke(null, null);
            var style = new TiledPaintNativeOracle.Style { Size = diameter, Pencil = diameter == 1, Hdr = true,
                Hardness = .71f, Color = new Color(-.48f, 2f, .9f, .72f), Flow = .62f, Opacity = .76f, Mask = mask };
            var batches = (Vector2[][])Oracle.GetMethod("MakePath", F).Invoke(null, new object[] { diameter, spacing, style.Pencil, true });
            report.batches = batches.Length; foreach (var batch in batches) report.dabs += batch.Length;
            if (reusedSurface)
            {
                long setup = Stopwatch.GetTimestamp(); retainedCurrent = new Path(true, style, batches); retainedCurrent.Fence();
                report.retainedCurrentSetupMs = Elapsed(setup);
                setup = Stopwatch.GetTimestamp(); retainedRaster = new Path(false, style, batches); retainedRaster.Fence();
                report.retainedRasterSetupMs = Elapsed(setup);
                // A real prior stroke leaves capacity in the same source's pool.
                // Restore the empty contents between samples; do not recreate pages.
                retainedCurrent.Measure(batches.Length); retainedRaster.Measure(batches.Length);
                for (int repeat = 0; repeat < repeats; repeat++) for (int order = 0; order < 2; order++)
                {
                    bool native = (repeat + order) % 2 == 0;
                    var runner = native ? retainedCurrent : retainedRaster;
                    runner.Reset(); runner.Fence();
                    var sample = runner.Measure(batches.Length);
                    sample.path = native ? "current" : "raster"; sample.repeat = repeat;
                    report.samples.Add(sample); Save();
                }
            }
            else
            for (int repeat = 0; repeat < repeats; repeat++) for (int order = 0; order < 2; order++)
            {
                bool native = (repeat + order) % 2 == 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    long start = Stopwatch.GetTimestamp();
                    using var runner = new Path(native, style, batches);
                    runner.Fence(); double setup = Elapsed(start);
                    double prepare = 0;
                    if (prepared) { start = Stopwatch.GetTimestamp(); runner.Prepare(); runner.Fence(); prepare = Elapsed(start); }
                    var sample = runner.Measure(batches.Length);
                    sample.path = native ? "current" : "raster"; sample.repeat = repeat;
                    sample.setupMs = setup; sample.prepareMs = prepare;
                    if (pass == 1) { report.samples.Add(sample); Save(); }
                }
            }
            if (comparePixels)
            {
                using var freshCurrent = reusedSurface ? null : new Path(true, style, batches);
                using var freshRaster = reusedSurface ? null : new Path(false, style, batches);
                var current = retainedCurrent ?? freshCurrent; var raster = retainedRaster ?? freshRaster;
                if (!reusedSurface) { current.Measure(batches.Length); raster.Measure(batches.Length); }
                float minX = Side, minY = Side, maxX = 0, maxY = 0;
                foreach (var batch in batches) foreach (var point in batch)
                {
                    TiledPaintNativeOracle.Bounds(style, point / Side, out float x, out float y, out float right, out float top);
                    minX = Mathf.Min(minX, x * Side); minY = Mathf.Min(minY, y * Side);
                    maxX = Mathf.Max(maxX, right * Side); maxY = Mathf.Max(maxY, top * Side);
                }
                int left = Mathf.Max(0, Mathf.FloorToInt(minX) - 2), bottom = Mathf.Max(0, Mathf.FloorToInt(minY) - 2);
                int rightEdge = Mathf.Min(Side, Mathf.CeilToInt(maxX) + 2), topEdge = Mathf.Min(Side, Mathf.CeilToInt(maxY) + 2);
                for (int y = bottom; y < topEdge; y += 512) for (int x = left; x < rightEdge; x += 512)
                {
                    var rect = new RectInt(x, y, Math.Min(512, rightEdge - x), Math.Min(512, topEdge - y));
                    var expected = current.Read(rect); var actual = raster.Read(rect);
                    for (int i = 0; i < expected.Length; i++) for (int c = 0; c < 4; c++)
                    {
                        report.channels++;
                        if (BitConverter.SingleToInt32Bits(expected[i][c]) == BitConverter.SingleToInt32Bits(actual[i][c])) continue;
                        report.mismatches++;
                        report.firstMismatch ??= $"({x + i % rect.width},{y + i / rect.width}) channel {c}: {expected[i][c]:R} != {actual[i][c]:R}";
                    }
                    report.checks++;
                }
            }
            report.medians = new { current = Median("current"), raster = Median("raster") };
            report.status = report.mismatches == 0 ? "passed" : "failed";
            report.checks += report.samples.Count;
        }
        catch (Exception e) { report.status = "failed"; report.error = e.ToString(); }
        finally
        {
            retainedCurrent?.Dispose(); retainedRaster?.Dispose();
            if (mask != null) Object.DestroyImmediate(mask); RenderTexture.active = previous; GL.sRGBWrite = srgb;
        }
        Save(); return Json(report);
        void Save() { Directory.CreateDirectory(PathDirectory(path)); File.WriteAllText(path, Json(report)); }
        object Median(string kind)
        {
            double M(Func<Sample, double> field)
            { var a = new List<double>(); foreach (var sample in report.samples) if (sample.path == kind) a.Add(field(sample)); a.Sort(); return a.Count % 2 == 0 ? (a[a.Count / 2 - 1] + a[a.Count / 2]) * .5 : a[a.Count / 2]; }
            return new { firstResultMs = M(s => s.firstResultMs), drawCompletedMs = M(s => s.drawCompletedMs),
                beginSubmissionMs = M(s => s.beginSubmissionMs), drawSubmissionMs = M(s => s.drawSubmissionMs),
                pendingRenderMs = M(s => s.pendingRenderMs), commitCompletedMs = M(s => s.commitCompletedMs),
                prepareMs = M(s => s.prepareMs), setupMs = M(s => s.setupMs), peakTextureBytes = M(s => s.peakTextureBytes) };
        }
    }

    sealed class Path : IDisposable
    {
        readonly bool native;
        readonly TiledPaintNativeOracle.Style style;
        readonly IDisposable old;
        readonly Action begin, commit, fence; readonly Action<int> add;
        readonly Action<TiledPaintNativeOracle.Sample> memory;
        readonly Func<RectInt, Color[]> read;
        readonly TiledPaintSurface surface;
        readonly TiledPaintSnapshot empty;
        readonly IList[] batches;
        readonly Material view;
        readonly RenderTexture preview, fenceTarget;
        readonly Texture2D fencePixel;
        readonly DrawingLayerBehaviour bridge;
        readonly WhimTexDocument document;
        readonly object context;
        readonly MethodInfo drawPreview = typeof(DrawingLayerBehaviour).GetMethod("DrawBrushPreviewTarget", F);
        readonly MethodInfo draw = typeof(DrawingLayerBehaviour).GetNestedType("PaintBrushRenderer", BindingFlags.NonPublic).GetMethod("Draw", F);
        TiledPaintDelta delta;

        internal Path(bool native, TiledPaintNativeOracle.Style style, Vector2[][] centers)
        {
            this.native = native; this.style = style;
            try
            {
                preview = new RenderTexture(PreviewSize, PreviewSize, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
                    { hideFlags = HideFlags.HideAndDontSave }; if (!preview.Create()) throw new InvalidOperationException("Cannot allocate preview.");
                fenceTarget = TiledPaintNativeOracle.Create(1, 1, false); TiledPaintNativeOracle.Clear(fenceTarget);
                fencePixel = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
                if (native)
                {
                    var type = Oracle.GetNestedType("Dense", BindingFlags.NonPublic);
                    old = (IDisposable)Activator.CreateInstance(type, F, null, new object[] { style, centers }, null);
                    begin = Bind<Action>("Begin"); add = Bind<Action<int>>("Add"); commit = Bind<Action>("Commit"); fence = Bind<Action>("Fence");
                    memory = Bind<Action<TiledPaintNativeOracle.Sample>>("Memory"); read = Bind<Func<RectInt, Color[]>>("Read");
                    T Bind<T>(string name) where T : Delegate => (T)type.GetMethod(name, F).CreateDelegate(typeof(T), old);
                    document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.width = document.height = Side;
                    bridge = new DrawingLayerBehaviour { colorRange = LayerColorRange.HDR, blendRange = LayerBlendRange.HDR };
                    Set(bridge, "paintSurface", Get(old, "source")); Set(bridge, "brushCompositeTransform", ProjectiveMatrix.Identity);
                    Set(bridge, "brushDirtyBounds", new RectInt(0, 0, Side, Side));
                    var settings = Activator.CreateInstance(Product("PaintToolSettings"), true);
                    var dynamics = Get(settings, "dynamics"); Set(dynamics, "opacity", style.Opacity); Set(dynamics, "blend", style.Mode);
                    var p = Call(settings, "GetStrokeParameters", false, (Color?)null); p = Call(p, "WithSelectionMask", style.Mask);
                    Set(bridge, "brushCompositeParameters", p);
                    context = Activator.CreateInstance(Product("LayerRenderContext"), F, null,
                        new object[] { document, null, PreviewSize, PreviewSize, 8f, true, false, null }, null);
                }
                else
                {
                    var shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"), Shader.Find("Hidden/WhimTex/TiledPaintCommit"),
                        AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.compute"));
                    surface = new TiledPaintSurface(new TiledPaintFrame(Side, Side, Side, Side, ProjectiveMatrix.Identity), shaders);
                    empty = surface.AcquireSnapshot();
                    view = new Material(Shader.Find("Hidden/WhimTex/TiledPaintView")) { hideFlags = HideFlags.HideAndDontSave };
                    batches = new IList[centers.Length];
                    for (int i = 0; i < centers.Length; i++)
                    { batches[i] = TiledPaintNativeOracle.NewStamps(); foreach (var point in centers[i]) batches[i].Add(TiledPaintNativeOracle.MakeStamp(point)); }
                }
            }
            catch { Dispose(); throw; }
        }
        internal void Prepare()
        {
            if (native) return;
            int span = Mathf.CeilToInt((style.Size + 4.02f) / 256f) + 1;
            surface.Prepare((span * span + 15) / 16 + 1);
        }
        internal void Reset()
        {
            if (native)
            {
                (Get(old, "stroke") as IDisposable)?.Dispose(); Set(old, "stroke", null);
                Set(bridge, "advancedStroke", null);
                TiledPaintNativeOracle.Clear((RenderTexture)Get(old, "source"));
            }
            else
            {
                surface.Restore(empty); delta?.Dispose(); delta = null;
            }
        }
        internal Sample Measure(int count)
        {
            var sample = new Sample();
            long start = Stopwatch.GetTimestamp();
            if (native) begin(); else surface.Begin(new TiledStrokeSettings(opacity: style.Opacity, mode: style.Mode, hdrBlend: true, selection: style.Mask));
            sample.beginSubmissionMs = Elapsed(start);
            long drawStarted = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                long submitted = Stopwatch.GetTimestamp();
                if (native) add(i);
                else
                {
                    Color color = style.Color; color.a *= style.Flow;
                    draw.Invoke(null, new object[] { null, batches[i], style.Size, style.Hardness, style.Pencil,
                        Enum.ToObject(Product("PencilShape"), 0), color, false, Side, Side, Vector2.one * .5f, false,
                        TextureTransform.Default, false, false, null, null, false, false, true, null, null, null, surface });
                }
                sample.drawSubmissionMs += Elapsed(submitted);
                if (i == 0) { Fence(); sample.firstResultMs = Elapsed(start); }
            }
            Fence(); sample.drawCompletedMs = Elapsed(drawStarted); Memory(sample);
            start = Stopwatch.GetTimestamp();
            if (native)
            {
                var stroke = Get(old, "stroke"); Set(bridge, "advancedStroke", stroke);
                drawPreview.Invoke(bridge, new object[] { preview, context, ProjectiveMatrix.Identity, FilterMode.Bilinear, true, Get(stroke, "touched") });
            }
            else surface.Render(preview, view, ProjectiveMatrix.Identity, FilterMode.Bilinear, false, null, false);
            Fence(preview); sample.pendingRenderMs = Elapsed(start);
            start = Stopwatch.GetTimestamp(); if (native) commit(); else delta = surface.Commit();
            Fence(); sample.commitCompletedMs = Elapsed(start); Memory(sample);
            return sample;
        }
        void Memory(Sample sample)
        {
            long bytes = surface?.AllocatedBytes ?? 0;
            if (native) { var value = new TiledPaintNativeOracle.Sample(); memory(value); bytes = value.TexturePeakBytes; }
            sample.peakTextureBytes = Math.Max(sample.peakTextureBytes, bytes + PreviewSize * (long)PreviewSize * 16);
        }
        internal void Fence(RenderTexture target = null)
        {
            if (target == null && native) { fence(); return; }
            var previous = RenderTexture.active;
            try
            {
                int x = 0, y = 0;
                if (target == null)
                {
                    if (surface.Pending) foreach (var tile in surface.PendingTiles) { target = tile.Texture; x = tile.PhysicalRect.x; y = tile.PhysicalRect.y; }
                    else if (delta != null) foreach (var tile in delta.After.Tiles) { target = tile.Texture; x = tile.PhysicalRect.x; y = tile.PhysicalRect.y; }
                }
                if (target != null)
                {
                    if (target.format == RenderTextureFormat.ARGBFloat) Graphics.Blit(target, fenceTarget, Vector2.zero, Vector2.one * .5f);
                    else Graphics.CopyTexture(target, 0, 0, x, y, 1, 1, fenceTarget, 0, 0, 0, 0);
                }
                RenderTexture.active = fenceTarget; fencePixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            }
            finally { RenderTexture.active = previous; }
        }
        internal Color[] Read(RectInt rect)
        {
            if (native) return read(rect);
            var target = TiledPaintNativeOracle.Create(rect.width, rect.height, false);
            var image = new Texture2D(rect.width, rect.height, TextureFormat.RGBAHalf, false, true);
            var previous = RenderTexture.active;
            try { surface.CopyRegion(rect, target); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, rect.width, rect.height), 0, 0, false); return image.GetPixels(); }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); TiledPaintNativeOracle.Destroy(target); }
        }
        public void Dispose()
        {
            if (bridge != null) { Set(bridge, "paintSurface", null); Set(bridge, "advancedStroke", null); }
            old?.Dispose(); delta?.Dispose(); empty?.Dispose(); surface?.Dispose();
            if (view != null) Object.DestroyImmediate(view); if (document != null) Object.DestroyImmediate(document);
            TiledPaintNativeOracle.Destroy(preview); TiledPaintNativeOracle.Destroy(fenceTarget);
            if (fencePixel != null) Object.DestroyImmediate(fencePixel);
        }
    }
    static string PathDirectory(string value) => System.IO.Path.GetDirectoryName(value);
    static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * (1000d / Stopwatch.Frequency);
    static string Json(object value) => (string)Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert", true)
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
}
