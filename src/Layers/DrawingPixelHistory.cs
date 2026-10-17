using System;
using System.Collections.Generic;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Unity records this small cursor. Pixel payloads belong to the session journal.
    internal sealed class DrawingPixelHistory : ScriptableObject
    {
        [SerializeField] internal Texture2D texture;
        [SerializeField] private string revision = "";
        [SerializeField] private string identity;
        [NonSerialized] private string applied;
        private static readonly Dictionary<UnityObjectID, DrawingPixelHistory> owners = new();
        internal string Revision => revision ?? "";
        internal bool NeedsRestore => (applied ?? "") != Revision;
        internal static DrawingPixelHistory Find(Texture2D texture, bool create = false)
        {
            if (texture == null) return null;
            var id = UnityObjectID.FromObject(texture);
            if (owners.TryGetValue(id, out var value) && value != null && value.texture == texture) return value;
            foreach (var existing in Resources.FindObjectsOfTypeAll<DrawingPixelHistory>())
                if (existing.texture == texture) { owners[id] = existing; return existing; }
            if (!create) return null;
            value = CreateInstance<DrawingPixelHistory>();
            value.name = "Drawing pixel history"; value.hideFlags = HideFlags.HideAndDontSave;
            value.texture = texture; value.identity = Guid.NewGuid().ToString("N"); value.applied = "";
            owners[id] = value; return value;
        }
        private void OnEnable() { if (!string.IsNullOrEmpty(identity)) applied = SessionState.GetString(identity, Revision); }
        internal PaintHistoryStore.Step Begin(RectInt[] blocks, string label)
        {
            var step = PaintHistoryStore.Begin(Revision, texture, blocks);
            revision = step.id; applied = revision;
            SessionState.SetString(identity, applied);
            EditorUtility.SetDirty(this);
            return step;
        }
        internal void Restore(Action<PaintHistoryStore.Block, bool> apply)
        {
            if (!NeedsRestore) return;
            using var profile = WhimTexProfiler.Measure("brush.undo.restore");
            PaintTileTransfer.Drain();
            string from = applied ?? "", to = Revision;
            var ancestors = new HashSet<string> { "" };
            for (string p = from; p.Length != 0; p = PaintHistoryStore.Get(p).parent) ancestors.Add(p);
            var forward = new List<PaintHistoryStore.Step>();
            string common = to;
            while (!ancestors.Contains(common)) { var s = PaintHistoryStore.Get(common); forward.Add(s); common = s.parent; }
            // Validate all files before modifying either CPU or GPU pixels.
            var backward = new List<PaintHistoryStore.Step>();
            for (string p = from; p != common;) { var s = PaintHistoryStore.Get(p); backward.Add(s); p = s.parent; }
            using (WhimTexProfiler.Measure("brush.undo.validate"))
            {
                foreach (var s in backward) ValidateStep(s);
                foreach (var s in forward) ValidateStep(s);
            }
            using (WhimTexProfiler.Measure("brush.undo.apply"))
            {
                foreach (var s in backward) PaintHistoryStore.Visit(s, b => apply(b, false));
                for (int i = forward.Count - 1; i >= 0; i--) PaintHistoryStore.Visit(forward[i], b => apply(b, true));
            }
            applied = to; SessionState.SetString(identity, applied);
        }
        private void ValidateStep(PaintHistoryStore.Step step)
        {
            if (step.width != texture.width || step.height != texture.height ||
                step.bytesPerPixel != (HdrUtility.IsHdr(texture) ? 8 : 4))
                throw new InvalidDataException("Paint history dimensions or format do not match the drawing.");
            PaintHistoryStore.Validate(step);
        }
        internal void RestoreCurrentHalf(Action<PaintHistoryStore.Block, bool> apply)
        {
            // A reload may have discarded the runtime surface. Reapply Half blocks on
            // the current stored baseline, retaining low-alpha accumulation precision.
            if (NeedsRestore || Revision.Length == 0 || !PaintTileTransfer.SupportsPixels(texture)) return;
            PaintTileTransfer.Drain();
            var seen = new HashSet<Vector2Int>();
            for (string p = Revision; p.Length != 0;)
            {
                var step = PaintHistoryStore.Get(p);
                if (step.width != texture.width || step.height != texture.height ||
                    step.bytesPerPixel != (HdrUtility.IsHdr(texture) ? 8 : 4)) break;
                PaintHistoryStore.Validate(step);
                PaintHistoryStore.Visit(step, b =>
                {
                    // Native pixel operations may have replaced tiles after the last brush stroke.
                    if (seen.Add(new Vector2Int(b.rect.x, b.rect.y)) &&
                        MatchesStored(texture, b.rect, step.bytesPerPixel, b.afterStored)) apply(b, true);
                });
                p = step.parent;
            }
        }
        private static bool MatchesStored(Texture2D texture, RectInt rect, int bpp, byte[] expected)
        {
            var raw = texture.GetRawTextureData<byte>();
            int rowBytes = rect.width * bpp;
            for (int y = 0; y < rect.height; y++)
            for (int x = 0, offset = ((rect.y + y) * texture.width + rect.x) * bpp; x < rowBytes; x++)
                if (raw[offset + x] != expected[y * rowBytes + x]) return false;
            return true;
        }
    }

    internal static class PaintHistoryStore
    {
        internal const long PendingBudget = 64L * 1024 * 1024;
        private const int Magic = 0x57545031;
        private const int CheckedDeflate = 0x57545032;
        private const int FastCheckedDeflate = 0x57545033;
        private const int FastCheckedRaw = 0x57545034;
        private static readonly Dictionary<string, Step> steps = new();
        private static readonly Queue<Page> writing = new();
        // At most ~64 MiB retained across all geometric buckets, separate from
        // the 64 MiB budget for payloads whose writes are still in progress.
        private static readonly ArrayPool<byte> buffers = ArrayPool<byte>.Create(PaintTileSet.Size * PaintTileSet.Size * 8, 64);
        private static string root;
        private static long pendingBytes;
        internal static long PendingBytes => Interlocked.Read(ref pendingBytes);
        internal static string Root
        {
            get
            {
                if (root != null) return root;
                string session = SessionState.GetString("WhimTex.PaintHistory.Session", "");
                if (!Guid.TryParseExact(session, "N", out _)) { session = Guid.NewGuid().ToString("N"); SessionState.SetString("WhimTex.PaintHistory.Session", session); }
                root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/WhimTex/PaintUndo", session));
                Directory.CreateDirectory(root); return root;
            }
        }
        [Serializable] internal sealed class Step
        {
            public string id, parent;
            public int width, height, bytesPerPixel, pages;
            [NonSerialized] internal Page[] payload;
        }
        internal sealed class Page
        {
            internal Block[] blocks;
            internal Task write;
            internal string path;
            internal long bytes;
            internal bool errorReported;
        }
        internal sealed class Block
        {
            internal RectInt rect;
            internal byte[] beforeHalf, afterHalf, beforeStored, afterStored;
        }
        internal static Step Begin(string parent, Texture2D texture, RectInt[] blocks)
        {
            var step = new Step { id = Guid.NewGuid().ToString("N"), parent = parent ?? "",
                width = texture.width, height = texture.height, bytesPerPixel = HdrUtility.IsHdr(texture) ? 8 : 4,
                pages = (blocks.Length + PaintTileTransfer.TilesPerPage - 1) / PaintTileTransfer.TilesPerPage };
            step.payload = new Page[step.pages];
            File.WriteAllText(Path.Combine(Root, step.id + ".json"), JsonUtility.ToJson(step));
            steps.Add(step.id, step); return step;
        }
        internal static Step Get(string id)
        {
            if (steps.TryGetValue(id, out var step)) return step;
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid paint history revision.");
            step = JsonUtility.FromJson<Step>(File.ReadAllText(Path.Combine(Root, id + ".json")));
            if (step == null || step.id != id || step.pages < 1 || step.pages > 4096 ||
                step.width < 1 || step.width > 16384 || step.height < 1 || step.height > 16384 ||
                (step.bytesPerPixel != 4 && step.bytesPerPixel != 8)) throw new InvalidDataException("Invalid paint history manifest.");
            step.payload = new Page[step.pages]; steps.Add(id, step); return step;
        }
        internal static void Store(Step step, int pageIndex, Block[] blocks)
        {
            using var profile = WhimTexProfiler.Measure("brush.history.store");
            long size = 0;
            foreach (var b in blocks) size += b.beforeHalf.LongLength + b.afterHalf.LongLength + b.beforeStored.LongLength + b.afterStored.LongLength;
            var page = new Page { blocks = blocks, bytes = size, path = Path.Combine(Root, step.id + "-" + pageIndex + ".bin") };
            step.payload[pageIndex] = page;
            Interlocked.Add(ref pendingBytes, size);
            page.write = Task.Run(() =>
            {
                string temporary = page.path + ".tmp";
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using var header = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                    bool compress = ShouldCompress(blocks);
                    header.Write(compress ? FastCheckedDeflate : FastCheckedRaw); header.Write(new byte[32]);
                    using (var zip = compress ? new DeflateStream(file, System.IO.Compression.CompressionLevel.Fastest, true) : null)
                    using (var writer = new BinaryWriter((Stream)zip ?? file, System.Text.Encoding.UTF8, true))
                    {
                        writer.Write(Magic); writer.Write(blocks.Length);
                        foreach (var b in blocks)
                        {
                            writer.Write(b.rect.x); writer.Write(b.rect.y); writer.Write(b.rect.width); writer.Write(b.rect.height);
                            writer.Write(b.beforeHalf); writer.Write(b.afterHalf); writer.Write(b.beforeStored); writer.Write(b.afterStored);
                        }
                    }
                    file.Position = 36;
                    byte[] hash = HashPage(file);
                    file.Position = 4; header.Write(hash);
                }
                File.Move(temporary, page.path);
                // Retain the in-memory payload on I/O failure; Undo can still use it.
                page.blocks = null;
                foreach (var block in blocks)
                {
                    ReturnBuffer(block.beforeHalf); ReturnBuffer(block.afterHalf);
                    ReturnBuffer(block.beforeStored); ReturnBuffer(block.afterStored);
                }
                Interlocked.Add(ref pendingBytes, -size);
            });
            writing.Enqueue(page);
        }
        internal static bool MakeRoom(long bytes, bool wait)
        {
            using (WhimTexProfiler.Measure("brush.history.backpressure"))
            while (writing.Count > 0)
            {
                if (!writing.Peek().write.IsCompleted)
                {
                    if (PendingBytes + bytes <= PendingBudget) break;
                    if (!wait) return false;
                }
                WaitForWrite(writing.Dequeue());
            }
            // Failed writes retain their payload in memory and report the failure;
            // they must not permanently prevent all subsequent CPU synchronization.
            return true;
        }
        private static bool ShouldCompress(Block[] blocks)
        {
            // Sample every channel payload and several rows of every tile, bounded to
            // 64 KiB per page. Dense detail should not spend seconds in compression.
            using var sample = new MemoryStream();
            int input = 0;
            using (var zip = new DeflateStream(sample, System.IO.Compression.CompressionLevel.Fastest, true))
            foreach (var block in blocks)
            {
                Sample(block.beforeHalf); Sample(block.afterHalf); Sample(block.beforeStored); Sample(block.afterStored);
                void Sample(byte[] bytes)
                {
                    int length = Math.Min(256, bytes.Length);
                    for (int part = 0; part < 4; part++)
                    { zip.Write(bytes, (bytes.Length - length) * part / 3, length); input += length; }
                }
            }
            return sample.Length * 3 < input;
        }
        internal static void FlushWrites()
        {
            using var profile = WhimTexProfiler.Measure("brush.history.flush");
            while (writing.Count > 0) WaitForWrite(writing.Dequeue());
        }
        internal static void ClearSession()
        {
            // Only this session's generated journal is disposable; never follow an arbitrary path.
            if (root == null) return;
            string parent = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/WhimTex/PaintUndo"));
            if (Path.GetDirectoryName(root) != parent || !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
                throw new InvalidOperationException("Invalid paint journal cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        private static void WaitForWrite(Page page)
        {
            if (page?.write == null) return;
            using var profile = WhimTexProfiler.Measure("brush.history.writeWait");
            try { page.write.GetAwaiter().GetResult(); }
            catch (Exception error) when (page.blocks != null)
            {
                if (page.errorReported) return;
                page.errorReported = true;
                Debug.LogError("WhimTex could not write paint history. This Undo data remains in memory: " + error.Message);
            }
        }
        internal static void Validate(Step step)
        {
            for (int i = 0; i < step.pages; i++)
            {
                Page page = step.payload[i]; WaitForWrite(page);
                if (page?.blocks != null) continue;
                using var file = OpenPage(step, i);
                using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, true);
                int format = reader.ReadInt32();
                if (format != CheckedDeflate && format != FastCheckedDeflate && format != FastCheckedRaw)
                { VisitPage(step, i, _ => { }); continue; } // Existing history in this Editor session.
                byte[] expected = Read(reader, 32);
                byte[] actual;
                if (format != CheckedDeflate) actual = HashPage(file);
                else { using var hash = SHA256.Create(); actual = hash.ComputeHash(file); }
                for (int j = 0; j < actual.Length; j++)
                    if (expected[j] != actual[j]) throw new InvalidDataException("Damaged paint history page; pixels were not restored.");
                // The payload checksum does not include the format marker. Check its
                // decoded header too, so a damaged raw/Deflate marker cannot pass validation.
                file.Position = 36;
                using var decoded = format == FastCheckedRaw ? null : new DeflateStream(file, CompressionMode.Decompress, true);
                using var payload = new BinaryReader((Stream)decoded ?? file, System.Text.Encoding.UTF8, true);
                if (payload.ReadInt32() != Magic) throw new InvalidDataException("Invalid paint history encoding.");
                int count = payload.ReadInt32();
                if (count < 1 || count > PaintTileTransfer.TilesPerPage) throw new InvalidDataException("Invalid paint history page.");
            }
        }
        // Hash128 is Unity's portable data checksum. Fixed-size chunks make Append
        // deterministic even when a stream returns a partial read. Not an authenticity check.
        private static byte[] HashPage(Stream stream)
        {
            var hash = new Hash128(); var buffer = new byte[65536];
            while (true)
            {
                int length = 0, read;
                while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0) length += read;
                if (length == 0) break;
                hash.Append(buffer, 0, length);
            }
            return System.Text.Encoding.ASCII.GetBytes(hash.ToString());
        }
        private static FileStream OpenPage(Step step, int index) => new FileStream(Path.Combine(Root, step.id + "-" + index + ".bin"),
            FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        internal static void Visit(Step step, Action<Block> visit)
        {
            using var profile = WhimTexProfiler.Measure("brush.history.read");
            // Visitors consume each block synchronously. Reuse its buffers rather than
            // allocating and zeroing the entire restored stroke on the managed heap.
            var scratch = new Block();
            for (int i = 0; i < step.pages; i++)
                VisitPage(step, i, visit, scratch);
        }
        private static void VisitPage(Step step, int i, Action<Block> visit, Block scratch = null)
        {
            Page page = step.payload[i];
            WaitForWrite(page);
            var retained = page?.blocks;
            if (retained != null) { foreach (var b in retained) visit(b); return; }
            using var file = OpenPage(step, i);
            using var header = new BinaryReader(file, System.Text.Encoding.UTF8, true);
            int format = header.ReadInt32();
            bool checkedDeflate = format == CheckedDeflate || format == FastCheckedDeflate;
            bool raw = format == FastCheckedRaw;
            file.Position = checkedDeflate || raw ? 36 : 0;
            using Stream zip = raw ? file : checkedDeflate ? new DeflateStream(file, CompressionMode.Decompress) : new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);
            if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unsupported paint history block.");
            int count = reader.ReadInt32();
            if (count < 1 || count > PaintTileTransfer.TilesPerPage) throw new InvalidDataException("Invalid paint history page.");
            scratch ??= new Block();
            for (int j = 0; j < count; j++)
            {
                var r = new RectInt(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                if (r.width < 1 || r.width > PaintTileSet.Size || r.height < 1 || r.height > PaintTileSet.Size ||
                    r.x < 0 || r.y < 0 || r.xMax > step.width || r.yMax > step.height) throw new InvalidDataException("Invalid paint history bounds.");
                int area = r.width * r.height;
                scratch.rect = r;
                using (WhimTexProfiler.Measure("brush.history.decode"))
                {
                    ReadInto(reader, ref scratch.beforeHalf, area * 8); ReadInto(reader, ref scratch.afterHalf, area * 8);
                    ReadInto(reader, ref scratch.beforeStored, area * step.bytesPerPixel); ReadInto(reader, ref scratch.afterStored, area * step.bytesPerPixel);
                }
                visit(scratch);
            }
            if (reader.Read() != -1) throw new InvalidDataException("Unexpected paint history data.");
        }
        private static byte[] Read(BinaryReader reader, int count)
        { byte[] bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException("Incomplete paint history block."); return bytes; }
        private static void ReadInto(BinaryReader reader, ref byte[] bytes, int count)
        {
            if (bytes == null || bytes.Length != count) bytes = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = reader.Read(bytes, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("Incomplete paint history block.");
                offset += read;
            }
        }
        internal static byte[] ReadRows(NativeArray<byte> source, int width, RectInt rect, int bpp)
        {
            var result = RentBuffer(rect.width * rect.height * bpp);
            for (int y = 0; y < rect.height; y++) NativeArray<byte>.Copy(source, ((rect.y + y) * width + rect.x) * bpp,
                result, y * rect.width * bpp, rect.width * bpp);
            return result;
        }
        internal static byte[] RentBuffer(int length)
        {
            // Full tiles use exact power-of-two buckets. Partial edge tiles retain
            // exact lengths too, so all storage/upload consumers share one contract.
            if (length < 16 || (length & (length - 1)) != 0) return new byte[length];
            byte[] bytes = buffers.Rent(length);
            if (bytes.Length == length) return bytes;
            buffers.Return(bytes);
            return new byte[length];
        }
        private static void ReturnBuffer(byte[] bytes)
        { if (bytes.Length >= 16 && (bytes.Length & (bytes.Length - 1)) == 0) buffers.Return(bytes); }
        internal static void WriteRows(NativeArray<byte> target, int width, RectInt rect, int bpp, byte[] bytes)
        {
            for (int y = 0; y < rect.height; y++) NativeArray<byte>.Copy(bytes, y * rect.width * bpp,
                target, ((rect.y + y) * width + rect.x) * bpp, rect.width * bpp);
        }
    }
}
