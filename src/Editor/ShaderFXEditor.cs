using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

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
            bool Editable() => effect != null && !WhimTexApi.IsShaderFXContentLocked(effect);
            var declaredParameters = new ShaderParameterView(effect, false, document, layer, fxIndex);
            ShaderProgramView program = null;
            bool refreshingStatus = false;
            void Compile()
            {
                if (!Editable()) return;
                root.Focus();
                serializedObject.ApplyModifiedProperties();
                if (effect.IsCatalogLinked) effect.ReloadCatalogSource(true);
                else effect.Compile();
                serializedObject.Update();
                RefreshStatus();
            }
            program = new ShaderProgramView(new ShaderCodeField(effect), new ShaderProgramView.Actions
            {
                changeMode = mode =>
                {
                    if (!Editable()) return;
                    Undo.RecordObject(effect, "Change Shader FX Mode");
                    if (document != null) Undo.RecordObject(document, "Change Shader FX Mode");
                    effect.SetMode(mode);
                    serializedObject.Update();
                    RefreshStatus();
                },
                editGraph = document != null && layer != null ? () => ShaderGraphWindow.Open(effect, layer, document) : null,
#if WHIMTEX_DEEP_DEBUG
                createDebugGraph = document != null && layer != null
                    ? () => ShaderGraphWindow.CreateDebugGraph(new ShaderGraphEditorTarget(document, layer.Id, effect, effectTarget: true)) : null,
#endif
                compile = Compile,
                choosePreset = anchor =>
                {
                    if (!Editable()) return;
                    new ShaderFXAddDropdown(entry =>
                    {
                        if (!Editable() || (document != null && layer != null &&
                            document.FindLayer(layer.Id)?.fx.Contains(effect) != true)) return;
                        Undo.RecordObject(effect, "Assign Shader Preset");
                        if (!effect.ApplyPreset(entry, out string error))
                            EditorUtility.DisplayDialog("Shader Preset", error, "OK");
                        serializedObject.Update();
                        RefreshStatus();
                    }, null, title: "Shader Presets").ShowAlignedRight(anchor);
                },
                savePreset = () =>
                {
                    if (!Editable()) return;
                    root.Focus();
                    serializedObject.ApplyModifiedProperties();
                    ShaderProgramView.SavePreset(effect.Program, effect.name, (path, overwrite) => ShaderPresetWriter.Save(path, effect, overwrite));
                },
                embed = () =>
                {
                    if (!Editable()) return;
                    Undo.RecordObject(effect, "Embed FX Source");
                    effect.DetachCatalog();
                    EditorUtility.SetDirty(effect);
                    effect.NotifyValuesChanged();
                    RefreshStatus();
                },
                clearPreset = () =>
                {
                    if (!Editable() || !effect.IsCatalogLinked) return;
                    Undo.RecordObject(effect, "Clear Shader Preset");
                    effect.ClearPreset();
                    serializedObject.Update();
                    RefreshStatus();
                },
                reloadPreset = Compile,
                openCode = () => { if (Editable()) ShaderFXExternalCode.OpenInUnityEditor(effect); },
                openInVsCode = () => { if (Editable()) ShaderFXExternalCode.OpenInVsCode(effect); }
            }, effectContext: true);
            root.Add(program);
            root.Add(declaredParameters);
            void RefreshStatus()
            {
                if (effect == null || program == null || refreshingStatus)
                    return;
                refreshingStatus = true;
                try
                {
                    program.Refresh(effect.Program, effect.CompilationStatus, Editable(),
                        effect.Mode == ShaderProgramMode.Code && effect.HasPendingChanges,
                        effect.UsingPresetFallback, effect.PresetSourcePath, effect: effect);
                    declaredParameters.Refresh();
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
