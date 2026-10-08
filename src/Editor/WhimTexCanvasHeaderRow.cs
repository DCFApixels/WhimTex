using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed class WhimTexCanvasHeaderRow : VisualElement
    {
        internal const string CompactClass = "whimtex-canvas-header-row--compact";
        private readonly HashSet<VisualElement> observed = new HashSet<VisualElement>();
        private readonly List<VisualElement> tracks = new List<VisualElement>();
        private readonly Dictionary<VisualElement, ControlWidth> controlWidths = new Dictionary<VisualElement, ControlWidth>();
        private IVisualElementScheduledItem pending;
        private float settledWidth = float.NaN;
        private bool settledCompact;

        private sealed class ControlWidth
        {
            internal float expanded, compact;
            internal int tracks;
            internal bool compactMeasured;
        }

        internal WhimTexCanvasHeaderRow()
        {
            AddToClassList("whimtex-canvas-header-row");
            RegisterCallback<AttachToPanelEvent>(_ => RefreshControls());
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<DetachFromPanelEvent>(_ => pending?.Pause());
        }

        internal void RefreshControls()
        {
            var removed = new List<VisualElement>();
            foreach (var element in observed)
                if (!Contains(element)) removed.Add(element);
            foreach (var element in removed)
            {
                element.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                observed.Remove(element);
                controlWidths.Remove(element);
            }
            tracks.Clear();
            Observe(this);
            RequestLayout();
        }

        private void Observe(VisualElement parent)
        {
            foreach (var element in parent.Children())
            {
                if (observed.Add(element))
                {
                    element.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                    if (element is Slider slider) slider.showInputField = true;
                    if (element is SliderInt sliderInt) sliderInt.showInputField = true;
                    TwoChoiceDropdown.Attach(element);
                }
                if (element.ClassListContains(BaseSlider<float>.dragContainerUssClassName)) tracks.Add(element);
                Observe(element);
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => RequestLayout();

        private void RequestLayout()
        {
            if (panel == null) return;
            if (pending == null) pending = schedule.Execute(UpdateLayout).StartingIn(16);
            else pending.ExecuteLater(16);
        }

        private void UpdateLayout()
        {
            if (tracks.Count == 0 || resolvedStyle.display == DisplayStyle.None || contentRect.width <= 0) return;
            bool compact = ClassListContains(CompactClass);
            foreach (var track in tracks)
            {
                if (!AncestorsVisible(track)) continue;
                bool hidden = track.resolvedStyle.display == DisplayStyle.None;
                if (hidden != compact || (compact ? track.layout.width > .5f : track.layout.width < track.resolvedStyle.minWidth.value - .5f))
                {
                    RequestLayout();
                    return;
                }
            }
            // Class changes are applied by the next style/layout pass. Never cache
            // an intermediate width or feed it back into the compact breakpoint.
            float actualWidth = 0;
            foreach (var child in Children())
                if (child.resolvedStyle.display != DisplayStyle.None)
                    actualWidth += child.resolvedStyle.width + child.resolvedStyle.marginLeft + child.resolvedStyle.marginRight;
            if (settledCompact != compact || Mathf.Abs(settledWidth - actualWidth) > .1f || float.IsNaN(settledWidth))
            {
                settledCompact = compact;
                settledWidth = actualWidth;
                RequestLayout();
                return;
            }
            float required = 0;
            foreach (var child in Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None) continue;
                float width = child.resolvedStyle.width + child.resolvedStyle.marginLeft + child.resolvedStyle.marginRight;
                int count = 0;
                float footprint = 0;
                foreach (var track in tracks)
                    if (child.Contains(track) && AncestorsVisible(track))
                    {
                        count++;
                        footprint += track.resolvedStyle.minWidth.value + track.resolvedStyle.marginLeft + track.resolvedStyle.marginRight;
                    }
                if (count == 0) { required += width; continue; }
                if (!controlWidths.TryGetValue(child, out var measured))
                {
                    measured = new ControlWidth { expanded = width + (compact ? footprint : 0), tracks = count };
                    controlWidths.Add(child, measured);
                }
                if (!compact)
                {
                    measured.expanded = width;
                    measured.compactMeasured = false;
                }
                else
                {
                    if (measured.compactMeasured) measured.expanded += width - measured.compact;
                    if (count != measured.tracks) measured.expanded += footprint * (count - measured.tracks) / count;
                    measured.compact = width;
                    measured.compactMeasured = true;
                }
                measured.tracks = count;
                required += measured.expanded;
            }
            EnableInClassList(CompactClass, compact ? contentRect.width < required + 8f : required > contentRect.width + .5f);
        }

        private bool AncestorsVisible(VisualElement element)
        {
            for (var parent = element.parent; parent != null && parent != this; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        internal static T ConfigureInput<T>(T field, float width) where T : VisualElement
        {
            TwoChoiceDropdown.Attach(field);
            if (field is Toggle || field is Slider || field is SliderInt || field is FloatField || field is IntegerField)
                return field;
            if (field is EnumField || field is DropdownField)
            {
                bool labeled = field is EnumField enumField && !string.IsNullOrEmpty(enumField.label);
                if (!labeled) field.AddToClassList("whimtex-header-dropdown-fixed");
                field.AddToClassList((labeled ? "whimtex-header-dropdown-input--" : "whimtex-header-dropdown--") + Mathf.RoundToInt(width));
            }
            else field.AddToClassList("whimtex-header-input--swatch");
            return field;
        }
    }
}
