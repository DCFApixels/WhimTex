using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    // Shared by readback and journal writers. A buffer has exactly one owner until
    // GPU completion and all CPU consumers have finished; only idle buffers enter the pool.
    internal static class PaintHistoryBuffers
    {
        internal const long IdleBudget = 64L * 1024 * 1024;
        static readonly object gate = new();
        static readonly Dictionary<int, Stack<NativeArray<byte>>> idle = new();
        static long activeBytes, idleBytes, allocatedBytes, reusedBytes;
        internal static long ActiveBytes => Interlocked.Read(ref activeBytes);
        internal static long IdleBytes => Interlocked.Read(ref idleBytes);
        internal static long AllocatedBytes => Interlocked.Read(ref allocatedBytes);
        internal static long ReusedBytes => Interlocked.Read(ref reusedBytes);

        internal static NativeArray<byte> Rent(int length)
        {
            if (length < 1 || length > PaintTileTransfer.PageSize * PaintTileTransfer.PageSize * 8)
                throw new ArgumentOutOfRangeException(nameof(length));
            NativeArray<byte> result = default;
            lock (gate)
                if (idle.TryGetValue(length, out var bucket) && bucket.Count > 0)
                { result = bucket.Pop(); idleBytes -= length; reusedBytes += length; }
            if (!result.IsCreated)
            {
                result = new NativeArray<byte>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                Interlocked.Add(ref allocatedBytes, length);
            }
            Interlocked.Add(ref activeBytes, length);
            return result;
        }
        internal static void Return(ref NativeArray<byte> buffer)
        {
            if (!buffer.IsCreated) return;
            var value = buffer; buffer = default;
            int length = value.Length;
            lock (gate)
            {
                Interlocked.Add(ref activeBytes, -length);
                if (idleBytes + length <= IdleBudget)
                {
                    if (!idle.TryGetValue(length, out var bucket)) idle.Add(length, bucket = new());
                    bucket.Push(value); idleBytes += length; return;
                }
            }
            value.Dispose();
        }
        internal static void Clear()
        {
            lock (gate)
            {
                foreach (var bucket in idle.Values) while (bucket.Count > 0) bucket.Pop().Dispose();
                idle.Clear(); idleBytes = 0;
            }
        }
    }

    // Readback planes are handed to the writer, not copied into four managed arrays
    // per tile. Only the old stored pixels need their own snapshot before FIFO patching.
    internal sealed class PaintHistoryPayload : IDisposable
    {
        internal readonly struct Entry
        {
            internal readonly RectInt rect, slot;
            internal Entry(RectInt rect, RectInt slot) { this.rect = rect; this.slot = slot; }
        }
        internal readonly int width, bpp;
        internal readonly List<Entry> blocks;
        internal NativeArray<byte> beforeHalf, afterHalf, beforeStored, afterStored;
        internal long Bytes { get; private set; }
        internal long ResidentBytes => (long)beforeHalf.Length + afterHalf.Length + beforeStored.Length + afterStored.Length;
        internal PaintHistoryPayload(int width, int pixels, int bpp, int capacity)
        {
            this.width = width; this.bpp = bpp; blocks = new List<Entry>(capacity);
            beforeStored = PaintHistoryBuffers.Rent(pixels * bpp);
        }
        internal void Add(NativeArray<byte> cpu, int cpuWidth, RectInt rect, RectInt slot, NativeArray<byte> stored)
        {
            CopyRows(cpu, cpuWidth, rect, beforeStored, width, slot, bpp);
            CopyRows(stored, width, slot, cpu, cpuWidth, rect, bpp);
            blocks.Add(new Entry(rect, slot));
            Bytes += (long)rect.width * rect.height * (16 + 2 * bpp);
        }
        internal static void CopyRows(NativeArray<byte> source, int sourceWidth, RectInt fromRect,
            NativeArray<byte> target, int targetWidth, RectInt toRect, int bpp)
        {
            var from = source.AsReadOnlySpan(); var to = target.AsSpan();
            int rowBytes = fromRect.width * bpp;
            for (int y = 0; y < fromRect.height; y++)
                from.Slice(((fromRect.y + y) * sourceWidth + fromRect.x) * bpp, rowBytes)
                    .CopyTo(to.Slice(((toRect.y + y) * targetWidth + toRect.x) * bpp, rowBytes));
        }
        internal void Visit(Action<PaintHistoryStore.Block> visit, PaintHistoryStore.Block scratch)
        {
            scratch ??= new PaintHistoryStore.Block();
            foreach (var b in blocks)
            {
                scratch.rect = b.rect;
                Extract(beforeHalf, b.slot, 8, ref scratch.beforeHalf); Extract(afterHalf, b.slot, 8, ref scratch.afterHalf);
                Extract(beforeStored, b.slot, bpp, ref scratch.beforeStored); Extract(afterStored, b.slot, bpp, ref scratch.afterStored);
                visit(scratch);
            }
        }
        void Extract(NativeArray<byte> source, RectInt r, int bytesPerPixel, ref byte[] target)
        {
            int row = r.width * bytesPerPixel, length = row * r.height;
            if (target == null || target.Length != length) target = new byte[length];
            var from = source.AsReadOnlySpan();
            for (int y = 0; y < r.height; y++)
                from.Slice(((r.y + y) * width + r.x) * bytesPerPixel, row).CopyTo(target.AsSpan(y * row, row));
        }
        public void Dispose()
        {
            PaintHistoryBuffers.Return(ref beforeHalf); PaintHistoryBuffers.Return(ref afterHalf);
            PaintHistoryBuffers.Return(ref beforeStored); PaintHistoryBuffers.Return(ref afterStored);
        }
    }
}
