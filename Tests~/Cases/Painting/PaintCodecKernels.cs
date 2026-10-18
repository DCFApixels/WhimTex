using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using DCFApixels.WhimTex;

// Diagnostic only. Native LZ4 use was explicitly authorized for this comparison.
// No production dependency, hardware intrinsics or drawing-path changes.
[BurstCompile]
public static unsafe class PaintCodecKernels
{
    [DllImport("liblz4", CallingConvention = CallingConvention.Cdecl)] public static extern int LZ4_versionNumber();
    [DllImport("liblz4", CallingConvention = CallingConvention.Cdecl)] static extern int LZ4_compress_default(byte* src, byte* dst, int size, int capacity);
    [DllImport("liblz4", CallingConvention = CallingConvention.Cdecl)] static extern int LZ4_decompress_safe(byte* src, byte* dst, int size, int capacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int Kernel(int mode, byte* input, byte* output, int length, int bpp);
    static FunctionPointer<Kernel> compiled;
    static bool direct;
    public static void Initialize()
    {
        compiled = BurstCompiler.CompileFunctionPointer<Kernel>(Run);
        direct = Run(4, null, null, 0, 0) == 1;
        if (!direct && compiled.Invoke(4, null, null, 0, 0) != 1) throw new Exception("RLE/shuffle did not compile with Burst (direct or function pointer)");
        if (LZ4_versionNumber() != 10901) throw new Exception("Unexpected installed LZ4 version");
    }
    static int Invoke(int mode, byte* input, byte* output, int length, int bpp) => direct ? Run(mode, input, output, length, bpp) : compiled.Invoke(mode, input, output, length, bpp);
    public static bool Same(NativeArray<byte> left, int leftOffset, NativeArray<byte> right, int rightOffset, int size)
        => Invoke(5, Workspace.Ptr(left) + leftOffset, Workspace.Ptr(right) + rightOffset, size, 0) == 1;
    [BurstDiscard] static void Managed(ref int value) { value = 0; }
    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    [AOT.MonoPInvokeCallback(typeof(Kernel))]
    public static int Run(int mode, byte* input, byte* output, int length, int bpp)
    {
        if (mode == 4) { int burst = 1; Managed(ref burst); return burst; }
        if (mode == 5) return UnsafeUtility.MemCmp(input, output, length) == 0 ? 1 : 0;
        if (mode == 0 || mode == 1)
        {
            int pixels = length / bpp;
            // Byte planes, including separate high/low bytes for Half channels.
            if (mode == 0)
                for (int c = 0; c < bpp; c++) for (int p = 0; p < pixels; p++) output[c * pixels + p] = input[p * bpp + c];
            else
                for (int p = 0; p < pixels; p++) for (int c = 0; c < bpp; c++) output[p * bpp + c] = input[c * pixels + p];
            return length;
        }
        if (mode == 2)
        {
            // Byte PackBits: literal 1..128, repeat 3..128. Raw fallback is in the caller.
            int at = 0, written = 0;
            while (at < length)
            {
                int run = 1;
                while (run < 128 && at + run < length && input[at + run] == input[at]) run++;
                if (run >= 3) { output[written++] = (byte)(257 - run); output[written++] = input[at]; at += run; }
                else
                {
                    int start = at; at += run;
                    while (at < length && at - start < 128)
                    {
                        if (at + 2 < length && input[at] == input[at + 1] && input[at] == input[at + 2]) break;
                        at++;
                    }
                    int count = at - start; output[written++] = (byte)(count - 1);
                    UnsafeUtility.MemCpy(output + written, input + start, count); written += count;
                }
            }
            return written;
        }
        int read = 0, dest = 0;
        while (read < length)
        {
            int token = input[read++];
            int count = token <= 127 ? token + 1 : 257 - token;
            if (token == 128 || dest + count > bpp) return -1;
            if (token <= 127)
            {
                if (read + count > length) return -1;
                UnsafeUtility.MemCpy(output + dest, input + read, count); read += count;
            }
            else { if (read == length) return -1; UnsafeUtility.MemSet(output + dest, input[read++], count); }
            dest += count;
        }
        return dest;
    }

    public sealed class Workspace : IDisposable
    {
        public const int Maximum = 256 * 256 * 8;
        public NativeArray<byte> source = new(Maximum, Allocator.Persistent), shuffled = new(Maximum, Allocator.Persistent),
            packed = new(Maximum + Maximum / 128 + 256, Allocator.Persistent), decoded = new(Maximum, Allocator.Persistent),
            restored = new(Maximum, Allocator.Persistent);
        readonly NativeArray<int> table = new(16384, Allocator.Persistent), result = new(1, Allocator.Persistent);
        readonly object encodeJob, decodeJob;
        readonly MethodInfo encode, decode;
        readonly FieldInfo encInput, encLength, decLength, decSize;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public Workspace()
        {
            // Project-owned implementation only; no reflected Unity internals.
            var codec = typeof(DrawingLayerBehaviour).Assembly.GetType("DCFApixels.WhimTex.PaintHistoryCodec", true);
            var et = codec.GetNestedType("EncodeJob", BindingFlags.NonPublic);
            var dt = codec.GetNestedType("DecodeJob", BindingFlags.NonPublic);
            encodeJob = Activator.CreateInstance(et); decodeJob = Activator.CreateInstance(dt);
            encInput = et.GetField("input", Flags); encLength = et.GetField("length", Flags);
            decLength = dt.GetField("length", Flags); decSize = dt.GetField("size", Flags);
            et.GetField("output", Flags).SetValue(encodeJob, packed); et.GetField("table", Flags).SetValue(encodeJob, table);
            et.GetField("result", Flags).SetValue(encodeJob, result);
            dt.GetField("input", Flags).SetValue(decodeJob, packed); dt.GetField("output", Flags).SetValue(decodeJob, decoded);
            encode = codec.GetMethod("Encode", BindingFlags.Static | BindingFlags.NonPublic);
            decode = codec.GetMethod("Decode", BindingFlags.Static | BindingFlags.NonPublic);
        }
        public static byte* Ptr(NativeArray<byte> array) => (byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(array);
        readonly object[] encodeArgs = new object[1], decodeArgs = new object[1];
        public int Encode(int codec, bool shuffle, int length, int bpp)
        {
            var from = source;
            if (shuffle) { Invoke(0, Ptr(source), Ptr(shuffled), length, bpp); from = shuffled; }
            int count;
            if (codec == 0)
            {
                encInput.SetValue(encodeJob, from); encLength.SetValue(encodeJob, length); encodeArgs[0] = encodeJob;
                if (!(bool)encode.Invoke(null, encodeArgs)) throw new Exception("Production codec is not using Burst");
                count = result[0];
            }
            else if (codec == 1) count = LZ4_compress_default(Ptr(from), Ptr(packed), length, packed.Length);
            else count = Invoke(2, Ptr(from), Ptr(packed), length, bpp);
            if (count <= 0) throw new Exception("Compression failed");
            if (count < length) return count;
            NativeArray<byte>.Copy(source, packed, length);
            return -length;
        }
        public void Decode(int codec, bool shuffle, int count, int length, int bpp)
        {
            if (count < 0) { NativeArray<byte>.Copy(packed, restored, length); return; }
            if (codec == 0)
            {
                decLength.SetValue(decodeJob, count); decSize.SetValue(decodeJob, length); decodeArgs[0] = decodeJob;
                if (!(bool)decode.Invoke(null, decodeArgs)) throw new Exception("Production decode failed");
            }
            else
            {
                int size = codec == 1 ? LZ4_decompress_safe(Ptr(packed), Ptr(decoded), count, length)
                    : Invoke(3, Ptr(packed), Ptr(decoded), count, length);
                if (size != length) throw new Exception("Decoded size mismatch");
            }
            if (shuffle) Invoke(1, Ptr(decoded), Ptr(restored), length, bpp);
            else NativeArray<byte>.Copy(decoded, restored, length);
        }
        public bool TryDecodeCurrent(int count, int size)
        {
            decLength.SetValue(decodeJob, count); decSize.SetValue(decodeJob, size); decodeArgs[0] = decodeJob;
            return (bool)decode.Invoke(null, decodeArgs);
        }
        public void Dispose() { source.Dispose(); shuffled.Dispose(); packed.Dispose(); decoded.Dispose(); restored.Dispose(); table.Dispose(); result.Dispose(); }
    }
}
