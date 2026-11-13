#if WHIMTEX_DEEP_DEBUG
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private const string CreateDebugGraphUndo = "Create Debug Graph";
        private ShaderGraph debugGraphLayout;

        internal static void CreateDebugGraph(ShaderGraphEditorTarget target)
        {
            if (!target.GraphActive || target.IsReadOnly || target.IsLinked) return;

            ShaderGraph graph;
            try { graph = BuildDebugGraph(target.Graph, target.IsEffect); }
            catch (Exception error)
            {
                EditorUtility.DisplayDialog(CreateDebugGraphUndo, error.Message, "OK");
                return;
            }

            var window = OpenTarget(target.Document, target.Layer, target.Effect, target.IsEffect);
            if (window == null) return;
            window.debugGraphLayout = graph;
            window.OpenSubGraph(null);
            if (window.canvas != null) window.canvas.ScheduleDebugGraphLayout(graph, target.Graph);
            else window.CommitDebugGraph(graph, target.Graph);
        }

        private void CommitDebugGraph(ShaderGraph graph, ShaderGraph previous)
        {
            debugGraphLayout = null;
            var target = Target;
            if (target.Graph == previous && Editable)
            {
                Undo.RegisterCompleteObjectUndo(target.Document, CreateDebugGraphUndo);
                if (target.IsEffect) Undo.RegisterCompleteObjectUndo(target.Effect, CreateDebugGraphUndo);
                target.Program.draftGraph = graph;
                target.InvalidateGraph();
                applying = true;
                try { target.MarkChanged(); }
                finally { applying = false; }
                canvas?.InvalidatePreviews();
                compileRequested = true;
                preview?.RequestLayerPreview();
            }
            refreshRequested = rebuildRequested = true;
        }

        internal static ShaderGraph BuildDebugGraph(ShaderGraph previous, bool effectContext)
        {
            var graph = ShaderProgramStorage.CopyWorkingGraph(previous);
            graph.nodes.Clear();
            graph.edges.Clear();
            graph.notes.Clear();
            graph.groups.Clear();
            graph.PruneFunctions();

            if (graph.parameters.Count == 0)
                graph.parameters.Add(new ShaderParameter { name = "Value", type = ShaderParameterType.Float });
            if (graph.subGraphs.Count == 0)
                graph.subGraphs.Add(CreateDebugSubGraph());

            var groups = new Dictionary<string, ShaderGraphGroup>(StringComparer.Ordinal);
            void Add(ShaderGraphNode node, string category)
            {
                graph.nodes.Add(node);
                if (!groups.TryGetValue(category, out var group))
                {
                    group = new ShaderGraphGroup { title = category };
                    groups.Add(category, group);
                    graph.groups.Add(group);
                }
                group.elements.Add(node.id);
            }

            var path = new List<string>();
            foreach (var entry in CreateNodeSearchTree(graph, replacing: false, svmOnly: false))
            {
                if (entry.level == 0) continue;
                int depth = entry.level - 1;
                if (path.Count > depth) path.RemoveRange(depth, path.Count - depth);
                if (entry is SearchTreeGroupEntry)
                {
                    path.Add(entry.content.text);
                    continue;
                }

                string category = string.Join(" / ", path);
                switch (entry.userData)
                {
                    case ShaderGraphNodeKind.ForEnd:
                        // Adding For Begin creates both endpoints and their default connection.
                        break;
                    case ShaderGraphNodeKind kind:
                        var node = ShaderGraphNodes.Create(kind, Vector2.zero);
                        if (node.function != null) graph.functions.Add(node.function);
                        Add(node, category);
                        var partner = ShaderGraphLoops.AddPartner(graph, node);
                        if (partner != null) groups[category].elements.Add(partner.id);
                        break;
                    case ShaderGraphFunctionCatalog.Entry function:
                        var functionNode = ShaderGraphNodes.Create(ShaderGraphNodeKind.CustomFunction, Vector2.zero);
                        ShaderGraphNodes.SetFunction(functionNode, function.Import(graph));
                        Add(functionNode, category);
                        break;
                    case ShaderGraphSubGraph definition:
                        var subGraphNode = ShaderGraphNodes.Create(ShaderGraphNodeKind.SubGraph, Vector2.zero);
                        subGraphNode.subGraphId = definition.id;
                        Add(subGraphNode, category);
                        break;
                    case string parameterId:
                        var parameterNode = ShaderGraphNodes.Create(ShaderGraphNodeKind.Parameter, Vector2.zero);
                        parameterNode.parameterId = parameterId;
                        Add(parameterNode, category);
                        break;
                }
            }

            Add(ShaderGraphNodes.Create(ShaderGraphNodeKind.Reroute, Vector2.zero), "Utility");
            Add(ShaderGraphNodes.Create(ShaderGraphNodeKind.Output, Vector2.zero), "Output");
            foreach (var node in graph.AllNodes())
                node.previewExpanded = node.codeExpanded = false;
            graph.BindFunctions();
            ShaderGraphCompiler.ValidateStorage(graph, effectContext);
            ArrangeDebugGraph(graph, node => new Rect(-240, 0, 580,
                100 + 32 * Mathf.Max(ShaderGraphNodes.StoredInputs(node).Count, ShaderGraphNodes.StoredOutputs(node).Length)));
            return graph;
        }

        private static ShaderGraphSubGraph CreateDebugSubGraph()
        {
            var definition = new ShaderGraphSubGraph();
            var input = new ShaderGraphInterfacePort { name = "In" };
            var output = new ShaderGraphInterfacePort { name = "Out" };
            definition.inputs.Add(input);
            definition.outputs.Add(output);
            var from = ShaderGraphNodes.Create(ShaderGraphNodeKind.SubGraphInput, new Vector2(40, 80));
            from.parameterId = input.id;
            var to = ShaderGraphNodes.Create(ShaderGraphNodeKind.SubGraphOutput, new Vector2(360, 80));
            definition.nodes.Add(from);
            definition.nodes.Add(to);
            definition.edges.Add(new ShaderGraphEdge { outputNode = from.id, inputNode = to.id, inputPort = output.id });
            return definition;
        }

        private static void ArrangeDebugGraph(ShaderGraph graph, Func<ShaderGraphNode, Rect> measure)
        {
            const float margin = 48, columnGap = 80, rowGap = 24;
            var nodes = new Dictionary<string, ShaderGraphNode>(graph.nodes.Count);
            foreach (var node in graph.nodes) nodes.Add(node.id, node);
            float x = margin;
            foreach (var group in graph.groups)
            {
                var bounds = new Rect[group.elements.Count];
                float left = 0, right = 0;
                for (int i = 0; i < bounds.Length; i++)
                {
                    bounds[i] = measure(nodes[group.elements[i]]);
                    left = Mathf.Min(left, bounds[i].xMin);
                    right = Mathf.Max(right, bounds[i].xMax);
                }
                float y = margin;
                float origin = x - left;
                group.position = new Vector2(origin, y);
                for (int i = 0; i < bounds.Length; i++)
                {
                    var rect = bounds[i];
                    nodes[group.elements[i]].position = new Vector2(origin, y - rect.yMin);
                    y += rect.height + rowGap;
                }
                x += right - left + columnGap;
            }
        }

        private sealed partial class Canvas
        {
            internal void ScheduleDebugGraphLayout(ShaderGraph graph, ShaderGraph previous)
            {
                int attempts = 0;
                void Layout()
                {
                    if (panel == null || window.debugGraphLayout != graph) return;
                    if (window.Target.Graph != previous || !window.Editable)
                    {
                        window.debugGraphLayout = null;
                        window.refreshRequested = window.rebuildRequested = true;
                        return;
                    }
                    foreach (var node in graph.nodes)
                    {
                        if (nodeViews.TryGetValue(node.id, out var view) && view.layout.width > 0 && view.layout.height > 0) continue;
                        if (++attempts < 8) schedule.Execute(Layout).ExecuteLater(16);
                        else window.CommitDebugGraph(graph, previous);
                        return;
                    }

                    ArrangeDebugGraph(graph, node =>
                    {
                        var view = nodeViews[node.id];
                        Rect body = contentViewContainer.WorldToLocal(view.worldBound);
                        Rect occupied = body;
                        foreach (var input in view.Query<VisualElement>(className: "whimtex-shader-graph-port-value").ToList())
                        {
                            var bounds = contentViewContainer.WorldToLocal(input.worldBound);
                            occupied = Rect.MinMaxRect(Mathf.Min(occupied.xMin, bounds.xMin), Mathf.Min(occupied.yMin, bounds.yMin),
                                Mathf.Max(occupied.xMax, bounds.xMax), Mathf.Max(occupied.yMax, bounds.yMax));
                        }
                        occupied.position -= body.position;
                        return occupied;
                    });
                    window.CommitDebugGraph(graph, previous);
                }
                schedule.Execute(Layout);
            }
        }
    }
}
#endif
