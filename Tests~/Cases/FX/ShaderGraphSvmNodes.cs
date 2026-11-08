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
    static readonly ShaderGraphNodeKind[] ProceduralVMKinds = {
        ShaderGraphNodeKind.CanvasPosition, ShaderGraphNodeKind.GradientMap, ShaderGraphNodeKind.ColorCurves,
        ShaderGraphNodeKind.PixelateUV, ShaderGraphNodeKind.TileUV, ShaderGraphNodeKind.Blackbody, ShaderGraphNodeKind.Dither,
        ShaderGraphNodeKind.Shape, ShaderGraphNodeKind.CombineDistance, ShaderGraphNodeKind.Checkerboard,
        ShaderGraphNodeKind.Stripes, ShaderGraphNodeKind.Grid, ShaderGraphNodeKind.Rings };
    static string RunSvmWindowChecks(string runId, bool procedural) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        if (procedural)
        {
            var graph = fixture.behaviour.draftGraph = new ShaderGraph();
            foreach (var kind in ProceduralVMKinds.Concat(new[] { ShaderGraphNodeKind.CanvasData,ShaderGraphNodeKind.Add,ShaderGraphNodeKind.Output }))
                graph.nodes.Add((ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,new Vector2(graph.nodes.Count%4*330,graph.nodes.Count/4*370)));
            Connect(graph,graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.CanvasData).id,"PixelScale",graph.nodes.Single(n=>n.kind==ShaderGraphNodeKind.Add).id,"A");
        }
        foreach (var node in fixture.behaviour.draftGraph.nodes) node.previewExpanded = true;
        Set(fixture.window, "svmPreviews", true); Set(fixture.window, "accurateLighting", false);
        fixture.Show();
        await NextUpdate(token); await NextUpdate(token);
        var timer = Stopwatch.StartNew();
        while (Get(fixture.window, "canvas") == null)
        { if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException("Graph UI"); await NextUpdate(token); }
        var canvas = Get(fixture.window, "canvas");
        var view = (UnityEditor.Experimental.GraphView.GraphView)canvas;
        var rows = new List<object>();
        string report = MigrationD.ProjectPath(procedural ? "Temp/WhimTex/svm-procedural-window.json" : "Temp/WhimTex/svm-window-checks.json");
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
            string id = (string)item.Key; var node = fixture.behaviour.draftGraph.FindNode(id);
            File.WriteAllText(report, Json(new { phase = node.kind.ToString(), rows }));
            var current = await Visit(id, item.Value);
            rows.Add(new { id, kind = node.kind.ToString(), displayedSVM = Get(current, "displayedSVM") });
        }
        t.True(entries.Count >= (procedural?14:20), "Graph preview population");
        var cubic = fixture.behaviour.draftGraph.nodes.Single(n => n.kind == (procedural?ShaderGraphNodeKind.TileUV:ShaderGraphNodeKind.ValueCubicNoise));
        var previewNode = ((System.Collections.IDictionary)Get(canvas,"previews"))[cubic.id];
        Call(previewNode, "ChangeExpanded", false); await NextUpdate(token);
        t.True(!(bool)Call(previewNode, "CanDisplay"), "Collapsed preview is not rendered");
        Call(previewNode, "ChangeExpanded", true); await Visit(cubic.id, previewNode);
        ((UnityEngine.UIElements.DropdownField)Get(previewNode, "output")).value = procedural?"Random":"Gradient";
        await Visit(cubic.id, previewNode);
        fixture.Edit(new Step { action = "value", node = cubic.id, port = "Seed", number = 731 });
        await Visit(cubic.id, previewNode);
        t.True((bool)Static("ShaderGraphPreviewVM","SupportsDefaultNode",ShaderGraphNodeKind.NormalLighting,"Out"),"Creation filter includes simplified lighting");
        foreach(var kind in new[]{ShaderParameterType.Gradient,ShaderParameterType.Curve})
            t.True((bool)Static("ShaderGraphPreviewVM","SupportsParameter",new ShaderParameter {type=kind}),"Creation filter includes "+kind);
        File.WriteAllText(report, Json(new { phase = "complete", rows, outputSwitch = true, reopen = true, edit = true }));
    });

    public static string SvmNodeChecks(string runId) => RunSvmNodeChecks(runId,false);
    public static string SvmProceduralChecks(string runId) => RunSvmNodeChecks(runId,true);
    static string RunSvmNodeChecks(string runId, bool procedural) => AsyncD.Start(runId, async (t, token) =>
    {
        using var fixture = new Fixture();
        var graph = new ShaderGraph();
        var rows = new List<object>();
        string report = MigrationD.ProjectPath(procedural ? "Temp/WhimTex/svm-procedural.json" : "Temp/WhimTex/svm-node-checks.json");
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
        bool transformed = false;
        async Task Check(string label, ShaderGraphNode node, string port = "Out", bool simplified = false)
        {
            File.WriteAllText(report, Json(new { phase = label, rows }));
            fixture.behaviour.draftGraph = graph;
            Static("ShaderGraphRevision", "Invalidate", graph);
            var compilation = Call(Static("ShaderGraphCompiler", "Prepare", graph, false), "Compile", node.id, port, simplified);
            request = Activator.CreateInstance(TypeOf("ShaderProgramRuntime+CompilationRequest"), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { graph, compilation, 0UL, "Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl", false, false, true, null, null }, null);
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
            Call(runtime, "Bind", graph, context, null, true, false); Call(runtime, "PrepareCoordinates", (Layer)fixture.behaviour, context);
            GL.sRGBWrite = false; RenderTexture.active=target; GL.Clear(false,true,Color.clear); Call(runtime, "Render", Texture2D.blackTexture, target, context); var expected = Pixels();
            object[] build = { graph, node.id, port, simplified, null };
            t.True((bool)Static("ShaderGraphPreviewVM", "TryCreateHybrid", build), label + " SVM build: " + Get(TypeOf("ShaderGraphPreviewVM"), "LastFallbackReason"));
            vm = build[4]; Call(vm, "ConfigureHybrid", fixture.document, (Layer)fixture.behaviour, false, false);
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
            t.True((bool)Call(vm, "RenderBoundaries", target, context), label + " rendered");
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
            ((IDisposable)runtime).Dispose(); runtime = null; ((IDisposable)vm).Dispose(); vm = null;
        }
        try
        {
            Add(ShaderGraphNodeKind.Output);
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
        }
    });
}
