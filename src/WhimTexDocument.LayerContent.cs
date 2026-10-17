using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [NonSerialized] private LayerContentCache layerContent;

        internal LayerContentState GetLayerContentState(Layer layer, bool deferUpdates = false)
        {
            if (layer == null || layer.Behaviour == null) return LayerContentState.Unknown;
            layerContent ??= new LayerContentCache(this);
            return layerContent.Get(layer, deferUpdates);
        }

        internal void ReleaseLayerContent()
        {
            layerContent?.Dispose();
            layerContent = null;
        }

        internal void RefreshLayerContentStructure() => layerContent?.RefreshStructure();

        private void OfferLayerContent(Layer layer, RenderTexture rendered, bool applyTransform, float scale)
        {
            if (layerContent == null || interactiveEffects || scale != 1f ||
                layer.IsGroup || layer.Behaviour is ShaderProcessorLayerBehaviour || layer.Behaviour is TargetedLayerBehaviour)
                return;
            if (applyTransform && (!layer.RenderTransform.ToMatrix(width, height).Equals(ProjectiveMatrix.Identity) ||
                !layer.CanvasTransform.ToMatrix(width, height).Equals(ProjectiveMatrix.Identity) ||
                layer.Behaviour is DrawingLayerBehaviour drawing && drawing.HasBakedFxFrame)) return;
            layerContent.AcceptRendered(layer, rendered);
        }

        internal void GetLayerContentSize(Layer layer, out int w, out int h)
        {
            Texture source = layer.SamplingSource;
            if (layer.Behaviour is FileLayerBehaviour file && file.sourceTexture != null)
                source = ResolveOriginalFileTexture(file.sourceTexture) ?? file.sourceTexture;
            w = source != null ? source.width : width;
            h = source != null ? source.height : height;
        }

        internal bool CanInspectLayerContent(Layer layer, HashSet<Layer> visited)
            => CanInspectLayerContentCore(layer, visited, true);

        internal bool CanInspectCropLayerContent(Layer layer, HashSet<Layer> visited)
            => CanInspectLayerContentCore(layer, visited, true, canvasSpace: true);

        private bool CanInspectLayerContentCore(Layer layer, HashSet<Layer> visited, bool intrinsic, bool canvasSpace = false)
        {
            if (layer?.Behaviour == null || layer.Behaviour is PendingLayerBehaviour ||
                WhimTexApi.ContainsReservation(layer) || !visited.Add(layer)) return false;
            try
            {
                if (!TryFindLayer(layer, out var container, out int index)) return false;
                if (layer.Behaviour is FileLayerBehaviour file && file.sourceTexture == null) return false;
                if (!intrinsic && layer.clippingMask && !CanInspectLayerContentCore(GetClippingBase(layer), visited, false, canvasSpace)) return false;
                if (layer.IsGroup)
                {
                    // A translated group must not turn existing children into a false empty badge.
                    // The shared compositor currently evaluates their transforms in canvas space.
                    if (!canvasSpace && layer.children.Count > 0 &&
                        !layer.CanvasTransform.ToMatrix(width, height).Equals(ProjectiveMatrix.Identity)) return false;
                    foreach (Layer child in layer.children)
                        if (child?.enabled == true && child.opacity > 0f && !CanInspectLayerContentCore(child, visited, false, canvasSpace)) return false;
                }
                if (layer.fx != null)
                    foreach (var item in layer.fx)
                    {
                        if (!(item is ShaderFX effect) || !effect.Active) continue;
                        if (effect.IsUnavailable) return false;
                        foreach (var parameter in effect.TextureLayerParameters())
                            if (!IsUsableShaderTexture(layer, parameter.textureLayerId) ||
                                !CanInspectLayerContentCore(FindLayer(parameter.textureLayerId), visited, false, canvasSpace)) return false;
                    }
                if (layer.Behaviour is TargetedLayerBehaviour targeted)
                {
                    if (!HasUsableEffectInput(targeted, container, index)) return false;
                    if (targeted.inputMode == EffectInputMode.Specific)
                        return CanInspectLayerContentCore(FindLayer(targeted.TargetLayerId), visited, false, canvasSpace);
                    if (targeted.inputMode != EffectInputMode.AllBelow)
                        return CanInspectLayerContentCore(container[NextContentLayer(container, index)], visited, false, canvasSpace);
                }
                if (layer.Behaviour is ShaderProcessorLayerBehaviour ||
                    layer.Behaviour is TargetedLayerBehaviour allBelow && allBelow.inputMode == EffectInputMode.AllBelow)
                    for (int i = index + 1; i < container.Count; i++)
                        if (container[i]?.enabled == true && container[i].opacity > 0f &&
                            !CanInspectLayerContentCore(container[i], visited, false, canvasSpace)) return false;
                return true;
            }
            finally { visited.Remove(layer); }
        }

        // Own content includes FX and channel mapping, before outer opacity, visibility,
        // transform and clipping. Child visibility/opacity remain part of group content.
        internal RenderTexture RenderLayerContent(Layer layer, int w, int h, EffectRenderCache cache)
        {
            if (!TryFindLayer(layer, out var container, out int index)) return null;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite, oldErrors = collectingErrors, oldQuality = interactiveEffects,
                oldPublishing = publishingLayerPreview;
            var previousCache = effectCache;
            try
            {
                GL.sRGBWrite = false;
                collectingErrors = interactiveEffects = publishingLayerPreview = false;
                effectCache = cache;
                RefreshTransformHierarchy();
                var stack = new HashSet<Layer>();
                if ((!layer.IsGroup || layer.Behaviour is BooleanGroupLayerBehaviour) && !(layer.Behaviour is ShaderProcessorLayerBehaviour))
                    return RenderStandaloneUncached(container, index, w, h, 1f, stack,
                        applyTransform: false, includeDisabled: true, applyClipping: false);
                stack.Add(layer);
                RenderTexture content = GetClearRenderTexture(w, h);
                try
                {
                    CompositeLayers(layer.IsGroup ? layer.children : container, ref content,
                        w, h, 1f, stack, firstIndex: layer.IsGroup ? 0 : index + 1);
                    if (layer.IsGroup)
                        layer.ApplyFx(ref content, new LayerRenderContext(this, null, w, h, 1f, false, true));
                    else
                    {
                        RenderTexture input = content;
                        content = null;
                        try { content = layer.Render(new LayerRenderContext(this, input, w, h, 1f, false, true)); }
                        finally { ProfiledRenderTexture.ReleaseTemporary(input); }
                    }
                    content = FinishStage(content, layer.colorRange == LayerColorRange.Standard, layer.channelMapping);
                    RenderTexture result = content;
                    content = null;
                    return result;
                }
                finally { if (content != null) ProfiledRenderTexture.ReleaseTemporary(content); }
            }
            finally
            {
                effectCache = previousCache;
                collectingErrors = oldErrors; interactiveEffects = oldQuality; publishingLayerPreview = oldPublishing;
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
            }
        }
    }
}
