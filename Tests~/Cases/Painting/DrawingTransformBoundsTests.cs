using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DCFApixels.WhimTex;
using DCFApixels.WhimTex.TiledPaint;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class DrawingTransformBoundsTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    delegate bool ReadBounds(out RectInt bounds);
    static Type Product(string name) => typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object o, string name, params object[] args) =>
        (o as Type ?? o.GetType()).GetMethod(name, F).Invoke(o is Type ? null : o, args);
    static void Flush() { Call(Product("PaintTileTransfer"), "Drain"); Call(Product("PaintHistoryStore"), "FlushWrites"); }

    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument Doc;
        internal readonly int Side;
        internal DrawingLayerBehaviour Drawing => (DrawingLayerBehaviour)Doc.layers[0].Behaviour;
        internal ReadBounds Read => (ReadBounds)typeof(DrawingLayerBehaviour).GetMethod("TryGetVisiblePixelBounds", F)
            .CreateDelegate(typeof(ReadBounds), Drawing);

        internal Fixture(int side, Texture2D input = null)
        {
            Side = side;
            Doc = ScriptableObject.CreateInstance<WhimTexDocument>();
            Doc.hideFlags = HideFlags.HideAndDontSave;
            Doc.width = Doc.height = side;
            var range = input != null && input.format == TextureFormat.RGBA32 ? LayerColorRange.Standard : LayerColorRange.HDR;
            Doc.layers.Add(new Layer(new DrawingLayerBehaviour()) { colorRange = range, blendRange = LayerBlendRange.HDR });
            Call(Doc, "NormalizeModel");
            if (input == null) Call(Drawing, "InitializeCanvas", side, side);
            else Call(Drawing, "AdoptStoredTexture", input);
            Call(Doc, "ResetUndoTrackingAfterLoad");
        }

        internal void Stamp(int x, int y, int diameter, bool erase = false, Texture2D mask = null)
        {
            var uv = new Vector2((x + .5f) / Side, (y + .5f) / Side);
            var type = Product("PaintToolSettings");
            object settings = Activator.CreateInstance(type, true);
            type.GetField("pencilSize", F).SetValue(settings, diameter);
            var shape = type.GetField("pencilShape", F);
            shape.SetValue(settings, Enum.Parse(shape.FieldType, "Square"));
            var parameters = Call(settings, "GetPencilParameters", erase, (Color?)new Color(2, -.25f, 3, 1));
            if (mask != null) parameters = Call(parameters, "WithSelectionMask", mask);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Call(Drawing, "PrepareBufferedStroke", Side, Side, "Owned Drawing bounds test");
            Call(Drawing, "BeginStroke", uv);
            Call(Drawing, "PaintPoint", uv, Side, Side, parameters);
            Call(Drawing, "EndStroke");
            Call(Doc, "MarkChanged");
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
        }

        internal Color[] ReadRegion(RectInt region)
        {
            Call(Drawing, "EnsureTiledSource", Side, Side);
            var surface = (TiledPaintSurface)typeof(DrawingLayerBehaviour).GetField("tiledSurface", F).GetValue(Drawing);
            var target = RenderTexture.GetTemporary(region.width, region.height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(region.width, region.height, TextureFormat.RGBAHalf, false, true);
            var previous = RenderTexture.active;
            try
            {
                surface.CopyRegion(region, target, false);
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, region.width, region.height), 0, 0, false);
                return read.GetPixels();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(read); }
        }

        public void Dispose()
        {
            Flush();
            PaintTileHistoryTests.ReleaseOwnedHistory(Doc);
            Undo.ClearUndo(Doc);
            Call(Drawing, "ReleaseTransientResources");
            Doc.layers.Clear();
            Object.DestroyImmediate(Doc);
        }
    }

    [Serializable] public sealed class Timing
    {
        public string workload;
        public int calls, scannedTiles;
        public double workMs, maxQueryMs, elapsedMs, cachedMicroseconds;
    }

    static RectInt Query(ReadBounds read, bool pumpPending = false)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 25)
        {
            bool ready = read(out RectInt bounds);
            if (ready) return bounds;
            if (pumpPending) Call(Product("PaintTileTransfer"), "Update");
            System.Threading.Thread.Sleep(1);
        }
        throw new TimeoutException("Drawing bounds did not become ready");
    }

    static RectInt PixelOracle(Color[] pixels, RectInt region)
    {
        int xMin = region.xMax, yMin = region.yMax, xMax = region.xMin, yMax = region.yMin;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!(pixels[i].a > 0)) continue;
            int x = region.x + i % region.width, y = region.y + i / region.width;
            xMin = Math.Min(xMin, x); xMax = Math.Max(xMax, x + 1);
            yMin = Math.Min(yMin, y); yMax = Math.Max(yMax, y + 1);
        }
        return xMax <= xMin ? default : new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    public static string Run() => TestContext.Run("Drawing content transform bounds", test =>
    {
        using var disk = new PaintTileHistoryTests.DiskScope();
        const int partialSize = 259;
        var standard = new Texture2D(partialSize, partialSize, TextureFormat.RGBA32, false, true);
        var standardData = standard.GetRawTextureData<Color32>();
        for (int i = 0; i < standardData.Length; i++) standardData[i] = new Color32(90, 42, 200, 0);
        standardData[257 * partialSize + 258] = new Color32(90, 42, 200, 1);
        standard.Apply(false, false);
        using (var partial = new Fixture(partialSize, standard))
            test.Equal(new RectInt(258, 257, 1, 1), Query(partial.Read), "Minimum nonzero 8-bit alpha in a partial edge tile with hidden RGB");
        CheckErasedExtremes(test);
        const int side = 256;
        var original = new Texture2D(side, side, TextureFormat.RGBAHalf, false, true);
        var colors = new Color[side * side];
        // Hidden RGB and negative alpha do not enlarge the frame; a small positive Half value does.
        colors[0] = new Color(9, -3, 8, 0);
        colors[255] = new Color(1, 2, 3, -.25f);
        colors[54 * side + 38] = new Color(-2, 3, 5, .00001f);
        colors[58 * side + 40] = new Color(1, 2, 3, .5f);
        original.SetPixels(colors); original.Apply(false, false);
        using var f = new Fixture(side, original);
        var initial = new RectInt(38, 54, 3, 5);
        test.Equal(initial, Query(f.Read), "Exact dense HDR bounds include weak positive alpha");
        var region = new RectInt(-256, -256, 512, 512);

        var mask = new Texture2D(side, side, TextureFormat.R8, false, true) { filterMode = FilterMode.Point };
        try
        {
            var coverage = mask.GetRawTextureData<byte>();
            for (int i = 0; i < coverage.Length; i++) coverage[i] = i % side >= 170 ? (byte)128 : (byte)0;
            mask.Apply(false, false);
            f.Stamp(170, 123, 9, mask: mask);
            var masked = f.ReadRegion(region);
            var maskedBounds = PixelOracle(masked, region);
            test.Equal(maskedBounds, Query(f.Read, pumpPending: true), "Masked stamp bounds match independently read GPU pixels");
            float alpha = masked[(123 - region.y) * region.width + 170 - region.x].a;
            test.True(alpha > 0 && alpha < 1 && maskedBounds.xMax > 170, "The half-strength mask actually writes the stamp");
            test.True(masked[(123 - region.y) * region.width + 169 - region.x].a == 0, "The black side of the mask excludes the stamp");

            f.Stamp(-37, -61, 3);
            var outside = f.ReadRegion(region);
            var extended = PixelOracle(outside, region);
            test.True(extended.xMin < 0 && extended.yMin < 0, "The source really has negative-coordinate pixels");
            test.Equal(extended, Query(f.Read, pumpPending: true), "Fitted bounds include the painted area beyond both canvas edges");
            VerifyFrame(test, f, extended);

            f.Stamp(-37, -61, 19, erase: true);
            test.Equal(maskedBounds, Query(f.Read, pumpPending: true), "An allocated but fully erased outer tile shrinks the frame");
            var erased = f.ReadRegion(region);
            test.Equal(maskedBounds, PixelOracle(erased, region), "Erasure shrinks the independently measured image");
            Flush();
            Undo.PerformUndo();
            test.Equal(extended, Query(f.Read, pumpPending: true), "Native Undo expands the bounds again");
            test.True(Same(outside, f.ReadRegion(region)), "Undo restores exact signed source and alpha");
            Undo.PerformRedo();
            test.Equal(maskedBounds, Query(f.Read, pumpPending: true), "Native Redo shrinks the bounds again");
            test.True(Same(erased, f.ReadRegion(region)), "Redo restores every source pixel");
            f.Stamp(39, 56, 21, erase: true);
            var erasedBase = PixelOracle(f.ReadRegion(region), region);
            test.True(erasedBase.xMin >= 170, "The sparse override erases the older dense island");
            test.Equal(erasedBase, Query(f.Read, pumpPending: true), "A sparse override replaces the underlying dense tile");
            f.Stamp(170, 123, 31, erase: true);
            test.Equal(default(RectInt), Query(f.Read, pumpPending: true), "A fully erased Drawing has no visible content");
            if (SystemInfo.supportsAsyncGPUReadback && SystemInfo.supportsComputeShaders)
            {
                f.Stamp(210, 211, 1);
                var read = f.Read;
                var transfer = Product("PaintTileTransfer");
                test.True((int)transfer.GetProperty("PendingCount", F).GetValue(null) > 0, "The latest Half page is still queued for CPU readback");
                test.True(!read(out _), "A first query for the changed GPU-only version yields while its bounds are in flight");
                f.Stamp(210, 211, 9, erase: true);
                f.Stamp(-90, 165, 1);
                var final = PixelOracle(f.ReadRegion(region), region);
                test.Equal(new RectInt(-90, 165, 1, 1), final, "The newer image has disjoint bounds while the first readback completes");
                test.Equal(final, Query(read, pumpPending: true), "A late readback cannot publish bounds for the superseded stroke");
            }
        }
        finally { Object.DestroyImmediate(mask); }
    });

    static void CheckErasedExtremes(TestContext test)
    {
        const int side = 1280;
        using var f = new Fixture(side);
        f.Stamp(525, 640, 1);
        f.Stamp(33, 49, 1);
        f.Stamp(1201, 1215, 1);
        Flush();
        var initial = new RectInt(33, 49, 1202 - 33, 1216 - 49);
        test.Equal(initial, Query(f.Read), "Outer witnesses enclose the untouched middle tile");
        f.Stamp(33, 49, 9, erase: true);
        f.Stamp(1201, 1215, 9, erase: true);
        Flush();
        test.Equal(new RectInt(525, 640, 1, 1), Query(f.Read),
            "After erasing every outer witness, the unmodified middle tile supplies all four exact edges");
        Undo.PerformUndo();
        test.Equal(new RectInt(525, 640, 1202 - 525, 1216 - 640), Query(f.Read, pumpPending: true),
            "Undo restores the far witness without reviving the erased near edge");
        Undo.PerformRedo();
        test.Equal(new RectInt(525, 640, 1, 1), Query(f.Read, pumpPending: true),
            "Redo recovers the sole middle pixel");
    }

    static bool Same(Color[] a, Color[] b)
    {
        if (a.Length != b.Length) throw new InvalidOperationException("Different source sizes");
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }

    static void VerifyFrame(TestContext test, Fixture f, RectInt rect)
    {
        var type = typeof(WhimTexWindow).GetNestedType("DrawingTransformFrame", F);
        var size = new Vector2(f.Side, f.Side);
        var basis = type.GetMethod("FromBounds", F).Invoke(null, new object[] { f.Drawing, rect, size });
        var layer = TextureTransform.Default;
        layer.position = new Double2(19, -31);
        layer.scale = new Double2(-.72, 1.33);
        layer.rotation = 23;
        layer.pivot = new Double2(.4, .72);
        object[] toFrame = { layer, size, TextureTransform.Default };
        test.True((bool)Call(basis, "TryGetFrame", toFrame), "A flipped and rotated layer accepts the fitted bounds");
        var frame = (TextureTransform)toFrame[2];
        var canvasPivot = layer.Map((Vector2)layer.pivot, size);
        test.True((canvasPivot - frame.Map((Vector2)frame.pivot, size)).sqrMagnitude < 1e-10f, "Fitting preserves pivot in canvas space");
        var probes = new[] { Vector2.zero, Vector2.one, Vector2.up, Vector2.right, new Vector2(.23f, .48f) };
        foreach (var uv in probes)
        {
            var source = new Vector2((rect.x + rect.width * uv.x) / f.Side, (rect.y + rect.height * uv.y) / f.Side);
            test.True((layer.Map(source, size) - frame.Map(uv, size)).sqrMagnitude < 1e-10f, "Frame corners and interior follow original signed source UV");
        }
        object[] toLayer = { frame, size, TextureTransform.Default };
        test.True((bool)Call(basis, "TryGetLayer", toLayer), "The fitted frame maps back to layer Transform");
        var roundTrip = (TextureTransform)toLayer[2];
        foreach (var uv in probes)
            test.True((layer.Map(uv, size) - roundTrip.Map(uv, size)).sqrMagnitude < 1e-10f, "Fitting alone preserves the complete layer mapping");

        var owner = f.Doc.layers[0];
        var previous = owner.transform;
        try
        {
            var doubled = frame;
            test.True(doubled.TrySetMatrix(frame.ToMatrix(size.x, size.y) * ProjectiveMatrix.Scale(2, 1)), "Double the fitted width about its left edge");
            object[] convert = { doubled, size, TextureTransform.Default };
            test.True((bool)Call(basis, "TryGetLayer", convert), "The edited fitted frame converts back to an ordinary transform");
            test.True((bool)Call(f.Doc, "SetCanvasTransform", owner, (TextureTransform)convert[2]), "The edit is applied through the normal layer transform");
            foreach (var uv in probes)
            {
                var source = new Vector2((rect.x + rect.width * uv.x) / f.Side, (rect.y + rect.height * uv.y) / f.Side);
                var expected = frame.Map(new Vector2(uv.x * 2f, uv.y), size);
                test.True((expected - owner.transform.Map(source, size)).sqrMagnitude < 1e-10f, "Resizing about the left edge moves the same original source UV");
            }
            test.Equal(rect, Query(f.Read), "Transform edits leave cached source-space pixels and their bounds unchanged");
        }
        finally { Call(f.Doc, "SetCanvasTransform", owner, previous); }
    }

    static string RunKey(string id) => "WhimTex.DrawingBounds8K." + Guid.Parse(id).ToString("N");
    public static string Start8K(string id)
    {
        string key = RunKey(id);
        if (AppDomain.CurrentDomain.GetData(key) != null) throw new InvalidOperationException("Duplicate diagnostic");
        var test = new TestContext();
        var rows = new List<Timing>();
        var steps = Scenarios(test, rows).GetEnumerator();
        // Pipeline recompiles the entry assembly for Poll and Cleanup. Only BCL data crosses that boundary.
        var state = new object[] { TestContext.Result("running", 0, "8K Drawing frame timing").ToJson(), null };
        state[1] = (Action)(() => Finish(new OperationCanceledException()));
        AppDomain.CurrentDomain.SetData(key, state);
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Reload;
        return (string)state[0];

        void Tick()
        {
            try { if (steps.MoveNext()) return; }
            catch (Exception error) { Finish(error); return; }
            Finish(null);
        }
        void Reload() => Finish(new InvalidOperationException("Reload during bounds measurements"));
        void Finish(Exception error)
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Reload;
            try { steps.Dispose(); }
            catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); }
            string status = error is OperationCanceledException ? "cancelled" : error == null ? "passed" : "failed";
            var message = new StringBuilder("Drawing content Transform 8K, one query per Editor update");
            foreach (var row in rows)
                message.AppendLine().Append(FormattableString.Invariant(
                    $"{row.workload}: scans={row.scannedTiles}, queries={row.calls}, elapsed={row.elapsedMs:F3} ms, work={row.workMs:F3} ms, max={row.maxQueryMs:F3} ms, warm={row.cachedMicroseconds:F3} us/query"));
            state[0] = TestContext.Result(status, test.Checks, message.ToString(), error == null ? Array.Empty<string>() : new[] {error.ToString()}).ToJson();
            state[1] = null;
        }
    }
    static object[] State(string id) => AppDomain.CurrentDomain.GetData(RunKey(id)) as object[]
        ?? throw new InvalidOperationException("Missing owned bounds diagnostic");
    public static string Poll8K(string id) => (string)State(id)[0];
    public static string Cancel8K(string id)
    {
        (State(id)[1] as Action)?.Invoke();
        return Poll8K(id);
    }
    public static string Cleanup8K(string id)
    {
        Cancel8K(id);
        AppDomain.CurrentDomain.SetData(RunKey(id), null);
        return TestContext.Result("passed", 1, "Owned diagnostic stopped and released").ToJson();
    }

    static IEnumerable<int> Scenarios(TestContext test, List<Timing> measurements)
    {
        const int side = 8192;
        using var disk = new PaintTileHistoryTests.DiskScope();
        var painted = new RectInt(19, 103, side - 50, side - 120);
        var basePixels = new Texture2D(side, side, TextureFormat.RGBA32, false, true);
        Fill(basePixels, painted);
        using (var dense = new Fixture(side, basePixels))
        {
            foreach (var tick in Cold(dense, "dense 8K RGBA32 first fit", painted)) yield return tick;
            var surface = (RenderTexture)Call(dense.Drawing, "EnsurePaintSurface", side, side);
            test.True(surface.IsCreated() && surface.format == RenderTextureFormat.ARGBHalf, "The measured 8K working surface is genuinely Half");
            // Only the working surface has this pixel. The CPU base would produce the earlier min X=19.
            var marker = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true);
            try
            {
                marker.SetPixel(0, 0, new Color(2, -.25f, 4, .5f));
                marker.Apply(false, false);
                Graphics.CopyTexture(marker, 0, 0, 0, 0, 1, 1, surface, 0, 0, 3, side/2);
                var ready = AsyncGPUReadback.Request(surface, 0, 3, 1, side/2, 1, 0, 1);
                ready.WaitForCompletion();
                test.True(!ready.hasError && ready.GetData<ushort>()[3] == 0x3800, "Working-surface creation, pixel placement and upload complete before timing");
            }
            finally { Object.DestroyImmediate(marker); }
            foreach (var tick in Cold(dense, "dense 8K live Half GPU first fit", new RectInt(3, 103, side - 34, side - 120))) yield return tick;
        }

        var inset = new RectInt(2103, 3061, 2832, 1917);
        var insetPixels = new Texture2D(side, side, TextureFormat.RGBA32, false, true);
        Fill(insetPixels, inset);
        using (var insetLayer = new Fixture(side, insetPixels))
            foreach (var tick in Cold(insetLayer, "dense 8K RGBA32 transparent margins", inset)) yield return tick;
        var islands = new Texture2D(side, side, TextureFormat.RGBA32, false, true);
        var a = new RectInt(3011, 3569, 2, 3);
        var b = new RectInt(4993, 4553, 3, 2);
        Fill(islands, a, b);
        using (var islandsLayer = new Fixture(side, islands))
            foreach (var tick in Cold(islandsLayer, "dense 8K RGBA32 two tiny islands", new RectInt(a.x, a.y, b.xMax-a.x, b.yMax-a.y))) yield return tick;
        using var sparse = new Fixture(side);
        sparse.Stamp(-30, 100, 1);
        sparse.Stamp(7000, 5000, 1);
        Flush();
        foreach (var tick in Measure(sparse, "sparse 8K HDR first fit, two tiles", new RectInt(-30, 100, 7031, 4901), 2)) yield return tick;
        sparse.Stamp(-30, 100, 5, erase: true);
        Flush();
        foreach (var tick in Measure(sparse, "sparse 8K one outer tile erased", new RectInt(7000, 5000, 1, 1), 1)) yield return tick;

        IEnumerable<int> Cold(Fixture f, string label, RectInt expected)
        {
            for (int sample = 1; sample <= 3; sample++)
            {
                typeof(DrawingLayerBehaviour).GetField("visibleBounds", F).SetValue(f.Drawing, null);
                foreach (var tick in Measure(f, label + " #" + sample, expected, 1024)) yield return tick;
            }
        }

        IEnumerable<int> Measure(Fixture f, string label, RectInt expected, int maximumTiles)
        {
            // Start on a fresh Editor update, after fixture setup, texture initialization and any previous sample.
            yield return 0;
            var read = f.Read;
            var t = new Timing { workload = label };
            measurements.Add(t);
            var elapsed = Stopwatch.StartNew();
            RectInt bounds;
            while (true)
            {
                long start = Stopwatch.GetTimestamp();
                bool ready = read(out bounds);
                double work = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
                t.workMs += work;
                t.maxQueryMs = Math.Max(t.maxQueryMs, work);
                t.calls++;
                t.elapsedMs = elapsed.Elapsed.TotalMilliseconds;
                if (ready) break;
                if (t.elapsedMs > 15000) throw new TimeoutException(label + " did not finish across Editor updates");
                yield return 0;
            }
            test.Equal(expected, bounds, label);
            t.scannedTiles = (int)typeof(DrawingLayerBehaviour).GetProperty("LastVisibleBoundsScannedTiles", F).GetValue(f.Drawing);
            test.True(t.scannedTiles >= 0 && t.scannedTiles <= maximumTiles, label + " scans at most " + maximumTiles + " source tiles");
            var batches = new double[15];
            for (int batch = 0; batch < batches.Length; batch++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int j = 0; j < 1000; j++)
                    if (!read(out RectInt cached) || cached != expected) throw new InvalidOperationException("Cached bounds changed");
                batches[batch] = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            }
            Array.Sort(batches);
            t.cachedMicroseconds = batches[batches.Length / 2]; // 1000 calls in ms equals microseconds per call.
        }
    }

    static void Fill(Texture2D texture, RectInt painted, RectInt second = default)
    {
        int width = texture.width;
        var bytes = texture.GetRawTextureData<uint>();
        for (int i = 0; i < bytes.Length; i++) bytes[i] = 0x00ddddffu;
        foreach (RectInt island in new[] { painted, second })
            for (int y = island.yMin; y < island.yMax; y++)
                for (int x = island.xMin; x < island.xMax; x++)
                    bytes[y * width + x] = 0xff808080u;
        texture.Apply(false, false);
    }
}
