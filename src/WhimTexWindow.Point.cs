using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private ShaderFX pointFX;
        [NonSerialized] private string pointParameterId;
        private VisualElement pointOverlay;
        private PointManipulator pointManipulator;

        private ShaderParameter PointParameter
        {
            get
            {
                if (canvasTool != CanvasTool.FXPoint || pointFX == null || activeDocument == null || GetSelectedLayer() is not Layer layer ||
                    !layer.fx.Contains(pointFX) || WhimTexApi.IsLayerContentLocked(activeDocument, layer) ||
                    WhimTexApi.IsShaderFXContentLocked(pointFX)) return null;
                foreach (var p in pointFX.Parameters)
                    if (p != null && p.id == pointParameterId && p.type == ShaderParameterType.Point) return p;
                return null;
            }
        }

        internal static void EditFXPoint(ShaderFX effect, string id)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            bool exists = false;
            foreach (var parameter in effect.Parameters)
                exists |= parameter != null && parameter.id == id && parameter.type == ShaderParameterType.Point;
            if (!exists) return;
            WhimTexWindow best = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<WhimTexWindow>())
                if (w.activeDocument != null && w.GetSelectedLayer() is Layer layer && layer.fx.Contains(effect) &&
                    !WhimTexApi.IsLayerContentLocked(w.activeDocument, layer) &&
                    (best == null || w == focusedWindow || best != focusedWindow && w.AgentFocusOrder > best.AgentFocusOrder)) best = w;
            if (best == null) return;
            best.ActivateTemporaryTool(CanvasTool.FXPoint, effect, id);
        }

        private void BuildPointTool()
        {
            var manipulator = pointManipulator = new PointManipulator(this);
            pointOverlay = toolkitCanvas.Overlays.Create(manipulator.Draw);
            toolkitCanvas.AddManipulator(manipulator);
        }

        private sealed class PointManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private ShaderFX effect;
            private ShaderParameter parameter;
            private Vector2 offset, start;
            private int pointer = -1, undoGroup;
            private bool moved;
            private Vector4 originalValue;
            internal bool IsDragging => pointer >= 0;

            internal PointManipulator(WhimTexWindow owner) => this.owner = owner;

            private Vector2 Handle(ShaderParameter value) => owner.toolkitCanvas.Coordinates.UvToView(value.vectorValue);

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<PointerCancelEvent>(Cancel);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Finish();
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<PointerCancelEvent>(Cancel);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
            }

            private void Down(PointerDownEvent e)
            {
                var value = owner.PointParameter;
                if (pointer >= 0 || e.button != 0 || e.altKey || !WantsPointer(e.localPosition)) return;
                owner.Focus(); target.Focus();
                parameter = value; effect = owner.pointFX; pointer = e.pointerId;
                originalValue = value.vectorValue;
                start = e.localPosition; offset = Handle(value) - start; moved = false;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Move FX Point");
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }

            internal bool WantsPointer(Vector2 point) => owner.PointParameter is ShaderParameter value &&
                owner.toolkitCanvas.ImageRect.width > 0 && Vector2.Distance(point, Handle(value)) <= 11;

            private void Set(Vector2 uv)
            {
                Undo.RecordObject(effect, "Move FX Point");
                parameter.vectorValue = new Vector4(uv.x, uv.y, parameter.vectorValue.z, parameter.vectorValue.w);
                EditorUtility.SetDirty(effect);
                effect.NotifyValuesChanged();
                owner.pointOverlay.MarkDirtyRepaint();
            }

            private Vector2 Snap(Vector2 uv)
            {
                Vector2 dimensions = new Vector2(owner.activeDocument.width, owner.activeDocument.height);
                Vector2 point = Vector2.Scale(uv, dimensions);
                float tolerance = GuideSnapPixels / owner.toolkitCanvas.PixelScale;
                for (int axis = 0; axis < 2; axis++)
                {
                    float edge = Mathf.Abs(point[axis]) <= Mathf.Abs(point[axis] - dimensions[axis]) ? 0f : dimensions[axis];
                    if (Mathf.Abs(point[axis] - edge) <= tolerance) point[axis] = edge;
                }
                point = owner.SnapCanvasGuidePoint(point);
                return new Vector2(point.x / dimensions.x, point.y / dimensions.y);
            }

            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || pointer < 0) return;
                e.StopImmediatePropagation();
                if (owner.PointParameter != parameter || effect == null || (e.pressedButtons & 1) == 0) { Finish(); return; }
                moved |= Vector2.Distance(start, e.localPosition) > 3;
                if (!moved) return;
                var uv = owner.toolkitCanvas.Coordinates.ViewToUv((Vector2)e.localPosition + offset);
                Set(e.ctrlKey || e.commandKey ? uv : Snap(uv));
            }

            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || pointer < 0 || e.button != 0) return;
                Finish();
                e.StopImmediatePropagation();
            }

            private void Lost(PointerCaptureOutEvent e) { if (e.pointerId == pointer) Finish(); }
            private void Cancel(PointerCancelEvent e) { if (e.pointerId == pointer) Finish(true); }
            private void Detach(DetachFromPanelEvent e) => Finish();

            internal void Finish(bool cancel = false)
            {
                if (pointer < 0) return;
                int old = pointer; pointer = -1;
                if (cancel && effect != null && parameter != null)
                {
                    parameter.vectorValue = originalValue;
                    effect.NotifyValuesChanged();
                    owner.pointOverlay?.MarkDirtyRepaint();
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                if (target.HasPointerCapture(old)) target.ReleasePointer(old);
                effect = null; parameter = null;
            }

            internal void Draw(MeshGenerationContext context)
            {
                var value = owner.PointParameter;
                if (value == null) return;
                Vector2 position = Handle(value);
                var p = context.painter2D;
                CanvasHandlePainter.Disc(p, position, 9, new Color(.08f, .08f, .08f, .92f));
                p.strokeColor = Color.white; p.lineWidth = 2;
                p.BeginPath(); CanvasHandlePainter.Circle(p, position, 7); p.Stroke();
                CanvasHandlePainter.Disc(p, position, 4, new Color(.25f, .82f, 1f, 1f));
            }
        }
    }
}
