using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

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
            var diagnosticsFoldout = new Foldout { text = "Diagnostics", value = false };
            diagnosticsFoldout.Add(diagnostics);
            root.Add(diagnosticsFoldout);
            var legacyParameters = new PropertyField(serializedObject.FindProperty("parameters"), "Parameters");
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
            root.Add(legacyParameters);
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
                sourceButtons.EnableInClassList("whimtex-shader-fx-hidden", !effect.IsCatalogLinked);
                legacyParameters.EnableInClassList("whimtex-shader-fx-hidden", effect.UsesCodeParameters);
                declaredParameters.EnableInClassList("whimtex-shader-fx-hidden", !effect.UsesCodeParameters);
                declaredParameters.Refresh();
                status.messageType = effect.LastApplyFailed ? HelpBoxMessageType.Error : HelpBoxMessageType.Info;
                status.EnableInClassList("whimtex-shader-fx-hidden", !effect.LastApplyFailed && !effect.HasPendingChanges);
                status.text = effect.LastApplyFailed
                    ? (effect.HasAppliedShader ? "Apply failed. The last successfully applied effect is still in use." : "Apply failed. This FX is skipped until it compiles successfully.")
                    : effect.HasPendingChanges ? "Unapplied code or parameter declarations. Click Apply when ready."
                    : "Applied. Values update without recompiling. Click Apply again after editing an included library.";
                diagnostics.SetValueWithoutNotify(effect.Diagnostics);
                diagnosticsFoldout.EnableInClassList("whimtex-shader-fx-hidden",
                    !effect.LastApplyFailed && (string.IsNullOrWhiteSpace(effect.Diagnostics) || effect.Diagnostics == "Applied successfully."));
                codeFoldout.text = effect.HasPendingChanges ? "Code • unapplied" : "Code";
                apply.SetEnabled(effect != null);
            }

            root.TrackSerializedObjectValue(serializedObject, _ => RefreshStatus());
            root.RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                if (evt.changedProperty != null && evt.changedProperty.propertyPath.StartsWith("parameters", System.StringComparison.Ordinal))
                    effect.NotifyValuesChanged();
            });
            root.Bind(serializedObject);
            void RefreshLock() => root.SetEnabled(!WhimTexApi.IsShaderFXContentLocked(effect));
            root.RegisterCallback<AttachToPanelEvent>(_ => { WhimTexApi.LiveEditLocksChanged -= RefreshLock; WhimTexApi.LiveEditLocksChanged += RefreshLock; RefreshLock(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => WhimTexApi.LiveEditLocksChanged -= RefreshLock);
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
            WhimTexUI.ApplyWindowStyles(root);
            root.AddToClassList("whimtex-shader-fx-parameter");
            root.Add(new PropertyField(property.FindPropertyRelative(nameof(ShaderFXParameter.name)), "Name"));
            SerializedProperty type = property.FindPropertyRelative(nameof(ShaderFXParameter.type));
            root.Add(new PropertyField(type, "Type"));
            string[] valueNames =
            {
                nameof(ShaderFXParameter.floatValue), nameof(ShaderFXParameter.colorValue),
                nameof(ShaderFXParameter.vectorValue), nameof(ShaderFXParameter.textureValue), nameof(ShaderFXParameter.transformValue),
                nameof(ShaderFXParameter.floatValue), nameof(ShaderFXParameter.floatValue), nameof(ShaderFXParameter.gradientValue),
                nameof(ShaderFXParameter.vectorValue), nameof(ShaderFXParameter.vectorValue), nameof(ShaderFXParameter.vectorValue),
                nameof(ShaderFXParameter.curveValue)
            };
            VisualElement[] fields = new VisualElement[valueNames.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                SerializedProperty value = property.FindPropertyRelative(valueNames[i]);
                if (i == (int)ShaderFXParameterType.Bool)
                {
                    var toggle = new Toggle("Value");
                    toggle.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
                    toggle.SetValueWithoutNotify(value.floatValue >= 0.5f);
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        value.serializedObject.Update();
                        value.floatValue = evt.newValue ? 1f : 0f;
                        value.serializedObject.ApplyModifiedProperties();
                        ((ShaderFX)value.serializedObject.targetObject).NotifyValuesChanged();
                    });
                    toggle.TrackPropertyValue(value, p => toggle.SetValueWithoutNotify(p.floatValue >= 0.5f));
                    fields[i] = toggle;
                }
                else if (i == (int)ShaderFXParameterType.Curve)
                {
                    var curve = new CurveField("Value");
                    curve.SetValueWithoutNotify(value.animationCurveValue ?? WhimTexCurveTexture.Default());
                    curve.RegisterValueChangedCallback(evt =>
                    {
                        var effect = (ShaderFX)value.serializedObject.targetObject;
                        if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                        value.serializedObject.Update();
                        value.animationCurveValue = WhimTexCurveTexture.Copy(evt.newValue);
                        value.serializedObject.ApplyModifiedProperties();
                        effect.NotifyValuesChanged();
                    });
                    curve.TrackPropertyValue(value, p => curve.SetValueWithoutNotify(p.animationCurveValue ?? WhimTexCurveTexture.Default()));
                    fields[i] = curve;
                }
                else if (i == (int)ShaderFXParameterType.Gradient)
                {
                    var gradient = new WhimTexGradientValueField("Value");
                    if (type.enumValueIndex == (int)ShaderFXParameterType.Gradient)
                        gradient.SetValueWithoutNotify(value.boxedValue as WhimTexGradient ?? new WhimTexGradient());
                    gradient.RegisterValueChangedCallback(evt =>
                    {
                        var effect = (ShaderFX)value.serializedObject.targetObject;
                        if (WhimTexApi.IsShaderFXContentLocked(effect)) return;
                        value.serializedObject.Update();
                        value.boxedValue = evt.newValue?.Clone() ?? new WhimTexGradient();
                        value.serializedObject.ApplyModifiedProperties();
                        effect.NotifyValuesChanged();
                    });
                    gradient.TrackPropertyValue(value, p =>
                    {
                        if (type.enumValueIndex == (int)ShaderFXParameterType.Gradient)
                            gradient.SetValueWithoutNotify(p.boxedValue as WhimTexGradient ?? new WhimTexGradient());
                    });
                    gradient.TrackPropertyValue(type, p =>
                    {
                        if (p.enumValueIndex == (int)ShaderFXParameterType.Gradient)
                            gradient.SetValueWithoutNotify(value.boxedValue as WhimTexGradient ?? new WhimTexGradient());
                    });
                    fields[i] = gradient;
                }
                else if (i == (int)ShaderFXParameterType.Vector2)
                {
                    var field = new Vector2Field("Value");
                    field.SetValueWithoutNotify(value.vector4Value);
                    field.RegisterValueChangedCallback(e => { value.serializedObject.Update(); value.vector4Value = e.newValue; value.serializedObject.ApplyModifiedProperties(); ((ShaderFX)value.serializedObject.targetObject).NotifyValuesChanged(); });
                    field.TrackPropertyValue(value, p => field.SetValueWithoutNotify(p.vector4Value));
                    fields[i] = field;
                }
                else if (i == (int)ShaderFXParameterType.Vector3 || i == (int)ShaderFXParameterType.Normal)
                {
                    bool normal = i == (int)ShaderFXParameterType.Normal;
                    var field = new Vector3Field("Value");
                    field.SetValueWithoutNotify(value.vector4Value);
                    field.RegisterValueChangedCallback(e => { value.serializedObject.Update(); value.vector4Value = normal ? ShaderFXParameter.NormalizeNormal(e.newValue) : e.newValue; value.serializedObject.ApplyModifiedProperties(); ((ShaderFX)value.serializedObject.targetObject).NotifyValuesChanged(); });
                    field.TrackPropertyValue(value, p => field.SetValueWithoutNotify(p.vector4Value));
                    fields[i] = field;
                }
                else if (valueNames[i] == nameof(ShaderFXParameter.textureValue))
                    fields[i] = new ShaderFXTextureField((ShaderFX)property.serializedObject.targetObject,
                        property.FindPropertyRelative("id").stringValue, "Value");
                else if (valueNames[i] == nameof(ShaderFXParameter.colorValue))
                {
                    ColorField color = WhimTexColorInputs.Bind(new ColorField("Value"), value,
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
            var editNormal = new Button(() => TextureCompositorWindow.EditFXNormal((ShaderFX)property.serializedObject.targetObject, property.FindPropertyRelative("id").stringValue)) { text = "Edit Normal on Canvas" };
            root.Add(editNormal);
            void RefreshType(SerializedProperty current)
            {
                for (int i = 0; i < fields.Length; i++)
                    fields[i].EnableInClassList("whimtex-shader-fx-hidden", i != current.enumValueIndex);
                editNormal.EnableInClassList("whimtex-shader-fx-hidden", current.enumValueIndex != (int)ShaderFXParameterType.Normal);
                editTransform.EnableInClassList("whimtex-shader-fx-hidden", current.enumValueIndex != (int)ShaderFXParameterType.Transform2D);
            }
            root.TrackPropertyValue(type, RefreshType);
            RefreshType(type);
            return root;
        }
    }
}
