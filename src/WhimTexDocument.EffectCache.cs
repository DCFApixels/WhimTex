using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private EffectRenderCache effectCache;
        [NonSerialized] private bool interactiveEffects;
        internal bool InteractiveEffects => interactiveEffects;

        [NonSerialized] private bool renderingViewport;
        [NonSerialized] private ProjectiveMatrix viewportToCanvas, canvasToViewport;
        internal bool RenderingViewport => renderingViewport;
        internal ProjectiveMatrix ViewportToCanvas => viewportToCanvas;
        internal ProjectiveMatrix CanvasToViewport => canvasToViewport;

        internal bool SupportsViewportRendering() => true;

        internal RenderTexture RenderCanvasViewport(CanvasRenderRegion region, EffectRenderCache cache,
            bool interactive, DrawingLayerBehaviour painting)
        {
            if (!SupportsViewportRendering()) throw new InvalidOperationException("This document requires full-frame rendering.");
            using var profile = WhimTexProfiler.Measure("document.canvas_viewport", this);
            var oldCache = effectCache;
            bool oldInteractive = interactiveEffects, oldPublishing = publishingLayerPreview, oldViewport = renderingViewport;
            var oldToCanvas = viewportToCanvas; var oldToViewport = canvasToViewport;
            try
            {
                effectCache = cache; interactiveEffects = interactive; publishingLayerPreview = false;
                cache.BeginFrame(this, painting);
                // Region output must never masquerade as a complete layer thumbnail or alpha census.
                lastLayerPreviewCache = null;
                renderingViewport = true;
                viewportToCanvas = ProjectiveMatrix.Translate((double)region.pixels.x / width, (double)region.pixels.y / height) *
                    ProjectiveMatrix.Scale((double)region.pixels.width / width, (double)region.pixels.height / height);
                viewportToCanvas.TryInverse(out canvasToViewport);
                return RenderCanvasCore(region.width, region.height, region.step);
            }
            finally
            {
                effectCache = oldCache; interactiveEffects = oldInteractive; publishingLayerPreview = oldPublishing;
                renderingViewport = oldViewport; viewportToCanvas = oldToCanvas; canvasToViewport = oldToViewport;
            }
        }

        internal RenderTexture RenderQuilting(MakeSeamlessLayerBehaviour layer, in LayerRenderContext context,
            float band, float feather, Vector4 channels)
        {
            ulong stamp=effectCache?.QuiltingStamp(layer,this) ?? 0;
            string key=layer.Id+"/quilting";
            var previous=RenderTexture.active;
            bool srgb=GL.sRGBWrite;
            try
            {
                GL.sRGBWrite=false;
                if(stamp!=0 && effectCache.TryGet(key,stamp,context.width,context.height,context.scaleMultiplier,
                    interactiveEffects,true,out var cached,out _,out _))
                {
                    var copy=ProfiledRenderTexture.GetTemporary(context.width,context.height,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                    copy.filterMode=FilterMode.Bilinear; copy.wrapMode=TextureWrapMode.Clamp;
                    try
                    {
                        using var profile = WhimTexProfiler.Measure("cache.restore", this, layer.Owner, detail: "quilting");
                        using (profile.Gpu()) Graphics.Blit(cached,copy);
                        return copy;
                    }
                    catch { ProfiledRenderTexture.ReleaseTemporary(copy); throw; }
                }
                float contrast=layer.quiltingContrastCompensation && float.IsFinite(layer.quiltingContrast)
                    ? Mathf.Clamp01(layer.quiltingContrast) : 0;
                var result=PatchQuiltingSeamless.RenderShifted(context.input,context.width,context.height,
                    layer.quiltingEdges,band,feather,layer.quiltingQuality,layer.quiltingSeed,layer.quiltingChannels,channels,contrast,layer.quiltingAlongSearch);
                try
                {
                    if(stamp!=0) effectCache.StoreFullPrecision(key,stamp,context.scaleMultiplier,interactiveEffects,result);
                    return result;
                }
                catch { ProfiledRenderTexture.ReleaseTemporary(result); throw; }
            }
            finally { RenderTexture.active=previous; GL.sRGBWrite=srgb; }
        }

        internal RenderTexture RenderCanvasWithCache(int maxSize, EffectRenderCache cache, bool interactive, DrawingLayerBehaviour painting)
        {
            using var profile = WhimTexProfiler.Measure("document.canvas_update", this,
                detail: interactive ? "interactive" : "settled");
            var previous = effectCache;
            bool previousQuality = interactiveEffects;
            bool previousPublishing = publishingLayerPreview;
            try
            {
                effectCache = cache;
                interactiveEffects = interactive;
                publishingLayerPreview = true;
                cache.BeginFrame(this, painting);
                var result = RenderCanvas(maxSize);
                lastLayerPreviewCache = cache;
                lastLayerPreviewSize = maxSize;
                lastLayerPreviewInteractive = interactive;
                lastLayerPreviewPainting = painting;
                return result;
            }
            finally { effectCache = previous; interactiveEffects = previousQuality; publishingLayerPreview = previousPublishing; }
        }

        private RenderTexture CachedEffectRender(Layer layer, string kind, int w, int h, float scale,
            bool alphaOnly, Func<RenderTexture> render)
        {
            ulong stamp = effectCache?.Stamp(layer) ?? 0;
            if (stamp == 0)
            {
                WhimTexProfiler.Event("effect.cache", "cache", "bypass: missing cache or uncacheable dependency");
                WhimTexProfiler.Reason("cache.bypass", effectCache == null ? "cache.not_attached" : "dependency.uncacheable", this, layer);
                return render();
            }
            // A region and a complete image must never share cache entries. Pan/zoom
            // also changes pixels even when document parameters remain identical.
            if (renderingViewport)
            {
                stamp = RenderFingerprint.Double(stamp, viewportToCanvas.m00);
                stamp = RenderFingerprint.Double(stamp, viewportToCanvas.m11);
                stamp = RenderFingerprint.Double(stamp, viewportToCanvas.m02);
                stamp = RenderFingerprint.Double(stamp, viewportToCanvas.m12);
            }
            string key = layer.Id + "/" + kind + (renderingViewport ? "/viewport" : "") +
                (viewportFxLimitLayer != null ? "/prefix/" + viewportFxLimitLayer.Id + "/" + viewportFxLimitCount : "") +
                (collectingErrors ? "/debug" : "");
            bool cacheHit = effectCache.TryGet(key, stamp, w, h, scale, interactiveEffects, !alphaOnly,
                out var cached, out var errors, out bool packedAlpha);
            // Interactive rendering is only an approximation when the dependency
            // changed. If the exact settled result is still valid, reuse it instead
            // of replacing it with a lower-quality Sharpen/blur pass.
            if (!cacheHit && interactiveEffects)
                cacheHit = effectCache.TryGet(key, stamp, w, h, scale, false, !alphaOnly,
                    out cached, out errors, out packedAlpha);
            if (cacheHit)
            {
                MergeEffectErrors(errors);
                var copy = HdrUtility.Temporary(w, h);
                copy.filterMode = cached.filterMode;
                try
                {
                    using var profile = WhimTexProfiler.Measure("cache.restore", this, layer, detail: kind);
                    using var gpu = profile.Gpu();
                    if (alphaOnly)
                    {
                        WhimTexMaterials.EffectCache.SetFloat("_PackedAlpha", packedAlpha ? 1f : 0f);
                        Graphics.Blit(cached, copy, WhimTexMaterials.EffectCache, 1);
                    }
                    else Graphics.Blit(cached, copy);
                    return copy;
                }
                catch { ProfiledRenderTexture.ReleaseTemporary(copy); throw; }
            }
            RenderTexture parentErrors = numericErrors;
            RenderTexture localErrors = collectingErrors ? ErrorTarget(w, h) : null;
            RenderTexture output = null;
            if (localErrors != null)
            {
                var previous = RenderTexture.active;
                RenderTexture.active = localErrors;
                using (var profile = WhimTexProfiler.Measure("render.diagnostic_clear", this, layer))
                using (profile.Gpu()) GL.Clear(false, true, Color.clear);
                RenderTexture.active = previous;
                numericErrors = localErrors;
            }
            try
            {
                output = render();
                effectCache.Store(key, stamp, scale, interactiveEffects, alphaOnly, output,
                    localErrors != null ? numericErrors : null);
                var result = output; output = null; return result;
            }
            finally
            {
                if (output != null) ProfiledRenderTexture.ReleaseTemporary(output);
                if (localErrors != null)
                {
                    localErrors = numericErrors;
                    numericErrors = parentErrors;
                    try { MergeEffectErrors(localErrors); }
                    finally { ProfiledRenderTexture.ReleaseTemporary(localErrors); }
                }
            }
        }

        private void MergeEffectErrors(Texture errors)
        {
            if (!collectingErrors || numericErrors == null || errors == null) return;
            var merged = ErrorTarget(numericErrors.width, numericErrors.height);
            try
            {
                WhimTexMaterials.EffectCache.SetTexture("_Errors", numericErrors);
                using var profile = WhimTexProfiler.Measure("render.diagnostic_merge", this);
                using (profile.Gpu()) Graphics.Blit(errors, merged, WhimTexMaterials.EffectCache, 2);
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(merged); throw; }
            ProfiledRenderTexture.ReleaseTemporary(numericErrors);
            numericErrors = merged;
        }

        private RenderTexture RenderStandalone(List<Layer> container, int index, int outputWidth, int outputHeight,
            float scaleMultiplier, HashSet<Layer> renderStack, bool applyTransform = true, bool applyFx = true,
            bool includeDisabled = false, bool applyClipping = true, RenderTexture accumulatedInput = null)
        {
            if (container == null || index < 0 || index >= container.Count) return null;
            Layer layer = container[index];
            if (layer?.Behaviour == null || !includeDisabled && !layer.enabled || renderStack != null && renderStack.Contains(layer)) return null;
            using var profile = WhimTexProfiler.Measure("layer.render", this, layer, detail: layer.layerName);
            if ((layer?.Behaviour is TargetedLayerBehaviour || EffectRenderCache.CanCacheLayer(layer)) &&
                applyTransform && applyFx && applyClipping)
            {
                var rendered = CachedEffectRender(layer, "effect", outputWidth, outputHeight, scaleMultiplier, false,
                    () => RenderStandaloneUncached(container, index, outputWidth, outputHeight, scaleMultiplier,
                        renderStack, applyTransform, applyFx, includeDisabled, applyClipping, accumulatedInput: accumulatedInput));
                // Cached output includes clipping; only an unclipped layer is interchangeable
                // with the intrinsic pre-clipping content consumed by the empty badge.
                if (!layer.clippingMask) OfferLayerContent(layer, rendered, applyTransform, scaleMultiplier);
                return rendered;
            }
            return RenderStandaloneUncached(container, index, outputWidth, outputHeight, scaleMultiplier,
                renderStack, applyTransform, applyFx, includeDisabled, applyClipping, accumulatedInput: accumulatedInput);
        }

        private RenderTexture RenderGroupEffectInput(Layer group, int outputWidth, int outputHeight,
            float scaleMultiplier, HashSet<Layer> renderStack, bool preserveColor, bool includeDisabled = false)
        {
            if (group?.Behaviour == null || !includeDisabled && !group.enabled || renderStack != null && renderStack.Contains(group)) return null;
            using var profile = WhimTexProfiler.Measure("layer.group_input", this, group, detail: group.layerName);
            bool color = preserveColor || effectCache != null && effectCache.NeedsColor(group);
            RenderTexture result = CachedEffectRender(group, "group", outputWidth, outputHeight, scaleMultiplier, !color,
                () => RenderGroupEffectInputUncached(group, outputWidth, outputHeight, scaleMultiplier, renderStack,
                    color, includeDisabled));
            if (result == null || preserveColor || !color) return result;
            // Preserve the established grayscale coverage used by Outline/SDF, even after RGBA promotion.
            var mask = HdrUtility.Temporary(outputWidth, outputHeight);
            try
            {
                WhimTexMaterials.EffectCache.SetFloat("_PackedAlpha", 0f);
                using var maskProfile = WhimTexProfiler.Measure("render.group_mask", this, group);
                using (maskProfile.Gpu()) Graphics.Blit(result, mask, WhimTexMaterials.EffectCache, 1);
                return mask;
            }
            catch { ProfiledRenderTexture.ReleaseTemporary(mask); throw; }
            finally { ProfiledRenderTexture.ReleaseTemporary(result); }
        }
    }
}
