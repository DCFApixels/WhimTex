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
            properties.Add(method);
            var classic = WhimTexUI.ConfigureField(new Toggle("Classic Liao") { name = "classicLiao", value = morph.classicLiao });
            toolkitInspectorBindings.Track(classic, () => morph.classicLiao);
            classic.RegisterValueChangedCallback(evt => apply("Change Classic Liao", () => morph.classicLiao = evt.newValue));
            var geometry = WhimTexUI.ConfigureField(new Toggle("Preserve Geometry") { name = "preserveGeometry", value = morph.preserveGeometry });
            toolkitInspectorBindings.Track(geometry, () => morph.preserveGeometry);
            geometry.RegisterValueChangedCallback(evt => apply("Change Preserve Geometry", () => morph.preserveGeometry = evt.newValue));
            var preserveStationaryDetails = WhimTexUI.ConfigureField(new Toggle("Preserve Stationary Details") { name = "preserveStationaryDetails", value = morph.preserveStationaryDetails });
            toolkitInspectorBindings.Track(preserveStationaryDetails, () => morph.preserveStationaryDetails);
            preserveStationaryDetails.RegisterValueChangedCallback(evt => apply("Change Preserve Stationary Details", () => morph.preserveStationaryDetails = evt.newValue));
            properties.Add(classic);
            properties.Add(geometry);
            properties.Add(preserveStationaryDetails);
            var interpolation = WhimTexUI.ConfigureField(new EnumField("Interpolation", morph.interpolation) { name = "morphInterpolation" });
            toolkitInspectorBindings.Track(interpolation, () => (Enum)morph.interpolation);
            interpolation.RegisterValueChangedCallback(evt =>
                apply("Change Morph Interpolation", () => morph.interpolation = (MorphInterpolation)evt.newValue));
            void RefreshVisibility()
            {
                bool raster = morph.mode == MorphMode.Raster;
                method.style.display = interpolation.style.display = raster ? DisplayStyle.Flex : DisplayStyle.None;
                var liao = raster && morph.rasterMethod == MorphRasterMethod.Liao ? DisplayStyle.Flex : DisplayStyle.None;
                classic.style.display = geometry.style.display = preserveStationaryDetails.style.display = liao;
            }
            toolkitInspectorBindings.Add(RefreshVisibility);
            properties.Add(interpolation);
            properties.Add(new Button(() => MorphGroupEditorWindow.Open(document, layer))
                { name = "editMorph", text = "Edit Morph" });
        }
    }
}
