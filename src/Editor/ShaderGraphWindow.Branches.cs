using System;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed class PortBranchLabel
        {
            internal readonly string nodeId, portName;
            internal readonly Label label;
            internal readonly Direction direction;
            readonly Canvas canvas;
            readonly VisualElement labelArea;
            readonly VisualElement bufferIcon;
            string text;
            bool connected;

            internal PortBranchLabel(Canvas canvas, string nodeId, string portName, Direction direction, Label label)
            {
                this.canvas = canvas; this.nodeId = nodeId; this.portName = portName; this.label = label;
                this.direction = direction;
                text = label.text;
                labelArea = label;
                label.AddToClassList("whimtex-shader-graph-branch-label");
                if (direction == Direction.Output && canvas.window.Types.Node(nodeId) is ShaderGraphNode node && ShaderGraphNodes.IsBuffered(node.kind))
                {
                    bufferIcon = new VisualElement { name = "buffer-icon", pickingMode = PickingMode.Ignore };
                    bufferIcon.AddToClassList("whimtex-shader-graph-buffer-icon");
                    bufferIcon.generateVisualContent += DrawBufferIcon;
                    labelArea = new VisualElement { name = "buffer-port-label" };
                    labelArea.AddToClassList("whimtex-shader-graph-buffer-label");
                    label.parent.Insert(label.parent.IndexOf(label), labelArea);
                    labelArea.Add(label);
                    labelArea.Add(bufferIcon);
                    label.RegisterCallback<GeometryChangedEvent>(_ => LayoutBufferIcon());
                }
                labelArea.RegisterCallback<PointerEnterEvent>(_ => { if (connected) canvas.ShowBranch(this); });
                labelArea.RegisterCallback<PointerLeaveEvent>(_ => canvas.HideBranch(this));
                labelArea.RegisterCallback<DetachFromPanelEvent>(_ => canvas.HideBranch(this));
                labelArea.RegisterCallback<TooltipEvent>(e => { e.tooltip = string.Empty; e.StopPropagation(); });
                labelArea.RegisterCallback<MouseDownEvent>(e =>
                {
                    if (connected && e.button == 0 && !e.altKey && !e.shiftKey)
                        canvas.BeginBranchClick(this, e);
                });
                Refresh();
            }

            internal void SetText(string value) { text = value; Refresh(); }

            internal void Refresh()
            {
                var graph = canvas.window.Graph;
                var node = canvas.window.Types.Node(nodeId);
                connected = node != null && (direction == Direction.Input
                    ? ShaderGraphNodes.InputInactiveReason(node, portName) == null &&
                        graph.edges.Exists(e => e.inputNode == nodeId && e.inputPort == portName &&
                            ShaderGraphNodes.OutputVisible(canvas.window.Types.Node(e.outputNode), e.outputPort))
                    : ShaderGraphNodes.OutputVisible(node, portName) &&
                        graph.edges.Exists(e => e.outputNode == nodeId && e.outputPort == portName &&
                            canvas.window.Types.Node(e.inputNode) is ShaderGraphNode target && ShaderGraphNodes.InputInactiveReason(target, e.inputPort) == null));
                labelArea.pickingMode = connected ? PickingMode.Position : PickingMode.Ignore;
                label.pickingMode = bufferIcon == null ? labelArea.pickingMode : PickingMode.Ignore;
                label.text = direction == Direction.Input && IsRepeatedInput(node, portName) ? "↻ " + text : text;
                LayoutBufferIcon();
                if (!connected) canvas.HideBranch(this);
            }

            void LayoutBufferIcon()
            {
                if (bufferIcon == null || label.panel == null) return;
                float size = label.resolvedStyle.fontSize;
                if (!(size > 0)) return;
                float space = label.MeasureTextSize("M M", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x -
                    label.MeasureTextSize("MM", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                bufferIcon.style.width = size;
                bufferIcon.style.height = size;
                bufferIcon.style.marginLeft = Mathf.Max(0, space);
            }

            void DrawBufferIcon(MeshGenerationContext context)
            {
                Rect rect = bufferIcon.contentRect;
                if (rect.width <= 0 || rect.height <= 0) return;
                Vector2 Point(float x, float y) => new Vector2(rect.x + x * rect.width, rect.y + y * rect.height);
                var painter = context.painter2D;
                painter.strokeColor = label.resolvedStyle.color;
                painter.lineWidth = Mathf.Min(rect.width, rect.height) / 12;
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .25f));
                painter.BezierCurveTo(Point(.13f, .04f), Point(.87f, .04f), Point(.87f, .25f));
                painter.BezierCurveTo(Point(.87f, .46f), Point(.13f, .46f), Point(.13f, .25f));
                painter.ClosePath(); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .25f)); painter.LineTo(Point(.13f, .75f));
                painter.BezierCurveTo(Point(.13f, .96f), Point(.87f, .96f), Point(.87f, .75f));
                painter.LineTo(Point(.87f, .25f)); painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(Point(.13f, .50f));
                painter.BezierCurveTo(Point(.13f, .71f), Point(.87f, .71f), Point(.87f, .50f));
                painter.Stroke();
            }

            static bool IsRepeatedInput(ShaderGraphNode node, string port)
            {
                if (node == null || ShaderGraphNodes.InputInactiveReason(node, port) != null) return false;
                if (node.kind == ShaderGraphNodeKind.ForEnd && port == "Value") return true;
                if (ShaderGraphNodes.IsScatterSampledInput(node, port)) return true;
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
            PortBranchLabel hoveredBranch;
            PortBranchLabel pressedBranch;
            Vector2 branchPressPosition;
            bool branchAdditive;
            readonly List<ISelectable> branchPreviousSelection = new();

            void SetupBranchClicks()
            {
                RegisterCallback<MouseMoveEvent>(e =>
                {
                    if (pressedBranch != null && e.mousePosition != branchPressPosition) CancelBranchClick();
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
                        SelectBranch(branch, branchAdditive);
                    }
                    branchPreviousSelection.Clear();
                });
                RegisterCallback<MouseCaptureOutEvent>(_ => CancelBranchClick());
                RegisterCallback<DetachFromPanelEvent>(_ => CancelBranchClick());
                RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Escape) CancelBranchClick();
                }, TrickleDown.TrickleDown);
            }

            internal void BeginBranchClick(PortBranchLabel branch, MouseDownEvent e)
            {
                var node = branch.label.GetFirstAncestorOfType<Node>();
                if (node == null) return;
                CancelBranchClick();
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

            void CancelBranchClick()
            {
                pressedBranch = null;
                branchPreviousSelection.Clear();
            }

            internal void ShowBranch(PortBranchLabel label)
            {
                ClearBranchHighlight(); hoveredBranch = label;
                var trace = GetBranch(label);
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

            internal void SelectBranch(PortBranchLabel label, bool additive)
            {
                var trace = GetBranch(label);
                if (trace == null) return;
                Focus();
                if (!additive) ClearSelection();
                foreach (string id in trace.nodes)
                    if (nodeViews.TryGetValue(id, out var node) && !selection.Contains(node)) AddToSelection(node);
            }

            ShaderGraphBranchTrace GetBranch(PortBranchLabel label)
            {
                string key = label.direction + ":" + label.nodeId + ":" + label.portName;
                if (!branchTraces.TryGetValue(key, out var trace))
                {
                    try
                    {
                        using var profile = WhimTexProfiler.Measure("shadergraph.branch.trace", window.document);
                        trace = label.direction == Direction.Input
                            ? ShaderGraphCompiler.TraceInputBranch(window.Graph, label.nodeId, label.portName, window.Target.IsEffect)
                            : TraceOutputBranch(label.nodeId, label.portName);
                    }
                    catch (Exception) { trace = null; }
                    branchTraces[key] = trace;
                }
                return trace;
            }

            ShaderGraphBranchTrace TraceOutputBranch(string nodeId, string portName)
            {
                var trace = new ShaderGraphBranchTrace();
                var outgoing = new Dictionary<string, List<ShaderGraphEdge>>();
                foreach (var edge in window.Graph.edges)
                {
                    if (!inputPorts.TryGetValue(edge.inputNode + ":" + edge.inputPort, out var input) || !input.enabledInHierarchy ||
                        !outputPorts.TryGetValue(edge.outputNode + ":" + edge.outputPort, out var output) || !output.enabledInHierarchy) continue;
                    if (!outgoing.TryGetValue(edge.outputNode, out var list)) outgoing.Add(edge.outputNode, list = new());
                    list.Add(edge);
                }
                var visited = new HashSet<string> { nodeId };
                var pending = new Queue<string>();
                pending.Enqueue(nodeId);
                while (pending.Count != 0)
                {
                    string current = pending.Dequeue();
                    if (!outgoing.TryGetValue(current, out var list)) continue;
                    foreach (var edge in list)
                    {
                        if (current == nodeId && edge.outputPort != portName) continue;
                        trace.edges.Add(edge);
                        if (!visited.Add(edge.inputNode)) continue;
                        trace.nodes.Add(edge.inputNode);
                        pending.Enqueue(edge.inputNode);
                    }
                }
                return trace;
            }

            void Highlight(VisualElement element)
            { element.AddToClassList(BranchHighlight); branchHighlights.Add(element); }

            internal void HideBranch(PortBranchLabel label)
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
                CancelBranchClick();
                var previous = hoveredBranch;
                ClearBranchHighlight(); hoveredBranch = null; branchTraces.Clear();
                if (refreshHover && previous?.label.panel != null && previous.label.enabledInHierarchy && previous.label.resolvedStyle.display != DisplayStyle.None)
                    ShowBranch(previous);
            }
        }
    }
}
