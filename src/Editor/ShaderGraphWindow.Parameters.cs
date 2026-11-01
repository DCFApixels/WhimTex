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
        private static readonly ShaderFXParameterType[] ParameterTypes = {
            ShaderFXParameterType.Float, ShaderFXParameterType.Vector2, ShaderFXParameterType.Vector3,
            ShaderFXParameterType.Vector, ShaderFXParameterType.Color, ShaderFXParameterType.Toggle,
            ShaderFXParameterType.Texture2D, ShaderFXParameterType.Gradient, ShaderFXParameterType.Curve
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
        private static string ParameterTypeName(ShaderFXParameterType type) => type == ShaderFXParameterType.Vector ? "Vector4" : type.ToString();
        private void AddParameter(ShaderFXParameterType type)
        {
            if (!Editable || Graph == null) return;
            if (Graph.parameters.Count >= 256) { ShowError("A Shader graph supports up to 256 parameters."); return; }
            string id = null;
            Edit("Add Shader Parameter", graph =>
            {
                string name = ParameterTypeName(type); int suffix = 1;
                while (graph.parameters.Exists(p => p.name == name)) name = ParameterTypeName(type) + " " + ++suffix;
                var p = new ShaderFXParameter { name = name, type = type };
                if (type == ShaderFXParameterType.Toggle) p.valueType = "bool";
                if (type == ShaderFXParameterType.Gradient) p.gradientValue = new WhimTexGradient();
                if (type == ShaderFXParameterType.Curve) p.curveValue = WhimTexCurveTexture.Default();
                graph.parameters.Add(p); id = p.id;
            });
            if (id != null) expandedParameters.Add(id);
        }
        private void RebuildParameters()
        {
            SetHoveredParameter(null);
            declarations.Clear();
            declarationRefresh.Clear();
            RebuildSubGraphInterface();
            parameterInsertion = null;
            if (Graph == null) return;
            foreach (var parameter in Graph.parameters)
            {
                if (parameter == null) continue;
                string id = parameter.id;
                var foldout = new Foldout { text = parameter.name + " · " + ParameterTypeName(parameter.type), value = expandedParameters.Contains(id), userData = id };
                foldout.AddToClassList("whimtex-shader-graph-parameter");
                foldout.RegisterCallback<PointerEnterEvent>(_ => SetHoveredParameter(id));
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
                var typeField = new DropdownField("Type", choices, Array.IndexOf(ParameterTypes, parameter.type));
                typeField.RegisterValueChangedCallback(e =>
                {
                    int selected = choices.IndexOf(e.newValue);
                    if (selected < 0) return;
                    Edit("Change Shader Parameter Type", graph =>
                    {
                        var p = graph.FindParameter(id); p.type = ParameterTypes[selected];
                        p.valueType = p.type == ShaderFXParameterType.Toggle ? "bool" : null;
                        p.hasMinimum = p.hasMaximum = false;
                        p.softMinimum = p.softMaximum = p.cyclicRange = false;
                        if (p.type == ShaderFXParameterType.Gradient) p.gradientValue ??= new WhimTexGradient();
                        if (p.type == ShaderFXParameterType.Curve) p.curveValue ??= WhimTexCurveTexture.Default();
                        if (p.textureSource == ShaderFXTextureSource.Self) p.textureSource = ShaderFXTextureSource.Texture;
                    });
                });
                foldout.Add(typeField);
                if (parameter.type == ShaderFXParameterType.Float || parameter.type == ShaderFXParameterType.Vector2)
                {
                    var range = new Toggle("Range") { value = parameter.hasMinimum && parameter.hasMaximum };
                    range.RegisterValueChangedCallback(e => Edit("Change Shader Parameter Range", graph =>
                    {
                        var p = graph.FindParameter(id); p.hasMinimum = p.hasMaximum = e.newValue;
                        if (e.newValue && p.maximum <= p.minimum) p.maximum = p.minimum + 1;
                    }, structural: false));
                    foldout.Add(range);
                    if (range.value)
                    {
                        var limits = new Vector2Field("Limits") { value = new Vector2(parameter.minimum, parameter.maximum) };
                        limits.RegisterValueChangedCallback(e =>
                        {
                            if (!ProjectiveMatrix.Finite(e.newValue.x) || !ProjectiveMatrix.Finite(e.newValue.y) || e.newValue.x >= e.newValue.y)
                            { limits.SetValueWithoutNotify(e.previousValue); return; }
                            Edit("Change Shader Parameter Limits", graph =>
                            { var p = graph.FindParameter(id); p.minimum = e.newValue.x; p.maximum = e.newValue.y; }, structural: false, rebuild: false);
                        });
                        declarationRefresh.Add(() =>
                        {
                            var p = Graph?.FindParameter(id);
                            if (p != null) limits.SetValueWithoutNotify(new Vector2(p.minimum, p.maximum));
                        });
                        foldout.Add(limits);
                    }
                }
                declarations.Add(foldout);
            }
            if (Graph.parameters.Count == 0) declarations.Add(new Label("Use + to add a parameter."));
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
                expandedParameters.Add(copy.id);
            });
        }

        private void RemoveParameter(string id)
        {
            if (Graph?.FindParameter(id) == null) return;
            Edit("Remove Shader Parameter", graph =>
            {
                var removed = new HashSet<string>();
                foreach (var node in RootGraph.AllNodes()) if (node.kind == ShaderGraphNodeKind.Parameter && node.parameterId == id) removed.Add(node.id);
                void Remove(ShaderGraph scope)
                {
                    scope.edges.RemoveAll(e => removed.Contains(e.inputNode) || removed.Contains(e.outputNode));
                    scope.nodes.RemoveAll(n => removed.Contains(n.id));
                    scope.PruneGroups();
                }
                Remove(RootGraph);
                foreach (var definition in RootGraph.subGraphs) Remove(RootGraph.SubGraphView(definition));
                graph.parameters.RemoveAll(p => p.id == id);
                expandedParameters.Remove(id);
            });
        }

        private void MoveParameter(string id, int insertion)
        {
            if (Graph?.FindParameter(id) is not ShaderFXParameter parameter) return;
            int from = Graph.parameters.IndexOf(parameter);
            int to = Mathf.Clamp(insertion, 0, Graph.parameters.Count);
            if (to > from) --to;
            if (from == to) return;
            Edit("Reorder Shader Parameters", graph =>
            {
                graph.parameters.RemoveAt(from);
                graph.parameters.Insert(to, parameter);
            });
        }

        private bool ReadParameterDrag(out ParameterDragData data)
        {
            data = DragAndDrop.GetGenericData(ParameterDragKey) as ParameterDragData;
            return data != null && data.window == this && Editable && Graph?.FindParameter(data.id) != null;
        }

        private int ParameterInsertionIndex(Vector2 position, out float y)
        {
            int index = 0;
            y = declarations.worldBound.yMin;
            foreach (var row in declarations.Children())
            {
                if (row is not Foldout) continue;
                var bounds = row.worldBound;
                if (position.y < bounds.center.y) { y = bounds.yMin; return index; }
                y = bounds.yMax; ++index;
            }
            return index;
        }

        private void HideParameterInsertion() => parameterInsertion?.AddToClassList("whimtex-hidden");

        private void OnParameterDragUpdated(DragUpdatedEvent e)
        {
            if (!ReadParameterDrag(out _)) { HideParameterInsertion(); return; }
            ParameterInsertionIndex(e.mousePosition, out float y);
            if (parameterInsertion == null)
            {
                parameterInsertion = new VisualElement { pickingMode = PickingMode.Ignore };
                parameterInsertion.AddToClassList("whimtex-shader-graph-parameter-insertion");
                declarations.Add(parameterInsertion);
            }
            parameterInsertion.RemoveFromClassList("whimtex-hidden");
            parameterInsertion.style.top = declarations.WorldToLocal(new Vector2(0, y)).y;
            DragAndDrop.visualMode = DragAndDropVisualMode.Move;
            e.StopPropagation();
        }

        private void OnParameterDragPerform(DragPerformEvent e)
        {
            if (!ReadParameterDrag(out var data)) return;
            int insertion = ParameterInsertionIndex(e.mousePosition, out _);
            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(ParameterDragKey, null);
            HideParameterInsertion();
            MoveParameter(data.id, insertion);
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
                DragAndDrop.StartDrag(window.Graph.FindParameter(id)?.name ?? "Shader Parameter");
                e.StopImmediatePropagation();
            }
            private void Up(PointerUpEvent e)
            {
                if (!armed || e.pointerId != pointerId || e.button != 0) return;
                armed = false;
                target.ReleasePointer(pointerId);
                if (target.worldBound.Contains(e.position)) foldout.value = !foldout.value;
                e.StopImmediatePropagation();
            }
            private void Cancel(PointerCaptureOutEvent e) => armed = false;
        }
    }
}
