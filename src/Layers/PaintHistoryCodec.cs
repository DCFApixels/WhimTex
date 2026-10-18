using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;

namespace DCFApixels.WhimTex
{
    // Independent lossless blocks: literals and backward byte matches. No floating-point
    // arithmetic, quantization, platform libraries, or persistent dictionaries.
    [BurstCompile]
    internal sealed class PaintHistoryCodec : IDisposable
    {
        internal const int MaxBytes = PaintTileSet.Size * PaintTileSet.Size * 8;
        NativeArray<byte> input, output;
        NativeArray<int> table, result;
        readonly byte[] packed = new byte[MaxBytes + MaxBytes / 255 + 32];

        internal PaintHistoryCodec()
        {
            try
            {
                input = new(MaxBytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                output = new(packed.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                table = new(16384, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                result = new(1, Allocator.Persistent);
            }
            catch { Dispose(); throw; }
        }

        internal void Write(BinaryWriter writer, byte[] bytes)
        {
            if (bytes.Length < 1 || bytes.Length > MaxBytes) throw new InvalidDataException("Invalid paint payload size.");
            bytes.AsSpan().CopyTo(input.AsSpan());
            var job = new EncodeJob { input = input, output = output, table = table, result = result, length = bytes.Length };
            LastUsedBurst = Encode(ref job);
            int count = result[0];
            if (count >= bytes.Length) { writer.Write(-bytes.Length); writer.Write(bytes); return; }
            output.AsReadOnlySpan().Slice(0, count).CopyTo(packed);
            writer.Write(count); writer.Write(packed, 0, count);
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
        struct DecodeJob
        {
            [ReadOnly] public NativeArray<byte> input;
            public NativeArray<byte> output;
            public int length, size;
            int Extended(ref int at, int count, int maximum)
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
            public bool Execute()
            {
                int at = 0, written = 0;
                while (at < length)
                {
                    int token = input[at++];
                    int literal = Extended(ref at, token >> 4, size - written);
                    if (literal < 0 || literal > length - at || literal > size - written) return false;
                    NativeArray<byte>.Copy(input, at, output, written, literal);
                    at += literal; written += literal;
                    if (at == length) return (token & 15) == 0 && written == size;
                    if (at > length - 2) return false;
                    int distance = input[at] | input[at + 1] << 8; at += 2;
                    if (distance < 1 || distance > written) return false;
                    int match = Extended(ref at, token & 15, size - written - 4);
                    if (match < 0 || match > size - written - 4) return false;
                    match += 4;
                    // Grow the already decoded prefix with disjoint copies. This keeps
                    // overlapping runs exact without a byte-by-byte dependency chain.
                    int copied = 0;
                    while (copied < match)
                    {
                        int count = Math.Min(distance + copied, match - copied);
                        NativeArray<byte>.Copy(output, written - distance, output, written + copied, count);
                        copied += count;
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
            uint Word(int at) => (uint)(input[at] | input[at + 1] << 8 | input[at + 2] << 16 | input[at + 3] << 24);
            void Extra(int count, ref int at)
            {
                while (count >= 255) { output[at++] = 255; count -= 255; }
                output[at++] = (byte)count;
            }
            public void Execute()
            {
                for (int t = 0; t < table.Length; t++) table[t] = -1;
                int i = 0, literal = 0, target = 0, misses = 0;
                // Leave five trailing literals and twelve bytes before the final match.
                while (i <= length - 12)
                {
                    uint word = Word(i); int key = (int)(unchecked(word * 2654435761u) >> 18);
                    int match = table[key]; table[key] = i;
                    if (match < 0 || i - match > 65535 || Word(match) != word)
                    { i += 1 + (misses++ >> 6); continue; }
                    int end = i + 4, from = match + 4;
                    while (end <= length - 9 && Word(from) == Word(end)) { from += 4; end += 4; }
                    while (end < length - 5 && input[from] == input[end]) { from++; end++; }
                    int count = i - literal, matchLength = end - i - 4;
                    output[target++] = (byte)(Math.Min(count, 15) << 4 | Math.Min(matchLength, 15));
                    if (count >= 15) Extra(count - 15, ref target);
                    NativeArray<byte>.Copy(input, literal, output, target, count); target += count;
                    int distance = i - match; output[target++] = (byte)distance; output[target++] = (byte)(distance >> 8);
                    if (matchLength >= 15) Extra(matchLength - 15, ref target);
                    i = literal = end; misses = 0;
                    if (i >= 2) table[(int)(unchecked(Word(i - 2) * 2654435761u) >> 18)] = i - 2;
                }
                int tail = length - literal; output[target++] = (byte)(Math.Min(tail, 15) << 4);
                if (tail >= 15) Extra(tail - 15, ref target);
                NativeArray<byte>.Copy(input, literal, output, target, tail); target += tail;
                result[0] = target;
            }
        }
    }
}
