using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexGradientColorMode { Linear, Gamma, Perceptual, Hue }
    public enum WhimTexGradientInterpolation { Blend, Balanced, Smooth, Adaptive, Fixed, Custom }
    public enum WhimTexGradientHuePath { Shortest, Longest, Increasing, Decreasing }

    public enum WhimTexGradientWrapMode
    {
        Clamp, Repeat, Mirror
    }

    [Serializable]
    public sealed partial class WhimTexGradient : ISerializationCallbackReceiver, IEquatable<WhimTexGradient>
    {
        [Serializable] private struct ColorStop
        {
            public Color color;
            public float time;
            public float midpoint;
            public ColorStop(Color color, float time) { this.color = color; this.time = time; midpoint = .5f; }
        }
        [Serializable] private struct AlphaStop
        {
            public float alpha, time;
            public float midpoint;
            public AlphaStop(float alpha, float time) { this.alpha = alpha; this.time = time; midpoint = .5f; }
        }
        [SerializeField] private ColorStop[] colors =
            { new ColorStop(Color.black, 0), new ColorStop(Color.white, 1) };
        [SerializeField] private AlphaStop[] alphas =
            { new AlphaStop(1, 0), new AlphaStop(1, 1) };
        [SerializeField] private WhimTexGradientColorMode colorMode = WhimTexGradientColorMode.Perceptual;
        [SerializeField] private WhimTexGradientInterpolation interpolation = WhimTexGradientInterpolation.Balanced;
        [SerializeField] private WhimTexGradientHuePath huePath = WhimTexGradientHuePath.Shortest;
        [SerializeField] private float chromaCorrection;
        [SerializeField] private WhimTexGradientWrapMode wrapMode = WhimTexGradientWrapMode.Clamp;
        [SerializeField] private ColorSpace colorSpace = ColorSpace.Gamma;
        [SerializeField] private float smoothness = 1;
        [NonSerialized] private PreparedState prepared;
        [NonSerialized] private uint revision, colorRevision;
        public uint Revision => revision;
        internal uint ColorRevision => colorRevision;
        public WhimTexGradient Clone()
        {
            var copy = (WhimTexGradient)MemberwiseClone();
            copy.colors = (ColorStop[])colors.Clone(); copy.alphas = (AlphaStop[])alphas.Clone();
            copy.customKeys = customKeys == null ? null : (WhimTexGradientCurveKey[])customKeys.Clone();
            copy.customPrepared = null;
            copy.customMarkers = null;
            copy.prepared = null;
            return copy;
        }
        public bool Equals(WhimTexGradient other)
        {
            if (ReferenceEquals(this, other)) return true;
            return other != null && wrapMode == other.wrapMode && EqualsRamp(other);
        }
        internal bool EqualsRamp(WhimTexGradient other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || colorMode != other.colorMode || interpolation != other.interpolation || huePath != other.huePath || chromaCorrection != other.chromaCorrection || colorSpace != other.colorSpace || smoothness != other.smoothness ||
                colors.Length != other.colors.Length || alphas.Length != other.alphas.Length) return false;
            for (int i=0;i<colors.Length;i++)
                if (!colors[i].color.Equals(other.colors[i].color) || colors[i].time != other.colors[i].time || colors[i].midpoint != other.colors[i].midpoint) return false;
            for (int i=0;i<alphas.Length;i++)
                if (alphas[i].alpha != other.alphas[i].alpha || alphas[i].time != other.alphas[i].time || alphas[i].midpoint != other.alphas[i].midpoint) return false;
            return EqualsCustom(other);
        }
        public override bool Equals(object other) => other is WhimTexGradient gradient && Equals(gradient);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (((int)colorMode * 397 ^ (int)interpolation) * 397 ^ (int)huePath) * 397 ^ chromaCorrection.GetHashCode();
                hash = ((hash * 397 ^ (int)wrapMode) * 397 ^ (int)colorSpace) * 397 ^ smoothness.GetHashCode();
                foreach (var key in colors) hash = hash * 397 ^ key.color.GetHashCode() ^ key.time.GetHashCode() ^ key.midpoint.GetHashCode();
                foreach (var key in alphas) hash = hash * 397 ^ key.alpha.GetHashCode() ^ key.time.GetHashCode() ^ key.midpoint.GetHashCode();
                hash = hash * 397 ^ CustomHash();
                return hash;
            }
        }

        internal ulong ContentFingerprint()
        {
            ulong hash = RenderFingerprint.Mix(14695981039346656037UL, (ulong)colorMode);
            hash = RenderFingerprint.Mix(hash, (ulong)interpolation);
            hash = RenderFingerprint.Mix(hash, (ulong)huePath);
            hash = RenderFingerprint.Float(hash, chromaCorrection);
            hash = RenderFingerprint.Mix(hash, (ulong)wrapMode);
            hash = RenderFingerprint.Mix(hash, (ulong)colorSpace);
            hash = RenderFingerprint.Float(hash, smoothness);
            hash = RenderFingerprint.Mix(hash, (ulong)colors.Length);
            foreach (var key in colors)
            {
                hash = RenderFingerprint.Float(hash, key.color.r); hash = RenderFingerprint.Float(hash, key.color.g);
                hash = RenderFingerprint.Float(hash, key.color.b); hash = RenderFingerprint.Float(hash, key.color.a);
                hash = RenderFingerprint.Float(hash, key.time); hash = RenderFingerprint.Float(hash, key.midpoint);
            }
            hash = RenderFingerprint.Mix(hash, (ulong)alphas.Length);
            foreach (var key in alphas)
            {
                hash = RenderFingerprint.Float(hash, key.alpha); hash = RenderFingerprint.Float(hash, key.time);
                hash = RenderFingerprint.Float(hash, key.midpoint);
            }
            return CustomFingerprint(hash);
        }

        public WhimTexGradientColorMode ColorMode
        {
            get => interpolation == WhimTexGradientInterpolation.Custom ? customColorMode : colorMode;
            set
            {
                if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (interpolation == WhimTexGradientInterpolation.Custom)
                {
                    if (customColorMode != value) throw new InvalidOperationException("Use ConvertCustomColorMode to convert manual curves.");
                    return;
                }
                if (colorMode == value) return; colorMode = value; Invalidate();
            }
        }
        public WhimTexGradientInterpolation Interpolation
        {
            get => interpolation;
            set
            {
                if (!Enum.IsDefined(typeof(WhimTexGradientInterpolation), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (interpolation == value) return;
                if (value == WhimTexGradientInterpolation.Custom && !HasCustomCurves) BuildCustom(colorMode);
                interpolation = value; Invalidate();
            }
        }
        public WhimTexGradientHuePath HuePath
        {
            get => huePath;
            set { if (!Enum.IsDefined(typeof(WhimTexGradientHuePath), value)) throw new ArgumentOutOfRangeException(nameof(value)); if (huePath == value) return; huePath = value; Invalidate(); }
        }
        public float ChromaCorrection
        {
            get => chromaCorrection;
            set { if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); if (chromaCorrection == value) return; chromaCorrection = value; Invalidate(); }
        }
        public WhimTexGradientWrapMode WrapMode
        {
            get => wrapMode;
            set
            {
                if (!Enum.IsDefined(typeof(WhimTexGradientWrapMode), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (wrapMode == value) return;
                wrapMode = value;
                InvalidateSampling();
            }
        }
        // Storage/output RGB encoding, independent of interpolation. Alpha is not color-encoded.
        public ColorSpace ColorSpace
        {
            get => colorSpace;
            set { if (value != ColorSpace.Gamma && value != ColorSpace.Linear) throw new ArgumentOutOfRangeException(nameof(value)); colorSpace = value; Invalidate(); }
        }
        // Smoothness controls Blend and Balanced; the other algorithms retain their own shape.
        public float Smoothness
        {
            get => smoothness;
            set { if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); if (smoothness == value) return; smoothness = value; Invalidate(); }
        }
        public GradientColorKey[] ColorKeys
            => interpolation == WhimTexGradientInterpolation.Custom ? CustomColorKeys() : AutomaticColorKeys;
        public GradientAlphaKey[] AlphaKeys
            => interpolation == WhimTexGradientInterpolation.Custom ? CustomAlphaKeys() : AutomaticAlphaKeys;
        public WhimTexGradientColorMode AutomaticColorMode => colorMode;
        public GradientColorKey[] AutomaticColorKeys
        {
            get
            {
                var result = new GradientColorKey[colors.Length];
                for (int i = 0; i < result.Length; i++) result[i] = new GradientColorKey(colors[i].color, colors[i].time);
                return result;
            }
        }
        public GradientAlphaKey[] AutomaticAlphaKeys
        {
            get
            {
                var result = new GradientAlphaKey[alphas.Length];
                for (int i = 0; i < result.Length; i++) result[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
                return result;
            }
        }

        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys)
        {
            if (colorKeys == null || alphaKeys == null) throw new ArgumentNullException();
            var c = (GradientColorKey[])colorKeys.Clone();
            var a = (GradientAlphaKey[])alphaKeys.Clone();
            Array.Sort(c, (x, y) => x.time.CompareTo(y.time));
            Array.Sort(a, (x, y) => x.time.CompareTo(y.time));
            Validate(c, a);
            var oldColors = colors; var oldAlphas = alphas;
            colors = new ColorStop[c.Length]; alphas = new AlphaStop[a.Length];
            for (int i = 0; i < c.Length; i++)
            {
                colors[i] = new ColorStop(c[i].color, c[i].time);
                if (oldColors != null) foreach (var old in oldColors)
                    if (old.time == c[i].time) { colors[i].midpoint = NormalizeMidpoint(old.midpoint); break; }
            }
            for (int i = 0; i < a.Length; i++)
            {
                alphas[i] = new AlphaStop(a[i].alpha, a[i].time);
                if (oldAlphas != null) foreach (var old in oldAlphas)
                    if (old.time == a[i].time) { alphas[i].midpoint = NormalizeMidpoint(old.midpoint); break; }
            }
            Invalidate();
        }

        public void Reverse()
        {
            if (interpolation == WhimTexGradientInterpolation.Custom) { ReverseCustom(); return; }
            float lastColorMidpoint = colors[colors.Length - 1].midpoint;
            float lastAlphaMidpoint = alphas[alphas.Length - 1].midpoint;
            Array.Reverse(colors);
            Array.Reverse(alphas);
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i].time = 1 - colors[i].time;
                colors[i].midpoint = i + 1 < colors.Length
                    ? Mathf.Clamp(1 - NormalizeMidpoint(colors[i + 1].midpoint), .01f, .99f) : lastColorMidpoint;
            }
            for (int i = 0; i < alphas.Length; i++)
            {
                alphas[i].time = 1 - alphas[i].time;
                alphas[i].midpoint = i + 1 < alphas.Length
                    ? Mathf.Clamp(1 - NormalizeMidpoint(alphas[i + 1].midpoint), .01f, .99f) : lastAlphaMidpoint;
            }
            if (huePath == WhimTexGradientHuePath.Increasing) huePath = WhimTexGradientHuePath.Decreasing;
            else if (huePath == WhimTexGradientHuePath.Decreasing) huePath = WhimTexGradientHuePath.Increasing;
            Invalidate();
        }

        private static float NormalizeMidpoint(float value) => Finite(value) && value >= .01f && value <= .99f ? value : .5f;
        public float GetMidpoint(bool alpha, int index) => NormalizeMidpoint(alpha ? alphas[index].midpoint : colors[index].midpoint);
        public void SetMidpoint(bool alpha, int index, float value)
        {
            if (index < 0 || index >= (alpha ? alphas.Length : colors.Length)) throw new ArgumentOutOfRangeException(nameof(index));
            if (!Finite(value) || value < .01f || value > .99f) throw new ArgumentOutOfRangeException(nameof(value));
            if (alpha) alphas[index].midpoint = value; else colors[index].midpoint = value;
            Invalidate();
        }

        internal Color EvaluateEncoded(float time) => EvaluateCore(time, true);
        public Color Evaluate(float time) => EvaluateCore(time, false);
        // Caller owns the buffer. No texture creation or per-sample allocations.
        public void Bake(Color[] destination)
        {
            if (destination == null || destination.Length == 0) throw new ArgumentException("A nonempty buffer is required.");
            for (int i = 0; i < destination.Length; i++)
                destination[i] = Evaluate(destination.Length == 1 ? 0 : i / (float)(destination.Length - 1));
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize()
        {
            prepared = null;
            customPrepared = null;
            customMarkers = null;
            unchecked { revision++; colorRevision++; }
        }
        private float WrapTime(float time)
        {
            switch (wrapMode)
            {
                case WhimTexGradientWrapMode.Repeat:
                    return time - Mathf.Floor(time);
                case WhimTexGradientWrapMode.Mirror:
                {
                    float mirrored = time - Mathf.Floor(time * .5f) * 2f;
                    return mirrored <= 1f ? mirrored : 2f - mirrored;
                }
                default:
                    return Mathf.Clamp01(time);
            }
        }
        private void InvalidateSampling() { unchecked { revision++; } }
        private void Invalidate() { prepared = null; customPrepared = null; customMarkers = null; unchecked { revision++; colorRevision++; } }
        private static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        private static void Validate(GradientColorKey[] c, GradientAlphaKey[] a)
        {
            if (c == null || a == null || c.Length < 1 || a.Length < 1 || c.Length > 64 || a.Length > 64)
                throw new ArgumentException("Each track requires 1..64 keys.");
            for (int i = 0; i < c.Length; i++)
            {
                CheckTime(c[i].time, i == 0 ? -1 : c[i - 1].time);
                Color v = c[i].color;
                if (!Finite(v.r) || !Finite(v.g) || !Finite(v.b) ||
                    Mathf.Abs(v.r) > 65504 || Mathf.Abs(v.g) > 65504 || Mathf.Abs(v.b) > 65504)
                    throw new ArgumentException("RGB must be finite and within half-float range.");
            }
            for (int i = 0; i < a.Length; i++)
            {
                CheckTime(a[i].time, i == 0 ? -1 : a[i - 1].time);
                if (!Finite(a[i].alpha) || a[i].alpha < 0 || a[i].alpha > 1)
                    throw new ArgumentException("Alpha must be finite and within 0..1.");
            }
        }
        private static void CheckTime(float t, float previous)
        {
            if (!Finite(t) || t < 0 || t > 1 || t <= previous || (previous >= 0 && t - previous < 0.000001f))
                throw new ArgumentException("Key times must increase in 0..1, at least 0.000001 apart.");
        }

    }
}
