using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
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
            bool SourceEditable() => Editable() && !ShaderPresetLink.IsLinked(Current()?.program);
            var presets = new VisualElement();
            presets.AddToClassList("whimtex-layer-fx-toolbar");
            presets.Add(ShaderFXAddDropdown.CreatePresetButton(entry =>
            {
                if (!Editable()) return;
                apply("Assign Shader Preset", () =>
                {
                    if (!ShaderPresetLink.ApplyLayer(Current(), document, entry, out string error))
                        EditorUtility.DisplayDialog("Shader Preset", error, "OK");
                });
            }));
            presets.Add(new Button(() =>
            {
                if (!Editable()) return;
                var current = Current();
                string path = EditorUtility.SaveFilePanel("Save Shader Preset", ShaderPresetCatalog.Folder, current.layerName, ShaderPresetIO.ExtensionFor(current.program));
                if (string.IsNullOrEmpty(path)) return;
                bool overwrite = System.IO.File.Exists(path);
                if (overwrite && !EditorUtility.DisplayDialog("Overwrite Shader Preset", "Replace this preset? A .bak copy will be kept.", "Overwrite", "Cancel")) return;
                try { ShaderPresetWriter.Save(path, current.program, document, overwrite, current.CodeSourcePath(document)); }
                catch (Exception error) { EditorUtility.DisplayDialog("Shader Preset", error.Message, "OK"); }
            }) { text = "Save Preset…" });
            var detach = new Button(() =>
            {
                if (Editable()) apply("Embed Shader Preset", () => ShaderPresetLink.DetachLayer(Current()));
            }) { text = "Embed Copy" };
            presets.Add(detach); root.Add(presets);
            var presetStatus = new Label(); root.Add(presetStatus);
            var inputMode = new DropdownField("Input", new List<string> { "None", "Texture", "Layer", "Composite Below" }, (int)layer.inputMode);
            var inputTexture = new ObjectField("Texture") { objectType = typeof(Texture2D), allowSceneObjects = false };
            static string InputName(Layer value) => value == null ? "None" : value.layerName + (value.IsGroup ? " [Group]" : "");
            var inputLayer = new PopupField<Layer>("Layer", new List<Layer> { null }, 0, InputName, InputName);
            var inputWarning = new HelpBox("This input would create a circular dependency.", HelpBoxMessageType.Warning);
            inputWarning.AddToClassList("whimtex-hidden");
            void ChangeInput(Action<ShaderLayerBehaviour> edit, bool textureChanged = false)
            {
                if (!Editable()) return;
                apply("Change Shader Input", () =>
                {
                    var current = Current(); if (current == null) return;
                    if (textureChanged) WhimTexDocumentJson.RestoreMissingAssets(document);
                    var previousMode = current.inputMode; var previousTexture = current.inputTexture; var previousId = current.inputLayerId;
                    edit(current);
                    bool valid = document.IsUsableShaderInput(current.Owner, current, stored: true);
                    if (!valid) { current.inputMode = previousMode; current.inputTexture = previousTexture; current.inputLayerId = previousId; }
                    if (textureChanged)
                    {
                        if (valid) WhimTexDocumentJson.SetShaderInputTexture(current, current.inputTexture);
                        WhimTexDocumentJson.CaptureMissingAssets(document);
                    }
                    inputWarning.EnableInClassList("whimtex-hidden", valid);
                });
            }
            inputMode.RegisterValueChangedCallback(e => ChangeInput(current => current.inputMode = (ShaderLayerInputMode)inputMode.choices.IndexOf(e.newValue)));
            inputTexture.RegisterValueChangedCallback(e => ChangeInput(current => current.inputTexture = e.newValue as Texture2D, textureChanged: true));
            inputLayer.RegisterValueChangedCallback(e => ChangeInput(current => current.inputLayerId = e.newValue?.Id));
            void RefreshInputChoices()
            {
                var current = Current(); if (current == null) return;
                var choices = new List<Layer> { null };
                void Visit(List<Layer> layers)
                {
                    foreach (var item in layers)
                    {
                        if (item == null) continue;
                        if (document.IsUsableStoredShaderTexture(current.Owner, item.Id)) choices.Add(item);
                        if (item.IsGroup) Visit(item.children);
                    }
                }
                Visit(document.layers);
                inputLayer.choices = choices;
                inputLayer.SetValueWithoutNotify(document.FindLayer(current.inputLayerId));
            }
            inputLayer.RegisterCallback<PointerDownEvent>(_ => RefreshInputChoices(), TrickleDown.TrickleDown);
            root.Add(inputMode); root.Add(inputTexture); root.Add(inputLayer); root.Add(inputWarning);
            var mode = new DropdownField("Mode", new List<string> { "Shader Graph", "Code" }, (int)layer.mode);
            mode.RegisterValueChangedCallback(e => apply("Change Shader Mode", () =>
            {
                var current = Current(); if (current == null) return;
                current.mode = e.newValue == "Code" ? ShaderProgramMode.Code : ShaderProgramMode.Graph;
                current.InvalidateGraph();
            }));
            root.Add(mode);
            var graphButton = WhimTexUI.CreateToolbarButton("Edit Graph", () => ShaderGraphWindow.Open(Current(), document));
            root.Add(graphButton);
            var codeArea = new VisualElement();
            codeArea.AddToClassList("whimtex-shader-fx");
            var code = new ShaderCodeField(document, layer, () => Current()?.draftCode ?? "", value =>
            {
                var current = Current(); if (current == null) return;
                current.draftCode = value; document.MarkChanged();
            }, SourceEditable);
            codeArea.Add(code);
            var compile = WhimTexUI.CreateToolbarButton("Compile", () => apply("Compile Shader Code", () => Current()?.CompileCode(document)));
            codeArea.Add(compile);
            var codeStatus = new Label(); codeArea.Add(codeStatus);
            root.Add(codeArea);
            var source = new ShaderGraphParameterViewSource(document, layerId);
            var view = new ShaderParameterView(source, document: document, layer: layer.Owner);
            root.RegisterCallback<DetachFromPanelEvent>(_ => source.ReleasePreview());
            root.Add(view);
            var diagnostics = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            root.Add(diagnostics);
            var diagnosticPassDetails = new ShaderDiagnosticPassDetails(); root.Add(diagnosticPassDetails);
            void Refresh()
            {
                var current = Current(); if (current == null) return;
                ShaderPresetLink.EnsureLayer(current, document);
                bool linked = ShaderPresetLink.IsLinked(current.program);
                detach.EnableInClassList("whimtex-hidden", !linked);
                presetStatus.text = ShaderPresetLink.LayerError(current) ?? ShaderPresetLink.LayerSourcePath(current) ?? string.Empty;
                presetStatus.EnableInClassList("whimtex-hidden", !linked);
                bool manual = current.mode == ShaderProgramMode.Code;
                inputMode.SetValueWithoutNotify(inputMode.choices[(int)current.inputMode]);
                inputMode.SetEnabled(Editable());
                inputTexture.SetEnabled(Editable()); inputLayer.SetEnabled(Editable());
                inputTexture.SetValueWithoutNotify(current.inputTexture);
                inputLayer.SetValueWithoutNotify(document.FindLayer(current.inputLayerId));
                inputTexture.EnableInClassList("whimtex-hidden", current.inputMode != ShaderLayerInputMode.Texture);
                inputLayer.EnableInClassList("whimtex-hidden", current.inputMode != ShaderLayerInputMode.Layer);
                mode.SetValueWithoutNotify(manual ? "Code" : "Shader Graph");
                graphButton.EnableInClassList("whimtex-hidden", manual);
                codeArea.EnableInClassList("whimtex-hidden", !manual);
                code.SyncFromModel();
                codeStatus.text = current.draftCode != current.workingCode ? "Changes pending compilation." : "";
                codeStatus.EnableInClassList("whimtex-hidden", codeStatus.text.Length == 0);
                source.RefreshParameters(); view.Refresh();
                var compilation = current.CompilationStatus;
                diagnostics.text = compilation.HasError || compilation.Messages.Count > 0 ? current.Diagnostics : string.Empty;
                diagnostics.messageType = compilation.Severity == ShaderGraphMessageLevel.Error ? HelpBoxMessageType.Error :
                    compilation.Severity == ShaderGraphMessageLevel.Warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
                diagnostics.EnableInClassList("whimtex-hidden", diagnostics.text.Length == 0);
                diagnosticPassDetails.Refresh(compilation.DiagnosticMessages);
            }
            RefreshInputChoices(); bindings.Add(Refresh); Refresh();
        }
    }
}
