using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexColorField
    {
        internal void BindHueDragLabel(VisualElement label)
        {
            if (label == null || label.ClassListContains("whimtex-color-hue-label")) return;
            label.AddToClassList("whimtex-color-hue-label");
            label.AddToClassList("unity-base-field__label--with-dragger");
            label.pickingMode = PickingMode.Position;
            label.AddManipulator(new HueLabelDrag(this));
        }

        // Rotating the hue sector preserves the extrema, including signed/HDR
        // values. HSV Saturation and Value are therefore retained without clipping.
        internal static Color ShiftHue(Color color, double turns)
        {
            double minimum = Math.Min(color.r, Math.Min(color.g, color.b));
            double maximum = Math.Max(color.r, Math.Max(color.g, color.b));
            double chroma = maximum - minimum;
            if (chroma == 0 || turns == 0) return color;
            double hue = maximum == color.r ? (color.g - (double)color.b) / chroma :
                maximum == color.g ? 2 + (color.b - (double)color.r) / chroma :
                4 + (color.r - (double)color.g) / chroma;
            hue += turns * 6;
            hue -= Math.Floor(hue / 6) * 6;
            int sector = (int)Math.Floor(hue);
            float low = (float)minimum, high = (float)maximum;
            float rising = (float)(minimum + chroma * (hue - sector));
            float falling = (float)(maximum - chroma * (hue - sector));
            return sector switch
            {
                0 => new Color(high, rising, low, color.a),
                1 => new Color(falling, high, low, color.a),
                2 => new Color(low, high, rising, color.a),
                3 => new Color(low, falling, high, color.a),
                4 => new Color(rising, low, high, color.a),
                _ => new Color(high, low, falling, color.a)
            };
        }

        private sealed class HueLabelDrag : PointerManipulator
        {
            readonly WhimTexColorField field;
            int pointer = -1, undoGroup = -1;
            Vector2 origin;
            Color original;
            bool changed;

            internal HueLabelDrag(WhimTexColorField field) => this.field = field;

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCancelEvent>(Cancel);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<MouseDownEvent>(MouseDown);
                target.RegisterCallback<MouseUpEvent>(MouseUp);
                target.RegisterCallback<DetachFromPanelEvent>(Detach);
                field.RegisterCallback<DetachFromPanelEvent>(Detach);
                field.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                Stop();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCancelEvent>(Cancel);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<MouseDownEvent>(MouseDown);
                target.UnregisterCallback<MouseUpEvent>(MouseUp);
                target.UnregisterCallback<DetachFromPanelEvent>(Detach);
                field.UnregisterCallback<DetachFromPanelEvent>(Detach);
                field.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }

            bool CanEdit => field.panel != null && field.enabledInHierarchy && target.enabledInHierarchy &&
                !field.showMixedValue && field.EditableComponents >= 3;

            void Down(PointerDownEvent e)
            {
                if (pointer >= 0 || e.button != 0 || !CanEdit) return;
                original = field.ReadPickerColor != null ? field.ReadPickerColor() : field.value;
                if (!WhimTexColorPicker.IsFinite(original)) return;
                origin = e.position;
                changed = false;
                pointer = e.pointerId;
                field.Focus();
                target.CapturePointer(pointer);
                e.StopImmediatePropagation();
            }

            void Move(PointerMoveEvent e)
            {
                if (pointer != e.pointerId || !target.HasPointerCapture(pointer)) return;
                if (!CanEdit) { Stop(); return; }
                // One horizontal panel point per hue degree. Use the initial
                // color so repeated moves do not accumulate conversion error.
                double delta = e.position.x - origin.x;
                if (!changed && Math.Abs(delta) < 3) { e.StopImmediatePropagation(); return; }
                var next = ShiftHue(original, delta / 360);
                if (!next.Equals(field.ReadPickerColor != null ? field.ReadPickerColor() : field.value))
                {
                    if (undoGroup < 0)
                    {
                        Undo.IncrementCurrentGroup();
                        undoGroup = Undo.GetCurrentGroup();
                    }
                    changed = true;
                    field.ApplyPickerColor(next);
                }
                e.StopImmediatePropagation();
            }

            void Up(PointerUpEvent e)
            {
                if (pointer != e.pointerId || e.button != 0) return;
                Stop();
                e.StopImmediatePropagation();
            }
            void MouseDown(MouseDownEvent e) { if (pointer >= 0 && e.button == 0) e.StopImmediatePropagation(); }
            void MouseUp(MouseUpEvent e) { if (e.button == 0 && CanEdit) e.StopPropagation(); }
            void Cancel(PointerCancelEvent e)
            {
                if (pointer != e.pointerId) return;
                Restore();
                e.StopImmediatePropagation();
            }
            void Key(KeyDownEvent e)
            {
                if (pointer < 0 || e.keyCode != KeyCode.Escape) return;
                Restore();
                e.StopImmediatePropagation();
            }
            void Restore()
            {
                if (changed && field.panel != null && field.enabledInHierarchy) field.ApplyPickerColor(original);
                Stop();
            }
            void Lost(PointerCaptureOutEvent e) { if (pointer == e.pointerId) Stop(); }
            void Detach(DetachFromPanelEvent e) { if (e.target == target || e.target == field) Stop(); }

            void Stop()
            {
                int previous = pointer;
                pointer = -1;
                if (previous >= 0 && target.HasPointerCapture(previous)) target.ReleasePointer(previous);
                if (undoGroup >= 0)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    Undo.IncrementCurrentGroup();
                    undoGroup = -1;
                }
                changed = false;
            }
        }
    }
}
