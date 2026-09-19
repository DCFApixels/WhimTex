using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    /// <summary>
    /// TIFF carrier of a WhimTex document: the composite image is the file itself, the document model
    /// and the drawing pixels travel in the container appended after the strip data.
    ///
    /// TIFF is used for every document, LDR or HDR, so that a single file never has to change its
    /// extension: the extension is part of the asset path, so switching between image formats would
    /// break every material reference that points at this texture.
    /// </summary>
    internal static class WhimTexTiffCarrier
    {
        public const string Extension = ".tiff";
        /// <summary>Written into the importer's .meta so opening an asset does not have to read the file.</summary>
        public const string MetaMarker = "whimtex.document";
        /// <summary>Tiny block carrying what the import pipeline needs, so it never reads the model.</summary>
        public const string FlagsBlock = "carrier";
        private const string FooterMagic = "WHIMTEXD";

        /// <summary>
        /// Cheap check used when an asset is opened. The marker in the .meta answers without touching the
        /// file; otherwise only the signature and the footer are read, never the whole image.
        /// </summary>
        public static bool IsDocument(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var importer = UnityEditor.AssetImporter.GetAtPath(path);
            if (importer != null && !string.IsNullOrEmpty(importer.userData))
                return importer.userData.Contains(MetaMarker);
            return HasFooter(path);
        }

        private static bool HasFooter(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < 24) return false;
                var head = new byte[4];
                if (stream.Read(head, 0, head.Length) != head.Length) return false;
                bool tiff = head[0] == 'I' && head[1] == 'I' && head[2] == 42 && head[3] == 0 ||
                            head[0] == 'M' && head[1] == 'M' && head[2] == 0 && head[3] == 42;
                if (!tiff) return TryRead(path, out _, out _); // earlier PNG and EXR carriers
                stream.Seek(-8, SeekOrigin.End);
                var tail = new byte[8];
                if (stream.Read(tail, 0, tail.Length) != tail.Length) return false;
                return Encoding.ASCII.GetString(tail) == FooterMagic;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>Builds the carrier: 8-bit samples for ordinary documents, 32-bit float for HDR ones.</summary>
        public static byte[] Write(WhimTexDocumentContainer container, Texture2D composite)
        {
            if (container == null) throw new WhimTexDocumentException("There is no document container to write.");
            if (composite == null) throw new WhimTexDocumentException("The document produced no composite image.");
            if (!composite.isReadable)
                throw new WhimTexDocumentException("The composite image of the document is not readable and cannot be saved.");
            bool hdr = composite.format == TextureFormat.RGBAHalf || composite.format == TextureFormat.RGBAFloat;
            byte[] image = hdr
                ? WhimTexTiffImage.Write(composite.width, composite.height, composite.GetPixels())
                : WhimTexTiffImage.Write(composite.width, composite.height, composite.GetPixels32());
            // A malformed carrier imports without an error but silently loses sprite sub-assets, so the
            // image is read back before the document ever reaches its folder.
            if (!WhimTexTiffImage.TryReadPixels(image, out int width, out int height, out byte[] decoded, out string error))
                throw new WhimTexDocumentException("The produced carrier image is invalid: " + error);
            int expected = composite.width * composite.height * 4 * (hdr ? 4 : 1);
            if (width != composite.width || height != composite.height || decoded.Length != expected)
                throw new WhimTexDocumentException("The produced carrier image does not match the composite.");
            // A few bytes the import pipeline can fetch without touching the model or the pixels.
            container.Set(FlagsBlock, new[] { (byte)(composite.isDataSRGB ? 1 : 0) },
                System.IO.Compression.CompressionLevel.Fastest);
            byte[] payload = container.Serialize();            var result = new byte[image.Length + payload.Length + 16];
            Buffer.BlockCopy(image, 0, result, 0, image.Length);
            Buffer.BlockCopy(payload, 0, result, image.Length, payload.Length);
            Buffer.BlockCopy(BitConverter.GetBytes((long)payload.Length), 0, result, image.Length + payload.Length, 8);
            Buffer.BlockCopy(Encoding.ASCII.GetBytes(FooterMagic), 0, result, image.Length + payload.Length + 8, 8);
            return result;
        }

        /// <summary>Extracts the document payload, also accepting the earlier PNG and EXR carriers.</summary>
        public static bool TryRead(byte[] file, out byte[] payload, out string error)
        {
            payload = null;
            error = null;
            if (file == null) { error = "The file is empty."; return false; }
            if (WhimTexTiffImage.IsTiff(file))
            {
                if (file.Length < 16 || Encoding.ASCII.GetString(file, file.Length - 8, 8) != FooterMagic)
                {
                    error = "The TIFF carries no WhimTex document.";
                    return false;
                }
                long length = BitConverter.ToInt64(file, file.Length - 16);
                if (length <= 0 || length > file.Length - 16)
                {
                    error = "The document footer is invalid.";
                    return false;
                }
                payload = new byte[length];
                Buffer.BlockCopy(file, (int)(file.Length - 16 - length), payload, 0, (int)length);
                return true;
            }
            return WhimTexDocumentContainer.TryReadPayload(file, out payload, out error);
        }

        public static bool TryRead(string path, out byte[] payload, out string error)        {
            payload = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "The document file was not found: " + path; return false; }
                return TryRead(File.ReadAllBytes(path), out payload, out error);
            }
            catch (IOException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Reads only what the import pipeline needs: the footer, the block table and the small flags block,
        /// without inflating the model or the drawing pixels.
        /// </summary>
        public static bool TryReadCarrierFlags(string path, out bool srgb, out bool isDocument, out string error)
        {
            srgb = false;
            isDocument = false;
            if (!TryRead(path, out byte[] payload, out error)) return false;
            isDocument = true;
            if (!WhimTexDocumentContainer.TryReadBlock(payload, FlagsBlock, out byte[] flags, out error)) return false;
            srgb = flags.Length > 0 && (flags[0] & 1) != 0;
            return true;
        }
    }
}
