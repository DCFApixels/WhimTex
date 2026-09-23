using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class SharpenLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(SharpenLayerBehaviour);
        protected override string PreviewTitle => "Preview (Sharpen)";
        public static void Open(SharpenLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<SharpenLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer source) =>
            BuildFields(root, (SharpenLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);

        internal static void BuildFields(VisualElement root, SharpenLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);
            Slider noise = null;
            var algorithm = WhimTexUI.ConfigureField(new EnumField("Algorithm", layer.algorithm)
                { tooltip = "Gaussian applies a conventional unsharp mask. Adaptive favors coherent edges over weak, irregular detail." });
            bindings.Track(algorithm, () => (Enum)layer.algorithm);
            algorithm.RegisterValueChangedCallback(evt =>
            {
                applyChange("Change Sharpen Algorithm", () => layer.algorithm = (SharpenLayerBehaviour.Algorithm)evt.newValue);
                noise?.SetEnabled(layer.algorithm == SharpenLayerBehaviour.Algorithm.Adaptive);
            });
            root.Add(algorithm);
            var strength = WhimTexUI.ConfigureField(new Slider("Strength (%)", 0f, SharpenLayerBehaviour.MaximumStrength * 100f)
                { showInputField = true, tooltip = "Enhances local contrast around edges. Zero leaves the source unchanged." });
            strength.SetValueWithoutNotify(layer.strength * 100f);
            bindings.Track(strength, () => layer.strength * 100f);
            strength.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Strength",
                () => layer.strength = Mathf.Clamp(evt.newValue / 100f, 0f, SharpenLayerBehaviour.MaximumStrength)));
            root.Add(strength);

            var radius = WhimTexUI.ConfigureField(new Slider("Radius (px)", 0f, SharpenLayerBehaviour.MaximumRadius)
                { showInputField = true, tooltip = "Distance of the contrast comparison in original canvas pixels." });
            radius.SetValueWithoutNotify(layer.radius);
            bindings.Track(radius, () => layer.radius);
            radius.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Radius",
                () => layer.radius = Mathf.Clamp(evt.newValue, 0f, SharpenLayerBehaviour.MaximumRadius)));
            root.Add(radius);

            var threshold = WhimTexUI.ConfigureField(new Slider("Threshold", 0f, 1f)
                { showInputField = true, tooltip = "Minimum local contrast to sharpen." });
            threshold.SetValueWithoutNotify(layer.threshold);
            bindings.Track(threshold, () => layer.threshold);
            threshold.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Threshold",
                () => layer.threshold = Mathf.Clamp01(evt.newValue)));
            root.Add(threshold);

            noise = WhimTexUI.ConfigureField(new Slider("Noise Reduction", 0f, 1f)
                { showInputField = true, tooltip = "Adaptive only. Increases suppression of weak, directionless detail while retaining coherent edges." });
            noise.SetValueWithoutNotify(layer.noiseReduction);
            noise.SetEnabled(layer.algorithm == SharpenLayerBehaviour.Algorithm.Adaptive);
            bindings.Track(noise, () => layer.noiseReduction);
            bindings.Add(() => noise.SetEnabled(layer.algorithm == SharpenLayerBehaviour.Algorithm.Adaptive));
            noise.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Noise Reduction",
                () => layer.noiseReduction = Mathf.Clamp01(evt.newValue)));
            root.Add(noise);

            var halo = WhimTexUI.ConfigureField(new Slider("Halo Suppression", 0f, 1f)
                { showInputField = true, tooltip = "Limits overshoot around edges." });
            halo.SetValueWithoutNotify(layer.haloSuppression);
            bindings.Track(halo, () => layer.haloSuppression);
            halo.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Halo Suppression",
                () => layer.haloSuppression = Mathf.Clamp01(evt.newValue)));
            root.Add(halo);

            var channels = WhimTexUI.ConfigureField(new EnumField("Channels", layer.channelMode)
                { tooltip = "Sharpen RGB independently or apply the detail to luminance." });
            bindings.Track(channels, () => (Enum)layer.channelMode);
            channels.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Channels",
                () => layer.channelMode = (SharpenLayerBehaviour.ChannelMode)evt.newValue));
            root.Add(channels);

            var edges = WhimTexUI.ConfigureField(new EnumField("Edges", layer.edges)
                { tooltip = "Sampling outside the source bounds." });
            bindings.Track(edges, () => (Enum)layer.edges);
            edges.RegisterValueChangedCallback(evt => applyChange("Change Sharpen Edges",
                () => layer.edges = (SharpenLayerBehaviour.EdgeMode)evt.newValue));
            root.Add(edges);
        }
    }
}
