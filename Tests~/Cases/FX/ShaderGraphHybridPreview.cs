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
            t.True(max<.00002f,"Lighting continuation preserves exact pass output: max="+max+" expected="+native[0]+" actual="+lit[0]);
            t.Near(.35,lit[0].a,.00001,"Alpha is preserved");
            var surfaces=((System.Collections.IEnumerable)Get(vm,"lightingSurfaces")).Cast<object>().ToArray();
            t.Equal(1,surfaces.Length,"One multi-output surface executor");
            t.True(surfaces.All(s=>(bool)Get(s,"Ready")),"Surface executors are ready");
            var nativeParts=((System.Collections.IEnumerable)Get(Get(vm,"hybridRuntime"),"bufferExecutables")).Cast<object>().ToArray();
            t.True(nativeParts.Any(p=>(bool)Get(p,"bindingsOnly")),"Surface preparation does not compile a native GPU pass");
            var custom=Add(ShaderGraphNodeKind.Expression);custom.valueType=ShaderGraphValueType.Float4;custom.expression="A";
            Connect(graph,custom.id,"Out",light.id,"Color");Static("ShaderGraphRevision","Invalidate",graph);
            object[] customBuild={graph,null,"Out",false,null};
            t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",customBuild),"User code retains a native surface boundary");
            t.Equal(0,((System.Collections.IEnumerable)Get(customBuild[4],"lightingSurfaces")).Cast<object>().Count(),"Unsupported surface input stays on HLSL");
            ((IDisposable)customBuild[4]).Dispose();graph.nodes.Remove(custom);graph.edges.RemoveAll(e=>e.outputNode==custom.id);Static("ShaderGraphRevision","Invalidate",graph);
            // A failed surface executor must recover using the retained native producer.
            surfaces[0].GetType().GetProperty("Failed",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(surfaces[0],true);
            lightTimer.Restart();
            while(!(bool)Get(vm,"Ready")){if(lightTimer.Elapsed.TotalSeconds>90)throw new TimeoutException("Surface native fallback");await NextUpdate(token);}
            Call(vm,"RenderBoundaries",target,lightingContext);var recovered=ReadPixels();
            max=0;for(int i=0;i<recovered.Length;i++)for(int c=0;c<4;c++)max=Mathf.Max(max,Mathf.Abs(native[i][c]-recovered[i][c]));
            t.True(max<.00002f,"Failed surface executor retains HLSL output");
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
            // A second lighting pass reads the first at full-image coordinates across tiles.
            ((IDisposable)runtime).Dispose();runtime=null;((IDisposable)vm).Dispose();vm=null;
            var secondLight=Add(ShaderGraphNodeKind.NormalLighting);secondLight.option=1;
            graph.edges.RemoveAll(e=>e.inputNode==output.id);
            Connect(graph,light.id,"Out",secondLight.id,"Color");Connect(graph,secondLight.id,"Out",output.id,"RGBA");
            Static("ShaderGraphRevision","Invalidate",graph);await CompileNative(true);
            Call(runtime,"Bind",graph,tileContext,null,true,false);Call(runtime,"PrepareCoordinates",layer,tileContext);
            var tiledViewport=new Vector4(.13f,.2f,.5f,.6f);
            ((Material)Get(runtime,"Material")).SetVector("_WhimTex_Viewport",tiledViewport);
            Call(runtime,"Render",Texture2D.blackTexture,target,tileContext);native=ReadPixels();
            object[] nestedBuild={graph,null,"Out",false,null};
            t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",nestedBuild),"Nested lighting surface build");vm=nestedBuild[4];
            Call(vm,"ConfigureHybrid",fixture.document,layer,false,true);lightTimer.Restart();
            while(!(bool)Get(vm,"Ready")) {if((bool)Get(vm,"Failed")||lightTimer.Elapsed.TotalSeconds>90)throw new TimeoutException("Nested lighting VM");await NextUpdate(token);}
            ((Material)Get(Get(vm,"hybridRuntime"),"Material")).SetVector("_WhimTex_Viewport",tiledViewport);
            Call(vm,"RenderBoundaries",target,tileContext);lit=ReadPixels();
            squared=0;relative=0;for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)
            {float d=Mathf.Abs(native[i][c]-lit[i][c]);squared+=d*d;relative=Mathf.Max(relative,d/(1+Mathf.Abs(native[i][c])));}
            t.True(relative<.005f&&Math.Sqrt(squared/(lit.Length*4))<.00005,"Nested lighting preserves tile/viewport sampling");
            graph.edges.RemoveAll(e=>e.inputNode==secondLight.id||e.outputNode==secondLight.id);graph.nodes.Remove(secondLight);
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
            vm=Static("ShaderGraphPreviewVM","BeginHybrid",graph,null,"Out",false,true,false);
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
            ((IDisposable)vm).Dispose(); vm=null; ((IDisposable)runtime).Dispose(); runtime=null;
            graph=fixture.behaviour.draftGraph=new ShaderGraph();
            var noise=Add(ShaderGraphNodeKind.ValueNoise); noise.noiseFiltering=true; noise.noisePeriodic=true;
            var inner=Add(ShaderGraphNodeKind.Fractal); inner.FindInput("Octaves").value=Vector4.one*2;
            var outer=Add(ShaderGraphNodeKind.Fractal); outer.FindInput("Octaves").value=Vector4.one*2;
            var normal=Add(ShaderGraphNodeKind.HeightToNormal); normal.heightSource=ShaderGraphHeightSource.Value;
            light=Add(ShaderGraphNodeKind.NormalLighting); light.option=1;
            output=Add(ShaderGraphNodeKind.Output);
            Connect(graph,noise.id,"Value",inner.id,"In"); Connect(graph,inner.id,"Out",outer.id,"In");
            Connect(graph,outer.id,"Out",normal.id,"Height"); Connect(graph,normal.id,"Out",light.id,"Normal");
            Connect(graph,outer.id,"Out",light.id,"Color"); Connect(graph,light.id,"Out",output.id,"RGBA");
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
            target=new RenderTexture(64,48,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); target.Create();
            read=new Texture2D(64,48,TextureFormat.RGBAFloat,false,true);
            await CompileNative(); var replayContext=Context();
            Call(runtime,"Bind",graph,replayContext,null,true,false);Call(runtime,"PrepareCoordinates",layer,replayContext);
            Call(runtime,"Render",Texture2D.blackTexture,target,replayContext);native=ReadPixels();
            object[] replayBuild={graph,null,"Out",false,null};
            t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",replayBuild),"Nested replay surface build");vm=replayBuild[4];
            Call(vm,"ConfigureHybrid",fixture.document,layer,false,true);lightTimer.Restart();
            while(!(bool)Get(vm,"Ready")){if((bool)Get(vm,"Failed")||lightTimer.Elapsed.TotalSeconds>120)throw new TimeoutException("Nested replay surface VM");await NextUpdate(token);}
            Call(vm,"RenderBoundaries",target,replayContext);lit=ReadPixels();
            max=0; for(int i=0;i<lit.Length;i++)for(int c=0;c<4;c++)max=Mathf.Max(max,Mathf.Abs(native[i][c]-lit[i][c]));
            t.True(max<.0002f,"Periodic executor preserves nested Fractal, sampled derivatives and simultaneous outputs: "+max);
            surfaces=((System.Collections.IEnumerable)Get(vm,"lightingSurfaces")).Cast<object>().ToArray();
            t.Equal(1,surfaces.Length,"Replay remains on multi-output SVM");
            t.True(((Material)Get(surfaces[0],"material")).IsKeywordEnabled("WT_VM_PERIODIC"),"Periodic replay uses the periodic executor");
            foreach (bool derivatives in new[] { false, true, false })
            {
                noise.noisePeriodic=derivatives;
                object[] replacement={graph,noise.id,derivatives?"Gradient":"Value",null};
                t.True((bool)Static("ShaderGraphPreviewVM","TryCreate",replacement),"Noise variant replacement builds");
                Call(vm,"ReplaceCode",replacement[3]); ((IDisposable)replacement[3]).Dispose();
                var shader=(Material)Get(vm,"Material");
                t.Equal(derivatives,shader.IsKeywordEnabled("WT_VM_PERIODIC"),"ReplaceCode switches periodic executor both ways");
                t.Equal(derivatives,shader.IsKeywordEnabled("WT_VM_BASIS_DERIVATIVES"),"ReplaceCode switches Value/Gradient executor both ways");
            }
        }
        finally
        {
            (request as IDisposable)?.Dispose();(runtime as IDisposable)?.Dispose();(vm as IDisposable)?.Dispose();
            RenderTexture.active=previous;GL.sRGBWrite=srgb;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);
        }
    });

}

public static partial class ShaderGraphEditingBench
{
    public static string BufferReuseChecks(string runId) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        var graph = fixture.behaviour.draftGraph = new ShaderGraph();
        ShaderGraphNode Node(ShaderGraphNodeKind kind) {
            var n=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,Vector2.zero);graph.nodes.Add(n);return n;
        }
        void Value(ShaderGraphNode n,string port,Vector4 value,ShaderGraphValueType type=ShaderGraphValueType.Float) {
            var input=n.FindInput(port); if(input==null){input=new ShaderGraphInput{port=port};n.inputs.Add(input);}
            input.source=ShaderGraphInputSource.Value;input.type=type;input.value=value;
        }
        void Wire(ShaderGraphNode a,string output,ShaderGraphNode b,string input) {
            graph.edges.RemoveAll(e=>e.inputNode==b.id&&e.inputPort==input);Connect(graph,a.id,output,b.id,input);
        }
        ShaderGraphNode Buffer(ShaderGraphNode source) {
            var n=Node(ShaderGraphNodeKind.Buffer);n.bufferResolution=ShaderGraphBufferResolution.Custom;n.bufferSize=new Vector2Int(32,32);
            Wire(source,"Out",n,"In");return n;
        }
        var uv=Node(ShaderGraphNodeKind.UV);var scale=Node(ShaderGraphNodeKind.Multiply);Value(scale,"B",Vector4.one*.7f);Wire(uv,"Out",scale,"A");
        var first=Buffer(scale);var second=Buffer(first);
        var independent=Node(ShaderGraphNodeKind.Add);Value(independent,"B",new Vector4(.3f,.2f,.1f,.4f),ShaderGraphValueType.Float4);var third=Buffer(independent);
        var sum=Node(ShaderGraphNodeKind.Add);Wire(second,"Out",sum,"A");Wire(third,"Out",sum,"B");
        var color=Node(ShaderGraphNodeKind.Saturation);Value(color,"Amount",Vector4.one*.6f);Wire(sum,"Out",color,"Color");
        var light=Node(ShaderGraphNodeKind.NormalLighting);light.option=1;Wire(color,"Out",light,"Color");
        var output=Node(ShaderGraphNodeKind.Output);Wire(light,"Out",output,"RGBA");
        var target=new RenderTexture(32,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        var read=new Texture2D(32,32,TextureFormat.RGBAFloat,false,true);var inputImage=new RenderTexture(8,8,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);inputImage.Create();
        var previous=RenderTexture.active;bool srgb=GL.sRGBWrite;object runtime=null,request=null,vm=null;
        Vector4 viewport=new Vector4(0,0,1,1);
        object Context(float time=0) {
            var context=Activator.CreateInstance(TypeOf("LayerRenderContext"),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,
                null,new object[]{fixture.document,null,32,32,16f,false,false,false},null);
            return Call(context,"WithShaderGraphPreviewTime",(float?)time);
        }
        Color[] Pixels(){RenderTexture.active=target;read.ReadPixels(new Rect(0,0,32,32),0,0);read.Apply();RenderTexture.active=previous;return read.GetPixels();}
        Color[] Render(float time=0) {
            var context=Context(time);Call(runtime,"Bind",graph,context,null,true,false);Call(runtime,"PrepareCoordinates",(Layer)fixture.behaviour,context);
            ((Material)Get(runtime,"Material")).SetVector("_WhimTex_Viewport",viewport);
            GL.sRGBWrite=false;Call(runtime,"Render",inputImage,target,context);return Pixels();
        }
        int Draws()=>Convert.ToInt32(Get(runtime,"BufferDrawCount"));
        void Near(Color[] a,Color[] b,string message) {
            float error=0;for(int i=0;i<a.Length;i++)for(int c=0;c<4;c++){t.True(float.IsFinite(b[i][c]),message+" finite");error=Mathf.Max(error,Mathf.Abs(a[i][c]-b[i][c]));}
            t.True(error<.00001f,message+" max="+error);
        }
        async Task Compile() {
            (runtime as IDisposable)?.Dispose();runtime=null;Static("ShaderGraphRevision","Invalidate",graph);
            var compilation=Call(Static("ShaderGraphCompiler","Prepare",graph,false),"Compile",null,"Out",false);
            request=Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"),BindingFlags.Instance|BindingFlags.NonPublic,null,
                new object[]{graph,compilation,Stamp(graph),"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",false,false,true,null,null},null);
            Call(request,"WithLighting",fixture.document);var clock=Stopwatch.StartNew();
            while(true){object[] done={null};if((bool)Call(request,"TryComplete",done)){runtime=done[0];break;}if(clock.Elapsed.TotalSeconds>90)throw new TimeoutException("Buffer compiler");await NextUpdate(token);}
            ((IDisposable)request).Dispose();request=null;
        }
        void CompareFresh(Color[] cached,string message,float time=0) {
            Call(runtime,"ReleaseBuffers");Call(fixture.document,"ReleaseShaderBuffers");Near(cached,Render(time),message);
        }
        try {
            await Compile();var original=Render();int before=Draws();Near(original,Render(),"Repeated render");t.Equal(before,Draws(),"Unchanged passes reused");
            Value(color,"Amount",Vector4.one*.2f);var changed=Render();t.Equal(before+2,Draws(),"Downstream color changes lighting only");CompareFresh(changed,"Downstream cached/cold");
            before=Draws();Value(scale,"B",Vector4.one*.4f);changed=Render();t.Equal(before+4,Draws(),"Upstream edit redraws dependent buffers and lighting, keeps independent buffer");CompareFresh(changed,"Upstream cached/cold");
            before=Draws();fixture.document.lights[0].intensity*=.63f;changed=Render();t.Equal(before+2,Draws(),"Light edit retains non-lighting passes");CompareFresh(changed,"Lighting cached/cold");
            before=Draws();viewport=new Vector4(.1f,.2f,.6f,.7f);changed=Render();t.Equal(before+2,Draws(),"Viewport changes screen-space passes only");CompareFresh(changed,"Viewport cached/cold");
            viewport=new Vector4(0,0,1,1);changed=Render();
            before=Draws();changed=Render(.8f);t.Equal(before,Draws(),"Unused Time does not invalidate producers");
            // A second runtime/VM for the same bindings must share immutable images.
            long requests=Counter("PassCompilationRequests");
            object[] build={graph,null,"Out",false,null};t.True((bool)Static("ShaderGraphPreviewVM","TryCreateHybrid",build),"Hybrid root builds");
            vm=build[4];Call(vm,"ConfigureHybrid",fixture.document,(Layer)fixture.behaviour,false,true);var clock=Stopwatch.StartNew();
            while(!(bool)Get(vm,"Ready")){if(clock.Elapsed.TotalSeconds>90)throw new TimeoutException("Hybrid readiness");await NextUpdate(token);}
            t.True((bool)Call(vm,"RenderBoundaries",target,Context(.8f)),"Hybrid draws");Near(changed,Pixels(),"Hybrid/native parity");
            var hybrid=Get(vm,"hybridRuntime");t.Equal(2,Convert.ToInt32(Get(hybrid,"BufferDrawCount")),"Hybrid reuses native buffers and evaluates its own lighting surface backend");
            t.True((bool)Get(Get(hybrid,"executable"),"bindingsOnly"),"Carrier does not request a GPU executable");
            t.Equal(requests,Counter("PassCompilationRequests"),"Shared producers and binding carrier need no new GPU compile request");
            // Independently compiled downstream previews still share the same native passes.
            ((IDisposable)vm).Dispose();vm=null;
            var cache=(System.Collections.IDictionary)Get(Get(fixture.document,"ShaderBuffers"),"entries");
            var textures=cache.Values.Cast<object>().Select(e=>Get(e,"texture")).ToArray();
            for(int i=0;i<16;i++){Value(color,"Amount",Vector4.one*(.01f*i));Render();}
            t.True(cache.Values.Cast<object>().All(e=>textures.Any(texture=>ReferenceEquals(texture,Get(e,"texture")))),"Changing values recycles unleased textures without new GPU allocations");
            var time=Node(ShaderGraphNodeKind.Time);Wire(time,"Out",scale,"B");await Compile();Render(.1f);before=Draws();changed=Render(.4f);
            t.Equal(before+4,Draws(),"Time-dependent producers refresh, independent producer remains cached");CompareFresh(changed,"Animated cached/cold",.4f);
            var sample=Node(ShaderGraphNodeKind.SampleInput);Wire(sample,"Out",first,"In");await Compile();
            Graphics.Blit(Texture2D.whiteTexture,inputImage);original=Render();before=Draws();
            Graphics.Blit(Texture2D.blackTexture,inputImage);changed=Render();t.True(Draws()>before,"External RenderTexture stays volatile");
            t.True(original.Zip(changed,(a,b)=>(a-b).maxColorComponent).Any(v=>Mathf.Abs(v)>.01f),"GPU writes observed without relying on texture update count");
            CompareFresh(changed,"External input cached/cold");
            var retained=((System.Collections.IDictionary)Get(Get(fixture.document,"ShaderBuffers"),"entries")).Count;
            t.True(retained<=48,"Cache entry budget");
        }
        finally {
            (request as IDisposable)?.Dispose();(vm as IDisposable)?.Dispose();(runtime as IDisposable)?.Dispose();
            RenderTexture.active=previous;GL.sRGBWrite=srgb;
            UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(read);UnityEngine.Object.DestroyImmediate(inputImage);
        }
    });
}
