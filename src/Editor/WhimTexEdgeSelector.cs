using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexEdgeSelector : VisualElement
    {
        private readonly Dictionary<Button, string> hoverGroups = new Dictionary<Button, string>();

        internal WhimTexEdgeSelector(string controlName, Action invert, string centerTooltip = "Invert selected edges.")
        {
            name = controlName;
            AddToClassList("whimtex-seamless-edges");
            var center = new Button(invert) { name = controlName + "-center", tooltip = centerTooltip };
            center.AddToClassList("whimtex-seamless-image");
            center.Add(new ImageIcon());
            Add(center);
            RegisterCallback<DetachFromPanelEvent>(_ => SetHovered(null));
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(null));
        }

        internal void AddEdge(Button button, string hoverGroup)
        {
            hoverGroups.Add(button, hoverGroup);
            button.RegisterCallback<PointerEnterEvent>(_ => SetHovered(hoverGroup));
            button.RegisterCallback<PointerLeaveEvent>(_ => SetHovered(null));
            Add(button);
        }

        private void SetHovered(string group)
        {
            foreach (var edge in hoverGroups)
                edge.Key.EnableInClassList("whimtex-seamless-edge--hovered", group != null && edge.Value == group);
        }

        private sealed class ImageIcon : VisualElement
        {
            public ImageIcon()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-seamless-center-icon");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width < 1f || r.height < 1f) return;
                var painter = context.painter2D;
                painter.fillColor = resolvedStyle.color;
                Vector2 Point(float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
                painter.BeginPath();
                painter.Arc(Point(.7f, .29f), r.width * .075f, 0f, 360f);
                painter.Fill();
                painter.BeginPath();
                painter.MoveTo(Point(.16f, .77f));
                painter.LineTo(Point(.4f, .37f));
                painter.LineTo(Point(.57f, .62f));
                painter.LineTo(Point(.69f, .49f));
                painter.LineTo(Point(.85f, .77f));
                painter.ClosePath();
                painter.Fill();
            }
        }
    }
}
