using System;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public enum WhimTexGradientMode
    {
        Classic = 3, Linear = 4, Perceptual = 5, Fixed = 2
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
        [SerializeField] private ColorSpace colorSpace = ColorSpace.Gamma;
        [SerializeField] private float smoothness = 1;
        [NonSerialized] private Curve[] curves;
        [NonSerialized] private uint revision;
        public uint Revision => revision;
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
                int hash = ((int)mode * 397 ^ (int)colorSpace) * 397 ^ smoothness.GetHashCode();
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
            set { if (!Finite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); if (smoothness == value) return; smoothness = value; unchecked { revision++; } }
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
            time = Mathf.Clamp01(time);
            if (mode == WhimTexGradientMode.Fixed)
            {
                int ci = 0, ai = 0;
                while (ci < colors.Length - 1 && time > colors[ci].time) ci++;
                while (ai < alphas.Length - 1 && time > alphas[ai].time) ai++;
                Color value = colors[ci].color;
                value.a = alphas[ai].alpha;
                return value;
            }
            Vector3 rgb = new Vector3(curves[0].Evaluate(time, smoothness),
                curves[1].Evaluate(time, smoothness), curves[2].Evaluate(time, smoothness));
            rgb = FromWorking(rgb);
            return new Color(rgb.x, rgb.y, rgb.z, Mathf.Clamp01(curves[3].Evaluate(time, smoothness)));
        }

        // Caller owns the buffer. No texture creation or per-sample allocations.
        public void Bake(Color[] destination)
        {
            if (destination == null || destination.Length == 0) throw new ArgumentException("A nonempty buffer is required.");
            for (int i = 0; i < destination.Length; i++)
                destination[i] = Evaluate(destination.Length == 1 ? 0 : i / (float)(destination.Length - 1));
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() => Invalidate();
        private void Invalidate() { curves = null; unchecked { revision++; } }
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
            if (!Enum.IsDefined(typeof(WhimTexGradientMode), mode) ||
                (colorSpace != ColorSpace.Gamma && colorSpace != ColorSpace.Linear) ||
                !Finite(smoothness) || smoothness < 0 || smoothness > 1)
                throw new ArgumentException("Invalid serialized gradient settings.");
            var built = new Curve[4];
            var values = new Vector3[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                values[i] = ToWorking(new Vector3(colors[i].color.r, colors[i].color.g, colors[i].color.b));
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
                built[channel] = new Curve(x, y);
            }
            var ax = new float[alphas.Length * 2 - 1]; var ay = new float[ax.Length];
            for (int i = 0; i < alphas.Length; i++)
            {
                ax[2*i] = alphas[i].time; ay[2*i] = alphas[i].alpha;
                if (i + 1 == alphas.Length) continue;
                ax[2*i+1] = MidpointTime(alphas[i].time, alphas[i+1].time, GetMidpoint(true, i));
                ay[2*i+1] = (alphas[i].alpha + alphas[i+1].alpha) * .5f;
            }
            built[3] = new Curve(ax, ay);
            curves = built;
        }

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

        private sealed class Curve
        {
            private readonly float[] x, y, tangent;
            internal Curve(float[] x, float[] y)
            {
                this.x = x; this.y = y; tangent = new float[x.Length];
                if (x.Length == 1) return;
                var h = new float[x.Length - 1]; var d = new float[h.Length];
                for (int i = 0; i < h.Length; i++) { h[i] = x[i+1]-x[i]; d[i] = (y[i+1]-y[i])/h[i]; }
                if (x.Length == 2) { tangent[0] = tangent[1] = d[0]; return; }
                tangent[0] = Endpoint(h[0], h[1], d[0], d[1]);
                int last = h.Length - 1;
                tangent[x.Length-1] = Endpoint(h[last], h[last-1], d[last], d[last-1]);
                for (int i = 1; i < x.Length-1; i++)
                {
                    if (d[i-1] == 0 || d[i] == 0 || Math.Sign(d[i-1]) != Math.Sign(d[i])) continue;
                    double w1 = 2*h[i]+h[i-1], w2 = h[i]+2*h[i-1];
                    tangent[i] = (float)((w1+w2)/(w1/d[i-1]+w2/d[i]));
                }
            }
            private static float Endpoint(float h0, float h1, float d0, float d1)
            {
                float m = ((2*h0+h1)*d0-h0*d1)/(h0+h1);
                if (Math.Sign(m) != Math.Sign(d0)) return 0;
                return Math.Sign(d0) != Math.Sign(d1) && Mathf.Abs(m) > 3*Mathf.Abs(d0) ? 3*d0 : m;
            }
            internal float Evaluate(float t, float smooth)
            {
                if (t <= x[0]) return y[0];
                if (t >= x[x.Length-1]) return y[y.Length-1];
                int lo = 0, hi = x.Length-1;
                while (hi-lo > 1) { int mid = (lo+hi)/2; if (t < x[mid]) hi = mid; else lo = mid; }
                float h = x[hi]-x[lo], u = (t-x[lo])/h, u2 = u*u, u3 = u2*u;
                float linear = Mathf.LerpUnclamped(y[lo], y[hi], u);
                float cubic = (2*u3-3*u2+1)*y[lo] + (u3-2*u2+u)*h*tangent[lo] +
                    (-2*u3+3*u2)*y[hi] + (u3-u2)*h*tangent[hi];
                return Mathf.LerpUnclamped(linear, Mathf.Clamp(cubic, Mathf.Min(y[lo],y[hi]), Mathf.Max(y[lo],y[hi])), smooth);
            }
        }
    }
}
