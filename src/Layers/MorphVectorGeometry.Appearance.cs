using System;
using System.Collections.Generic;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal static partial class MorphVectorGeometry
    {
        // Fill and stroke use the same coordinate and palette contracts.
        private struct Paint
        {
            internal VectorFillMode mode;
            internal Color color;
            internal Vector2 start, end;
            internal float repetitions;
            internal WhimTexGradient ramp;
            internal FreeformGradient freeform;

            internal Paint(VectorShape shape, bool ink)
            {
                mode = ink ? shape.strokeMode : shape.fillMode;
                color = ink ? shape.strokeColor : shape.fillColor;
                start = ink ? shape.strokeGradientStart : shape.gradientStart;
                end = ink ? shape.strokeGradientEnd : shape.gradientEnd;
                repetitions = ink ? shape.strokeGradientRepetitions : shape.gradientRepetitions;
                ramp = ink ? shape.strokeGradient : shape.gradient;
                freeform = ink ? shape.strokeFreeform : shape.freeform;
            }

            internal void Store(VectorShape shape, bool ink)
            {
                if (ink)
                {
                    shape.strokeMode = mode; shape.strokeColor = color; shape.strokeGradient = ramp;
                    shape.strokeFreeform = freeform; shape.strokeGradientStart = start;
                    shape.strokeGradientEnd = end; shape.strokeGradientRepetitions = repetitions;
                }
                else
                {
                    shape.fillMode = mode; shape.fillColor = color; shape.gradient = ramp;
                    shape.freeform = freeform; shape.gradientStart = start;
                    shape.gradientEnd = end; shape.gradientRepetitions = repetitions;
                }
            }

            internal bool Rebase(ProjectiveMatrix m, Vector2 reference, Vector2 size, bool active)
            {
                Vector2 mappedStart = m.Point(start), mappedEnd = m.Point(end);
                if (active && mode == VectorFillMode.LinearGradient && !start.Equals(end))
                {
                    // Transform the gradient covector. Mapping just the two points
                    // would rotate the iso-color lines incorrectly under shear/unequal scale.
                    Vector2 axis = Vector2.Scale(end - start, reference);
                    m.TryInverse(out var inverse);
                    double x = axis.x * (double)reference.x, y = axis.y * (double)reference.y;
                    double dx = (inverse.m00 * x + inverse.m10 * y) / size.x;
                    double dy = (inverse.m01 * x + inverse.m11 * y) / size.y;
                    double k = ((double)axis.x * axis.x + (double)axis.y * axis.y) / (dx * dx + dy * dy);
                    mappedEnd = mappedStart + new Vector2((float)(dx*k/size.x), (float)(dy*k/size.y));
                }
                else if (active && FigureFill.IsPaletteGradient(mode))
                {
                    PixelScale(m, reference, size, out bool uniform);
                    if (!uniform || (mode == VectorFillMode.AngularGradient && m.m00*m.m11 < m.m01*m.m10)) return false;
                }
                start = mappedStart; end = mappedEnd;
                if (freeform?.points != null)
                {
                    PixelScale(m, Vector2.one, Vector2.one, out bool uniformUV);
                    if (active && mode == VectorFillMode.Freeform && !uniformUV) return false;
                    for (int i = 0; i < freeform.points.Count; i++)
                    {
                        var point = freeform.points[i];
                        point.position = m.Point(point.position);
                        freeform.points[i] = point;
                    }
                }
                return true;
            }

            internal static bool Blend(Paint a, Paint b, float t, out Paint output)
            {
                output = t < .5f ? a : b;
                bool gradA = FigureFill.IsGradient(a.mode), gradB = FigureFill.IsGradient(b.mode);
                if (gradA && gradB && a.mode != b.mode) return false;
                output.mode = gradA ? a.mode : gradB ? b.mode :
                    a.mode == VectorFillMode.None && b.mode == VectorFillMode.None ? VectorFillMode.None : VectorFillMode.Solid;
                // For a missing paint, fade the other paint's own RGB to transparent.
                Color ca = a.mode == VectorFillMode.None ? b.color : a.color;
                Color cb = b.mode == VectorFillMode.None ? a.color : b.color;
                if (a.mode == VectorFillMode.None) ca.a = 0;
                if (b.mode == VectorFillMode.None) cb.a = 0;
                output.color = Color.Lerp(ca, cb, t);
                output.start = Vector2.Lerp(gradB && !gradA ? b.start : a.start, gradA && !gradB ? a.start : b.start, t);
                output.end = Vector2.Lerp(gradB && !gradA ? b.end : a.end, gradA && !gradB ? a.end : b.end, t);
                output.repetitions = Mathf.Lerp(gradB && !gradA ? b.repetitions : a.repetitions,
                    gradA && !gradB ? a.repetitions : b.repetitions, t);

                if (FigureFill.IsPaletteGradient(output.mode))
                {
                    if (gradA && gradB && a.ramp.Equals(b.ramp)) output.ramp = a.ramp;
                    else
                    {
                        var left = gradA ? a.ramp.Clone() : null;
                        var right = gradB ? b.ramp.Clone() : null;
                        // Sample the unwrapped ramps. Preserve independent alpha and signed HDR.
                        // Each interpolated ramp has 64 stops (the package's per-track limit).
                        var wrap = (gradA && (!gradB || t < .5f) ? left : right).WrapMode;
                        if (left != null) left.WrapMode = WhimTexGradientWrapMode.Clamp;
                        if (right != null) right.WrapMode = WhimTexGradientWrapMode.Clamp;
                        var colors = new GradientColorKey[64];
                        var alphas = new GradientAlphaKey[colors.Length];
                        for (int i = 0; i < colors.Length; i++)
                        {
                            float time = i / (float)(colors.Length - 1);
                            Color x = left?.EvaluateEncoded(time) ?? ca, y = right?.EvaluateEncoded(time) ?? cb;
                            if (a.mode == VectorFillMode.None) x = new Color(y.r, y.g, y.b, 0);
                            if (b.mode == VectorFillMode.None) y = new Color(x.r, x.g, x.b, 0);
                            Color value = Color.Lerp(x, y, t);
                            colors[i] = new GradientColorKey(value, time);
                            alphas[i] = new GradientAlphaKey(value.a, time);
                        }
                        output.ramp = new WhimTexGradient { Interpolation = WhimTexGradientInterpolation.Blend,
                            ColorMode = WhimTexGradientColorMode.Gamma, Smoothness = 0, WrapMode = wrap };
                        output.ramp.SetKeys(colors, alphas);
                    }
                }
                else if (output.mode == VectorFillMode.Freeform)
                {
                    var left = gradA ? a.freeform : b.freeform;
                    var right = gradB ? b.freeform : a.freeform;
                    if (left.points.Count != right.points.Count) return false;
                    output.freeform = (t < .5f ? left : right).Clone();
                    for (int i = 0; i < left.points.Count; i++)
                    {
                        var x = left.points[i]; var y = right.points[i];
                        if (!gradA) x.color = a.mode == VectorFillMode.None ? new Color(y.color.r, y.color.g, y.color.b, 0) : ca;
                        if (!gradB) y.color = b.mode == VectorFillMode.None ? new Color(x.color.r, x.color.g, x.color.b, 0) : cb;
                        output.freeform.points[i] = new FreeformGradientPoint(Vector2.Lerp(x.position, y.position, t),
                            Color.Lerp(x.color, y.color, t), Mathf.Lerp(x.influence, y.influence, t));
                    }
                }
                return true;
            }
        }

        private static bool RebasePaint(VectorShape shape, ProjectiveMatrix matrix, Vector2 reference, Vector2 size)
        {
            var fill = new Paint(shape, false); var ink = new Paint(shape, true);
            if (!fill.Rebase(matrix, reference, size, true) || !ink.Rebase(matrix, reference, size, shape.stroke)) return false;
            fill.Store(shape, false); ink.Store(shape, true);
            return true;
        }

        private static VectorShape BlendStyle(VectorShape a, VectorShape b, float t)
        {
            var nearest = t < .5f ? a : b;
            var shape = new VectorShape { name = nearest.name, fillRule = nearest.fillRule, visible = a.visible || b.visible,
                strokePosition = nearest.strokePosition, join = nearest.join, cap = nearest.cap,
                miterLimit = Mathf.Lerp(a.miterLimit, b.miterLimit, t), stroke = a.stroke || b.stroke,
                strokeWidth = Mathf.Lerp(a.stroke ? a.strokeWidth : 0, b.stroke ? b.strokeWidth : 0, t),
                dashOffset = Mathf.Lerp(a.dashOffset, b.dashOffset, t) };
            var fa = new Paint(a, false); var fb = new Paint(b, false);
            var sa = new Paint(a, true); var sb = new Paint(b, true);
            if (!a.visible) fa.mode = VectorFillMode.None;
            if (!b.visible) fb.mode = VectorFillMode.None;
            if (!a.visible || !a.stroke) sa.mode = VectorFillMode.None;
            if (!b.visible || !b.stroke) sb.mode = VectorFillMode.None;
            if (!Paint.Blend(fa, fb, t, out var fill) || !Paint.Blend(sa, sb, t, out var ink)) return null;
            fill.Store(shape, false); ink.Store(shape, true);
            int count = a.dashPattern?.Count ?? 0;
            if (count == (b.dashPattern?.Count ?? 0))
            {
                shape.dashPatternRelative = a.dashPatternRelative && b.dashPatternRelative;
                for (int i = 0; i < count; i++)
                {
                    float x = a.dashPattern[i], y = b.dashPattern[i];
                    if (!shape.dashPatternRelative) { x *= a.DashLengthScale; y *= b.DashLengthScale; }
                    shape.dashPattern.Add(Mathf.Lerp(x, y, t));
                }
            }
            else
            {
                shape.dashPatternRelative = nearest.dashPatternRelative;
                shape.dashPattern = nearest.dashPattern;
            }
            // Prepared samples must not expose their cached paint objects or dash arrays.
            return shape.Clone();
        }
    }
}
