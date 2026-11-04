using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class CanvasHandlePainter
    {
        internal static void Line(Painter2D p, Vector2 a, Vector2 b)
        { p.MoveTo(a); p.LineTo(b); }
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
