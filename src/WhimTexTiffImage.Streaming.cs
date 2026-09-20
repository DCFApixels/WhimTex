using System;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public static partial class WhimTexTiffImage
    {
        internal static bool HasValuesOutsideUnitRange(NativeArray<byte> raw, int sourceBits)
        {
            if (sourceBits == 8) return false;
            int found = 0, step = sourceBits / 8;
            int samples = raw.Length / step, chunkSize = 2 * 1024 * 1024;
            Parallel.For(0, (samples + chunkSize - 1) / chunkSize, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (chunk, state) =>
            {
                var span = raw.AsSpan();
                int last = Math.Min(samples, (chunk + 1) * chunkSize);
                for (int i = chunk * chunkSize; i < last; i++)
                {
                    int p = i * step;
                    float value = sourceBits == 16 ? Mathf.HalfToFloat((ushort)(span[p] | span[p + 1] << 8))
                        : BitConverter.Int32BitsToSingle(span[p] | span[p + 1] << 8 | span[p + 2] << 16 | span[p + 3] << 24);
                    if (float.IsNaN(value) || value > 1.0005f || value < -.0005f)
                    { System.Threading.Interlocked.Exchange(ref found, 1); state.Stop(); return; }
                }
            });
            return found != 0;
        }

        internal static void WriteRawTo(Stream stream, int width, int height, NativeArray<byte> source,
            int sourceBits, int targetBits, bool linearToSrgb, bool srgbToLinear)
        {
            if (stream.Position != 0 || !stream.CanSeek) throw new WhimTexDocumentException("TIFF needs an empty seekable stream.");
            if (width < 1 || height < 1 || width > 16384 || height > 16384 ||
                (sourceBits != 8 && sourceBits != 16 && sourceBits != 32) || (targetBits != 8 && targetBits != 32) ||
                (long)width * height * 4 * (sourceBits / 8) > source.Length)
                throw new WhimTexDocumentException("Invalid TIFF source pixels.");
            long rawLength = (long)width * height * 4 * (targetBits / 8);
            if (rawLength > int.MaxValue) throw new WhimTexDocumentException("TIFF composite exceeds the 2 GiB decoded image limit. Reduce canvas size or precision.");
            int count = StripCount((int)rawLength, height, true);
            int rowsPerStrip = (height + count - 1) / count;
            count = (height + rowsPerStrip - 1) / rowsPerStrip;
            int[] sizes = new int[count];
            byte[] header = StripHeader(width, height, targetBits, rowsPerStrip, sizes);
            stream.Write(header, 0, header.Length);
            float[] lookup = linearToSrgb ? SrgbLookup() : null;
            byte[] half = null, halfAlpha = null;
            int[] halfFloatBits = null;
            if (sourceBits == 16 && targetBits == 8)
            {
                half = new byte[65536];
                halfAlpha = new byte[65536];
                for (int i = 0; i < half.Length; i++)
                {
                    float value = Mathf.HalfToFloat((ushort)i);
                    half[i] = ToByte(lookup == null ? value : EncodeSrgb(value, lookup));
                    halfAlpha[i] = ToByte(value);
                }
            }
            else if (sourceBits == 16 && targetBits == 32 && !srgbToLinear)
            {
                halfFloatBits = new int[65536];
                for (int i = 0; i < halfFloatBits.Length; i++)
                    halfFloatBits[i] = BitConverter.SingleToInt32Bits(Mathf.HalfToFloat((ushort)i));
            }
            int workers = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
            for (int first = 0; first < count; first += workers)
            {
                WhimTexDocumentOperation.Report("Encoding TIFF strips", .45f + .3f * first / count);
                int start = first, jobs = Math.Min(workers, count - first);
                var stored = new byte[jobs][];
                WhimTexDocumentOperation.Run(() => Parallel.For(0, jobs, i =>
                {
                    int row = (start + i) * rowsPerStrip;
                    byte[] raw = ConvertRows(width, height, source.AsSpan(), sourceBits, targetBits, row,
                        Math.Min(rowsPerStrip, height - row), lookup, half, halfAlpha, halfFloatBits, srgbToLinear);
                    stored[i] = Deflate(raw, 0, raw.Length);
                }));
                for (int i = 0; i < jobs; i++)
                {
                    sizes[start + i] = stored[i].Length;
                    stream.Write(stored[i], 0, stored[i].Length);
                }
            }
            long end = stream.Position;
            stream.Position = 0;
            header = StripHeader(width, height, targetBits, rowsPerStrip, sizes);
            stream.Write(header, 0, header.Length);
            stream.Position = end;
        }

        private static byte[] ConvertRows(int width, int height, ReadOnlySpan<byte> source, int sourceBits,
            int targetBits, int first, int rows, float[] lookup, byte[] half, byte[] halfAlpha, int[] halfFloatBits, bool srgbToLinear)
        {
            int sourceBytes = sourceBits / 8, targetBytes = targetBits / 8;
            int samples = checked(width * 4), rowBytes = checked(samples * sourceBytes);
            var result = new byte[checked(rows * samples * targetBytes)];
            for (int y = 0; y < rows; y++)
            {
                int offset = (height - 1 - first - y) * rowBytes;
                int target = y * samples * targetBytes;
                if (sourceBits == targetBits && lookup == null && !srgbToLinear)
                { source.Slice(offset, rowBytes).CopyTo(result.AsSpan(target, rowBytes)); continue; }
                for (int i = 0; i < samples; i++)
                {
                    int p = offset + i * sourceBytes;
                    if (half != null)
                    { result[target + i] = ((i & 3) == 3 ? halfAlpha : half)[source[p] | source[p + 1] << 8]; continue; }
                    if (halfFloatBits != null)
                    { PutU32(result, target + i * 4, (uint)halfFloatBits[source[p] | source[p + 1] << 8]); continue; }
                    float value = sourceBits == 8 ? source[p] / 255f : sourceBits == 16
                        ? Mathf.HalfToFloat((ushort)(source[p] | source[p + 1] << 8))
                        : BitConverter.Int32BitsToSingle(source[p] | source[p + 1] << 8 | source[p + 2] << 16 | source[p + 3] << 24);
                    bool alpha = (i & 3) == 3;
                    if (srgbToLinear && !alpha) value = value <= .04045f ? value / 12.92f : (float)Math.Pow((value + .055f) / 1.055f, 2.4);
                    if (targetBits == 32) PutFloat(result, target + i * 4, value);
                    else result[target + i] = ToByte(lookup != null && !alpha ? EncodeSrgb(value, lookup) : value);
                }
            }
            return result;
        }

        private static byte[] StripHeader(int width, int height, int bits, int rows, int[] sizes)
        {
            int count = sizes.Length;
            int bitsAt = HeaderSize + IfdSize, formatAt = bitsAt + 8, offsetsAt = formatAt + 8;
            int countsAt = offsetsAt + count * 4, dataAt = count == 1 ? offsetsAt : countsAt + count * 4;
            var header = new byte[dataAt];
            header[0] = (byte)'I'; header[1] = (byte)'I'; PutU16(header, 2, 42); PutU32(header, 4, HeaderSize);
            PutU16(header, HeaderSize, EntryCount);
            int entry = HeaderSize + 2;
            void Add(ushort tag, ushort type, uint n, uint value)
            {
                PutU16(header, entry, tag); PutU16(header, entry + 2, type);
                PutU32(header, entry + 4, n); PutU32(header, entry + 8, value); entry += 12;
            }
            Add(TagImageWidth, TypeLong, 1, (uint)width); Add(TagImageLength, TypeLong, 1, (uint)height);
            Add(TagBitsPerSample, TypeShort, 4, (uint)bitsAt); Add(TagCompression, TypeShort, 1, CompressionDeflate);
            Add(TagPhotometric, TypeShort, 1, 2); Add(TagStripOffsets, TypeLong, (uint)count, (uint)(count == 1 ? dataAt : offsetsAt));
            Add(TagSamplesPerPixel, TypeShort, 1, 4); Add(TagRowsPerStrip, TypeLong, 1, (uint)rows);
            Add(TagStripByteCounts, TypeLong, (uint)count, count == 1 ? (uint)sizes[0] : (uint)countsAt);
            Add(TagPlanarConfig, TypeShort, 1, 1); Add(TagExtraSamples, TypeShort, 1, 2);
            Add(TagSampleFormat, TypeShort, 4, (uint)formatAt);
            for (int i = 0; i < 4; i++) { PutU16(header, bitsAt + i * 2, bits); PutU16(header, formatAt + i * 2, bits == 32 ? SampleFormatFloat : SampleFormatUnsigned); }
            long position = dataAt;
            for (int i = 0; i < count; i++)
            {
                if (count != 1) { PutU32(header, offsetsAt + i * 4, checked((uint)position)); PutU32(header, countsAt + i * 4, (uint)sizes[i]); }
                position += sizes[i];
            }
            return header;
        }

        internal static void ValidateStream(Stream stream)
        {
            long restore = stream.Position;
            try
            {
                stream.Position = 0;
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
                byte[] header = reader.ReadBytes(HeaderSize + IfdSize + 16);
                if (header.Length != HeaderSize + IfdSize + 16 || header[0] != 'I' || header[1] != 'I' ||
                    ReadU16(header, 2) != 42 || ReadU32(header, 4) != HeaderSize || ReadU16(header, HeaderSize) != EntryCount)
                    throw new WhimTexDocumentException("Invalid WhimTex TIFF header.");
                uint Value(ushort tag)
                {
                    for (int e = HeaderSize + 2; e < HeaderSize + 2 + EntryCount * 12; e += 12)
                        if (ReadU16(header, e) == tag) return ReadU32(header, e + 8);
                    throw new WhimTexDocumentException("Missing TIFF tag.");
                }
                int width = checked((int)Value(TagImageWidth)), height = checked((int)Value(TagImageLength));
                int rows = checked((int)Value(TagRowsPerStrip));
                int bitsAt = checked((int)Value(TagBitsPerSample)), formatAt = checked((int)Value(TagSampleFormat));
                if (width < 1 || height < 1 || width > 16384 || height > 16384 || rows < 1 || rows > height ||
                    bitsAt != HeaderSize + IfdSize || formatAt != bitsAt + 8 || Value(TagSamplesPerPixel) != 4 ||
                    Value(TagCompression) != CompressionDeflate || Value(TagPhotometric) != 2 || Value(TagPlanarConfig) != 1 || Value(TagExtraSamples) != 2)
                    throw new WhimTexDocumentException("Invalid TIFF image layout.");
                int bits = ReadU16(header, bitsAt);
                if (bits != 8 && bits != 32) throw new WhimTexDocumentException("Unsupported TIFF precision.");
                for (int i = 0; i < 4; i++)
                    if (ReadU16(header, bitsAt + i * 2) != bits || ReadU16(header, formatAt + i * 2) != (bits == 32 ? SampleFormatFloat : SampleFormatUnsigned))
                        throw new WhimTexDocumentException("Invalid TIFF channel layout.");
                int count = (height + rows - 1) / rows;
                if ((long)width * height * 4 * (bits / 8) > int.MaxValue || count > 64)
                    throw new WhimTexDocumentException("TIFF image exceeds supported decoded size or strip count.");
                uint offsets = Value(TagStripOffsets), sizes = Value(TagStripByteCounts);
                if (count > 1 && (offsets != header.Length || sizes != header.Length + count * 4L))
                    throw new WhimTexDocumentException("Invalid TIFF strip directory.");
                for (int e = HeaderSize + 2; e < HeaderSize + 2 + EntryCount * 12; e += 12)
                    if ((ReadU16(header, e) == TagStripOffsets || ReadU16(header, e) == TagStripByteCounts) && ReadU32(header, e + 4) != count)
                        throw new WhimTexDocumentException("Invalid TIFF strip count.");
                long previousEnd = count == 1 ? header.Length : header.Length + count * 8L;
                for (int first = 0; first < count;)
                {
                    WhimTexDocumentOperation.Report("Verifying TIFF strips", .75f + .08f * first / count);
                    var batch = new System.Collections.Generic.List<byte[]>(4);
                    var decodedSizes = new System.Collections.Generic.List<int>(4);
                    long batchBytes = 0;
                    for (int i = first; i < count && batch.Count < 4; i++)
                    {
                        stream.Position = offsets + i * 4L;
                        long offset = count == 1 ? offsets : reader.ReadUInt32();
                        stream.Position = sizes + i * 4L;
                        long bytes = count == 1 ? sizes : reader.ReadUInt32();
                        int expected = checked(Math.Min(rows, height - i * rows) * width * 4 * (bits / 8));
                        // Deflate can slightly enlarge incompressible data, never by this generous margin.
                        if (offset != previousEnd || bytes < 6 || bytes > int.MaxValue ||
                            bytes > expected + expected / 100L + 65536 || offset > stream.Length - bytes)
                            throw new WhimTexDocumentException("Invalid TIFF strip bounds.");
                        if (batch.Count > 0 && batchBytes + bytes > 64L * 1024 * 1024) break;
                        stream.Position = offset;
                        byte[] stored = reader.ReadBytes((int)bytes);
                        if (stored.Length != bytes) throw new WhimTexDocumentException("Truncated TIFF strip.");
                        batch.Add(stored); decodedSizes.Add(expected); batchBytes += bytes;
                        previousEnd = offset + bytes;
                    }
                    var valid = new bool[batch.Count];
                    WhimTexDocumentOperation.Run(() => Parallel.For(0, batch.Count, i =>
                        valid[i] = TryInflate(batch[i], 0, batch[i].Length, null, 0, decodedSizes[i], out _)));
                    foreach (bool ok in valid)
                        if (!ok) throw new WhimTexDocumentException("Invalid TIFF strip pixels or checksum.");
                    first += batch.Count;
                }
            }
            finally { stream.Position = restore; }
        }
    }
}
