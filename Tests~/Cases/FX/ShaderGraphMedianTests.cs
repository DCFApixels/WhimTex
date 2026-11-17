using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DCFApixels.WhimTex;
using UnityEngine;
using UnityEngine.Rendering;
using WhimTex.Tests;
using Object = UnityEngine.Object;

// Owned transient graphs only. The independent oracle sorts every pixel in the square.
public static partial class ShaderGraphMedianTests
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic;
    static Type Find(string name) => typeof(ShaderGraph).Assembly.GetType("DCFApixels.WhimTex." + name, true);
    static object Call(Type type, object target, string name, params object[] args) => type.GetMethods(F | (target == null ? BindingFlags.Static : BindingFlags.Instance))
        .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target,args);
    static object Get(object target,string name) => target.GetType().GetField(name,F|BindingFlags.Instance)?.GetValue(target) ?? target.GetType().GetProperty(name,F|BindingFlags.Instance)?.GetValue(target);
    static object Static(string type,string name,params object[] args) => Call(Find(type),null,name,args);
    static ShaderGraphNode Node(ShaderGraph graph,ShaderGraphNodeKind kind)
    { var node=(ShaderGraphNode)Static("ShaderGraphNodes","Create",kind,Vector2.zero); graph.nodes.Add(node);return node; }
    static void Connect(ShaderGraph graph,ShaderGraphNode from,ShaderGraphNode to,string input,string output="Out")
        => graph.edges.Add(new ShaderGraphEdge{outputNode=from.id,outputPort=output,inputNode=to.id,inputPort=input});
    static Color Oracle(Color[] input,int size,int x,int y,int r)
    {
        var result=new Color();
        for(int k=0;k<4;k++)
        {
            var values=new float[(2*r+1)*(2*r+1)];int n=0;
            for(int dy=-r;dy<=r;dy++)for(int dx=-r;dx<=r;dx++)
                values[n++]=input[Mathf.Clamp(y+dy,0,size-1)*size+Mathf.Clamp(x+dx,0,size-1)][k];
            Array.Sort(values);result[k]=values[values.Length/2];
        }
        return result;
    }
    public static async Task<object> Run()
    {
        try{return await Check(false);}catch(Exception e){return TestContext.Result("failed",0,"Dense Median",e.ToString()).ToJson();}
    }
    public static async Task<object> Benchmark()
    {
        try{return await Check(true);}catch(Exception e){return TestContext.Result("failed",0,"Median benchmark",e.ToString()).ToJson();}
    }
    public static async Task<object> Tiles()
    {
        try{return await Check(true,257);}catch(Exception e){return TestContext.Result("failed",0,"Median partial tiles",e.ToString()).ToJson();}
    }
    public static async Task<object> UniformBenchmark()
    {
        try{return await Check(true,256,true);}catch(Exception e){return TestContext.Result("failed",0,"Uniform Median benchmark",e.ToString()).ToJson();}
    }
    static async Task<object> Check(bool benchmark, int benchmarkSize=256, bool uniformBenchmark=false)
    {
        var t=new TestContext();int size=benchmark?benchmarkSize:32;
        var originalActive=RenderTexture.active;bool originalSrgb=GL.sRGBWrite;
        WhimTexDocument document=null;Texture2D texture=null,readback=null;RenderTexture target=null;
        var measurements=new List<string>();float maximumError=0,maximumVmError=0,maximumSamplingError=0;
        var timeout=Stopwatch.StartNew();
        async Task Until(Func<bool> ready,string label)
        {while(!ready()){if(timeout.Elapsed.TotalSeconds>180)throw new TimeoutException(label);await Task.Delay(25);}}
        try
        {
            var compute=UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.dcfapixels.whimtex/src/Shaders/MedianColumns.compute");
            t.True(compute!=null && new[]{"Columns","Select","Resolve"}.All(k=>compute.IsSupported(compute.FindKernel(k))),"Sorted-column compute kernels are available");
            foreach(var message in UnityEditor.ShaderUtil.GetComputeShaderMessages(compute))
                t.True(message.severity.ToString() is not ("Warning" or "Error"),message.message);
            document=ScriptableObject.CreateInstance<WhimTexDocument>();document.hideFlags=HideFlags.HideAndDontSave;document.width=document.height=size;
            texture=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var random=new System.Random(71253);var input=new Color[size*size];
            for(int i=0;i<input.Length;i++)input[i]=new Color((float)random.NextDouble()*20-10,(float)random.NextDouble()*1000,(i%4)*.25f,i%3==0?0:(float)random.NextDouble()*6-2);
            texture.SetPixels(input);texture.Apply();
            target=new RenderTexture(size,size,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave};t.True(target.Create(),"Target allocated");
            readback=new Texture2D(size,size,TextureFormat.RGBAFloat,false,true){hideFlags=HideFlags.HideAndDontSave};
            Color[] Read(RenderTexture image=null){var active=RenderTexture.active;try{RenderTexture.active=image??target;readback.ReadPixels(new Rect(0,0,size,size),0,0);readback.Apply();return readback.GetPixels();}finally{RenderTexture.active=active;}}
            var context=Activator.CreateInstance(Find("LayerRenderContext"),new object[]{document,null,size,size,1f,false,false,false});
            var graph=new ShaderGraph();var sample=Node(graph,ShaderGraphNodeKind.SampleTexture);sample.wrap=ShaderGraphWrap.Clamp;sample.filter=ShaderGraphFilter.Point;
            sample.inputs.Add(new ShaderGraphInput{port="Texture",type=ShaderGraphValueType.Texture,resource=new ShaderParameter{type=ShaderParameterType.Texture2D,textureValue=texture}});
            var median=Node(graph,ShaderGraphNodeKind.Median);var output=Node(graph,ShaderGraphNodeKind.Output);Connect(graph,sample,median,"In");Connect(graph,median,output,"RGBA");
            var number=Node(graph,ShaderGraphNodeKind.Value);number.literal.floatValue=1;
            var opaque=Node(graph,ShaderGraphNodeKind.Expression);opaque.expression="A";Connect(graph,number,opaque,"A");
            Layer layer=new ShaderLayerBehaviour{mode=ShaderProgramMode.Graph,draftGraph=graph};Call(typeof(Layer),layer,"AssignNewId");document.layers.Add(layer);
            var requestType=Find("ShaderProgramRuntime+CompilationRequest");var vmType=Find("ShaderGraphPreviewVM");
            foreach(float requested in benchmark?new[]{4f,8f,16f,32f}:new[]{-1f,0f,.9f,1f,1.9f,2f,2.9f,3f,8f})
            foreach(bool precise in new[]{false,true})
            {
                int radius=Math.Max(0,(int)Math.Floor(requested));
                median.FindInput("Radius").value=Vector4.one*requested;
                number.literal.floatValue=requested;
                graph.edges.RemoveAll(e=>e.inputNode==median.id&&e.inputPort=="Radius");
                if(uniformBenchmark)Connect(graph,precise?opaque:number,median,"Radius");
                else if(precise)Connect(graph,opaque,median,"Radius");
                using var request=(IDisposable)Activator.CreateInstance(requestType,BindingFlags.Instance|F,null,
                    new object[]{graph,median.id,"Out",false,false,"Packages/com.dcfapixels.whimtex/ShaderGraph.hlsl",true},null);
                object[] completed={null};await Until(()=>(bool)Call(requestType,request,"TryComplete",completed),"Native compilation");
                using var runtime=(IDisposable)completed[0];var rt=runtime.GetType();
                foreach(var diagnostic in (IEnumerable)Get(runtime,"DiagnosticMessages"))
                    t.True(Get(diagnostic,"Severity").ToString() is not ("Warning" or "Error"),$"Radius={requested}, direct={precise}: {diagnostic}");
                Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"PrepareCoordinates",layer,context);
                GL.sRGBWrite=false;Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);var native=Read();
                var exact=native;
                if(radius>2&&!precise)
                {
                    var image=((IEnumerable)Get(runtime,"bufferImages")).Cast<object>().Single(b=>(bool)Get(Get(b,"pass"),"median"));
                    exact=Read((RenderTexture)Get(image,"texture"));
                }
                int[] coordinates=benchmark?new[]{0,1,126,127,128,129,size-2,size-1}:Enumerable.Range(0,size).ToArray();
                foreach(int y in coordinates)foreach(int x in coordinates)
                {
                    var expected=Oracle(input,size,x,y,radius);
                    for(int k=0;k<4;k++)
                    {
                        int pixel=y*size+x;float e=Math.Abs(expected[k]-exact[pixel][k]);maximumError=Math.Max(maximumError,e);
                        t.Near(expected[k],exact[pixel][k],.0008f,$"r{radius}/{precise}/{x},{y}/{k}");
                        // Non-power-of-two UV roundoff creates tiny bilinear weights even at a pixel center.
                        // Check exact selection separately from display sampling; inputs span [-10,1000].
                        float range=k==1?1000:k==0?20:6;
                        maximumSamplingError=Math.Max(maximumSamplingError,Math.Abs(native[pixel][k]-exact[pixel][k]));
                        t.Near(exact[pixel][k],native[pixel][k],range*4e-5f+1e-6f,"Prepared image sampling");
                    }
                }
                int draws=(int)Get(runtime,"BufferDrawCount");Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                if(radius>2&&!precise)t.Equal(draws,(int)Get(runtime,"BufferDrawCount"),"Unchanged source reuses filtered image");
                if(!benchmark && radius==3 && !precise)
                {
                    median.FindInput("Radius").value=Vector4.one*4;
                    Call(rt,runtime,"Bind",graph,context,null,true,false);Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                    var changed=Read();t.True((int)Get(runtime,"BufferDrawCount")>draws,"Radius edit invalidates cached result without recompilation");
                    foreach(int p in new[]{0,1,15,31})
                    {
                        var expected=Oracle(input,size,p,p,4);
                        for(int k=0;k<4;k++)t.Near(expected[k],changed[p*size+p][k],.0008f,"Updated radius pixels");
                    }
                    median.FindInput("Radius").value=Vector4.one*radius;
                }
                if((!benchmark || benchmarkSize!=256) && !precise)
                {
                    IDisposable vm;
                    if(radius>2 || precise)vm=(IDisposable)Static("ShaderGraphPreviewVM","BeginHybrid",graph,median.id,"Out",false,false,false);
                    else{object[] args={graph,median.id,"Out",null};t.True((bool)Call(vmType,null,"TryCreate",args),"Direct SVM build");vm=(IDisposable)args[3];}
                    using(vm)
                    {
                        t.True(vm!=null,"VM exists");Call(vmType,vm,"ConfigureHybrid",document,layer,false,true);
                        await Until(()=>{if((bool)Get(vm,"Failed"))throw new InvalidOperationException("SVM failed");return (bool)Call(vmType,vm,"UpdatePreparation");},"SVM preparation");
                        t.True((bool)Call(vmType,vm,"RenderBoundaries",target,context),"SVM render");var actual=Read();
                        for(int i=0;i<actual.Length;i++)for(int k=0;k<4;k++){float e=Math.Abs(actual[i][k]-native[i][k]);maximumVmError=Math.Max(maximumVmError,e);t.Near(native[i][k],actual[i][k],.0008f,"SVM/native");}
                    }
                }
                else if(benchmark)
                {
                    foreach(bool editRadius in uniformBenchmark?new[]{false,true}:new[]{false})
                    {
                    var times=new List<double>();
                    int warmup=uniformBenchmark?3:1, repeats=uniformBenchmark?7:5;
                    for(int iteration=0;iteration<warmup+repeats;iteration++)
                    {
                        // Unique fractional edits keep the same r/r+1 sample areas while
                        // preventing reuse of a previously visited scalar/result cache entry.
                        if(editRadius) number.literal.floatValue=radius+(iteration%2)+(iteration+1)*.001f;
                        else { texture.SetPixel(0,0,input[0]);texture.Apply(false,false); } // Invalidate outside the timed region.
                        int before=(int)Get(runtime,"BufferDrawCount");
                        var timer=Stopwatch.StartNew();
                        if(editRadius) Call(rt,runtime,"Bind",graph,context,null,true,false);
                        Call(rt,runtime,"Render",Texture2D.blackTexture,target,context);
                        var fence=AsyncGPUReadback.Request(target,0,0,1,0,1,0,1);fence.WaitForCompletion();
                        timer.Stop();t.True(!fence.hasError,"GPU completion");
                        if(!precise)t.True((int)Get(runtime,"BufferDrawCount")>before,"Benchmark performs filtering rather than returning a cached image");
                        if(iteration>=warmup)times.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    times.Sort();measurements.Add($"r{radius} {(uniformBenchmark?((precise?"direct ":"uniform ")+(editRadius?"radius edit":"source edit")):(precise?"dense":"columns"))}: {times[times.Count/2]:F3} ms median ({string.Join(",",times.Select(v=>v.ToString("F3")))})");
                    }
                }
            }
            if(!uniformBenchmark) {
            graph.edges.RemoveAll(e=>e.inputNode==median.id&&e.inputPort=="Radius");
            median.FindInput("Radius").value=Vector4.one*2;ulong small=(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph);
            median.FindInput("Radius").value=Vector4.one*3;t.True(small!=(ulong)Static("ShaderGraphCompiler","CodeFingerprint",graph),"Crossing radius threshold changes executable fingerprint");
            object Compile()=>Static("ShaderGraphCompiler","Compile",graph,null,"Out",false);
            t.Equal(2,((ICollection)Get(Compile(),"buffers")).Count,"Large default radius has source and result passes");
            Connect(graph,opaque,median,"Radius");
            t.Equal(0,((ICollection)Get(Compile(),"buffers")).Count,"Opaque radius remains direct");
            graph.edges.RemoveAll(e=>e.inputNode==median.id && e.inputPort=="Radius");
            var fractal=Node(graph,ShaderGraphNodeKind.Fractal);Connect(graph,median,fractal,"In");
            graph.edges.RemoveAll(e=>e.inputNode==output.id);Connect(graph,fractal,output,"RGBA");
            t.Equal(0,((ICollection)Get(Compile(),"buffers")).Count,"Fractal preserves per-iteration branch evaluation");
            }

        }
        finally
        {
            RenderTexture.active=originalActive;GL.sRGBWrite=originalSrgb;
            if(document!=null)Object.DestroyImmediate(document);if(texture!=null)Object.DestroyImmediate(texture);if(readback!=null)Object.DestroyImmediate(readback);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}
        }
        return new TestResult{status="passed",checks=t.Checks,failures=Array.Empty<string>(),message=$"Median {size}x{size}, {Application.unityVersion}, {SystemInfo.graphicsDeviceName}/{SystemInfo.graphicsDeviceType}, CPU error {maximumError:R}, VM error {(benchmark&&benchmarkSize==256?"not measured":maximumVmError.ToString("R"))}, display sampling difference {maximumSamplingError:R}. "+string.Join("; ",measurements)};
    }
}
