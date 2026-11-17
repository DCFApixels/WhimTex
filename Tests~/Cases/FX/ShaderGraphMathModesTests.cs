using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ShaderGraphMathModesTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }

    public static async Task<object> Run()
    {
        var nodes = Find("ShaderGraphNodes");
        var graph = new ShaderGraph();
        var cases = new List<(string label, ShaderGraphNode node, string port, Color expected)>();
        ShaderGraphNode Node(ShaderGraphNodeKind kind)
        {
            var n = (ShaderGraphNode)Call(nodes, null, "Create", kind, Vector2.zero);
            graph.nodes.Add(n); return n;
        }
        void Set(ShaderGraphNode n, string port, Vector4 value, ShaderGraphValueType? type = null)
        {
            var input = n.FindInput(port);
            Check(input != null, "Missing input " + n.kind + "." + port);
            input.value = value;
            if (type.HasValue) input.type = type.Value;
        }
        void Scalar(ShaderGraphNode n, string port, float value) => Set(n, port, Vector4.one * value);
        void Case(string label, ShaderGraphNode n, Color expected, string port = "Out") => cases.Add((label, n, port, expected));
        Color Gray(float v) => new Color(v, v, v, 1);
        Color Vector(Vector3 v) => new Color(v.x, v.y, v.z, 1);

        Check(((string[])Call(nodes, null, "ModeNames", ShaderGraphNodeKind.Log)).SequenceEqual(new[] { "Base E", "Base 2", "Base 10" }), "Log bases");
        Check(((string[])Call(nodes, null, "ModeNames", ShaderGraphNodeKind.RotateAboutAxis)).SequenceEqual(new[] { "Radians", "Degrees", "Turns" }), "Rotation units");
        Check(((string[])Call(nodes, null, "ModeNames", ShaderGraphNodeKind.Refract)).SequenceEqual(new[] { "Critical Angle", "Safe" }), "Refraction modes");

        for (int mode = 0; mode < 3; mode++)
        for (int width = 1; width <= 4; width++)
        {
            var n = Node(ShaderGraphNodeKind.Log); n.option = mode;
            var input = new Vector4(.125f, 1, 4, 10);
            Set(n, "In", input, (ShaderGraphValueType)(width - 1));
            var output = new Vector4(0, 0, 0, 1);
            for (int c = 0; c < width; c++) output[c] = (float)(Math.Log(input[c]) / (mode == 1 ? Math.Log(2) : mode == 2 ? Math.Log(10) : 1));
            Case("Log/" + mode + "/" + width, n, width == 1 ? Gray(output.x) : (Color)output);
        }
        var modulo = Node(ShaderGraphNodeKind.Modulo);
        Set(modulo, "A", new Vector4(-7, 7, -7, 7), ShaderGraphValueType.Float4);
        Set(modulo, "B", new Vector4(3, -3, -3, 3), ShaderGraphValueType.Float4);
        Case("Modulo signs", modulo, new Color(-1, 1, -1, 1));
        var mixed = Node(ShaderGraphNodeKind.Modulo);
        Scalar(mixed, "A", -7);
        Set(mixed, "B", new Vector4(3, -2, 4, 0), ShaderGraphValueType.Float3);
        Case("Modulo widths", mixed, new Color(-1, -1, -3, 1));

        for (int mode = 0; mode < 3; mode++)
        {
            var n = Node(ShaderGraphNodeKind.RotateAboutAxis); n.option = mode;
            Set(n, "In", new Vector4(2, 0, 0, 0));
            Set(n, "Axis", new Vector4(0, 0, 3, 0));
            Scalar(n, "Rotation", mode == 0 ? Mathf.PI / 2 : mode == 1 ? 90 : .25f);
            Case("Rotation/" + mode, n, new Color(0, 2, 0, 1));
            var tilted = Node(ShaderGraphNodeKind.RotateAboutAxis); tilted.option = mode;
            var axis = new Vector3(1, -2, 3);
            var value = new Vector3(-.2f, .5f, .7f);
            Set(tilted, "In", value); Set(tilted, "Axis", axis);
            Scalar(tilted, "Rotation", mode == 0 ? -.7f : mode == 1 ? -.7f * Mathf.Rad2Deg : -.7f / (2 * Mathf.PI));
            Case("Tilted rotation/" + mode, tilted, Vector(Quaternion.AngleAxis(-.7f * Mathf.Rad2Deg, axis) * value));
        }

        ShaderGraphNode refractForBoth = null;
        Color bothExpected = default;
        foreach (var pair in new[] { (1f, 1.5f, 0f), (1f, 1.5f, .6f), (1.5f, 1f, .2f), (1.5f, 1f, .9f), (.5f, .8f, .6f) })
        for (int mode = 0; mode < 2; mode++)
        {
            var n = Node(ShaderGraphNodeKind.Refract);
            Check(n.option == 1 && n.FindInput("IORSource").value.x == 1 && n.FindInput("IORMedium").value.x == 1.5f, "Refraction defaults");
            n.option = mode;
            float x = pair.Item3;
            var incident = new Vector3(x, 0, Mathf.Sqrt(1 - x * x));
            Set(n, "Incident", incident);
            Scalar(n, "IORSource", pair.Item1); Scalar(n, "IORMedium", pair.Item2);
            double from = Math.Max(pair.Item1, 1), to = Math.Max(pair.Item2, 1), eta = from / to, cos = incident.z;
            double k = 1 - eta * eta * (1 - cos * cos);
            Vector3 direction = k < 0 && mode == 0 ? new Vector3(incident.x, 0, -incident.z)
                : new Vector3((float)(eta * incident.x), 0, (float)Math.Sqrt(Math.Max(k, 0)));
            double intensity;
            if (from <= to)
            {
                double f0 = Math.Pow((to - from) / (to + from), 2);
                intensity = Math.Clamp((1 - f0) * (1 - Math.Pow(1 - cos, 5)), 0, 1);
            }
            else if (k < 0) intensity = mode == 0 ? 1 : 0;
            else
            {
                double cosT = Math.Sqrt(k);
                double rs = (from * cos - to * cosT) / (from * cos + to * cosT);
                double rp = (to * cos - from * cosT) / (to * cos + from * cosT);
                intensity = .5 * (rs * rs + rp * rp);
            }
            Case("Refract/" + pair + "/" + mode, n, Vector(direction), "Refracted");
            Case("Intensity/" + pair + "/" + mode, n, Gray((float)intensity), "Intensity");
            if (refractForBoth == null) { refractForBoth = n; bothExpected = Vector(direction); bothExpected.a = (float)intensity; }
        }
        var setAlpha = Node(ShaderGraphNodeKind.SetAlpha);
        graph.edges.Add(new ShaderGraphEdge { outputNode = refractForBoth.id, outputPort = "Refracted", inputNode = setAlpha.id, inputPort = "Color" });
        graph.edges.Add(new ShaderGraphEdge { outputNode = refractForBoth.id, outputPort = "Intensity", inputNode = setAlpha.id, inputPort = "Alpha" });
        Case("Both refraction outputs", setAlpha, bothExpected);
        var outputNode = Node(ShaderGraphNodeKind.Output);
        graph.edges.Add(new ShaderGraphEdge { outputNode = setAlpha.id, inputNode = outputNode.id, inputPort = "RGBA" });

        var document = ScriptableObject.CreateInstance<WhimTexDocument>();
        document.hideFlags = HideFlags.HideAndDontSave; document.width = document.height = 16;
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
        Call(typeof(Layer), layer, "AssignNewId"); document.layers.Add(layer);
        var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var previousActive = RenderTexture.active; bool previousSrgb = GL.sRGBWrite;
        target.Create();
        try
        {
            string json = WhimTexDocumentJson.Write(document).Json;
            using (var loaded = WhimTexDocumentJson.Read(json, false))
            {
                var saved = ((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph;
                Check(saved.nodes.Count == graph.nodes.Count && saved.edges.Count == graph.edges.Count, "JSON graph roundtrip");
                foreach (var node in graph.nodes)
                    Check(saved.FindNode(node.id).option == node.option, "Saved mode " + node.id);
                Check(((string[])Call(nodes, null, "Outputs", saved.FindNode(refractForBoth.id))).SequenceEqual(new[] { "Refracted", "Intensity" }), "Saved refraction outputs");
            }
            var requestType = Find("ShaderProgramRuntime+CompilationRequest");
            var vmType = Find("ShaderGraphPreviewVM");
            var context = Activator.CreateInstance(Find("LayerRenderContext"), new object[] { document, null, 16, 16, 1f, false, false, false });
            float maxError = 0, maxDifference = 0;
            async Task Until(Func<bool> condition, string label)
            {
                var timer = Stopwatch.StartNew();
                while (!condition()) { if (timer.Elapsed.TotalSeconds > 45) throw new TimeoutException(label); await Task.Delay(25); }
            }
            Color Read()
            {
                var active = RenderTexture.active;
                try { RenderTexture.active = target; pixel.ReadPixels(new Rect(3, 3, 1, 1), 0, 0); pixel.Apply(); return pixel.GetPixel(0, 0); }
                finally { RenderTexture.active = active; }
            }
            foreach (var c in cases)
            {
                using var request = (IDisposable)Activator.CreateInstance(requestType, BindingFlags.Instance | F, null,
                    new object[] { graph, c.node.id, c.port, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false }, null);
                object[] completed = { null };
                await Until(() => (bool)Call(requestType, request, "TryComplete", completed), c.label + " HLSL");
                using var runtime = (IDisposable)completed[0];
                var runtimeType = runtime.GetType();
                Call(runtimeType, runtime, "Bind", graph, context, null, true, false);
                Call(runtimeType, runtime, "PrepareCoordinates", layer, context);
                bool srgb = GL.sRGBWrite;
                try { GL.sRGBWrite = false; Call(runtimeType, runtime, "Render", Texture2D.blackTexture, target, context); }
                finally { GL.sRGBWrite = srgb; }
                Color native = Read();
                object[] build = { graph, c.node.id, c.port, null };
                Check((bool)Call(vmType, null, "TryCreate", build), c.label + " SVM build");
                using var vm = (IDisposable)build[3];
                await Until(() => (bool)Call(vmType, vm, "UpdatePreparation"), c.label + " SVM preparation");
                Check((bool)Call(vmType, vm, "Render", target, 0f), c.label + " SVM render");
                Color svm = Read();
                for (int channel = 0; channel < 4; channel++)
                {
                    float error = Mathf.Max(Mathf.Abs(native[channel] - c.expected[channel]), Mathf.Abs(svm[channel] - c.expected[channel]));
                    float difference = Mathf.Abs(native[channel] - svm[channel]);
                    Check(float.IsFinite(error) && error < 0.0005f, $"{c.label}[{channel}] expected {c.expected[channel]:R}, HLSL {native[channel]:R}, SVM {svm[channel]:R}");
                    Check(difference < 0.0001f, c.label + " HLSL/SVM difference " + difference);
                    maxError = Mathf.Max(maxError, error); maxDifference = Mathf.Max(maxDifference, difference);
                }
            }
            return new {
                status = "passed", checks = cases.Count, failures = Array.Empty<string>(),
                message = $"{cases.Count} GPU cases, HLSL/SVM and independent numeric expectations; mode/default/JSON checks. Max error {maxError:R}, max HLSL/SVM difference {maxDifference:R}."
            };
        }
        finally { RenderTexture.active = previousActive; GL.sRGBWrite = previousSrgb; Object.DestroyImmediate(pixel); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(document); }
    }
}
