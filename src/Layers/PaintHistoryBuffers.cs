using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
            if (length < 1 || length > PaintTileTransfer.MaximumPagePixels * 8)
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
    [BurstCompile]
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
        internal static bool LastPatchUsedBurst { get; private set; }
        internal long ResidentBytes => (long)beforeHalf.Length + afterHalf.Length + beforeStored.Length + afterStored.Length;
        internal PaintHistoryPayload(int width, int pixels, int bpp, int capacity)
        {
            this.width = width; this.bpp = bpp; blocks = new List<Entry>(capacity);
            beforeStored = PaintHistoryBuffers.Rent(pixels * bpp);
        }
        internal void Add(NativeArray<byte> cpu, int cpuWidth, RectInt rect, RectInt slot, NativeArray<byte> stored)
        {
            // Validate the entire copy before mutating CPU storage. The three planes
            // have separate owners and remain alive for this synchronous call.
            ValidateRect(cpu.Length, cpuWidth, rect, bpp);
            ValidateRect(beforeStored.Length, width, slot, bpp);
            ValidateRect(stored.Length, width, slot, bpp);
            if (rect.size != slot.size) throw new ArgumentException("Mismatched paint block dimensions.");
            var patch = new Patch
            {
                cpu = cpu, before = beforeStored, after = stored,
                cpuOffset = (rect.y * cpuWidth + rect.x) * bpp,
                pageOffset = (slot.y * width + slot.x) * bpp,
                cpuStride = cpuWidth * bpp, pageStride = width * bpp,
                rowBytes = rect.width * bpp, rows = rect.height
            };
            LastPatchUsedBurst = PatchRows(ref patch);
            blocks.Add(new Entry(rect, slot));
            Bytes += (long)rect.width * rect.height * (16 + 2 * bpp);
        }
        static void ValidateRect(int length, int stridePixels, RectInt rect, int bpp)
        {
            if ((bpp != 4 && bpp != 8) || stridePixels <= 0 || rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0 ||
                (long)rect.x + rect.width > stridePixels ||
                (((long)rect.y + rect.height - 1) * stridePixels + rect.x + rect.width) * bpp > length)
                throw new ArgumentException("Invalid paint block bounds.");
        }
        struct Patch
        {
            internal NativeArray<byte> cpu, before;
            [ReadOnly] internal NativeArray<byte> after;
            internal int cpuOffset, pageOffset, cpuStride, pageStride, rowBytes, rows;
        }
        [BurstDiscard] static void MarkManaged(ref bool accelerated) => accelerated = false;
        [BurstCompile(CompileSynchronously = true, OptimizeFor = OptimizeFor.Performance)]
        static unsafe bool PatchRows(ref Patch patch)
        {
            bool accelerated = true; MarkManaged(ref accelerated);
            byte* cpu = (byte*)patch.cpu.GetUnsafePtr() + patch.cpuOffset;
            byte* before = (byte*)patch.before.GetUnsafePtr() + patch.pageOffset;
            byte* after = (byte*)patch.after.GetUnsafeReadOnlyPtr() + patch.pageOffset;
            // Capture then replace each row while the CPU cache line is still hot.
            // Raw copies retain hidden RGB, signed HDR and every Half bit.
            for (int y = 0; y < patch.rows; y++)
            {
                UnsafeUtility.MemCpy(before, cpu, patch.rowBytes);
                UnsafeUtility.MemCpy(cpu, after, patch.rowBytes);
                cpu += patch.cpuStride; before += patch.pageStride; after += patch.pageStride;
            }
            return accelerated;
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
