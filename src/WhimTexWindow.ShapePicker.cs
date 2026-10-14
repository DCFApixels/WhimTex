using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private ShapePickerManipulator shapePicker;
        private ShapePickerManipulator marqueePicker;
        private CropPickerManipulator cropPicker;
        private CanvasToolIcon marqueeToolIcon;
        private CanvasToolIcon cropToolIcon;
        private ShapeToolIcon shapeToolIcon;

        private sealed class ToolDropdownMarker : VisualElement
        {
            internal ToolDropdownMarker()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-dropdown-marker");
                generateVisualContent += Draw;
            }
            private void Draw(MeshGenerationContext context)
            {
                Rect r = contentRect;
                if (r.width <= 0f || r.height <= 0f) return;
                var p = context.painter2D;
                p.fillColor = resolvedStyle.color;
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMax, r.yMin));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.LineTo(new Vector2(r.xMin, r.yMax));
                p.ClosePath();
                p.Fill();
            }
        }

        private sealed class ShapeToolIcon : VisualElement
        {
            private ShapeLayerBehaviour.ShapeKind kind;
            internal ShapeToolIcon(ShapeLayerBehaviour.ShapeKind kind)
            {
                this.kind = kind;
                pickingMode = PickingMode.Ignore;
                AddToClassList("whimtex-tool-icon");
                generateVisualContent += Draw;
            }
            internal void SetKind(ShapeLayerBehaviour.ShapeKind value)
            {
                if (kind == value) return;
                kind = value;
                MarkDirtyRepaint();
            }
            private void Draw(MeshGenerationContext context)
            {
                if (contentRect.width < 1f || contentRect.height < 1f) return;
                var p = context.painter2D;
                p.strokeColor = resolvedStyle.color;
                p.lineWidth = 1.4f;
                p.lineJoin = LineJoin.Round;
                p.lineCap = LineCap.Round;
                Vector2 center = contentRect.center;
                float radius = Mathf.Min(contentRect.width, contentRect.height) * .37f;
                p.BeginPath();
                if (kind == ShapeLayerBehaviour.ShapeKind.Line)
                {
                    p.MoveTo(center + new Vector2(-radius, radius));
                    p.LineTo(center + new Vector2(radius, -radius));
                }
                else if (kind == ShapeLayerBehaviour.ShapeKind.Arc || kind == ShapeLayerBehaviour.ShapeKind.Sector)
                {
                    p.Arc(center, radius, -60f, 200f);
                    if (kind == ShapeLayerBehaviour.ShapeKind.Sector) { p.LineTo(center); p.ClosePath(); }
                }
                else if (kind == ShapeLayerBehaviour.ShapeKind.Ellipse)
                {
                    p.Arc(center, radius, 0f, 360f);
                    p.ClosePath();
                }
                else
                {
                    int count = kind == ShapeLayerBehaviour.ShapeKind.Rectangle ? 4 : kind == ShapeLayerBehaviour.ShapeKind.Star ? 10 : 5;
                    for (int i = 0; i < count; i++)
                    {
                        float a = -Mathf.PI * .5f + i * Mathf.PI * 2f / count;
                        float r = kind == ShapeLayerBehaviour.ShapeKind.Star && (i & 1) != 0 ? radius * .45f : radius;
                        Vector2 point = kind == ShapeLayerBehaviour.ShapeKind.Rectangle
                            ? center + new Vector2(i == 0 || i == 3 ? -radius : radius, i < 2 ? -radius : radius)
                            : center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                        if (i == 0) p.MoveTo(point); else p.LineTo(point);
                    }
                    p.ClosePath();
                }
                p.Stroke();
            }
        }

        private sealed class ShapePickerManipulator : ToolSubtypePickerManipulator
        {
            private readonly ShapeLayerBehaviour.ShapeKind[] kinds;
            private readonly bool marquee;

            internal ShapePickerManipulator(WhimTexWindow owner, bool marquee = false)
                : base(owner, marquee ? 2 : Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind)).Length)
            {
                this.marquee = marquee;
                kinds = marquee
                    ? new[] { ShapeLayerBehaviour.ShapeKind.Rectangle, ShapeLayerBehaviour.ShapeKind.Ellipse }
                    : (ShapeLayerBehaviour.ShapeKind[])Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind));
            }

            protected override string MenuName => marquee ? "marqueePicker" : "shapePicker";
            protected override bool IsSelected(int index) => marquee
                ? (int)owner.marqueeShape == index : owner.shapeToolSettings.kind == kinds[index];
            protected override string ItemTooltip(int index) => kinds[index].ToString();
            protected override VisualElement ItemIcon(int index) => marquee
                ? new CanvasToolIcon(CanvasTool.RectangleSelect, index == 1) : new ShapeToolIcon(kinds[index]);
            protected override void Select(int index)
            {
                if (marquee)
                {
                    owner.areaSelectionManipulator?.Cancel();
                    owner.marqueeShape = (MarqueeShape)index;
                }
                else
                {
                    owner.shapeManipulator?.Cancel();
                    owner.shapeToolSettings.kind = kinds[index];
                }
            }
            protected override void Activate() => owner.SetCanvasTool(marquee ? CanvasTool.RectangleSelect : CanvasTool.Shape);
        }

        private sealed class CropPickerManipulator : ToolSubtypePickerManipulator
        {
            internal CropPickerManipulator(WhimTexWindow owner) : base(owner, 2) { }
            protected override string MenuName => "cropPicker";
            protected override string ItemName(int index) => index == 0 ? "cropScale" : "cropResize";
            protected override string ItemTooltip(int index) => "Crop: " + (CanvasCropMode)index;
            protected override bool IsSelected(int index) => owner.canvasCropMode == (CanvasCropMode)index;
            protected override VisualElement ItemIcon(int index) => new CanvasToolIcon(CanvasTool.Crop, cropMode: (CanvasCropMode)index);
            protected override void Select(int index) => owner.SetCanvasCropMode((CanvasCropMode)index);
            protected override void Activate() => owner.SetCanvasTool(CanvasTool.Crop);
        }

        private abstract class ToolSubtypePickerManipulator : PointerManipulator
        {
            private const int HoldMilliseconds = 160;
            private const float DragDistance = 3f;
            private const float ItemSize = 30f, Inset = 3f;
            protected readonly WhimTexWindow owner;
            private readonly VisualElement[] items;
            private VisualElement menu, root;
            private IVisualElementScheduledItem hold;
            private int pointer = -1, hovered = -1;
            private Vector2 press, current, menuPosition;
            internal bool IsPressed => pointer >= 0;

            protected ToolSubtypePickerManipulator(WhimTexWindow owner, int itemCount)
            {
                this.owner = owner;
                items = new VisualElement[itemCount];
            }
            protected abstract string MenuName { get; }
            protected virtual string ItemName(int index) => string.Empty;
            protected abstract string ItemTooltip(int index);
            protected abstract bool IsSelected(int index);
            protected abstract VisualElement ItemIcon(int index);
            protected abstract void Select(int index);
            protected abstract void Activate();
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Interrupted);
                target.RegisterCallback<DetachFromPanelEvent>(Detached);
                target.RegisterCallback<GeometryChangedEvent>(Geometry);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Cancel();
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Interrupted);
                target.UnregisterCallback<DetachFromPanelEvent>(Detached);
                target.UnregisterCallback<GeometryChangedEvent>(Geometry);
            }
            private void Down(PointerDownEvent evt)
            {
                if (evt.button != 0 || IsPressed || !target.enabledInHierarchy) return;
                owner.Focus(); target.Focus();
                pointer = evt.pointerId;
                press = current = evt.position;
                root = owner.rootVisualElement;
                root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                root.RegisterCallback<GeometryChangedEvent>(Geometry);
                target.CapturePointer(pointer);
                hold = target.schedule.Execute(Open).StartingIn(HoldMilliseconds);
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Open()
            {
                hold?.Pause(); hold = null;
                if (!IsPressed) return;
                if (target.panel == null || !target.HasPointerCapture(pointer)) { Cancel(); return; }
                if (menu != null) return;
                menu = new VisualElement { name = MenuName, pickingMode = PickingMode.Ignore };
                menu.AddToClassList("whimtex-shape-picker");
                menu.EnableInClassList("whimtex-shape-picker--light", !EditorGUIUtility.isProSkin);
                for (int i = 0; i < items.Length; i++)
                {
                    var item = new VisualElement { name = ItemName(i), tooltip = ItemTooltip(i), pickingMode = PickingMode.Ignore };
                    item.AddToClassList("whimtex-shape-picker-item");
                    item.EnableInClassList("whimtex-shape-picker-item--selected", IsSelected(i));
                    item.Add(ItemIcon(i));
                    items[i] = item;
                    menu.Add(item);
                }
                Vector2 anchor = root.WorldToLocal(new Vector2(target.worldBound.xMax + 2f, target.worldBound.yMin));
                Rect bounds = root.contentRect;
                menuPosition = new Vector2(Mathf.Clamp(anchor.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - ItemSize - Inset * 2f)),
                    Mathf.Clamp(anchor.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - items.Length * ItemSize - Inset * 2f)));
                menu.style.left = menuPosition.x;
                menu.style.top = menuPosition.y;
                root.Add(menu);
                UpdateHover();
            }
            private int ItemAt(Vector2 local)
            {
                if (local.x < Inset || local.x >= Inset + ItemSize || local.y < Inset || local.y >= Inset + items.Length * ItemSize) return -1;
                return Mathf.FloorToInt((local.y - Inset) / ItemSize);
            }
            private void UpdateHover()
            {
                if (menu == null) return;
                int next = ItemAt(root.WorldToLocal(current) - menuPosition);
                if (hovered == next) return;
                if (hovered >= 0) items[hovered].RemoveFromClassList("whimtex-shape-picker-item--hover");
                hovered = next;
                if (hovered >= 0) items[hovered].AddToClassList("whimtex-shape-picker-item--hover");
            }
            private void Move(PointerMoveEvent evt)
            {
                if (!IsPressed || evt.pointerId != pointer) return;
                current = evt.position;
                if ((evt.pressedButtons & 1) == 0 || !target.HasPointerCapture(pointer)) Cancel();
                else
                {
                    if (menu == null && (current - press).sqrMagnitude >= DragDistance * DragDistance) Open();
                    UpdateHover();
                }
                WhimTexUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (!IsPressed || evt.pointerId != pointer || evt.button != 0) return;
                if (!target.HasPointerCapture(pointer)) { Cancel(); WhimTexUI.ConsumeEvent(evt); return; }
                current = evt.position;
                UpdateHover();
                int selection = hovered;
                bool click = menu == null && target.worldBound.Contains(current);
                Cancel();
                if (selection >= 0) Select(selection);
                if (selection >= 0 || click) Activate();
                WhimTexUI.ConsumeEvent(evt);
            }
            internal void Cancel()
            {
                int captured = pointer; pointer = -1;
                hold?.Pause(); hold = null;
                menu?.RemoveFromHierarchy(); menu = null;
                Array.Clear(items, 0, items.Length);
                hovered = -1;
                if (root != null)
                {
                    root.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                    root.UnregisterCallback<GeometryChangedEvent>(Geometry);
                    root = null;
                }
                if (captured >= 0 && target != null && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            }
            private void Key(KeyDownEvent evt)
            {
                if (IsPressed && evt.keyCode == KeyCode.Escape) { Cancel(); WhimTexUI.ConsumeEvent(evt); }
            }
            private void Lost(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId == pointer && !target.HasPointerCapture(pointer)) Cancel();
            }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
        }
    }
}
