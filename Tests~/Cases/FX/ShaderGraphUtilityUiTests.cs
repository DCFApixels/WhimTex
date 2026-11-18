using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public static class ShaderGraphUtilityUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o, args);
    public static async Task<object> Run()
    {
        System.IO.Directory.CreateDirectory("Temp/WhimTex/UtilityNodes");
        ShaderGraphWindow window = null; WhimTexDocument document = null;
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception(label); }
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader); Call(document, "NormalizeModel");
            var graph = shader.draftGraph;
            var kinds = new[] { ShaderGraphNodeKind.Switch, ShaderGraphNodeKind.Append, ShaderGraphNodeKind.NormalUnpack, ShaderGraphNodeKind.Shape };
            var testNodes = kinds.Select((kind, i) => (ShaderGraphNode)T("ShaderGraphNodes").GetMethod("Create", F).Invoke(null, new object[] { kind, new Vector2(50 + i * 270, 80) })).ToArray();
            foreach (var n in testNodes) { n.previewExpanded = false; graph.nodes.Add(n); }
            testNodes[3].option = 7;
            var uv = (ShaderGraphNode)T("ShaderGraphNodes").GetMethod("Create", F).Invoke(null, new object[] { ShaderGraphNodeKind.UV, new Vector2(30, 430) });
            uv.previewExpanded = false; graph.nodes.Add(uv);
            graph.edges.Add(new ShaderGraphEdge { outputNode = uv.id, inputNode = testNodes[1].id, inputPort = "A" });
            graph.edges.Add(new ShaderGraphEdge { outputNode = uv.id, inputNode = testNodes[0].id, inputPort = "A" });
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>();
            window.titleContent = new GUIContent("Utility Nodes Check");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            window.ShowUtility(); window.position = new Rect(40, 80, 1480, 760); window.CreateGUI();
            await Task.Delay(400);
            var canvas = (GraphView)Get(window, "canvas");
            canvas.UpdateViewTransform(new Vector3(250, 10, 0), Vector3.one);
            Node View(ShaderGraphNode n) => canvas.nodes.First(v => v.viewDataKey == n.id);
            var switchView = View(testNodes[0]);
            Check(switchView.Query<Port>().ToList().Any(p => p.portName == "A(2)"), "Switch case label and inferred width");
            Check(View(testNodes[1]).Query<Port>().ToList().Any(p => p.direction == Direction.Output && p.portName == "Out(3)"), "Append Float2 plus Float becomes Float3");
            var floor = switchView.Query<Toggle>().ToList().Single(t => t.label == "Floor Predicate");
            floor.value = false; await Task.Delay(120);
            Check(!graph.FindNode(testNodes[0].id).floorPredicate, "Floor toggle updates model");
            var condition = View(testNodes[0]).Q(className: "whimtex-shader-switch-case").Q<DropdownField>();
            condition.value = ">";
            await Task.Delay(120);
            Check(graph.FindNode(testNodes[0].id).switchCases[0].comparison == ShaderGraphComparison.Greater, "Condition updates case");
            var threshold = View(testNodes[0]).Q(className: "whimtex-shader-switch-case").Q<FloatField>();
            threshold.value = .25f; await Task.Delay(120);
            Check(graph.FindNode(testNodes[0].id).switchCases[0].threshold == .25f, "Threshold updates case");
            Call(window, "Edit", "Add test case", (Action<ShaderGraph>)(g => g.FindNode(testNodes[0].id).switchCases.Add(new ShaderGraphSwitchCase { port = "Third", threshold = 2 })), true, true, false);
            await Task.Delay(120);
            Check(View(testNodes[0]).Query<Port>().ToList().Any(p => p.portName.StartsWith("C(")), "Added case has readable label");
            var switchNode = graph.FindNode(testNodes[0].id);
            T("ShaderGraphNodes").GetMethod("RemoveSwitchCase", F).Invoke(null, new object[] { graph, switchNode, 1 });
            Check(graph.edges.Any(e => e.inputNode == switchNode.id && e.inputPort == "A"), "Removal keeps other case connection");
            Check(switchNode.switchCases[1].port == "Third", "Removal keeps stable port identity");
            window.CreateGUI(); await Task.Delay(180); canvas = (GraphView)Get(window, "canvas");
            canvas.UpdateViewTransform(new Vector3(250, 10, 0), Vector3.one);
            await Task.Delay(120);
            using (var layoutCancel = new CancellationTokenSource(6000))
                await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/UtilityNodes/layout-before.png", layoutCancel.Token);
            foreach (var row in View(testNodes[0]).Query(className: "whimtex-shader-switch-case").ToList())
            {
                var e = row.Q<DropdownField>(); var f = row.Q<FloatField>();
                Check(float.IsFinite(row.worldBound.width) && row.worldBound.width > 0, "Finite case row");
                Check(e.worldBound.xMax <= f.worldBound.xMin + 1, "Condition does not overlap value");
                Check(f.worldBound.xMax <= row.worldBound.xMax + 1 && f.worldBound.width >= 30, $"Threshold remains visible: row={row.worldBound}, enum={e.worldBound}, value={f.worldBound}");
                float labelWidth = e.labelElement.MeasureTextSize(e.label, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                Check(e.labelElement.worldBound.width >= labelWidth - .5f, "Case label remains fully visible");
            }
            var mode = View(testNodes[2]).Query<DropdownField>().ToList().First(f => f.choices.Contains("Tangent"));
            mode.value = "Object"; await Task.Delay(120);
            Check(graph.FindNode(testNodes[2].id).option == 1, "Unpack mode updates");
            Check(View(testNodes[3]).Query<Port>().ToList().Any(p => p.portName.StartsWith("Roundness(")), "Rounded polygon port");
            var shape = View(testNodes[3]).Query<DropdownField>().ToList().Single(f => f.label == "Shape");
            Check(shape.choices.SequenceEqual(new[] { "Ellipse", "Rectangle", "Rounded Rectangle", "Polygon",
                "Rounded Polygon", "Star", "Rounded Star", "Capsule", "Line Segment" }), "Adjacent rounded modes");
            shape.value = "Rounded Star"; await Task.Delay(120);
            Check(graph.FindNode(testNodes[3].id).option == 8, "Rounded Star maps to its stable option");
            Check(View(testNodes[3]).Query<Port>().ToList().Any(p => p.portName.StartsWith("InnerRadius(")), "Rounded Star inner radius port");
            Check(View(testNodes[3]).Query<Port>().ToList().Any(p => p.portName.StartsWith("Roundness(")), "Rounded Star roundness port");
            Undo.PerformUndo(); await Task.Delay(160);
            Check(((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph.FindNode(testNodes[3].id).option == 7, "Rounded Star mode Undo");
            Undo.PerformRedo(); await Task.Delay(160);
            Check(((ShaderLayerBehaviour)document.layers[0].Behaviour).draftGraph.FindNode(testNodes[3].id).option == 8, "Rounded Star mode Redo");
            using var cancel = new CancellationTokenSource(6000);
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/UtilityNodes/layout.png", cancel.Token);
            return new { status = "passed", checks, failures = Array.Empty<string>(),
                message = "Utility controls, Shape menu order, Rounded Star ports and mode Undo/Redo." };
        }
        finally
        {
            if (window != null) window.Close();
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
        }
    }
}
