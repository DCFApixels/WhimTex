using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEditor;
using WhimTex.Tests;

public static class PaintCodecBenchmark
{
    const string Key = "WhimTex.Tests.CodecBenchmark";
    static object[] State => AppDomain.CurrentDomain.GetData(Key) as object[];
    [Serializable] public sealed class Page { public string file, sha256; public int stroke, blocks; public long rawBytes, fileBytes; }
    [Serializable] public sealed class Corpus { public int version, side, bpp, strokes; public string mode, error; public Page[] pages; [NonSerialized] public string root; }
    [Serializable] public sealed class Row
    {
        public string mode, codec; public int pass, plane; public bool shuffle; public long blocks, rawBytes, packedBytes, rawBlocks, encodeTicks, decodeTicks;
    }
    [Serializable] sealed class Report { public string status, error, corpus; public long frequency; public int lz4Version, passes; public List<Row> rows = new(); }
    static Type JsonType => Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
    public static string Json(object value) => (string)JsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new[] { value });
    public static bool Same(NativeArray<byte> a, int offsetA, NativeArray<byte> b, int offsetB, int size)
        => PaintCodecKernels.Same(a, offsetA, b, offsetB, size);
    public static Corpus Load(int side, string mode, CancellationToken cancel = default)
    {
        string root = File.ReadAllText(Path.GetFullPath("Temp/WhimTex/codec-comparison/latest-" + side + "-" + mode + ".txt")).Trim();
        string allowed = Path.GetFullPath("Temp/WhimTex/codec-comparison/corpus") + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new Exception("Corpus outside diagnostic directory");
        var corpus = (Corpus)JsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { File.ReadAllText(Path.Combine(root, "manifest.json")), typeof(Corpus) }); corpus.root = root;
        bool completedDetail = mode == "detail" && corpus.strokes == 1 && corpus.pages?.Length == 64
            && corpus.error != null && corpus.error.Contains("OperationCanceledException");
        if (corpus.version != 1 || corpus.side != side || corpus.mode != mode ||
            !completedDetail && (corpus.strokes != 2 || !string.IsNullOrEmpty(corpus.error))) throw new Exception("Incomplete corpus");
        if (completedDetail) foreach (var page in corpus.pages) if (page.stroke != 0) throw new Exception("Only the fully captured first detail stroke may be measured");
        foreach (var page in corpus.pages)
        {
            cancel.ThrowIfCancellationRequested();
            if (Path.GetFileName(page.file) != page.file) throw new Exception("Invalid page name");
            using var file = File.OpenRead(Path.Combine(root, page.file)); using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (hash != page.sha256 || file.Length != page.fileBytes) throw new Exception("Corpus authentication failed");
        }
        return corpus;
    }
    // Source page decoding and validation are outside measured candidate-codec intervals.
    public static void Visit(Corpus corpus, Page page, PaintCodecKernels.Workspace work, byte[] buffer, Action<int, int, int> block)
    {
        using var file = File.OpenRead(Path.Combine(corpus.root, page.file)); using var reader = new BinaryReader(file);
        if (reader.ReadInt32() != 0x57545035) throw new Exception("Unsupported corpus format");
        file.Position = 36;
        if (reader.ReadInt32() != 0x57545035) throw new Exception("Invalid corpus payload");
        int blocks = reader.ReadInt32(); if (blocks != page.blocks) throw new Exception("Block count mismatch");
        long raw = 0;
        for (int i = 0; i < blocks; i++)
        {
            int x = reader.ReadInt32(), y = reader.ReadInt32(), width = reader.ReadInt32(), height = reader.ReadInt32();
            if (width < 1 || width > 256 || height < 1 || height > 256 || x < 0 || y < 0 || x + width > corpus.side || y + height > corpus.side) throw new Exception("Invalid block bounds");
            for (int plane = 0; plane < 4; plane++)
            {
                int bpp = plane < 2 ? 8 : corpus.bpp, size = checked(width * height * bpp), count = reader.ReadInt32(), bytes = Math.Abs(count);
                if (!(count == -size || count > 0 && count < size)) throw new Exception("Invalid blob");
                int at = 0; while (at < bytes) { int n = reader.Read(buffer, at, bytes - at); if (n == 0) throw new EndOfStreamException(); at += n; }
                NativeArray<byte>.Copy(buffer, 0, work.packed, 0, bytes); work.Decode(0, false, count, size, bpp);
                NativeArray<byte>.Copy(work.restored, work.source, size);
                work.Decode(1, false, count, size, bpp);
                if (!Same(work.source, 0, work.restored, 0, size)) throw new Exception("Frozen source block differs from native LZ4 decoding");
                raw += size; block(size, bpp, plane);
            }
        }
        if (raw != page.rawBytes || file.Position != file.Length) throw new Exception("Page size mismatch");
    }
    public static string Start(string runId, int side, string mode)
        => StartCore(runId, side, mode, false);
    public static string StartLz4(string runId, int side, string mode)
        => StartCore(runId, side, mode, true);
    static string StartCore(string runId, int side, string mode, bool lz4Only)
    {
        if (AppDomain.CurrentDomain.GetData("WhimTex.Tests.CodecPause") is true)
            return TestContext.Result("skipped", 0, "New codec runs paused for Editor coordination").ToJson();
        if (State != null) return TestContext.Result("failed", 0, "Another codec benchmark exists").ToJson();
        PaintCodecKernels.Initialize();
        var cancel = new CancellationTokenSource(); var state = new object[] { runId, null, cancel };
        EditorApplication.LockReloadAssemblies(); AppDomain.CurrentDomain.SetData(Key, state);
        state[1] = Task.Run(() =>
        {
            try { return Measure(Load(side, mode, cancel.Token), cancel.Token, lz4Only); }
            catch (Exception error) { return TestContext.Result("failed", 0, "Corpus preparation", error.ToString()).ToJson(); }
        });
        return TestContext.Result("running", 0, "Measuring actual " + mode + " blocks").ToJson();
    }
    public static string Poll(string runId)
    {
        if (State == null || (string)State[0] != runId) return TestContext.Result("failed", 0, "Missing codec benchmark").ToJson();
        var task = (Task<string>)State[1]; return task.IsCompleted ? task.GetAwaiter().GetResult() : TestContext.Result("running", 0, "Codec comparison running on worker").ToJson();
    }
    public static string Cleanup(string runId) => TestContext.Run("Codec worker cleanup", t =>
    {
        t.True(State == null || (string)State[0] == runId, "Matching worker");
        if (State == null) return;
        ((CancellationTokenSource)State[2]).Cancel(); var task = State[1] as Task;
        t.True(task == null || task.Wait(5000), "Worker finished before release");
        ((CancellationTokenSource)State[2]).Dispose(); AppDomain.CurrentDomain.SetData(Key, null); EditorApplication.UnlockReloadAssemblies();
    });
    static string Measure(Corpus corpus, CancellationToken cancel, bool lz4Only)
    {
        var report = new Report { status = "running", corpus = corpus.root, frequency = Stopwatch.Frequency, lz4Version = 10901, passes = 3 };
        var variants = lz4Only ? new[] { 0, 2 } : new[] { 0, 1, 2, 3, 4, 5 };
        var t = new TestContext(); string stem = lz4Only ? "lz4" : "codec";
        string path = Path.Combine(corpus.root, stem + "-results.json");
        try
        {
            using var w = new PaintCodecKernels.Workspace(); var buffer = new byte[w.packed.Length]; long blockIndex = 0;
            for (int pass = -1; pass < 3; pass++)
            {
                var rows = new Row[variants.Length, 4];
                for (int index = 0; index < variants.Length; index++) for (int plane = 0; plane < 4; plane++)
                {
                    int v = variants[index];
                    rows[index, plane] = new Row { mode = corpus.mode,
                        codec = v / 2 == 0 ? "current" : v / 2 == 1 ? "lz4-1.9.1" : "byte-packbits", shuffle = v % 2 == 1, pass = pass, plane = plane };
                }
                int pageLimit = pass < 0 ? 1 : corpus.pages.Length;
                for (int page = 0; page < pageLimit; page++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Visit(corpus, corpus.pages[page], w, buffer, (size, bpp, plane) =>
                    {
                        for (int k = 0; k < variants.Length; k++)
                        {
                            int index = (int)((blockIndex + k) % variants.Length), v = variants[index], codec = v / 2; bool shuffle = v % 2 == 1;
                            long start = Stopwatch.GetTimestamp(); int count = w.Encode(codec, shuffle, size, bpp); long encode = Stopwatch.GetTimestamp() - start;
                            start = Stopwatch.GetTimestamp(); w.Decode(codec, shuffle, count, size, bpp); long decode = Stopwatch.GetTimestamp() - start;
                            t.True(Same(w.source, 0, w.restored, 0, size), "Byte-exact roundtrip");
                            if (lz4Only)
                            {
                                w.Decode(1 - codec, false, count, size, bpp);
                                t.True(Same(w.source, 0, w.restored, 0, size), "Real block cross-decodes with native LZ4");
                            }
                            var r = rows[index, plane]; r.blocks++; r.rawBytes += size; r.packedBytes += Math.Abs(count) + 4;
                            if (count < 0) r.rawBlocks++; r.encodeTicks += encode; r.decodeTicks += decode;
                        }
                        blockIndex++;
                    });
                }
                if (pass >= 0) foreach (var row in rows) report.rows.Add(row);
                File.WriteAllText(Path.Combine(corpus.root, stem + "-progress.json"), Json(report));
            }
            report.status = "passed";
        }
        catch (Exception error) { report.status = "failed"; report.error = error.ToString(); }
        File.WriteAllText(path, Json(report));
        return TestContext.Result(report.status, t.Checks, "Codec results: " + path, report.error == null ? Array.Empty<string>() : new[] { report.error }).ToJson();
    }
}
