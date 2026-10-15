using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class ImageClipboard
    {
        private const int MaximumBytes = CanvasSelection.MaximumPixels * 4 + 1048576;
        private const uint SelectionTokenMagic = 0x534D4857u;
        private const uint SelectionTokenVersion = 1;
        private const int SelectionTokenBytes = 24;

        internal static bool SupportsImageWrite
        {
            get
            {
#if UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

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

        // Unsupported platforms retain the caller's internal image; Windows failures are reported.
        internal static bool TryPublish(Texture2D source, Guid token, out uint revision)
        {
            revision = 0;
#if UNITY_EDITOR_WIN
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (token == Guid.Empty) throw new ArgumentException("Clipboard image token cannot be empty.", nameof(token));
            ValidateDimensions(source.width, source.height);

            Texture2D ldr = HdrUtility.ToLdr(source);
            try { return PublishLdr(ldr, token, out revision); }
            finally { UnityEngine.Object.DestroyImmediate(ldr); }
#else
            return false;
#endif
        }

        internal static bool TryPublishPixels(Color[] linearPixels, int width, int height, Guid token, out uint revision)
        {
            revision = 0;
#if UNITY_EDITOR_WIN
            if (linearPixels == null) throw new ArgumentNullException(nameof(linearPixels));
            if (token == Guid.Empty) throw new ArgumentException("Clipboard image token cannot be empty.", nameof(token));
            ValidateDimensions(width, height);
            if (linearPixels.Length != (long)width * height)
                throw new ArgumentException("Clipboard image pixels do not match its dimensions.", nameof(linearPixels));
            Texture2D ldr = CreateTexture(width, height);
            try
            {
                var encoded = ldr.GetRawTextureData<Color32>();
                for (int i = 0; i < linearPixels.Length; i++)
                    encoded[i] = HdrUtility.Encode(HdrUtility.Saturate(HdrUtility.Safe(linearPixels[i])));
                ldr.Apply(false, false);
                return PublishLdr(ldr, token, out revision);
            }
            finally { UnityEngine.Object.DestroyImmediate(ldr); }
#else
            return false;
#endif
        }

#if UNITY_EDITOR_WIN
        private static bool PublishLdr(Texture2D ldr, Guid token, out uint revision)
        {
            revision = 0;
            byte[] png = ldr.EncodeToPNG();
            byte[] dib = EncodeDib(ldr.GetPixels32(), ldr.width, ldr.height);
            if (png == null || png.Length == 0 || png.Length > MaximumBytes)
                throw new InvalidOperationException("Cannot encode the clipboard image as PNG.");
            var marker = new byte[SelectionTokenBytes];
            WriteUInt32(marker, 0, SelectionTokenMagic);
            WriteUInt32(marker, 4, SelectionTokenVersion);
            Buffer.BlockCopy(token.ToByteArray(), 0, marker, 8, 16);

            IntPtr owner = ClipboardOwner();
            uint pngFormat = RegisterClipboardFormat("PNG"), tokenFormat = RegisterClipboardFormat("WhimTex.Selection");
            if (pngFormat == 0 || tokenFormat == 0)
                throw new InvalidOperationException("Cannot register the clipboard image formats.");
            IntPtr pngMemory = IntPtr.Zero, dibMemory = IntPtr.Zero, tokenMemory = IntPtr.Zero;
            bool opened = false;
            try
            {
                // Prepare every payload, including its transferable memory, before replacing the clipboard.
                pngMemory = AllocateClipboardMemory(png);
                dibMemory = AllocateClipboardMemory(dib);
                tokenMemory = AllocateClipboardMemory(marker);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (OpenClipboard(owner)) { opened = true; break; }
                    if (attempt < 2) System.Threading.Thread.Sleep(10);
                }
                if (!opened) throw new InvalidOperationException("Clipboard is busy. Try copying again.");
                if (!EmptyClipboard()) throw new InvalidOperationException("Cannot replace the clipboard contents.");
                PublishMemory(pngFormat, ref pngMemory);
                PublishMemory(17, ref dibMemory); // CF_DIBV5
                // Publish identity last: a partial image write cannot validate an internal HDR snapshot.
                PublishMemory(tokenFormat, ref tokenMemory);
                if (!CloseClipboard()) throw new InvalidOperationException("Cannot finish copying the clipboard image.");
                opened = false;
                revision = GetClipboardSequenceNumber();
                return true;
            }
            finally
            {
                if (opened) CloseClipboard();
                if (pngMemory != IntPtr.Zero) GlobalFree(pngMemory);
                if (dibMemory != IntPtr.Zero) GlobalFree(dibMemory);
                if (tokenMemory != IntPtr.Zero) GlobalFree(tokenMemory);
            }
        }
#endif

        internal static bool IsCurrentImage(Guid token, uint revision)
        {
#if UNITY_EDITOR_WIN
            if (token == Guid.Empty || GetClipboardSequenceNumber() != revision) return false;
            if (!OpenClipboard(IntPtr.Zero)) throw new InvalidOperationException("Clipboard is busy. Try pasting again.");
            try
            {
                if (GetClipboardSequenceNumber() != revision) return false;
                uint format = RegisterClipboardFormat("WhimTex.Selection");
                if (format == 0 || !IsClipboardFormatAvailable(format)) return false;
                IntPtr memory = GetClipboardData(format);
                if (memory == IntPtr.Zero) return false;
                ulong size = GlobalSize(memory).ToUInt64();
                // GlobalAlloc may reserve more bytes than requested; only the versioned payload is meaningful.
                if (size < SelectionTokenBytes || size > 256) return false;
                IntPtr data = GlobalLock(memory);
                if (data == IntPtr.Zero) return false;
                try
                {
                    var marker = new byte[SelectionTokenBytes];
                    Marshal.Copy(data, marker, 0, marker.Length);
                    if (BitConverter.ToUInt32(marker, 0) != SelectionTokenMagic ||
                        BitConverter.ToUInt32(marker, 4) != SelectionTokenVersion) return false;
                    byte[] expected = token.ToByteArray();
                    for (int i = 0; i < expected.Length; i++) if (marker[i + 8] != expected[i]) return false;
                    return true;
                }
                finally { GlobalUnlock(memory); }
            }
            finally { CloseClipboard(); }
#else
            return false;
#endif
        }

        // Unity pixels run bottom to top; packed V5 pixels below run top to bottom in premultiplied BGRA.
        internal static byte[] EncodeDib(Color32[] pixels, int width, int height)
        {
            if (width < 1 || height < 1 || (long)width * height > CanvasSelection.MaximumPixels)
                throw new ArgumentOutOfRangeException(nameof(width), "Clipboard image dimensions are invalid.");
            if (pixels == null || pixels.Length != (long)width * height)
                throw new ArgumentException("Clipboard image pixels do not match its dimensions.", nameof(pixels));
            const int headerBytes = 124;
            var result = new byte[checked(headerBytes + pixels.Length * 4)];
            WriteUInt32(result, 0, headerBytes);
            WriteUInt32(result, 4, (uint)width);
            WriteUInt32(result, 8, unchecked((uint)-height));
            result[12] = 1; // One plane.
            result[14] = 32;
            WriteUInt32(result, 16, 3); // BI_BITFIELDS
            WriteUInt32(result, 20, (uint)(pixels.Length * 4));
            WriteUInt32(result, 40, 0x00FF0000u);
            WriteUInt32(result, 44, 0x0000FF00u);
            WriteUInt32(result, 48, 0x000000FFu);
            WriteUInt32(result, 52, 0xFF000000u);
            WriteUInt32(result, 56, 0x73524742u); // LCS_sRGB
            WriteUInt32(result, 108, 4); // LCS_GM_IMAGES
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width, output = headerBytes + y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[sourceRow + x];
                    result[output++] = (byte)((pixel.b * pixel.a + 127) / 255);
                    result[output++] = (byte)((pixel.g * pixel.a + 127) / 255);
                    result[output++] = (byte)((pixel.r * pixel.a + 127) / 255);
                    result[output++] = pixel.a;
                }
            }
            return result;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
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
        private static IntPtr ClipboardOwner()
        {
            uint process = GetCurrentProcessId();
            IntPtr owner = GetActiveWindow();
            if (owner != IntPtr.Zero && GetWindowThreadProcessId(owner, out uint activeProcess) != 0 && activeProcess == process)
                return owner;
            owner = GetForegroundWindow();
            if (owner != IntPtr.Zero && GetWindowThreadProcessId(owner, out uint foregroundProcess) != 0 && foregroundProcess == process)
                return owner;
            using (var current = System.Diagnostics.Process.GetCurrentProcess())
            {
                owner = current.MainWindowHandle;
                if (owner != IntPtr.Zero && GetWindowThreadProcessId(owner, out uint mainProcess) != 0 && mainProcess == process)
                    return owner;
            }
            throw new InvalidOperationException("Focus the Unity Editor window before copying an image.");
        }

        private static IntPtr AllocateClipboardMemory(byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(2, new UIntPtr((uint)bytes.Length)); // GMEM_MOVEABLE
            if (memory == IntPtr.Zero) throw new InvalidOperationException("Cannot allocate the clipboard image memory.");
            try
            {
                IntPtr data = GlobalLock(memory);
                if (data == IntPtr.Zero) throw new InvalidOperationException("Cannot access the clipboard image memory.");
                try { Marshal.Copy(bytes, 0, data, bytes.Length); }
                finally { GlobalUnlock(memory); }
                return memory;
            }
            catch { GlobalFree(memory); throw; }
        }

        private static void PublishMemory(uint format, ref IntPtr memory)
        {
            if (SetClipboardData(format, memory) == IntPtr.Zero)
                throw new InvalidOperationException("Cannot publish the clipboard image.");
            memory = IntPtr.Zero; // Windows owns successfully published handles.
        }

        [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] private static extern bool CloseClipboard();
        [DllImport("user32.dll")] private static extern bool EmptyClipboard();
        [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
#endif
    }
}
