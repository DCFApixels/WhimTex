// run_script entry DocumentSaveCostTests.Hashes or .Container or .Tiff.
// Diagnostic only: no assets, scenes, imports, file writes, or changes to open documents.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using DCFApixels.WhimTex;
using Unity.Collections;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

public static class DocumentSaveCostTests
{
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    [Serializable] public sealed class Cost
    {
        public string operation, detail;
        public double ms;
        public long bytes;
    }
    [Serializable] public sealed class Report
    {
        public string unity = Application.unityVersion;
        public List<Cost> costs = new List<Cost>();
    }
    static void Measure(Report report, string name, Action action, long bytes = 0, string detail = null)
    {
        var watch = Stopwatch.StartNew(); action(); watch.Stop();
        report.costs.Add(new Cost { operation = name, ms = watch.Elapsed.TotalMilliseconds, bytes = bytes, detail = detail });
    }
    static byte[] Pixels(int mebibytes, int seed = 19, bool opaque = true)
    {
        if (mebibytes < 1 || mebibytes > 64) throw new ArgumentOutOfRangeException(nameof(mebibytes));
        var bytes = new byte[mebibytes * 1048576];
        uint state = (uint)seed;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            bytes[i] = (byte)state; bytes[i + 1] = (byte)(state >> 8); bytes[i + 2] = (byte)(state >> 16);
            bytes[i + 3] = opaque ? (byte)255 : (byte)(state >> 24);
        }
        return bytes;
    }
    static Report ExecuteHashes(int mebibytes = 64)
    {
        var report = new Report(); var bytes = Pixels(mebibytes);
        using var native = new NativeArray<byte>(bytes, Allocator.Persistent);
        byte[] expected = null, actual = null;
        using (var warmup = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) warmup.AppendData(new byte[4096]);
        for (int run = 0; run < 2; run++)
        {
            Measure(report, "incremental-native-" + run, () => {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(native.AsSpan()); actual = hash.GetHashAndReset();
            }, bytes.Length);
            if (expected == null) expected = actual; else Equal(expected, actual);
            Measure(report, "incremental-managed-" + run, () => {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(bytes); actual = hash.GetHashAndReset();
            }, bytes.Length); Equal(expected, actual);
            using (var hash = SHA256.Create())
            {
                Measure(report, "SHA256.Create-" + run, () => actual = hash.ComputeHash(bytes), bytes.Length, hash.GetType().FullName);
                Equal(expected, actual);
            }
            try
            {
                using var hash = new SHA256CryptoServiceProvider();
                Measure(report, "SHA256CSP-" + run, () => actual = hash.ComputeHash(bytes), bytes.Length, hash.GetType().FullName);
                Equal(expected, actual);
            }
            catch (PlatformNotSupportedException error) { report.costs.Add(new Cost { operation = "SHA256CSP", detail = error.Message }); }
            byte[] copy = null;
            Measure(report, "native-to-managed-" + run, () => copy = native.ToArray(), bytes.Length);
            GC.KeepAlive(copy);
            Measure(report, "native-snapshot-" + run, () => {
                using var snapshot = new NativeArray<byte>(native.Length, Allocator.Persistent);
                NativeArray<byte>.Copy(native, snapshot);
            }, bytes.Length);
        }
        return report;
    }
    static void Equal(byte[] a, byte[] b)
    {
        UnityBRun.Check(!(!a.AsSpan().SequenceEqual(b)), "Digest mismatch.");
    }
    static void StoredEqual(byte[] expected, object stored)
    {
        var type = stored.GetType();
        const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var bytes = (byte[])type.GetField("bytes", fields).GetValue(stored);
        bool compressed = (bool)type.GetField("compressed", fields).GetValue(stored);
        UnityBRun.Check((long)type.GetField("rawLength", fields).GetValue(stored) == expected.LongLength, "Stored block raw length");
        if (!compressed) { Equal(expected, bytes); return; }
        using var source = new MemoryStream(bytes, false);
        using var inflate = new DeflateStream(source, CompressionMode.Decompress);
        var scratch = new byte[65536]; int offset = 0, read;
        while ((read = inflate.Read(scratch, 0, scratch.Length)) > 0)
        {
            UnityBRun.Check(offset <= expected.Length - read, "Stored block does not inflate beyond input");
            UnityBRun.Check(scratch.AsSpan(0, read).SequenceEqual(expected.AsSpan(offset, read)), "Streamed stored pixels match input at " + offset);
            offset += read;
        }
        UnityBRun.Check(offset == expected.Length, "Stored block inflates to exact input length");
    }
    static Report ExecuteFastFingerprint()
    {
        var report = new Report(); var bytes = Pixels(64);
        using var native = new NativeArray<byte>(bytes, Allocator.Persistent);
        Hash128 fingerprint = default;
        for (int i = 0; i < 3; i++)
            Measure(report, "Hash128-native-" + i, () => fingerprint = Hash128.Compute(native), bytes.Length);
        using var stream = new MemoryStream();
        using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, true)) deflate.Write(bytes, 0, bytes.Length);
        byte[] stored = stream.ToArray();
        Measure(report, "inflate-exact-compare", () => {
            using var source = new MemoryStream(stored, false);
            using var inflate = new DeflateStream(source, CompressionMode.Decompress);
            byte[] scratch = new byte[65536]; int offset = 0, read;
            while ((read = inflate.Read(scratch, 0, scratch.Length)) > 0)
            {
                UnityBRun.Check(!(!scratch.AsSpan(0, read).SequenceEqual(native.AsSpan().Slice(offset, read))), "Mismatch.");
                offset += read;
            }
            UnityBRun.Check(!(offset != bytes.Length), "Length mismatch.");
        }, bytes.Length);
        GC.KeepAlive(fingerprint);
        return report;
    }
    // Count streamed bytes without retaining a second complete container in memory.
    sealed class Sink : Stream
    {
        long count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => count;
        public override long Position { get => count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int length) { count += length; }
        public override void Write(ReadOnlySpan<byte> buffer) { count += buffer.Length; }
        public override int Read(byte[] buffer, int offset, int length) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
    }
    static Report ExecuteContainer(int layers = 4, bool opaque = true)
    {
        if (layers < 1 || layers > 4) throw new ArgumentOutOfRangeException(nameof(layers));
        var report = new Report(); string prefix = "diagnostic-" + Guid.NewGuid().ToString("N");
        var arrays = new byte[layers][];
        for (int i = 0; i < layers; i++) arrays[i] = Pixels(64, 19 + i, opaque);
        var cache = (IDictionary)typeof(WhimTexDocumentContainer).GetField("CompressedCache", PrivateStatic).GetValue(null);
        var write = typeof(WhimTexDocumentContainer).GetMethod("WriteTo", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int run = 0; run < 3; run++)
        {
            int hits = 0;
            for (int i = 0; i < layers; i++) if (cache.Contains(prefix + i)) hits++;
            using var container = new WhimTexDocumentContainer();
            for (int i = 0; i < layers; i++) container.SetCompressed("texture:" + i, prefix + i, arrays[i]);
            using var sink = new Sink();
            Measure(report, "container-write-" + run, () => write.Invoke(container, new object[] { sink }),
                (long)layers * arrays[0].Length, "cache hits before write: " + hits);
            report.costs.Add(new Cost { operation = "stored-size-" + run, bytes = sink.Length });
            UnityBRun.Check(sink.Length > 0, "Streamed container is nonempty: run " + run);
            var prepared = (IDictionary)typeof(WhimTexDocumentContainer).GetField("_prepared",
                BindingFlags.NonPublic | BindingFlags.Instance).GetValue(container);
            for (int i = 0; i < layers; i++)
                StoredEqual(arrays[i], prepared["texture:" + i]);
        }
        return report;
    }
    static Report ExecuteTiff(int size = 4096)
    {
        if (size != 2048 && size != 4096) throw new ArgumentOutOfRangeException(nameof(size));
        var report = new Report(); var raw = Pixels(size * size * 4 / 1048576); byte[] image = null;
        Measure(report, "tiff-encode-opaque-random", () => image = WhimTexTiffImage.WriteRaw(size, size, raw, 8), raw.Length);
        var validate = typeof(WhimTexTiffImage).GetMethod("Validate", PrivateStatic);
        var args = new object[] { image, 0, 0, null };
        Measure(report, "tiff-validate", () => {
            UnityBRun.Check(!(!(bool)validate.Invoke(null, args)), (string)args[3]);
        }, image.Length);
        byte[] compressed = null;
        Measure(report, "deflate-single-layer", () => {
            using var stream = new MemoryStream();
            using (var compressor = new DeflateStream(stream, CompressionLevel.Fastest, true)) compressor.Write(raw, 0, raw.Length);
            compressed = stream.ToArray();
        }, raw.Length);
        Measure(report, "digest-compressed-layer", () => {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(compressed.AsSpan()); GC.KeepAlive(hash.GetHashAndReset());
        }, compressed.Length);
        return report;
    }

    static Report ExecuteCacheSafety()
    {
        var report = new Report();
        var texture = UnityBRun.Track(new Texture2D(32, 32, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave });
        try
        {
            void Count(string name) => report.costs.Add(new Cost { operation = name, bytes = texture.updateCount });
            Count("updateCount-created");
            texture.SetPixel(0, 0, Color.red); Count("updateCount-SetPixel");
            texture.Apply(false, false); Count("updateCount-Apply");
            var view = texture.GetRawTextureData<byte>(); view[0] ^= 1; Count("updateCount-raw-write-without-Apply");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        var write = typeof(WhimTexDocumentContainer).GetMethod("WriteTo", BindingFlags.NonPublic | BindingFlags.Instance);
        var cached = typeof(WhimTexDocumentContainer).GetMethod("TryReusePixels", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (bool opaque in new[] { true, false })
        {
            byte[] raw = Pixels(1, 19, opaque);
            string key = "diagnostic-native-" + Guid.NewGuid().ToString("N");
            using var container = new WhimTexDocumentContainer();
            container.SetNative("texture:0", key, new NativeArray<byte>(raw, Allocator.Persistent));
            using var sink = new Sink(); write.Invoke(container, new object[] { sink });
            using var next = new WhimTexDocumentContainer();
            using var current = new NativeArray<byte>(raw, Allocator.Persistent);
            bool hit = (bool)cached.Invoke(next, new object[] { "texture:0", key, current });
            report.costs.Add(new Cost { operation = "native-cache-opaque-" + opaque, bytes = sink.Length, detail = "next save cache hit: " + hit });
            UnityBRun.Check(hit, "Unchanged native pixels reuse cache; opaque=" + opaque);
            // Read through the serialized container, so a true lookup alone cannot satisfy the oracle.
            using (var restored = WhimTexDocumentContainer.Parse(next.Serialize())) Equal(raw, restored.Get("texture:0"));
            var editable = current; editable[0] ^= 1;
            using var changed = new WhimTexDocumentContainer();
            UnityBRun.Check(!(bool)cached.Invoke(changed, new object[] { "texture:0", key, current }),
                "Raw edit under the same candidate key rejects cached pixels; opaque=" + opaque);
            UnityBRun.Check(!changed.Contains("texture:0"), "Failed reuse does not insert a stale block");
            editable[0] ^= 1;
            using var same = new WhimTexDocumentContainer();
            UnityBRun.Check((bool)cached.Invoke(same, new object[] { "texture:0", key, current }),
                "Rejected candidate did not damage original cache; opaque=" + opaque);
        }
        return report;
    }
    static string Diagnostic(string name, Func<Report> body)
    {
        Report report = null;
        var result = JsonUtility.FromJson<WhimTex.Tests.TestResult>(UnityBRun.Run(name, () => report = body()));
        if (result.status == "passed")
        {
            try
            {
                // Preserve ephemeral Report.costs through the existing public Json.NET assembly.
                string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true)
                    .GetMethod("SerializeObject", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(object) }, null).Invoke(null, new object[] { report });
                result.message = name + "\n" + json; // UnityBRun has already completed owned cleanup.
            }
            catch (Exception error)
            {
                result.status = "failed";
                result.message = name + ": diagnostic serialization failed after owned cleanup";
                result.failures = new[] { error.ToString() };
            }
        }
        return result.ToJson();
    }
    public static string Container(int layers = 4, bool opaque = true) => Diagnostic("DocumentSaveCostProbe.Container", () => ExecuteContainer(layers, opaque));
    public static string CacheSafety() => Diagnostic("DocumentSaveCostProbe.CacheSafety", ExecuteCacheSafety);
    public static string Hashes(int mebibytes = 64) => Diagnostic("DocumentSaveCostProbe.Hashes", () => ExecuteHashes(mebibytes));
    public static string FastFingerprint() => Diagnostic("DocumentSaveCostProbe.FastFingerprint", ExecuteFastFingerprint);
    public static string Tiff(int size = 4096) => Diagnostic("DocumentSaveCostProbe.Tiff", () => ExecuteTiff(size));
}
