using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using DCFApixels.WhimTex;
using WhimTex.Tests;

public static class PaintReleaseTests
{
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Field(object target, string name) => target.GetType().GetField(name, F).GetValue(target);
    public static string Roundtrip() => TestContext.Run("Exact bounded paint block codec", t =>
    {
        var type = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryCodec", true);
        using var codec = (IDisposable)Activator.CreateInstance(type, true);
        var write = type.GetMethod("Write", F); var read = type.GetMethod("Read", F);
        var random = new Random(371);
        foreach (int size in new[] { 1, 4, 7, 12, 15, 16, 17, 255, 256, 257, 65535, 65536, 65537, 524288 })
        for (int pattern = 0; pattern < 6; pattern++)
        {
            var bytes = new byte[size]; random.NextBytes(bytes);
            for (int i = 0; i < size; i++)
            {
                if (pattern == 0) bytes[i] = 0;
                if (pattern == 1) bytes[i] = 255;
                if (pattern == 2) bytes[i] = (byte)(i % 37);
                if (pattern == 3 && i % 2048 > 1024) bytes[i] = 0;
                if (pattern == 4 && i >= 65520) bytes[i] = bytes[i - 65520];
            }
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) write.Invoke(codec, new object[] { writer, bytes });
            t.True(stream.Length <= size + 4, "Raw fallback bounds storage expansion");
            stream.Position = 0;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            object[] args = { reader, null, size }; read.Invoke(codec, args);
            t.True(bytes.AsSpan().SequenceEqual((byte[])args[1]), "All bytes preserved, including arbitrary Half bit patterns");
            t.Equal(stream.Length, stream.Position, "Payload consumed exactly");
            byte[] truncated = stream.ToArray(); Array.Resize(ref truncated, truncated.Length - 1);
            Reject(truncated, size);
        }
        Reject(new byte[] { 0, 0, 0, 0 }, 32);
        Reject(new byte[] { 3, 0, 0, 0, 0, 0, 0 }, 32); // Zero match offset.
        Reject(new byte[] { 2, 0, 0, 0, 240, 255 }, 32); // Oversized literal run.
        void Reject(byte[] payload, int size)
        {
            using var stream = new MemoryStream(payload); using var reader = new BinaryReader(stream);
            bool rejected = false;
            try { read.Invoke(codec, new object[] { reader, null, size }); }
            catch (TargetInvocationException error) when (error.InnerException is IOException || error.InnerException is InvalidDataException) { rejected = true; }
            t.True(rejected, "Malformed or truncated payload rejected before use");
        }
    });
    public static string InspectWriters() => TestContext.Run("Read completed writer timings", t =>
    {
        var type = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryStore", true);
        type.GetMethod("EnsureLoaded", S).Invoke(null, null);
        var steps = (IDictionary)type.GetField("steps", S).GetValue(null);
        object last = null;
        foreach (object step in steps.Values)
            if (last == null || (long)Field(step, "order") > (long)Field(last, "order")) last = step;
        t.True(last != null, "Existing history step");
        var rows = new List<object>();
        foreach (object page in (Array)Field(last, "payload"))
        {
            if (page == null) continue;
            var write = (System.Threading.Tasks.Task)Field(page, "write");
            if (write == null || !write.IsCompleted) continue;
            double ms = 1000.0 / Stopwatch.Frequency;
            rows.Add(new { bytes = Field(page, "bytes"), fileBytes = Field(page, "fileBytes"), compressed = Field(page, "compressed"),
                queueMs = ((long)Field(page, "startedAt") - (long)Field(page, "queuedAt")) * ms,
                writeMs = ((long)Field(page, "finishedAt") - (long)Field(page, "startedAt")) * ms,
                sampleMs = (long)Field(page, "sampleTicks") * ms,
                encodeMs = (long)Field(page, "encodeTicks") * ms, hashMs = (long)Field(page, "hashTicks") * ms });
        }
        var jsonType = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
        var json = (string)jsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { rows });
        string directory = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Temp/WhimTex"));
        File.WriteAllText(Path.Combine(directory, "release-writers-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"), json);
    });
    static List<byte[]> samples;
    static void Collect(object block)
    {
        foreach (string name in new[] { "beforeHalf", "afterHalf", "beforeStored", "afterStored" })
            samples.Add((byte[])((byte[])Field(block, name)).Clone());
    }
    public static string BenchmarkCodecs() => TestContext.Run("Compare bounded journal packing", t =>
    {
        var type = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryStore", true);
        type.GetMethod("EnsureLoaded", S).Invoke(null, null);
        var steps = (IDictionary)type.GetField("steps", S).GetValue(null); object last = null;
        foreach (object step in steps.Values) if (last == null || (long)Field(step, "order") > (long)Field(last, "order")) last = step;
        t.True(last != null, "Existing journal");
        var visit = type.GetMethod("VisitPage", S);
        var callback = Delegate.CreateDelegate(visit.GetParameters()[2].ParameterType, typeof(PaintReleaseTests).GetMethod("Collect", S));
        samples = new List<byte[]>(); var rows = new List<object>();
        try
        {
            foreach (int index in new[] { 0, (int)Field(last, "pages") / 2, (int)Field(last, "pages") - 1 })
            {
                samples.Clear(); visit.Invoke(null, new object[] { last, index, callback, null });
                foreach (string mode in new[] { "blocks", "deflate", "raw" })
                {
                    using var output = new MemoryStream(32 * 1024 * 1024); using var pack = (IDisposable)Activator.CreateInstance(typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryCodec", true), true);
                    var encode = pack.GetType().GetMethod("Write", F); var clock = Stopwatch.StartNew();
                    using (Stream zip = mode.Contains("deflate") ? new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true) : null)
                    using (var writer = new BinaryWriter((Stream)zip ?? output, System.Text.Encoding.UTF8, true))
                        foreach (byte[] bytes in samples) { if (mode == "blocks") { encode.Invoke(pack, new object[] { writer, bytes }); } else writer.Write(bytes); }
                    double elapsed = clock.Elapsed.TotalMilliseconds;
                    if (mode == "blocks")
                    {
                        output.Position = 0; using var reader = new BinaryReader(output, System.Text.Encoding.UTF8, true);
                        var decode = pack.GetType().GetMethod("Read", F);
                        foreach (byte[] bytes in samples)
                        {
                            object[] args = { reader, null, bytes.Length }; decode.Invoke(pack, args);
                            var restored = (byte[])args[1];
                            t.True(bytes.AsSpan().SequenceEqual(restored), "Packed page byte identity");
                        }
                    }
                    rows.Add(new { index, mode, ms = elapsed, length = output.Length, burst = pack.GetType().GetProperty("LastUsedBurst", F).GetValue(pack) });
                }
            }
            var jsonType = Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true);
            string json = (string)jsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { rows });
            File.WriteAllText(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../Temp/WhimTex/release-codecs.json")), json);
        }
        finally { samples = null; }
    });
}
