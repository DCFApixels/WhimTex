using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexOutputPreview : IDisposable
    {
        private Material material;
        private RenderTexture result;
        private Texture2D source;
        private uint revision;
        private int mip = -1, channel = -1;

        internal Texture Get(Texture2D texture, int selectedChannel, int selectedMip)
        {
            if (texture == null || (selectedChannel == 0 && selectedMip == 0))
            {
                ReleaseTexture();
                return texture;
            }
            selectedMip = Mathf.Clamp(selectedMip, 0, texture.mipmapCount - 1);
            if (source == texture && revision == texture.updateCount && mip == selectedMip && channel == selectedChannel && result != null && result.IsCreated())
                return result;
            if (material == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.dcfapixels.whimtex/src/Shaders/OutputPreview.shader");
                if (shader == null || !shader.isSupported) return texture;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            int w = Mathf.Max(1, texture.width >> selectedMip), h = Mathf.Max(1, texture.height >> selectedMip);
            float scale = Mathf.Min(1, 1024f / Mathf.Max(w, h));
            w = Mathf.Max(1, Mathf.RoundToInt(w * scale)); h = Mathf.Max(1, Mathf.RoundToInt(h * scale));
            if (result == null || result.width != w || result.height != h || !result.IsCreated())
            {
                ReleaseTexture();
                result = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, name = "WhimTex Output Preview" };
                result.Create();
            }
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                material.SetFloat("_Mip", selectedMip);
                material.SetFloat("_Channel", selectedChannel);
                GL.sRGBWrite = result.sRGB;
                Graphics.Blit(texture, result, material);
                source = texture; revision = texture.updateCount; mip = selectedMip; channel = selectedChannel;
                return result;
            }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSrgb; }
        }

        internal void ReleaseTexture()
        {
            if (result != null) { result.Release(); UnityEngine.Object.DestroyImmediate(result); }
            result = null; source = null; mip = channel = -1;
        }

        public void Dispose()
        {
            ReleaseTexture();
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            material = null;
        }
    }
}
