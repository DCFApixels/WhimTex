using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class LayerActionIcon : VisualElement
    {
        internal enum Kind { Add, Group, Delete, Bug, Eye, EyeOff, Alpha, AddDrawing, Transform, Properties, Effects, Settings }

        private readonly Kind kind;

        internal LayerActionIcon(Kind kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-layer-action-icon");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1f || contentRect.height < 1f)
                return;

            Painter2D painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1.5f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            switch (kind)
            {
                case Kind.Settings:
                    DrawSettings(painter);
                    return;
                case Kind.Transform:
                    DrawTransform(painter);
                    return;
                case Kind.Properties:
                    DrawProperties(painter);
                    return;
                case Kind.Effects:
                    painter.strokeColor = new Color(0.7f, 0.6f, 0.86f);
                    painter.MoveTo(new Vector2(6f, 2f));
                    painter.LineTo(new Vector2(7.5f, 6.5f));
                    painter.LineTo(new Vector2(12f, 8f));
                    painter.LineTo(new Vector2(7.5f, 9.5f));
                    painter.LineTo(new Vector2(6f, 14f));
                    painter.LineTo(new Vector2(4.5f, 9.5f));
                    painter.LineTo(new Vector2(1f, 8f));
                    painter.LineTo(new Vector2(4.5f, 6.5f));
                    painter.ClosePath();
                    painter.MoveTo(new Vector2(12f, 1f));
                    painter.LineTo(new Vector2(12f, 5f));
                    painter.MoveTo(new Vector2(10f, 3f));
                    painter.LineTo(new Vector2(14f, 3f));
                    break;
                case Kind.Alpha:
                    DrawAlpha(painter);
                    return;
                case Kind.Eye:
                case Kind.EyeOff:
                    DrawEye(painter, kind == Kind.EyeOff);
                    return;
                case Kind.Bug:
                    DrawBug(painter);
                    return;
                case Kind.Add:
                    painter.MoveTo(new Vector2(8f, 3f));
                    painter.LineTo(new Vector2(8f, 13f));
                    painter.MoveTo(new Vector2(3f, 8f));
                    painter.LineTo(new Vector2(13f, 8f));
                    break;
                case Kind.AddDrawing:
                    painter.lineWidth = 1.25f;
                    painter.MoveTo(new Vector2(6f, 14f));
                    painter.LineTo(new Vector2(2f, 14f));
                    painter.LineTo(new Vector2(2f, 2f));
                    painter.LineTo(new Vector2(10f, 2f));
                    painter.LineTo(new Vector2(14f, 6f));
                    painter.LineTo(new Vector2(14f, 7f));
                    painter.MoveTo(new Vector2(10f, 2f));
                    painter.LineTo(new Vector2(10f, 6f));
                    painter.LineTo(new Vector2(14f, 6f));
                    painter.Stroke();
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(11f, 8f));
                    painter.LineTo(new Vector2(11f, 14f));
                    painter.MoveTo(new Vector2(8f, 11f));
                    painter.LineTo(new Vector2(14f, 11f));
                    break;
                case Kind.Group:
                    painter.MoveTo(new Vector2(2f, 13f));
                    painter.LineTo(new Vector2(2f, 3f));
                    painter.LineTo(new Vector2(6f, 3f));
                    painter.LineTo(new Vector2(8f, 5f));
                    painter.LineTo(new Vector2(14f, 5f));
                    painter.LineTo(new Vector2(14f, 13f));
                    painter.ClosePath();
                    break;
                case Kind.Delete:
                    painter.MoveTo(new Vector2(3f, 4f));
                    painter.LineTo(new Vector2(13f, 4f));
                    painter.MoveTo(new Vector2(6f, 4f));
                    painter.LineTo(new Vector2(6f, 2f));
                    painter.LineTo(new Vector2(10f, 2f));
                    painter.LineTo(new Vector2(10f, 4f));
                    painter.MoveTo(new Vector2(4f, 6f));
                    painter.LineTo(new Vector2(5f, 14f));
                    painter.LineTo(new Vector2(11f, 14f));
                    painter.LineTo(new Vector2(12f, 6f));
                    painter.MoveTo(new Vector2(7f, 7f));
                    painter.LineTo(new Vector2(7f, 11f));
                    painter.MoveTo(new Vector2(9f, 7f));
                    painter.LineTo(new Vector2(9f, 11f));
                    break;
            }
            painter.Stroke();
        }

        private static void DrawSettings(Painter2D painter)
        {
            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            for (int i = 0; i < 24; i++)
            {
                float angle = (i - 0.5f) * Mathf.PI / 12f;
                float radius = i % 4 == 1 || i % 4 == 2 ? 6.5f : 5f;
                Vector2 point = new Vector2(8f + Mathf.Cos(angle) * radius, 8f + Mathf.Sin(angle) * radius);
                if (i == 0) painter.MoveTo(point);
                else painter.LineTo(point);
            }
            painter.ClosePath();
            painter.MoveTo(new Vector2(10.6f, 8f));
            painter.Arc(new Vector2(8f, 8f), 2.6f, 0f, 360f);
            painter.ClosePath();
            painter.Fill(FillRule.OddEven);
        }

        private static void DrawTransform(Painter2D painter)
        {
            void Axis(Vector2 end, Vector2 wing, Color color)
            {
                painter.strokeColor = color;
                painter.BeginPath();
                painter.MoveTo(new Vector2(7f, 10f));
                painter.LineTo(end);
                painter.LineTo(wing);
                painter.Stroke();
            }
            Axis(new Vector2(7f, 2f), new Vector2(5f, 4f), new Color(0.56f, 0.78f, 0.46f));
            Axis(new Vector2(14f, 12f), new Vector2(12f, 9f), new Color(0.9f, 0.55f, 0.48f));
            Axis(new Vector2(2f, 14f), new Vector2(2f, 11f), new Color(0.48f, 0.7f, 0.9f));
        }

        private static void DrawProperties(Painter2D painter)
        {
            painter.strokeColor = new Color(0.48f, 0.72f, 0.8f);
            painter.lineWidth = 1.25f;
            for (int i = 0; i < 3; i++)
            {
                float y = 3f + i * 5f;
                float knob = i == 1 ? 10f : 6f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(2f, y));
                painter.LineTo(new Vector2(knob - 1.5f, y));
                painter.MoveTo(new Vector2(knob + 1.5f, y));
                painter.LineTo(new Vector2(14f, y));
                painter.MoveTo(new Vector2(knob - 1.5f, y - 1.5f));
                painter.LineTo(new Vector2(knob + 1.5f, y - 1.5f));
                painter.LineTo(new Vector2(knob + 1.5f, y + 1.5f));
                painter.LineTo(new Vector2(knob - 1.5f, y + 1.5f));
                painter.ClosePath();
                painter.Stroke();
            }
        }

        private static void DrawAlpha(Painter2D painter)
        {
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(2.5f, 2.5f));
            painter.LineTo(new Vector2(13.5f, 2.5f));
            painter.LineTo(new Vector2(13.5f, 13.5f));
            painter.LineTo(new Vector2(2.5f, 13.5f));
            painter.ClosePath();
            painter.Stroke();
            painter.fillColor = painter.strokeColor;
            for (int i = 0; i < 2; i++)
            {
                float start = 3.5f + i * 4.5f;
                float end = start + 4.5f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(start, start));
                painter.LineTo(new Vector2(end, start));
                painter.LineTo(new Vector2(end, end));
                painter.LineTo(new Vector2(start, end));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private static void DrawEye(Painter2D painter, bool hidden)
        {
            painter.lineWidth = 1.25f;
            painter.BeginPath();
            if (hidden)
            {
                painter.MoveTo(new Vector2(6.2f, 4.1f));
                painter.BezierCurveTo(new Vector2(9.8f, 3.1f), new Vector2(12.7f, 5.2f), new Vector2(14.5f, 8f));
                painter.BezierCurveTo(new Vector2(13.6f, 9.2f), new Vector2(12.6f, 10.2f), new Vector2(11.4f, 10.9f));
                painter.MoveTo(new Vector2(9.6f, 11.8f));
                painter.BezierCurveTo(new Vector2(6.1f, 12.7f), new Vector2(3.2f, 10.4f), new Vector2(1.5f, 8f));
                painter.BezierCurveTo(new Vector2(2.3f, 6.8f), new Vector2(3.2f, 5.8f), new Vector2(4.4f, 5.1f));
                painter.MoveTo(new Vector2(2.2f, 2.2f));
                painter.LineTo(new Vector2(13.8f, 13.8f));
                painter.Stroke();
            }
            else
            {
                painter.MoveTo(new Vector2(1.5f, 8f));
                painter.BezierCurveTo(new Vector2(5f, 2.7f), new Vector2(11f, 2.7f), new Vector2(14.5f, 8f));
                painter.BezierCurveTo(new Vector2(11f, 13.3f), new Vector2(5f, 13.3f), new Vector2(1.5f, 8f));
                painter.ClosePath();
                painter.Stroke();
            }
            if (hidden)
            {
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                painter.Arc(new Vector2(8f, 8f), 2f, 225f, 405f);
                painter.Stroke();
                painter.BeginPath();
                painter.Arc(new Vector2(8f, 8f), 2f, 100f, 170f);
                painter.Stroke();
                return;
            }
            painter.BeginPath();
            painter.Arc(new Vector2(8f, 8f), 2f, 0f, 360f);
            painter.ClosePath();
            painter.Stroke();
        }

        private void DrawBug(Painter2D painter)
        {
            painter.lineWidth = 1.25f;
            painter.fillColor = resolvedStyle.color;
            for (int side = 0; side < 2; side++)
            {
                Vector2 P(float x, float y) => new Vector2(side == 0 ? x : 16f - x, y);
                painter.BeginPath();
                painter.MoveTo(P(6.7f, 3.3f));
                painter.LineTo(P(5.4f, 1.4f));
                painter.MoveTo(P(5.2f, 7.5f));
                painter.LineTo(P(3.2f, 6f));
                painter.LineTo(P(2.9f, 4.5f));
                painter.MoveTo(P(4.8f, 9.5f));
                painter.LineTo(P(1.6f, 9.5f));
                painter.MoveTo(P(5.2f, 11.5f));
                painter.LineTo(P(3.2f, 12.8f));
                painter.LineTo(P(2.9f, 14.1f));
                painter.Stroke();

                painter.BeginPath();
                painter.MoveTo(P(7.4f, 6.3f));
                painter.BezierCurveTo(P(5.5f, 6.1f), P(4.4f, 7.7f), P(4.4f, 9.6f));
                painter.BezierCurveTo(P(4.4f, 12.1f), P(5.7f, 14f), P(7.4f, 14.3f));
                painter.ClosePath();
                painter.Fill();
            }

            painter.BeginPath();
            painter.MoveTo(new Vector2(5.8f, 5.2f));
            painter.BezierCurveTo(new Vector2(5.8f, 1.9f), new Vector2(10.2f, 1.9f), new Vector2(10.2f, 5.2f));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
