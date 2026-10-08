using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum RasterImageFormat { Png, Jpeg, Tga, Exr }

    internal static class WhimTexRasterEncoder
    {
        internal static byte[] Encode(Texture2D texture, RasterImageFormat format, int jpegQuality = 95,
            Texture2D.EXRFlags exrFlags = Texture2D.EXRFlags.CompressZIP)
        {
            if (format == RasterImageFormat.Exr) return texture.EncodeToEXR(exrFlags);
            if (format != RasterImageFormat.Png && format != RasterImageFormat.Jpeg && format != RasterImageFormat.Tga)
                throw new ArgumentOutOfRangeException(nameof(format));
            Texture2D ldr = HdrUtility.ToLdr(texture, format == RasterImageFormat.Jpeg);
            try
            {
                switch (format)
                {
                    case RasterImageFormat.Png: return ldr.EncodeToPNG();
                    case RasterImageFormat.Tga: return ldr.EncodeToTGA();
                    case RasterImageFormat.Jpeg: return ldr.EncodeToJPG(jpegQuality);
                    default: throw new ArgumentOutOfRangeException(nameof(format));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(ldr); }
        }
    }
}
