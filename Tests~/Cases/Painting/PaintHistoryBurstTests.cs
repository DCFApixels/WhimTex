using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Uses the owned fixture in PaintTileHistoryTests. No windows or synthetic OS input.
public static partial class PaintTileHistoryTests
{
    const string BurstKey = "WhimTex.Tests.PaintHistoryBurst";
    static object[] BurstState => AppDomain.CurrentDomain.GetData(BurstKey) as object[];
    public static string StartBurst(string runId, int side, bool paced)
    {
        if (BurstState != null) return TestContext.Result("failed", 0, "A burst fixture already exists").ToJson();
        var state = new object[] { runId, TestContext.Result("running", 0, "Preparing owned history burst").ToJson(), null };
        AppDomain.CurrentDomain.SetData(BurstKey, state);
        var run = new HistoryBurst(state, side, paced);
        state[2] = (Action)(() => run.Finish(new OperationCanceledException()));
        try { run.Prepare(); } catch (Exception error) { run.Finish(error); }
        return (string)state[1];
    }
    public static string PollBurst(string runId) => BurstState != null && (string)BurstState[0] == runId
        ? (string)BurstState[1] : TestContext.Result("failed", 0, "Missing burst fixture").ToJson();
    public static string CancelBurst(string runId)
    {
        if (BurstState != null && (string)BurstState[0] == runId) ((Action)BurstState[2])();
        return TestContext.Result("cancelled", 0, "Owned burst cancelled").ToJson();
    }
    public static string CleanupBurst(string runId) => TestContext.Run("Owned burst cleanup", t =>
    {
        t.True(BurstState == null || (string)BurstState[0] == runId, "Only clean the matching fixture");
        if (BurstState == null) return;
        ((Action)BurstState[2])(); AppDomain.CurrentDomain.SetData(BurstKey, null);
    });
    sealed class HistoryBurst
    {
        readonly object[] state;
        readonly int side;
        readonly bool paced;
        readonly TestContext test = new();
        readonly List<object> frames = new();
        readonly List<byte[]> expected = new();
        Fixture fixture;
        DiskScope diskScope;
        Texture2D mask;
        object parameters;
        string path;
        bool finished, profiling;
        int point, stroke, group, phase, restore;
        int StrokeCount => paced ? 3 : 6;
        long maxStaging, maxPending, started, last, drainAt;
        double releaseMs, drainMs;
        RectInt ProbeRect => new RectInt(side / 2 - 16, side / 2 - 16, 32, 32);
        Vector2 Point(int i) => new Vector2((i / 2) % 2 == 0 ? (i % 2 == 0 ? .04f : .96f) : (i % 2 == 0 ? .96f : .04f), .06f + (i / 2) * .125f);
        internal HistoryBurst(object[] state, int side, bool paced) { this.state = state; this.side = side; this.paced = paced; }
        internal void Prepare()
        {
            test.True(side == 512 || side == 8192, "Bounded fixture size");
            test.True(side < 8192 || SystemInfo.graphicsMemorySize >= 7000, "8K needs 7 GB VRAM");
            test.True(!(bool)Type("WhimTexProfiler").GetProperty("Recording", S).GetValue(null), "No user capture running");
            test.Equal(0, Transfer<int>("PendingCount"), "No pending user transfer");
            test.Equal(0L, (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null), "No pending user writer");
            foreach (var window in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                test.True(Get(window, "paintingLayer") == null, "No active user stroke");
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-tile-history"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-burst-" + side + "-" + paced);
            var previous = Type("WhimTexProfiler").GetProperty("Current", S).GetValue(null);
            if (previous != null) File.WriteAllText(path + "-previous-profile.json", Json(previous));
            diskScope = new DiskScope();
            fixture = new Fixture(side, false); mask = Mask();
            Set(fixture.settings, "brushSize", side / 4f);
            parameters = fixture.Parameters(mask);
            expected.Add(Probe(fixture.surface, ProbeRect));
            Static("WhimTexProfiler", "Start", 131072, 120.0, null); profiling = true;
            started = last = Stopwatch.GetTimestamp();
            EditorApplication.update += Tick;
        }
        void Tick()
        {
            var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { Step(); } catch (Exception error) { Finish(error); }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
        }
        void Step()
        {
            if (Stopwatch.GetTimestamp() - started > Stopwatch.Frequency * 90) throw new TimeoutException("Owned burst exceeded 90 seconds");
            long now = Stopwatch.GetTimestamp(); double interval = (now - last) * 1000.0 / Stopwatch.Frequency; last = now;
            long waitBefore = Transfer<long>("CapacityWaits");
            var clock = Stopwatch.StartNew();
            if (phase == 0)
            {
                if (point == 0)
                {
                    Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                    Call(fixture.layer, "PrepareBufferedStroke", side, side, "Owned burst stroke");
                    Call(fixture.layer, "BeginStroke", Point(0));
                }
                int limit = paced ? point + 1 : 16;
                for (; point < limit; point++)
                {
                    if (point == 0) Call(fixture.layer, "PaintPoint", Point(0), side, side, parameters);
                    else Call(fixture.layer, "PaintSegment", Point(point - 1), Point(point), side, side, false, parameters);
                }
                // Separate submitted paint from release latency, with a one-pixel fence.
                using (Phase("test.paint.burstFence")) Probe(fixture.surface, new RectInt(0, 0, 1, 1));
                if (point == 16)
                {
                    var end = Stopwatch.StartNew(); Call(fixture.layer, "EndStroke"); releaseMs = end.Elapsed.TotalMilliseconds;
                    Call(fixture.document, "MarkChanged"); Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                    stroke++; point = 0; phase = 1;
                }
            }
            else if (phase == 1)
            {
                expected.Add(Probe(fixture.surface, ProbeRect));
                if (stroke < StrokeCount) phase = 0;
                else { phase = 2; drainAt = Stopwatch.GetTimestamp(); }
            }
            else if (phase == 2)
            {
                if (Transfer<int>("PendingCount") == 0 && (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null) == 0)
                {
                    Static("PaintHistoryStore", "FlushWrites");
                    string revision = (string)Get(Get(fixture.layer, "PixelUndoObject"), "Revision");
                    var step = Static("PaintHistoryStore", "Get", revision);
                    test.True((bool)Get(step, "reservationReleased"), "Last writer releases raw reservation without idle maintenance");
                    test.True(!File.Exists(Path.Combine((string)Static("PaintHistoryStore", "StepDirectory", revision), "reservation")), "Completed stroke is charged by actual disk bytes");
                    drainMs = (Stopwatch.GetTimestamp() - drainAt) * 1000.0 / Stopwatch.Frequency; phase = 3; restore = StrokeCount - 1;
                }
            }
            else if (phase == 3)
            {
                Undo.PerformUndo(); Exact(test, expected[restore], Probe((RenderTexture)Get(fixture.layer, "paintSurface"), ProbeRect), "Burst ordered exact Undo " + restore);
                if (--restore < 0) { phase = 4; restore = 1; }
            }
            else
            {
                Undo.PerformRedo(); Exact(test, expected[restore], Probe((RenderTexture)Get(fixture.layer, "paintSurface"), ProbeRect), "Burst ordered exact Redo " + restore);
                if (++restore == expected.Count) { Finish(null); return; }
            }
            long staging = Transfer<long>("StagingBytes"), queued = (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null);
            maxStaging = Math.Max(maxStaging, staging); maxPending = Math.Max(maxPending, queued);
            test.True(staging <= (long)Type("PaintTileTransfer").GetField("StagingBudget", S).GetValue(null), "Shared snapshot cap across repeated strokes");
            test.True(queued <= (long)Type("PaintHistoryStore").GetField("PendingBudget", S).GetValue(null), "Writer payload stays bounded");
            frames.Add(new { phase, stroke, point, intervalMs = interval, workMs = clock.Elapsed.TotalMilliseconds, releaseMs,
                staging, queued, waits = Transfer<long>("CapacityWaits") - waitBefore }); releaseMs = 0;
        }
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true; EditorApplication.update -= Tick;
            try
            {
                if (profiling)
                {
                    Static("WhimTexProfiler", "Stop", error == null ? "test_complete" : "test_failed"); profiling = false;
                    File.WriteAllText(path + "-profile.json", Json(new { frequency = Stopwatch.Frequency, capture = Type("WhimTexProfiler").GetProperty("Current", S).GetValue(null) }));
                }
                if (path != null) File.WriteAllText(path + ".json", Json(new { side, paced, frames, maxStaging, maxPending, drainMs, processorCount = Environment.ProcessorCount,
                    writerBudget = Type("PaintHistoryStore").GetField("PendingBudget", S).GetValue(null) }));
            }
            catch (Exception failure) { error ??= failure; }
            finally
            {
                try
                {
                    if (fixture != null)
                    {
                        var cursor = Get(fixture.layer, "PixelUndoObject");
                        string owner = cursor == null ? null : (string)Get(cursor, "Identity");
                        string directory = cursor == null ? null : (string)Get(cursor, "StateDirectory");
                        var owned = new List<object>();
                        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)Type("PaintHistoryStore").GetField("steps", S).GetValue(null))
                            if ((string)Get(entry.Value, "owner") == owner) owned.Add(entry.Value);
                        fixture.Dispose();
                        foreach (var step in owned) Static("PaintHistoryStore", "DeleteStep", step);
                        if (directory != null) Call(diskScope.disk, "DeleteOwned", directory);
                    }
                    if (mask != null) Object.DestroyImmediate(mask);
                }
                catch (Exception failure) { error ??= failure; }
                finally { try { diskScope?.Dispose(); } catch (Exception failure) { error ??= failure; } }
            }
            state[1] = TestContext.Result(error == null ? "passed" : "failed", test.Checks, "Owned repeated dense strokes",
                error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
        }
    }
}
