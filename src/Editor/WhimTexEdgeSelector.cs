using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class WhimTexEdgeSelector : VisualElement
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

    }
}
