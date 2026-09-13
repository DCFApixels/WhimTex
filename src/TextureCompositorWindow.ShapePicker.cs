using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.SpriteEditor
{
    public sealed partial class TextureCompositorWindow
    {
        private ShapePickerManipulator shapePicker;
        private ShapePickerManipulator marqueePicker;
        private PreviewToolIcon marqueeToolIcon;
        private ShapeToolIcon shapeToolIcon;

        private sealed class ToolDropdownMarker : VisualElement
        {
            internal ToolDropdownMarker()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("sprite-editor-tool-dropdown-marker");
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
                AddToClassList("sprite-editor-tool-icon");
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

        private sealed class ShapePickerManipulator : PointerManipulator
        {
            private const int HoldMilliseconds = 160;
            private const float DragDistance = 3f;
            private const float ItemSize = 30f, Inset = 3f;
            private readonly ShapeLayerBehaviour.ShapeKind[] Kinds;
            private readonly bool marquee;
            private readonly TextureCompositorWindow owner;
            private readonly VisualElement[] items;
            private VisualElement menu, root;
            private IVisualElementScheduledItem hold;
            private int pointer = -1, hovered = -1;
            private Vector2 press, current, menuPosition;
            private bool IsPressed => pointer >= 0;

            internal ShapePickerManipulator(TextureCompositorWindow owner, bool marquee = false)
            {
                this.owner = owner;
                this.marquee = marquee;
                Kinds = marquee
                    ? new[] { ShapeLayerBehaviour.ShapeKind.Rectangle, ShapeLayerBehaviour.ShapeKind.Ellipse, ShapeLayerBehaviour.ShapeKind.Polygon }
                    : (ShapeLayerBehaviour.ShapeKind[])Enum.GetValues(typeof(ShapeLayerBehaviour.ShapeKind));
                items = new VisualElement[Kinds.Length];
            }
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
                SpriteEditorUI.ConsumeEvent(evt);
            }
            private void Open()
            {
                hold?.Pause(); hold = null;
                if (!IsPressed || target.panel == null || !target.HasPointerCapture(pointer) || menu != null) return;
                menu = new VisualElement { name = marquee ? "marqueePicker" : "shapePicker", pickingMode = PickingMode.Ignore };
                menu.AddToClassList("sprite-editor-shape-picker");
                menu.EnableInClassList("sprite-editor-shape-picker--light", !EditorGUIUtility.isProSkin);
                for (int i = 0; i < Kinds.Length; i++)
                {
                    var item = new VisualElement { tooltip = marquee && i == 2 ? "UV Island" : Kinds[i].ToString(), pickingMode = PickingMode.Ignore };
                    item.AddToClassList("sprite-editor-shape-picker-item");
                    item.EnableInClassList("sprite-editor-shape-picker-item--selected", marquee
                        ? (int)owner.marqueeShape == i : owner.shapeToolSettings.kind == Kinds[i]);
                    if (marquee) item.Add(new PreviewToolIcon(PreviewTool.RectangleSelect, i == 1, i == 2));
                    else item.Add(new ShapeToolIcon(Kinds[i]));
                    items[i] = item;
                    menu.Add(item);
                }
                Vector2 anchor = root.WorldToLocal(new Vector2(target.worldBound.xMax + 2f, target.worldBound.yMin));
                Rect bounds = root.contentRect;
                menuPosition = new Vector2(Mathf.Clamp(anchor.x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - ItemSize - Inset * 2f)),
                    Mathf.Clamp(anchor.y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - Kinds.Length * ItemSize - Inset * 2f)));
                menu.style.left = menuPosition.x;
                menu.style.top = menuPosition.y;
                root.Add(menu);
                UpdateHover();
            }
            private int ItemAt(Vector2 local)
            {
                if (local.x < Inset || local.x >= Inset + ItemSize || local.y < Inset || local.y >= Inset + Kinds.Length * ItemSize) return -1;
                return Mathf.FloorToInt((local.y - Inset) / ItemSize);
            }
            private void UpdateHover()
            {
                if (menu == null) return;
                int next = ItemAt(root.WorldToLocal(current) - menuPosition);
                if (hovered == next) return;
                if (hovered >= 0) items[hovered].RemoveFromClassList("sprite-editor-shape-picker-item--hover");
                hovered = next;
                if (hovered >= 0) items[hovered].AddToClassList("sprite-editor-shape-picker-item--hover");
            }
            private void Move(PointerMoveEvent evt)
            {
                if (!IsPressed || evt.pointerId != pointer) return;
                current = evt.position;
                if ((evt.pressedButtons & 1) == 0) Cancel();
                else
                {
                    if (menu == null && (current - press).sqrMagnitude >= DragDistance * DragDistance) Open();
                    UpdateHover();
                }
                SpriteEditorUI.ConsumeEvent(evt);
            }
            private void Up(PointerUpEvent evt)
            {
                if (!IsPressed || evt.pointerId != pointer || evt.button != 0) return;
                current = evt.position;
                UpdateHover();
                int selection = hovered;
                bool click = menu == null && target.worldBound.Contains(current);
                Cancel();
                if (selection >= 0)
                {
                    if (marquee)
                    {
                        owner.areaSelectionManipulator?.Cancel();
                        owner.marqueeShape = (MarqueeShape)selection;
                        if (owner.marqueeShape == MarqueeShape.UvIsland)
                        {
                            if (!owner.uvEnabled || owner.compositor?.uvReferenceMesh == null) owner.OpenUvDrawer();
                        }
                    }
                    else
                    {
                        owner.shapeManipulator?.Cancel();
                        owner.shapeToolSettings.kind = Kinds[selection];
                    }
                }
                if (selection >= 0 || click) owner.SetPreviewTool(marquee ? PreviewTool.RectangleSelect : PreviewTool.Shape);
                SpriteEditorUI.ConsumeEvent(evt);
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
                if (IsPressed && evt.keyCode == KeyCode.Escape) { Cancel(); SpriteEditorUI.ConsumeEvent(evt); }
            }
            private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Cancel(); }
            private void Interrupted(PointerCancelEvent evt) => Cancel();
            private void Detached(DetachFromPanelEvent evt) => Cancel();
            private void Geometry(GeometryChangedEvent evt) => Cancel();
        }
    }
}
