using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class ChannelDragManipulator : ToggleDragManipulator
    {
        internal ChannelDragManipulator(Button[] buttons, Func<int> getMask, Action<int> toggle)
            : base(buttons, i => (getMask() & (1 << i)) != 0, i => toggle(1 << i)) { }
    }

    internal class ToggleDragManipulator : PointerManipulator
    {
        private readonly IReadOnlyList<Button> buttons;
        private readonly Func<int, bool> getValue;
        private readonly Action<int> toggle;
        private readonly Action begin, changed, end;
        private readonly Func<Rect> clipBounds;
        private int pointer = -1;
        private bool value;
        private Vector2 previous;
        private VisualElement keyboardRoot;

        internal ToggleDragManipulator(IReadOnlyList<Button> buttons, Func<int, bool> getValue, Action<int> toggle,
            Action begin = null, Action changed = null, Action end = null, Func<Rect> clipBounds = null)
        {
            this.buttons = buttons;
            this.getValue = getValue;
            this.toggle = toggle;
            this.begin = begin;
            this.changed = changed;
            this.end = end;
            this.clipBounds = clipBounds;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerCancelEvent>(Cancel);
            target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            target.RegisterCallback<DetachFromPanelEvent>(Detach);
            target.RegisterCallback<FocusOutEvent>(Blur);
            target.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Release();
            target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCancelEvent>(Cancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            target.UnregisterCallback<DetachFromPanelEvent>(Detach);
            target.UnregisterCallback<FocusOutEvent>(Blur);
            target.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
        }

        private void Down(PointerDownEvent evt)
        {
            if (pointer >= 0 || evt.button != 0 || evt.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            var element = evt.target as VisualElement;
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                if (!CanUse(button) || (element != button && !button.Contains(element))) continue;
                button.Focus();
                value = !getValue(i);
                begin?.Invoke();
                pointer = evt.pointerId;
                previous = evt.position;
                target.CapturePointer(pointer);
                keyboardRoot = target.panel?.visualTree;
                if (keyboardRoot != target)
                    keyboardRoot?.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
                if (Apply(i)) changed?.Invoke();
                evt.StopImmediatePropagation();
                return;
            }
        }

        private void Move(PointerMoveEvent evt)
        {
            if (evt.pointerId != pointer) return;
            evt.StopImmediatePropagation();
            if ((evt.pressedButtons & 1) == 0 || !target.enabledInHierarchy) { Release(); return; }
            Sweep(evt.position);
        }

        private void Up(PointerUpEvent evt)
        {
            if (evt.pointerId != pointer || evt.button != 0) return;
            Sweep(evt.position);
            Release();
            evt.StopImmediatePropagation();
        }

        private static bool CanUse(Button button) => button.enabledInHierarchy && button.panel != null &&
            button.resolvedStyle.display != DisplayStyle.None && button.resolvedStyle.visibility == Visibility.Visible;

        private bool Apply(int index)
        {
            if (getValue(index) == value) return false;
            toggle(index);
            return true;
        }

        private void Sweep(Vector2 position)
        {
            bool modified = false;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (!CanUse(buttons[i])) continue;
                Rect rect = buttons[i].worldBound;
                if (clipBounds != null)
                {
                    Rect clip = clipBounds();
                    rect = Rect.MinMaxRect(Mathf.Max(rect.xMin, clip.xMin), Mathf.Max(rect.yMin, clip.yMin),
                        Mathf.Min(rect.xMax, clip.xMax), Mathf.Min(rect.yMax, clip.yMax));
                }
                if (rect.width > 0 && rect.height > 0 && Crosses(rect, previous, position)) modified |= Apply(i);
            }
            previous = position;
            if (modified) changed?.Invoke();
        }

        private static bool Crosses(Rect rect, Vector2 from, Vector2 to)
        {
            float enter = 0, exit = 1;
            return Clip(from.x, to.x - from.x, rect.xMin, rect.xMax, ref enter, ref exit) &&
                Clip(from.y, to.y - from.y, rect.yMin, rect.yMax, ref enter, ref exit);
        }

        private static bool Clip(float origin, float delta, float min, float max, ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) < .00001f) return origin >= min && origin <= max;
            float a = (min - origin) / delta, b = (max - origin) / delta;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        internal void Release()
        {
            if (keyboardRoot != target)
                keyboardRoot?.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            keyboardRoot = null;
            int captured = pointer;
            pointer = -1;
            if (captured >= 0 && target.HasPointerCapture(captured)) target.ReleasePointer(captured);
            if (captured >= 0) end?.Invoke();
        }

        private void Cancel(PointerCancelEvent evt) { if (evt.pointerId == pointer) Release(); }
        private void Lost(PointerCaptureOutEvent evt) { if (evt.pointerId == pointer) Release(); }
        private void Detach(DetachFromPanelEvent evt) => Release();
        private void Blur(FocusOutEvent evt)
        {
            var next = evt.relatedTarget as VisualElement;
            if (next == null || (next != target && !target.Contains(next))) Release();
        }
        private void Key(KeyDownEvent evt)
        {
            if (pointer < 0 || evt.keyCode != KeyCode.Escape) return;
            Release();
            evt.StopImmediatePropagation();
        }
    }
}
