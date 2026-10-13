using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class WhimTexWindow
    {
        private readonly struct LayerDropTarget
        {
            internal readonly List<Layer> Container;
            internal readonly int Index, Depth;
            internal readonly Layer Expand;
            internal readonly VisualElement Indicator;
            internal readonly bool Before;
            internal LayerDropTarget(List<Layer> container, int index, int depth,
                Layer expand, VisualElement indicator, bool before)
            { Container = container; Index = index; Depth = depth; Expand = expand; Indicator = indicator; Before = before; }
        }

        // Geometry determines placement independently of the native drag payload.
        private bool TryResolveLayerDropTarget(Vector2 point, out LayerDropTarget result)
        {
            result = default;
            if (activeDocument == null || toolkitLayerHierarchyRoot?.panel == null ||
                !toolkitSettingsScroll.contentViewport.worldBound.Contains(point)) return false;
            foreach (VisualElement row in toolkitLayerHierarchyRoot.Children())
            {
                if (!row.ClassListContains("whimtex-layer-row") || !row.worldBound.Contains(point)) continue;
                if (!(row.userData is string id) || activeDocument.FindLayer(id) is not Layer layer ||
                    !activeDocument.TryFindLayer(layer, out var container, out int index)) return false;
                return ResolveLayerRowDrop(row, point, layer, container, index, out result);
            }
            if (!IsLayerListEndDropArea(point)) return false;
            result = new LayerDropTarget(activeDocument.layers, activeDocument.layers.Count, 0,
                null, toolkitLayerEndDropZone ?? toolkitLayerHierarchyRoot, true);
            return true;
        }

        private bool ResolveLayerRowDrop(VisualElement row, Vector2 point, Layer layer,
            List<Layer> container, int index, out LayerDropTarget result)
        {
            int treeIndex = -1, depth = 0;
            for (int i = 0; i < toolkitLayerTree.Count; i++)
                if (toolkitLayerTree[i].Layer == layer) { treeIndex = i; depth = toolkitLayerTree[i].Depth; break; }
            float fraction = row.WorldToLocal(point).y / Mathf.Max(1f, row.resolvedStyle.height);
            bool expandedChildren = layer.IsGroup && GetGroupExpanded(layer) && layer.layers.Count > 0;
            if (layer.IsGroup && fraction >= .25f && (expandedChildren || fraction <= .75f))
            {
                result = new LayerDropTarget(layer.layers, 0, depth + 1, layer, row, true);
                return true;
            }
            bool before = layer.IsGroup ? fraction < .25f : fraction < .5f;
            result = new LayerDropTarget(container, index + (before ? 0 : 1), depth, null, row, before);
            if (treeIndex < 0) return true;

            // At a shared boundary start from the deepest preceding visible row.
            LayerTreeEntry previous = toolkitLayerTree[treeIndex];
            if (before)
            {
                bool found = false;
                for (int i = treeIndex - 1; i >= 0; i--)
                    if (toolkitLayerTree[i].Layer != null) { previous = toolkitLayerTree[i]; found = true; break; }
                if (!found || previous.Depth < depth) return true;
            }
            container = previous.Container;
            index = previous.Index + 1;
            depth = previous.Depth;
            var boundary = new LayerDropTarget(container, index, depth, null, row, before);
            float lineOrigin = row.worldBound.xMin + 26f;
            int requestedDepth = Mathf.Max(0, Mathf.FloorToInt((point.x - lineOrigin) / ToolkitLayerIndent));
            while (index == container.Count && depth > requestedDepth && depth > 0)
            {
                if (!activeDocument.TryFindParentGroup(container, out _, out var parentContainer, out int parentIndex)) break;
                container = parentContainer;
                index = parentIndex + 1;
                depth--;
                boundary = new LayerDropTarget(container, index, depth, null, row, before);
            }
            result = boundary;
            return true;
        }

        private void ShowLayerDropTarget(LayerDropTarget target)
            => SetToolkitDropIndicator(target.Indicator, target.Expand != null, target.Before, target.Depth);
    }
}
