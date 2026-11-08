using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests.UnityD;

public static partial class ShaderGraphEditingBench
{
    static object LightingCompilation(ShaderGraph graph, string node, bool simplified, bool effect = false)
        => Call(Static("ShaderGraphCompiler", "Prepare", graph, effect), "Compile", node, "Out", simplified);
    static string PrepareLighting(object compilation, bool effect = false)
    {
        object[] args = { compilation, true, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", effect, null };
        return (string)Static("ShaderExecutableCache", "Prepare", args);
    }
    static ShaderGraphNode LightingNode(ShaderGraph graph, ShaderGraphNodeKind kind)
    {
        var node = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, new Vector2(graph.nodes.Count * 250, 0));
        graph.nodes.Add(node); return node;
    }
    static void LightingConnect(ShaderGraph graph, string source, string output, string target, string input)
    { graph.edges.RemoveAll(e => e.inputNode == target && e.inputPort == input); Connect(graph, source, output, target, input); }
    static int LightingMessages(object compilation) => ((IEnumerable)Get(compilation, "messages")).Cast<object>().Count();
    static (ShaderGraph graph, ShaderGraphNode light, ShaderGraphNode output) LightingGraph()
    {
        var graph = new ShaderGraph();
        var light = LightingNode(graph, ShaderGraphNodeKind.NormalLighting); light.option = 1;
        var output = LightingNode(graph, ShaderGraphNodeKind.Output);
        LightingConnect(graph, light.id, "Out", output.id, "RGBA");
        return (graph, light, output);
    }
    public static string PreviewLightingChecks(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        var (graph, light, output) = LightingGraph();
        object Full() => LightingCompilation(graph, null, false);
        var original = Full(); string source = PrepareLighting(original);
        t.Equal(source, PrepareLighting(LightingCompilation(graph, null, true)), "Final ignores simplified preview policy");
        t.True((bool)Get(original, "usesLighting"), "Final keeps document lighting");
        var simple = LightingCompilation(graph, light.id, true);
        t.True(!PrepareLighting(simple).Contains("WTGlobalLighting("), "Simple preview omits global evaluator");
        t.True(!(bool)Get(simple, "usesLighting"), "Simple preview needs no document lighting variants");
        t.Equal(0, LightingMessages(original), "One Global node needs no Info");

        var second = LightingNode(graph, ShaderGraphNodeKind.NormalLighting); second.option = 1;
        t.Equal(0, LightingMessages(Full()), "Disconnected lighting is excluded");
        var add = LightingNode(graph, ShaderGraphNodeKind.Add);
        LightingConnect(graph, light.id, "Out", add.id, "A"); LightingConnect(graph, light.id, "Out", add.id, "B");
        LightingConnect(graph, add.id, "Out", output.id, "RGBA");
        t.Equal(0, LightingMessages(Full()), "Fan-out of one evaluation needs no Info");
        LightingConnect(graph, second.id, "Out", add.id, "B");
        t.Equal(2, LightingMessages(Full()), "Two active Global nodes receive Info");
        foreach (object message in (IEnumerable)Get(Full(), "messages"))
            t.Equal("Info", Get(message, "level").ToString(), "Advisory is informational");
        second.option = 0;
        t.Equal(0, LightingMessages(Full()), "Local is excluded from repeated document lighting");
        second.option = 1;
        var buffer = LightingNode(graph, ShaderGraphNodeKind.Buffer);
        LightingConnect(graph, light.id, "Out", buffer.id, "In"); LightingConnect(graph, buffer.id, "Out", add.id, "A");
        var split = Full();
        t.Equal(0, LightingMessages(split), "Separate passes are counted separately");
        t.Equal(1, ((IEnumerable)Get(split, "buffers")).Cast<object>().Count(), "Buffer remains a separate pass");
        var buffered = LightingCompilation(graph, add.id, true); PrepareLighting(buffered);
        t.True(!(bool)Get(buffered, "usesLighting"), "Downstream simple preview is simplified");
        foreach (object pass in (IEnumerable)Get(buffered, "buffers"))
            t.True(!(bool)Get(Get(pass, "program"), "usesLighting"), "Buffer producer inherits simple preview lighting");
        var accurate = LightingCompilation(graph, add.id, false); PrepareLighting(accurate);
        foreach (object pass in (IEnumerable)Get(accurate, "buffers"))
            t.True((bool)Get(Get(pass, "program"), "usesLighting"), "Accurate Buffer producer keeps document lighting");
        t.Equal(PrepareLighting(Full()), PrepareLighting(LightingCompilation(graph, null, true)), "Buffer final is unchanged");
        var effect = LightingCompilation(graph, add.id, true, true); PrepareLighting(effect, true);
        t.True(!(bool)Get(effect, "usesLighting"), "FX node preview also simplifies lighting");
        light.option = second.option = 0;
        t.Equal(PrepareLighting(LightingCompilation(graph, add.id, false)), PrepareLighting(LightingCompilation(graph, add.id, true)), "Local preview is identical in both modes");

        light.option = second.option = 1;
        var state = Activator.CreateInstance(TypeOf("ShaderGraphPreviewState"), true); Call(state, "Prepare", graph);
        ulong finalStamp = (ulong)Call(state, "Fingerprint", output.id);
        t.True((ulong)Call(state, "PreviewFingerprint", add.id, true) != (ulong)Call(state, "PreviewFingerprint", add.id, false), "Downstream preview cache distinguishes lighting");
        t.Equal(finalStamp, (ulong)Call(state, "Fingerprint", output.id), "Output stamp excludes editor preference");
        var independent = LightingNode(graph, ShaderGraphNodeKind.UV); Call(state, "Prepare", graph);
        t.Equal((ulong)Call(state, "PreviewFingerprint", independent.id, true), (ulong)Call(state, "PreviewFingerprint", independent.id, false), "Unrelated preview keeps its cache");

        using (var renderFixture = new Fixture())
        {
            var sample = LightingGraph();
            sample.light.normalEncoding = ShaderGraphNormalEncoding.SignedXYZ;
            void Input(string name, ShaderGraphValueType type, Vector4 value)
            {
                sample.light.inputs.RemoveAll(x => x.port == name);
                sample.light.inputs.Add(new ShaderGraphInput { port = name, type = type, value = value, source = ShaderGraphInputSource.Value });
            }
            Input("Normal", ShaderGraphValueType.Float3, new Vector4(.3f, -.2f, .93f, 0));
            Input("Color", ShaderGraphValueType.Color, new Vector4(-.2f, .6f, 1.4f, .35f));
            Input("SpecularIntensity", ShaderGraphValueType.Float, new Vector4(.55f, 0, 0, 0));
            Input("Roughness", ShaderGraphValueType.Float, new Vector4(.3f, 0, 0, 0));
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true); target.Create();
            var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
            object program = null, request = null; Color[] expected = null;
            try
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    if (mode == 1)
                    {
                        sample.light.option = 0;
                        Input("LightDirection", ShaderGraphValueType.Float3, new Vector4(-.5f, .5f, .7071067812f, 0));
                    }
                    var compilation = LightingCompilation(sample.graph, sample.light.id, mode == 0);
                    request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new object[] { sample.graph, compilation, Stamp(sample.graph), "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, false, false, null, Activator.CreateInstance(TypeOf("ShaderSourceAnalysis")) }, null);
                    var timer = Stopwatch.StartNew();
                    while (true)
                    {
                        object[] done = { null };
                        if ((bool)Call(request, "TryComplete", done)) { program = done[0]; break; }
                        if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException("Simple lighting render");
                        await NextUpdate(token);
                    }
                    ((IDisposable)request).Dispose(); request = null;
                    var bindings = Activator.CreateInstance(TypeOf("ShaderGraphBindingState"), true); Call(bindings, "Prepare", sample.graph);
                    var context = Activator.CreateInstance(TypeOf("LayerRenderContext"), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null, new object[] { renderFixture.document, null, 16, 16, 32f, false, false, false }, null);
                    Call(program, "Bind", sample.graph, context, bindings, true, false);
                    Call(program, "PrepareCoordinates", (Layer)renderFixture.behaviour, context);
                    GL.sRGBWrite = false; Call(program, "Render", Texture2D.blackTexture, target, context);
                    RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); readback.Apply();
                    var pixels = readback.GetPixels();
                    if (mode == 0) expected = pixels;
                    else
                    {
                        float error = 0;
                        for (int i = 0; i < pixels.Length; i++) for (int c = 0; c < 4; c++) error = Mathf.Max(error, Mathf.Abs(pixels[i][c] - expected[i][c]));
                        t.True(error < .00002f, "Fixed light matches existing Local evaluator with HDR, alpha and specular");
                    }
                    t.True(pixels.All(p => float.IsFinite(p.r) && float.IsFinite(p.g) && float.IsFinite(p.b)), "Lighting output is finite");
                    t.True(Mathf.Abs(pixels[0].a - .35f) < .00001f, "Preview keeps surface alpha");
                    ((IDisposable)program).Dispose(); program = null;
                }
            }
            finally
            {
                (request as IDisposable)?.Dispose(); (program as IDisposable)?.Dispose();
                RenderTexture.active = previous; GL.sRGBWrite = srgb;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        const string key = "DCFApixels.WhimTex.ShaderGraph.AccurateLighting";
        bool existed = EditorPrefs.HasKey(key), saved = EditorPrefs.GetBool(key, false);
        var setting = TypeOf("WhimTexUserSettings").GetProperty("AccurateGraphLighting", Flags);
        var focused = EditorWindow.focusedWindow;
        Fixture first = null, reopened = null;
        try
        {
            setting.SetValue(null, false);
            first = new Fixture(); first.behaviour.draftGraph = LightingGraph().graph; first.Show();
            var timer = Stopwatch.StartNew();
            while (!FinalReady(first, Stamp(first.behaviour.draftGraph)))
            { if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException("Settings fixture"); await NextUpdate(token); }
            string snapshot = Snapshot(first.behaviour.draftGraph);
            object runtime = Get(first.behaviour, "Runtime");
            var toggle = first.window.rootVisualElement.Q<ToolbarToggle>("accurate-lighting");
            t.True(toggle != null && !toggle.value, "Native toolbar toggle defaults to simplified");
            toggle.value = true; await NextUpdate(token);
            t.True(EditorPrefs.GetBool(key, false), "Preference is persisted");
            t.True((bool)Get(first.window, "accurateLighting"), "Window observes preference");
            t.Equal(snapshot, Snapshot(first.behaviour.draftGraph), "Switch does not edit document graph");
            t.True(ReferenceEquals(runtime, Get(first.behaviour, "Runtime")), "Switch retains final executable");
            async Task WaitPreview(bool lighting)
            {
                var clock = Stopwatch.StartNew();
                string id = first.behaviour.draftGraph.nodes.First(n => n.kind == ShaderGraphNodeKind.NormalLighting).id;
                while (true)
                {
                    var previews = (IDictionary)Get(Get(first.window, "canvas"), "previews");
                    object preview = previews[id];
                    if (preview != null && (bool)Get(preview, "renderError")) throw new InvalidOperationException("Lighting preview failed");
                    object compiled = preview == null ? null : Get(preview, "program");
                    if (compiled != null && Get(preview, "pending") == null && !(bool)Get(preview, "dirty") &&
                        Get(preview, "texture") != null && (bool)Get(compiled, "UsesLighting") == lighting) break;
                    if (clock.Elapsed.TotalSeconds > 60) throw new TimeoutException("Updated lighting image");
                    await NextUpdate(token);
                }
            }
            await WaitPreview(true);
            toggle.value = false; toggle.value = true; toggle.value = false;
            await WaitPreview(false);
            t.True(ReferenceEquals(runtime, Get(first.behaviour, "Runtime")), "Rapid preview toggles do not replace final executable");
            toggle.value = true; await WaitPreview(true);
            first.Dispose(); first = null;
            reopened = new Fixture(); reopened.behaviour.draftGraph = LightingGraph().graph; reopened.Show();
            await NextUpdate(token);
            t.True(reopened.window.rootVisualElement.Q<ToolbarToggle>("accurate-lighting").value, "Reopened window restores persisted preference");
            reopened.window.position = new Rect(50, 50, 680, 450);
            await NextUpdate(token); await NextUpdate(token);
            var control = reopened.window.rootVisualElement.Q<ToolbarToggle>("accurate-lighting");
            t.True(control.worldBound.xMin >= 0 && control.worldBound.xMax <= reopened.window.rootVisualElement.worldBound.xMax + 1, "Toggle fits minimum window width");
        }
        finally
        {
            first?.Dispose(); reopened?.Dispose(); setting.SetValue(null, saved);
            if (!existed) EditorPrefs.DeleteKey(key);
            if (focused != null) focused.Focus();
        }
    });

    public static string PreviewLightingTiming(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        string baseline = File.ReadAllText(MigrationD.ProjectPath("Temp/WhimTex/shader-cold/baseline.txt")).Trim();
        string dir = MigrationD.ProjectPath("Temp/WhimTex/preview-lighting/" + runId); Directory.CreateDirectory(dir);
        using var fixture = new Fixture(); var graph = fixture.behaviour.draftGraph;
        var rows = new List<object>();
        void Save() => File.WriteAllText(Path.Combine(dir, "measurements.json"), Json(new { baseline,
            unity = Application.unityVersion, graphics = SystemInfo.graphicsDeviceType.ToString(), device = SystemInfo.graphicsDeviceName,
            protocol = "Serial native compilation; unique fragment entry per submission; active document lighting variants; alternating order; driver cache retained. Main plus required Buffer programs timed separately from rendering.", rows }));
        string Prepare(object compilation) => PrepareLighting(compilation);
        var final = LightingCompilation(graph, null, true);
        t.Equal(File.ReadAllText(Path.Combine(baseline, "final-original.shader.txt")), Prepare(final), "Actual output source remains exactly pre-change source");
        int bi = 0;
        foreach (object pass in (IEnumerable)Get(final, "buffers"))
            t.Equal(File.ReadAllText(Path.Combine(baseline, "final-original-buffer" + bi++ + ".shader.txt")), (string)Get(pass, "preparedSource"), "Final buffer source unchanged");
        var nodes = graph.nodes.Where(n => n.kind == ShaderGraphNodeKind.NormalLighting).ToArray();
        var downstream = LightingNode(graph, ShaderGraphNodeKind.Add);
        LightingConnect(graph, nodes[0].id, "Out", downstream.id, "A"); LightingConnect(graph, nodes[1].id, "Out", downstream.id, "B");
        foreach (var node in nodes.Concat(new[] { downstream }))
        {
            object[] variants = { LightingCompilation(graph, node.id, false), LightingCompilation(graph, node.id, true) };
            string[] sources = variants.Select(Prepare).ToArray();
            if (node.kind == ShaderGraphNodeKind.NormalLighting)
                t.Equal(File.ReadAllText(Path.Combine(baseline, "branch-NormalLighting-" + node.id.Substring(0, 5) + ".shader.txt")), sources[0], "Accurate node preview source matches before");
            for (int repetition = 0; repetition < 3; repetition++) for (int order = 0; order < 2; order++)
            {
                int mode = (order + repetition) % 2;
                var programs = new List<(string source, bool lighting)> { (sources[mode], (bool)Get(variants[mode], "usesLighting")) };
                foreach (object pass in (IEnumerable)Get(variants[mode], "buffers"))
                    programs.Add(((string)Get(pass, "preparedSource"), (bool)Get(Get(pass, "program"), "usesLighting")));
                double total = 0; var parts = new List<double>();
                foreach (var part in programs)
                {
                    var idle = Stopwatch.StartNew();
                    while (ShaderUtil.anythingCompiling || Counter("ActiveCount") != 0)
                    { if (idle.Elapsed.TotalSeconds > 60) throw new TimeoutException("Other work did not settle"); await NextUpdate(token); }
                    string salted = part.source.Replace("SpriteFXFragment", "WTPreviewLighting_" + Guid.NewGuid().ToString("N"));
                    Shader shader = null; Material material = null; var timer = Stopwatch.StartNew();
                    try
                    {
                        bool old = ShaderUtil.allowAsyncCompilation;
                        try
                        {
                            ShaderUtil.allowAsyncCompilation = true;
                            shader = ShaderUtil.CreateShaderAsset(salted, false); shader.hideFlags = HideFlags.HideAndDontSave;
                            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                            if (part.lighting) Static("WhimTexDocument", "SetLightingVariant", material, Get(fixture.document, "LightingVariant"));
                            ShaderUtil.CompilePass(material, 0, false);
                        }
                        finally { ShaderUtil.allowAsyncCompilation = old; }
                        while (!ShaderUtil.IsPassCompiled(material, 0))
                        {
                            if (ShaderUtil.ShaderHasError(shader) || timer.Elapsed.TotalSeconds > 60)
                                throw new InvalidOperationException(string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
                            await NextUpdate(token);
                        }
                        t.True(!ShaderUtil.ShaderHasError(shader), "Native preview compiles");
                        total += timer.Elapsed.TotalMilliseconds; parts.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    finally
                    { if (material != null) UnityEngine.Object.DestroyImmediate(material); if (shader != null) UnityEngine.Object.DestroyImmediate(shader); }
                }
                rows.Add(new { node = node.id, kind = node.kind.ToString(), simplified = mode == 1, repetition, milliseconds = total, parts, sourceChars = sources[mode].Length }); Save();
            }
        }
        Save();
    });
}
