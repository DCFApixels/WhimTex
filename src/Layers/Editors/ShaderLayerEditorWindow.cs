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
            var code = new ShaderCodeField(document, layer, () => Current()?.draftCode ?? "", value =>
            {
                var current = Current(); if (current == null) return;
                current.draftCode = value; document.MarkChanged();
            }, SourceEditable);
            var program = new ShaderProgramView(code, new ShaderProgramView.Actions
            {
                changeMode = mode => apply("Change Shader Mode", () =>
                {
                    var current = Current(); if (current == null) return;
                    current.mode = mode;
                    current.InvalidateGraph();
                }),
                editGraph = () => ShaderGraphWindow.Open(Current(), document),
                compile = () => apply("Compile Shader Code", () => Current()?.CompileCode(document)),
                openCode = () => { if (SourceEditable()) ShaderFXExternalCode.OpenInUnityEditor(document, layerId); },
                openInVsCode = () => { if (SourceEditable()) ShaderFXExternalCode.OpenInVsCode(document, layerId); },
                choosePreset = anchor =>
                {
                    if (!Editable()) return;
                    new ShaderFXAddDropdown(entry => apply("Assign Shader Preset", () =>
                    {
                        var current = Current();
                        if (current == null || (ShaderPresetLink.IsLinked(current.program) &&
                            entry.id == current.program.presetId && entry.version == current.program.presetVersion)) return;
                        if (!ShaderPresetLink.ApplyLayer(current, document, entry, out string error))
                            EditorUtility.DisplayDialog("Shader Preset", error, "OK");
                    }), null, title: "Shader Presets").ShowAlignedRight(anchor);
                },
                savePreset = () =>
                {
                    if (!Editable()) return;
                    var current = Current();
                    ShaderProgramView.SavePreset(current.program, current.layerName,
                        (path, overwrite) => ShaderPresetWriter.Save(path, current.program, document, overwrite, current.CodeSourcePath(document)));
                },
                embed = () => apply("Embed Shader Preset", () => ShaderPresetLink.DetachLayer(Current())),
                clearPreset = () => apply("Clear Shader Preset", () =>
                {
                    var current = Current();
                    if (current == null || !ShaderPresetLink.IsLinked(current.program)) return;
                    current.ReleaseTransientResources();
                    current.program = new ShaderProgram { mode = current.mode };
                })
            }, effectContext: false);
            root.Add(program);
            var source = new ShaderGraphParameterViewSource(document, layerId);
            var view = new ShaderParameterView(source, document: document, layer: layer.Owner);
            root.RegisterCallback<DetachFromPanelEvent>(_ => source.ReleasePreview());
            root.Add(view);
            void Refresh()
            {
                var current = Current(); if (current == null) return;
                ShaderPresetLink.EnsureLayer(current, document);
                inputMode.SetValueWithoutNotify(inputMode.choices[(int)current.inputMode]);
                inputMode.SetEnabled(Editable());
                inputTexture.SetEnabled(Editable()); inputLayer.SetEnabled(Editable());
                inputTexture.SetValueWithoutNotify(current.inputTexture);
                inputLayer.SetValueWithoutNotify(document.FindLayer(current.inputLayerId));
                inputTexture.EnableInClassList("whimtex-hidden", current.inputMode != ShaderLayerInputMode.Texture);
                inputLayer.EnableInClassList("whimtex-hidden", current.inputMode != ShaderLayerInputMode.Layer);
                program.Refresh(current.program, current.CompilationStatus, Editable(),
                    current.mode == ShaderProgramMode.Code && current.draftCode != current.workingCode,
                    ShaderPresetLink.LayerUsingFallback(current), ShaderPresetLink.LayerSourcePath(current), ShaderPresetLink.LayerError(current));
                source.RefreshParameters(); view.Refresh();
            }
            RefreshInputChoices(); bindings.Add(Refresh); Refresh();
        }
    }
}
