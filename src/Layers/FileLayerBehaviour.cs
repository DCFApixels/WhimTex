using UnityEngine;

namespace DCFApixels.WhimTex
{
    using UnityEngine.Experimental.Rendering;
    [System.Serializable]
    public sealed class FileLayerBehaviour : LayerBehaviour
    {
        public Texture2D sourceTexture;
        public string externalSourcePath;
        internal bool HasExternalSource => !string.IsNullOrEmpty(externalSourcePath);
        internal Texture2D ResolvedTexture => ResolveSourceTexture(BoundOwner?.transformCache?.document);
        internal Texture2D ResolveSourceTexture(WhimTexDocument owner) => HasExternalSource
            ? owner != null ? owner.ResolveExternalFileTexture(externalSourcePath) : null
            : sourceTexture;
        public override TransformTilingMode SourceTiling => TransformTilingMode.Source;
        [SerializeField] private bool sourceAssigned;
        [SerializeField, HideInInspector] internal string portableAssetGuid;
        [SerializeField, HideInInspector] internal string portableAssetLocalId;

        internal void AssignSourceTexture(Texture2D texture, WhimTexDocument owner, bool initializeCanvas = false)
        {
            bool wasEmpty = sourceTexture == null && !HasExternalSource;
            bool changed = HasExternalSource || sourceTexture != texture;
            externalSourcePath = null;
            portableAssetGuid = null;
            portableAssetLocalId = null;
            Texture2D sourceForSizing = texture;
            if (texture != null && owner != null)
                sourceForSizing = owner.ResolveOriginalFileTexture(texture);
            if (initializeCanvas && wasEmpty && !sourceAssigned && texture != null && CanInitializeCanvas(owner))
            {
                owner.width = Mathf.Max(1, sourceForSizing != null ? sourceForSizing.width : texture.width);
                owner.height = Mathf.Max(1, sourceForSizing != null ? sourceForSizing.height : texture.height);
            }
            sourceAssigned |= sourceTexture != null || texture != null;
            sourceTexture = texture;
            if (changed && texture != null && GraphicsFormatUtility.IsHDRFormat(texture.graphicsFormat))
            {
                colorRange = LayerColorRange.HDR;
                blendRange = LayerBlendRange.HDR;
            }
            if (wasEmpty && texture != null && TryGetOriginalAspectTransform(owner, out TextureTransform fitted))
                transform = fitted;
        }

        internal void AssignSourcePath(string path, WhimTexDocument owner, bool initializeCanvas = false)
        {
            var texture = FileLayerSource.Prepare(owner, path);
            var asset = FileLayerSource.ProjectTexture(path);
            if (asset != null)
            {
                AssignSourceTexture(asset, owner, initializeCanvas);
                return;
            }
            path = FileLayerSource.NormalizeExternalPath(path);
            bool wasEmpty = sourceTexture == null && !HasExternalSource;
            bool changed = sourceTexture != null || externalSourcePath != path;
            if (initializeCanvas && wasEmpty && !sourceAssigned && CanInitializeCanvas(owner))
            { owner.width = texture.width; owner.height = texture.height; }
            sourceAssigned = true;
            sourceTexture = null;
            portableAssetGuid = portableAssetLocalId = null;
            externalSourcePath = path;
            if (changed && GraphicsFormatUtility.IsHDRFormat(texture.graphicsFormat))
            { colorRange = LayerColorRange.HDR; blendRange = LayerBlendRange.HDR; }
            if (wasEmpty && TryGetOriginalAspectTransform(owner, out TextureTransform fitted)) transform = fitted;
        }

        private bool CanInitializeCanvas(WhimTexDocument owner)
        {
            if (owner == null) return false;
            if (owner.layers != null)
                foreach (Layer layer in owner.layers)
                    if (layer != null && !ReferenceEquals(layer, Owner))
                        return false;
            return true;
        }

        public override Texture2D GetPreviewTexture(int size)
        {
            return ResolvedTexture;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Texture2D input = ResolveSourceTexture(context.activeDocument);
            if (input == null)
                return null;
            Texture2D source = !HasExternalSource && context.activeDocument != null
                ? context.activeDocument.ResolveOriginalFileTexture(input) : input;
            return ApplyTransformAndFx(source != null ? source : input, context);
        }
    }
}
