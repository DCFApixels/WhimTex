using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum LayerColorRange { Standard, HDR }
    public enum LayerBlendRange { Standard, HDR }
    internal readonly struct LayerRenderContext
    {
        public readonly WhimTexDocument activeDocument;
        public readonly RenderTexture input;
        public readonly int width;
        public readonly int height;
        public readonly float scaleMultiplier;
        public readonly float pixelScale;
        public float CanvasPixels(float logicalPixels) => logicalPixels * pixelScale;
        public float RenderPixels(float logicalPixels) => logicalPixels * pixelScale / scaleMultiplier;
        public readonly bool applyTransform;
        public readonly bool applyFx;
        public readonly bool transformFxCoordinates;

        public LayerRenderContext(
            WhimTexDocument activeDocument,
            RenderTexture input,
            int width,
            int height,
            float scaleMultiplier,
            bool applyTransform = true,
            bool applyFx = true,
            bool? transformFxCoordinates = null)
        {
            this.activeDocument = activeDocument;
            this.input = input;
            this.width = width;
            this.height = height;
            this.scaleMultiplier = Mathf.Max(0.0001f, scaleMultiplier);
            pixelScale = activeDocument != null ? activeDocument.GetPixelScale() : 1f;
            this.applyTransform = applyTransform;
            this.applyFx = applyFx;
            this.transformFxCoordinates = transformFxCoordinates ?? applyTransform;
        }
    }
    [Serializable]
    public sealed class Layer
    {
        [SerializeField] private string id;

        public string layerName = "New Layer";
        public bool enabled = true;
        public bool clippingMask;
        [Range(0f, 1f)] public float opacity = 1f;
        public BlendMode blendMode = BlendMode.Normal;
        public LayerColorRange colorRange;
        public LayerBlendRange blendRange;
        public LayerChannelMapping channelMapping;
        public List<ShaderFX> fx = new List<ShaderFX>();
        public TextureTransform transform = TextureTransform.Default;
        public VectorBooleanOperation booleanOperation;
        [SerializeField, HideInInspector] internal Vector2Int sourceCanvasSize;
        [SerializeField, HideInInspector] internal float sourcePixelScale;
        [SerializeField, HideInInspector] internal Vector2 sourceCanvasScale = Vector2.one;
        internal float GetPixelScale() => transformCache?.document != null ? transformCache.document.GetPixelScale() : 1f;
        internal float GetSourcePixelScale() => sourcePixelScale == 0f ? GetPixelScale() : sourcePixelScale;
        internal Vector2Int GetSourceCanvasSize(int width, int height)
        {
            ValidateSourceCanvasSize();
            return sourceCanvasSize == Vector2Int.zero ? new Vector2Int(width, height) : sourceCanvasSize;
        }
        internal Vector2 GetPixelSourceCanvasSize(int width, int height)
        {
            Vector2 size = Vector2.Scale(GetSourceCanvasSize(width, height), sourceCanvasScale);
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
                throw new WhimTexDocumentException("Layer source pixel metric exceeds its supported range: " + id);
            return size;
        }
        internal void ValidateSourceCanvasSize()
        {
            if (sourcePixelScale != 0f) WhimTexDocument.ValidatePixelScale(sourcePixelScale);
            if (!float.IsFinite(sourceCanvasScale.x) || !float.IsFinite(sourceCanvasScale.y) || sourceCanvasScale.x <= 0 || sourceCanvasScale.y <= 0)
                throw new WhimTexDocumentException("Layer sourceCanvasScale must contain finite positive values: " + id);
            if (sourceCanvasSize == Vector2Int.zero) return;
            if (sourceCanvasSize.x < 1 || sourceCanvasSize.y < 1 ||
                sourceCanvasSize.x > WhimTexDocument.MaximumCanvasFrameDimension || sourceCanvasSize.y > WhimTexDocument.MaximumCanvasFrameDimension ||
                (long)sourceCanvasSize.x * sourceCanvasSize.y > WhimTexDocument.MaximumCanvasFramePixels)
                throw new WhimTexDocumentException("Layer sourceCanvasSize must be [0,0] or 1..16384 per axis, at most 16,777,216 pixels: " + id);
        }
        [NonSerialized] internal LayerTransformCache transformCache;
        internal TextureTransform CanvasTransform
        {
            get
            {
                var value = transform;
                if (transformCache?.parent != null)
                {
                    value.storage = TransformStorage.Projective;
                    value.matrix = transformCache.world;
                }
                return value;
            }
        }
        internal TextureTransform RenderTransform
        {
            get
            {
                var value = PixelCanvasTransform;
                if ((RequiresInput || Behaviour is ShaderProcessorLayerBehaviour) && transformCache?.parent != null)
                    value.matrix = transformCache.world * transformCache.parentInverse;
                value.tiling = ResolveTiling(value.tiling);
                return value;
            }
        }
        public LayerFilterMode filterMode = LayerFilterMode.Source;
        internal TextureTransform PixelCanvasTransform => Behaviour is DrawingLayerBehaviour drawing
            ? drawing.GetPixelTransform(CanvasTransform) : CanvasTransform;
        [SerializeReference] private LayerBehaviour behaviour;
        [SerializeField] private string behaviourId;
        internal string BehaviourId => behaviourId;
        internal LayerBehaviour BehaviourWithoutBinding => behaviour;
        [SerializeReference] public List<Layer> children;
        [SerializeField] private bool group;
        public GroupCompositing compositing;
        // Converted Character text retains its original LayerToLocal FX frame.
        [SerializeField, HideInInspector] internal bool groupFxInLayerSpace;

        public Layer() { }
        public Layer(LayerBehaviour behaviour)
        {
            SetBehaviour(behaviour);
            behaviour?.InitializeLayer(this);
        }

        public LayerBehaviour Behaviour
        {
            get { behaviour?.Bind(this); return behaviour; }
        }

        public void SetBehaviour(LayerBehaviour value)
        {
            if (ReferenceEquals(behaviour, value)) return;
            if (value != null && !value.IsGroup && children != null && children.Count != 0)
                throw new InvalidOperationException("A group with children must be rasterized or ungrouped before changing its behaviour.");
            value?.ValidateOwner(this);
            behaviour?.OnDetached();
            value?.Bind(this);
            behaviour = value;
            if (value != null) behaviourId = value.RecoveryId;
            if (value != null) group = value.IsGroup;
            if (group) children ??= new List<Layer>();
        }

        internal LayerBehaviour DetachBehaviour()
        {
            var result = behaviour;
            behaviour = null;
            return result;
        }

        // The caller disposes any replaced owned assets/children before adopting prepared content.
        // Keep the wrapper itself: selections, effect targets and agent reservations refer to it.
        internal void AdoptContent(Layer prepared)
        {
            if (prepared == null || ReferenceEquals(prepared, this))
                throw new ArgumentException("A separate prepared layer is required.", nameof(prepared));
            string retainedId = id;
            string retainedName = layerName;
            bool retainedEnabled = enabled;
            children = null;
            SetBehaviour(prepared.DetachBehaviour());
            CopyRasterizedIdentityFrom(prepared);
            transform = prepared.transform;
            children = prepared.children;
            prepared.children = null;
            compositing = prepared.compositing;
            id = retainedId;
            layerName = retainedName;
            enabled = retainedEnabled;
        }

        public static implicit operator Layer(LayerBehaviour value) => value?.Owner;
        public static explicit operator DrawingLayerBehaviour(Layer value) => (DrawingLayerBehaviour)value?.Behaviour;
        public static explicit operator FileLayerBehaviour(Layer value) => (FileLayerBehaviour)value?.Behaviour;
        public static explicit operator ColorFillLayerBehaviour(Layer value) => (ColorFillLayerBehaviour)value?.Behaviour;
        public static explicit operator GradientLayerBehaviour(Layer value) => (GradientLayerBehaviour)value?.Behaviour;
        public static explicit operator TextLayerBehaviour(Layer value) => (TextLayerBehaviour)value?.Behaviour;
        public static explicit operator NoiseLayerBehaviour(Layer value) => (NoiseLayerBehaviour)value?.Behaviour;
        public static explicit operator OutlineLayerBehaviour(Layer value) => (OutlineLayerBehaviour)value?.Behaviour;
        public static explicit operator SDFLayerBehaviour(Layer value) => (SDFLayerBehaviour)value?.Behaviour;
        public static explicit operator NormalMapLayerBehaviour(Layer value) => (NormalMapLayerBehaviour)value?.Behaviour;
        public static explicit operator BlurLayerBehaviour(Layer value) => (BlurLayerBehaviour)value?.Behaviour;
        public static explicit operator SharpenLayerBehaviour(Layer value) => (SharpenLayerBehaviour)value?.Behaviour;
        public static explicit operator MakeSeamlessLayerBehaviour(Layer value) => (MakeSeamlessLayerBehaviour)value?.Behaviour;
        public static explicit operator ShaderProcessorLayerBehaviour(Layer value) => (ShaderProcessorLayerBehaviour)value?.Behaviour;
        public static explicit operator GroupLayerBehaviour(Layer value) => (GroupLayerBehaviour)value?.Behaviour;
        public static explicit operator PendingLayerBehaviour(Layer value) => (PendingLayerBehaviour)value?.Behaviour;
        public static explicit operator TargetedLayerBehaviour(Layer value) => (TargetedLayerBehaviour)value?.Behaviour;

        public string Id => id;

        /// <summary>Checks this layer's active FX only, not child layers or source generation.</summary>
        public bool AreActiveFxPixelLocal()
        {
            if (fx != null)
                foreach (var effect in fx)
                    if (effect != null && effect.Active && !effect.IsPixelLocal) return false;
            return true;
        }
        internal bool RequiresInput => Behaviour?.RequiresInput ?? false;
        internal bool IsGroup => behaviour?.IsGroup ?? group;
        internal Layer AsGroup() => IsGroup ? this : null;
        internal bool HasFx => fx != null && fx.Exists(effect => effect != null && effect.Active && !effect.IsUnavailable);
        internal bool IsPassThrough => Behaviour is not BooleanGroupLayerBehaviour && compositing == GroupCompositing.PassThrough && channelMapping.IsIdentity && !HasFx;
        internal BlendMode EffectiveBlendMode => Behaviour is not BooleanGroupLayerBehaviour && compositing == GroupCompositing.PassThrough ? BlendMode.Normal : blendMode;
        internal BlendMode CompositeBlendMode => Behaviour is DrawingLayerBehaviour drawing && drawing.UsesPremultipliedOverwrite
            ? (BlendMode)101 : blendMode;
        // Keep existing orphan clipping layers from attaching themselves to a baked Processor.
        // Explicitly enabling clipping on the Drawing opts into ordinary clipping semantics.
        internal bool IsClippingBarrier => Behaviour is ShaderProcessorLayerBehaviour ||
            !clippingMask && Behaviour is DrawingLayerBehaviour drawing && drawing.IsProcessorSnapshot;
        internal List<Layer> layers { get => children ??= new List<Layer>(); set => children = value; }

        internal void AssignNewId()
        {
            id = Guid.NewGuid().ToString("N");
            Behaviour?.AssignRecoveryId();
            if (behaviour != null) behaviourId = behaviour.RecoveryId;
        }

        internal void AdoptReservation(PendingLayerBehaviour reservation)
        {
            id = reservation.Id;
            layerName = reservation.layerName;
            enabled = reservation.enabled;
        }

        internal void EnsureId(HashSet<string> usedIds)
        {
            ValidateSourceCanvasSize();
            if (string.IsNullOrEmpty(id) || usedIds.Contains(id))
                id = Guid.NewGuid().ToString("N");
            usedIds.Add(id);
            fx ??= new List<ShaderFX>();
            behaviour?.Bind(this);
            if (behaviour != null) { behaviourId = behaviour.RecoveryId; group = behaviour.IsGroup; }
            if (group) children ??= new List<Layer>();
        }

        internal RenderTexture Render(in LayerRenderContext context)
        {
            LayerBehaviour renderer = Behaviour;
            if (renderer == null) return null;
            using var profile = WhimTexProfiler.Measure("layer.generate", context.activeDocument, this,
                detail: "Inclusive behaviour render: generation, transform and FX; GPU interval includes CPU submission gaps");
            // Dispatch still runs on empty inputs so behaviour-owned cleanup is preserved. These
            // known empty paths submit no pixels and should not acquire an elapsed GPU interval.
            bool empty = renderer is GroupLayerBehaviour || renderer is PendingLayerBehaviour ||
                (renderer.RequiresInput || renderer is ShaderProcessorLayerBehaviour) && context.input == null ||
                renderer is FileLayerBehaviour file && file.ResolveSourceTexture(context.activeDocument) == null;
            using var gpu = empty ? default : profile.Gpu();
            // Includes nested transform/FX intervals; it is not an exclusive GPU generation cost.
            return renderer.Render(context);
        }

        internal void SetRenderInverse(Material material, string prefix, in LayerRenderContext context)
        {
            if (!context.applyTransform) { ProjectiveMatrix.Identity.SetShader(material, prefix); return; }
            // This input is already in the same regional frame as the output.
            // FX coordinates still use the layer's complete canvas transform.
            if (context.activeDocument.RenderingViewport && context.activeDocument.CanGenerateRegionalProcessor(this))
            { ProjectiveMatrix.Identity.SetShader(material, prefix); return; }
            if (context.activeDocument.RenderingViewport)
            {
                RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height).TryInverse(out var viewportInverse);
                (viewportInverse * context.activeDocument.ViewportToCanvas).SetShader(material, prefix);
                return;
            }
            var cache = transformCache;
            if (cache != null && cache.document == context.activeDocument && cache.local.Equals(transform) &&
                !(Behaviour is DrawingLayerBehaviour drawing && drawing.HasBakedPixelFrame))
            {
                if (RequiresInput || Behaviour is ShaderProcessorLayerBehaviour) cache.inputInverseGpu.Set(material, prefix);
                else cache.inverseGpu.Set(material, prefix);
                return;
            }
            RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height).TryInverse(out var inverse);
            inverse.SetShader(material, prefix);
        }

        internal Texture SamplingSource => this?.Behaviour is FileLayerBehaviour file ? file.ResolvedTexture :
            this?.Behaviour is DrawingLayerBehaviour drawing ? drawing.StoredTexture : null;

        public TransformTilingMode EffectiveTiling => ResolveTiling(transform.tiling);
        internal TransformTilingMode ResolveTiling(TransformTilingMode requested) => requested == TransformTilingMode.Source
            ? Behaviour?.SourceTiling ?? TransformTilingMode.Clip : requested;

        internal FilterMode ResolveFilterMode(Texture fallback = null)
        {
            switch (filterMode)
            {
                case LayerFilterMode.Point: return FilterMode.Point;
                case LayerFilterMode.Bilinear: return FilterMode.Bilinear;
                case LayerFilterMode.Trilinear: return FilterMode.Trilinear;
                default:
                    Texture source = SamplingSource;
                    if (source == null)
                        source = fallback;
                    return source != null ? source.filterMode : FilterMode.Bilinear;
            }
        }

        internal bool TryGetOriginalAspectTransform(WhimTexDocument owner, out TextureTransform fitted, bool originalSize = false)
        {
            fitted = transform;
            if (owner == null || IsGroup)
                return false;
            Vector2 canvasSize = new Vector2(owner.width, owner.height);
            Vector2 sourceSize = canvasSize;
            Texture source = this?.Behaviour is FileLayerBehaviour file ? file.ResolveSourceTexture(owner) :
                this?.Behaviour is DrawingLayerBehaviour drawing ? drawing.StoredTexture : null;
            if (this?.Behaviour is FileLayerBehaviour && source == null)
                return false;
            if (this?.Behaviour is FileLayerBehaviour && source != null)
                source = owner.ResolveOriginalFileTexture(source as Texture2D);
            if (source != null)
                sourceSize = new Vector2(source.width, source.height);
            return transform.TryFitOriginalAspect(canvasSize, sourceSize, out fitted, originalSize);
        }

        internal void CopyRasterizedIdentityFrom(Layer source)
        {
            id = source.id;
            layerName = source.layerName;
            enabled = source.enabled;
            clippingMask = source.clippingMask;
            opacity = source.opacity;
            blendMode = source.blendMode;
            colorRange = source.colorRange;
            blendRange = source.blendRange;
            channelMapping = source.channelMapping;
            filterMode = source.filterMode;
            booleanOperation = source.booleanOperation;
            sourceCanvasSize = source.sourceCanvasSize;
            sourcePixelScale = source.sourcePixelScale;
            sourceCanvasScale = source.sourceCanvasScale;
            fx = source.fx == null ? new List<ShaderFX>() : new List<ShaderFX>(source.fx);
        }

        public Texture2D GetPreviewTexture(int size)
        {
            return Behaviour?.GetPreviewTexture(size);
        }

        internal void ReleaseTransientResources()
        {
            Behaviour?.ReleaseTransientResources();
        }

        internal RenderTexture ApplyTransformAndFx(Texture source, in LayerRenderContext context, ProjectiveMatrix? sourceToLocal = null,
            bool premultipliedSource = false, Texture straightFallback = null, bool decodeFallback = false)
        {
            if (source == null)
                return null;

            RenderTexture current = ProfiledRenderTexture.GetTemporary(
                context.width,
                context.height,
                0,
                RenderTextureFormat.ARGBFloat,
                RenderTextureReadWrite.Linear);
            FilterMode resolvedFilter = ResolveFilterMode(source);
            current.filterMode = resolvedFilter;
            current.wrapMode = TextureWrapMode.Clamp;

            try
            {
                using (var profile = WhimTexProfiler.Measure("layer.transform", context.activeDocument, this,
                    detail: "Resampling blit and finishing passes; excludes FX"))
                {
                    Material transformMaterial = WhimTexMaterials.Transform;
                    if (transformMaterial != null)
                    {
                        ConfigureSourceTransform(transformMaterial, source, context, sourceToLocal,
                            premultipliedSource, straightFallback, decodeFallback);
                    }
                    using (profile.Gpu())
                    {
                        if (transformMaterial == null) Graphics.Blit(source, current);
                        else Graphics.Blit(source, current, transformMaterial, 0);
                        current = context.activeDocument.FinishStage(current);
                    }
                }

                ApplyFx(ref current, context, resolvedFilter);
                return current;
            }
            catch
            {
                ProfiledRenderTexture.ReleaseTemporary(current);
                throw;
            }
        }

        internal void ConfigureSourceTransform(Material transformMaterial, Texture source, in LayerRenderContext context,
            ProjectiveMatrix? sourceToLocal = null, bool premultipliedSource = false,
            Texture straightFallback = null, bool decodeFallback = false)
        {
            FilterMode resolvedFilter = ResolveFilterMode(source);
            transformMaterial.SetFloat("_PremultipliedSource", premultipliedSource ? 1 : 0);
            transformMaterial.SetFloat("_StraightFallback", straightFallback != null ? decodeFallback ? 2 : 1 : 0);
            transformMaterial.SetTexture("_OriginalStraight", straightFallback);
            transformMaterial.SetFloat("_DecodeSource", source is Texture2D t &&
                UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat) ? 1f : 0f);
            // Even identity transforms must use the independent filter/wrap settings.
            TextureTransform applied = context.applyTransform ? RenderTransform : TextureTransform.Default;
            applied.tiling = ResolveTiling(applied.tiling);
            Texture samplingSource = SamplingSource;
            TextureWrapMode wrapU = TextureWrapMode.Clamp;
            TextureWrapMode wrapV = TextureWrapMode.Clamp;
            switch (applied.tiling)
            {
                case TransformTilingMode.Source:
                    if (samplingSource != null)
                    {
                        wrapU = samplingSource.wrapModeU;
                        wrapV = samplingSource.wrapModeV;
                    }
                    break;
                case TransformTilingMode.Repeat:
                    wrapU = wrapV = TextureWrapMode.Repeat;
                    break;
                case TransformTilingMode.Mirror:
                    wrapU = wrapV = TextureWrapMode.Mirror;
                    break;
            }
            if (sourceToLocal.HasValue)
            {
                var localToCanvas = context.applyTransform ? RenderTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height) : ProjectiveMatrix.Identity;
                (localToCanvas * sourceToLocal.Value).TryInverse(out var sourceInverse);
                if (context.applyTransform && context.activeDocument.RenderingViewport)
                    sourceInverse *= context.activeDocument.ViewportToCanvas;
                sourceInverse.SetShader(transformMaterial, "_TransformRow");
            }
            else SetRenderInverse(transformMaterial, "_TransformRow", context);
            transformMaterial.SetInt("_ClipOutside", applied.tiling == TransformTilingMode.Clip || applied.tiling == TransformTilingMode.Unbounded ? 1 : 0);
            transformMaterial.SetInt("_WrapModeU", (int)wrapU);
            transformMaterial.SetInt("_WrapModeV", (int)wrapV);
            transformMaterial.SetInt("_FilterMode", (int)resolvedFilter);
            transformMaterial.SetInt("_SourceMipCount", source is Texture2D texture ? texture.mipmapCount : 1);
            transformMaterial.SetVector("_OutputSize", new Vector4(context.width, context.height, 0f, 0f));
        }

        internal ShaderFX DiagnosticEffect
        {
            get
            {
                ShaderFX warning = null;
                if (fx != null)
                    foreach (var fxEntry in fx)
                        if (fxEntry is ShaderFX effect)
                        {
                            if (effect.IsUnavailable) return effect;
                            if (warning == null && effect.DiagnosticNotice != null) warning = effect;
                        }
                return warning;
            }
        }

        internal void ApplyFx(ref RenderTexture current, in LayerRenderContext context, FilterMode filter = FilterMode.Bilinear,
            int count = int.MaxValue, int start = 0)
        {
            if (!context.applyFx || fx == null || fx.Count == 0 || count <= 0 || current == null)
                return;
            using var block = WhimTexProfiler.Measure("fx.block", context.activeDocument, this);
            using var blockGpu = block.Gpu();
            count = Math.Min(count, context.activeDocument.ViewportFxLimit(this));
            for (int i = start; i < fx.Count && i < count; i++)
            {
                var effect = fx[i];
                if (effect == null || !effect.Active || effect.IsUnavailable) continue;
                using var profile = WhimTexProfiler.Measure("fx.render", context.activeDocument, this, i,
                    WhimTexProfiler.Recording && fx[i] != null ? fx[i].name : null);
                ShaderTextureBindings textureInputs = null;
                RenderTexture next = null;
                try
                {
                    Material material;
                    using (WhimTexProfiler.Measure("fx.prepare"))
                    {
                        textureInputs = context.activeDocument.BindShaderTextureLayers(effect, this, context, current);
                        material = effect.GetMaterial(context);
                        if (material == null) continue;
                        textureInputs?.Apply(material);
                        {
                            material.SetFloat("_WhimTex_InputFilter", current.filterMode == FilterMode.Point ? 0f : 1f);
                            if (!context.transformFxCoordinates && !(IsGroup && groupFxInLayerSpace))
                                ProjectiveMatrix.Identity.SetShader(material, "_WhimTex_LayerToLocalRow");
                            else if (Behaviour is DrawingLayerBehaviour drawing && drawing.HasBakedFxFrame)
                                drawing.GetBakedFxInverse(context.activeDocument.width, context.activeDocument.height).SetShader(material, "_WhimTex_LayerToLocalRow");
                            else if (transformCache != null)
                                transformCache.inverseGpu.Set(material, "_WhimTex_LayerToLocalRow");
                            else
                            {
                                CanvasTransform.ToMatrix(context.activeDocument.width, context.activeDocument.height).TryInverse(out var inverse);
                                inverse.SetShader(material, "_WhimTex_LayerToLocalRow");
                            }
                        }
                        next = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                            RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                        next.filterMode = filter;
                        next.wrapMode = TextureWrapMode.Clamp;
                        ShaderFXPreviewRendering.Capture(effect, this, i, context, material, current);
                    }
                    using (profile.Gpu())
                    using (WhimTexProfiler.Measure("fx.submit"))
                    {
                        Graphics.Blit(current, next, material);
                        ProfiledRenderTexture.ReleaseTemporary(current);
                        current = next; next = null;
                        current = context.activeDocument.FinishStage(current);
                    }
                }
                finally
                {
                    if (next != null) ProfiledRenderTexture.ReleaseTemporary(next);
                    textureInputs?.Dispose();
                }
            }
        }
    }
    [Serializable]
    public abstract class LayerBehaviour
    {
        [NonSerialized] private Layer owner;
        [SerializeField] private string recoveryId = Guid.NewGuid().ToString("N");
        internal string RecoveryId => recoveryId ??= Guid.NewGuid().ToString("N");
        internal void AssignRecoveryId() => recoveryId = Guid.NewGuid().ToString("N");
        public Layer Owner => owner ?? new Layer(this);
        internal Layer BoundOwner => owner;
        internal LayerBehaviour Behaviour => this;
        internal void Bind(Layer layer)
        {
            ValidateOwner(layer);
            owner = layer;
        }
        internal void ValidateOwner(Layer layer)
        {
            if (owner != null && !ReferenceEquals(owner, layer) && ReferenceEquals(owner.BehaviourWithoutBinding, this))
                throw new InvalidOperationException("A layer behaviour cannot be shared by two layers.");
        }

        internal virtual void InitializeLayer(Layer layer) { }
        public virtual TransformTilingMode SourceTiling => TransformTilingMode.Clip;
        public string Id => Owner.Id;
        public string layerName { get => Owner.layerName; set => Owner.layerName = value; }
        public bool enabled { get => Owner.enabled; set => Owner.enabled = value; }
        public bool clippingMask { get => Owner.clippingMask; set => Owner.clippingMask = value; }
        public float opacity { get => Owner.opacity; set => Owner.opacity = value; }
        public BlendMode blendMode { get => Owner.blendMode; set => Owner.blendMode = value; }
        public LayerColorRange colorRange { get => Owner.colorRange; set => Owner.colorRange = value; }
        public LayerBlendRange blendRange { get => Owner.blendRange; set => Owner.blendRange = value; }
        public ref LayerChannelMapping channelMapping => ref Owner.channelMapping;
        public ref TextureTransform transform => ref Owner.transform;
        public LayerFilterMode filterMode { get => Owner.filterMode; set => Owner.filterMode = value; }
        public List<ShaderFX> fx { get => Owner.fx; set => Owner.fx = value; }
        internal virtual bool RequiresInput => false;
        internal virtual bool IsGroup => false;
        internal Layer AsGroup() => Owner.AsGroup();
        internal void AssignNewId() => Owner.AssignNewId();
        internal void AdoptReservation(PendingLayerBehaviour reservation) => Owner.AdoptReservation(reservation);
        internal void CopyRasterizedIdentityFrom(Layer source) => Owner.CopyRasterizedIdentityFrom(source);
        internal Texture SamplingSource => Owner.SamplingSource;
        internal FilterMode ResolveFilterMode(Texture fallback = null) => Owner.ResolveFilterMode(fallback);
        internal bool TryGetOriginalAspectTransform(WhimTexDocument document, out TextureTransform fitted) => Owner.TryGetOriginalAspectTransform(document, out fitted);
        internal RenderTexture ApplyTransformAndFx(Texture source, in LayerRenderContext context) => Owner.ApplyTransformAndFx(source, context);
        internal abstract RenderTexture Render(in LayerRenderContext context);
        public virtual Texture2D GetPreviewTexture(int size) => null;
        internal virtual void ReleaseTransientResources() { }
        internal virtual void OnDetached() => ReleaseTransientResources();
    }
    [Serializable]
    public abstract class TargetedLayerBehaviour : LayerBehaviour
    {
        [SerializeField] private string targetLayerId;

        public EffectInputMode inputMode = EffectInputMode.Previous;
        public string TargetLayerId
        {
            get => targetLayerId;
            set => targetLayerId = value;
        }

        internal sealed override bool RequiresInput => true;
        internal virtual bool RequiresColorInput => false;
    }
}
