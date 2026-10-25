using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private static Button CreateCanvasDrawerTab(string name, string tooltip, VisualElement icon, Action toggle)
        {
            var button = new Button(toggle) { name = name, tooltip = tooltip };
            button.AddToClassList("whimtex-tool-button");
            button.AddToClassList("whimtex-canvas-view-drawer-tab");
            button.Add(icon);
            return button;
        }

        private static void RefreshCanvasDrawerTab(Button button, bool expanded)
        {
            button?.EnableInClassList("whimtex-tool-button--selected", expanded);
        }

        private static Button AddCanvasDrawerActivation(VisualElement drawer, string name, string text, Action activate)
        {
            var button = new Button(activate) { name = name, text = text };
            button.AddToClassList("whimtex-canvas-drawer-activate");
            drawer.Add(button);
            return button;
        }

        private sealed class CanvasPreviewPanelIcon : VisualElement
        {
            internal enum Kind { PostFx, Uv }
            private readonly Kind kind;

            internal CanvasPreviewPanelIcon(Kind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
            }

            private Vector2 P(float x, float y) => new Vector2(
                contentRect.x + x * contentRect.width / 22f,
                contentRect.y + y * contentRect.height / 22f);

            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f) return;
                var painter = context.painter2D;
                painter.strokeColor = painter.fillColor = resolvedStyle.color;
                painter.lineWidth = 1.35f;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                if (kind == Kind.Uv)
                {
                    painter.MoveTo(P(3, 4)); painter.LineTo(P(18, 3));
                    painter.LineTo(P(19, 18)); painter.LineTo(P(4, 19));
                    painter.ClosePath();
                    painter.MoveTo(P(3, 4)); painter.LineTo(P(10, 11)); painter.LineTo(P(18, 3));
                    painter.MoveTo(P(4, 19)); painter.LineTo(P(10, 11)); painter.LineTo(P(19, 18));
                    painter.Stroke();
                }
                else
                {
                    painter.MoveTo(P(9, 6)); painter.LineTo(P(3, 6));
                    painter.LineTo(P(3, 19)); painter.LineTo(P(17, 19));
                    painter.LineTo(P(17, 13)); painter.Stroke();
                    DrawSparkle(painter, 15, 7, 6);
                    DrawSparkle(painter, 7, 13, 2.5f);
                }
            }

            private void DrawSparkle(Painter2D painter, float x, float y, float radius)
            {
                float neck = radius * .24f;
                painter.BeginPath();
                painter.MoveTo(P(x, y - radius));
                painter.QuadraticCurveTo(P(x + neck, y - neck), P(x + radius, y));
                painter.QuadraticCurveTo(P(x + neck, y + neck), P(x, y + radius));
                painter.QuadraticCurveTo(P(x - neck, y + neck), P(x - radius, y));
                painter.QuadraticCurveTo(P(x - neck, y - neck), P(x, y - radius));
                painter.ClosePath(); painter.Fill();
            }
        }
    }
}
