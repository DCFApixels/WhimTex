using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private VisualElement normalOverlay;
        private NormalManipulator normalManipulator;
        private ShaderParameter NormalParameter => CanvasShaderParameter(CanvasTool.FXNormal, ShaderParameterType.Normal);

        internal static void EditFXNormal(ShaderFX effect, string id) => EditFXParameter(effect, id, ShaderParameterType.Normal);

        internal static Vector3 ProjectNormal(Vector2 xy, bool back)
        {
            xy = Vector2.ClampMagnitude(xy, 1);
            float z = Mathf.Max(1e-6f, Mathf.Sqrt(Mathf.Max(0, 1-xy.sqrMagnitude)));
            return new Vector3(xy.x, xy.y, back ? -z : z).normalized;
        }

        private void BuildNormalTool()
        {
            var manipulator = normalManipulator = new NormalManipulator(this);
            normalOverlay = toolkitCanvas.Overlays.Create(manipulator.Draw, manipulator.ValidateSelection);
            toolkitCanvas.AddManipulator(manipulator);
        }

        private sealed class NormalManipulator : PointerManipulator
        {
            private const float Radius = 90;
            private readonly WhimTexWindow owner;
            private int pointer = -1, undoGroup;
            private Vector2 start, offset, dimensions;
            private ShaderParameterCanvasTarget parameterTarget;
            private ShaderParameter parameter;
            private bool moved, back;
            private Vector4 originalValue;
            internal bool IsDragging => pointer >= 0;
            internal NormalManipulator(WhimTexWindow owner) { this.owner = owner; }
            private Vector2 Center => owner.toolkitCanvas.Coordinates.UvToView(Vector2.one * .5f);
            private Vector2 Tip(ShaderParameter p)
            {
                var n = ShaderParameter.NormalizeNormal(p.vectorValue);
                return Center + owner.canvasViewport.ToViewDelta(new Vector2(n.x, -n.y) * Radius);
            }
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
                var p = owner.NormalParameter;
                if (pointer >= 0 || e.button != 0 || e.altKey || !WantsPointer(e.localPosition)) return;
                parameter = p; parameterTarget = owner.temporaryShaderTarget; pointer = e.pointerId;
                dimensions = new Vector2(owner.activeDocument.width, owner.activeDocument.height);
                originalValue = p.vectorValue;
                owner.Focus(); target.Focus();
                start = e.localPosition; offset = Tip(p) - start;
                back = p.vectorValue.z < 0 || p.vectorValue.z == 0 && BitConverter.SingleToInt32Bits(p.vectorValue.z) < 0;
                moved = false;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }

            internal bool WantsPointer(Vector2 point) => owner.NormalParameter is ShaderParameter value &&
                Vector2.Distance(point, Tip(value)) <= 12;
            private string UndoName => parameterTarget.IsEffect ? "Change FX Normal" : "Change Shader Normal";
            private bool IsCurrent => ReferenceEquals(parameterTarget, owner.temporaryShaderTarget) &&
                owner.NormalParameter == parameter && parameterTarget?.IsCurrent(parameter) == true &&
                dimensions == new Vector2(owner.activeDocument.width, owner.activeDocument.height);
            internal void ValidateSelection() { if (IsDragging && !IsCurrent) Finish(); }
            private void Set(Vector3 n)
            {
                parameterTarget.RecordUndo(UndoName);
                parameter.vectorValue = n;
                parameterTarget.NotifyValuesChanged();
                owner.normalOverlay.MarkDirtyRepaint();
            }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || pointer < 0) return;
                e.StopImmediatePropagation();
                if (!IsCurrent || (e.pressedButtons & 1) == 0) { Finish(); return; }
                moved |= Vector2.Distance(start,e.localPosition) > 3;
                if (!moved) return;
                Vector2 xy = owner.canvasViewport.ToCanvasDelta((Vector2)e.localPosition + offset - Center) / Radius;
                Set(ProjectNormal(new Vector2(xy.x,-xy.y),back));
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || pointer < 0 || e.button != 0) return;
                if (!moved && IsCurrent)
                {
                    var n = ShaderParameter.NormalizeNormal(parameter.vectorValue);
                    n.z = back ? Mathf.Abs(n.z) : -Mathf.Abs(n.z);
                    Set(n);
                }
                Finish(); e.StopImmediatePropagation();
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
                    owner.normalOverlay?.MarkDirtyRepaint();
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
                var value = owner.NormalParameter;
                if (value == null) return;
                Vector2 center = Center, tip = Tip(value);
                var p = context.painter2D;
                p.BeginPath(); CanvasHandlePainter.Line(p, center, tip);
                CanvasHandlePainter.StrokeOutlined(p, new Color(.4f,1,.65f), 2, Color.black);
                p.fillColor = new Color(.12f,.12f,.12f);
                p.BeginPath(); CanvasHandlePainter.Circle(p, tip, 10); p.Fill(); p.Stroke();
                p.strokeColor = Color.white;
                p.BeginPath(); CanvasHandlePainter.Line(p, tip + Vector2.left*4, tip + Vector2.right*4);
                if (!(value.vectorValue.z < 0 || value.vectorValue.z == 0 && BitConverter.SingleToInt32Bits(value.vectorValue.z) < 0))
                    CanvasHandlePainter.Line(p, tip + Vector2.up*4, tip + Vector2.down*4);
                p.Stroke();
            }
        }
    }
}
