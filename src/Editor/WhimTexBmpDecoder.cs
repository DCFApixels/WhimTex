using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>Small cross-platform decoder for common Windows BMP variants.</summary>
    internal static class WhimTexBmpDecoder
    {
        private const uint BI_RGB = 0;
        private const uint BI_BITFIELDS = 3;

        internal static bool TryDecode(byte[] data, bool linear, FilterMode filter,
            TextureWrapMode wrapU, TextureWrapMode wrapV, out Texture2D texture)
        {
            texture = null;
            try
            {
                if (data == null || data.Length < 54 || data[0] != 'B' || data[1] != 'M') return false;
                int pixelOffset = ReadInt32(data, 10);
                int dibSize = ReadInt32(data, 14);
                if (dibSize < 40 || pixelOffset < 14 + dibSize || pixelOffset >= data.Length) return false;

                int width = ReadInt32(data, 18);
                int signedHeight = ReadInt32(data, 22);
                ushort planes = ReadUInt16(data, 26);
                ushort depth = ReadUInt16(data, 28);
                uint compression = ReadUInt32(data, 30);
                uint colorsUsed = ReadUInt32(data, 46);
                if (width <= 0 || signedHeight == 0 || width > 32768 ||
                    signedHeight == int.MinValue || Math.Abs((long)signedHeight) > 32768 ||
                    planes != 1 || (depth != 1 && depth != 4 && depth != 8 && depth != 16 && depth != 24 && depth != 32) ||
                    (compression != BI_RGB && compression != BI_BITFIELDS)) return false;

                bool topDown = signedHeight < 0;
                int height = Math.Abs(signedHeight);
                // BITMAPV2+ stores masks inside the DIB at the standard offset;
                // the original 40-byte header stores them immediately after it.
                int maskOffset = dibSize >= 52 ? 14 + 40 : 14 + dibSize;
                uint redMask = 0, greenMask = 0, blueMask = 0, alphaMask = 0;
                if (depth == 16 || depth == 32)
                {
                    if (compression == BI_BITFIELDS)
                    {
                        if (maskOffset + 12 > data.Length) return false;
                        redMask = ReadUInt32(data, maskOffset);
                        greenMask = ReadUInt32(data, maskOffset + 4);
                        blueMask = ReadUInt32(data, maskOffset + 8);
                        if (dibSize >= 56 && maskOffset + 16 <= data.Length)
                            alphaMask = ReadUInt32(data, maskOffset + 12);
                        else if (maskOffset + 16 <= pixelOffset && ReadUInt32(data, maskOffset + 12) != 0)
                            alphaMask = ReadUInt32(data, maskOffset + 12);
                    }
                    else if (depth == 16)
                    {
                        redMask = 0x7C00; greenMask = 0x03E0; blueMask = 0x001F;
                    }
                    else
                    {
                        redMask = 0x00FF0000; greenMask = 0x0000FF00; blueMask = 0x000000FF;
                    }
                    if (redMask == 0 || greenMask == 0 || blueMask == 0) return false;
                }

                Color32[] palette = null;
                int paletteCount = depth <= 8 ? (int)(colorsUsed != 0 ? colorsUsed : (1u << depth)) : 0;
                int paletteOffset = 14 + dibSize;
                if (paletteCount > 0)
                {
                    if (paletteCount > 256 || paletteOffset + paletteCount * 4 > data.Length) return false;
                    palette = new Color32[paletteCount];
                    for (int i = 0; i < paletteCount; i++)
                    {
                        int p = paletteOffset + i * 4;
                        palette[i] = new Color32(data[p + 2], data[p + 1], data[p], data[p + 3]);
                    }
                }

                long strideLong = ((long)depth * width + 31L) / 32L * 4L;
                long pixelBytes = strideLong * height;
                if (strideLong > int.MaxValue || pixelOffset + pixelBytes > data.Length) return false;
                Color32[] pixels = new Color32[width * height];
                bool alphaUsed = false;
                int stride = (int)strideLong;
                for (int row = 0; row < height; row++)
                {
                    int source = pixelOffset + row * stride;
                    int destinationY = topDown ? height - 1 - row : row;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 color;
                        if (depth <= 8)
                        {
                            int index = depth == 8 ? data[source + x] : ReadPackedIndex(data, source, x, depth);
                            if (palette == null || index < 0 || index >= palette.Length) return false;
                            color = palette[index];
                            alphaUsed |= color.a != 0;
                        }
                        else if (depth == 24)
                        {
                            int p = source + x * 3;
                            color = new Color32(data[p + 2], data[p + 1], data[p], 255);
                        }
                        else
                        {
                            int bytes = depth / 8;
                            uint packed = bytes == 2 ? ReadUInt16(data, source + x * 2) : ReadUInt32(data, source + x * 4);
                            byte alpha = alphaMask == 0 ? (byte)255 : ReadMasked(packed, alphaMask);
                            color = new Color32(ReadMasked(packed, redMask), ReadMasked(packed, greenMask),
                                ReadMasked(packed, blueMask), alpha);
                            alphaUsed |= alpha != 0;
                        }
                        pixels[destinationY * width + x] = color;
                    }
                }

                // Most 32-bit BMP writers store an unused zero alpha channel (BGRX).
                if ((depth == 32 || depth <= 8) && !alphaUsed)
                    for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255;

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

        private static int ReadPackedIndex(byte[] data, int row, int x, int depth)
        {
            int perByte = 8 / depth;
            int value = data[row + x / perByte];
            int shift = (perByte - 1 - x % perByte) * depth;
            return (value >> shift) & ((1 << depth) - 1);
        }

        private static byte ReadMasked(uint value, uint mask)
        {
            int shift = 0;
            while ((mask & 1u) == 0) { mask >>= 1; shift++; }
            uint component = (value >> shift) & mask;
            return (byte)((component * 255u + mask / 2u) / mask);
        }

        private static ushort ReadUInt16(byte[] data, int offset) => (ushort)(data[offset] | (data[offset + 1] << 8));
        private static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        private static int ReadInt32(byte[] data, int offset) => unchecked((int)ReadUInt32(data, offset));
    }
}
