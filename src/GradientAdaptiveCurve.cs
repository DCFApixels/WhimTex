using System;
using System.Collections.Generic;
using System.Linq;

namespace DCFApixels.WhimTex
{
    internal sealed class GradientAdaptiveCurve
    {
        // The scalar fit is shared by RGB working coordinates, unwrapped hue and alpha.
        private const double Gain = 12, Turn = 1.5, Compression = .95;
        private const double ParameterTolerance = 4 * 2.220446049250313e-16;
        private static readonly double[] GaussX = { -.9602898564975363, -.7966664774136267, -.525532409916329, -.1834346424956498, .1834346424956498, .525532409916329, .7966664774136267, .9602898564975363 };
        private static readonly double[] GaussW = { .1012285362903763, .2223810344533745, .3137066458778873, .362683783378362, .362683783378362, .3137066458778873, .2223810344533745, .1012285362903763 };
        private sealed class Segment
        {
            internal double width, x1, x2, a, b, c, d;
            internal double[] elevated, x, y, dx;
            internal bool rotated, neutral;
        }
        private sealed class Fit
        {
            internal Segment[] segments;
            internal double[] slopes;
        }
        private readonly struct Term
        {
            internal readonly int index;
            internal readonly double value;
            internal Term(int i, double v) { index = i; value = v; }
        }
        private sealed class Row
        {
            internal Term[] terms;
            internal double bound;
            internal double[] inverse;
            internal Row(Term[] terms, double bound) { this.terms = terms; this.bound = bound; }
        }
        private readonly Segment[] segments;

        internal GradientAdaptiveCurve(double[] times, double[] values, double[] midpoints)
        {
            var fit = BaseFit(times, values);
            Rotate(fit, times, values, midpoints);
            segments = fit.segments;
            for (int i = 0; i < segments.Length; i++) PrepareSegment(segments[i], midpoints[i]);
        }

        internal double Evaluate(int index, double position)
        {
            var s = segments[index];
            if (s.neutral) return Cubic(s.a, s.b, s.c, s.d, Parameter(s, position, true));
            return s.a + (s.d - s.a) * Bezier(s.y, Parameter(s, position, false));
        }

        private static Fit BaseFit(double[] times, double[] values)
        {
            // Fit shared monotone slopes by bending energy with endpoint/ordering constraints.
            int n = times.Length;
            var widths = new double[n - 1]; var rates = new double[n - 1];
            for (int i = 0; i < n - 1; i++) { widths[i] = times[i + 1] - times[i]; rates[i] = Math.Abs((values[i + 1] - values[i]) / widths[i]); }
            var incoming = new double[n]; var outgoing = new double[n]; var factors = Enumerable.Repeat(1.0, n).ToArray();
            for (int i = 0; i < n; i++)
            {
                double left = widths[Math.Max(0, i - 1)], right = widths[Math.Min(n - 2, i)];
                double span = i == 0 ? right : i == n - 1 ? left : 2 * left * right / (left + right);
                double rl = rates[Math.Max(0, i - 1)], rr = rates[Math.Min(n - 2, i)];
                double contrast = rl + rr != 0 ? (rr - rl) / (rr + rl) : 0;
                double bias = i == 0 || i == n - 1 ? 0 : .5 * Math.Log(2) * contrast;
                incoming[i] = .5 * 1.8 * span * Math.Exp(-bias); outgoing[i] = .5 * 1.8 * span * Math.Exp(bias);
            }
            for (int i = 0; i < n - 1; i++)
            {
                double f = Math.Min(1, .96 * widths[i] / (outgoing[i] + incoming[i + 1]));
                factors[i] = Math.Min(factors[i], f); factors[i + 1] = Math.Min(factors[i + 1], f);
            }
            for (int i = 0; i < n; i++) { incoming[i] *= factors[i]; outgoing[i] *= factors[i]; }
            double origin = values[0], amplitude = values.Max(v => Math.Abs(v - origin)); if (amplitude == 0) amplitude = 1;
            var y = values.Select(v => (v - origin) / amplitude).ToArray();
            var delta = widths.Select((w, i) => y[i + 1] - y[i]).ToArray();
            var secants = widths.Select((w, i) => delta[i] / w).ToArray();
            var ids = Enumerable.Repeat(-1, n).ToArray(); var signs = new double[n]; var scales = new double[n];
            var initial = new List<double>(); var preferred = new List<double>();
            for (int i = 1; i < n - 1; i++) if (secants[i - 1] * secants[i] > 0)
            {
                ids[i] = initial.Count; signs[i] = Math.Sign(secants[i]); scales[i] = Math.Min(widths[i - 1], widths[i]);
                initial.Add(.05 * Math.Min(Math.Abs(secants[i - 1]), Math.Abs(secants[i])) * scales[i]);
                double angle = Math.Min(Math.PI / 2 - 1e-5, .25 * .5 * (Math.Atan(Math.Abs(secants[i - 1])) + Math.Atan(Math.Abs(secants[i]))));
                preferred.Add(Math.Tan(angle) * scales[i]);
            }
            int count = initial.Count;
            var hessian = new double[count, count]; var gradient = new double[count]; var constraints = new List<Row>();
            double energyScale = Math.Max(1e-30, widths.Select((w, i) => delta[i] * delta[i] / (w * w * w)).Sum());
            Term[] Terms(int i, double a, double b) => new[] {
                new Term(ids[i], ids[i] < 0 ? 0 : a * signs[i] / scales[i]),
                new Term(ids[i + 1], ids[i + 1] < 0 ? 0 : b * signs[i + 1] / scales[i + 1]) };
            void AddRow(Term[] terms, double constant, double weight)
            {
                weight /= energyScale;
                var row = terms.Where(v => v.index >= 0 && v.value != 0).ToArray();
                for (int j = 0; j < row.Length; j++)
                {
                    int at = row[j].index; double v = row[j].value;
                    gradient[at] += weight * v * constant;
                    for (int k = 0; k <= j; k++)
                    {
                        int other = row[k].index; double value = weight * v * row[k].value;
                        hessian[at, other] += value; if (at != other) hessian[other, at] += value;
                    }
                }
            }
            Row previousEnd = null;
            for (int i = 0; i < n - 1; i++)
            {
                double w = widths[i], a = outgoing[i] / w, b = incoming[i + 1] / w;
                var row = Terms(i, outgoing[i], incoming[i + 1]).Where(t => t.index >= 0).Select(t => new Term(t.index, Math.Abs(t.value))).ToArray();
                if (row.Length > 0) constraints.Add(new Row(row, Math.Abs(delta[i])));
                var cuts = new List<double> { 0, .25, .5, .75, 1 };
                for (double t = a; t < .25; t *= 2) { if (t == 0) throw new InvalidOperationException("Degenerate gradient handle."); cuts.Add(t); }
                for (double t = b; t < .25; t *= 2) { if (t == 0) throw new InvalidOperationException("Degenerate gradient handle."); cuts.Add(1 - t); }
                cuts.Sort();
                for (int k = 0; k < cuts.Count - 1; k++) for (int j = 0; j < 8; j++)
                {
                    double half = .5 * (cuts[k + 1] - cuts[k]); if (!(half > 0)) continue;
                    double u = .5 * (cuts[k + 1] + cuts[k]) + half * GaussX[j], v = 1 - u;
                    double dx = 3 * (v * v * a + 2 * v * u * (1 - a - b) + u * u * b), ddx = 6 * (v * (1 - 2 * a - b) + u * (a + 2 * b - 1));
                    double dy0 = 6 * v * u * delta[i], ddy0 = 6 * (v - u) * delta[i];
                    double dm0 = 3 * (v * v - 2 * v * u) * a * w, ddm0 = 6 * (-2 * v + u) * a * w;
                    double dm1 = 3 * (u * u - 2 * v * u) * b * w, ddm1 = 6 * (-v + 2 * u) * b * w;
                    AddRow(Terms(i, ddm0 * dx - dm0 * ddx, ddm1 * dx - dm1 * ddx), ddy0 * dx - dy0 * ddx, half * GaussW[j] / (Math.Pow(dx, 5) * Math.Pow(w, 3)));
                }
                var start = new Row(Terms(i, (2.0 / 3) * (-1 + b) / (a * a * w), (2.0 / 3) * -b / (a * a * w)), (2.0 / 3) * delta[i] / (a * a * w * w));
                var end = new Row(Terms(i, (2.0 / 3) * a / (b * b * w), (2.0 / 3) * (1 - a) / (b * b * w)), -(2.0 / 3) * delta[i] / (b * b * w * w));
                if (previousEnd != null) AddRow(Merge(previousEnd.terms, start.terms, 1), previousEnd.bound - start.bound, 3 * 2 * widths[i - 1] * w / (widths[i - 1] + w));
                previousEnd = end;
            }
            for (int i = 0; i < n; i++) if (ids[i] >= 0)
            {
                int j = ids[i]; double local = Math.Max(Math.Abs(secants[i - 1]), Math.Abs(secants[i]));
                AddRow(new[] { new Term(j, 1 / scales[i]) }, -preferred[j] / scales[i], .03 * (widths[i - 1] + widths[i]) * local * local);
                constraints.Add(new Row(new[] { new Term(j, -1) }, -.02 * Math.Min(Math.Abs(secants[i - 1]), Math.Abs(secants[i])) * scales[i]));
            }
            var solved = count > 0 ? Solve(hessian, gradient, constraints, initial.ToArray()) : Array.Empty<double>();
            var slopes = new double[n];
            for (int i = 0; i < n; i++) slopes[i] = ids[i] < 0 ? 0 : signs[i] * solved[ids[i]] / scales[i] * amplitude;
            var result = new Segment[n - 1];
            for (int i = 0; i < result.Length; i++) result[i] = new Segment {
                width = widths[i], x1 = outgoing[i] / widths[i], x2 = 1 - incoming[i + 1] / widths[i],
                a = values[i], b = values[i] + slopes[i] * outgoing[i], c = values[i + 1] - slopes[i + 1] * incoming[i + 1], d = values[i + 1] };
            return new Fit { segments = result, slopes = slopes };
        }

        private static void Rotate(Fit fit, double[] times, double[] values, double[] midpoints)
        {
            // Midpoints adjust common key derivatives before the degree-seven interior controls.
            int n = values.Length;
            var signals = midpoints.Take(n - 1).Select(m => 3 * Math.Tanh(Gain * (1 - 2 * m) / 3)).ToArray();
            if (signals.All(v => v == 0)) return;
            var h = fit.segments.Select(s => s.width).ToArray();
            double origin = values[0], amplitude = values.Max(v => Math.Abs(v - origin)); if (amplitude == 0) amplitude = 1;
            var y = values.Select(v => (v - origin) / amplitude).ToArray(); var d = h.Select((w, i) => y[i + 1] - y[i]).ToArray();
            var scales = times.Select((v, i) => i == 0 ? h[0] : i == n - 1 ? h[n - 2] : Math.Min(h[i - 1], h[i])).ToArray();
            var slopeIds = Enumerable.Repeat(-1, n).ToArray(); var curveIds = Enumerable.Repeat(-1, n).ToArray(); var signs = new double[n];
            var initial = new List<double>(); var target = new List<double>(); var weights = new List<double>(); var constraints = new List<Row>();
            for (int i = 0; i < n; i++)
            {
                double before = i > 0 ? signals[i - 1] : 0, after = i < n - 1 ? signals[i] : 0;
                double m = fit.slopes[i] / amplitude, scale = scales[i];
                if (i > 0 && i < n - 1 && d[i - 1] * d[i] > 0)
                {
                    int j = initial.Count; double secant = Math.Min(Math.Abs(d[i - 1] / h[i - 1]), Math.Abs(d[i] / h[i]));
                    slopeIds[i] = j; signs[i] = Math.Sign(m); initial.Add(Math.Abs(m) * scale);
                    target.Add(Math.Abs(m) * scale * Math.Exp(.5 * Turn * (after - before)));
                    weights.Add(1 / Math.Pow(Math.Max(.02, Math.Abs(m) * scale), 2));
                    constraints.Add(new Row(new[] { new Term(j, -1) }, -Math.Min(.02 * secant * scale, initial[j])));
                }
                if ((i == 0 || d[i - 1] != 0) && (i == n - 1 || d[i] != 0))
                {
                    double curvature = 0; int countSides = 0;
                    if (i > 0) { curvature += before * d[i - 1] / (h[i - 1] * h[i - 1]); countSides++; }
                    if (i < n - 1) { curvature += after * d[i] / (h[i] * h[i]); countSides++; }
                    curveIds[i] = initial.Count; initial.Add(0); target.Add(2 * Turn * curvature / countSides * scale * scale); weights.Add(1);
                }
            }
            int count = initial.Count; if (count == 0) return;
            var matrix = new double[count, count]; var gradient = new double[count];
            for (int i = 0; i < count; i++) { matrix[i, i] = weights[i]; gradient[i] = -weights[i] * target[i]; }
            Row Jet(int i, double baseline, double offset, double curve)
            {
                var a = new List<Term>(); double b = baseline, m = fit.slopes[i] / amplitude;
                if (slopeIds[i] >= 0) { a.Add(new Term(slopeIds[i], offset * signs[i] / scales[i])); b -= offset * m; }
                if (curveIds[i] >= 0 && curve != 0) a.Add(new Term(curveIds[i], curve / (scales[i] * scales[i])));
                return new Row(a.ToArray(), b);
            }
            var templates = new Row[n - 1][];
            for (int i = 0; i < n - 1; i++)
            {
                var s = fit.segments[i]; double width = s.width;
                var x = Elevate(new[] { 0.0, s.x1, s.x2, 1.0 });
                var neutral = Elevate(new[] { (s.a - origin) / amplitude, (s.b - origin) / amplitude, (s.c - origin) / amplitude, (s.d - origin) / amplitude });
                var rows = new[] { new Row(Array.Empty<Term>(), y[i]),
                    Jet(i, neutral[1], width*x[1], 0), Jet(i, neutral[2], width*x[2], 7.0/6*Math.Pow(width*x[1], 2)),
                    Jet(i+1, neutral[5], width*(x[5]-1), 7.0/6*Math.Pow(width*(1-x[6]), 2)),
                    Jet(i+1, neutral[6], width*(x[6]-1), 0), new Row(Array.Empty<Term>(), y[i+1]) };
                int sign = Math.Sign(d[i]);
                if (sign != 0) for (int j = 0; j < 5; j++)
                {
                    var terms = Merge(rows[j].terms, rows[j + 1].terms, sign);
                    double bound = sign * (rows[j + 1].bound - rows[j].bound) - (j == 2 ? .5 * Math.Abs(neutral[5] - neutral[2]) : 0);
                    if (terms.Length > 0) constraints.Add(new Row(terms, bound));
                    else if (bound < -1e-12) throw new InvalidOperationException("Infeasible gradient curve basis.");
                }
                templates[i] = rows;
            }
            var solved = Solve(matrix, gradient, constraints, initial.ToArray());
            for (int i = 0; i < n - 1; i++)
            {
                var s = fit.segments[i]; double delta = s.d - s.a; if (delta == 0) continue;
                var rows = templates[i]; var outer = rows.Select(r => r.bound + Dot(r, solved)).ToArray();
                var local = Elevate(new[] { 0.0, (s.b - s.a) / delta, (s.c - s.a) / delta, 1.0 });
                var elevated = (double[])local.Clone();
                elevated[1] = (outer[1] - y[i]) / d[i]; elevated[2] = (outer[2] - y[i]) / d[i];
                elevated[5] = (outer[3] - y[i]) / d[i]; elevated[6] = (outer[4] - y[i]) / d[i];
                double span = elevated[5] - elevated[2], oldSpan = local[5] - local[2];
                for (int j = 3; j <= 4; j++) elevated[j] = elevated[2] + span * (local[j] - local[2]) / oldSpan;
                s.rotated = true; s.elevated = elevated;
            }
        }

        private static void PrepareSegment(Segment s, double midpoint)
        {
            double delta = s.d - s.a, bias = Gain * (1 - 2 * midpoint);
            var x = Elevate(new[] { 0.0, s.x1, s.x2, 1.0 });
            var y = s.elevated ?? (delta != 0 ? Elevate(new[] { 0.0, (s.b - s.a) / delta, (s.c - s.a) / delta, 1.0 }) : new double[8]);
            s.neutral = (bias == 0 || delta == 0) && !s.rotated;
            if (bias != 0 && delta != 0)
            {
                for (int i = 3; i <= 4; i++) x[i] = Shift(x[i], x[2], x[5], -bias);
                double left = y[2], span = y[5] - left;
                for (int i = 3; i <= 4; i++) y[i] = left + span * ((Shift(y[i], left, y[5], bias) - left) / span);
            }
            double factor = 1 - Compression * bias * bias / (36 + bias * bias);
            if (factor != 1) { x = Compress(x, factor); y = Compress(y, factor); s.neutral &= delta == 0; }
            s.x = x; s.y = y; s.dx = Derivative(x);
        }
        private static double Shift(double value, double left, double right, double bias)
        {
            if (bias == 0 || left == right) return value;
            double range = right - left, r = ((value - left) / range - .02) / .96;
            if (!(r > 0 && r < 1)) throw new InvalidOperationException("Degenerate interior gradient control.");
            double z = Math.Log(r / (1 - r)) + bias;
            return left + range * (.02 + .96 / (1 + Math.Exp(-z)));
        }
        private static double[] Compress(double[] source, double f)
        {
            // Apply the same endpoint-jet scaling to X and Y, preserving graph derivatives.
            var q = (double[])source.Clone(); double v = 1 - f;
            q[1] = v * source[0] + f * source[1]; q[2] = v * v * source[0] + 2 * f * v * source[1] + f * f * source[2];
            q[6] = v * source[7] + f * source[6]; q[5] = v * v * source[7] + 2 * f * v * source[6] + f * f * source[5];
            double span = source[5] - source[2], next = q[5] - q[2];
            for (int i = 3; i <= 4; i++) q[i] = span != 0 ? q[2] + next * (source[i] - source[2]) / span : q[2];
            return q;
        }
        private static double Parameter(Segment s, double x, bool cubic)
        {
            if (x <= 0) return 0; if (x >= 1) return 1;
            double lo = 0, hi = 1, u = x;
            for (int step = 0; step < 56; step++)
            {
                double error = (cubic ? Cubic(0, s.x1, s.x2, 1, u) : Bezier(s.x, u)) - x;
                if (Math.Abs(error) <= ParameterTolerance) return u;
                if (error < 0) lo = u; else hi = u;
                double v = 1 - u;
                double derivative = cubic ? 3 * (v * v * s.x1 + 2 * v * u * (s.x2 - s.x1) + u * u * (1 - s.x2)) : Bezier(s.dx, u);
                double candidate = u - error / derivative;
                u = candidate > lo && candidate < hi ? candidate : (lo + hi) * .5;
            }
            return (lo + hi) * .5;
        }
        private static double Cubic(double a, double b, double c, double d, double u)
        { double v = 1 - u; return v * v * v * a + 3 * u * v * v * b + 3 * u * u * v * c + u * u * u * d; }
        private static double Bezier(double[] values, double u)
        {
            int n = values.Length - 1; if (u <= 0) return values[0]; if (u >= 1) return values[n];
            bool reverse = u > .5; double t = reverse ? 1 - u : u, v = 1 - t, ratio = t / v;
            double weight = Math.Pow(v, n), result = weight * values[reverse ? n : 0];
            for (int i = 1; i <= n; i++) { weight *= ratio * (n - i + 1) / i; result += weight * values[reverse ? n - i : i]; }
            return result;
        }
        private static double[] Derivative(double[] values)
        { int n = values.Length - 1; var d = new double[n]; for (int i = 0; i < n; i++) d[i] = n * (values[i + 1] - values[i]); return d; }
        private static double[] Elevate(double[] values)
        {
            var p = (double[])values.Clone();
            for (int degree = p.Length; degree <= 7; degree++)
            {
                var q = new double[degree + 1]; q[0] = p[0]; q[degree] = p[p.Length - 1];
                for (int i = 1; i < degree; i++) q[i] = i / (double)degree * p[i - 1] + (1 - i / (double)degree) * p[i];
                p = q;
            }
            return p;
        }
        private static Term[] Merge(Term[] a, Term[] b, double sign)
        {
            var terms = new SortedDictionary<int, double>();
            foreach (var t in a) if (t.index >= 0) terms[t.index] = (terms.TryGetValue(t.index, out double v) ? v : 0) + sign * t.value;
            foreach (var t in b) if (t.index >= 0) terms[t.index] = (terms.TryGetValue(t.index, out double v) ? v : 0) - sign * t.value;
            return terms.Where(t => t.Value != 0).Select(t => new Term(t.Key, t.Value)).ToArray();
        }
        private static double Dot(Row row, double[] vector)
        { double sum = 0; foreach (var t in row.terms) sum += t.value * vector[t.index]; return sum; }
        private static double Maximum(double[] v) { double max = 0; for (int i = 0; i < v.Length; i++) max = Math.Max(max, Math.Abs(v[i])); return max; }
        private static double[,] Cholesky(double[,] matrix)
        {
            int n = matrix.GetLength(0); var l = (double[,])matrix.Clone();
            for (int i = 0; i < n; i++) for (int j = 0; j <= i; j++)
            {
                double v = l[i, j]; for (int k = 0; k < j; k++) v -= l[i, k] * l[j, k];
                if (i == j) { if (!(v > 1e-15)) throw new InvalidOperationException("Dependent gradient fitting system."); l[i, j] = Math.Sqrt(v); }
                else l[i, j] = v / l[j, j];
            }
            return l;
        }
        private static double[] LinearSolve(double[,] l, double[] b)
        {
            int n = b.Length; var x = (double[])b.Clone();
            for (int i = 0; i < n; i++) { for (int j = 0; j < i; j++) x[i] -= l[i, j] * x[j]; x[i] /= l[i, i]; }
            for (int i = n - 1; i >= 0; i--) { for (int j = i + 1; j < n; j++) x[i] -= l[j, i] * x[j]; x[i] /= l[i, i]; }
            return x;
        }
        private static double[] Solve(double[,] h, double[] g, List<Row> constraints, double[] initial)
        {
            // Scale the convex QP, then solve its active constraints without relaxing them.
            int n = g.Length; var scale = new double[n]; var q = new double[n]; var matrix = new double[n, n];
            for (int i = 0; i < n; i++) scale[i] = 1 / Math.Sqrt(h[i, i]);
            for (int i = 0; i < n; i++)
            {
                q[i] = g[i] * scale[i];
                for (int j = 0; j <= i; j++) matrix[i, j] = matrix[j, i] = h[i, j] * scale[i] * scale[j];
            }
            var l = Cholesky(matrix); var unconstrained = LinearSolve(l, q.Select(v => -v).ToArray());
            var rows = new List<Row>();
            foreach (var c in constraints)
            {
                var terms = c.terms.Select(t => new Term(t.index, t.value * scale[t.index])).ToArray();
                var vector = new double[n]; foreach (var term in terms) vector[term.index] = term.value;
                var inverse = LinearSolve(l, vector); var row = new Row(terms, c.bound);
                double norm = Math.Sqrt(Dot(row, inverse));
                row.terms = terms.Select(t => new Term(t.index, t.value / norm)).ToArray(); row.bound /= norm; row.inverse = inverse.Select(v => v / norm).ToArray();
                var same = rows.Find(r => r.terms.Length == row.terms.Length && r.terms.Select((t, j) => t.index == row.terms[j].index && Math.Abs(t.value - row.terms[j].value) < 1e-12 * Math.Max(1, Math.Abs(t.value))).All(v => v));
                if (same != null) same.bound = Math.Min(same.bound, row.bound); else rows.Add(row);
            }
            var x = initial.Select((v, i) => v / scale[i]).ToArray(); var active = new List<int>(); var dependent = new HashSet<int>();
            if (rows.Any(r => Dot(r, x) > r.bound + 1e-12)) throw new InvalidOperationException("Infeasible initial gradient slopes.");
            bool converged = false; double[] multipliers = Array.Empty<double>();
            for (int iteration = 0; iteration < 50 + 20 * n; iteration++)
            {
                var target = (double[])unconstrained.Clone(); double[,] factor = null; multipliers = Array.Empty<double>();
                if (active.Count > 0)
                {
                    var s = new double[active.Count, active.Count];
                    for (int i = 0; i < active.Count; i++) for (int j = 0; j < active.Count; j++) s[i, j] = Dot(rows[active[i]], rows[active[j]].inverse);
                    factor = Cholesky(s); multipliers = LinearSolve(factor, active.Select(i => Dot(rows[i], unconstrained) - rows[i].bound).ToArray());
                    for (int j = 0; j < active.Count; j++) for (int i = 0; i < n; i++) target[i] -= rows[active[j]].inverse[i] * multipliers[j];
                }
                var step = target.Select((v, i) => v - x[i]).ToArray();
                if (Maximum(step) <= 2e-11 * (1 + Maximum(x)))
                {
                    int remove = -1; double worst = -2e-10 * (1 + Maximum(multipliers));
                    for (int j = 0; j < multipliers.Length; j++) if (multipliers[j] < worst) { worst = multipliers[j]; remove = j; }
                    if (remove < 0) { x = target; converged = true; break; }
                    active.RemoveAt(remove); dependent.Clear(); continue;
                }
                double alpha = 1; int blocking = -1;
                for (int j = 0; j < rows.Count; j++) if (!active.Contains(j) && !dependent.Contains(j))
                {
                    double towards = Dot(rows[j], step); if (towards <= 1e-13) continue;
                    double limit = Math.Max(0, (rows[j].bound - Dot(rows[j], x)) / towards); if (limit >= alpha) continue;
                    if (factor != null)
                    {
                        var cross = active.Select(i => Dot(rows[i], rows[j].inverse)).ToArray(); var projection = LinearSolve(factor, cross);
                        double remainder = 1; for (int k = 0; k < cross.Length; k++) remainder -= cross[k] * projection[k];
                        if (remainder < 1e-12) { dependent.Add(j); continue; }
                    }
                    alpha = limit; blocking = j;
                }
                for (int i = 0; i < n; i++) x[i] += alpha * step[i];
                if (blocking >= 0) { active.Add(blocking); dependent.Clear(); }
            }
            if (!converged) throw new InvalidOperationException("Gradient fitting did not converge.");
            var residual = (double[])q.Clone();
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) residual[i] += matrix[i, j] * x[j];
            for (int j = 0; j < active.Count; j++) foreach (var t in rows[active[j]].terms) residual[t.index] += t.value * multipliers[j];
            if (Maximum(residual) / (1 + Maximum(q)) > 1e-7 || rows.Any(r => Dot(r, x) - r.bound > 1e-8)) throw new InvalidOperationException("Insufficient gradient fitting accuracy.");
            for (int i = 0; i < n; i++) x[i] *= scale[i];
            return x;
        }
    }
}
