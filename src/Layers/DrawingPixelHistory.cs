using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DCFApixels.WhimTex
{
    // Per-stroke Unity records select this cursor; pixel payloads belong to the session journal.
    internal sealed partial class DrawingPixelHistory : ScriptableObject
    {
        [SerializeField] internal Texture2D texture;
        [SerializeField] private string revision = "";
        [SerializeField] private string identity;
        [SerializeField] private List<DrawingPixelUndoRecord> undoRecords = new();
        [NonSerialized] private string applied;
        private static readonly Dictionary<UnityObjectID, DrawingPixelHistory> owners = new();
        internal string Revision => revision ?? "";
        internal bool NeedsRestore { get { SynchronizeUndoRecords(); return (applied ?? "") != Revision; } }
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
            var step = PaintHistoryStore.Begin(Revision, texture, blocks, this);
            var record = CreateInstance<DrawingPixelUndoRecord>();
            record.hideFlags = HideFlags.HideAndDontSave; record.name = label;
            record.before = Revision; record.after = step.id;
            Undo.RegisterCompleteObjectUndo(record, label);
            record.active = record.observed = true; EditorUtility.SetDirty(record);
            undoRecords.Add(record);
            revision = step.id; applied = revision;
            SessionState.SetString(identity, applied);
            EditorUtility.SetDirty(this);
            return step;
        }
        private void SynchronizeUndoRecords()
        {
            // A native group may contain several strokes: Undo selects the earliest parent,
            // Redo the latest child. Other objects in the group remain entirely Unity-owned.
            string backward = null, forward = null;
            foreach (var record in undoRecords)
            {
                if (record == null || record.observed == record.active) continue;
                if (record.active) forward = record.after;
                else backward ??= record.before;
                record.observed = record.active;
            }
            if (backward != null || forward != null) revision = forward ?? backward;
        }
        internal void ForgetUndoRecord(string step)
        {
            for (int i = undoRecords.Count - 1; i >= 0; i--)
            {
                var record = undoRecords[i];
                if (record != null && record.after != step) continue;
                undoRecords.RemoveAt(i);
                if (record == null) continue;
                Undo.ClearUndo(record); DestroyImmediate(record);
            }
        }
        private void OnDestroy()
        {
            foreach (var record in undoRecords)
                if (record != null) { Undo.ClearUndo(record); DestroyImmediate(record); }
            undoRecords.Clear();
        }
        internal bool Restore(Action<PaintHistoryStore.Block, bool> apply)
        {
            if (!NeedsRestore) return false;
            using var profile = WhimTexProfiler.Measure("brush.undo.restore");
            PaintTileTransfer.Drain();
            string from = applied ?? "", to = Revision;
            if (!TryPath(from, out var fromPath) || !TryPath(to, out var toPath))
            {
                revision = from; EditorUtility.SetDirty(this);
                return false; // A retired native Undo marker must never restore only part of a stroke.
            }
            var ancestors = new HashSet<string>(fromPath) { Boundary };
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
            return true;
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
            if (NeedsRestore || !PaintTileTransfer.SupportsPixels(texture)) return;
            PaintTileTransfer.Drain();
            var seen = new HashSet<Vector2Int>();
            for (string p = Revision; p != Boundary && p.Length != 0;)
            {
                if (!PaintHistoryStore.TryGet(p, out var step)) break;
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
            VisitBaseline(b =>
            {
                if (seen.Add(new Vector2Int(b.rect.x, b.rect.y)) && MatchesStored(texture, b.rect, HdrUtility.IsHdr(texture) ? 8 : 4, b.afterStored)) apply(b, true);
            });
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

    internal static partial class PaintHistoryStore
    {
        // Enough immutable payloads to keep several writers busy on larger CPUs,
        // without letting a rapid stroke sequence grow an unbounded native queue.
        internal static readonly long PendingBudget = Math.Clamp(Environment.ProcessorCount / 2, 2, 4) * 32L * 1024 * 1024;
        private const int Magic = 0x57545031;
        private const int CheckedDeflate = 0x57545032;
        private const int FastCheckedDeflate = 0x57545033;
        private const int FastCheckedRaw = 0x57545034;
        private const int FastCheckedBlocks = 0x57545035;
        private static readonly Dictionary<string, Step> steps = new();
        private static readonly Queue<Page> writing = new();
        private static long pendingBytes;
        internal static long PendingBytes => Interlocked.Read(ref pendingBytes);
        internal static string Root
        {
            get
            {
                return PaintUndoDisk.Current.Root;
            }
        }
        [Serializable] internal sealed class Step
        {
            public string id, parent, owner, document;
            public int width, height, bytesPerPixel, pages;
            public long order;
            [NonSerialized] internal Page[] payload;
            [NonSerialized] internal bool reservationReleased;
            [NonSerialized] internal int completedWrites;
            [NonSerialized] internal long peakPendingBytes;
            [NonSerialized] internal long comparisonTicks, extractionTicks;
        }
        internal sealed class Page
        {
            internal PaintHistoryPayload native;
            internal long residentBytes;
            internal Task write;
            internal string path;
            internal long bytes;
            internal int blockCount;
            internal bool errorReported;
            // Worker wall timings; read only after write.IsCompleted (task publication barrier).
            // The main-thread profiler's store scope measures enqueueing, not this work.
            internal long queuedAt, startedAt, finishedAt, sampleTicks, encodeTicks, hashTicks, fileBytes;
            internal long inputCopyTicks, compressionTicks, outputCopyTicks, fileWriteTicks;
            internal bool compressed;
        }
        internal sealed class Block
        {
            internal RectInt rect;
            internal byte[] beforeHalf, afterHalf, beforeStored, afterStored;
        }
        internal static Step Begin(string parent, Texture2D texture, RectInt[] blocks, DrawingPixelHistory history)
        {
            EnsureLoaded();
            foreach (var existing in steps.Values)
                foreach (var page in existing.payload)
                    if (page?.write?.IsFaulted == true)
                        throw new IOException("Paint Undo could not write to disk. Clear Paint Undo after checking free disk space before starting another stroke.");
            DiscardBranches(history);
            var step = new Step { id = Guid.NewGuid().ToString("N"), parent = parent ?? "",
                width = texture.width, height = texture.height, bytesPerPixel = HdrUtility.IsHdr(texture) ? 8 : 4,
                owner = history.Identity, document = history.DocumentIdentity, order = DateTime.UtcNow.Ticks,
                pages = (blocks.Length + PaintTileTransfer.TilesPerPage - 1) / PaintTileTransfer.TilesPerPage };
            long estimate = 1024 * 1024;
            foreach (var block in blocks) estimate += (long)block.width * block.height * (16 + 2 * step.bytesPerPixel);
            estimate += estimate / 100;
            while (!PaintUndoDisk.Current.Reserve(StepDirectory(step.id), estimate, out string reason))
            {
                PaintTileTransfer.Drain(); FlushWrites();
                // A completed writer may have released a large worst-case reservation.
                // Retry the real quota before retiring any existing Undo operation.
                if (PaintUndoDisk.Current.Reserve(StepDirectory(step.id), estimate, out reason)) break;
                if (!RetireOldest()) throw new IOException(reason + " The stroke was not applied. Increase Undo Disk Limit or clear history in WhimTex Settings.");
            }
            step.payload = new Page[step.pages];
            try
            {
                PaintUndoDisk.AtomicText(Path.Combine(StepDirectory(step.id), "manifest.json"), JsonUtility.ToJson(step));
                steps.Add(step.id, step); return step;
            }
            catch { PaintUndoDisk.Current.DeleteOwned(StepDirectory(step.id)); throw; }
        }
        internal static Step Get(string id)
        {
            if (steps.TryGetValue(id, out var step)) return step;
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid paint history revision.");
            step = JsonUtility.FromJson<Step>(File.ReadAllText(Path.Combine(StepDirectory(id), "manifest.json")));
            if (step == null || step.id != id || !Guid.TryParseExact(step.owner, "N", out _) ||
                !Guid.TryParseExact(step.document, "N", out _) || step.pages < 1 || step.pages > 4096 ||
                step.width < 1 || step.width > 16384 || step.height < 1 || step.height > 16384 ||
                (step.bytesPerPixel != 4 && step.bytesPerPixel != 8)) throw new InvalidDataException("Invalid paint history manifest.");
            step.payload = new Page[step.pages]; steps.Add(id, step); return step;
        }
        internal static void Store(Step step, int pageIndex, PaintHistoryPayload payload)
        {
            using var profile = WhimTexProfiler.Measure("brush.history.store");
            long size = payload.ResidentBytes;
            var page = new Page { native = payload, blockCount = payload.blocks.Count, bytes = payload.Bytes,
                residentBytes = size, path = PagePath(step, pageIndex), queuedAt = Stopwatch.GetTimestamp() };
            step.payload[pageIndex] = page;
            long queuedBytes = Interlocked.Add(ref pendingBytes, size);
            step.peakPendingBytes = Math.Max(step.peakPendingBytes, queuedBytes);
            var disk = PaintUndoDisk.Current;
            string directory = StepDirectory(step.id);
            try { page.write = Task.Run(() =>
            {
                page.startedAt = Stopwatch.GetTimestamp();
                string temporary = page.path + ".tmp";
                using var workspace = PaintHistoryCodec.Borrow();
                var codec = workspace.Value;
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    using var header = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                    long stage = Stopwatch.GetTimestamp();
                    bool packed = Unity.Burst.BurstCompiler.Options.EnableBurstCompilation;
                    bool compress = !packed && ShouldCompress(payload);
                    page.sampleTicks = Stopwatch.GetTimestamp() - stage;
                    page.compressed = packed || compress;
                    header.Write(packed ? FastCheckedBlocks : compress ? FastCheckedDeflate : FastCheckedRaw); header.Write(new byte[32]);
                    stage = Stopwatch.GetTimestamp();
                    using (var zip = compress ? new DeflateStream(file, System.IO.Compression.CompressionLevel.Fastest, true) : null)
                    using (var writer = new BinaryWriter((Stream)zip ?? file, System.Text.Encoding.UTF8, true))
                    {
                        writer.Write(packed ? FastCheckedBlocks : Magic); writer.Write(payload.blocks.Count);
                        foreach (var b in payload.blocks)
                        {
                            writer.Write(b.rect.x); writer.Write(b.rect.y); writer.Write(b.rect.width); writer.Write(b.rect.height);
                            Write(payload.beforeHalf, 8); Write(payload.afterHalf, 8);
                            Write(payload.beforeStored, payload.bpp); Write(payload.afterStored, payload.bpp);
                            void Write(NativeArray<byte> source, int bpp) => codec.WriteNative(writer, source,
                                payload.width * bpp, (b.slot.y * payload.width + b.slot.x) * bpp,
                                b.slot.width * bpp, b.slot.height, packed);
                        }
                    }
                    page.encodeTicks = Stopwatch.GetTimestamp() - stage;
                    page.inputCopyTicks = codec.InputCopyTicks; page.compressionTicks = codec.EncodeTicks;
                    page.outputCopyTicks = codec.OutputCopyTicks; page.fileWriteTicks = codec.WriteTicks;
                    page.fileBytes = file.Length;
                    file.Position = 36;
                    stage = Stopwatch.GetTimestamp();
                    byte[] hash = HashPage(file);
                    page.hashTicks = Stopwatch.GetTimestamp() - stage;
                    file.Position = 4; header.Write(hash);
                }
                File.Move(temporary, page.path);
                // An I/O failure keeps the immutable planes for exact Undo. Successful
                // publication alone releases them for a later readback or writer.
                page.native = null; payload.Dispose();
                Interlocked.Add(ref pendingBytes, -size);
                if (Interlocked.Increment(ref step.completedWrites) == step.pages)
                {
                    disk.Complete(directory);
                    Volatile.Write(ref step.reservationReleased, true);
                }
                page.finishedAt = Stopwatch.GetTimestamp();
            }); }
            catch
            {
                // Task creation failed before ownership crossed the thread boundary.
                step.payload[pageIndex] = null; Interlocked.Add(ref pendingBytes, -size);
                throw;
            }
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
        private static bool ShouldCompress(PaintHistoryPayload payload)
        {
            // Bounded sample from immutable page rows; no per-block payload arrays.
            using var sample = new MemoryStream();
            byte[] buffer = new byte[256]; int input = 0;
            using (var zip = new DeflateStream(sample, System.IO.Compression.CompressionLevel.Fastest, true))
            foreach (var block in payload.blocks)
            {
                Sample(payload.beforeHalf, 8); Sample(payload.afterHalf, 8);
                Sample(payload.beforeStored, payload.bpp); Sample(payload.afterStored, payload.bpp);
                void Sample(NativeArray<byte> bytes, int bpp)
                {
                    int length = Math.Min(buffer.Length, block.slot.width * bpp);
                    for (int part = 0; part < 4; part++)
                    {
                        int row = block.slot.y + (block.slot.height - 1) * part / 3;
                        bytes.AsReadOnlySpan().Slice((row * payload.width + block.slot.x) * bpp, length).CopyTo(buffer);
                        zip.Write(buffer, 0, length); input += length;
                    }
                }
            }
            return sample.Length * 3 < input;
        }
        // Called only after transfers, writers and checkpoint readers have joined at
        // domain shutdown. Faulted journal payloads cannot outlive their native owners.
        internal static void ReleaseNativePayloads()
        {
            foreach (var step in steps.Values)
            foreach (var page in step.payload)
            {
                if (page?.native == null) continue;
                page.native.Dispose(); page.native = null;
                Interlocked.Add(ref pendingBytes, -page.residentBytes);
            }
        }
        internal static void FlushWrites()
        {
            using var profile = WhimTexProfiler.Measure("brush.history.flush");
            while (writing.Count > 0) WaitForWrite(writing.Dequeue());
        }
        internal static void ClearSession()
        {
            FinishCheckpointWork();
            checkpointScratch = null;
            PaintUndoDisk.Current.Close();
        }
        private static void WaitForWrite(Page page)
        {
            if (page?.write == null) return;
            using var profile = WhimTexProfiler.Measure("brush.history.writeWait");
            try { page.write.GetAwaiter().GetResult(); }
            catch (Exception error) when (page.native != null)
            {
                if (page.errorReported) return;
                page.errorReported = true;
                Debug.LogError("WhimTex could not write paint history. This Undo data remains in memory: " + error.Message);
            }
        }
        internal static void Validate(Step step)
        {
            for (int i = 0; i < step.pages; i++)
                ValidatePage(step, i);
        }
        internal static void ValidatePage(Step step, int i)
        {
            Page page = step.payload[i]; WaitForWrite(page);
            if (page?.native != null) return;
            using var file = OpenPage(step, i);
            using var reader = new BinaryReader(file, System.Text.Encoding.UTF8, true);
            int format = reader.ReadInt32();
            if (format != CheckedDeflate && format != FastCheckedDeflate && format != FastCheckedRaw && format != FastCheckedBlocks)
            { VisitPage(step, i, _ => { }); return; } // Existing history in this Editor session.
            byte[] expected = Read(reader, 32);
            byte[] actual;
            if (format != CheckedDeflate) actual = HashPage(file);
            else { using var hash = SHA256.Create(); actual = hash.ComputeHash(file); }
            for (int j = 0; j < actual.Length; j++)
                if (expected[j] != actual[j]) throw new InvalidDataException("Damaged paint history page; pixels were not restored.");
            // The payload checksum does not include the format marker. Check its
            // decoded header too, so a damaged raw/Deflate marker cannot pass validation.
            file.Position = 36;
            using var decoded = format == FastCheckedRaw || format == FastCheckedBlocks ? null : new DeflateStream(file, CompressionMode.Decompress, true);
            using var payload = new BinaryReader((Stream)decoded ?? file, System.Text.Encoding.UTF8, true);
            if (payload.ReadInt32() != (format == FastCheckedBlocks ? FastCheckedBlocks : Magic)) throw new InvalidDataException("Invalid paint history encoding.");
            int count = payload.ReadInt32();
            if (count < 0 || count > PaintTileTransfer.MaximumHistoryBlocksPerPage) throw new InvalidDataException("Invalid paint history page.");
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
        private static FileStream OpenPage(Step step, int index) => new FileStream(PagePath(step, index),
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
            var retained = page?.native;
            if (retained != null) { retained.Visit(visit, scratch); return; }
            using var file = OpenPage(step, i);
            using var header = new BinaryReader(file, System.Text.Encoding.UTF8, true);
            int format = header.ReadInt32();
            bool checkedDeflate = format == CheckedDeflate || format == FastCheckedDeflate;
            bool packed = format == FastCheckedBlocks;
            bool raw = format == FastCheckedRaw || packed;
            file.Position = checkedDeflate || raw ? 36 : 0;
            using Stream zip = raw ? file : checkedDeflate ? new DeflateStream(file, CompressionMode.Decompress) : new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);
            if (reader.ReadInt32() != (packed ? FastCheckedBlocks : Magic)) throw new InvalidDataException("Unsupported paint history block.");
            int count = reader.ReadInt32();
            if (count < 0 || count > PaintTileTransfer.MaximumHistoryBlocksPerPage) throw new InvalidDataException("Invalid paint history page.");
            scratch ??= new Block();
            using var workspace = PaintHistoryCodec.Borrow();
            var codec = workspace.Value;
            for (int j = 0; j < count; j++)
            {
                var r = new RectInt(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                if (r.width < 1 || r.width > PaintTileSet.Size || r.height < 1 || r.height > PaintTileSet.Size ||
                    r.x < 0 || r.y < 0 || r.xMax > step.width || r.yMax > step.height) throw new InvalidDataException("Invalid paint history bounds.");
                int area = r.width * r.height;
                scratch.rect = r;
                using (WhimTexProfiler.Measure("brush.history.decode"))
                {
                    if (packed)
                    {
                        codec.Read(reader, ref scratch.beforeHalf, area * 8); codec.Read(reader, ref scratch.afterHalf, area * 8);
                        codec.Read(reader, ref scratch.beforeStored, area * step.bytesPerPixel); codec.Read(reader, ref scratch.afterStored, area * step.bytesPerPixel);
                    }
                    else
                    {
                        ReadInto(reader, ref scratch.beforeHalf, area * 8); ReadInto(reader, ref scratch.afterHalf, area * 8);
                        ReadInto(reader, ref scratch.beforeStored, area * step.bytesPerPixel); ReadInto(reader, ref scratch.afterStored, area * step.bytesPerPixel);
                    }
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
        internal static void WriteRows(NativeArray<byte> target, int width, RectInt rect, int bpp, byte[] bytes)
        {
            var from = bytes.AsSpan(); var to = target.AsSpan();
            int rowBytes = rect.width * bpp;
            for (int y = 0; y < rect.height; y++)
                from.Slice(y * rowBytes, rowBytes).CopyTo(to.Slice(((rect.y + y) * width + rect.x) * bpp, rowBytes));
        }
    }
}
