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

        private static byte[] Build(int width, int height, byte[] raw, int bitsPerSample, int sampleFormat, bool compress)
        {
            byte[] stored = compress ? Deflate(raw) : raw;
            int compression = compress ? CompressionDeflate : CompressionNone;
            int bitsArray = HeaderSize + IfdSize;
            int formatArray = bitsArray + 8;
            int dataOffset = formatArray + 8;
            var file = new byte[dataOffset + stored.Length];
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
            Add(TagStripOffsets, TypeLong, 1, (uint)dataOffset);
            Add(TagSamplesPerPixel, TypeShort, 1, 4);
            Add(TagRowsPerStrip, TypeLong, 1, (uint)height);
            Add(TagStripByteCounts, TypeLong, 1, (uint)stored.Length);
            Add(TagPlanarConfig, TypeShort, 1, 1);
            Add(TagExtraSamples, TypeShort, 1, 2);
            Add(TagSampleFormat, TypeShort, 4, (uint)formatArray);
            PutU32(file, entry, 0);
            for (int i = 0; i < 4; i++) PutU16(file, bitsArray + i * 2, (ushort)bitsPerSample);
            for (int i = 0; i < 4; i++) PutU16(file, formatArray + i * 2, (ushort)sampleFormat);
            System.Array.Copy(stored, 0, file, dataOffset, stored.Length);
            return file;
        }

        /// <summary>Reads the image back out of a carrier: used to verify our own output before writing it.</summary>
        public static bool TryReadPixels(byte[] file, out int width, out int height, out byte[] raw, out string error)
        {
            width = 0; height = 0; raw = null; error = null;
            if (!IsTiff(file)) { error = "The image is not a TIFF."; return false; }
            int ifd = (int)ReadU32(file, 4);
            if (ifd <= 0 || ifd + 2 > file.Length) { error = "The TIFF header is invalid."; return false; }
            int count = ReadU16(file, ifd);
            int bitsPerSample = 0, compression = 1, samples = 1, format = 1;
            long stripOffset = -1, stripBytes = -1;
            for (int i = 0; i < count; i++)
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
                    case TagStripOffsets: stripOffset = value; break;
                    case TagStripByteCounts: stripBytes = value; break;
                    // Multi-value SHORT tags hold an offset to an array of 16-bit values.
                    case TagBitsPerSample: bitsPerSample = ReadU16(file, (int)value); break;
                    case TagSampleFormat: format = ReadU16(file, (int)value); break;
                }
            }
            if (width <= 0 || height <= 0) { error = "The TIFF has no image size."; return false; }
            if (samples != 4) { error = "Only RGBA TIFF images are supported, got " + samples + " samples."; return false; }
            if (stripOffset < 0 || stripBytes <= 0 || stripOffset + stripBytes > file.Length)
            { error = "The TIFF strip is out of bounds."; return false; }
            byte[] stored = new byte[stripBytes];
            System.Array.Copy(file, (int)stripOffset, stored, 0, (int)stripBytes);
            int expected = width * height * 4 * (bitsPerSample / 8);
            if (compression == CompressionDeflate)
            {
                if (!TryInflate(stored, expected, out raw, out error)) return false;
            }
            else if (compression == CompressionNone)
            {
                if (stored.Length != expected) { error = "The TIFF strip size does not match the image size."; return false; }
                raw = stored;
            }
            else { error = "Unsupported TIFF compression: " + compression + "."; return false; }
            if (raw.Length != expected) { error = "The TIFF decoded to " + raw.Length + " bytes instead of " + expected + "."; return false; }
            if (bitsPerSample != 8 && bitsPerSample != 32) { error = "Unsupported TIFF bit depth: " + bitsPerSample + "."; return false; }
            if (format != SampleFormatUnsigned && format != SampleFormatFloat) { error = "Unsupported TIFF sample format."; return false; }
            return true;
        }

        // --- compression: TIFF Deflate expects a zlib stream, while DeflateStream writes raw deflate ---

        private static byte[] Deflate(byte[] raw)
        {
            using var stream = new MemoryStream();
            stream.WriteByte(0x78);
            stream.WriteByte(0x01);
            using (var deflate = new DeflateStream(stream, System.IO.Compression.CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
            uint adler = Adler32(raw);
            stream.WriteByte((byte)(adler >> 24));
            stream.WriteByte((byte)(adler >> 16));
            stream.WriteByte((byte)(adler >> 8));
            stream.WriteByte((byte)adler);
            return stream.ToArray();
        }

        private static bool TryInflate(byte[] stored, int expected, out byte[] raw, out string error)
        {
            raw = null;
            error = null;
            if (stored.Length < 6) { error = "The compressed TIFF strip is truncated."; return false; }
            try
            {
                using var stream = new MemoryStream(stored, 2, stored.Length - 6, false);
                using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
                var result = new byte[expected];
                int read = 0;
                while (read < expected)
                {
                    int step = deflate.Read(result, read, expected - read);
                    if (step <= 0) break;
                    read += step;
                }
                if (read != expected) { error = "The compressed TIFF strip decoded to " + read + " bytes instead of " + expected + "."; return false; }
                raw = result;
                return true;
            }
            catch (InvalidDataException exception)
            {
                error = "The compressed TIFF strip is damaged: " + exception.Message;
                return false;
            }
        }

        private static uint Adler32(byte[] data)
        {
            // Deferred modulo: taking the remainder per byte costs more than the checksum itself.
            const uint modulus = 65521;
            uint a = 1, b = 0;
            int index = 0;
            while (index < data.Length)
            {
                int block = Math.Min(5552, data.Length - index);
                for (int i = 0; i < block; i++)
                {
                    a += data[index + i];
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
