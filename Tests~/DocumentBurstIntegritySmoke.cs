// run_script entry DocumentBurstIntegritySmoke.Run. No assets/files/Unity settings modified.
// Independent .NET-only container fixture proves wire compatibility, not just writer-reader agreement.
using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DCFApixels.WhimTex;

public static class DocumentBurstIntegritySmoke
{
    delegate byte[] Digest(ReadOnlySpan<byte> bytes);
    static int checks;
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static byte[] Reference(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return hash.ComputeHash(bytes);
    }
    static byte[] Fixture(byte[] raw, bool compress, out int textureOffset, out int storedLength)
    {
        byte[] stored = raw;
        if (compress)
        {
            using var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
            stored = compressed.ToArray();
            if (stored.Length >= raw.Length) stored = raw;
        }
        byte[] model = Encoding.UTF8.GetBytes("{}");
        using var manifestStream = new MemoryStream();
        using (var manifest = new BinaryWriter(manifestStream, Encoding.UTF8, true))
        {
            manifest.Write(2);
            manifest.Write("document"); manifest.Write(Reference(model));
            manifest.Write("texture:0"); manifest.Write(Reference(stored));
        }
        byte[] integrity = manifestStream.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(Encoding.ASCII.GetBytes("WHIMTEXD")); writer.Write(1); writer.Write(3);
        void Entry(string name, int flag, long rawBytes, long storedBytes)
        {
            byte[] label = Encoding.UTF8.GetBytes(name);
            writer.Write(label.Length); writer.Write(label); writer.Write(flag); writer.Write(rawBytes); writer.Write(storedBytes);
        }
        Entry("document", 0, model.Length, model.Length);
        Entry("texture:0", ReferenceEquals(stored, raw) ? 0 : 1, raw.Length, stored.Length);
        Entry("integrity:sha256", 0, integrity.Length, integrity.Length);
        writer.Write(model);
        textureOffset = (int)stream.Position; storedLength = stored.Length;
        writer.Write(stored); writer.Write(integrity);
        return stream.ToArray();
    }
    public static async Task<string> Run()
    {
        checks = 0;
        await Task.Run(() =>
        {
            var hashType = typeof(WhimTexDocumentContainer).Assembly.GetType("DCFApixels.WhimTex.WhimTexSha256");
            var auto = (Digest)hashType.GetMethod("Compute", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Digest));
            var fallback = (Digest)hashType.GetMethod("ComputeManaged", BindingFlags.Static | BindingFlags.NonPublic).CreateDelegate(typeof(Digest));
            var rng = new Random(83);
            foreach (int size in new[] { 0, 1, 55, 56, 63, 64, 4095, 4096, 4097, 1048576 + 61 })
            {
                var bytes = new byte[size]; rng.NextBytes(bytes);
                Check(auto(bytes).AsSpan().SequenceEqual(Reference(bytes)), "Dispatch digest mismatch " + size);
                Check(fallback(bytes).AsSpan().SequenceEqual(Reference(bytes)), "Managed fallback mismatch " + size);
            }
            foreach (bool compress in new[] { false, true })
            {
                byte[] raw = new byte[4 * 1048576]; rng.NextBytes(raw);
                if (compress) for (int i = 3; i < raw.Length; i += 4) raw[i] = 255;
                byte[] fixture = Fixture(raw, compress, out int offset, out int storedLength);
                using (var parsed = WhimTexDocumentContainer.Parse(fixture))
                    Check(parsed.Get("texture:0").AsSpan().SequenceEqual(raw), "Reads .NET-generated fixture " + compress);
                using var current = new WhimTexDocumentContainer();
                current.Set("document", Encoding.UTF8.GetBytes("{}"), CompressionLevel.Fastest);
                current.Set("texture:0", raw, compress ? CompressionLevel.Fastest : CompressionLevel.NoCompression);
                byte[] produced = current.Serialize();
                Check(produced.AsSpan().SequenceEqual(fixture), "Byte-identical container including .NET SHA-256 manifest " + compress);
                using (var parsed = WhimTexDocumentContainer.Parse(produced))
                    Check(parsed.Get("texture:0").AsSpan().SequenceEqual(raw), "New roundtrip " + compress);
                foreach (int at in new[] { offset, offset + storedLength / 2, offset + storedLength - 1 })
                {
                    var corrupted = (byte[])fixture.Clone(); corrupted[at] ^= 0x80;
                    bool rejected = false;
                    try
                    {
                        using var parsed = WhimTexDocumentContainer.Parse(corrupted);
                        parsed.Get("texture:0");
                    }
                    catch (WhimTexDocumentException e) { rejected = e.Message.Contains("Checksum failed"); }
                    Check(rejected, "Corrupt stored block must fail SHA before inflate " + compress);
                }
            }
        });
        return "PASS: " + checks + " Burst dispatch/fallback/container compatibility checks.";
    }
}
