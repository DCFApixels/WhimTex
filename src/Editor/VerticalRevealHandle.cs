using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class VerticalRevealHandle : VisualElement
    {
        internal const float HeaderHeight = 20f;
        internal const float MinimumExpandedHeight = 60f;
        private readonly ResizeManipulator resize;

        internal static bool ShouldCollapse(float requestedHeight, float maximum) =>
            requestedHeight <= Mathf.Min(34f, HeaderHeight + (maximum - HeaderHeight) * .5f);

        internal VerticalRevealHandle(string title, Func<float> height, Action<float> onResize, Func<VisualElement, bool> ignore = null)
        {
            AddToClassList("whimtex-output-preview-title");
            Add(new Label(title) { pickingMode = PickingMode.Ignore });
            var grip = new VisualElement { pickingMode = PickingMode.Ignore };
            grip.AddToClassList("whimtex-output-preview-grip");
            Add(grip);
            resize = new ResizeManipulator(height, onResize, ignore);
            this.AddManipulator(resize);
            RegisterCallback<DetachFromPanelEvent>(_ => Release());
        }

        internal void Release() => resize.Release();

        private sealed class ResizeManipulator : PointerManipulator
        {
            private readonly Func<float> height;
            private readonly Action<float> onResize;
            private readonly Func<VisualElement, bool> ignore;
            private int pointer = -1;
            private float startY, startHeight;
            internal ResizeManipulator(Func<float> height, Action<float> onResize, Func<VisualElement, bool> ignore)
            { this.height = height; this.onResize = onResize; this.ignore = ignore; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                Release();
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            private void Down(PointerDownEvent evt)
            {
                if (!target.enabledInHierarchy || evt.button != 0 || pointer >= 0 || ignore?.Invoke(evt.target as VisualElement) == true) return;
                pointer = evt.pointerId; startY = evt.position.y; startHeight = height();
                target.CapturePointer(pointer); evt.StopPropagation();
            }
            private void Move(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointer || !target.HasPointerCapture(pointer)) return;
                onResize(startHeight + startY - evt.position.y); evt.StopPropagation();
            }
            private void Up(PointerUpEvent evt)
            {
                if (evt.pointerId != pointer || evt.button != 0) return;
                Release(); evt.StopPropagation();
            }
            private void Lost(PointerCaptureOutEvent evt) { pointer = -1; }
            internal void Release()
            {
                int id = pointer; pointer = -1;
                if (id >= 0 && target != null && target.HasPointerCapture(id)) target.ReleasePointer(id);
            }
        }
    }
}
