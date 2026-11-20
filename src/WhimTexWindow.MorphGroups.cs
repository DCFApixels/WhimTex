using System;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private void BuildMorphGroupProperties(VisualElement properties, Layer layer,
            MorphGroupLayerBehaviour morph, Action<string, Action> apply)
        {
            var document = activeDocument;
            var mode = WhimTexUI.ConfigureField(new EnumField("Mode", morph.mode) { name = "morphMode" });
            toolkitInspectorBindings.Track(mode, () => (Enum)morph.mode);
            mode.RegisterValueChangedCallback(evt =>
                apply("Change Morph Mode", () => morph.mode = (MorphMode)evt.newValue));
            properties.Add(mode);
            var method = WhimTexUI.ConfigureField(new EnumField("Raster Method", morph.rasterMethod) { name = "rasterMethod" });
            toolkitInspectorBindings.Track(method, () => (Enum)morph.rasterMethod);
            method.RegisterValueChangedCallback(evt =>
                apply("Change Raster Method", () => morph.rasterMethod = (MorphRasterMethod)evt.newValue));
            method.style.display = morph.mode == MorphMode.Raster ? DisplayStyle.Flex : DisplayStyle.None;
            properties.Add(method);
            var classic = WhimTexUI.ConfigureField(new Toggle("Classic Liao") { name = "classicLiao", value = morph.classicLiao });
            toolkitInspectorBindings.Track(classic, () => morph.classicLiao);
            classic.RegisterValueChangedCallback(evt => apply("Change Classic Liao", () => morph.classicLiao = evt.newValue));
            var geometry = WhimTexUI.ConfigureField(new Toggle("Preserve Geometry") { name = "preserveGeometry", value = morph.preserveGeometry });
            toolkitInspectorBindings.Track(geometry, () => morph.preserveGeometry);
            geometry.RegisterValueChangedCallback(evt => apply("Change Preserve Geometry", () => morph.preserveGeometry = evt.newValue));
            void ShowLiaoOptions() => geometry.style.display = classic.style.display = (MorphMode)mode.value == MorphMode.Raster &&
                (MorphRasterMethod)method.value == MorphRasterMethod.Liao ? DisplayStyle.Flex : DisplayStyle.None;
            ShowLiaoOptions();
            mode.RegisterValueChangedCallback(_ => ShowLiaoOptions());
            method.RegisterValueChangedCallback(_ => ShowLiaoOptions());
            properties.Add(classic);
            properties.Add(geometry);
            var interpolation = WhimTexUI.ConfigureField(new EnumField("Interpolation", morph.interpolation) { name = "morphInterpolation" });
            toolkitInspectorBindings.Track(interpolation, () => (Enum)morph.interpolation);
            interpolation.RegisterValueChangedCallback(evt =>
                apply("Change Morph Interpolation", () => morph.interpolation = (MorphInterpolation)evt.newValue));
            interpolation.style.display = method.style.display;
            mode.RegisterValueChangedCallback(evt =>
                interpolation.style.display = method.style.display = (MorphMode)evt.newValue == MorphMode.Raster ? DisplayStyle.Flex : DisplayStyle.None);
            properties.Add(interpolation);
            properties.Add(new Button(() => MorphGroupEditorWindow.Open(document, layer))
                { name = "editMorph", text = "Edit Morph" });
        }
    }
}
