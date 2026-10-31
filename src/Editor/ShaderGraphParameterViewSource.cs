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
        private readonly string layerId;
        private readonly List<ShaderFXParameter> displayed = new List<ShaderFXParameter>();
        private int undoGroup;
        internal ShaderGraphParameterViewSource(WhimTexDocument document, string layerId)
        { this.document = document; this.layerId = layerId; RefreshParameters(); }
        internal ShaderLayerBehaviour Behaviour => document != null ? document.FindLayer(layerId)?.Behaviour as ShaderLayerBehaviour : null;
        internal override IReadOnlyList<ShaderFXParameter> Parameters => displayed;
        internal override string Code => string.Empty;
        internal override WhimTexDocument Document => document;
        internal override bool IsReadOnly => Behaviour == null || WhimTexApi.IsLayerContentLocked(document, Behaviour.Owner);
        internal override bool SupportsLayerTextures => true;
        internal override Vector2 Dimensions => document != null ? new Vector2(document.width, document.height) : Vector2.one;

        internal void RefreshParameters()
        {
            var parameters = Behaviour?.graph?.parameters;
            if (parameters == null) { displayed.Clear(); return; }
            while (displayed.Count > parameters.Count) displayed.RemoveAt(displayed.Count - 1);
            for (int i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i];
                if (p == null) continue;
                if (i == displayed.Count) displayed.Add(new ShaderFXParameter());
                var view = displayed[i];
                view.id = p.id; view.name = p.name; view.type = p.type; view.valueType = p.valueType;
                view.floatValue = p.floatValue; view.colorValue = p.colorValue; view.vectorValue = p.vectorValue;
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
        }

        internal override void Change(string id, Action<ShaderFXParameter> edit, bool recordUndo = true)
        {
            if (IsReadOnly || Behaviour.graph.FindParameter(id) is not ShaderFXParameter parameter) return;
            if (recordUndo) Undo.RecordObject(document, "Change Shader Parameter");
            edit(parameter);
            RefreshParameters();
            document.MarkChanged();
        }
        internal override void ChangeParameters(string undoName, IReadOnlyList<ShaderFXParameter> values)
        {
            if (IsReadOnly || values.Count == 0) return;
            foreach (var value in values)
            {
                var target = Behaviour.graph.FindParameter(value.id);
                if (target == null || target.type != value.type || target.ValueType != value.ValueType) return;
            }
            Undo.RecordObject(document, undoName);
            foreach (var value in values) ShaderFXMetadata.CopyValue(value, Behaviour.graph.FindParameter(value.id));
            RefreshParameters(); document.MarkChanged();
        }
        internal override void BeginDrag()
        {
            if (IsReadOnly) return;
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Change Shader Parameter");
            Undo.RecordObject(document, "Change Shader Parameter");
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
                mode = new DropdownField(label + " Source", new List<string> { "Texture", "Layer", "None" }, 0);
                texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
                layer = new PopupField<Layer>(label, new List<Layer> { null }, 0, Name, Name);
                warning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
                Add(mode); Add(texture); Add(layer); Add(warning);
                mode.RegisterValueChangedCallback(e => Change(p => p.textureSource = e.newValue == "Layer" ? ShaderFXTextureSource.Layer : e.newValue == "None" ? ShaderFXTextureSource.None : ShaderFXTextureSource.Texture));
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
            private bool Usable(Layer value) => source.Behaviour != null && source.document.IsUsableShaderTexture(source.Behaviour.Owner, value.Id);
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
                mode.SetValueWithoutNotify(fromLayer ? "Layer" : p.textureSource == ShaderFXTextureSource.None ? "None" : "Texture");
                texture.SetValueWithoutNotify(p.textureValue);
                texture.EnableInClassList("whimtex-hidden", p.textureSource != ShaderFXTextureSource.Texture);
                layer.EnableInClassList("whimtex-hidden", !fromLayer);
                var selected = source.document != null ? source.document.FindLayer(p.textureLayerId) : null;
                if (selected != null && !layer.choices.Contains(selected)) layer.choices.Add(selected);
                layer.SetValueWithoutNotify(selected);
                warning.text = p.textureSource == ShaderFXTextureSource.Self ? "Self is not available for Shader layers. Choose Texture, Layer or None."
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
