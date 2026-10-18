using System;
using Unity.Collections;
using WhimTex.Tests;

public static class PaintCodecPreflight
{
    public static string Check() => TestContext.Run("Lossless codec comparison preflight", t =>
    {
        PaintCodecKernels.Initialize();
        t.Equal(10901, PaintCodecKernels.LZ4_versionNumber(), "Installed LZ4 1.9.1");
        using var w = new PaintCodecKernels.Workspace();
        t.True(PaintCodecKernels.Same(w.source, 0, w.restored, 0, w.source.Length), "Exact comparison accepts equal arrays");
        foreach (int offset in new[] { 0, 1, 262143, w.source.Length - 1 })
        {
            w.restored[offset] = 1;
            t.True(!PaintCodecKernels.Same(w.source, 0, w.restored, 0, w.source.Length), "Exact comparison detects mismatch at " + offset);
            w.restored[offset] = 0;
        }
        t.True(PaintCodecKernels.Same(w.source, 3, w.restored, 5, 127), "Exact comparison accepts unaligned subranges");
        uint random = 19;
        foreach (int bpp in new[] { 4, 8 }) foreach (int pixels in new[] { 1, 31, 32, 33, 8191, 65536 })
        foreach (int pattern in new[] { 0, 1, 2, 3 })
        {
            int size = pixels * bpp;
            for (int i = 0; i < size; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                w.source[i] = pattern == 0 ? (byte)0 : pattern == 1 ? (byte)(i / 137) :
                    pattern == 2 ? (byte)random : (byte)(i % bpp == 3 ? 0 : (i / bpp) % 251);
            }
            foreach (int codec in new[] { 0, 1, 2 }) foreach (bool shuffle in new[] { false, true })
            {
                int count = w.Encode(codec, shuffle, size, bpp);
                w.Decode(codec, shuffle, count, size, bpp);
                t.True(PaintCodecKernels.Same(w.source, 0, w.restored, 0, size),
                    "Exact bytes including hidden color/arbitrary Half bit patterns: " + codec + "/" + shuffle + "/" + size + "/" + pattern);
            }
        }
        // Independent interoperability, including odd lengths, tiny blocks, long offsets,
        // byte runs, and arbitrary bit patterns (NaNs and hidden RGB are just bytes).
        foreach (int size in new[] { 1, 7, 12, 13, 15, 16, 17, 255, 256, 257, 65535, 65536, 65537, 524288 })
        foreach (int distance in new[] { 1, 2, 3, 4, 7, 8, 16, 251, 65520, 65535, 65536 })
        {
            for (int i = 0; i < size; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                w.source[i] = i >= distance ? w.source[i - distance] : (byte)random;
            }
            foreach (int encoder in new[] { 0, 1 })
            {
                int count = w.Encode(encoder, false, size, 1);
                w.Decode(1 - encoder, false, count, size, 1);
                t.True(PaintCodecKernels.Same(w.source, 0, w.restored, 0, size),
                    "Cross-decode LZ4 encoder=" + encoder + " size=" + size + " distance=" + distance);
            }
        }
        // A decoder may accept a random stream; even then it must respect its exact
        // destination capacity. Guard bytes catch short-input and overlap overruns.
        for (int sample = 0; sample < 2048; sample++)
        {
            int count = 1 + sample % 96, size = 1 + sample % 256;
            for (int i = 0; i < count; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                w.packed[i] = (byte)random;
            }
            for (int i = size; i < size + 32; i++) w.decoded[i] = 0xAC;
            w.TryDecodeCurrent(count, size);
            for (int i = size; i < size + 32; i++) t.Equal((byte)0xAC, w.decoded[i], "Malformed input stays inside output capacity");
        }
    });
}
