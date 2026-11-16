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

    }
}
