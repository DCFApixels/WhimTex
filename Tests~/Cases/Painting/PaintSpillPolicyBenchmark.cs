using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEditor;
using WhimTex.Tests;

// Isolated CPU history replay. No production scheduler, GPU, user history or settings are changed.
public static class PaintSpillPolicyBenchmark
{
    const long MiB = 1024 * 1024;
    const string Key = "WhimTex.Tests.SpillPolicy";
    static readonly System.Reflection.MethodInfo HashPage = typeof(DCFApixels.WhimTex.DrawingLayerBehaviour).Assembly
        .GetType("DCFApixels.WhimTex.PaintHistoryStore", true).GetMethod("HashPage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    static byte[] Checksum(Stream stream) => (byte[])HashPage.Invoke(null, new object[] { stream });
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];
    sealed class Segment { internal int offset, size, bpp; }
    sealed class Source : IDisposable
    {
        internal NativeArray<byte> bytes; internal readonly List<Segment> segments = new();
        public void Dispose() { if (bytes.IsCreated) bytes.Dispose(); }
    }
    sealed class Work { internal Source source; internal NativeArray<byte> bytes; internal int id; }
    [Serializable] sealed class Record
    {
        public string policy, mode, schedule; public int repetition, steps, pages, checkedPages;
        public long fixtureBytes, peakResidentBytes, idlePoolBytes, writtenBytes, bytesBeforeFlush, retainedBeforeFlush, workspaceBytes;
        public double producerMs, blockedMs, maxBlockMs, p95BlockMs, copyMs, foregroundMs, drainMs, elapsedMs, verifyMs;
        public bool exact, oldestFirst;
    }
    [Serializable] sealed class Report { public string status, error, corpus; public List<Record> records = new(); }
    public static string Start(string runId, int side, string mode)
        => StartSchedule(runId, side, mode, "all");
    public static string StartSchedule(string runId, int side, string mode, string schedule)
        => StartRange(runId, side, mode, schedule, 0, 2);
    public static string StartRepeat(string runId, int side, string mode, string schedule, int repetition)
        => StartRange(runId, side, mode, schedule, repetition, repetition + 1);
    static string StartRange(string runId, int side, string mode, string schedule, int first, int end)
    {
        if (first < 0 || end > 2 || first >= end) return TestContext.Result("failed", 0, "Invalid repetition range").ToJson();
        if (schedule != "all" && schedule != "short" && schedule != "paced" && schedule != "burst")
            return TestContext.Result("failed", 0, "Unknown replay schedule").ToJson();
        if (State != null) return TestContext.Result("failed", 0, "Another policy replay exists").ToJson();
        PaintCodecKernels.Initialize();
        var cancel = new CancellationTokenSource(); var state = new object[] { runId, null, cancel };
        EditorApplication.LockReloadAssemblies(); AppDomain.CurrentDomain.SetData(Key, state); state[1] = Task.Run(() =>
        {
            try { return Run(PaintCodecBenchmark.Load(side, mode, cancel.Token), schedule, first, end, cancel.Token); }
            catch (Exception error) { return TestContext.Result("failed", 0, "Policy corpus preparation", error.ToString()).ToJson(); }
        });
        return TestContext.Result("running", 0, "Replaying bounded history spill policies").ToJson();
    }
    public static string Poll(string runId)
    {
        if (State == null || (string)State[0] != runId) return TestContext.Result("failed", 0, "Missing policy replay").ToJson();
        var task = (Task<string>)State[1]; return task.IsCompleted ? task.GetAwaiter().GetResult() : TestContext.Result("running", 0, "Policy replay worker active").ToJson();
    }
    public static string Cleanup(string runId) => TestContext.Run("Owned spill worker cleanup", t =>
    {
        t.True(State == null || (string)State[0] == runId, "Matching replay"); if (State == null) return;
        ((CancellationTokenSource)State[2]).Cancel(); t.True(((Task)State[1]).Wait(5000), "Workers released");
        ((CancellationTokenSource)State[2]).Dispose(); AppDomain.CurrentDomain.SetData(Key, null); EditorApplication.UnlockReloadAssemblies();
    });
    static string Run(PaintCodecBenchmark.Corpus corpus, string selectedSchedule, int first, int end, CancellationToken cancel)
    {
        var report = new Report { status = "running", corpus = corpus.root }; var sources = new List<Source>();
        string root = Path.Combine(corpus.root, "spill-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var t = new TestContext();
        try
        {
            // Frozen fixture memory is reported separately. Replay copies pages into its bounded resident history.
            using (var workspace = new PaintCodecKernels.Workspace())
            {
                var buffer = new byte[workspace.packed.Length];
                foreach (var page in corpus.pages)
                {
                    if (page.stroke != 1) continue; cancel.ThrowIfCancellationRequested();
                    var source = new Source { bytes = new NativeArray<byte>(checked((int)page.rawBytes), Allocator.Persistent, NativeArrayOptions.UninitializedMemory) };
                    sources.Add(source); int at = 0;
                    PaintCodecBenchmark.Visit(corpus, page, workspace, buffer, (size, bpp, plane) =>
                    { source.segments.Add(new Segment { offset = at, size = size, bpp = bpp }); NativeArray<byte>.Copy(workspace.source, 0, source.bytes, at, size); at += size; });
                }
            }
            long fixture = 0; foreach (var source in sources) fixture += source.bytes.Length;
            t.True(fixture > 0 && fixture <= 2L * 1024 * MiB, "Bounded real-payload fixture");
            t.True(new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace > fixture * 6 + 64 * MiB,
                "Free disk covers the largest raw replay before compression");
            for (int repetition = first; repetition < end; repetition++)
            foreach (string schedule in selectedSchedule == "all" ? new[] { "short", "paced", "burst" } : new[] { selectedSchedule })
            for (int order = 0; order < 4; order++)
            {
                int policy = (order + repetition) % 4; cancel.ThrowIfCancellationRequested();
                string folder = Path.Combine(root, repetition + "-" + schedule + "-" + policy); Directory.CreateDirectory(folder);
                using var replay = new Replay(folder, policy, cancel);
                var row = replay.Execute(sources, schedule); row.fixtureBytes = fixture; row.mode = corpus.mode; row.repetition = repetition;
                t.True(row.exact && row.oldestFirst, "Exact payloads and oldest-first dispatch"); report.records.Add(row);
                // Delete only this run's verified output pages. Corpus and other sessions remain intact.
                foreach (string file in Directory.GetFiles(folder, "page-*.bin")) File.Delete(file);
                Directory.Delete(folder, false);
                File.WriteAllText(Path.Combine(root, "progress.json"), PaintCodecBenchmark.Json(report));
            }
            report.status = "passed";
        }
        catch (Exception error) { report.status = "failed"; report.error = error.ToString(); }
        finally { foreach (var source in sources) source.Dispose(); }
        string path = Path.Combine(root, "results.json"); File.WriteAllText(path, PaintCodecBenchmark.Json(report));
        if (report.status == "passed") File.WriteAllText(Path.Combine(corpus.root, "latest-spill.txt"), path);
        return TestContext.Result(report.status, t.Checks, path, report.error == null ? Array.Empty<string>() : new[] { report.error }).ToJson();
    }
    sealed class Replay : IDisposable
    {
        readonly object gate = new(); readonly Queue<Work> queue = new(); readonly List<Work> all = new();
        readonly List<NativeArray<byte>> pool = new(); readonly List<double> waits = new(); readonly List<int> dispatch = new();
        readonly Task[] workers = new Task[4]; readonly string root; readonly int policy; readonly CancellationToken cancel;
        readonly CountdownEvent ready = new(4);
        readonly long hard, soft, low; long resident, peak, poolBytes, written; bool force, stopping, spilling; Exception fault;
        double foregroundMs, copyMs; readonly PaintCodecKernels.Workspace foreground = new();
        internal Replay(string root, int policy, CancellationToken cancel)
        {
            this.root = root; this.policy = policy; this.cancel = cancel;
            hard = (policy == 0 ? 128 : 768) * MiB; soft = policy < 2 ? 0 : 512 * MiB; low = policy < 2 ? 0 : 384 * MiB;
            for (int i = 0; i < workers.Length; i++) workers[i] = Task.Run(Worker);
        }
        void Check() { cancel.ThrowIfCancellationRequested(); if (fault != null) throw new IOException("Spill worker failed", fault); }
        bool Ready()
        {
            if (!force && spilling && resident <= low) spilling = false;
            return queue.Count > 0 && (force || soft == 0 || spilling);
        }
        Work Take() { var work = queue.Dequeue(); dispatch.Add(work.id); return work; }
        void Worker()
        {
            try
            {
                using var w = new PaintCodecKernels.Workspace(); var buffer = new byte[w.packed.Length];
                ready.Signal();
                while (true)
                {
                    Work work;
                    lock (gate)
                    {
                        while (!stopping && !Ready()) { Check(); Monitor.Wait(gate, 50); }
                        if (stopping) return; Check(); work = Take();
                    }
                    Write(work, w, buffer);
                }
            }
            catch (Exception error) { lock (gate) { fault ??= error; Monitor.PulseAll(gate); } }
        }
        void Write(Work work, PaintCodecKernels.Workspace w, byte[] buffer)
        {
            string path = Path.Combine(root, "page-" + work.id + ".bin");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[32]);
                foreach (var s in work.source.segments)
                {
                    NativeArray<byte>.Copy(work.bytes, s.offset, w.source, 0, s.size);
                    int count = w.Encode(0, false, s.size, s.bpp); writer.Write(count);
                    NativeArray<byte>.Copy(w.packed, 0, buffer, 0, Math.Abs(count)); writer.Write(buffer, 0, Math.Abs(count));
                }
                writer.Flush(); long size = stream.Length; stream.Position = 32;
                byte[] checksum = Checksum(stream); stream.Position = 0; writer.Write(checksum);
                Interlocked.Add(ref written, size);
            }
            lock (gate)
            {
                resident -= work.bytes.Length;
                if (poolBytes + work.bytes.Length <= 64 * MiB) { pool.Add(work.bytes); poolBytes += work.bytes.Length; }
                else work.bytes.Dispose(); work.bytes = default; Monitor.PulseAll(gate);
            }
        }
        void Add(Source source)
        {
            long began = Stopwatch.GetTimestamp(); bool blocked = false;
            while (true)
            {
                Work help = null;
                lock (gate)
                {
                    Check();
                    if (resident + source.bytes.Length <= hard) { resident += source.bytes.Length; peak = Math.Max(peak, resident); break; }
                    blocked = true; spilling = true; Monitor.PulseAll(gate);
                    if (policy == 3 && queue.Count > 0) help = Take(); else Monitor.Wait(gate, 5);
                }
                if (help != null)
                {
                    long start = Stopwatch.GetTimestamp(); Write(help, foreground, scratch); foregroundMs += Milliseconds(start);
                }
            }
            if (blocked) waits.Add(Milliseconds(began));
            NativeArray<byte> bytes = default;
            lock (gate) for (int i = pool.Count - 1; i >= 0; i--) if (pool[i].Length == source.bytes.Length)
            { bytes = pool[i]; pool.RemoveAt(i); poolBytes -= bytes.Length; break; }
            long copy = Stopwatch.GetTimestamp();
            if (!bytes.IsCreated) bytes = new NativeArray<byte>(source.bytes.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(source.bytes, bytes); copyMs += Milliseconds(copy);
            var work = new Work { source = source, bytes = bytes, id = all.Count }; all.Add(work);
            lock (gate) { queue.Enqueue(work); if (resident >= soft) spilling = true; Monitor.PulseAll(gate); }
        }
        readonly byte[] scratch = new byte[PaintCodecKernels.Workspace.Maximum + PaintCodecKernels.Workspace.Maximum / 128 + 256];
        internal Record Execute(List<Source> sources, string schedule)
        {
            var row = new Record { policy = new[] { "immediate-128", "immediate-768", "soft512-hard768", "soft512-hard768-help" }[policy], schedule = schedule,
                steps = schedule == "short" ? 1 : schedule == "paced" ? 3 : 6,
                workspaceBytes = 5L * (6 * PaintCodecKernels.Workspace.Maximum + 2 * (PaintCodecKernels.Workspace.Maximum / 128 + 256) + 16384 * 4 + 4) };
            // Thread-pool startup and workspace allocation are outside the queue-policy measurement.
            while (!ready.Wait(50)) { lock (gate) Check(); }
            long start = Stopwatch.GetTimestamp();
            for (int step = 0; step < row.steps; step++)
            {
                int pages = schedule == "short" ? Math.Min(8, sources.Count) : sources.Count;
                for (int i = 0; i < pages; i++) Add(sources[i]);
                if (schedule == "paced" && step + 1 < row.steps && cancel.WaitHandle.WaitOne(750)) cancel.ThrowIfCancellationRequested();
            }
            row.producerMs = Milliseconds(start); row.pages = all.Count;
            lock (gate) { Check(); row.bytesBeforeFlush = written; row.retainedBeforeFlush = resident; force = true; Monitor.PulseAll(gate); }
            long drain = Stopwatch.GetTimestamp();
            lock (gate) { while (resident > 0) { Check(); Monitor.Wait(gate, 10); } }
            row.drainMs = Milliseconds(drain); row.elapsedMs = Milliseconds(start); row.copyMs = copyMs; row.foregroundMs = foregroundMs;
            row.peakResidentBytes = peak; row.idlePoolBytes = poolBytes; row.writtenBytes = written;
            waits.Sort(); foreach (double value in waits) row.blockedMs += value;
            row.maxBlockMs = waits.Count == 0 ? 0 : waits[waits.Count - 1]; row.p95BlockMs = waits.Count == 0 ? 0 : waits[(int)((waits.Count - 1) * .95)];
            row.oldestFirst = dispatch.Count == all.Count;
            for (int i = 0; i < dispatch.Count; i++) row.oldestFirst &= dispatch[i] == i;
            long verify = Stopwatch.GetTimestamp(); row.exact = true;
            foreach (var work in all)
            {
                Check(); using var stream = File.OpenRead(Path.Combine(root, "page-" + work.id + ".bin")); using var reader = new BinaryReader(stream);
                byte[] expected = reader.ReadBytes(32), actual = Checksum(stream);
                if (!expected.AsSpan().SequenceEqual(actual)) throw new Exception("Replay page checksum changed");
                stream.Position = 32;
                foreach (var s in work.source.segments)
                {
                    int count = reader.ReadInt32(), at = 0, length = Math.Abs(count);
                    if (!(count == -s.size || count > 0 && count < s.size)) throw new Exception("Invalid replay blob");
                    while (at < length) { int n = reader.Read(scratch, at, length - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                    NativeArray<byte>.Copy(scratch, 0, foreground.packed, 0, length); foreground.Decode(0, false, count, s.size, s.bpp);
                    if (!PaintCodecBenchmark.Same(foreground.restored, 0, work.source.bytes, s.offset, s.size)) throw new Exception("Replay changed payload bytes");
                }
                if (stream.Position != stream.Length) throw new Exception("Trailing replay bytes"); row.checkedPages++;
            }
            row.verifyMs = Milliseconds(verify); return row;
        }
        static double Milliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        public void Dispose()
        {
            lock (gate) { stopping = true; Monitor.PulseAll(gate); }
            Task.WaitAll(workers);
            foreach (var work in all) if (work.bytes.IsCreated) work.bytes.Dispose();
            foreach (var bytes in pool) if (bytes.IsCreated) bytes.Dispose(); foreground.Dispose(); ready.Dispose();
        }
    }
}
