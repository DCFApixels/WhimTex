using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed partial class Canvas
        {
            internal void ArrangeSelected()
            {
                if (!window.Editable || window.Graph == null) return;
                var moving = new HashSet<string>();
                foreach (var selected in selection)
                    if (selected is INodeView view) moving.Add(view.Id);
                if (moving.Count < 2) return;

                var geometry = new Dictionary<string, ShaderGraphArrangement.Geometry>(nodeViews.Count);
                foreach (var pair in nodeViews)
                {
                    var view = pair.Value;
                    var bounds = contentViewContainer.WorldToLocal(view.worldBound);
                    if (!(bounds.width > 0 && bounds.height > 0)) continue;

                    var origin = view.GetPosition().position;
                    view.Query<VisualElement>(className: "whimtex-shader-graph-value-fields").ForEach(field =>
                    {
                        if (field.worldBound.width > 0 && field.worldBound.height > 0)
                            bounds = ShaderGraphArrangement.Union(bounds, contentViewContainer.WorldToLocal(field.worldBound));
                    });
                    var item = new ShaderGraphArrangement.Geometry { bounds = ShaderGraphArrangement.Translate(bounds, -origin) };
                    view.Query<Port>().ForEach(port =>
                    {
                        if (port.userData is not PortData data) return;
                        var connector = port.Q("connector") ?? port;
                        var point = contentViewContainer.WorldToLocal(connector.worldBound.center) - origin;
                        (port.direction == Direction.Output ? item.outputs : item.inputs)[data.name] = point;
                    });
                    geometry.Add(pair.Key, item);
                }

                var groups = new Dictionary<string, Rect>(groupViews.Count);
                foreach (var pair in groupViews)
                {
                    var bounds = contentViewContainer.WorldToLocal(pair.Value.worldBound);
                    if (bounds.width > 0 && bounds.height > 0) groups.Add(pair.Key, bounds);
                }

                var positions = ShaderGraphArrangement.Plan(window.Graph, moving, geometry, groups, keepNearSelection: true);
                if (!window.Graph.nodes.Exists(n => positions.TryGetValue(n.id, out var p) && (n.position - p).sqrMagnitude > .01f)) return;
                window.Edit("Arrange Selected Shader Nodes", graph =>
                {
                    foreach (var node in graph.nodes)
                        if (positions.TryGetValue(node.id, out var position)) node.position = position;
                }, structural: false, rebuild: false, layoutOnly: true);
            }

            private void ArrangeMenu(ContextualMenuPopulateEvent e)
            {
                if (!window.Editable) return;
                int count = 0;
                foreach (var selected in selection)
                    if (selected is INodeView && ++count == 2)
                    {
                        e.menu.AppendAction("Arrange Selected", _ => ArrangeSelected());
                        return;
                    }
            }
        }
    }
}
