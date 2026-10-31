using System;
using System.Collections.Generic;
using UnityEditor;
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
            declarations.Clear();
            if (Graph == null) return;
            foreach (var parameter in Graph.parameters)
            {
                if (parameter == null) continue;
                string id = parameter.id;
                var foldout = new Foldout { text = parameter.name + " · " + ParameterTypeName(parameter.type), value = expandedParameters.Contains(id) };
                foldout.AddToClassList("whimtex-shader-graph-parameter");
                foldout.RegisterValueChangedCallback(e =>
                {
                    if (e.target != foldout) return;
                    if (e.newValue) expandedParameters.Add(id); else expandedParameters.Remove(id);
                });
                var title = foldout.Q<Label>();
                if (title != null) { title.AddManipulator(new ParameterDrag(this, id)); title.tooltip = "Drag onto the graph to use this parameter."; }
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
                        foldout.Add(limits);
                    }
                }
                var buttons = new VisualElement(); buttons.AddToClassList("whimtex-shader-graph-parameter-actions");
                buttons.Add(new Button(() => AddNode(ShaderGraphNodeKind.Parameter, canvas.CenterPosition, id)) { text = "Use in Graph" });
                buttons.Add(new Button(() => Edit("Remove Shader Parameter", graph =>
                {
                    var removed = new HashSet<string>();
                    foreach (var node in graph.nodes) if (node.kind == ShaderGraphNodeKind.Parameter && node.parameterId == id) removed.Add(node.id);
                    graph.edges.RemoveAll(e => removed.Contains(e.inputNode) || removed.Contains(e.outputNode));
                    graph.nodes.RemoveAll(n => removed.Contains(n.id));
                    graph.parameters.RemoveAll(p => p.id == id);
                    expandedParameters.Remove(id);
                })) { text = "Remove", tooltip = "Remove this parameter and its nodes. Undo restores them." });
                foldout.Add(buttons); declarations.Add(foldout);
            }
            if (Graph.parameters.Count == 0) declarations.Add(new Label("Use + to add a parameter."));
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
            private Vector2 start;
            private bool armed;
            internal ParameterDrag(ShaderGraphWindow window, string id) { this.window = window; this.id = id; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Cancel);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Cancel);
            }
            private void Down(PointerDownEvent e)
            {
                if (e.button != 0 || !window.Editable) return;
                start = e.position; armed = true;
            }
            private void Move(PointerMoveEvent e)
            {
                if (!armed || e.pressedButtons != 1 || ((Vector2)e.position - start).sqrMagnitude < 25) return;
                armed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ParameterDragKey, new ParameterDragData { window = window, id = id });
                DragAndDrop.StartDrag(window.Graph.FindParameter(id)?.name ?? "Shader Parameter");
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e) => armed = false;
            private void Cancel(PointerCaptureOutEvent e) => armed = false;
        }
    }
}
