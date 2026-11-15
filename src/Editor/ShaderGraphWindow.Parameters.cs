using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DCFApixels.WhimTex
{
    public sealed partial class ShaderGraphWindow
    {
        private static readonly ShaderParameterType[] ParameterTypes = {
            ShaderParameterType.Float, ShaderParameterType.Vector2, ShaderParameterType.Vector3,
            ShaderParameterType.Vector, ShaderParameterType.Color, ShaderParameterType.Toggle,
            ShaderParameterType.Texture2D, ShaderParameterType.Gradient, ShaderParameterType.Curve,
            ShaderParameterType.Enum, ShaderParameterType.Seed, ShaderParameterType.Point, ShaderParameterType.Normal, ShaderParameterType.Transform2D
        };
        private readonly HashSet<string> expandedParameters = new HashSet<string>();
        private VisualElement parameterInsertion;
        private string hoveredParameter;
        private void SetHoveredParameter(string id)
        {
            if (hoveredParameter == id) return;
            hoveredParameter = id;
            canvas?.HighlightParameter(id);
        }
        private static string ParameterTypeName(ShaderParameterType type) => type == ShaderParameterType.Vector ? "Vector4" : type.ToString();
        private void AddParameter(ShaderParameterType type)
        {
            if (!Editable || Graph == null) return;
            if (Graph.parameters.Count >= 256) { ShowError("A Shader graph supports up to 256 parameters."); return; }
            string id = null;
            Edit("Add Shader Parameter", graph =>
            {
                string name = ParameterTypeName(type); int suffix = 1;
                while (graph.parameters.Exists(p => p.name == name)) name = ParameterTypeName(type) + " " + ++suffix;
                var p = new ShaderParameter { name = name, type = type };
                ShaderGraphParameters.SetKind(p, type);
                if (type == ShaderParameterType.Gradient) p.gradientValue = new WhimTexGradient();
                if (type == ShaderParameterType.Curve) p.curveValue = WhimTexCurveTexture.Default();
                graph.parameters.Add(p); id = p.id;
                ShaderGraphParameterLayout.Normalize(graph);
                string parent = ParameterAddParent();
                if (parent != null) ShaderGraphParameterLayout.Move(graph, p.id, parent, int.MaxValue);
            });
            if (id != null) expandedParameters.Add(id);
        }
        private void RebuildParameters() => RebuildParameterTree();

        private Foldout BuildParameterBlock(ShaderParameter parameter)
        {
                string id = parameter.id;
                var foldout = new Foldout { text = parameter.name, value = expandedParameters.Contains(id), userData = id };
                foldout.AddToClassList("whimtex-shader-graph-parameter");
                foldout.RegisterCallback<PointerEnterEvent>(_ => SetHoveredParameter(id));
                foldout.RegisterCallback<PointerMoveEvent>(_ => SetHoveredParameter(id));
                foldout.RegisterCallback<PointerLeaveEvent>(_ => { if (hoveredParameter == id) SetHoveredParameter(null); });
                foldout.RegisterCallback<DetachFromPanelEvent>(_ => { if (hoveredParameter == id) SetHoveredParameter(null); });
                foldout.RegisterValueChangedCallback(e =>
                {
                    if (e.target != foldout) return;
                    if (e.newValue) expandedParameters.Add(id); else expandedParameters.Remove(id);
                });
                var header = foldout.Q<Toggle>(className: Foldout.toggleUssClassName);
                header.AddToClassList("whimtex-shader-graph-parameter-header");
                header.AddManipulator(new ParameterDrag(this, id, foldout));
                header.AddManipulator(new ContextualMenuManipulator(e =>
                {
                    bool available = Editable && Graph?.FindParameter(id) != null;
                    var status = available ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
                    e.menu.AppendAction("Use in Graph", _ => AddNode(ShaderGraphNodeKind.Parameter, canvas.CenterPosition, id),
                        available && Graph.nodes.Count < 512 ? status : DropdownMenuAction.Status.Disabled);
                    e.menu.AppendAction("Duplicate", _ => DuplicateParameter(id),
                        available && Graph.parameters.Count < 256 ? status : DropdownMenuAction.Status.Disabled);
                    e.menu.AppendSeparator();
                    e.menu.AppendAction("Remove", _ => RemoveParameter(id), status);
                    e.StopPropagation();
                }));
                var typeLabel = new Label(ParameterTypeName(ShaderGraphParameters.Kind(parameter))) { pickingMode = PickingMode.Ignore };
                typeLabel.AddToClassList("whimtex-shader-graph-parameter-kind"); header.Add(typeLabel);
                var indicator = NodeView.CreatePort(id, string.Empty, Direction.Output, ShaderGraphNodes.ParameterType(parameter), false);
                indicator.RemoveManipulator(indicator.edgeConnector);
                indicator.capabilities = 0; indicator.focusable = false; indicator.tooltip = string.Empty;
                indicator.AddToClassList("whimtex-shader-graph-parameter-port");
                indicator.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
                indicator.pickingMode = PickingMode.Ignore;
                header.Add(indicator);
                var name = new TextField("Name") { value = parameter.name, isDelayed = true };
                name.RegisterValueChangedCallback(e =>
                {
                    string value = e.newValue?.Trim();
                    if (string.IsNullOrEmpty(value)) { name.SetValueWithoutNotify(Graph.FindParameter(id)?.name); return; }
                    if (Graph.parameters.Exists(p => p.id != id && string.Equals(p.name, value, StringComparison.Ordinal)))
                    { ShowError("A parameter with this name already exists."); name.SetValueWithoutNotify(Graph.FindParameter(id)?.name); return; }
                    Edit("Rename Shader Parameter", graph => graph.FindParameter(id).name = value, structural: false);
                });
                foldout.Add(name);
                var choices = new List<string>();
                foreach (var type in ParameterTypes) choices.Add(ParameterTypeName(type));
                var typeField = new DropdownField("Type", choices, Array.IndexOf(ParameterTypes, ShaderGraphParameters.Kind(parameter)));
                typeField.RegisterValueChangedCallback(e =>
                {
                    int selected = choices.IndexOf(e.newValue);
                    if (selected < 0) return;
                    Edit("Change Shader Parameter Type", graph =>
                    {
                        var p = graph.FindParameter(id);
                        ShaderGraphParameters.SetKind(p, ParameterTypes[selected]);
                        foreach (var item in ShaderGraphParameterLayout.All(graph))
                            if (item.headerParameterId == id && !ShaderGraphParameterLayout.CanUseHeader(p, item.kind)) item.headerParameterId = null;
                        if (!Target.IsEffect && p.textureSource == ShaderTextureSource.Self) p.textureSource = ShaderTextureSource.Texture;
                    });
                });
                foldout.Add(typeField);
                BuildParameterOptions(foldout, parameter);
                return foldout;
        }

        private void DuplicateParameter(string id)
        {
            if (!Editable || Graph?.FindParameter(id) == null) return;
            if (Graph.parameters.Count >= 256) { ShowError("A Shader graph supports up to 256 parameters."); return; }
            Edit("Duplicate Shader Parameter", graph =>
            {
                var source = graph.FindParameter(id);
                var copy = source.Copy();
                copy.id = Guid.NewGuid().ToString("N");
                string name = source.name + " Copy"; int suffix = 1;
                copy.name = name;
                while (graph.parameters.Exists(p => p.name == copy.name)) copy.name = name + " " + ++suffix;
                graph.parameters.Insert(graph.parameters.IndexOf(source) + 1, copy);
                ShaderGraphParameterLayout.Normalize(graph);
                var entry = ShaderGraphParameterLayout.FindParameterItem(graph, id);
                var parent = ShaderGraphParameterLayout.FindParent(graph, entry.id);
                int index = (parent?.children ?? graph.parameterLayout).IndexOf(entry) + 1;
                ShaderGraphParameterLayout.Move(graph, copy.id, parent?.id, index);
                expandedParameters.Add(copy.id);
            });
        }

        private void RemoveParameter(string id)
        {
            if (!Editable || Graph?.FindParameter(id) is not ShaderParameter parameter) return;
            int uses = 0;
            foreach (var node in RootGraph.AllNodes())
                if (node.kind == ShaderGraphNodeKind.Parameter && node.parameterId == id) uses++;
            if (uses > 0 && !EditorUtility.DisplayDialog("Remove Shader Parameter",
                $"Remove \"{parameter.name}\"?\n\nIt is used by {uses} parameter node(s), including Sub Graphs. " +
                "These nodes will become independent Value nodes with their current values and connections preserved.",
                "Remove", "Cancel")) return;
            RemoveParameterConfirmed(id);
        }

        private void RemoveParameterConfirmed(string id)
        {
            Edit("Remove Shader Parameter", graph =>
            {
                ShaderGraphParameters.Remove(RootGraph, id);
                if (hoveredParameter == id) SetHoveredParameter(null);
                expandedParameters.Remove(id);
            });
        }

        private void MoveParameter(string id, int insertion)
        {
            MoveParameterLayout(id, null, insertion);
        }

        private bool ReadParameterDrag(out ParameterDragData data)
        {
            data = DragAndDrop.GetGenericData(ParameterDragKey) as ParameterDragData;
            return data != null && data.window == this && Editable && Graph?.FindParameter(data.id) != null;
        }

        private void HideParameterInsertion()
        {
            parameterInsertion?.AddToClassList("whimtex-hidden");
            foreach (var block in parameterBlocks.Values) block.foldout.RemoveFromClassList("whimtex-shader-graph-parameter-drop");
        }

        private void OnParameterDragUpdated(DragUpdatedEvent e)
        {
            if (!ReadParameterLayoutDrag(out var data) || !ParameterDestination(e.mousePosition, data.id, out var parent, out int index, out Rect marker))
            { HideParameterInsertion(); DragAndDrop.visualMode = DragAndDropVisualMode.Rejected; return; }
            HideParameterInsertion();
            if (parameterInsertion == null)
            {
                parameterInsertion = new VisualElement { pickingMode = PickingMode.Ignore };
                parameterInsertion.AddToClassList("whimtex-shader-graph-parameter-insertion");
                declarations.Add(parameterInsertion);
            }
            parameterInsertion.RemoveFromClassList("whimtex-hidden");
            parameterInsertion.style.top = declarations.WorldToLocal(marker.position).y;
            parameterInsertion.style.left = declarations.WorldToLocal(marker.position).x;
            parameterInsertion.style.right = StyleKeyword.Auto;
            parameterInsertion.style.width = marker.width;
            if (parent != null && parameterBlocks.TryGetValue(parent, out var block)) block.foldout.AddToClassList("whimtex-shader-graph-parameter-drop");
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            e.StopPropagation();
        }

        private void OnParameterDragPerform(DragPerformEvent e)
        {
            if (!ReadParameterLayoutDrag(out var data) || !ParameterDestination(e.mousePosition, data.id, out var parent, out int insertion, out _)) return;
            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(ParameterDragKey, null);
            HideParameterInsertion();
            MoveParameterLayout(data.id, parent, insertion);
            e.StopPropagation();
        }

        private const string ParameterDragKey = "WhimTex.ShaderGraph.Parameter";
        private sealed class ParameterDragData
        {
            internal ShaderGraphWindow window;
            internal string id;
        }
        private sealed class ParameterDrag : PointerManipulator
        {
            private readonly ShaderGraphWindow window;
            private readonly string id;
            private readonly Foldout foldout;
            private Vector2 start;
            private bool armed;
            private int pointerId;
            internal ParameterDrag(ShaderGraphWindow window, string id, Foldout foldout) { this.window = window; this.id = id; this.foldout = foldout; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerCaptureOutEvent>(Cancel);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
                target.UnregisterCallback<PointerCaptureOutEvent>(Cancel);
            }
            private void Down(PointerDownEvent e)
            {
                if (e.button != 0 || !window.Editable) return;
                start = e.position; armed = true; pointerId = e.pointerId;
                target.CapturePointer(pointerId);
                e.StopImmediatePropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (!armed || e.pointerId != pointerId || (e.pressedButtons & 1) == 0 || ((Vector2)e.position - start).sqrMagnitude < 25) return;
                armed = false;
                target.ReleasePointer(pointerId);
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ParameterDragKey, new ParameterDragData { window = window, id = id });
                DragAndDrop.StartDrag(window.Graph.FindParameter(id)?.name ?? ShaderGraphParameterLayout.Find(window.Graph, id)?.title ?? "Shader Parameter");
                e.StopImmediatePropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if (!armed || e.pointerId != pointerId || e.button != 0) return;
                armed = false;
                target.ReleasePointer(pointerId);
                if (target.worldBound.Contains(e.position)) { window.SelectParameterItem(id); foldout.value = !foldout.value; }
                e.StopImmediatePropagation();
            }
            private void Cancel(PointerCaptureOutEvent e) => armed = false;
        }
    }
}
