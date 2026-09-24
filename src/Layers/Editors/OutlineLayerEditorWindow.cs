using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class OutlineLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(OutlineLayerBehaviour);

        public static void Open(OutlineLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<OutlineLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (OutlineLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings, AddEffectTarget);
        }

        internal static void BuildFields(
            VisualElement root, OutlineLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings,
            Action<VisualElement, TargetedLayerBehaviour> addEffectTarget)
        {
            addEffectTarget(root, layer);

            EnumField metric = WhimTexUI.ConfigureField(new EnumField("Distance Algorithm", layer.metric));
            metric.tooltip = "Euclidean Antialiased follows the 50% alpha contour, including soft edges. Euclidean Exact uses a hard silhouette.";
            bindings.Track(metric, () => (Enum)layer.metric);
            metric.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Algorithm",
                () => layer.metric = (DistanceMetric)evt.newValue));
            root.Add(metric);

            EnumField sourceChannel = WhimTexUI.ConfigureField(new EnumField("Source Channel", layer.sourceChannel));
            sourceChannel.tooltip = "Selects the channel used to detect the outline. Alpha preserves the traditional silhouette behavior.";
            bindings.Track(sourceChannel, () => (Enum)layer.sourceChannel);
            sourceChannel.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Source Channel",
                () => layer.sourceChannel = (OutlineLayerBehaviour.SourceChannel)evt.newValue));
            root.Add(sourceChannel);

            ColorField color = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField("Color"), bindings, () => layer.outlineColor));
            color.RegisterValueChangedCallback(evt =>
                applyChange("Change Outline Color", () => layer.outlineColor = evt.newValue));
            root.Add(color);

            FloatField width = WhimTexUI.ConfigureField(new FloatField("Width (px)"));
            width.SetValueWithoutNotify(layer.outlineWidth);
            bindings.Track(width, () => layer.outlineWidth);
            width.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Width",
                () => layer.outlineWidth = Mathf.Max(0f, evt.newValue)));
            root.Add(width);

            FloatField softness = WhimTexUI.ConfigureField(new FloatField("Softness (px)"));
            softness.tooltip = "Softens both edges of the outline. Zero keeps a crisp, antialiased edge; Width controls the band independently.";
            softness.SetValueWithoutNotify(layer.outlineSoftness);
            bindings.Track(softness, () => layer.outlineSoftness);
            softness.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Softness",
                () => layer.outlineSoftness = Mathf.Max(0f, evt.newValue)));
            root.Add(softness);

            EnumField position = WhimTexUI.ConfigureField(new EnumField("Position", layer.outlinePosition));
            bindings.Track(position, () => (Enum)layer.outlinePosition);
            position.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Position",
                () => layer.outlinePosition = (OutlineLayerBehaviour.OutlinePosition)evt.newValue));
            root.Add(position);

            FloatField offset = WhimTexUI.ConfigureField(new FloatField("Offset (px)"));
            offset.tooltip = "Moves the outline without changing its width. Negative moves inward; positive moves outward.";
            offset.SetValueWithoutNotify(layer.outlineOffset);
            bindings.Track(offset, () => layer.outlineOffset);
            offset.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Offset", () => layer.outlineOffset = evt.newValue));
            root.Add(offset);

            Toggle fill = WhimTexUI.ConfigureField(new Toggle("Fill Center"));
            fill.SetValueWithoutNotify(layer.fillCenter);
            bindings.Track(fill, () => layer.fillCenter);
            fill.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Fill", () => layer.fillCenter = evt.newValue));
            fill.tooltip = "Fills the inside of the outline. Place this effect below its source to use it as a backing silhouette.";
            root.Add(fill);

            ColorField fillColor = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new ColorField("Fill Color"), bindings, () => layer.fillColor));
            fillColor.RegisterValueChangedCallback(evt => applyChange(
                "Change Outline Fill Color", () => layer.fillColor = evt.newValue));
            fillColor.SetEnabled(layer.fillCenter);
            bindings.Add(() => fillColor.SetEnabled(layer.fillCenter));
            root.Add(fillColor);
        }
    }
}
