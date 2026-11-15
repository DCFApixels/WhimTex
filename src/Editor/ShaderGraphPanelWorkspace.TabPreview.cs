using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    internal sealed partial class ShaderGraphPanelWorkspace
    {
        private sealed class TabHeaderState
        {
            internal VisualElement header;
            internal Rect bounds;
            internal StyleTranslate translate;
            internal StyleFloat opacity;
            internal bool excluded;
        }
        private readonly List<TabHeaderState> tabPreviewHeaders = new();
        private readonly List<VisualElement> tabGhosts = new();
        private VisualElement tabPreviewOverlay, tabGhostStrip;
        private Pane tabPreviewPane;
        private ShaderGraphFloatingPanel tabPreviewMoving;
        private StyleFloat tabPreviewMovingOpacity;
        private StyleLength tabPreviewViewportWidth;
        private ShaderGraphPanelKind? draggedTab;
        private float draggedTabOffset, tabPreviewWidth, tabPreviewStart;
        private int tabPreviewIndex;

        private void ShowTabPreview(Pane pane, Vector2 position, ShaderGraphFloatingPanel moving, ShaderGraphPanelKind? except)
        {
            if (tabPreviewPane != pane || tabPreviewMoving != moving)
            {
                ClearTabPreview();
                tabPreviewPane = pane; tabPreviewMoving = moving;
                if (moving != null) { tabPreviewMovingOpacity = moving.style.opacity; moving.style.opacity = 0; }
                if (tabPreviewOverlay == null)
                {
                    tabPreviewOverlay = new VisualElement { name = "graph-panel-tab-preview", pickingMode = PickingMode.Ignore };
                    tabPreviewOverlay.AddToClassList("whimtex-shader-graph-tool-panel");
                    tabPreviewOverlay.AddToClassList("whimtex-shader-graph-tab-preview");
                    tabGhostStrip = new VisualElement { name = "graph-panel-tab-ghost", pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
                    tabGhostStrip.AddToClassList("whimtex-shader-graph-tab-ghost");
                    tabPreviewOverlay.Add(tabGhostStrip); viewport.Add(tabPreviewOverlay);
                }
                var bar = pane.header.worldBound;
                foreach (var kind in pane.node.tabs)
                {
                    var header = tabsByKind[kind].tabHeader;
                    Rect rect = header.worldBound; rect.position -= bar.position;
                    tabPreviewHeaders.Add(new TabHeaderState { header = header, bounds = rect,
                        translate = header.style.translate, opacity = header.style.opacity, excluded = kind == except });
                    if (kind == except) header.style.opacity = 0;
                }
                tabPreviewStart = tabPreviewHeaders[0].bounds.x;
                var members = except.HasValue ? new List<ShaderGraphPanelKind> { except.Value } : layout.Members(panels[moving].root);
                tabPreviewWidth = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    if (tabGhosts.Count == i)
                    {
                        var ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                        ghost.AddToClassList("whimtex-shader-graph-panel-title");
                        ghost.AddToClassList("whimtex-shader-graph-tab-ghost-title");
                        var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("unity-tab__header-label");
                        ghost.Add(label); tabGhosts.Add(ghost); tabGhostStrip.Add(ghost);
                    }
                    var source = tabsByKind[members[i]];
                    float width = source.tabHeader.worldBound.width;
                    tabGhosts[i].style.display = DisplayStyle.Flex;
                    tabGhosts[i].style.width = width;
                    ((Label)tabGhosts[i][0]).text = source.label;
                    tabPreviewWidth += width;
                }
                for (int i = members.Count; i < tabGhosts.Count; i++) tabGhosts[i].style.display = DisplayStyle.None;
                if (!draggedTab.HasValue) draggedTabOffset = tabPreviewWidth * .5f;
                var tabs = (TabView)pane.root;
                tabPreviewViewportWidth = tabs.contentViewport.style.width;
                float total = tabPreviewWidth;
                foreach (var state in tabPreviewHeaders) if (!state.excluded) total += state.bounds.width;
                tabs.contentViewport.style.width = total;
                tabPreviewIndex = except.HasValue ? pane.node.tabs.IndexOf(except.Value) : InitialTabSlot(position.x - bar.xMin);
                tabPreviewOverlay.style.display = DisplayStyle.Flex;
                tabPreviewOverlay.BringToFront();
            }
            hintVisible = false; dropHint.style.display = DisplayStyle.None;
            UpdateTabPreview(position);
        }
        private int InitialTabSlot(float x)
        {
            int index = 0; float edge = tabPreviewStart;
            foreach (var state in tabPreviewHeaders)
            {
                if (state.excluded) continue;
                if (x < edge + state.bounds.width * .5f) break;
                edge += state.bounds.width; index++;
            }
            return index;
        }
        private void UpdateTabPreview(Vector2 position)
        {
            Rect bar = tabPreviewPane.header.worldBound;
            float ghostX = position.x - bar.xMin - draggedTabOffset;
            float center = ghostX + tabPreviewWidth * .5f;
            int index = 0, next = tabPreviewIndex;
            float edge = tabPreviewStart;
            // Compare against the displayed gap, not translated world bounds. Crossing back has a
            // different threshold, so a stationary pointer cannot bounce between adjacent slots.
            foreach (var state in tabPreviewHeaders)
            {
                if (state.excluded) continue;
                float middle = edge + state.bounds.width * .5f + (index >= tabPreviewIndex ? tabPreviewWidth : 0);
                if (index < tabPreviewIndex && center < middle) next = Mathf.Min(next, index);
                if (index >= tabPreviewIndex && center > middle) next = index + 1;
                edge += state.bounds.width; index++;
            }
            tabPreviewIndex = next; edge = tabPreviewStart; index = 0;
            foreach (var state in tabPreviewHeaders)
            {
                if (state.excluded) continue;
                float x = edge + (index >= tabPreviewIndex ? tabPreviewWidth : 0);
                state.header.style.translate = new Translate(x - state.bounds.x, 0);
                edge += state.bounds.width; index++;
            }
            var local = viewport.WorldToLocal(bar.position);
            tabPreviewOverlay.style.left = local.x; tabPreviewOverlay.style.top = local.y;
            tabPreviewOverlay.style.width = bar.width; tabPreviewOverlay.style.height = bar.height;
            tabGhostStrip.style.translate = new Translate(ghostX, 0);
        }
        private int TabDropIndex(Pane pane, Vector2 position)
        {
            if (pane == tabPreviewPane) { UpdateTabPreview(position); return tabPreviewIndex; }
            int index = 0;
            foreach (var kind in pane.node.tabs)
            {
                if (position.x < tabsByKind[kind].tabHeader.worldBound.center.x) break;
                index++;
            }
            return index;
        }
        private void ClearTabPreview()
        {
            if (tabPreviewPane == null) return;
            foreach (var state in tabPreviewHeaders)
            { state.header.style.translate = state.translate; state.header.style.opacity = state.opacity; }
            ((TabView)tabPreviewPane.root).contentViewport.style.width = tabPreviewViewportWidth;
            if (tabPreviewMoving != null) tabPreviewMoving.style.opacity = tabPreviewMovingOpacity;
            tabPreviewOverlay.style.display = DisplayStyle.None;
            tabPreviewHeaders.Clear(); tabPreviewPane = null; tabPreviewMoving = null;
        }
    }
}
