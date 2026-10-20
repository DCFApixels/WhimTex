using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal void SetColorRange(LayerColorRange range)
        {
            colorRange = range;
            EnsureHdrStorage();
        }

        private void EnsureHdrStorage()
        {
            EnsureDeferredTexture();
            if (colorRange == LayerColorRange.HDR && pixels != null && !HdrUtility.IsHdr(pixels))
                ChangeStorage(TextureFormat.RGBAHalf, "Promote Drawing Storage");
        }

        internal void ConvertTo8Bit()
        {
            EnsureDeferredTexture();
            colorRange = LayerColorRange.Standard;
            if (pixels != null && HdrUtility.IsHdr(pixels)) ChangeStorage(TextureFormat.RGBA32, "Convert Drawing to 8-bit");
        }

        private void ChangeStorage(TextureFormat format, string undoName)
        {
            SyncSurfaceToTexture();
            Undo.RegisterCompleteObjectUndo(pixels, undoName);
            using var values = HdrUtility.ReadPixels(pixels, Allocator.Temp);
            var replacement = new Texture2D(pixels.width, pixels.height, format, false, format == TextureFormat.RGBAHalf)
            { name = pixels.name, hideFlags = pixels.hideFlags, filterMode = pixels.filterMode,
                wrapModeU = pixels.wrapModeU, wrapModeV = pixels.wrapModeV };
            try
            {
                HdrUtility.WritePixels(replacement, values);
                // Preserve sub-asset identity and let native object Undo restore both layout and pixels.
                EditorUtility.CopySerialized(replacement, pixels);
                EditorUtility.SetDirty(pixels);
                ReleasePaintSurface();
            }
            finally { Object.DestroyImmediate(replacement); }
        }

        internal void ApplyFillPixels(NativeArray<Color> output, int width, int height, string undoName)
        {
            PaintTileTransfer.Drain();
            EnsureHdrStorage();
            InitializeCanvas(width, height);
            if (output.Length != pixels.width * pixels.height)
                throw new System.ArgumentException("Pixel replacement must match the Drawing source dimensions.", nameof(output));
            PrepareStroke(width, height, undoName);
            if (pixelUndoLabel == null)
            {
                HdrUtility.WritePixels(pixels, output);
                EditorUtility.SetDirty(pixels);
                ReleasePaintSurface();
                return;
            }
            var replacement = new Texture2D(pixels.width, pixels.height, pixels.format, false, !pixels.isDataSRGB)
                { hideFlags = HideFlags.HideAndDontSave };
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                // Keep old stored bytes intact until the ordered transfer patches them.
                // An explicit replacement also owns zero-alpha RGB; do not unpremultiply it.
                HdrUtility.WritePixels(replacement, output);
                CapturePixelEdit(new RectInt(0, 0, pixels.width, pixels.height));
                GL.sRGBWrite = false;
                var conversion = WhimTexMaterials.AlphaConversion;
                conversion.SetFloat("_Mode", 0);
                conversion.SetFloat("_DecodeSource", HdrUtility.IsHdr(pixels) ? 0 : 1);
                Graphics.Blit(replacement, paintSurface, conversion);
                paintSurfaceDirty = true;
                InvalidateAlphaCoverage();
                unchecked { paintSurfaceRevision++; }
                CommitPixelEdit(replacement);
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; Object.DestroyImmediate(replacement); }
        }
    }
}
