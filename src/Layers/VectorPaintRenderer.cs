using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static class VectorPaintRenderer
    {
        internal static void Render(VectorLayerBehaviour model, List<VectorRasterizer.Coverage> coverage,
            ProjectiveMatrix matrix, RenderTexture target)
        {
            var material = WhimTexMaterials.VectorPaint;
            RenderTexture previous = RenderTexture.active, accumulation = null;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                if (coverage.Count == 0 || !matrix.TryInverse(out var inverse))
                { RenderTexture.active = target; GL.Clear(false, true, Color.clear); return; }
                bool single = coverage.Count == 1;
                if (!single)
                {
                    accumulation = ProfiledRenderTexture.GetTemporary(target.width, target.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                    accumulation.filterMode = FilterMode.Point;
                    RenderTexture.active = accumulation; GL.Clear(false, true, Color.clear);
                }
                material.SetVector("_TargetSize", new Vector4(target.width, target.height, model.referenceSize.x, model.referenceSize.y));
                material.SetVector("_LocalRow0", new Vector4((float)inverse.m00, (float)inverse.m01, (float)inverse.m02, 0));
                material.SetVector("_LocalRow1", new Vector4((float)inverse.m10, (float)inverse.m11, (float)inverse.m12, 0));
                material.SetVector("_LocalRow2", new Vector4((float)inverse.m20, (float)inverse.m21, (float)inverse.m22, 0));
                foreach (var entry in coverage)
                {
                    var shape = model.shapes[entry.shapeIndex];
                    material.SetVector("_CoverageRect", new Vector4(entry.x, entry.y, entry.texture.width, entry.texture.height));
                    material.SetVector("_FillColor", HdrUtility.Decode(shape.fillColor));
                    material.SetVector("_StrokeColor", HdrUtility.Decode(shape.strokeColor));
                    material.SetInt("_FillMode", (int)shape.fillMode); material.SetInt("_StrokeMode", (int)shape.strokeMode);
                    material.SetVector("_FillAnchors", new Vector4(shape.gradientStart.x, shape.gradientStart.y, shape.gradientEnd.x, shape.gradientEnd.y));
                    material.SetVector("_StrokeAnchors", new Vector4(shape.strokeGradientStart.x, shape.strokeGradientStart.y, shape.strokeGradientEnd.x, shape.strokeGradientEnd.y));
                    material.SetVector("_Repetitions", new Vector4(shape.gradientRepetitions, shape.strokeGradientRepetitions, 0, 0));
                    if (shape.fillMode == VectorFillMode.Freeform) shape.freeform.Bind(material);
                    if (shape.strokeMode == VectorFillMode.Freeform) shape.strokeFreeform.Bind(material, stroke: true);
                    if (FigureFill.IsPaletteGradient(shape.fillMode)) entry.fillPalette.Bind(material, shape.gradient);
                    if (FigureFill.IsPaletteGradient(shape.strokeMode)) entry.strokePalette.Bind(material, shape.strokeGradient, true);
                    Graphics.Blit(entry.texture, single ? target : accumulation, material, single ? 2 : 0);
                }
                if (!single) Graphics.Blit(accumulation, target, material, 1);
            }
            finally
            {
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (accumulation != null) ProfiledRenderTexture.ReleaseTemporary(accumulation);
            }
        }
    }
}
