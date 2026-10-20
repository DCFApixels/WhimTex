using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class GradientLayerBehaviour : LayerBehaviour
    {
        public override TransformTilingMode SourceTiling => TransformTilingMode.Unbounded;
        public GradientType gradientType = GradientType.Linear;
        public WhimTexGradient gradient = GradientUtility.Create(GradientUtility.WhiteToBlack);
        public FreeformGradient freeform = new FreeformGradient();
        [NonSerialized] internal int selectedFreeformPoint;
        internal static readonly Vector2 BaseCenter = new Vector2(0.5f, 0.5f);
        internal const float BaseRadius = 0.5f;
        public float circularRepetitions = 1f;
        public float circularStartAngle;
        public WrapMode circularWrapMode = WrapMode.Repeat;

        [NonSerialized] private Texture2D cachedPreview;
        [NonSerialized] private int cachedHash;
        [NonSerialized] private WhimTexGradient previewGradient;
        [NonSerialized] private WhimTexGradientPalette palette;

        public override Texture2D GetPreviewTexture(int size)
        {
            size = Mathf.Max(1, size);
            int currentHash = ComputeHash();
            WhimTexGradient evaluated = gradient ?? GradientUtility.WhiteToBlack;
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
            Material material = WhimTexMaterials.Gradient;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Gradient shader is unavailable or unsupported on this graphics device.");
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            RenderTexture source = null;
            try
            {
                if (gradientType == GradientType.Freeform)
                    (freeform ?? throw new InvalidOperationException("Freeform gradient data is missing.")).Bind(material);
                else
                {
                    palette ??= new WhimTexGradientPalette();
                    palette.Bind(material, gradient ?? GradientUtility.WhiteToBlack);
                }
                material.SetInt("_GradientType", (int)gradientType);
                material.SetInt("_GradientWrapMode", (int)(gradient?.WrapMode ?? WhimTexGradientWrapMode.Clamp));
                material.SetVector("_GradientOutputSize", new Vector4(context.width, context.height, 0f, 0f));
                material.SetVector("_GradientShape", new Vector4(BaseCenter.x, BaseCenter.y, BaseRadius,
                    Mathf.Max(float.Epsilon, circularRepetitions)));
                material.SetInt("_GradientPingPong", circularWrapMode == WrapMode.PingPong ? 1 : 0);
                material.SetFloat("_GradientStartTurn", Mathf.Repeat(circularStartAngle, 360f) / 360f);
                source = ProfiledRenderTexture.GetTemporary(context.width, context.height, 0,
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                source.filterMode = FilterMode.Bilinear;
                source.wrapMode = TextureWrapMode.Clamp;
                GL.sRGBWrite = false;
                var renderedContext = ProceduralUv.Prepare(material, Owner, context);
                Graphics.Blit(null, source, material, 0);
                return ApplyTransformAndFx(source, renderedContext);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                if (source != null) ProfiledRenderTexture.ReleaseTemporary(source);
            }
        }

        private static void CopyGradient(ref WhimTexGradient destination, WhimTexGradient source)
        {
            destination = source.Clone();
        }

        public float GetGradientCoord(float u, float v)
        {
            switch (gradientType)
            {
                case GradientType.Linear:
                    return u;
                case GradientType.Radial:
                    return DistanceToGradientTime(Vector2.Distance(new Vector2(u, v), BaseCenter));
                case GradientType.Angular:
                {
                    Vector2 direction = new Vector2(u - BaseCenter.x, v - BaseCenter.y);
                    float angle = Mathf.Atan2(direction.y, direction.x);
                    float turn = (angle + Mathf.PI) / (2f * Mathf.PI);
                    turn -= Mathf.Repeat(circularStartAngle, 360f) / 360f;
                    if (turn < 0f || turn > 1f) turn -= Mathf.Floor(turn);
                    float repeated = turn * Mathf.Max(float.Epsilon, circularRepetitions);
                    return circularWrapMode == WrapMode.PingPong
                        ? Mathf.PingPong(repeated, 1f)
                        : repeated - Mathf.Floor(repeated);
                }
                case GradientType.Square:
                    return DistanceToGradientTime(Mathf.Max(Mathf.Abs(u - BaseCenter.x), Mathf.Abs(v - BaseCenter.y)));
                default:
                    return 0f;
            }
        }

        internal override void ReleaseTransientResources()
        {
            if (cachedPreview != null) UnityEngine.Object.DestroyImmediate(cachedPreview);
            palette?.Dispose();
            cachedPreview = null;
            palette = null;
            previewGradient = null;
        }

        public override string ToString()
        {
            return gradientType.ToString();
        }

        private static float DistanceToGradientTime(float distance)
        {
            return distance / BaseRadius;
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
            WhimTexGradient evaluatedGradient = gradient ?? GradientUtility.WhiteToBlack;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    pixels[y * width + x] = gradientType == GradientType.Freeform
                        ? freeform.Evaluate(new Vector2(u, v))
                        : HdrUtility.Decode(evaluatedGradient.EvaluateEncoded(GetGradientCoord(u, v)));
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
                hash = hash * 31 + circularRepetitions.GetHashCode();
                hash = hash * 31 + circularStartAngle.GetHashCode();
                hash = hash * 31 + circularWrapMode.GetHashCode();
                if (gradientType == GradientType.Freeform) hash = hash * 31 + (freeform?.ContentHash() ?? 0);
                return hash;
            }
        }

        public enum GradientType
        {
            Linear = 1,
            Radial = 2,
            Angular = 3,
            Square = 5,
            Freeform = 6
        }

        public enum WrapMode
        {
            Repeat,
            PingPong
        }
    }
}
