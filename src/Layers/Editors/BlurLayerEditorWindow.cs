using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class BlurLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(BlurLayerBehaviour);
        protected override string PreviewTitle => "Preview (Blur)";
        public static void Open(BlurLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<BlurLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (BlurLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);

        internal static void BuildFields(VisualElement root, BlurLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);
            var mode = WhimTexUI.ConfigureField(new EnumField("Mode", layer.mode));
            bindings.Track(mode, () => (Enum)layer.mode);
            mode.RegisterValueChangedCallback(evt => applyChange("Change Blur Mode",
                () => layer.mode = (BlurType)evt.newValue));
            root.Add(mode);

            void Slider(VisualElement parent, string label, string tooltip, Func<float> get, Action<float> set, float min, float max)
            {
                var field = WhimTexUI.ConfigureField(new Slider(label, min, max) { showInputField = true, tooltip = tooltip });
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(evt => applyChange("Change Blur " + label,
                    () => set(MotionBlurRenderer.Limit(evt.newValue, min, max, get()))));
                parent.Add(field);
            }

            Slider(root, "Strength (%)", "0: original; 100: normal blur; above 100: denser translucent blur without changing its size or color brightness. Fully opaque areas are unchanged above 100.",
                () => layer.strength * 100f, value => layer.strength = value / 100f, 0f, BlurLayerBehaviour.MaximumStrength * 100f);

            var gaussian = new VisualElement();
            Slider(gaussian, "Radius (px)", "Extent in original canvas pixels. Zero leaves the source unchanged.",
                () => layer.radius, value => layer.radius = value, 0f, BlurLayerBehaviour.MaximumRadius);
            root.Add(gaussian);

            var linear = new VisualElement();
            Slider(linear, "Distance (px)", "Total length in original canvas pixels. Zero leaves the source unchanged.",
                () => layer.distance, value => layer.distance = value, 0f, BlurLayerBehaviour.MaximumDistance);
            Slider(linear, "Angle (deg)", "Zero points right; positive angles turn counterclockwise.",
                () => layer.angle, value => layer.angle = value, -180f, 180f);
            root.Add(linear);

            var circular = new VisualElement();
            Slider(circular, "Arc (deg)", "Total rotation swept by the blur. Zero leaves the source unchanged.",
                () => layer.arc, value => layer.arc = value, 0f, 360f);
            var center = WhimTexUI.ConfigureField(new Vector2Field("Center"));
            center.tooltip = "Normalized canvas position: (0, 0) bottom-left, (1, 1) top-right. Independent of the output Transform pivot.";
            bindings.Track(center, () => layer.center);
            center.RegisterValueChangedCallback(evt => applyChange("Change Blur Center", () => layer.center = new Vector2(
                MotionBlurRenderer.Limit(evt.newValue.x, 0f, 1f, layer.center.x),
                MotionBlurRenderer.Limit(evt.newValue.y, 0f, 1f, layer.center.y))));
            circular.Add(center);
            root.Add(circular);

            var direction = WhimTexUI.ConfigureField(new EnumField("Direction", layer.direction));
            direction.tooltip = "Centered spreads both ways. Forward follows Angle in Linear mode and turns counterclockwise in Circular mode; Backward reverses it.";
            bindings.Track(direction, () => (Enum)layer.direction);
            direction.RegisterValueChangedCallback(evt => applyChange("Change Blur Direction",
                () => layer.direction = (BlurLayerBehaviour.MotionDirection)evt.newValue));
            root.Add(direction);
            var edges = WhimTexUI.ConfigureField(new EnumField("Edges", layer.edges));
            edges.tooltip = "Sampling outside the source canvas, independent of tiled preview and Transform tiling.";
            bindings.Track(edges, () => (Enum)layer.edges);
            edges.RegisterValueChangedCallback(evt => applyChange("Change Blur Edges",
                () => layer.edges = (BlurLayerBehaviour.EdgeMode)evt.newValue));
            root.Add(edges);

            bindings.Add(() =>
            {
                gaussian.EnableInClassList("whimtex-hidden", layer.mode != BlurType.Gaussian);
                direction.EnableInClassList("whimtex-hidden", layer.mode == BlurType.Gaussian);
                linear.EnableInClassList("whimtex-hidden", layer.mode != BlurType.Linear);
                circular.EnableInClassList("whimtex-hidden", layer.mode != BlurType.Circular);
            });
        }
    }
}
