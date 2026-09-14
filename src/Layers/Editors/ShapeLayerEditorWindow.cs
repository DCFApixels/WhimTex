using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class ShapeLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ShapeLayerBehaviour);
        public static void Open(ShapeLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<ShapeLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (ShapeLayerBehaviour)source.Behaviour, Compositor, ApplyLayerChange, SettingsBindings);

        internal static void BuildFields(VisualElement root, ShapeLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            var kind = WhimTexUI.ConfigureField(new EnumField("Shape", layer.kind));
            bindings.Track(kind, () => (Enum)layer.kind);
            kind.RegisterValueChangedCallback(evt => apply("Change Shape", () => layer.kind = (ShapeLayerBehaviour.ShapeKind)evt.newValue));
            root.Add(kind);
            void Toggle(string label, Func<bool> get, Action<bool> set)
            {
                var field = WhimTexUI.ConfigureField(new Toggle(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => apply("Change Shape " + label, () => set(evt.newValue)));
                root.Add(field);
            }
            void Color(string label, Func<Color> get, Action<Color> set)
            {
                var field = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField(label), bindings, get));
                field.RegisterValueChangedCallback(evt => apply("Change Shape " + label, () => set(evt.newValue)));
                root.Add(field);
            }
            Slider Number(string label, Func<float> get, Action<float> set, float min, float max)
            {
                var field = WhimTexUI.ConfigureField(new Slider(label, min, max) { showInputField = true });
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => apply("Change Shape " + label,
                    () => set(ShapeLayerBehaviour.Limit(evt.newValue, min, max, get()))));
                root.Add(field);
                return field;
            }
            Toggle("Fill", () => layer.fill, value => layer.fill = value);
            Color("Fill Color", () => layer.fillColor, value => layer.fillColor = value);
            Toggle("Stroke", () => layer.stroke, value => layer.stroke = value);
            Color("Stroke Color", () => layer.strokeColor, value => layer.strokeColor = value);
            var width = WhimTexUI.ConfigureField(new FloatField("Stroke Width (px)"));
            bindings.Track(width, () => layer.strokeWidth);
            width.RegisterValueChangedCallback(evt =>
            {
                float value = ShapeLayerBehaviour.Limit(evt.newValue, 0f, 8192f, layer.strokeWidth);
                apply("Change Shape Stroke Width", () => layer.strokeWidth = value);
                width.SetValueWithoutNotify(value);
            });
            width.tooltip = "Inside outline in canvas pixels. Type a value or drag the label to adjust. Resizing the shape keeps this width.";
            root.Add(width);
            var roundness = ShapeCornerSettingsView.Build(layer, apply, bindings);
            root.Add(roundness);
            var sides = WhimTexUI.ConfigureField(new SliderInt("Sides / Points", 3, 32) { showInputField = true });
            bindings.Track(sides, () => layer.sides);
            sides.RegisterValueChangedCallback(evt => apply("Change Shape Points", () => layer.sides = Mathf.Clamp(evt.newValue, 3, 32)));
            root.Add(sides);
            var inner = Number("Inner Radius", () => layer.innerRadius, value => layer.innerRadius = value, .01f, 1f);
            bindings.Add(() =>
            {
                width.SetEnabled(layer.stroke);
                roundness.EnableInClassList("whimtex-hidden", layer.kind != ShapeLayerBehaviour.ShapeKind.Rectangle);
                sides.EnableInClassList("whimtex-hidden", layer.kind != ShapeLayerBehaviour.ShapeKind.Polygon && layer.kind != ShapeLayerBehaviour.ShapeKind.Star);
                inner.EnableInClassList("whimtex-hidden", layer.kind != ShapeLayerBehaviour.ShapeKind.Star);
            });
        }
    }
}
