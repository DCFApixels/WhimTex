using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class ProceduralLayerThumbnail : IDisposable
    {
        private Texture2D texture;
        private int settingsHash;

        internal Texture2D Get(LayerBehaviour layer, int size, int hash)
        {
            size = Mathf.Clamp(size, 1, 128);
            if (texture != null && texture.width == size && settingsHash == hash) return texture;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            TextureCompositor document = null;
            RenderTexture rendered = null, reduced = null;
            try
            {
                GL.sRGBWrite = false;
                document = ScriptableObject.CreateInstance<TextureCompositor>();
                document.hideFlags = HideFlags.HideAndDontSave;
                int resolution = Mathf.Max(64, size * 2);
                document.width = document.height = resolution;
                var context = new LayerRenderContext(document, null, resolution, resolution, 1f,
                    applyTransform: false, applyModifiers: false);
                rendered = layer.Render(context);
                if (rendered == null) return null;
                reduced = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                Graphics.Blit(rendered, reduced);
                Texture2D next = TextureCompositor.CopyToTexture2D(reduced);
                Dispose();
                texture = next;
                settingsHash = hash;
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (reduced != null) RenderTexture.ReleaseTemporary(reduced);
                if (rendered != null) RenderTexture.ReleaseTemporary(rendered);
                if (document != null) UnityEngine.Object.DestroyImmediate(document);
            }
        }

        public void Dispose()
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
