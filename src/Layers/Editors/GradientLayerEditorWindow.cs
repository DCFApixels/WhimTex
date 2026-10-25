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

        public static void Open(GradientLayerBehaviour layer, WhimTexDocument activeDocument)
        {
            OpenPropertiesWindow<GradientLayerEditorWindow>(layer, activeDocument);
        }

        protected override void BuildSettings(VisualElement root, Layer source)
        {
            BuildFields(root, (GradientLayerBehaviour)source, Document, ApplyLayerChange, SettingsBindings);
        }

        internal static void BuildFields(
            VisualElement root, GradientLayerBehaviour layer, WhimTexDocument activeDocument,
            Action<string, Action> applyChange, WhimTexUI.ValueBindings bindings)
        {

            var gradientType = WhimTexUI.ConfigureField(new GradientTypeField("Gradient Type") { name = "gradientLayerType" });
            bindings.Track(gradientType, () => layer.gradientType);
            gradientType.RegisterValueChangedCallback(evt =>
            {
                applyChange(
                    "Change Gradient Type",
                    () =>
                    {
                        layer.gradientType = evt.newValue;
                        if (evt.newValue == GradientLayerBehaviour.GradientType.Mesh && layer.mesh == null)
                            layer.mesh = new MeshGradient();
                    });
            });
            root.Add(gradientType);

            WhimTexGradientValueField gradient = WhimTexUI.ConfigureField(WhimTexColorInputs.Bind(new WhimTexGradientValueField("Gradient"), bindings, () => layer.gradient));
            gradient.RegisterValueChangedCallback(evt =>
                applyChange("Change Gradient", () => layer.gradient = GradientUtility.Create(evt.newValue)));
            root.Add(gradient);
            var freeformSettings = new VisualElement();
            FreeformGradientEditor.Build(freeformSettings, layer, activeDocument, applyChange, bindings);
            root.Add(freeformSettings);
            var meshSettings = new VisualElement();
            MeshGradientEditor.Build(meshSettings, layer, activeDocument, applyChange, bindings);
            root.Add(meshSettings);

            VisualElement angularSettings = new VisualElement();
            FloatField startAngle = WhimTexUI.ConfigureField(new FloatField("Start Angle"));
            bindings.Track(startAngle, () => layer.circularStartAngle);
            startAngle.RegisterValueChangedCallback(evt => applyChange(
                "Rotate Angular Gradient", () => layer.circularStartAngle = Mathf.Repeat(evt.newValue, 360f)));
            angularSettings.Add(startAngle);
            FloatField repetitions = WhimTexUI.ConfigureField(new FloatField("Repetitions"));
            bindings.Track(repetitions, () => layer.circularRepetitions);
            repetitions.RegisterValueChangedCallback(evt => applyChange(
                "Change Gradient Repetitions",
                () => layer.circularRepetitions = Mathf.Max(float.Epsilon, evt.newValue)));
            angularSettings.Add(repetitions);

            EnumField wrapMode = WhimTexUI.ConfigureField(
                new EnumField("Wrap Mode", layer.circularWrapMode));
            bindings.Track(wrapMode, () => (Enum)layer.circularWrapMode);
            wrapMode.RegisterValueChangedCallback(evt => applyChange(
                "Change Gradient Wrap Mode",
                () => layer.circularWrapMode = (GradientLayerBehaviour.WrapMode)evt.newValue));
            angularSettings.Add(wrapMode);
            root.Add(angularSettings);
            bindings.Add(() =>
            {
                bool angular = layer.gradientType == GradientLayerBehaviour.GradientType.Angular;
                gradient.style.display = layer.gradientType == GradientLayerBehaviour.GradientType.Freeform || layer.gradientType == GradientLayerBehaviour.GradientType.Mesh ? DisplayStyle.None : DisplayStyle.Flex;
                angularSettings.style.display = angular ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }
    }
}
