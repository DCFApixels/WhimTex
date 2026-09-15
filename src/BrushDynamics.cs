using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum BrushTipChannel { Alpha, Luminance, InvertedLuminance, Color }
    internal enum BrushRandomAlgorithm { Random, Sobol }
    internal enum BrushRotationMode { Fixed, StrokeDirection }
    internal enum BrushProceduralMode { Hardness, SdfGradient }
    internal enum BrushBlendApplication { Stroke, Stamp }

    [Serializable]
    internal sealed class BrushDynamics
    {
        public float opacity = 1f;
        public float flow = 1f;
        public float scatter;
        public float scatterBias;
        public float sizeJitter;
        public float angleJitter;
        public float angleOffset;
        public float flipX;
        public float flipY;
        public BrushRotationMode rotationMode;
        public BrushRandomAlgorithm randomAlgorithm;
        public WhimTexGradient tintGradient = WhiteGradient();
        public Texture2D tip;
        public BrushTipChannel tipChannel;
        public bool tipSdf;
        public BrushProceduralMode proceduralMode;
        public WhimTexGradient tipGradient = DefaultTipGradient();
        public BlendMode blend = BlendMode.Normal;
        public BrushBlendApplication blendApplication;
        public int seed = 1;

        [NonSerialized] private bool tintVaries;
        [NonSerialized] private Color constantTint = Color.white;
        internal bool HasTint => tintVaries || !constantTint.Equals(Color.white);
        internal bool UsesSdfGradient => tip != null ? tipSdf : proceduralMode == BrushProceduralMode.SdfGradient;
        internal bool CanRotateTip => tip != null && (angleOffset != 0f || angleJitter > 0f || rotationMode == BrushRotationMode.StrokeDirection);
        internal bool PerStamp => scatter > 0f || sizeJitter > 0f || HasTint || CanRotateTip || tip != null && (flipX > 0f || flipY > 0f);
        internal bool NeedsStrokeBuffer(bool erase) => opacity < 1f || !erase && blend != BlendMode.Normal;
        internal bool BlendsEachStamp(bool erase) => !erase && blend != BlendMode.Normal && blendApplication == BrushBlendApplication.Stamp;

        internal void Normalize()
        {
            opacity = Unit(opacity, 1f);
            flow = Unit(flow, 1f);
            scatter = Mathf.Clamp(Finite(scatter, 0f), 0f, 4f);
            scatterBias = Mathf.Clamp(Finite(scatterBias, 0f), -1f, 1f);
            sizeJitter = Unit(sizeJitter, 0f);
            angleJitter = Mathf.Clamp(Finite(angleJitter, 0f), 0f, 180f);
            angleOffset = Mathf.Clamp(Finite(angleOffset, 0f), -180f, 180f);
            flipX = Unit(flipX, 0f);
            flipY = Unit(flipY, 0f);
            tipGradient ??= DefaultTipGradient();
            tintGradient ??= WhiteGradient();
            PrepareTint();
            if (!Enum.IsDefined(typeof(BlendMode), blend) || blend == BlendMode.Overwrite || blend == BlendMode.None) blend = BlendMode.Normal;
            if (!Enum.IsDefined(typeof(BrushTipChannel), tipChannel)) tipChannel = BrushTipChannel.Alpha;
            if (!Enum.IsDefined(typeof(BrushRandomAlgorithm), randomAlgorithm)) randomAlgorithm = BrushRandomAlgorithm.Random;
            if (!Enum.IsDefined(typeof(BrushRotationMode), rotationMode)) rotationMode = BrushRotationMode.Fixed;
            if (!Enum.IsDefined(typeof(BrushProceduralMode), proceduralMode)) proceduralMode = BrushProceduralMode.Hardness;
            if (!Enum.IsDefined(typeof(BrushBlendApplication), blendApplication)) blendApplication = BrushBlendApplication.Stroke;
        }

        private static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        private static float Unit(float value, float fallback) => Mathf.Clamp01(Finite(value, fallback));
        internal static WhimTexGradient DefaultTipGradient()
        {
            var result = new WhimTexGradient { Mode = WhimTexGradientMode.Linear };
            result.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, .4f), new GradientAlphaKey(0f, .6f) });
            return result;
        }

        internal void ResetTint()
        {
            tintGradient = WhiteGradient();
            PrepareTint();
        }

        internal void PrepareTint()
        {
            tintGradient ??= WhiteGradient();
            constantTint = tintGradient.EvaluateEncoded(0f);
            tintVaries = false;
            var colors = tintGradient.ColorKeys;
            for (int i = 1; i < colors.Length; i++)
            {
                Color first = colors[0].color, next = colors[i].color;
                if (first.r != next.r || first.g != next.g || first.b != next.b) tintVaries = true;
            }
            var alphas = tintGradient.AlphaKeys;
            for (int i = 1; i < alphas.Length; i++)
                if (alphas[0].alpha != alphas[i].alpha) tintVaries = true;
        }

        internal Color SampleTint(ref uint state, uint stampIndex) => tintVaries
            ? tintGradient.EvaluateEncoded(SampleRandom(ref state, stampIndex, 4)) : constantTint;

        internal float GetScatterExponent()
        {
            return scatterBias == 0f ? .5f : .5f * Mathf.Pow(2f, -4f * scatterBias);
        }

        internal static float ScatterRadius(float sample, float exponent)
        {
            return exponent == .5f ? Mathf.Sqrt(sample) : Mathf.Pow(sample, exponent);
        }

        internal static float MovementAngle(float dx, float dy, float previous)
        {
            return dx * dx + dy * dy > .000001f ? Mathf.Atan2(dy, dx) : previous;
        }

        internal bool SampleFlip(ref uint state, uint stampIndex, int dimension, float probability)
        {
            return probability > 0f && (probability >= 1f || SampleRandom(ref state, stampIndex, dimension) < probability);
        }

        // Stable dimensions: scatter angle/radius, size, rotation, tint, flip X/Y.
        internal float SampleRandom(ref uint state, uint stampIndex, int dimension) => randomAlgorithm == BrushRandomAlgorithm.Sobol
            ? Sobol.Sample(stampIndex, dimension, unchecked((uint)seed)) : Random01(ref state);

        // Initialized only when Sobol is first used; no per-stamp buffers or allocations.
        private static class Sobol
        {
            private static readonly uint[,] directions = CreateDirections();

            internal static float Sample(uint index, int dimension, uint seed)
            {
                uint value = DigitalShift(seed, dimension);
                uint gray = index ^ (index >> 1);
                for (int bit = 0; gray != 0; bit++, gray >>= 1)
                    if ((gray & 1u) != 0) value ^= directions[dimension, bit];
                return (value >> 8) * (1f / 16777216f);
            }

            private static uint DigitalShift(uint seed, int dimension)
            {
                unchecked
                {
                    uint value = seed + 0x9e3779b9u * (uint)(dimension + 1);
                    value = (value ^ (value >> 16)) * 0x85ebca6bu;
                    value = (value ^ (value >> 13)) * 0xc2b2ae35u;
                    return value ^ (value >> 16);
                }
            }

            private static uint[,] CreateDirections()
            {
                var result = new uint[7, 32];
                for (int bit = 0; bit < 32; bit++) result[0, bit] = 1u << (31 - bit);
                // Joe–Kuo D(6) first seven dimensions. See ThirdPartyNotices.md.
                FillDirections(result, 1, 1, 0, new uint[] { 1 });
                FillDirections(result, 2, 2, 1, new uint[] { 1, 3 });
                FillDirections(result, 3, 3, 1, new uint[] { 1, 3, 1 });
                FillDirections(result, 4, 3, 2, new uint[] { 1, 1, 1 });
                FillDirections(result, 5, 4, 1, new uint[] { 1, 1, 3, 3 });
                FillDirections(result, 6, 4, 4, new uint[] { 1, 3, 5, 13 });
                return result;
            }

            private static void FillDirections(uint[,] result, int dimension, int degree, int coefficients, uint[] initial)
            {
                for (int bit = 0; bit < degree; bit++) result[dimension, bit] = initial[bit] << (31 - bit);
                for (int bit = degree; bit < 32; bit++)
                {
                    uint value = result[dimension, bit - degree];
                    value ^= value >> degree;
                    for (int k = 1; k < degree; k++)
                        if (((coefficients >> (degree - 1 - k)) & 1) != 0) value ^= result[dimension, bit - k];
                    result[dimension, bit] = value;
                }
            }
        }

        private static WhimTexGradient WhiteGradient()
        {
            var result = new WhimTexGradient();
            result.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return result;
        }

        internal static float Random01(ref uint state)
        {
            if (state == 0) state = 0x6d2b79f5u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state >> 8) * (1f / 16777216f);
        }
    }

    internal struct BrushSpacingState
    {
        private double remaining;

        internal int Sample(double distance, double spacing, bool includeStart, out double first)
        {
            spacing = Math.Max(1d, spacing);
            first = includeStart ? 0d : remaining > 0d ? remaining : spacing;
            int count = distance + 0.000001d < first ? 0 : (int)Math.Min(int.MaxValue,
                Math.Floor((distance - first + 0.000001d) / spacing) + 1d);
            remaining = Math.Max(0d, first + count * spacing - distance);
            return count;
        }
    }
}
