using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.SpriteEditor
{
    [Serializable]
    public sealed class GradientLayerBehaviour : LayerBehaviour
    {
        public GradientType gradientType = GradientType.Vertical;
        public Gradient gradient = GradientUtility.Create(GradientUtility.WhiteToBlack);
        public Vector2 center = new Vector2(0.5f, 0.5f);
        public float radius = 0.5f;
        public float circularRepetitions = 1f;
        public WrapMode circularWrapMode = WrapMode.Repeat;

        [NonSerialized] private Texture2D cachedPreview;
        [NonSerialized] private int cachedHash;
        [NonSerialized] private Gradient previewGradient;
        [NonSerialized] private Gradient paletteGradient;
        [NonSerialized] private Texture2D palette;
        [NonSerialized] private Vector4[] paletteIntervals;
        [NonSerialized] private int paletteIntervalCount;
        [NonSerialized] private Color paletteStart;
        private const int PaletteWidth = 256;
        private const int MaxPaletteIntervals = 17; // Eight color keys, eight alpha keys, plus 0/1.

        public override Texture2D GetPreviewTexture(int size)
        {
            size = Mathf.Max(1, size);
            int currentHash = ComputeHash();
            Gradient evaluated = gradient ?? GradientUtility.WhiteToBlack;
            if (cachedPreview != null && cachedHash == currentHash && cachedPreview.width == size &&
                previewGradient != null && previewGradient.Equals(evaluated))
                return cachedPreview;

            if (cachedPreview != null) UnityEngine.Object.DestroyImmediate(cachedPreview);
            cachedPreview = GenerateGradientTexture(size, size);
            CopyGradient(ref previewGradient, evaluated);
            cachedHash = currentHash;
            return cachedPreview;
        }

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Material material = SpriteEditorMaterials.Gradient;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Gradient shader is unavailable or unsupported on this graphics device.");
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture source = null;
            try
            {
                UpdatePalette();
                material.SetTexture("_GradientPalette", palette);
                material.SetVectorArray("_GradientIntervals", paletteIntervals);
                material.SetInt("_GradientIntervalCount", paletteIntervalCount);
                material.SetVector("_GradientStart", paletteStart);
                material.SetInt("_GradientType", (int)gradientType);
                material.SetVector("_GradientOutputSize", new Vector4(context.width, context.height, 0f, 0f));
                material.SetVector("_GradientShape", new Vector4(center.x, center.y, radius,
                    Mathf.Max(float.Epsilon, circularRepetitions)));
                material.SetInt("_GradientPingPong", circularWrapMode == WrapMode.PingPong ? 1 : 0);
                source = RenderTexture.GetTemporary(context.width, context.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                source.filterMode = FilterMode.Bilinear;
                source.wrapMode = TextureWrapMode.Clamp;
                GL.sRGBWrite = false;
                var renderedContext = ProceduralUv.Prepare(material, transform, context);
                Graphics.Blit(null, source, material, 0);
                return ApplyTransformAndModifiers(source, renderedContext);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (source != null) RenderTexture.ReleaseTemporary(source);
            }
        }

        private static void CopyGradient(ref Gradient destination, Gradient source)
        {
            destination ??= new Gradient();
            destination.SetKeys(source.colorKeys, source.alphaKeys);
            destination.mode = source.mode;
            destination.colorSpace = source.colorSpace;
        }

        private void UpdatePalette()
        {
            Gradient evaluated = gradient ?? GradientUtility.WhiteToBlack;
            if (palette != null && paletteGradient != null && paletteGradient.Equals(evaluated)) return;

            // Each interval has its own row, so even very close keys retain the full ramp
            // resolution. Native Evaluate preserves Unity's perceptual/color-space behavior.
            var times = new List<float>(MaxPaletteIntervals + 1) { 0f, 1f };
            foreach (var key in evaluated.colorKeys) times.Add(key.time);
            foreach (var key in evaluated.alphaKeys) times.Add(key.time);
            times.Sort();
            for (int i = times.Count - 1; i > 0; i--)
                if (times[i] == times[i - 1]) times.RemoveAt(i);
            paletteIntervalCount = times.Count - 1;
            paletteIntervals ??= new Vector4[MaxPaletteIntervals];
            if (palette == null || palette.height != paletteIntervalCount)
            {
                if (palette != null) UnityEngine.Object.DestroyImmediate(palette);
                palette = new Texture2D(PaletteWidth, paletteIntervalCount, TextureFormat.RGBAFloat, false, true)
                {
                    name = "Gradient Palette", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
            }
            var pixels = palette.GetRawTextureData<Color>();
            bool fixedMode = evaluated.mode == GradientMode.Fixed;
            paletteStart = evaluated.Evaluate(0f);
            for (int row = 0; row < paletteIntervalCount; row++)
            {
                float start = times[row], end = times[row + 1];
                paletteIntervals[row] = new Vector4(start, end, 1f / (end - start), 0f);
                Color fixedColor = fixedMode ? evaluated.Evaluate((start + end) * .5f) : default;
                for (int x = 0; x < PaletteWidth; x++)
                    pixels[row * PaletteWidth + x] = fixedMode ? fixedColor :
                        evaluated.Evaluate(Mathf.Lerp(start, end, x / (float)(PaletteWidth - 1)));
            }
            palette.Apply(false, false);
            CopyGradient(ref paletteGradient, evaluated);
        }

        public float GetGradientCoord(float u, float v)
        {
            switch (gradientType)
            {
                case GradientType.Vertical:
                    return v;
                case GradientType.Horizontal:
                    return u;
                case GradientType.Radial:
                    return NormalizeDistance(Vector2.Distance(new Vector2(u, v), center));
                case GradientType.Circular:
                {
                    Vector2 direction = new Vector2(u - center.x, v - center.y);
                    float angle = Mathf.Atan2(direction.y, direction.x);
                    float turn = (angle + Mathf.PI) / (2f * Mathf.PI);
                    float repeated = turn * Mathf.Max(float.Epsilon, circularRepetitions);
                    return circularWrapMode == WrapMode.PingPong
                        ? Mathf.PingPong(repeated, 1f)
                        : repeated - Mathf.Floor(repeated);
                }
                case GradientType.Diamond:
                    return NormalizeDistance(Mathf.Abs(u - center.x) + Mathf.Abs(v - center.y));
                case GradientType.Square:
                    return NormalizeDistance(Mathf.Max(Mathf.Abs(u - center.x), Mathf.Abs(v - center.y)));
                default:
                    return 0f;
            }
        }

        internal override void ReleaseTransientResources()
        {
            if (cachedPreview != null) UnityEngine.Object.DestroyImmediate(cachedPreview);
            if (palette != null) UnityEngine.Object.DestroyImmediate(palette);
            cachedPreview = null;
            palette = null;
            previewGradient = paletteGradient = null;
            paletteIntervals = null;
        }

        public override string ToString()
        {
            return gradientType.ToString();
        }

        private float NormalizeDistance(float distance)
        {
            return radius <= 0f ? 0f : Mathf.Clamp01(distance / radius);
        }

        private Texture2D GenerateGradientTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = texture.GetRawTextureData<Color>();
            Gradient evaluatedGradient = gradient ?? GradientUtility.WhiteToBlack;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    pixels[y * width + x] = HdrUtility.Decode(evaluatedGradient.Evaluate(GetGradientCoord(u, v)));
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        private int ComputeHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + gradientType.GetHashCode();
                hash = hash * 31 + center.GetHashCode();
                hash = hash * 31 + radius.GetHashCode();
                hash = hash * 31 + circularRepetitions.GetHashCode();
                hash = hash * 31 + circularWrapMode.GetHashCode();
                return hash;
            }
        }

        public enum GradientType
        {
            Vertical,
            Horizontal,
            Radial,
            Circular,
            Diamond,
            Square
        }

        public enum WrapMode
        {
            Repeat,
            PingPong
        }
    }
}
