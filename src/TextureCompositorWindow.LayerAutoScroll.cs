using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private LayerDragAutoScrollManipulator layerDragAutoScroll;

        private bool IsLayerListEndDropArea(Vector2 point) =>
            toolkitLayerEndDropZone?.panel != null &&
            IsBelowLayerList(toolkitSettingsScroll.contentViewport.worldBound, toolkitLayerEndDropZone.worldBound.yMin, point);

        private static bool IsBelowLayerList(Rect viewport, float endY, Vector2 point) =>
            viewport.Contains(point) && point.y >= endY;

        private bool UpdateLayerListEndDrop(Vector2 point)
        {
            Layer dragged = GetDraggedLayer();
            if (dragged == null || !IsLayerListEndDropArea(point)) return false;
            bool valid = CanDropLayer(dragged, compositor.layers, compositor.layers.Count);
            if (valid) SetToolkitDropIndicator(toolkitLayerEndDropZone, false, true, 0);
            else ClearToolkitDropIndicator();
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
            return true;
        }

        private sealed class LayerListEndDropManipulator : PointerManipulator
        {
            private readonly TextureCompositorWindow owner;
            internal LayerListEndDropManipulator(TextureCompositorWindow owner) { this.owner = owner; }
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
                if (!owner.CanDropLayer(dragged, owner.compositor.layers, owner.compositor.layers.Count))
                { ClearIndicator(); return; }
                DragAndDrop.AcceptDrag();
                owner.ClearToolkitDropIndicator();
                try { owner.PerformLayerDrop(dragged, owner.compositor.layers, owner.compositor.layers.Count, null); }
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
            private readonly TextureCompositorWindow owner;
            private readonly ScrollView scroll;
            private IVisualElementScheduledItem timer;
            private Vector2 pointer;
            private double lastTick;
            private bool running;

            internal LayerDragAutoScrollManipulator(TextureCompositorWindow owner, ScrollView scroll)
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
            {
                pointer = evt.mousePosition;
                if (owner.GetDraggedLayer() == null || EdgeSpeed(scroll.contentViewport.worldBound, pointer) == 0f)
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
                if (!running || target.panel == null || owner.GetDraggedLayer() == null)
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
                if (owner.UpdateLayerListEndDrop(pointer)) return;
                VisualElement picked = target.panel.Pick(pointer);
                if (picked == null || !scroll.contentViewport.Contains(picked)) return;
                for (VisualElement element = picked; element != null && element != target; element = element.parent)
                {
                    if (element.ClassListContains("whimtex-layer-row") && element.userData is string id)
                    {
                        Layer layer = owner.compositor.FindLayer(id);
                        if (layer != null && owner.compositor.TryFindLayer(layer, out List<Layer> container, out int index) &&
                            owner.TryGetToolkitDrop(element, pointer, layer, container, index,
                                out _, out _, out _, out Layer group, out bool before))
                        {
                            owner.SetToolkitDropIndicator(element, group != null, before, 0);
                            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                        }
                        else
                        {
                            owner.ClearToolkitDropIndicator();
                            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                        }
                        return;
                    }
                    if (element.userData is List<Layer> destination)
                    {
                        if (owner.CanDropLayer(owner.GetDraggedLayer(), destination, destination.Count))
                        {
                            owner.SetToolkitDropIndicator(element, false, true, 0);
                            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                        }
                        else
                        {
                            owner.ClearToolkitDropIndicator();
                            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                        }
                        return;
                    }
                }
                owner.ClearToolkitDropIndicator();
            }
        }
    }
}
