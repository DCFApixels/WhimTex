using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ShaderFXTextureField : VisualElement
    {
        private readonly ShaderFX effect;
        private readonly string id;
        private readonly EnumField source;
        private readonly ObjectField texture;
        private readonly PopupField<Layer> layer;
        private readonly HelpBox warning;
        private readonly List<Layer> choices = new List<Layer> { null };
        private TextureCompositor document;

        internal ShaderFXTextureField(ShaderFX effect, string id, string label)
        {
            this.effect = effect; this.id = id;
            document = TextureCompositorWindow.FindFXTransformDocument(effect);
            source = new EnumField(label + " Source", ShaderFXTextureSource.Texture);
            source.tooltip = "None: transparent. Self: image immediately before this FX. Texture: asset (white if empty). Layer: another layer, including hidden sources.";
            texture = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false };
            layer = new PopupField<Layer>(label, choices, 0, Name, Name);
            layer.AddToClassList("whimtex-effect-target");
            layer.tooltip = "Select or drop a layer from this document. Hidden layers are sampled too, like an effect Target.";
            warning = new HelpBox("", HelpBoxMessageType.Warning);
            Add(source); Add(texture); Add(layer); Add(warning);
            source.RegisterValueChangedCallback(e => Change(p => p.textureSource = (ShaderFXTextureSource)e.newValue));
            texture.RegisterValueChangedCallback(e => Change(p => { p.textureValue = e.newValue as Texture2D; p.textureSource = ShaderFXTextureSource.Texture; }));
            layer.RegisterValueChangedCallback(e => {
                if (e.newValue == null || document != null && document.IsUsableShaderTexture(effect, e.newValue.Id))
                    Change(p => { p.textureSource = ShaderFXTextureSource.Layer; p.textureLayerId = e.newValue?.Id; });
            });
            layer.RegisterCallback<PointerDownEvent>(_ => RefreshChoices(), TrickleDown.TrickleDown);
            this.AddManipulator(new Drop(this));
            schedule.Execute(Refresh).Every(250);
            Refresh();
        }

        private string Name(Layer value) => value != null ? value.layerName + (value.IsGroup ? " [Group]" : "") : "None (Layer)";
        private ShaderFXParameter Parameter
        {
            get { if (effect != null) foreach (var p in effect.Parameters) if (p != null && p.id == id) return p; return null; }
        }
        private void Change(Action<ShaderFXParameter> edit)
        {
            var p = Parameter;
            if (p == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            Undo.RecordObject(effect, "Change FX Texture Source");
            edit(p); EditorUtility.SetDirty(effect); effect.NotifyValuesChanged(); Refresh();
        }

        private void RefreshChoices()
        {
            if (document == null) document = TextureCompositorWindow.FindFXTransformDocument(effect);
            if (document != null) document.GetShaderTextureOptions(effect, choices);
            else { choices.Clear(); choices.Add(null); }
            var selected = document != null ? document.FindLayer(Parameter?.textureLayerId) : null;
            if (selected != null && !choices.Contains(selected)) choices.Add(selected);
            layer.choices = new List<Layer>(choices);
            layer.SetValueWithoutNotify(selected);
        }

        internal void Refresh()
        {
            var p = Parameter;
            if (p == null) return;
            source.SetValueWithoutNotify(p.textureSource);
            texture.SetValueWithoutNotify(p.textureValue);
            bool fromLayer = p.textureSource == ShaderFXTextureSource.Layer;
            texture.EnableInClassList("whimtex-shader-fx-hidden", p.textureSource != ShaderFXTextureSource.Texture);
            layer.EnableInClassList("whimtex-shader-fx-hidden", !fromLayer);
            if (!fromLayer)
            {
                warning.EnableInClassList("whimtex-shader-fx-hidden", true);
                return;
            }
            if (document == null) document = TextureCompositorWindow.FindFXTransformDocument(effect);
            var selected = document != null ? document.FindLayer(p.textureLayerId) : null;
            if (selected != null && !layer.choices.Contains(selected)) layer.choices.Add(selected);
            layer.SetValueWithoutNotify(selected);
            string message = document == null ? "Select a layer using this FX in a WhimTex document."
                : string.IsNullOrEmpty(p.textureLayerId) ? "Choose a source layer. An empty source samples transparent pixels."
                : !document.IsUsableShaderTexture(effect, p.textureLayerId) ? "Source is missing or creates a cyclic dependency. It samples transparent pixels." : "";
            warning.text = message;
            warning.EnableInClassList("whimtex-shader-fx-hidden", message.Length == 0);
        }

        private sealed class Drop : PointerManipulator
        {
            private readonly ShaderFXTextureField owner;
            internal Drop(ShaderFXTextureField owner) => this.owner = owner;
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                target.RegisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                target.RegisterCallback<DragLeaveEvent>(Leave);
                target.RegisterCallback<DragExitedEvent>(Exited);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Clear();
                target.UnregisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragLeaveEvent>(Leave);
                target.UnregisterCallback<DragExitedEvent>(Exited);
            }
            private bool Get(out Layer layer, out Texture2D texture)
            {
                layer = null; texture = null;
                if (!target.enabledInHierarchy || owner.effect == null || WhimTexApi.IsShaderFXContentLocked(owner.effect)) return false;
                if (owner.document == null) owner.document = TextureCompositorWindow.FindFXTransformDocument(owner.effect);
                layer = TextureCompositorWindow.GetDraggedLayerForDocument(owner.document);
                if (layer != null) return owner.document != null && owner.document.IsUsableShaderTexture(owner.effect, layer.Id);
                if (DragAndDrop.objectReferences.Length == 1) texture = DragAndDrop.objectReferences[0] as Texture2D;
                return texture != null;
            }
            private void Updated(DragUpdatedEvent e)
            {
                bool valid = Get(out _, out _);
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                owner.layer.EnableInClassList("whimtex-effect-target--drop", valid);
                e.StopImmediatePropagation();
            }
            private void Perform(DragPerformEvent e)
            {
                e.StopImmediatePropagation(); Clear();
                if (!Get(out var layer, out var texture)) return;
                DragAndDrop.AcceptDrag();
                owner.Change(p => {
                    p.textureSource = layer != null ? ShaderFXTextureSource.Layer : ShaderFXTextureSource.Texture;
                    if (layer != null) p.textureLayerId = layer.Id; else p.textureValue = texture;
                });
                if (layer != null) TextureCompositorWindow.ClearDraggedLayerReference();
            }
            private void Clear() => owner.layer.RemoveFromClassList("whimtex-effect-target--drop");
            private void Leave(DragLeaveEvent e) => Clear();
            private void Exited(DragExitedEvent e) => Clear();
        }
    }
}
