using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    internal sealed class LayerShaderFXView : VisualElement
    {
        private readonly Layer layer;
        private readonly TextureCompositor owner;
        private readonly Action<string, Action> applyChange;
        private readonly VisualElement entries = new VisualElement();
        private readonly List<UnityEngine.Object> displayed = new List<UnityEngine.Object>();

        internal LayerShaderFXView(Layer layer, TextureCompositor owner, Action<string, Action> applyChange)
        {
            this.layer = layer;
            this.owner = owner;
            this.applyChange = applyChange;
            SpriteEditorUI.ApplyWindowStyles(this);
            AddToClassList("sprite-editor-layer-fx");
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("sprite-editor-layer-fx-toolbar");
            toolbar.Add(new Button(() => Change("Add Shader FX", () => owner.AddEmbeddedShaderFX(layer))) { text = "+ Shader FX" });
            toolbar.Add(new Button(() => ShaderFXCatalog.ShowMenu(entry => Change("Add Catalog FX", () => owner.AddCatalogShaderFX(layer, entry)))) { text = "+ Preset ▾", tooltip = "Effects from the project and your user ShaderFX preset folder." });
            toolbar.Add(new Button(() => Change("Add FX Reference", () => layer.modifiers.Add(null))) { text = "+ Reference" });
            Add(toolbar);
            Add(entries);
            Refresh();
        }

        internal void Refresh()
        {
            bool changed = displayed.Count != layer.modifiers.Count;
            for (int i = 0; !changed && i < displayed.Count; i++)
                changed = displayed[i] != layer.modifiers[i];
            if (!changed)
                return;
            entries.Clear();
            displayed.Clear();
            displayed.AddRange(layer.modifiers);
            for (int i = 0; i < displayed.Count; i++)
                AddEntry(i);
        }

        private void AddEntry(int index)
        {
            UnityEngine.Object modifier = layer.modifiers[index];
            ShaderFX effect = modifier as ShaderFX;
            VisualElement card = new VisualElement();
            card.AddToClassList("sprite-editor-layer-fx-entry");
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("sprite-editor-layer-fx-toolbar");
            bool embedded = effect != null && effect.EmbeddedOwner == owner;
            if (embedded)
            {
                Label name = new Label($"{index + 1}. {effect.name}");
                name.AddToClassList("sprite-editor-layer-fx-name");
                toolbar.Add(name);
            }
            else
            {
                ObjectField reference = new ObjectField
                {
                    objectType = typeof(UnityEngine.Object), allowSceneObjects = false
                };
                reference.AddToClassList("sprite-editor-layer-fx-name");
                reference.SetValueWithoutNotify(modifier);
                reference.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue != null && !(evt.newValue is Material) && !(evt.newValue is ShaderFX))
                    {
                        reference.SetValueWithoutNotify(layer.modifiers[index]);
                        return;
                    }
                    Change("Change FX Reference", () => layer.modifiers[index] = evt.newValue);
                });
                toolbar.Add(reference);
                if (effect != null)
                    toolbar.Add(new Button(() => Change("Embed Shader FX", () => owner.EmbedShaderFX(layer, index)))
                    {
                        text = "Embed", tooltip = "Copy this effect into the document. The external asset is not changed."
                    });
            }
            Button up = new Button(() => Move(index, -1)) { text = "↑", tooltip = "Apply earlier" };
            Button down = new Button(() => Move(index, 1)) { text = "↓", tooltip = "Apply later" };
            up.SetEnabled(index > 0);
            down.SetEnabled(index + 1 < layer.modifiers.Count);
            toolbar.Add(up);
            toolbar.Add(down);
            toolbar.Add(new Button(() => Change("Remove FX", () => layer.modifiers.RemoveAt(index))) { text = "×", tooltip = "Remove modifier" });
            card.Add(toolbar);
            if (effect != null)
            {
                if (!embedded)
                    card.Add(new HelpBox("External Shader FX: edits affect every document using this asset.", HelpBoxMessageType.Info));
                Foldout editor = new Foldout { text = "Code & Parameters", value = true };
                editor.Add(ShaderFXEditor.CreateInlineView(effect));
                card.Add(editor);
            }
            entries.Add(card);
        }

        private void Move(int index, int delta)
        {
            Change("Reorder FX", () =>
            {
                UnityEngine.Object effect = layer.modifiers[index];
                layer.modifiers.RemoveAt(index);
                layer.modifiers.Insert(index + delta, effect);
            });
        }

        private void Change(string name, Action change)
        {
            if (owner == null || !owner.TryFindLayer(layer, out _, out _) || SpriteEditorApi.IsLayerContentLocked(owner, layer))
                return;
            applyChange(name, change);
            Refresh();
        }
    }
}
