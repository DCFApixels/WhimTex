using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexDocument
    {
        [SerializeField, HideInInspector] private Texture2D outputTexture;

        public Texture2D OutputTexture => outputTexture;

        internal static event System.Action<WhimTexDocumentOutputChange> OutputTextureChanged;

        internal void NotifyOutputTextureChanged()
        {
            if (outputTexture != null)
                OutputTextureChanged?.Invoke(new WhimTexDocumentOutputChange(this));
        }
    }
}
