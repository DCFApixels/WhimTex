using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private ShaderGraphColorMode colorMode;
        private ToolbarMenu colorModeMenu;
        private static readonly string[] ColorModeNames = { "<None>", "Category", "Heatmap", "Precision", "User Defined" };

        private void AddColorMode(Toolbar toolbar)
        {
            colorMode = WhimTexUserSettings.GraphColorMode;
            var field = new VisualElement();
            field.AddToClassList("whimtex-shader-graph-color-mode-field");
            field.Add(new Label("Color Mode"));
            colorModeMenu = new ToolbarMenu { name = "graph-color-mode", text = ColorModeNames[(int)colorMode] };
            colorModeMenu.AddToClassList("whimtex-shader-graph-color-mode");
            for (int i = 0; i < ColorModeNames.Length; i++)
            {
                var mode = (ShaderGraphColorMode)i;
                colorModeMenu.menu.AppendAction(ColorModeNames[i], _ => WhimTexUserSettings.GraphColorMode = mode,
                    _ => colorMode == mode ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            field.Add(colorModeMenu);
            toolbar.Add(field);
        }

        private void RefreshColorMode()
        {
            var value = WhimTexUserSettings.GraphColorMode;
            if (colorMode == value) return;
            colorMode = value;
            if (colorModeMenu != null) colorModeMenu.text = ColorModeNames[(int)value];
            canvas?.RefreshNodeColors();
        }

        private sealed partial class NodeView
        {
            internal void ApplyNodeColor()
            {
                bool colored = ShaderGraphNodeColors.TryGet(Data, window.colorMode, out Color color);
                titleContainer.style.borderBottomColor = colored ? new StyleColor(color) : new StyleColor(StyleKeyword.Null);
                // Source metric: MaterialNodeView.uss, title border, package 17.7.0.
                titleContainer.style.borderBottomWidth = colored ? new StyleFloat(8f) : new StyleFloat(StyleKeyword.Null);
            }
        }

        private sealed partial class Canvas
        {
            internal void RefreshNodeColors()
            {
                foreach (var view in nodeViews.Values) if (view is NodeView node) node.ApplyNodeColor();
            }

            private void NodeColorMenu(ContextualMenuPopulateEvent e)
            {
                if (window.colorMode != ShaderGraphColorMode.UserDefined) return;
                var target = e.target as VisualElement;
                var clicked = target as NodeView ?? target?.GetFirstAncestorOfType<NodeView>();
                if (clicked == null) return;
                var ids = new List<string>();
                if (selection.Contains(clicked))
                {
                    foreach (var selected in selection) if (selected is NodeView node) ids.Add(node.Id);
                }
                else ids.Add(clicked.Id);
                var graph = window.Graph;
                bool Valid() => panel != null && window.Editable && ReferenceEquals(graph, window.Graph) &&
                    ids.TrueForAll(id => graph.FindNode(id) != null);
                DropdownMenuAction.Status Enabled(DropdownMenuAction _) => Valid() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
                void Apply(string[] colors)
                {
                    if (!Valid()) return;
                    window.Edit("Change Node Color", g =>
                    {
                        for (int i = 0; i < ids.Count; i++) g.FindNode(ids[i]).userColor = colors[i];
                    }, structural: false, rebuild: false, layoutOnly: true);
                    RefreshNodeColors();
                }
                e.menu.AppendSeparator();
                e.menu.AppendAction("Color/Change...", _ =>
                {
                    if (!Valid()) return;
                    var original = new string[ids.Count];
                    for (int i = 0; i < ids.Count; i++) original[i] = graph.FindNode(ids[i]).userColor;
                    if (!ColorUtility.TryParseHtmlString(original[0], out var initial)) initial = Color.gray;
                    var picker = WhimTexColorPicker.Open(initial, false, false, WhimTexColorRange.StandardOnly, null,
                        color =>
                        {
                            var colors = new string[ids.Count];
                            string html = "#" + ColorUtility.ToHtmlStringRGB(color);
                            for (int i = 0; i < colors.Length; i++) colors[i] = html;
                            Apply(colors);
                        }, valid: Valid);
                    bool mixed = false;
                    for (int i = 1; i < original.Length; i++) mixed |= original[i] != original[0];
                    picker.SetMultiValueSource(mixed, () => Apply(original));
                }, Enabled);
                e.menu.AppendAction("Color/Reset", _ => Apply(new string[ids.Count]), Enabled);
            }
        }
    }
}
