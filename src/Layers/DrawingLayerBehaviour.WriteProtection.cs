using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        [NonSerialized] private RenderTexture paintWriteBefore, paintWriteScratch;
        [NonSerialized] private int strokeWriteChannels = 15;
        [NonSerialized] private bool strokeLockAlpha;
        [NonSerialized] private bool preserveStraightPaintRgb;
        private bool StrokeWritesNothing => strokeWriteChannels == 0 || strokeLockAlpha && strokeWriteChannels == 8;

        internal void ConfigureStrokeWriteProtection(int channels, bool lockAlpha)
        {
            channels &= 15;
            if (strokeWriteChannels == channels && strokeLockAlpha == lockAlpha &&
                (paintWriteBefore != null || channels == 15 && !lockAlpha || StrokeWritesNothing)) return;
            ReleasePaintWriteProtection();
            strokeWriteChannels = channels;
            strokeLockAlpha = lockAlpha;
            if (channels == 15 && !lockAlpha || StrokeWritesNothing || paintSurface == null) return;
            preserveStraightPaintRgb = pixels != null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                paintWriteBefore = RenderTexture.GetTemporary(paintSurface.descriptor);
                paintWriteScratch = RenderTexture.GetTemporary(paintSurface.descriptor);
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
                    !strokeLockAlpha && (strokeWriteChannels & 8) != 0 ? 1 : 0));
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
            if (paintWriteBefore != null) RenderTexture.ReleaseTemporary(paintWriteBefore);
            if (paintWriteScratch != null) RenderTexture.ReleaseTemporary(paintWriteScratch);
            paintWriteBefore = paintWriteScratch = null;
            strokeWriteChannels = 15; strokeLockAlpha = false;
        }

        internal static Color ProtectPaintColor(Color before, Color after, int channels, bool lockAlpha)
        {
            if ((lockAlpha || (channels & 8) == 0) && before.a <= 0f) return before;
            if ((channels & 1) == 0) after.r = before.r;
            if ((channels & 2) == 0) after.g = before.g;
            if ((channels & 4) == 0) after.b = before.b;
            if (lockAlpha || (channels & 8) == 0) after.a = before.a;
            return after;
        }
    }
}
