using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private LayerDragAutoScrollManipulator layerDragAutoScroll;

        private bool IsLayerListEndDropArea(Vector2 point) =>
            toolkitLayerEndDropZone?.panel != null &&
            IsBelowLayerList(toolkitSettingsScroll.contentViewport.worldBound,
                activeDocument.layers.Count == 0 ? toolkitLayerHierarchyRoot.worldBound.yMin : toolkitLayerEndDropZone.worldBound.yMin, point);

        private static bool IsBelowLayerList(Rect viewport, float endY, Vector2 point) =>
            viewport.Contains(point) && point.y >= endY;

        private bool UpdateLayerListEndDrop(Vector2 point)
        {
            Layer dragged = GetDraggedLayer();
            if (dragged == null || !IsLayerListEndDropArea(point)) return false;
            bool valid = CanDropLayer(dragged, activeDocument.layers, activeDocument.layers.Count);
            if (valid) SetToolkitDropIndicator(toolkitLayerEndDropZone, false, true, 0);
            else ClearToolkitDropIndicator();
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
            return true;
        }

        private sealed class LayerListEndDropManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            internal LayerListEndDropManipulator(WhimTexWindow owner) { this.owner = owner; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnUpdated);
                target.RegisterCallback<DragPerformEvent>(OnPerform);
                target.RegisterCallback<DragLeaveEvent>(OnLeave);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<DragUpdatedEvent>(OnUpdated);
                target.UnregisterCallback<DragPerformEvent>(OnPerform);
                target.UnregisterCallback<DragLeaveEvent>(OnLeave);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                ClearIndicator();
            }
            private void OnUpdated(DragUpdatedEvent evt)
            {
                if (owner.UpdateLayerListEndDrop(evt.mousePosition)) evt.StopImmediatePropagation();
            }
            private void OnPerform(DragPerformEvent evt)
            {
                Layer dragged = owner.GetDraggedLayer();
                if (dragged == null || !owner.IsLayerListEndDropArea(evt.mousePosition)) return;
                evt.StopImmediatePropagation();
                if (!owner.CanDropLayer(dragged, owner.activeDocument.layers, owner.activeDocument.layers.Count))
                { ClearIndicator(); return; }
                DragAndDrop.AcceptDrag();
                owner.ClearToolkitDropIndicator();
                try { owner.PerformLayerDrop(dragged, owner.activeDocument.layers, owner.activeDocument.layers.Count, null); }
                finally { owner.ClearLayerDragData(); }
            }
            private void ClearIndicator()
            {
                if (owner.activeDropElement == owner.toolkitLayerEndDropZone) owner.ClearToolkitDropIndicator();
            }
            private void OnLeave(DragLeaveEvent evt) => ClearIndicator();
            private void OnDetach(DetachFromPanelEvent evt) => ClearIndicator();
        }

        private sealed class LayerDragAutoScrollManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private readonly ScrollView scroll;
            private IVisualElementScheduledItem timer;
            private Vector2 pointer;
            private double lastTick;
            private bool running;

            internal LayerDragAutoScrollManipulator(WhimTexWindow owner, ScrollView scroll)
            {
                this.owner = owner;
                this.scroll = scroll;
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
                target.RegisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
                target.RegisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
                target.RegisterCallback<DragLeaveEvent>(OnDragLeave);
                target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Stop();
                timer = null;
                target.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragExitedEvent>(OnDragExited, TrickleDown.TrickleDown);
                target.UnregisterCallback<DragLeaveEvent>(OnDragLeave);
                target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            }

            private void OnDragUpdated(DragUpdatedEvent evt)
                => UpdatePointer(evt.mousePosition);

            internal void UpdatePointer(Vector2 position)
            {
                pointer = position;
                if (!HasScrollableDrag() || EdgeSpeed(scroll.contentViewport.worldBound, pointer) == 0f)
                {
                    Stop();
                    return;
                }
                if (running) return;
                running = true;
                lastTick = EditorApplication.timeSinceStartup;
                if (timer == null) timer = target.schedule.Execute(Tick).Every(16);
                else timer.Resume();
            }

            internal void Stop()
            {
                running = false;
                timer?.Pause();
            }

            private bool HasScrollableDrag() => owner.GetDraggedLayer() != null ||
                owner.IsCrossWindowLayerDrag() || owner.HasFileTextureDrag();

            private void OnDragPerform(DragPerformEvent evt) => Stop();
            private void OnDragExited(DragExitedEvent evt) => Stop();
            private void OnDragLeave(DragLeaveEvent evt) { if (evt.target == target) Stop(); }
            private void OnDetach(DetachFromPanelEvent evt) { if (evt.target == target) Stop(); }

            internal static float EdgeSpeed(Rect viewport, Vector2 point)
            {
                if (viewport.width <= 0f || viewport.height <= 0f || !viewport.Contains(point)) return 0f;
                float band = Mathf.Min(32f, viewport.height * .25f);
                float top = point.y - viewport.yMin, bottom = viewport.yMax - point.y;
                if (top < band) return -480f * (1f - top / band);
                if (bottom < band) return 480f * (1f - bottom / band);
                return 0f;
            }

            internal static float ClampOffset(float value, float low, float high)
            {
                // A ScrollView whose content fits can expose a negative highValue.
                // Clamping to that inverted range translates the entire list downwards.
                float minimum = Mathf.Max(0f, low);
                return Mathf.Clamp(value, minimum, Mathf.Max(minimum, high));
            }

            private void Tick()
            {
                if (!running || target.panel == null || !HasScrollableDrag())
                {
                    Stop();
                    return;
                }
                float speed = EdgeSpeed(scroll.contentViewport.worldBound, pointer);
                if (speed == 0f) { Stop(); return; }
                double now = EditorApplication.timeSinceStartup;
                float seconds = Mathf.Clamp((float)(now - lastTick), 0f, .05f);
                lastTick = now;
                RefreshDropIndicator();
                Vector2 offset = scroll.scrollOffset;
                offset.y = ClampOffset(offset.y + speed * seconds,
                    scroll.verticalScroller.lowValue, scroll.verticalScroller.highValue);
                if (offset != scroll.scrollOffset) scroll.scrollOffset = offset;
            }

            private void RefreshDropIndicator()
            {
                if (owner.IsCrossWindowLayerDrag()) { owner.UpdateCrossWindowLayerDrop(pointer); return; }
                if (owner.HasFileTextureDrag())
                {
                    if (owner.TryResolveLayerDropTarget(pointer, out var textureDrop))
                    { owner.ShowLayerDropTarget(textureDrop); DragAndDrop.visualMode = DragAndDropVisualMode.Copy; }
                    else { owner.ClearToolkitDropIndicator(); DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; }
                    return;
                }
                if (owner.UpdateLayerListEndDrop(pointer)) return;
                Layer dragged = owner.GetDraggedLayer();
                if (dragged != null && owner.TryResolveLayerDropTarget(pointer, out var drop) &&
                    owner.CanDropLayer(dragged, drop.Container, drop.Index))
                {
                    owner.ShowLayerDropTarget(drop);
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                }
                else
                {
                    owner.ClearToolkitDropIndicator();
                    DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                }
            }
        }
    }
}
