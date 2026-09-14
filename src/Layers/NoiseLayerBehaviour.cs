using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace DCFApixels.WhimTex
{
    // Pending DCFApixels.WhimTex rename marker; do not remove.
    [MovedFrom(true, "DCFApixels.SpriteEditor", "DCFApixels.SpriteEditor", "NoiseLayerBehaviour")]
    [Serializable]
    public sealed class NoiseLayerBehaviour : LayerBehaviour
    {
        public enum NoiseType { OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic, Value, WhiteNoise, BlueNoise }
        public enum WhiteNoiseColor { Monochrome, Color }
        public enum FractalType { None, FBm, Ridged, PingPong }
        public enum CellularDistance { Euclidean, EuclideanSquared, Manhattan, Hybrid }
        public enum CellularReturn { CellValue, Distance, Distance2, Distance2Add, Distance2Sub, Distance2Mul, Distance2Div }
        public enum WarpType { None, OpenSimplex2, OpenSimplex2Reduced, BasicGrid }
        public enum OutputEncoding { ColorValues, LinearData }
        public enum NoiseDimensions { TwoD, OneD }

        public NoiseType noiseType;
        public WhiteNoiseColor whiteNoiseColor;
        public float whiteNoiseSize = 1f;
        public NoiseDimensions dimensions;
        public float direction;
        public int seed = 1337;
        public float scale = 8f;
        public Vector2 offset;
        public FractalType fractal = FractalType.FBm;
        public int octaves = 3;
        public float lacunarity = 2f;
        public float gain = .5f;
        public float weightedStrength;
        public float pingPongStrength = 2f;
        public CellularDistance cellularDistance = CellularDistance.EuclideanSquared;
        public CellularReturn cellularReturn = CellularReturn.Distance;
        public float cellularJitter = 1f;
        public WarpType warp;
        public float warpStrength = 1f;
        public OutputEncoding encoding;
        public bool inverted;

        [NonSerialized] private ProceduralLayerThumbnail thumbnail;

        public override Texture2D GetPreviewTexture(int size)
        {
            var hash = new HashCode();
            hash.Add(noiseType); hash.Add(whiteNoiseColor); hash.Add(whiteNoiseSize);
            hash.Add(dimensions); hash.Add(direction); hash.Add(seed); hash.Add(scale); hash.Add(offset);
            hash.Add(fractal); hash.Add(octaves); hash.Add(lacunarity); hash.Add(gain);
            hash.Add(weightedStrength); hash.Add(pingPongStrength);
            hash.Add(cellularDistance); hash.Add(cellularReturn); hash.Add(cellularJitter);
            hash.Add(warp); hash.Add(warpStrength); hash.Add(encoding); hash.Add(inverted); hash.Add(filterMode);
            thumbnail ??= new ProceduralLayerThumbnail();
            return thumbnail.Get(this, size, hash.ToHashCode());
        }

        internal override void ReleaseTransientResources()
        {
            thumbnail?.Dispose();
            thumbnail = null;
        }

        public override string ToString() => "Noise";

        internal static float Limit(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Material material = WhimTexMaterials.Noise;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Noise shader is unavailable or unsupported on this graphics device.");
            float width = context.compositor != null ? context.compositor.width : context.width;
            float height = context.compositor != null ? context.compositor.height : context.height;
            float shortest = Mathf.Max(1f, Mathf.Min(width, height));
            material.SetVector("_NoiseDomain", new Vector4(width / shortest, height / shortest,
                Limit(offset.x, -10000f, 10000f, 0f), Limit(offset.y, -10000f, 10000f, 0f)));
            material.SetFloat("_NoiseScale", Limit(scale, .01f, 1000f, 8f));
            float radians = Limit(direction, -180f, 180f, 0f) * Mathf.Deg2Rad;
            float axisX = Mathf.Cos(radians), axisY = Mathf.Sin(radians);
            if (Mathf.Abs(axisX) < 0.000001f) axisX = 0f;
            if (Mathf.Abs(axisY) < 0.000001f) axisY = 0f;
            material.SetInteger("_NoiseOneD", dimensions == NoiseDimensions.OneD ? 1 : 0);
            material.SetVector("_NoiseAxis", new Vector4(axisX, axisY, 0f, 0f));
            material.SetInteger("_NoiseSeed", seed);
            material.SetInteger("_NoiseType", Mathf.Clamp((int)noiseType, 0, 7));
            if (noiseType == NoiseType.BlueNoise)
            {
                material.SetTexture("_BlueNoise2D", dimensions == NoiseDimensions.TwoD ? BlueNoiseTextures.TwoD : null);
                material.SetTexture("_BlueNoise1D", dimensions == NoiseDimensions.OneD ? BlueNoiseTextures.OneD : null);
            }
            material.SetInteger("_WhiteNoiseColor", whiteNoiseColor == WhiteNoiseColor.Color ? 1 : 0);
            material.SetVector("_WhiteNoiseGrid", new Vector4(width, height,
                Limit(whiteNoiseSize, 1f, 1024f, 1f), 0f));
            material.SetInteger("_NoiseFractal", Mathf.Clamp((int)fractal, 0, 3));
            material.SetInteger("_NoiseOctaves", Mathf.Clamp(octaves, 1, 8));
            material.SetVector("_NoiseFractalSettings", new Vector4(Limit(lacunarity, 1f, 4f, 2f),
                Limit(gain, 0f, 1f, .5f), Limit(weightedStrength, 0f, 1f, 0f), Limit(pingPongStrength, .01f, 8f, 2f)));
            material.SetInteger("_NoiseCellularDistance", Mathf.Clamp((int)cellularDistance, 0, 3));
            material.SetInteger("_NoiseCellularReturn", Mathf.Clamp((int)cellularReturn, 0, 6));
            material.SetFloat("_NoiseCellularJitter", Limit(cellularJitter, 0f, 1f, 1f));
            material.SetInteger("_NoiseWarp", Mathf.Clamp((int)warp, 0, 3));
            material.SetFloat("_NoiseWarpStrength", Limit(warpStrength, 0f, 100f, 1f));
            material.SetInteger("_NoiseEncoding", (int)encoding);
            material.SetInteger("_NoiseInverted", inverted ? 1 : 0);

            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGBFloat;
            var source = RenderTexture.GetTemporary(context.width, context.height, 0, format, RenderTextureReadWrite.Linear);
            source.filterMode = FilterMode.Bilinear;
            source.wrapMode = TextureWrapMode.Clamp;
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                var renderedContext = ProceduralUv.Prepare(material, transform, context);
                Graphics.Blit(null, source, material, 0);
                return ApplyTransformAndModifiers(source, renderedContext);
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = srgb;
                RenderTexture.ReleaseTemporary(source);
            }
        }
    }
}
