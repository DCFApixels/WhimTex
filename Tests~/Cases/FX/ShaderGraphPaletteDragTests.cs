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

public static class ShaderGraphPaletteDragTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Key = "WhimTex.ShaderGraph.NodePalette";
    static object Get(object o, string name) => o.GetType().GetField(name, F)?.GetValue(o) ?? o.GetType().GetProperty(name, F)?.GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, F).Invoke(o, args);
    static Type Type(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);

    public static async Task<object> Run(bool capture = false)
    {
        WhimTexDocument document = null; ShaderGraphWindow window = null; object preview = null;
        int checks = 0, candidates = 0; bool keepWindow = false;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        try
        {
            Check(DragAndDrop.GetGenericData(Key) == null, "No existing palette drag");
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader); Call(document, "NormalizeModel");
            shader.draftGraph.parameters.Add(new ShaderParameter { name = "Drag Test", type = ShaderParameterType.Float });
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
            window.name = "WhimTex Palette Drag Test";
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            Set(window, "panelLayout", Activator.CreateInstance(Type("ShaderGraphWorkspaceLayout")));
            window.titleContent = new GUIContent("Node Drag Check");
            window.ShowUtility(); window.position = new Rect(80, 80, 1100, 800); window.CreateGUI();
            await Task.Delay(250);
            var root = window.rootVisualElement;
            var canvas = (GraphView)Get(window, "canvas");
            var palette = Get(window, "nodePalette"); Call(palette, "RefreshIfNeeded");
            preview = Get(window, "paletteDragPreview");
            var choices = ((IEnumerable)Get(palette, "choices")).Cast<object>().ToArray();
            var graph = shader.draftGraph;
            string before = JsonUtility.ToJson(graph);
            int inputs = ((IDictionary)Get(canvas, "inputPorts")).Count;
            int outputs = ((IDictionary)Get(canvas, "outputPorts")).Count;
            int previews = ((IDictionary)Get(canvas, "previews")).Count;
            int caretKeys = ((IDictionary)Get(window, "functionCaretKeys")).Count;
            Vector2 point = new Vector2(canvas.worldBound.xMax - 220, canvas.worldBound.yMin + 150);
            void Begin(object choice)
            {
                var data = Activator.CreateInstance(typeof(ShaderGraphWindow).GetNestedType("NodePaletteDragData", F), true);
                Set(data, "palette", palette); Set(data, "graph", graph); Set(data, "choice", choice);
                Set(data, "svmOnly", (bool)Get(window, "svmNodesOnly"));
                DragAndDrop.SetGenericData(Key, data); Call(preview, "Begin", data, point);
            }
            foreach (var choice in choices)
            {
                Begin(choice); candidates++;
                var ghost = root.Q("node-palette-drag-preview");
                Check(ghost != null, "Ghost created: " + choice);
                Check(ghost.Query<VisualElement>().ToList().All(e => e.pickingMode == PickingMode.Ignore && !e.focusable), "Ghost cannot intercept input: " + choice);
                Check(ghost.Query<VisualElement>().ToList().All(e => e.GetType().Name != "NodePreview"), "No live preview: " + choice);
                Call(preview, "Clear");
            }
            Check(before == JsonUtility.ToJson(graph), "All candidates leave graph unchanged");
            Check(inputs == ((IDictionary)Get(canvas, "inputPorts")).Count && outputs == ((IDictionary)Get(canvas, "outputPorts")).Count, "No registered candidate ports");
            Check(previews == ((IDictionary)Get(canvas, "previews")).Count, "No candidate previews queued");
            Check(caretKeys == ((IDictionary)Get(window, "functionCaretKeys")).Count, "No retained candidate code caret keys");

            foreach (float scale in new[] { .4f, 1f, 2f })
            {
                canvas.UpdateViewTransform(new Vector3(35, 25, 0), new Vector3(scale, scale, 1));
                Begin(ShaderGraphNodeKind.GaborNoise); await Task.Delay(140);
                var ghost = root.Q("node-palette-drag-preview");
                var node = ghost.Q<Node>();
                Check(node.worldBound.width > 0 && node.worldBound.height > 0, "Candidate has layout at " + scale);
                Check(Mathf.Abs(node.worldBound.width - node.layout.width * scale) < 1, "Candidate follows canvas zoom " + scale);
                Vector2 moved = point + new Vector2(-40, 30);
                using (var e = DragUpdatedEvent.GetPooled(new Event { type = EventType.DragUpdated, mousePosition = moved }))
                { e.target = canvas; canvas.SendEvent(e); }
                await Task.Delay(50);
                var expected = (Vector2)Call(preview, "DropPosition", moved);
                var actual = canvas.contentViewContainer.WorldToLocal(node.worldBound.position);
                Check(Vector2.Distance(expected, actual) < 1, "Drop matches displayed node at " + scale + ": " + expected + "/" + actual);
                using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) { e.target = root; root.SendEvent(e); }
                Check(root.Q("node-palette-drag-preview") == null && DragAndDrop.GetGenericData(Key) == null, "Escape cancels");
            }

            Begin(ShaderGraphNodeKind.Add); await Task.Delay(100);
            using (var e = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = new Vector2(10, 10) }))
            { e.target = root; root.SendEvent(e); }
            Check(root.Q("node-palette-drag-preview") == null && before == JsonUtility.ToJson(graph), "Invalid drop does not add node");

            Begin(ShaderGraphNodeKind.Add); await Task.Delay(100);
            var drop = (Vector2)Call(preview, "DropPosition", point);
            int count = graph.nodes.Count;
            Undo.FlushUndoRecordObjects(); Undo.ClearUndo(document); Undo.IncrementCurrentGroup();
            using (var e = DragPerformEvent.GetPooled(new Event { type = EventType.DragPerform, mousePosition = point }))
            { e.target = canvas; canvas.SendEvent(e); }
            Undo.FlushUndoRecordObjects(); await Task.Delay(150);
            Check(graph.nodes.Count == count + 1 && graph.nodes.Last().kind == ShaderGraphNodeKind.Add, "Canvas drop creates exactly one node");
            Check(Vector2.Distance(graph.nodes.Last().position, drop) < .01f, "Drop preserves ghost position");
            Check(root.Q("node-palette-drag-preview") == null && DragAndDrop.GetGenericData(Key) == null, "Drop releases ghost and payload");
            Undo.PerformUndo(); await Task.Delay(100);
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Check(graph.nodes.Count == count, "One Undo removes dropped node");
            Undo.PerformRedo(); await Task.Delay(100);
            graph = ((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph;
            Check(graph.nodes.Count == count + 1, "Redo restores dropped node");

            Begin(ShaderGraphNodeKind.Add); DragAndDrop.SetGenericData(Key, null); Call(preview, "Validate");
            Check(root.Q("node-palette-drag-preview") == null, "Stale payload removes ghost");
            Begin(ShaderGraphNodeKind.Add); window.CreateGUI();
            Check(root.Q("node-palette-drag-preview") == null && DragAndDrop.GetGenericData(Key) == null, "Window rebuild cancels drag");
            if (capture)
            {
                canvas = (GraphView)Get(window, "canvas"); palette = Get(window, "nodePalette"); preview = Get(window, "paletteDragPreview");
                Call(palette, "RefreshIfNeeded");
                canvas.UpdateViewTransform(Vector3.zero, new Vector3(.85f, .85f, 1));
                await Task.Delay(150);
                point = new Vector2(canvas.worldBound.center.x, canvas.worldBound.yMin + 160);
                Begin(ShaderGraphNodeKind.GaborNoise); await Task.Delay(180); keepWindow = true;
            }
            return new { status = "passed", checks, failures = Array.Empty<string>(), message = candidates + " catalog candidates; isolated UI, zoom/drop alignment, cancellation, Undo/Redo and rebuild cleanup." };
        }
        catch (Exception e) { return new { status = "failed", checks, failures = new[] { e.ToString() }, message = e.Message }; }
        finally
        {
            if (!keepWindow)
            {
                if (preview != null) Call(preview, "Clear");
                if (window != null) window.Close();
                if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
            }
        }
    }

    public static object CloseCapture()
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<ShaderGraphWindow>().Where(w => w.name == "WhimTex Palette Drag Test").ToArray())
        {
            var document = (WhimTexDocument)Get(window, "document");
            window.Close(); Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document);
        }
        return new { status = "passed" };
    }

    public static object PositionCapture()
    {
        var window = Resources.FindObjectsOfTypeAll<ShaderGraphWindow>().Single(w => w.name == "WhimTex Palette Drag Test");
        Call(Get(window, "paletteDragPreview"), "Move", window.rootVisualElement.worldBound.center - new Vector2(0, 160));
        return window.titleContent.text;
    }
}
