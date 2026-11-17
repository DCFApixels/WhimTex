using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    // Known row heights need no geometry estimates for items outside the viewport.
    internal sealed class WhimTexVirtualList : VisualElement
    {
        sealed class Slot { internal VisualElement element; internal int index = -1; }
        readonly List<Slot> slots = new();
        readonly ScrollView scroll;
        float[] offsets = { 0 };
        bool updating;
        int requestedIndex;
        float requestedFraction;
        bool pendingScroll, revealRequested;
        internal IList itemsSource;
        internal float fixedItemHeight = 20;
        internal Func<int, float> itemHeight;
        internal Func<VisualElement> makeItem;
        internal Action<VisualElement, int> bindItem, unbindItem;
        internal int Count => itemsSource?.Count ?? 0;
        internal float TotalHeight => offsets[offsets.Length - 1];
        internal ScrollView Scroll => scroll;

        internal WhimTexVirtualList()
        {
            style.flexGrow = 1; style.minHeight = 0; style.minWidth = 0;
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1; scroll.style.minHeight = 0; scroll.style.minWidth = 0;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentContainer.style.position = Position.Relative;
            scroll.contentContainer.style.flexShrink = 0;
            Add(scroll);
            scroll.verticalScroller.valueChanged += _ => UpdateVisible(false);
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => { ClampScroll(); ApplyRequestedScroll(); UpdateVisible(false); });
            RegisterCallback<AttachToPanelEvent>(_ => { ApplyRequestedScroll(); UpdateVisible(false); });
            RegisterCallback<WheelEvent>(_ => pendingScroll = false, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(_ => pendingScroll = false, TrickleDown.TrickleDown);
        }

        internal void Rebuild()
        {
            for (int i = 0; i < slots.Count; i++) Unbind(slots[i]);
            offsets = new float[Count + 1];
            for (int i = 0; i < Count; i++)
            {
                float height = itemHeight?.Invoke(i) ?? fixedItemHeight;
                if (!float.IsFinite(height) || height <= 0) throw new InvalidOperationException("Invalid list row height.");
                offsets[i + 1] = offsets[i] + height;
            }
            scroll.contentContainer.style.height = TotalHeight;
            ClampScroll(); UpdateVisible(true);
        }

        void ClampScroll()
        {
            float viewport = scroll.contentViewport.layout.height;
            if (!float.IsFinite(viewport) || viewport <= 0) return;
            scroll.verticalScroller.lowValue = 0;
            scroll.verticalScroller.highValue = Mathf.Max(0, TotalHeight - viewport);
            float y = Mathf.Clamp(scroll.scrollOffset.y, 0, scroll.verticalScroller.highValue);
            if (Mathf.Abs(y - scroll.scrollOffset.y) > .01f) scroll.scrollOffset = new Vector2(0, y);
        }

        int IndexAt(float y)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (offsets[mid + 1] <= y) lo = mid + 1; else hi = mid;
            }
            return Mathf.Min(lo, Count - 1);
        }

        internal void GetAnchor(out int index, out float fraction)
        {
            index = IndexAt(scroll.scrollOffset.y);
            fraction = index < 0 ? 0 : Mathf.Clamp01((scroll.scrollOffset.y - offsets[index]) / (offsets[index + 1] - offsets[index]));
        }

        internal void RestoreAnchor(int index, float fraction)
        {
            requestedIndex = index; requestedFraction = fraction;
            pendingScroll = true; revealRequested = false; ApplyRequestedScroll();
        }

        internal void ScrollToItem(int index)
        {
            requestedIndex = index; pendingScroll = true; revealRequested = true; ApplyRequestedScroll();
        }

        void ApplyRequestedScroll()
        {
            if (!pendingScroll || offsets.Length != Count + 1) return;
            float viewport = scroll.contentViewport.layout.height;
            if (panel == null || !float.IsFinite(viewport) || viewport <= 0) return;
            pendingScroll = false;
            if (Count == 0) return;
            int index = requestedIndex;
            if (!revealRequested)
            {
                index = Mathf.Clamp(index, 0, Count - 1);
                SetOffset(offsets[index] + Mathf.Clamp01(requestedFraction) * (offsets[index + 1] - offsets[index]));
                return;
            }
            if (index == -1) { SetOffset(TotalHeight); return; }
            if (index < 0 || index >= Count) return;
            float y = scroll.scrollOffset.y;
            if (offsets[index] < y) y = offsets[index];
            else if (offsets[index + 1] > y + viewport) y = offsets[index + 1] - viewport;
            SetOffset(y);
        }

        void SetOffset(float y)
        {
            ClampScroll();
            scroll.scrollOffset = new Vector2(0, Mathf.Clamp(y, 0, scroll.verticalScroller.highValue));
            UpdateVisible(false);
        }

        internal void RefreshVisible() => UpdateVisible(true);
        void UpdateVisible(bool force)
        {
            if (updating || offsets.Length != Count + 1 || makeItem == null || bindItem == null) return;
            float viewport = scroll.contentViewport.layout.height;
            if (!float.IsFinite(viewport) || viewport <= 0) return;
            updating = true;
            try
            {
                int first = Count == 0 ? 0 : Mathf.Max(0, IndexAt(scroll.scrollOffset.y) - 1);
                int last = Count == 0 ? -1 : Mathf.Min(Count - 1, IndexAt(scroll.scrollOffset.y + viewport) + 1);
                foreach (var slot in slots) if (slot.index < first || slot.index > last) Unbind(slot);
                for (int index = first; index <= last; index++)
                {
                    Slot slot = null;
                    for (int i = 0; i < slots.Count; i++) if (slots[i].index == index) { slot = slots[i]; break; }
                    bool bind = slot == null || force;
                    if (slot == null)
                    {
                        for (int i = 0; i < slots.Count; i++) if (slots[i].index < 0) { slot = slots[i]; break; }
                        if (slot == null)
                        {
                            slot = new Slot { element = makeItem() };
                            slot.element.style.position = Position.Absolute;
                            slot.element.style.left = slot.element.style.right = 0;
                            slots.Add(slot); scroll.Add(slot.element);
                        }
                        slot.index = index;
                    }
                    slot.element.style.display = DisplayStyle.Flex;
                    slot.element.style.top = offsets[index];
                    slot.element.style.height = offsets[index + 1] - offsets[index];
                    if (bind) bindItem(slot.element, index);
                }
            }
            finally { updating = false; }
        }

        void Unbind(Slot slot)
        {
            if (slot.index >= 0) unbindItem?.Invoke(slot.element, slot.index);
            slot.index = -1; slot.element.style.display = DisplayStyle.None;
        }
    }
}
