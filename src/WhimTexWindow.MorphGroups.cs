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
            mode.RegisterValueChangedCallback(evt =>
                method.style.display = (MorphMode)evt.newValue == MorphMode.Raster ? DisplayStyle.Flex : DisplayStyle.None);
            properties.Add(method);
            properties.Add(new Button(() => MorphGroupEditorWindow.Open(document, layer))
                { name = "editMorph", text = "Edit Morph" });
        }
    }
}
