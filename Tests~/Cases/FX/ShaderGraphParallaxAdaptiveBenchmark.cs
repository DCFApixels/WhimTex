using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Opt-in experiment: caller supplies frozen graph and algorithm candidates under Temp.
// No user document, project asset or preview window is modified.
public static class ShaderGraphParallaxAdaptiveBenchmark
{
    const string Dir = "Temp/WhimTex/PomStudy/";
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const int Size = 512;
    static Type T(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) => type.GetMethods(Flags)
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
    static object Get(object o, string name) => o.GetType().GetProperty(name, Flags)?.GetValue(o) ?? o.GetType().GetField(name, Flags)?.GetValue(o);
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    static string ReplaceLibrary(string source, string library)
    {
        const string include = "#include \"Packages/com.dcfapixels.whimtex/src/Shaders/GraphParallax.hlsl\"";
        if (source.Contains(include)) return source.Replace(include, library);
        // Preparation hoists struct declarations above the expanded functions.
        source = Regex.Replace(source, @"struct\s+GraphParallaxTrace\s*\{[^}]*\}\s*;", "");
        int start = source.IndexOf("bool GraphParallaxFinite(float value)", StringComparison.Ordinal);
        int function = source.IndexOf("bool GraphParallaxAdvance(", StringComparison.Ordinal);
        Check(start >= 0 && function > start, "Parallax library location in prepared shader");
        int brace = source.IndexOf('{', function), end = brace + 1, depth = 1;
        for (; depth > 0 && end < source.Length; end++) { if (source[end] == '{') depth++; if (source[end] == '}') depth--; }
        Check(depth == 0, "Parallax function end");
        return source.Substring(0, start) + library + source.Substring(end);
    }

    public static async Task<object> Run()
    {
        var graph = JsonUtility.FromJson<ShaderGraph>(File.ReadAllText(Dir + "current-graph.json"));
        var pom = graph.nodes.Single(n => n.kind == ShaderGraphNodeKind.ParallaxOcclusionMapping);
        var labels = new[] { "before", "adaptive-full", "candidate", "reference" };
        string[] libraries = labels.Select(n => File.ReadAllText(Dir + "GraphParallax." + n + ".hlsl")).ToArray();
        var doc = ScriptableObject.CreateInstance<WhimTexDocument>(); doc.hideFlags = HideFlags.HideAndDontSave; doc.width = doc.height = Size;
        Layer layer = new ShaderLayerBehaviour { mode = ShaderProgramMode.Graph, draftGraph = graph };
        Call(typeof(Layer), layer, "AssignNewId"); doc.layers.Add(layer);
        var context = Activator.CreateInstance(T("LayerRenderContext"), new object[] { doc, null, Size, Size, 1f, false, false, false });
        var owned = new List<Object>();
        var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create(); owned.Add(target);
        var fence = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); fence.Create(); owned.Add(fence);
        var pixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true); owned.Add(pixel);
        var readback = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true); owned.Add(readback);
        var prior = RenderTexture.active; bool srgb = GL.sRGBWrite;
        var rows = new List<object>();
        var snapshots = new List<Color[]>();
        var parity = new List<object>();
        try
        {
            for (int root = 0; root < 2; root++)
            {
                var requestType = T("ShaderProgramRuntime+CompilationRequest");
                using var request = (IDisposable)Activator.CreateInstance(requestType, Flags, null,
                    new object[] { graph, root == 0 ? pom.id : null, "Out", false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
                object[] completed = { null }; var deadline = Stopwatch.StartNew();
                while (!(bool)Call(requestType, request, "TryComplete", completed))
                { if (deadline.Elapsed.TotalSeconds > 50) throw new TimeoutException("Graph preparation"); await Task.Delay(25); }
                using var runtime = (IDisposable)completed[0]; var rt = runtime.GetType();
                Call(rt, runtime, "Bind", graph, context, null, true, false); Call(rt, runtime, "PrepareCoordinates", layer, context);
                var original = (Material)Get(runtime, "Material");
                string source = (string)Get(runtime, "ShaderSource");
                File.WriteAllText(Dir + (root == 0 ? "uv" : "final") + ".shader.txt", source);
                var materials = new Material[labels.Length];
                var captures = new List<Color[]>();
                for (int v = 0; v < labels.Length; v++)
                {
                    string variant = ReplaceLibrary(source, libraries[v]);
                    if (v == 3) variant = variant.Replace("<68;", "<1037;");
                    variant = Regex.Replace(variant, "Shader \\\"[^\\\"]+\\\"", "Shader \"Hidden/WhimTex/PomStudy/" + root + "/" + labels[v] + "\"");
                    var shader = ShaderUtil.CreateShaderAsset(variant); shader.hideFlags = HideFlags.HideAndDontSave; owned.Add(shader);
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; owned.Add(material);
                    material.CopyPropertiesFromMaterial(original); materials[v] = material;
                    bool wasAsync = ShaderUtil.allowAsyncCompilation;
                    try { ShaderUtil.allowAsyncCompilation = true; ShaderUtil.CompilePass(material, 0, false); }
                    finally { ShaderUtil.allowAsyncCompilation = wasAsync; }
                    deadline.Restart();
                    while (!ShaderUtil.IsPassCompiled(material, 0))
                    { if (deadline.Elapsed.TotalSeconds > 50) throw new TimeoutException("Candidate compilation " + labels[v]); await Task.Delay(25); }
                    var messages = ((System.Collections.IEnumerable)Call(T("ShaderDiagnostics"), null, "ReadCompiler", shader)).Cast<object>().ToArray();
                    Check(messages.Length == 0, labels[v] + ": " + string.Join("; ", messages.Select(m => m.ToString())));
                    GL.sRGBWrite = false; Graphics.Blit(Texture2D.blackTexture, target, material, 0);
                    RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
                    var colors = readback.GetPixels();
                    captures.Add(colors);
                    Check(colors.All(c => float.IsFinite(c.r) && float.IsFinite(c.g) && float.IsFinite(c.b)), "Finite pixels " + labels[v]);
                    if (root == 0) snapshots.Add(colors);
                    else
                    {
                        var png = new Texture2D(Size, Size, TextureFormat.RGB24, false, true); owned.Add(png);
                        png.SetPixels(colors); png.Apply(); File.WriteAllBytes(Dir + labels[v] + ".png", png.EncodeToPNG());
                    }
                }
                // The production path must match the selected candidate, including VM frame replay.
                object[] build = { graph, root == 0 ? pom.id : null, "Out", null };
                var vmType = T("ShaderGraphPreviewVM");
                Check((bool)Call(vmType, null, "TryCreate", build), "Current graph builds pure SVM");
                using (var vm = (IDisposable)build[3])
                {
                    Call(vmType, vm, "ConfigureHybrid", doc, layer, false, true); deadline.Restart();
                    while (!(bool)Call(vmType, vm, "UpdatePreparation"))
                    { Check(!(bool)Get(vm, "Failed"), "SVM preparation"); if (deadline.Elapsed.TotalSeconds > 50) throw new TimeoutException("SVM preparation"); await Task.Delay(25); }
                    Check((bool)Call(vmType, vm, "RenderBoundaries", target, context), "SVM render");
                    RenderTexture.active = target; readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); readback.Apply();
                    Color[] actual = readback.GetPixels(); float maxDifference = 0; double sumDifference = 0;
                    for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
                    {
                        float difference = Mathf.Abs(actual[i][c] - captures[2][i][c]);
                        Check(float.IsFinite(difference), "Finite SVM parity");
                        maxDifference = Mathf.Max(maxDifference, difference); sumDifference += difference;
                    }
                    Check(maxDifference < .0006f, "SVM/candidate parity: " + maxDifference);
                    parity.Add(new { root = root == 0 ? "UV" : "final", maxDifference, meanDifference = sumDifference / (actual.Length * 4) });
                }
                if (root == 0) continue;
                var comparison = new Texture2D(Size * 3, Size, TextureFormat.RGB24, false, true); owned.Add(comparison);
                int[] displayed = { 0, 2, 3 };
                for (int i = 0; i < displayed.Length; i++) comparison.SetPixels(i * Size, 0, Size, Size, captures[displayed[i]]);
                comparison.Apply(); File.WriteAllBytes(Dir + "comparison.png", comparison.EncodeToPNG());
                double Draw(int v, int repeats)
                {
                    var watch = Stopwatch.StartNew();
                    for (int k = 0; k < repeats; k++) Graphics.Blit(Texture2D.blackTexture, target, materials[v], 0);
                    Graphics.Blit(target, fence); RenderTexture.active = fence;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); pixel.Apply();
                    return watch.Elapsed.TotalMilliseconds / repeats;
                }
                for (int k = 0; k < 4; k++) for (int v = 0; v < 3; v++) Draw(v, 4);
                var times = new[] { new double[21], new double[21], new double[21] };
                for (int k = 0; k < 21; k++) for (int order = 0; order < 3; order++)
                { int v = (k + order) % 3; times[v][k] = Draw(v, 12); }
                for (int v = 0; v < 3; v++)
                {
                    double sum = 0, max = 0; int overPixel = 0;
                    for (int i = 0; i < snapshots[v].Length; i++)
                    {
                        double dx = snapshots[v][i].r - snapshots[3][i].r, dy = snapshots[v][i].g - snapshots[3][i].g;
                        double error = Math.Sqrt(dx * dx + dy * dy) * Size;
                        sum += error; max = Math.Max(max, error); if (error > 1) overPixel++;
                    }
                    var sorted = times[v].OrderBy(x => x).ToArray();
                    rows.Add(new { name = labels[v], medianMs = sorted[10], p10Ms = sorted[2], p90Ms = sorted[18], samples = times[v],
                        meanUvErrorPixels = sum / snapshots[v].Length, maxUvErrorPixels = max, overOnePixelPercent = 100.0 * overPixel / snapshots[v].Length });
                }
            }
            var report = new { status = "passed", checks = 8, gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(), size = Size,
                amplitude = pom.FindInput("Amplitude").value.x, direction = new[] { pom.FindInput("ViewDirection").value.x, pom.FindInput("ViewDirection").value.y, pom.FindInput("ViewDirection").value.z }, steps = pom.FindInput("Steps").value.x,
                protocol = "21 rotating batches of 12 actual graph draws, 4 warmups, GPU completion via 1x1 readback; CPU submission + synchronized GPU proxy, not timestamps. Quality: native procedural 1024-step reference, 12 refinements.", rows, parity };
            string json = (string)Type.GetType("Newtonsoft.Json.JsonConvert, Newtonsoft.Json", true).GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            File.WriteAllText(Dir + "gpu-comparison.json", json); return report;
        }
        finally
        {
            RenderTexture.active = prior; GL.sRGBWrite = srgb;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) { if (owned[i] is RenderTexture texture) texture.Release(); Object.DestroyImmediate(owned[i]); }
            Object.DestroyImmediate(doc);
        }
    }
}
