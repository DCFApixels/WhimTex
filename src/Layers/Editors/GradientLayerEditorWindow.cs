using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class GradientLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(GradientLayerBehaviour);

        public static void Open(GradientLayerBehaviour layer, TextureCompositor compositor)
        {
            OpenPropertiesWindow<GradientLayerEditorWindow>(layer, compositor);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (GradientLayerBehaviour)source, Compositor, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, GradientLayerBehaviour layer, TextureCompositor compositor,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            EnumField gradientType = WhimTexUI.ConfigureField(
                new EnumField("Gradient Type", layer.gradientType));
            bindings.Track(gradientType, () => (Enum)layer.gradientType);
            gradientType.RegisterValueChangedCallback(evt =>
            {
                applyChange(
                    "Change Gradient Type",
                    () => layer.gradientType = (GradientLayerBehaviour.GradientType)evt.newValue);
            });
            root.Add(gradientType);

            WhimTexGradientValueField gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient"), bindings, () => layer.gradient));
            gradient.RegisterValueChangedCallback(evt =>
                applyChange("Change Gradient", () => layer.gradient = GradientUtility.Create(evt.newValue)));
            root.Add(gradient);

            Vector2Field center = WhimTexUI.ConfigureField(new Vector2Field("Center"));
            bindings.Track(center, () => layer.center);
            center.RegisterValueChangedCallback(evt =>
                applyChange("Change Gradient Center", () => layer.center = evt.newValue));
            root.Add(center);

            FloatField radius = WhimTexUI.ConfigureField(new FloatField("Radius"));
            bindings.Track(radius, () => layer.radius);
            radius.RegisterValueChangedCallback(evt =>
                applyChange("Change Gradient Radius", () => layer.radius = Mathf.Max(0f, evt.newValue)));
            root.Add(radius);

            VisualElement circularSettings = new VisualElement();
            FloatField repetitions = WhimTexUI.ConfigureField(new FloatField("Repetitions"));
            bindings.Track(repetitions, () => layer.circularRepetitions);
            repetitions.RegisterValueChangedCallback(evt => applyChange(
                "Change Gradient Repetitions",
                () => layer.circularRepetitions = Mathf.Max(float.Epsilon, evt.newValue)));
            circularSettings.Add(repetitions);

            EnumField wrapMode = WhimTexUI.ConfigureField(
                new EnumField("Wrap Mode", layer.circularWrapMode));
            bindings.Track(wrapMode, () => (Enum)layer.circularWrapMode);
            wrapMode.RegisterValueChangedCallback(evt => applyChange(
                "Change Gradient Wrap Mode",
                () => layer.circularWrapMode = (GradientLayerBehaviour.WrapMode)evt.newValue));
            circularSettings.Add(wrapMode);
            root.Add(circularSettings);
            bindings.Add(() =>
            {
                bool radial = layer.gradientType == GradientLayerBehaviour.GradientType.Radial ||
                              layer.gradientType == GradientLayerBehaviour.GradientType.Diamond ||
                              layer.gradientType == GradientLayerBehaviour.GradientType.Square;
                bool circular = layer.gradientType == GradientLayerBehaviour.GradientType.Circular;
                center.style.display = radial || circular ? DisplayStyle.Flex : DisplayStyle.None;
                radius.style.display = radial ? DisplayStyle.Flex : DisplayStyle.None;
                circularSettings.style.display = circular ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }
    }
}
