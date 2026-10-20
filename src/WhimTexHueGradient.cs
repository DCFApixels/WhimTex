using UnityEditor;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [InitializeOnLoad]
    internal static class WhimTexHueGradient
    {
        internal const int Resolution = 1536;
        private static readonly int TextureId = Shader.PropertyToID("_WhimTexHueGradient");
        private static readonly int SizeId = Shader.PropertyToID("_WhimTexHueGradient_TexelSize");
        private static Texture2D texture;

        static WhimTexHueGradient()
        {
            EditorApplication.delayCall += Ensure;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        internal static void Ensure()
        {
            if (texture == null)
            {
                var pixels = new Color[Resolution];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.HSVToRGB(i / (float)Resolution, 1f, 1f);
                texture = new Texture2D(Resolution, 1, TextureFormat.RGBAFloat, false, true)
                {
                    name = "WhimTex Hue Gradient", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, anisoLevel = 0
                };
                texture.SetPixels(pixels); texture.Apply(false, true);
            }
            if (Shader.GetGlobalTexture(TextureId) != texture)
            {
                Shader.SetGlobalTexture(TextureId, texture);
                Shader.SetGlobalVector(SizeId, new Vector4(1f / Resolution, 1f, Resolution, 1f));
            }
        }

        private static void Release()
        {
            if (Shader.GetGlobalTexture(TextureId) == texture) Shader.SetGlobalTexture(TextureId, null);
            if (texture != null) Object.DestroyImmediate(texture);
            texture = null;
        }
    }
}
