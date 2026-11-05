using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private VisualElement pointOverlay;
        private PointManipulator pointManipulator;

        private ShaderParameter PointParameter => CanvasShaderParameter(CanvasTool.FXPoint, ShaderParameterType.Point);

        internal static void EditFXPoint(ShaderFX effect, string id) => EditFXParameter(effect, id, ShaderParameterType.Point);

        private void BuildPointTool()
        {
            var manipulator = pointManipulator = new PointManipulator(this);
            pointOverlay = toolkitCanvas.Overlays.Create(manipulator.Draw, manipulator.ValidateSelection);
            toolkitCanvas.AddManipulator(manipulator);
        }

        private sealed class PointManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private ShaderParameterCanvasTarget parameterTarget;
            private ShaderParameter parameter;
            private Vector2 offset, start, dimensions;
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
                parameter = value; parameterTarget = owner.temporaryShaderTarget; pointer = e.pointerId;
                dimensions = new Vector2(owner.activeDocument.width, owner.activeDocument.height);
                originalValue = value.vectorValue;
                start = e.localPosition; offset = Handle(value) - start; moved = false;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }

            internal bool WantsPointer(Vector2 point) => owner.PointParameter is ShaderParameter value &&
                owner.toolkitCanvas.ImageRect.width > 0 && Vector2.Distance(point, Handle(value)) <= 11;

            private string UndoName => parameterTarget.IsEffect ? "Move FX Point" : "Move Shader Point";
            private bool IsCurrent => ReferenceEquals(parameterTarget, owner.temporaryShaderTarget) &&
                owner.PointParameter == parameter && parameterTarget?.IsCurrent(parameter) == true &&
                dimensions == new Vector2(owner.activeDocument.width, owner.activeDocument.height);
            internal void ValidateSelection() { if (IsDragging && !IsCurrent) Finish(); }

            private void Set(Vector2 uv)
            {
                parameterTarget.RecordUndo(UndoName);
                parameter.vectorValue = new Vector4(uv.x, uv.y, parameter.vectorValue.z, parameter.vectorValue.w);
                parameterTarget.NotifyValuesChanged();
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
                if (!IsCurrent || (e.pressedButtons & 1) == 0) { Finish(); return; }
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
                if (cancel && IsCurrent)
                {
                    parameter.vectorValue = originalValue;
                    parameterTarget.NotifyValuesChanged();
                    owner.pointOverlay?.MarkDirtyRepaint();
                }
                if (!Undo.isProcessing && !WhimTexDocument.IsRefreshingUndo)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(undoGroup);
                }
                if (target.HasPointerCapture(old)) target.ReleasePointer(old);
                parameterTarget = null; parameter = null;
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
