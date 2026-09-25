using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        // Lists are top-to-bottom. Hidden bases still own their chains.
        // Never cross a group boundary or recursively mask through other clipped siblings.
        internal static int FindClippingBaseIndex(List<Layer> container, int index)
        {
            if (container == null || index < 0 || index >= container.Count ||
                container[index] == null || container[index].IsClippingBarrier || !container[index].clippingMask) return -1;
            for (int i = index + 1; i < container.Count; i++)
            {
                if (container[i]?.Behaviour is PendingLayerBehaviour) continue;
                if (container[i] == null || container[i].IsClippingBarrier) return -1;
                if (!container[i].clippingMask) return i;
            }
            return -1;
        }

        public Layer GetClippingBase(Layer layer)
        {
            if (layer == null || !layer.clippingMask) return null;
            if (!TryFindLayer(layer, out var container, out int index)) return null;
            int baseIndex = FindClippingBaseIndex(container, index);
            return baseIndex < 0 ? null : container[baseIndex];
        }

        internal bool IsGroupIsolatedByClipping(Layer group)
        {
            if (group.clippingMask) return true;
            if (!TryFindLayer(group, out var container, out int index)) return false;
            do { index--; } while (index >= 0 && container[index]?.Behaviour is PendingLayerBehaviour);
            return index >= 0 && container[index] != null && container[index].clippingMask;
        }

        private static bool IsClippingSourceVisible(Layer layer) => layer?.Behaviour != null && layer.enabled && layer.opacity > 0f &&
            (layer?.AsGroup() is Layer group ? group.EffectiveBlendMode != BlendMode.None : layer.blendMode != BlendMode.None);

        // Source color before its outer opacity, blend mode and clipping coverage.
        // Groups are isolated here, including those configured as Pass Through.
        private RenderTexture RenderClippingSource(List<Layer> container, int index, int w, int h,
            float scale, HashSet<Layer> stack, HashSet<Layer> included = null)
        {
            if (!(container[index]?.AsGroup() is Layer group))
                return RenderStandalone(container, index, w, h, scale, stack, applyClipping: false);
            stack ??= new HashSet<Layer>();
            if (!stack.Add(group)) return null;
            RenderTexture content = null;
            try
            {
                content = GetClearRenderTexture(w, h);
                CompositeLayers(group.layers, ref content, w, h, scale, stack, included);
                group.ApplyModifiers(ref content, new LayerRenderContext(this, null, w, h, scale, false, true));
                content = FinishStage(content, group.colorRange == LayerColorRange.Standard, group.swizzle);
                RenderTexture result = content;
                content = null;
                return result;
            }
            finally
            {
                if (content != null) RenderTexture.ReleaseTemporary(content);
                stack.Remove(group);
            }
        }

        private void CompositeClippingChain(List<Layer> container, int baseIndex, int topIndex,
            ref RenderTexture accumulator, int w, int h, float scale, HashSet<Layer> stack, HashSet<Layer> included)
        {
            Layer basis = container[baseIndex];
            if (!IsClippingSourceVisible(basis)) return;
            bool includeBase = included == null || included.Contains(basis);
            bool any = includeBase;
            for (int i = topIndex; !any && i < baseIndex; i++) any = included.Contains(container[i]);
            if (!any) return;
            RenderTexture basePixels = null;
            RenderTexture chain = null;
            try
            {
                basePixels = RenderClippingSource(container, baseIndex, w, h, scale, stack, includeBase ? included : null);
                if (basePixels == null) return;
                if (includeBase) { chain = basePixels; basePixels = null; }
                else chain = GetClearRenderTexture(w, h);
                for (int i = baseIndex - 1; i >= topIndex; i--)
                {
                    Layer layer = container[i];
                    if (!IsClippingSourceVisible(layer) || (included != null && !included.Contains(layer))) continue;
                    RenderTexture source = RenderClippingSource(container, i, w, h, scale, stack, included);
                    if (source == null) continue;
                    try
                    {
                        BlendMode mode = layer?.AsGroup() is Layer group ? group.EffectiveBlendMode : layer.blendMode;
                        if (!includeBase && mode == BlendMode.Overwrite) mode = BlendMode.Normal;
                        BlendInto(ref chain, source, mode, layer.opacity, layer.blendRange, preserveAlpha: includeBase);
                    }
                    finally { RenderTexture.ReleaseTemporary(source); }
                }
                // Partial merges use the unselected base only as a mask. Apply its
                // coverage once, not once per selected clipping layer.
                if (!includeBase) BlendInto(ref chain, basePixels, (BlendMode)102, 1f);
                BlendMode baseMode = basis?.AsGroup() is Layer baseGroup ? baseGroup.EffectiveBlendMode : basis.blendMode;
                BlendInto(ref accumulator, chain, includeBase ? baseMode : BlendMode.Normal, basis.opacity, basis.blendRange);
            }
            finally
            {
                if (chain != null) RenderTexture.ReleaseTemporary(chain);
                if (basePixels != null) RenderTexture.ReleaseTemporary(basePixels);
            }
        }

        private void ApplyClippingCoverage(ref RenderTexture pixels, List<Layer> container, int index,
            int w, int h, float scale, HashSet<Layer> stack)
        {
            int baseIndex = FindClippingBaseIndex(container, index);
            RenderTexture mask = null;
            try
            {
                if (baseIndex >= 0 && IsClippingSourceVisible(container[baseIndex]))
                    mask = RenderClippingSource(container, baseIndex, w, h, scale, stack);
                if (mask == null) mask = GetClearRenderTexture(w, h);
                BlendInto(ref pixels, mask, (BlendMode)102, baseIndex < 0 ? 0f : container[baseIndex].opacity);
            }
            finally { if (mask != null) RenderTexture.ReleaseTemporary(mask); }
        }
    }
}
