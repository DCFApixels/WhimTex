using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private ShaderFX pointFX;
        [NonSerialized] private string pointParameterId;
        private VisualElement pointOverlay;
        private PointManipulator pointManipulator;

        private ShaderFXParameter PointParameter
        {
            get
            {
                if (previewTool != PreviewTool.FXPoint || pointFX == null || compositor == null || GetSelectedLayer() is not Layer layer ||
                    !layer.modifiers.Contains(pointFX) || WhimTexApi.IsLayerContentLocked(compositor, layer) ||
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
            TextureCompositorWindow best = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (w.compositor != null && w.GetSelectedLayer() is Layer layer && layer.modifiers.Contains(effect) &&
                    !WhimTexApi.IsLayerContentLocked(w.compositor, layer) &&
                    (best == null || w == focusedWindow || best != focusedWindow && w.AgentFocusOrder > best.AgentFocusOrder)) best = w;
            if (best == null) return;
            best.ActivateTemporaryTool(PreviewTool.FXPoint, effect, id);
        }

        private void BuildPointTool()
        {
            pointOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            pointOverlay.StretchToParentSize();
            toolkitPreviewCanvas.Add(pointOverlay);
            var manipulator = pointManipulator = new PointManipulator(this);
            toolkitPreviewCanvas.AddManipulator(manipulator);
            pointOverlay.generateVisualContent += manipulator.Draw;
            toolkitPreviewCanvas.RegisterCallback<GeometryChangedEvent>(_ => pointOverlay.MarkDirtyRepaint());
            toolkitPreviewCanvas.ViewChanged += pointOverlay.MarkDirtyRepaint;
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
            private readonly TextureCompositorWindow owner;
            private ShaderFX effect;
            private ShaderFXParameter parameter;
            private Vector2 offset, start;
            private int pointer = -1, undoGroup;
            private bool moved;
            private Vector4 originalValue;
            internal bool IsDragging => pointer >= 0;

            internal PointManipulator(TextureCompositorWindow owner) => this.owner = owner;

            private Vector2 Handle(ShaderFXParameter value)
            {
                Rect image = owner.toolkitPreviewCanvas.ImageRect;
                Vector2 uv = new Vector2(value.vectorValue.x, value.vectorValue.y);
                Vector2 position = new Vector2(image.xMin + uv.x * image.width, image.yMax - uv.y * image.height);
                return owner.toolkitPreviewCanvas.ToView(position);
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
                if (pointer >= 0 || value == null || e.button != 0 || e.altKey || owner.toolkitPreviewCanvas.ImageRect.width <= 0 ||
                    Vector2.Distance(e.localPosition, Handle(value)) > 11) return;
                owner.Focus(); target.Focus();
                parameter = value; effect = owner.pointFX; pointer = e.pointerId;
                originalValue = value.vectorValue;
                start = e.localPosition; offset = Handle(value) - start; moved = false;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Move FX Point");
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }

            private void Set(Vector2 uv)
            {
                Undo.RecordObject(effect, "Move FX Point");
                parameter.vectorValue = new Vector4(uv.x, uv.y, parameter.vectorValue.z, parameter.vectorValue.w);
                EditorUtility.SetDirty(effect);
                effect.NotifyValuesChanged();
                owner.pointOverlay.MarkDirtyRepaint();
            }

            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || pointer < 0) return;
                e.StopImmediatePropagation();
                if (owner.PointParameter != parameter || effect == null || (e.pressedButtons & 1) == 0) { Finish(); return; }
                moved |= Vector2.Distance(start, e.localPosition) > 3;
                if (!moved) return;
                Rect image = owner.toolkitPreviewCanvas.ImageRect;
                Vector2 canvas = owner.toolkitPreviewCanvas.ToCanvas((Vector2)e.localPosition + offset);
                var uv = new Vector2(Mathf.Clamp01((canvas.x - image.xMin) / image.width),
                    Mathf.Clamp01(1f - (canvas.y - image.yMin) / image.height));
                Set(uv);
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
