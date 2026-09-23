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
            entries.AddToClassList("whimtex-layer-fx-entries");
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
            if (effect != null) card.AddToClassList("whimtex-layer-fx-entry--shader");
            if (index > 0 && layer.modifiers[index - 1] is ShaderFX)
                card.AddToClassList("whimtex-layer-fx-entry--after-shader");
            Foldout foldout = null;
            Toggle foldoutToggle = null;
            if (effect != null)
            {
                foldout = new Foldout { value = true };
                foldout.AddToClassList("whimtex-layer-fx-foldout");
                foldout.viewDataKey = $"whimtex-layer-fx-{layer.Id}-{UnityObjectID.FromObject(modifier)}";
                foldoutToggle = foldout.Q<Toggle>();
                foldoutToggle.AddToClassList("whimtex-fx-hidden-foldout-toggle");
            }
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("whimtex-layer-fx-toolbar");
            toolbar.AddToClassList("whimtex-layer-fx-entry-toolbar");
            toolbar.AddToClassList("whimtex-layer-fx-header");
            bool embedded = effect != null && effect.EmbeddedOwner == owner;
            if (effect != null)
            {
                var foldoutButton = new Button { text = foldout.value ? "▾" : "▸", tooltip = "Collapse or expand this FX" };
                foldoutButton.AddToClassList("whimtex-fx-foldout-toggle");
                foldoutButton.clicked += () => foldout.value = !foldout.value;
                foldout.RegisterValueChangedCallback(evt => foldoutButton.text = evt.newValue ? "▾" : "▸");
                foldout.RegisterCallback<AttachToPanelEvent>(_ =>
                    foldout.schedule.Execute(() => foldoutButton.text = foldout.value ? "▾" : "▸"));
                toolbar.Add(foldoutButton);

                var active = new Toggle { tooltip = embedded ? "Enable or disable this FX" : "Enable or disable this shared FX asset" };
                active.AddToClassList("whimtex-fx-visibility-toggle");
                Label activeLabel = active.Q<Label>();
                if (activeLabel != null) activeLabel.AddToClassList("whimtex-fx-hidden-label");
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
            var dragHandle = new Label("⠿") { tooltip = "Drag to reorder this FX" };
            dragHandle.AddToClassList("whimtex-fx-drag-handle");
            dragHandle.SetEnabled(owner != null && !WhimTexApi.IsLayerContentLocked(owner, layer));
            dragHandle.AddManipulator(new ReorderManipulator(index, UpdateDropMarker, MoveTo));
            refreshActivity.Add(() => dragHandle.SetEnabled(owner != null && !WhimTexApi.IsLayerContentLocked(owner, layer)));
            toolbar.Add(dragHandle);
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
            VisualElement body = new VisualElement();
            if (effect != null)
            {
                if (!embedded)
                {
                    var shared = new Label("Shared asset") { tooltip = "Edits affect every document using this asset. Use ⋮ → Embed Copy for an independent copy." };
                    shared.AddToClassList("whimtex-fx-note");
                    body.Add(shared);
                }
                body.Add(ShaderFXEditor.CreateInlineView(effect));
            }
            if (foldout != null)
            {
                foldout.Add(body);
                card.Add(foldout);
            }
            entries.Add(card);
        }

        private void Move(int index, int delta)
        {
            MoveTo(index, index + delta);
        }

        private void MoveTo(int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= layer.modifiers.Count ||
                targetIndex < 0 || targetIndex >= layer.modifiers.Count || sourceIndex == targetIndex)
                return;
            Change("Reorder FX", () =>
            {
                UnityEngine.Object modifier = layer.modifiers[sourceIndex];
                layer.modifiers.RemoveAt(sourceIndex);
                layer.modifiers.Insert(targetIndex, modifier);
            });
        }

        private void UpdateDropMarker(Vector2 panelPosition, bool active)
        {
            foreach (VisualElement child in entries.Children())
            {
                child.RemoveFromClassList("whimtex-layer-fx-entry--drop-before");
                child.RemoveFromClassList("whimtex-layer-fx-entry--drop-after");
            }
            if (!active || entries.childCount == 0)
                return;

            int insertionSlot = entries.childCount;
            for (int i = 0; i < entries.childCount; i++)
            {
                if (panelPosition.y < entries[i].worldBound.center.y)
                {
                    insertionSlot = i;
                    break;
                }
            }
            if (insertionSlot < entries.childCount)
                entries[insertionSlot].AddToClassList("whimtex-layer-fx-entry--drop-before");
            else
                entries[entries.childCount - 1].AddToClassList("whimtex-layer-fx-entry--drop-after");
        }

        private sealed class ReorderManipulator : PointerManipulator
        {
            private readonly int sourceIndex;
            private readonly Action<Vector2, bool> updateDropMarker;
            private readonly Action<int, int> move;
            private Vector2 pointerDownPosition;
            private int pointerId = -1;
            private int targetIndex = -1;
            private bool dragging;
            private Vector2 lastPointerPosition;
            private ScrollView scrollView;
            private IVisualElementScheduledItem autoScrollSchedule;

            internal ReorderManipulator(int sourceIndex, Action<Vector2, bool> updateDropMarker, Action<int, int> move)
            {
                this.sourceIndex = sourceIndex;
                this.updateDropMarker = updateDropMarker;
                this.move = move;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown);
                target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                target.RegisterCallback<PointerUpEvent>(OnPointerUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                ClearDrag();
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointerId != -1)
                    return;
                pointerId = evt.pointerId;
                pointerDownPosition = new Vector2(evt.position.x, evt.position.y);
                targetIndex = sourceIndex;
                target.CapturePointer(pointerId);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;
                Vector2 position = new Vector2(evt.position.x, evt.position.y);
                if (!dragging && (position - pointerDownPosition).sqrMagnitude >= 16f)
                {
                    dragging = true;
                    target.AddToClassList("whimtex-fx-drag-handle--dragging");
                    scrollView = FindScrollView();
                    autoScrollSchedule = target.schedule.Execute(AutoScrollTick).Every(16);
                }
                if (!dragging)
                    return;
                lastPointerPosition = position;
                targetIndex = FindTargetIndex(position);
                updateDropMarker(position, true);
                evt.StopPropagation();
            }

            private ScrollView FindScrollView()
            {
                for (VisualElement current = target; current != null; current = current.parent)
                    if (current is ScrollView scroll)
                        return scroll;
                return null;
            }

            private void AutoScrollTick()
            {
                if (!dragging || scrollView == null)
                    return;

                Rect viewport = scrollView.contentViewport.worldBound;
                const float edgeSize = 36f;
                float direction = 0f;
                float strength = 0f;
                if (lastPointerPosition.y < viewport.yMin + edgeSize)
                {
                    direction = -1f;
                    strength = Mathf.Clamp01((viewport.yMin + edgeSize - lastPointerPosition.y) / edgeSize);
                }
                else if (lastPointerPosition.y > viewport.yMax - edgeSize)
                {
                    direction = 1f;
                    strength = Mathf.Clamp01((lastPointerPosition.y - (viewport.yMax - edgeSize)) / edgeSize);
                }

                if (direction == 0f)
                    return;

                Vector2 offset = scrollView.scrollOffset;
                offset.y += direction * Mathf.Lerp(4f, 18f, strength);
                scrollView.scrollOffset = offset;
                targetIndex = FindTargetIndex(lastPointerPosition);
                updateDropMarker(lastPointerPosition, true);
            }

            private int FindTargetIndex(Vector2 panelPosition)
            {
                VisualElement list = target;
                while (list != null && !list.ClassListContains("whimtex-layer-fx-entries"))
                    list = list.parent;
                if (list == null || list.childCount <= 1)
                    return sourceIndex;

                int insertionSlot = list.childCount;
                for (int i = 0; i < list.childCount; i++)
                {
                    if (panelPosition.y < list[i].worldBound.center.y)
                    {
                        insertionSlot = i;
                        break;
                    }
                }
                int result = insertionSlot > sourceIndex ? insertionSlot - 1 : insertionSlot;
                return Mathf.Clamp(result, 0, list.childCount - 1);
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;
                bool shouldMove = dragging;
                int destination = targetIndex;
                if (target.HasPointerCapture(pointerId))
                    target.ReleasePointer(pointerId);
                ClearDrag();
                if (shouldMove && destination >= 0 && destination != sourceIndex)
                    move(sourceIndex, destination);
                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt) => ClearDrag();

            private void ClearDrag()
            {
                if (target == null)
                    return;
                target.RemoveFromClassList("whimtex-fx-drag-handle--dragging");
                autoScrollSchedule?.Pause();
                autoScrollSchedule = null;
                scrollView = null;
                pointerId = -1;
                targetIndex = -1;
                dragging = false;
                updateDropMarker(default, false);
            }
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
