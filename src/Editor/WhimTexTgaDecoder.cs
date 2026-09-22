using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class WhimTexTgaDecoder
    {
        internal static bool TryDecode(byte[] data, bool linear, FilterMode filter, TextureWrapMode wrapU,
            TextureWrapMode wrapV, out Texture2D texture)
        {
            texture = null;
            if (data == null || data.Length < 18) return false;
            try
            {
                int idLength = data[0];
                int colorMapType = data[1];
                int imageType = data[2];
                int colorMapFirst = Read16(data, 3);
                int colorMapLength = Read16(data, 5);
                int colorMapDepth = data[7];
                int width = Read16(data, 12);
                int height = Read16(data, 14);
                int depth = data[16];
                byte descriptor = data[17];
                if (width <= 0 || height <= 0 || width > 32768 || height > 32768) return false;

                bool rle = imageType == 9 || imageType == 10 || imageType == 11;
                bool indexed = imageType == 1 || imageType == 9;
                bool grayscale = imageType == 3 || imageType == 11;
                bool trueColor = imageType == 2 || imageType == 10;
                if ((!indexed && !grayscale && !trueColor) || colorMapType > 1) return false;
                if (indexed && colorMapType != 1) return false;
                if (!indexed && colorMapType != 0) return false;

                int offset = 18 + idLength;
                if (offset < 18 || offset > data.Length) return false;
                Color32[] palette = null;
                if (colorMapType == 1)
                {
                    int bytesPerEntry = (colorMapDepth + 7) / 8;
                    if (colorMapLength <= 0 || (colorMapDepth != 15 && colorMapDepth != 16 &&
                        colorMapDepth != 24 && colorMapDepth != 32)) return false;
                    if (!TryReadPalette(data, ref offset, colorMapFirst, colorMapLength, bytesPerEntry,
                        colorMapDepth, descriptor, out palette)) return false;
                }

                int bytesPerPixel = (depth + 7) / 8;
                if (indexed && depth != 8 && depth != 16) return false;
                if (grayscale && depth != 8 && depth != 16) return false;
                if (trueColor && depth != 16 && depth != 24 && depth != 32) return false;
                Color32[] pixels = new Color32[width * height];
                int written = 0;
                while (written < pixels.Length)
                {
                    int packetCount = 1;
                    bool packetRle = false;
                    if (rle)
                    {
                        if (offset >= data.Length) return false;
                        byte packet = data[offset++];
                        packetRle = (packet & 0x80) != 0;
                        packetCount = (packet & 0x7f) + 1;
                    }
                    if (written + packetCount > pixels.Length) return false;
                    Color32 value = default;
                    for (int i = 0; i < packetCount; i++)
                    {
                        if (packetRle && i > 0) { }
                        else if (!TryReadPixel(data, ref offset, bytesPerPixel, depth, descriptor, indexed,
                            grayscale, palette, colorMapFirst, out value)) return false;
                        int fileIndex = written + i;
                        int row = fileIndex / width;
                        int column = fileIndex - row * width;
                        int destinationRow = (descriptor & 0x20) != 0 ? height - 1 - row : row;
                        int destinationColumn = (descriptor & 0x10) != 0 ? width - 1 - column : column;
                        pixels[destinationRow * width + destinationColumn] = value;
                    }
                    written += packetCount;
                }

                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = filter,
                    wrapModeU = wrapU,
                    wrapModeV = wrapV
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return true;
            }
            catch (Exception)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
                return false;
            }
        }

        private static bool TryReadPalette(byte[] data, ref int offset, int first, int length,
            int bytesPerEntry, int depth, byte descriptor, out Color32[] palette)
        {
            palette = new Color32[length];
            for (int i = 0; i < length; i++)
                if (!TryReadColor(data, ref offset, bytesPerEntry, depth, descriptor, out palette[i]))
                    return false;
            return first >= 0;
        }

        private static bool TryReadPixel(byte[] data, ref int offset, int bytesPerPixel, int depth,
            byte descriptor, bool indexed, bool grayscale, Color32[] palette, int paletteFirst, out Color32 value)
        {
            value = default;
            if (indexed)
            {
                if (offset + bytesPerPixel > data.Length) return false;
                int index = bytesPerPixel == 1 ? data[offset] : Read16(data, offset);
                offset += bytesPerPixel;
                index -= paletteFirst;
                if (palette == null || index < 0 || index >= palette.Length) return false;
                value = palette[index];
                return true;
            }
            return TryReadColor(data, ref offset, bytesPerPixel, depth, descriptor, out value, grayscale);
        }

        private static bool TryReadColor(byte[] data, ref int offset, int bytesPerPixel, int depth,
            byte descriptor, out Color32 value, bool grayscale = false)
        {
            value = default;
            if (offset + bytesPerPixel > data.Length) return false;
            if (grayscale)
            {
                byte gray = data[offset++];
                byte alpha = depth == 16 ? data[offset++] : (byte)255;
                value = new Color32(gray, gray, gray, alpha);
                return true;
            }
            if (depth == 16)
            {
                int packed = Read16(data, offset); offset += 2;
                value = new Color32(ToByte((packed >> 10) & 31), ToByte((packed >> 5) & 31),
                    ToByte(packed & 31), (descriptor & 0x0f) != 0 ? ((packed & 0x8000) != 0 ? (byte)255 : (byte)0) : (byte)255);
                return true;
            }
            byte b = data[offset++];
            byte g = data[offset++];
            byte r = data[offset++];
            byte a = depth == 32 ? data[offset++] : (byte)255;
            value = new Color32(r, g, b, a);
            return true;
        }

        private static byte ToByte(int value) => (byte)((value * 255 + 15) / 31);
        private static int Read16(byte[] data, int offset) => data[offset] | (data[offset + 1] << 8);
    }
}
