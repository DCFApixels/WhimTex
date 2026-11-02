using System;
using System.Collections.Generic;
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
            string layerId = layer.Id;
            ShaderLayerBehaviour Current() => document.FindLayer(layerId)?.Behaviour as ShaderLayerBehaviour;
            bool Editable() => Current() is ShaderLayerBehaviour current && !WhimTexApi.IsLayerContentLocked(document, current.Owner);
            var mode = new DropdownField("Mode", new List<string> { "Shader Graph", "Code" }, (int)layer.mode);
            mode.RegisterValueChangedCallback(e => apply("Change Shader Mode", () =>
            {
                var current = Current(); if (current == null) return;
                current.mode = e.newValue == "Code" ? ShaderLayerMode.Code : ShaderLayerMode.Graph;
                current.InvalidateGraph();
            }));
            root.Add(mode);
            var graphButton = WhimTexUI.CreateToolbarButton("Edit Graph", () => ShaderGraphWindow.Open(Current(), document));
            root.Add(graphButton);
            var codeArea = new VisualElement();
            codeArea.AddToClassList("whimtex-shader-fx");
            var code = new ShaderFXCodeField(document, layer, () => Current()?.code ?? "", value =>
            {
                var current = Current(); if (current == null) return;
                current.code = value; document.MarkChanged();
            }, Editable);
            codeArea.Add(code);
            var compile = WhimTexUI.CreateToolbarButton("Compile", () => apply("Compile Shader Code", () => Current()?.CompileCode(document)));
            codeArea.Add(compile);
            var codeStatus = new Label(); codeArea.Add(codeStatus);
            root.Add(codeArea);
            var source = new ShaderGraphParameterViewSource(document, layerId);
            var view = new ShaderFXParameterView(source, document: document, layer: layer.Owner);
            root.RegisterCallback<DetachFromPanelEvent>(_ => source.ReleasePreview());
            root.Add(view);
            var diagnostics = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            root.Add(diagnostics);
            void Refresh()
            {
                var current = Current(); if (current == null) return;
                bool manual = current.mode == ShaderLayerMode.Code;
                mode.SetValueWithoutNotify(manual ? "Code" : "Shader Graph");
                graphButton.EnableInClassList("whimtex-hidden", manual);
                codeArea.EnableInClassList("whimtex-hidden", !manual);
                code.SyncFromModel();
                codeStatus.text = current.code != current.compiledCode ? "Changes pending compilation." : "";
                codeStatus.EnableInClassList("whimtex-hidden", codeStatus.text.Length == 0);
                source.RefreshParameters(); view.Refresh();
                diagnostics.text = current.HasCompileError ? current.Diagnostics : string.Empty;
                diagnostics.EnableInClassList("whimtex-hidden", diagnostics.text.Length == 0);
            }
            bindings.Add(Refresh); Refresh();
        }
    }
}
