using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using WhimTex.Tests;
using Object = UnityEngine.Object;

public static class CellularAxisJitterTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    const string Root = "Temp/WhimTex/CellularAxisJitter/";
    const string Fixtures = "Packages/com.dcfapixels.whimtex/Tests~/Fixtures/CellularAxisJitter/";
    const int Size = 16;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) =>
        type.GetMethods(F).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object target, string name) => target.GetType().GetProperty(name, F)?.GetValue(target)
        ?? target.GetType().GetField(name, F)?.GetValue(target);
    static object Static(string type, string method, params object[] args) => Call(T(type), null, method, args);
    sealed class Example
    {
        internal ShaderGraph graph = new ShaderGraph();
        internal ShaderGraphNode noise, root;
        internal string output, label;
        internal bool baseline, zeroAxis;
        internal Vector3 jitter;
        internal int dimensions;
    }
    static void Set(ShaderGraphNode node, string port, Vector4 value, ShaderGraphValueType type = ShaderGraphValueType.Float)
    {
        var input = node.FindInput(port);
        if (input == null) { input = new ShaderGraphInput { port = port }; node.inputs.Add(input); }
        input.type = type; input.value = value;
    }
    static List<Example> Cases(int suite)
    {
        int dimensions = suite == 3 ? 2 : suite + 1;
        var kind = suite == 3 ? ShaderGraphNodeKind.TriangleVoronoiNoise : ShaderGraphNodeKind.CellularNoise;
        var result = new List<Example>();
        void Add(int distribution, int variant, Vector3 jitter, string output, bool baseline, bool fractal = false, bool zeroAxis = false)
        {
            var e = new Example { dimensions = dimensions, jitter = jitter, baseline = baseline, zeroAxis = zeroAxis,
                label = distribution + "-" + variant + "-" + output + "-" + result.Count };
            var n = e.noise = e.root = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
            n.noiseDimensions = (ShaderGraphNoiseDimensions)(dimensions - 1);
            n.noiseDistribution = (ShaderGraphNoiseDistribution)distribution;
            n.noiseFiltering = variant >= 2; n.noisePeriodic = variant == 1 || variant == 2;
            n.noiseOutput = variant switch { 1 => ShaderGraphNoiseOutput.DistanceToEdge, 2 => ShaderGraphNoiseOutput.CellValue,
                3 => ShaderGraphNoiseOutput.Divide, _ => ShaderGraphNoiseOutput.F1 };
            n.noiseMetric = (ShaderGraphNoiseMetric)(variant % 4);
            e.graph.nodes.Add(n);
            var type = (ShaderGraphValueType)(dimensions - 1);
            Set(n, "Jitter", jitter, type);
            Set(n, "Evolution", Vector4.one * (variant == 0 && !zeroAxis ? 0 : variant <= 1 ? .73f : -.43f));
            Set(n, "Scale", Vector4.one * (zeroAxis ? 4 : n.noiseFiltering ? 12 : 5), type);
            Set(n, "Period", new Vector4(1.25f, .75f, 1.5f, 0), type);
            Set(n, "Offset", Vector4.zero, type);
            if (suite != 3) Set(n, "Smoothness", Vector4.one * (variant == 0 ? 0 : .35f));
            if (suite == 3) Set(n, "Stretch", new Vector4(.6f, 1.4f, 0, 0), ShaderGraphValueType.Float2);
            e.output = output;
            if (fractal)
            {
                var f = e.root = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", ShaderGraphNodeKind.Fractal, Vector2.zero);
                e.graph.nodes.Add(f); Set(f, "Octaves", Vector4.one * 2);
                e.graph.edges.Add(new ShaderGraphEdge { outputNode = n.id, outputPort = output, inputNode = f.id, inputPort = "In" });
                e.output = "Out";
            }
            var end = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", ShaderGraphNodeKind.Output, Vector2.zero);
            e.graph.nodes.Add(end);
            e.graph.edges.Add(new ShaderGraphEdge { outputNode = e.root.id, outputPort = e.output, inputNode = end.id, inputPort = "RGBA" });
            result.Add(e);
        }
        foreach (int distribution in new[] { 0, 1 })
        {
            for (int variant = 0; variant < 4; variant++)
                Add(distribution, variant, Vector3.one * (variant == 0 ? 0 : variant == 2 ? 1 : .37f), "Value", true);
            foreach (string output in suite == 3 ? new[] { "Distance", "CellValue", "ToPoint", "BorderNormal" } :
                new[] { "F1", "F2", "CellValue", "ToPoint" })
                Add(distribution, 1, Vector3.one * .63f, output, true);
            Add(distribution, 2, Vector3.one * .41f, "Value", true, true);
            var axes = dimensions == 1 ? new[] { new Vector3(.23f, 0, 1), new Vector3(0, 1, 1) } :
                dimensions == 2 ? new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(.2f, .8f, 0) } :
                new[] { new Vector3(1, 0, 0), new Vector3(0, 1, .3f), new Vector3(.1f, .6f, 1) };
            for (int i = 0; i < axes.Length; i++)
            {
                Add(distribution, i + 1, axes[i], "Value", false);
                Add(distribution, 0, axes[i], "ToPoint", false, zeroAxis: true);
            }
            Add(distribution, 2, axes[0], "Value", false, true);
        }
        foreach (bool scalar in dimensions == 1 ? new[] { true } : new[] { true, false })
        {
            Add(0, 2, Vector3.one * .42f, "Value", false);
            var e = result[result.Count - 1];
            var source = (ShaderGraphNode)Static("ShaderGraphNodes", "Create",
                scalar ? ShaderGraphNodeKind.Add : ShaderGraphNodeKind.UV, Vector2.zero);
            if (scalar) Set(source, "A", Vector4.one * .42f);
            e.graph.nodes.Add(source);
            e.graph.edges.Add(new ShaderGraphEdge { outputNode = source.id, inputNode = e.noise.id, inputPort = "Jitter" });
        }
        foreach (int distribution in new[] { 0, 1 })
            Add(distribution, 1, new Vector3(.2f, .8f, .4f), "ToPoint", false);
        return result;
    }
    static void Diagnostics(TestContext t, IEnumerable messages, string label)
    {
        foreach (var message in messages)
            t.True(Get(message, "Severity").ToString() is not ("Warning" or "Error"), label + ": " + message);
    }
    static Color[] Load(string path)
    {
        string relative = path.Substring(Fixtures.Length);
        var match = Regex.Match(File.ReadAllText(Fixtures + "manifest.json"),
            "\"path\"\\s*:\\s*\"" + Regex.Escape(relative) + "\"\\s*,\\s*\"sha256\"\\s*:\\s*\"([a-f0-9]{64})\"");
        if (!match.Success) throw new InvalidOperationException("Unregistered frozen baseline: " + relative);
        string hash = match.Groups[1].Value;
        using (var sha = SHA256.Create())
            if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() != hash)
                throw new InvalidOperationException("Frozen baseline hash mismatch: " + relative);
        using var reader = new BinaryReader(File.OpenRead(path));
        var result = new Color[Size * Size];
        for (int i = 0; i < result.Length; i++) result[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return result;
    }
    public static Task<object> Run(int suite, int part) => Execute(suite, part);
    public static async Task<object> Prepare(int suite)
    {
        Directory.CreateDirectory(Root);
        var t = new TestContext(); var timer = Stopwatch.StartNew(); var variants = new HashSet<string>();
        try
        {
            foreach (var e in Cases(suite))
            {
                object[] build = { e.graph, e.root.id, e.output, null };
                t.True((bool)Static("ShaderGraphPreviewVM", "TryCreate", build), "Prepare pure SVM");
                using var vm = (IDisposable)build[3];
                bool ready = (bool)Call(vm.GetType(), vm, "UpdatePreparation");
                var material = (Material)Get(vm, "Material");
                string key = string.Join("/", material.shaderKeywords.OrderBy(s => s));
                File.WriteAllText(Root + "warm.json", key + "\n" + e.label);
                if (!variants.Add(key)) continue;
                while (!ready)
                {
                    t.True(!(bool)Get(vm, "Failed"), "SVM variant preparation");
                    if (timer.Elapsed.TotalSeconds > 174) throw new TimeoutException("Cold SVM variant preparation");
                    await Task.Delay(25); ready = (bool)Call(vm.GetType(), vm, "UpdatePreparation");
                }
                Diagnostics(t, (IEnumerable)Static("ShaderDiagnostics", "ReadCompiler", material.shader), "Prepared variant");
            }
            return new TestResult { status = "passed", checks = t.Checks, failures = Array.Empty<string>(),
                message = "Prepared " + variants.Count + " cold SVM variants for suite " + suite };
        }
        catch (Exception error)
        {
            return new TestResult { status = "failed", checks = t.Checks, failures = new[] { error.ToString() },
                message = "Cold SVM preparation suite " + suite };
        }
    }
    static async Task<object> Execute(int suite, int part)
    {
        if (suite < 0 || suite > 3) throw new ArgumentOutOfRangeException(nameof(suite));
        Directory.CreateDirectory(Root);
        var t = new TestContext(); var cases = Cases(suite);
        if (part < 0 || part > 3) throw new ArgumentOutOfRangeException(nameof(part));
        string folder = Fixtures + "baseline-" + suite + "/";
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave;
        doc.width = doc.height = Size;
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph };
        Call(typeof(Layer), layer, "AssignNewId"); doc.layers.Add(layer);
        var owner = (ShaderLayerBehaviour)layer.Behaviour;
        var context = Activator.CreateInstance(T("LayerRenderContext"), new object[] { doc, null, Size, Size, 1f, false, false, false });
        var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var cpu = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
        var active = RenderTexture.active; bool srgb = GL.sRGBWrite;
        float maxParity = 0, maxBaseline = 0; int count = 0;
        var timer = Stopwatch.StartNew();
        async Task Wait(Func<bool> ready)
        {
            while (!ready()) { if (timer.Elapsed.TotalSeconds > 54) throw new TimeoutException("Axis jitter GPU budget"); await Task.Delay(15); }
        }
        Color[] Pixels()
        {
            RenderTexture.active = target; cpu.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); cpu.Apply(); return cpu.GetPixels();
        }
        try
        {
            t.True(target.Create(), "Float target");
            foreach (var e in cases.Where((e, index) => index % 4 == part))
            {
                File.WriteAllText(Root + "progress.json", JsonUtility.ToJson(new Progress { suite = suite, part = part, count = count, label = e.label, stage = "native" }));
                owner.draftGraph = e.graph;
                using var request = (IDisposable)Activator.CreateInstance(T("ShaderProgramRuntime+CompilationRequest"), F, null,
                    new object[] { e.graph, e.root.id, e.output, false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                object[] completed = { null };
                await Wait(() => (bool)Call(request.GetType(), request, "TryComplete", completed));
                using var runtime = (IDisposable)completed[0];
                Diagnostics(t, (IEnumerable)Get(runtime, "DiagnosticMessages"), e.label + " HLSL");
                Call(runtime.GetType(), runtime, "Bind", e.graph, context, null, true, false);
                Call(runtime.GetType(), runtime, "PrepareCoordinates", layer, context);
                GL.sRGBWrite = false; Call(runtime.GetType(), runtime, "Render", Texture2D.blackTexture, target, context);
                var native = Pixels(); count++;
                string file = folder + e.label + ".bin";
                object[] build = { e.graph, e.root.id, e.output, null };
                t.True((bool)Static("ShaderGraphPreviewVM", "TryCreate", build), e.label + " pure SVM");
                using var vm = (IDisposable)build[3];
                File.WriteAllText(Root + "progress.json", JsonUtility.ToJson(new Progress { suite = suite, part = part, count = count, label = e.label, stage = "SVM" }));
                await Wait(() => {
                    bool ready = (bool)Call(vm.GetType(), vm, "UpdatePreparation");
                    t.True(!(bool)Get(vm, "Failed"), e.label + " SVM preparation"); return ready;
                });
                t.True((bool)Call(vm.GetType(), vm, "Render", target, 0f), e.label + " SVM render");
                Diagnostics(t, (IEnumerable)Static("ShaderDiagnostics", "ReadCompiler", ((Material)Get(vm, "Material")).shader), e.label + " SVM");
                var actual = Pixels(); var previous = e.baseline ? Load(file) : null;
                for (int i = 0; i < actual.Length; i++) for (int channel = 0; channel < 4; channel++)
                {
                    float a = actual[i][channel], b = native[i][channel];
                    t.True(float.IsFinite(a) && float.IsFinite(b), e.label + " finite");
                    maxParity = Mathf.Max(maxParity, Mathf.Abs(a - b));
                    t.Near(b, a, .00015, e.label + " HLSL/SVM " + i + "/" + channel);
                    if (previous != null)
                    {
                        maxBaseline = Mathf.Max(maxBaseline, Mathf.Abs(b - previous[i][channel]));
                        t.Near(previous[i][channel], b, .00015, e.label + " retained scalar result " + i + "/" + channel);
                    }
                }
                if (e.zeroAxis)
                {
                    for (int axis = 0; axis < e.dimensions; axis++) if (e.jitter[axis] == 0)
                    for (int i = 0; i < native.Length; i++)
                    {
                        float p = axis == 0 ? (i % Size + .5f) / Size * 4 : axis == 1 ? (i / Size + .5f) / Size * 4 : 0;
                        float feature = p + native[i][axis] - (e.noise.noiseDistribution == ShaderGraphNoiseDistribution.Uniform ? .5f : 0);
                        t.Near(Mathf.Round(feature), feature, .00015, e.label + " zero axis anchors feature");
                    }
                }
                if (e.noise.noisePeriodic && e.output == "ToPoint")
                {
                    var offset = e.noise.FindInput("Offset"); Vector4 before = offset.value;
                    Vector4 period = e.noise.FindInput("Period").value;
                    offset.value = new Vector4(period.x, -period.y, 2 * period.z, 0);
                    try
                    {
                        Call(runtime.GetType(), runtime, "Bind", e.graph, context, null, true, false);
                        Call(runtime.GetType(), runtime, "PrepareCoordinates", layer, context);
                        GL.sRGBWrite = false; Call(runtime.GetType(), runtime, "Render", Texture2D.blackTexture, target, context);
                        var shifted = Pixels();
                        for (int i = 0; i < shifted.Length; i++) for (int channel = 0; channel < 4; channel++)
                            t.Near(native[i][channel], shifted[i][channel], .0002, e.label + " positive/negative period translation");
                    }
                    finally { offset.value = before; }
                }
                using var loaded = WhimTexDocumentJson.Read(WhimTexDocumentJson.Write(doc).Json, false);
                var restored = ((ShaderLayerBehaviour)loaded.Document.layers[0].Behaviour).draftGraph.FindNode(e.noise.id);
                t.Equal(e.noise.FindInput("Jitter").value, restored.FindInput("Jitter").value, e.label + " JSON components");
            }
            return new TestResult { status = "passed", checks = t.Checks, failures = Array.Empty<string>(),
                message = "Axis jitter native/SVM, scalar baseline, zero-axis geometry, JSON and sampled Fractal. " +
                    "Suite=" + suite + ", part=" + part + ", cases=" + count + ", maxParity=" + maxParity.ToString("R") + ", maxBaseline=" + maxBaseline.ToString("R") };
        }
        catch (Exception error)
        {
            return new TestResult { status = "failed", checks = t.Checks, failures = new[] { error.ToString() },
                message = "Axis jitter suite=" + suite + ", part=" + part + ", completed native cases=" + count +
                    ", maxParity=" + maxParity.ToString("R") + ", maxBaseline=" + maxBaseline.ToString("R") };
        }
        finally
        {
            RenderTexture.active = active; GL.sRGBWrite = srgb; target.Release();
            Object.DestroyImmediate(cpu); Object.DestroyImmediate(target); Object.DestroyImmediate(doc);
        }
    }
    [Serializable] sealed class Progress { public int suite, part, count; public string label, stage; }
}
