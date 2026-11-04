using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class InputBranchLabel
        {
            internal readonly string nodeId, portName;
            internal readonly Label label;
            readonly Canvas canvas;
            readonly string text;
            bool connected;

            internal InputBranchLabel(Canvas canvas, string nodeId, string portName, Label label)
            {
                this.canvas = canvas; this.nodeId = nodeId; this.portName = portName; this.label = label;
                text = label.text;
                label.AddToClassList("whimtex-shader-graph-input-label");
                label.RegisterCallback<PointerEnterEvent>(_ => { if (connected) canvas.ShowInputBranch(this); });
                label.RegisterCallback<PointerLeaveEvent>(_ => canvas.HideInputBranch(this));
                label.RegisterCallback<DetachFromPanelEvent>(_ => canvas.HideInputBranch(this));
                label.RegisterCallback<TooltipEvent>(e => { e.tooltip = string.Empty; e.StopPropagation(); });
                var click = new Clickable(e =>
                {
                    bool additive = e is IPointerEvent p ? p.ctrlKey || p.commandKey : e is IMouseEvent m && (m.ctrlKey || m.commandKey);
                    if (connected) canvas.SelectInputBranch(this, additive);
                });
                click.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Control });
                click.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = EventModifiers.Command });
                label.AddManipulator(click);
                Refresh();
            }

            internal void Refresh()
            {
                var graph = canvas.window.Graph;
                var node = graph?.FindNode(nodeId);
                connected = node != null && ShaderGraphNodes.InputInactiveReason(node, portName) == null &&
                    graph.edges.Exists(e => e.inputNode == nodeId && e.inputPort == portName &&
                        ShaderGraphNodes.OutputVisible(graph.FindNode(e.outputNode), e.outputPort));
                label.pickingMode = connected ? PickingMode.Position : PickingMode.Ignore;
                label.text = connected && IsRepeatedInput(node, portName) ? "↻ " + text : text;
                if (!connected) canvas.HideInputBranch(this);
            }

            static bool IsRepeatedInput(ShaderGraphNode node, string port)
            {
                if (node == null || ShaderGraphNodes.InputInactiveReason(node, port) != null) return false;
                if (ShaderGraphNodes.UsesHeightTexture(node.kind) && node.heightSource == ShaderGraphHeightSource.Value && port == "Height")
                    return true;
                if (node.kind == ShaderGraphNodeKind.SlopeBlur && port == "Slope") return true;
                if (node.kind == ShaderGraphNodeKind.BilateralBlur && node.option == 1 && port == "Guide") return true;
                return port == "In" && ShaderGraphNodes.IsFilter(node.kind) && node.kind != ShaderGraphNodeKind.Buffer;
            }

        }

        private sealed partial class Canvas
        {
            const string BranchHighlight = "whimtex-shader-graph-branch-highlight";
            readonly Dictionary<string, ShaderGraphBranchTrace> branchTraces = new();
            readonly List<VisualElement> branchHighlights = new();
            readonly List<VisualElement> bufferBoundaries = new();
            InputBranchLabel hoveredBranch;

            internal void ShowInputBranch(InputBranchLabel label)
            {
                ClearBranchHighlight(); hoveredBranch = label;
                var trace = GetInputBranch(label);
                if (trace == null) return;
                foreach (string id in trace.nodes) if (nodeViews.TryGetValue(id, out var node)) Highlight(node);
                var markedPorts = new HashSet<Port>();
                foreach (var edge in edges)
                {
                    if (edge.userData is not ShaderGraphEdge data || !trace.edges.Contains(data)) continue;
                    Highlight(edge);
                    if (!trace.buffers.Contains(data.outputNode) || !markedPorts.Add(edge.output)) continue;
                    var connector = edge.output?.Q("connector");
                    if (connector == null) continue;
                    var mark = new VisualElement { name = "buffer-boundary", pickingMode = PickingMode.Ignore };
                    mark.AddToClassList("whimtex-shader-graph-buffer-boundary");
                    connector.Add(mark); bufferBoundaries.Add(mark);
                }
            }

            internal void SelectInputBranch(InputBranchLabel label, bool additive)
            {
                var trace = GetInputBranch(label);
                if (trace == null) return;
                Focus();
                if (!additive) ClearSelection();
                foreach (string id in trace.nodes)
                    if (nodeViews.TryGetValue(id, out var node) && !selection.Contains(node)) AddToSelection(node);
            }

            ShaderGraphBranchTrace GetInputBranch(InputBranchLabel label)
            {
                string key = label.nodeId + ":" + label.portName;
                if (!branchTraces.TryGetValue(key, out var trace))
                {
                    try
                    {
                        using var profile = WhimTexProfiler.Measure("shadergraph.branch.trace", window.document);
                        trace = ShaderGraphCompiler.TraceInputBranch(window.Graph, label.nodeId, label.portName, window.Target.IsEffect);
                    }
                    catch (Exception) { trace = null; }
                    branchTraces[key] = trace;
                }
                return trace;
            }

            void Highlight(VisualElement element)
            { element.AddToClassList(BranchHighlight); branchHighlights.Add(element); }

            internal void HideInputBranch(InputBranchLabel label)
            {
                if (!ReferenceEquals(hoveredBranch, label)) return;
                ClearBranchHighlight(); hoveredBranch = null;
            }

            void ClearBranchHighlight()
            {
                foreach (var element in branchHighlights) element.RemoveFromClassList(BranchHighlight);
                branchHighlights.Clear();
                foreach (var mark in bufferBoundaries) mark.RemoveFromHierarchy();
                bufferBoundaries.Clear();
            }

            void ResetBranchTraces(bool refreshHover = false)
            {
                var previous = hoveredBranch;
                ClearBranchHighlight(); hoveredBranch = null; branchTraces.Clear();
                if (refreshHover && previous?.label.panel != null && previous.label.enabledInHierarchy && previous.label.resolvedStyle.display != DisplayStyle.None)
                    ShowInputBranch(previous);
            }
        }
    }
}
