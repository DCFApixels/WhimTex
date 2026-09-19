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
        private const string FooterMagic = "WHIMTEXD";

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
            byte[] payload = container.Serialize();
            var result = new byte[image.Length + payload.Length + 16];
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

        public static bool TryRead(string path, out byte[] payload, out string error)
        {
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
    }
}
