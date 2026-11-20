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
            var classic = WhimTexUI.ConfigureField(new Toggle("Classic Liao") { name = "classicLiao", value = morph.classicLiao });
            toolkitInspectorBindings.Track(classic, () => morph.classicLiao);
            classic.RegisterValueChangedCallback(evt => apply("Change Classic Liao", () => morph.classicLiao = evt.newValue));
            var geometry = WhimTexUI.ConfigureField(new Toggle("Preserve Geometry") { name = "preserveGeometry", value = morph.preserveGeometry });
            toolkitInspectorBindings.Track(geometry, () => morph.preserveGeometry);
            geometry.RegisterValueChangedCallback(evt => apply("Change Preserve Geometry", () => morph.preserveGeometry = evt.newValue));
            var preserveStationaryDetails = WhimTexUI.ConfigureField(new Toggle("Preserve Stationary Details") { name = "preserveStationaryDetails", value = morph.preserveStationaryDetails });
            toolkitInspectorBindings.Track(preserveStationaryDetails, () => morph.preserveStationaryDetails);
            preserveStationaryDetails.RegisterValueChangedCallback(evt => apply("Change Preserve Stationary Details", () => morph.preserveStationaryDetails = evt.newValue));
            var visibility = WhimTexUI.ConfigureField(new Toggle("Visibility Mask") { name = "visibilityMask", value = morph.visibilityMask });
            toolkitInspectorBindings.Track(visibility, () => morph.visibilityMask);
            visibility.RegisterValueChangedCallback(evt => apply("Change Visibility Mask", () => morph.visibilityMask = evt.newValue));
            properties.Add(classic);
            var matching = WhimTexUI.ConfigureField(new EnumField("Matching", morph.matching) { name = "morphMatching" });
            toolkitInspectorBindings.Track(matching, () => (Enum)morph.matching);
            matching.RegisterValueChangedCallback(evt => apply("Change Morph Matching", () => morph.matching = (MorphMatching)evt.newValue));
            properties.Add(matching);
            properties.Add(visibility);
            var maskBlend = WhimTexUI.ConfigureField(new EnumField("Mask Blend", morph.maskBlend) { name = "maskBlend" });
            toolkitInspectorBindings.Track(maskBlend, () => (Enum)morph.maskBlend);
            maskBlend.RegisterValueChangedCallback(evt => apply("Change Morph Mask Blend", () => morph.maskBlend = (MorphMaskBlend)evt.newValue));
            properties.Add(maskBlend);
            properties.Add(geometry);
            properties.Add(preserveStationaryDetails);
            var interpolation = WhimTexUI.ConfigureField(new EnumField("Interpolation", morph.interpolation) { name = "morphInterpolation" });
            toolkitInspectorBindings.Track(interpolation, () => (Enum)morph.interpolation);
            interpolation.RegisterValueChangedCallback(evt =>
                apply("Change Morph Interpolation", () => morph.interpolation = (MorphInterpolation)evt.newValue));
            void RefreshVisibility()
            {
                var display = morph.mode == MorphMode.Raster ? DisplayStyle.Flex : DisplayStyle.None;
                maskBlend.style.display = display;
                maskBlend.SetEnabled(morph.visibilityMask && !morph.classicLiao);
                visibility.SetEnabled(!morph.classicLiao);
                matching.SetEnabled(!morph.classicLiao);
                matching.style.display = display;
                visibility.style.display = interpolation.style.display = classic.style.display = geometry.style.display = preserveStationaryDetails.style.display = display;
            }
            toolkitInspectorBindings.Add(RefreshVisibility);
            properties.Add(interpolation);
            properties.Add(new Button(() => MorphGroupEditorWindow.Open(document, layer))
                { name = "editMorph", text = "Edit Morph" });
        }
    }
}
