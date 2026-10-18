using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;

namespace DCFApixels.WhimTex
{
    // Independent LZ4 blocks, without byte shuffling or floating-point conversion.
    // Portable Burst kernels; each background writer leases its own reusable workspace.
    [BurstCompile]
    internal sealed class PaintHistoryCodec : IDisposable
    {
        internal const int MaxBytes = PaintTileSet.Size * PaintTileSet.Size * 8;
        NativeArray<byte> input, output;
        NativeArray<int> table, result;
        readonly byte[] packed = new byte[MaxBytes + MaxBytes / 255 + 32];
        static readonly Stack<PaintHistoryCodec> idle = new();
        internal long InputCopyTicks, EncodeTicks, OutputCopyTicks, WriteTicks;
        internal readonly struct Lease : IDisposable
        {
            internal readonly PaintHistoryCodec Value;
            internal Lease(PaintHistoryCodec value) { Value = value; }
            public void Dispose()
            {
                lock (idle) { if (idle.Count < 4) { idle.Push(Value); return; } }
                Value.Dispose();
            }
        }
        internal static Lease Borrow()
        {
            PaintHistoryCodec codec;
            lock (idle) codec = idle.Count > 0 ? idle.Pop() : new PaintHistoryCodec();
            codec.InputCopyTicks = codec.EncodeTicks = codec.OutputCopyTicks = codec.WriteTicks = 0;
            return new Lease(codec);
        }
        internal static void ClearPool()
        { lock (idle) while (idle.Count > 0) idle.Pop().Dispose(); }

        internal PaintHistoryCodec()
        {
            try
            {
                input = new(MaxBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                output = new(packed.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                table = new(4096, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                result = new(1, Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }

        internal void Write(BinaryWriter writer, byte[] bytes)
        {
            if (bytes.Length < 1 || bytes.Length > MaxBytes) throw new InvalidDataException("Invalid paint payload size.");
            bytes.AsSpan().CopyTo(input.AsSpan());
            WriteInput(writer, input.GetSubArray(0, bytes.Length), true);
        }
        // Atlas rows need gathering for the contiguous block codec. Gather native-to-native
        // into the leased workspace; contiguous input is encoded directly without a copy.
        internal void WriteNative(BinaryWriter writer, NativeArray<byte> source, int stride, int offset, int rowBytes, int rows, bool compress = true)
        {
            int length = checked(rowBytes * rows);
            if (length < 1 || length > MaxBytes || rowBytes > stride || offset < 0 ||
                offset + (long)(rows - 1) * stride + rowBytes > source.Length) throw new InvalidDataException("Invalid native paint block.");
            long start = Stopwatch.GetTimestamp();
            var bytes = source.GetSubArray(offset, length);
            if (stride != rowBytes && rows > 1)
            {
                var from = source.AsReadOnlySpan(); var to = input.AsSpan();
                for (int y = 0; y < rows; y++) from.Slice(offset + y * stride, rowBytes).CopyTo(to.Slice(y * rowBytes, rowBytes));
                bytes = input.GetSubArray(0, length);
            }
            InputCopyTicks += Stopwatch.GetTimestamp() - start;
            WriteInput(writer, bytes, compress);
        }
        void WriteInput(BinaryWriter writer, NativeArray<byte> bytes, bool compress)
        {
            long start = Stopwatch.GetTimestamp();
            var job = new EncodeJob { input = bytes, output = output, table = table, result = result, length = bytes.Length };
            if (compress)
            {
                LastUsedBurst = Encode(ref job);
                EncodeTicks += Stopwatch.GetTimestamp() - start;
            }
            int count = compress ? result[0] : bytes.Length;
            bool raw = count >= bytes.Length;
            if (raw) count = bytes.Length;
            start = Stopwatch.GetTimestamp();
            (raw ? bytes : output).AsReadOnlySpan().Slice(0, count).CopyTo(packed);
            OutputCopyTicks += Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            if (compress) writer.Write(raw ? -count : count);
            writer.Write(packed, 0, count);
            WriteTicks += Stopwatch.GetTimestamp() - start;
        }
        internal void Read(BinaryReader reader, ref byte[] target, int size)
        {
            if (size < 1 || size > MaxBytes) throw new InvalidDataException("Invalid paint payload size.");
            int count = reader.ReadInt32();
            if (count == -size)
            {
                if (target == null || target.Length != size) target = new byte[size];
                ReadExactly(reader, target, size); return;
            }
            if (count < 1 || count >= size) throw new InvalidDataException("Invalid packed paint payload size.");
            ReadExactly(reader, packed, count);
            packed.AsSpan(0, count).CopyTo(input.AsSpan());
            var job = new DecodeJob { input = input, output = output, length = count, size = size };
            if (!Decode(ref job)) throw new InvalidDataException("Invalid paint block encoding.");
            if (target == null || target.Length != size) target = new byte[size];
            output.AsReadOnlySpan().Slice(0, size).CopyTo(target);
        }
        static void ReadExactly(BinaryReader reader, byte[] target, int size)
        {
            int at = 0;
            while (at < size) { int count = reader.Read(target, at, size - at); if (count == 0) throw new EndOfStreamException(); at += count; }
        }
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        static bool Decode(ref DecodeJob job) => job.Execute();
        // LZ4 block format and fast match search adapted from lz4.c v1.9.4.
        // Copyright (C) 2011-2020, Yann Collet. BSD-2-Clause; see ThirdPartyNotices.md.
        // Native-width reads use portable memcpy, including at unaligned pixel offsets.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe uint Read32(byte* p) { uint value; UnsafeUtility.MemCpy(&value, p, 4); return value; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe ulong Read64(byte* p) { ulong value; UnsafeUtility.MemCpy(&value, p, 8); return value; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static unsafe int Hash(byte* p) => (int)(unchecked((Read64(p) << 24) * 889523592379UL) >> 52);
        static unsafe void Extra(byte* output, int count, ref int at)
        {
            while (count >= 255) { output[at++] = 255; count -= 255; }
            output[at++] = (byte)count;
        }
        static unsafe int Extended(byte* input, int length, ref int at, int count, int maximum)
        {
            if (count == 15)
            {
                int next;
                do
                {
                    if (at >= length) return -1;
                    next = input[at++];
                    if (count > maximum - next) return -1;
                    count += next;
                } while (next == 255);
            }
            return count;
        }
        struct DecodeJob
        {
            [ReadOnly] public NativeArray<byte> input;
            public NativeArray<byte> output;
            public int length, size;
            public unsafe bool Execute()
            {
                if (length < 1 || length > input.Length || size < 1 || size > output.Length) return false;
                byte* src = (byte*)input.GetUnsafeReadOnlyPtr();
                byte* dst = (byte*)output.GetUnsafePtr();
                int at = 0, written = 0;
                while (at < length)
                {
                    int token = src[at++];
                    int literal = token >> 4;
                    // Fixed-size copies let Burst inline common short sequences. The
                    // extra bytes stay inside BOTH buffers and are overwritten before use.
                    if (literal < 15 && at <= length - 16 && written <= size - 32)
                        UnsafeUtility.MemCpy(dst + written, src + at, 16);
                    else
                    {
                        literal = Extended(src, length, ref at, literal, size - written);
                        if (literal < 0 || literal > length - at || literal > size - written) return false;
                        if (literal > 0) UnsafeUtility.MemCpy(dst + written, src + at, literal);
                    }
                    at += literal; written += literal;
                    if (at == length) return (token & 15) == 0 && written == size;
                    if (at > length - 2) return false;
                    int distance = src[at] | src[at + 1] << 8; at += 2;
                    if (distance < 1 || distance > written) return false;
                    if ((token & 15) < 15 && distance >= 8 && written <= size - 18)
                    {
                        // Sequential disjoint chunks also support distance 8..17:
                        // later chunks may read bytes produced by the previous chunk.
                        UnsafeUtility.MemCpy(dst + written, dst + written - distance, 8);
                        UnsafeUtility.MemCpy(dst + written + 8, dst + written - distance + 8, 8);
                        UnsafeUtility.MemCpy(dst + written + 16, dst + written - distance + 16, 2);
                        written += (token & 15) + 4;
                        continue;
                    }
                    int match = Extended(src, length, ref at, token & 15, size - written - 4);
                    if (match < 0 || match > size - written - 4) return false;
                    match += 4;
                    if (distance == 1) UnsafeUtility.MemSet(dst + written, dst[written - 1], match);
                    else
                    {
                        // Each memcpy is disjoint even when the LZ4 match overlaps itself.
                        int copied = 0;
                        while (copied < match)
                        {
                            int count = Math.Min(distance + copied, match - copied);
                            UnsafeUtility.MemCpy(dst + written + copied, dst + written - distance, count);
                            copied += count;
                        }
                    }
                    written += match;
                }
                return false;
            }
        }
        public void Dispose()
        {
            if (input.IsCreated) input.Dispose(); if (output.IsCreated) output.Dispose();
            if (table.IsCreated) table.Dispose(); if (result.IsCreated) result.Dispose();
        }

        internal bool LastUsedBurst { get; private set; }
        [BurstDiscard] static void MarkManaged(ref bool accelerated) => accelerated = false;
        [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        static bool Encode(ref EncodeJob job)
        {
            bool accelerated = true; MarkManaged(ref accelerated); job.Execute(); return accelerated;
        }
        struct EncodeJob
        {
            [ReadOnly] public NativeArray<byte> input;
            public NativeArray<byte> output;
            public NativeArray<int> table, result;
            public int length;
            public unsafe void Execute()
            {
                byte* src = (byte*)input.GetUnsafeReadOnlyPtr();
                byte* dst = (byte*)output.GetUnsafePtr();
                int* hashes = (int*)table.GetUnsafePtr();
                UnsafeUtility.MemClear(hashes, 4096 * sizeof(int));
                int i = 1, anchor = 0, target = 0;
                if (length >= 13) hashes[Hash(src)] = 0;
                while (i <= length - 12)
                {
                    int forward = i, step = 1, attempts = 64, match;
                    int hash = Hash(src + forward);
                    while (true)
                    {
                        i = forward; forward += step; step = attempts++ >> 6;
                        if (forward > length - 12) goto LastLiterals;
                        match = hashes[hash];
                        int nextHash = Hash(src + forward);
                        hashes[hash] = i; hash = nextHash;
                        if (i - match <= 65535 && Read32(src + match) == Read32(src + i)) break;
                    }
                    // Extend backwards only into the unencoded literals, never a prior sequence.
                    while (i > anchor && match > 0 && src[i - 1] == src[match - 1]) { i--; match--; }
                    int literal = i - anchor, tokenAt = target++;
                    dst[tokenAt] = (byte)(Math.Min(literal, 15) << 4);
                    if (literal >= 15) Extra(dst, literal - 15, ref target);
                    if (literal > 0) UnsafeUtility.MemCpy(dst + target, src + anchor, literal);
                    target += literal;
                    int distance = i - match;
                    dst[target++] = (byte)distance; dst[target++] = (byte)(distance >> 8);
                    int end = i + 4, from = match + 4;
                    // Five trailing literals and a final match starting >=12 bytes before EOF.
                    while (end <= length - 13 && Read64(src + from) == Read64(src + end)) { from += 8; end += 8; }
                    while (end < length - 5 && src[from] == src[end]) { from++; end++; }
                    int matchLength = end - i - 4;
                    dst[tokenAt] |= (byte)Math.Min(matchLength, 15);
                    if (matchLength >= 15) Extra(dst, matchLength - 15, ref target);
                    i = anchor = end;
                    if (i <= length - 12) hashes[Hash(src + i - 2)] = i - 2;
                }
            LastLiterals:
                int tail = length - anchor;
                dst[target++] = (byte)(Math.Min(tail, 15) << 4);
                if (tail >= 15) Extra(dst, tail - 15, ref target);
                UnsafeUtility.MemCpy(dst + target, src + anchor, tail); target += tail;
                result[0] = target;
            }
        }

    }
}
