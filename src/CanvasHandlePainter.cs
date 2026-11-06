using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class CanvasHandlePainter
    {
        internal static void Line(Painter2D p, Vector2 a, Vector2 b)
        { p.MoveTo(a); p.LineTo(b); }
        internal static void Capsule(Painter2D p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 direction = (b - a).normalized, normal = new Vector2(-direction.y, direction.x) * radius;
            Vector2 cap = direction * (radius * 4 / 3);
            p.MoveTo(a + normal); p.LineTo(b + normal);
            p.BezierCurveTo(b + normal + cap, b - normal + cap, b - normal);
            p.LineTo(a - normal);
            p.BezierCurveTo(a - normal - cap, a + normal - cap, a + normal); p.ClosePath();
        }
        internal static void Polyline(Painter2D p, IReadOnlyList<Vector2> points, bool closed = false)
        {
            if (points.Count < 2) return;
            p.MoveTo(points[0]);
            int count = closed && (points[points.Count - 1] - points[0]).sqrMagnitude < 1e-8f ? points.Count - 1 : points.Count;
            for (int i = 1; i < count; i++)
                if ((points[i] - points[i - 1]).sqrMagnitude > 1e-8f) p.LineTo(points[i]);
            if (closed) p.ClosePath();
        }
        // Append a continuous curve in view coordinates. Keep endpoints exact when joining different curves.
        internal static void SampleCurve(List<Vector2> points, Func<float, Vector2> evaluate,
            float start, float end, float tolerance = .25f)
        {
            Vector2 a = evaluate(start);
            if (points.Count == 0 || (points[points.Count - 1] - a).sqrMagnitude > 1e-8f) points.Add(a);
            for (int i = 0; i < 8; i++)
            {
                float t0 = Mathf.Lerp(start, end, i / 8f), t1 = Mathf.Lerp(start, end, (i + 1) / 8f);
                Vector2 b = evaluate(t1);
                Split(t0, t1, a, b, 0); a = b;
            }
            void Split(float t0, float t1, Vector2 a0, Vector2 b0, int depth)
            {
                float mid = (t0 + t1) * .5f;
                Vector2 m = evaluate(mid), chord = b0 - a0;
                float along = chord.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(m - a0, chord) / chord.sqrMagnitude) : 0;
                if (depth < 9 && (m - a0 - chord * along).sqrMagnitude > tolerance * tolerance)
                { Split(t0, mid, a0, m, depth + 1); Split(mid, t1, m, b0, depth + 1); }
                else points.Add(b0);
            }
        }
        internal static void Circle(Painter2D p, Vector2 center, float radius)
        { p.MoveTo(center + Vector2.right * radius); p.Arc(center, radius, 0, 360); p.ClosePath(); }
        internal static void Square(Painter2D p, Vector2 center, float radius)
        {
            p.MoveTo(center + new Vector2(-radius, -radius)); p.LineTo(center + new Vector2(radius, -radius));
            p.LineTo(center + new Vector2(radius, radius)); p.LineTo(center + new Vector2(-radius, radius)); p.ClosePath();
        }
        internal static void Diamond(Painter2D p, Vector2 center, float radius)
        {
            p.MoveTo(center + Vector2.up * radius); p.LineTo(center + Vector2.right * radius);
            p.LineTo(center + Vector2.down * radius); p.LineTo(center + Vector2.left * radius); p.ClosePath();
        }
        internal static void StrokeOutlined(Painter2D p, Color color, float width, Color outline, float border = 1)
        {
            p.strokeColor = outline; p.lineWidth = width + border * 2; p.Stroke();
            p.strokeColor = color; p.lineWidth = width; p.Stroke();
        }
        internal static void StrokeOutlined(Painter2D p, Action path, Color color, float width, Color outline, float border = 1)
        {
            p.strokeColor = outline; p.lineWidth = width + border * 2; p.BeginPath(); path(); p.Stroke();
            p.strokeColor = color; p.lineWidth = width; p.BeginPath(); path(); p.Stroke();
        }
        internal static void Disc(Painter2D p, Vector2 center, float radius, Color color)
        { p.fillColor = color; p.BeginPath(); Circle(p, center, radius); p.Fill(); }
        internal static void ColorHandle(Painter2D p, Vector2 center, Color color, bool selected)
        {
            Disc(p, center, selected ? 10.6f : 9.1f, Color.black);
            if (selected) Disc(p, center, 9.6f, new Color(.4f, .85f, 1));
            Disc(p, center, 8.1f, Color.white);
            Disc(p, center, 7.1f, color);
        }
    }
}
