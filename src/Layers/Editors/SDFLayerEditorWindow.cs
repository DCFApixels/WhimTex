using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class SDFLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(SDFLayerBehaviour);
        protected override string PreviewTitle => "Preview (SDF)";

        public static void Open(SDFLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<SDFLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (SDFLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);
        }

        internal static void BuildFields(
            VisualElement root, SDFLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);

            EnumField metric = WhimTexUI.ConfigureField(new EnumField("Distance Algorithm", layer.metric));
            metric.tooltip = "Euclidean Antialiased locates the Threshold contour between pixels, including soft edges. Euclidean Exact keeps a hard threshold; useful for pixel masks.";
            bindings.Track(metric, () => (Enum)layer.metric);
            metric.RegisterValueChangedCallback(evt => applyChange(
                "Change SDF Algorithm",
                () => layer.metric = (DistanceMetric)evt.newValue));
            root.Add(metric);

            EnumField sourceChannel = WhimTexUI.ConfigureField(
                new EnumField("Source Channel", layer.sourceChannel));
            bindings.Track(sourceChannel, () => (Enum)layer.sourceChannel);
            sourceChannel.RegisterValueChangedCallback(evt => applyChange(
                "Change SDF Source Channel",
                () => layer.sourceChannel = (SDFLayerBehaviour.SourceChannel)evt.newValue));
            root.Add(sourceChannel);

            SliderInt threshold = new SliderInt("Threshold", 0, 255);
            WhimTexUI.ConfigureField(threshold);
            threshold.showInputField = true;
            threshold.SetValueWithoutNotify(layer.threshold);
            bindings.Track(threshold, () => (int)layer.threshold);
            threshold.RegisterValueChangedCallback(evt => applyChange(
                "Change SDF Threshold",
                () => layer.threshold = (byte)Mathf.Clamp(evt.newValue, 0, 255)));
            root.Add(threshold);

            FloatField maxDistance = WhimTexUI.ConfigureField(
                new FloatField("Max Distance (px, 0 = auto)"));
            maxDistance.SetValueWithoutNotify(layer.maxDistanceNormalization);
            bindings.Track(maxDistance, () => layer.maxDistanceNormalization);
            maxDistance.RegisterValueChangedCallback(evt => applyChange(
                "Change SDF Max Distance",
                () => layer.maxDistanceNormalization = Mathf.Max(0f, evt.newValue)));
            root.Add(maxDistance);

            EnumField distancePosition = WhimTexUI.ConfigureField(
                new EnumField("Position", layer.distancePosition));
            bindings.Track(distancePosition, () => (Enum)layer.distancePosition);
            distancePosition.RegisterValueChangedCallback(evt => applyChange(
                "Change SDF Position",
                () => layer.distancePosition = (SDFLayerBehaviour.DistancePosition)evt.newValue));
            root.Add(distancePosition);

            Toggle inverted = WhimTexUI.ConfigureField(new Toggle("Inverted"));
            inverted.SetValueWithoutNotify(layer.inverted);
            bindings.Track(inverted, () => layer.inverted);
            inverted.RegisterValueChangedCallback(evt =>
                applyChange("Invert SDF", () => layer.inverted = evt.newValue));
            root.Add(inverted);

            GradientField gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new GradientField("Gradient"), bindings, () => layer.gradient));
            gradient.RegisterValueChangedCallback(evt =>
                applyChange("Change SDF Gradient", () => layer.gradient = GradientUtility.Create(evt.newValue)));
            root.Add(gradient);
        }
    }
}
