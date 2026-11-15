using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPanelWorkspace
    {
        private Pane DropTarget(ShaderGraphFloatingPanel moving, Vector2 position,
            out ShaderGraphPanelGrouping grouping, out bool before, out Rect highlight)
        {
            grouping = ShaderGraphPanelGrouping.Tabs; before = false; highlight = default;
            CacheGeometry();
            Pane other = null;
            // Floating hosts are direct siblings of the canvas. Their order is also their visual stacking order.
            for (int i = viewport.childCount - 1; i >= 0; i--)
            {
                if (viewport[i] is not ShaderGraphFloatingPanel panel || panel == moving || !panel.visible ||
                    panel.resolvedStyle.display == DisplayStyle.None || !panel.worldBound.Contains(position)) continue;
                // A preview overlay still blocks a tool-panel drop, exactly as it blocks pointer picking.
                if (!panels.ContainsKey(panel)) return null;
                foreach (var pane in panes)
                {
                    if (pane.panel != panel) continue;
                    other ??= pane;
                    if (Bounds(pane).Contains(position)) { other = pane; break; }
                }
                break;
            }
            if (other == null) return null;
            if (HeaderBounds(other).Contains(position))
            { highlight = HeaderBounds(other); return other; }
            var boundary = BoundaryTarget(other.host.root, other, position, out var edgeGrouping, out bool edgeBefore, out var edgeHighlight);
            if (boundary != null)
            { grouping = edgeGrouping; before = edgeBefore; highlight = edgeHighlight; return boundary; }
            if (!Bounds(other).Contains(position)) return null;
            Rect bounds = Bounds(other);
            {
                float top = HeaderBounds(other).yMax;
                float x = (position.x - bounds.x) / Mathf.Max(1, bounds.width);
                float y = (position.y - top) / Mathf.Max(1, bounds.yMax - top);
                bool horizontal = Mathf.Min(x, 1 - x) < Mathf.Min(y, 1 - y);
                grouping = horizontal ? ShaderGraphPanelGrouping.SideBySide : ShaderGraphPanelGrouping.Sections;
                before = horizontal ? x < .5f : y < .5f;
                highlight = horizontal
                    ? new Rect(before ? bounds.x : bounds.center.x, top, bounds.width * .5f, bounds.yMax - top)
                    : new Rect(bounds.x, before ? top : (top + bounds.yMax) * .5f, bounds.width, (bounds.yMax - top) * .5f);
            }
            return other;
        }
        private Pane BoundaryTarget(string id, Pane pane, Vector2 position,
            out ShaderGraphPanelGrouping grouping, out bool before, out Rect highlight)
        {
            grouping = ShaderGraphPanelGrouping.Tabs; before = false; highlight = default;
            var node = layout.Find(id);
            if (!node.IsSplit) return null;
            var region = views[id]; Rect bounds = Bounds(region);
            if (!bounds.Contains(position)) return null;
            float left = position.x - bounds.xMin, right = bounds.xMax - position.x;
            float top = position.y - bounds.yMin, bottom = bounds.yMax - position.y;
            // The outer edge addresses the whole subtree; farther inside, a drop addresses its leaf pane.
            float edge = ShaderGraphFloatingPanel.SnapDistance * .5f;
            if (Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom)) <= edge)
            {
                bool horizontal = Mathf.Min(left, right) < Mathf.Min(top, bottom);
                grouping = horizontal ? ShaderGraphPanelGrouping.SideBySide : ShaderGraphPanelGrouping.Sections;
                before = horizontal ? left < right : top < bottom;
                highlight = horizontal
                    ? new Rect(before ? bounds.x : bounds.center.x, bounds.y, bounds.width * .5f, bounds.height)
                    : new Rect(bounds.x, before ? bounds.y : bounds.center.y, bounds.width, bounds.height * .5f);
                return region;
            }
            foreach (var child in node.children)
            {
                var target = BoundaryTarget(child, pane, position, out grouping, out before, out highlight);
                if (target != null) return target;
            }
            return null;
        }
        private void OnDrag(ShaderGraphFloatingPanel moving, Vector2 position)
        {
            var pane = DropTarget(moving, position, out var grouping, out bool before, out var rect);
            if (pane == null)
            { HideDropHint(); return; }
            if (grouping == ShaderGraphPanelGrouping.Tabs) ShowTabPreview(pane, position, moving, null);
            else { ClearTabPreview(); ShowDropHint(grouping, before, rect); }
        }
        private void ShowDropHint(ShaderGraphPanelGrouping grouping, bool before, Rect rect)
        {
            rect.position = viewport.WorldToLocal(rect.position);
            if (hintVisible && hintGrouping == grouping && hintBefore == before && hintRect == rect) return;
            hintVisible = true; hintGrouping = grouping; hintBefore = before; hintRect = rect;
            dropHint.text = grouping == ShaderGraphPanelGrouping.Tabs ? string.Empty :
                grouping == ShaderGraphPanelGrouping.SideBySide ? (before ? "Left" : "Right") : (before ? "Above" : "Below");
            dropHint.style.display = DisplayStyle.Flex;
            dropHint.style.left = rect.x; dropHint.style.top = rect.y;
            dropHint.style.width = rect.width; dropHint.style.height = rect.height;
            if (viewport[viewport.childCount - 1] != dropHint) dropHint.BringToFront();
        }
        private bool DragTab(ShaderGraphPanelKind kind, Vector2 position, Vector2 start)
        {
            if (disposed) return false;
            if (draggedTab != kind)
            {
                draggedTab = kind;
                var header = tabsByKind[kind].tabHeader.worldBound;
                draggedTabOffset = Mathf.Clamp(start.x - header.xMin, 0, header.width);
            }
            var pane = panes.Find(p => p.node.tabs.Contains(kind));
            if (pane == null || pane.node.tabs.Count < 2 || !pane.header.worldBound.Contains(position))
            { ClearTabPreview(); return false; }
            ShowTabPreview(pane, position, null, kind);
            return true;
        }
        private void DropTab(ShaderGraphPanelKind kind, Vector2 position, bool cancelled)
        {
            var pane = panes.Find(p => p.node.tabs.Contains(kind));
            int to = pane == null ? 0 : TabDropIndex(pane, position);
            HideDropHint();
            if (disposed || cancelled) return;
            if (pane == null || !pane.header.worldBound.Contains(position)) return;
            int from = pane.node.tabs.IndexOf(kind);
            if (from == to) return;
            pane.node.tabs.RemoveAt(from); pane.node.tabs.Insert(to, kind);
            MoveTab((TabView)pane.root, from, to);
            geometryDirty = true; RequestSave();
        }
        private bool geometryDirty = true, hintVisible;
        private ShaderGraphPanelGrouping hintGrouping;
        private bool hintBefore;
        private Rect hintRect;
        private void CacheGeometry()
        {
            if (!geometryDirty) return;
            geometryDirty = false;
            foreach (var pane in views.Values)
            {
                pane.bounds = pane.root.worldBound;
                pane.bounds.position -= pane.panel.worldBound.position;
                if (pane.header == null) continue;
                pane.headerBounds = pane.header.worldBound;
                pane.headerBounds.position -= pane.panel.worldBound.position;
            }
        }
        private static Rect Bounds(Pane pane)
        { var bounds = pane.bounds; bounds.position += pane.panel.worldBound.position; return bounds; }
        private static Rect HeaderBounds(Pane pane)
        { var bounds = pane.headerBounds; bounds.position += pane.panel.worldBound.position; return bounds; }
        private void HideDropHint()
        {
            ClearTabPreview();
            hintVisible = false;
            if (dropHint.style.display != DisplayStyle.None) dropHint.style.display = DisplayStyle.None;
        }
    }
}
