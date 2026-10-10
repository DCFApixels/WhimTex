using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    [Serializable]
    public sealed class NoiseLayerBehaviour : LayerBehaviour
    {
        internal override void InitializeLayer(Layer layer) => layer.transform.tiling = TransformTilingMode.Unbounded;
        public enum NoiseType { OpenSimplex2, OpenSimplex2S, Cellular, Perlin, ValueCubic, Value, WhiteNoise, BlueNoise }
        public enum Field { Value, Curl, GradientVector, CellDirection }
        public enum VectorOutput { PackedVector, SignedVector }
        public enum GrainColor { Monochrome, Color }
        public enum FractalType { None, FBm, Ridged, PingPong }
        public enum CellularDistance { Euclidean, EuclideanSquared, Manhattan, Hybrid }
        public enum CellularReturn { CellValue, Distance, Distance2, Distance2Add, Distance2Sub, Distance2Mul, Distance2Div }
        public enum WarpType { None, OpenSimplex2, OpenSimplex2Reduced, BasicGrid }
        public enum OutputEncoding { ColorValues, LinearData, Gradient }
        public enum NoiseDimensions { TwoD, OneD, ThreeD }
        public enum PeriodicAxes { None, X, Y, XY }

        public NoiseType noiseType;
        public Field field;
        public VectorOutput vectorOutput;
        public bool normalize;
        public float strength = 1f;
        public GrainColor grainColor;
        public float grainSize = 1f;
        public NoiseDimensions dimensions;
        public float direction;
        public int seed = 1337;
        public float scale = 8f;
        public float scaleY = 8f;
        public float scaleZ = 1f;
        public bool linkScale = true;
        public Vector3 offset;
        public PeriodicAxes periodic;
        public bool periodic1D;
        public Vector2 Scale
        {
            get => new Vector2(Limit(scale, .01f, 1000f, 8f), Limit(scaleY, .01f, 1000f, 8f));
            set { scale = Limit(value.x, .01f, 1000f, 8f); scaleY = Limit(value.y, .01f, 1000f, 8f); }
        }
        public Vector3 Scale3D
        {
            get => new Vector3(Scale.x, Scale.y, Limit(scaleZ, .01f, 1000f, 1f));
            set { Scale = value; scaleZ = Limit(value.z, .01f, 1000f, 1f); }
        }
        public bool IsVectorField => field != Field.Value;
        public static bool SupportsNoiseType(Field field, NoiseType type) => field switch
        {
            Field.Curl => type == NoiseType.OpenSimplex2 || type == NoiseType.OpenSimplex2S || type == NoiseType.Perlin,
            Field.GradientVector => type != NoiseType.Cellular && type <= NoiseType.Value,
            Field.CellDirection => type == NoiseType.Cellular,
            _ => true
        };
        public NoiseType EffectiveNoiseType => SupportsNoiseType(field, noiseType) ? noiseType
            : field == Field.CellDirection ? NoiseType.Cellular : NoiseType.OpenSimplex2;
        internal bool IsGrain => EffectiveNoiseType == NoiseType.WhiteNoise || EffectiveNoiseType == NoiseType.BlueNoise;
        public NoiseDimensions EffectiveDimensions => IsVectorField && dimensions == NoiseDimensions.OneD
            || IsGrain && dimensions == NoiseDimensions.ThreeD ? NoiseDimensions.TwoD : dimensions;
        internal PeriodicAxes EffectivePeriodic => IsGrain ? PeriodicAxes.None
            : EffectiveDimensions == NoiseDimensions.OneD ? (periodic1D ? PeriodicAxes.X : PeriodicAxes.None) : periodic;
        internal FractalType EffectiveFractal => field == Field.CellDirection ? FractalType.None : fractal;

        internal Vector2 AdjustScale(Vector2 next)
            => AdjustLinkedScale(Scale, next, linkScale);

        internal Vector3 AdjustScale3D(Vector3 next)
            => AdjustLinkedScale3D(Scale3D, next, linkScale);

        internal Vector2 AdjustWarpScale(Vector2 next)
            => AdjustLinkedScale(WarpScale, next, linkWarpScale);

        internal Vector3 AdjustWarpScale3D(Vector3 next)
            => AdjustLinkedScale3D(WarpScale3D, next, linkWarpScale);

        private static Vector3 AdjustLinkedScale3D(Vector3 old, Vector3 next, bool linked)
        {
            next = new Vector3(Limit(next.x, .01f, 1000f, old.x), Limit(next.y, .01f, 1000f, old.y),
                Limit(next.z, .01f, 1000f, old.z));
            if (!linked) return next;
            float ratio = next.x != old.x ? next.x / old.x : next.y != old.y ? next.y / old.y : next.z / old.z;
            ratio = Mathf.Clamp(ratio, Mathf.Max(.01f / old.x, .01f / old.y, .01f / old.z),
                Mathf.Min(1000f / old.x, 1000f / old.y, 1000f / old.z));
            return old * ratio;
        }

        private static Vector2 AdjustLinkedScale(Vector2 old, Vector2 next, bool linked)
        {
            next = new Vector2(Limit(next.x, .01f, 1000f, old.x), Limit(next.y, .01f, 1000f, old.y));
            if (!linked) return next;
            float ratio = next.x != old.x ? next.x / old.x : next.y / old.y;
            ratio = Mathf.Clamp(ratio, Mathf.Max(.01f / old.x, .01f / old.y), Mathf.Min(1000f / old.x, 1000f / old.y));
            return old * ratio;
        }
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
        public int warpSeed = 1337;
        public float warpStrength = 1f;
        public float warpScale = 1f;
        public float warpScaleY = 1f;
        public float warpScaleZ = 1f;
        public bool linkWarpScale = true;
        public Vector2 WarpScale
        {
            get => new Vector2(Limit(warpScale, .01f, 1000f, 1f), Limit(warpScaleY, .01f, 1000f, 1f));
            set { warpScale = Limit(value.x, .01f, 1000f, 1f); warpScaleY = Limit(value.y, .01f, 1000f, 1f); }
        }
        public Vector3 WarpScale3D
        {
            get => new Vector3(WarpScale.x, WarpScale.y, Limit(warpScaleZ, .01f, 1000f, 1f));
            set { WarpScale = value; warpScaleZ = Limit(value.z, .01f, 1000f, 1f); }
        }
        public OutputEncoding encoding = OutputEncoding.LinearData;
        public bool inverted;
        public WhimTexGradient gradient = new WhimTexGradient();

        internal bool SupportsGradient => !IsVectorField && (!IsGrain || grainColor != GrainColor.Color);
        internal OutputEncoding EffectiveOutput => encoding == OutputEncoding.Gradient && !SupportsGradient
            ? OutputEncoding.ColorValues : encoding;

        [NonSerialized] private ProceduralLayerThumbnail thumbnail;
        [NonSerialized] private WhimTexGradientTexture gradientLut;
        [NonSerialized] private NoiseLatticeSettings lattice;
        private static readonly string[] NoiseKeywords = { "WT_NOISE_0", "WT_NOISE_1", "WT_NOISE_2", "WT_NOISE_3", "WT_NOISE_4", "WT_NOISE_5" };

        public override Texture2D GetPreviewTexture(int size)
        {
            var hash = new HashCode();
            hash.Add(field); hash.Add(vectorOutput); hash.Add(normalize); hash.Add(strength);
            hash.Add(noiseType); hash.Add(grainColor); hash.Add(grainSize);
            hash.Add(dimensions); hash.Add(direction); hash.Add(seed); hash.Add(scale); hash.Add(offset);
            hash.Add(scaleY); hash.Add(scaleZ); hash.Add(periodic); hash.Add(periodic1D);
            hash.Add(fractal); hash.Add(octaves); hash.Add(lacunarity); hash.Add(gain);
            hash.Add(weightedStrength); hash.Add(pingPongStrength);
            hash.Add(cellularDistance); hash.Add(cellularReturn); hash.Add(cellularJitter);
            hash.Add(warp); hash.Add(warpSeed); hash.Add(warpStrength); hash.Add(WarpScale3D); hash.Add(encoding); hash.Add(inverted); hash.Add(filterMode);
            hash.Add(gradient);
            thumbnail ??= new ProceduralLayerThumbnail();
            return thumbnail.Get(this, size, hash.ToHashCode());
        }

        internal override void ReleaseTransientResources()
        {
            thumbnail?.Dispose();
            thumbnail = null;
            gradientLut?.Dispose();
            gradientLut = null;
            lattice = null;
        }

        public override string ToString() => "Noise";

        internal static float Limit(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal override RenderTexture Render(in LayerRenderContext context)
        {
            Material material = WhimTexMaterials.Noise;
            if (material == null || !material.shader.isSupported)
                throw new InvalidOperationException("Noise shader is unavailable or unsupported on this graphics device.");
            int kernel = Mathf.Clamp((int)EffectiveNoiseType, 0, 5);
            for (int k = 0; k < NoiseKeywords.Length; k++)
                if (k == kernel) material.EnableKeyword(NoiseKeywords[k]); else material.DisableKeyword(NoiseKeywords[k]);
            if (EffectiveDimensions == NoiseDimensions.ThreeD) material.EnableKeyword("WT_NOISE_3D");
            else material.DisableKeyword("WT_NOISE_3D");
            if (EffectivePeriodic != PeriodicAxes.None) material.EnableKeyword("WT_NOISE_PERIODIC");
            else material.DisableKeyword("WT_NOISE_PERIODIC");
            if (IsVectorField) material.EnableKeyword("WT_NOISE_VECTOR");
            else material.DisableKeyword("WT_NOISE_VECTOR");
            float width = context.activeDocument != null ? context.activeDocument.width : context.width;
            float height = context.activeDocument != null ? context.activeDocument.height : context.height;
            float shortest = Mathf.Max(1f, Mathf.Min(width, height));
            material.SetVector("_NoiseDomain", new Vector4(width / shortest, height / shortest,
                Limit(offset.x, -10000f, 10000f, 0f), Limit(offset.y, -10000f, 10000f, 0f)));
            Vector2 axesScale = Scale;
            material.SetVector("_NoiseScale", Scale3D);
            material.SetFloat("_NoiseZ", Limit(offset.z, -10000f, 10000f, 0f) * Scale3D.z);
            material.SetInteger("_NoisePeriodic", (int)EffectivePeriodic);
            float radians = Limit(direction, -180f, 180f, 0f) * Mathf.Deg2Rad;
            float axisX = Mathf.Cos(radians), axisY = Mathf.Sin(radians);
            if (Mathf.Abs(axisX) < 0.000001f) axisX = 0f;
            if (Mathf.Abs(axisY) < 0.000001f) axisY = 0f;
            double projectedX = (double)width / shortest * axesScale.x * axisX;
            double projectedY = (double)height / shortest * axesScale.y * axisY;
            double axisPeriod = Math.Abs(projectedX) + Math.Abs(projectedY);
            if (EffectivePeriodic != PeriodicAxes.None)
            {
                lattice ??= new NoiseLatticeSettings();
                if (EffectiveDimensions == NoiseDimensions.OneD)
                    lattice.Apply(material, this, axisPeriod, 1);
                else
                    lattice.Apply(material, this, width / shortest * axesScale.x, height / shortest * axesScale.y);
            }
            material.SetInteger("_NoiseOneD", EffectiveDimensions == NoiseDimensions.OneD ? 1 : 0);
            material.SetVector("_NoiseAxis", new Vector4(axisX, axisY,
                (float)(projectedX / axisPeriod), (float)(projectedY / axisPeriod)));
            material.SetInteger("_NoiseSeed", seed);
            material.SetInteger("_NoiseType", Mathf.Clamp((int)EffectiveNoiseType, 0, 7));
            material.SetInteger("_NoiseField", (int)field);
            material.SetInteger("_NoiseVectorOutput", (int)vectorOutput);
            material.SetInteger("_NoiseNormalize", normalize ? 1 : 0);
            material.SetFloat("_NoiseStrength", Limit(strength, 0f, 100f, 1f));
            if (EffectiveNoiseType == NoiseType.BlueNoise)
            {
                material.SetTexture("_BlueNoise2D", EffectiveDimensions == NoiseDimensions.TwoD ? BlueNoiseTextures.TwoD : null);
                material.SetTexture("_BlueNoise1D", EffectiveDimensions == NoiseDimensions.OneD ? BlueNoiseTextures.OneD : null);
            }
            material.SetInteger("_GrainColor", grainColor == GrainColor.Color ? 1 : 0);
            material.SetVector("_GrainGrid", new Vector4(width, height,
                Limit(grainSize, 1f, 1024f, 1f), 0f));
            material.SetInteger("_NoiseFractal", Mathf.Clamp((int)EffectiveFractal, 0, 3));
            material.SetInteger("_NoiseOctaves", Mathf.Clamp(octaves, 1, 8));
            material.SetVector("_NoiseFractalSettings", new Vector4(Limit(lacunarity, 1f, 4f, 2f),
                Limit(gain, 0f, 1f, .5f), Limit(weightedStrength, 0f, 1f, 0f), Limit(pingPongStrength, .01f, 8f, 2f)));
            material.SetInteger("_NoiseCellularDistance", Mathf.Clamp((int)cellularDistance, 0, 3));
            material.SetInteger("_NoiseCellularReturn", Mathf.Clamp((int)cellularReturn, 0, 6));
            material.SetFloat("_NoiseCellularJitter", Limit(cellularJitter, 0f, 1f, 1f));
            material.SetInteger("_NoiseWarp", Mathf.Clamp((int)warp, 0, 3));
            material.SetInteger("_NoiseWarpSeed", warpSeed);
            material.SetFloat("_NoiseWarpStrength", Limit(warpStrength, 0f, 100f, 1f));
            material.SetVector("_NoiseWarpScale", WarpScale3D);
            bool applyGradient = !IsVectorField && EffectiveOutput == OutputEncoding.Gradient;
            material.SetInteger("_NoiseEncoding", (int)EffectiveOutput);
            material.SetInteger("_NoiseInverted", inverted ? 1 : 0);
            material.SetInteger("_UseGradient", applyGradient ? 1 : 0);
            if (applyGradient)
            {
                gradient ??= new WhimTexGradient();
                gradientLut ??= new WhimTexGradientTexture();
                material.SetTexture("_GradientLut", gradientLut.GetTexture(gradient, ColorSpace.Gamma));
                material.SetInteger("_GradientWrapMode", (int)gradient.WrapMode);
            }

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
                var renderedContext = ProceduralUv.Prepare(material, Owner, context);
                Graphics.Blit(null, source, material, 0);
                return ApplyTransformAndFx(source, renderedContext);
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
