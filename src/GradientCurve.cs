using System;
using System.Collections.Generic;

namespace DCFApixels.WhimTex
{
    internal sealed class GradientCurve
    {
        // Coefficients and fits are prepared once. Sampling never allocates or changes the fit.
        private static readonly double[] Ease = { 0, .0438, .099, .1644, .2303, .2988, .3667, .433, .4974, .5642, .6302, .6985, .7656, .8333, .8945, .9517, 1 };
        private readonly double[] times, values, midpoints, slopes;
        private readonly WhimTexGradientInterpolation interpolation;
        private readonly double smoothness;
        private readonly GradientAdaptiveCurve adaptive;

        internal GradientCurve(double[] x, double[] y, double[] midpoint, WhimTexGradientInterpolation interpolation, double smoothness)
        {
            this.interpolation = interpolation; this.smoothness = smoothness;
            times = x; values = y; midpoints = midpoint;
            if (x.Length < 2) return;
            if (interpolation == WhimTexGradientInterpolation.Adaptive)
                adaptive = new GradientAdaptiveCurve(x, y, midpoint);
            if (interpolation != WhimTexGradientInterpolation.Balanced) return;
            // Keep centered half-value nodes too, so crossing midpoint = 0.5 is continuous.
            var nx = new List<double>(); var ny = new List<double>(); var auxiliary = new List<bool>();
            for (int i = 0; i < x.Length; i++)
            {
                nx.Add(x[i]); ny.Add(y[i]); auxiliary.Add(false);
                if (i == x.Length - 1) continue;
                double p = x[i] + (x[i + 1] - x[i]) * Clamp(midpoint[i], .01, .99);
                if (p <= x[i] || p >= x[i + 1]) continue;
                nx.Add(p); ny.Add(y[i] * .5 + y[i + 1] * .5); auxiliary.Add(true);
            }
            times = nx.ToArray(); values = ny.ToArray();
            int count = times.Length;
            var widths = new double[count - 1]; var secants = new double[count - 1];
            for (int i = 0; i < count - 1; i++) { widths[i] = times[i + 1] - times[i]; secants[i] = (values[i + 1] - values[i]) / widths[i]; }
            slopes = new double[count];
            for (int i = 1; i < count - 1; i++)
            {
                double before = secants[i - 1], after = secants[i];
                if (before == 0 || after == 0 || Math.Sign(before) != Math.Sign(after)) continue;
                double w1 = 2 * widths[i] + widths[i - 1], w2 = widths[i] + 2 * widths[i - 1];
                slopes[i] = (w1 + w2) / (w1 / before + w2 / after);
            }
            for (int i = 1; i < count - 1; i++)
            {
                if (!auxiliary[i] || i != 1 && i != count - 2) continue;
                // Rational outer spans flatten the endpoints; match their adjacent curvature.
                double dl = secants[i - 1], dr = secants[i];
                if (dl == 0 || dr == 0 || Math.Sign(dl) != Math.Sign(dr)) { slopes[i] = 0; continue; }
                double hl = widths[i - 1], hr = widths[i], rl = i == 1 ? 6 : 3, rr = i == count - 2 ? 6 : 3;
                double proposed = ((rl * dl - slopes[i - 1]) / hl + (rr * dr - slopes[i + 1]) / hr) / ((rl - 1) / hl + (rr - 1) / hr);
                double sign = Math.Sign(dl);
                slopes[i] = sign * Math.Min(Math.Max(0, sign * proposed), 3 * Math.Min(Math.Abs(dl), Math.Abs(dr)));
            }
        }

        internal double Evaluate(double t)
        {
            if (t <= times[0]) return values[0];
            if (t >= times[times.Length - 1]) return values[values.Length - 1];
            int i = Segment(times, t);
            if (t == times[i]) return values[i];
            if (adaptive != null) return adaptive.Evaluate(i, (t - times[i]) / (times[i + 1] - times[i]));
            double u = (t - times[i]) / (times[i + 1] - times[i]);
            double a = values[i], delta = values[i + 1] - a;
            if (interpolation == WhimTexGradientInterpolation.Balanced)
            {
                double linear = a + delta * u;
                if (smoothness == 0) return linear;
                double width = times[i + 1] - times[i], left = slopes[i] * width, right = slopes[i + 1] * width;
                double cubic;
                if (i == 0 || i == times.Length - 2)
                {
                    double z = 1 - u;
                    cubic = a + (left * u * z * z + (6 * delta - right) * u * u * z + delta * u * u * u) / (z * z * z + 6 * u * z + u * u * u);
                }
                else cubic = a + u * (left + u * (3 * delta - 2 * left - right + u * (-2 * delta + left + right)));
                return smoothness == 1 ? cubic : linear + (cubic - linear) * smoothness;
            }
            double m = Clamp(midpoints[i], .05, .95);
            double weight = u <= m ? .5 * u / m : .5 + .5 * (u - m) / (1 - m);
            if (interpolation == WhimTexGradientInterpolation.Blend)
            {
                if (smoothness > 0)
                {
                    double p = u * (1 - m), r = p / (p + (1 - u) * m), eased = r * r * (3 - 2 * r);
                    weight = smoothness == 1 ? eased : weight + (eased - weight) * smoothness;
                }
            }
            else
            {
                double p = Clamp(weight, 0, 1) * 16; int index = Math.Min((int)Math.Floor(p), 15);
                weight = Ease[index] + (Ease[index + 1] - Ease[index]) * (p - index);
            }
            return a + delta * weight;
        }

        internal static int Segment(double[] times, double t)
        {
            int lo = 0, hi = times.Length - 1;
            while (hi - lo > 1) { int middle = (lo + hi) >> 1; if (t < times[middle]) hi = middle; else lo = middle; }
            return lo;
        }
        internal static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
    }
}
