using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositor
    {
        internal Layer PickLayerAtPixel(int x, int y, float threshold, bool insideGroups, Layer selectedLayer = null)
        {
            if (x < 0 || y < 0 || x >= width || y >= height || layers == null) return null;
            threshold = float.IsNaN(threshold) || float.IsInfinity(threshold) ? .1f : Mathf.Clamp01(threshold);
            HashSet<Layer> enteredGroups = null;
            if (!insideGroups && selectedLayer != null && TryFindLayer(selectedLayer, out var selectedContainer, out _))
            {
                enteredGroups = new HashSet<Layer>();
                if (selectedLayer.IsGroup) enteredGroups.Add(selectedLayer);
                while (TryFindParentGroup(selectedContainer, out Layer parent, out var ancestors, out _))
                {
                    enteredGroups.Add(parent);
                    selectedContainer = ancestors;
                }
            }
            var previousTarget = RenderTexture.active;
            var previousCache = effectCache;
            bool previousErrors = collectingErrors, previousQuality = interactiveEffects;
            Texture2D sample = null;
            using var cache = new EffectRenderCache();
            var coverage = new Dictionary<Layer, float>();
            var stack = new HashSet<Layer>();
            try
            {
                collectingErrors = false;
                interactiveEffects = false;
                effectCache = cache;
                cache.BeginFrame(this);
                sample = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                return Pick(layers, 1f);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                effectCache = previousCache;
                collectingErrors = previousErrors;
                interactiveEffects = previousQuality;
                if (sample != null) DestroyImmediate(sample);
            }

            float Alpha(List<Layer> container, int index)
            {
                Layer layer = container[index];
                if (coverage.TryGetValue(layer, out float value)) return value;
                RenderTexture rendered = RenderClippingSource(container, index, width, height, 1f, stack);
                try
                {
                    value = 0f;
                    if (rendered != null)
                    {
                        RenderTexture.active = rendered;
                        sample.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
                        value = sample.GetPixel(0, 0).a;
                        value = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
                    }
                    coverage[layer] = value;
                    return value;
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                    if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
                }
            }

            Layer Pick(List<Layer> container, float inheritedOpacity)
            {
                if (container == null) return null;
                for (int i = 0; i < container.Count; i++)
                {
                    Layer layer = container[i];
                    if (!IsPickable(layer)) continue;
                    float opacity = inheritedOpacity * Mathf.Clamp01(layer.opacity);
                    if (layer.clippingMask)
                    {
                        int baseIndex = FindClippingBaseIndex(container, i);
                        if (baseIndex < 0 || !IsPickable(container[baseIndex])) continue;
                        opacity *= Alpha(container, baseIndex) * Mathf.Clamp01(container[baseIndex].opacity);
                    }
                    if (!MeetsPickThreshold(opacity, threshold) || !MeetsPickThreshold(Alpha(container, i) * opacity, threshold)) continue;
                    if (layer.IsGroup && (insideGroups || enteredGroups?.Contains(layer) == true))
                    {
                        Layer child = Pick(layer.layers, opacity);
                        if (child != null) return child;
                    }
                    return layer;
                }
                return null;
            }
        }

        private static bool IsPickable(Layer layer) => layer?.Behaviour != null && !(layer.Behaviour is PendingLayerBehaviour) &&
            layer.enabled && layer.opacity > 0f && (layer.IsGroup ? layer.IsPassThrough || layer.EffectiveBlendMode != BlendMode.None : layer.blendMode != BlendMode.None);

        // Half-float render targets can represent 10% as 0.0999756. Keep the threshold inclusive.
        private static bool MeetsPickThreshold(float alpha, float threshold) => alpha > 0f && alpha + .0001f >= threshold;
    }
}
