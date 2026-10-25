using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexGradientPalette : IDisposable
    {
        private const int Width = 256, MaxIntervals = 129;
        private Texture2D texture;
        private WhimTexGradient source;
        private readonly Vector4[] intervals = new Vector4[MaxIntervals];
        private int count;
        private Color startColor;

        internal void Bind(Material material, WhimTexGradient gradient, bool stroke = false)
        {
            if (gradient == null) throw new ArgumentNullException(nameof(gradient));
            if (texture == null || source == null || !source.EqualsRamp(gradient)) Update(gradient);
            material.SetTexture(stroke ? "_StrokeGradientPalette" : "_GradientPalette", texture);
            material.SetVectorArray(stroke ? "_StrokeGradientIntervals" : "_GradientIntervals", intervals);
            material.SetInt(stroke ? "_StrokeGradientIntervalCount" : "_GradientIntervalCount", count);
            material.SetVector(stroke ? "_StrokeGradientStart" : "_GradientStart", startColor);
            material.SetInt(stroke ? "_StrokeGradientWrapMode" : "_GradientWrapMode", (int)gradient.WrapMode);
        }
        private void Update(WhimTexGradient gradient)
        {
            var times = new List<float>(MaxIntervals + 1) { 0, 1 };
            foreach (var key in gradient.ColorKeys) times.Add(key.time);
            foreach (var key in gradient.AlphaKeys) times.Add(key.time);
            times.Sort();
            for (int i = times.Count - 1; i > 0; i--) if (times[i] == times[i - 1]) times.RemoveAt(i);
            count = times.Count - 1;
            if (count > MaxIntervals) throw new InvalidOperationException("Too many gradient intervals.");
            int height = Math.Max(2, count);
            if (texture == null || texture.height != height)
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                texture = new Texture2D(Width, height, TextureFormat.RGBAFloat, false, true)
                { name = "Gradient Palette", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }
            var pixels = texture.GetRawTextureData<Color>();
            var evaluated = gradient.Clone(); evaluated.WrapMode = WhimTexGradientWrapMode.Clamp;
            bool fixedMode = evaluated.Interpolation == WhimTexGradientInterpolation.Fixed;
            startColor = evaluated.EvaluateEncoded(0);
            for (int row = 0; row < count; row++)
            {
                float start = times[row], end = times[row + 1];
                intervals[row] = new Vector4(start, end, 1 / (end - start), 0);
                Color fixedColor = fixedMode ? evaluated.EvaluateEncoded((start + end) * .5f) : default;
                for (int x = 0; x < Width; x++) pixels[row * Width + x] = fixedMode ? fixedColor :
                    evaluated.EvaluateEncoded(Mathf.Lerp(start, end, x / (float)(Width - 1)));
            }
            texture.Apply(false, false); source = gradient.Clone();
        }
        public void Dispose()
        {
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            texture = null; source = null;
        }
    }
}
