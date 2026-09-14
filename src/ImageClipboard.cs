using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ImageClipboard
    {
        private const int MaximumBytes = CanvasSelection.MaximumPixels * 4 + 1048576;

        internal static uint Revision
        {
            get
            {
#if UNITY_EDITOR_WIN
                return GetClipboardSequenceNumber();
#else
                return 0;
#endif
            }
        }

        internal static Texture2D ReadImage()
        {
#if UNITY_EDITOR_WIN
            if (!OpenClipboard(IntPtr.Zero))
                throw new InvalidOperationException("Clipboard is busy. Try pasting again.");
            byte[] bytes;
            bool png;
            try
            {
                uint pngFormat = RegisterClipboardFormat("PNG");
                png = pngFormat != 0 && IsClipboardFormatAvailable(pngFormat);
                uint format = png ? pngFormat : IsClipboardFormatAvailable(17) ? 17u : 8u; // CF_DIBV5 / CF_DIB
                if (!IsClipboardFormatAvailable(format)) return null;
                IntPtr handle = GetClipboardData(format);
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Cannot read the clipboard image.");
                ulong size = GlobalSize(handle).ToUInt64();
                if (size == 0 || size > MaximumBytes) throw new InvalidOperationException("Clipboard image data is too large or empty.");
                IntPtr data = GlobalLock(handle);
                if (data == IntPtr.Zero) throw new InvalidOperationException("Cannot access the clipboard image.");
                try { bytes = new byte[(int)size]; Marshal.Copy(data, bytes, 0, bytes.Length); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
            // Release the system clipboard before decoding or allocating GPU resources.
            return png ? DecodePng(bytes) : DecodeDib(bytes);
#else
            throw new NotSupportedException("System image paste is currently supported on Windows. You can drag an image file into WhimTex instead.");
#endif
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width < 1 || height < 1 || width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize ||
                (long)width * height > CanvasSelection.MaximumPixels)
                throw new InvalidOperationException("Clipboard image exceeds the supported texture size (16 megapixels maximum).");
        }

        internal static Texture2D DecodeWebImage(byte[] bytes)
        {
            if (bytes != null && bytes.Length >= 8 && BitConverter.ToUInt64(bytes, 0) == 0x0A1A0A0D474E5089UL)
                return DecodePng(bytes);
            // Read JPEG dimensions before handing potentially huge images to the native decoder.
            if (bytes != null && bytes.Length > 4 && bytes[0] == 255 && bytes[1] == 216)
            {
                int p = 2;
                while (p + 3 < bytes.Length)
                {
                    if (bytes[p++] != 255) break;
                    while (p < bytes.Length && bytes[p] == 255) p++;
                    if (p >= bytes.Length) break;
                    int marker = bytes[p++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || (marker >= 208 && marker <= 215)) continue;
                    if (p + 1 >= bytes.Length) break;
                    int length = (bytes[p] << 8) | bytes[p + 1];
                    if (length < 2 || length > bytes.Length - p) break;
                    if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                    {
                        if (length < 8) break;
                        ValidateDimensions((bytes[p + 5] << 8) | bytes[p + 6], (bytes[p + 3] << 8) | bytes[p + 4]);
                        var texture = CreateTexture(2, 2);
                        try
                        {
                            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidOperationException("Cannot decode JPEG image.");
                            ValidateDimensions(texture.width, texture.height);
                            return texture;
                        }
                        catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
                    }
                    p += length;
                }
            }
            throw new InvalidOperationException("The link must return a PNG or JPEG image, not a web page.");
        }

        internal static Texture2D DecodePng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 33 || BitConverter.ToUInt64(bytes, 0) != 0x0A1A0A0D474E5089UL ||
                BitConverter.ToUInt32(bytes, 12) != 0x52444849u)
                throw new InvalidOperationException("The clipboard PNG is invalid.");
            int ReadBigEndian(int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
            ValidateDimensions(ReadBigEndian(16), ReadBigEndian(20));
            Texture2D texture = CreateTexture(2, 2);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidOperationException("Cannot decode the clipboard PNG.");
                return texture;
            }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }

        // Packed DIB layout: https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-bitmapv5header
        internal static Texture2D DecodeDib(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 40) throw new InvalidOperationException("The clipboard bitmap is incomplete.");
            uint header = BitConverter.ToUInt32(bytes, 0);
            if ((header != 40 && header != 52 && header != 56 && header != 108 && header != 124) || header > bytes.Length)
                throw new InvalidOperationException("Unsupported clipboard bitmap header.");
            int width = BitConverter.ToInt32(bytes, 4), signedHeight = BitConverter.ToInt32(bytes, 8);
            int height = signedHeight == int.MinValue ? 0 : Math.Abs(signedHeight);
            ValidateDimensions(width, height);
            int bits = BitConverter.ToUInt16(bytes, 14);
            uint compression = BitConverter.ToUInt32(bytes, 16);
            if (BitConverter.ToUInt16(bytes, 12) != 1 || (bits != 16 && bits != 24 && bits != 32) ||
                (compression != 0 && compression != 3) || (compression == 3 && bits == 24))
                throw new InvalidOperationException("Unsupported clipboard bitmap encoding. Copy it as PNG or a 24/32-bit image.");

            int offset = (int)header;
            uint red = bits == 16 ? 0x7C00u : 0xFF0000u, green = bits == 16 ? 0x03E0u : 0xFF00u;
            uint blue = bits == 16 ? 0x001Fu : 0xFFu, alpha = 0;
            if (compression == 3)
            {
                if (header == 40) offset += 12;
                if (bytes.Length < offset) throw new InvalidOperationException("The clipboard color masks are incomplete.");
                red = BitConverter.ToUInt32(bytes, 40); green = BitConverter.ToUInt32(bytes, 44); blue = BitConverter.ToUInt32(bytes, 48);
                if (header >= 56) alpha = BitConverter.ToUInt32(bytes, 52);
                uint available = bits == 16 ? 0xFFFFu : uint.MaxValue;
                if (!ValidMask(red) || !ValidMask(green) || !ValidMask(blue) || (alpha != 0 && !ValidMask(alpha)) ||
                    ((red | green | blue | alpha) & ~available) != 0 || (red & green) != 0 || (red & blue) != 0 ||
                    (green & blue) != 0 || ((red | green | blue) & alpha) != 0)
                    throw new InvalidOperationException("Invalid clipboard color masks.");
            }
            long start = offset + (long)BitConverter.ToUInt32(bytes, 32) * 4;
            if (header == 124)
            {
                uint profile = BitConverter.ToUInt32(bytes, 112), profileSize = BitConverter.ToUInt32(bytes, 116);
                if (profileSize > 0 && profile == start) start += profileSize;
            }
            long stride = ((long)width * bits + 31) / 32 * 4;
            if (start + stride * height > bytes.Length) throw new InvalidOperationException("The clipboard bitmap pixels are incomplete.");
            int redShift = MaskShift(red), greenShift = MaskShift(green), blueShift = MaskShift(blue), alphaShift = MaskShift(alpha);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = (int)(start + stride * (signedHeight < 0 ? height - 1 - y : y));
                for (int x = 0; x < width; x++)
                {
                    int p = row + x * (bits / 8);
                    uint value = bits == 16 ? BitConverter.ToUInt16(bytes, p) :
                        bits == 32 ? BitConverter.ToUInt32(bytes, p) : (uint)(bytes[p] | bytes[p + 1] << 8 | bytes[p + 2] << 16);
                    // BI_RGB's fourth byte is reserved, not alpha (screenshots often leave it zero).
                    pixels[y * width + x] = new Color32(Channel(value, red, redShift), Channel(value, green, greenShift), Channel(value, blue, blueShift),
                        alpha == 0 ? (byte)255 : Channel(value, alpha, alphaShift));
                }
            }
            Texture2D texture = CreateTexture(width, height);
            try { texture.SetPixels32(pixels); texture.Apply(false, false); return texture; }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }

        private static bool ValidMask(uint mask)
        {
            if (mask == 0) return false;
            while ((mask & 1) == 0) mask >>= 1;
            return (mask & (mask + 1)) == 0;
        }

        private static int MaskShift(uint mask)
        {
            int shift = 0;
            if (mask != 0) while ((mask & 1) == 0) { mask >>= 1; shift++; }
            return shift;
        }

        private static byte Channel(uint value, uint mask, int shift) =>
            (byte)(((ulong)((value & mask) >> shift) * 255 + (mask >> shift) / 2) / (mask >> shift));

        private static Texture2D CreateTexture(int width, int height) => new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        { name = "Clipboard Image", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

#if UNITY_EDITOR_WIN
        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
#endif
    }
}
