using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal enum BrushTipChannel { Alpha, Luminance, InvertedLuminance, Color }
    internal enum BrushTipSource { Standard = 0, HLSL = 2 }
    internal enum BrushRandomAlgorithm { Random, Sobol }
    internal enum BrushRotationMode { Fixed, StrokeDirection }
    internal enum BrushProceduralMode { Hardness, SdfGradient }
    internal enum BrushBlendApplication { Stroke, Stamp }

    [Serializable]
    internal sealed class BrushPressureBinding
    {
        public bool enabled;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);

        internal BrushPressureBinding() { }
        internal BrushPressureBinding(bool enabled) => this.enabled = enabled;
        internal float Evaluate(float pressure) => enabled ? EvaluateCurve(curve, pressure) : 1f;
        internal static float EvaluateCurve(AnimationCurve curve, float pressure)
        {
            pressure = float.IsFinite(pressure) ? Mathf.Clamp01(pressure) : 1f;
            float value = curve == null || curve.length == 0 ? pressure : curve.Evaluate(pressure);
            return float.IsFinite(value) ? Mathf.Clamp01(value) : pressure;
        }
        internal BrushPressureBinding Copy() => new BrushPressureBinding(enabled) { curve = WhimTexCurveTexture.Copy(curve) };
    }

    internal static class BrushPressureInput
    {
        internal static float Read(string pointerType, float pressure) => pointerType == UnityEngine.UIElements.PointerType.pen
            ? (float.IsFinite(pressure) ? Mathf.Clamp01(pressure) : 0f) : 1f;
    }

    [Serializable]
    internal sealed class BrushDynamics
    {
        public float opacity = 1f;
        public float flow = 1f;
        public bool pressure = true;
        public BrushPressureBinding pressureSize = new BrushPressureBinding();
        public BrushPressureBinding pressureOpacity = new BrushPressureBinding();
        public BrushPressureBinding pressureFlow = new BrushPressureBinding(true);
        public int writeChannels = 15;
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
        public BrushTipSource source;
        public string hlslCode = BrushTipProgram.DefaultSource;
        public int hlslResolution = 512;
        public System.Collections.Generic.List<ShaderFXParameter> hlslParameters = new System.Collections.Generic.List<ShaderFXParameter>();
        public BrushTipChannel tipChannel;
        public bool tipSdf;
        public BrushProceduralMode proceduralMode;
        public WhimTexGradient tipGradient = DefaultTipGradient();
        public BlendMode blend = BlendMode.Normal;
        public BrushBlendApplication blendApplication;
        public int seed = 1;

        [NonSerialized] internal BrushTipProgram hlslProgram;
        [NonSerialized] private bool ownsHlslProgram;
        [NonSerialized] private Texture2D ownedHlslTip;
        [NonSerialized] private BrushSequenceState sequence;
        internal bool DynamicTip => source == BrushTipSource.HLSL && hlslProgram?.IsDynamic == true;
        internal bool HasTip => tip != null || DynamicTip;
        internal BrushSequenceState Sequence => sequence ??= new BrushSequenceState();
        internal bool NeedsDirection => DynamicTip && hlslProgram.UsesMotion || HasTip && rotationMode == BrushRotationMode.StrokeDirection;

        internal void ResetSequence() => sequence?.Reset();

        internal void EnsureHlsl()
        {
            if (source != BrushTipSource.HLSL || hlslProgram?.Matches(hlslCode) == true && (DynamicTip || tip != null)) return;
            if (hlslProgram == null && tip != null && !BrushTipProgram.ReadDynamic(hlslCode)) return;
            ApplyHlsl(hlslCode, hlslParameters, hlslResolution);
        }

        internal void ApplyHlsl(string code, System.Collections.Generic.List<ShaderFXParameter> parameters, int resolution)
        {
            bool dynamic = BrushTipProgram.ReadDynamic(code);
            bool changed = hlslCode != code || source != BrushTipSource.HLSL;
            var program = ownsHlslProgram ? hlslProgram : new BrushTipProgram();
            Texture2D baked = null;
            try
            {
                if (dynamic) program.PrepareDynamic(code, parameters);
                else baked = program.Bake(code, parameters, resolution);
            }
            catch { if (!ownsHlslProgram) program.Dispose(); throw; }
            ReleaseHlslTip();
            hlslProgram = program; ownsHlslProgram = true;
            tip = ownedHlslTip = baked;
            source = BrushTipSource.HLSL; hlslCode = code; hlslParameters = parameters; hlslResolution = resolution;
            if (changed) ResetSequence();
        }

        internal void BorrowHlsl(BrushDynamics other)
        {
            if (ownsHlslProgram) throw new InvalidOperationException("A preview must borrow into fresh brush settings.");
            hlslProgram = other.hlslProgram;
        }

        internal void ReleaseHlsl()
        {
            if (ownsHlslProgram) hlslProgram?.Dispose();
            hlslProgram = null; ownsHlslProgram = false;
            ReleaseHlslTip();
            ResetSequence();
        }

        private void ReleaseHlslTip()
        {
            if (ownedHlslTip == null) return;
            if (tip == ownedHlslTip) tip = null;
            UnityEngine.Object.DestroyImmediate(ownedHlslTip);
            ownedHlslTip = null;
        }

        [NonSerialized] private bool tintVaries;
        [NonSerialized] private Color constantTint = Color.white;
        internal bool HasTint => tintVaries || !constantTint.Equals(Color.white);
        internal bool UsesSdfGradient => HasTip ? tipSdf : proceduralMode == BrushProceduralMode.SdfGradient;
        internal bool CanRotateTip => HasTip && (angleOffset != 0f || angleJitter > 0f || rotationMode == BrushRotationMode.StrokeDirection);
        internal bool HasPressureBindings => pressure && (pressureSize?.enabled == true || pressureOpacity?.enabled == true || pressureFlow?.enabled == true);
        internal bool PressureOpacity => pressure && pressureOpacity?.enabled == true;
        internal bool PerStamp => HasPressureBindings || DynamicTip || scatter > 0f || sizeJitter > 0f || HasTint || CanRotateTip || HasTip && (flipX > 0f || flipY > 0f);
        internal bool NeedsStrokeBuffer(bool erase) => opacity < 1f || PressureOpacity || !erase && blend != BlendMode.Normal || NeedsDirection;
        internal float SizePressure(float value) => pressure ? pressureSize?.Evaluate(value) ?? 1f : 1f;
        internal float OpacityPressure(float value) => pressure ? pressureOpacity?.Evaluate(value) ?? 1f : 1f;
        internal float FlowPressure(float value) => pressure ? pressureFlow?.Evaluate(value) ?? 1f : 1f;
        internal bool BlendsEachStamp(bool erase) => !erase && blend != BlendMode.Normal && blendApplication == BrushBlendApplication.Stamp;

        // Geometry/random colors are already sampled. Retain only the draw settings, borrowing the compiled tip.
        internal BrushDynamics SnapshotForStroke()
        {
            var snapshot = (BrushDynamics)MemberwiseClone();
            snapshot.tipGradient = tipGradient?.Clone();
            snapshot.pressureSize = pressureSize?.Copy();
            snapshot.pressureOpacity = pressureOpacity?.Copy();
            snapshot.pressureFlow = pressureFlow?.Copy();
            snapshot.ownsHlslProgram = false;
            snapshot.ownedHlslTip = null;
            snapshot.sequence = null;
            return snapshot;
        }

        internal void Normalize()
        {
            pressureSize ??= new BrushPressureBinding();
            pressureOpacity ??= new BrushPressureBinding();
            pressureFlow ??= new BrushPressureBinding(true);
            if (!Enum.IsDefined(typeof(BrushTipSource), source)) source = BrushTipSource.Standard;
            hlslResolution = Mathf.Clamp(hlslResolution, 32, 2048);
            hlslParameters ??= new System.Collections.Generic.List<ShaderFXParameter>();
            opacity = Unit(opacity, 1f);
            flow = Unit(flow, 1f);
            writeChannels &= 15;
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
            var result = new WhimTexGradient { Mode = WhimTexGradientMode.Perceptual };
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
            if (!tintVaries)
            {
                // Constant keys need no interpolation/color-space round trip. Keep neutral white exact.
                constantTint = colors[0].color;
                constantTint.a = alphas[0].alpha;
                if (tintGradient.ColorSpace == ColorSpace.Linear &&
                    (constantTint.r != 1f || constantTint.g != 1f || constantTint.b != 1f))
                    constantTint = HdrUtility.Encode(constantTint);
            }
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

        internal static float RefineStartAngle(float provisional, float actual)
        {
            float delta = Mathf.DeltaAngle(provisional * Mathf.Rad2Deg, actual * Mathf.Rad2Deg);
            // Positive SoftSign: 45 degrees gives 0.95, larger discrepancies approach (but never reach) one.
            float magnitude = Mathf.Abs(delta);
            float weight = magnitude / (magnitude + 45f / 19f);
            return provisional + delta * weight * Mathf.Deg2Rad;
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

    // View-space history excludes camera motion; only its direction seeds the spacing-length phantom delta.
    internal struct BrushPointerMotion
    {
        private bool initialized;
        private int pointerId;
        private Vector2 position;
        private Vector2 filteredDirection;
        internal Vector2 Delta { get; private set; }

        internal static float SmoothingDistance(float visibleBrushSize)
            => float.IsFinite(visibleBrushSize) ? Mathf.Clamp(visibleBrushSize * .08f, 3f, 6f) : 3f;

        internal void Observe(Vector2 point, int pointer, float visibleBrushSize)
        {
            if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) { this = default; return; }
            if (!initialized || pointerId != pointer)
            {
                this = default;
                initialized = true;
                pointerId = pointer;
            }
            else
            {
                Vector2 delta = point - position;
                float length = delta.magnitude;
                if (!float.IsFinite(length)) { this = default; return; }
                if (length > .001f)
                {
                    Vector2 direction = delta / length;
                    if (Delta == Vector2.zero) filteredDirection = direction;
                    else
                    {
                        float decay = Mathf.Exp(-length / SmoothingDistance(visibleBrushSize));
                        // Keep the unnormalized state: normalizing each event would depend on event density.
                        filteredDirection = filteredDirection * decay + direction * (1f - decay);
                    }
                    float magnitude = filteredDirection.magnitude;
                    // Opposing directions can cancel; retain the last valid direction until movement resolves it.
                    if (magnitude > .0001f) Delta = filteredDirection / magnitude;
                }
            }
            position = point;
        }
    }

    internal readonly struct BrushStampContext
    {
        internal readonly uint seed, strokeIndex, stampIndex;
        internal readonly float distance, totalDistance;
        internal readonly Vector2 deltaPixels;
        internal BrushStampContext(uint seed, uint strokeIndex, uint stampIndex, double distance, double totalDistance, Vector2 deltaPixels)
        {
            this.seed = seed; this.strokeIndex = strokeIndex; this.stampIndex = stampIndex;
            this.distance = (float)distance; this.totalDistance = (float)totalDistance;
            this.deltaPixels = deltaPixels;
        }
        internal Vector4 StrokeUniform => new Vector4(seed & 65535u, seed >> 16, strokeIndex & 65535u, strokeIndex >> 16);
        internal Vector4 StampVertex => new Vector4(stampIndex & 65535u, stampIndex >> 16, distance, totalDistance);
    }

    internal sealed class BrushSequenceState
    {
        private bool initialized;
        private uint seed, nextStroke;
        internal double TotalDistance { get; private set; }
        internal BrushStampContext Begin(int rootSeed)
        {
            uint value = unchecked((uint)rootSeed);
            if (!initialized || seed != value) { Reset(); seed = value; initialized = true; }
            return new BrushStampContext(seed, nextStroke++, 0, 0, TotalDistance, Vector2.zero);
        }
        internal void Advance(double distance)
        {
            if (distance > 0 && !double.IsNaN(distance) && !double.IsInfinity(distance)) TotalDistance += distance;
        }
        internal void Reset() { initialized = false; nextStroke = 0; TotalDistance = 0; }
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
