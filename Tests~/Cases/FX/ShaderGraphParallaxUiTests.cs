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

public static class ShaderGraphParallaxUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, F).SetValue(o, value);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o, args);
    public static async Task<object> Run()
    {
        ShaderGraphWindow window = null; WhimTexDocument document = null; int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception(label); }
        try
        {
            document = ScriptableObject.CreateInstance<WhimTexDocument>(); document.hideFlags = HideFlags.HideAndDontSave;
            document.width = document.height = 16;
            var shader = new ShaderLayerBehaviour(); document.layers.Add(shader); Call(document, "NormalizeModel"); var graph = shader.draftGraph;
            foreach (var existing in graph.nodes) existing.position = new Vector2(1800, 1400);
            var nodes = new ShaderGraphNode[4];
            for (int i = 0; i < 4; i++)
            {
                var kind = i % 2 == 0 ? ShaderGraphNodeKind.ParallaxMapping : ShaderGraphNodeKind.ParallaxOcclusionMapping;
                var n = (ShaderGraphNode)T("ShaderGraphNodes").GetMethod("Create", F).Invoke(null, new object[] { kind, new Vector2(200 + (i % 2) * 550, 70 + (i / 2) * 320) });
                n.previewExpanded = false;
                if (i >= 2) { n.heightSource = ShaderGraphHeightSource.Value; n.inputs.RemoveAll(p => p.port == "Height"); }
                nodes[i] = n; graph.nodes.Add(n);
            }
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); window.titleContent = new GUIContent("Parallax Nodes Check");
            Set(window, "document", document); Set(window, "layerId", document.layers[0].Id);
            window.ShowUtility(); window.position = new Rect(40, 80, 1550, 800); window.CreateGUI(); await Task.Delay(400);
            var canvas = (GraphView)Get(window, "canvas"); canvas.UpdateViewTransform(new Vector3(260, 40, 0), Vector3.one * .85f);
            Node View(int i) => canvas.nodes.First(v => v.viewDataKey == nodes[i].id);
            for (int i = 0; i < 4; i++)
            {
                var v = View(i); var ports = v.Query<Port>().ToList();
                Check(ports.Any(p => p.direction == Direction.Output && p.portName == "Out(2)"), "Float2 output " + i);
                Check(ports.Any(p => p.portName.EndsWith("Height(" + (i < 2 ? "T2" : "1") + ")")), "Height type " + i);
                Check(ports.Any(p => p.portName == "Steps(1)") == (i % 2 == 1), "POM steps only " + i);
                Check(!v.Query<DropdownField>().ToList().Any(f => f.label == "Derivative"), "No derivative option " + i);
                Check(v.Query<DropdownField>().ToList().Any(f => f.label == "Channel") == (i < 2), "Texture channel only " + i);
                Check(v.Query<EnumField>().ToList().Any(f => f.label == "Wrap") == (i < 2), "Texture wrap only " + i);
                Check(v.Query<EnumField>().ToList().Any(f => f.label == "Filter") == (i < 2), "Texture filter only " + i);
            }
            var source = View(1).Query<DropdownField>().ToList().Single(f => f.label == "Source"); source.value = "Value"; await Task.Delay(150);
            Check(graph.FindNode(nodes[1].id).heightSource == ShaderGraphHeightSource.Value, "Source switch changes model");
            Check(View(1).Query<Port>().ToList().Any(p => p.portName.EndsWith("Height(1)")), "Source switch rebuilds input");
            Check(View(1).Query<Label>().ToList().Any(l => l.text.StartsWith("↻ ") && l.text.Contains("Height")), "POM replay indicator without connection");
            Check(!View(2).Query<Label>().ToList().Any(l => l.text.StartsWith("↻ ") && l.text.Contains("Height")), "PM single sample has no replay indicator");
            foreach (var n in nodes)
            {
                string aliases = (string)T("ShaderGraphNodes").GetMethod("SearchAliases", F).Invoke(null, new object[] { n.kind });
                Check(aliases.Contains(n.kind == ShaderGraphNodeKind.ParallaxMapping ? "Bump Offset" : "POM"), "Search alias");
            }
            View(1).Query<DropdownField>().ToList().Single(f => f.label == "Source").value = "Texture";
            await Task.Delay(150);
            Check(View(1).Query<Port>().ToList().Any(p => p.portName == "Height(T2)"), "Switch back restores texture input");
            using var cancel = new CancellationTokenSource(6000);
            await WhimTex.Tests.UnityA.UnityACapture.Capture(window, "Temp/WhimTex/Parallax/layout.png", cancel.Token);
            return new { status = "passed", checks };
        }
        finally
        {
            if (window != null) window.Close();
            if (document != null) { Undo.ClearUndo(document); UnityEngine.Object.DestroyImmediate(document); }
        }
    }
}
