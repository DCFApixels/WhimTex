using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    [CustomPropertyDrawer(typeof(ShaderTransform))]
    internal sealed class ShaderTransformDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout { text = property.displayName, value = true };
            var effect = property.serializedObject.targetObject as ShaderFX;
            var document = effect != null ? WhimTexWindow.FindFXTransformDocument(effect) : null;
            UnityEngine.Vector2 Dimensions() => document != null ? new UnityEngine.Vector2(document.width, document.height) : UnityEngine.Vector2.one;
            var position = new Vector2Field("Position");
            var size = new Vector2Field("Size");
            var rotation = new DoubleField("Rotation");
            root.Add(position); root.Add(size); root.Add(rotation);
            void Refresh(SerializedProperty p)
            {
                var transform = (ShaderTransform)p.boxedValue;
                transform.GetDisplay(Dimensions(), out var location, out var scale, out var angle);
                position.SetValueWithoutNotify(location); size.SetValueWithoutNotify(scale); rotation.SetValueWithoutNotify(angle);
            }
            void Change(System.Func<ShaderTransform, ShaderTransform> edit)
            {
                if (effect != null && WhimTexApi.IsShaderFXContentLocked(effect)) return;
                property.serializedObject.Update();
                property.boxedValue = edit((ShaderTransform)property.boxedValue);
                property.serializedObject.ApplyModifiedProperties();
                effect?.NotifyValuesChanged();
                Refresh(property);
            }
            position.RegisterValueChangedCallback(e => Change(t => { t.EditPosition(e.newValue, Dimensions()); return t; }));
            size.RegisterValueChangedCallback(e => Change(t => {
                t.EditSize(new Double2(ShaderTransform.SafeSize(e.newValue.x), ShaderTransform.SafeSize(e.newValue.y)), Dimensions()); return t; }));
            rotation.RegisterValueChangedCallback(e => Change(t => { t.EditRotation(e.newValue, Dimensions()); return t; }));
            root.Add(new Button(() => Change(_ => ShaderTransform.Default)) { text = "Reset Transform" });
            root.TrackPropertyValue(property, Refresh);
            Refresh(property);
            return root;
        }
    }

    [CustomEditor(typeof(ShaderFX))]
    public sealed class ShaderFXEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            return BuildView((ShaderFX)target, serializedObject);
        }

        internal static VisualElement CreateInlineView(ShaderFX effect, WhimTexDocument document = null, Layer layer = null, int fxIndex = -1)
        {
            VisualElement host = new VisualElement();
            SerializedObject data = null;
            host.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != host || data != null || effect == null)
                    return;
                data = new SerializedObject(effect);
                host.Add(BuildView(effect, data, document, layer, fxIndex));
            });
            host.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target != host)
                    return;
                host.Unbind();
                host.Clear();
                data?.Dispose();
                data = null;
            });
            return host;
        }

        private static VisualElement BuildView(ShaderFX effect, SerializedObject serializedObject,
            WhimTexDocument document = null, Layer layer = null, int fxIndex = -1)
        {
            VisualElement root = new VisualElement { focusable = true };
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-shader-fx");
            var mode = new DropdownField("Mode", new System.Collections.Generic.List<string> { "Shader Graph", "Code" }, (int)effect.Mode);
            root.Add(mode);
            var graphButton = new Button(() => ShaderGraphWindow.Open(effect, layer, document)) { text = "Edit Graph" };
            root.Add(graphButton);
            ShaderCodeField code = new ShaderCodeField(effect);
            var codeFoldout = new Foldout { text = "Code", name = "shaderFXCodeFoldout", value = false,
                tooltip = "Edit HLSL and declarations here. Parameter values update live without recompiling." };
            var codeInfo = new ShaderCodeInfo(code);
            codeFoldout.Add(codeInfo.PixelLocalStatus);
            var presetSource = new ShaderFXPresetField(effect);
            root.Add(presetSource);
            codeFoldout.Add(code);
            root.Add(codeFoldout);
            var externalCodeButtons = new VisualElement();
            externalCodeButtons.AddToClassList("whimtex-layer-fx-toolbar");
            var openCode = new Button(() => ShaderFXExternalCode.OpenInUnityEditor(effect))
            {
                text = "Open Code",
                tooltip = "Open this document-local code file with the script editor selected in Unity Preferences. Saved external edits are synchronized into the FX draft."
            };
            var openInVsCode = new Button(() => ShaderFXExternalCode.OpenInVsCode(effect))
            {
                text = "Open in VS Code",
                tooltip = "Open with WhimTex syntax highlighting and directive diagnostics in an isolated editor profile."
            };
            openInVsCode.EnableInClassList("whimtex-shader-fx-hidden", !ShaderFXExternalCode.HasVsCode);
            System.Action refreshVsCodeAvailability = () =>
                openInVsCode.EnableInClassList("whimtex-shader-fx-hidden", !ShaderFXExternalCode.HasVsCode);
            root.RegisterCallback<AttachToPanelEvent>(_ => WhimTexUserSettings.Changed += refreshVsCodeAvailability);
            root.RegisterCallback<DetachFromPanelEvent>(_ => WhimTexUserSettings.Changed -= refreshVsCodeAvailability);
            externalCodeButtons.Add(openCode);
            externalCodeButtons.Add(openInVsCode);
            codeFoldout.Add(externalCodeButtons);
            var sourceButtons = new VisualElement();
            sourceButtons.AddToClassList("whimtex-layer-fx-toolbar");
            sourceButtons.Add(new Button(() => ShaderFXExternalCode.OpenPresetSource(effect.PresetSourcePath)) { text = "Open Preset Source" });
            var detach = new Button { text = "Embed Copy", tooltip = "Stop following the preset and edit its embedded copy." };
            sourceButtons.Add(detach);
            root.Add(sourceButtons);

            HelpBox status = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(status);
            Button apply = new Button { text = "Compile", name = "shaderFXCompile", tooltip = "Compile and use the current HLSL code and declarations; re-read included libraries. Does not bake the FX into the layer." };
            var codeActions = new VisualElement();
            codeActions.AddToClassList("whimtex-layer-fx-toolbar");
            codeActions.Add(apply);
            root.Add(codeActions);
            void SavePreset()
            {
                if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                try
                {
                    System.IO.Directory.CreateDirectory(ShaderPresetCatalog.Folder);
                    string path = EditorUtility.SaveFilePanel("Save Shader Preset", ShaderPresetCatalog.Folder, effect.name,
                        effect.Mode == ShaderProgramMode.Graph ? "whimtexshader" : "hlsl");
                    if (string.IsNullOrEmpty(path)) return;
                    bool overwrite = System.IO.File.Exists(path);
                    if (overwrite && !EditorUtility.DisplayDialog("Overwrite Shader Preset", "Replace this preset? A .bak copy will be kept.", "Overwrite", "Cancel")) return;
                    ShaderPresetWriter.Save(path, effect, overwrite);
                    status.messageType = HelpBoxMessageType.Info;
                    status.text = "Shader preset saved. Find it in + Preset.";
                    status.RemoveFromClassList("whimtex-shader-fx-hidden");
                }
                catch (System.Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
            }
            codeActions.Add(new Button(SavePreset) { text = "Save Preset…", tooltip = "Save the shader with current parameter values as defaults." });

            TextField diagnostics = new TextField
            {
                name = "shaderFXDiagnostics",
                multiline = true,
                isReadOnly = true,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            diagnostics.AddToClassList("whimtex-shader-fx-diagnostics");
            root.Add(diagnostics);
            var diagnosticPassDetails = new ShaderDiagnosticPassDetails(); root.Add(diagnosticPassDetails);
            // The shared source supplies HLSL declarations or synthesized graph controls.
            var declaredParameters = new ShaderParameterView(effect, false, document, layer, fxIndex);
            bool refreshingStatus = false;
            detach.clicked += () => { Undo.RecordObject(effect, "Embed FX Source"); effect.DetachCatalog(); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged(); RefreshStatus(); };
            apply.clicked += () =>
            {
                if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                if (effect.IsCatalogLinked) effect.ReloadCatalogSource(true);
                else effect.Compile();
                serializedObject.Update();
                RefreshStatus();
            };
            root.Add(declaredParameters);
            codeFoldout.Add(codeInfo);
            mode.RegisterValueChangedCallback(evt =>
            {
                if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
                Undo.RecordObject(effect, "Change Shader FX Mode");
                if (document != null) Undo.RecordObject(document, "Change Shader FX Mode");
                effect.SetMode(evt.newValue == "Code" ? ShaderProgramMode.Code : ShaderProgramMode.Graph);
                serializedObject.Update(); RefreshStatus();
            });

            void RefreshStatus()
            {
                if (effect == null || refreshingStatus)
                    return;
                refreshingStatus = true;
                try
                {
                    bool graphMode = effect.Mode == ShaderProgramMode.Graph;
                    mode.SetValueWithoutNotify(graphMode ? "Shader Graph" : "Code");
                    mode.SetEnabled(!WhimTexApi.IsShaderFXContentLocked(effect));
                    graphButton.EnableInClassList("whimtex-shader-fx-hidden", !graphMode);
                    graphButton.SetEnabled(document != null && layer != null);
                    codeFoldout.EnableInClassList("whimtex-shader-fx-hidden", graphMode);
                    code.SyncFromModel();
                    presetSource.Refresh();
                    code.SetEnabled(!effect.IsCatalogLinked);
                    externalCodeButtons.EnableInClassList("whimtex-shader-fx-hidden", effect.IsCatalogLinked);
                    sourceButtons.EnableInClassList("whimtex-shader-fx-hidden", !effect.IsCatalogLinked);
                    declaredParameters.EnableInClassList("whimtex-shader-fx-hidden", false);
                    declaredParameters.Refresh();
                    bool pendingChanges = effect.HasPendingChanges;
                    codeInfo.Refresh(effect, pendingChanges);
                    status.messageType = effect.DiagnosticSeverity == ShaderDiagnosticSeverity.Error ? HelpBoxMessageType.Error :
                        effect.DiagnosticSeverity == ShaderDiagnosticSeverity.Warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
                    status.EnableInClassList("whimtex-shader-fx-hidden", !effect.LastCompileFailed && !pendingChanges && effect.DiagnosticNotice == null);
                    status.text = effect.LastCompileFailed
                        ? effect.IsUnavailable ? "Compilation failed. No working version is available; this FX is skipped."
                            : "Compilation failed. Showing the last successfully compiled version."
                        : pendingChanges ? "Uncompiled shader changes. Click Compile when ready."
                        : effect.DiagnosticNotice != null ? "Compiled with warnings. This FX remains active."
                        : "Compiled. Values update without recompiling. Click Compile again after editing an included library.";
                    diagnostics.SetValueWithoutNotify(effect.Diagnostics);
                    diagnosticPassDetails.Refresh(effect.DiagnosticMessages);
                    diagnostics.EnableInClassList("whimtex-shader-fx-hidden",
                        !effect.LastCompileFailed && (string.IsNullOrWhiteSpace(effect.Diagnostics) || effect.Diagnostics == "Applied successfully."));
                    codeFoldout.text = pendingChanges ? "Code • uncompiled" : "Code";
                    apply.SetEnabled(effect != null);
                }
                finally { refreshingStatus = false; }
            }

            void ExternalCodeChanged(ShaderFX changed)
            {
                if (changed != effect) return;
                RefreshStatus();
            }
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
            root.Bind(serializedObject);
            void RefreshLock() => root.SetEnabled(!WhimTexApi.IsShaderFXContentLocked(effect));
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                WhimTexApi.LiveEditLocksChanged -= RefreshLock;
                WhimTexApi.LiveEditLocksChanged += RefreshLock;
                ShaderFXExternalCode.CodeChanged -= ExternalCodeChanged;
                ShaderFXExternalCode.CodeChanged += ExternalCodeChanged;
                ShaderFX.Changed -= ExternalCodeChanged;
                ShaderFX.Changed += ExternalCodeChanged;
                Undo.undoRedoPerformed -= RefreshStatus;
                Undo.undoRedoPerformed += RefreshStatus;
                RefreshLock();
                RefreshStatus();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                WhimTexApi.LiveEditLocksChanged -= RefreshLock;
                ShaderFXExternalCode.CodeChanged -= ExternalCodeChanged;
                ShaderFX.Changed -= ExternalCodeChanged;
                Undo.undoRedoPerformed -= RefreshStatus;
            });
            RefreshLock();
            RefreshStatus();
            return root;
        }
    }

}
