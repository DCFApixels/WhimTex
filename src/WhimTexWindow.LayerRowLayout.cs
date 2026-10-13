using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        [NonSerialized] private LayerRowLayout toolkitLayerRowLayout;

        private void RegisterLayerRowLayout(VisualElement row, VisualElement nameCell, VisualElement name,
            VisualElement opacity, VisualElement blend)
        {
            if (row.ClassListContains("whimtex-layer-table-header"))
            {
                toolkitLayerRowLayout?.Dispose();
                toolkitLayerRowLayout = new LayerRowLayout(row);
            }
            toolkitLayerRowLayout ??= new LayerRowLayout(row);
            toolkitLayerRowLayout.Register(row, nameCell, name, opacity, blend);
        }

        private sealed class LayerRowLayout
        {
            private const string HideBlendClass = "whimtex-layer-row--hide-blend";
            private const string HideOpacityClass = "whimtex-layer-row--hide-opacity";
            private const float MinimumNameWidth = 96f;
            private const float OpacityWidth = 52f;
            private const float BlendWidth = 130f;

            private readonly List<Row> rows = new List<Row>();
            private VisualElement anchor;
            private IVisualElementScheduledItem refresh;
            private bool queued;
            private bool hideBlend;
            private bool hideOpacity;
            private bool disposed;

            internal LayerRowLayout(VisualElement anchor) { this.anchor = anchor; }

            internal void Register(VisualElement element, VisualElement nameCell, VisualElement name,
                VisualElement opacity, VisualElement blend)
            {
                new Row(this, element, nameCell, name, opacity, blend);
            }

            internal void Dispose()
            {
                disposed = true;
                refresh?.Pause();
                queued = false;
                foreach (var row in rows) row.Dispose();
                rows.Clear();
                anchor = null;
            }

            private void Attach(Row row)
            {
                if (disposed) return;
                if (!rows.Contains(row)) rows.Add(row);
                ApplyColumns(row.element);
                QueueRefresh();
            }

            private void Detach(Row row)
            {
                rows.Remove(row);
                if (row.element == anchor)
                {
                    refresh?.Pause();
                    queued = false;
                    return;
                }
                QueueRefresh();
            }

            private void QueueRefresh()
            {
                if (disposed || queued || anchor?.panel == null) return;
                queued = true;
                if (refresh == null) refresh = anchor.schedule.Execute(Refresh);
                else refresh.ExecuteLater(0);
            }

            private void Refresh()
            {
                queued = false;
                if (disposed || anchor?.panel == null) return;

                bool measured = false;
                float minimumBudget = float.PositiveInfinity;
                foreach (var row in rows)
                {
                    if (!row.IsVisible()) continue;
                    measured = true;
                    minimumBudget = Math.Min(minimumBudget, row.NameBudget());
                }

                if (!measured) return;
                hideBlend = minimumBudget < MinimumNameWidth + OpacityWidth + BlendWidth;
                hideOpacity = minimumBudget < MinimumNameWidth + OpacityWidth;
                foreach (var row in rows)
                    ApplyColumns(row.element);
            }

            private void ApplyColumns(VisualElement element)
            {
                element.EnableInClassList(HideBlendClass, hideBlend);
                element.EnableInClassList(HideOpacityClass, hideOpacity);
            }

            private sealed class Row
            {
                internal readonly VisualElement element;
                private readonly LayerRowLayout owner;
                private readonly VisualElement nameCell;
                private readonly VisualElement name;
                private readonly VisualElement opacity;
                private readonly VisualElement blend;

                internal Row(LayerRowLayout owner, VisualElement element, VisualElement nameCell,
                    VisualElement name, VisualElement opacity, VisualElement blend)
                {
                    this.owner = owner;
                    this.element = element;
                    this.nameCell = nameCell;
                    this.name = name;
                    this.opacity = opacity;
                    this.blend = blend;
                    element.RegisterCallback<AttachToPanelEvent>(OnAttach);
                    element.RegisterCallback<DetachFromPanelEvent>(OnDetach);
                    element.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                    nameCell.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                    if (name != nameCell) name.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                    opacity.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                    blend.RegisterCallback<GeometryChangedEvent>(OnGeometry);
                    owner.ApplyColumns(element);
                    if (element.panel != null) owner.Attach(this);
                }

                internal void Dispose()
                {
                    element.UnregisterCallback<AttachToPanelEvent>(OnAttach);
                    element.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
                    element.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                    nameCell.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                    if (name != nameCell) name.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                    opacity.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                    blend.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                }

                private void OnAttach(AttachToPanelEvent evt) => owner.Attach(this);
                private void OnDetach(DetachFromPanelEvent evt) => owner.Detach(this);
                private void OnGeometry(GeometryChangedEvent evt) => owner.QueueRefresh();

                internal bool IsVisible()
                {
                    if (element.panel == null || !IsFinite(element.contentRect.width) ||
                        element.contentRect.width <= 0f) return false;
                    for (var current = element; current != null; current = current.parent)
                        if (current.resolvedStyle.display == DisplayStyle.None ||
                            current.resolvedStyle.visibility == Visibility.Hidden)
                            return false;
                    return true;
                }

                internal float NameBudget()
                {
                    float available = element.contentRect.width;
                    foreach (var child in element.Children())
                        if (child != nameCell && child != opacity && child != blend && OccupiesRow(child))
                            available -= OuterWidth(child);

                    var cellStyle = nameCell.resolvedStyle;
                    available -= HorizontalMargins(nameCell) + FiniteOrZero(cellStyle.paddingLeft) +
                        FiniteOrZero(cellStyle.paddingRight) + FiniteOrZero(cellStyle.borderLeftWidth) +
                        FiniteOrZero(cellStyle.borderRightWidth);
                    if (name != nameCell) available -= HorizontalMargins(name);
                    foreach (var child in nameCell.Children())
                        if (child != name && OccupiesRow(child))
                            available -= OuterWidth(child);
                    return available;
                }
            }

            private static bool OccupiesRow(VisualElement element) =>
                element.resolvedStyle.display != DisplayStyle.None &&
                element.resolvedStyle.position != Position.Absolute;

            private static float OuterWidth(VisualElement element) =>
                FiniteOrZero(element.layout.width) + HorizontalMargins(element);

            private static float HorizontalMargins(VisualElement element) =>
                FiniteOrZero(element.resolvedStyle.marginLeft) + FiniteOrZero(element.resolvedStyle.marginRight);

            private static float FiniteOrZero(float value) => IsFinite(value) ? value : 0f;

            private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
