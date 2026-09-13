using System;
using UnityEditor;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    [InitializeOnLoad]
    internal static class BlueNoiseTextures
    {
        private static Texture2D twoD, oneD;

        static BlueNoiseTextures()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        internal static Texture2D TwoD => twoD != null ? twoD : twoD = Create(128, 128, BlueNoiseData.TwoD);
        internal static Texture2D OneD => oneD != null ? oneD : oneD = Create(256, 1, BlueNoiseData.OneD);

        private static Texture2D Create(int width, int height, string data)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "WhimTex Blue Noise " + width + "x" + height,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            try
            {
                texture.LoadRawTextureData(Convert.FromBase64String(data));
                texture.Apply(false, true);
                return texture;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
        }

        private static void Release()
        {
            if (twoD != null) UnityEngine.Object.DestroyImmediate(twoD);
            if (oneD != null) UnityEngine.Object.DestroyImmediate(oneD);
            twoD = oneD = null;
        }
    }
}
