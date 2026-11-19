using System;
using System.Collections.Generic;
using DCFApixels.WhimTex.TiledPaint;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class DrawingLayerBehaviour
    {
        private static readonly Vector4[] BlurBrushKernel = new Vector4[GaussianKernel.Capacity];

        internal void BlurSegment(Vector2 fromSourceUv, Vector2 toSourceUv, int outputWidth, int outputHeight,
            float size, float hardness, float strength, RenderTexture sample, bool tiled = false, Texture selection = null)
        {
            if (StrokeWritesNothing || strength <= 0 || !TiledCanvasUtility.IsInvertible(Owner.PixelCanvasTransform)) return;
            try { Apply(); }
            catch { CancelStroke(); throw; }

            void Apply()
            {
                EnsureTiledSource(outputWidth, outputHeight);
                EnsureTiledStorage(TiledSourceWidth, TiledSourceHeight);
                regionBefore ??= tiledSurface.AcquireSnapshot();
                pixelUndoLabel ??= "Blur Stroke";
                var transform = Owner.PixelCanvasTransform;
                var matrix = transform.ToMatrix(outputWidth, outputHeight);
                matrix.TryInverse(out var inverse);
                size = Mathf.Max(.0001f, size);
                hardness = Mathf.Clamp01(hardness);
                segmentStamps ??= new List<PaintStamp>(256);
                segmentStamps.Clear();
                float distance = Vector2.Distance(fromSourceUv * new Vector2(outputWidth, outputHeight), toSourceUv * new Vector2(outputWidth, outputHeight));
                if (!float.IsFinite(distance) || distance / Mathf.Max(1f, size * .2f) > 32768)
                    throw new ArgumentException("Blur stroke segment is too long.");
                int count = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(1f, size * .2f)));
                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                var max = -min;
                for (int i = 0; i <= count; i++)
                {
                    var center = transform.Map(Vector2.Lerp(fromSourceUv, toSourceUv, i / (float)count), new Vector2(outputWidth, outputHeight));
                    segmentStamps.Add(new PaintStamp { center = center, size = size, color = Color.white });
                    float padding = PaintBrushRenderer.CanvasPixelPadding(center, inverse, matrix, outputWidth, outputHeight, 1d / outputWidth, 1d / outputHeight);
                    double minW = double.PositiveInfinity, maxW = double.NegativeInfinity;
                    for (int y = -1; y <= 1; y += 2) for (int x = -1; x <= 1; x += 2)
                    {
                        var canvas = center + new Vector2(x * (size * .5f + padding) / outputWidth, y * (size * .5f + padding) / outputHeight);
                        double w = inverse.m20 * canvas.x + inverse.m21 * canvas.y + inverse.m22;
                        minW = Math.Min(minW, w); maxW = Math.Max(maxW, w);
                        var p = inverse.Point(canvas);
                        if (!double.IsFinite(p.x) || !double.IsFinite(p.y) || Math.Abs(p.x * TiledSourceWidth) > 1048576 || Math.Abs(p.y * TiledSourceHeight) > 1048576)
                            throw new InvalidOperationException("Blur tip exceeds the source coordinate budget.");
                        min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                    }
                    if (minW <= 0 && maxW >= 0) throw new InvalidOperationException("Blur tip crosses the layer projection horizon.");
                }
                if (tiled) { min = Vector2.zero; max = Vector2.one; }
                int left = Mathf.FloorToInt(min.x * TiledSourceWidth / 256) * 256;
                int bottom = Mathf.FloorToInt(min.y * TiledSourceHeight / 256) * 256;
                int right = Mathf.CeilToInt(max.x * TiledSourceWidth / 256) * 256;
                int top = Mathf.CeilToInt(max.y * TiledSourceHeight / 256) * 256;
                if ((long)(right - left) * (top - bottom) > 268435456)
                    throw new ArgumentException("Blur stroke covers too many pixels.");

                float pixelScale = Owner.GetPixelScale(), radius = Mathf.Max(pixelScale, size * .5f);
                BlurBrushKernel[0] = new Vector4(Mathf.Max(pixelScale, radius * .15f), .12f, 0, 0);
                BlurBrushKernel[1] = new Vector4(Mathf.Max(2 * pixelScale, radius * .30f), .10f, 0, 0);
                BlurBrushKernel[2] = new Vector4(Mathf.Max(3 * pixelScale, radius * .45f), .08f, 0, 0);
                BlurBrushKernel[3] = new Vector4(Mathf.Max(4 * pixelScale, radius * .60f), .06f, 0, 0);
                BlurBrushKernel[4] = new Vector4(Mathf.Max(5 * pixelScale, radius * .80f), .04f, 0, 0);
                var blur = WhimTexMaterials.GaussianBlur;
                GaussianKernel.Upload(blur, .2f, 5, BlurBrushKernel);
                blur.SetInt("_Edges", 1);
                int hx = Mathf.CeilToInt(BlurBrushKernel[4].x * TiledSourceWidth / outputWidth) + 2;
                int hy = Mathf.CeilToInt(BlurBrushKernel[4].x * TiledSourceHeight / outputHeight) + 2;
                // Non-binary repeated frames retain the native raster grid for exact edge interpolation.
                bool nativeRepeat = tiled && (!Mathf.IsPowerOfTwo(TiledSourceWidth) || !Mathf.IsPowerOfTwo(TiledSourceHeight));
                int block = nativeRepeat ? Math.Max(TiledSourceWidth, TiledSourceHeight) : Math.Max(hx, hy) > 1024 ? 512 : 1024;
                int stripWidth = nativeRepeat ? TiledSourceWidth : Math.Max(TiledSourceWidth, Mathf.NextPowerOfTwo(block + 2 * hx));
                int stripHeight = nativeRepeat ? TiledSourceHeight : Mathf.NextPowerOfTwo(block + 2 * hy);
                int scratchWidth = nativeRepeat ? TiledSourceWidth : Mathf.NextPowerOfTwo(block + 2);
                if (stripWidth > SystemInfo.maxTextureSize || Math.Max(TiledSourceHeight, stripHeight) > SystemInfo.maxTextureSize ||
                    (long)stripWidth * stripHeight * 8 + (long)scratchWidth * Math.Max(TiledSourceHeight, stripHeight) * 8 > (nativeRepeat ? 1073741824L : 536870912))
                    throw new InvalidOperationException("Blur sampling buffers exceed their workspace budget.");
                var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
                RenderTexture mask = null; var maskFrame = Vector2Int.zero;
                using var profile = WhimTexProfiler.Measure("brush.blur.regions", layer: Owner);
                try
                {
                    GL.sRGBWrite = false;
                    // Rasterize coverage on the original canvas grid; shifting a small target changes
                    // interpolated coverage rounding. One float channel suffices (white ink R == A).
                    mask = ProfiledRenderTexture.GetTemporary(outputWidth, outputHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                    mask.filterMode = FilterMode.Bilinear; mask.wrapMode = TextureWrapMode.Clamp;
                    for (int fy = Mathf.FloorToInt(bottom / (float)TiledSourceHeight) * TiledSourceHeight; fy < (tiled ? TiledSourceHeight : top); fy += TiledSourceHeight)
                    for (int fx = Mathf.FloorToInt(left / (float)TiledSourceWidth) * TiledSourceWidth; fx < (tiled ? TiledSourceWidth : right); fx += TiledSourceWidth)
                    {
                        maskFrame = new Vector2Int(fx, fy); Clear(mask);
                        PaintBrushRenderer.Draw(mask, segmentStamps, size, hardness, false, PencilShape.Circle, Color.white,
                            false, outputWidth, outputHeight, patternCenter, tiled, transform, false, false,
                            sourceRegion: new Rect(fx / (float)TiledSourceWidth, fy / (float)TiledSourceHeight, 1, 1));
                        if (PaintBrushRenderer.DrawBounds.width <= 0) continue;
                        int x0 = Math.Max(left, Mathf.FloorToInt(fx / 256f) * 256), y0 = Math.Max(bottom, Mathf.FloorToInt(fy / 256f) * 256);
                        int x1 = Math.Min(right, Mathf.CeilToInt((fx + TiledSourceWidth) / 256f) * 256), y1 = Math.Min(top, Mathf.CeilToInt((fy + TiledSourceHeight) / 256f) * 256);
                        if (nativeRepeat) { x1 = TiledSourceWidth; y1 = TiledSourceHeight; }
                        for (int y = y0; y < y1; y += block) for (int x = x0; x < x1; x += block)
                            BlurRegion(new RectInt(x, y, Math.Min(block, x1 - x), Math.Min(block, y1 - y)));
                    }
                    paintSurfaceDirty = true;
                    InvalidateAlphaCoverage();
                    unchecked { paintSurfaceRevision++; }
                }
                finally { RenderTexture.active = previous; if (mask != null) ProfiledRenderTexture.ReleaseTemporary(mask); blur.SetVector("_RegionFrame", Vector4.zero); RenderTexture.active = previous; GL.sRGBWrite = srgb;
                    var composite = WhimTexMaterials.BlurBrush;
                    foreach (var name in new[] { "_BlurTex", "_MaskTex", "_Before", "_OriginalStraight", "_Selection" }) composite.SetTexture(name, null);
                }

                void BlurRegion(RectInt area)
                {
                    // Binary texel scales preserve the full-frame hardware filtering weights.
                    var padded = new RectInt(area.x - hx, area.y - hy,
                        Math.Max(TiledSourceWidth, Mathf.NextPowerOfTwo(area.width + 2 * hx)), Mathf.NextPowerOfTwo(area.height + 2 * hy));
                    if (tiled) padded = new RectInt(0, padded.y, TiledSourceWidth, Math.Min(TiledSourceHeight, padded.height));
                    if (nativeRepeat) padded = new RectInt(0, 0, TiledSourceWidth, TiledSourceHeight);
                    int horizontalY = tiled ? 0 : padded.y;
                    RenderTexture source = null, scratch = null, current = null, before = null, output = null;
                    try
                    {
                        var uv = new Rect(area.x / (float)TiledSourceWidth, area.y / (float)TiledSourceHeight,
                            area.width / (float)TiledSourceWidth, area.height / (float)TiledSourceHeight);
                        if (!uv.Overlaps(PaintBrushRenderer.DrawBounds)) return;
                        source = HdrUtility.Temporary(padded.width, padded.height);
                        if (sample == null) tiledSurface.CopyRegion(padded, source, regionBefore, tiled);
                        else
                        {
                            var wrap = sample.wrapMode;
                            try
                            {
                                if (tiled) sample.wrapMode = TextureWrapMode.Repeat;
                                Graphics.Blit(sample, source, new Vector2(padded.width / (float)TiledSourceWidth, padded.height / (float)TiledSourceHeight),
                                    new Vector2(padded.x / (float)TiledSourceWidth, padded.y / (float)TiledSourceHeight));
                            }
                            finally { sample.wrapMode = wrap; }
                        }
                        scratch = HdrUtility.Temporary(nativeRepeat ? area.width : Mathf.NextPowerOfTwo(area.width + 2), Math.Max(TiledSourceHeight, padded.height));
                        blur.SetInt("_Edges", nativeRepeat ? 2 : 1);
                        blur.SetVector("_RegionFrame", nativeRepeat ? Vector4.zero : new Vector4(TiledSourceWidth, TiledSourceHeight, tiled ? 1 : 0, 0));
                        blur.SetVector("_RegionInput", new Vector4(padded.x, padded.y, 0, 0));
                        blur.SetVector("_RegionOutput", new Vector4(area.x - 1, horizontalY, scratch.width, scratch.height));
                        blur.SetVector("_Direction", new Vector4(1f / outputWidth, 0, 0, 0));
                        Graphics.Blit(source, scratch, blur, 2);
                        ProfiledRenderTexture.ReleaseTemporary(source); source = null;
                        source = HdrUtility.Temporary(nativeRepeat ? area.width : Mathf.NextPowerOfTwo(area.width + 2), nativeRepeat ? area.height : Mathf.NextPowerOfTwo(area.height + 2));
                        blur.SetVector("_RegionInput", new Vector4(area.x - 1, horizontalY, 0, 0));
                        blur.SetVector("_RegionOutput", new Vector4(area.x - 1, area.y - 1, source.width, source.height));
                        blur.SetVector("_Direction", new Vector4(0, 1f / outputHeight, 0, 0));
                        Graphics.Blit(scratch, source, blur, 2);
                        ProfiledRenderTexture.ReleaseTemporary(scratch); scratch = null;
                        current = HdrUtility.Temporary(area.width, area.height);
                        tiledSurface.CopyRegion(area, current);
                        before = HdrUtility.Temporary(area.width, area.height);
                        tiledSurface.CopyRegion(area, before, regionBefore);
                        output = HdrUtility.Temporary(area.width, area.height);
                        var composite = WhimTexMaterials.BlurBrush;
                        composite.SetTexture("_BlurTex", source); composite.SetTexture("_MaskTex", mask);
                        composite.SetVector("_MaskMap", new Vector4((area.x - maskFrame.x) / (float)TiledSourceWidth, (area.y - maskFrame.y) / (float)TiledSourceHeight, uv.width, uv.height));
                        composite.SetTexture("_Before", before); composite.SetTexture("_OriginalStraight", pixels);
                        composite.SetTexture("_Selection", selection ?? Texture2D.whiteTexture);
                        composite.SetFloat("_UseSelection", selection != null ? 1 : 0);
                        composite.SetFloat("_Wrap", tiled ? 1 : 0);
                        composite.SetFloat("_Decode", !HdrUtility.IsHdr(pixels) && QualitySettings.activeColorSpace == ColorSpace.Gamma ? 1 : 0);
                        composite.SetVector("_Channels", new Vector4((strokeWriteChannels & 1) != 0 ? 1 : 0, (strokeWriteChannels & 2) != 0 ? 1 : 0,
                            (strokeWriteChannels & 4) != 0 ? 1 : 0, (strokeWriteChannels & 8) != 0 ? 1 : 0));
                        composite.SetVector("_Region", new Vector4(uv.x, uv.y, uv.width, uv.height));
                        composite.SetVector("_BlurMap", nativeRepeat ? new Vector4(0, 0, 1, 1) : new Vector4(1f / source.width, 1f / source.height,
                            area.width / (float)source.width, area.height / (float)source.height));
                        // Preserve the final 3x3 clamp after the wrapped Gaussian passes.
                        composite.SetVector("_BlurClamp", tiled && !nativeRepeat ? new Vector4((1.5f - area.x) / source.width, (1.5f - area.y) / source.height,
                            (TiledSourceWidth + .5f - area.x) / source.width, (TiledSourceHeight + .5f - area.y) / source.height) : new Vector4(0, 0, 1, 1));
                        matrix.SetShader(composite, "_CanvasRow");
                        composite.SetFloat("_Strength", Mathf.Clamp01(strength));
                        Graphics.Blit(current, output, composite);
                        var imports = new List<TiledPaintImportTile>();
                        var edges = new List<RenderTexture>();
                        try
                        {
                            for (int ty = 0; ty < area.height; ty += 256) for (int tx = 0; tx < area.width; tx += 256)
                            {
                                var key = new Vector2Int((area.x + tx) / 256, (area.y + ty) / 256);
                                int w = Math.Min(256, area.width - tx), h = Math.Min(256, area.height - ty);
                                var tileOutput = output; var rectangle = new RectInt(tx, ty, 256, 256);
                                if (w < 256 || h < 256)
                                {
                                    tileOutput = HdrUtility.Temporary(256, 256); edges.Add(tileOutput);
                                    tiledSurface.CopyRegion(new RectInt(key * 256, new Vector2Int(256, 256)), tileOutput);
                                    Graphics.CopyTexture(output, 0, 0, tx, ty, w, h, tileOutput, 0, 0, 0, 0);
                                    rectangle = new RectInt(0, 0, 256, 256);
                                }
                                imports.Add(new TiledPaintImportTile(key, tileOutput, rectangle));
                                regionChanged.Add(key);
                            }
                            using var delta = tiledSurface.ImportTiles(imports);
                        }
                        finally { RenderTexture.active = previous; foreach (var edge in edges) ProfiledRenderTexture.ReleaseTemporary(edge); }
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        if (source != null) ProfiledRenderTexture.ReleaseTemporary(source);
                        if (scratch != null) ProfiledRenderTexture.ReleaseTemporary(scratch);
                        if (current != null) ProfiledRenderTexture.ReleaseTemporary(current);
                        if (before != null) ProfiledRenderTexture.ReleaseTemporary(before);
                        if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
                    }
                }
            }
        }


        private static void Clear(RenderTexture target)
        {
            RenderTexture previous = RenderTexture.active;
            try { RenderTexture.active = target; GL.Clear(true, true, Color.clear); }
            finally { RenderTexture.active = previous; }
        }
    }
}
