using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        internal void BlurSegment(Vector2 fromSourceUv, Vector2 toSourceUv, int outputWidth, int outputHeight,
            float size, float hardness, float strength, RenderTexture sample)
        {
            if (!TiledCanvasUtility.IsInvertible(Owner.PixelCanvasTransform)) return;
            RenderTexture surface = EnsurePaintSurface(outputWidth, outputHeight);
            if (surface == null) return;
            size = Mathf.Clamp(size, 1f, Mathf.Max(outputWidth, outputHeight));
            hardness = Mathf.Clamp01(hardness);
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f) return;

            segmentStamps ??= new List<PaintStamp>(256);
            segmentStamps.Clear();
            float distance = Vector2.Distance(fromSourceUv * new Vector2(outputWidth, outputHeight),
                toSourceUv * new Vector2(outputWidth, outputHeight));
            int count = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(1f, size * .2f)));
            for (int i = 0; i <= count; i++)
                segmentStamps.Add(new PaintStamp
                {
                    center = Owner.PixelCanvasTransform.Map(Vector2.Lerp(fromSourceUv, toSourceUv, i / (float)count),
                        new Vector2(outputWidth, outputHeight)),
                    size = size,
                    rotation = 0f,
                    flip = 0,
                    color = Color.white
                });

            RenderTexture mask = null, source = null, scratch = null, blurred = null, output = null;
            try
            {
                mask = RenderTexture.GetTemporary(outputWidth, outputHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                mask.filterMode = FilterMode.Bilinear;
                mask.wrapMode = TextureWrapMode.Clamp;
                Clear(mask);
                PaintBrushRenderer.Draw(mask, segmentStamps, size, hardness, false, PencilShape.Circle, Color.white,
                    false, outputWidth, outputHeight, patternCenter, false, Owner.PixelCanvasTransform,
                    false, false, null, null, false, false, false);

                source = RenderTexture.GetTemporary(surface.descriptor);
                Graphics.Blit(sample != null ? sample : surface, source);
                scratch = RenderTexture.GetTemporary(surface.descriptor);
                blurred = RenderTexture.GetTemporary(surface.descriptor);
                Material blur = WhimTexMaterials.GaussianBlur;
                blur.SetInt("_Edges", 1);
                blur.SetFloat("_Strength", 1f);
                blur.SetTexture("_SourceTex", null);
                float radius = Mathf.Max(1f, size * .5f);
                blur.SetFloat("_CenterWeight", .2f);
                blur.SetInt("_PairCount", 5);
                blur.SetVectorArray("_Kernel", new[]
                {
                    new Vector4(Mathf.Max(1f, radius * .15f), .12f, 0f, 0f),
                    new Vector4(Mathf.Max(2f, radius * .30f), .10f, 0f, 0f),
                    new Vector4(Mathf.Max(3f, radius * .45f), .08f, 0f, 0f),
                    new Vector4(Mathf.Max(4f, radius * .60f), .06f, 0f, 0f),
                    new Vector4(Mathf.Max(5f, radius * .80f), .04f, 0f, 0f)
                });
                blur.SetVector("_Direction", new Vector4(1f / outputWidth, 0f, 0f, 0f));
                Graphics.Blit(source, scratch, blur, 2);
                blur.SetVector("_Direction", new Vector4(0f, 1f / outputHeight, 0f, 0f));
                Graphics.Blit(scratch, blurred, blur, 2);
                output = RenderTexture.GetTemporary(surface.descriptor);
                Material composite = WhimTexMaterials.BlurBrush;
                composite.SetTexture("_BlurTex", blurred);
                composite.SetTexture("_MaskTex", mask);
                composite.SetFloat("_Strength", strength);
                Graphics.Blit(surface, output, composite);
                Graphics.Blit(output, surface);
                paintSurfaceDirty = true;
                unchecked { paintSurfaceRevision++; }
                originalImageUrl = null;
            }
            finally
            {
                if (output != null) RenderTexture.ReleaseTemporary(output);
                if (blurred != null) RenderTexture.ReleaseTemporary(blurred);
                if (scratch != null) RenderTexture.ReleaseTemporary(scratch);
                if (source != null) RenderTexture.ReleaseTemporary(source);
                if (mask != null) RenderTexture.ReleaseTemporary(mask);
            }
        }

        internal RenderTexture CaptureBlurSource(int width, int height)
        {
            RenderTexture surface = EnsurePaintSurface(width, height);
            if (surface == null) return null;
            RenderTexture snapshot = RenderTexture.GetTemporary(surface.descriptor);
            snapshot.filterMode = FilterMode.Bilinear;
            snapshot.wrapMode = TextureWrapMode.Clamp;
            Graphics.Blit(surface, snapshot);
            return snapshot;
        }

        private static void Clear(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = target; GL.Clear(true, true, Color.clear); }
            finally { RenderTexture.active = previous; }
        }
    }
}
