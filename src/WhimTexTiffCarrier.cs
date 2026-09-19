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
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase)) return false;
            var importer = UnityEditor.AssetImporter.GetAtPath(path);
            if (importer != null && !string.IsNullOrEmpty(importer.userData) && importer.userData.Contains(MetaMarker))
                return true;
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
                if (!tiff) return false;
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
        public static byte[] Write(WhimTexDocumentContainer container, Texture2D composite, bool? srgbOutput = null)
        {
            using var stream = new MemoryStream();
            WriteTo(stream, container, composite, srgbOutput);
            return stream.ToArray();
        }

        internal static void WriteTo(Stream stream, WhimTexDocumentContainer container, Texture2D composite, bool? srgbOutput = null)
        {
            if (container == null) throw new WhimTexDocumentException("There is no document container to write.");
            if (composite == null) throw new WhimTexDocumentException("The document produced no composite image.");
            if (!composite.isReadable)
                throw new WhimTexDocumentException("The composite image of the document is not readable and cannot be saved.");
            bool halfSource = composite.format == TextureFormat.RGBAHalf;
            bool floatSource = composite.format == TextureFormat.RGBAFloat;
            int sourceBits = halfSource ? 16 : floatSource ? 32 : 8;
            byte[] rawPixels = composite.GetRawTextureData<byte>().ToArray();
            // The sample format follows the content, not the document setting: colours that all fit inside
            // 0..1 do not need float samples, a float file and BC6H compression on every single import.
            bool needsFloat = sourceBits != 8 && WhimTexTiffImage.HasValuesOutsideUnitRange(rawPixels, sourceBits);
            int targetBits = needsFloat ? 32 : 8;
            // Dropping half-float to 8-bit linear bands in the shadows, so those values are encoded to sRGB
            // and the carrier flags tell Unity to decode them back. Data that is already 8-bit keeps its own
            // encoding: a linear data texture must not be reinterpreted.
            bool encodeSrgb = targetBits == 8 && sourceBits != 8 && !composite.isDataSRGB && (srgbOutput ?? true);
            byte[] image = WhimTexTiffImage.WriteRaw(composite.width, composite.height, rawPixels, sourceBits,
                targetBits, encodeSrgb);
            // A malformed carrier imports without an error but silently loses sprite sub-assets, so the
            // image is read back before the document ever reaches its folder.
            if (!WhimTexTiffImage.Validate(image, out int width, out int height, out string error))
                throw new WhimTexDocumentException("The produced carrier image is invalid: " + error);
            if (width != composite.width || height != composite.height)
                throw new WhimTexDocumentException("The produced carrier image does not match the composite.");
            // A few bytes the import pipeline can fetch without touching the model or the pixels.
            container.Set(FlagsBlock, new[] { (byte)(encodeSrgb || composite.isDataSRGB ? 1 : 0) },
                System.IO.Compression.CompressionLevel.Fastest);
            stream.Write(image, 0, image.Length);
            long start = stream.Position;
            container.WriteTo(stream);
            long length = stream.Position - start;
            using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            writer.Write(length);
            writer.Write(Encoding.ASCII.GetBytes(FooterMagic));
        }

        internal static WhimTexDocumentContainer OpenContainer(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (stream.Length < 24 || stream.Length > 4L * 1024 * 1024 * 1024)
                    throw new WhimTexDocumentException("Invalid file size (maximum 4 GiB).");
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (!WhimTexTiffImage.IsTiff(reader.ReadBytes(8)))
                    throw new WhimTexDocumentException("WhimTex documents must be TIFF images.");
                stream.Position = stream.Length - 16;
                long length = reader.ReadInt64();
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != FooterMagic) throw new WhimTexDocumentException("The TIFF carries no WhimTex document.");
                return WhimTexDocumentContainer.Open(stream, stream.Length - 16 - length, length);
            }
            catch { stream.Dispose(); throw; }
        }

        /// <summary>Extracts the TIFF document payload for in-memory diagnostics/tests.</summary>
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
            error = "WhimTex documents must be TIFF images.";
            return false;
        }

        public static bool TryRead(string path, out byte[] payload, out string error)
        {
            payload = null;
            error = null;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "The document file was not found: " + path; return false; }
                if (new FileInfo(path).Length > 512L * 1024 * 1024) { error = "Use the streaming loader for documents larger than 512 MiB."; return false; }
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
            error = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < 24) return false;
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                if (!WhimTexTiffImage.IsTiff(reader.ReadBytes(8))) return false;
                stream.Position = stream.Length - 16;
                long length = reader.ReadInt64();
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != FooterMagic) return false;
                byte[] flags = WhimTexDocumentContainer.ReadSmallBlock(stream, stream.Length - 16 - length, length, FlagsBlock);
                if (flags.Length != 1) throw new WhimTexDocumentException("Invalid carrier flags.");
                srgb = (flags[0] & 1) != 0;
                isDocument = true;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is WhimTexDocumentException || exception is UnauthorizedAccessException)
            { error = exception.Message; return false; }
        }
    }
}
