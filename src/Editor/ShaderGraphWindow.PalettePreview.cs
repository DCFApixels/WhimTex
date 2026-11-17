using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private PaletteDragPreview paletteDragPreview;

        private sealed class PaletteDragPreview : IDisposable
        {
            readonly ShaderGraphWindow window;
            readonly VisualElement root;
            NodePaletteDragData data;
            VisualElement overlay;
            Node view;
            Vector2 pointer, anchor;

            internal PaletteDragPreview(ShaderGraphWindow window)
            {
                this.window = window; root = window.rootVisualElement;
                root.RegisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                root.RegisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                root.RegisterCallback<DragExitedEvent>(Exited, TrickleDown.TrickleDown);
                root.RegisterCallback<DragLeaveEvent>(Leave, TrickleDown.TrickleDown);
                root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }

            internal void Begin(NodePaletteDragData drag, Vector2 position)
            {
                Clear(); data = drag;
                var node = CreateModel(drag.choice);
                if (node == null) { Clear(); return; }
                // Resolve only the candidate's ports. It never enters the document,
                // registered graph ports, preview queue or Undo until the drop.
                var graph = new ShaderGraph { parameters = drag.graph.parameters };
                graph.nodes.Add(node);
                var types = new ShaderGraphTypeCache(); types.Prepare(graph);
                view = node.kind switch
                {
                    ShaderGraphNodeKind.Parameter => new ParameterView(window, window.canvas, node, types),
                    ShaderGraphNodeKind.Reroute => new RerouteView(window, window.canvas, node, types),
                    _ => new NodeView(window, window.canvas, node, types)
                };
                overlay = new VisualElement { name = "node-palette-drag-preview", usageHints = UsageHints.DynamicTransform };
                overlay.AddToClassList("whimtex-shader-graph-drag-preview");
                overlay.Add(view);
                view.capabilities = 0;
                IgnoreInput(overlay);
                view.RegisterCallback<GeometryChangedEvent>(Geometry);
                root.Add(overlay);
                IgnoreInput(overlay);
                Move(position);
            }

            ShaderGraphNode CreateModel(object choice)
            {
                switch (choice)
                {
                    case ShaderGraphNodeKind kind: return ShaderGraphNodes.Create(kind, Vector2.zero);
                    case string parameter:
                        var token = ShaderGraphNodes.Create(ShaderGraphNodeKind.Parameter, Vector2.zero);
                        token.parameterId = parameter; return token;
                    case ShaderGraphSubGraph definition:
                        return new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraph, subGraphId = definition.id, subGraph = definition };
                    case ShaderGraphFunctionCatalog.Entry function:
                        var node = ShaderGraphNodes.Create(ShaderGraphNodeKind.CustomFunction, Vector2.zero);
                        ShaderGraphNodes.SetFunction(node, CopyFunctionData(function.definition)); return node;
                    default: return null;
                }
            }

            static void IgnoreInput(VisualElement element)
            {
                element.pickingMode = PickingMode.Ignore; element.focusable = false; element.tooltip = string.Empty;
                for (int i = 0; i < element.hierarchy.childCount; i++) IgnoreInput(element.hierarchy[i]);
            }

            void Geometry(GeometryChangedEvent e)
            {
                if (view == null) return;
                IgnoreInput(overlay);
                // Grip the actual title center; no second copy of node/header metrics.
                var header = view.titleContainer;
                anchor = header.parent != null && header.layout.height > 0
                    ? header.ChangeCoordinatesTo(view, header.contentRect.center)
                    : view.contentRect.center;
                Move(pointer);
            }

            internal Vector2 DropPosition(Vector2 position)
            {
                var scale = window.canvas.contentViewContainer.resolvedStyle.scale.value;
                return window.canvas.contentViewContainer.WorldToLocal(position - Vector2.Scale(anchor, new Vector2(scale.x, scale.y)));
            }

            void Move(Vector2 position)
            {
                pointer = position;
                if (overlay == null) return;
                var scale = window.canvas.contentViewContainer.resolvedStyle.scale.value;
                overlay.style.scale = new Scale(new Vector3(scale.x, scale.y, 1));
                var local = root.WorldToLocal(position) - Vector2.Scale(anchor, new Vector2(scale.x, scale.y));
                overlay.style.translate = new Translate(local.x, local.y);
                overlay.style.display = root.worldBound.Contains(position) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            internal void Validate()
            {
                if (data != null && (window.canvas == null || !window.Editable ||
                    !ReferenceEquals(window.Graph, data.graph) || window.svmNodesOnly != data.svmOnly ||
                    !ReferenceEquals(DragAndDrop.GetGenericData(NodePaletteDragKey), data))) Clear();
            }

            void Updated(DragUpdatedEvent e)
            {
                Validate();
                if (data == null) return;
                if (!window.ReadNodePaletteDrag(out _)) { Clear(); return; }
                Move(e.mousePosition);
                DragAndDrop.visualMode = OverCanvas(e.mousePosition) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            }

            bool OverCanvas(Vector2 position)
            {
                for (var picked = root.panel?.Pick(position); picked != null; picked = picked.parent)
                    if (picked == window.canvas) return true;
                return false;
            }

            void Perform(DragPerformEvent e)
            {
                if (data == null || OverCanvas(e.mousePosition)) return;
                Clear(); e.StopPropagation();
            }
            void Exited(DragExitedEvent e) => Clear();
            void Leave(DragLeaveEvent e)
            {
                if (overlay != null && !root.worldBound.Contains(e.mousePosition)) overlay.style.display = DisplayStyle.None;
            }
            void Key(KeyDownEvent e) { if (e.keyCode == KeyCode.Escape) Clear(); }

            internal void Clear()
            {
                if (data != null && ReferenceEquals(DragAndDrop.GetGenericData(NodePaletteDragKey), data))
                    DragAndDrop.SetGenericData(NodePaletteDragKey, null);
                overlay?.RemoveFromHierarchy(); overlay = null; view = null; data = null; anchor = default;
            }

            public void Dispose()
            {
                Clear();
                root.UnregisterCallback<DragUpdatedEvent>(Updated, TrickleDown.TrickleDown);
                root.UnregisterCallback<DragPerformEvent>(Perform, TrickleDown.TrickleDown);
                root.UnregisterCallback<DragExitedEvent>(Exited, TrickleDown.TrickleDown);
                root.UnregisterCallback<DragLeaveEvent>(Leave, TrickleDown.TrickleDown);
                root.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            }
        }
    }
}
