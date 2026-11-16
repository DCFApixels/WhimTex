using System.Collections.Generic;
using System.Text;
using System;
using UnityEngine.UIElements;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class NodeMessageBadge : VisualElement
        {
            readonly VisualElement icon;
            readonly HelpBox popup;
            ShaderGraphMessageLevel level;
            internal NodeMessageBadge()
            {
                name = "node-message";
                AddToClassList("whimtex-shader-graph-message");
                icon = new VisualElement { name = "message-icon" }; Add(icon);
                icon.generateVisualContent += DrawIcon;
                popup = new HelpBox { name = "message-popup", pickingMode = PickingMode.Ignore };
                popup.AddToClassList("whimtex-shader-graph-message-popup");
                popup.style.display = DisplayStyle.None; Add(popup);
                icon.RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
                icon.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
                RegisterCallback<DetachFromPanelEvent>(_ => SetHovered(false));
            }
            void SetHovered(bool hovered)
            {
                if (hovered) parent?.BringToFront();
                popup.style.display = hovered ? DisplayStyle.Flex : DisplayStyle.None;
            }
            internal void SetMessage(string text, ShaderGraphMessageLevel level)
            {
                if (popup.text == text && this.level == level) return;
                this.level = level;
                popup.text = text;
                icon.MarkDirtyRepaint();
                EnableInClassList("whimtex-shader-graph-message--error", level == ShaderGraphMessageLevel.Error);
                EnableInClassList("whimtex-shader-graph-message--warning", level == ShaderGraphMessageLevel.Warning);
                EnableInClassList("whimtex-shader-graph-message--info", level == ShaderGraphMessageLevel.Info);
            }
            void DrawIcon(MeshGenerationContext context)
            {
                Rect rect = icon.contentRect;
                float scale = Mathf.Min(rect.width, rect.height) / 16f;
                if (scale <= 0) return;
                Vector2 Point(float x, float y) => rect.center + new Vector2(x - 8, y - 8) * scale;
                var painter = context.painter2D;
                painter.fillColor = level switch
                {
                    ShaderGraphMessageLevel.Error => new Color32(229, 81, 81, 255),
                    ShaderGraphMessageLevel.Warning => new Color32(245, 193, 65, 255),
                    _ => new Color32(192, 192, 192, 255)
                };
                painter.strokeColor = new Color(0, 0, 0, .55f);
                painter.lineWidth = scale;
                painter.lineJoin = LineJoin.Round;
                painter.lineCap = LineCap.Round;
                painter.BeginPath();
                if (level == ShaderGraphMessageLevel.Warning)
                {
                    painter.MoveTo(Point(8, 1.25f));
                    painter.LineTo(Point(14.75f, 14.25f));
                    painter.LineTo(Point(1.25f, 14.25f));
                    painter.ClosePath();
                }
                else if (level == ShaderGraphMessageLevel.Error)
                {
                    painter.MoveTo(Point(4.2f, 1.2f));
                    painter.LineTo(Point(11.8f, 1.2f));
                    painter.LineTo(Point(14.8f, 4.2f));
                    painter.LineTo(Point(14.8f, 11.8f));
                    painter.LineTo(Point(11.8f, 14.8f));
                    painter.LineTo(Point(4.2f, 14.8f));
                    painter.LineTo(Point(1.2f, 11.8f));
                    painter.LineTo(Point(1.2f, 4.2f));
                    painter.ClosePath();
                }
                else
                {
                    painter.Arc(Point(8, 8), 6.8f * scale, 0, 360);
                    painter.ClosePath();
                }
                painter.Fill(); painter.Stroke();
                painter.fillColor = painter.strokeColor = new Color32(43, 35, 17, 255);
                painter.lineWidth = 1.8f * scale;
                painter.BeginPath();
                float stemTop = level switch
                {
                    ShaderGraphMessageLevel.Error => 4.3f,
                    ShaderGraphMessageLevel.Warning => 5.3f,
                    _ => 7.1f
                };
                bool info = level == ShaderGraphMessageLevel.Info;
                painter.MoveTo(Point(8, stemTop));
                painter.LineTo(Point(8, info ? 11.3f : 8.8f));
                painter.Stroke();
                painter.BeginPath();
                painter.Arc(Point(8, info ? 4.5f : 11.6f), .95f * scale, 0, 360);
                painter.Fill();
            }
        }

        private sealed partial class PortBranchLabel
        {
            void DrawBufferIcon(MeshGenerationContext context)
            {
                Rect rect = bufferIcon.contentRect;
                if (rect.width <= 0 || rect.height <= 0) return;
                Vector2 Point(float x, float y) => new Vector2(rect.x + x * rect.width, rect.y + y * rect.height);
                var painter = context.painter2D;
                painter.strokeColor = label.resolvedStyle.color;
                painter.lineWidth = Mathf.Min(rect.width, rect.height) / 12;
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .25f));
                painter.BezierCurveTo(Point(.13f, .04f), Point(.87f, .04f), Point(.87f, .25f));
                painter.BezierCurveTo(Point(.87f, .46f), Point(.13f, .46f), Point(.13f, .25f));
                painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .25f)); painter.LineTo(Point(.13f, .75f));
                painter.BezierCurveTo(Point(.13f, .96f), Point(.87f, .96f), Point(.87f, .75f));
                painter.LineTo(Point(.87f, .25f)); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .50f));
                painter.BezierCurveTo(Point(.13f, .71f), Point(.87f, .71f), Point(.87f, .50f));
                painter.Stroke();
            }
        }

    }
}
