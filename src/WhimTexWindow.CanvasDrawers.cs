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

        private static VisualElement AddCanvasDrawerHeader(VisualElement drawer)
        {
            var header = new VisualElement();
            header.AddToClassList("whimtex-pane-header");
            header.AddToClassList("whimtex-canvas-drawer-header");
            header.EnableInClassList("whimtex-pane-header--light", !UnityEditor.EditorGUIUtility.isProSkin);
            drawer.Add(header);
            return header;
        }

        private static Button AddCanvasDrawerActivationHeader(VisualElement drawer, string title, string titleName,
            string name, string text, Action activate)
        {
            var header = AddCanvasDrawerHeader(drawer);
            var label = new Label(title) { name = titleName, pickingMode = PickingMode.Ignore };
            label.AddToClassList("whimtex-canvas-drawer-title");
            header.Add(label);
            var button = new Button(activate) { name = name, text = text };
            button.AddToClassList("whimtex-canvas-drawer-activate");
            header.Add(button);
            return button;
        }

        private static Toggle AddCanvasDrawerToggleHeader(VisualElement drawer, string title, string titleName,
            string toggleName, bool enabled, string tooltip, Action<bool> change)
        {
            var header = AddCanvasDrawerHeader(drawer);
            var toggle = new Toggle { name = toggleName, text = title, value = enabled, tooltip = tooltip };
            toggle.AddToClassList("whimtex-canvas-drawer-toggle");
            var label = toggle.Q<Label>(className: "unity-toggle__text");
            if (label != null) label.name = titleName;
            toggle.RegisterValueChangedCallback(evt => change(evt.newValue));
            header.Add(toggle);
            return toggle;
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
                    painter.lineWidth = 1f;
                    painter.lineJoin = LineJoin.Miter;
                    painter.lineCap = LineCap.Butt;
                    painter.MoveTo(P(.5f, 8.5f)); painter.LineTo(P(5.5f, 8.5f));
                    painter.LineTo(P(5.5f, 3.5f)); painter.LineTo(P(10.5f, 3.5f));
                    painter.LineTo(P(10.5f, 8.5f)); painter.LineTo(P(20.5f, 8.5f));
                    painter.LineTo(P(20.5f, 13.5f)); painter.LineTo(P(10.5f, 13.5f));
                    painter.LineTo(P(10.5f, 18.5f)); painter.LineTo(P(5.5f, 18.5f));
                    painter.LineTo(P(5.5f, 13.5f)); painter.LineTo(P(.5f, 13.5f));
                    painter.ClosePath();
                    painter.MoveTo(P(5.5f, 8.5f)); painter.LineTo(P(5.5f, 13.5f));
                    painter.MoveTo(P(10.5f, 8.5f)); painter.LineTo(P(10.5f, 13.5f));
                    painter.MoveTo(P(15.5f, 8.5f)); painter.LineTo(P(15.5f, 13.5f));
                    painter.MoveTo(P(5.5f, 8.5f)); painter.LineTo(P(10.5f, 8.5f));
                    painter.MoveTo(P(5.5f, 13.5f)); painter.LineTo(P(10.5f, 13.5f));
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
