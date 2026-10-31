using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using GraphNodeView = UnityEditor.Experimental.GraphView.Node;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private sealed partial class Canvas
        {
            private ConnectedPortSnapper portSnapper;

            private static bool IsFieldControl(VisualElement element)
            {
                for (; element != null && element is not GraphNodeView && element is not GraphView; element = element.parent)
                    if (element.ClassListContains("unity-base-field") || element is Button) return true;
                return false;
            }

            private sealed class ConnectedPortSnapper : MouseManipulator
            {
                private const float DistancePixels = 4;
                private readonly Canvas canvas;
                private readonly Dictionary<GraphNodeView, Vector2> starts = new Dictionary<GraphNodeView, Vector2>();
                private GraphNodeView anchor;
                private Vector2 startMouse;

                internal ConnectedPortSnapper(Canvas canvas) { this.canvas = canvas; }
                protected override void RegisterCallbacksOnTarget()
                {
                    target.RegisterCallback<MouseDownEvent>(Begin, TrickleDown.TrickleDown);
                    target.RegisterCallback<MouseMoveEvent>(Move);
                    target.RegisterCallback<MouseUpEvent>(End);
                    target.RegisterCallback<MouseCaptureOutEvent>(Cancel);
                }
                protected override void UnregisterCallbacksFromTarget()
                {
                    target.UnregisterCallback<MouseDownEvent>(Begin, TrickleDown.TrickleDown);
                    target.UnregisterCallback<MouseMoveEvent>(Move);
                    target.UnregisterCallback<MouseUpEvent>(End);
                    target.UnregisterCallback<MouseCaptureOutEvent>(Cancel);
                    Clear();
                }
                private void Clear() { anchor = null; starts.Clear(); }
                private void End(MouseUpEvent e) { if (e.button == 0) Clear(); }
                private void Cancel(MouseCaptureOutEvent e) => Clear();

                private void Begin(MouseDownEvent e)
                {
                    Clear();
                    if (e.button != 0 || e.clickCount != 1 || !canvas.window.Editable || IsFieldControl(e.target as VisualElement)) return;
                    var element = e.target as VisualElement;
                    if (element is Port || element?.GetFirstAncestorOfType<Port>() != null) return;
                    anchor = element as GraphNodeView ?? element?.GetFirstAncestorOfType<GraphNodeView>();
                    if (anchor is not INodeView || (anchor.capabilities & Capabilities.Movable) == 0) { anchor = null; return; }
                    if (canvas.selection.Contains(anchor) || e.actionKey || e.shiftKey)
                        foreach (var selected in canvas.selection)
                            if (selected is GraphNodeView node && node is INodeView && (node.capabilities & Capabilities.Movable) != 0)
                                starts[node] = node.GetPosition().position;
                    starts[anchor] = anchor.GetPosition().position;
                    startMouse = canvas.contentViewContainer.WorldToLocal(e.mousePosition);
                }

                private void Move(MouseMoveEvent e)
                {
                    if (anchor == null || anchor.panel == null || !canvas.HasMouseCapture() || (e.pressedButtons & 1) == 0 ||
                        e.shiftKey || !EditorPrefs.GetBool("GraphSnapping", true)) return;
                    float scale = Mathf.Abs(canvas.contentViewContainer.worldTransform.m11);
                    if (scale < .0001f) return;
                    var proposed = CurrentRect(anchor);
                    Vector2 raw = starts[anchor] + canvas.contentViewContainer.WorldToLocal(e.mousePosition) - startMouse;
                    // Native rectangle snapping runs first. Only refine a free vertical axis.
                    if (Mathf.Abs(proposed.y - raw.y) * scale > .5f || RectAligned(proposed, scale)) return;

                    float best = DistancePixels / scale, adjustment = 0;
                    foreach (var edge in canvas.window.Graph.edges)
                    {
                        if (!canvas.outputPorts.TryGetValue(edge.outputNode + ":" + edge.outputPort, out var output) ||
                            !canvas.inputPorts.TryGetValue(edge.inputNode + ":" + edge.inputPort, out var input)) continue;
                        bool fromMoves = starts.ContainsKey(output.node), toMoves = starts.ContainsKey(input.node);
                        if (fromMoves == toMoves) continue;
                        Port moving = fromMoves ? output : input, stationary = fromMoves ? input : output;
                        float delta = PortY(stationary) - PortY(moving);
                        if (Mathf.Abs(delta) > best) continue;
                        best = Mathf.Abs(delta); adjustment = delta;
                    }
                    if (adjustment == 0) return;
                    foreach (var item in starts)
                    {
                        if (item.Key.panel == null) continue;
                        var position = CurrentRect(item.Key); position.y += adjustment;
                        item.Key.SetPosition(position);
                    }
                }

                private bool RectAligned(Rect proposed, float scale)
                {
                    foreach (var node in canvas.nodeViews.Values)
                    {
                        if (starts.ContainsKey(node) || node.panel == null) continue;
                        var other = node.GetPosition();
                        for (int a = 0; a < 3; a++) for (int b = 0; b < 3; b++)
                            if (Mathf.Abs((proposed.y + proposed.height * a * .5f) - (other.y + other.height * b * .5f)) * scale < .25f) return true;
                    }
                    return false;
                }
                private float PortY(Port port)
                {
                    var connector = port.Q("connector") ?? port;
                    return canvas.contentViewContainer.WorldToLocal(connector.worldBound.center).y +
                        CurrentRect(port.node).y - port.node.GetPosition().y;
                }
                private static Rect CurrentRect(GraphNodeView node)
                {
                    var rect = node.GetPosition();
                    // SetPosition updates inline coordinates before UI Toolkit's next layout pass.
                    rect.position = new Vector2(node.style.left.value.value, node.style.top.value.value);
                    return rect;
                }
            }
        }
    }
}
