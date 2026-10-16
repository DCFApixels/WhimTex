using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        internal RenderTexture RenderCombinedLayerStyles(Layer group, in LayerRenderContext context)
        {
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            var tiling = group.RenderTransform.tiling;
            bool sampleExtension = context.applyTransform && tiling != TransformTilingMode.Unbounded && tiling != TransformTilingMode.Clip;
            RenderTexture result = null;
            var saved = new List<(Layer layer, LayerTransformCache cache)>();
            void DetachCaches(List<Layer> children)
            {
                foreach (var child in children)
                {
                    saved.Add((child, child.transformCache)); child.transformCache = null;
                    if (child.IsGroup) DetachCaches(child.children);
                }
            }
            try
            {
                if (!context.applyTransform || sampleExtension)
                {
                    DetachCaches(group.children);
                    VisitTransforms(group.children, null, ProjectiveMatrix.Identity, 0);
                }
                result = GetClearRenderTexture(context.width, context.height);
                for (int i = group.children.Count - 1; i >= 0; i--)
                {
                    var child = group.children[i]; if (!child.enabled) continue;
                    var pixels = child.Render(new LayerRenderContext(this, null, context.width, context.height,
                        context.scaleMultiplier, applyFx: false));
                    try { if (pixels != null) BlendInto(ref result, pixels, BlendMode.Normal, 1f, LayerBlendRange.HDR); }
                    finally { if (pixels != null) ProfiledRenderTexture.ReleaseTemporary(pixels); }
                }
                if (sampleExtension)
                    return group.ApplyTransformAndFx(result, context);
                if (context.applyTransform && tiling == TransformTilingMode.Clip)
                {
                    var mask = group.ApplyTransformAndFx(Texture2D.whiteTexture,
                        new LayerRenderContext(this, null, context.width, context.height, context.scaleMultiplier, applyFx: false));
                    try { BlendInto(ref result, mask, (BlendMode)102, 1f, LayerBlendRange.HDR); }
                    finally { ProfiledRenderTexture.ReleaseTemporary(mask); }
                }
                if (context.applyFx)
                    group.ApplyFx(ref result, new LayerRenderContext(this, context.input, context.width, context.height,
                        context.scaleMultiplier, applyTransform: false, transformFxCoordinates: false));
                var output = result; result = null; return output;
            }
            finally
            {
                foreach (var item in saved) item.layer.transformCache = item.cache;
                if (result != null) ProfiledRenderTexture.ReleaseTemporary(result);
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
            }
        }

        internal bool IsBooleanStyleSuppressed(Layer layer)
        {
            RefreshTransformHierarchy();
            for (var parent = layer?.transformCache?.parent; parent != null; parent = parent.transformCache?.parent)
                if (parent.Behaviour is BooleanGroupLayerBehaviour boolean &&
                    (boolean.appearance != BooleanGroupAppearance.Layers || BooleanGroupLayerBehaviour.ReadMode(parent) != VectorBooleanOperation.Combine))
                    return true;
            return false;
        }

        // All group entry points use this before group FX/mapping/outer blending.
        private void RenderGroupGeometry(Layer group, ref RenderTexture content, int width, int height,
            float scale, HashSet<Layer> stack, HashSet<Layer> included = null, bool localAccumulator = true)
        {
            if (group.Behaviour is not BooleanGroupLayerBehaviour)
            {
                CompositeLayers(group.layers, ref content, width, height, scale, stack, included, localAccumulator: localAccumulator);
                return;
            }
            var next = group.Render(new LayerRenderContext(this, null, width, height, scale, applyFx: false));
            if (content != null) ProfiledRenderTexture.ReleaseTemporary(content);
            content = next;
        }

        internal bool IsBooleanOperand(Layer layer)
        {
            RefreshTransformHierarchy();
            return layer?.transformCache?.parent?.Behaviour is BooleanGroupLayerBehaviour;
        }

        internal bool CanInsertInto(Layer layer, List<Layer> destination)
        {
            if (!TryFindParentGroup(destination, out var parent, out _, out _) || parent.Behaviour is not BooleanGroupLayerBehaviour) return true;
            try { BooleanGroupLayerBehaviour.ValidateOperands(new List<Layer> { layer }, new HashSet<Layer>(), 0); return true; }
            catch (System.InvalidOperationException) { return false; }
        }
    }
}
