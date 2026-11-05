using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderNodeReplacementTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Type Nodes = typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphNodes", true);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, F).Invoke(owner, args);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, F).SetValue(owner, value);
    static object Get(object owner, string name) => owner.GetType().GetField(name, F).GetValue(owner);
    static ShaderGraphNode Node(ShaderGraph graph, ShaderGraphNodeKind kind)
    {
        var node = (ShaderGraphNode)Nodes.GetMethod("Create", F).Invoke(null, new object[] { kind, new Vector2(240, 120) });
        graph.nodes.Add(node); return node;
    }
    static void Edge(ShaderGraph graph, ShaderGraphNode from, ShaderGraphNode to, string input, string output = "Out") =>
        graph.edges.Add(new ShaderGraphEdge { outputNode = from.id, outputPort = output, inputNode = to.id, inputPort = input });
    static string Edges(ShaderGraph graph) => string.Join(";", graph.edges.Select(e => e.outputNode + ":" + e.outputPort + ">" + e.inputNode + ":" + e.inputPort));

    sealed class Fixture : IDisposable
    {
        internal readonly WhimTexDocument document = ScriptableObject.CreateInstance<WhimTexDocument>();
        internal readonly ShaderLayerBehaviour layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
        internal readonly ShaderGraphWindow window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
        readonly EditorWindow focus = EditorWindow.focusedWindow;
        internal ShaderGraph Graph => layer.draftGraph;
        internal Fixture()
        {
            document.hideFlags = window.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            Call((Layer)layer, "AssignNewId"); document.layers.Add(layer);
            Set(window, "document", document); Set(window, "layerId", ((Layer)layer).Id);
            Graph.nodes.Clear(); Graph.edges.Clear(); Graph.parameters.Clear();
        }
        internal bool Replace(ShaderGraphNode node, object choice, ShaderGraph graph = null) =>
            (bool)Call(window, "ReplaceNode", graph ?? Graph, node, choice);
        public void Dispose()
        {
            if (window != null) UnityEngine.Object.DestroyImmediate(window);
            Call((Layer)layer, "ReleaseTransientResources");
            Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document);
            if (focus != null) focus.Focus();
        }
    }

    public static string Model() => TestContext.Run("Node replacement preserves matching ports and one-step Undo", t =>
    {
        using (var f = new Fixture())
        {
            var g = f.Graph;
            var vector = Node(g, ShaderGraphNodeKind.Combine); vector.valueType = ShaderGraphValueType.Float3;
            var source = Node(g, ShaderGraphNodeKind.Add);
            source.FindInput("B").type = ShaderGraphValueType.Float3; source.FindInput("B").value = new Vector4(.2f, .4f, .6f, 0);
            var length = Node(g, ShaderGraphNodeKind.Length); var output = Node(g, ShaderGraphNodeKind.Output);
            Edge(g, vector, source, "A"); Edge(g, source, length, "In"); Edge(g, source, output, "RGBA"); Edge(g, length, output, "AlphaScale");
            g.groups.Add(new ShaderGraphGroup { elements = new List<string> { source.id } });
            string expected = Edges(g); string id = source.id;
            Undo.IncrementCurrentGroup();
            t.True(f.Replace(source, ShaderGraphNodeKind.Multiply), "Replace Add by Multiply");
            t.Equal(ShaderGraphNodeKind.Multiply, g.FindNode(id).kind, "New node kind");
            t.Equal(source.position, g.FindNode(id).position, "Node position retained");
            t.Equal(id, g.groups[0].elements[0], "Group membership retained");
            t.Equal(expected, Edges(g), "Dynamic vector input, fan-out and unrelated edges retained");
            t.Equal(source.FindInput("B").value, g.FindNode(id).FindInput("B").value, "Matching inline values retained");
            t.True(!ReferenceEquals(source.FindInput("B"), g.FindNode(id).FindInput("B")), "Inline input is independently owned");
            g.Validate();
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            var restored = ((ShaderLayerBehaviour)f.document.layers[0].Behaviour).draftGraph;
            t.Equal(ShaderGraphNodeKind.Add, restored.FindNode(id).kind, "One Undo restores the original node");
            t.Equal(expected, Edges(restored), "One Undo restores all edges");
            Undo.PerformRedo(); restored = ((ShaderLayerBehaviour)f.document.layers[0].Behaviour).draftGraph;
            t.Equal(ShaderGraphNodeKind.Multiply, restored.FindNode(id).kind, "One Redo restores replacement");
            t.Equal(expected, Edges(restored), "Redo retains edge order");
        }
        using (var f = new Fixture())
        {
            var g = f.Graph;
            var vector = Node(g, ShaderGraphNodeKind.Combine); var output = Node(g, ShaderGraphNodeKind.Output);
            Edge(g, vector, output, "RGBA");
            t.True(f.Replace(vector, ShaderGraphNodeKind.Constant), "Replace vector output by scalar output");
            t.Equal(0, g.edges.Count, "Same name but different output type does not transfer");
            t.True(!f.Replace(vector, ShaderGraphNodeKind.Add), "Stale selection cannot replace a new node with the same ID");
            t.True(!f.Replace(output, ShaderGraphNodeKind.Add), "Required Output is protected");
            t.True(!f.Replace(g.FindNode(vector.id), ShaderGraphNodeKind.Add, new ShaderGraph()), "Wrong graph is rejected");
            g.Validate();
        }
        using (var f = new Fixture())
        {
            var g = f.Graph;
            var number = Node(g, ShaderGraphNodeKind.Constant); var add = Node(g, ShaderGraphNodeKind.Add); var output = Node(g, ShaderGraphNodeKind.Output);
            Edge(g, number, add, "A"); Edge(g, add, output, "RGBA");
            t.True(f.Replace(add, ShaderGraphNodeKind.Sine), "Replace with different input names");
            t.Equal(1, g.edges.Count, "Unmatched inputs removed, matching scalar Out retained");
            t.Equal(output.id, g.edges[0].inputNode, "Outgoing edge retained"); g.Validate();
        }
        using (var f = new Fixture())
        {
            var g = f.Graph;
            var sample = Node(g, ShaderGraphNodeKind.SampleGradient); Node(g, ShaderGraphNodeKind.Output);
            var resource = new ShaderParameter { type = ShaderParameterType.Gradient, gradientValue = new WhimTexGradient() };
            sample.inputs.Add(new ShaderGraphInput { port = "Gradient", type = ShaderGraphValueType.Gradient, resource = resource });
            t.True(f.Replace(sample, ShaderGraphNodeKind.SampleGradient), "Resource port replacement");
            var copied = g.FindNode(sample.id).FindInput("Gradient").resource;
            t.True(copied != resource && copied.gradientValue.Equals(resource.gradientValue), "Matching gradient data copied without aliasing"); g.Validate();
            t.True(f.Replace(g.FindNode(sample.id), ShaderGraphNodeKind.CustomFunction), "Inline custom function can be a replacement");
            t.Equal(1, g.functions.Count, "Replacement registers its function"); g.Validate();
            t.True(f.Replace(g.FindNode(sample.id), ShaderGraphNodeKind.Constant), "Replace function by built-in node");
            t.Equal(0, g.functions.Count, "Unused function is pruned"); g.Validate();
        }
        using (var f = new Fixture())
        {
            var g = f.Graph;
            var number = Node(g, ShaderGraphNodeKind.Constant); var function = Node(g, ShaderGraphNodeKind.CustomFunction);
            g.functions.Add((ShaderGraphFunction)Get(function, "function")); Node(g, ShaderGraphNodeKind.Output);
            Edge(g, number, function, "In");
            t.True(f.Replace(function, ShaderGraphNodeKind.Sine), "Replace fixed Color In by dynamic In");
            t.Equal(0, g.edges.Count, "Scalar-converted Color port is not confused with a scalar dynamic port"); g.Validate();
        }
        using (var f = new Fixture())
        {
            var g = f.Graph;
            ShaderGraphSubGraph Definition()
            {
                var sub = new ShaderGraphSubGraph();
                sub.inputs.Add(new ShaderGraphInterfacePort { name = "Value", type = ShaderGraphValueType.Float });
                sub.outputs.Add(new ShaderGraphInterfacePort { name = "Result", type = ShaderGraphValueType.Float });
                var input = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphInput, parameterId = sub.inputs[0].id };
                var output = new ShaderGraphNode { kind = ShaderGraphNodeKind.SubGraphOutput };
                sub.nodes.Add(input); sub.nodes.Add(output);
                sub.edges.Add(new ShaderGraphEdge { outputNode = input.id, inputNode = output.id, inputPort = sub.outputs[0].id });
                g.subGraphs.Add(sub); return sub;
            }
            var first = Definition(); var second = Definition();
            var source = Node(g, ShaderGraphNodeKind.Constant); var subNode = Node(g, ShaderGraphNodeKind.SubGraph); subNode.subGraphId = first.id;
            var output = Node(g, ShaderGraphNodeKind.Output); Call(g, "BindFunctions");
            Edge(g, source, subNode, first.inputs[0].id); Edge(g, subNode, output, "RGBA", first.outputs[0].id);
            t.True(f.Replace(subNode, second), "Replace subgraph definition");
            t.Equal(second.inputs[0].id, g.edges[0].inputPort, "Input mapped by label across distinct pin IDs");
            t.Equal(second.outputs[0].id, g.edges[1].outputPort, "Output mapped by label across distinct pin IDs"); g.Validate();
            var math = new ShaderGraphNode { kind = ShaderGraphNodeKind.Add }; second.nodes.Add(math);
            Set(f.window, "editingSubGraphId", second.id);
            var view = (ShaderGraph)f.window.GetType().GetProperty("Graph", F).GetValue(f.window);
            t.True(f.Replace(math, ShaderGraphNodeKind.Multiply, view), "Replace inside subgraph");
            t.Equal(ShaderGraphNodeKind.Multiply, second.nodes.Find(n => n.id == math.id).kind, "Nested model updated");
            t.True(!f.Replace(second.nodes.Find(n => n.id == math.id), second, view), "Recursive subgraph replacement rejected");
            g.Validate();
        }
        using (var f = new Fixture())
        {
            var effect = ScriptableObject.CreateInstance<ShaderFX>(); effect.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                f.layer.fx.Add(effect);
                var program = Get(effect, "program"); var graph = new ShaderGraph();
                Set(program, "mode", ShaderProgramMode.Graph); Set(program, "draftGraph", graph);
                Set(f.window, "effect", effect); Set(f.window, "effectTarget", true); Set(f.window, "target", null);
                var number = Node(graph, ShaderGraphNodeKind.Constant); var output = Node(graph, ShaderGraphNodeKind.Output);
                Edge(graph, number, output, "RGBA");
                var parameter = new ShaderParameter { type = ShaderParameterType.Float, name = "_Number" }; graph.parameters.Add(parameter);
                t.True(f.Replace(number, parameter.id, graph), "Replace in FX-hosted graph by a parameter");
                t.Equal(1, graph.edges.Count, "Matching scalar parameter Out retains the edge");
                t.Equal(parameter.id, graph.FindNode(number.id).parameterId, "Selected parameter is assigned");
                t.True((bool)Call(effect, "CompileGraph"), "Replaced FX graph compiles successfully");

                var entryType = typeof(ShaderFX).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphFunctionCatalog+Entry", true);
                var entry = Activator.CreateInstance(entryType, true);
                var definition = new ShaderGraphFunction { inline = false, name = "Replacement Test", entryPoint = "ReplacementTest", returnType = "float", returnValueType = ShaderGraphValueType.Float, returnName = "Out" };
                Set(entry, "definition", definition);
                Set(entry, "source", "float ReplacementTest() { return 0.25; }");
                Set(entry, "path", System.IO.Path.Combine(Application.dataPath, "ReplacementTest.hlsl"));
                t.True(f.Replace(graph.FindNode(number.id), entry, graph), "Library function replaces parameter without writing an asset");
                t.Equal(1, graph.functions.Count, "Library import registers its definition exactly once");
                t.Equal(1, graph.edges.Count, "Library scalar Out retains the edge");
                t.True((bool)Call(effect, "CompileGraph"), "Library replacement compiles in FX host");
            }
            finally
            {
                f.layer.fx.Remove(effect); Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect);
            }
        }
    });

    public static string UI() => TestContext.Run("Node context menu and replacement picker share the create catalog", t =>
    {
        using var f = new Fixture();
        var add = Node(f.Graph, ShaderGraphNodeKind.Add); Node(f.Graph, ShaderGraphNodeKind.Output);
        f.window.ShowUtility(); f.window.CreateGUI();
        var canvas = (GraphView)Get(f.window, "canvas");
        var view = canvas.nodes.First(n => n.title == "Add");
        var menu = new DropdownMenu();
        using (var trigger = MouseDownEvent.GetPooled())
        using (var evt = ContextualMenuPopulateEvent.GetPooled(trigger, menu, view, canvas.panel.contextualMenuManager))
            canvas.BuildContextualMenu(evt);
        t.True(menu.MenuItems().OfType<DropdownMenuAction>().Any(a => a.name == "Replace Node"), "Context menu contains Replace Node");
        var searchType = canvas.GetType().GetNestedType("NodeSearch", F);
        var search = ScriptableObject.CreateInstance(searchType);
        try
        {
            Set(search, "canvas", canvas); Set(search, "sourceGraph", f.Graph);
            var provider = (ISearchWindowProvider)search;
            var all = provider.CreateSearchTree(new SearchWindowContext(Vector2.zero));
            Set(search, "replacement", add);
            var tree = provider.CreateSearchTree(new SearchWindowContext(Vector2.zero));
            t.Equal("Replace Node", tree[0].content.text, "Full replacement root retained");
            t.True(tree.Any(e => e.level == 2 && e.userData is ShaderGraphNodeKind k && k == ShaderGraphNodeKind.Multiply), "Math siblings retain canonical depth");
            t.True(!tree.Any(e => e.content.text == "Other Categories"), "No synthetic categories");
            t.Equal(all.Count(e => e is not SearchTreeGroupEntry && e.userData is not (ShaderGraphNodeKind.ForBegin or ShaderGraphNodeKind.ForEnd)), tree.Count(e => e is not SearchTreeGroupEntry), "Search retains every replaceable creation choice once");
            for (int i = 1; i < tree.Count; i++) t.True(tree[i].level > 0 && tree[i].level <= tree[i - 1].level + (tree[i - 1] is SearchTreeGroupEntry ? 1 : 0), "Valid hierarchy at " + i);
            canvas.AddToSelection(view);
            var replacement = tree.First(e => e.userData is ShaderGraphNodeKind k && k == ShaderGraphNodeKind.Multiply);
            t.True(provider.OnSelectEntry(replacement, new SearchWindowContext(Vector2.zero)), "Picker performs replacement");
            Call(f.window, "RefreshInterface");
            t.Equal(1, canvas.nodes.Count(n => n.title == "Multiply"), "View shows new node title");
            t.True(canvas.selection.OfType<UnityEditor.Experimental.GraphView.Node>().Any(n => n.title == "Multiply"), "Selection survives replacement");
            var hue = Node(f.Graph, ShaderGraphNodeKind.Hue); Set(search, "replacement", hue);
            tree = provider.CreateSearchTree(new SearchWindowContext(Vector2.zero));
            t.Equal("Replace Node", tree[0].content.text, "Nested replacement retains full tree");
            t.True(tree.Any(e => e.level > 1 && e.userData is ShaderGraphNodeKind k && k == ShaderGraphNodeKind.Saturation), "Nested siblings retain their category");
            EditorWindow popup = null;
            try
            {
                popup = (EditorWindow)Call(search, "Open", new Vector2(500, 300));
                t.True(popup != null, "Replacement opens UITK popup");
                var group = popup.GetType().GetProperty("CurrentGroup", F).GetValue(popup);
                t.Equal("Adjustments", (string)Get(group, "text"), "Nested category opens directly without rebasing");
                t.Equal(1, popup.rootVisualElement.Query<VisualElement>(className: "whimtex-search-highlighted").ToList().Count, "Current kind is highlighted");
                t.Equal(0, popup.rootVisualElement.Query<IMGUIContainer>().ToList().Count, "Picker is entirely UITK");
                var item = Choice(Get(popup,"root"), data => data is ShaderGraphNodeKind kind && kind == ShaderGraphNodeKind.Saturation);
                t.True((bool)((Delegate)Get(popup,"select")).DynamicInvoke(item), "UITK selection invokes replacement callback");
                t.Equal(ShaderGraphNodeKind.Saturation,f.Graph.FindNode(hue.id).kind,"Selected replacement reaches graph");
            }
            finally { if (popup != null) popup.Close(); }
        }
        finally { UnityEngine.Object.DestroyImmediate(search); }
    });

    static object Choice(object parent, Func<object,bool> match)
    {
        if (Get(parent,"data") is SearchTreeEntry entry && match(entry.userData)) return parent;
        foreach (object item in (System.Collections.IEnumerable)Get(parent,"children"))
        { var found = Choice(item,match); if (found != null) return found; }
        return null;
    }
    public static string CreateUI() => Creation(false);
    public static string ConnectedUI() => Creation(true);
    static string Creation(bool connected) => TestContext.Run("UITK picker invokes node creation callbacks", t =>
    {
        using var f = new Fixture();
        var source = Node(f.Graph,ShaderGraphNodeKind.Constant); Node(f.Graph,ShaderGraphNodeKind.Output);
        f.window.ShowUtility(); f.window.CreateGUI();
        var canvas = (GraphView)Get(f.window,"canvas");
        var popupType = typeof(ShaderGraphWindow).Assembly.GetType("DCFApixels.WhimTex.WhimTexSearchDropdown",true);
        EditorWindow popup = null;
        try
        {
            if (connected)
            {
                var port = canvas.ports.First(p=>p.direction==Direction.Output && (string)Get(p.userData,"nodeId")==source.id);
                Call(canvas,"ShowConnectedSearch",port.userData,new Vector2(450,200));
            }
            else Call(canvas,"ShowSearch",new Vector2(600,400));
            popup = (EditorWindow)popupType.GetField("current",F).GetValue(null);
            t.True(popup != null,"Canvas opens shared UITK popup");
            var item = Choice(Get(popup,"root"), data => connected
                ? data != null && data.GetType().Name == "ConnectedNodeChoice" && (ShaderGraphNodeKind)Get(data,"kind")==ShaderGraphNodeKind.Add && (string)Get(data,"port")=="A"
                : data is ShaderGraphNodeKind kind && kind==ShaderGraphNodeKind.Add);
            t.True(item != null,"Creation choice is in shared tree");
            t.True((bool)((Delegate)Get(popup,"select")).DynamicInvoke(item),"Adapter accepts selected entry");
            var node = f.Graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.Add);
            t.Equal(3,f.Graph.nodes.Count,"One new node created");
            t.Equal(connected?1:0,f.Graph.edges.Count,"Expected automatic connection count");
            if (connected) t.True(f.Graph.edges.Single().outputNode==source.id && f.Graph.edges.Single().inputNode==node.id && f.Graph.edges.Single().inputPort=="A","Correct source and target port");
            Undo.PerformUndo(); t.Equal(2,f.Graph.nodes.Count,"One Undo restores creation");
            t.Equal(0,f.Graph.edges.Count,"Creation Undo restores connections");
            Undo.PerformRedo(); t.Equal(3,f.Graph.nodes.Count,"Redo restores new node");
        }
        finally { if (popup != null) popup.Close(); }
    });
}
