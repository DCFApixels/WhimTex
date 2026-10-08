using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using ColorField = DCFApixels.WhimTex.WhimTexColorField;

namespace DCFApixels.WhimTex
{
    [CustomPropertyDrawer(typeof(ShaderFXTransform))]
    internal sealed class ShaderFXTransformDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout { text = property.displayName, value = true };
            var effect = property.serializedObject.targetObject as ShaderFX;
            var document = effect != null ? TextureCompositorWindow.FindFXTransformDocument(effect) : null;
            UnityEngine.Vector2 Dimensions() => document != null ? new UnityEngine.Vector2(document.width, document.height) : UnityEngine.Vector2.one;
            var position = new Vector2Field("Position");
            var size = new Vector2Field("Size");
            var rotation = new DoubleField("Rotation");
            root.Add(position); root.Add(size); root.Add(rotation);
            void Refresh(SerializedProperty p)
            {
                var transform = (ShaderFXTransform)p.boxedValue;
                transform.GetDisplay(Dimensions(), out var location, out var scale, out var angle);
                position.SetValueWithoutNotify(location); size.SetValueWithoutNotify(scale); rotation.SetValueWithoutNotify(angle);
            }
            void Change(System.Func<ShaderFXTransform, ShaderFXTransform> edit)
            {
                if (effect != null && WhimTexApi.IsShaderFXContentLocked(effect)) return;
                property.serializedObject.Update();
                property.boxedValue = edit((ShaderFXTransform)property.boxedValue);
                property.serializedObject.ApplyModifiedProperties();
                effect?.NotifyValuesChanged();
                Refresh(property);
            }
            position.RegisterValueChangedCallback(e => Change(t => { t.EditPosition(e.newValue, Dimensions()); return t; }));
            size.RegisterValueChangedCallback(e => Change(t => {
                t.EditSize(new Double2(ShaderFXTransform.SafeSize(e.newValue.x), ShaderFXTransform.SafeSize(e.newValue.y)), Dimensions()); return t; }));
            rotation.RegisterValueChangedCallback(e => Change(t => { t.EditRotation(e.newValue, Dimensions()); return t; }));
            root.Add(new Button(() => Change(_ => ShaderFXTransform.Default)) { text = "Reset Transform" });
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

        internal static VisualElement CreateInlineView(ShaderFX effect)
        {
            VisualElement host = new VisualElement();
            SerializedObject data = null;
            host.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                if (evt.target != host || data != null || effect == null)
                    return;
                data = new SerializedObject(effect);
                host.Add(BuildView(effect, data));
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

        private static VisualElement BuildView(ShaderFX effect, SerializedObject serializedObject)
        {
            VisualElement root = new VisualElement { focusable = true };
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-shader-fx");
            ShaderFXCodeField code = new ShaderFXCodeField(effect);
            var codeFoldout = new Foldout { text = "Code", value = false,
                tooltip = "Edit HLSL and declarations here. Parameter values update live without recompiling." };
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
            sourceButtons.Add(new Button(() => { string path = effect.CatalogPath; if (!string.IsNullOrEmpty(path)) AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(path)); }) { text = "Open HLSL Source" });
            var detach = new Button { text = "Embed Copy", tooltip = "Stop following the HLSL file and edit a copy in this document." };
            sourceButtons.Add(detach);
            codeFoldout.Add(sourceButtons);

            HelpBox status = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(status);
            Button apply = new Button { text = "Apply", tooltip = "Compile the code and parameter declarations; re-read included libraries. Embedded FX save with the document." };
            var codeActions = new VisualElement();
            codeActions.AddToClassList("whimtex-layer-fx-toolbar");
            codeActions.Add(apply);
            codeFoldout.Add(codeActions);
            void SavePreset()
            {
                if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                try
                {
                    System.IO.Directory.CreateDirectory(ShaderFXCatalog.Folder);
                    string path = EditorUtility.SaveFilePanel("Save HLSL Preset", ShaderFXCatalog.Folder, effect.name, "hlsl");
                    if (string.IsNullOrEmpty(path)) return;
                    bool overwrite = System.IO.File.Exists(path);
                    if (overwrite && !EditorUtility.DisplayDialog("Overwrite HLSL Preset", "Replace this preset? A .bak copy will be kept.", "Overwrite", "Cancel")) return;
                    ShaderFXPresetWriter.Save(path, effect, overwrite);
                    status.messageType = HelpBoxMessageType.Info;
                    status.text = "HLSL preset saved. Find it in + Preset.";
                    status.RemoveFromClassList("whimtex-shader-fx-hidden");
                }
                catch (System.Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
            }
            codeActions.Add(new Button(SavePreset) { text = "Save Preset…", tooltip = "Save HLSL with current parameter values as defaults. Layer sources remain document-local." });

            TextField diagnostics = new TextField
            {
                name = "shaderFXDiagnostics",
                multiline = true,
                isReadOnly = true,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            diagnostics.AddToClassList("whimtex-shader-fx-diagnostics");
            root.Add(diagnostics);
            // Controls are owned by the HLSL @param declarations.
            var declaredParameters = new ShaderFXParameterView(effect);
            detach.clicked += () => { Undo.RecordObject(effect, "Embed FX Source"); effect.DetachCatalog(); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged(); RefreshStatus(); };
            apply.clicked += () =>
            {
                if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                if (effect.IsCatalogLinked) effect.ReloadCatalogSource(true);
                else effect.Apply();
                serializedObject.Update();
                RefreshStatus();
            };
            root.Add(declaredParameters);
            Foldout reference = new Foldout { text = "Shader inputs", value = false };
            var inputHelp = new Label(
                "Implement float4 ApplyFX(float2 uv, float4 color).\n" +
                "Declare parameters with // @param; do not redeclare generated uniforms.\n" +
                "Use #include with Assets/Packages paths or paths relative to the containing asset (Assets before the first save). UnityCG.cginc is already included.\n\n" +
                "uv: normalized coordinates; color: straight RGBA at uv.\n" +
                "SampleInput(uv), _MainTex and _MainTex_TexelSize: incoming texture.\n" +
                "_InputSize: render width, height, 1/width, 1/height.\n" +
                "_CanvasSize: full-resolution canvas size in the same format.\n" +
                "_PreviewScale: full-size pixels per preview pixel (1 for export).\n" +
                "Texture parameters: sampler2D with <name>_TexelSize; sample with tex2D(name, uv).\n" +
                "Return straight RGBA. The layer's blend mode and opacity are applied afterwards.");
            inputHelp.AddToClassList("whimtex-fx-reference");
            reference.Add(inputHelp);
            codeFoldout.Add(reference);

            void RefreshStatus()
            {
                if (effect == null)
                    return;
                code.SyncFromModel();
                code.SetEnabled(!effect.IsCatalogLinked);
                externalCodeButtons.EnableInClassList("whimtex-shader-fx-hidden", effect.IsCatalogLinked);
                sourceButtons.EnableInClassList("whimtex-shader-fx-hidden", !effect.IsCatalogLinked);
                declaredParameters.EnableInClassList("whimtex-shader-fx-hidden", false);
                declaredParameters.Refresh();
                bool pendingChanges = effect.HasPendingChanges;
                status.messageType = effect.LastApplyFailed ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
                status.EnableInClassList("whimtex-shader-fx-hidden", !effect.LastApplyFailed && !pendingChanges);
                status.text = effect.LastApplyFailed
                    ? "Apply failed. This FX is skipped until it compiles successfully."
                    : pendingChanges ? "Unapplied code or parameter declarations. Click Apply when ready."
                    : "Applied. Values update without recompiling. Click Apply again after editing an included library.";
                diagnostics.SetValueWithoutNotify(effect.Diagnostics);
                diagnostics.EnableInClassList("whimtex-shader-fx-hidden",
                    !effect.LastApplyFailed && (string.IsNullOrWhiteSpace(effect.Diagnostics) || effect.Diagnostics == "Applied successfully."));
                codeFoldout.text = pendingChanges ? "Code • unapplied" : "Code";
                apply.SetEnabled(effect != null);
            }

            void ExternalCodeChanged(ShaderFX changed)
            {
                if (changed != effect) return;
                code.SyncFromModel();
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
                RefreshLock();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                WhimTexApi.LiveEditLocksChanged -= RefreshLock;
                ShaderFXExternalCode.CodeChanged -= ExternalCodeChanged;
            });
            RefreshLock();
            RefreshStatus();
            return root;
        }
    }

}
