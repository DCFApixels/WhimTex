using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Only owned fixtures opt into subtiles. GPU pages, paint tiles and production defaults stay fixed.
public static partial class PaintTileHistoryTests
{
    public static string CheckUndoBlocks() => TestContext.Run("Subtile history exactness and unchanged omission", t =>
    {
        using var disk = new DiskScope();
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var mask = new Texture2D(32, 32, TextureFormat.RGBA32, false, true);
        var values = new Color32[1024];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            values[y * 32 + x] = new Color32((byte)(x < 24 ? 0 : y < 16 ? 64 : 255), 0, 0, 255);
        mask.SetPixels32(values); mask.Apply(false, false);
        try
        {
            foreach (bool hdr in new[] { false, true }) foreach (int size in new[] { 64, 128, 256 })
            {
                using var f = new Fixture(518, hdr); using var reference = new Fixture(518, hdr);
                Set(f.layer, "historyCaptureBlockSize", size); Set(f.layer, "historySkipUnchanged", true);
                var pattern = new Color[518 * 518];
                for (int i = 0; i < pattern.Length; i++) pattern[i] = new Color(hdr ? -.3f : .3f, hdr ? 1.7f : .7f,
                    (i % 518) / 517f, i % 7 == 0 ? 0f : .001f + i % 50 * .008f);
                foreach (var p in new[] { f, reference })
                {
                    p.pixels.SetPixels(pattern); p.pixels.Apply(false, false); Call(p.layer, "InvalidatePaintSurface");
                    Call(p.layer, "EnsurePaintSurface", 518, 518); Set(p.settings, "brushSize", 518f);
                }
                var rect = new RectInt(0, 0, 518, 518);
                var halves = new List<byte[]> { Probe((RenderTexture)Get(f.layer, "paintSurface"), rect) };
                var stored = new List<byte[]> { f.pixels.GetRawTextureData<byte>().ToArray() };
                for (int stroke = 0; stroke < 3; stroke++)
                {
                    Set(f.dynamics, "flow", stroke == 1 ? .0001f : .13f);
                    Set(reference.dynamics, "flow", stroke == 1 ? .0001f : .13f);
                    var points = new[] { new Vector2(.31f, .24f), new Vector2(.96f, .96f) };
                    f.Stroke(points, mask, erase: stroke == 2); reference.Stroke(points, mask, erase: stroke == 2);
                    Static("PaintHistoryStore", "FlushWrites");
                    // Reference owns an independent native Undo record, so discard it before testing f.
                    var rc = (Object)Get(reference.layer, "PixelUndoObject");
                    if (rc != null) { Undo.ClearUndo(rc); Object.DestroyImmediate(rc); }
                    halves.Add(Probe((RenderTexture)Get(f.layer, "paintSurface"), rect));
                    stored.Add(f.pixels.GetRawTextureData<byte>().ToArray());
                    Exact(t, Probe((RenderTexture)Get(reference.layer, "paintSurface"), rect), halves[stroke + 1], "Same Half result for " + size);
                    Exact(t, reference.pixels.GetRawTextureData<byte>().ToArray(), stored[stroke + 1], "Same straight/hidden RGB result");
                }
                var step = Static("PaintHistoryStore", "Get", (string)Get(Get(f.layer, "PixelUndoObject"), "Revision"));
                long bytes = 0; int kept = 0;
                foreach (var page in (Array)Get(step, "payload")) { bytes += (long)Get(page, "bytes"); kept += (int)Get(page, "blockCount"); }
                t.True(bytes < 518L * 518 * (hdr ? 32 : 24), "Zero-mask areas omitted at size " + size);
                t.True(kept > 0, "Changed blocks retained");
                for (int i = 2; i >= 0; i--)
                {
                    Undo.PerformUndo(); Exact(t, stored[i], f.pixels.GetRawTextureData<byte>().ToArray(), "Ordered exact stored Undo");
                    Exact(t, halves[i], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Ordered exact Half Undo");
                }
                for (int i = 1; i <= 3; i++)
                {
                    Undo.PerformRedo(); Exact(t, stored[i], f.pixels.GetRawTextureData<byte>().ToArray(), "Ordered exact stored Redo");
                    Exact(t, halves[i], Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Ordered exact Half Redo");
                }
                t.Equal("Present", CoverageState(f.layer, 518), "Subtile restore preserves occupancy");
                // A fully blocked page is a valid empty payload, including after disk reload.
                var zero = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    zero.SetPixel(0, 0, Color.clear); zero.Apply(false, false);
                    f.Stroke(new[] { new Vector2(.5f, .5f) }, zero); Static("PaintHistoryStore", "FlushWrites");
                    var empty = Static("PaintHistoryStore", "Get", (string)Get(Get(f.layer, "PixelUndoObject"), "Revision"));
                    foreach (var page in (Array)Get(empty, "payload")) t.Equal(0, (int)Get(page, "blockCount"), "No bytes for an unchanged block");
                    Undo.PerformUndo(); Undo.PerformRedo(); Exact(t, stored[3], f.pixels.GetRawTextureData<byte>().ToArray(), "Empty payload replay");
                }
                finally { Object.DestroyImmediate(zero); }
                if (Unity.Burst.BurstCompiler.Options.EnableBurstCompilation)
                    t.True(Transfer<bool>("LastComparisonUsedBurst"), "Comparison actually ran through Burst");
                Call(f.layer, "InvalidatePaintSurface");
                Exact(t, halves[3], Probe((RenderTexture)Call(f.layer, "EnsurePaintSurface", 518, 518), rect), "Exact Half reconstruction with one block layout");
            }
        }
        finally { Object.DestroyImmediate(mask); RenderTexture.active = active; GL.sRGBWrite = srgb; }
    });

    const string UndoBlockKey = "WhimTex.Tests.UndoBlockSize";
    static object[] UndoBlockState => AppDomain.CurrentDomain.GetData(UndoBlockKey) as object[];
    public static string StartUndoBlocks(string runId, int side, int blockSize, bool skip, bool wide)
    {
        if (UndoBlockState != null) return TestContext.Result("failed", 0, "Another owned block-size run exists").ToJson();
        var state = new object[] { runId, TestContext.Result("running", 0, "Preparing block-size comparison").ToJson(), null };
        AppDomain.CurrentDomain.SetData(UndoBlockKey, state);
        var run = new UndoBlocksRun(state, side, blockSize, skip, wide);
        state[2] = (Action)(() => run.Finish(new OperationCanceledException()));
        try { run.Prepare(); } catch (Exception error) { run.Finish(error); }
        return (string)state[1];
    }
    public static string PollUndoBlocks(string runId) => UndoBlockState != null && (string)UndoBlockState[0] == runId
        ? (string)UndoBlockState[1] : TestContext.Result("failed", 0, "Missing block-size fixture").ToJson();
    public static string CancelUndoBlocks(string runId)
    {
        if (UndoBlockState != null && (string)UndoBlockState[0] == runId) ((Action)UndoBlockState[2])();
        return TestContext.Result("cancelled", 0, "Owned block-size run cancelled").ToJson();
    }
    public static string CleanupUndoBlocks(string runId) => TestContext.Run("Owned block-size cleanup", t =>
    {
        t.True(UndoBlockState == null || (string)UndoBlockState[0] == runId, "Only matching run can be cleaned");
        if (UndoBlockState == null) return;
        ((Action)UndoBlockState[2])(); AppDomain.CurrentDomain.SetData(UndoBlockKey, null);
    });

    sealed class UndoBlocksRun
    {
        readonly object[] state;
        readonly int side, blockSize;
        readonly bool skip, wide;
        readonly TestContext test = new();
        readonly List<object> strokes = new();
        readonly List<string> storedHashes = new();
        readonly List<byte[][]> halfProbes = new();
        readonly List<object> cpuPhases = new();
        Fixture fixture;
        DiskScope disk;
        Texture2D mask;
        Vector2[] points;
        RectInt[] probes;
        object parameters;
        string path;
        int phase, stroke, point, group;
        long started, releaseAt, readbackBefore, copyBefore, waitsBefore, stagedAtRelease;
        double drawMs, releaseMs, drainMs, undoMs, redoMs;
        bool finished, profiling;
        internal UndoBlocksRun(object[] state, int side, int blockSize, bool skip, bool wide)
        { this.state = state; this.side = side; this.blockSize = blockSize; this.skip = skip; this.wide = wide; }
        internal void Prepare()
        {
            test.True(side == 512 || side == 8192, "Bounded size");
            test.True(side < 8192 || SystemInfo.graphicsMemorySize >= 7000, "8K requires 7 GB VRAM");
            test.Equal(0, Transfer<int>("PendingCount"), "No pending user transfer");
            test.Equal(0L, (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null), "No pending user writer");
            test.True(!(bool)Type("WhimTexProfiler").GetProperty("Recording", S).GetValue(null), "No user profiler recording");
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>()) test.True(Get(window, "paintingLayer") == null, "No user stroke");
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/undo-block-size")); Directory.CreateDirectory(directory);
            path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + side + "-" + blockSize + "-" + skip + "-" + (wide ? "wide" : "thin"));
            disk = new DiskScope(); Call(disk.disk, "SetLimits", (int?)8, (int?)50);
            fixture = new Fixture(side, false); mask = Mask();
            Set(fixture.layer, "historyCaptureBlockSize", blockSize); Set(fixture.layer, "historySkipUnchanged", skip);
            Set(fixture.settings, "brushSize", wide ? side / 4f : 32f);
            parameters = fixture.Parameters(mask);
            if (wide)
            {
                points = new Vector2[16];
                for (int i = 0; i < 8; i++)
                { points[i * 2] = new Vector2(i % 2 == 0 ? .04f : .96f, .06f + i * .125f); points[i * 2 + 1] = new Vector2(i % 2 == 0 ? .96f : .04f, .06f + i * .125f); }
            }
            else
            {
                points = new Vector2[129];
                for (int i = 0; i < points.Length; i++)
                { float a = i * 2 * Mathf.PI / 128; points[i] = new Vector2(.5f + .45f * Mathf.Sin(3 * a + .2f), .5f + .45f * Mathf.Sin(2 * a)); }
            }
            probes = new RectInt[8];
            for (int i = 0; i < probes.Length; i++)
            { var p = points[i * (points.Length - 1) / probes.Length]; probes[i] = new RectInt(Mathf.Clamp((int)(p.x * side) - 8, 0, side - 16), Mathf.Clamp((int)(p.y * side) - 8, 0, side - 16), 16, 16); }
            Remember();
            Static("WhimTexProfiler", "Start", 131072, 180.0, null); profiling = true;
            started = Stopwatch.GetTimestamp(); EditorApplication.update += Tick;
        }
        void Tick()
        {
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { Advance(); } catch (Exception error) { Finish(error); }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        void Advance()
        {
            if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * 165) throw new TimeoutException("Block-size run exceeded 165 seconds");
            if (phase == 0)
            {
                if (point == 0)
                {
                    readbackBefore = Transfer<long>("ReadbackBytes"); copyBefore = Transfer<long>("TransferredBytes"); waitsBefore = Transfer<long>("CapacityWaits"); drawMs = 0;
                    Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                    Call(fixture.layer, "PrepareBufferedStroke", side, side, "Owned block-size stroke"); Call(fixture.layer, "BeginStroke", points[0]);
                }
                var watch = Stopwatch.StartNew();
                int limit = Math.Min(points.Length, point + (wide ? 2 : 16));
                for (; point < limit; point++)
                {
                    if (point == 0) Call(fixture.layer, "PaintPoint", points[0], side, side, parameters);
                    else Call(fixture.layer, "PaintSegment", points[point - 1], points[point], side, side, false, parameters);
                }
                // A bounded one-pixel fence includes drawing GPU work, before release timing.
                Probe(fixture.surface, new RectInt(0, 0, 1, 1)); drawMs += watch.Elapsed.TotalMilliseconds;
                if (point < points.Length) return;
                watch.Restart(); Call(fixture.layer, "EndStroke"); releaseMs = watch.Elapsed.TotalMilliseconds;
                releaseAt = Stopwatch.GetTimestamp(); stagedAtRelease = Transfer<long>("StagingBytes");
                Call(fixture.document, "MarkChanged"); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup(); phase = 1;
            }
            else if (phase == 1)
            {
                if (Transfer<int>("PendingCount") != 0 || (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null) != 0) return;
                Static("PaintHistoryStore", "FlushWrites"); drainMs = (Stopwatch.GetTimestamp() - releaseAt) * 1000.0 / Stopwatch.Frequency;
                if (skip && Unity.Burst.BurstCompiler.Options.EnableBurstCompilation)
                    test.True(Transfer<bool>("LastComparisonUsedBurst"), "Do not accept a managed fallback as a Burst measurement");
                var step = Static("PaintHistoryStore", "Get", (string)Get(Get(fixture.layer, "PixelUndoObject"), "Revision"));
                var writers = new List<object>(); long raw = 0, packed = 0; int blocks = 0;
                foreach (var p in (Array)Get(step, "payload"))
                {
                    raw += (long)Get(p, "bytes"); packed += (long)Get(p, "fileBytes"); blocks += (int)Get(p, "blockCount");
                    double ms = 1000.0 / Stopwatch.Frequency;
                    writers.Add(new { blocks = Get(p, "blockCount"), raw = Get(p, "bytes"), packed = Get(p, "fileBytes"),
                        encodeMs = (long)Get(p, "encodeTicks") * ms, hashMs = (long)Get(p, "hashTicks") * ms,
                        queueMs = ((long)Get(p, "startedAt") - (long)Get(p, "queuedAt")) * ms,
                        workerMs = ((long)Get(p, "finishedAt") - (long)Get(p, "startedAt")) * ms });
                }
                strokes.Add(new { stroke, drawMs, releaseMs, drainMs, stagedAtRelease, writerPeak = Get(step, "peakPendingBytes"),
                    comparisonMs = (long)Get(step, "comparisonTicks") * 1000.0 / Stopwatch.Frequency,
                    extractionMs = (long)Get(step, "extractionTicks") * 1000.0 / Stopwatch.Frequency,
                    comparisonBurst = skip && Transfer<bool>("LastComparisonUsedBurst"),
                    sourceTiles = Get(fixture.layer, "LastPaintTileCount"), blocks, raw, packed, pages = Get(step, "pages"),
                    readback = Transfer<long>("ReadbackBytes") - readbackBefore, copied = Transfer<long>("TransferredBytes") - copyBefore,
                    waits = Transfer<long>("CapacityWaits") - waitsBefore, writers });
                Remember(); stroke++; point = 0;
                if (stroke < 2) phase = 0;
                else { StopProfile(); phase = 2; }
            }
            else if (phase == 2)
            {
                var watch = Stopwatch.StartNew(); Undo.PerformUndo(); undoMs = watch.Elapsed.TotalMilliseconds;
                VerifyRemembered(1); phase = 3;
            }
            else
            {
                var watch = Stopwatch.StartNew(); Undo.PerformRedo(); redoMs = watch.Elapsed.TotalMilliseconds;
                VerifyRemembered(2); test.Equal("Present", CoverageState(fixture.layer, side), "Final occupancy after subtile Redo"); Finish(null);
            }
        }
        string StoredHash()
        {
            var hash = new Hash128(); hash.Append(fixture.pixels.GetRawTextureData<byte>()); return hash.ToString();
        }
        void Remember()
        {
            storedHashes.Add(StoredHash()); var samples = new byte[probes.Length][];
            for (int i = 0; i < samples.Length; i++) samples[i] = Probe((RenderTexture)Get(fixture.layer, "paintSurface"), probes[i]);
            halfProbes.Add(samples);
        }
        void VerifyRemembered(int index)
        {
            test.Equal(storedHashes[index], StoredHash(), "Whole CPU storage hash at revision " + index);
            for (int i = 0; i < probes.Length; i++) Exact(test, halfProbes[index][i], Probe((RenderTexture)Get(fixture.layer, "paintSurface"), probes[i]), "Exact Half probe " + i);
        }
        void StopProfile()
        {
            if (!profiling) return;
            Static("WhimTexProfiler", "Stop", "test_complete"); profiling = false;
            var capture = Type("WhimTexProfiler").GetProperty("Current", S).GetValue(null);
            var events = (Array)Get(capture, "events"); int count = (int)Get(capture, "count");
            var used = Array.CreateInstance(events.GetType().GetElementType(), count); Array.Copy(events, used, count);
            var totals = new Dictionary<string, double[]>();
            foreach (var e in used)
            {
                string name = (string)Get(e, "phase");
                if ((string)Get(e, "kind") != "span" || name == null ||
                    !(name.StartsWith("brush.history.") || name.StartsWith("brush.readback."))) continue;
                if (!totals.TryGetValue(name, out var v)) totals.Add(name, v = new double[3]);
                double ms = ((long)Get(e, "end") - (long)Get(e, "start")) * 1000.0 / Stopwatch.Frequency;
                v[0]++; v[1] += ms; v[2] = Math.Max(v[2], ms);
            }
            foreach (var pair in totals) cpuPhases.Add(new { phase = pair.Key, count = pair.Value[0], totalMs = pair.Value[1], maxMs = pair.Value[2] });
            File.WriteAllText(path + "-profile.json", Json(new { frequency = Stopwatch.Frequency,
                capture = new { events = used, count, dropped = Get(capture, "dropped"), memory = Get(capture, "memory") } }));
        }
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true; EditorApplication.update -= Tick;
            try
            {
                StopProfile();
                if (path != null) File.WriteAllText(path + ".json", Json(new { implementation = "burst-words-v2", side, blockSize, skip, wide, strokes, storedHashes, undoMs, redoMs, cpuPhases,
                    gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), error = error?.ToString() }));
            }
            catch (Exception e) { error ??= e; }
            finally
            {
                try { fixture?.Dispose(); if (mask != null) Object.DestroyImmediate(mask); }
                catch (Exception e) { error ??= e; }
                finally { try { disk?.Dispose(); } catch (Exception e) { error ??= e; } }
            }
            state[1] = TestContext.Result(error == null ? "passed" : "failed", test.Checks, "Owned Undo block-size comparison",
                error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
        }
    }
}
