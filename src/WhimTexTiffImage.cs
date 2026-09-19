using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// Minimal TIFF writer and validator for document carriers.
    ///
    /// Unity has no TIFF encoder, so the image is written here: baseline TIFF, RGBA, single strip,
    /// Deflate-compressed (zlib), with 8-bit samples for LDR documents and 32-bit IEEE float samples
    /// for HDR ones. Both variants were verified to import through the native TextureImporter.
    ///
    /// The writer validates its own output by reading it back and comparing the pixels: a malformed
    /// carrier is imported by Unity without an error but silently loses sprite sub-assets, so the file
    /// must never reach the documents folder unverified.
    /// </summary>
    public static class WhimTexTiffImage
    {
        private const int EntryCount = 12;
        private const int HeaderSize = 8;
        private const int IfdSize = 2 + EntryCount * 12 + 4;
        private const ushort TagImageWidth = 256;
        private const ushort TagImageLength = 257;
        private const ushort TagBitsPerSample = 258;
        private const ushort TagCompression = 259;
        private const ushort TagPhotometric = 262;
        private const ushort TagStripOffsets = 273;
        private const ushort TagSamplesPerPixel = 277;
        private const ushort TagRowsPerStrip = 278;
        private const ushort TagStripByteCounts = 279;
        private const ushort TagPlanarConfig = 284;
        private const ushort TagExtraSamples = 338;
        private const ushort TagSampleFormat = 339;
        private const ushort TypeShort = 3;
        private const ushort TypeLong = 4;
        private const int CompressionNone = 1;
        private const int CompressionDeflate = 8;
        private const int SampleFormatUnsigned = 1;
        private const int SampleFormatFloat = 3;

        public static bool IsTiff(byte[] file)
        {
            if (file == null || file.Length < 8) return false;
            bool littleEndian = file[0] == (byte)'I' && file[1] == (byte)'I';
            bool bigEndian = file[0] == (byte)'M' && file[1] == (byte)'M';
            if (!littleEndian && !bigEndian) return false;
            int magic = littleEndian ? file[2] | file[3] << 8 : file[2] << 8 | file[3];
            return magic == 42;
        }

        /// <summary>8-bit RGBA carrier for ordinary documents.</summary>
        public static byte[] Write(int width, int height, Color32[] pixels, bool compress = true)
        {
            if (pixels == null || pixels.Length != width * height)
                throw new WhimTexDocumentException("The pixel array does not match the image size.");
            var raw = new byte[width * height * 4];
            // TIFF rows are stored top-down, while Unity pixel arrays start at the bottom-left.
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width;
                int targetRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[sourceRow + x];
                    int target = (targetRow + x) * 4;
                    raw[target] = pixel.r;
                    raw[target + 1] = pixel.g;
                    raw[target + 2] = pixel.b;
                    raw[target + 3] = pixel.a;
                }
            }
            return Build(width, height, raw, 8, SampleFormatUnsigned, compress);
        }

        /// <summary>32-bit float RGBA carrier for HDR documents; values above 1 survive.</summary>
        public static byte[] Write(int width, int height, Color[] pixels, bool compress = true)
        {
            if (pixels == null || pixels.Length != width * height)
                throw new WhimTexDocumentException("The pixel array does not match the image size.");
            using var stream = new MemoryStream(width * height * 16);
            using (var writer = new BinaryWriter(stream))
                for (int y = 0; y < height; y++)
                {
                    int sourceRow = (height - 1 - y) * width;
                    for (int x = 0; x < width; x++)
                    {
                        Color pixel = pixels[sourceRow + x];
                        writer.Write(pixel.r); writer.Write(pixel.g); writer.Write(pixel.b); writer.Write(pixel.a);
                    }
                }
            return Build(width, height, stream.ToArray(), 32, SampleFormatFloat, compress);
        }

        /// <summary>Strips are independent zlib streams: several of them compress on all cores at once.</summary>
        private static int StripCount(int rawLength, int height, bool compress)
        {
            if (!compress || height < 2) return 1;
            // Two megabytes per strip keeps the largest one from deciding when the save is done.
            int wanted = (int)System.Math.Min(64, System.Math.Max(1, rawLength / (2 * 1024 * 1024)));
            return System.Math.Max(1, System.Math.Min(wanted, height));
        }

        private static byte[] Build(int width, int height, byte[] raw, int bitsPerSample, int sampleFormat, bool compress)
        {
            int rowBytes = width * 4 * (bitsPerSample / 8);
            int strips = StripCount(raw.Length, height, compress);
            int rowsPerStrip = (height + strips - 1) / strips;
            strips = (height + rowsPerStrip - 1) / rowsPerStrip;
            var stored = new byte[strips][];
            if (!compress) stored[0] = raw;
            else if (strips == 1) stored[0] = Deflate(raw, 0, raw.Length);
            else
                System.Threading.Tasks.Parallel.For(0, strips, i =>
                {
                    int first = i * rowsPerStrip;
                    int rows = System.Math.Min(rowsPerStrip, height - first);
                    stored[i] = Deflate(raw, first * rowBytes, rows * rowBytes);
                });
            int compression = compress ? CompressionDeflate : CompressionNone;
            // A strip list of one entry is stored inside the directory entry itself, a longer one points at
            // an array, which is what the format asks for.
            bool inlineStrips = strips == 1;
            int bitsArray = HeaderSize + IfdSize;
            int formatArray = bitsArray + 8;
            int offsetsArray = formatArray + 8;
            int countsArray = offsetsArray + 4 * strips;
            int dataOffset = inlineStrips ? offsetsArray : countsArray + 4 * strips;
            long total = dataOffset;
            foreach (byte[] part in stored) total += part.Length;
            if (total > int.MaxValue) throw new WhimTexDocumentException("The image is too large to write.");
            var file = new byte[total];
            file[0] = (byte)'I'; file[1] = (byte)'I';
            PutU16(file, 2, 42);
            PutU32(file, 4, HeaderSize);
            PutU16(file, HeaderSize, EntryCount);
            int entry = HeaderSize + 2;
            void Add(ushort tag, ushort type, uint count, uint value)
            {
                PutU16(file, entry, tag);
                PutU16(file, entry + 2, type);
                PutU32(file, entry + 4, count);
                PutU32(file, entry + 8, value);
                entry += 12;
            }
            Add(TagImageWidth, TypeLong, 1, (uint)width);
            Add(TagImageLength, TypeLong, 1, (uint)height);
            Add(TagBitsPerSample, TypeShort, 4, (uint)bitsArray);
            Add(TagCompression, TypeShort, 1, (uint)compression);
            Add(TagPhotometric, TypeShort, 1, 2);
            Add(TagStripOffsets, TypeLong, (uint)strips, inlineStrips ? (uint)dataOffset : (uint)offsetsArray);
            Add(TagSamplesPerPixel, TypeShort, 1, 4);
            Add(TagRowsPerStrip, TypeLong, 1, (uint)rowsPerStrip);
            Add(TagStripByteCounts, TypeLong, (uint)strips, inlineStrips ? (uint)stored[0].Length : (uint)countsArray);
            Add(TagPlanarConfig, TypeShort, 1, 1);
            Add(TagExtraSamples, TypeShort, 1, 2);
            Add(TagSampleFormat, TypeShort, 4, (uint)formatArray);
            PutU32(file, entry, 0);
            for (int i = 0; i < 4; i++) PutU16(file, bitsArray + i * 2, (ushort)bitsPerSample);
            for (int i = 0; i < 4; i++) PutU16(file, formatArray + i * 2, (ushort)sampleFormat);
            int offset = dataOffset;
            for (int i = 0; i < strips; i++)
            {
                if (!inlineStrips)
                {
                    PutU32(file, offsetsArray + i * 4, (uint)offset);
                    PutU32(file, countsArray + i * 4, (uint)stored[i].Length);
                }
                System.Array.Copy(stored[i], 0, file, offset, stored[i].Length);
                offset += stored[i].Length;
            }
            return file;
        }

        /// <summary>
        /// Writes the carrier from the texture's own raw bytes, without a managed Color array or a
        /// per-pixel conversion: at 16 bytes per pixel those copies cost more than the compression.
        /// Half-float sources are widened to float when the target keeps 32-bit samples, because Unity
        /// reads a 16-bit float TIFF as integer data and loses the values above 1.
        /// </summary>
        public static byte[] WriteRaw(int width, int height, byte[] raw, int sourceBits, int targetBits = 0,
            bool linearToSrgb = false, bool compress = true)
        {
            if (raw == null) throw new WhimTexDocumentException("The texture has no raw data.");
            if (sourceBits != 8 && sourceBits != 16 && sourceBits != 32)
                throw new WhimTexDocumentException("Unsupported source sample size: " + sourceBits + " bits.");
            if (targetBits == 0) targetBits = sourceBits == 16 ? 32 : sourceBits;
            if (targetBits != 8 && targetBits != 32)
                throw new WhimTexDocumentException("Unsupported target sample size: " + targetBits + " bits.");
            if (targetBits == 32 && sourceBits == 8)
                throw new WhimTexDocumentException("An 8-bit source cannot be written as 32-bit samples.");
            int sourceBytes = sourceBits / 8;
            int targetBytes = targetBits / 8;
            int rowBytes = width * 4 * sourceBytes;
            if (raw.Length < (long)rowBytes * height)
                throw new WhimTexDocumentException("The raw texture data is smaller than the image size.");
            long total = (long)width * height * 4 * targetBytes;
            if (total > int.MaxValue) throw new WhimTexDocumentException("The image is too large to write.");
            var data = new byte[total];
            float[] lookup = linearToSrgb ? SrgbLookup() : null;
            // Half-float samples that end up as 8-bit go through a table: a 4096x4096 composite holds 67
            // million samples, and converting them one at a time costs more than compressing the result.
            byte[] halfToByte = null;
            if (sourceBits == 16 && targetBits == 8)
            {
                halfToByte = new byte[65536];
                for (int i = 0; i < 65536; i++)
                {
                    float sample = Mathf.HalfToFloat((ushort)i);
                    halfToByte[i] = ToByte(lookup == null ? sample : EncodeSrgb(sample, lookup));
                }
            }
            bool copyRows = sourceBits == targetBits && !linearToSrgb;
            void Row(int y)
            {
                int target = y * width * 4 * targetBytes;
                int source = (height - 1 - y) * rowBytes; // TIFF rows are top-down, Unity's start at the bottom
                if (copyRows)
                {
                    Buffer.BlockCopy(raw, source, data, target, rowBytes);
                    return;
                }
                if (halfToByte != null)
                {
                    for (int i = 0; i < width * 4; i++)
                    {
                        ushort sample = (ushort)(raw[source + i * 2] | raw[source + i * 2 + 1] << 8);
                        data[target + i] = (i & 3) == 3 ? ToByte(Mathf.HalfToFloat(sample)) : halfToByte[sample];
                    }
                    return;
                }
                for (int i = 0; i < width * 4; i++)
                {
                    float value;
                    if (sourceBits == 16)
                    {
                        ushort half = (ushort)(raw[source + i * 2] | raw[source + i * 2 + 1] << 8);
                        value = Mathf.HalfToFloat(half);
                    }
                    else if (sourceBits == 32)
                        value = BitConverter.Int32BitsToSingle(raw[source + i * 4] | raw[source + i * 4 + 1] << 8 |
                            raw[source + i * 4 + 2] << 16 | raw[source + i * 4 + 3] << 24);
                    else
                        value = raw[source + i] / 255f;
                    if (targetBytes == 4) PutFloat(data, target + i * 4, value);
                    else data[target + i] = ToByte(lookup == null || (i & 3) == 3 ? value : EncodeSrgb(value, lookup));
                }
            }
            // Rows are independent, so a large image is converted on all cores; only managed math runs here.
            if (height >= 64) System.Threading.Tasks.Parallel.For(0, height, Row);
            else for (int y = 0; y < height; y++) Row(y);
            return Build(width, height, data, targetBits,
                targetBits == 32 ? SampleFormatFloat : SampleFormatUnsigned, compress);
        }

        /// <summary>
        /// True when a channel falls outside 0..1. Such values cannot be stored in 8-bit samples at all,
        /// which is what decides the carrier format: a document that only holds ordinary colours must not
        /// pay for float samples and BC6H compression on every save and import.
        /// </summary>
        public static bool HasValuesOutsideUnitRange(byte[] raw, int sourceBits)
        {
            const float upper = 1.0005f;
            const float lower = -0.0005f;
            if (sourceBits == 8) return false;
            int bytesPerSample = sourceBits / 8;
            int samples = raw.Length / bytesPerSample;
            var found = new bool[1];
            int perChunk = System.Math.Max(1, 8 * 1024 * 1024 / bytesPerSample);
            int chunks = (samples + perChunk - 1) / perChunk;
            // A composite holds hundreds of megabytes, so the scan runs on all cores and stops early.
            System.Threading.Tasks.Parallel.For(0, chunks, (chunk, state) =>
            {
                int last = System.Math.Min(samples, (chunk + 1) * perChunk);
                for (int i = chunk * perChunk; i < last; i++)
                {
                    float value;
                    if (sourceBits == 16)
                    {
                        ushort half = (ushort)(raw[i * 2] | raw[i * 2 + 1] << 8);
                        value = Mathf.HalfToFloat(half);
                    }
                    else
                        value = BitConverter.Int32BitsToSingle(raw[i * 4] | raw[i * 4 + 1] << 8 |
                            raw[i * 4 + 2] << 16 | raw[i * 4 + 3] << 24);
                    if (float.IsNaN(value) || value > upper || value < lower)
                    {
                        found[0] = true;
                        state.Stop();
                        return;
                    }
                }
            });
            return found[0];
        }

        private static float[] SrgbLookup()
        {
            var lookup = new float[1025];
            for (int i = 0; i <= 1024; i++)
            {
                float value = i / 1024f;
                lookup[i] = value <= 0.0031308f ? value * 12.92f : 1.055f * Mathf.Pow(value, 1f / 2.4f) - 0.055f;
            }
            return lookup;
        }

        /// <summary>Linear to sRGB through a table: half-float values dropped to 8-bit linear band in shadows.</summary>
        private static float EncodeSrgb(float value, float[] lookup)
        {
            // A composite can hold NaN, and every comparison against it is false, so it must be rejected
            // before the value is scaled into a table index.
            if (float.IsNaN(value) || value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            float scaled = value * 1024f;
            int index = (int)scaled;
            float low = lookup[index];
            return index >= 1024 ? low : low + (lookup[index + 1] - low) * (scaled - index);
        }

        private static byte ToByte(float value)
        {
            if (float.IsNaN(value) || value < 0f) return 0;
            return (byte)((value > 1f ? 1f : value) * 255f + 0.5f);
        }

        private static void PutFloat(byte[] buffer, int offset, float value)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            buffer[offset] = (byte)bits;
            buffer[offset + 1] = (byte)(bits >> 8);
            buffer[offset + 2] = (byte)(bits >> 16);
            buffer[offset + 3] = (byte)(bits >> 24);
        }

        /// <summary>Reads the image back out of a carrier: used to verify our own output before writing it.</summary>
        public static bool TryReadPixels(byte[] file, out int width, out int height, out byte[] raw, out string error)
            => ReadPixelsCore(file, true, out width, out height, out raw, out error);

        internal static bool Validate(byte[] file, out int width, out int height, out string error)
            => ReadPixelsCore(file, false, out width, out height, out _, out error);

        private static bool ReadPixelsCore(byte[] file, bool retainPixels, out int width, out int height, out byte[] raw, out string error)
        {
            width = 0; height = 0; raw = null; error = null;
            if (!IsTiff(file)) { error = "The image is not a TIFF."; return false; }
            int ifd = (int)ReadU32(file, 4);
            if (ifd <= 0 || ifd + 2 > file.Length) { error = "The TIFF header is invalid."; return false; }
            int entryCount = ReadU16(file, ifd);
            int bitsPerSample = 0, compression = 1, samples = 1, format = 1;
            int rowsPerStrip = 0, stripCount = 1;
            long offsetsValue = -1, countsValue = -1;
            for (int i = 0; i < entryCount; i++)
            {
                int entry = ifd + 2 + i * 12;
                if (entry + 12 > file.Length) { error = "The TIFF directory is truncated."; return false; }
                int tag = ReadU16(file, entry);
                long value = ReadU32(file, entry + 8);
                switch (tag)
                {
                    case TagImageWidth: width = (int)value; break;
                    case TagImageLength: height = (int)value; break;
                    case TagCompression: compression = (int)value; break;
                    case TagSamplesPerPixel: samples = (int)value; break;
                    // A long tag with several values points at an array, a single one sits in the entry.
                    case TagStripOffsets: offsetsValue = value; stripCount = (int)ReadU32(file, entry + 4); break;
                    case TagStripByteCounts: countsValue = value; break;
                    case TagRowsPerStrip: rowsPerStrip = (int)value; break;
                    // Multi-value SHORT tags hold an offset to an array of 16-bit values.
                    case TagBitsPerSample: bitsPerSample = ReadU16(file, (int)value); break;
                    case TagSampleFormat: format = ReadU16(file, (int)value); break;
                }
            }
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384) { error = "Invalid TIFF image size."; return false; }
            if (samples != 4) { error = "Only RGBA TIFF images are supported, got " + samples + " samples."; return false; }
            if (bitsPerSample != 8 && bitsPerSample != 32) { error = "Unsupported TIFF bit depth: " + bitsPerSample + "."; return false; }
            if (format != SampleFormatUnsigned && format != SampleFormatFloat) { error = "Unsupported TIFF sample format."; return false; }
            if (compression != CompressionDeflate && compression != CompressionNone)
            { error = "Unsupported TIFF compression: " + compression + "."; return false; }
            if (offsetsValue < 0 || countsValue < 0 || stripCount <= 0 || stripCount > height) { error = "The TIFF has no pixel data."; return false; }
            if (rowsPerStrip <= 0) rowsPerStrip = height;
            int bytesPerPixel = 4 * (bitsPerSample / 8);
            long expectedSize = (long)width * height * bytesPerPixel;
            if (expectedSize > int.MaxValue) { error = "The TIFF image is too large."; return false; }
            int expected = (int)expectedSize;
            var decoded = retainPixels ? new byte[expected] : null;
            var stripSource = new int[stripCount];
            var stripStored = new int[stripCount];
            var stripDecoded = new int[stripCount];
            var stripTarget = new int[stripCount];
            int written = 0;
            for (int i = 0; i < stripCount; i++)
            {
                int offset = (int)(stripCount == 1 ? offsetsValue : ReadU32(file, (int)offsetsValue + i * 4));
                int bytes = (int)(stripCount == 1 ? countsValue : ReadU32(file, (int)countsValue + i * 4));
                if (offset < 0 || bytes <= 0 || (long)offset + bytes > file.Length)
                { error = "The TIFF strip is out of bounds."; return false; }
                int rows = System.Math.Min(rowsPerStrip, height - i * rowsPerStrip);
                if (rows <= 0) { error = "The TIFF declares more strips than it has rows."; return false; }
                int decodedBytes = rows * width * bytesPerPixel;
                if (written + decodedBytes > expected) { error = "The TIFF strips are larger than the image."; return false; }
                stripSource[i] = offset;
                stripStored[i] = bytes;
                stripDecoded[i] = decodedBytes;
                stripTarget[i] = written;
                written += decodedBytes;
            }
            if (written != expected) { error = "The TIFF decoded to " + written + " bytes instead of " + expected + "."; return false; }
            // Strips are separate streams, so a large image is verified on all cores; each one is inflated
            // straight into its place in the image, without a second copy.
            var problems = new string[stripCount];
            System.Threading.Tasks.Parallel.For(0, stripCount, i =>
            {
                if (compression == CompressionDeflate)
                {
                    if (!TryInflate(file, stripSource[i], stripStored[i], decoded, stripTarget[i], stripDecoded[i], out string problem)) problems[i] = problem;
                }
                else if (stripStored[i] != stripDecoded[i]) problems[i] = "The TIFF strip size does not match the image size.";
                else if (decoded != null) System.Array.Copy(file, stripSource[i], decoded, stripTarget[i], stripStored[i]);
            });
            foreach (string problem in problems) if (problem != null) { error = problem; return false; }
            raw = decoded;
            return true;
        }

        // --- compression: TIFF Deflate expects a zlib stream, while DeflateStream writes raw deflate ---

        private static byte[] Deflate(byte[] raw, int offset, int count)
        {
            using var stream = new MemoryStream();
            stream.WriteByte(0x78);
            stream.WriteByte(0x01);
            using (var deflate = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Fastest, true)) deflate.Write(raw, offset, count);
            uint adler = Adler32(raw, offset, count);
            stream.WriteByte((byte)(adler >> 24));
            stream.WriteByte((byte)(adler >> 16));
            stream.WriteByte((byte)(adler >> 8));
            stream.WriteByte((byte)adler);
            return stream.ToArray();
        }

        private static bool TryInflate(byte[] stored, int offset, int count, byte[] target, int targetOffset, int expected, out string error)
        {
            error = null;
            if (count < 6) { error = "The compressed TIFF strip is truncated."; return false; }
            try
            {
                using var stream = new MemoryStream(stored, offset + 2, count - 6, false);
                using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
                byte[] scratch = target == null ? new byte[Math.Min(expected, 65536)] : null;
                int read = 0;
                uint a = 1, b = 0;
                while (read < expected)
                {
                    byte[] buffer = target ?? scratch;
                    int destination = target == null ? 0 : targetOffset + read;
                    int step = deflate.Read(buffer, destination, Math.Min(65536, expected - read));
                    if (step <= 0) break;
                    for (int i = 0; i < step; i++) { a = (a + buffer[destination + i]) % 65521; b = (b + a) % 65521; }
                    read += step;
                }
                if (read != expected) { error = "The compressed TIFF strip decoded to " + read + " bytes instead of " + expected + "."; return false; }
                int checksumOffset = offset + count - 4;
                uint checksum = (uint)stored[checksumOffset] << 24 | (uint)stored[checksumOffset + 1] << 16 |
                    (uint)stored[checksumOffset + 2] << 8 | stored[checksumOffset + 3];
                if (deflate.ReadByte() != -1 || checksum != (b << 16 | a)) { error = "Invalid TIFF strip checksum or length."; return false; }
                return true;
            }
            catch (InvalidDataException exception)
            {
                error = "The compressed TIFF strip is damaged: " + exception.Message;
                return false;
            }
        }

        private static uint Adler32(byte[] data, int offset, int count)
        {
            // Deferred modulo: taking the remainder per byte costs more than the checksum itself.
            const uint modulus = 65521;
            uint a = 1, b = 0;
            int index = 0;
            while (index < count)
            {
                int block = Math.Min(5552, count - index);
                for (int i = 0; i < block; i++)
                {
                    a += data[offset + index + i];
                    b += a;
                }
                a %= modulus;
                b %= modulus;
                index += block;
            }
            return b << 16 | a;
        }

        private static void PutU16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void PutU32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadU16(byte[] buffer, int offset) => buffer[offset] | buffer[offset + 1] << 8;

        private static uint ReadU32(byte[] buffer, int offset) =>
            (uint)(buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24);
    }
}
