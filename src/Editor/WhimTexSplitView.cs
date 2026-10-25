using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed class WhimTexSplitView : VisualElement
    {
        public VisualElement FirstPane { get; }
        public VisualElement SecondPane { get; }
        public int FixedPaneIndex { get; }
        public bool IsHorizontal { get; }
        public event Action<float> FixedPaneSizeChanged;

        private readonly VisualElement divider;
        private float preferredSize;
        private int pointer = -1;
        private float dragStart, dragSize, dragPreference;
        private VisualElement FixedPane => FixedPaneIndex == 0 ? FirstPane : SecondPane;
        private VisualElement FlexiblePane => FixedPaneIndex == 0 ? SecondPane : FirstPane;

        public float FixedPaneSize
        {
            get => preferredSize;
            set
            {
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (Mathf.Approximately(preferredSize, value)) return;
                preferredSize = value;
                FixedPane.style.flexBasis = value;
                FixedPaneSizeChanged?.Invoke(value);
            }
        }

        public WhimTexSplitView(VisualElement firstPane, VisualElement secondPane,
            int fixedPaneIndex, float fixedPaneSize, bool horizontal)
        {
            FirstPane = firstPane ?? throw new ArgumentNullException(nameof(firstPane));
            SecondPane = secondPane ?? throw new ArgumentNullException(nameof(secondPane));
            if (firstPane == secondPane) throw new ArgumentException("Split panes must be distinct.");
            if (fixedPaneIndex != 0 && fixedPaneIndex != 1) throw new ArgumentOutOfRangeException(nameof(fixedPaneIndex));
            if (!float.IsFinite(fixedPaneSize) || fixedPaneSize < 0) throw new ArgumentOutOfRangeException(nameof(fixedPaneSize));
            FixedPaneIndex = fixedPaneIndex; IsHorizontal = horizontal; preferredSize = fixedPaneSize;
            WhimTexUI.ApplyWindowStyles(this);
            AddToClassList("whimtex-split");
            style.flexDirection = horizontal ? FlexDirection.Row : FlexDirection.Column;
            // Flex shrinks the preferred basis when space is limited, without changing the saved size.
            FixedPane.style.flexGrow = 0; FixedPane.style.flexShrink = 1;
            FixedPane.style.flexBasis = preferredSize;
            FlexiblePane.style.flexGrow = 1; FlexiblePane.style.flexShrink = 0;
            FlexiblePane.style.flexBasis = 0;
            if (horizontal) FlexiblePane.style.width = StyleKeyword.Auto;
            else FlexiblePane.style.height = StyleKeyword.Auto;

            divider = new VisualElement { name = "splitDivider", focusable = true };
            divider.AddToClassList("whimtex-split-handle");
            divider.AddToClassList(horizontal ? "whimtex-split-handle--columns" : "whimtex-split-handle--rows");
            divider.EnableInClassList("whimtex-split-handle--light", !EditorGUIUtility.isProSkin);
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("whimtex-split-line");
            for (int i = 0; i < 3; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("whimtex-split-grip"); line.Add(dot);
            }
            divider.Add(line);
            var hitArea = new VisualElement { name = "splitHitArea" };
            hitArea.AddToClassList("whimtex-split-hit-area"); divider.Add(hitArea);
            hierarchy.Add(FirstPane); hierarchy.Add(divider); hierarchy.Add(SecondPane);

            RegisterCallback<DetachFromPanelEvent>(_ => EndDrag(false));
            divider.RegisterCallback<PointerDownEvent>(OnPointerDown);
            divider.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            divider.RegisterCallback<PointerUpEvent>(OnPointerUp);
            divider.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == pointer) EndDrag(true); });
            divider.RegisterCallback<PointerCaptureOutEvent>(e => { if (e.pointerId == pointer) EndDrag(false); });
            divider.RegisterCallback<KeyDownEvent>(e =>
            {
                if (pointer < 0 || e.keyCode != KeyCode.Escape) return;
                EndDrag(true); e.StopPropagation();
            });
        }

        private float ClampSize(float requested)
        {
            var fixedStyle = FixedPane.resolvedStyle;
            var flexibleStyle = FlexiblePane.resolvedStyle;
            float minimum = IsHorizontal ? fixedStyle.minWidth.value : fixedStyle.minHeight.value;
            float otherMinimum = IsHorizontal ? flexibleStyle.minWidth.value : flexibleStyle.minHeight.value;
            minimum = float.IsFinite(minimum) ? Mathf.Max(0, minimum) : 0;
            otherMinimum = float.IsFinite(otherMinimum) ? Mathf.Max(0, otherMinimum) : 0;
            float length = IsHorizontal ? contentRect.width : contentRect.height;
            float separator = IsHorizontal ? divider.layout.width : divider.layout.height;
            if (!float.IsFinite(length) || !float.IsFinite(separator)) return Mathf.Max(minimum, requested);
            float margins = IsHorizontal
                ? fixedStyle.marginLeft + fixedStyle.marginRight + flexibleStyle.marginLeft + flexibleStyle.marginRight
                : fixedStyle.marginTop + fixedStyle.marginBottom + flexibleStyle.marginTop + flexibleStyle.marginBottom;
            float maximum = Mathf.Max(minimum, length - separator - margins - otherMinimum);
            var explicitMaximum = IsHorizontal ? fixedStyle.maxWidth : fixedStyle.maxHeight;
            if (explicitMaximum.keyword == StyleKeyword.Undefined && float.IsFinite(explicitMaximum.value))
                maximum = Mathf.Max(minimum, Mathf.Min(maximum, explicitMaximum.value));
            return Mathf.Clamp(requested, minimum, maximum);
        }

        private float PointerPosition(Vector2 world)
        {
            Vector2 local = this.WorldToLocal(world);
            return IsHorizontal ? local.x : local.y;
        }

        private void OnPointerDown(PointerDownEvent e)
        {
            if (pointer >= 0 || e.button != 0 || !enabledInHierarchy) return;
            pointer = e.pointerId; dragStart = PointerPosition(e.position);
            dragSize = IsHorizontal ? FixedPane.resolvedStyle.width : FixedPane.resolvedStyle.height;
            dragPreference = preferredSize;
            divider.Focus(); divider.CapturePointer(pointer);
            divider.AddToClassList("whimtex-split-handle--dragging");
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (pointer != e.pointerId) return;
            if ((e.pressedButtons & 1) == 0) { EndDrag(false); return; }
            float delta = PointerPosition(e.position) - dragStart;
            FixedPaneSize = ClampSize(dragSize + (FixedPaneIndex == 0 ? delta : -delta));
            e.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (pointer != e.pointerId || e.button != 0) return;
            EndDrag(false); e.StopPropagation();
        }

        private void EndDrag(bool cancel)
        {
            if (pointer < 0) return;
            int captured = pointer; pointer = -1;
            if (cancel) FixedPaneSize = dragPreference;
            divider.RemoveFromClassList("whimtex-split-handle--dragging");
            if (divider.HasPointerCapture(captured)) divider.ReleasePointer(captured);
        }
    }
}
