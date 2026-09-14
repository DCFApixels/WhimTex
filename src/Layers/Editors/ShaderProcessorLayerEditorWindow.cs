using System;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class ShaderProcessorLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ShaderProcessorLayerBehaviour);
        protected override string PreviewTitle => "Preview (processed lower layers)";
        public static void Open(ShaderProcessorLayerBehaviour layer, TextureCompositor compositor) =>
            OpenPropertiesWindow<ShaderProcessorLayerEditorWindow>(layer, compositor);
        protected override void BuildSettings(VisualElement root, Layer layer)
        {
            WhimTexUI.AddHelpBox(root, "Processes the composited layers below. Add or edit its effects in FX.", HelpBoxMessageType.Info);
        }
    }
}
