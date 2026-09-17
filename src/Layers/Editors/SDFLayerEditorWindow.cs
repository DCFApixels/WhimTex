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

            var offset = WhimTexUI.ConfigureField(new Vector2Field("Source Offset (px)"));
            offset.SetValueWithoutNotify(layer.sourceOffset);
            bindings.Track(offset, () => layer.sourceOffset);
            offset.RegisterValueChangedCallback(e => applyChange("Change SDF Source Offset", () =>
                layer.sourceOffset = new Vector2(Mathf.Clamp(e.newValue.x, -16384, 16384), Mathf.Clamp(e.newValue.y, -16384, 16384))));
            root.Add(offset);
            var edges = WhimTexUI.ConfigureField(new EnumField("Source Edges", layer.sourceEdges));
            bindings.Track(edges, () => (Enum)layer.sourceEdges);
            edges.RegisterValueChangedCallback(e => applyChange("Change SDF Source Edges", () => layer.sourceEdges = (SDFLayerBehaviour.SourceEdges)e.newValue));
            root.Add(edges);

            FloatField DistanceField(string label, Func<float> get, Action<float> set, float min)
            {
                var field = WhimTexUI.ConfigureField(new FloatField(label));
                field.SetValueWithoutNotify(get());
                bindings.Track(field, get);
                field.RegisterValueChangedCallback(e => applyChange("Change SDF " + label, () => set(Mathf.Clamp(e.newValue, min, 16384))));
                root.Add(field);
                return field;
            }
            DistanceField("Contour Offset (px)", () => layer.contourOffset, v => layer.contourOffset = v, -16384);

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
            var inside = DistanceField("Inside Distance (px)", () => layer.insideDistance, v => layer.insideDistance = v, 0);
            var outside = DistanceField("Outside Distance (px)", () => layer.outsideDistance, v => layer.outsideDistance = v, 0);
            inside.tooltip = outside.tooltip = "Signed mode only. 0 uses Max Distance, including its automatic range.";
            void RefreshRanges()
            {
                bool signed = layer.distancePosition == SDFLayerBehaviour.DistancePosition.Signed;
                inside.SetEnabled(signed); outside.SetEnabled(signed);
            }
            bindings.Add(RefreshRanges);
            distancePosition.RegisterValueChangedCallback(_ => RefreshRanges());
            RefreshRanges();
            var profile = WhimTexUI.ConfigureField(new CurveField("Profile"));
            profile.tooltip = "Remaps normalized distance before the gradient. Signed uses separate Inside/Outside distances; zero distance settings use Max Distance (or auto).";
            profile.SetValueWithoutNotify(layer.profile ?? WhimTexCurveTexture.Default());
            bindings.Track(profile, () => layer.profile ?? WhimTexCurveTexture.Default());
            profile.RegisterValueChangedCallback(e => applyChange("Change SDF Profile", () => layer.profile = WhimTexCurveTexture.Copy(e.newValue)));
            root.Add(profile);

            Toggle inverted = WhimTexUI.ConfigureField(new Toggle("Inverted"));
            inverted.SetValueWithoutNotify(layer.inverted);
            bindings.Track(inverted, () => layer.inverted);
            inverted.RegisterValueChangedCallback(evt =>
                applyChange("Invert SDF", () => layer.inverted = evt.newValue));
            root.Add(inverted);

            WhimTexGradientValueField gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient"), bindings, () => layer.gradient));
            gradient.RegisterValueChangedCallback(evt =>
                applyChange("Change SDF Gradient", () => layer.gradient = GradientUtility.Create(evt.newValue)));
            root.Add(gradient);
        }
    }
}
