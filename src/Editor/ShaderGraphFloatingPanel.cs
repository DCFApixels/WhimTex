using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    [Flags]
    internal enum ShaderGraphPanelAnchor { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

    [Serializable]
    internal sealed class ShaderGraphPanelPlacement
    {
        public Rect bounds;
        public ShaderGraphPanelAnchor anchor;
        public ShaderGraphPanelPlacement(Rect bounds, ShaderGraphPanelAnchor anchor)
        { this.bounds = bounds; this.anchor = anchor; }
    }

    internal class ShaderGraphFloatingPanel : VisualElement
    {
        internal const float SnapDistance = 16f;
        internal readonly ShaderGraphPanelPlacement Placement;
        private readonly Vector2 minimum;
        private VisualElement viewport;
        internal Rect Bounds { get; private set; }
        internal event Action PlacementChanged;
        internal event Action<ShaderGraphFloatingPanel, Vector2> Dragged;
        internal event Action<ShaderGraphFloatingPanel, Vector2, bool> DragEnded;

        internal ShaderGraphFloatingPanel(ShaderGraphPanelPlacement placement, Vector2 minimum)
        {
            Placement = placement; this.minimum = minimum;
            usageHints = UsageHints.DynamicTransform;
            AddToClassList("whimtex-shader-graph-floating-panel");
            RegisterCallback<PointerDownEvent>(_ => BringToFront(), TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                viewport = parent;
                viewport.RegisterCallback<GeometryChangedEvent>(OnViewportGeometry);
                Place();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                viewport?.UnregisterCallback<GeometryChangedEvent>(OnViewportGeometry);
                viewport = null;
            });
        }

        internal void AddResizeHandles()
        {
            AddHandle("left", 1); AddHandle("right", 2);
            AddHandle("top", 4); AddHandle("bottom", 8);
            AddHandle("top-left", 5); AddHandle("top-right", 6);
            AddHandle("bottom-left", 9); AddHandle("bottom-right", 10);
        }

        private void AddHandle(string side, int edges)
        {
            var handle = new VisualElement { name = "panel-resize-" + side };
            handle.AddToClassList("whimtex-shader-graph-panel-resize");
            handle.AddToClassList("whimtex-shader-graph-panel-resize--" + side);
            handle.AddManipulator(new PanelGesture(this, edges, null, null, null));
            Add(handle);
        }

        internal void AddDragHandle(VisualElement handle, VisualElement ignore = null,
            Func<Vector2, ShaderGraphFloatingPanel> detach = null, Action select = null)
            => handle.AddManipulator(new PanelGesture(this, 0, ignore, detach, select));

        private Vector2 Available => viewport?.contentRect.size ?? Vector2.zero;
        private void OnViewportGeometry(GeometryChangedEvent evt) => Place();

        internal void Place()
        {
            Vector2 available = Available;
            if (!ProjectiveMatrix.Finite(available.x) || !ProjectiveMatrix.Finite(available.y) || available.x <= 0 || available.y <= 0) return;
            Bounds = Fit(Placement.bounds, Placement.anchor, available, minimum);
            var anchor = Placement.anchor;
            EnableInClassList("whimtex-shader-graph-panel--left", (anchor & ShaderGraphPanelAnchor.Left) != 0);
            EnableInClassList("whimtex-shader-graph-panel--right", (anchor & ShaderGraphPanelAnchor.Right) != 0);
            EnableInClassList("whimtex-shader-graph-panel--top", (anchor & ShaderGraphPanelAnchor.Top) != 0);
            EnableInClassList("whimtex-shader-graph-panel--bottom", (anchor & ShaderGraphPanelAnchor.Bottom) != 0);
            style.width = Bounds.width; style.height = Bounds.height;
            style.translate = new Translate(Bounds.x, Bounds.y);
        }

        internal static Rect Fit(Rect bounds, ShaderGraphPanelAnchor anchor, Vector2 available, Vector2 minimum)
        {
            if (!ProjectiveMatrix.Finite(bounds.x)) bounds.x = 0;
            if (!ProjectiveMatrix.Finite(bounds.y)) bounds.y = 0;
            if (!ProjectiveMatrix.Finite(bounds.width)) bounds.width = minimum.x;
            if (!ProjectiveMatrix.Finite(bounds.height)) bounds.height = minimum.y;
            bounds.size = Vector2.Min(Vector2.Max(bounds.size, Vector2.Min(minimum, available)), available);
            if ((anchor & (ShaderGraphPanelAnchor.Left | ShaderGraphPanelAnchor.Right)) == (ShaderGraphPanelAnchor.Left | ShaderGraphPanelAnchor.Right))
                bounds.width = available.x;
            if ((anchor & (ShaderGraphPanelAnchor.Top | ShaderGraphPanelAnchor.Bottom)) == (ShaderGraphPanelAnchor.Top | ShaderGraphPanelAnchor.Bottom))
                bounds.height = available.y;
            Vector2 room = available - bounds.size;
            if ((anchor & ShaderGraphPanelAnchor.Left) != 0) bounds.x = 0;
            else if ((anchor & ShaderGraphPanelAnchor.Right) != 0) bounds.x = room.x;
            if ((anchor & ShaderGraphPanelAnchor.Top) != 0) bounds.y = 0;
            else if ((anchor & ShaderGraphPanelAnchor.Bottom) != 0) bounds.y = room.y;
            bounds.x = Mathf.Clamp(bounds.x, 0, room.x); bounds.y = Mathf.Clamp(bounds.y, 0, room.y);
            return bounds;
        }

        internal void Drag(Rect start, Vector2 delta, int edges)
        {
            Rect next = start;
            if (edges == 0)
            {
                Vector2 position = start.position + delta, room = Available - next.size;
                Placement.anchor = SnapAxis(ref position.x, room.x, ShaderGraphPanelAnchor.Left, ShaderGraphPanelAnchor.Right)
                    | SnapAxis(ref position.y, room.y, ShaderGraphPanelAnchor.Top, ShaderGraphPanelAnchor.Bottom);
                next.position = position;
            }
            else
            {
                Placement.anchor &= ~(ShaderGraphPanelAnchor)edges;
                Vector2 min = Vector2.Min(minimum, Available), available = Available;
                if ((edges & 1) != 0) next.xMin = Mathf.Clamp(start.xMin + delta.x, 0, start.xMax - min.x);
                if ((edges & 2) != 0) next.xMax = Mathf.Clamp(start.xMax + delta.x, start.xMin + min.x, available.x);
                if ((edges & 4) != 0) next.yMin = Mathf.Clamp(start.yMin + delta.y, 0, start.yMax - min.y);
                if ((edges & 8) != 0) next.yMax = Mathf.Clamp(start.yMax + delta.y, start.yMin + min.y, available.y);
                if ((edges & 1) != 0 && next.xMin <= SnapDistance)
                { next.xMin = 0; Placement.anchor |= ShaderGraphPanelAnchor.Left; }
                if ((edges & 2) != 0 && available.x - next.xMax <= SnapDistance)
                { next.xMax = available.x; Placement.anchor |= ShaderGraphPanelAnchor.Right; }
                if ((edges & 4) != 0 && next.yMin <= SnapDistance)
                { next.yMin = 0; Placement.anchor |= ShaderGraphPanelAnchor.Top; }
                if ((edges & 8) != 0 && available.y - next.yMax <= SnapDistance)
                { next.yMax = available.y; Placement.anchor |= ShaderGraphPanelAnchor.Bottom; }
            }
            Placement.bounds = next; Place(); Placement.bounds = Bounds;
        }

        private static ShaderGraphPanelAnchor SnapAxis(ref float position, float room, ShaderGraphPanelAnchor near, ShaderGraphPanelAnchor far)
        {
            room = Mathf.Max(0, room); position = Mathf.Clamp(position, 0, room);
            if (Mathf.Min(position, room - position) > SnapDistance) return ShaderGraphPanelAnchor.None;
            if (position <= room - position) { position = 0; return near; }
            position = room; return far;
        }

        private sealed class PanelGesture : PointerManipulator
        {
            private readonly ShaderGraphFloatingPanel owner;
            private readonly int edges;
            private readonly VisualElement ignore;
            private readonly Func<Vector2, ShaderGraphFloatingPanel> detach;
            private readonly Action select;
            private ShaderGraphFloatingPanel moving;
            private VisualElement capture;
            private VisualElement keyboardRoot;
            private int pointer = -1;
            private Vector2 origin, downPosition;
            private Rect start;
            private ShaderGraphPanelAnchor startAnchor;
            private bool dragging;

            internal PanelGesture(ShaderGraphFloatingPanel owner, int edges, VisualElement ignore,
                Func<Vector2, ShaderGraphFloatingPanel> detach, Action select)
            { this.owner = owner; this.edges = edges; this.ignore = ignore; this.detach = detach; this.select = select; }
            protected override void RegisterCallbacksOnTarget()
                => target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            protected override void UnregisterCallbacksFromTarget()
            { Finish(Vector2.zero, true); target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown); }

            private void Down(PointerDownEvent e)
            {
                if (e.button != 0 || pointer >= 0 || owner.viewport == null) return;
                var element = e.target as VisualElement;
                if (ignore != null && (element == ignore || ignore.Contains(element))) return;
                if (edges == 0 && element?.ClassListContains("whimtex-shader-graph-panel-resize") == true) return;
                select?.Invoke();
                moving = owner; capture = owner.viewport; pointer = e.pointerId; dragging = false;
                downPosition = e.position; origin = capture.WorldToLocal(e.position);
                start = owner.Bounds; startAnchor = owner.Placement.anchor;
                owner.BringToFront();
                capture.RegisterCallback<PointerMoveEvent>(Move);
                capture.RegisterCallback<PointerUpEvent>(Up);
                capture.RegisterCallback<PointerCaptureOutEvent>(Lost);
                keyboardRoot = capture.panel.visualTree;
                keyboardRoot.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                capture.RegisterCallback<DetachFromPanelEvent>(Detached);
                capture.CapturePointer(pointer); e.StopImmediatePropagation();
            }

            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || !capture.HasPointerCapture(pointer)) return;
                Vector2 delta = capture.WorldToLocal(e.position) - origin;
                if (!dragging && delta.sqrMagnitude >= (edges == 0 ? 16 : 1))
                {
                    dragging = true;
                    moving = detach?.Invoke(downPosition) ?? owner;
                    moving.Place(); start = moving.Bounds; startAnchor = moving.Placement.anchor;
                    moving.BringToFront();
                }
                if (dragging)
                {
                    moving.Drag(start, delta, edges);
                    if (edges == 0) moving.Dragged?.Invoke(moving, e.position);
                }
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e)
            { if (e.pointerId == pointer && e.button == 0) { Finish(e.position, false); e.StopPropagation(); } }
            private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) Finish(Vector2.zero, true); }
            private void Detached(DetachFromPanelEvent e) { if (e.target == capture) Finish(Vector2.zero, true); }
            private void Key(KeyDownEvent e)
            { if (e.keyCode == KeyCode.Escape) { Finish(Vector2.zero, true); e.StopImmediatePropagation(); } }

            private void Finish(Vector2 position, bool cancelled)
            {
                if (pointer < 0) return;
                int id = pointer; pointer = -1;
                var root = capture; capture = null;
                root.UnregisterCallback<PointerMoveEvent>(Move);
                root.UnregisterCallback<PointerUpEvent>(Up);
                root.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                keyboardRoot.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown); keyboardRoot = null;
                root.UnregisterCallback<DetachFromPanelEvent>(Detached);
                if (root.HasPointerCapture(id)) root.ReleasePointer(id);
                if (!dragging) return;
                if (cancelled) { moving.Placement.bounds = start; moving.Placement.anchor = startAnchor; moving.Place(); }
                if (edges == 0) moving.DragEnded?.Invoke(moving, position, cancelled);
                moving.PlacementChanged?.Invoke();
            }
        }
    }
}
