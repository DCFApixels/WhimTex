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
                label.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (connected && e.button == 0 && !e.altKey && !e.shiftKey)
                        canvas.BeginInputBranchClick(this, e);
                });
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
            InputBranchLabel pressedBranch;
            Vector2 branchPressPosition;
            bool branchAdditive;
            readonly List<ISelectable> branchPreviousSelection = new();

            void SetupInputBranchClicks()
            {
                RegisterCallback<MouseMoveEvent>(e =>
                {
                    if (pressedBranch != null && e.mousePosition != branchPressPosition) CancelInputBranchClick();
                }, TrickleDown.TrickleDown);
                // Run after the native dragger has finished the gesture and released capture.
                RegisterCallback<MouseUpEvent>(e =>
                {
                    if (e.button != 0) return;
                    var branch = pressedBranch;
                    pressedBranch = null;
                    if (branch != null && e.mousePosition == branchPressPosition && branch.label.panel != null)
                    {
                        if (branchAdditive)
                        {
                            ClearSelection();
                            foreach (var item in branchPreviousSelection) AddToSelection(item);
                        }
                        SelectInputBranch(branch, branchAdditive);
                    }
                    branchPreviousSelection.Clear();
                });
                RegisterCallback<MouseCaptureOutEvent>(_ => CancelInputBranchClick());
                RegisterCallback<DetachFromPanelEvent>(_ => CancelInputBranchClick());
                RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Escape) CancelInputBranchClick();
                }, TrickleDown.TrickleDown);
            }

            internal void BeginInputBranchClick(InputBranchLabel branch, MouseDownEvent e)
            {
                var node = branch.label.GetFirstAncestorOfType<Node>();
                if (node == null) return;
                CancelInputBranchClick();
                pressedBranch = branch;
                branchPressPosition = e.mousePosition;
                branchAdditive = e.ctrlKey || e.commandKey;
                if (branchAdditive) branchPreviousSelection.AddRange(selection);
                // Port is not movable. Address the node so native selection, dragging and snapping work.
                using (var down = MouseDownEvent.GetPooled(e))
                {
                    down.target = node;
                    node.SendEvent(down);
                }
                e.StopPropagation();
            }

            void CancelInputBranchClick()
            {
                pressedBranch = null;
                branchPreviousSelection.Clear();
            }

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
                CancelInputBranchClick();
                var previous = hoveredBranch;
                ClearBranchHighlight(); hoveredBranch = null; branchTraces.Clear();
                if (refreshHover && previous?.label.panel != null && previous.label.enabledInHierarchy && previous.label.resolvedStyle.display != DisplayStyle.None)
                    ShowInputBranch(previous);
            }
        }
    }
}
