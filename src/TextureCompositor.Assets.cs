using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositor
    {
        [SerializeField, HideInInspector] private Texture2D outputTexture;

        public Texture2D OutputTexture => outputTexture;

        internal static event System.Action<CompositorOutputChange> OutputTextureChanged;

        internal void NotifyOutputTextureChanged()
        {
            if (outputTexture != null)
                OutputTextureChanged?.Invoke(new CompositorOutputChange(this));
        }
    }
}
