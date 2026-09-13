using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
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
            SpriteEditorUI.ApplyWindowStyles(root);
            root.AddToClassList("sprite-editor-shader-fx");
            root.Add(new HelpBox(
                "Implement float4 ApplyFX(float2 uv, float4 color). SampleInput(uv) reads the incoming layer. " +
                "Use #include with Assets/Packages paths, or paths relative to the containing asset (Assets before the first save). " +
                "UnityCG.cginc is already included.", HelpBoxMessageType.Info));

            Label heading = new Label("HLSL Code");
            heading.AddToClassList("sprite-editor-shader-fx-heading");
            root.Add(heading);
            ShaderFXCodeField code = new ShaderFXCodeField(effect);
            var codeFoldout = new Foldout { text = "Code", value = !effect.IsCatalogLinked };
            codeFoldout.Add(code);
            root.Add(codeFoldout);
            var sourceButtons = new VisualElement();
            sourceButtons.Add(new Button(() => { string path = effect.CatalogPath; if (!string.IsNullOrEmpty(path)) AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(path)); }) { text = "Open HLSL Source" });
            var detach = new Button { text = "Embed Copy", tooltip = "Stop following the HLSL file and edit a copy in this document." };
            sourceButtons.Add(detach);
            root.Add(sourceButtons);

            HelpBox status = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            root.Add(status);
            Button apply = new Button { text = "Apply", tooltip = "Compile the code and parameter declarations; re-read included libraries. Embedded FX save with the document." };
            root.Add(apply);
            root.Add(new Button(() =>
            {
                if (SpriteEditorApi.IsShaderFXContentLocked(effect)) return;
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
                }
                catch (System.Exception error) { EditorUtility.DisplayDialog("Shader FX Presets", error.Message, "OK"); }
            })
            {
                text = "Save HLSL Preset…",
                tooltip = "Save code with current parameter values as defaults in the user ShaderFX folder or project Assets. Texture defaults reference project assets; they are not embedded."
            });

            TextField diagnostics = new TextField
            {
                name = "shaderFXDiagnostics",
                multiline = true,
                isReadOnly = true,
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            diagnostics.AddToClassList("sprite-editor-shader-fx-diagnostics");
            root.Add(diagnostics);
            var legacyParameters = new PropertyField(serializedObject.FindProperty("parameters"), "Parameters");
            var declaredParameters = new ShaderFXParameterView(effect);
            detach.clicked += () => { Undo.RecordObject(effect, "Embed FX Source"); effect.DetachCatalog(); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged(); RefreshStatus(); };
            apply.clicked += () =>
            {
                if (SpriteEditorApi.IsShaderFXContentLocked(effect)) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                if (effect.IsCatalogLinked) effect.ReloadCatalogSource(true);
                else effect.Apply();
                serializedObject.Update();
                RefreshStatus();
            };
            root.Add(new HelpBox(
                "Values update live. Catalog effects have independent settings; + Reference shares the asset. " +
                "Declare parameters with // @param, or use the manual list for legacy code. Declaration changes require Apply; do not redeclare generated uniforms.", HelpBoxMessageType.Info));
            root.Add(legacyParameters);
            root.Add(declaredParameters);
            Foldout reference = new Foldout { text = "Shader inputs", value = false };
            reference.Add(new HelpBox(
                "uv: normalized coordinates; color: straight RGBA at uv.\n" +
                "SampleInput(uv), _MainTex and _MainTex_TexelSize: incoming texture.\n" +
                "_InputSize: render width, height, 1/width, 1/height.\n" +
                "_CanvasSize: full-resolution canvas size in the same format.\n" +
                "_PreviewScale: full-size pixels per preview pixel (1 for export).\n" +
                "Texture parameters: sampler2D with <name>_TexelSize; sample with tex2D(name, uv).\n" +
                "Return straight RGBA. The layer's blend mode and opacity are applied afterwards.", HelpBoxMessageType.Info));
            root.Add(reference);

            void RefreshStatus()
            {
                if (effect == null)
                    return;
                code.SyncFromModel();
                code.SetEnabled(!effect.IsCatalogLinked);
                sourceButtons.EnableInClassList("sprite-editor-shader-fx-hidden", !effect.IsCatalogLinked);
                legacyParameters.EnableInClassList("sprite-editor-shader-fx-hidden", effect.UsesCodeParameters);
                declaredParameters.EnableInClassList("sprite-editor-shader-fx-hidden", !effect.UsesCodeParameters);
                declaredParameters.Refresh();
                status.messageType = effect.LastApplyFailed ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
                status.text = effect.LastApplyFailed
                    ? (effect.HasAppliedShader ? "Apply failed. The last successfully applied effect is still in use." : "Apply failed. This FX is skipped until it compiles successfully.")
                    : effect.HasPendingChanges ? "Unapplied code or parameter declarations. Click Apply when ready."
                    : "Applied. Values update without recompiling. Click Apply again after editing an included library.";
                diagnostics.SetValueWithoutNotify(effect.Diagnostics);
                apply.SetEnabled(effect != null);
            }

            root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
            root.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                if (evt.changedProperty != null && evt.changedProperty.propertyPath.StartsWith("parameters", System.StringComparison.Ordinal))
                    effect.NotifyValuesChanged();
            });
            root.Bind(serializedObject);
            void RefreshLock() => root.SetEnabled(!SpriteEditorApi.IsShaderFXContentLocked(effect));
            root.RegisterCallback<AttachToPanelEvent>(_ => { SpriteEditorApi.LiveEditLocksChanged -= RefreshLock; SpriteEditorApi.LiveEditLocksChanged += RefreshLock; RefreshLock(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => SpriteEditorApi.LiveEditLocksChanged -= RefreshLock);
            RefreshLock();
            RefreshStatus();
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(ShaderFXParameter))]
    public sealed class ShaderFXParameterDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VisualElement root = new VisualElement();
            SpriteEditorUI.ApplyWindowStyles(root);
            root.AddToClassList("sprite-editor-shader-fx-parameter");
            root.Add(new PropertyField(property.FindPropertyRelative(nameof(ShaderFXParameter.name)), "Name"));
            SerializedProperty type = property.FindPropertyRelative(nameof(ShaderFXParameter.type));
            root.Add(new PropertyField(type, "Type"));
            string[] valueNames =
            {
                nameof(ShaderFXParameter.floatValue), nameof(ShaderFXParameter.colorValue),
                nameof(ShaderFXParameter.vectorValue), nameof(ShaderFXParameter.textureValue), nameof(ShaderFXParameter.transformValue)
            };
            VisualElement[] fields = new VisualElement[valueNames.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                SerializedProperty value = property.FindPropertyRelative(valueNames[i]);
                if (valueNames[i] == nameof(ShaderFXParameter.colorValue))
                {
                    ColorField color = SpriteEditorColorInputs.Bind(new ColorField("Value"), value,
                        () => ((ShaderFX)value.serializedObject.targetObject).NotifyValuesChanged());
                    color.AddToClassList(BaseField<UnityEngine.Color>.alignedFieldUssClassName);
                    fields[i] = color;
                }
                else
                    fields[i] = new PropertyField(value, "Value");
                root.Add(fields[i]);
            }
            var editTransform = new Button(() =>
            {
                var current = property.FindPropertyRelative("id");
                TextureCompositorWindow.EditFXTransform((ShaderFX)property.serializedObject.targetObject, current.stringValue);
            }) { text = "Edit Transform on Canvas" };
            root.Add(editTransform);
            void RefreshType(SerializedProperty current)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].EnableInClassList("sprite-editor-shader-fx-hidden", i != current.enumValueIndex);
                editTransform.EnableInClassList("sprite-editor-shader-fx-hidden", current.enumValueIndex != (int)ShaderFXParameterType.Transform2D);
            }
            root.TrackPropertyValue(type, RefreshType);
            RefreshType(type);
            return root;
        }
    }
}
