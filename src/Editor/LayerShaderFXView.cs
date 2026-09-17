using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class LayerShaderFXView : VisualElement
    {
        private readonly Layer layer;
        private readonly TextureCompositor owner;
        private readonly Action<string, Action> applyChange;
        private readonly VisualElement entries = new VisualElement();
        private readonly List<UnityEngine.Object> displayed = new List<UnityEngine.Object>();
        private readonly List<Action> refreshActivity = new List<Action>();

        internal LayerShaderFXView(Layer layer, TextureCompositor owner, Action<string, Action> applyChange)
        {
            this.layer = layer;
            this.owner = owner;
            this.applyChange = applyChange;
            WhimTexUI.ApplyWindowStyles(this);
            AddToClassList("whimtex-layer-fx");
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("whimtex-layer-fx-toolbar");
            toolbar.Add(new Button(() => Change("Add Shader FX", () => owner.AddEmbeddedShaderFX(layer))) { text = "+ Shader FX" });
            toolbar.Add(new Button(() => ShaderFXCatalog.ShowMenu(entry => Change("Add Catalog FX", () => owner.AddCatalogShaderFX(layer, entry)))) { text = "+ Preset ▾", tooltip = "Effects from the project and your user ShaderFX preset folder." });
            toolbar.Add(new Button(() => Change("Add FX Reference", () => layer.modifiers.Add(null))) { text = "+ Reference" });
            Add(toolbar);
            Add(entries);
            Refresh();
        }

        internal void Refresh()
        {
            foreach (var refresh in refreshActivity) refresh();
            bool changed = displayed.Count != layer.modifiers.Count;
            for (int i = 0; !changed && i < displayed.Count; i++)
                changed = displayed[i] != layer.modifiers[i];
            if (!changed)
                return;
            entries.Clear();
            refreshActivity.Clear();
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
            card.AddToClassList("whimtex-layer-fx-entry");
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("whimtex-layer-fx-toolbar");
            bool embedded = effect != null && effect.EmbeddedOwner == owner;
            toolbar.AddToClassList("whimtex-layer-fx-header");
            if (effect != null)
            {
                var active = new Toggle { tooltip = embedded ? "Enable or disable this FX" : "Enable or disable this shared FX asset" };
                active.SetValueWithoutNotify(effect.Active);
                active.RegisterValueChangedCallback(evt =>
                {
                    if (WhimTexApi.IsShaderFXContentLocked(effect))
                    {
                        active.SetValueWithoutNotify(effect.Active);
                        return;
                    }
                    Change("Toggle Shader FX", () =>
                    {
                        Undo.RecordObject(effect, "Toggle Shader FX");
                        effect.Active = evt.newValue;
                    });
                    active.SetValueWithoutNotify(effect.Active);
                });
                toolbar.Add(active);
                refreshActivity.Add(() =>
                {
                    if (effect == null) return;
                    active.SetValueWithoutNotify(effect.Active);
                    active.SetEnabled(!WhimTexApi.IsShaderFXContentLocked(effect));
                });
            }
            if (embedded)
            {
                Label name = new Label($"{index + 1}. {effect.name}");
                name.AddToClassList("whimtex-layer-fx-name");
                toolbar.Add(name);
            }
            else
            {
                ObjectField reference = new ObjectField
                {
                    objectType = typeof(UnityEngine.Object), allowSceneObjects = false
                };
                reference.AddToClassList("whimtex-layer-fx-name");
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
            }
            var actions = new Button(() =>
            {
                var menu = new GenericMenu();
                if (index > 0) menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, -1));
                else menu.AddDisabledItem(new GUIContent("Move Up"));
                if (index + 1 < layer.modifiers.Count) menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, 1));
                else menu.AddDisabledItem(new GUIContent("Move Down"));
                if (effect != null && !embedded)
                    menu.AddItem(new GUIContent("Embed Copy"), false, () => Change("Embed Shader FX", () => owner.EmbedShaderFX(layer, index)));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Remove"), false, () => Change("Remove FX", () => layer.modifiers.RemoveAt(index)));
                menu.ShowAsContext();
            }) { text = "⋮", tooltip = "Reorder, embed or remove this effect" };
            actions.AddToClassList("whimtex-fx-menu-button");
            toolbar.Add(actions);
            card.Add(toolbar);
            if (effect != null)
            {
                if (!embedded)
                {
                    var shared = new Label("Shared asset") { tooltip = "Edits affect every document using this asset. Use ⋮ → Embed Copy for an independent copy." };
                    shared.AddToClassList("whimtex-fx-note");
                    card.Add(shared);
                }
                card.Add(ShaderFXEditor.CreateInlineView(effect));
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
            if (owner == null || !owner.TryFindLayer(layer, out _, out _) || WhimTexApi.IsLayerContentLocked(owner, layer))
                return;
            applyChange(name, change);
            Refresh();
        }
    }
}
