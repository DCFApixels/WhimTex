using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private HashSet<Layer> morphLocalRoots;
        [NonSerialized] private HashSet<Layer> renderingMorphs;

        internal RenderTexture RenderMorphGroup(MorphGroupLayerBehaviour morph, in LayerRenderContext context)
        {
            var group = morph.Owner;
            renderingMorphs ??= new HashSet<Layer>();
            if (!renderingMorphs.Add(group)) return GetClearRenderTexture(context.width, context.height);
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture from = null, to = null, local = null;
            try
            {
                morph.Sample(EvaluationContext.Time, out var a, out var b, out var pair, out float t);
                if (t > 0 && morph.mode == MorphMode.Vector && MorphGroupLayerBehaviour.CanMorphGeometry(a, b))
                {
                    var geometry = morph.Interpolate(a, b, new Vector2(width, height), pair?.anchors,
                        effectCache?.Stamp(a) ?? 0, effectCache?.Stamp(b) ?? 0, t);
                    if (geometry != null)
                    {
                        try
                        {
                            var shapeContext = new LayerRenderContext(this, null, context.width, context.height,
                                context.scaleMultiplier, context.applyTransform, false);
                            local = geometry.RenderWithOwner(shapeContext, group);
                            ApplyMorphOpacity(ref local, Mathf.Lerp(a.opacity, b.opacity, t));
                            if (context.applyFx) group.ApplyFx(ref local, context);
                            var result = local; local = null;
                            return result;
                        }
                        finally { geometry.ReleaseTransientResources(); }
                    }
                }
                from = RenderMorphEndpoint(group, a, context.width, context.height, context.scaleMultiplier);
                if (t > 0 && b != null)
                {
                    to = RenderMorphEndpoint(group, b, context.width, context.height, context.scaleMultiplier);
                    ulong stampA = effectCache?.Stamp(a) ?? 0, stampB = effectCache?.Stamp(b) ?? 0;
                    var cache = stampA != 0 && stampB != 0 ? (morph.rasterFlow ??= new MorphRasterRenderer.FlowCache()) : null;
                    bool raster = morph.mode == MorphMode.Raster;
                    var edges = raster ? pair?.RasterGuides(from.width, from.height) : null;
                    var matching = raster && !morph.classicLiao ? morph.matching : MorphMatching.Liao;
                    local = MorphRasterRenderer.RenderGuided(raster, from, to, pair?.RasterAnchors(matching == MorphMatching.MARCO, edges, from.width, from.height), t, cache,
                        (a, b, stampA, stampB, context.width, context.height, context.scaleMultiplier),
                        raster ? morph.interpolation : MorphInterpolation.Linear,
                        raster && morph.classicLiao, raster && morph.preserveGeometry,
                        edges, raster && morph.preserveStationaryDetails,
                        !raster || morph.visibilityMask, matching == MorphMatching.ThreeHypotheses,
                        raster ? morph.maskBlend : MorphMaskBlend.Original);
                }
                else { local = from; from = null; }
                return group.ApplyTransformAndFx(local, context);
            }
            finally
            {
                renderingMorphs.Remove(group);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                if (local != null) ProfiledRenderTexture.ReleaseTemporary(local);
                if (to != null) ProfiledRenderTexture.ReleaseTemporary(to);
                if (from != null) ProfiledRenderTexture.ReleaseTemporary(from);
            }
        }

        internal RenderTexture RenderMorphEndpoint(Layer group, Layer child, int w, int h) =>
            RenderMorphEndpoint(group, child, w, h, width / (float)w);

        private RenderTexture RenderMorphEndpoint(Layer group, Layer child, int w, int h, float scale)
        {
            if (child == null) return GetClearRenderTexture(w, h);
            if (IsPreviewEvaluation && effectCache != null)
                return CachedEffectRender(child, "morph-key", w, h, scale, false,
                    () => RenderMorphEndpointUncached(group, child, w, h, scale));
            return RenderMorphEndpointUncached(group, child, w, h, scale);
        }

        private RenderTexture RenderMorphEndpointUncached(Layer group, Layer child, int w, int h, float scale)
        {
            int index = group.children.IndexOf(child);
            if (index < 0) throw new InvalidOperationException("Morph endpoint must be a direct child.");
            RefreshTransformHierarchy();
            bool changeFrame = !group.transformCache.world.Equals(ProjectiveMatrix.Identity);
            List<(Layer layer, LayerTransformCache cache)> saved = null;
            var previousCache = effectCache;
            var previousTarget = RenderTexture.active;
            bool srgb = GL.sRGBWrite, publish = publishingLayerPreview;
            RenderTexture pixels = null;
            try
            {
                if (changeFrame)
                {
                    saved = new List<(Layer, LayerTransformCache)>();
                    Detach(group.children);
                    (morphLocalRoots ??= new HashSet<Layer>()).Add(group);
                    VisitTransforms(group.children, group, ProjectiveMatrix.Identity, 0);
                }
                effectCache = null; publishingLayerPreview = false; GL.sRGBWrite = false;
                var stack = new HashSet<Layer> { group };
                if (child.IsGroup)
                    pixels = RenderGroupEffectInputUncached(child, w, h, scale, stack, preserveColor: true);
                else
                {
                    pixels = RenderStandaloneUncached(group.children, index, w, h, scale, stack);
                    ApplyMorphOpacity(ref pixels, child.opacity);
                }
                var result = pixels ?? GetClearRenderTexture(w, h);
                pixels = null;
                return result;
            }
            finally
            {
                if (pixels != null) ProfiledRenderTexture.ReleaseTemporary(pixels);
                if (saved != null)
                {
                    morphLocalRoots.Remove(group);
                    foreach (var item in saved) item.layer.transformCache = item.cache;
                }
                effectCache = previousCache; publishingLayerPreview = publish;
                RenderTexture.active = previousTarget; GL.sRGBWrite = srgb;
            }

            void Detach(List<Layer> children)
            {
                foreach (var layer in children)
                {
                    if (layer == null) continue;
                    saved.Add((layer, layer.transformCache));
                    layer.transformCache = null;
                    if (layer.IsGroup) Detach(layer.children);
                }
            }
        }

        private void ApplyMorphOpacity(ref RenderTexture pixels, float opacity)
        {
            if (pixels == null || opacity >= 1f) return;
            var scaled = GetClearRenderTexture(pixels.width, pixels.height);
            try
            {
                BlendInto(ref scaled, pixels, BlendMode.Normal, opacity, LayerBlendRange.HDR);
                ProfiledRenderTexture.ReleaseTemporary(pixels);
                pixels = scaled; scaled = null;
            }
            finally { if (scaled != null) ProfiledRenderTexture.ReleaseTemporary(scaled); }
        }
    }
}
