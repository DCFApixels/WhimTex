using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [NonSerialized] private LayerThumbnailCache layerThumbnails;

        internal Texture2D GetLayerThumbnail(Layer layer, int size, bool deferUpdates = false)
        {
            if (layer?.Behaviour is TargetedLayerBehaviour || layer?.Behaviour is ShaderProcessorLayerBehaviour)
            {
                layerThumbnails ??= new LayerThumbnailCache(this);
                return layerThumbnails.Get(layer, size, deferUpdates);
            }
            return layer?.GetPreviewTexture(size);
        }

        internal void ReleaseLayerThumbnails()
        {
            layerThumbnails?.Dispose();
            layerThumbnails = null;
        }

        internal void RefreshThumbnailStructure() => layerThumbnails?.RefreshStructure();

        internal RenderTexture RenderThumbnailLayer(Layer layer, int maxSize, EffectRenderCache cache)
        {
            if (!TryFindLayer(layer, out var container, out int index)) return null;
            var previousTarget = RenderTexture.active;
            var previousCache = effectCache;
            bool previousErrors = collectingErrors, previousQuality = interactiveEffects;
            try
            {
                collectingErrors = false;
                interactiveEffects = false;
                effectCache = cache;
                GetPreviewDimensions(maxSize, out int w, out int h, out float scale);
                return RenderStandalone(container, index, w, h, scale, new HashSet<Layer>(),
                    applyTransform: false, includeDisabled: true, applyClipping: false);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                effectCache = previousCache;
                collectingErrors = previousErrors;
                interactiveEffects = previousQuality;
            }
        }
    }
}
