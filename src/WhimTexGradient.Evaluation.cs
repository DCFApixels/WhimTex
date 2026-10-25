using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexGradient
    {
        private sealed class PreparedState
        {
            internal double[] times, reduction, minimumRgb, maximumRgb;
            internal GradientCurve[] channels, hueRuns;
            internal GradientCurve alpha;
            internal GradientVector[] residuals;
        }

        private Color EvaluateCore(float time, bool encoded)
        {
            if (!Finite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            if (interpolation == WhimTexGradientInterpolation.Custom) return EvaluateCustom(WrapTime(time), encoded);
            Prepare();
            double t = WrapTime(time);
            if (interpolation == WhimTexGradientInterpolation.Fixed)
            {
                int ci = 0, ai = 0;
                while (ci < colors.Length - 1 && t > colors[ci].time) ci++;
                while (ai < alphas.Length - 1 && t > alphas[ai].time) ai++;
                return KeyColor(ci, alphas[ai].alpha, encoded);
            }
            float alpha = (float)GradientCurve.Clamp(prepared.alpha.Evaluate(t), 0, 1);
            if (t <= colors[0].time) return KeyColor(0, alpha, encoded);
            if (t >= colors[colors.Length - 1].time) return KeyColor(colors.Length - 1, alpha, encoded);
            int i = GradientCurve.Segment(prepared.times, t);
            if (t == colors[i].time) return KeyColor(i, alpha, encoded);
            double u = (t - prepared.times[i]) / (prepared.times[i + 1] - prepared.times[i]);
            var value = new GradientVector(prepared.channels[0].Evaluate(t), prepared.channels[1].Evaluate(t), prepared.channels[2].Evaluate(t));
            if (colorMode == WhimTexGradientColorMode.Hue) value.z = prepared.hueRuns[i]?.Evaluate(t) ?? 0;
            GradientVector rgb = colorMode switch {
                WhimTexGradientColorMode.Gamma => value,
                WhimTexGradientColorMode.Linear => GradientColorMath.Encode(value),
                WhimTexGradientColorMode.Hue => GradientColorMath.FromLab(GradientColorMath.HueLab(value)),
                _ => GradientColorMath.FromLab(value)
            };
            if (colorMode == WhimTexGradientColorMode.Hue)
            {
                // Anchor neutral-axis matrix residuals to the original encoded RGB keys.
                double w = u * u * (3 - 2 * u);
                rgb += prepared.residuals[i] * (1 - w) + prepared.residuals[i + 1] * w;
            }
            if (chromaCorrection > 0 && prepared.reduction[i] > 0)
            {
                var lab = colorMode == WhimTexGradientColorMode.Perceptual ? value : GradientColorMath.ToLab(rgb);
                double envelope = 4 * u * (1 - u), scale = 1 - chromaCorrection * prepared.reduction[i] * envelope * envelope * envelope;
                lab.y *= scale; lab.z *= scale; rgb = GradientColorMath.FromLab(lab);
            }
            double minimum = prepared.minimumRgb[i], maximum = prepared.maximumRgb[i];
            rgb = new GradientVector(GradientCurve.Clamp(rgb.x, minimum, maximum),
                GradientCurve.Clamp(rgb.y, minimum, maximum), GradientCurve.Clamp(rgb.z, minimum, maximum));
            if (!encoded && colorSpace == ColorSpace.Linear) rgb = GradientColorMath.Decode(rgb);
            return new Color((float)rgb.x, (float)rgb.y, (float)rgb.z, alpha);
        }

        private Color KeyColor(int index, float alpha, bool encoded)
        {
            Color value = colors[index].color;
            if (encoded && colorSpace == ColorSpace.Linear)
            {
                var rgb = GradientColorMath.Encode(new GradientVector(value.r, value.g, value.b));
                value = new Color((float)rgb.x, (float)rgb.y, (float)rgb.z, alpha);
            }
            value.a = alpha;
            return value;
        }

        private void Prepare()
        {
            if (prepared != null) return;
            if (colors == null || alphas == null) throw new ArgumentException("Missing gradient tracks.");
            Validate(ColorKeys, AlphaKeys);
            if (!Enum.IsDefined(typeof(WhimTexGradientColorMode), colorMode) ||
                !Enum.IsDefined(typeof(WhimTexGradientInterpolation), interpolation) ||
                !Enum.IsDefined(typeof(WhimTexGradientHuePath), huePath) ||
                !Enum.IsDefined(typeof(WhimTexGradientWrapMode), wrapMode) ||
                (colorSpace != ColorSpace.Gamma && colorSpace != ColorSpace.Linear) ||
                !Finite(smoothness) || smoothness < 0 || smoothness > 1 ||
                !Finite(chromaCorrection) || chromaCorrection < 0 || chromaCorrection > 1)
                throw new ArgumentException("Invalid serialized gradient settings.");
            var state = new PreparedState();
            if (interpolation == WhimTexGradientInterpolation.Fixed) { prepared = state; return; }
            int n = colors.Length;
            state.times = new double[n]; var midpoint = new double[n];
            var encoded = new GradientVector[n]; var lab = new GradientVector[n]; var working = new GradientVector[n];
            var hues = new double[n]; var chromatic = new bool[n]; state.residuals = new GradientVector[n];
            for (int i = 0; i < n; i++)
            {
                state.times[i] = colors[i].time; midpoint[i] = GetMidpoint(false, i);
                var key = colors[i].color; encoded[i] = new GradientVector(key.r, key.g, key.b);
                if (colorSpace == ColorSpace.Linear) encoded[i] = GradientColorMath.Encode(encoded[i]);
                lab[i] = GradientColorMath.ToLab(encoded[i]);
                switch (colorMode)
                {
                    case WhimTexGradientColorMode.Gamma: working[i] = encoded[i]; break;
                    case WhimTexGradientColorMode.Linear: working[i] = GradientColorMath.Decode(encoded[i]); break;
                    case WhimTexGradientColorMode.Hue:
                        var lch = GradientColorMath.ToLch(lab[i]);
                        hues[i] = lch.z; chromatic[i] = lch.y > 1e-7 * Math.Max(1, Math.Abs(lch.x));
                        if (!chromatic[i])
                        {
                            lch.y = 0;
                            state.residuals[i] = encoded[i] - GradientColorMath.FromLab(new GradientVector(lch.x, 0, 0));
                        }
                        working[i] = new GradientVector(lch.x, lch.y, 0); break;
                    default: working[i] = lab[i]; break;
                }
            }
            // One shared RGB interval per segment, expanded by both keys in the same encoding as rgb.
            // Independent per-channel bounds would suppress intermediate hues absent from the keys.
            state.minimumRgb = new double[Math.Max(0, n - 1)];
            state.maximumRgb = new double[state.minimumRgb.Length];
            for (int i = 0; i < state.minimumRgb.Length; i++)
            {
                double minimum = 0, maximum = 1;
                for (int channel = 0; channel < 3; channel++)
                {
                    minimum = Math.Min(minimum, Math.Min(encoded[i][channel], encoded[i + 1][channel]));
                    maximum = Math.Max(maximum, Math.Max(encoded[i][channel], encoded[i + 1][channel]));
                }
                state.minimumRgb[i] = minimum; state.maximumRgb[i] = maximum;
            }
            state.channels = new GradientCurve[3];
            for (int channel = 0; channel < 3; channel++)
            {
                for (int other = 0; other < channel; other++)
                {
                    bool same = true; for (int i = 0; i < n; i++) if (working[i][channel] != working[i][other]) { same = false; break; }
                    if (same) { state.channels[channel] = state.channels[other]; break; }
                }
                if (state.channels[channel] != null) continue;
                var values = new double[n]; for (int i = 0; i < n; i++) values[i] = working[i][channel];
                state.channels[channel] = new GradientCurve(state.times, values, midpoint, interpolation, smoothness);
            }
            state.reduction = new double[Math.Max(0, n - 1)];
            if (chromaCorrection > 0) for (int i = 0; i < n - 1; i++)
            {
                var a = lab[i]; var b = lab[i + 1]; double ca = Math.Sqrt(a.y * a.y + a.z * a.z), cb = Math.Sqrt(b.y * b.y + b.z * b.z);
                if (ca <= 1e-5 || cb <= 1e-5) continue;
                double sumA = a.y + b.y, sumB = a.z + b.z;
                state.reduction[i] = .5 * GradientCurve.Clamp(1 - Math.Sqrt(sumA * sumA + sumB * sumB) / (ca + cb), 0, 1);
            }
            if (colorMode == WhimTexGradientColorMode.Hue)
            {
                // Gray keys separate hue runs; neighboring chromatic runs lend endpoint hue.
                state.hueRuns = new GradientCurve[Math.Max(0, n - 1)];
                for (int i = 0; i < n; i++)
                {
                    if (!chromatic[i]) continue;
                    int start = i; while (i + 1 < n && chromatic[i + 1]) i++;
                    int end = i; double hue = hues[start];
                    var x = new List<double>(); var y = new List<double>(); var m = new List<double>();
                    void Add(int index, double h) { x.Add(state.times[index]); y.Add(h); m.Add(midpoint[index]); }
                    if (start > 0) Add(start - 1, hue);
                    for (int j = start; j <= end; j++)
                    {
                        if (j > start) hue += GradientColorMath.HueDelta(hues[j - 1], hues[j], huePath);
                        Add(j, hue);
                    }
                    if (end + 1 < n) Add(end + 1, hue);
                    var curve = new GradientCurve(x.ToArray(), y.ToArray(), m.ToArray(), interpolation, smoothness);
                    for (int j = Math.Max(0, start - 1); j < Math.Min(n - 1, end + 1); j++) state.hueRuns[j] = curve;
                }
            }
            var ax = new double[alphas.Length]; var ay = new double[alphas.Length]; var am = new double[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) { ax[i] = alphas[i].time; ay[i] = alphas[i].alpha; am[i] = GetMidpoint(true, i); }
            state.alpha = new GradientCurve(ax, ay, am, interpolation, smoothness);
            prepared = state;
        }
    }
}
