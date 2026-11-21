using Unity.Collections;
using Unity.Mathematics;
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
            QuantizeTiledStorage();
        }

        private void QuantizeTiledStorage()
        {
            if (!HasTiledSource) return;
            CommitTiledStroke();
            PaintTileTransfer.DrainFor(pixels);
            foreach (var tile in TiledPixels)
            {
                Undo.RegisterCompleteObjectUndo(tile.texture, "Convert Drawing to 8-bit");
                var values = tile.texture.GetRawTextureData<half4>();
                for (int i = 0; i < values.Length; i++)
                {
                    float4 pixel = values[i];
                    Color straight = pixel.w > .00001f
                        ? new Color(pixel.x / pixel.w, pixel.y / pixel.w, pixel.z / pixel.w, pixel.w)
                        : Color.clear;
                    Color32 encoded = HdrUtility.Encode(HdrUtility.Saturate(HdrUtility.Safe(straight)));
                    Color rounded = HdrUtility.Decode(encoded);
                    values[i] = new half4(new float4(rounded.r * rounded.a, rounded.g * rounded.a,
                        rounded.b * rounded.a, rounded.a));
                }
                tile.texture.Apply(false, false);
                EditorUtility.SetDirty(tile.texture);
            }
            ReleaseTiledRuntime();
            InvalidateAlphaCoverage();
            unchecked { paintSurfaceRevision++; }
        }

        private void ChangeStorage(TextureFormat format, string undoName)
        {
            // A clean preview must not round-trip untouched pixels before Undo captures them.
            SyncPendingSurfaceToTexture();
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
            PaintTileTransfer.DrainFor(pixels);
            EnsureHdrStorage();
            InitializeCanvas(width, height);
            if (output.Length != pixels.width * pixels.height)
                throw new System.ArgumentException("Pixel replacement must match the Drawing source dimensions.", nameof(output));
            PrepareStroke(width, height, undoName);
            if (pixelUndoLabel == null)
            {
                HdrUtility.WritePixels(pixels, output);
                tiledBaseEmpty = false;
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
