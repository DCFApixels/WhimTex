using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    public enum LayerColorRange { Standard, HDR }
    public enum LayerBlendRange { Standard, HDR }
    internal readonly struct LayerRenderContext
    {
        public readonly TextureCompositor compositor;
        public readonly RenderTexture input;
        public readonly int width;
        public readonly int height;
        public readonly float scaleMultiplier;
        public readonly bool applyTransform;
        public readonly bool applyModifiers;

        public LayerRenderContext(
            TextureCompositor compositor,
            RenderTexture input,
            int width,
            int height,
            float scaleMultiplier,
            bool applyTransform = true,
            bool applyModifiers = true)
        {
            this.compositor = compositor;
            this.input = input;
            this.width = width;
            this.height = height;
            this.scaleMultiplier = Mathf.Max(0.0001f, scaleMultiplier);
            this.applyTransform = applyTransform;
            this.applyModifiers = applyModifiers;
        }
    }

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "Layer")]
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
        public LayerSwizzle swizzle;
        public List<UnityEngine.Object> modifiers = new List<UnityEngine.Object>();
        public TextureTransform transform = TextureTransform.Default;
        public LayerFilterMode filterMode = LayerFilterMode.Source;
        [SerializeReference] private LayerBehaviour behaviour;
        [SerializeField] private string behaviourId;
        internal string BehaviourId => behaviourId;
        internal LayerBehaviour BehaviourWithoutBinding => behaviour;
        [SerializeReference] public List<Layer> children;
        [SerializeField] private bool group;
        public GroupCompositing compositing;

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
            if (value != null && !(value is GroupLayerBehaviour) && children != null && children.Count != 0)
                throw new InvalidOperationException("A group with children must be rasterized or ungrouped before changing its behaviour.");
            value?.ValidateOwner(this);
            behaviour?.OnDetached();
            value?.Bind(this);
            behaviour = value;
            if (value != null) behaviourId = value.RecoveryId;
            if (value != null) group = value is GroupLayerBehaviour;
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
        public static explicit operator NoiseLayerBehaviour(Layer value) => (NoiseLayerBehaviour)value?.Behaviour;
        public static explicit operator OutlineLayerBehaviour(Layer value) => (OutlineLayerBehaviour)value?.Behaviour;
        public static explicit operator SDFLayerBehaviour(Layer value) => (SDFLayerBehaviour)value?.Behaviour;
        public static explicit operator NormalMapLayerBehaviour(Layer value) => (NormalMapLayerBehaviour)value?.Behaviour;
        public static explicit operator BlurLayerBehaviour(Layer value) => (BlurLayerBehaviour)value?.Behaviour;
        public static explicit operator MakeSeamlessLayerBehaviour(Layer value) => (MakeSeamlessLayerBehaviour)value?.Behaviour;
        public static explicit operator ShaderProcessorLayerBehaviour(Layer value) => (ShaderProcessorLayerBehaviour)value?.Behaviour;
        public static explicit operator GroupLayerBehaviour(Layer value) => (GroupLayerBehaviour)value?.Behaviour;
        public static explicit operator PendingLayerBehaviour(Layer value) => (PendingLayerBehaviour)value?.Behaviour;
        public static explicit operator TargetedLayerBehaviour(Layer value) => (TargetedLayerBehaviour)value?.Behaviour;

        public string Id => id;
        internal bool RequiresInput => Behaviour?.RequiresInput ?? false;
        internal bool IsGroup => group;
        internal Layer AsGroup() => group ? this : null;
        internal bool HasModifiers => modifiers != null && modifiers.Exists(value => value != null);
        internal bool IsPassThrough => compositing == GroupCompositing.PassThrough && swizzle.IsIdentity && !HasModifiers;
        internal BlendMode EffectiveBlendMode => compositing == GroupCompositing.PassThrough ? BlendMode.Normal : blendMode;
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
            if (string.IsNullOrEmpty(id) || usedIds.Contains(id))
                id = Guid.NewGuid().ToString("N");
            usedIds.Add(id);
            modifiers ??= new List<UnityEngine.Object>();
            behaviour?.Bind(this);
            if (behaviour != null) behaviourId = behaviour.RecoveryId;
            if (group) children ??= new List<Layer>();
        }

        internal RenderTexture Render(in LayerRenderContext context) => Behaviour?.Render(context);

        internal Texture SamplingSource => this?.Behaviour is FileLayerBehaviour file ? file.sourceTexture :
            this?.Behaviour is DrawingLayerBehaviour drawing ? drawing.StoredTexture : null;

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

        internal bool TryGetOriginalAspectTransform(TextureCompositor owner, out TextureTransform fitted, bool originalSize = false)
        {
            fitted = transform;
            if (owner == null || IsGroup)
                return false;
            Vector2 canvasSize = new Vector2(owner.width, owner.height);
            Vector2 sourceSize = canvasSize;
            Texture source = this?.Behaviour is FileLayerBehaviour file ? file.sourceTexture :
                this?.Behaviour is DrawingLayerBehaviour drawing ? drawing.StoredTexture : null;
            if (this?.Behaviour is FileLayerBehaviour && source == null)
                return false;
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
            swizzle = source.swizzle;
            filterMode = source.filterMode;
            modifiers = source.modifiers == null ? new List<UnityEngine.Object>() : new List<UnityEngine.Object>(source.modifiers);
        }

        public Texture2D GetPreviewTexture(int size)
        {
            return Behaviour?.GetPreviewTexture(size);
        }

        internal void ReleaseTransientResources()
        {
            Behaviour?.ReleaseTransientResources();
        }

        internal RenderTexture ApplyTransformAndModifiers(Texture source, in LayerRenderContext context)
        {
            if (source == null)
                return null;

            RenderTexture current = RenderTexture.GetTemporary(
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
                Material transformMaterial = WhimTexMaterials.Transform;
                if (transformMaterial != null)
                    transformMaterial.SetFloat("_DecodeSource", source is Texture2D t &&
                        UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat) ? 1f : 0f);
                if (transformMaterial == null)
                {
                    Graphics.Blit(source, current);
                }
                else
                {
                    // Even identity transforms must use the independent filter/wrap settings.
                    TextureTransform applied = context.applyTransform ? transform : TextureTransform.Default;
                    Texture samplingSource = SamplingSource;
                    if (samplingSource == null)
                        samplingSource = source;
                    TextureWrapMode wrapU = TextureWrapMode.Clamp;
                    TextureWrapMode wrapV = TextureWrapMode.Clamp;
                    switch (applied.tiling)
                    {
                        case TransformTilingMode.Source:
                            wrapU = samplingSource.wrapModeU;
                            wrapV = samplingSource.wrapModeV;
                            break;
                        case TransformTilingMode.Repeat:
                            wrapU = wrapV = TextureWrapMode.Repeat;
                            break;
                        case TransformTilingMode.Mirror:
                            wrapU = wrapV = TextureWrapMode.Mirror;
                            break;
                    }
                    if (!applied.ToMatrix(context.compositor.width, context.compositor.height).TryInverse(out var inverse))
                        inverse = default;
                    inverse.SetShader(transformMaterial, "_TransformRow");
                    transformMaterial.SetInt("_ClipOutside", applied.tiling == TransformTilingMode.Clip || applied.tiling == TransformTilingMode.Unbounded ? 1 : 0);
                    transformMaterial.SetInt("_WrapModeU", (int)wrapU);
                    transformMaterial.SetInt("_WrapModeV", (int)wrapV);
                    transformMaterial.SetInt("_FilterMode", (int)resolvedFilter);
                    transformMaterial.SetInt("_SourceMipCount", source is Texture2D texture ? texture.mipmapCount : 1);
                    transformMaterial.SetVector("_OutputSize", new Vector4(context.width, context.height, 0f, 0f));
                    Graphics.Blit(source, current, transformMaterial);
                }

                current = context.compositor.FinishStage(current);
                ApplyModifiers(ref current, context, resolvedFilter);
                return current;
            }
            catch
            {
                RenderTexture.ReleaseTemporary(current);
                throw;
            }
        }

        internal void ApplyModifiers(ref RenderTexture current, in LayerRenderContext context, FilterMode filter = FilterMode.Bilinear)
        {
            if (!context.applyModifiers || modifiers == null || current == null)
                return;
            for (int i = 0; i < modifiers.Count; i++)
            {
                Material modifier = modifiers[i] is ShaderFX shaderFX
                    ? shaderFX.GetMaterial(context)
                    : modifiers[i] as Material;
                if (modifier == null)
                    continue;

                RenderTexture next = RenderTexture.GetTemporary(
                    context.width,
                    context.height,
                    0,
                    RenderTextureFormat.ARGBFloat,
                    RenderTextureReadWrite.Linear);
                next.filterMode = filter;
                next.wrapMode = TextureWrapMode.Clamp;
                try
                {
                    Graphics.Blit(current, next, modifier);
                }
                catch
                {
                    RenderTexture.ReleaseTemporary(next);
                    throw;
                }
                RenderTexture.ReleaseTemporary(current);
                current = next;
                current = context.compositor.FinishStage(current);
            }
        }
    }

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "LayerBehaviour")]
    [Serializable]
    public abstract class LayerBehaviour
    {
        [NonSerialized] private Layer owner;
        [SerializeField] private string recoveryId = Guid.NewGuid().ToString("N");
        internal string RecoveryId => recoveryId ??= Guid.NewGuid().ToString("N");
        internal void AssignRecoveryId() => recoveryId = Guid.NewGuid().ToString("N");
        public Layer Owner => owner ?? new Layer(this);
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
        public string Id => Owner.Id;
        public string layerName { get => Owner.layerName; set => Owner.layerName = value; }
        public bool enabled { get => Owner.enabled; set => Owner.enabled = value; }
        public bool clippingMask { get => Owner.clippingMask; set => Owner.clippingMask = value; }
        public float opacity { get => Owner.opacity; set => Owner.opacity = value; }
        public BlendMode blendMode { get => Owner.blendMode; set => Owner.blendMode = value; }
        public LayerColorRange colorRange { get => Owner.colorRange; set => Owner.colorRange = value; }
        public LayerBlendRange blendRange { get => Owner.blendRange; set => Owner.blendRange = value; }
        public ref LayerSwizzle swizzle => ref Owner.swizzle;
        public ref TextureTransform transform => ref Owner.transform;
        public LayerFilterMode filterMode { get => Owner.filterMode; set => Owner.filterMode = value; }
        public List<UnityEngine.Object> modifiers { get => Owner.modifiers; set => Owner.modifiers = value; }
        internal virtual bool RequiresInput => false;
        internal virtual bool IsGroup => false;
        internal Layer AsGroup() => Owner.AsGroup();
        internal void AssignNewId() => Owner.AssignNewId();
        internal void AdoptReservation(PendingLayerBehaviour reservation) => Owner.AdoptReservation(reservation);
        internal void CopyRasterizedIdentityFrom(Layer source) => Owner.CopyRasterizedIdentityFrom(source);
        internal Texture SamplingSource => Owner.SamplingSource;
        internal FilterMode ResolveFilterMode(Texture fallback = null) => Owner.ResolveFilterMode(fallback);
        internal bool TryGetOriginalAspectTransform(TextureCompositor document, out TextureTransform fitted) => Owner.TryGetOriginalAspectTransform(document, out fitted);
        internal RenderTexture ApplyTransformAndModifiers(Texture source, in LayerRenderContext context) => Owner.ApplyTransformAndModifiers(source, context);
        internal abstract RenderTexture Render(in LayerRenderContext context);
        public virtual Texture2D GetPreviewTexture(int size) => null;
        internal virtual void ReleaseTransientResources() { }
        internal virtual void OnDetached() => ReleaseTransientResources();
    }

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "TargetedLayerBehaviour")]
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
