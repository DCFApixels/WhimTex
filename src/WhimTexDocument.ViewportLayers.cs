using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private Layer viewportFxLimitLayer;
        [NonSerialized] private int viewportFxLimitCount;

        internal int ViewportFxLimit(Layer layer) => layer == viewportFxLimitLayer ? viewportFxLimitCount : int.MaxValue;

        // -1: generate and process the region. Otherwise render the complete source
        // through the last spatial FX, then run the remaining local FX on the region.
        // Texture-layer inputs recurse through the same decision independently.
        private int ViewportFullPrefix(Layer layer, bool applyFx = true)
        {
            int prefix = CanGenerateViewport(layer) ? -1 : 0;
            if (applyFx && layer.fx != null)
                for (int i = 0; i < layer.fx.Count; i++)
                {
                    var fx = layer.fx[i];
                    if (fx != null && fx.Active && !fx.IsUnavailable &&
                        !fx.IsPixelLocal && !fx.SupportsRegionalColorInput) prefix = i + 1;
                }
            return prefix;
        }

        private bool CanGenerateViewport(Layer layer)
        {
            var tiling = layer.EffectiveTiling;
            switch (layer.Behaviour)
            {
                // Graph UV math and texture reads require the complete logical image.
                case ShaderLayerBehaviour: return false;
                case ShaderProcessorLayerBehaviour: return CanGenerateRegionalProcessor(layer);
                case GroupLayerBehaviour: return true;
                case BooleanGroupLayerBehaviour:
                case VectorLayerBehaviour:
                    return tiling == TransformTilingMode.Unbounded || tiling == TransformTilingMode.Clip;
                case GradientLayerBehaviour gradient:
                    return gradient.gradientType != GradientLayerBehaviour.GradientType.Mesh && tiling == TransformTilingMode.Unbounded;
                case NoiseLayerBehaviour: return tiling == TransformTilingMode.Unbounded;
                case ColorFillLayerBehaviour fill when fill.mode == ColorFillLayerBehaviour.FillMode.Pattern:
                    return (fill.pattern == null || !fill.pattern.seamless) && tiling == TransformTilingMode.Unbounded;
                case ColorFillLayerBehaviour fill when fill.mode == ColorFillLayerBehaviour.FillMode.UV:
                    return tiling == TransformTilingMode.Unbounded;
                case DrawingLayerBehaviour:
                case FileLayerBehaviour:
                case TextLayerBehaviour:
                case ShapeLayerBehaviour:
                case ColorFillLayerBehaviour:
                case PendingLayerBehaviour: return true;
                // Input processors may transform/sample pixels outside the viewport.
                default: return false;
            }
        }

        private RenderTexture RenderViewportPrefix(Layer layer, int w, int h, float scale, int prefix,
            bool applyFx, bool transformFx, Func<int, int, RenderTexture> renderFull)
        {
            bool oldViewport = renderingViewport, oldDiagnostics = collectingErrors;
            Layer oldLimitLayer = viewportFxLimitLayer;
            int oldLimitCount = viewportFxLimitCount;
            RenderTexture oldActive = RenderTexture.active, full = null, region = null;
            RenderTexture parentErrors = numericErrors, fullErrors = null;
            bool ownsFullErrors = false;
            bool oldSrgb = GL.sRGBWrite;
            try
            {
                int fullWidth = Mathf.Max(1, Mathf.CeilToInt(width / scale));
                int fullHeight = Mathf.Max(1, Mathf.CeilToInt(height / scale));
                GL.sRGBWrite = false;
                try
                {
                    renderingViewport = false;
                    if (oldDiagnostics)
                    {
                        numericErrors = ErrorTarget(fullWidth, fullHeight);
                        ownsFullErrors = true;
                        RenderTexture.active = numericErrors;
                        GL.Clear(false, true, Color.clear);
                        RenderTexture.active = oldActive;
                    }
                    viewportFxLimitLayer = layer; viewportFxLimitCount = prefix;
                    full = CachedEffectRender(layer, "viewport-source", fullWidth, fullHeight, scale, false,
                        () => renderFull(fullWidth, fullHeight));
                }
                finally
                {
                    if (ownsFullErrors) { fullErrors = numericErrors; numericErrors = parentErrors; }
                    renderingViewport = oldViewport; collectingErrors = oldDiagnostics;
                    viewportFxLimitLayer = oldLimitLayer; viewportFxLimitCount = oldLimitCount;
                }
                if (full == null) return null;
                if (fullErrors != null)
                {
                    var errors = ErrorTarget(w, h);
                    try
                    {
                        Graphics.Blit(fullErrors, errors,
                            new Vector2((float)viewportToCanvas.m00, (float)viewportToCanvas.m11),
                            new Vector2((float)viewportToCanvas.m02, (float)viewportToCanvas.m12));
                        MergeEffectErrors(errors);
                    }
                    finally { ProfiledRenderTexture.ReleaseTemporary(errors); }
                }
                region = HdrUtility.Temporary(w, h);
                region.filterMode = full.filterMode; region.wrapMode = TextureWrapMode.Clamp;
                GL.sRGBWrite = false;
                using (var profile = WhimTexProfiler.Measure("render.viewport_crop", this, layer))
                using (profile.Gpu()) Graphics.Blit(full, region,
                    new Vector2((float)viewportToCanvas.m00, (float)viewportToCanvas.m11),
                    new Vector2((float)viewportToCanvas.m02, (float)viewportToCanvas.m12));
                layer.ApplyFx(ref region, new LayerRenderContext(this, null, w, h, scale, false, applyFx, transformFx),
                    region.filterMode, start: prefix);
                var result = region; region = null; return result;
            }
            finally
            {
                if (full != null) ProfiledRenderTexture.ReleaseTemporary(full);
                if (fullErrors != null) ProfiledRenderTexture.ReleaseTemporary(fullErrors);
                if (region != null) ProfiledRenderTexture.ReleaseTemporary(region);
                RenderTexture.active = oldActive; GL.sRGBWrite = oldSrgb;
            }
        }

        // Shared by compositing, clipping and group input generation. Outer opacity,
        // mapping and clipping remain at their original call sites, exactly once.
        private RenderTexture RenderGroupPixels(Layer group, int w, int h, float scale,
            HashSet<Layer> stack, HashSet<Layer> included = null)
        {
            int prefix = renderingViewport ? ViewportFullPrefix(group) : -1;
            if (prefix >= 0)
                return RenderViewportPrefix(group, w, h, scale, prefix, true, false,
                    (fw, fh) => RenderGroupPixels(group, fw, fh, scale, stack, included));
            RenderTexture result = GetClearRenderTexture(w, h);
            try
            {
                RenderGroupGeometry(group, ref result, w, h, scale, stack, included);
                group.ApplyFx(ref result, new LayerRenderContext(this, null, w, h, scale, false, true));
                return result;
            }
            catch { if (result != null) ProfiledRenderTexture.ReleaseTemporary(result); throw; }
        }
    }
}
