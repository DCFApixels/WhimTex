using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    public sealed class ShapeLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ShapeLayerBehaviour);
        public static void Open(ShapeLayerBehaviour layer, WhimTexDocument activeDocument) =>
            OpenPropertiesWindow<ShapeLayerEditorWindow>(layer, activeDocument);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (ShapeLayerBehaviour)source.Behaviour, Document, ApplyLayerChange, SettingsBindings);

        internal static void BuildFields(VisualElement root, ShapeLayerBehaviour layer, WhimTexDocument activeDocument,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            var kind = WhimTexUI.ConfigureField(new EnumField("Shape", layer.kind));
            bindings.Track(kind, () => (Enum)layer.kind);
            kind.RegisterValueChangedCallback(evt => apply("Change Shape", () =>
            { layer.kind = (ShapeLayerBehaviour.ShapeKind)evt.newValue; layer.NormalizeCorners(layer.GeometryHalfSize(activeDocument)); }));
            root.Add(kind);
            Toggle Toggle(string label, Func<bool> get, Action<bool> set)
            {
                var field = WhimTexUI.ConfigureField(new Toggle(label));
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => apply("Change Shape " + label, () => set(evt.newValue)));
                root.Add(field);
                return field;
            }
            ColorField Color(string label, Func<Color> get, Action<Color> set)
            {
                var field = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField(label), bindings, get));
                field.RegisterValueChangedCallback(evt => apply("Change Shape " + label, () => set(evt.newValue)));
                root.Add(field);
                return field;
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
            FloatField Pixels(string label, Func<float> get, Action<float> set, string tooltip)
            {
                var field = WhimTexUI.ConfigureField(new FloatField(label) { tooltip = tooltip });
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt =>
                {
                    float value = ShapeLayerBehaviour.Limit(evt.newValue, 0f, 8192f, get());
                    apply("Change Shape " + label, () => set(value));
                    field.SetValueWithoutNotify(value);
                });
                root.Add(field);
                return field;
            }
            var thickness = Pixels("Thickness (px)", () => layer.arcThickness, value => layer.arcThickness = value,
                "Arc body thickness in canvas pixels, independent of the outline. Transform controls the centerline ellipse.");
            Toggle("Fill", () => layer.fill, value => layer.fill = value);
            Color("Fill Color", () => layer.fillColor, value => layer.fillColor = value);
            Toggle("Stroke", () => layer.stroke, value => layer.stroke = value);
            Color("Stroke Color", () => layer.strokeColor, value => layer.strokeColor = value);
            var width = Pixels("Stroke Width (px)", () => layer.strokeWidth, value => layer.strokeWidth = value,
                "Outline width in canvas pixels. Resizing the shape keeps this width.");
            var strokePosition = WhimTexUI.ConfigureField(new EnumField("Stroke Position", layer.strokePosition));
            bindings.Track(strokePosition, () => (Enum)layer.strokePosition);
            strokePosition.RegisterValueChangedCallback(evt => apply("Change Shape Stroke Position", () => layer.strokePosition = (ShapeLayerBehaviour.StrokePosition)evt.newValue));
            root.Add(strokePosition);
            var caps = WhimTexUI.ConfigureField(new EnumField("Line Caps", layer.lineCap));
            bindings.Track(caps, () => (Enum)layer.lineCap);
            caps.RegisterValueChangedCallback(evt => apply("Change Shape Line Caps", () => layer.lineCap = (ShapeLayerBehaviour.LineCap)evt.newValue));
            root.Add(caps);
            var edgeMode = WhimTexUI.ConfigureField(new EnumField("Edge Mode", layer.edgeMode));
            bindings.Track(edgeMode, () => (Enum)layer.edgeMode);
            edgeMode.RegisterValueChangedCallback(evt => apply("Change Shape Edge Mode", () => layer.edgeMode = (ShapeLayerBehaviour.EdgeMode)evt.newValue));
            root.Add(edgeMode);
            var feather = WhimTexUI.ConfigureField(new FloatField("Feather (px)"));
            bindings.Track(feather, () => layer.feather);
            feather.RegisterValueChangedCallback(evt =>
            {
                float value = ShapeLayerBehaviour.Limit(evt.newValue, 0f, 8192f, layer.feather);
                apply("Change Shape Feather", () => layer.feather = value);
                feather.SetValueWithoutNotify(value);
            });
            feather.tooltip = "Soft edge transition in canvas pixels. Zero keeps the original antialiased edge.";
            root.Add(feather);
            var featherPosition = WhimTexUI.ConfigureField(new EnumField("Feather Position", layer.featherPosition));
            bindings.Track(featherPosition, () => (Enum)layer.featherPosition);
            featherPosition.RegisterValueChangedCallback(evt => apply("Change Shape Feather Position",
                () => layer.featherPosition = (ShapeLayerBehaviour.FeatherPosition)evt.newValue));
            featherPosition.tooltip = "Fade inside, outside, or across the contour. Also applies to both edges of a hollow stroke.";
            root.Add(featherPosition);
            var corners = ShapeCornerSettingsView.Build(layer, activeDocument, apply, bindings);
            root.Add(corners);
            var sides = WhimTexUI.ConfigureField(new SliderInt("Sides / Points", 3, 32) { showInputField = true });
            bindings.Track(sides, () => layer.sides);
            sides.RegisterValueChangedCallback(evt => apply("Change Shape Points", () =>
            { layer.sides = Mathf.Clamp(evt.newValue, 3, 32); layer.NormalizeCorners(layer.GeometryHalfSize(activeDocument)); }));
            root.Add(sides);
            var inner = Number("Inner Radius", () => layer.innerRadius, value =>
            { layer.innerRadius = value; layer.NormalizeCorners(layer.GeometryHalfSize(activeDocument)); }, .01f, 1f);
            var start = WhimTexUI.ConfigureField(new FloatField("Start Angle"));
            var sweep = WhimTexUI.ConfigureField(new FloatField("Sweep Angle"));
            bindings.Track(start, () => layer.startAngle); bindings.Track(sweep, () => layer.sweepAngle);
            start.RegisterValueChangedCallback(evt => apply("Change Shape Start Angle", () => layer.startAngle = ShapeLayerBehaviour.Limit(evt.newValue, -360000, 360000, 0)));
            sweep.RegisterValueChangedCallback(evt => apply("Change Shape Sweep Angle", () =>
            { layer.sweepAngle = ShapeLayerBehaviour.Limit(evt.newValue, 0, 360, 90); layer.NormalizeCorners(layer.GeometryHalfSize(activeDocument)); }));
            root.Add(start); root.Add(sweep);
            bindings.Add(() =>
            {
                bool arc = layer.kind == ShapeLayerBehaviour.ShapeKind.Arc, sector = layer.kind == ShapeLayerBehaviour.ShapeKind.Sector;
                thickness.EnableInClassList("whimtex-hidden", !arc);
                width.SetEnabled(layer.stroke);
                strokePosition.EnableInClassList("whimtex-hidden", !layer.stroke);
                caps.EnableInClassList("whimtex-hidden", !arc && layer.kind != ShapeLayerBehaviour.ShapeKind.Line);
                feather.SetEnabled(layer.edgeMode != ShapeLayerBehaviour.EdgeMode.Step);
                featherPosition.SetEnabled(layer.feather > 0f && layer.edgeMode != ShapeLayerBehaviour.EdgeMode.Step);
                start.EnableInClassList("whimtex-hidden", !arc && !sector); sweep.EnableInClassList("whimtex-hidden", !arc && !sector);
                sides.EnableInClassList("whimtex-hidden", layer.kind != ShapeLayerBehaviour.ShapeKind.Polygon && layer.kind != ShapeLayerBehaviour.ShapeKind.Star);
                inner.EnableInClassList("whimtex-hidden", layer.kind != ShapeLayerBehaviour.ShapeKind.Star);
            });
        }
    }
}
