using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using Object = UnityEngine.Object;
using BlendMode = DCFApixels.WhimTex.BlendMode;

// Detached readiness checks. No windows, documents, preferences or existing assets
// are touched. Shader assets live in one owned temporary folder for this run only.
public static class TiledPaintReadinessTests
{
    const string Package = "Packages/com.dcfapixels.whimtex/";
    const string Output = "Temp/WhimTex/tiled-paint-readiness/";
    const int Tile = 256;
    static int assertions;

    [Serializable] public sealed class CaseResult
    {
        public string name, status, error;
        public double milliseconds;
        public long allocatedBytes;
    }
    [Serializable] public sealed class Report
    {
        public string status, scenario, device, message;
        public int passed, failed, checks;
        public List<CaseResult> cases = new List<CaseResult>();
        public List<string> failures = new List<string>();
    }

    public static string Run(string scenario = "core")
    {
        assertions = 0;
        var report = new Report { scenario = scenario, device = SystemInfo.graphicsDeviceType.ToString(), message = "Detached tiled paint readiness: " + scenario };
        var logs = new List<string>();
        Application.LogCallback onLog = (condition, stack, type) =>
        { if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert) logs.Add(type + ": " + condition); };
        Application.logMessageReceived += onLog;
        var previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            if (scenario != "core" && scenario != "dimensions" && scenario != "native" && scenario != "all")
                throw new ArgumentException("Choose core, dimensions, native or all.");
            GL.sRGBWrite = false;
            using (var fixture = new ShaderFixture())
            {
                if (scenario == "core" || scenario == "all") Core(report, fixture.Shaders);
                if (scenario == "dimensions" || scenario == "all") Dimensions(report, fixture.Shaders);
                if (scenario == "native" || scenario == "all") Native(report, fixture.Shaders);
                Case(report, "shader-compiler-diagnostics", () => { fixture.ValidateDiagnostics(); return 0; });
            }
        }
        catch (Exception exception)
        {
            report.failed++;
            report.cases.Add(new CaseResult { name = "fixture", status = "failed", error = exception.ToString() });
            report.failures.Add("fixture: " + exception);
        }
        finally { Application.logMessageReceived -= onLog; RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        Case(report, "runtime-log-diagnostics", () => { Check(logs.Count == 0, string.Join("\n", logs)); return 0; });
        report.checks = assertions;
        report.status = report.failed == 0 ? "passed" : "failed";
        Directory.CreateDirectory(Output);
        // Bind the package's JSON assembly explicitly: other installed editor
        // packages can expose another public JsonConvert with the same full name.
        string json = (string)System.Reflection.Assembly.Load("Newtonsoft.Json")
            .GetType("Newtonsoft.Json.JsonConvert", true).GetMethod("SerializeObject", new[] { typeof(object) })
            .Invoke(null, new object[] { report });
        File.WriteAllText(Output + "readiness-" + scenario + ".json", json);
        return json;
    }

    static void Case(Report report, string name, Func<long> body)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var result = new CaseResult { name = name };
        try { result.allocatedBytes = body(); result.status = "passed"; report.passed++; }
        catch (Exception exception) { result.status = "failed"; result.error = exception.ToString(); report.failed++; report.failures.Add(name + ": " + exception); }
        result.milliseconds = timer.Elapsed.TotalMilliseconds;
        report.cases.Add(result);
    }

    static TiledPaintFrame Frame(int width = 8192, int height = 8192)
        => new TiledPaintFrame(width, height, width, height, ProjectiveMatrix.Identity);
    static void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    static void Equal(byte[] actual, byte[] expected, string message)
    {
        Check(actual.Length == expected.Length, message + " length");
        for (int i = 0; i < actual.Length; i++)
            if (actual[i] != expected[i]) throw new InvalidOperationException(message + " byte " + i + ": " + actual[i] + " != " + expected[i]);
    }
    static void ColorNear(Color actual, Color expected, string message, float tolerance = .002f)
    {
        for (int c = 0; c < 4; c++)
            Check(float.IsFinite(actual[c]) && Mathf.Abs(actual[c] - expected[c]) <= tolerance, message + " channel " + c + ": " + actual + " != " + expected);
    }

    static Texture2D Texture(Color color, bool pattern = false)
    {
        var texture = new Texture2D(Tile, Tile, TextureFormat.RGBAHalf, false, true)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var colors = new Color[Tile * Tile];
        for (int y = 0; y < Tile; y++) for (int x = 0; x < Tile; x++)
            colors[y * Tile + x] = pattern ? new Color((x % 16 - 8) / 16f, 2 + (y % 16) / 16f, ((x + y) % 8) / 16f, .5f) : color;
        texture.SetPixels(colors); texture.Apply(false, false); return texture;
    }
    static RenderTexture Target(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Check(texture.Create(), "Target creation failed."); return texture;
    }
    static void Destroy(RenderTexture texture)
    { if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); } }
    static byte[] Read(RenderTexture texture)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(texture.width, texture.height, TextureFormat.RGBAHalf, false, true);
        try
        {
            RenderTexture.active = texture;
            cpu.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            return cpu.GetRawTextureData<byte>().ToArray();
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
    }
    static byte[] Read(TiledPaintSurface surface, RectInt region)
    {
        var target = Target(region.width, region.height);
        try { surface.CopyRegion(region, target); return Read(target); }
        finally { Destroy(target); }
    }
    static Color Pixel(TiledPaintSurface surface, Vector2Int pixel)
    {
        byte[] bytes = Read(surface, new RectInt(pixel.x, pixel.y, 1, 1));
        var cpu = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
        try { cpu.LoadRawTextureData(bytes); cpu.Apply(false, false); return cpu.GetPixel(0, 0); }
        finally { Object.DestroyImmediate(cpu); }
    }

    // Writes explicit premultiplied pixels, independently of brush coverage. This
    // isolates page ownership, composition, mask mapping and history from geometry.
    sealed class TileBatch : ITiledPaintRasterBatch
    {
        readonly TiledPaintFrame frame;
        readonly Texture2D texture;
        readonly Vector2Int key;
        readonly bool failAfterRecord;
        readonly Action afterRecord;
        public int Count => 1;
        public TileBatch(TiledPaintFrame frame, Texture2D texture, Vector2Int key, bool failAfterRecord = false, Action afterRecord = null)
        { this.frame = frame; this.texture = texture; this.key = key; this.failAfterRecord = failAfterRecord; this.afterRecord = afterRecord; }
        public Rect Bounds(int stamp)
            => new Rect((key.x * Tile + .25f) / frame.SourceWidth, (key.y * Tile + .25f) / frame.SourceHeight,
                (Tile - .5f) / frame.SourceWidth, (Tile - .5f) / frame.SourceHeight);
        public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame suppliedFrame)
        {
            Check(ReferenceEquals(frame, suppliedFrame), "Backend replaced the immutable source frame.");
            foreach (var page in pages) foreach (var tile in page.Tiles)
                if (tile.Key == key && tile.Indices.Count != 0)
                    commands.CopyTexture(texture, 0, 0, 0, 0, Tile, Tile, page.Texture, 0, 0, tile.Origin.x, tile.Origin.y);
            afterRecord?.Invoke();
            if (failAfterRecord) throw new InvalidOperationException("Injected before GPU submission.");
        }
    }
    sealed class InvalidBatch : ITiledPaintRasterBatch
    {
        readonly Rect bounds;
        public int Count => 1;
        public InvalidBatch(Rect bounds) { this.bounds = bounds; }
        public Rect Bounds(int stamp) => bounds;
        public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
            => throw new NotSupportedException("Invalid input reached Record.");
    }
    sealed class LateInvalidBatch : ITiledPaintRasterBatch
    {
        public int Count => 2;
        public Rect Bounds(int stamp) => stamp == 0 ? new Rect(.25f, .25f, .01f, .01f) : new Rect(float.NaN, 0, 1, 1);
        public void Record(CommandBuffer commands, IReadOnlyList<TiledPaintPage> pages, TiledPaintFrame frame)
            => throw new NotSupportedException("Late invalid input reached Record.");
    }
    static TiledPaintDelta Paint(TiledPaintSurface surface, TiledPaintFrame frame, Texture2D texture,
        Vector2Int key, TiledStrokeSettings settings)
    { surface.Begin(settings); surface.Append(new TileBatch(frame, texture, key)); return surface.Commit(); }

    static void Core(Report report, TiledPaintShaders shaders)
    {
        Case(report, "premultiplied-half-import-copy-texture-and-rendertexture", () =>
        {
            var frame = Frame(); var input = Texture(Color.clear, true); var gpu = Target(Tile, Tile);
            try
            {
                Graphics.CopyTexture(input, gpu);
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(new Vector2Int(-1, 2), input, new RectInt(0, 0, Tile, Tile)),
                        new TiledPaintImportTile(new Vector2Int(3, -2), gpu, new RectInt(0, 0, Tile, Tile)) })) { }
                    Equal(Read(surface, new RectInt(-Tile, 2 * Tile, Tile, Tile)), input.GetRawTextureData<byte>().ToArray(), "Texture2D import");
                    Equal(Read(surface, new RectInt(3 * Tile, -2 * Tile, Tile, Tile)), input.GetRawTextureData<byte>().ToArray(), "RenderTexture import");
                    ColorNear(Pixel(surface, new Vector2Int(9000, 9000)), Color.clear, "Missing tile is transparent", 0);
                    using (var snapshot = surface.AcquireSnapshot())
                    {
                        Check(snapshot.Count == 2, "Unexpected snapshot count.");
                        Check(snapshot.TryGetTile(new Vector2Int(-1, 2), out var view), "Negative tile missing.");
                        Check(view.PhysicalRect.width == Tile && view.PhysicalRect.height == Tile, "Invalid physical tile view.");
                    }
                    surface.Validate(); return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(input); Destroy(gpu); }
        });

        Case(report, "ten-retained-deltas-exact-undo-redo-and-cancel", () =>
        {
            var frame = Frame(); var deltas = new List<TiledPaintDelta>(); var expected = new List<byte[]>();
            using (var surface = new TiledPaintSurface(frame, shaders))
            try
            {
                var region = new RectInt(0, 0, 16, 16); expected.Add(Read(surface, region));
                for (int i = 0; i < 10; i++)
                {
                    var input = Texture(new Color(i / 16f, (10 - i) / 16f, 2 + i / 8f, 1));
                    try { deltas.Add(Paint(surface, frame, input, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))); }
                    finally { Object.DestroyImmediate(input); }
                    Check(deltas[i].ChangedTiles.Contains(Vector2Int.zero), "Delta omitted changed key.");
                    expected.Add(Read(surface, region));
                }
                for (int i = 9; i >= 0; i--) { surface.Restore(deltas[i].Before); Equal(Read(surface, region), expected[i], "Undo " + i); }
                for (int i = 0; i < 10; i++) { surface.Restore(deltas[i].After); Equal(Read(surface, region), expected[i + 1], "Redo " + i); }
                var cancelled = Texture(new Color(4, -.5f, 2, 1));
                try
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1)); surface.Append(new TileBatch(frame, cancelled, Vector2Int.zero));
                    Check(surface.Pending, "Append did not open pending state."); surface.Cancel();
                    Check(!surface.Pending, "Cancel left pending state."); Equal(Read(surface, region), expected[10], "Cancel altered committed content");
                }
                finally { Object.DestroyImmediate(cancelled); }
                surface.Validate(); return surface.AllocatedBytes;
            }
            finally { foreach (var delta in deltas) delta.Dispose(); }
        });

        Case(report, "record-failure-is-atomic-and-surface-reusable", () =>
        {
            var frame = Frame(); var input = Texture(new Color(1, 2, .5f, 1));
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (Paint(surface, frame, input, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))) { }
                    byte[] before = Read(surface, new RectInt(-Tile, 0, 2 * Tile, Tile));
                    surface.Begin(new TiledStrokeSettings(opacity: 1));
                    Throws<InvalidOperationException>(() => surface.Append(new TileBatch(frame, input, new Vector2Int(-1, 0), true)));
                    Check(!surface.Pending, "Record failure did not cancel pending resources.");
                    Equal(Read(surface, new RectInt(-Tile, 0, 2 * Tile, Tile)), before, "Failed batch wrote pixels");
                    using (Paint(surface, frame, input, new Vector2Int(-1, 0), new TiledStrokeSettings(opacity: 1))) { }
                    ColorNear(Pixel(surface, new Vector2Int(-1, 1)), new Color(1, 2, .5f, 1), "Recovery stroke");
                    surface.Validate(); return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(input); }
        });

        Case(report, "invalid-bounds-rejected-before-record", () =>
        {
            using (var surface = new TiledPaintSurface(Frame(), shaders))
            {
                surface.Begin(new TiledStrokeSettings(opacity: 1));
                Throws<ArgumentException>(() => surface.Append(new InvalidBatch(new Rect(float.NaN, 0, 1, 1))));
                surface.Cancel(); Check(surface.TileCount == 0, "Invalid batch changed source.");
                surface.Validate(); return surface.AllocatedBytes;
            }
        });

        Case(report, "budget-failure-preserves-source", () =>
        {
            var options = new TiledPaintOptions { GpuBudgetBytes = 16L * 1024 * 1024, IdleBudgetBytes = 0 };
            using (var surface = new TiledPaintSurface(Frame(), shaders, options))
            {
                surface.Begin(new TiledStrokeSettings(opacity: 1));
                Throws<InvalidOperationException>(() => surface.Append(new InvalidBatch(new Rect(0, 0, 2304f / 8192, 1024f / 8192))));
                Check(!surface.Pending, "Allocation failure left a pending stroke.");
                Check(surface.TileCount == 0, "Allocation failure changed source.");
                Check(surface.AllocatedBytes <= options.GpuBudgetBytes, "Budget exceeded.");
                surface.Validate(); return surface.AllocatedBytes;
            }
        });

        Case(report, "mask-hdr-and-outside-canvas", () =>
        {
            var frame = Frame(); var input = Texture(new Color(-.5f, 2, .25f, .5f)); var mask = Texture(new Color(.25f, .25f, .25f, 1));
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    var settings = new TiledStrokeSettings(opacity: .5f, selection: mask);
                    using (Paint(surface, frame, input, Vector2Int.zero, settings)) { }
                    ColorNear(Pixel(surface, new Vector2Int(8, 8)), new Color(-.0625f, .25f, .03125f, .0625f), "Masked HDR composition");
                    using (Paint(surface, frame, input, new Vector2Int(-1, 0), settings)) { }
                    ColorNear(Pixel(surface, new Vector2Int(-8, 8)), Color.clear, "Non-wrapped mask outside canvas", 0);
                    using (Paint(surface, frame, input, new Vector2Int(-1, 0), new TiledStrokeSettings(opacity: 1))) { }
                    ColorNear(Pixel(surface, new Vector2Int(-8, 8)), new Color(-.5f, 2, .25f, .5f), "Unbounded source without mask");
                    surface.Validate(); return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(input); Object.DestroyImmediate(mask); }
        });

        Case(report, "released-source-resource-is-reported", () =>
        {
            var frame = Frame(); var input = Texture(Color.white);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (Paint(surface, frame, input, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))) { }
                    using (var snapshot = surface.AcquireSnapshot())
                    { var tile = snapshot.Tiles.First(); tile.Texture.Release(); Throws<TiledPaintResourceLostException>(() => surface.Validate()); }
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(Vector2Int.zero, input, new RectInt(0, 0, Tile, Tile)) }, true)) { }
                    surface.Validate(); ColorNear(Pixel(surface, Vector2Int.zero), Color.white, "Full replacement did not recover lost source");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(input); }
        });

        Case(report, "main-thread-and-disposed-guards", () =>
        {
            var surface = new TiledPaintSurface(Frame(), shaders);
            try
            {
                Exception error = Task.Run(() => { try { surface.Validate(); return null; } catch (Exception e) { return e; } }).GetAwaiter().GetResult();
                Check(error is InvalidOperationException, "Worker thread was not rejected before Unity access.");
            }
            finally { surface.Dispose(); }
            Throws<ObjectDisposedException>(() => surface.Validate());
            Throws<ObjectDisposedException>(() => surface.Begin(new TiledStrokeSettings(opacity: 1)));
            surface.Dispose(); return 0;
        });

        Case(report, "caller-render-state-survives-all-operations", () =>
        {
            var frame = Frame(); var input = Texture(Color.white); var sentinel = Target(2, 2); var output = Target(4, 4);
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    Action<Action> state = action =>
                    {
                        RenderTexture.active = sentinel; GL.sRGBWrite = true;
                        action();
                        Check(RenderTexture.active == sentinel && GL.sRGBWrite, "Caller render state changed.");
                    };
                    state(() => surface.Prepare(2));
                    state(() => surface.Begin(new TiledStrokeSettings(opacity: 1)));
                    state(() => surface.Append(new TileBatch(frame, input, Vector2Int.zero)));
                    state(() => surface.CopyRegion(new RectInt(0, 0, 4, 4), output, true));
                    TiledPaintDelta delta = null;
                    try
                    {
                        state(() => delta = surface.Commit());
                        state(() => surface.CopyRegion(new RectInt(0, 0, 4, 4), output));
                        state(() => surface.Restore(delta.Before));
                        state(() => surface.Restore(delta.After));
                        state(() => surface.Begin(new TiledStrokeSettings(opacity: 1)));
                        state(() => surface.Cancel());
                    }
                    finally { delta?.Dispose(); }
                    return surface.AllocatedBytes;
                }
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; Object.DestroyImmediate(input); Destroy(sentinel); Destroy(output); }
        });

        Case(report, "replace-import-retained-snapshot-lifetime", () =>
        {
            var frame = Frame(); var first = Texture(new Color(2, -.25f, 1, 1)); var second = Texture(new Color(.5f, 3, 2, 1));
            TiledPaintSnapshot retained = null;
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(Vector2Int.zero, first, new RectInt(0, 0, Tile, Tile)) })) { }
                    retained = surface.AcquireSnapshot();
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(new Vector2Int(2, 1), second, new RectInt(0, 0, Tile, Tile)) }, true)) { }
                    ColorNear(Pixel(surface, Vector2Int.zero), Color.clear, "Replace left old source", 0);
                    surface.Restore(retained);
                    ColorNear(Pixel(surface, Vector2Int.zero), new Color(2, -.25f, 1, 1), "Snapshot lost replaced source");
                }
                // An external snapshot owns its pages even after the surface closes.
                Check(retained.TryGetTile(Vector2Int.zero, out var view) && view.Texture.IsCreated(), "Surface disposal destroyed retained history.");
                return 0;
            }
            finally { retained?.Dispose(); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        });

        Case(report, "channel-write-preserves-zero-alpha-straight-color", () =>
        {
            var frame = Frame(); var ink = Texture(new Color(.5f, .25f, .125f, .5f)); var straight = Texture(new Color(2, 3, 4, 0));
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    // Only red and alpha change; hidden green/blue come from the explicit straight source.
                    using (Paint(surface, frame, ink, Vector2Int.zero,
                        new TiledStrokeSettings(opacity: 1, writeChannels: 9, originalStraight: straight))) { }
                    ColorNear(Pixel(surface, new Vector2Int(8, 8)), new Color(.5f, 1.5f, 2, .5f), "Zero-alpha straight fallback");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); Object.DestroyImmediate(straight); }
        });

        Case(report, "pending-preview-odd-region-matches-commit", () =>
        {
            var projective = ProjectiveMatrix.Translate(1024, 512) * ProjectiveMatrix.Scale(8, 4);
            projective.m20=.0005;projective.m21=-.00025;
            var frame = new TiledPaintFrame(333, 257, 8192, 4096, projective);
            var ink = Texture(new Color(-.5f, 2, .125f, .5f)); var baseColor = Texture(new Color(.25f, .125f, .5f, .5f));
            var mask = Texture(Color.clear);
            var maskPixels = mask.GetPixels();
            for (int y = 0; y < Tile; y++) for (int x = 0; x < Tile; x++) maskPixels[y * Tile + x] = new Color(x / 255f, 0, 0, 1);
            mask.SetPixels(maskPixels); mask.Apply(false, false);
            var region = new RectInt(-73, 19, 257, 193); var preview = Target(region.width, region.height);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(Vector2Int.zero, baseColor, new RectInt(0, 0, Tile, Tile)),
                        new TiledPaintImportTile(new Vector2Int(-1, 0), baseColor, new RectInt(0, 0, Tile, Tile)) })) { }
                    using (var snapshot = surface.AcquireSnapshot())
                    {
                        byte[] before = Read(surface, region);
                        surface.Begin(new TiledStrokeSettings(opacity: .625f, selection: mask, writeChannels: 13));
                        surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                        surface.Append(new TileBatch(frame, ink, new Vector2Int(-1, 0)));
                        long allocated = surface.AllocatedBytes; int count = surface.TileCount;
                        surface.CopyRegion(region, preview, true); byte[] pending = Read(preview);
                        Check(surface.Pending && surface.TileCount == count && surface.AllocatedBytes == allocated, "Preview changed ownership or committed source.");
                        Equal(Read(surface, region), before, "Preview changed committed pixels");
                        Check(!pending.SequenceEqual(before), "Preview omitted the pending stroke.");
                        using (surface.Commit()) { }
                        Equal(Read(surface, region), pending, "Pending preview differs from commit");
                        surface.Restore(snapshot); Equal(Read(surface, region), before, "Preview damaged retained history");
                    }
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); Object.DestroyImmediate(baseColor); Object.DestroyImmediate(mask); Destroy(preview); }
        });

        Case(report, "commit-budget-failure-preserves-existing-source", () =>
        {
            var frame = Frame(); var initial = Texture(new Color(.25f, .5f, 2, 1)); var ink = Texture(new Color(4, -.5f, 1, 1));
            try
            {
                var options = new TiledPaintOptions { GpuBudgetBytes = 16L * 1024 * 1024, IdleBudgetBytes = 0 };
                using (var surface = new TiledPaintSurface(frame, shaders, options))
                {
                    using (surface.ImportTiles(new[] { new TiledPaintImportTile(Vector2Int.zero, initial, new RectInt(0, 0, Tile, Tile)) })) { }
                    byte[] before = Read(surface, new RectInt(0, 0, 16, 16));
                    surface.Begin(new TiledStrokeSettings(opacity: 1)); surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                    Throws<InvalidOperationException>(() => { using (surface.Commit()) { } });
                    Check(!surface.Pending, "Failed commit left pending state.");
                    Equal(Read(surface, new RectInt(0, 0, 16, 16)), before, "Failed commit changed existing source");
                    surface.Validate(); return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(initial); Object.DestroyImmediate(ink); }
        });

        Case(report, "stroke-freezes-selection-and-straight-inputs", () =>
        {
            var frame = Frame(); var ink = Texture(new Color(.5f, .25f, .125f, .5f));
            var mask = Texture(new Color(.5f, .5f, .5f, 1)); var straight = Texture(new Color(2, 3, 4, 0));
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1, writeChannels: 9, selection: mask, originalStraight: straight));
                    Object.DestroyImmediate(mask); mask = null; Object.DestroyImmediate(straight); straight = null;
                    surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                    using (surface.Commit()) { }
                    ColorNear(Pixel(surface, new Vector2Int(8, 8)), new Color(.25f, .75f, 1, .25f), "Frozen inputs changed after their owners disposed them");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); Object.DestroyImmediate(mask); Object.DestroyImmediate(straight); }
        });

        Case(report, "late-invalid-bounds-preserve-prior-pending-batch", () =>
        {
            var frame = Frame(); var ink = Texture(new Color(2, .5f, -.25f, 1));
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1)); surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                    long memory = surface.AllocatedBytes;
                    Throws<ArgumentException>(() => surface.Append(new LateInvalidBatch()));
                    Check(surface.Pending && surface.AllocatedBytes == memory, "Late validation partially mutated or cancelled the existing stroke.");
                    using (surface.Commit()) { }
                    ColorNear(Pixel(surface, Vector2Int.zero), new Color(2, .5f, -.25f, 1), "Valid earlier batch was lost");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); }
        });

        Case(report, "history-only-texture-cannot-be-copy-target", () =>
        {
            var frame = Frame(); var ink = Texture(Color.white);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (var delta = Paint(surface, frame, ink, Vector2Int.zero, new TiledStrokeSettings(opacity: 1)))
                    {
                        var historyTexture = delta.After.Tiles.First().Texture;
                        surface.Restore(delta.Before);
                        byte[] before = Read(historyTexture);
                        var region = new RectInt(0, 0, historyTexture.width, historyTexture.height);
                        Throws<ArgumentException>(() => surface.CopyRegion(region, historyTexture));
                        Throws<ArgumentException>(() => surface.CopyRegion(region, historyTexture, true));
                        Equal(Read(historyTexture), before, "Rejected target alias damaged history");
                        surface.Restore(delta.After); ColorNear(Pixel(surface, Vector2Int.zero), Color.white, "History restore after rejected alias");
                    }
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); }
        });

        Case(report, "record-cannot-reenter-surface-cancel", () =>
        {
            var frame = Frame(); var ink = Texture(Color.white);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1));
                    Throws<InvalidOperationException>(() => surface.Append(new TileBatch(frame, ink, Vector2Int.zero, afterRecord: surface.Cancel)));
                    Check(!surface.Pending && surface.TileCount == 0, "Reentrant callback left partial content or pending resources.");
                    surface.Validate();
                    using (Paint(surface, frame, ink, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))) { }
                    ColorNear(Pixel(surface, Vector2Int.zero), Color.white, "Surface could not recover after rejected reentry");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); }
        });

        Case(report, "rotated-frame-selection-uses-canvas-coordinates", () =>
        {
            var frame = new TiledPaintFrame(8192, 4096, 8192, 4096,
                ProjectiveMatrix.Translate(8192, 4096) * ProjectiveMatrix.Scale(-1, -1));
            var ink = Texture(new Color(-.25f, 2, .5f, .5f)); var mask = Texture(Color.clear);
            var pixels = mask.GetPixels();
            for (int y = 0; y < Tile; y++) for (int x = 0; x < Tile; x++) pixels[y * Tile + x] = x < 128 && y < 128 ? Color.white : Color.clear;
            mask.SetPixels(pixels); mask.Apply(false, false);
            var preview = Target(32, 32);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1, selection: mask));
                    surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                    surface.Append(new TileBatch(frame, ink, new Vector2Int(31, 15)));
                    var region = new RectInt(31 * Tile + 16, 15 * Tile + 16, 32, 32);
                    surface.CopyRegion(region, preview, true); byte[] pending = Read(preview);
                    using (surface.Commit()) { }
                    Equal(Read(surface, region), pending, "Rotated masked preview differs from commit");
                    ColorNear(Pixel(surface, new Vector2Int(128, 128)), Color.clear, "Rotated source origin should be outside selected canvas quadrant", 0);
                    ColorNear(Pixel(surface, new Vector2Int(8064, 3968)), new Color(-.25f, 2, .5f, .5f), "Rotated source end should map into selected canvas quadrant");
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); Object.DestroyImmediate(mask); Destroy(preview); }
        });

        Case(report, "8k-r8-selection-frozen-lifecycle-and-sparse-preview", () =>
        {
            const long maskBytes = 8192L * 8192;
            var frame = Frame(); var ink = Texture(new Color(-.5f, 2, .25f, .5f));
            var mask = new Texture2D(8192, 8192, TextureFormat.R8, false, true)
            { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var inside = Target(1, 1); var outside = Target(1, 1);
            try
            {
                var data = mask.GetRawTextureData<byte>();
                for (int i = 0; i < data.Length; i++) data[i] = 128;
                mask.Apply(false, false);
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    surface.Begin(new TiledStrokeSettings(opacity: 1, selection: mask));
                    Check(surface.AllocatedBytes >= maskBytes, "The frozen 8K R8 mask is missing from GPU accounting.");
                    Object.DestroyImmediate(mask); mask = null;
                    surface.Append(new TileBatch(frame, ink, Vector2Int.zero));
                    surface.Append(new TileBatch(frame, ink, new Vector2Int(-1, 0)));
                    long pendingBytes = surface.AllocatedBytes;
                    Check(pendingBytes >= maskBytes && pendingBytes <= maskBytes + 16L * 1024 * 1024,
                        "An 8K mask plus two sparse pending pages exceeded the 80 MiB fixture budget.");
                    Check(surface.Pending && surface.TileCount == 0, "Painting allocated or published a full source.");
                    var insideRegion = new RectInt(8, 8, 1, 1); var outsideRegion = new RectInt(-8, 8, 1, 1);
                    surface.CopyRegion(insideRegion, inside, true); surface.CopyRegion(outsideRegion, outside, true);
                    byte[] expectedInside = Read(inside), expectedOutside = Read(outside);
                    Check(surface.AllocatedBytes == pendingBytes, "Regional preview retained additional textures.");
                    using (surface.Commit()) { }
                    Equal(Read(surface, insideRegion), expectedInside, "Frozen 8K mask preview/commit inside mismatch");
                    Equal(Read(surface, outsideRegion), expectedOutside, "Frozen 8K mask preview/commit outside mismatch");
                    float coverage = 128f / 255;
                    ColorNear(Pixel(surface, new Vector2Int(8, 8)), new Color(-.5f, 2, .25f, .5f) * coverage, "8K R8 mask coverage and HDR");
                    ColorNear(Pixel(surface, new Vector2Int(-8, 8)), Color.clear, "8K mask must reject unwrapped off-canvas pixels", 0);
                    Check(surface.AllocatedBytes < maskBytes, "Commit retained the frozen 64 MiB mask.");
                    surface.Validate(); return pendingBytes;
                }
            }
            finally { Object.DestroyImmediate(mask); Object.DestroyImmediate(ink); Destroy(inside); Destroy(outside); }
        });

        Case(report, "source-loss-after-begin-cannot-publish-commit", () =>
        {
            var frame = Frame(); var ink = Texture(Color.white);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (Paint(surface, frame, ink, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))) { }
                    using (var snapshot = surface.AcquireSnapshot())
                    {
                        surface.Begin(new TiledStrokeSettings(opacity: 1));
                        snapshot.Tiles.First().Texture.Release();
                        // Plain ink does not read the source during pointer updates.
                        surface.Append(new TileBatch(frame, ink, new Vector2Int(2, 0)));
                        Throws<TiledPaintResourceLostException>(() => { using (surface.Commit()) { } });
                        Check(!surface.Pending && surface.TileCount == 1, "Resource loss published a partial new source.");
                        Throws<TiledPaintResourceLostException>(() => { using (surface.AcquireSnapshot()) { } });
                    }
                    return surface.AllocatedBytes;
                }
            }
            finally { Object.DestroyImmediate(ink); }
        });
    }

    static void Dimensions(Report report, TiledPaintShaders shaders)
    {
        foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(255, 257), new Vector2Int(1000, 333), new Vector2Int(8192, 4096), new Vector2Int(8192, 8192) })
        {
            var captured = size;
            Case(report, "source-frame-" + size.x + "x" + size.y, () =>
            {
                var frame = new TiledPaintFrame(captured.x, captured.y, 8192, 4096, ProjectiveMatrix.Translate(13, -7) * ProjectiveMatrix.Scale(2, .5));
                var input = Texture(new Color(-.5f, 3, .125f, 1));
                try
                {
                    using (var surface = new TiledPaintSurface(frame, shaders))
                    {
                        surface.Prepare(2);
                        Vector2 uv = frame.SourceUv(new Vector2(-256, captured.y + 512));
                        using (Paint(surface, frame, input, Vector2Int.zero, new TiledStrokeSettings(opacity: 1))) { }
                        var key = new Vector2Int(-2, captured.y / Tile + 2);
                        using (Paint(surface, frame, input, key, new TiledStrokeSettings(opacity: 1))) { }
                        Check(frame.SourceUv(new Vector2(-256, captured.y + 512)) == uv, "Occupied bounds changed UV frame.");
                        ColorNear(Pixel(surface, key * Tile + Vector2Int.one), new Color(-.5f, 3, .125f, 1), "Outside source content");
                        ColorNear(Pixel(surface, Vector2Int.zero), new Color(-.5f, 3, .125f, 1), "Original tile survived expansion");
                        using (var snapshot = surface.AcquireSnapshot()) Check(ReferenceEquals(snapshot.Frame, frame), "Snapshot changed the immutable frame.");
                        surface.Validate(); return surface.AllocatedBytes;
                    }
                }
                finally { Object.DestroyImmediate(input); }
            });
        }
    }

    static void Native(Report report, TiledPaintShaders shaders)
    {
        foreach (var shape in new[] { TiledPaintPencilShape.Brush, TiledPaintPencilShape.Circle, TiledPaintPencilShape.Square, TiledPaintPencilShape.Diamond })
        {
            var captured = shape;
            Case(report, "native-" + shape + "-seam-hdr-and-source-boundary", () =>
            {
                var frame = Frame(255, 257);
                var brush = new TiledPaintBrushSettings(81, .4f, new Color(-.5f, 2, .25f, .5f), captured);
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    var centers = new[] { new Vector2(128, 128), new Vector2(254, 256), new Vector2(-80, -80) };
                    var stamps = centers.Select(center => TiledPaintBrushStamp.FromSourcePixels(frame, center, brush)).ToArray();
                    surface.Begin(new TiledStrokeSettings(opacity: 1));
                    using (var batch = new TiledPaintBrushBatch(shaders.Brush, frame, brush, stamps)) surface.Append(batch);
                    using (surface.Commit()) { }
                    foreach (var center in centers)
                        ColorNear(Pixel(surface, Vector2Int.FloorToInt(center)), new Color(-.25f, 1, .125f, .5f), "Native center " + center);
                    // The crossing dab must survive both the last partial source tile and its unbounded remainder.
                    ColorNear(Pixel(surface, new Vector2Int(257, 259)), new Color(-.25f, 1, .125f, .5f), "Partial edge tile raw remainder");
                    surface.Validate(); return surface.AllocatedBytes;
                }
            });
        }

        Case(report, "native-8k-2048px-batch-partition-exact", () =>
        {
            var frame = Frame(); var brush = new TiledPaintBrushSettings(2048, .37f, new Color(-.25f, 2, .5f, .03125f));
            var centers = Enumerable.Range(0, 12).Select(i => new Vector2(3064 + i * 4.25f, 4057 + i * 1.5f)).ToArray();
            var stamps = centers.Select(center => TiledPaintBrushStamp.FromSourcePixels(frame, center, brush)).ToArray();
            using (var single = new TiledPaintSurface(frame, shaders))
            using (var partitioned = new TiledPaintSurface(frame, shaders))
            {
                single.Begin(new TiledStrokeSettings(opacity: .625f));
                using (var batch = new TiledPaintBrushBatch(shaders.Brush, frame, brush, stamps)) single.Append(batch);
                using (single.Commit()) { }
                partitioned.Begin(new TiledStrokeSettings(opacity: .625f));
                using (var rasterizer = new TiledPaintBrushRasterizer(shaders.Brush, frame, brush))
                {
                    int offset = 0;
                    foreach (int count in new[] { 1, 7, 4 })
                    {
                        using (var batch = rasterizer.CreateBatch(stamps.Skip(offset).Take(count).ToArray())) partitioned.Append(batch);
                        offset += count;
                    }
                }
                using (partitioned.Commit()) { }
                foreach (var region in new[] { new RectInt(3008, 4000, 256, 256), new RectInt(2000, 4000, 128, 128),
                    new RectInt(4000, 4000, 128, 128), new RectInt(3008, 3008, 128, 128) })
                    Equal(Read(single, region), Read(partitioned, region), "Batch boundary changed native accumulation " + region);
                single.Validate(); partitioned.Validate(); return single.AllocatedBytes + partitioned.AllocatedBytes;
            }
        });

        Case(report, "native-partial-tile-333x257-outside-point", () =>
        {
            var frame = Frame(333, 257); var brush = new TiledPaintBrushSettings(81, .4f, new Color(2, -.5f, .25f, .5f));
            using (var surface = new TiledPaintSurface(frame, shaders))
            {
                var stamp = TiledPaintBrushStamp.FromSourcePixels(frame, new Vector2(350, 270), brush);
                surface.Begin(new TiledStrokeSettings(opacity: 1));
                using (var batch = new TiledPaintBrushBatch(shaders.Brush, frame, brush, new[] { stamp })) surface.Append(batch);
                using (surface.Commit()) { }
                ColorNear(Pixel(surface, new Vector2Int(350, 270)), new Color(1, -.25f, .125f, .5f), "Outside partial tile center");
                surface.Validate(); return surface.AllocatedBytes;
            }
        });

        Case(report, "native-compute-raster-commit-equivalence", () =>
        {
            Check(SystemInfo.supportsComputeShaders && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                && shaders.ComputeCommit != null && shaders.ComputeCommit.IsSupported(shaders.ComputeCommit.FindKernel("Commit")),
                "The compute comparison requires an actual supported compute path.");
            // More than 16 changed tiles forces the compute commit path.
            var frame = Frame(); var brush = new TiledPaintBrushSettings(2048, .35f, new Color(.5f, 2, -.125f, .125f));
            var stamps = new[] { TiledPaintBrushStamp.FromSourcePixels(frame, new Vector2(3061, 4059), brush),
                TiledPaintBrushStamp.FromSourcePixels(frame, new Vector2(3093, 4083), brush) };
            using (var compute = new TiledPaintSurface(frame, shaders, new TiledPaintOptions { ComputeCommit = true }))
            using (var raster = new TiledPaintSurface(frame, shaders, new TiledPaintOptions { ComputeCommit = false }))
            {
                foreach (var surface in new[] { compute, raster })
                {
                    surface.Begin(new TiledStrokeSettings(opacity: .5f));
                    using (var batch = new TiledPaintBrushBatch(shaders.Brush, frame, brush, stamps)) surface.Append(batch);
                    using (surface.Commit()) { }
                }
                Check(compute.TileCount > 16, "Fixture did not reach the compute commit threshold.");
                foreach (var region in new[] { new RectInt(3008, 4000, 128, 128), new RectInt(2016, 4000, 128, 128),
                    new RectInt(4000, 4000, 128, 128), new RectInt(3008, 3008, 128, 128) })
                    Equal(Read(compute, region), Read(raster, region), "Commit backend changed pixels " + region);
                return compute.AllocatedBytes + raster.AllocatedBytes;
            }
        });

        Case(report, "native-persistent-rasterizer-lease-guards", () =>
        {
            var frame = Frame(); var settings = new TiledPaintBrushSettings(81, .4f, new Color(2, -.5f, .25f, .5f));
            var stamp = TiledPaintBrushStamp.FromSourcePixels(frame, new Vector2(128, 128), settings);
            var input = new[] { stamp }; var rasterizer = new TiledPaintBrushRasterizer(shaders.Brush, frame, settings);
            try
            {
                using (var surface = new TiledPaintSurface(frame, shaders))
                {
                    using (var first = rasterizer.CreateBatch(input))
                    {
                        Rect original = first.Bounds(0);
                        input[0] = TiledPaintBrushStamp.FromSourcePixels(frame, new Vector2(700, 700), settings);
                        Check(first.Bounds(0) == original, "Caller mutated a leased stamp payload.");
                        Throws<InvalidOperationException>(() => { using (rasterizer.CreateBatch(input)) { } });
                        Throws<InvalidOperationException>(() => rasterizer.Dispose());
                        surface.Begin(new TiledStrokeSettings(opacity: 1)); surface.Append(first);
                        Throws<InvalidOperationException>(() => surface.Append(first));
                        Check(!surface.Pending && surface.TileCount == 0, "Reusing a one-shot lease left a partial transaction.");
                    }
                    Throws<ArgumentException>(() => { using (rasterizer.CreateBatch(new[] { default(TiledPaintBrushStamp) })) { } });
                    var next = rasterizer.CreateBatch(new[] { stamp });
                    try { surface.Begin(new TiledStrokeSettings(opacity: 1)); surface.Append(next); }
                    finally { next.Dispose(); }
                    Throws<ObjectDisposedException>(() => next.Bounds(0));
                    using (surface.Commit()) { }
                    ColorNear(Pixel(surface, new Vector2Int(128, 128)), new Color(1, -.25f, .125f, .5f), "Rasterizer failed after a rejected lease");
                    surface.Validate();
                }
                rasterizer.Dispose();
                Throws<ObjectDisposedException>(() => { using (rasterizer.CreateBatch(new[] { stamp })) { } });
                return 0;
            }
            finally { rasterizer.Dispose(); }
        });
    }

    public sealed class ShaderFixture : IDisposable
    {
        readonly string folder;
        public readonly TiledPaintShaders Shaders;
        public ShaderFixture()
        {
            folder = "Assets/__WhimTexTiledReadiness_" + Guid.NewGuid().ToString("N");
            try
            {
                string source = Package + "Staging~/TiledPaint/Source/Shaders";
                Check(Directory.Exists(source), "Staged shader source directory is missing: " + source);
                Directory.CreateDirectory(folder);
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = file.Substring(source.Length).TrimStart('/', '\\');
                    string target = Path.Combine(folder, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
                }
                AssetDatabase.ImportAsset(folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
                Shader raster = null; ComputeShader compute = null;
                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    { Check(raster == null, "Expected exactly one staged raster commit shader."); raster = AssetDatabase.LoadAssetAtPath<Shader>(path); }
                    if (path.EndsWith(".compute", StringComparison.OrdinalIgnoreCase))
                    { Check(compute == null, "Expected exactly one staged compute commit shader."); compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path); }
                }
                Check(raster != null && compute != null, "Staged commit shaders failed to import.");
                Shaders = new TiledPaintShaders(Shader.Find("Hidden/WhimTex/PaintBrush"), raster, compute);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (folder != null && Directory.Exists(folder))
                Check(AssetDatabase.DeleteAsset(folder), "Could not remove owned shader fixture: " + folder);
        }
        public void ValidateDiagnostics()
        {
            foreach (var shader in new[] { Shaders.Brush, Shaders.RasterCommit })
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Check(message.severity.ToString() != "Warning" && message.severity.ToString() != "Error", shader.name + ": " + message.message);
            foreach (var message in ShaderUtil.GetComputeShaderMessages(Shaders.ComputeCommit))
                Check(message.severity.ToString() != "Warning" && message.severity.ToString() != "Error", "Compute: " + message.message);
            Check(Shaders.Brush.isSupported && Shaders.RasterCommit.isSupported, "Required shader became unsupported.");
        }
    }
}
