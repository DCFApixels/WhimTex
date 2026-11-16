using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using Object = UnityEngine.Object;

// Backend isolation: this deliberately measures a fixed tiny raster batch, not
// native brush generation, editor repaint, disk history, or the complete gesture.
public static class TiledDrawingOccupancyBenchmark
{
    const int SourceSide = 8192;
    const string DirectoryPath = "Temp/WhimTex/raster-cycle-audit";

    sealed class Sample
    {
        public int repeat;
        public double acquireSnapshotCpuMs, beginCpuMs, appendCpuMs, commitCpuMs, restoreCpuMs, disposeDeltaCpuMs;
        public double committedPointCpuMs, committedBilinearCpuMs, pendingPointCpuMs, pendingBilinearCpuMs;
        public double gpuDrainMs;
    }

    sealed class Case
    {
        public int hiddenTiles, sourceTiles, pageSize;
        public long initialPoolBytes, peakPoolBytes;
        public double importAndDrainMs, warmupMs;
        public long comparedChannels, differentChannels;
        public string firstDifference;
        public List<Sample> samples = new List<Sample>();
        public object medians;
    }

    sealed class Report
    {
        public string status = "running", error;
        public string scope = "Public sparse backend only; one visible tile, fixed 32px ink batch, hidden tiles outside source frame; warmed same-source pool; CPU submission excludes GPU drain.";
        public string unity = Application.unityVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString();
        public int viewportSize, repeats, maxHiddenTiles;
        public long checks;
        public List<Case> cases = new List<Case>();
    }

    public static string Run(int repeats = 7, int viewportSize = 512, int maxHiddenTiles = 1024)
    {
        if (repeats < 3 || repeats > 25 || (viewportSize != 512 && viewportSize != 1024) ||
            (maxHiddenTiles != 0 && maxHiddenTiles != 64 && maxHiddenTiles != 256 && maxHiddenTiles != 1024))
            throw new ArgumentOutOfRangeException("Use 3..25 repeats, viewport 512/1024, and maximum hidden count 0/64/256/1024.");
        var report = new Report { repeats = repeats, viewportSize = viewportSize, maxHiddenTiles = maxHiddenTiles };
        string path = Path.Combine(DirectoryPath, "occupancy-" + viewportSize + ".json");
        var previous = RenderTexture.active;
        bool oldSrgb = GL.sRGBWrite;
        Texture2D seed = null, fencePixel = null, reader = null;
        RenderTexture target = null, fenceTarget = null;
        Shader stampShader = null;
        Material view = null, ink = null;
        var reference = new Dictionary<string, float[]>();
        try
        {
            GL.sRGBWrite = false;
            seed = new Texture2D(256, 256, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[256 * 256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float alpha = .25f + (x % 13) / 32f;
                pixels[y * 256 + x] = new Color((1.5f + x / 256f) * alpha, -.25f * alpha, y / 256f * alpha, alpha);
            }
            seed.SetPixels(pixels); seed.Apply(false, false);
            target = Create(viewportSize, viewportSize, RenderTextureFormat.ARGBFloat);
            fenceTarget = Create(1, 1, RenderTextureFormat.ARGBFloat);
            fencePixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            reader = new Texture2D(viewportSize, viewportSize, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
            stampShader = ShaderUtil.CreateShaderAsset(StampSource);
            foreach (var message in ShaderUtil.GetShaderMessages(stampShader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            ink = new Material(stampShader) { hideFlags = HideFlags.HideAndDontSave };
            view = new Material(Shader.Find("Hidden/WhimTex/TiledPaintView")) { hideFlags = HideFlags.HideAndDontSave };
            var shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"), Shader.Find("Hidden/WhimTex/TiledPaintCommit"),
                AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/TiledPaintCommit.compute"));
            var frame = new TiledPaintFrame(SourceSide, SourceSide, SourceSide, SourceSide, ProjectiveMatrix.Identity);
            // The view always covers source pixels 0..512, regardless of output resolution.
            var matrix = ProjectiveMatrix.Scale(16, 16);
            var batch = new TinyBatch(ink);
            foreach (int hiddenCount in new[] { 0, 64, 256, 1024 })
            {
                if (hiddenCount > maxHiddenTiles) continue;
                var result = new Case { hiddenTiles = hiddenCount };
                report.cases.Add(result);
                using var surface = new TiledPaintSurface(frame, shaders,
                    new TiledPaintOptions { GpuBudgetBytes = 768L * 1024 * 1024, IdleBudgetBytes = 32L * 1024 * 1024 });
                var imports = new List<TiledPaintImportTile>(hiddenCount + 1)
                    { new TiledPaintImportTile(Vector2Int.zero, seed, new RectInt(0, 0, 256, 256)) };
                for (int i = 0; i < hiddenCount; i++)
                    imports.Add(new TiledPaintImportTile(new Vector2Int(64 + i % 32, 64 + i / 32), seed, new RectInt(0, 0, 256, 256)));
                long start = Stopwatch.GetTimestamp();
                using (surface.ImportTiles(imports)) { }
                surface.Render(target, view, matrix, FilterMode.Point, false, null, false);
                Fence();
                result.importAndDrainMs = Elapsed(start);
                result.sourceTiles = surface.TileCount; result.pageSize = surface.PageSize;
                result.initialPoolBytes = result.peakPoolBytes = surface.AllocatedBytes;
                if (result.sourceTiles != hiddenCount + 1) throw new InvalidOperationException("Incorrect seeded tile count.");
                report.checks++;
                using var initial = surface.AcquireSnapshot();

                start = Stopwatch.GetTimestamp();
                using (var warm = Stroke()) { surface.Restore(initial); }
                foreach (var filter in new[] { FilterMode.Point, FilterMode.Bilinear })
                    surface.Render(target, view, matrix, filter, false, null, false);
                Fence(); result.warmupMs = Elapsed(start);
                result.peakPoolBytes = Math.Max(result.peakPoolBytes, surface.AllocatedBytes);

                for (int repeat = 0; repeat < repeats; repeat++)
                {
                    var sample = new Sample { repeat = repeat };
                    start = Stopwatch.GetTimestamp(); using (surface.AcquireSnapshot()) { }
                    sample.acquireSnapshotCpuMs = Elapsed(start);
                    sample.committedPointCpuMs = Render(FilterMode.Point, "committed-point");
                    sample.committedBilinearCpuMs = Render(FilterMode.Bilinear, "committed-bilinear");
                    start = Stopwatch.GetTimestamp(); surface.Begin(new TiledStrokeSettings(opacity: .65f)); sample.beginCpuMs = Elapsed(start);
                    start = Stopwatch.GetTimestamp(); surface.Append(batch); sample.appendCpuMs = Elapsed(start);
                    Drain();
                    sample.pendingPointCpuMs = Render(FilterMode.Point, "pending-point");
                    sample.pendingBilinearCpuMs = Render(FilterMode.Bilinear, "pending-bilinear");
                    start = Stopwatch.GetTimestamp(); var delta = surface.Commit(); sample.commitCpuMs = Elapsed(start);
                    try
                    {
                        Drain();
                        if (repeat == 0)
                        {
                            surface.Render(target, view, matrix, FilterMode.Point, false, null, false); Identity("after-point");
                            surface.Render(target, view, matrix, FilterMode.Bilinear, false, null, false); Identity("after-bilinear");
                        }
                        result.peakPoolBytes = Math.Max(result.peakPoolBytes, surface.AllocatedBytes);
                        start = Stopwatch.GetTimestamp(); surface.Restore(initial); sample.restoreCpuMs = Elapsed(start);
                    }
                    finally { start = Stopwatch.GetTimestamp(); delta.Dispose(); sample.disposeDeltaCpuMs = Elapsed(start); }
                    result.samples.Add(sample); Save();

                    double Render(FilterMode filter, string state)
                    {
                        long then = Stopwatch.GetTimestamp(); surface.Render(target, view, matrix, filter, false, null, false);
                        double cpu = Elapsed(then); Drain();
                        if (repeat == 0) Identity(state);
                        return cpu;
                    }
                    void Drain() { long then = Stopwatch.GetTimestamp(); Fence(); sample.gpuDrainMs += Elapsed(then); }
                }
                result.medians = new
                {
                    acquireSnapshotCpuMs = Median(s => s.acquireSnapshotCpuMs), beginCpuMs = Median(s => s.beginCpuMs),
                    appendCpuMs = Median(s => s.appendCpuMs), commitCpuMs = Median(s => s.commitCpuMs),
                    restoreCpuMs = Median(s => s.restoreCpuMs), disposeDeltaCpuMs = Median(s => s.disposeDeltaCpuMs),
                    committedPointCpuMs = Median(s => s.committedPointCpuMs), committedBilinearCpuMs = Median(s => s.committedBilinearCpuMs),
                    pendingPointCpuMs = Median(s => s.pendingPointCpuMs), pendingBilinearCpuMs = Median(s => s.pendingBilinearCpuMs),
                    gpuDrainMs = Median(s => s.gpuDrainMs)
                };
                Save();

                TiledPaintDelta Stroke()
                { surface.Begin(new TiledStrokeSettings(opacity: .65f)); surface.Append(batch); return surface.Commit(); }
                double Median(Func<Sample, double> select)
                { var values = result.samples.ConvertAll(s => select(s)); values.Sort(); int n = values.Count; return n % 2 == 0 ? (values[n / 2 - 1] + values[n / 2]) * .5 : values[n / 2]; }
                void Identity(string state)
                {
                    RenderTexture.active = target;
                    reader.ReadPixels(new Rect(0, 0, viewportSize, viewportSize), 0, 0, false);
                    var values = reader.GetRawTextureData<float>();
                    if (!reference.TryGetValue(state, out var expected))
                    {
                        if (state == "committed-point")
                        {
                            bool hdr = false, negative = false, visible = false, transparent = false;
                            for (int i = 0; i < values.Length; i += 4)
                            { hdr |= values[i] > 1; negative |= values[i + 1] < 0; visible |= values[i + 3] > 0; transparent |= values[i + 3] == 0; }
                            if (!hdr || !negative || !visible || !transparent) throw new InvalidOperationException("Baseline view lost HDR, negative channels, visible seed, or transparent surroundings.");
                            report.checks++;
                        }
                        if (state == "pending-point")
                        {
                            var before = reference["committed-point"]; bool changed = false;
                            for (int i = 0; i < values.Length; i++) changed |= BitConverter.SingleToInt32Bits(values[i]) != BitConverter.SingleToInt32Bits(before[i]);
                            if (!changed) throw new InvalidOperationException("Diagnostic batch did not alter the visible preview.");
                            report.checks++;
                        }
                        reference.Add(state, values.ToArray()); report.checks++; return;
                    }
                    for (int i = 0; i < values.Length; i++)
                    {
                        result.comparedChannels++;
                        if (BitConverter.SingleToInt32Bits(values[i]) == BitConverter.SingleToInt32Bits(expected[i])) continue;
                        result.differentChannels++;
                        result.firstDifference ??= state + " channel " + i + ": " + expected[i].ToString("R") + " != " + values[i].ToString("R");
                    }
                    report.checks++;
                }
            }
            report.status = report.cases.Exists(c => c.differentChannels != 0) ? "failed" : "passed";
        }
        catch (Exception exception) { report.status = "failed"; report.error = exception.ToString(); }
        finally
        {
            RenderTexture.active = previous; GL.sRGBWrite = oldSrgb;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (fenceTarget != null) { fenceTarget.Release(); Object.DestroyImmediate(fenceTarget); }
            foreach (var item in new Object[] { seed, fencePixel, reader, view, ink, stampShader }) if (item != null) Object.DestroyImmediate(item);
        }
        Save();
        var failures = new List<string>();
        if (report.error != null) failures.Add(report.error);
        foreach (var result in report.cases) if (result.differentChannels != 0) failures.Add(result.firstDifference);
        return Json(new { report.status, report.checks, failures, message = path });

        void Save() { Directory.CreateDirectory(DirectoryPath); File.WriteAllText(path, Json(report)); }
        void Fence()
        {
            Graphics.Blit(target, fenceTarget); RenderTexture.active = fenceTarget;
            fencePixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        }
    }

    static string Json(object value) => (string)Assembly.Load("Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert")
        .GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
    static RenderTexture Create(int width, int height, RenderTextureFormat format)
    {
        var result = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
        if (result.Create()) return result;
        Object.DestroyImmediate(result); throw new InvalidOperationException("Cannot allocate occupancy diagnostic render target.");
    }

    sealed class TinyBatch : ITiledPaintRasterBatch
    {
        readonly Material material;
        public TinyBatch(Material material) { this.material = material; }
        public int Count => 1;
        public Rect Bounds(int stamp) => new Rect(96f / SourceSide, 96f / SourceSide, 32f / SourceSide, 32f / SourceSide);
        public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
        {
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, 1, 0, 1, -1, 1));
            foreach (var page in pages) foreach (var tile in page.Tiles)
            {
                if (tile.Indices.Count == 0) continue;
                if (tile.Key != Vector2Int.zero) throw new InvalidOperationException("Tiny batch unexpectedly covers another logical tile.");
                var origin = tile.Origin;
                commands.SetRenderTarget(page.Texture); commands.SetViewport(new Rect(origin.x, origin.y, 256, 256));
                commands.EnableScissorRect(new Rect(origin.x + 96, origin.y + 96, 32, 32));
                commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                commands.DisableScissorRect();
            }
        }
    }

    const string StampSource = @"Shader ""Hidden/WhimTex/OccupancyDiagnosticInk"" { SubShader { Pass {
        ZTest Always ZWrite Off Cull Off Blend Off
        HLSLPROGRAM
        #pragma target 4.5
        #pragma vertex Vert
        #pragma fragment Frag
        #include ""UnityCG.cginc""
        float4 Vert(uint id:SV_VertexID):SV_POSITION { float2 uv=float2(id==2?2:0,id==1?2:0); return mul(UNITY_MATRIX_VP,float4(uv,0,1)); }
        float4 Frag():SV_Target { return float4(1,-.125,.375,.5); }
        ENDHLSL
    } } }";
}
