using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private RenderTexture paintWriteBefore, paintWriteScratch;
        [NonSerialized] private int strokeWriteChannels = 15;
        [NonSerialized] private bool preserveStraightPaintRgb;
        private bool StrokeWritesNothing => strokeWriteChannels == 0;

        internal void ConfigureStrokeWriteProtection(int channels)
        {
            channels &= 15;
            if (strokeWriteChannels == channels &&
                (paintWriteBefore != null || channels == 15 || StrokeWritesNothing)) return;
            ReleasePaintWriteProtection();
            strokeWriteChannels = channels;
            if (channels == 15 || StrokeWritesNothing || paintSurface == null) return;
            preserveStraightPaintRgb = pixels != null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                paintWriteBefore = ProfiledRenderTexture.GetTemporary(paintSurface.descriptor);
                paintWriteScratch = ProfiledRenderTexture.GetTemporary(paintSurface.descriptor);
                paintWriteBefore.filterMode = paintWriteScratch.filterMode = FilterMode.Point;
                Graphics.Blit(paintSurface, paintWriteBefore);
            }
            catch { ReleasePaintWriteProtection(); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }

        private void ApplyPaintWriteProtection()
        {
            if (paintWriteBefore == null || paintSurface == null) return;
            var material = WhimTexMaterials.PaintWriteProtection;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Paint write-protection shader is unavailable.");
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetTexture("_PaintBefore", paintWriteBefore);
                material.SetTexture("_OriginalStraight", pixels);
                material.SetFloat("_StraightFallback", pixels != null ? (HdrUtility.IsHdr(pixels) ? 1f : 2f) : 0f);
                material.SetVector("_WriteChannels", new Vector4((strokeWriteChannels & 1) != 0 ? 1 : 0,
                    (strokeWriteChannels & 2) != 0 ? 1 : 0, (strokeWriteChannels & 4) != 0 ? 1 : 0,
                    (strokeWriteChannels & 8) != 0 ? 1 : 0));
                Graphics.Blit(paintSurface, paintWriteScratch, material, 0);
                Graphics.Blit(paintWriteScratch, paintSurface);
            }
            finally
            {
                material.SetTexture("_PaintBefore", null);
                material.SetTexture("_OriginalStraight", null);
                material.SetFloat("_StraightFallback", 0f);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        private void ReleasePaintWriteProtection()
        {
            if (paintWriteBefore != null) ProfiledRenderTexture.ReleaseTemporary(paintWriteBefore);
            if (paintWriteScratch != null) ProfiledRenderTexture.ReleaseTemporary(paintWriteScratch);
            paintWriteBefore = paintWriteScratch = null;
            strokeWriteChannels = 15;
        }

        internal static Color ProtectPaintColor(Color before, Color after, int channels)
        {
            if ((channels & 8) == 0 && before.a <= 0f) return before;
            if ((channels & 1) == 0) after.r = before.r;
            if ((channels & 2) == 0) after.g = before.g;
            if ((channels & 4) == 0) after.b = before.b;
            if ((channels & 8) == 0) after.a = before.a;
            return after;
        }
    }
}
