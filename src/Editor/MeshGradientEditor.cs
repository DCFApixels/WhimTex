using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal static class MeshGradientEditor
    {
        internal static void Build(VisualElement root, GradientLayerBehaviour layer, WhimTexDocument document,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            int Index() => Mathf.Clamp(layer.selectedMeshNode, 0, (layer.mesh?.NodeCount ?? 1) - 1);
            void Change(string label, Action<MeshGradient> edit)
            {
                if (layer.mesh == null) return;
                var next = layer.mesh.Clone(); edit(next); next.Validate();
                apply(label, () => layer.mesh = next);
            }
            var model = WhimTexUI.ConfigureField(new PopupField<string>("Color Model", new List<string> { "Linear", "Gamma", "Perceptual", "Hue" }, 2) { name = "meshColorModel" });
            bindings.Track(model, () => (layer.mesh?.colorMode ?? WhimTexGradientColorMode.Perceptual).ToString());
            model.RegisterValueChangedCallback(e => Change("Change Mesh Color Model", next => next.SetColorMode(Enum.Parse<WhimTexGradientColorMode>(e.newValue))));
            root.Add(model);
            var dimensions = new Label { name = "meshDimensions" }; root.Add(dimensions);
            string NodeLabel(int n) => $"{n % ((layer.mesh?.columns ?? 1) + 1) + 1}, {n / ((layer.mesh?.columns ?? 1) + 1) + 1}";
            var node = WhimTexUI.ConfigureField(new PopupField<int>("Node", new List<int> { 0 }, 0, NodeLabel, NodeLabel) { name = "meshNode" });
            node.RegisterValueChangedCallback(e => layer.selectedMeshNode = e.newValue); root.Add(node);
            var nodeMode = WhimTexUI.ConfigureField(new PopupField<MeshGradientNodeMode>("Node Type",
                new List<MeshGradientNodeMode> { MeshGradientNodeMode.Smooth, MeshGradientNodeMode.Corner }, 0) { name = "meshNodeMode" });
            bindings.Track(nodeMode, () => layer.mesh?.NodeMode(Index()) ?? MeshGradientNodeMode.Smooth);
            nodeMode.RegisterValueChangedCallback(e => Change("Change Mesh Node Type", next => next.SetNodeMode(Index(), e.newValue)));
            root.Add(nodeMode);
            var position = WhimTexUI.ConfigureField(new Vector2Field("Position") { name = "meshPosition" });
            bindings.Track(position, () => layer.mesh?.NodePosition(Index()) ?? Vector2.zero);
            position.RegisterValueChangedCallback(e =>
            {
                if (!float.IsFinite(e.newValue.x) || !float.IsFinite(e.newValue.y)) return;
                Change("Move Mesh Node", next => next.SetNodePosition(Index(), e.newValue));
            }); root.Add(position);
            var color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexColorField("Color")
                { name = "meshColor", showAlpha = true, Document = () => document }, bindings, () => layer.mesh?.NodeColor(Index()) ?? Color.white));
            color.RegisterValueChangedCallback(e => Change("Change Mesh Color", next => next.SetNodeColor(Index(), e.newValue)));
            color.OpenPickerOverride = () =>
            {
                int index = Index(); var expected = layer.mesh; if (expected == null) return;
                Color initial = expected.NodeColor(index);
                bool Valid() => color.panel != null && color.enabledInHierarchy && layer.gradientType == GradientLayerBehaviour.GradientType.Mesh &&
                    layer.mesh == expected && Index() == index;
                var picker = WhimTexColorPicker.Open(initial, color.hdr || initial.maxColorComponent > 1 || initial.r < 0 || initial.g < 0 || initial.b < 0,
                    true, WhimTexColorRange.Switchable, document, value =>
                    {
                        if (!Valid() || expected.NodeColor(index).Equals(value)) return;
                        Change("Change Mesh Color", next => next.SetNodeColor(index, value)); expected = layer.mesh;
                    }, value => WhimTexColorInputs.Hdr = value, Valid);
                picker.SetChannelSource(color.ResolveChannelSource());
            }; root.Add(color);
            var opacity = WhimTexUI.ConfigureField(new Slider("Opacity", 0, 100) { name = "meshOpacity", showInputField = true });
            bindings.Track(opacity, () => (layer.mesh?.NodeColor(Index()).a ?? 1) * 100);
            opacity.RegisterValueChangedCallback(e => Change("Change Mesh Opacity", next =>
            { var value = next.NodeColor(Index()); value.a = e.newValue / 100; next.SetNodeColor(Index(), value); })); root.Add(opacity);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } }; root.Add(buttons);
            var row = new Button(() =>
            {
                int r = Index() / (layer.mesh.columns + 1), c = Index() % (layer.mesh.columns + 1);
                Change("Add Mesh Row", next => next.SplitRow(Mathf.Min(r, next.rows - 1)));
                layer.selectedMeshNode = (Mathf.Min(r, layer.mesh.rows - 2) + 1) * (layer.mesh.columns + 1) + c;
            }) { text = "Add Row", name = "meshAddRow" }; buttons.Add(row);
            var column = new Button(() =>
            {
                int r = Index() / (layer.mesh.columns + 1), c = Index() % (layer.mesh.columns + 1);
                Change("Add Mesh Column", next => next.SplitColumn(Mathf.Min(c, next.columns - 1)));
                layer.selectedMeshNode = r * (layer.mesh.columns + 1) + Mathf.Min(c, layer.mesh.columns - 2) + 1;
            }) { text = "Add Column", name = "meshAddColumn" }; buttons.Add(column);
            bindings.Add(() =>
            {
                bool visible = layer.gradientType == GradientLayerBehaviour.GradientType.Mesh;
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;
                dimensions.text = $"{layer.mesh.columns} columns × {layer.mesh.rows} rows";
                if (node.choices.Count != layer.mesh.NodeCount)
                { var choices = new List<int>(); for (int i = 0; i < layer.mesh.NodeCount; i++) choices.Add(i); node.choices = choices; }
                node.SetValueWithoutNotify(Index());
                row.SetEnabled(layer.mesh.rows < MeshGradient.MaxCells); column.SetEnabled(layer.mesh.columns < MeshGradient.MaxCells);
            });
        }
    }
}
