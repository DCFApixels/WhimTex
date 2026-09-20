// run_script entry DocumentBurstHashProbe.Run. Memory-only; no assets, windows, or Burst settings changed.
// Result artifact: Temp/WhimTex/TiffValidationResults/burst-sha256.json (project-relative).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using Unity.Burst;
using UnityEngine;

public static class DocumentBurstHashProbe
{
    delegate byte[] Accelerated(ReadOnlySpan<byte> data, out bool ranBurst);
    static readonly Type HashType = typeof(WhimTexDocumentContainer).Assembly.GetType("DCFApixels.WhimTex.WhimTexSha256");
    static readonly Accelerated Hash = (Accelerated)HashType.GetMethod("ComputeAccelerated", BindingFlags.NonPublic | BindingFlags.Static)
        .CreateDelegate(typeof(Accelerated));
    [Serializable] public sealed class Sample { public string name; public long bytes; public int iterations; public double milliseconds; }
    [Serializable] public sealed class Report
    {
        public string unity, burstAssembly, timestampUtc;
        public bool burstEnabled, allCallsUsedBurst;
        public int checks;
        public List<Sample> samples = new List<Sample>();
    }
    static byte[] Reference(ReadOnlySpan<byte> bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bytes);
        return hash.GetHashAndReset();
    }
    static void Equal(Report report, byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) throw new Exception("SHA-256 mismatch at check " + report.checks);
        report.checks++;
    }
    static byte[] Burst(Report report, ReadOnlySpan<byte> data)
    {
        var result = Hash(data, out bool ranBurst);
        report.allCallsUsedBurst &= ranBurst;
        return result;
    }
    static void Measure(Report report, string name, long bytes, int iterations, Action action)
    {
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        watch.Stop();
        report.samples.Add(new Sample { name = name, bytes = bytes, iterations = iterations, milliseconds = watch.Elapsed.TotalMilliseconds / iterations });
    }
    public static async Task<Report> Run(int mebibytes = 64, int runs = 3)
    {
        if (mebibytes < 1 || mebibytes > 64 || runs < 1 || runs > 5) throw new ArgumentOutOfRangeException();
        var report = new Report
        {
            unity = Application.unityVersion, burstAssembly = typeof(BurstCompileAttribute).Assembly.FullName,
            timestampUtc = DateTime.UtcNow.ToString("O"), burstEnabled = BurstCompiler.Options.EnableBurstCompilation,
            allCallsUsedBurst = true
        };
        // First call deliberately occurs on a background thread, matching container preparation after reload.
        await Task.Run(() => VerifyAndMeasure(report, mebibytes, runs));
        string directory = "Temp/WhimTex/TiffValidationResults";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "burst-sha256.json"), JsonUtility.ToJson(report, true));
        return report;
    }
    static void VerifyAndMeasure(Report report, int mebibytes, int runs)
    {
        byte[] actual = null;
        Measure(report, "first-worker-call-including-lazy-initialization", 3, 1, () => actual = Burst(report, Encoding.ASCII.GetBytes("abc")));
        Equal(report, Convert.FromBase64String("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0="), actual);
        var vectors = new[]
        {
            ("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
            ("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
            ("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq", "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1")
        };
        foreach (var vector in vectors)
        {
            byte[] digest = Burst(report, Encoding.ASCII.GetBytes(vector.Item1));
            string hex = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            if (hex != vector.Item2) throw new Exception("Known SHA-256 vector failed: " + vector.Item1);
            report.checks++;
        }
        var million = new byte[1000000]; Array.Fill(million, (byte)'a');
        string millionHex = BitConverter.ToString(Burst(report, million)).Replace("-", "").ToLowerInvariant();
        if (millionHex != "cdc76e5c9914fb9281a1c7e284d73e67f1809a48a497200e046d39ccc7112cd0") throw new Exception("Million-a vector failed.");
        report.checks++;

        var random = new System.Random(719);
        var small = new byte[513]; random.NextBytes(small);
        for (int length = 0; length <= 256; length++)
            foreach (int start in new[] { 0, 1, 13 })
                Equal(report, Reference(small.AsSpan(start, length)), Burst(report, small.AsSpan(start, length)));
        var boundary = new byte[2 * 1048576 + 256]; random.NextBytes(boundary);
        foreach (int length in new[] { 1048511, 1048512, 1048513, 1048575, 1048576, 1048577, 1048631, 1048632, 1048633, 2097151, 2097152, 2097153 })
            Equal(report, Reference(boundary.AsSpan(7, length)), Burst(report, boundary.AsSpan(7, length)));
        // Concurrent independent states and scratch allocations; no mutable static hash state.
        var concurrent = new byte[4][];
        Parallel.For(0, 4, i =>
        {
            concurrent[i] = Hash(boundary.AsSpan(i, 1048576 + i), out bool used);
            if (report.burstEnabled && !used) throw new Exception("Parallel call did not execute Burst.");
        });
        for (int i = 0; i < concurrent.Length; i++) Equal(report, Reference(boundary.AsSpan(i, 1048576 + i)), concurrent[i]);

        foreach (int length in new[] { 0, 64, 256, 1024, 4096, 65536 })
        {
            var bytes = new byte[length]; random.NextBytes(bytes);
            Measure(report, "small-managed", length, 50, () => actual = Reference(bytes));
            byte[] expected = actual;
            Measure(report, "small-Burst-including-copy-allocate-free", length, 50, () => actual = Burst(report, bytes));
            Equal(report, expected, actual);
        }
        var large = new byte[mebibytes * 1048576]; random.NextBytes(large);
        byte[] reference = Reference(large);
        for (int run = 0; run < runs; run++)
        {
            // Reverse order every other run to reduce order/thermal bias.
            Action managed = () => Measure(report, "large-managed-" + run, large.Length, 1, () => actual = Reference(large));
            Action burst = () => Measure(report, "large-Burst-including-copy-allocate-free-" + run, large.Length, 1, () => actual = Burst(report, large));
            if ((run & 1) == 0) { managed(); Equal(report, reference, actual); burst(); }
            else { burst(); Equal(report, reference, actual); managed(); }
            Equal(report, reference, actual);
        }
        foreach (bool burst in new[] { false, true })
        {
            Measure(report, burst ? "four-independent-Burst" : "four-independent-managed", (long)large.Length * 4, 1, () =>
                Parallel.For(0, 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    // Sharing immutable input saves benchmark memory; each output/state is independent.
                    var value = burst ? Hash(large, out _) : Reference(large);
                    if (!value.AsSpan().SequenceEqual(reference)) throw new Exception("Large parallel digest mismatch.");
                }));
            report.checks += 4;
        }
        if (report.burstEnabled && !report.allCallsUsedBurst) throw new Exception("Burst is enabled but kernel ran managed.");
    }
}
