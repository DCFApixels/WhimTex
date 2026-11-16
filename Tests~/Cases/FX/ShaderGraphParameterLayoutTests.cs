using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

public static class ShaderGraphParameterLayoutTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(object target, string name, params object[] args) => (target as Type ?? target.GetType()).GetMethod(name, F).Invoke(target is Type ? null : target, args);
    static object Get(object target, string name)
    {
        var field = target.GetType().GetField(name, F);
        return field != null ? field.GetValue(target) : target.GetType().GetProperty(name, F).GetValue(target);
    }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, F).SetValue(target, value);
    static object New(string name, params object[] args) => Activator.CreateInstance(T(name), F, null, args, null);
    static object Layout(string name, params object[] args) => Call(T("ShaderGraphParameterLayout"), name, args);
    static ShaderGraphParameterItem Add(ShaderGraph g, ShaderContainerKind kind, string title, string parent = null) => (ShaderGraphParameterItem)Layout("AddContainer", g, kind, title, parent);
    static void Move(ShaderGraph g, string id, string parent, int at = int.MaxValue) => Layout("Move", g, id, parent, at);
    static ShaderGraphParameterItem Parent(ShaderGraph g, string id) => (ShaderGraphParameterItem)Layout("FindParent", g, id);
    static ShaderParameter Parameter(ShaderGraph g, string name, ShaderParameterType type)
    {
        var p = new ShaderParameter { name = name }; Call(T("ShaderGraphParameters"), "SetKind", p, type); g.parameters.Add(p); return p;
    }
    static Task Frame() => Task.Delay(140);
    static void CheckHiddenTitles(WhimTex.Tests.TestContext test)
    {
        var graph = ShaderGraph.CreateDefault(); var p = graph.parameters[0];
        var group = Add(graph, ShaderContainerKind.Group, "Remember Group");
        var tabs = Add(graph, ShaderContainerKind.Tabs, "Remember Tabs", group.id);
        Move(graph, p.id, tabs.children[0].id);
        string before = (string)Get(Call(T("ShaderGraphCompiler"), "Compile", graph, null, "Out", false), "executableSource");
        ulong stamp = (ulong)Layout("Fingerprint", graph);
        Layout("SetTitleVisible", group, false); Layout("SetTitleVisible", tabs, false);
        Layout("Validate", graph);
        test.True(stamp != (ulong)Layout("Fingerprint", graph), "Title visibility invalidates layout");
        var compiled = Call(T("ShaderGraphCompiler"), "Compile", graph, null, "Out", false);
        test.Equal(before, (string)Get(compiled, "executableSource"), "Hiding titles leaves shader computations unchanged");
        var parsed = (List<ShaderParameter>)Call(T("ShaderMetadata"), "Parse", Get(compiled, "source"), true, null);
        var path = parsed[0].controls[0].containers;
        test.True(path.Length == 3 && string.IsNullOrEmpty(path[0].title) && string.IsNullOrEmpty(path[1].title) && !string.IsNullOrEmpty(path[2].title), "Generated HLSL uses untitled Group/Tabs and a titled Tab");
        var snapshot = Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", graph, null);
        var reopened = (ShaderGraph)Call(typeof(WhimTexDocumentJson), "ReadShaderGraph", snapshot);
        var saved = (ShaderGraphParameterItem)Layout("Find", reopened, tabs.id);
        test.True(saved.hideTitle && saved.title == "Remember Tabs", "Hidden title survives JSON with its text");
        var subset = (List<ShaderGraphParameterItem>)Layout("CopySubset", graph.parameterLayout, new HashSet<string> { p.id }, null);
        test.True(subset[0].hideTitle && subset[0].children[0].hideTitle, "Node clipboard retains hidden titles");
        Layout("SetTitleVisible", saved, true);
        test.True(!saved.hideTitle && saved.title == "Remember Tabs", "Showing title restores its text");
        var blank = new ShaderGraphParameterItem { kind = ShaderContainerKind.Group, title = "" };
        Layout("SetTitleVisible", blank, true);
        test.True(!blank.hideTitle && !string.IsNullOrEmpty(blank.title), "Showing an untitled container supplies a title");
        foreach (var kind in new[] { ShaderContainerKind.Foldout, ShaderContainerKind.Tab })
        {
            bool rejected = false;
            try { Layout("SetTitleVisible", new ShaderGraphParameterItem { kind = kind, title = "Required" }, false); }
            catch (TargetInvocationException e) when (e.InnerException is FormatException) { rejected = true; }
            test.True(rejected, kind + " keeps its required title");
        }
        var jsonType = snapshot.GetType().Assembly.GetType("Newtonsoft.Json.Linq.JArray");
        var operations = jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { "[{\"op\":\"setTitleVisibility\",\"id\":\"" + tabs.id + "\",\"visible\":true}]" });
        var edited = (ShaderGraph)Call(typeof(WhimTexApi), "EditShaderParameterLayout", graph, operations, null);
        test.True(!((ShaderGraphParameterItem)Layout("Find", edited, tabs.id)).hideTitle && tabs.hideTitle, "Visibility API edits atomically on a copy");
    }
    static string Signature(object source)
    {
        Call(source, "RefreshParameters");
        return string.Join("|", ((IReadOnlyList<ShaderParameter>)Get(source, "Parameters")).SelectMany(p => p.controls.Select(c => p.id + ":" + c.order + ":" + string.Join("/", c.containers.Select(x => x.kind + ":" + x.title + ":" + x.headerParameter)))));
    }
    static Foldout Block(VisualElement root, string id) => root.Query<Foldout>().ToList().Single(f => f.userData as string == id);
    static void Drag(ShaderGraphWindow window, VisualElement receiver, string id, Vector2 position)
    {
        var data = Activator.CreateInstance(typeof(ShaderGraphWindow).GetNestedType("ParameterDragData", F)); Set(data, "window", window); Set(data, "id", id);
        DragAndDrop.PrepareStartDrag(); DragAndDrop.SetGenericData("WhimTex.ShaderGraph.Parameter", data);
        using (var e = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated, mousePosition = position })) { e.target = receiver; receiver.SendEvent(e); }
        if (DragAndDrop.visualMode != DragAndDropVisualMode.Move) throw new Exception("Valid parameter drop rejected");
        using (var e = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = position })) { e.target = receiver; receiver.SendEvent(e); }
    }
    [Serializable] sealed class LayoutState { public List<ShaderGraphParameterItem> items; }
    static string LayoutJson(ShaderGraph graph) => JsonUtility.ToJson(new LayoutState { items = graph.parameterLayout });
    static ShaderGraph Clone(ShaderGraph graph) => (ShaderGraph)Call(typeof(WhimTexDocumentJson), "ReadShaderGraph",
        Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", graph, null));
    static string ViewTree(VisualElement view)
    {
        var result = new System.Text.StringBuilder();
        void Visit(VisualElement item, int depth)
        {
            result.Append(depth).Append(':').Append(item.GetType().Name);
            if (item is TextElement text) result.Append(':').Append(text.text);
            result.AppendLine();
            foreach (var child in item.Children()) Visit(child, depth + 1);
        }
        Visit(view, 0); return result.ToString();
    }
    public static async Task<object> Reliability()
    {
        var test = new WhimTex.Tests.TestContext();
        using var scope = new WhimTex.Tests.UnityA.UnityAScope();
        const string pref = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
        bool hadPref = EditorPrefs.HasKey(pref); string previousPref = EditorPrefs.GetString(pref);
        object oldDrag = DragAndDrop.GetGenericData("WhimTex.ShaderGraph.Parameter");
        WhimTexDocument document = null, loaded = null; ShaderFX effect = null; ShaderGraphWindow window = null;
        try
        {
            var graph = ShaderGraph.CreateDefault(); foreach (var node in graph.nodes) node.previewExpanded = false;
            var amount = Parameter(graph, "Amount", ShaderParameterType.Float); amount.floatValue = .37f;
            var vector = Parameter(graph, "Offset", ShaderParameterType.Vector3); vector.vectorValue = new Vector4(2, 3, 4, 0);
            var toggle = Parameter(graph, "Enabled", ShaderParameterType.Toggle); toggle.floatValue = 1;
            var a = Add(graph, ShaderContainerKind.Group, "Surface"); Move(graph, graph.parameters[0].id, a.id);
            var b = Add(graph, ShaderContainerKind.Foldout, "Details"); Move(graph, amount.id, b.id);
            var tabs = Add(graph, ShaderContainerKind.Tabs, "Variants"); tabs.hideTitle = true;
            var first = tabs.children[0]; first.title = "First"; Move(graph, toggle.id, first.id); first.headerParameterId = toggle.id;
            var second = Add(graph, ShaderContainerKind.Tab, "Second", tabs.id); Move(graph, vector.id, second.id);
            var empty = Add(graph, ShaderContainerKind.Group, "Empty"); empty.hideTitle = true;
            var initialGraph = Clone(graph);
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            document.layers.Add(new ShaderLayerBehaviour { draftGraph = graph }); Call(document, "NormalizeModel");
            effect = (ShaderFX)Call(document, "AddEmbeddedShaderFX", document.layers[0]);
            Set(effect, "program", new ShaderProgram { mode = ShaderProgramMode.Graph, draftGraph = Clone(graph) });
            Undo.FlushUndoRecordObjects(); Undo.ClearUndo(document); Undo.ClearUndo(effect);
            ShaderGraph LayerGraph() => ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            ShaderGraph EffectGraph() => ((ShaderProgram)Get(effect, "program")).draftGraph;
            async Task Open(bool fx)
            {
                if (window != null) window.Close();
                window = scope.OwnWindow(ScriptableObject.CreateInstance<ShaderGraphWindow>());
                Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
                Set(window, "effect", fx ? effect : null); Set(window, "effectTarget", fx);
                Set(window, "panelLayout", New("ShaderGraphWorkspaceLayout"));
                window.ShowUtility(); window.position = new Rect(70, 60, 1150, 900); window.CreateGUI(); await Frame(); await Frame();
            }
            void CompareViews(string label)
            {
                var targetGraph = (ShaderGraph)Get(window, "RootGraph");
                var other = Get(window, "effectTarget") is true ? LayerGraph() : EffectGraph();
                // Independent copies prevent a shared object from making the equality vacuous.
                if (!ReferenceEquals(other, targetGraph))
                {
                    var copy = Clone(targetGraph);
                    if ((bool)Get(window, "effectTarget")) ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph = copy;
                    else ((ShaderProgram)Get(effect, "program")).draftGraph = copy;
                }
                var inspectorSource = Get(window, "parameterSource");
                var layerSource = New("ShaderGraphParameterViewSource", document, document.layers[0].Id, false);
                var fxSource = New("ShaderFXParameterViewSource", effect);
                test.Equal(Signature(inspectorSource), Signature(layerSource), label + ": Inspector/layer metadata");
                test.Equal(Signature(inspectorSource), Signature(fxSource), label + ": Inspector/FX metadata");
                VisualElement View(object source) => (VisualElement)New("ShaderParameterView", source, false, null, null, -1);
                var inspector = View(inspectorSource); var layer = View(layerSource); var fxView = View(fxSource);
                test.Equal(ViewTree(inspector), ViewTree(layer), label + ": Inspector/layer controls and labels");
                test.Equal(ViewTree(inspector), ViewTree(fxView), label + ": Inspector/FX controls and labels");
            }
            foreach (bool fx in new[] { false, true })
            {
                await Open(fx); string context = fx ? "FX" : "Layer";
                ShaderGraph Current() => (ShaderGraph)Get(window, "RootGraph");
                VisualElement declarations = (VisualElement)Get(window, "declarations");
                async Task Drop(string id, string destination, bool before = false)
                {
                    foreach (var block in declarations.Query<Foldout>(className: "whimtex-shader-graph-parameter").ToList()) block.value = false;
                    for (var parent = Parent(Current(), destination); parent != null; parent = Parent(Current(), parent.id)) Block(declarations, parent.id).value = true;
                    await Frame();
                    var header = Block(declarations, destination).Q<Toggle>(className: Foldout.toggleUssClassName);
                    var position = header.worldBound.center; if (before) position.y = header.worldBound.yMin + 1;
                    string initial = LayoutJson(Current()); Undo.IncrementCurrentGroup();
                    Drag(window, declarations, id, position); Undo.FlushUndoRecordObjects(); await Frame(); Call(window, "RefreshInterface");
                    string expected = LayoutJson(Current()); test.True(expected != initial, context + ": drag changes layout");
                    Layout("Validate", Current());
                    Undo.PerformUndo(); await Frame(); Call(window, "RefreshInterface");
                    test.Equal(initial, LayoutJson(Current()), context + ": drag Undo restores complete tree");
                    Undo.PerformRedo(); await Frame(); Call(window, "RefreshInterface");
                    test.Equal(expected, LayoutJson(Current()), context + ": drag Redo restores complete tree");
                }
                await Drop(amount.id, a.id); test.Equal(a.id, Parent(Current(), amount.id)?.id, context + ": into Group");
                await Drop(amount.id, first.id); test.Equal(first.id, Parent(Current(), amount.id)?.id, context + ": into first Tab");
                await Drop(amount.id, second.id); test.Equal(second.id, Parent(Current(), amount.id)?.id, context + ": between Tabs");
                await Drop(amount.id, a.id, true); test.True(Parent(Current(), amount.id) == null, context + ": back to root");
                await Drop(a.id, b.id); test.Equal(b.id, Parent(Current(), a.id)?.id, context + ": whole Group into Foldout");
                await Drop(a.id, b.id, true); test.True(Parent(Current(), a.id) == null, context + ": whole Group back to root");
                await Drop(second.id, first.id, true);
                test.Equal(second.id, ((ShaderGraphParameterItem)Layout("Find", Current(), tabs.id)).children[0].id, context + ": reorder Tabs");
                CompareViews(context + " after drag/Undo/Redo");

                var states = new List<string> { LayoutJson(Current()) }; var random = new System.Random(98137);
                var destinations = new[] { a.id, b.id, first.id, second.id, (string)null };
                for (int i = 0; i < 18; i++)
                {
                    string parent = destinations[random.Next(destinations.Length)];
                    if (Parent(Current(), amount.id)?.id == parent) parent = destinations[(Array.IndexOf(destinations, parent) + 1) % destinations.Length];
                    Undo.IncrementCurrentGroup(); Call(window, "MoveParameterLayout", amount.id, parent, 0); Undo.FlushUndoRecordObjects();
                    states.Add(LayoutJson(Current())); test.True(states[i] != states[i + 1], context + ": sequence move " + i);
                }
                for (int i = states.Count - 2; i >= 0; i--)
                { Undo.PerformUndo(); Call(window, "RefreshInterface"); test.Equal(states[i], LayoutJson(Current()), context + ": multi Undo " + i); }
                for (int i = 1; i < states.Count; i++)
                { Undo.PerformRedo(); Call(window, "RefreshInterface"); test.Equal(states[i], LayoutJson(Current()), context + ": multi Redo " + i); }
                test.Near(.37, Current().FindParameter(amount.id).floatValue, 1e-6, context + ": value unchanged after move history");
                CompareViews(context + " after sequence");
                window.Close(); window = null;
                // Restore the fixture's initial independent copies for the second target.
                if (!fx) { ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph = Clone(initialGraph); ((ShaderProgram)Get(effect, "program")).draftGraph = Clone(initialGraph); }
            }

            await Open(false); CompareViews("Before TIFF save");
            string layerLayout = LayoutJson(LayerGraph()), fxLayout = LayoutJson(EffectGraph());
            string path = WhimTexDocumentFile.Save(document, scope.Assets + "/parameter-layout.tiff");
            window.Close(); window = null;
            Undo.ClearUndo(document); Undo.ClearUndo(effect);
            UnityEngine.Object.DestroyImmediate(document); document = null; effect = null;
            loaded = WhimTexDocumentFile.Load(path);
            test.True(string.IsNullOrEmpty((string)Get(loaded, "documentLoadWarning")), "TIFF reopened without recovery warnings");
            document = loaded; loaded = null; effect = (ShaderFX)document.layers[0].fx[0];
            test.Equal(layerLayout, LayoutJson(LayerGraph()), "TIFF restores layer layout, empty groups and hidden titles");
            test.Equal(fxLayout, LayoutJson(EffectGraph()), "TIFF restores FX layout, empty groups and hidden titles");
            test.Near(.37, LayerGraph().FindParameter(amount.id).floatValue, 1e-6, "TIFF restores layer value");
            test.Near(.37, EffectGraph().FindParameter(amount.id).floatValue, 1e-6, "TIFF restores FX value");
            await Open(false); CompareViews("After TIFF reopen");
            Undo.IncrementCurrentGroup(); Call(window, "MoveParameterLayout", amount.id, empty.id, 0); Undo.FlushUndoRecordObjects();
            layerLayout = LayoutJson(LayerGraph());
            Undo.PerformUndo(); await Frame(); Call(window, "RefreshInterface");
            test.Equal(fxLayout, LayoutJson(LayerGraph()), "Undo after reopening restores the saved tree");
            Undo.PerformRedo(); await Frame(); Call(window, "RefreshInterface");
            test.Equal(layerLayout, LayoutJson(LayerGraph()), "Redo after reopening restores the new group");
            WhimTexDocumentFile.Save(document, path); window.Close(); window = null;
            Undo.ClearUndo(document); Undo.ClearUndo(effect);
            UnityEngine.Object.DestroyImmediate(document); document = null; effect = null;
            document = WhimTexDocumentFile.Load(path); effect = (ShaderFX)document.layers[0].fx[0];
            test.Equal(layerLayout, LayoutJson(LayerGraph()), "Layout-only edit survives resaving the existing TIFF");
            await Open(false); CompareViews("After layout-only resave");
            return WhimTex.Tests.TestContext.Result("passed", test.Checks, "Parameter groups: drag, history, real TIFF reopen and shared views");
        }
        catch (Exception error) { return WhimTex.Tests.TestContext.Result("failed", test.Checks, "Parameter group reliability", error.ToString()); }
        finally
        {
            DragAndDrop.SetGenericData("WhimTex.ShaderGraph.Parameter", oldDrag);
            if (window != null) window.Close();
            if (effect != null) { Undo.ClearUndo(effect); UnityEngine.Object.DestroyImmediate(effect); }
            if (loaded != null) UnityEngine.Object.DestroyImmediate(loaded);
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadPref) EditorPrefs.SetString(pref, previousPref); else EditorPrefs.DeleteKey(pref);
        }
    }
    public static async Task<object> Run()
    {
        var test = new WhimTex.Tests.TestContext();
        WhimTexDocument document = null; ShaderGraphWindow window = null; ShaderFX effect = null;
        const string pref = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
        bool hadPref = EditorPrefs.HasKey(pref); string previousPref = EditorPrefs.GetString(pref);
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (TargetInvocationException e) when (e.InnerException is FormatException || e.InnerException?.GetType().Name == "WhimTexApiException") { rejected = true; }
            test.True(rejected, message);
        }
        try
        {
            CheckHiddenTitles(test);
            var graph = ShaderGraph.CreateDefault();
            var tint = graph.parameters[0]; var amount = Parameter(graph, "Amount", ShaderParameterType.Float);
            var toggle = Parameter(graph, "Enabled", ShaderParameterType.Toggle); var offset = Parameter(graph, "Offset", ShaderParameterType.Vector3);
            object before = Call(T("ShaderGraphCompiler"), "Compile", graph, null, "Out", false);
            string executable = (string)Get(before, "executableSource"), rawControls = JsonUtility.ToJson(tint);
            var group = Add(graph, ShaderContainerKind.Group, "Surface"); Move(graph, tint.id, group.id);
            var detail = Add(graph, ShaderContainerKind.Foldout, "Detail", group.id); Move(graph, amount.id, detail.id);
            var tabs = Add(graph, ShaderContainerKind.Tabs, "Variants", detail.id); var first = tabs.children[0]; first.title = "Primary";
            Move(graph, toggle.id, first.id); first.headerParameterId = toggle.id;
            var second = Add(graph, ShaderContainerKind.Tab, "Secondary", tabs.id); Move(graph, offset.id, second.id);
            var empty = Add(graph, ShaderContainerKind.Group, "Empty");
            group.headerParameterId = tint.id;
            Layout("Validate", graph);
            test.True(Parent(graph, offset.id) == second, "Parameter belongs to its Tab");
            string authored = JsonUtility.ToJson(graph);
            Reject(() => Move(graph, group.id, detail.id), "Moving a group into its descendant is rejected");
            Reject(() => Move(graph, amount.id, tabs.id), "Tabs rejects direct fields");
            Reject(() => Add(graph, ShaderContainerKind.Tab, "Detached"), "Orphan Tab rejected");
            test.Equal(authored, JsonUtility.ToJson(graph), "Invalid root Add and Move roll back completely");
            Reject(() => Add(graph, ShaderContainerKind.Group, "Nested", group.id), "Group nesting matches code declarations");
            Reject(() => Add(graph, ShaderContainerKind.Tabs, "Nested", second.id), "Nested Tabs rejected");
            Reject(() => Add(graph, ShaderContainerKind.Tab, "Primary", tabs.id), "Repeated page title rejected");
            test.Equal(authored, JsonUtility.ToJson(graph), "Invalid nested additions leave graph intact");
            var deep = ShaderGraph.CreateDefault(); ShaderGraphParameterItem deepest = null;
            for (int i = 0; i < 16; i++) deepest = Add(deep, ShaderContainerKind.Foldout, "Depth " + i, deepest?.id);
            Move(deep, deep.parameters[0].id, deepest.id);
            var nativeCopy = JsonUtility.FromJson<ShaderGraph>(JsonUtility.ToJson(deep));
            test.Equal(deepest.id, Parent(nativeCopy, nativeCopy.parameters[0].id)?.id, "All 16 layout levels survive native serialization");
            object after = Call(T("ShaderGraphCompiler"), "Compile", graph, null, "Out", false);
            test.Equal(executable, (string)Get(after, "executableSource"), "Layout leaves executable HLSL unchanged");
            test.True(((string)Get(after, "source")).Contains("@group(Surface; _GraphParam"), "Generated HLSL retains grouping and generated header symbol");
            test.Equal(rawControls, JsonUtility.ToJson(tint), "Projection does not mutate authored controls or values");
            foreach (var parameter in new[] { amount, toggle, offset })
            {
                var node = new ShaderGraphNode { kind = ShaderGraphNodeKind.Parameter, parameterId = parameter.id }; graph.nodes.Add(node);
                var generated = Call(T("ShaderGraphCompiler"), "Compile", graph, node.id, "Out", false);
                var parsed = (List<ShaderParameter>)Call(T("ShaderMetadata"), "Parse", Get(generated, "source"), true, null);
                test.True(parsed.Count == 1 && parsed[0].controls[0].containers.Length >= 2, "Nested generated metadata parses for " + parameter.name);
                graph.nodes.Remove(node);
            }
            string previousTitle = group.title; group.title = "";
            Reject(() => Layout("Validate", graph), "A linked header requires a title"); group.title = previousTitle;
            string previousName = amount.name; amount.name = toggle.name;
            Reject(() => Layout("Validate", graph), "Ambiguous header names rejected"); amount.name = previousName;
            test.True(!(bool)Layout("CanUseHeader", new ShaderParameter { type = ShaderParameterType.Texture2D }, ShaderContainerKind.Group), "Resource cannot be offered as header");
            var fragment = (List<ShaderGraphParameterItem>)Layout("CopySubset", graph.parameterLayout, new HashSet<string> { offset.id }, null);
            var newId = Guid.NewGuid().ToString("N");
            var remapped = (List<ShaderGraphParameterItem>)Layout("CopySubset", fragment, new HashSet<string> { offset.id }, new Dictionary<string, string> { [offset.id] = newId });
            var import = ShaderGraph.CreateDefault(); var imported = new ShaderParameter { id = newId, name = "Imported", type = ShaderParameterType.Vector3 };
            import.parameters.Add(imported); import.parameterLayout = remapped; Layout("Normalize", import); Layout("Validate", import);
            test.Equal("Secondary", Parent(import, newId).title, "Subset import preserves selected Tab ancestry");
            test.True(remapped[0].id != group.id && remapped[0].headerParameterId == null, "Subset import renews container IDs and drops uncopied header binding");
            var snapshot = Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", graph, null);
            var reopened = (ShaderGraph)Call(typeof(WhimTexDocumentJson), "ReadShaderGraph", snapshot);
            test.True(snapshot.ToString() == Call(typeof(WhimTexDocumentJson), "ShaderGraphSnapshot", reopened, null).ToString(), "Graph JSON roundtrip including empty groups");
            test.True(reopened.parameterLayout.Any(i => i.id == empty.id && i.children.Count == 0), "Empty authoring group survives reopening");
            var program = new ShaderProgram { draftGraph = graph, workingGraph = reopened, mode = ShaderProgramMode.Code };
            var programData = Call(typeof(WhimTexDocumentJson), "ShaderProgramSnapshot", program, null, null);
            var readProgram = (ShaderProgram)Call(typeof(WhimTexDocumentJson), "ReadShaderProgram", programData);
            test.True(programData.ToString() == Call(typeof(WhimTexDocumentJson), "ShaderProgramSnapshot", readProgram, null, null).ToString(), "Draft and working layout survive inactive Graph mode");

            string operations = "[{\"op\":\"addContainer\",\"kind\":\"Foldout\",\"title\":\"API\",\"as\":\"a\"},{\"op\":\"move\",\"id\":\"" + amount.id + "\",\"parentId\":\"@a\"}]";
            var jsonType = snapshot.GetType().Assembly.GetType("Newtonsoft.Json.Linq.JArray");
            object Parse(string json) => jsonType.GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { json });
            var ops = Parse(operations);
            var edited = (ShaderGraph)Call(typeof(WhimTexApi), "EditShaderParameterLayout", graph, ops, null);
            test.Equal("API", Parent(edited, amount.id).title, "API aliases and move operation");
            test.Equal(authored, JsonUtility.ToJson(graph), "API edits a copy");
            ops = Parse(operations.Substring(0, operations.Length - 1) + ",{\"op\":\"move\",\"id\":\"@a\",\"parentId\":\"@a\"}]");
            Reject(() => Call(typeof(WhimTexApi), "EditShaderParameterLayout", graph, ops, null), "Invalid API batch rejected atomically");
            test.Equal(authored, JsonUtility.ToJson(graph), "Failed API batch does not change original");

            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 32;
            var shader = new ShaderLayerBehaviour(); shader.draftGraph = graph; document.layers.Add(shader); Call(document, "NormalizeModel");
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); window.name = "WhimTex Parameter Layout Test " + Guid.NewGuid().ToString("N");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id); Set(window, "panelLayout", New("ShaderGraphWorkspaceLayout"));
            window.ShowUtility(); window.position = new Rect(80, 80, 1100, 760); window.CreateGUI(); await Frame(); await Frame();
            var declarations = (VisualElement)Get(window, "declarations"); var source = Get(window, "parameterSource");
            var layerSource = New("ShaderGraphParameterViewSource", document, document.layers[0].Id, false);
            effect = ScriptableObject.CreateInstance<ShaderFX>(); Set(effect, "program", new ShaderProgram { mode = ShaderProgramMode.Graph, draftGraph = graph });
            var fxSource = New("ShaderFXParameterViewSource", effect);
            test.Equal(Signature(source), Signature(layerSource), "Inspector and Shader layer share layout");
            test.Equal(Signature(source), Signature(fxSource), "FX shares the same layout");
            var values = (VisualElement)Get(window, "parameterValues"); Call(values, "Refresh");
            test.True(values.Query(className: "whimtex-fx-parameter-group").ToList().Count >= 2, "Common renderer creates groups");
            test.True(values.Query<Foldout>().ToList().Count >= 1, "Common renderer creates Foldout");
            test.True(values.Query<Button>().ToList().Any(b => b.text == "Secondary"), "Common renderer creates Tab buttons");
            var rootBlock = Block(declarations, group.id); rootBlock.value = true; await Frame();
            var detailBlock = Block(declarations, detail.id); detailBlock.value = true; await Frame();
            var amountBlock = Block(declarations, amount.id); var header = rootBlock.Q<Toggle>(className: Foldout.toggleUssClassName);
            Undo.IncrementCurrentGroup();
            Drag(window, declarations, amount.id, header.worldBound.center); await Frame(); Call(window, "RefreshInterface");
            test.Equal(group.id, Parent(((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph, amount.id)?.id, "UI drag moves into group");
            test.True(ReferenceEquals(amountBlock, Block(declarations, amount.id)), "Reparent retains parameter block and controls");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); await Frame(); Call(window, "RefreshInterface");
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            test.Equal(detail.id, Parent(graph, amount.id)?.id, "Drag Undo restores parent");
            Undo.PerformRedo(); await Frame(); Call(window, "RefreshInterface");
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            test.Equal(group.id, Parent(graph, amount.id)?.id, "Drag Redo restores destination");
            test.Equal(Signature(source), Signature(layerSource), "Both views resolve new model after Undo/Redo");
            var stable = Block(declarations, amount.id); Call(window, "RebuildParameters");
            test.True(ReferenceEquals(stable, Block(declarations, amount.id)), "Unchanged rebuild retains controls");
            Block(declarations, amount.id).value = true; await Frame();
            var nameField = Block(declarations, amount.id).Q<TextField>(); nameField.value = "Roughness";
            await Frame(); Call(window, "RefreshInterface");
            test.Equal("Roughness", graph.FindParameter(amount.id).name, "Reused block callback resolves current parameter");
            Call(window, "SelectParameterItem", detail.id); Call(window, "AddParameter", ShaderParameterType.Vector2); await Frame(); Call(window, "RefreshInterface");
            test.Equal(detail.id, Parent(graph, graph.parameters.Last().id)?.id, "Plus adds parameter to selected container");
            var rootHeader = Block(declarations, group.id).Q<Toggle>(className: Foldout.toggleUssClassName);
            Drag(window, declarations, empty.id, new Vector2(rootHeader.worldBound.center.x, rootHeader.worldBound.yMin + 1)); await Frame(); Call(window, "RefreshInterface");
            test.Equal(empty.id, graph.parameterLayout[0].id, "Whole groups reorder as one block");
            var detailHeader = Block(declarations, detail.id).Q<Toggle>(className: Foldout.toggleUssClassName);
            Drag(window, declarations, empty.id, detailHeader.worldBound.center); await Frame(); Call(window, "RefreshInterface");
            test.Equal(detail.id, Parent(graph, empty.id)?.id, "Whole group can move into a Foldout");
            var copied = (ShaderGraph)Call(typeof(WhimTexDocumentJson), "ReadShaderGraph", snapshot);
            Layout("Ungroup", copied, tabs.id);
            test.True(Parent(copied, offset.id)?.id == detail.id && copied.parameters.Count == graph.parameters.Count - 1, "Ungroup preserves parameter data and moves page contents");

            var canvas = (GraphView)Get(window, "canvas");
            var nodeViews = (System.Collections.IDictionary)Get(canvas, "nodeViews");
            var parameterNode = graph.nodes.First(n => n.kind == ShaderGraphNodeKind.Parameter);
            string clipboard = (string)Call(canvas, "CopyNodes", new List<GraphElement> { (GraphElement)nodeViews[parameterNode.id] });
            var clipboardType = typeof(ShaderGraphWindow).GetNestedType("NodeClipboardData", F);
            string prefix = (string)canvas.GetType().GetField("ClipboardPrefix", F).GetValue(null);
            var packet = JsonUtility.FromJson(clipboard.Substring(prefix.Length), clipboardType); Set(packet, "target", "another-graph");
            int parameterCount = graph.parameters.Count;
            Call(canvas, "PasteNodes", "Paste", prefix + JsonUtility.ToJson(packet)); await Frame(); Call(window, "RefreshInterface");
            test.Equal(parameterCount + 1, graph.parameters.Count, "Cross-graph node paste imports parameter");
            var pasted = graph.parameters.Last();
            test.Equal("Surface", Parent(graph, pasted.id)?.title, "Actual node clipboard preserves grouping");
            test.Equal(pasted.id, Parent(graph, pasted.id)?.headerParameterId, "Actual node clipboard remaps header binding");
            Call(canvas, "PasteNodes", "Paste", clipboard); await Frame(); Call(window, "RefreshInterface");
            test.Equal(parameterCount + 1, graph.parameters.Count, "Same-graph node paste reuses parameter and layout");

            test.True(Block(declarations, group.id).Q<Button>("parameter-title-visibility")?.enabledSelf == false, "Linked Group header cannot be hidden");
            test.True(!Block(declarations, detail.id).contentContainer.Children().Any(c => c.ClassListContains("whimtex-shader-graph-parameter-title")) &&
                !Block(declarations, second.id).contentContainer.Children().Any(c => c.ClassListContains("whimtex-shader-graph-parameter-title")), "Foldout and Tab have no visibility button of their own");
            Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
            var visibility = Block(declarations, tabs.id).Q<Button>("parameter-title-visibility");
            using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = visibility; visibility.SendEvent(evt); }
            Undo.FlushUndoRecordObjects(); await Frame(); Call(window, "RefreshInterface");
            var hiddenTabs = (ShaderGraphParameterItem)Layout("Find", Get(window, "Graph"), tabs.id);
            test.True(hiddenTabs.hideTitle && hiddenTabs.title == "Variants", "Eye button hides title without erasing its text");
            test.True(!Block(declarations, tabs.id).Q<TextField>().enabledSelf, "Hidden title field is disabled");
            test.True(((VisualElement)Get(window, "parameterValues")).Q(className: "whimtex-fx-tabs-title") == null, "Inspector removes the title above its tabs");
            test.Equal(Signature(source), Signature(layerSource), "Hidden title matches Shader layer rendering metadata");
            test.Equal(Signature(source), Signature(fxSource), "Hidden title matches FX rendering metadata");
            Undo.PerformUndo(); await Frame(); Call(window, "RefreshInterface");
            test.True(!((ShaderGraphParameterItem)Layout("Find", Get(window, "Graph"), tabs.id)).hideTitle, "Undo restores title visibility");
            test.True(((VisualElement)Get(window, "parameterValues")).Q(className: "whimtex-fx-tabs-title") != null, "Undo restores the Inspector title");

            foreach (string id in new[] { group.id, detail.id, tabs.id }) Block(declarations, id).value = true;
            await Frame();

            System.IO.Directory.CreateDirectory("Temp/WhimTex/ParameterLayout");
            foreach (int width in new[] { 282, 230 })
            {
                var host = window.rootVisualElement.Q("graph-tools-group"); host.style.width = width; await Frame(); await Frame();
                var titleBlock = Block(declarations, tabs.id);
                var titleButton = titleBlock.Q<Button>("parameter-title-visibility");
                test.True(titleButton.worldBound.width > 0 && titleButton.worldBound.xMax <= titleBlock.worldBound.xMax + 1, "Title visibility button fits width " + width);
                var titleInput = titleBlock.Q<TextField>();
                test.True(titleInput.Q("unity-text-input").worldBound.width > 0 && titleInput.worldBound.xMax <= titleButton.worldBound.xMin, "Title input remains visible beside eye at width " + width);
                foreach (var block in declarations.Query<Foldout>(className: "whimtex-shader-graph-parameter").ToList())
                {
                    if (block.resolvedStyle.display == DisplayStyle.None || block.worldBound.height <= 0) continue;
                    test.True(block.worldBound.width > 80 && block.worldBound.xMax <= declarations.worldBound.xMax + 1, "Blocks fit panel width " + width);
                }
                using var cancel = new CancellationTokenSource(6000);
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/ParameterLayout/parameters-" + width + ".png", cancel.Token);
            }
            var inspectorTab = window.rootVisualElement.Query<Tab>().ToList().Single(t => t.label == "Inspector");
            inspectorTab.GetFirstAncestorOfType<TabView>().activeTab = inspectorTab;
            foreach (int width in new[] { 282, 230 })
            {
                window.rootVisualElement.Q("graph-tools-group").style.width = width; await Frame(); await Frame();
                using var cancel = new CancellationTokenSource(6000);
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/ParameterLayout/inspector-" + width + ".png", cancel.Token);
            }
            return WhimTex.Tests.TestContext.Result("passed", test.Checks, "Parameter layout, shared rendering, storage and Undo");
        }
        catch (Exception error) { return WhimTex.Tests.TestContext.Result("failed", test.Checks, "Parameter layout", error.ToString()); }
        finally
        {
            DragAndDrop.SetGenericData("WhimTex.ShaderGraph.Parameter", null);
            if (window != null) window.Close();
            if (effect != null) UnityEngine.Object.DestroyImmediate(effect);
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadPref) EditorPrefs.SetString(pref, previousPref); else EditorPrefs.DeleteKey(pref);
        }
    }
}
