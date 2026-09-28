using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexGradientMode
    {
        Classic = 3, Linear = 4, Perceptual = 5, Fixed = 2
    }

    public enum WhimTexGradientWrapMode
    {
        Clamp, Repeat, Mirror
    }

    [Serializable]
    public sealed class WhimTexGradient : ISerializationCallbackReceiver, IEquatable<WhimTexGradient>
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
        [SerializeField] private WhimTexGradientMode mode = WhimTexGradientMode.Classic;
        [SerializeField] private WhimTexGradientWrapMode wrapMode = WhimTexGradientWrapMode.Clamp;
        [SerializeField] private ColorSpace colorSpace = ColorSpace.Gamma;
        [SerializeField] private float smoothness = 1;
        [NonSerialized] private Curve[] curves;
        [NonSerialized] private float[] chromaReduction;
        [NonSerialized] private RoundedMap roundedColorMap, roundedAlphaMap;
        [NonSerialized] private uint revision, colorRevision;
        public uint Revision => revision;
        internal uint ColorRevision => colorRevision;
        public WhimTexGradient Clone()
        {
            var copy = (WhimTexGradient)MemberwiseClone();
            copy.colors = (ColorStop[])colors.Clone(); copy.alphas = (AlphaStop[])alphas.Clone();
            copy.curves = null;
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
            if (other == null || mode != other.mode || colorSpace != other.colorSpace || smoothness != other.smoothness ||
                colors.Length != other.colors.Length || alphas.Length != other.alphas.Length) return false;
            for (int i=0;i<colors.Length;i++)
                if (!colors[i].color.Equals(other.colors[i].color) || colors[i].time != other.colors[i].time || colors[i].midpoint != other.colors[i].midpoint) return false;
            for (int i=0;i<alphas.Length;i++)
                if (alphas[i].alpha != other.alphas[i].alpha || alphas[i].time != other.alphas[i].time || alphas[i].midpoint != other.alphas[i].midpoint) return false;
            return true;
        }
        public override bool Equals(object other) => other is WhimTexGradient gradient && Equals(gradient);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (((int)mode * 397 ^ (int)wrapMode) * 397 ^ (int)colorSpace) * 397 ^ smoothness.GetHashCode();
                foreach (var key in colors) hash = hash * 397 ^ key.color.GetHashCode() ^ key.time.GetHashCode() ^ key.midpoint.GetHashCode();
                foreach (var key in alphas) hash = hash * 397 ^ key.alpha.GetHashCode() ^ key.time.GetHashCode() ^ key.midpoint.GetHashCode();
                return hash;
            }
        }

        public WhimTexGradientMode Mode
        {
            get => mode;
            set { if (!Enum.IsDefined(typeof(WhimTexGradientMode), value)) throw new ArgumentOutOfRangeException(nameof(value)); mode = value; Invalidate(); }
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
        // Stored and returned RGB share this space. Alpha is always linear.
        public ColorSpace ColorSpace
        {
            get => colorSpace;
            set { if (value != ColorSpace.Gamma && value != ColorSpace.Linear) throw new ArgumentOutOfRangeException(nameof(value)); colorSpace = value; Invalidate(); }
        }
        // Smoothness changes the interpolation curve, not the color space. Fixed ignores it.
        public float Smoothness
        {
            get => smoothness;
            set { if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); if (smoothness == value) return; smoothness = value; unchecked { revision++; colorRevision++; } }
        }
        public GradientColorKey[] ColorKeys
        {
            get
            {
                var result = new GradientColorKey[colors.Length];
                for (int i = 0; i < result.Length; i++) result[i] = new GradientColorKey(colors[i].color, colors[i].time);
                return result;
            }
        }
        public GradientAlphaKey[] AlphaKeys
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

        private static float NormalizeMidpoint(float value) => Finite(value) && value >= .01f && value <= .99f ? value : .5f;
        private static float MidpointTime(float left, float right, float ratio) =>
            Mathf.Clamp(Mathf.Lerp(left, right, ratio), left + .0000001f, right - .0000001f);
        public float GetMidpoint(bool alpha, int index) => NormalizeMidpoint(alpha ? alphas[index].midpoint : colors[index].midpoint);
        public void SetMidpoint(bool alpha, int index, float value)
        {
            if (index < 0 || index >= (alpha ? alphas.Length : colors.Length)) throw new ArgumentOutOfRangeException(nameof(index));
            if (!Finite(value) || value < .01f || value > .99f) throw new ArgumentOutOfRangeException(nameof(value));
            if (alpha) alphas[index].midpoint = value; else colors[index].midpoint = value;
            Invalidate();
        }

        internal Color EvaluateEncoded(float time)
        {
            Color value = Evaluate(time);
            return colorSpace == ColorSpace.Linear ? HdrUtility.Encode(value) : value;
        }
        public Color Evaluate(float time)
        {
            if (!Finite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            Prepare();
            time = WrapTime(time);
            if (mode == WhimTexGradientMode.Fixed)
            {
                int ci = 0, ai = 0;
                while (ci < colors.Length - 1 && time > colors[ci].time) ci++;
                while (ai < alphas.Length - 1 && time > alphas[ai].time) ai++;
                Color value = colors[ci].color;
                value.a = alphas[ai].alpha;
                return value;
            }
            float colorTime = time, alphaTime = time;
            if (roundedColorMap != null) colorTime = roundedColorMap.Evaluate(time, smoothness);
            if (roundedAlphaMap != null) alphaTime = roundedAlphaMap.Evaluate(time, smoothness);
            Vector3 rgb = new Vector3(EvaluateChannel(0, colorTime),
                EvaluateChannel(1, colorTime), EvaluateChannel(2, colorTime));
            if (chromaReduction != null && smoothness > 0 && colorTime > colors[0].time && colorTime < colors[colors.Length-1].time)
            {
                int lo = 0, hi = colors.Length-1;
                while (hi-lo > 1) { int mid = (lo+hi)/2; if (colorTime < colors[mid].time) hi = mid; else lo = mid; }
                float midpoint = MidpointTime(colors[lo].time, colors[hi].time, GetMidpoint(false, lo));
                float u = colorTime <= midpoint ? .5f*(colorTime-colors[lo].time)/(midpoint-colors[lo].time) :
                    .5f + .5f*(colorTime-midpoint)/(colors[hi].time-midpoint);
                float envelope = 4*u*(1-u);
                float scale = 1-smoothness*chromaReduction[lo]*envelope*envelope;
                rgb.y *= scale; rgb.z *= scale;
            }
            rgb = FromWorking(rgb);
            return new Color(rgb.x, rgb.y, rgb.z, Mathf.Clamp01(EvaluateChannel(3, alphaTime)));
        }

        private float EvaluateChannel(int channel, float time) => curves[channel].Evaluate(time, smoothness);

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
            curves = null;
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
        private void Invalidate() { curves = null; unchecked { revision++; colorRevision++; } }
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

        private void Prepare()
        {
            if (curves != null) return;
            if (colors == null || alphas == null) throw new ArgumentException("Missing gradient tracks.");
            var colorKeys = ColorKeys;
            var alphaKeys = AlphaKeys;
            Validate(colorKeys, alphaKeys);
            if (!Enum.IsDefined(typeof(WhimTexGradientMode), mode) || !Enum.IsDefined(typeof(WhimTexGradientWrapMode), wrapMode) ||
                (colorSpace != ColorSpace.Gamma && colorSpace != ColorSpace.Linear) ||
                !Finite(smoothness) || smoothness < 0 || smoothness > 1)
                throw new ArgumentException("Invalid serialized gradient settings.");
            var built = new Curve[4];
            var values = new Vector3[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                values[i] = ToWorking(new Vector3(colors[i].color.r, colors[i].color.g, colors[i].color.b));
            chromaReduction = null;
            roundedColorMap = roundedAlphaMap = null;
            var colorHeld = new bool[Math.Max(0, colors.Length-1)];
            for (int i = 0; i < colorHeld.Length; i++)
                colorHeld[i] = colors[i].color.r == colors[i+1].color.r &&
                    colors[i].color.g == colors[i+1].color.g && colors[i].color.b == colors[i+1].color.b;
            if (mode == WhimTexGradientMode.Perceptual && colors.Length > 1)
            {
                chromaReduction = new float[colors.Length-1];
                for (int i = 0; i < chromaReduction.Length; i++)
                {
                    var a = new Vector2(values[i].y, values[i].z);
                    var b = new Vector2(values[i+1].y, values[i+1].z);
                    float ca = a.magnitude, cb = b.magnitude;
                    // Reference-fitted perceptual variant: only opposing chroma bends
                    // toward neutral. Neutral/same-hue ramps and the stops stay intact.
                    if (ca > .00001f && cb > .00001f)
                        chromaReduction[i] = .5f*Mathf.Clamp01(1-(a+b).magnitude/(ca+cb));
                }
            }
            for (int channel = 0; channel < 3; channel++)
            {
                var x = new float[colors.Length * 2 - 1]; var y = new float[x.Length];
                for (int i = 0; i < colors.Length; i++)
                {
                    x[2*i] = colors[i].time; y[2*i] = values[i][channel];
                    if (i + 1 == colors.Length) continue;
                    x[2*i+1] = MidpointTime(colors[i].time, colors[i+1].time, GetMidpoint(false, i));
                    y[2*i+1] = (values[i][channel] + values[i+1][channel]) * .5f;
                }
                built[channel] = new Curve(x, y, colorHeld);
            }
            var ax = new float[alphas.Length * 2 - 1]; var ay = new float[ax.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                ax[2*i] = alphas[i].time; ay[2*i] = alphas[i].alpha;
                if (i + 1 == alphas.Length) continue;
                ax[2*i+1] = MidpointTime(alphas[i].time, alphas[i+1].time, GetMidpoint(true, i));
                ay[2*i+1] = (alphas[i].alpha + alphas[i+1].alpha) * .5f;
            }

            roundedColorMap = new RoundedMap(built[0].Times, colorHeld,
                (key, end) => HasFlatColorJet(built, values, key, end));
            var held = new bool[alphas.Length-1];
            for (int i = 0; i < held.Length; i++) held[i] = alphas[i].alpha == alphas[i+1].alpha;
            built[3] = new Curve(ax, ay, held);
            roundedAlphaMap = new RoundedMap(ax, held, null);
            curves = built;
        }

        private bool HasFlatColorJet(Curve[] built, Vector3[] values, int key, bool end)
        {
            int node = 2*key, segment = end ? node-1 : node;
            double h = built[0].Times[segment+1]-built[0].Times[segment];
            var first = new Vector3(); var second = new Vector3();
            for (int c = 0; c < 3; c++)
            {
                built[c].Jet(segment, end, out double d1, out double d2);
                first[c] = (float)(d1*h); second[c] = (float)(d2*h*h);
            }
            if (mode == WhimTexGradientMode.Perceptual)
            {
                float strength = chromaReduction[end ? key-1 : key];
                second.y -= 8*strength*values[key].y;
                second.z -= 8*strength*values[key].z;
                Vector3 q = LabToLms(values[key]), q1 = LabToLms(first), q2 = LabToLms(second);
                for (int c = 0; c < 3; c++)
                {
                    first[c] = 3*q[c]*q[c]*q1[c];
                    second[c] = 6*q[c]*q1[c]*q1[c]+3*q[c]*q[c]*q2[c];
                }
            }
            // The final linear transform is invertible; zero LMS jets are zero RGB jets.
            return first.sqrMagnitude < 1e-24f && second.sqrMagnitude < 1e-24f;
        }

        private static Vector3 LabToLms(Vector3 v) => new Vector3(
            v.x+.3963377774f*v.y+.2158037573f*v.z,
            v.x-.1055613458f*v.y-.0638541728f*v.z,
            v.x-.0894841775f*v.y-1.291485548f*v.z);

        private Vector3 ToWorking(Vector3 v)
        {
            if (mode == WhimTexGradientMode.Classic)
                return colorSpace == ColorSpace.Linear ? Transfer(v, false) : v;
            if (colorSpace == ColorSpace.Gamma) v = Transfer(v, true);
            return mode == WhimTexGradientMode.Perceptual ? ToLab(v) : v;
        }
        private Vector3 FromWorking(Vector3 v)
        {
            if (mode == WhimTexGradientMode.Classic)
                return colorSpace == ColorSpace.Linear ? Transfer(v, true) : v;
            if (mode == WhimTexGradientMode.Perceptual) v = FromLab(v);
            return colorSpace == ColorSpace.Gamma ? Transfer(v, false) : v;
        }
        private static Vector3 Transfer(Vector3 v, bool decode)
        {
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Abs(v[i]), sign = Mathf.Sign(v[i]);
                v[i] = sign * (decode ? (a <= .04045f ? a / 12.92f : Mathf.Pow((a + .055f) / 1.055f, 2.4f)) :
                    (a <= .0031308f ? a * 12.92f : 1.055f * Mathf.Pow(a, 1f / 2.4f) - .055f));
            }
            return v;
        }
        private static float Cbrt(float x) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 1f / 3f);
        private static Vector3 ToLab(Vector3 v)
        {
            float l = Cbrt(.4122214708f*v.x + .5363325363f*v.y + .0514459929f*v.z);
            float m = Cbrt(.2119034982f*v.x + .6806995451f*v.y + .1073969566f*v.z);
            float s = Cbrt(.0883024619f*v.x + .2817188376f*v.y + .6299787005f*v.z);
            return new Vector3(.2104542553f*l + .793617785f*m - .0040720468f*s,
                1.9779984951f*l - 2.428592205f*m + .4505937099f*s,
                .0259040371f*l + .7827717662f*m - .808675766f*s);
        }
        private static Vector3 FromLab(Vector3 v)
        {
            float l = v.x + .3963377774f*v.y + .2158037573f*v.z;
            float m = v.x - .1055613458f*v.y - .0638541728f*v.z;
            float s = v.x - .0894841775f*v.y - 1.291485548f*v.z;
            l *= l*l; m *= m*m; s *= s*s;
            return new Vector3(4.0767416621f*l - 3.3077115913f*m + .2309699292f*s,
                -1.2684380046f*l + 2.6097574011f*m - .3413193965f*s,
                -.0041960863f*l - .7034186147f*m + 1.707614701f*s);
        }

        private sealed class RoundedMap
        {
            private struct Patch
            {
                internal double key, width, left, right;
                internal bool end;
            }
            private readonly Patch[] patches;
            internal RoundedMap(float[] x, bool[] held, Func<int, bool, bool> flatJet)
            {
                var list = new System.Collections.Generic.List<Patch>();
                int previousEnd = -1;
                for (int i = 0; i < held.Length; i++)
                {
                    if (held[i]) continue;
                    int start = i;
                    while (i+1 < held.Length && !held[i+1]) i++;
                    int end = i+1, next = end;
                    while (next < held.Length && held[next]) next++;
                    Add(start, false, previousEnd < 0 ? double.PositiveInfinity : (x[2*start]-x[2*previousEnd])*.5);
                    Add(end, true, next == held.Length ? double.PositiveInfinity : (x[2*next]-x[2*end])*.5);
                    previousEnd = end;
                }
                patches = list.ToArray();
                void Add(int key, bool end, double limit)
                {
                    if (flatJet != null && flatJet(key, end)) return;
                    double value = x[2*key];
                    double half = end ? value-x[2*key-1] : x[2*key+1]-value;
                    // Use the whole available span up to the midpoint, distributing
                    // the speed surplus instead of returning abruptly to the base curve.
                    double width = Math.Min(half, limit);
                    if (width <= 0) return;
                    if (end) value = 1-value;
                    list.Add(new Patch { key=value, width=width, left=Math.Max(0,value-width), right=value+width, end=end });
                }
            }
            internal float Evaluate(float time, float smooth)
            {
                if (smooth == 0) return time;
                foreach (var patch in patches)
                {
                    double t = patch.end ? 1.0-time : time;
                    if (t < patch.left || t > patch.right) continue;
                    double h = patch.right-patch.left, u = (t-patch.left)/h, q = h/patch.width;
                    // Integral of a smooth onset plus a broad positive speed surplus.
                    // Both ends have zero second derivative; the final slope is q.
                    // q=1 handles domain-clipped supports; q=2 is symmetric rounding.
                    const double onsetWidth = .2;
                    double v = Math.Min(u/onsetWidth, 1);
                    double onset = u < onsetWidth ? onsetWidth*v*v*v*(1-.5*v) : u-onsetWidth*.5;
                    double fast = onset+onsetWidth*.5*u*u*u*(10+u*(-15+6*u));
                    double slow = u*u*u*(2-u);
                    double w = (2-q)*fast+(q-1)*slow;
                    double mapped = patch.key+patch.width*w;
                    if (patch.end) mapped = 1-mapped;
                    return Mathf.LerpUnclamped(time, (float)mapped, smooth);
                }
                return time;
            }
        }

        private sealed class Curve
        {
            private readonly float[] x, y, tangent;
            private readonly float[] incoming, outgoing;
            internal float[] Times => x;
            internal Curve(float[] x, float[] y, bool[] held)
            {
                this.x = x; this.y = y; tangent = new float[x.Length];
                if (x.Length == 1) return;
                var h = new float[x.Length - 1]; var d = new float[h.Length];
                for (int i = 0; i < h.Length; i++) { h[i] = x[i+1]-x[i]; d[i] = (y[i+1]-y[i])/h[i]; }
                for (int i = 1; i < x.Length-1; i++)
                {
                    if (d[i-1] == 0 || d[i] == 0 || Math.Sign(d[i-1]) != Math.Sign(d[i])) continue;
                    double w1 = 2*h[i]+h[i-1], w2 = h[i]+2*h[i-1];
                    tangent[i] = (float)((w1+w2)/(w1/d[i-1]+w2/d[i]));
                }
                tangent[0] = d[0]; tangent[x.Length-1] = d[d.Length-1];
                if (held != null)
                {
                    incoming = (float[])tangent.Clone(); outgoing = (float[])tangent.Clone();
                    for (int i = 0; i < held.Length; i++)
                    {
                        if (held[i]) continue;
                        if (i == 0 || held[i-1]) outgoing[2*i] = d[2*i];
                        if (i == held.Length-1 || held[i+1]) incoming[2*i+2] = d[2*i+1];
                    }
                }
            }
            internal void Jet(int segment, bool end, out double first, out double second)
            {
                double h = x[segment+1]-x[segment], d = ((double)y[segment+1]-y[segment])/h;
                double a = outgoing == null ? tangent[segment] : outgoing[segment];
                double b = incoming == null ? tangent[segment+1] : incoming[segment+1];
                first = end ? b : a;
                second = end ? 2*(-3*d+a+2*b)/h : 2*(3*d-2*a-b)/h;
            }
            internal float Evaluate(float t, float smooth)
            {
                if (t <= x[0]) return y[0];
                if (t >= x[x.Length-1]) return y[y.Length-1];
                int lo = 0, hi = x.Length-1;
                while (hi-lo > 1) { int mid = (lo+hi)/2; if (t < x[mid]) hi = mid; else lo = mid; }
                float h = x[hi]-x[lo], u = (t-x[lo])/h;
                float linear = Mathf.LerpUnclamped(y[lo], y[hi], u);
                if (outgoing != null)
                {
                    double v = ((double)t-x[lo])/h, v2 = v*v, v3 = v2*v;
                    double value = (2*v3-3*v2+1)*y[lo]+(v3-2*v2+v)*h*outgoing[lo]+
                        (-2*v3+3*v2)*y[hi]+(v3-v2)*h*incoming[hi];
                    return Mathf.LerpUnclamped(linear, Mathf.Clamp((float)value, Mathf.Min(y[lo],y[hi]), Mathf.Max(y[lo],y[hi])), smooth);
                }
                float u2 = u*u, u3 = u2*u;
                float cubic = (2*u3-3*u2+1)*y[lo] + (u3-2*u2+u)*h*tangent[lo] +
                    (-2*u3+3*u2)*y[hi] + (u3-u2)*h*tangent[hi];
                return Mathf.LerpUnclamped(linear, Mathf.Clamp(cubic, Mathf.Min(y[lo],y[hi]), Mathf.Max(y[lo],y[hi])), smooth);
            }
        }
    }
}
