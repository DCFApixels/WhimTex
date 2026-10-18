using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static partial class PaintTileHistoryTests
{
    const string ContinuationKey = "WhimTex.Tests.PaintUndoContinuation";
    static object[] ContinuationState => AppDomain.CurrentDomain.GetData(ContinuationKey) as object[];
    public static string StartContinuation(string id, int size, int diameter, bool cleanup, bool textured)
        => StartContinuationCore(id, size, diameter, cleanup, textured, "normal");
    public static string StartStartupIsolation(string id, int size, string mode)
        => StartContinuationCore(id, size, 81, mode.StartsWith("cleanup", StringComparison.Ordinal), true, mode);
    static string StartContinuationCore(string id, int size, int diameter, bool cleanup, bool textured, string mode)
    {
        if (ContinuationState != null) return TestContext.Result("failed", 0, "Another continuation test exists").ToJson();
        var state = new object[] { id, TestContext.Result("running", 0, "Starting continuation test").ToJson(), null };
        AppDomain.CurrentDomain.SetData(ContinuationKey, state);
        var run = new ContinuationRun(state, size, diameter, cleanup, textured, mode);
        state[2] = (Action)(() => run.Finish(new OperationCanceledException("Owned diagnostic cancelled")));
        try { run.Start(); } catch (Exception error) { run.Finish(error); }
        return (string)state[1];
    }
    public static string PollContinuation(string id) => ContinuationState != null && (string)ContinuationState[0] == id
        ? (string)ContinuationState[1] : TestContext.Result("failed", 0, "Missing continuation diagnostic").ToJson();
    public static string CancelContinuation(string id)
    {
        if (ContinuationState != null && (string)ContinuationState[0] == id) ((Action)ContinuationState[2])();
        return TestContext.Result("cancelled", 0, "Owned continuation cancelled").ToJson();
    }
    public static string CleanupContinuation(string id) => TestContext.Run("Clean owned continuation diagnostic", t =>
    {
        t.True(ContinuationState == null || (string)ContinuationState[0] == id, "Owned run identity");
        if (ContinuationState == null) return;
        ((Action)ContinuationState[2])(); AppDomain.CurrentDomain.SetData(ContinuationKey, null);
    });
    sealed class ContinuationFrame
    {
        public int stroke, point, pendingPages, collections;
        public bool workerRunning;
        public double prepare, paint, render, gpuWait, housekeeping, total, interval, beforeGpu, paintGpu;
    }
    sealed class ContinuationRun
    {
        readonly object[] state;
        readonly int size, diameter;
        readonly bool cleanup, textured;
        readonly string mode;
        readonly TestContext test = new TestContext();
        readonly List<ContinuationFrame> frames = new();
        readonly List<object> ends = new();
        readonly List<string> revisions = new();
        readonly Stopwatch lifetime = Stopwatch.StartNew();
        Fixture f;
        Texture2D mask, fence;
        RenderTexture preview, fenceTarget;
        RenderTexture heldStroke, heldPreview;
        IDisposable cache;
        Process process;
        object cursor, parameters;
        EditorApplication.CallbackFunction originalUpdate, measuredUpdate;
        int seed, stroke, point, group, overlaps, primeUpdates;
        double next, last, housekeeping, maxHousekeeping, gapStart, peakGap, warmedAt, warmToFirstMs;
        long privateBaseline;
        bool finished, warmed, primed, gap, primingWorkerStarted;
        string output;
        const int Strokes = 4, Points = 24;
        long PendingBytes => (long)Type("PaintHistoryStore").GetProperty("PendingBytes", S).GetValue(null);
        internal ContinuationRun(object[] state, int size, int diameter, bool cleanup, bool textured, string mode)
        { this.state = state; this.size = size; this.diameter = diameter; this.cleanup = cleanup; this.textured = textured; this.mode = mode; }
        internal void Start()
        {
            test.True(size >= 512 && size <= 8192 && diameter >= 32 && diameter <= 2048, "Bounded workload");
            test.True(Array.IndexOf(new[] { "normal", "control", "cleanup", "idle", "cleanup-wait", "cleanup-pool", "idle-pool",
                "control-fenced", "cleanup-fenced", "idle-fenced" }, mode) >= 0, "Known startup isolation");
            test.True(size < 8192 || SystemInfo.graphicsMemorySize >= 7000, "8K graphics memory guard");
            test.True(!(bool)Static("PaintHistoryStore", "HasActiveStroke"), "No active user stroke");
            test.Equal(50, (int)Get(Call(Type("PaintUndoDisk").GetProperty("Current", S).GetValue(null), "ReadLimits"), "steps"), "Existing limit is 50; do not change user settings");
            process = Process.GetCurrentProcess(); process.Refresh(); privateBaseline = process.PrivateMemorySize64;
            f = new Fixture(size, false, textured); mask = Mask(); cache = (IDisposable)New("EffectRenderCache");
            fence = new Texture2D(1, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.HideAndDontSave };
            fenceTarget = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/WhimTex/paint-undo-continuation"));
            Directory.CreateDirectory(directory);
            output = Path.Combine(directory, size + "-" + diameter + "-" + cleanup + "-" + textured +
                (mode == "normal" ? "" : "-" + mode) + ".json");
            // Wrap our own update callback only: preserve its production body and restore it on exit.
            originalUpdate = (EditorApplication.CallbackFunction)Delegate.CreateDelegate(typeof(EditorApplication.CallbackFunction),
                Type("PaintTileTransfer").GetMethod("Update", S));
            measuredUpdate = () =>
            {
                var watch = Stopwatch.StartNew();
                // Seed history across real frames; defer maintenance until the measured overlap.
                if (primed) originalUpdate(); else Static("PaintTileTransfer", "Process", false, int.MaxValue);
                double elapsed = watch.Elapsed.TotalMilliseconds;
                housekeeping += elapsed; maxHousekeeping = Math.Max(maxHousekeeping, elapsed);
            };
            EditorApplication.update -= originalUpdate; EditorApplication.update += measuredUpdate;
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            Save("prepared"); EditorApplication.QueuePlayerLoopUpdate();
        }
        bool WorkerRunning()
        {
            var work = Type("PaintHistoryStore").GetField("checkpointWork", S).GetValue(null);
            return work != null && ReferenceEquals(Get(work, "owner"), cursor) &&
                !((System.Threading.Tasks.Task)Get(work, "prepare")).IsCompleted;
        }
        void Save(string phase)
        {
            if (output == null) return;
            File.WriteAllText(output, Json(new { phase, size, diameter, cleanup, textured, mode, seed, overlaps, frames, ends, maxHousekeeping,
                primeUpdates, warmToFirstMs, primingWorkerStarted,
                timing = "One bounded step per real Editor update, four following strokes. CPU stages and 1x1 GPU completion fence, not presented FPS. Control partitions only this fixture's count metadata after 50; user limits/store remain untouched." }));
            UnityEngine.Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "WhimTex continuation diagnostic {0}: {1}; seeded={2}, measured={3}, frames={4}", state[0], phase, seed, stroke, frames.Count);
        }
        void Render()
        {
            if (preview != null) RenderTexture.ReleaseTemporary(preview);
            preview = (RenderTexture)Call(f.document, "RenderCanvasWithCache", 512, cache, true, f.layer);
        }
        void Fence()
        {
            Graphics.Blit(preview, fenceTarget); RenderTexture.active = fenceTarget;
            fence.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
        }
        Vector2 Point(int i)
        {
            double a = i * Math.PI * 2 / (Points - 1);
            return new Vector2(.5f + .46f * (float)Math.Sin(2 * a + stroke * .08),
                .5f + .46f * (float)Math.Sin(a + Math.PI / 4));
        }
        void BeforeReload() => Finish(new InvalidOperationException("Reload interrupted continuation diagnostic"));
        void Tick()
        {
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            try { Step(); } catch (Exception error) { Finish(error); }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                // Pending readbacks must advance even when the detached fixture has no repainting window.
                if (!finished) EditorApplication.QueuePlayerLoopUpdate();
            }
        }
        void Step()
        {
            if (finished) return;
            if (lifetime.Elapsed.TotalSeconds > 50) throw new TimeoutException("Bounded continuation diagnostic exceeded 50 seconds");
            process.Refresh();
            test.True(process.PrivateMemorySize64 - privateBaseline < 4L * 1024 * 1024 * 1024 &&
                process.PrivateMemorySize64 < SystemInfo.systemMemorySize * 1024L * 1024 * .6, "Stop before excessive process memory growth");
            double now = EditorApplication.timeSinceStartup;
            if (seed < 51)
            {
                if (Transfer<int>("PendingCount") != 0)
                {
                    // Preparation only: submit queued graphics work without depending on an Editor window repaint.
                    Fence();
                    return;
                }
                if (PendingBytes != 0) return;
                // The control retains the identical pixel revisions but puts subsequent operations
                // in a second count bucket owned by this fixture. No global limit is changed.
                if (seed == 50 && !cleanup) Set(cursor, "documentIdentity", Guid.NewGuid().ToString("N"));
                var frame = new[] { new Vector2(.005f, .005f), new Vector2(.995f, .005f),
                    new Vector2(.995f, .995f), new Vector2(.005f, .995f), new Vector2(.005f, .005f) };
                f.Stroke(seed < 4 ? frame : new[] { new Vector2(.35f + seed * .001f, .5f) }, mask, drain: false);
                cursor = Get(f.layer, "PixelUndoObject"); seed++;
                if (seed % 10 == 0 || seed == 51) Save("seeding");
                housekeeping = 0; return;
            }
            if (!warmed)
            {
                if (Transfer<int>("PendingCount") != 0) { Fence(); return; }
                if (PendingBytes != 0) return;
                Set(f.settings, "brushSize", (float)diameter); parameters = f.Parameters(mask);
                Render();
                if (mode.EndsWith("-pool", StringComparison.Ordinal))
                {
                    heldStroke = (RenderTexture)Static("HdrUtility", "Temporary", size, size, false);
                    heldPreview = (RenderTexture)Static("HdrUtility", "Temporary", size, size, false);
                    RenderTexture.active = heldStroke; GL.Clear(false, true, Color.clear);
                    RenderTexture.active = heldPreview; GL.Clear(false, true, Color.clear);
                }
                Fence(); warmed = true; Save("warm"); warmedAt = EditorApplication.timeSinceStartup; housekeeping = 0; return;
            }
            if (!primed)
            {
                primeUpdates++;
                if (cleanup)
                {
                    if (!primingWorkerStarted)
                    {
                        Static("PaintHistoryStore", "Maintenance", true);
                        if (!WorkerRunning()) return;
                        primingWorkerStarted = true;
                    }
                    if (mode == "cleanup-wait" && WorkerRunning()) return;
                }
                // Match the several-second priming gap needed to start real retention work,
                // while leaving retention disabled for this fixture.
                if (mode.StartsWith("idle", StringComparison.Ordinal) &&
                    (primeUpdates < 6 || now - warmedAt < 2.6)) return;
                primed = true; last = now;
                warmToFirstMs = (EditorApplication.timeSinceStartup - warmedAt) * 1000;
                ReleaseHeldPool();
            }
            if (gap)
            {
                peakGap = Math.Max(peakGap, housekeeping); housekeeping = 0;
                if (now < next) return;
                ends.Add(new { kind = "gap", afterStroke = stroke + 51, ms = (now - gapStart) * 1000, maximumWork = peakGap });
                gap = false; peakGap = 0;
            }
            if (stroke >= Strokes)
            {
                // Measurements are finished; submit the final asynchronous capture without a window repaint.
                if (Transfer<int>("PendingCount") != 0) { Fence(); return; }
                if (PendingBytes != 0) return;
                Verify(); Finish(null); return;
            }
            if (now < next) return;
            if (point == Points)
            {
                var watch = Stopwatch.StartNew(); bool worker = WorkerRunning();
                long waits = Transfer<long>("CapacityWaits"), reads = Transfer<long>("ReadbackBytes");
                Call(f.layer, "EndStroke"); Call(f.layer, "FinishStrokeStorage"); Call(f.document, "MarkChanged");
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group); Undo.IncrementCurrentGroup();
                ends.Add(new { kind = "commit", stroke = stroke + 52, ms = watch.Elapsed.TotalMilliseconds,
                    worker, pending = Transfer<int>("PendingCount"), staging = Transfer<long>("StagingBytes"),
                    capacityWaits = Transfer<long>("CapacityWaits") - waits,
                    readbackBytes = Transfer<long>("ReadbackBytes") - reads });
                revisions.Add((string)Get(cursor, "Revision")); stroke++; point = 0;
                gap = true; gapStart = EditorApplication.timeSinceStartup; next = gapStart + .05;
                Save("stroke-finished"); return;
            }
            var sample = new ContinuationFrame { stroke = stroke + 52, point = point, workerRunning = WorkerRunning(),
                pendingPages = Transfer<int>("PendingCount"), housekeeping = housekeeping, interval = (now - last) * 1000 };
            if (sample.workerRunning) overlaps++;
            int gc = GC.CollectionCount(0); var timer = Stopwatch.StartNew();
            if (point == 0 && mode.EndsWith("-fenced", StringComparison.Ordinal))
            { Fence(); sample.beforeGpu = timer.Elapsed.TotalMilliseconds; timer.Restart(); }
            if (point == 0)
            {
                Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup();
                Call(f.layer, "PrepareBufferedStroke", size, size, f.historyLabel); Call(f.layer, "BeginStroke", Point(0));
            }
            sample.prepare = timer.Elapsed.TotalMilliseconds; timer.Restart();
            if (point == 0) Call(f.layer, "PaintPoint", Point(0), size, size, parameters);
            else Call(f.layer, "PaintSegment", Point(point - 1), Point(point), size, size, false, parameters);
            sample.paint = timer.Elapsed.TotalMilliseconds; timer.Restart();
            if (point == 0 && mode.EndsWith("-fenced", StringComparison.Ordinal))
            { Fence(); sample.paintGpu = timer.Elapsed.TotalMilliseconds; timer.Restart(); }
            Render(); sample.render = timer.Elapsed.TotalMilliseconds; timer.Restart(); Fence();
            sample.gpuWait = timer.Elapsed.TotalMilliseconds;
            sample.total = sample.prepare + sample.paint + sample.render + sample.gpuWait + sample.housekeeping + sample.beforeGpu + sample.paintGpu;
            sample.collections = GC.CollectionCount(0) - gc; frames.Add(sample);
            housekeeping = 0; point++; last = now; next = now + 1.0 / 60;
        }
        void Verify()
        {
            test.True(!cleanup || (mode == "cleanup-wait" ? primingWorkerStarted : overlaps > 0), "Actual checkpoint worker ran in the intended phase");
            test.Equal(Points * Strokes, frames.Count, "Every input was rendered");
            test.Equal(f.historyLabel, Undo.GetCurrentGroupName(), "Do not undo an intervening user operation");
            // Probe the last stroke's first dab, not the unpainted middle of the curve.
            int x = Mathf.RoundToInt(size * (.5f + .46f * (float)Math.Sin((Strokes - 1) * .08)));
            int y = Mathf.RoundToInt(size * (.5f + .46f * (float)Math.Sin(Math.PI / 4)));
            var rect = new RectInt(Mathf.Clamp(x - 32, 0, size - 64), Mathf.Clamp(y - 32, 0, size - 64), 64, 64);
            byte[] final = Probe(f.surface, rect);
            for (int i = Strokes - 2; i >= Strokes - 4; i--)
            {
                Undo.PerformUndo(); test.Equal(revisions[i], (string)Get(cursor, "Revision"), "Following Undo order");
                if (i == Strokes - 2)
                {
                    byte[] undone = Probe((RenderTexture)Get(f.layer, "paintSurface"), rect);
                    bool changed = false;
                    for (int b = 0; b < final.Length; b++) changed |= final[b] != undone[b];
                    test.True(changed, "Probe covers pixels changed by the last stroke");
                }
            }
            for (int i = Strokes - 3; i < Strokes; i++)
            { Undo.PerformRedo(); test.Equal(revisions[i], (string)Get(cursor, "Revision"), "Following Redo order"); }
            Exact(test, final, Probe((RenderTexture)Get(f.layer, "paintSurface"), rect), "Following-stroke exact Half roundtrip");
        }
        void ReleaseHeldPool()
        {
            if (heldStroke != null) Static("ProfiledRenderTexture", "ReleaseTemporary", heldStroke);
            if (heldPreview != null) Static("ProfiledRenderTexture", "ReleaseTemporary", heldPreview);
            heldStroke = heldPreview = null;
        }
        internal void Finish(Exception error)
        {
            if (finished) return; finished = true;
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            if (measuredUpdate != null) { EditorApplication.update -= measuredUpdate; EditorApplication.update += originalUpdate; }
            try
            {
                ReleaseHeldPool();
                if (f != null)
                {
                    if (cursor != null) Static("PaintHistoryStore", "FinishCheckpointWork", cursor);
                    string owner = cursor == null ? null : (string)Get(cursor, "Identity");
                    string directory = cursor == null ? null : (string)Get(cursor, "StateDirectory");
                    var owned = new List<object>();
                    foreach (DictionaryEntry entry in (IDictionary)Type("PaintHistoryStore").GetField("steps", S).GetValue(null))
                        if ((string)Get(entry.Value, "owner") == owner) owned.Add(entry.Value);
                    if ((bool)Get(f.layer, "HasBufferedStroke")) Call(f.layer, "ReleaseAdvancedStroke");
                    f.Dispose();
                    foreach (var step in owned) Static("PaintHistoryStore", "DeleteStep", step);
                    if (directory != null) Call(Type("PaintUndoDisk").GetProperty("Current", S).GetValue(null), "DeleteOwned", directory);
                }
                if (preview != null) RenderTexture.ReleaseTemporary(preview);
                cache?.Dispose(); if (fenceTarget != null) RenderTexture.ReleaseTemporary(fenceTarget);
                if (fence != null) Object.DestroyImmediate(fence); if (mask != null) Object.DestroyImmediate(mask);
                process?.Dispose();
            }
            catch (Exception cleanupError) { error = error == null ? cleanupError : new AggregateException(error, cleanupError); }
            Save(error == null ? "passed" : "failed: " + error.Message);
            state[1] = TestContext.Result(error == null ? "passed" : "failed", test.Checks,
                "Continuation diagnostic", error == null ? Array.Empty<string>() : new[] { error.ToString() }).ToJson();
        }
    }
}
