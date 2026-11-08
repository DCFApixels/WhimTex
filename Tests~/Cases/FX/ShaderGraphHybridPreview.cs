using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEditor;
using UnityEngine;
using WhimTex.Tests.UnityD;

public static partial class ShaderGraphEditingBench
{
    public static string HybridTiming(string runId, string label) => AsyncD.Start(runId, async (t, token) =>
    {
        var rows = new List<object>();
        var focus = EditorWindow.focusedWindow;
        string folder = MigrationD.ProjectPath("Temp/WhimTex/hybrid/" + label + "-" + runId);
        Directory.CreateDirectory(folder);
        try
        {
            for (int pass = 0; pass < 3; pass++)
            using (var fixture = new Fixture(true))
            {
                var drain = Stopwatch.StartNew();
                while (Counter("ActiveCount") != 0)
                { if (drain.Elapsed.TotalSeconds > 60) throw new TimeoutException("Previous native jobs"); await NextUpdate(token); }
                // Forget lookup caches without destroying live materials owned by other windows.
                var cacheType=TypeOf("ShaderExecutableCache");
                var cache=(System.Collections.IDictionary)Get(cacheType,"cache");
                foreach(var entry in cache.Values) Set(entry,"cached",false);
                cache.Clear();cacheType.GetField("sourceBytes",Flags).SetValue(null,0L);
                lock(Get(cacheType,"preparedLock"))
                { ((System.Collections.IDictionary)Get(cacheType,"prepared")).Clear();cacheType.GetField("preparedBytes",Flags).SetValue(null,0L); }
                bool hybrid = label.StartsWith("continuation-");
                if (hybrid)
                {
                    var g = fixture.behaviour.draftGraph;
                    var lighting = g.nodes.Where(n => n.kind == ShaderGraphNodeKind.NormalLighting).ToArray();
                    var sum = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", ShaderGraphNodeKind.Add, Vector2.zero);g.nodes.Add(sum);
                    Connect(g, lighting[0].id, "Out", sum.id, "A"); Connect(g, lighting[1].id, "Out", sum.id, "B");
                    var end = g.nodes.Single(n => n.kind == ShaderGraphNodeKind.Output);
                    g.edges.RemoveAll(e => e.inputNode == end.id && e.inputPort == "RGBA"); Connect(g,sum.id,"Out",end.id,"RGBA");
                    Set(fixture.window, "svmPreviews", label.EndsWith("svm"));
                }
                long starts = Counter("NativeCompilations");
                var clock = Stopwatch.StartNew();
                fixture.Show();
                var expected = Stamp(fixture.behaviour.draftGraph);
                bool CurrentReady()
                {
                    object candidate = Call(fixture.window,"OutputVM");
                    return candidate != null && (bool)Get(candidate,"Ready") || FinalReady(fixture,expected);
                }
                while (!CurrentReady())
                { if (clock.Elapsed.TotalSeconds > 90) throw new TimeoutException("Final compilation"); await NextUpdate(token); }
                double compile = clock.Elapsed.TotalMilliseconds;
                var panel = Get(fixture.window, "preview");
                Call(panel, "RequestLayerPreview");
                do { await NextUpdate(token); Call(panel, "Tick"); }
                while (Get(panel, "source") == null || (bool)Get(panel, "dirty"));
                var target = (RenderTexture)Get(panel, "source");
                var read = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); read.Apply();
                    double ready = clock.Elapsed.TotalMilliseconds;
                    File.WriteAllBytes(Path.Combine(folder, "pixels-" + pass + ".bin"), read.GetRawTextureData());
                    rows.Add(new { pass, compileMs = compile, readyMs = ready, width = target.width, height = target.height,
                        nativeStarts = Counter("NativeCompilations") - starts, svm = (bool)Get(panel,"UsesSVM") });
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(read); }
                await NextUpdate(token);
            }
        }
        finally { if (focus != null) focus.Focus(); }
        File.WriteAllText(Path.Combine(folder, "timing.json"), Json(new { label, rows,
            cache = "Fresh graph node symbols and empty WhimTex executable/preparation lookup caches each run; Unity/driver cache and shared SVM executor retained. Timing includes actual final image GPU readback; excludes display scanout." }));
        t.True(rows.Count == 3, "Three cold-source final images completed");
    });
    public static string HybridChecks(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        var graph = new ShaderGraph();
        ShaderGraphNode Add(ShaderGraphNodeKind kind)
        {
            var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero);
            graph.nodes.Add(n); return n;
        }
        var uv = Add(ShaderGraphNodeKind.UV);
        var expression = Add(ShaderGraphNodeKind.Expression); expression.valueType = ShaderGraphValueType.Float4; expression.expression = "A*2-1";
        expression.inputs.Clear();
        Connect(graph, uv.id, "Out", expression.id, "A");
        var buffer = Add(ShaderGraphNodeKind.Buffer); buffer.bufferResolution = ShaderGraphBufferResolution.Custom; buffer.bufferSize = new Vector2Int(32, 24);
        Connect(graph, expression.id, "Out", buffer.id, "In");
        var multiply = Add(ShaderGraphNodeKind.Multiply); multiply.FindInput("B").value = Vector4.one * 1.7f;
        Connect(graph, buffer.id, "Out", multiply.id, "A");
        var output = Add(ShaderGraphNodeKind.Output); Connect(graph, multiply.id, "Out", output.id, "RGBA");
        fixture.behaviour.draftGraph = graph;
        var target = new RenderTexture(64, 48, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
        var read = new Texture2D(64, 48, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        object runtime = null, request = null, vm = null;
        Color[] ReadPixels()
        { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); read.Apply(); RenderTexture.active = previous; return read.GetPixels(); }
        async Task CompileNative(bool inline = false)
        {
            if (inline)
            {
                var compilation = Call(Static("ShaderGraphCompiler","Prepare",graph,false),"CompileInlineLighting");
                request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"),BindingFlags.Instance|BindingFlags.NonPublic,
                    null,new object[]{graph,compilation,0UL,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",false,false,true,null,null},null);
            }
            else request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { graph, null, "Out", false, false, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", true }, null);
            Call(request, "WithLighting", fixture.document);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                object[] done = { null };
                if ((bool)Call(request, "TryComplete", done)) { runtime = done[0]; break; }
                if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException("Hybrid native reference");
                await NextUpdate(token);
            }
            ((IDisposable)request).Dispose(); request = null;
        }
        object Context(bool transform = false) => Activator.CreateInstance(TypeOf("LayerRenderContext"), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new object[] { fixture.document, null, target.width, target.height, 8f, transform, false, false }, null);
        try
        {
            for (int mode = 0; mode < 8; mode++)
            {
                buffer.wrap = (ShaderGraphWrap)(mode / 2); buffer.filter = (ShaderGraphFilter)(mode % 2);
                if (buffer.FindInput("UV") == null) buffer.inputs.Add(new ShaderGraphInput { port = "UV", type = ShaderGraphValueType.Float2 });
                buffer.FindInput("UV").source = ShaderGraphInputSource.Value;
                buffer.FindInput("UV").value = new Vector4(-.11f, 1.03f, 0, 0);
                Static("ShaderGraphRevision", "Invalidate", graph);
                await CompileNative();
                var context = Context();
                Call(runtime, "Bind", graph, context, null, true, false); Call(runtime, "PrepareCoordinates", (Layer)fixture.behaviour, context);
                GL.sRGBWrite = false; Call(runtime, "Render", Texture2D.blackTexture, target, context); var expected = ReadPixels();
                object[] build = { graph, null, "Out", false, null };
                t.True((bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", build), "HLSL through Buffer resumes SVM"); vm = build[4];
                Call(vm, "ConfigureHybrid", fixture.document, (Layer)fixture.behaviour, false, true);
                var timer = Stopwatch.StartNew();
                while (!(bool)Get(vm, "Ready"))
                { if ((bool)Get(vm,"Failed") || timer.Elapsed.TotalSeconds > 90) throw new Exception("Hybrid executor: " + Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason")); await NextUpdate(token); }
                t.True((bool)Call(vm, "RenderBoundaries", target, context), "Hybrid image rendered");
                var actual = ReadPixels(); float error = 0;
                for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++) error = Mathf.Max(error, Mathf.Abs(actual[i][c] - expected[i][c]));
                t.True(error < .00002f, "Wrap/filter preserves HDR and raw alpha: " + mode + " error=" + error);
                ((IDisposable)vm).Dispose(); vm = null; ((IDisposable)runtime).Dispose(); runtime = null;
            }
            graph.edges.RemoveAll(e => e.inputNode == multiply.id); Connect(graph, expression.id,"Out",multiply.id,"A");
            object[] unsupported = { graph, null, "Out", false, null };
            t.True(!(bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", unsupported), "No implicit arbitrary HLSL boundary");

            graph.nodes.Clear(); graph.edges.Clear();
            var light = Add(ShaderGraphNodeKind.NormalLighting); light.option = 1;
            light.FindInput("Color").value = new Vector4(-.2f,.4f,1.8f,.35f);
            light.FindInput("SpecularIntensity").value = Vector4.one * .8f;
            output = Add(ShaderGraphNodeKind.Output); Connect(graph,light.id,"Out",output.id,"RGBA");
            await CompileNative();
            var lightingContext = Context();
            Call(runtime,"Bind",graph,lightingContext,null,true,false); Call(runtime,"PrepareCoordinates",(Layer)fixture.behaviour,lightingContext);
            Call(runtime,"Render",Texture2D.blackTexture,target,lightingContext); var native = ReadPixels();
            object[] lightingBuild = {graph,null,"Out",false,null};
            t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",lightingBuild),"Exact lighting resumes SVM"); vm=lightingBuild[4];
            Call(vm,"ConfigureHybrid",fixture.document,(Layer)fixture.behaviour,false,true);
            var lightTimer=Stopwatch.StartNew();
            while(!(bool)Get(vm,"Ready")) { if(lightTimer.Elapsed.TotalSeconds>90)throw new TimeoutException("Lighting VM"); await NextUpdate(token); }
            Call(vm,"RenderBoundaries",target,lightingContext);var lit=ReadPixels();
            float max=0;for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)max=Mathf.Max(max,Mathf.Abs(native[i][c]-lit[i][c]));
            t.True(max<.00002f,"Lighting continuation preserves exact pass output");
            t.Near(.35,lit[0].a,.00001,"Alpha is preserved");
            ((IDisposable)vm).Dispose(); vm = null; ((IDisposable)runtime).Dispose(); runtime = null;
            // Exercise native producer coordinates and SVM suffix UV together.
            uv = Add(ShaderGraphNodeKind.UV);
            var sum = Add(ShaderGraphNodeKind.Add);
            graph.edges.RemoveAll(e => e.inputNode == output.id);
            Connect(graph, light.id, "Out", sum.id, "A"); Connect(graph, uv.id, "Out", sum.id, "B");
            Connect(graph, sum.id, "Out", output.id, "RGBA");
            Static("ShaderGraphRevision", "Invalidate", graph);
            await CompileNative();
            object[] transformedBuild = { graph, null, "Out", false, null };
            t.True((bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", transformedBuild), "Lighting plus UV continuation"); vm = transformedBuild[4];
            Call(vm,"ConfigureHybrid",fixture.document,(Layer)fixture.behaviour,false,true);
            lightTimer.Restart();
            while(!(bool)Get(vm,"Ready")) { if((bool)Get(vm,"Failed")||lightTimer.Elapsed.TotalSeconds>90)throw new TimeoutException("Transformed VM"); await NextUpdate(token); }
            var layer = (Layer)fixture.behaviour;
            layer.transform.position = new Double2(137,-93); layer.transform.scale = new Double2(.71,.83); layer.transform.rotation = 17;
            foreach (var tiling in new[] { TransformTilingMode.Repeat, TransformTilingMode.Mirror, TransformTilingMode.Unbounded })
            {
                layer.transform.tiling = tiling;
                Call(fixture.document, "RefreshTransformHierarchy");
                var ctx = Context(true); var viewport = new Vector4(.13f,.2f,.5f,.6f);
                Call(runtime,"Bind",graph,ctx,null,true,false); Call(runtime,"PrepareCoordinates",layer,ctx);
                ((Material)Get(runtime,"Material")).SetVector("_WhimTex_Viewport",viewport);
                Call(runtime,"Render",Texture2D.blackTexture,target,ctx);native=ReadPixels();
                var hybridRuntime=Get(vm,"hybridRuntime");
                ((Material)Get(hybridRuntime,"Material")).SetVector("_WhimTex_Viewport",viewport);
                Call(vm,"RenderBoundaries",target,ctx);lit=ReadPixels();
                max=0;for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)max=Mathf.Max(max,Mathf.Abs(native[i][c]-lit[i][c]));
                t.True(max<.00002f,"Transform/viewport continuation "+tiling+" error="+max);
            }
            ((IDisposable)runtime).Dispose(); runtime = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
            target = new RenderTexture(578, 530, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
            read = new Texture2D(578,530,TextureFormat.RGBAFloat,false,true);
            await CompileNative(true);
            var tileContext=Context(true);
            Call(runtime,"Bind",graph,tileContext,null,true,false);Call(runtime,"PrepareCoordinates",layer,tileContext);
            Call(runtime,"Render",Texture2D.blackTexture,target,tileContext);native=ReadPixels();
            ((Material)Get(Get(vm,"hybridRuntime"),"Material")).SetVector("_WhimTex_Viewport",new Vector4(0,0,1,1));
            Call(vm,"RenderBoundaries",target,tileContext);lit=ReadPixels();
            max=0;bool finite=true;for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)
            { finite &= !float.IsNaN(lit[i][c])&&!float.IsInfinity(lit[i][c]);max=Mathf.Max(max,Mathf.Abs(native[i][c]-lit[i][c])); }
            t.True(finite,"Finite tile pixels");
            double squared=0;float relative=0;int worst=0;
            for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)
            { float d=Mathf.Abs(native[i][c]-lit[i][c]); squared+=d*d; float rel=d/(1+Mathf.Abs(native[i][c])); if(rel>relative){relative=rel;worst=i;} }
            File.WriteAllText(MigrationD.ProjectPath("Temp/WhimTex/hybrid/tile-error.json"),Json(new {max,relative,rms=Math.Sqrt(squared/(lit.Length*4)),x=worst%target.width,y=worst/target.width,expected=new[]{native[worst].r,native[worst].g,native[worst].b,native[worst].a},actual=new[]{lit[worst].r,lit[worst].g,lit[worst].b,lit[worst].a}}));
            t.True(relative<.005f && Math.Sqrt(squared/(lit.Length*4))<.00005,"Tiled lighting matches inline float-pass budget error="+max);
            // Sampling the lighting at octave coordinates must retain the inline evaluator.
            graph.edges.RemoveAll(e => e.inputNode == output.id);
            var fractal = Add(ShaderGraphNodeKind.Fractal);
            Connect(graph, light.id,"Out",fractal.id,"In"); Connect(graph,fractal.id,"Out",output.id,"RGBA");
            Static("ShaderGraphRevision","Invalidate",graph);
            var sampled = Call(Static("ShaderGraphCompiler","Prepare",graph,false),"Compile",null,"Out",false);
            t.True(!((System.Collections.IEnumerable)Get(sampled,"buffers")).Cast<object>().Any(p => (bool)Get(p,"screenSpace")),"Fractal lighting stays inline");
            object[] sampledBuild={graph,null,"Out",false,null};
            t.True(!(bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",sampledBuild),"Sampled lighting falls back to native");
            ((IDisposable)vm).Dispose();vm=null;
            vm=Static("ShaderGraphPreviewVM","BeginHybrid",graph,null,"Out",false,true);
            var asyncTimer=Stopwatch.StartNew();
            while(!(bool)Get(vm,"Failed") && !(bool)Get(vm,"Ready"))
            { if(asyncTimer.Elapsed.TotalSeconds>30)throw new TimeoutException("Fallback preparation");await NextUpdate(token); }
            t.True((bool)Get(vm,"Failed"),"Unsupported asynchronous continuation reports fallback");
            t.True(!(bool)Get(vm,"Ready") && !(bool)Get(vm,"Ready"),"Repeated Ready never uploads an empty program");
            var pureGraph=new ShaderGraph(); var pureUv=(ShaderGraphNode)Static("ShaderGraphNodes","Create",ShaderGraphNodeKind.UV,Vector2.zero);pureGraph.nodes.Add(pureUv);
            object[] pureBuild={pureGraph,pureUv.id,"Out",null};
            t.True((bool)Static("ShaderGraphPreviewVM","TryCreate",pureBuild),"Pure recovery build");
            Call(vm,"ReplaceCode",pureBuild[3]);((IDisposable)pureBuild[3]).Dispose();
            t.True(!(bool)Get(vm,"Failed"),"New code clears failed preparation");


        }
        finally
        {
            (request as IDisposable)?.Dispose();(runtime as IDisposable)?.Dispose();(vm as IDisposable)?.Dispose();
            RenderTexture.active=previous;GL.sRGBWrite=srgb;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);
        }
    });

}
