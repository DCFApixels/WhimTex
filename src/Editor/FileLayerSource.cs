using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class FileLayerSource
    {
        internal static readonly string[] FileFilters = { "Supported images", "png,jpg,jpeg,tga,bmp,exr,tif,tiff" };

        internal static bool Supports(string path)
        {
            switch (Path.GetExtension(path ?? string.Empty).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".tga": case ".bmp":
                case ".exr": case ".tif": case ".tiff": return true;
                default: return false;
            }
        }

        internal static Texture2D ProjectTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string assetPath = Path.IsPathRooted(path) ? PresetLibraryPaths.AssetPath(path) : path.Replace('\\', '/');
            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        internal static string[] DragPaths()
        {
            var objects = DragAndDrop.objectReferences;
            if (objects.Length > 0)
            {
                var paths = new string[objects.Length];
                bool allTextures = true;
                for (int i = 0; i < objects.Length; i++)
                {
                    if (!(objects[i] is Texture2D) || !AssetDatabase.Contains(objects[i])) { allTextures = false; break; }
                    paths[i] = AssetDatabase.GetAssetPath(objects[i]);
                }
                if (allTextures) return paths;
            }
            var external = DragAndDrop.paths;
            if (external.Length == 0) return null;
            foreach (string path in external)
                if (!File.Exists(path) || !Supports(path) && ProjectTexture(path) == null) return null;
            return external;
        }

        internal static Texture2D Prepare(WhimTexDocument owner, string path)
        {
            var asset = ProjectTexture(path);
            if (asset != null)
            {
                if (WhimTexDocumentService.IsOwnOutput(owner, asset)) throw new InvalidOperationException("A document cannot sample its own output texture.");
                return asset;
            }
            path = NormalizeExternalPath(path);
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            string documentPath = WhimTexDocumentService.PathOf(owner);
            if (!string.IsNullOrEmpty(documentPath) && string.Equals(path, Path.GetFullPath(documentPath),
                Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("A document cannot sample its own output file.");
            return owner.ResolveExternalFileTexture(path, true) ?? throw new IOException(owner.ExternalFileError(path));
        }

        internal static string NormalizeExternalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                throw new ArgumentException("An external image needs an absolute file path.");
            if (!Supports(path)) throw new NotSupportedException("No image parser is available for " + Path.GetExtension(path) + ".");
            return Path.GetFullPath(path);
        }

        internal static Texture2D Decode(string path)
        {
            path = NormalizeExternalPath(path);
            byte[] bytes = File.ReadAllBytes(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            Texture2D texture = null;
            try
            {
                if (extension == ".tif" || extension == ".tiff")
                {
                    bool linear = WhimTexTiffCarrier.TryReadCarrierFlags(path, out bool srgb, out _, out _) && !srgb;
                    texture = DecodeTiff(bytes, linear);
                }
                else if (extension == ".tga")
                {
                    if (bytes.Length < 18) throw new InvalidDataException("Incomplete TGA header.");
                    CheckSize(U16(bytes, 12), U16(bytes, 14));
                    if (!WhimTexTgaDecoder.TryDecode(bytes, false, FilterMode.Bilinear, TextureWrapMode.Clamp, TextureWrapMode.Clamp, out texture))
                        throw new InvalidDataException("Unsupported or damaged TGA image.");
                }
                else if (extension == ".bmp")
                {
                    if (bytes.Length < 54) throw new InvalidDataException("Incomplete BMP header.");
                    CheckSize(I32(bytes, 18), Math.Abs((long)I32(bytes, 22)));
                    if (!WhimTexBmpDecoder.TryDecode(bytes, false, FilterMode.Bilinear, TextureWrapMode.Clamp, TextureWrapMode.Clamp, out texture))
                        throw new InvalidDataException("Unsupported or damaged BMP image.");
                }
                else
                {
                    ValidateNativeHeader(bytes, extension);
                    bool hdr = extension == ".exr";
                    texture = new Texture2D(2, 2, hdr ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, hdr);
                    if (!ImageConversion.LoadImage(texture, bytes, false))
                        throw new InvalidDataException("Unsupported or damaged " + extension + " image.");
                    CheckSize(texture.width, texture.height);
                }
                texture.name = Path.GetFileName(path);
                texture.hideFlags = HideFlags.HideAndDontSave;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch { if (texture != null) UnityEngine.Object.DestroyImmediate(texture); throw; }
        }

        private static void CheckSize(long width, long height)
        {
            int maximum = Math.Min(16384, SystemInfo.maxTextureSize);
            if (width < 1 || height < 1 || width > maximum || height > maximum)
                throw new InvalidDataException("Image dimensions exceed the supported texture size (" + maximum + " per side).");
        }

        private static int U16(byte[] b, int p) => b[p] | b[p + 1] << 8;
        private static int I32(byte[] b, int p) => b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24;
        private static uint Big32(byte[] b, int p) => (uint)b[p] << 24 | (uint)b[p + 1] << 16 | (uint)b[p + 2] << 8 | b[p + 3];

        private static void ValidateNativeHeader(byte[] b, string extension)
        {
            if (extension == ".png")
            {
                if (b.Length < 33 || Big32(b, 0) != 0x89504e47 || Big32(b, 4) != 0x0d0a1a0a || Big32(b, 12) != 0x49484452)
                    throw new InvalidDataException("Invalid PNG header.");
                CheckSize(Big32(b, 16), Big32(b, 20)); return;
            }
            if (extension == ".exr")
            {
                if (b.Length < 9 || I32(b, 0) != 20000630) throw new InvalidDataException("Invalid EXR header.");
                int p = 8;
                string Text()
                {
                    int start = p;
                    while (p < b.Length && b[p] != 0 && p - start <= 255) p++;
                    if (p == b.Length || p - start > 255) throw new InvalidDataException("Invalid EXR attribute.");
                    return Encoding.ASCII.GetString(b, start, p++ - start);
                }
                while (p < b.Length && b[p] != 0)
                {
                    string name = Text(); string type = Text();
                    if (p > b.Length - 4) break;
                    int size = I32(b, p); p += 4;
                    if (size < 0 || size > b.Length - p) break;
                    if (name == "dataWindow" && type == "box2i" && size == 16)
                    { CheckSize((long)I32(b, p + 8) - I32(b, p) + 1, (long)I32(b, p + 12) - I32(b, p + 4) + 1); return; }
                    p += size;
                }
                throw new InvalidDataException("EXR has no valid data window.");
            }
            if (b.Length > 4 && b[0] == 255 && b[1] == 216)
            {
                int p = 2;
                while (p + 3 < b.Length)
                {
                    if (b[p++] != 255) break;
                    while (p < b.Length && b[p] == 255) p++;
                    if (p >= b.Length) break;
                    int marker = b[p++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    if (p + 1 >= b.Length) break;
                    int length = b[p] << 8 | b[p + 1];
                    if (length < 2 || length > b.Length - p) break;
                    if (marker >= 192 && marker <= 207 && marker != 196 && marker != 200 && marker != 204)
                    {
                        if (length < 8) break;
                        CheckSize(b[p + 5] << 8 | b[p + 6], b[p + 3] << 8 | b[p + 4]); return;
                    }
                    p += length;
                }
            }
            throw new InvalidDataException("Invalid JPEG header.");
        }

        private static Texture2D DecodeTiff(byte[] bytes, bool linear)
        {
            if (bytes.Length < 8 || bytes[0] != 'I' || bytes[1] != 'I' || U16(bytes, 2) != 42)
                throw new InvalidDataException("The TIFF parser requires a little-endian RGBA TIFF.");
            int ifd = I32(bytes, 4);
            if (ifd < 8 || ifd > bytes.Length - 2) throw new InvalidDataException("Invalid TIFF directory.");
            int count = U16(bytes, ifd), bits = 0, sampleFormat = 1;
            for (int i = 0; i < count; i++)
            {
                int p = ifd + 2 + 12 * i;
                if (p > bytes.Length - 12) throw new InvalidDataException("Incomplete TIFF directory.");
                int tag = U16(bytes, p), n = I32(bytes, p + 4), data = I32(bytes, p + 8);
                if (tag == 258 || tag == 339)
                {
                    if (U16(bytes, p + 2) != 3 || n != 4 || data < 0 || data > bytes.Length - 8)
                        throw new InvalidDataException("TIFF needs four matching channel formats.");
                    int value = U16(bytes, data);
                    for (int c = 1; c < 4; c++) if (U16(bytes, data + 2 * c) != value) throw new InvalidDataException("Mixed TIFF channel formats are unsupported.");
                    if (tag == 258) bits = value; else sampleFormat = value;
                }
                if (tag == 262 && data != 2 || tag == 274 && data != 1 || tag == 284 && data != 1 ||
                    tag == 317 && data != 1 || tag == 338 && data != 2)
                    throw new InvalidDataException("Unsupported TIFF layout. Use top-down, straight RGBA without a predictor.");
            }
            if (!(bits == 8 && sampleFormat == 1 || bits == 32 && sampleFormat == 3))
                throw new InvalidDataException("TIFF supports RGBA 8-bit or 32-bit float samples.");
            if (!WhimTexTiffImage.TryReadPixels(bytes, out int width, out int height, out byte[] raw, out string error))
                throw new InvalidDataException(error);
            CheckSize(width, height);
            int stride = width * 4 * (bits / 8);
            byte[] row = new byte[stride];
            for (int y = 0; y < height / 2; y++)
            {
                int bottom = (height - 1 - y) * stride, top = y * stride;
                Buffer.BlockCopy(raw, top, row, 0, stride);
                Buffer.BlockCopy(raw, bottom, raw, top, stride);
                Buffer.BlockCopy(row, 0, raw, bottom, stride);
            }
            var texture = new Texture2D(width, height, bits == 32 ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, bits == 32 || linear);
            try { texture.LoadRawTextureData(raw); texture.Apply(false, false); return texture; }
            catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
        }
    }
}
