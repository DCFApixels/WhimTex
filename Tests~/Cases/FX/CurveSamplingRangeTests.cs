using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DCFApixels.WhimTex;
using WhimTex.Tests.UnityC;

public static class CurveSamplingRangeTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type TypeOf(string n) => typeof(WhimTexDocument).Assembly.GetType("DCFApixels.WhimTex." + n);
    static object Get(object o, string n) => o.GetType().GetField(n, F)?.GetValue(o) ?? o.GetType().GetProperty(n, F)?.GetValue(o);
    static object Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, F).Invoke(o, a);
    static object Static(string type, string n, params object[] a) => TypeOf(type).GetMethod(n, F).Invoke(null, a);
    static ShaderGraphNode Node(ShaderGraphNodeKind kind) => (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
    static async Task Ready(object vm, CancellationToken token)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!(bool)Get(vm, "Ready"))
        {
            Call(vm, "UpdatePreparation");
            if ((bool)Get(vm, "Failed")) throw new InvalidOperationException("SVM curve executor failed");
            if (timer.Elapsed.TotalSeconds > 20) throw new TimeoutException("SVM curve executor");
            await Task.Delay(20, token);
        }
    }
    static float Pixel(RenderTexture image, Texture2D read)
    {
        var active = RenderTexture.active;
        try { RenderTexture.active = image; read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); return read.GetPixel(0, 0).r; }
        finally { RenderTexture.active = active; }
    }
    public static string Start(string id)
    {
        var job = AsyncFixture.Create(id);
        job.Worker = FixtureContext.RunAsync(job, async token =>
        {
            var t = job.Context; var scope = job.Scope;
            var doc = scope.Own(ScriptableObject.CreateInstance<WhimTexDocument>()); doc.width = doc.height = 16;
            var image = scope.Own(new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)); image.Create();
            var read = scope.Own(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
            var context = Activator.CreateInstance(TypeOf("LayerRenderContext"), F, null,
                new object[] { doc, null, 1, 1, 1f, false, false, false }, null);
            var graph = new ShaderGraph();
            var parameter = new ShaderParameter { name = "_Profile", type = ShaderParameterType.Curve, curveValue = AnimationCurve.Linear(0, 0, 1, 1) };
            graph.parameters.Add(parameter);
            var resource = Node(ShaderGraphNodeKind.Parameter); resource.parameterId = parameter.id;
            var sample = Node(ShaderGraphNodeKind.SampleCurve);
            sample.inputs.Add(new ShaderGraphInput { port = "T", type = ShaderGraphValueType.Float });
            var position = sample.FindInput("T");
            var output = Node(ShaderGraphNodeKind.Output);
            graph.nodes.AddRange(new[] { resource, sample, output });
            graph.edges.Add(new ShaderGraphEdge { outputNode = resource.id, inputNode = sample.id, inputPort = "Curve" });
            graph.edges.Add(new ShaderGraphEdge { outputNode = sample.id, inputNode = output.id, inputPort = "RGBA" });
            graph.Validate();
            using var native = (IDisposable)Static("ShaderProgramRuntime", "Create", graph, sample.id, "Out", false);
            var factory = typeof(ShaderFX).GetMethod("CreateAgentDraft", F, null,
                new[] { typeof(WhimTexDocument), typeof(string), typeof(List<ShaderParameter>), typeof(string) }, null);
            var fx = scope.Own((ShaderFX)factory.Invoke(null, new object[] { doc,
                "// @param curve _Profile\n// @param float _T\nfloat4 ApplyFX(PinnedUV uv, float4 color) { return float4(_Profile_Sample(_T), 0, 0, 1); }",
                new List<ShaderParameter>(), null }));
            Call(fx, "CompileAgentDraft");
            t.True((bool)Get(fx, "HasWorkingShader") && !(bool)Get(fx, "LastCompileFailed"), "Curve HLSL compiles");
            var fxParameters = (List<ShaderParameter>)Get(fx, "parameters");
            var fxCurve = fxParameters.Find(p => p.name == "_Profile"); var fxT = fxParameters.Find(p => p.name == "_T");
            var fxRuntime = Get(fx, "runtime"); var originalShader = Get(fxRuntime, "Shader");
            var curves = new AnimationCurve[] {
                AnimationCurve.Linear(0, -2, 1, 3), AnimationCurve.Linear(-2, -4, 3, 6),
                new AnimationCurve(new Keyframe(-2, -3, 0, 3, .2f, .25f) { weightedMode = WeightedMode.Both },
                    new Keyframe(.4f, 2, 0, 0, .3f, .25f) { weightedMode = WeightedMode.Both }, new Keyframe(3, 5, 2, 0)),
                AnimationCurve.Linear(2, -1, 5, 4), new AnimationCurve(new Keyframe(2, 3)), new AnimationCurve(),
                AnimationCurve.Linear(-.0001f, -2, .0001f, 3)
            };
            float[] positions = { -8, -3, -2, -1, -.0001f, 0, .0001f, .4f, .5f, 1, 2, 3, 4, 5, 8 };
            int cases = 0;
            foreach (var curve in curves)
            foreach (var before in new[] { WrapMode.ClampForever, WrapMode.Loop, WrapMode.PingPong })
            foreach (var after in new[] { WrapMode.ClampForever, WrapMode.Loop, WrapMode.PingPong })
            {
                token.ThrowIfCancellationRequested();
                curve.preWrapMode = before; curve.postWrapMode = after;
                parameter.curveValue = fxCurve.curveValue = curve;
                position.value = Vector4.one * 17.123f;
                object[] args = { graph, sample.id, "Out", null };
                t.True((bool)Static("ShaderGraphPreviewVM", "TryCreate", args), "Sample Curve supports SVM");
                using var vm = (IDisposable)args[3]; await Ready(vm, token);
                foreach (float x in positions)
                {
                    position.value = Vector4.one * x; fxT.floatValue = x;
                    float expected = curve.Evaluate(x);
                    GL.sRGBWrite = false;
                    Call(native, "Bind", graph, context, null, false, false);
                    Call(native, "Render", Texture2D.blackTexture, image, context);
                    float a = Pixel(image, read);
                    Call(fxRuntime, "BindCode", fxParameters, context, true);
                    Call(fxRuntime, "Render", Texture2D.blackTexture, image, context);
                    float b = Pixel(image, read);
                    t.True((bool)Call(vm, "TryUpdateValues", graph, null), "Updating T retains the SVM executor");
                    t.True((bool)Call(vm, "Render", image, 0f), "SVM renders");
                    float c = Pixel(image, read);
                    string label = "curve " + cases + ", T=" + x + ", " + before + "/" + after;
                    // Existing 512-sample LUT approximates weighted curves between samples.
                    t.Near(expected, a, .015, "Native " + label);
                    t.Near(expected, b, .015, "HLSL " + label);
                    t.Near(expected, c, .015, "SVM " + label);
                    t.Near(a, c, .0005, "Native/SVM parity " + label);
                }
                t.True(ReferenceEquals(originalShader, Get(fxRuntime, "Shader")), "Range/Wrap edits do not recompile FX");
                cases++; await Task.Delay(1, token);
            }
            using var lut = (IDisposable)Activator.CreateInstance(TypeOf("WhimTexCurveTexture"), true);
            var extended = AnimationCurve.Linear(-2, -4, 3, 6);
            var tex = (Texture2D)Call(lut, "GetTexture", extended);
            t.Equal(TextureFormat.RFloat, tex.format, "Float curve storage");
            t.Near(-4, tex.GetPixel(0, 0).r, .00001, "First key outside 0..1 is stored");
            t.Near(6, tex.GetPixel(511, 0).r, .00001, "Last key outside 0..1 is stored");
            int bakes = (int)Get(lut, "BakeCount"); Call(lut, "GetTexture", extended);
            t.Equal(bakes, (int)Get(lut, "BakeCount"), "Unchanged LUT reused");
            tex = (Texture2D)Call(lut, "GetNormalizedTexture", extended);
            t.Near(extended.Evaluate(0), tex.GetPixel(0, 0).r, .00001, "SDF/pattern normalized profile remains unchanged");
            t.Near(extended.Evaluate(1), tex.GetPixel(511, 0).r, .00001, "Normalized profile end");
            tex = (Texture2D)Call(lut, "GetTexture", (object)null);
            t.Near(1, tex.GetPixel(511, 0).r, .00001, "Null curve keeps the identity default");
        });
        return job.Read();
    }
    public static string Poll(string id) => AsyncFixture.Poll(id);
    public static Task<string> Cancel(string id) => AsyncFixture.Cancel(id);
    public static Task<string> Cleanup(string id) => AsyncFixture.Cleanup(id);
}
