using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests;

public static class ShaderGraphHeightSourceTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Static(string type, string name, params object[] args) => Call(T(type), null, name, args);
    static ShaderGraphNode Add(ShaderGraph graph, ShaderGraphNodeKind kind, Vector2 position)
    {
        var node = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, position);
        node.previewExpanded = false; graph.nodes.Add(node); return node;
    }
    static object Snapshot(ShaderGraph graph) => Static("WhimTexDocumentJson", "ShaderGraphSnapshot", graph, null);
    static ShaderGraph Read(object json) => (ShaderGraph)Static("WhimTexDocumentJson", "ReadShaderGraph", json);
    static void Switch(ShaderGraph graph, ShaderGraphNode node, ShaderGraphHeightSource source) => Static("ShaderGraphNodes", "ChangeHeightSource", graph, node, source);
    static bool SameSnapshot(object a, object b) => a.ToString() == b.ToString();
    static string HeightState(ShaderGraph graph, string id)
    {
        var node = graph.FindNode(id);
        return node.heightSource + "|" + string.Join("|", node.inputs.Select(p => p.port + ":" + p.type + ":" + p.source + ":" + p.value.ToString("R") +
            ":" + (p.resource == null ? "none" : p.resource.id + ":" + p.resource.type + ":" + AssetDatabase.GetAssetPath(p.resource.textureValue)))) +
            "|" + string.Join("|", graph.edges.Where(e => e.inputNode == id).Select(e => e.outputNode + ":" + e.outputPort + ":" + e.inputPort));
    }
    static string Source(ShaderGraph graph, string node) => (string)Get(Static("ShaderGraphCompiler", "Compile", graph, node, "Out", false), "source");

    public static async Task<object> Run()
    {
        using var scope = new WhimTex.Tests.UnityA.UnityAScope();
        const string layoutKey = "DCFApixels.WhimTex.ShaderGraph.PanelLayout";
        bool hadLayout = EditorPrefs.HasKey(layoutKey); string layout = EditorPrefs.GetString(layoutKey);
        var t = new TestContext();
        WhimTexDocument document = null; ShaderGraphWindow window = null;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader);
            Call(typeof(WhimTexDocument), document, "NormalizeModel");
            var graph = shader.draftGraph;
            foreach (var node in graph.nodes) { node.position = new Vector2(2200, 1600); node.previewExpanded = false; }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.dcfapixels.whimtex/src/Editor/Icons/GradientCursor.png");
            t.True(texture != null, "Existing texture fixture");
            var parameter = new ShaderParameter { name = "_HeightSource", type = ShaderParameterType.Texture2D, textureValue = texture };
            graph.parameters.Add(parameter);
            var resource = Add(graph, ShaderGraphNodeKind.Parameter, new Vector2(40, 50)); resource.parameterId = parameter.id;
            var value = Add(graph, ShaderGraphNodeKind.UV, new Vector2(40, 200));
            var kinds = new[] { ShaderGraphNodeKind.HeightToNormal, ShaderGraphNodeKind.HeightToDistortion,
                ShaderGraphNodeKind.ParallaxMapping, ShaderGraphNodeKind.ParallaxOcclusionMapping };
            var nodes = kinds.Select((kind, i) => Add(graph, kind, new Vector2(300 + i * 290, 80))).ToArray();
            foreach (var node in nodes)
            {
                node.inputs.Add(new ShaderGraphInput { port = "Height", type = ShaderGraphValueType.Texture,
                    resource = new ShaderParameter { type = ShaderParameterType.Texture2D, textureValue = texture } });
                graph.edges.Add(new ShaderGraphEdge { outputNode = resource.id, inputNode = node.id, inputPort = "Height" });
                var before = Snapshot(graph);
                var textureSource = Source(graph, node.id);
                Switch(graph, node, ShaderGraphHeightSource.Value);
                t.True(node.FindInput("Height") == null, node.kind + " new Value uses its default");
                t.True(node.FindInput("HeightTexture").resource.textureValue == texture, node.kind + " retains inline texture");
                t.True(graph.edges.Any(e => e.inputNode == node.id && e.inputPort == "HeightTexture" && e.outputNode == resource.id), node.kind + " retains texture edge");
                var ports = ((IEnumerable)Static("ShaderGraphNodes", "Inputs", node)).Cast<object>();
                t.Near(node.kind == ShaderGraphNodeKind.HeightToNormal || node.kind == ShaderGraphNodeKind.HeightToDistortion ? 0 : .5,
                    ((Vector4)Get(ports.Single(p => (string)Get(p, "name") == "Height"), "value")).x, 0, node.kind + " original default");
                Switch(graph, node, ShaderGraphHeightSource.Texture);
                t.True(SameSnapshot(before, Snapshot(graph)), node.kind + " untouched round trip is exact");
                t.Equal(textureSource, Source(graph, node.id), node.kind + " texture HLSL unchanged");
                Switch(graph, node, ShaderGraphHeightSource.Value);
                node.inputs.Add(new ShaderGraphInput { port = "Height", type = ShaderGraphValueType.Float, value = Vector4.one * .37f });
                graph.edges.Add(new ShaderGraphEdge { outputNode = value.id, inputNode = node.id, inputPort = "Height" });
                var both = Snapshot(graph);
                for (int i = 0; i < 3; i++)
                {
                    Switch(graph, node, ShaderGraphHeightSource.Texture);
                    t.Near(.37, node.FindInput("HeightValue").value.x, 1e-6, node.kind + " retains scalar default");
                    Switch(graph, node, ShaderGraphHeightSource.Value);
                }
                t.True(SameSnapshot(both, Snapshot(graph)), node.kind + " repeated switches preserve both sources");
                foreach (var mode in new[] { ShaderGraphHeightSource.Texture, ShaderGraphHeightSource.Value })
                {
                    Switch(graph, node, mode);
                    var saved = Snapshot(graph); var loaded = Read(saved);
                    t.True(SameSnapshot(saved, Snapshot(loaded)), node.kind + " JSON round trip " + mode);
                    var clean = Read(saved); var cleanNode = clean.FindNode(node.id);
                    string inactive = mode == ShaderGraphHeightSource.Texture ? "HeightValue" : "HeightTexture";
                    cleanNode.inputs.RemoveAll(p => p.port == inactive);
                    clean.edges.RemoveAll(e => e.inputNode == node.id && e.inputPort == inactive);
                    t.Equal(Source(clean, node.id), Source(loaded, node.id), node.kind + " inactive source excluded from HLSL " + mode);
                    object[] vmArgs = { loaded, node.id, "Out", null };
                    t.True((bool)Static("ShaderGraphPreviewVM", "TryCreate", vmArgs), node.kind + " SVM accepts retained source " + mode);
                    using ((IDisposable)vmArgs[3])
                    {
                        object[] cleanVmArgs = { clean, node.id, "Out", null };
                        t.True((bool)Static("ShaderGraphPreviewVM", "TryCreate", cleanVmArgs), node.kind + " clean SVM " + mode);
                        using ((IDisposable)cleanVmArgs[3])
                            t.True(((IEnumerable)Get(vmArgs[3], "instructions")).Cast<object>().SequenceEqual(
                                ((IEnumerable)Get(cleanVmArgs[3], "instructions")).Cast<object>()), node.kind + " inactive source excluded from SVM " + mode);
                    }
                    Switch(loaded, loaded.FindNode(node.id), mode == ShaderGraphHeightSource.Texture ? ShaderGraphHeightSource.Value : ShaderGraphHeightSource.Texture);
                    loaded.Validate();
                }
            }
            window = scope.OwnWindow(ScriptableObject.CreateInstance<ShaderGraphWindow>()); window.titleContent = new GUIContent("Height Source Check");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            window.ShowUtility(); window.position = new Rect(40, 80, 1550, 700); window.CreateGUI();
            await Task.Delay(250);
            GraphView Canvas() => (GraphView)Get(window, "canvas");
            Node View(string id) => Canvas().nodes.Single(n => n.viewDataKey == id);
            async Task<(Port active, Port inactive)> ReadyPorts(string id, string mode)
            {
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    var ports = View(id).Query<Port>().ToList().Where(p => p.direction == Direction.Input).ToList();
                    var active = ports.FirstOrDefault(p => (string)Get(p.userData, "name") == "Height");
                    var inactive = ports.FirstOrDefault(p => (string)Get(p.userData, "name") == (mode == "Texture" ? "HeightValue" : "HeightTexture"));
                    if (active != null && inactive != null && active.connected && inactive.connected &&
                        active.enabledInHierarchy && !inactive.enabledInHierarchy) return (active, inactive);
                    await Task.Delay(50);
                }
                throw new TimeoutException("Height ports did not finish rebuilding: " + id + " " + mode);
            }
            foreach (var original in nodes)
            {
                string id = original.id;
                foreach (var mode in new[] { "Texture", "Value" })
                {
                    View(id).Query<DropdownField>().ToList().Single(f => f.label == "Source").value = mode;
                    var (active, inactive) = await ReadyPorts(id, mode); Undo.FlushUndoRecordObjects();
                    t.True(active.enabledInHierarchy && !inactive.enabledInHierarchy, original.kind + " active/inactive port state " + mode);
                    t.True(active.connected && inactive.connected, original.kind + " both wires visible " + mode);
                    t.Equal(2, shader.draftGraph.edges.Count(e => e.inputNode == id), original.kind + " no lost edges " + mode);
                }
            }
            string undoId = nodes[0].id;
            string undoBefore = HeightState(shader.draftGraph, undoId);
            Undo.IncrementCurrentGroup();
            View(undoId).Query<DropdownField>().ToList().Single(f => f.label == "Source").value = "Texture";
            await ReadyPorts(undoId, "Texture"); Undo.FlushUndoRecordObjects();
            string undoAfter = HeightState(shader.draftGraph, undoId);
            Undo.PerformUndo(); await ReadyPorts(undoId, "Value");
            shader = (ShaderLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(undoBefore, HeightState(shader.draftGraph, undoId), "Undo restores sources, defaults and edges");
            Undo.PerformRedo(); await ReadyPorts(undoId, "Texture");
            shader = (ShaderLayerBehaviour)document.layers[0].Behaviour;
            t.Equal(undoAfter, HeightState(shader.draftGraph, undoId), "Redo restores sources, defaults and edges");
            return TestContext.Result("passed", t.Checks, "Height sources retain connections, inline values and inactive ports through switching, JSON and Undo/Redo; HLSL and SVM exclude the inactive source.").ToJson();
        }
        catch (Exception error) { return TestContext.Result("failed", t.Checks, "Height source retention", error.ToString()).ToJson(); }
        finally
        {
            if (window != null) WhimTex.Tests.UnityA.UnityAScope.CloseOwned(window);
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            if (hadLayout) EditorPrefs.SetString(layoutKey, layout); else EditorPrefs.DeleteKey(layoutKey);
        }
    }
}
