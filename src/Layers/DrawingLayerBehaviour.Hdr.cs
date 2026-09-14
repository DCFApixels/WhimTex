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
            if (colorRange == LayerColorRange.HDR && pixels != null && !HdrUtility.IsHdr(pixels))
                ChangeStorage(TextureFormat.RGBAHalf, "Promote Drawing Storage");
        }

        internal void ConvertTo8Bit()
        {
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
                unchecked { pixelsRevision++; }
                ReleasePaintSurface();
            }
            finally { Object.DestroyImmediate(replacement); }
        }

        internal void ApplyFillPixels(NativeArray<Color> output, int width, int height, string undoName)
        {
            EnsureHdrStorage();
            InitializeCanvas(width, height);
            Undo.RegisterCompleteObjectUndo(pixels, undoName);
            unchecked { pixelsRevision++; }
            HdrUtility.WritePixels(pixels, output);
            EditorUtility.SetDirty(pixels);
            ReleasePaintSurface();
        }
    }
}
