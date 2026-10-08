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

        private ShaderFXParameter PointParameter
        {
            get
            {
                if (canvasTool != CanvasTool.FXPoint || pointFX == null || activeDocument == null || GetSelectedLayer() is not Layer layer ||
                    !layer.fx.Contains(pointFX) || WhimTexApi.IsLayerContentLocked(activeDocument, layer) ||
                    WhimTexApi.IsShaderFXContentLocked(pointFX)) return null;
                foreach (var p in pointFX.Parameters)
                    if (p != null && p.id == pointParameterId && p.type == ShaderFXParameterType.Point) return p;
                return null;
            }
        }

        internal static void EditFXPoint(ShaderFX effect, string id)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            bool exists = false;
            foreach (var parameter in effect.Parameters)
                exists |= parameter != null && parameter.id == id && parameter.type == ShaderFXParameterType.Point;
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
            pointOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            pointOverlay.StretchToParentSize();
            toolkitCanvas.Add(pointOverlay);
            var manipulator = pointManipulator = new PointManipulator(this);
            toolkitCanvas.AddManipulator(manipulator);
            pointOverlay.generateVisualContent += manipulator.Draw;
            toolkitCanvas.RegisterCallback<GeometryChangedEvent>(_ => pointOverlay.MarkDirtyRepaint());
            toolkitCanvas.ViewChanged += pointOverlay.MarkDirtyRepaint;
            bool wasActive = false;
            pointOverlay.schedule.Execute(() =>
            {
                bool active = PointParameter != null;
                if (active || wasActive) pointOverlay.MarkDirtyRepaint();
                wasActive = active;
            }).Every(50);
        }

        private sealed class PointManipulator : PointerManipulator
        {
            private readonly WhimTexWindow owner;
            private ShaderFX effect;
            private ShaderFXParameter parameter;
            private Vector2 offset, start;
            private int pointer = -1, undoGroup;
            private bool moved;
            private Vector4 originalValue;
            internal bool IsDragging => pointer >= 0;

            internal PointManipulator(WhimTexWindow owner) => this.owner = owner;

            private Vector2 Handle(ShaderFXParameter value)
            {
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 uv = new Vector2(value.vectorValue.x, value.vectorValue.y);
                Vector2 position = new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height);
                return owner.toolkitCanvas.ToView(position);
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

            internal bool WantsPointer(Vector2 point) => owner.PointParameter is ShaderFXParameter value &&
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
                Rect image = owner.toolkitCanvas.ImageRect;
                Vector2 canvas = owner.toolkitCanvas.ToCanvas((Vector2)e.localPosition + offset);
                var uv = new Vector2((canvas.x - image.xMin) / image.width,
                    1f - (canvas.y - image.yMin) / image.height);
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
                p.fillColor = new Color(.08f, .08f, .08f, .92f);
                p.BeginPath(); p.Arc(position, 9, 0, 360); p.ClosePath(); p.Fill();
                p.strokeColor = Color.white; p.lineWidth = 2;
                p.BeginPath(); p.Arc(position, 7, 0, 360); p.Stroke();
                p.fillColor = new Color(.25f, .82f, 1f, 1f);
                p.BeginPath(); p.Arc(position, 4, 0, 360); p.Fill();
            }
        }
    }
}
