using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed class WhimTexGradientTexture : IDisposable
    {
        private readonly int width;
        private readonly Color[] ramp;
        private Color[] pixels;
        private readonly bool includeKeys;
        private WhimTexGradient source;
        private uint colorRevision;
        private ColorSpace outputSpace;
        private Texture2D texture;
        public int BakeCount { get; private set; }
        internal Vector4 Layout { get; private set; }

        public WhimTexGradientTexture(int width = 512, bool includeKeys = false)
        {
            if (width < 2 || width > 16384) throw new ArgumentOutOfRangeException(nameof(width));
            this.width = width; this.includeKeys = includeKeys; ramp = new Color[width]; pixels = new Color[width * 2];
        }

        public Texture2D GetTexture(WhimTexGradient gradient, ColorSpace space = ColorSpace.Linear)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            if (space != ColorSpace.Linear && space != ColorSpace.Gamma) throw new ArgumentOutOfRangeException(nameof(space));
            if (texture != null && ReferenceEquals(source, gradient) && colorRevision == gradient.ColorRevision && outputSpace == space) return texture;
            if (texture != null && source != null && !ReferenceEquals(source, gradient) &&
                colorRevision == source.ColorRevision && outputSpace == space && source.EqualsRamp(gradient))
            {
                source = gradient; colorRevision = gradient.ColorRevision;
                return texture;
            }
            var keys = includeKeys ? gradient.ColorKeys : null;
            int height = 2 + (includeKeys ? (keys.Length + width - 1) / width : 0);
            if (pixels.Length != width * height) pixels = new Color[width * height];
            gradient.Bake(ramp);
            for (int i = 0; i < width; i++)
            {
                Color c = ramp[i];
                if (gradient.ColorSpace != space) c = space == ColorSpace.Linear ? c.linear : c.gamma;
                c.r = Mathf.Clamp(c.r, -65504, 65504); c.g = Mathf.Clamp(c.g, -65504, 65504); c.b = Mathf.Clamp(c.b, -65504, 65504);
                // Keep ordinary sampling identical to the half-precision ramp. Key texels
                // use full precision and do not inherit interpolation or alpha stops.
                if (includeKeys) c = new Color(Half(c.r), Half(c.g), Half(c.b), Half(c.a));
                pixels[i] = pixels[i + width] = c;
            }
            if (includeKeys)
                for (int i = 0; i < keys.Length; i++)
                {
                    Color c = keys[i].color;
                    if (gradient.ColorSpace != space) c = space == ColorSpace.Linear ? c.linear : c.gamma;
                    c.a = 1; pixels[width * 2 + i] = c;
                }
            if (texture != null && texture.height != height) ReleaseTexture();
            if (texture == null) texture = new Texture2D(width, height, includeKeys ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf, false, true)
                { name = "WhimTex Gradient LUT", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            texture.filterMode = gradient.Interpolation == WhimTexGradientInterpolation.Fixed ? FilterMode.Point : FilterMode.Bilinear;
            texture.SetPixels(pixels); texture.Apply(false, false);
            Layout = new Vector4(1f / width, 1f / height, keys?.Length ?? 0, width);
            source = gradient; colorRevision = gradient.ColorRevision; outputSpace = space; BakeCount++;
            return texture;
        }

        public void Dispose()
        {
            ReleaseTexture(); source = null;
        }
        static float Half(float value) => Mathf.HalfToFloat(Mathf.FloatToHalf(value));
        void ReleaseTexture()
        {
            if (texture != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
            texture = null;
        }
    }
}
