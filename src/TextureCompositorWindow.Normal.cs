using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class TextureCompositorWindow
    {
        [NonSerialized] private ShaderFX normalFX;
        [NonSerialized] private string normalParameterId;
        private VisualElement normalOverlay;
        private NormalManipulator normalManipulator;
        private ShaderFXParameter NormalParameter
        {
            get
            {
                if (previewTool != PreviewTool.FXNormal || normalFX == null || compositor == null || GetSelectedLayer() is not Layer layer ||
                    !layer.modifiers.Contains(normalFX) || WhimTexApi.IsLayerContentLocked(compositor, layer) ||
                    WhimTexApi.IsShaderFXContentLocked(normalFX)) return null;
                foreach (var p in normalFX.Parameters)
                    if (p != null && p.id == normalParameterId && p.type == ShaderFXParameterType.Normal) return p;
                return null;
            }
        }

        internal static void EditFXNormal(ShaderFX effect, string id)
        {
            if (effect == null || WhimTexApi.IsShaderFXContentLocked(effect)) return;
            bool exists = false;
            foreach (var parameter in effect.Parameters)
                exists |= parameter != null && parameter.id == id && parameter.type == ShaderFXParameterType.Normal;
            if (!exists) return;
            TextureCompositorWindow best = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<TextureCompositorWindow>())
                if (w.compositor != null && w.GetSelectedLayer() is Layer layer && layer.modifiers.Contains(effect) &&
                    !WhimTexApi.IsLayerContentLocked(w.compositor, layer) &&
                    (best == null || w == focusedWindow || best != focusedWindow && w.AgentFocusOrder > best.AgentFocusOrder)) best = w;
            if (best == null) return;
            best.ActivateTemporaryTool(PreviewTool.FXNormal, effect, id);
        }

        internal static Vector3 ProjectNormal(Vector2 xy, bool back)
        {
            xy = Vector2.ClampMagnitude(xy, 1);
            float z = Mathf.Max(1e-6f, Mathf.Sqrt(Mathf.Max(0, 1-xy.sqrMagnitude)));
            return new Vector3(xy.x, xy.y, back ? -z : z).normalized;
        }

        private void BuildNormalTool()
        {
            normalOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            normalOverlay.StretchToParentSize();
            toolkitPreviewCanvas.Add(normalOverlay);
            var manipulator = normalManipulator = new NormalManipulator(this);
            toolkitPreviewCanvas.AddManipulator(manipulator);
            normalOverlay.generateVisualContent += manipulator.Draw;
            bool wasActive = false;
            normalOverlay.schedule.Execute(() =>
            {
                bool active = NormalParameter != null;
                if (active || wasActive) normalOverlay.MarkDirtyRepaint();
                wasActive = active;
            }).Every(50);
        }

        private sealed class NormalManipulator : PointerManipulator
        {
            private const float Radius = 90;
            private readonly TextureCompositorWindow owner;
            private int pointer = -1, undoGroup;
            private Vector2 start, offset;
            private ShaderFX effect;
            private ShaderFXParameter parameter;
            private bool moved, back;
            private Vector4 originalValue;
            internal bool IsDragging => pointer >= 0;
            internal NormalManipulator(TextureCompositorWindow owner) { this.owner = owner; }
            private Vector2 Center => owner.toolkitPreviewCanvas.ToView(owner.toolkitPreviewCanvas.ImageRect.center);
            private Vector2 Tip(ShaderFXParameter p)
            {
                var n = ShaderFXParameter.NormalizeNormal(p.vectorValue);
                return Center + owner.previewViewport.ToViewDelta(new Vector2(n.x, -n.y) * Radius);
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
                if (pointer >= 0 || p == null || e.button != 0 || e.altKey ||
                    Vector2.Distance(e.localPosition, Tip(p)) > 12) return;
                parameter = p; effect = owner.normalFX; pointer = e.pointerId;
                originalValue = p.vectorValue;
                owner.Focus(); target.Focus();
                start = e.localPosition; offset = Tip(p) - start;
                back = p.vectorValue.z < 0 || p.vectorValue.z == 0 && BitConverter.SingleToInt32Bits(p.vectorValue.z) < 0;
                moved = false;
                Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Change FX Normal");
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }
            private void Set(Vector3 n)
            {
                Undo.RecordObject(effect, "Change FX Normal");
                parameter.vectorValue = n;
                EditorUtility.SetDirty(effect);
                effect.NotifyValuesChanged();
                owner.normalOverlay.MarkDirtyRepaint();
            }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || pointer < 0) return;
                e.StopImmediatePropagation();
                if (owner.NormalParameter != parameter || effect == null || (e.pressedButtons & 1) == 0) { Finish(); return; }
                moved |= Vector2.Distance(start,e.localPosition) > 3;
                if (!moved) return;
                Vector2 xy = owner.previewViewport.ToCanvasDelta((Vector2)e.localPosition + offset - Center) / Radius;
                Set(ProjectNormal(new Vector2(xy.x,-xy.y),back));
            }
            private void Up(PointerUpEvent e)
            {
                if (e.pointerId != pointer || pointer < 0 || e.button != 0) return;
                if (!moved && owner.NormalParameter == parameter && effect != null)
                {
                    var n = ShaderFXParameter.NormalizeNormal(parameter.vectorValue);
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
                if (cancel && effect != null && parameter != null)
                {
                    parameter.vectorValue = originalValue;
                    effect.NotifyValuesChanged();
                    owner.normalOverlay?.MarkDirtyRepaint();
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                if (target.HasPointerCapture(old)) target.ReleasePointer(old);
                effect = null; parameter = null;
            }
            internal void Draw(MeshGenerationContext context)
            {
                var value = owner.NormalParameter;
                if (value == null) return;
                Vector2 center = Center, tip = Tip(value);
                var p = context.painter2D;
                p.strokeColor = Color.black; p.lineWidth = 4;
                p.BeginPath(); p.MoveTo(center); p.LineTo(tip); p.Stroke();
                p.strokeColor = new Color(.4f,1,.65f); p.lineWidth = 2;
                p.BeginPath(); p.MoveTo(center); p.LineTo(tip); p.Stroke();
                p.fillColor = new Color(.12f,.12f,.12f);
                p.BeginPath(); p.Arc(tip,10,0,360); p.ClosePath(); p.Fill(); p.Stroke();
                p.strokeColor = Color.white;
                p.BeginPath(); p.MoveTo(tip + Vector2.left*4); p.LineTo(tip + Vector2.right*4);
                if (!(value.vectorValue.z < 0 || value.vectorValue.z == 0 && BitConverter.SingleToInt32Bits(value.vectorValue.z) < 0))
                { p.MoveTo(tip + Vector2.up*4); p.LineTo(tip + Vector2.down*4); }
                p.Stroke();
            }
        }
    }
}
