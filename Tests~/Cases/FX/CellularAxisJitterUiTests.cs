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

public static class CellularAxisJitterUiTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static object Call(Type t, object target, string method, params object[] args) =>
        t.GetMethods(F).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(target, args);
    static Type Nodes => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex.ShaderGraphNodes");
    static void Set(object target, string field, object value) => target.GetType().GetField(field, F).SetValue(target, value);
    public static async Task<object> Run()
    {
        var t = new TestContext(); var focus = EditorWindow.focusedWindow;
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        ShaderGraphWindow window = null;
        try
        {
            doc.width = doc.height = 16; var behaviour = new ShaderLayerBehaviour(); doc.layers.Add(behaviour);
            Call(doc.GetType(), doc, "NormalizeModel"); var graph = behaviour.draftGraph;
            var nodes = new ShaderGraphNode[4];
            for (int i = 0; i < 4; i++)
            {
                var kind = i == 3 ? ShaderGraphNodeKind.TriangleVoronoiNoise : ShaderGraphNodeKind.CellularNoise;
                var n = nodes[i] = (ShaderGraphNode)Call(Nodes, null, "Create", kind, new Vector2(70 + 270 * i, 50));
                graph.nodes.Add(n); n.previewExpanded = false;
                Call(Nodes, null, "ChangeNoiseDimensions", graph, n, (ShaderGraphNoiseDimensions)(i == 3 ? 1 : i));
            }
            nodes[1].FindInput("Jitter").value = new Vector4(.2f, .7f, .9f, 0);
            window = ScriptableObject.CreateInstance<ShaderGraphWindow>(); Set(window, "document", doc); Set(window, "layerId", doc.layers[0].Id);
            window.ShowUtility(); window.position = new Rect(40, 50, 1420, 760); window.CreateGUI(); await Task.Delay(240);
            var canvas = (GraphView)window.GetType().GetField("canvas", F).GetValue(window);
            canvas.UpdateViewTransform(new Vector3(260, 10, 0), Vector3.one);
            Node View(int i) => canvas.nodes.First(n => n.viewDataKey == nodes[i].id);
            Port Jitter(int i) => View(i).Query<Port>().ToList().Single(p => p.direction == Direction.Input &&
                (string)p.userData.GetType().GetField("name", F).GetValue(p.userData) == "Jitter");
            for (int i = 0; i < 4; i++)
            {
                int width = i == 3 ? 2 : i + 1;
                t.Equal(width == 1 ? typeof(float) : width == 2 ? typeof(Vector2) : typeof(Vector3), Jitter(i).portType, "Jitter input width " + i);
                var ports = ((IEnumerable)Call(Nodes, null, "Inputs", nodes[i])).Cast<object>();
                var port = ports.Single(p => (string)p.GetType().GetField("name", F).GetValue(p) == "Jitter");
                t.Equal((ShaderGraphValueType)(width - 1), (ShaderGraphValueType)port.GetType().GetField("type", F).GetValue(port), "Registered Jitter type");
            }
            var dimensions = View(1).Query<DropdownField>().ToList().Single(f => f.label == "Dimensions");
            Undo.IncrementCurrentGroup();
            dimensions.value = "3D"; await Task.Delay(100);
            t.Equal(typeof(Vector3), Jitter(1).portType, "2D to 3D port");
            var current = ((ShaderLayerBehaviour)doc.layers[0].Behaviour).draftGraph.FindNode(nodes[1].id);
            t.Equal(new Vector4(.2f, .7f, .9f, 0), current.FindInput("Jitter").value, "Hidden axis retained");
            Undo.IncrementCurrentGroup();
            View(1).Query<DropdownField>().ToList().Single(f => f.label == "Dimensions").value = "1D"; await Task.Delay(100);
            Undo.IncrementCurrentGroup();
            View(1).Query<DropdownField>().ToList().Single(f => f.label == "Dimensions").value = "3D"; await Task.Delay(100);
            current = ((ShaderLayerBehaviour)doc.layers[0].Behaviour).draftGraph.FindNode(nodes[1].id);
            t.Equal(new Vector4(.2f, .7f, .9f, 0), current.FindInput("Jitter").value, "3D to 1D to 3D components");
            Undo.PerformUndo(); await Task.Delay(100);
            t.Equal(ShaderGraphNoiseDimensions.OneD, ((ShaderLayerBehaviour)doc.layers[0].Behaviour).draftGraph.FindNode(nodes[1].id).noiseDimensions, "Dimension Undo");
            Undo.PerformRedo(); await Task.Delay(100);
            t.Equal(ShaderGraphNoiseDimensions.ThreeD, ((ShaderLayerBehaviour)doc.layers[0].Behaviour).draftGraph.FindNode(nodes[1].id).noiseDimensions, "Dimension Redo");
            return new TestResult { status = "passed", checks = t.Checks, failures = Array.Empty<string>(),
                message = "Cellular and Triangle vector Jitter ports, hidden-axis retention and dimension Undo/Redo." };
        }
        catch (Exception error)
        {
            return new TestResult { status = "failed", checks = t.Checks, failures = new[] { error.ToString() },
                message = "Axis Jitter UI verification" };
        }
        finally
        {
            if (window != null) window.Close();
            Undo.ClearUndo(doc); UnityEngine.Object.DestroyImmediate(doc);
            if (focus != null) focus.Focus();
        }
    }
}
