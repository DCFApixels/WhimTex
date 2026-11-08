using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.UIElements;
using WhimTex.Tests.UnityD;

public static partial class ShaderGraphEditingBench
{
    public static string SvmWindowChecks(string runId) => RunSvmWindowChecks(runId, false);
    public static string SvmProceduralWindowChecks(string runId) => RunSvmWindowChecks(runId, true);
    public static string SvmNoiseWindowChecks(string runId) => RunSvmWindowChecks(runId, false, true);
    static readonly ShaderGraphNodeKind[] NoiseVMKinds = {
        ShaderGraphNodeKind.PerlinNoise, ShaderGraphNodeKind.ValueNoise, ShaderGraphNodeKind.ValueCubicNoise,
        ShaderGraphNodeKind.OpenSimplex2Noise, ShaderGraphNodeKind.OpenSimplex2SNoise, ShaderGraphNodeKind.FlowNoise,
        ShaderGraphNodeKind.GaborNoise, ShaderGraphNodeKind.CellularNoise, ShaderGraphNodeKind.ShardNoise };
    static readonly ShaderGraphNodeKind[] ProceduralVMKinds = {
        ShaderGraphNodeKind.CanvasPosition, ShaderGraphNodeKind.GradientMap, ShaderGraphNodeKind.ColorCurves,
        ShaderGraphNodeKind.PixelateUV, ShaderGraphNodeKind.TileUV, ShaderGraphNodeKind.Blackbody, ShaderGraphNodeKind.Dither,
        ShaderGraphNodeKind.Shape, ShaderGraphNodeKind.CombineDistance, ShaderGraphNodeKind.Checkerboard,
        ShaderGraphNodeKind.Stripes, ShaderGraphNodeKind.Grid, ShaderGraphNodeKind.Rings };
    static string RunSvmWindowChecks(string runId, bool procedural, bool noiseChecks = false, bool remainingChecks = false, int imageChecks = 0) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        using var imageFixture=imageChecks!=0?new SvmImageWindowFixture(fixture,imageChecks is 2 or 4,imageChecks>=3):null;
        if (remainingChecks)
        {
            var graph=fixture.behaviour.draftGraph=new ShaderGraph();
            foreach(var kind in RemainingVMKinds.Concat(new[]{ShaderGraphNodeKind.Output}))
                graph.nodes.Add((ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,new Vector2(graph.nodes.Count%4*330,graph.nodes.Count/4*440)));
        }
        if (noiseChecks)
        {
            var graph = fixture.behaviour.draftGraph = new ShaderGraph();
            foreach (var kind in NoiseVMKinds)
            foreach (ShaderGraphNoiseDimensions dimensions in Enum.GetValues(typeof(ShaderGraphNoiseDimensions)))
            {
                var n=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,new Vector2(graph.nodes.Count%5*330,graph.nodes.Count/5*440));
                graph.nodes.Add(n); Static("ShaderGraphNodes","ChangeNoiseDimensions",graph,n,dimensions);
                n.noisePeriodic=n.noiseFiltering=true;
            }
            graph.nodes.Add((ShaderGraphNode)Static("ShaderGraphNodes","Create",ShaderGraphNodeKind.Output,Vector2.zero));
        }
        if (procedural)
        {
            var graph = fixture.behaviour.draftGraph = new ShaderGraph();
            foreach (var kind in ProceduralVMKinds.Concat(new[] { ShaderGraphNodeKind.CanvasData,ShaderGraphNodeKind.Add,ShaderGraphNodeKind.Output }))
                graph.nodes.Add((ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,new Vector2(graph.nodes.Count%4*330,graph.nodes.Count/4*370)));
            Connect(graph,graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.CanvasData).id,"PixelScale",graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.Add).id,"A");
        }
        foreach (var node in ((ShaderGraph)Get(fixture.window,"Graph")).nodes) node.previewExpanded = true;
        Set(fixture.window, "svmPreviews", true); Set(fixture.window, "accurateLighting", false);
        fixture.Show();
        await NextUpdate(token); await NextUpdate(token);
        var timer = Stopwatch.StartNew();
        while (Get(fixture.window, "canvas") == null)
        { if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("Graph UI"); await NextUpdate(token); }
        var canvas = Get(fixture.window, "canvas");
        var view = (UnityEditor.Experimental.GraphView.GraphView)canvas;
        var rows = new List<object>();
        string report = MigrationD.ProjectPath(imageChecks!=0 ? "Temp/WhimTex/svm-image-window-"+imageChecks+".json" : remainingChecks ? "Temp/WhimTex/svm-remaining-window.json" : noiseChecks ? "Temp/WhimTex/svm-noise-window.json" : procedural ? "Temp/WhimTex/svm-procedural-window.json" : "Temp/WhimTex/svm-window-checks.json");
        async Task<object> Visit(string id, object preview)
        {
            timer.Restart();
            while (((UnityEngine.UIElements.VisualElement)Get(preview, "surface")).contentRect.width < 1)
            { if (timer.Elapsed.TotalSeconds > 10) throw new TimeoutException("Preview layout"); await NextUpdate(token); }
            var surface = (UnityEngine.UIElements.VisualElement)Get(preview, "surface");
            var point = view.contentViewContainer.WorldToLocal(surface.worldBound.center);
            view.UpdateViewTransform(view.contentRect.center - point * .7f, Vector3.one * .7f);
            await NextUpdate(token);
            timer.Restart();
            while (!(bool)Call(preview, "CanDisplay") || !(bool)Get(preview, "displayedSVM") ||
                !(bool)Get(preview, "svmRendered") || (bool)Get(preview, "dirty"))
            {
                var current = ((System.Collections.IDictionary)Get(canvas, "previews"))[id];
                if (!ReferenceEquals(preview,current))
                {
                    preview=current; surface=(VisualElement)Get(preview,"surface");
                    point=view.contentViewContainer.WorldToLocal(surface.worldBound.center);
                    view.UpdateViewTransform(view.contentRect.center-point*.7f,Vector3.one*.7f);
                }
                if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException("Actual SVM preview: " + id + ", " + Json(new {
                    visible=Call(preview,"CanDisplay"), displayed=Get(preview,"displayedSVM"), rendered=Get(preview,"svmRendered"),dirty=Get(preview,"dirty"),
                    attached=((VisualElement)preview).panel!=null, expanded=Get(preview,"expanded"),bounds=surface.worldBound.ToString(),canvas=view.worldBound.ToString(),
                    reason=Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason") }));
                await NextUpdate(token);
            }
            t.True(Get(preview, "texture") != null && Get(preview, "program") == null && Get(preview, "pending") == null, "Actual VM image and no redundant native consumer: " + id);
            return preview;
        }
        var entries = new List<System.Collections.DictionaryEntry>();
        foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)Get(canvas, "previews")) entries.Add(entry);
        foreach (var item in entries)
        {
            string id = (string)item.Key; var node = ((ShaderGraph)Get(fixture.window,"Graph")).FindNode(id);
            File.WriteAllText(report, Json(new { phase = node.kind.ToString(), rows }));
            var current = await Visit(id, item.Value);
            rows.Add(new { id, kind = node.kind.ToString(), dimensions=node.noiseDimensions.ToString(), displayedSVM = Get(current, "displayedSVM") });
        }
        if(imageChecks>=3) {
            t.True(entries.Count>=3,"Parameter/Sub Graph preview population");
            var graph=(ShaderGraph)Get(fixture.window,"Graph");var sub=graph.nodes.First(n=>n.kind==ShaderGraphNodeKind.SubGraph);
            var card=((System.Collections.IDictionary)Get(canvas,"previews"))[sub.id];
            Call(card,"ChangeExpanded",false);await NextUpdate(token);Call(card,"ChangeExpanded",true);await Visit(sub.id,card);
            ((DropdownField)Get(card,"output")).value=graph.subGraphs[0].outputs[1].id;await Visit(sub.id,card);
            ((DropdownField)Get(card,"output")).value=graph.subGraphs[0].outputs[0].id;await Visit(sub.id,card);
            Call(fixture.window,"Edit","Change integer parameter",(Action<ShaderGraph>)(g=>Call(g.parameters[0],"SetNumber",0,7d)),false,false,false);
            foreach(var item in entries)await Visit((string)item.Key,((System.Collections.IDictionary)Get(canvas,"previews"))[item.Key]);
            foreach(var p in graph.parameters)t.True((bool)Static("ShaderGraphPreviewVM","SupportsParameter",p),"Parameter creation filter "+p.name);
            t.True((bool)Static("ShaderGraphPreviewVM","SupportsSubGraph",graph,graph.subGraphs[0],null),"Sub Graph creation filter");
            Call(fixture.window,"OpenSubGraph",graph.subGraphs[0].id);await NextUpdate(token);await NextUpdate(token);
            canvas=Get(fixture.window,"canvas");view=(UnityEditor.Experimental.GraphView.GraphView)canvas;
            var innerEntries=new System.Collections.Generic.List<System.Collections.DictionaryEntry>();
            foreach(System.Collections.DictionaryEntry item in (System.Collections.IDictionary)Get(canvas,"previews"))innerEntries.Add(item);
            t.True(innerEntries.Count>0,"Sub Graph editor has preview cards");
            foreach(var item in innerEntries)await Visit((string)item.Key,item.Value);
            File.WriteAllText(report,Json(new{phase="complete",rows,outputSwitch=true,reopen=true,edit=true}));return;
        }
        if(imageChecks!=0) {
            t.True(entries.Count>=5,"Image node preview population");
            var graph=(ShaderGraph)Get(fixture.window,"Graph");var sampler=graph.nodes.First(n=>n.kind==ShaderGraphNodeKind.SampleTexture);
            var card=((System.Collections.IDictionary)Get(canvas,"previews"))[sampler.id];
            Call(card,"ChangeExpanded",false);await NextUpdate(token);Call(card,"ChangeExpanded",true);await Visit(sampler.id,card);
            ((DropdownField)Get(card,"output")).value="R";await Visit(sampler.id,card);
            Call(fixture.window,"EditNode",sampler.id,"Texture binding",(Action<ShaderGraphNode>)(n=>{
                n.inputs.Add(new ShaderGraphInput {port="Texture",type=ShaderGraphValueType.Texture,
                    resource=new ShaderParameter {type=ShaderParameterType.Texture2D,textureValue=Texture2D.blackTexture}});
            }),false,false);
            await Visit(sampler.id,card);
            var active=RenderTexture.active;var pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=(RenderTexture)Get(card,"texture");pixel.ReadPixels(new Rect(0,0,1,1),0,0);pixel.Apply();t.True(pixel.GetPixel(0,0).r<.001f,"Changed texture binding reaches SVM card");}
            finally {RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(pixel);}
            var height=graph.nodes.First(n=>n.kind==ShaderGraphNodeKind.HeightToNormal);
            fixture.Edit(new Step {action="value",node=height.id,port="Strength",number=4.7f});
            await Visit(height.id,((System.Collections.IDictionary)Get(canvas,"previews"))[height.id]);
            t.True((bool)Static("ShaderGraphPreviewVM","SupportsParameter",new ShaderParameter {type=ShaderParameterType.Texture2D}),"Texture parameter creation filter");
            t.True((bool)Static("ShaderGraphPreviewVM","SupportsDefaultNode",ShaderGraphNodeKind.TextureSize,"Size"),"Texture Size creation filter");
            File.WriteAllText(report,Json(new {phase="complete",rows,outputSwitch=true,reopen=true,edit=true}));return;
        }
        t.True(entries.Count >= (remainingChecks?9:procedural?14:20), "Graph preview population");
        var cubic = fixture.behaviour.draftGraph.nodes.First(n => n.kind == (remainingChecks?ShaderGraphNodeKind.BasicGridWarpNoise:noiseChecks?ShaderGraphNodeKind.FlowNoise:procedural?ShaderGraphNodeKind.TileUV:ShaderGraphNodeKind.ValueCubicNoise));
        var previewNode = ((System.Collections.IDictionary)Get(canvas,"previews"))[cubic.id];
        Call(previewNode, "ChangeExpanded", false); await NextUpdate(token);
        t.True(!(bool)Call(previewNode, "CanDisplay"), "Collapsed preview is not rendered");
        Call(previewNode, "ChangeExpanded", true); await Visit(cubic.id, previewNode);
        ((UnityEngine.UIElements.DropdownField)Get(previewNode, "output")).value = remainingChecks?"Position":procedural?"Random":"Gradient";
        await Visit(cubic.id, previewNode);
        fixture.Edit(new Step { action = "value", node = cubic.id, port = "Seed", number = 731 });
        await Visit(cubic.id, previewNode);
        if (noiseChecks)
        {
            fixture.Edit(new Step { action="dimensions",node=cubic.id,value="ThreeD" }); await Visit(cubic.id,previewNode);
            fixture.Edit(new Step { action="value",node=cubic.id,port="Phase",number=.37f }); await Visit(cubic.id,previewNode);
            foreach (var kind in NoiseVMKinds) t.True((bool)Static("ShaderGraphPreviewVM","SupportsDefaultNode",kind,"Value"),"Noise creation filter "+kind);
        }
        t.True((bool)Static("ShaderGraphPreviewVM","SupportsDefaultNode",ShaderGraphNodeKind.NormalLighting,"Out"),"Creation filter includes simplified lighting");
        foreach(var kind in new[]{ShaderParameterType.Gradient,ShaderParameterType.Curve,ShaderParameterType.Transform2D})
            t.True((bool)Static("ShaderGraphPreviewVM","SupportsParameter",new ShaderParameter {type=kind}),"Creation filter includes "+kind);
        File.WriteAllText(report, Json(new { phase = "complete", rows, outputSwitch = true, reopen = true, edit = true }));
    });

    public static string SvmNodeChecks(string runId) => RunSvmNodeChecks(runId,false);
    public static string SvmProceduralChecks(string runId) => RunSvmNodeChecks(runId,true);
    public static string SvmNoiseChecks(string runId) => RunSvmNodeChecks(runId,false,true);
    public static string SvmNoiseDetailChecks(string runId) => RunSvmNodeChecks(runId,false,true,true);
    static string RunSvmNodeChecks(string runId, bool procedural, bool noiseChecks = false, bool detailNoises = false, int remaining = 0) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        var graph = new ShaderGraph();
        var rows = new List<object>();
        string report = MigrationD.ProjectPath(remaining!=0 ? "Temp/WhimTex/svm-remaining-"+remaining+".json" : detailNoises ? "Temp/WhimTex/svm-noise-detail.json" : noiseChecks ? "Temp/WhimTex/svm-noise.json" : procedural ? "Temp/WhimTex/svm-procedural.json" : "Temp/WhimTex/svm-node-checks.json");
        var target = new RenderTexture(40, 32, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); target.Create();
        var read = new Texture2D(40, 32, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active; bool srgb = GL.sRGBWrite;
        object vm = null, runtime = null, request = null;
        ShaderGraphNode Add(ShaderGraphNodeKind kind)
        { var n = (ShaderGraphNode)Static("ShaderGraphNodes", "Create", kind, Vector2.zero); graph.nodes.Add(n); return n; }
        void Value(ShaderGraphNode n, string port, Vector4 value)
        {
            var input = n.FindInput(port);
            if (input == null) { input = new ShaderGraphInput { port = port, type = ShaderGraphValueType.Float }; n.inputs.Add(input); }
            input.source = ShaderGraphInputSource.Value; input.value = value;
        }
        Color[] Pixels()
        { RenderTexture.active = target; read.ReadPixels(new Rect(0, 0, 40, 32), 0, 0); read.Apply(); RenderTexture.active = previous; return read.GetPixels(); }
        bool transformed = false; float evaluationTime = 0; bool effectImages=remaining==11;
        RenderTexture effectImage=null;
        if(effectImages) {effectImage=new RenderTexture(40,32,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);effectImage.Create();}
        async Task Check(string label, ShaderGraphNode node, string port = "Out", bool simplified = false)
        {
            File.WriteAllText(report, Json(new { phase = label, rows }));
            fixture.behaviour.draftGraph = graph;
            Static("ShaderGraphRevision", "Invalidate", graph);
            var compilation = Call(Static("ShaderGraphCompiler", "Prepare", graph, effectImages), "Compile", node?.id, port, simplified);
            request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { graph, compilation, 0UL, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, effectImages, true, null, null }, null);
            Call(request, "WithLighting", fixture.document);
            var timer = Stopwatch.StartNew();
            while (true)
            {
                object[] done = { null };
                if ((bool)Call(request, "TryComplete", done)) { runtime = done[0]; break; }
                if (timer.Elapsed.TotalSeconds > 90) throw new TimeoutException(label + " native");
                await NextUpdate(token);
            }
            ((IDisposable)request).Dispose(); request = null;
            object context = Activator.CreateInstance(TypeOf("LayerRenderContext"), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new object[] { fixture.document, null, 40, 32, 12.8f, transformed, false, false }, null);
            if(evaluationTime!=0) context=Call(context,"WithShaderGraphPreviewTime",(float?)evaluationTime);
            if(effectImages) {Call(runtime,"BindEffect",graph,context,null,true);GL.sRGBWrite=false;Graphics.Blit(fixture.behaviour.inputTexture,effectImage);}
            else {Call(runtime, "Bind", graph, context, null, true, false); Call(runtime, "PrepareCoordinates", (Layer)fixture.behaviour, context);}
            GL.sRGBWrite = false; RenderTexture.active=target; GL.Clear(false,true,Color.clear); if(remaining>=8) {
                using var input=(IDisposable)(effectImages?Activator.CreateInstance(TypeOf("ShaderLayerInput"),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{effectImage,context,false},null):Call(fixture.document,"ResolveShaderInput",fixture.behaviour,context));
                var material=(Material)Get(runtime,"Material");
                var pixels=Call(input,"Bind",material,Get(runtime,"EmptyInput"));
                using var bindings=(IDisposable)typeof(WhimTexDocument).GetMethods(BindingFlags.Instance|BindingFlags.NonPublic).Single(m=>m.Name=="BindShaderTextureLayers"&&m.GetParameters()[0].ParameterType==typeof(IReadOnlyList<ShaderParameter>)).Invoke(fixture.document,new object[]{Get(runtime,"Parameters"),(Layer)fixture.behaviour,context,effectImage});
                if(bindings!=null) Call(bindings,"Apply",material);
                Call(runtime,"Render",pixels,target,context);
                material.SetTexture("_MainTex",null);
            } else Call(runtime, "Render", Texture2D.blackTexture, target, context);
            var expected = Pixels();
            object[] build = { graph, node?.id, port, simplified, null };
            t.True((bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", build), label + " SVM build: " + Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason"));
            vm = build[4]; Call(vm, "ConfigureHybrid", fixture.document, (Layer)fixture.behaviour, effectImages, false);
            if(noiseChecks || remaining is > 0 and < 8) t.True(!(bool)Get(vm,"HasBoundaries"),label+" uses SVM without a native producer");
            timer.Restart();
            double logged = -5;
            while (!(bool)Get(vm, "Ready"))
            {
                if (timer.Elapsed.TotalSeconds - logged > 5)
                {
                    logged=timer.Elapsed.TotalSeconds;var m=(Material)Get(vm,"Material");
                    File.WriteAllText(report,Json(new { phase=label,stage="executor",seconds=logged,compiling=UnityEditor.ShaderUtil.anythingCompiling,
                        keywords=m?.shaderKeywords,messages=m==null?null:UnityEditor.ShaderUtil.GetShaderMessages(m.shader).Select(x=>x.message).ToArray(),rows }));
                }
                if ((bool)Get(vm, "Failed") || timer.Elapsed.TotalSeconds > 120) throw new Exception(label + " executor: " + Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason"));
                await NextUpdate(token);
            }
            if (transformed) Static("ShaderProgramRuntime","PrepareGraphCoordinates",Get(vm,"Material"),(Layer)fixture.behaviour,context,true);
            RenderTexture.active=target; GL.Clear(false,true,Color.clear);
            t.True((bool)(effectImages?Call(vm,"RenderEffectPreview",target,context,effectImage):Call(vm, "RenderBoundaries", target, context)), label + " rendered");
            var actual = Pixels(); float max = 0, relative = 0; double squared = 0;
            for (int i = 0; i < actual.Length; i++) for (int c = 0; c < 4; c++)
            {
                float d = Mathf.Abs(actual[i][c] - expected[i][c]);
                t.True(float.IsFinite(actual[i][c]), label + " finite");
                max = Mathf.Max(max, d); relative = Mathf.Max(relative, d / (1 + Mathf.Abs(expected[i][c]))); squared += d * d;
            }
            double rms = Math.Sqrt(squared / (actual.Length * 4));
            rows.Add(new { label, max, relative, rms, instructions = Get(vm, "InstructionCount"), hybrid = Get(vm, "HasBoundaries") });
            File.WriteAllText(report, Json(new { phase = label, rows }));
            t.True(relative < .002 && rms < .0003, label + " parity max=" + max + " rms=" + rms);
            if(remaining==12 && label=="PixelsFloat scale 2.5")
            {
                fixture.document.pixelScale=.75f;
                var scaled=Activator.CreateInstance(TypeOf("LayerRenderContext"),BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,
                    null,new object[]{fixture.document,null,40,32,12.8f,false,false,false},null);
                t.True((bool)Call(vm,"RenderBoundaries",target,scaled),"Reuse pixel parameter VM after scale change");
                t.True(Mathf.Abs(Pixels()[0].r-3.25f*.75f)<.000001f,"Pixel Scale reaches existing VM without rebuilding instructions");
                fixture.document.pixelScale=2.5f;
            }
            ((IDisposable)runtime).Dispose(); runtime = null; ((IDisposable)vm).Dispose(); vm = null;
        }
        try
        {
            Add(ShaderGraphNodeKind.Output);
            if(remaining!=0) {
                if(remaining>=12) await CheckParameterSvm(remaining,fixture,graph,Add,Value,Check);
                else if(remaining>=8) await CheckImageSvm(remaining,fixture,graph,Add,Value,Check);
                else await CheckRemainingSvm(remaining,fixture,graph,Add,Value,Check);
                File.WriteAllText(report,Json(new{phase="complete",rows}));return;
            }
            if (noiseChecks)
            {
                var f=Add(ShaderGraphNodeKind.Fractal); Value(f,"Octaves",Vector4.one*3);
                foreach (var kind in detailNoises ? NoiseVMKinds.Skip(6) : NoiseVMKinds.Take(6))
                {
                    var n=Add(kind);
                    foreach(var dim in new[]{ShaderGraphNoiseDimensions.ThreeD,ShaderGraphNoiseDimensions.OneD,ShaderGraphNoiseDimensions.TwoD})
                    {
                        Static("ShaderGraphNodes","ChangeNoiseDimensions",graph,n,dim);
                        Value(n,"Scale",new Vector4(9,7,5,0));
                        Value(n,"Position",new Vector4(0,0,.371f,0));
                        n.FindInput("Position").source=ShaderGraphInputSource.UVX;
                        n.FindInput("Position").type=(ShaderGraphValueType)(int)dim;
                        n.noiseFiltering=n.noisePeriodic=false;
                        if(kind==ShaderGraphNodeKind.FlowNoise) Value(n,"Phase",Vector4.one*.37f);
                        if(kind==ShaderGraphNodeKind.GaborNoise) {n.option=2;Value(n,"PhaseOffset",Vector4.one*.23f);Value(n,"Width",Vector4.one*.31f);}
                        if(kind==ShaderGraphNodeKind.CellularNoise) Value(n,"Evolution",Vector4.one*.43f);
                        await Check(kind+" "+dim+" unfiltered",n,"Value");
                        n.noiseFiltering=n.noisePeriodic=true;Value(n,"Period",new Vector4(1.3f,.9f,1.1f,0));
                        foreach(string p in (string[])Static("ShaderGraphNodes","Outputs",n)) await Check(kind+" "+dim+" periodic filtered "+p,n,p);
                    }
                    Static("ShaderGraphNodes","ChangeNoiseDimensions",graph,n,ShaderGraphNoiseDimensions.ThreeD);
                    graph.edges.RemoveAll(e=>e.inputNode==f.id);Connect(graph,n.id,"Value",f.id,"In");
                    await Check(kind+" 3D filtered Fractal",f);
                }
                if(detailNoises)
                {
                    var cell=graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.CellularNoise);
                    Static("ShaderGraphNodes","ChangeNoiseDimensions",graph,cell,ShaderGraphNoiseDimensions.OneD);
                    cell.noiseDistribution=ShaderGraphNoiseDistribution.FixedRadius;cell.noiseOutput=ShaderGraphNoiseOutput.DistanceToEdge;
                    Value(cell,"Smoothness",Vector4.one*.4f);await Check("Cellular 1D FixedRadius edge",cell,"Value");
                    File.WriteAllText(report,Json(new{phase="complete",rows}));return;
                }
                var flow=graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.FlowNoise);
                var perlin=graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.PerlinNoise);
                var sum=Add(ShaderGraphNodeKind.Add);Connect(graph,flow.id,"Value",sum.id,"A");Connect(graph,perlin.id,"Value",sum.id,"B");
                await Check("Flow restores rotation before Perlin",sum);
                var time=Add(ShaderGraphNodeKind.Time);Connect(graph,time.id,"Out",flow.id,"Phase");
                foreach(float seconds in new[]{.19f,1.37f}) {evaluationTime=seconds;await Check("Flow animated Phase "+seconds,flow,"Gradient");}
                var flowHeight=Add(ShaderGraphNodeKind.HeightToNormal);flowHeight.heightSource=ShaderGraphHeightSource.Value;
                Connect(graph,flow.id,"Value",flowHeight.id,"Height");await Check("Flow sampled filtering",flowHeight);
                File.WriteAllText(report,Json(new{phase="complete",rows}));return;
            }
            if (procedural)
            {
                fixture.document.width=640; fixture.document.height=360; fixture.document.pixelScale=2.5f;
                var canvas=Add(ShaderGraphNodeKind.CanvasPosition);
                foreach (string p in new[]{"Normalized","AspectX","AspectY"}) await Check("Canvas "+p,canvas,p);
                Layer layer=fixture.behaviour;
                layer.transform.position=new Double2(71,-43);layer.transform.scale=new Double2(.71,.83);layer.transform.rotation=17;
                transformed=true;
                foreach (TransformTilingMode tiling in Enum.GetValues(typeof(TransformTilingMode)))
                { layer.transform.tiling=tiling;await Check("Canvas transformed "+tiling,canvas,"AspectX"); }
                transformed=false;
                var data=Add(ShaderGraphNodeKind.CanvasData);
                foreach(string p in new[]{"Size","Texel","PixelScale"})await Check("Canvas data "+p,data,p);
                foreach(var kind in new[]{ShaderGraphNodeKind.PixelateUV,ShaderGraphNodeKind.TileUV,ShaderGraphNodeKind.Blackbody,ShaderGraphNodeKind.Dither})
                {
                    var n=Add(kind);
                    if(kind==ShaderGraphNodeKind.TileUV)
                    {
                        n.option=1;Value(n,"RandomRotation",new Vector4(120,0,0,0));Value(n,"RandomScale",Vector4.one*.7f);
                        foreach(string p in (string[])Static("ShaderGraphNodes","Outputs",n)) await Check("Tile "+p,n,p);
                        n.option=0;
                        var ignored=Add(ShaderGraphNodeKind.Expression);ignored.expression="0.25";
                        Connect(graph,ignored.id,"Out",n.id,"RowOffset");await Check("Tile grid ignores inactive RowOffset",n,"UV");
                    }
                    else if(kind==ShaderGraphNodeKind.Blackbody)
                        foreach(float temperature in new[]{900f,3200f,6500f,50000f}) {Value(n,"Temperature",Vector4.one*temperature);await Check("Blackbody "+temperature,n);}
                    else if(kind==ShaderGraphNodeKind.Dither)
                        for(int mode=0;mode<3;mode++) {n.option=mode;Value(n,"Color",new Vector4(-.2f,1.8f,.37f,.4f));await Check("Dither "+mode,n);}
                    else {Value(n,"CellSize",new Vector4(13,9,0,0));Value(n,"Offset",new Vector4(2,-3,0,0));await Check("Pixelate",n);}
                }
                var shape=Add(ShaderGraphNodeKind.Shape);Value(shape,"Size",new Vector4(.7f,.4f,0,0));
                var f=Add(ShaderGraphNodeKind.Fractal);Value(f,"Octaves",Vector4.one*3);
                async Task FractalCheck(ShaderGraphNode n,string port="Out")
                {graph.edges.RemoveAll(e=>e.inputNode==f.id);Connect(graph,n.id,port,f.id,"In");await Check(n.kind+" Fractal",f);}
                for(int mode=0;mode<7;mode++)
                {shape.option=mode;await Check("Shape "+mode,shape);await Check("Shape distance "+mode,shape,"Distance");}
                shape.option=4;await FractalCheck(shape);
                var combine=Add(ShaderGraphNodeKind.CombineDistance);Connect(graph,shape.id,"Distance",combine.id,"A");Value(combine,"B",Vector4.one*-.03f);
                for(int mode=0;mode<3;mode++) {combine.option=mode;await Check("Combine distance "+mode,combine);await Check("Combine mask "+mode,combine,"Mask");}
                await FractalCheck(combine,"Mask");
                foreach(var kind in new[]{ShaderGraphNodeKind.Checkerboard,ShaderGraphNodeKind.Stripes,ShaderGraphNodeKind.Grid,ShaderGraphNodeKind.Rings})
                {
                    var n=Add(kind);
                    int modes=kind==ShaderGraphNodeKind.Stripes?3:1;
                    for(int mode=0;mode<modes;mode++)
                    {
                        n.option=mode;await Check(kind+" "+mode,n);
                        if(kind is ShaderGraphNodeKind.Stripes or ShaderGraphNodeKind.Rings) {Value(n,"Frequency",Vector4.one*37);Value(n,"Phase",Vector4.one*43.17f);}
                        else {Value(n,"Scale",new Vector4(43,31,0,0));Value(n,"Offset",new Vector4(7.13f,-2.71f,0,0));}
                        await Check(kind+" dense "+mode,n);await FractalCheck(n);
                    }
                }
                var rotateUV=Add(ShaderGraphNodeKind.TransformUV);Value(rotateUV,"Rotation",Vector4.one*27);
                var rings=graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.Rings);
                Connect(graph,rotateUV.id,"Out",rings.id,"UV");await Check("Rings transformed UV",rings);await FractalCheck(rings);
                var normal=Add(ShaderGraphNodeKind.HeightToNormal);normal.heightSource=ShaderGraphHeightSource.Value;
                Connect(graph,rings.id,"Out",normal.id,"Height");await Check("Rings sampled height AA",normal);
                var map=Add(ShaderGraphNodeKind.GradientMap);
                var ramp=new ShaderParameter{type=ShaderParameterType.Gradient,gradientValue=new WhimTexGradient()};
                ramp.gradientValue.SetKeys(new[]{new GradientColorKey(new Color(2,-.2f,.7f),0),new GradientColorKey(new Color(.1f,1.5f,0),1)},new[]{new GradientAlphaKey(.2f,0),new GradientAlphaKey(.7f,1)});
                map.inputs.Add(new ShaderGraphInput{port="Gradient",type=ShaderGraphValueType.Gradient,resource=ramp});
                Value(map,"Color",new Vector4(.7f,.3f,1.8f,.45f));
                for(int mode=0;mode<3;mode++) {map.option=mode;map.channel=ShaderGraphChannel.Luminance;await Check("Gradient Map alpha "+mode,map);}
                ramp.gradientValue.WrapMode=WhimTexGradientWrapMode.Repeat;ramp.gradientValue.Interpolation=WhimTexGradientInterpolation.Fixed;
                map.channel=ShaderGraphChannel.Blue;await Check("Gradient Map Fixed Repeat",map);
                Connect(graph,canvas.id,"Normalized",map.id,"Color");map.channel=ShaderGraphChannel.Red;
                foreach(WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
                {ramp.gradientValue.WrapMode=wrap;await Check("Gradient Map varying "+wrap,map);}
                var curves=Add(ShaderGraphNodeKind.ColorCurves);Value(curves,"Color",new Vector4(-.3f,2,.6f,.4f));
                await Check("Curves unassigned",curves);
                foreach(string p in new[]{"Master","Red","Green","Blue","Alpha"})
                    curves.inputs.Add(new ShaderGraphInput{port=p,type=ShaderGraphValueType.Curve,resource=new ShaderParameter{type=ShaderParameterType.Curve,curveValue=AnimationCurve.EaseInOut(0,.1f,1,.8f)}});
                for(int mode=0;mode<2;mode++) {curves.option=mode;await Check("Curves HDR "+mode,curves);}
                File.WriteAllText(report,Json(new{phase="complete",rows}));return;
            }
            var uv = Add(ShaderGraphNodeKind.UV);
            var rotate = Add(ShaderGraphNodeKind.RotateVector); Connect(graph, uv.id, "Out", rotate.id, "In"); Value(rotate, "Angle", Vector4.one * 31);
            var basis = Add(ShaderGraphNodeKind.ValueCubicNoise); basis.noiseFiltering = true;
            Connect(graph, rotate.id, "Out", basis.id, "Position"); Value(basis, "Scale", new Vector4(13, 9, 0, 0));
            await Check("filtered Value Cubic", basis, "Value");
            var fractal = Add(ShaderGraphNodeKind.Fractal); Connect(graph, basis.id, "Value", fractal.id, "In");
            await Check("filtered Value Cubic in Fractal", fractal);
            var height = Add(ShaderGraphNodeKind.HeightToNormal); height.heightSource = ShaderGraphHeightSource.Value;
            Connect(graph, fractal.id, "Out", height.id, "Height"); Value(height, "Strength", Vector4.one * 15);
            foreach (ShaderGraphDerivative derivative in Enum.GetValues(typeof(ShaderGraphDerivative)))
            { height.derivative = derivative; await Check("Height " + derivative, height); }
            var light = Add(ShaderGraphNodeKind.NormalLighting); Connect(graph, height.id, "Out", light.id, "Normal");
            Value(light, "SpecularIntensity", Vector4.one * .8f); Value(light, "Color", new Vector4(2, -.1f, .4f, .35f));
            await Check("Local Lighting", light);
            light.option = 1; await Check("Simplified Global Lighting", light, "Out", true);
            var shard = Add(ShaderGraphNodeKind.ShardNoise); shard.noiseFiltering = true; shard.noisePeriodic = true;
            foreach (ShaderGraphNoiseDimensions dimensions in Enum.GetValues(typeof(ShaderGraphNoiseDimensions)))
            {
                shard.noiseDimensions = dimensions;
                var scale=shard.FindInput("Scale");scale.type=dimensions==ShaderGraphNoiseDimensions.OneD?ShaderGraphValueType.Float:dimensions==ShaderGraphNoiseDimensions.TwoD?ShaderGraphValueType.Float2:ShaderGraphValueType.Float3;
                scale.value=new Vector4(9,7,5,0);
                await Check("Shard " + dimensions, shard, "Value");
            }
            graph.edges.RemoveAll(e => e.inputNode == fractal.id); Connect(graph, shard.id, "Value", fractal.id, "In");
            await Check("filtered Shard Fractal", fractal);
            var gabor=Add(ShaderGraphNodeKind.GaborNoise);gabor.noiseFiltering=true;gabor.noisePeriodic=true;
            await Check("Filtered Gabor",gabor,"Value");await Check("Filtered Gabor gradient",gabor,"Gradient");
            var cellular=Add(ShaderGraphNodeKind.CellularNoise);cellular.noiseFiltering=true;cellular.noisePeriodic=true;
            await Check("Filtered Cellular",cellular,"Value");
            cellular.noiseDimensions=ShaderGraphNoiseDimensions.ThreeD;
            await Check("Filtered Cellular 3D",cellular,"ToPoint");
            var gradient = Add(ShaderGraphNodeKind.SampleGradient);
            var parameter = new ShaderParameter { type = ShaderParameterType.Gradient, gradientValue = new WhimTexGradient() };
            parameter.gradientValue.SetKeys(new[] {new GradientColorKey(new Color(2,-.2f,.7f),0),new GradientColorKey(new Color(.1f,1.5f,0),1)},
                new[] {new GradientAlphaKey(.25f,0),new GradientAlphaKey(.8f,1)});
            gradient.inputs.Add(new ShaderGraphInput { port = "Gradient", type = ShaderGraphValueType.Gradient, resource = parameter });
            foreach (WhimTexGradientWrapMode wrap in Enum.GetValues(typeof(WhimTexGradientWrapMode)))
            { parameter.gradientValue.WrapMode = wrap; Value(gradient, "T", new Vector4(1.31f, 0, 0, 0)); await Check("Gradient " + wrap, gradient); }
            parameter.gradientValue.Interpolation=WhimTexGradientInterpolation.Fixed;
            gradient.FindInput("T").source=ShaderGraphInputSource.UVY;
            await Check("Fixed Gradient",gradient);await Check("Gradient alpha output",gradient,"A");
            parameter.name="_Ramp";graph.parameters.Add(parameter);
            var parameterNode=Add(ShaderGraphNodeKind.Parameter);parameterNode.parameterId=parameter.id;
            var reroute=Add(ShaderGraphNodeKind.Reroute);Connect(graph,parameterNode.id,"Out",reroute.id,"In");Connect(graph,reroute.id,"Out",gradient.id,"Gradient");
            await Check("Gradient parameter through Reroute",gradient);
            var curve = Add(ShaderGraphNodeKind.SampleCurve); await Check("Curve UV default", curve);
            var blend = Add(ShaderGraphNodeKind.Blend); Value(blend,"Background",new Vector4(-.2f,2,.6f,.4f));Value(blend,"Foreground",new Vector4(.7f,.3f,1.3f,.6f));
            foreach (int mode in new[] { 0, 1, 15, 29 }) foreach (bool hdr in new[] { false, true })
            { blend.option=mode;blend.hdr=hdr;await Check("Blend "+mode+" HDR="+hdr,blend); }
            // Real heavy graph: evaluate every preview independently, with the same
            // simplified policy used by the node cards, not the final Output policy.
            graph = Load();
            foreach (var node in graph.nodes.Where(n => n.kind is ShaderGraphNodeKind.HeightToNormal or ShaderGraphNodeKind.Hue))
                await Check("Frozen " + node.kind + " " + node.id.Substring(0,6), node, "Out", true);
            foreach (var node in graph.nodes.Where(n => n.kind != ShaderGraphNodeKind.Reroute && n.kind != ShaderGraphNodeKind.Output))
            {
                var ports = (string[])Static("ShaderGraphNodes", "Outputs", node); if (ports.Length == 0) continue;
                object[] build = { graph, node.id, ports[0], true, null };
                bool supported = (bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", build);
                rows.Add(new { coverage = node.kind.ToString(), supported, reason = Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason") });
                (build[4] as IDisposable)?.Dispose();
                t.True(supported, "Frozen graph preview " + node.kind + ": " + Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason"));
            }
            File.WriteAllText(report, Json(new { phase = "complete", rows }));
        }
        finally
        {
            (request as IDisposable)?.Dispose(); (runtime as IDisposable)?.Dispose(); (vm as IDisposable)?.Dispose();
            RenderTexture.active = previous; GL.sRGBWrite = srgb;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(read);
            if(effectImage!=null){effectImage.Release();UnityEngine.Object.DestroyImmediate(effectImage);}
        }
    });
}
