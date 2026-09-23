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
        private readonly Button pasteButton;
        private readonly List<UnityEngine.Object> displayed = new List<UnityEngine.Object>();
        private readonly List<Action> refreshActivity = new List<Action>();
        private VisualElement activeLayerDropMarker;

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
            pasteButton = new Button(PasteAtEnd) { text = "Paste FX", tooltip = "Paste a copied FX block at the end of this stack." };
            toolbar.Add(pasteButton);
            RefreshClipboardActionState();
            RegisterCallback<AttachToPanelEvent>(_ => ShaderFXClipboard.Changed += RefreshClipboardActionState);
            RegisterCallback<DetachFromPanelEvent>(_ => ShaderFXClipboard.Changed -= RefreshClipboardActionState);
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
            var dragHandle = new Label("⠿") { tooltip = "Drag the header to reorder this FX or move it to another layer" };
            dragHandle.AddToClassList("whimtex-fx-drag-handle");
            dragHandle.SetEnabled(owner != null && !WhimTexApi.IsLayerContentLocked(owner, layer));
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
                if (effect != null || modifier is Material)
                    menu.AddItem(new GUIContent("Copy FX"), false, () => ShaderFXClipboard.Copy(owner, modifier));
                else
                    menu.AddDisabledItem(new GUIContent("Copy FX"));
                if (ShaderFXClipboard.Current != null)
                    menu.AddItem(new GUIContent("Paste FX As New"), false, () => PasteAt(index + 1));
                else
                    menu.AddDisabledItem(new GUIContent("Paste FX As New"));
                menu.AddSeparator(string.Empty);
                if (index > 0) menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, -1));
                else menu.AddDisabledItem(new GUIContent("Move Up"));
                if (index + 1 < layer.modifiers.Count) menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, 1));
                else menu.AddDisabledItem(new GUIContent("Move Down"));
                if (effect != null && !embedded)
                    menu.AddItem(new GUIContent("Embed Copy"), false, () => Change("Embed Shader FX", () => owner.EmbedShaderFX(layer, index)));
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Remove"), false, () => Change("Remove FX", () => layer.modifiers.RemoveAt(index)));
                menu.ShowAsContext();
            }) { tooltip = "Copy, paste, reorder, embed or remove this effect" };
            actions.AddToClassList("whimtex-layer-menu-button");
            actions.EnableInClassList("whimtex-layer-menu-button--light", !EditorGUIUtility.isProSkin);
            for (int i = 0; i < 3; i++)
            {
                VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("whimtex-layer-menu-dot");
                actions.Add(dot);
            }
            toolbar.Add(actions);
            toolbar.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                DropdownMenu menu = evt.menu;
                bool canEdit = owner != null && !WhimTexApi.IsLayerContentLocked(owner, layer);
                if (effect != null || modifier is Material)
                    menu.AppendAction("Copy FX", _ => ShaderFXClipboard.Copy(owner, modifier));
                else
                    menu.AppendAction("Copy FX", _ => { }, DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Paste FX As New", _ => PasteAt(index + 1),
                    ShaderFXClipboard.Current != null && canEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendSeparator();
                menu.AppendAction("Move Up", _ => Move(index, -1),
                    canEdit && index > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction("Move Down", _ => Move(index, 1),
                    canEdit && index + 1 < layer.modifiers.Count ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                if (effect != null && !embedded)
                    menu.AppendAction("Embed Copy", _ => Change("Embed Shader FX", () => owner.EmbedShaderFX(layer, index)),
                        canEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendSeparator();
                menu.AppendAction("Remove", _ => Change("Remove FX", () => layer.modifiers.RemoveAt(index)),
                    canEdit ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
            toolbar.AddManipulator(new ReorderManipulator(
                index, dragHandle, UpdateDropMarker, MoveTo, CanReorderLayer,
                FindLayerDropTarget, CanMoveToLayer, UpdateLayerDropMarker, MoveToLayer));
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

        private void PasteAtEnd() => PasteAt(layer.modifiers.Count);

        private void PasteAt(int insertionIndex)
        {
            if (ShaderFXClipboard.Current == null)
                return;

            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paste FX");
            try
            {
                Change("Paste FX", () =>
                {
                    UnityEngine.Object pasted = ShaderFXClipboard.CreatePasteValue(owner);
                    if (pasted == null)
                        return;
                    int destinationIndex = Mathf.Clamp(insertionIndex, 0, layer.modifiers.Count);
                    if (pasted is ShaderFX effect)
                        owner.AdoptAgentShaderFX(effect, "Paste FX");
                    layer.modifiers.Insert(destinationIndex, pasted);
                });
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.IncrementCurrentGroup();
            }
        }

        private void RefreshClipboardActionState()
        {
            if (pasteButton != null)
                pasteButton.SetEnabled(ShaderFXClipboard.Current != null &&
                    owner != null && !WhimTexApi.IsLayerContentLocked(owner, layer));
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

        private LayerDropTarget FindLayerDropTarget(Vector2 panelPosition)
        {
            VisualElement element = panel?.Pick(panelPosition);
            while (element != null && !element.ClassListContains("whimtex-layer-row"))
                element = element.parent;
            if (element == null)
                return default;

            Layer destination = element.userData is string id ? owner?.FindLayer(id) : null;
            return new LayerDropTarget(destination, element, true);
        }

        private bool CanMoveToLayer(Layer destination)
        {
            return CanReorderLayer() && destination != null && !ReferenceEquals(destination, layer) &&
                   owner.TryFindLayer(destination, out _, out _) &&
                   !WhimTexApi.IsLayerContentLocked(owner, destination);
        }

        private bool CanReorderLayer()
        {
            return owner != null && owner.TryFindLayer(layer, out _, out _) &&
                   !WhimTexApi.IsLayerContentLocked(owner, layer);
        }

        private void UpdateLayerDropMarker(VisualElement row)
        {
            if (activeLayerDropMarker == row)
                return;
            activeLayerDropMarker?.RemoveFromClassList("whimtex-layer-row--drop-fx");
            activeLayerDropMarker = row;
            activeLayerDropMarker?.AddToClassList("whimtex-layer-row--drop-fx");
        }

        private void MoveToLayer(int sourceIndex, Layer destination)
        {
            if (!CanMoveToLayer(destination) || sourceIndex < 0 || sourceIndex >= layer.modifiers.Count)
                return;

            UnityEngine.Object modifier = layer.modifiers[sourceIndex];
            Change("Move FX to Layer", () =>
            {
                if (!CanMoveToLayer(destination) || sourceIndex >= layer.modifiers.Count ||
                    !ReferenceEquals(layer.modifiers[sourceIndex], modifier))
                    return;
                layer.modifiers.RemoveAt(sourceIndex);
                destination.modifiers ??= new List<UnityEngine.Object>();
                destination.modifiers.Add(modifier);
            });
        }

        internal readonly struct LayerDropTarget
        {
            internal readonly Layer Layer;
            internal readonly VisualElement Row;
            internal readonly bool IsLayerRow;

            internal LayerDropTarget(Layer layer, VisualElement row, bool isLayerRow)
            {
                Layer = layer;
                Row = row;
                IsLayerRow = isLayerRow;
            }
        }

        private sealed class ReorderManipulator : PointerManipulator
        {
            private readonly int sourceIndex;
            private readonly VisualElement dragHandle;
            private readonly Action<Vector2, bool> updateDropMarker;
            private readonly Action<int, int> move;
            private readonly Func<bool> canReorder;
            private readonly Func<Vector2, LayerDropTarget> findLayerDropTarget;
            private readonly Func<Layer, bool> canMoveToLayer;
            private readonly Action<VisualElement> updateLayerDropMarker;
            private readonly Action<int, Layer> moveToLayer;
            private VisualElement eventRoot;
            private EditorWindow dragWindow;
            private Vector2 pointerDownPosition;
            private int pointerId = -1;
            private int targetIndex = -1;
            private Layer destinationLayer;
            private bool overLayerRow;
            private bool dragging;
            private Vector2 lastPointerPosition;
            private ScrollView scrollView;
            private IVisualElementScheduledItem autoScrollSchedule;

            internal ReorderManipulator(
                int sourceIndex,
                VisualElement dragHandle,
                Action<Vector2, bool> updateDropMarker,
                Action<int, int> move,
                Func<bool> canReorder,
                Func<Vector2, LayerDropTarget> findLayerDropTarget,
                Func<Layer, bool> canMoveToLayer,
                Action<VisualElement> updateLayerDropMarker,
                Action<int, Layer> moveToLayer)
            {
                this.sourceIndex = sourceIndex;
                this.dragHandle = dragHandle;
                this.updateDropMarker = updateDropMarker;
                this.move = move;
                this.canReorder = canReorder;
                this.findLayerDropTarget = findLayerDropTarget;
                this.canMoveToLayer = canMoveToLayer;
                this.updateLayerDropMarker = updateLayerDropMarker;
                this.moveToLayer = moveToLayer;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(OnPanelPointerMove);
                target.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
                target.RegisterCallback<PointerCancelEvent>(OnPanelPointerCancel);
                target.RegisterCallback<PointerCaptureOutEvent>(OnPanelPointerCaptureOut);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPanelPointerMove);
                target.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp);
                target.UnregisterCallback<PointerCancelEvent>(OnPanelPointerCancel);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnPanelPointerCaptureOut);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                ClearDrag();
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || pointerId != -1 || !canReorder())
                    return;
                for (VisualElement element = evt.target as VisualElement; element != null && element != target; element = element.parent)
                    if (element is Button || element is Toggle || element is ObjectField)
                        return;
                pointerId = evt.pointerId;
                pointerDownPosition = new Vector2(evt.position.x, evt.position.y);
                targetIndex = sourceIndex;
                eventRoot = target.panel?.visualTree;
                if (eventRoot == null)
                {
                    pointerId = -1;
                    return;
                }
                eventRoot.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
                eventRoot.RegisterCallback<MouseLeaveWindowEvent>(OnMouseLeaveWindow);
                dragWindow = EditorWindow.focusedWindow;
                EditorApplication.update += CheckWindowFocus;
                target.CapturePointer(pointerId);
                evt.StopPropagation();
            }

            private void OnPanelPointerMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;
                if ((evt.pressedButtons & 1) == 0)
                {
                    ClearDrag();
                    return;
                }
                Vector2 position = new Vector2(evt.position.x, evt.position.y);
                if (!dragging && (position - pointerDownPosition).sqrMagnitude >= 16f)
                {
                    if (!canReorder())
                    {
                        ClearDrag();
                        return;
                    }
                    dragging = true;
                    dragHandle.AddToClassList("whimtex-fx-drag-handle--dragging");
                    scrollView = FindScrollView(target);
                    autoScrollSchedule = target.schedule.Execute(AutoScrollTick).Every(16);
                }
                if (!dragging)
                    return;
                lastPointerPosition = position;
                UpdateDragTarget(position);
                evt.StopPropagation();
            }

            private void OnPanelPointerUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;
                bool shouldMove = dragging;
                int destination;
                Layer destinationLayerAtRelease;
                bool releasedOverLayerRow;
                try
                {
                    if (dragging)
                        UpdateDragTarget(new Vector2(evt.position.x, evt.position.y));
                    destination = targetIndex;
                    destinationLayerAtRelease = destinationLayer;
                    releasedOverLayerRow = overLayerRow;
                }
                finally
                {
                    ClearDrag();
                    evt.StopPropagation();
                }
                if (shouldMove)
                {
                    if (destinationLayerAtRelease != null)
                        moveToLayer(sourceIndex, destinationLayerAtRelease);
                    else if (!releasedOverLayerRow && destination >= 0 && destination != sourceIndex)
                        move(sourceIndex, destination);
                    evt.StopPropagation();
                }
            }

            private void OnPanelPointerCancel(PointerCancelEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;
                bool wasDragging = dragging;
                ClearDrag();
                if (wasDragging)
                    evt.StopPropagation();
            }

            private void OnPanelPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId == pointerId && evt.target == target)
                    ClearDrag();
            }

            private void OnDetach(DetachFromPanelEvent evt) => ClearDrag();

            private void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.keyCode != KeyCode.Escape || pointerId < 0)
                    return;
                ClearDrag();
                evt.StopPropagation();
            }

            private void OnMouseLeaveWindow(MouseLeaveWindowEvent evt) => ClearDrag();

            private void CheckWindowFocus()
            {
                if (target.panel == null || EditorWindow.focusedWindow != dragWindow)
                    ClearDrag();
            }

            private void UpdateDragTarget(Vector2 panelPosition)
            {
                LayerDropTarget candidate = findLayerDropTarget(panelPosition);
                overLayerRow = candidate.IsLayerRow;
                destinationLayer = overLayerRow && canMoveToLayer(candidate.Layer)
                    ? candidate.Layer
                    : null;

                if (overLayerRow)
                {
                    targetIndex = -1;
                    updateDropMarker(default, false);
                    updateLayerDropMarker(destinationLayer != null ? candidate.Row : null);
                    scrollView = FindScrollView(candidate.Row);
                    return;
                }

                destinationLayer = null;
                updateLayerDropMarker(null);
                scrollView = FindScrollView(target);
                bool overStack = scrollView != null && scrollView.contentViewport.worldBound.Contains(panelPosition);
                targetIndex = overStack ? FindTargetIndex(panelPosition) : -1;
                updateDropMarker(panelPosition, overStack);
                if (!overStack)
                    scrollView = null;
            }

            private static ScrollView FindScrollView(VisualElement from)
            {
                for (VisualElement current = from; current != null; current = current.parent)
                    if (current is ScrollView scroll)
                        return scroll;
                return null;
            }

            private void AutoScrollTick()
            {
                if (!dragging || scrollView == null)
                    return;

                Rect viewport = scrollView.contentViewport.worldBound;
                if (!viewport.Contains(lastPointerPosition))
                    return;
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
                float previousY = offset.y;
                offset.y += direction * Mathf.Lerp(4f, 18f, strength);
                scrollView.scrollOffset = offset;
                if (scrollView.scrollOffset.y != previousY)
                    UpdateDragTarget(lastPointerPosition);
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

            private void ClearDrag()
            {
                int activePointerId = pointerId;
                pointerId = -1;
                EditorApplication.update -= CheckWindowFocus;
                dragWindow = null;
                autoScrollSchedule?.Pause();
                autoScrollSchedule = null;
                dragging = false;
                if (eventRoot != null)
                {
                    eventRoot.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
                    eventRoot.UnregisterCallback<MouseLeaveWindowEvent>(OnMouseLeaveWindow);
                    eventRoot = null;
                }
                if (activePointerId >= 0 && target != null && target.HasPointerCapture(activePointerId))
                    target.ReleasePointer(activePointerId);
                if (dragHandle != null)
                    dragHandle.RemoveFromClassList("whimtex-fx-drag-handle--dragging");
                scrollView = null;
                targetIndex = -1;
                destinationLayer = null;
                overLayerRow = false;
                updateLayerDropMarker(null);
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
