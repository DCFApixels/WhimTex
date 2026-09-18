using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "TextureCompositor")]
    [CreateAssetMenu(fileName = "TextureCompositor", menuName = "WhimTex/Texture Compositor")]
    public sealed partial class TextureCompositor : ScriptableObject, ISerializationCallbackReceiver
    {
        private const int MinimumOutputSize = 1;
        private const int MaximumOutputSize = 16384;
        private const int AlphaUnionMode = 100;

        public int width = 512;
        public int height = 512;
        public FilterMode outputFilter = FilterMode.Bilinear;
        [SerializeField, HideInInspector] internal Mesh uvReferenceMesh;
        [SerializeField, HideInInspector] internal int uvReferenceChannel;
        [SerializeField, HideInInspector] internal int uvReferenceSubmesh = -1;
        [SerializeReference] public List<Layer> layers = new List<Layer>();
        [SerializeField, HideInInspector] private List<ShaderFX> embeddedShaderFX = new List<ShaderFX>();

        internal static event Action<TextureCompositor> Changed;

        internal static void NotifyShaderFXChanged(ShaderFX effect)
        {
            // Only loaded documents need repainting; never load or rebake saved assets here.
            foreach (TextureCompositor document in Resources.FindObjectsOfTypeAll<TextureCompositor>())
                if (ContainsShaderFX(document.layers, effect))
                    Changed?.Invoke(document);
        }

        private static bool ContainsShaderFX(List<Layer> source, ShaderFX effect)
        {
            if (source == null)
                return false;
            foreach (Layer layer in source)
            {
                if (layer?.modifiers != null && layer.modifiers.Contains(effect))
                    return true;
                if (layer?.AsGroup() is Layer group && ContainsShaderFX(group.layers, effect))
                    return true;
            }
            return false;
        }

        private void OnEnable()
        {
            NormalizeModel();
            EnsureOutputSettingsBaseline();
            undoDeserialized = Undo.isProcessing;
            CaptureNativeUndoVersions();
        }

        private void OnValidate()
        {
            NormalizeModel();
        }

        private void OnDisable()
        {
            ReleaseLayerThumbnails();
            StopLiveOutput();
            ReleaseLayerResources(layers, preserveDrawingPixels: true);
            ReleaseDiagnostics();
        }

        private void OnDestroy()
        {
            ReleaseLayerThumbnails();
            ReleaseLayerResources(layers);
            foreach (ShaderFX effect in embeddedShaderFX)
                if (effect != null && effect.EmbeddedOwner == this && !AssetDatabase.Contains(effect))
                    DestroyImmediate(effect);
        }

        internal ShaderFX AddEmbeddedShaderFX(Layer layer)
        {
            ShaderFX effect = ShaderFX.CreateEmbedded(this);
            Undo.RecordObject(this, "Add Shader FX");
            embeddedShaderFX.Add(effect);
            layer.modifiers.Add(effect);
            return effect;
        }

        internal void EmbedShaderFX(Layer layer, int index)
        {
            if (!(layer.modifiers[index] is ShaderFX source))
                return;
            ShaderFX copy = source.CloneForDocument(this);
            Undo.RegisterCreatedObjectUndo(copy, "Embed Shader FX");
            Undo.RecordObject(this, "Embed Shader FX");
            embeddedShaderFX.Add(copy);
            layer.modifiers[index] = copy;
        }

        internal void AddCatalogShaderFX(Layer layer, ShaderFXCatalog.Entry entry)
        {
            ShaderFX effect;
            try { effect = ShaderFX.FromCatalog(this, entry); }
            catch (Exception error) { Debug.LogError("WhimTex FX: " + error.Message); return; }
            Undo.RecordObject(this, "Add Catalog FX");
            AdoptAgentShaderFX(effect, "Add Catalog FX");
            layer.modifiers.Add(effect);
        }

        internal void PersistEmbeddedShaderFX()
        {
            foreach (ShaderFX effect in embeddedShaderFX)
                if (effect != null)
                    effect.PersistEmbedded(this);
        }

        internal void AdoptAgentShaderFX(ShaderFX effect, string undoName)
        {
            if (effect == null || effect.EmbeddedOwner != this || embeddedShaderFX.Contains(effect)) return;
            effect.RegisterCreatedCopyUndo(undoName);
            Undo.RegisterCompleteObjectUndo(this, undoName);
            embeddedShaderFX.Add(effect);
        }

        internal void RemoveUnusedEmbeddedShaderFX()
        {
            if (Undo.isProcessing)
                return;

            HashSet<ShaderFX> unused = null;
            foreach (ShaderFX effect in embeddedShaderFX)
                if (effect != null && effect.EmbeddedOwner == this && !ContainsShaderFX(layers, effect))
                {
                    unused ??= new HashSet<ShaderFX>();
                    unused.Add(effect);
                }
            if (unused == null)
                return;

            Undo.FlushUndoRecordObjects();
            Undo.RegisterCompleteObjectUndo(this, "Remove Unused Shader FX");
            embeddedShaderFX.RemoveAll(effect => effect == null || unused.Contains(effect));
            foreach (ShaderFX effect in unused)
                effect.DestroyEmbeddedWithUndo(this);
            EditorUtility.SetDirty(this);
        }

        internal void CloneEmbeddedShaderFX()
        {
            embeddedShaderFX = new List<ShaderFX>();
            Dictionary<ShaderFX, ShaderFX> copies = new Dictionary<ShaderFX, ShaderFX>();
            CloneIn(layers);
            void CloneIn(List<Layer> source)
            {
                if (source == null)
                    return;
                foreach (Layer layer in source)
                {
                    if (layer == null)
                        continue;
                    if (layer.modifiers != null)
                        for (int i = 0; i < layer.modifiers.Count; i++)
                            if (layer.modifiers[i] is ShaderFX effect && effect.EmbeddedOwner != null)
                            {
                                if (!copies.TryGetValue(effect, out ShaderFX copy))
                                {
                                    copy = effect.CloneForDocument(this);
                                    copies.Add(effect, copy);
                                    embeddedShaderFX.Add(copy);
                                }
                                layer.modifiers[i] = copy;
                            }
                    if (layer?.AsGroup() is Layer group)
                        CloneIn(group.layers);
                }
            }
        }

        public Texture2D Compose()
        {
            NormalizeModel();
            return ComposeAtSize(width, height, 1f);
        }

        internal Texture2D ComposePreview(int maxSize)
        {
            GetPreviewDimensions(maxSize, out int previewWidth, out int previewHeight, out float scaleMultiplier);
            return ComposeAtSize(previewWidth, previewHeight, scaleMultiplier);
        }

        internal RenderTexture RenderPreview(int maxSize)
        {
            GetPreviewDimensions(maxSize, out int previewWidth, out int previewHeight, out float scaleMultiplier);
            return RenderComposite(previewWidth, previewHeight, scaleMultiplier);
        }

        internal RenderTexture RenderLayerPreview(Layer layer, int maxSize) =>
            RenderLayerPreviewCore(layer, maxSize, false, false);

        internal RenderTexture RenderAgentLayerPreview(Layer layer, int maxSize) =>
            RenderLayerPreviewCore(layer, maxSize, true, true);

        private RenderTexture RenderLayerPreviewCore(Layer layer, int maxSize, bool includeDisabled, bool preserveGroupColor)
        {
            if (layer == null || !TryFindLayer(layer, out List<Layer> container, out int index))
                return null;

            RefreshTransformHierarchy();
            // Unlike RenderComposite, these paths enter layer rendering directly.
            // Do not leave their temporary output bound in the caller's render state.
            RenderTexture previous = RenderTexture.active;
            try
            {
                GetPreviewDimensions(maxSize, out int previewWidth, out int previewHeight, out float scaleMultiplier);
                if (preserveGroupColor && layer?.AsGroup() is Layer group)
                    return RenderGroupEffectInput(group, previewWidth, previewHeight, scaleMultiplier,
                        new HashSet<Layer>(), preserveColor: true, includeDisabled: includeDisabled);
                return RenderStandalone(
                    container,
                    index,
                    previewWidth,
                    previewHeight,
                    scaleMultiplier,
                    new HashSet<Layer>(), includeDisabled: includeDisabled);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        internal Layer FindLayer(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            return FindLayerRecursive(layers, id);
        }

        internal Texture2D RasterizeLayer(Layer layer, bool applyTransform)
        {
            if (layer == null || !TryFindLayer(layer, out List<Layer> container, out int index))
                throw new InvalidOperationException("The layer no longer belongs to this composition.");

            RenderTexture previous = RenderTexture.active;
            RefreshTransformHierarchy();
            RenderTexture rendered = null;
            try
            {
                if (layer?.AsGroup() is Layer group)
                {
                    if (IsGroupIsolatedByClipping(group))
                        rendered = RenderClippingSource(container, index, width, height, 1f, new HashSet<Layer>());
                    else
                    {
                        rendered = GetClearRenderTexture(width, height);
                        CompositeGroup(group, ref rendered, width, height, 1f, new HashSet<Layer>());
                    }
                }
                else
                {
                    rendered = RenderStandalone(container, index, width, height, 1f, new HashSet<Layer>(),
                        applyTransform: applyTransform, applyModifiers: false, includeDisabled: true, applyClipping: false);
                    if (rendered == null)
                        rendered = GetClearRenderTexture(width, height);
                }
                Texture2D linear = HdrUtility.ReadLinear(rendered);
                if (layer.colorRange == LayerColorRange.HDR || layer?.IsGroup == true || layer?.Behaviour is DrawingLayerBehaviour drawing && HdrUtility.IsHdr(drawing.StoredTexture))
                    return linear;
                try { return HdrUtility.ToLdr(linear); }
                finally { DestroyImmediate(linear); }
            }
            finally
            {
                // Unbind our output before returning it to Unity's temporary pool.
                RenderTexture.active = previous;
                if (rendered != null)
                    RenderTexture.ReleaseTemporary(rendered);
            }
        }

        internal bool TryFindLayer(Layer target, out List<Layer> container, out int index)
        {
            return TryFindLayerRecursive(layers, target, out container, out index);
        }

        internal bool TryFindParentGroup(List<Layer> childList, out Layer parent, out List<Layer> parentContainer, out int parentIndex)
        {
            return TryFindParentGroupRecursive(layers, childList, out parent, out parentContainer, out parentIndex);
        }

        internal void GetEffectTargetOptions(
            TargetedLayerBehaviour consumer,
            List<string> targetIds,
            List<string> labels)
        {
            if (targetIds == null)
                throw new ArgumentNullException(nameof(targetIds));
            if (labels == null)
                throw new ArgumentNullException(nameof(labels));

            targetIds.Clear();
            labels.Clear();
            if (consumer == null)
                return;

            CollectEffectTargetOptions(layers, consumer, 0, targetIds, labels, new HashSet<Layer>());
        }

        internal bool IsUsableEffectTarget(TargetedLayerBehaviour consumer, string targetId)
        {
            Layer target = FindLayer(targetId);
            return target?.Behaviour != null && !(target.Behaviour is PendingLayerBehaviour) && !LayerDependsOn(target, consumer, new HashSet<Layer>());
        }

        internal bool HasUsableEffectInput(
            TargetedLayerBehaviour effect,
            List<Layer> container,
            int index)
        {
            if (effect == null)
                return false;
            if (effect.inputMode == EffectInputMode.Specific)
                return IsUsableEffectTarget(effect, effect.TargetLayerId);
            return container != null && NextContentLayer(container, index) < container.Count;
        }

        private static int NextContentLayer(List<Layer> container, int index)
        {
            do { index++; } while (index < container.Count && (container[index]?.Behaviour == null || container[index].Behaviour is PendingLayerBehaviour));
            return index;
        }

        internal void NormalizeModel()
        {
            width = Mathf.Clamp(width, MinimumOutputSize, MaximumOutputSize);
            height = Mathf.Clamp(height, MinimumOutputSize, MaximumOutputSize);
            if (outputFilter != FilterMode.Point && outputFilter != FilterMode.Bilinear && outputFilter != FilterMode.Trilinear)
                outputFilter = FilterMode.Bilinear;
            layers ??= new List<Layer>();
            HashSet<string> usedIds = new HashSet<string>();
            NormalizeLayers(layers, usedIds);
            SynchronizeNextAutomaticNumbers();
        }

        internal void MarkChanged()
        {
            undoDeserialized = false;
            NormalizeModel();
            RemoveUnusedEmbeddedShaderFX();
            if (AssetDatabase.Contains(this))
            {
                PersistEmbeddedShaderFX();
                PersistDrawingLayerTextures();
                EditorUtility.SetDirty(this);
            }
            CaptureNativeUndoVersions();
            Changed?.Invoke(this);
        }

        internal void SyncDrawingLayerTextures()
        {
            VisitDrawingLayers(layers, drawing => drawing.SyncSurfaceToTexture());
        }

        internal void CloneDrawingLayerTextures()
        {
            VisitDrawingLayers(layers, drawing => drawing.CloneStoredTexture());
        }

        internal void PersistDrawingLayerTextures(bool reimport = true)
        {
            if (!AssetDatabase.Contains(this))
                return;

            bool addedTexture = false;
            VisitDrawingLayers(layers, drawing => addedTexture |= drawing.MakeTexturePersistent(this));
            if (!addedTexture || !reimport)
                return;

            string assetPath = AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        internal void InvalidateDrawingLayerSurfaces()
        {
            VisitDrawingLayers(layers, drawing => drawing.InvalidatePaintSurface());
        }

        internal void DestroyLayerAssets(Layer layer)
        {
            if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                drawing.DestroyStoredTextureWithUndo();
            if (!(layer?.AsGroup() is Layer group) || group.layers == null)
                return;
            for (int i = 0; i < group.layers.Count; i++)
                DestroyLayerAssets(group.layers[i]);
        }

        internal static Texture2D CopyToTexture2D(RenderTexture source, bool uploadToGpu = true)
        {
            if (source == null)
                return null;

            RenderTexture encoded = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode,
                wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                var conversion = WhimTexMaterials.Hdr;
                conversion.SetFloat("_Saturate", 1f);
                conversion.SetFloat("_Encode", 1f);
                conversion.SetFloat("_UseSwizzle", 0f);
                Graphics.Blit(source, encoded, conversion, 0);
                RenderTexture.active = encoded;
                texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                if (uploadToGpu)
                    texture.Apply(false, false);
                return texture;
            }
            catch
            {
                DestroyImmediate(texture);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(encoded);
            }
        }

        private Texture2D ComposeAtSize(int outputWidth, int outputHeight, float scaleMultiplier)
        {
            RenderTexture composite = RenderComposite(outputWidth, outputHeight, scaleMultiplier);
            try
            {
                Texture2D result = HdrUtility.ReadLinear(composite);
                result.filterMode = outputFilter;
                return result;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(composite);
            }
        }

        private RenderTexture RenderComposite(int outputWidth, int outputHeight, float scaleMultiplier)
        {
            RefreshTransformHierarchy();
            EffectRenderCache localCache = null;
            if (effectCache == null)
            {
                localCache = effectCache = new EffectRenderCache();
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture accumulator = null;
            try
            {
                localCache?.BeginFrame(this);
                BeginDiagnostics(outputWidth, outputHeight);
                accumulator = GetClearRenderTexture(outputWidth, outputHeight);
                CompositeLayers(
                    layers,
                    ref accumulator,
                    outputWidth,
                    outputHeight,
                    scaleMultiplier,
                    new HashSet<Layer>());
                EndDiagnostics();
                accumulator.filterMode = outputFilter;
                return accumulator;
            }
            catch
            {
                RenderTexture.active = previous;
                if (accumulator != null) RenderTexture.ReleaseTemporary(accumulator);
                ReleaseDiagnostics();
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (localCache != null) { localCache.Dispose(); effectCache = null; }
            }
        }

        private void CompositeLayers(
            List<Layer> sourceLayers,
            ref RenderTexture accumulator,
            int outputWidth,
            int outputHeight,
            float scaleMultiplier,
            HashSet<Layer> renderStack,
            HashSet<Layer> included = null, int firstIndex = 0)
        {
            if (sourceLayers == null)
                return;

            // Data and UI are ordered top-to-bottom. Rendering therefore walks backwards.
            if (sourceLayers.Exists(layer => layer?.Behaviour is PendingLayerBehaviour))
            {
                var content = new List<Layer>(sourceLayers.Count);
                int start = 0;
                for (int n = 0; n < sourceLayers.Count; n++)
                    if (!(sourceLayers[n]?.Behaviour is PendingLayerBehaviour))
                    {
                        if (n < firstIndex) start++;
                        content.Add(sourceLayers[n]);
                    }
                sourceLayers = content;
                firstIndex = start;
            }
            for (int i = sourceLayers.Count - 1; i >= firstIndex; i--)
            {
                Layer layer = sourceLayers[i];
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour processor)
                {
                    if (processor.enabled && processor.opacity > 0f && processor.blendMode != BlendMode.None &&
                        (included == null || included.Contains(processor)))
                        CompositeProcessor(processor, ref accumulator, outputWidth, outputHeight, scaleMultiplier, renderStack);
                    continue;
                }
                // A chain is resolved before selection/visibility filtering: a hidden base
                // still owns (and hides) its clipping layers. Orphans never render freely.
                if (layer == null || layer.clippingMask) continue;
                int top = i;
                while (top > firstIndex && sourceLayers[top - 1] != null && !(sourceLayers[top - 1]?.Behaviour is ShaderProcessorLayerBehaviour) && sourceLayers[top - 1].clippingMask) top--;
                if (top < i)
                {
                    CompositeClippingChain(sourceLayers, i, top, ref accumulator,
                        outputWidth, outputHeight, scaleMultiplier, renderStack, included);
                    i = top;
                    continue;
                }
                if (included != null && !included.Contains(layer))
                    continue;
                if (layer?.Behaviour == null || !layer.enabled || layer.opacity <= 0f ||
                    (layer?.AsGroup() is Layer pass ? !pass.IsPassThrough && pass.EffectiveBlendMode == BlendMode.None : layer.blendMode == BlendMode.None))
                    continue;

                if (layer?.AsGroup() is Layer group)
                {
                    CompositeGroup(group, ref accumulator, outputWidth, outputHeight, scaleMultiplier, renderStack, included);
                    continue;
                }

                RenderTexture rendered = RenderStandalone(
                    sourceLayers,
                    i,
                    outputWidth,
                    outputHeight,
                    scaleMultiplier,
                    renderStack);
                if (rendered == null)
                    continue;

                try
                {
                    BlendInto(ref accumulator, rendered, layer.blendMode, layer.opacity, layer.blendRange);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(rendered);
                }
            }
        }

        private void CompositeGroup(Layer group, ref RenderTexture accumulator, int w, int h,
            float scale, HashSet<Layer> stack, HashSet<Layer> included = null)
        {
            if (group.opacity <= 0f) return;
            bool passThrough = group.IsPassThrough;
            if (passThrough && group.opacity >= 1f)
            {
                CompositeLayers(group.layers, ref accumulator, w, h, scale, stack, included);
                return;
            }
            RenderTexture content = GetClearRenderTexture(w, h);
            try
            {
                if (passThrough) Graphics.Blit(accumulator, content);
                CompositeLayers(group.layers, ref content, w, h, scale, stack, included);
                if (!passThrough)
                {
                    group.ApplyModifiers(ref content, new LayerRenderContext(this, null, w, h, scale, false, true));
                    content = FinishStage(content, group.colorRange == LayerColorRange.Standard, group.swizzle);
                }
                BlendInto(ref accumulator, content, passThrough ? (BlendMode)101 : group.EffectiveBlendMode,
                    group.opacity, group.blendRange);
            }
            finally { RenderTexture.ReleaseTemporary(content); }
        }

        private RenderTexture RenderStandaloneUncached(
            List<Layer> container,
            int index,
            int outputWidth,
            int outputHeight,
            float scaleMultiplier,
            HashSet<Layer> renderStack,
            bool applyTransform = true,
            bool applyModifiers = true,
            bool includeDisabled = false,
            bool applyClipping = true)
        {
            if (container == null || index < 0 || index >= container.Count)
                return null;

            Layer layer = container[index];
            if (layer == null || (!includeDisabled && !layer.enabled))
                return null;
            if (layer?.AsGroup() is Layer group)
                return RenderGroupEffectInput(group, outputWidth, outputHeight, scaleMultiplier, renderStack,
                    preserveColor: false, includeDisabled: includeDisabled);

            renderStack ??= new HashSet<Layer>();
            if (!renderStack.Add(layer))
                return null;

            RenderTexture input = null;
            try
            {
                if (layer?.Behaviour is ShaderProcessorLayerBehaviour)
                {
                    input = GetClearRenderTexture(outputWidth, outputHeight);
                    CompositeLayers(container, ref input, outputWidth, outputHeight, scaleMultiplier, renderStack, firstIndex: index + 1);
                    // Effect inputs may read hidden source layers. A disabled stack processor
                    // is a bypass, not a hidden image to process again through that path.
                    if (!layer.enabled)
                    {
                        RenderTexture bypass = input;
                        input = null;
                        return bypass;
                    }
                }
                if (layer?.Behaviour is TargetedLayerBehaviour effect)
                {
                    input = RenderEffectInput(
                        effect,
                        container,
                        index,
                        outputWidth,
                        outputHeight,
                        scaleMultiplier,
                        renderStack);
                }

                LayerRenderContext context = new LayerRenderContext(
                    this,
                    input,
                    outputWidth,
                    outputHeight,
                    scaleMultiplier,
                    applyTransform,
                    applyModifiers);
                RenderTexture raw = layer.Render(context);
                try
                {
                    raw = FinishStage(raw, layer.colorRange == LayerColorRange.Standard, applyModifiers ? layer.swizzle : default);
                    if (applyClipping && raw != null && layer.clippingMask)
                        ApplyClippingCoverage(ref raw, container, index, outputWidth, outputHeight, scaleMultiplier, renderStack);
                    return raw;
                }
                catch { if (raw != null) RenderTexture.ReleaseTemporary(raw); throw; }
            }
            finally
            {
                if (input != null)
                    RenderTexture.ReleaseTemporary(input);
                renderStack.Remove(layer);
            }
        }

        private RenderTexture RenderEffectInput(
            TargetedLayerBehaviour effect,
            List<Layer> container,
            int index,
            int outputWidth,
            int outputHeight,
            float scaleMultiplier,
            HashSet<Layer> renderStack)
        {
            if (effect.inputMode != EffectInputMode.Specific)
            {
                return RenderPreviousEffectInput(
                    container,
                    index,
                    outputWidth,
                    outputHeight,
                    scaleMultiplier,
                    renderStack, effect.RequiresColorInput);
            }

            Layer target = FindLayer(effect.TargetLayerId);
            if (target == null ||
                !IsUsableEffectTarget(effect, effect.TargetLayerId))
                return null;

            if (target?.AsGroup() is Layer group)
                return RenderGroupEffectInput(group, outputWidth, outputHeight, scaleMultiplier, renderStack,
                    effect.RequiresColorInput, includeDisabled: true);
            if (!TryFindLayer(target, out List<Layer> targetContainer, out int targetIndex))
                return null;

            return RenderStandalone(
                targetContainer,
                targetIndex,
                outputWidth,
                outputHeight,
                scaleMultiplier,
                renderStack, includeDisabled: true);
        }

        private RenderTexture RenderPreviousInput(
            List<Layer> container, int currentIndex, int outputWidth, int outputHeight,
            float scaleMultiplier, HashSet<Layer> renderStack) => RenderPreviousEffectInput(
                container, currentIndex, outputWidth, outputHeight, scaleMultiplier, renderStack, false);

        private RenderTexture RenderPreviousEffectInput(
            List<Layer> container,
            int currentIndex,
            int outputWidth,
            int outputHeight,
            float scaleMultiplier,
            HashSet<Layer> renderStack, bool preserveGroupColor)
        {
            int previousIndex = NextContentLayer(container, currentIndex);
            if (previousIndex >= container.Count)
                return null;

            Layer previous = container[previousIndex];
            if (previous?.AsGroup() is Layer group)
                return RenderGroupEffectInput(group, outputWidth, outputHeight, scaleMultiplier, renderStack,
                    preserveGroupColor, includeDisabled: true);
            return RenderStandalone(
                container,
                previousIndex,
                outputWidth,
                outputHeight,
                scaleMultiplier,
                renderStack, includeDisabled: true);
        }

        private RenderTexture RenderGroupAlpha(
            Layer group, int outputWidth, int outputHeight, float scaleMultiplier,
            HashSet<Layer> renderStack) => RenderGroupEffectInput(group, outputWidth, outputHeight, scaleMultiplier, renderStack, false);

        private RenderTexture RenderGroupEffectInputUncached(
            Layer group,
            int outputWidth,
            int outputHeight,
            float scaleMultiplier,
            HashSet<Layer> renderStack, bool preserveColor, bool includeDisabled = false)
        {
            if (group == null || (!includeDisabled && !group.enabled))
                return null;

            renderStack ??= new HashSet<Layer>();
            if (!renderStack.Add(group)) return null;

            RenderTexture previous = RenderTexture.active;
            RenderTexture mask = GetClearRenderTexture(outputWidth, outputHeight);
            RenderTexture scaled = null;
            try
            {
                // Render only the group's own content against transparency, never its external backdrop.
                // This also respects nested opacity and alpha-replacing blend modes.
                CompositeLayers(group.layers, ref mask, outputWidth, outputHeight, scaleMultiplier, renderStack);
                group.ApplyModifiers(ref mask, new LayerRenderContext(this, null, outputWidth, outputHeight, scaleMultiplier, false, true));
                if (preserveColor || !group.swizzle.IsIdentity)
                    mask = FinishStage(mask, group.colorRange == LayerColorRange.Standard, group.swizzle);
                if (group.clippingMask && TryFindLayer(group, out var container, out int index))
                    ApplyClippingCoverage(ref mask, container, index, outputWidth, outputHeight, scaleMultiplier, renderStack);
                scaled = GetClearRenderTexture(outputWidth, outputHeight);
                BlendInto(ref scaled, mask, preserveColor ? BlendMode.Normal : (BlendMode)AlphaUnionMode,
                    group.opacity, preserveColor ? LayerBlendRange.HDR : LayerBlendRange.Standard);
                RenderTexture result = scaled;
                scaled = null;
                return result;
            }
            finally
            {
                renderStack.Remove(group);
                RenderTexture.active = previous;
                if (mask != null)
                    RenderTexture.ReleaseTemporary(mask);
                if (scaled != null)
                    RenderTexture.ReleaseTemporary(scaled);
            }
        }

        private void BlendInto(ref RenderTexture accumulator, RenderTexture layer, BlendMode mode, float opacity,
            LayerBlendRange blendRange = LayerBlendRange.Standard, bool preserveAlpha = false)
        {
            RenderTexture previous = RenderTexture.active;
            Material material = WhimTexMaterials.Blend;
            RenderTexture result = RenderTexture.GetTemporary(
                accumulator.width,
                accumulator.height,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            try
            {
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                if (material == null)
                {
                    Graphics.Blit(layer, result);
                }
                else
                {
                    material.SetTexture("_Blend", layer);
                    material.SetFloat("_Mode", (int)mode);
                    material.SetFloat("_HdrBlend", blendRange == LayerBlendRange.HDR ? 1f : 0f);
                    material.SetFloat("_Opacity", Mathf.Clamp01(opacity));
                    material.SetFloat("_PreserveAlpha", preserveAlpha ? 1f : 0f);
                    Graphics.Blit(accumulator, result, material, 0);
                }
                result = FinishStage(result);
            }
            catch
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(result);
                throw;
            }

            RenderTexture.ReleaseTemporary(accumulator);
            accumulator = result;
        }

        private static RenderTexture GetClearRenderTexture(int outputWidth, int outputHeight)
        {
            RenderTexture result = RenderTexture.GetTemporary(
                outputWidth,
                outputHeight,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                RenderTexture.active = result;
                GL.Clear(true, true, Color.clear);
            }
            catch
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(result);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return result;
        }

        private void GetPreviewDimensions(int maxSize, out int previewWidth, out int previewHeight, out float scaleMultiplier)
        {
            maxSize = Mathf.Max(1, maxSize);
            float scale = Mathf.Min(1f, (float)maxSize / Mathf.Max(width, height));
            previewWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            previewHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            scaleMultiplier = Mathf.Max((float)width / previewWidth, (float)height / previewHeight);
        }

        private void CollectEffectTargetOptions(
            List<Layer> sourceLayers,
            TargetedLayerBehaviour consumer,
            int depth,
            List<string> targetIds,
            List<string> labels,
            HashSet<Layer> visited)
        {
            if (sourceLayers == null)
                return;

            for (int i = 0; i < sourceLayers.Count; i++)
            {
                Layer candidate = sourceLayers[i];
                if (candidate == null || candidate?.Behaviour is PendingLayerBehaviour)
                    continue;

                bool isGroup = candidate?.IsGroup == true;
                visited.Clear();
                if (!LayerDependsOn(candidate, consumer, visited))
                {
                    string candidateName = string.IsNullOrWhiteSpace(candidate.layerName)
                        ? candidate.Behaviour?.GetType().Name ?? "Missing Behaviour"
                        : candidate.layerName;
                    string indentation = depth > 0 ? new string(' ', depth * 4) + "↳ " : string.Empty;
                    targetIds.Add(candidate.Id);
                    labels.Add(indentation + candidateName + (isGroup ? "  [Group]" : string.Empty));
                }

                if (candidate?.AsGroup() is Layer group)
                {
                    CollectEffectTargetOptions(
                        group.layers,
                        consumer,
                        depth + 1,
                        targetIds,
                        labels,
                        visited);
                }
            }
        }

        private bool LayerDependsOn(Layer candidate, Layer soughtLayer, HashSet<Layer> visited)
        {
            if (candidate == null)
                return false;
            if (ReferenceEquals(candidate, soughtLayer) || soughtLayer != null && candidate.Id == soughtLayer.Id)
                return true;
            if (!visited.Add(candidate))
                return false;

            if (candidate.modifiers != null)
                foreach (var modifier in candidate.modifiers)
                    if (modifier is ShaderFX fx)
                        foreach (var parameter in fx.TextureLayerParameters())
                            if (LayerDependsOn(FindLayer(parameter.textureLayerId), soughtLayer, visited)) return true;

            if (candidate.clippingMask && LayerDependsOn(GetClippingBase(candidate), soughtLayer, visited))
                return true;

            if (candidate?.AsGroup() is Layer group)
            {
                if (group.layers == null)
                    return false;
                for (int i = 0; i < group.layers.Count; i++)
                {
                    if (LayerDependsOn(group.layers[i], soughtLayer, visited))
                        return true;
                }
                return false;
            }

            if (candidate?.Behaviour is ShaderProcessorLayerBehaviour)
            {
                if (TryFindLayer(candidate, out var siblings, out int processorIndex))
                    for (int i = processorIndex + 1; i < siblings.Count; i++)
                        if (LayerDependsOn(siblings[i], soughtLayer, visited)) return true;
                return false;
            }

            if (!(candidate?.Behaviour is TargetedLayerBehaviour effect))
                return false;

            Layer input = null;
            if (effect.inputMode == EffectInputMode.Specific)
            {
                input = FindLayer(effect.TargetLayerId);
            }
            else if (TryFindLayer(effect, out List<Layer> container, out int index) &&
                     NextContentLayer(container, index) < container.Count)
            {
                input = container[NextContentLayer(container, index)];
            }

            return LayerDependsOn(input, soughtLayer, visited);
        }

        private static void NormalizeLayers(List<Layer> sourceLayers, HashSet<string> usedIds)
        {
            for (int i = 0; i < sourceLayers.Count; i++)
            {
                Layer layer = sourceLayers[i];
                if (layer == null)
                    continue;

                layer.EnsureId(usedIds);
                layer.opacity = Mathf.Clamp01(layer.opacity);
                if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                    drawing.NormalizeSettings();
                if (layer?.AsGroup() is Layer group)
                {
                    group.layers ??= new List<Layer>();
                    NormalizeLayers(group.layers, usedIds);
                }
            }
        }

        private static int SynchronizeCounter(int nextNumber, int highestNumber)
        {
            int numberAfterExisting = highestNumber < int.MaxValue ? highestNumber + 1 : int.MaxValue;
            return Mathf.Max(1, Mathf.Max(nextNumber, numberAfterExisting));
        }

        private static Layer FindLayerRecursive(List<Layer> sourceLayers, string id)
        {
            if (sourceLayers == null)
                return null;

            for (int i = 0; i < sourceLayers.Count; i++)
            {
                Layer layer = sourceLayers[i];
                if (layer == null)
                    continue;
                if (layer.Id == id)
                    return layer;
                if (layer?.AsGroup() is Layer group)
                {
                    Layer found = FindLayerRecursive(group.layers, id);
                    if (found != null)
                        return found;
                }
            }
            return null;
        }

        private static bool TryFindLayerRecursive(
            List<Layer> sourceLayers,
            Layer target,
            out List<Layer> container,
            out int index)
        {
            if (sourceLayers != null)
            {
                for (int i = 0; i < sourceLayers.Count; i++)
                {
                    Layer layer = sourceLayers[i];
                    if (ReferenceEquals(layer, target))
                    {
                        container = sourceLayers;
                        index = i;
                        return true;
                    }

                    if (layer?.AsGroup() is Layer group &&
                        TryFindLayerRecursive(group.layers, target, out container, out index))
                        return true;
                }
            }

            container = null;
            index = -1;
            return false;
        }

        private static bool TryFindParentGroupRecursive(
            List<Layer> sourceLayers,
            List<Layer> childList,
            out Layer parent,
            out List<Layer> parentContainer,
            out int parentIndex)
        {
            if (sourceLayers != null)
            {
                for (int i = 0; i < sourceLayers.Count; i++)
                {
                    if (!(sourceLayers[i]?.AsGroup() is Layer group))
                        continue;
                    if (ReferenceEquals(group.layers, childList))
                    {
                        parent = group;
                        parentContainer = sourceLayers;
                        parentIndex = i;
                        return true;
                    }
                    if (TryFindParentGroupRecursive(group.layers, childList, out parent, out parentContainer, out parentIndex))
                        return true;
                }
            }

            parent = null;
            parentContainer = null;
            parentIndex = -1;
            return false;
        }

        private static void ReleaseLayerResources(List<Layer> sourceLayers, bool preserveDrawingPixels = false)
        {
            if (sourceLayers == null)
                return;
            for (int i = 0; i < sourceLayers.Count; i++)
            {
                Layer layer = sourceLayers[i];
                if (layer == null)
                    continue;
                if (preserveDrawingPixels && layer?.Behaviour is DrawingLayerBehaviour drawing)
                    drawing.ReleasePaintResources();
                else
                    layer.ReleaseTransientResources();
                if (layer?.AsGroup() is Layer group)
                    ReleaseLayerResources(group.layers, preserveDrawingPixels);
            }
        }

        private static void VisitDrawingLayers(List<Layer> sourceLayers, Action<DrawingLayerBehaviour> visitor)
        {
            if (sourceLayers == null || visitor == null)
                return;
            for (int i = 0; i < sourceLayers.Count; i++)
            {
                Layer layer = sourceLayers[i];
                if (layer?.Behaviour is DrawingLayerBehaviour drawing)
                    visitor(drawing);
                if (layer?.AsGroup() is Layer group)
                    VisitDrawingLayers(group.layers, visitor);
            }
        }
    }
}
