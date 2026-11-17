using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class ShaderGraphArrangementTests
{
    sealed class MenuCapture : ContextualMenuManager
    {
        public override void DisplayMenuIfEventMatches(EventBase evt, IEventHandler target) { }
        protected override void DoDisplayMenu(DropdownMenu menu, EventBase triggerEvent) { }
    }

    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Source = "00000000000000000000000000000001";
    const string Filter = "00000000000000000000000000000002";
    const string Output = "00000000000000000000000000000003";
    const string Fixed = "00000000000000000000000000000004";
    const string Note = "00000000000000000000000000000005";
    const string Group = "00000000000000000000000000000006";
    const string Definition = "00000000000000000000000000000007";
    const string SubInput = "00000000000000000000000000000008";
    const string SubOutput = "00000000000000000000000000000009";

    static Type Model(string type) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + type, true);
    static object Call(object owner, string name, params object[] args)
    {
        var type = owner as Type ?? owner.GetType();
        return type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(owner is Type ? null : owner, args);
    }
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Flags).SetValue(owner, value);
    static object Json(string text) => Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json", true)
        .GetMethod("Parse", new[] { typeof(string) }).Invoke(null, new object[] { text });
    static ulong Code(ShaderGraph graph) => (ulong)Call(Model("ShaderGraphCompiler"), "CodeFingerprint", graph);

    static string Request(bool nestedProgram, bool explicitSource = false)
    {
        string graph = $@"{{
            ""nodes"": [
                {{ ""id"": ""{Source}"", ""kind"": ""PerlinNoise""{(explicitSource ? @", ""position"": [-400,140]" : "")} }},
                {{ ""id"": ""{Filter}"", ""kind"": ""Fractal"" }},
                {{ ""id"": ""{Output}"", ""kind"": ""Output"", ""position"": [1100, 60] }},
                {{ ""id"": ""{Fixed}"", ""kind"": ""Time"", ""position"": [0, 0] }}
            ],
            ""edges"": [
                {{ ""outputNode"": ""{Source}"", ""outputPort"": ""Value"", ""inputNode"": ""{Filter}"", ""inputPort"": ""In"" }},
                {{ ""outputNode"": ""{Filter}"", ""outputPort"": ""Out"", ""inputNode"": ""{Output}"", ""inputPort"": ""RGBA"" }}
            ],
            ""notes"": [{{ ""id"": ""{Note}"", ""position"": [380, 0], ""size"": [180, 220] }}],
            ""groups"": [{{ ""id"": ""{Group}"", ""position"": [12,32], ""elements"": [""{Fixed}""] }}],
            ""subGraphs"": [{{
                ""id"": ""{Definition}"",
                ""outputs"": [{{ ""id"": ""height"", ""name"": ""Height"", ""type"": ""Float"" }}],
                ""nodes"": [
                    {{ ""id"": ""{SubInput}"", ""kind"": ""Value"" }},
                    {{ ""id"": ""{SubOutput}"", ""kind"": ""SubGraphOutput"" }}
                ],
                ""edges"": [{{ ""outputNode"": ""{SubInput}"", ""inputNode"": ""{SubOutput}"", ""inputPort"": ""height"" }}]
            }}]
        }}";
        string settings = $@"{{ ""mode"": ""Graph"", ""draftGraph"": {graph} }}";
        return nestedProgram ? $@"{{ ""program"": {settings} }}" : settings;
    }

    public static async Task<object> Run()
    {
        int checks = 0;
        void Expect(bool condition, string description)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(description);
        }
        ShaderGraphWindow editor = null;
        EditorWindow host = null;
        WhimTexDocument document = null;
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>();
            document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 64;
            var shader = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
            Layer layer = shader;
            Call(layer, "AssignNewId");
            document.layers.Add(layer);
            var api = typeof(WhimTexApi);
            void SetShader(string request) => Call(api, "SetShader", document, shader, Json(request), null);
            SetShader(Request(false));
            ShaderGraph graph = shader.draftGraph;
            ShaderGraphNode Node(string id) => graph.FindNode(id);
            Vector2 Position(string id) => Node(id).position;
            Expect(Position(Fixed) == Vector2.zero && Position(Output) == new Vector2(1100, 60), "Explicit positions, including zero, are pinned");
            Expect(Position(Source).x < Position(Filter).x && Position(Filter).x < Position(Output).x, "New branch follows its connections");
            var definition = graph.FindSubGraph(Definition);
            Expect(definition.nodes[0].position.x < definition.nodes[1].position.x, "Sub Graph is arranged in its own scope");
            var before = graph.nodes.ToDictionary(n => n.id, n => n.position);
            var fx = (ShaderProgram)Call(api, "ReadFxProgramEdit", new ShaderProgram(), Json(Request(true)), document, null);
            Expect(fx.draftGraph.nodes.All(n => n.position == before[n.id]), "Nested FX program has deterministic identical layout");
            Node(Source).position = new Vector2(-900, 341);
            SetShader(Request(false));
            graph = shader.draftGraph;
            Expect(Position(Source) == new Vector2(-900, 341), "Omitted coordinate for an existing ID preserves its position");
            SetShader(Request(false, explicitSource: true));
            graph = shader.draftGraph;
            Expect(Position(Source) == new Vector2(-400, 140), "Explicit coordinates override retained positions");

            using (var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(document).Json, false))
            {
                var restored = ((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
                Expect(restored.nodes.All(n => n.position == Position(n.id)), "Coordinates survive document roundtrip");
            }

            // Build the real canvas in a host with no shader-preview update loop.
            editor = ScriptableObject.CreateInstance<ShaderGraphWindow>();
            Set(editor, "document", document);
            Set(editor, "layerId", layer.Id);
            var canvasType = typeof(ShaderGraphWindow).GetNestedType("Canvas", Flags);
            var canvas = (GraphView)Activator.CreateInstance(canvasType, Flags, null, new object[] { editor }, null);
            Set(editor, "canvas", canvas);
            Node(Source).position = Node(Filter).position = new Vector2(210, 80);
            var overlapped = Position(Source);
            var other = Position(Fixed);
            ulong fingerprint = Code(graph);
            host = ScriptableObject.CreateInstance<ArrangementTestHost>();
            host.titleContent = new GUIContent("WhimTex Test Arrange Selected");
            host.ShowUtility();
            var root = host.rootVisualElement;
            Call(Model("WhimTexUI"), "ApplyWindowStyles", root);
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.dcfapixels.whimtex/src/Editor/ShaderGraphWindow.uss"));
            root.AddToClassList("whimtex-shader-graph");
            root.Add(canvas);
            canvas.StretchToParentSize();
            Call(canvas, "Rebuild");
            await Task.Delay(200);
            foreach (var view in canvas.nodes)
                if (view.viewDataKey == Source || view.viewDataKey == Filter) canvas.AddToSelection(view);
            Expect(canvas.selection.Count == 2, "Both nodes selected in the real canvas");
            Undo.FlushUndoRecordObjects();
            Undo.ClearUndo(document);
            Undo.IncrementCurrentGroup();
            var menu = new DropdownMenu();
            var target = canvas.nodes.Single(n => n.viewDataKey == Filter);
            using (var evt = ContextualMenuPopulateEvent.GetPooled(null, menu, target, new MenuCapture()))
                target.SendEvent(evt);
            var arrange = menu.MenuItems().OfType<DropdownMenuAction>().SingleOrDefault(a => a.name == "Arrange Selected");
            Expect(arrange != null, "Arrange Selected is reachable from the node context menu");
            arrange.Execute();
            Undo.FlushUndoRecordObjects();
            Call(canvas, "RefreshValues");
            await Task.Delay(140);

            Rect Box(string id)
            {
                var view = canvas.nodes.Single(n => n.viewDataKey == id);
                var bounds = canvas.contentViewContainer.WorldToLocal(view.worldBound);
                foreach (var field in view.Query<VisualElement>(className: "whimtex-shader-graph-value-fields").ToList())
                {
                    var r = canvas.contentViewContainer.WorldToLocal(field.worldBound);
                    if (r.width <= 0 || r.height <= 0) continue;
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                        Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
                }
                return bounds;
            }
            Expect(Position(Source).x < Position(Filter).x && !Box(Source).Overlaps(Box(Filter)), "Arrange Selected separates real node and inline-field bounds");
            var note = graph.notes.Single();
            var noteBounds = new Rect(note.position, note.size);
            Expect(!Box(Source).Overlaps(noteBounds) && !Box(Filter).Overlaps(noteBounds), "Arrange Selected avoids the existing note");
            Expect(Position(Fixed) == other && note.position == new Vector2(380, 0) &&
                graph.groups.Single().elements.SequenceEqual(new[] { Fixed }), "Unselected objects and membership are preserved");
            Expect(Code(graph) == fingerprint && graph.edges.Count == 2, "Arrangement only changes shader graph positions");
            Expect(!(bool)typeof(ShaderGraphWindow).GetField("compileRequested", Flags).GetValue(editor), "Arrangement does not request compilation");

            var arranged = Position(Source);
            Undo.PerformUndo();
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Expect(Position(Source) == overlapped && Position(Filter) == overlapped, "One Undo restores the whole selection");
            Undo.PerformRedo();
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Expect(Position(Source) == arranged && Code(graph) == fingerprint, "Redo restores the layout");
            return new { status = "passed", checks, failures = Array.Empty<string>(), message = "Agent Shader and FX layout, Sub Graph, fixed positions, persistence, real canvas selection, collision avoidance and Undo/Redo." };
        }
        catch (Exception error)
        {
            return new { status = "failed", checks, failures = new[] { error.ToString() }, message = error.GetBaseException().Message };
        }
        finally
        {
            if (host != null) host.Close();
            if (editor != null) Object.DestroyImmediate(editor);
            if (document != null)
            {
                Undo.ClearUndo(document);
                Object.DestroyImmediate(document);
            }
        }
    }
}

public sealed class ArrangementTestHost : EditorWindow { }
