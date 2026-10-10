using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class LayerActionIcon : VisualElement
    {
        internal enum Kind { Add, Group, Delete, Bug, Eye, EyeOff, Alpha, AddDrawing, Transform, Properties, Effects, Settings, Warning,
            CenterPivot, CenterOnCanvas, FlipHorizontal, FlipVertical, RotateLeft, RotateRight, OriginalAspect, OriginalSize, Reset,
            TextAlignLeft, TextAlignCenter, TextAlignRight, TextJustify, TextAlignTop, TextAlignMiddle, TextAlignBottom }

        private readonly Kind kind;

        internal LayerActionIcon(Kind kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("whimtex-layer-action-icon");
            if (kind == Kind.OriginalSize)
            {
                Label ratio = new Label("1:1") { pickingMode = PickingMode.Ignore };
                ratio.AddToClassList("whimtex-transform-action-ratio");
                Add(ratio);
            }
            else generateVisualContent += Draw;
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
                case Kind.TextAlignLeft:
                case Kind.TextAlignCenter:
                case Kind.TextAlignRight:
                case Kind.TextJustify:
                    DrawTextAlignment(painter, (int)kind - (int)Kind.TextAlignLeft, false);
                    return;
                case Kind.TextAlignTop:
                case Kind.TextAlignMiddle:
                case Kind.TextAlignBottom:
                    DrawTextAlignment(painter, (int)kind - (int)Kind.TextAlignTop, true);
                    return;
                case Kind.CenterPivot:
                    DrawCenterPivot(painter);
                    return;
                case Kind.CenterOnCanvas:
                    DrawCenter(painter);
                    return;
                case Kind.FlipHorizontal:
                case Kind.FlipVertical:
                    DrawFlip(painter, kind == Kind.FlipVertical);
                    return;
                case Kind.RotateLeft:
                case Kind.RotateRight:
                    DrawQuarterTurn(painter, kind == Kind.RotateLeft);
                    return;
                case Kind.OriginalAspect:
                    DrawOriginalAspect(painter);
                    return;
                case Kind.Reset:
                    DrawReset(painter);
                    return;
                case Kind.Warning:
                    painter.fillColor = new Color(1f, 0.73f, 0.2f);
                    painter.MoveTo(new Vector2(8f, 1f));
                    painter.LineTo(new Vector2(15f, 14f));
                    painter.LineTo(new Vector2(1f, 14f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.strokeColor = new Color(0.2f, 0.17f, 0.1f);
                    painter.lineWidth = 1.6f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(8f, 5.5f));
                    painter.LineTo(new Vector2(8f, 9f));
                    painter.MoveTo(new Vector2(8f, 11.5f));
                    painter.LineTo(new Vector2(8f, 11.6f));
                    painter.Stroke();
                    return;
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

        private static void DrawTextAlignment(Painter2D painter, int alignment, bool vertical)
        {
            painter.lineWidth = 1f; painter.lineCap = LineCap.Butt;
            painter.BeginPath();
            if (vertical)
            {
                float y = 2.5f + alignment * 4;
                painter.MoveTo(new Vector2(2, y)); painter.LineTo(new Vector2(14, y));
                painter.MoveTo(new Vector2(4, y + 3)); painter.LineTo(new Vector2(12, y + 3));
                painter.Stroke(); return;
            }
            for (int i = 0; i < 4; i++)
            {
                float width = alignment == 3 ? 12 : i == 1 ? 7 : i == 3 ? 6 : 12;
                float x = alignment == 1 ? 8 - width * .5f : alignment == 2 ? 14 - width : 2;
                float y = 3.5f + i * 3;
                painter.MoveTo(new Vector2(x, y)); painter.LineTo(new Vector2(x + width, y));
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

        private void DrawCenterPivot(Painter2D painter)
        {
            float iconSize = Mathf.Min(contentRect.width, contentRect.height);
            Rect bounds = contentRect;
            bounds.xMin += iconSize / 16f;
            bounds.xMax -= iconSize / 16f;
            bounds.yMin += iconSize / 16f;
            bounds.yMax -= iconSize / 16f;
            float markerSize = iconSize * 3f / 16f;
            float stepX = (bounds.width - markerSize) * .5f;
            float stepY = (bounds.height - markerSize) * .5f;
            float pixelsPerPoint = UnityEditor.EditorGUIUtility.pixelsPerPoint;
            float borderX = 1f / Mathf.Max(.001f, worldTransform.MultiplyVector(Vector3.right).magnitude * pixelsPerPoint);
            float borderY = 1f / Mathf.Max(.001f, worldTransform.MultiplyVector(Vector3.up).magnitude * pixelsPerPoint);
            Vector2 center = bounds.center;
            float centerHalfSize = iconSize * 2f / 16f;
            Vector2 centerPixelSize = new Vector2(
                Mathf.Max(1f, Mathf.Round(centerHalfSize * 2f / borderX)),
                Mathf.Max(1f, Mathf.Round(centerHalfSize * 2f / borderY)));
            Vector2 centerPixelMin = this.LocalToWorld(center) * pixelsPerPoint - centerPixelSize * .5f;
            centerPixelMin.x = Mathf.Round(centerPixelMin.x);
            centerPixelMin.y = Mathf.Round(centerPixelMin.y);
            Rect centerBounds = new Rect(this.WorldToLocal(centerPixelMin / pixelsPerPoint),
                new Vector2(centerPixelSize.x * borderX, centerPixelSize.y * borderY));
            centerBounds.xMin += borderX;
            centerBounds.yMax -= borderY;
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    if (row == 1 && column == 1) continue;
                    float x = bounds.xMin + column * stepX, y = bounds.yMin + row * stepY;
                    painter.MoveTo(new Vector2(x, y));
                    painter.LineTo(new Vector2(x + markerSize, y));
                    painter.LineTo(new Vector2(x + markerSize, y + markerSize));
                    painter.LineTo(new Vector2(x, y + markerSize));
                    painter.ClosePath();
                }
            painter.Fill();
            painter.BeginPath();
            for (int edge = 0; edge < 2; edge++)
                for (int segment = 0; segment < 2; segment++)
                {
                    float sideX = bounds.xMin + markerSize * .5f + edge * (bounds.width - markerSize);
                    float sideY = bounds.yMin + markerSize * .5f + edge * (bounds.height - markerSize);
                    float startX = bounds.xMin + markerSize + segment * stepX;
                    float startY = bounds.yMin + markerSize + segment * stepY;
                    painter.MoveTo(new Vector2(startX, sideY));
                    painter.LineTo(new Vector2(startX + stepX - markerSize, sideY));
                    painter.MoveTo(new Vector2(sideX, startY));
                    painter.LineTo(new Vector2(sideX, startY + stepY - markerSize));
                }
            painter.Stroke();
            painter.fillColor = Color.white;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerBounds.xMin - borderX, centerBounds.yMin - borderY));
            painter.LineTo(new Vector2(centerBounds.xMax + borderX, centerBounds.yMin - borderY));
            painter.LineTo(new Vector2(centerBounds.xMax + borderX, centerBounds.yMax + borderY));
            painter.LineTo(new Vector2(centerBounds.xMin - borderX, centerBounds.yMax + borderY));
            painter.ClosePath();
            painter.Fill();
            painter.fillColor = new Color(1f, .6f, .2f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(centerBounds.xMin, centerBounds.yMin));
            painter.LineTo(new Vector2(centerBounds.xMax, centerBounds.yMin));
            painter.LineTo(new Vector2(centerBounds.xMax, centerBounds.yMax));
            painter.LineTo(new Vector2(centerBounds.xMin, centerBounds.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawCenter(Painter2D painter)
        {
            painter.lineWidth = 1.25f;
            painter.BeginPath();
            foreach (var corner in new[] { new Vector2(2, 2), new Vector2(14, 2), new Vector2(14, 14), new Vector2(2, 14) })
            {
                painter.MoveTo(new Vector2(corner.x, corner.y < 8 ? 5 : 11));
                painter.LineTo(corner);
                painter.LineTo(new Vector2(corner.x < 8 ? 5 : 11, corner.y));
            }
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(new Vector2(4, 8)); painter.LineTo(new Vector2(12, 8));
            painter.MoveTo(new Vector2(8, 4)); painter.LineTo(new Vector2(8, 12));
            painter.MoveTo(new Vector2(6, 6)); painter.LineTo(new Vector2(8, 8)); painter.LineTo(new Vector2(10, 6));
            painter.MoveTo(new Vector2(6, 10)); painter.LineTo(new Vector2(8, 8)); painter.LineTo(new Vector2(10, 10));
            painter.Stroke();
        }

        private static void DrawFlip(Painter2D painter, bool vertical)
        {
            Vector2 Point(float x, float y) => vertical ? new Vector2(y, x) : new Vector2(x, y);
            painter.lineWidth = 1f;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            for (int i = 0; i < 4; i++)
            {
                float y = 1.25f + i * 4f;
                painter.MoveTo(Point(8, y));
                painter.LineTo(Point(8, y + 1.5f));
            }
            painter.MoveTo(Point(14.5f, 3));
            painter.LineTo(Point(10.5f, 5.5f));
            painter.LineTo(Point(10.5f, 10.5f));
            painter.LineTo(Point(14.5f, 13));
            painter.ClosePath();
            painter.Stroke();
            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            painter.MoveTo(Point(1, 2.5f));
            painter.LineTo(Point(6, 5.5f));
            painter.LineTo(Point(6, 10.5f));
            painter.LineTo(Point(1, 13.5f));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawQuarterTurn(Painter2D painter, bool left)
        {
            const float longSide = 9f;
            const float shortSide = 4f;
            const float outlineWidth = 1f;
            Vector2 Point(float x, float y) => new Vector2(left ? x : 16f - x, y);
            void Rectangle(float x, float y, bool filled)
            {
                float width = filled ? longSide : shortSide;
                float height = filled ? shortSide : longSide;
                float inset = filled ? 0f : outlineWidth * .5f;
                painter.BeginPath();
                painter.MoveTo(Point(x + inset, y + inset));
                painter.LineTo(Point(x + width - inset, y + inset));
                painter.LineTo(Point(x + width - inset, y + height - inset));
                painter.LineTo(Point(x + inset, y + height - inset));
                painter.ClosePath();
                if (filled) painter.Fill();
                else painter.Stroke();
            }

            painter.lineWidth = outlineWidth;
            painter.lineCap = LineCap.Butt;
            painter.lineJoin = LineJoin.Miter;
            Rectangle(11, 1, false);

            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(Point(8.5f, 2.5f));
            painter.LineTo(Point(6.5f, 2.5f));
            painter.QuadraticCurveTo(Point(3.5f, 2.5f), Point(3.5f, 5.5f));
            painter.LineTo(Point(3.5f, 8));
            painter.Stroke();

            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            painter.MoveTo(Point(1, 7));
            painter.LineTo(Point(6, 7));
            painter.LineTo(Point(3.5f, 10));
            painter.ClosePath();
            painter.Fill();
            Rectangle(1, 11, true);
        }

        private static void DrawOriginalAspect(Painter2D painter)
        {
            painter.lineWidth = 1f;
            painter.lineJoin = LineJoin.Miter;
            painter.BeginPath();
            painter.MoveTo(new Vector2(1.5f, 1.5f));
            painter.LineTo(new Vector2(14.5f, 1.5f));
            painter.LineTo(new Vector2(14.5f, 14.5f));
            painter.LineTo(new Vector2(1.5f, 14.5f));
            painter.ClosePath();
            painter.Stroke();
            painter.fillColor = painter.strokeColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(3, 5));
            painter.LineTo(new Vector2(13, 5));
            painter.LineTo(new Vector2(13, 11));
            painter.LineTo(new Vector2(3, 11));
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawReset(Painter2D painter)
        {
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(new Vector2(8, 8), 5.5f, -135f, 135f);
            painter.MoveTo(new Vector2(4.1f, 4.1f));
            painter.LineTo(new Vector2(2.5f, 5.7f));
            painter.MoveTo(new Vector2(2.5f, 2));
            painter.LineTo(new Vector2(2.5f, 5.7f));
            painter.LineTo(new Vector2(6.2f, 5.7f));
            painter.Stroke();
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
