// run_script entry DocumentSaveCostProbe.Hashes or .Container or .Tiff.
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

public static class DocumentSaveCostProbe
{
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    public sealed class Cost
    {
        public string operation, detail;
        public double ms;
        public long bytes;
    }
    public sealed class Report
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
    public static Report Hashes(int mebibytes = 64)
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
        if (!a.AsSpan().SequenceEqual(b)) throw new Exception("Digest mismatch.");
    }
    public static Report FastFingerprint()
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
                if (!scratch.AsSpan(0, read).SequenceEqual(native.AsSpan().Slice(offset, read))) throw new Exception("Mismatch.");
                offset += read;
            }
            if (offset != bytes.Length) throw new Exception("Length mismatch.");
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
    public static Report Container(int layers = 4, bool opaque = true)
    {
        if (layers < 1 || layers > 4) throw new ArgumentOutOfRangeException(nameof(layers));
        var report = new Report(); string prefix = "diagnostic-" + Guid.NewGuid().ToString("N");
        var arrays = new byte[layers][];
        for (int i = 0; i < layers; i++) arrays[i] = Pixels(64, 19 + i, opaque);
        var cache = (IDictionary)typeof(WhimTexDocumentContainer).GetField("CompressedCache", PrivateStatic).GetValue(null);
        for (int run = 0; run < 3; run++)
        {
            int hits = 0;
            for (int i = 0; i < layers; i++) if (cache.Contains(prefix + i)) hits++;
            using var container = new WhimTexDocumentContainer();
            for (int i = 0; i < layers; i++) container.SetCompressed("texture:" + i, prefix + i, arrays[i]);
            using var sink = new Sink();
            var write = typeof(WhimTexDocumentContainer).GetMethod("WriteTo", BindingFlags.NonPublic | BindingFlags.Instance);
            Measure(report, "container-write-" + run, () => write.Invoke(container, new object[] { sink }), (long)layers * arrays[0].Length, "cache hits before write: " + hits);
            report.costs.Add(new Cost { operation = "stored-size-" + run, bytes = sink.Length });
        }
        return report;
    }
    public static Report Tiff(int size = 4096)
    {
        if (size != 2048 && size != 4096) throw new ArgumentOutOfRangeException(nameof(size));
        var report = new Report(); var raw = Pixels(size * size * 4 / 1048576); byte[] image = null;
        Measure(report, "tiff-encode-opaque-random", () => image = WhimTexTiffImage.WriteRaw(size, size, raw, 8), raw.Length);
        var validate = typeof(WhimTexTiffImage).GetMethod("Validate", PrivateStatic);
        var args = new object[] { image, 0, 0, null };
        Measure(report, "tiff-validate", () => {
            if (!(bool)validate.Invoke(null, args)) throw new Exception((string)args[3]);
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

    public static Report CacheSafety()
    {
        var report = new Report();
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
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
        }
        return report;
    }
}
