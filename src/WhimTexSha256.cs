using System;
using System.Security.Cryptography;
using Unity.Burst;
using Unity.Collections;

namespace DCFApixels.WhimTex
{
    /// <summary>Portable SHA-256 for document integrity. No ISA intrinsics or OS-specific providers.</summary>
    [BurstCompile]
    internal static class WhimTexSha256
    {
        // Bound staging memory independently of Drawing size and simultaneous container workers.
        internal const int BufferBytes = 1024 * 1024;

        internal static byte[] Compute(ReadOnlySpan<byte> data)
        {
            // Small metadata does not need a native allocation or a first-use compiler invocation.
            // Respect the user's Burst setting; .NET remains the portable fallback.
            return data.Length < 4096 || !BurstCompiler.Options.EnableBurstCompilation
                ? ComputeManaged(data) : ComputeAccelerated(data, out _);
        }

        internal static byte[] ComputeManaged(ReadOnlySpan<byte> data)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(data);
            return hash.GetHashAndReset();
        }

        // Separate entry lets benchmarks prove actual Burst execution without changing global settings.
        // Direct calls also work on ordinary container worker threads.
        internal static byte[] ComputeAccelerated(ReadOnlySpan<byte> data, out bool ranBurst)
        {
            using var input = new NativeArray<byte>(Math.Min(BufferBytes, Math.Max(128, data.Length)),
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            var state = new State
            {
                a = 0x6a09e667u, b = 0xbb67ae85u, c = 0x3c6ef372u, d = 0xa54ff53au,
                e = 0x510e527fu, f = 0x9b05688cu, g = 0x1f83d9abu, h = 0x5be0cd19u
            };
            ranBurst = true;
            int complete = data.Length & ~63;
            for (int offset = 0; offset < complete;)
            {
                int count = Math.Min(input.Length & ~63, complete - offset);
                data.Slice(offset, count).CopyTo(input.AsSpan());
                ranBurst &= Process(input, count, ref state);
                offset += count;
            }
            int remaining = data.Length - complete;
            int padded = remaining < 56 ? 64 : 128;
            var tail = input.AsSpan().Slice(0, padded);
            tail.Clear();
            data.Slice(complete).CopyTo(tail);
            tail[remaining] = 0x80;
            ulong bits = (ulong)data.Length * 8;
            for (int i = 0; i < 8; i++) tail[padded - 1 - i] = (byte)(bits >> (8 * i));
            ranBurst &= Process(input, padded, ref state);

            var digest = new byte[32];
            WriteWord(digest, 0, state.a); WriteWord(digest, 4, state.b);
            WriteWord(digest, 8, state.c); WriteWord(digest, 12, state.d);
            WriteWord(digest, 16, state.e); WriteWord(digest, 20, state.f);
            WriteWord(digest, 24, state.g); WriteWord(digest, 28, state.h);
            return digest;
        }

        private static void WriteWord(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24); bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8); bytes[offset + 3] = (byte)value;
        }

        private struct State { public uint a, b, c, d, e, f, g, h; }

        // SHA-256 constants and operations from FIPS 180-4 sections 4.1.2, 4.2.2 and 6.2.
        private static readonly uint[] RoundConstants =
        {
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
            0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
            0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
            0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
            0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
            0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
        };

        private static uint Rotate(uint value, int bits) => (value >> bits) | (value << (32 - bits));

        [BurstDiscard]
        private static void MarkManaged(ref bool burst) => burst = false;

        // Synchronous compilation prevents a first large save from silently running this kernel in Mono.
        // Not a Job: container preparation already parallelizes INDEPENDENT messages. SHA blocks within
        // one message must retain their sequential chaining state to remain standard SHA-256.
        [BurstCompile(CompileSynchronously = true)]
        private static bool Process(in NativeArray<byte> input, int length, ref State state)
        {
            bool burst = true;
            MarkManaged(ref burst);
            var words = new FixedList128Bytes<uint> { Length = 16 };
            unchecked
            {
                for (int offset = 0; offset < length; offset += 64)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int p = offset + i * 4;
                        words[i] = ((uint)input[p] << 24) | ((uint)input[p + 1] << 16) |
                            ((uint)input[p + 2] << 8) | input[p + 3];
                    }
                    uint a = state.a, b = state.b, c = state.c, d = state.d;
                    uint e = state.e, f = state.f, g = state.g, h = state.h;
                    for (int i = 0; i < 64; i++)
                    {
                        int slot = i & 15;
                        if (i >= 16)
                        {
                            uint x = words[(i - 15) & 15], y = words[(i - 2) & 15];
                            words[slot] += (Rotate(x, 7) ^ Rotate(x, 18) ^ (x >> 3)) +
                                words[(i - 7) & 15] + (Rotate(y, 17) ^ Rotate(y, 19) ^ (y >> 10));
                        }
                        uint t1 = h + (Rotate(e, 6) ^ Rotate(e, 11) ^ Rotate(e, 25)) +
                            ((e & f) ^ (~e & g)) + RoundConstants[i] + words[slot];
                        uint t2 = (Rotate(a, 2) ^ Rotate(a, 13) ^ Rotate(a, 22)) +
                            ((a & b) ^ (a & c) ^ (b & c));
                        h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                    }
                    state.a += a; state.b += b; state.c += c; state.d += d;
                    state.e += e; state.f += f; state.g += g; state.h += h;
                }
            }
            return burst;
        }
    }
}
