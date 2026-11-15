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
    static object Get(object target, string name) => target.GetType().GetField(name, F)?.GetValue(target) ?? target.GetType().GetProperty(name, F).GetValue(target);
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
