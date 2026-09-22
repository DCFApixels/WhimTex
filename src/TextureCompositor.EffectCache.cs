using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [NonSerialized] private EffectRenderCache effectCache;
        [NonSerialized] private bool interactiveEffects;
        internal bool InteractiveEffects => interactiveEffects;

        internal RenderTexture RenderCachedPreview(int maxSize, EffectRenderCache cache, bool interactive, DrawingLayerBehaviour painting)
        {
            var previous = effectCache;
            bool previousQuality = interactiveEffects;
            try
            {
                effectCache = cache;
                interactiveEffects = interactive;
                cache.BeginFrame(this, painting);
                return RenderPreview(maxSize);
            }
            finally { effectCache = previous; interactiveEffects = previousQuality; }
        }

        private RenderTexture CachedEffectRender(Layer layer, string kind, int w, int h, float scale,
            bool alphaOnly, Func<RenderTexture> render)
        {
            ulong stamp = effectCache?.Stamp(layer) ?? 0;
            if (stamp == 0) return render();
            // Sharpen has an expensive Gaussian preparation path. Keep its cache
            // namespace explicit so it cannot collide with other targeted effects
            // and so cache diagnostics can distinguish a Sharpen hit from a generic FX.
            string cacheKind = layer.Behaviour is SharpenLayerBehaviour ? "sharpen" : kind;
            string key = layer.Id + "/" + cacheKind + (collectingErrors ? "/debug" : "");
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
                    if (alphaOnly)
                    {
                        WhimTexMaterials.EffectCache.SetFloat("_PackedAlpha", packedAlpha ? 1f : 0f);
                        Graphics.Blit(cached, copy, WhimTexMaterials.EffectCache, 1);
                    }
                    else Graphics.Blit(cached, copy);
                    return copy;
                }
                catch { RenderTexture.ReleaseTemporary(copy); throw; }
            }
            RenderTexture parentErrors = numericErrors;
            RenderTexture localErrors = collectingErrors ? ErrorTarget(w, h) : null;
            RenderTexture output = null;
            if (localErrors != null)
            {
                var previous = RenderTexture.active;
                RenderTexture.active = localErrors;
                GL.Clear(false, true, Color.clear);
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
                if (output != null) RenderTexture.ReleaseTemporary(output);
                if (localErrors != null)
                {
                    localErrors = numericErrors;
                    numericErrors = parentErrors;
                    try { MergeEffectErrors(localErrors); }
                    finally { RenderTexture.ReleaseTemporary(localErrors); }
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
                Graphics.Blit(errors, merged, WhimTexMaterials.EffectCache, 2);
            }
            catch { RenderTexture.ReleaseTemporary(merged); throw; }
            RenderTexture.ReleaseTemporary(numericErrors);
            numericErrors = merged;
        }

        private RenderTexture RenderStandalone(List<Layer> container, int index, int outputWidth, int outputHeight,
            float scaleMultiplier, HashSet<Layer> renderStack, bool applyTransform = true, bool applyModifiers = true,
            bool includeDisabled = false, bool applyClipping = true)
        {
            if (container == null || index < 0 || index >= container.Count) return null;
            Layer layer = container[index];
            if (layer?.Behaviour == null || !includeDisabled && !layer.enabled || renderStack != null && renderStack.Contains(layer)) return null;
            if (layer?.Behaviour is TargetedLayerBehaviour && applyTransform && applyModifiers && applyClipping)
                return CachedEffectRender(layer, "effect", outputWidth, outputHeight, scaleMultiplier, false,
                    () => RenderStandaloneUncached(container, index, outputWidth, outputHeight, scaleMultiplier,
                        renderStack, applyTransform, applyModifiers, includeDisabled, applyClipping));
            return RenderStandaloneUncached(container, index, outputWidth, outputHeight, scaleMultiplier,
                renderStack, applyTransform, applyModifiers, includeDisabled, applyClipping);
        }

        private RenderTexture RenderGroupEffectInput(Layer group, int outputWidth, int outputHeight,
            float scaleMultiplier, HashSet<Layer> renderStack, bool preserveColor, bool includeDisabled = false)
        {
            if (group?.Behaviour == null || !includeDisabled && !group.enabled || renderStack != null && renderStack.Contains(group)) return null;
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
                Graphics.Blit(result, mask, WhimTexMaterials.EffectCache, 1);
                return mask;
            }
            catch { RenderTexture.ReleaseTemporary(mask); throw; }
            finally { RenderTexture.ReleaseTemporary(result); }
        }
    }
}
