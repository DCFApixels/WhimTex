using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    using UnityEngine.Experimental.Rendering;

    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "FileLayerBehaviour")]
    [System.Serializable]
    public sealed class FileLayerBehaviour : LayerBehaviour
    {
        public Texture2D sourceTexture;
        [SerializeField] private bool sourceAssigned;
        [SerializeField, HideInInspector] internal string portableAssetGuid;
        [SerializeField, HideInInspector] internal string portableAssetLocalId;

        internal void AssignSourceTexture(Texture2D texture, TextureCompositor owner, bool initializeCanvas = false)
        {
            portableAssetGuid = null;
            portableAssetLocalId = null;
            bool wasEmpty = sourceTexture == null;
            bool changed = sourceTexture != texture;
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

        private bool CanInitializeCanvas(TextureCompositor owner)
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
            return sourceTexture;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            if (sourceTexture == null)
                return null;
            Texture2D source = context.compositor != null
                ? context.compositor.ResolveOriginalFileTexture(sourceTexture)
                : sourceTexture;
            return ApplyTransformAndModifiers(source != null ? source : sourceTexture, context);
        }
    }
}
