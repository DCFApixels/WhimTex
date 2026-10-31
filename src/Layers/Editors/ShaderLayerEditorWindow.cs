using System;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class ShaderLayerEditorWindow : LayerEditorWindowBase
    {
        protected override Type EditedLayerType => typeof(ShaderLayerBehaviour);
        public static void Open(ShaderLayerBehaviour layer, WhimTexDocument document) =>
            OpenPropertiesWindow<ShaderLayerEditorWindow>(layer, document);
        protected override void BuildSettings(VisualElement root, Layer layer) =>
            BuildFields(root, (ShaderLayerBehaviour)layer.Behaviour, Document, ApplyLayerChange, SettingsBindings);

        internal static void BuildFields(VisualElement root, ShaderLayerBehaviour layer, WhimTexDocument document,
            Action<string, Action> apply, WhimTexUI.ValueBindings bindings)
        {
            root.Add(WhimTexUI.CreateToolbarButton("Edit Graph", () => ShaderGraphWindow.Open(layer, document)));
            var source = new ShaderGraphParameterViewSource(document, layer.Id);
            var view = new ShaderFXParameterView(source);
            root.Add(view);
            var diagnostics = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            root.Add(diagnostics);
            void Refresh()
            {
                source.RefreshParameters(); view.Refresh();
                diagnostics.text = source.Behaviour?.HasCompileError == true ? source.Behaviour.Diagnostics : string.Empty;
                diagnostics.EnableInClassList("whimtex-hidden", diagnostics.text.Length == 0);
            }
            bindings.Add(Refresh); Refresh();
        }
    }
}
