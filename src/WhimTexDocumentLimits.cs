using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DCFApixels.WhimTex
{
    public enum WhimTexOutputPrecision { Auto = 0, EightBit = 1, Float32 = 2 }

    internal static class WhimTexDocumentLimits
    {
        internal const long TextureBytes = 256L * 1024 * 1024;
        internal const long TotalTextureBytes = 1024L * 1024 * 1024;

        internal static void CheckTexture(long bytes, ref long total, string label)
        {
            total = checked(total + bytes);
            if (bytes < 0 || bytes > TextureBytes || total > TotalTextureBytes)
                throw new WhimTexDocumentException(label + " exceeds TIFF document limits: " +
                    (bytes / (1024d * 1024)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                    " MiB; maximum 256 MiB per embedded texture and 1 GiB total. Reduce Drawing source resolution or split the document. Original files are unchanged.");
        }

        internal static long Validate(TextureCompositor document)
        {
            if (document.width < 1 || document.height < 1 || document.width > 16384 || document.height > 16384)
                throw new WhimTexDocumentException("TIFF canvas dimensions must be between 1 and 16384.");
            if (!Enum.IsDefined(typeof(WhimTexOutputPrecision), document.outputPrecision))
                throw new WhimTexDocumentException("Unsupported TIFF output precision.");
            if ((long)document.width * document.height * 8 > int.MaxValue)
                throw new WhimTexDocumentException("The half-float working composite exceeds the 2 GiB buffer limit. Reduce canvas size.");
            if (document.outputPrecision == WhimTexOutputPrecision.Float32 && (long)document.width * document.height * 16 > int.MaxValue)
                throw new WhimTexDocumentException("Float32 TIFF composite exceeds the 2 GiB decoded image limit. Reduce canvas size.");
            long total = 0;
            var textures = new HashSet<Texture2D>();
            var visited = new HashSet<Layer>();
            void Visit(List<Layer> layers, int depth)
            {
                if (layers == null) return;
                if (depth > 128) throw new WhimTexDocumentException("The layer hierarchy exceeds the supported depth of 128.");
                foreach (var layer in layers)
                {
                    if (layer == null) continue;
                    if (!visited.Add(layer)) throw new WhimTexDocumentException("A layer occurs more than once in the document hierarchy. Remove the duplicate or circular group reference.");
                    if (layer.Behaviour is DrawingLayerBehaviour drawing)
                    {
                        var texture = drawing.StoredTexture;
                        if (texture == null || textures.Add(texture))
                            CheckTexture(drawing.DocumentPixelBytes, ref total, "Drawing '" + layer.layerName + "'");
                    }
                    Visit(layer.children, depth + 1);
                }
            }
            Visit(document.layers, 0);
            return total;
        }

        internal static void ValidateDirectory(WhimTexDocumentContainer container)
        {
            long total = 0;
            foreach (string name in container.Names)
                if (name.StartsWith("texture:", StringComparison.Ordinal) && !name.EndsWith(":sampling", StringComparison.Ordinal))
                    CheckTexture(container.LengthOf(name), ref total, "Drawing block '" + name + "'");
        }

        internal static long ExpectedBytes(int width, int height, TextureFormat format, int mips, bool linear)
        {
            var graphics = GraphicsFormatUtility.GetGraphicsFormat(format, !linear);
            if (graphics == GraphicsFormat.None) throw new WhimTexDocumentException("Unsupported embedded texture format.");
            long bytes = 0;
            for (int i = 0; i < mips; i++)
            {
                bytes = checked(bytes + (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, graphics));
                width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
            }
            return bytes;
        }
    }
}
