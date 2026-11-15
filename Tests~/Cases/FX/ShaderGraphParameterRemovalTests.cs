using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;

public static class ShaderGraphParameterRemovalTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object owner, string name, params object[] args) => (owner as Type ?? owner.GetType()).GetMethod(name, F).Invoke(owner is Type ? null : owner, args);
    static object Get(object owner, string name) => owner.GetType().GetField(name, F)?.GetValue(owner) ?? owner.GetType().GetProperty(name, F).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, F).SetValue(owner, value);
    static void Remove(ShaderGraph graph, string id) => Call(T("ShaderGraphParameters"), "Remove", graph, id);
    static Vector4 Numeric(ShaderParameter p) => (Vector4)Call(T("ShaderGraphPreviewVM"), "ParameterValue", p);
    static ShaderGraphNode ParameterNode(ShaderParameter p) => new ShaderGraphNode
    { kind = ShaderGraphNodeKind.Parameter, parameterId = p.id, position = new Vector2(91, 127), previewExpanded = false, previewHeight = true };
    static string Wire(ShaderGraphEdge e) => e.outputNode + ":" + e.outputPort + "->" + e.inputNode + ":" + e.inputPort;

    public static async Task<object> Run()
    {
        var test = new WhimTex.Tests.TestContext();
        WhimTexDocument document = null; ShaderGraphWindow window = null; ShaderFX effect = null;
        var focus = EditorWindow.focusedWindow;
        const string pref = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
        bool hadPref = EditorPrefs.HasKey(pref); string oldPref = EditorPrefs.GetString(pref);
        try
        {
            var cases = new List<ShaderParameter>();
            foreach (ShaderParameterType kind in Enum.GetValues(typeof(ShaderParameterType)))
            {
                var p = new ShaderParameter { name = "Source" };
                Call(T("ShaderGraphParameters"), "SetKind", p, kind);
                p.floatValue = 1.75f; p.vectorValue = new Vector4(-3, 4, 2, .75f);
                p.colorValue = new Color(.3f, 1.5f, -.2f, .6f); p.pixelUnits = true;
                if (kind == ShaderParameterType.Seed) p.floatValue = 321;
                if (kind == ShaderParameterType.Toggle) p.integerValues[0] = 1;
                cases.Add(p);
            }
            cases.Add(new ShaderParameter { name = "Integer", type = ShaderParameterType.Float, valueType = "int", integerValues = new long[] { -123, 0, 0, 0 }, pixelUnits = true });
            cases.Add(new ShaderParameter { name = "IntegerVector", type = ShaderParameterType.Vector3, valueType = "int3", integerValues = new long[] { -9, 4, 300, 0 }, pixelUnits = true });
            cases.Add(new ShaderParameter { name = "IntegerColor", type = ShaderParameterType.Color, valueType = "uint4", integerValues = new long[] { 1, 2, 3, 1 } });
            foreach (var p in cases)
            {
                var g = ShaderGraph.CreateDefault(); g.parameters.Add(p);
                var node = ParameterNode(p); var loose = ParameterNode(p); g.nodes.Add(node); g.nodes.Add(loose);
                g.groups.Add(new ShaderGraphGroup { elements = new List<string> { node.id, loose.id } });
                var reroute = new ShaderGraphNode { kind = ShaderGraphNodeKind.Reroute }; g.nodes.Add(reroute);
                g.edges.Add(new ShaderGraphEdge { outputNode = node.id, inputNode = reroute.id, inputPort = "In" });
                var sub = new ShaderGraphSubGraph { name = "Nested" }; g.subGraphs.Add(sub);
                sub.outputs.Add(new ShaderGraphInterfacePort { name = "Out" });
                var nested = ParameterNode(p); sub.nodes.Add(nested);
                sub.nodes.Add(new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphOutput });
                var subReroute = new ShaderGraphNode { kind = ShaderGraphNodeKind.Reroute }; sub.nodes.Add(subReroute);
                sub.edges.Add(new ShaderGraphEdge { outputNode = nested.id, inputNode = subReroute.id, inputPort = "In" });
                var group = (ShaderGraphParameterItem)Call(T("ShaderGraphParameterLayout"), "AddContainer", g, ShaderContainerKind.Group, "Group", null);
                Call(T("ShaderGraphParameterLayout"), "Move", g, p.id, group.id, int.MaxValue);
                if ((bool)Call(T("ShaderGraphParameterLayout"), "CanUseHeader", p, ShaderContainerKind.Group)) group.headerParameterId = p.id;
                var edges = g.edges.Concat(sub.edges).ToArray(); string wires = string.Join("|", edges.Select(Wire));
                int nodeCount = g.nodes.Count;
                string source = JsonUtility.ToJson(p);
                object outputType = Call(T("ShaderGraphNodes"), "ParameterType", p);
                bool numeric = (int)Get(p, "Components") > 0;
                Vector4 expected = numeric ? Numeric(p) : default;
                Remove(g, p.id);
                test.True(g.FindParameter(p.id) == null && g.nodes.Count == nodeCount, "Declaration removed, all nodes retained: " + p.type);
                test.Equal(wires, string.Join("|", g.edges.Concat(sub.edges).Select(Wire)), "All wires retained");
                test.True(edges.SequenceEqual(g.edges.Concat(sub.edges)), "Edge objects unchanged");
                test.True(g.groups[0].elements.SequenceEqual(new[] { node.id, loose.id }), "Canvas group membership retained");
                test.True(group.headerParameterId == null && group.children.All(i => i.parameterId != p.id), "Layout and header reference cleared");
                foreach (var replacement in new[] { node, loose, nested })
                {
                    test.True(replacement.kind == ShaderGraphNodeKind.Value && replacement.parameterId == null, "Root, loose and Sub Graph nodes converted");
                    test.True(replacement.position == new Vector2(91, 127) && !replacement.previewExpanded && replacement.previewHeight, "Node placement and preview state retained");
                    test.Equal(outputType, Call(T("ShaderGraphNodes"), "ValueOutputType", replacement), "Output type retained");
                    test.True(replacement.literal.id != p.id && replacement.literal.controls.Count == 0, "Independent value without declaration UI");
                    test.Equal(Get(p, "UsesPixelScale"), Get(replacement.literal, "UsesPixelScale"), "Pixel unit behavior retained");
                    if (numeric)
                    {
                        Vector4 actual = Numeric(replacement.literal);
                        for (int c = 0; c < 4; c++) test.Near(expected[c], actual[c], 2e-6, "Effective numeric component " + c);
                    }
                }
                test.True(!ReferenceEquals(node.literal, loose.literal) && node.literal.id != loose.literal.id, "Each Value owns its data");
                if (p.gradientValue != null) test.True(!ReferenceEquals(node.literal.gradientValue, p.gradientValue) && !ReferenceEquals(node.literal.gradientValue, loose.literal.gradientValue), "Independent gradients");
                if (p.curveValue != null) test.True(!ReferenceEquals(node.literal.curveValue, p.curveValue) && !ReferenceEquals(node.literal.curveValue, loose.literal.curveValue), "Independent curves");
                test.Equal(source, JsonUtility.ToJson(p), "Original value untouched");
                Call(T("ShaderGraphCompiler"), "Compile", g, numeric ? node.id : null, "Out", false);
                var snapshot = Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", g, null);
                var reopened = (ShaderGraph)Call(typeof(WhimTexDocumentJson), "ReadShaderGraph", snapshot);
                test.Equal(snapshot.ToString(), Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", reopened, null).ToString(), "Current JSON roundtrip");
            }

            var graph = ShaderGraph.CreateDefault(); string parameterId = graph.parameters[0].id, nodeId = graph.nodes[0].id;
            graph.nodes[0].previewExpanded = false;
            var unused = new ShaderParameter { name = "Unused" }; graph.parameters.Add(unused);
            var definition = new ShaderGraphSubGraph { name = "Other" }; graph.subGraphs.Add(definition);
            definition.outputs.Add(new ShaderGraphInterfacePort { name = "Out" });
            var inside = ParameterNode(graph.parameters[0]); definition.nodes.Add(inside);
            definition.nodes.Add(new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphOutput });
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour { draftGraph = graph }; document.layers.Add(shader); Call(document, "NormalizeModel");
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            window.ShowUtility(); window.position = new Rect(80, 80, 1000, 700); window.CreateGUI(); await Task.Delay(250);
            Call(window, "RemoveParameter", unused.id);
            test.True(((ShaderGraph)Get(window, "RootGraph")).FindParameter(unused.id) == null, "Unused parameter removed without a dialog");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            Call(window, "OpenSubGraph", definition.id); await Task.Delay(200);
            graph = (ShaderGraph)Get(window, "RootGraph"); string before = JsonUtility.ToJson(graph);
            Call(window, "RemoveParameterConfirmed", parameterId); Undo.FlushUndoRecordObjects(); await Task.Delay(250); Call(window, "RefreshInterface");
            graph = (ShaderGraph)Get(window, "RootGraph");
            test.True(graph.FindNode(nodeId).kind == ShaderGraphNodeKind.Value && graph.FindSubGraph(definition.id).nodes[0].kind == ShaderGraphNodeKind.Value, "Removal from Sub Graph converts every scope");
            var views = (IDictionary)Get(Get(window, "canvas"), "nodeViews");
            test.Equal("NodeView", views[inside.id].GetType().Name, "UI replaces ParameterView with Value editor");
            string after = JsonUtility.ToJson(graph);
            Undo.PerformUndo(); await Task.Delay(250); Call(window, "RefreshInterface");
            test.Equal(before, JsonUtility.ToJson(Get(window, "RootGraph")), "One Undo restores declaration, values, nodes and connections");
            Undo.PerformRedo(); await Task.Delay(250); Call(window, "RefreshInterface");
            test.Equal(after, JsonUtility.ToJson(Get(window, "RootGraph")), "Redo restores independent Value nodes");

            window.Close(); window = null;
            effect = ScriptableObject.CreateInstance<ShaderFX>(); effect.hideFlags = HideFlags.HideAndDontSave;
            var fxGraph = ShaderGraph.CreateDefault(); fxGraph.nodes[0].previewExpanded = false;
            Set(effect, "program", new ShaderProgram { mode = ShaderProgramMode.Graph, draftGraph = fxGraph });
            document.layers[0].fx.Add(effect);
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            Set(window, "effect", effect); Set(window, "effectTarget", true);
            window.ShowUtility(); window.position = new Rect(80, 80, 1000, 700); window.CreateGUI(); await Task.Delay(250);
            Undo.IncrementCurrentGroup(); before = JsonUtility.ToJson(Get(window, "RootGraph"));
            Call(window, "RemoveParameterConfirmed", fxGraph.parameters[0].id); Undo.FlushUndoRecordObjects(); await Task.Delay(250);
            test.True(((ShaderGraph)Get(window, "RootGraph")).parameters.Count == 0 && ((ShaderGraph)Get(window, "RootGraph")).nodes[0].kind == ShaderGraphNodeKind.Value, "FX uses the same conversion");
            Undo.PerformUndo(); await Task.Delay(250);
            test.Equal(before, JsonUtility.ToJson(Get(window, "RootGraph")), "FX Undo restores declaration and nodes");
            return WhimTex.Tests.TestContext.Result("passed", test.Checks, "Parameter removal, Value conversion, Sub Graph, UI and Undo");
        }
        catch (Exception error) { return WhimTex.Tests.TestContext.Result("failed", test.Checks, "Parameter removal", error.ToString()); }
        finally
        {
            if (window != null) window.Close();
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); }
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadPref) EditorPrefs.SetString(pref, oldPref); else EditorPrefs.DeleteKey(pref);
            if (focus != null) focus.Focus();
        }
    }
}
