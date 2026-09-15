using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed class WhimTexGradientTexture : IDisposable
    {
        private readonly int width;
        private readonly Color[] ramp, pixels;
        private WhimTexGradient source;
        private uint revision;
        private ColorSpace outputSpace;
        private Texture2D texture;
        public int BakeCount { get; private set; }

        public WhimTexGradientTexture(int width = 512)
        {
            if (width < 2 || width > 16384) throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width; ramp = new Color[width]; pixels = new Color[width * 2];
        }

        public Texture2D GetTexture(WhimTexGradient gradient, ColorSpace space = ColorSpace.Linear)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            if (space != ColorSpace.Linear && space != ColorSpace.Gamma) throw new ArgumentOutOfRangeException(nameof(space));
            if (texture != null && ReferenceEquals(source, gradient) && revision == gradient.Revision && outputSpace == space) return texture;
            gradient.Bake(ramp);
            for (int i = 0; i < width; i++)
            {
                Color c = ramp[i];
                if (gradient.ColorSpace != space) c = space == ColorSpace.Linear ? c.linear : c.gamma;
                c.r = Mathf.Clamp(c.r, -65504, 65504); c.g = Mathf.Clamp(c.g, -65504, 65504); c.b = Mathf.Clamp(c.b, -65504, 65504);
                pixels[i] = pixels[i + width] = c;
            }
            if (texture == null) texture = new Texture2D(width, 2, TextureFormat.RGBAHalf, false, true)
                { name = "WhimTex Gradient LUT", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.filterMode = gradient.Mode == WhimTexGradientMode.Fixed ? FilterMode.Point : FilterMode.Bilinear;
            texture.SetPixels(pixels); texture.Apply(false, false);
            source = gradient; revision = gradient.Revision; outputSpace = space; BakeCount++;
            return texture;
        }

        public void Dispose()
        {
            if (texture != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
            texture = null; source = null;
        }
    }
}
