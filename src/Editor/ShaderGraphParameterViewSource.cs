using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderGraphParameterViewSource : ShaderParameterViewSource
    {
        private readonly WhimTexDocument document;
        private readonly ShaderGraphEditorTarget target;
        private readonly bool graphOnly;
        private readonly List<ShaderFXParameter> displayed = new List<ShaderFXParameter>();
        private int undoGroup;
        private ShaderFX previewEffect;
        private string previewCode;
        private List<ShaderFXParameter> previewParameters;
        internal ShaderGraphParameterViewSource(WhimTexDocument document, string layerId, bool graphOnly = false)
            : this(new ShaderGraphEditorTarget(document, layerId), graphOnly) { }
        internal ShaderGraphParameterViewSource(WhimTexDocument document, string layerId, ShaderFX effect, bool graphOnly = false)
            : this(new ShaderGraphEditorTarget(document, layerId, effect, effectTarget: true), graphOnly) { }
        internal ShaderGraphParameterViewSource(ShaderGraphEditorTarget target, bool graphOnly = false)
        { this.target = target; document = target.Document; this.graphOnly = graphOnly; RefreshParameters(); }
        internal ShaderLayerBehaviour Behaviour => target.Behaviour;
        internal bool CodeMode => !graphOnly && target.Program?.mode == ShaderLayerMode.Code;
        internal override IReadOnlyList<ShaderFXParameter> Parameters => CodeMode ? target.Program.codeParameters : displayed;
        internal override string Code => CodeMode ? target.Program.compiledCode : string.Empty;
        internal override WhimTexDocument Document => document;
        internal override bool IsReadOnly => target.IsReadOnly || graphOnly && !target.GraphActive;
        internal override bool SupportsLayerTextures => true;
        internal override bool IsShaderLayer => CodeMode && !target.IsEffect;
        internal override ShaderFX PreviewEffect
        {
            get
            {
                if (!CodeMode) return null;
                if (target.IsEffect) return target.Effect;
                var current = Behaviour;
                if (previewEffect == null || previewCode != current.compiledCode || !ReferenceEquals(previewParameters, current.codeParameters))
                {
                    ReleasePreview();
                    previewEffect = ShaderFX.CreateAgentDraft(document, current.compiledCode, current.codeParameters, current.CodeSourcePath(document));
                    previewCode = current.compiledCode; previewParameters = current.codeParameters;
                }
                return previewEffect;
            }
        }
        internal void ReleasePreview()
        {
            if (previewEffect != null) UnityEngine.Object.DestroyImmediate(previewEffect);
            previewEffect = null; previewCode = null; previewParameters = null;
        }
        internal override Vector2 Dimensions => document != null ? new Vector2(document.width, document.height) : Vector2.one;

        internal override void RefreshParameters()
        {
            if (CodeMode) { displayed.Clear(); return; }
            if (previewEffect != null) ReleasePreview();
            RefreshDisplayParameters(displayed, target.Graph?.parameters);
        }

        internal static void RefreshDisplayParameters(List<ShaderFXParameter> displayed, IReadOnlyList<ShaderFXParameter> parameters)
        {
            if (parameters == null) { displayed.Clear(); return; }
            int count = 0;
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p == null) continue;
                if (count == displayed.Count) displayed.Add(new ShaderFXParameter());
                var view = displayed[count++];
                view.id = p.id; view.name = p.name; view.type = p.type; view.valueType = p.valueType;
                view.floatValue = p.floatValue; view.colorValue = p.colorValue; view.vectorValue = p.vectorValue;
                view.transformValue = p.transformValue; view.pixelUnits = p.pixelUnits;
                view.integerValues = p.integerValues; view.textureValue = p.textureValue;
                view.textureSource = p.textureSource; view.textureLayerId = p.textureLayerId;
                view.gradientValue = p.gradientValue; view.curveValue = p.curveValue;
                view.hasMinimum = p.hasMinimum; view.hasMaximum = p.hasMaximum;
                view.minimum = p.minimum; view.maximum = p.maximum;
                view.softMinimum = p.softMinimum; view.softMaximum = p.softMaximum; view.cyclicRange = p.cyclicRange;
                if (view.controls.Count == 0) view.controls.Add(new ShaderFXParameterControl());
                var control = view.controls[0];
                control.type = p.type; control.label = p.name; control.order = i; control.valueType = p.ValueType;
                control.hasMinimum = p.hasMinimum; control.hasMaximum = p.hasMaximum;
                control.minimum = p.minimum; control.maximum = p.maximum;
                control.softMinimum = p.softMinimum; control.softMaximum = p.softMaximum; control.cyclicRange = p.cyclicRange;
            }
            if (displayed.Count > count) displayed.RemoveRange(count, displayed.Count - count);
        }

        private ShaderFXParameter FindDefinition(string id) => CodeMode
            ? target.Program.codeParameters.Find(p => p?.id == id) : target.Graph?.FindParameter(id);

        internal override void Change(string id, Action<ShaderFXParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly || FindDefinition(id) is not ShaderFXParameter parameter) return;
            if (recordUndo) target.RecordUndo("Change Shader Parameter");
            edit(parameter);
            RefreshParameters();
            if (previewEffect != null) previewEffect.NotifyValuesChanged();
            target.MarkChanged();
        }
        internal override void ChangeParameters(string undoName, IReadOnlyList<ShaderFXParameter> values)
        {
            if (IsReadOnly || values.Count == 0) return;
            foreach (var value in values)
            {
                if (value == null) return;
                var target = FindDefinition(value.id);
                if (target == null || target.type != value.type || target.ValueType != value.ValueType) return;
            }
            target.RecordUndo(undoName);
            foreach (var value in values) ShaderFXMetadata.CopyValue(value, FindDefinition(value.id));
            RefreshParameters(); if (previewEffect != null) previewEffect.NotifyValuesChanged(); target.MarkChanged();
        }
        internal override void BeginDrag()
        {
            if (IsReadOnly) return;
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change Shader Parameter");
            target.RecordUndo("Change Shader Parameter");
        }
        internal override void EndDrag(bool changed) { if (changed) Undo.CollapseUndoOperations(undoGroup); }
        internal override VisualElement CreateTextureField(string label, string id, List<Action> refresh)
        {
            var field = new TextureField(this, label, id);
            refresh.Add(field.Refresh);
            return field;
        }

        private sealed class TextureField : VisualElement
        {
            private readonly ShaderGraphParameterViewSource source;
            private readonly string id;
            private readonly DropdownField mode;
            private readonly ObjectField texture;
            private readonly PopupField<Layer> layer;
            private readonly HelpBox warning;
            internal TextureField(ShaderGraphParameterViewSource source, string label, string id)
            {
                this.source = source; this.id = id;
                var sources = new List<string> { "Texture", "Layer" };
                if (source.target.IsEffect) sources.Add("Self");
                sources.Add("None");
                mode = new DropdownField(label + " Source", sources, 0);
                texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
                layer = new PopupField<Layer>(label, new List<Layer> { null }, 0, Name, Name);
                warning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
                Add(mode); Add(texture); Add(layer); Add(warning);
                mode.RegisterValueChangedCallback(e => Change(p => p.textureSource = e.newValue switch
                {
                    "Layer" => ShaderFXTextureSource.Layer, "Self" => ShaderFXTextureSource.Self,
                    "None" => ShaderFXTextureSource.None, _ => ShaderFXTextureSource.Texture
                }));
                texture.RegisterValueChangedCallback(e => Change(p => p.textureValue = e.newValue as Texture2D));
                layer.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue == null || Usable(e.newValue)) Change(p => p.textureLayerId = e.newValue?.Id);
                });
                layer.RegisterCallback<PointerDownEvent>(_ => RefreshChoices(), TrickleDown.TrickleDown);
                this.AddManipulator(new TextureDrop(this));
                RefreshChoices(); Refresh();
            }
            private static string Name(Layer value) => value == null ? "None" : value.layerName + (value.IsGroup ? " [Group]" : "");
            private bool Usable(Layer value) => source.target.Available && source.document.IsUsableStoredShaderTexture(source.target.Layer, value.Id);
            private void Change(Action<ShaderFXParameter> edit) { source.Change(id, edit); Refresh(); }
            private void RefreshChoices()
            {
                var options = new List<Layer> { null };
                void Visit(List<Layer> items)
                {
                    if (items == null) return;
                    foreach (var item in items)
                    {
                        if (item == null) continue;
                        if (Usable(item)) options.Add(item);
                        if (item.IsGroup) Visit(item.children);
                    }
                }
                if (source.document != null) Visit(source.document.layers);
                layer.choices = options;
                Refresh();
            }
            internal void Refresh()
            {
                var p = source.Find(id);
                if (p == null) return;
                bool fromLayer = p.textureSource == ShaderFXTextureSource.Layer;
                mode.SetValueWithoutNotify(fromLayer ? "Layer" : p.textureSource == ShaderFXTextureSource.None ? "None" :
                    p.textureSource == ShaderFXTextureSource.Self && source.target.IsEffect ? "Self" : "Texture");
                texture.SetValueWithoutNotify(p.textureValue);
                texture.EnableInClassList("whimtex-hidden", p.textureSource != ShaderFXTextureSource.Texture);
                layer.EnableInClassList("whimtex-hidden", !fromLayer);
                var selected = source.document != null ? source.document.FindLayer(p.textureLayerId) : null;
                if (selected != null && !layer.choices.Contains(selected)) layer.choices.Add(selected);
                layer.SetValueWithoutNotify(selected);
                warning.text = p.textureSource == ShaderFXTextureSource.Self && !source.target.IsEffect ? "Self is not available for Shader layers. Choose Texture, Layer or None."
                    : fromLayer && !string.IsNullOrEmpty(p.textureLayerId) && (selected == null || !Usable(selected))
                    ? "The source is missing or creates a cyclic dependency." : string.Empty;
                warning.EnableInClassList("whimtex-hidden", warning.text.Length == 0);
                SetEnabled(!source.IsReadOnly);
            }

            private sealed class TextureDrop : PointerManipulator
            {
                private readonly TextureField owner;
                internal TextureDrop(TextureField owner) => this.owner = owner;
                protected override void RegisterCallbacksOnTarget()
                {
                    target.RegisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                    target.RegisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                }
                protected override void UnregisterCallbacksFromTarget()
                {
                    target.UnregisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                    target.UnregisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                }
                private bool Read(out Layer layer, out Texture2D texture)
                {
                    layer = null; texture = null;
                    if (owner.source.IsReadOnly) return false;
                    layer = WhimTexWindow.GetDraggedLayerForDocument(owner.source.document);
                    if (layer != null) return owner.Usable(layer);
                    if (DragAndDrop.objectReferences.Length == 1) texture = DragAndDrop.objectReferences[0] as Texture2D;
                    return texture != null;
                }
                private void Updated(DragUpdatedEvent e)
                {
                    DragAndDrop.visualMode = Read(out _, out _) ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                    e.StopPropagation();
                }
                private void Perform(DragPerformEvent e)
                {
                    if (!Read(out var layer, out var texture)) return;
                    DragAndDrop.AcceptDrag();
                    owner.Change(p =>
                    {
                        p.textureSource = layer != null ? ShaderFXTextureSource.Layer : ShaderFXTextureSource.Texture;
                        if (layer != null) p.textureLayerId = layer.Id; else p.textureValue = texture;
                    });
                    if (layer != null) WhimTexWindow.ClearDraggedLayerReference();
                    e.StopPropagation();
                }
            }
        }
    }
}
